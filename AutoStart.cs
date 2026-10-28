using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Bloodlust;

// "Start a seed" mode: when the main menu appears and user://bloodlust_autostart.json exists, consume it and
// start that run straight away, skipping character select. scripts/start-seed.sh writes the file.
//   {"seed": "BL00D7150", "character": "IRONCLAD", "ascension": 0, "abandon": false}
// With a run already in progress it does nothing unless "abandon" is true, which abandons it the way the
// main menu's Abandon button does (it is recorded in run history as abandoned).
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class AutoStart
{
    private record Request(string Seed, string? Character, int Ascension, bool Abandon);

    internal static string RequestPath => Path.Combine(ProjectSettings.GlobalizePath("user://"), "bloodlust_autostart.json");

    [HarmonyPostfix]
    private static void Postfix(NMainMenu __instance)
    {
        if (!File.Exists(RequestPath)) return;
        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(File.ReadAllText(RequestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Bloodlust] Ignoring unreadable {RequestPath}: {e.Message}");
            return;
        }
        finally
        {
            // One-shot, so a later normal launch or return to the menu is not hijacked.
            File.Delete(RequestPath);
        }
        if (request?.Seed is not { Length: > 0 }) return;
        // Let the menu finish its first frame (buttons, run-save check) before leaving it.
        Callable.From(() => TaskHelper.RunSafely(Start(__instance, request))).CallDeferred();
    }

    private static async Task Start(NMainMenu menu, Request request)
    {
        var character = request.Character is { } name
            ? ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry.Equals(name, StringComparison.OrdinalIgnoreCase))
            : ModelDb.AllCharacters.First();
        if (character == null)
        {
            GD.PushWarning($"[Bloodlust] Autostart: no character named {request.Character}");
            return;
        }
        var saves = SaveManager.Instance;
        if (saves.HasRunSave)
        {
            if (!request.Abandon)
            {
                GD.PushWarning("[Bloodlust] Autostart skipped: a run is in progress (pass --abandon to replace it).");
                return;
            }
            menu.AbandonRun();
        }
        string seed = SeedHelper.CanonicalizeSeed(request.Seed);
        // Same act selection and ascension clamp as the character-select lobby (StartRunLobby.BeginRun).
        var unlocks = saves.GenerateUnlockStateFromProgress();
        var acts = ActModel.GetRandomList(new Rng(StringHelper.GetDeterministicHashCode(seed), "act_selection"), unlocks, false).ToList();
        int ascension = Math.Clamp(request.Ascension, 0, saves.Progress.GetOrCreateCharacterStats(character.Id).MaxAscension);
        GD.Print($"[Bloodlust] Autostart: {character.Id.Entry} seed {seed} ascension {ascension}");
        await NGame.Instance!.Transition.FadeOut(0.8f, character.CharacterSelectTransitionPath);
        await NGame.Instance.StartNewSingleplayerRun(character, shouldSave: true, acts, [], seed, GameMode.Standard, ascension);
    }
}
