using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Relic = Bloodlust.Relics.Bloodlust;

namespace Bloodlust;

// Screenshots at the start of each fight (once the first player turn is ready, after Bloodlust's Vigor),
// at its end, and of its card reward once the cards have animated in, into
// user://bloodlust_screenshots/<run start>_<seed>/, with a log.txt of Vigor and stash per shot.
// Purely observational: any error turns capturing off for the session instead of touching the game.
internal static class FightCapture
{
    internal static string Root => Path.Combine(ProjectSettings.GlobalizePath("user://"), "bloodlust_screenshots");

    private static RunState? _run;
    private static string? _directory;
    private static int _next;
    // "floor17_start" etc. already in the folder: resuming after Save & Quit replays the fight from its start.
    private static readonly HashSet<string> _taken = [];
    private static readonly AccessTools.FieldRef<RunManager, long> StartTime =
        AccessTools.FieldRefAccess<RunManager, long>("_startTime");
    private static bool _failed;
    // The floor whose fight just ended, and the last floor whose card reward was shot (one per fight).
    private static int _endedFloor = -1;
    private static int _cardsFloor = -1;
    // PNG encoding hitches a frame, so saves run in order on one background chain.
    private static Task _writes = Task.CompletedTask;

    internal static void Initialize()
    {
        CombatManager.Instance.TurnStarted += state =>
        {
            if (state.RoundNumber == 1 && state.CurrentSide == CombatSide.Player)
                CaptureNextFrame("start");
        };
        CombatManager.Instance.CombatEnded += _ => Safe(() =>
        {
            _endedFloor = RunManager.Instance.DebugOnlyGetState()?.TotalFloor ?? -1;
            CaptureNextFrame("end");
        });
    }

    // Card reward screens also open outside fights (events); only the reward for the fight just ended is shot.
    // A re-roll calls RefreshOptions again, but the first set of cards is the one kept.
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
    private static class CardReward
    {
        private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, Tween> CardTween =
            AccessTools.FieldRefAccess<NCardRewardSelectionScreen, Tween>("_cardTween");

        [HarmonyPostfix]
        private static void Postfix(NCardRewardSelectionScreen __instance) => Safe(() =>
        {
            int floor = RunManager.Instance.DebugOnlyGetState()?.TotalFloor ?? -2;
            if (floor != _endedFloor || floor == _cardsFloor) return;
            _cardsFloor = floor;
            CardTween(__instance).Connect(Tween.SignalName.Finished, Callable.From(() => CaptureNextFrame("cards")),
                (uint)GodotObject.ConnectFlags.OneShot);
        });
    }

    private static void Safe(Action action)
    {
        if (_failed) return;
        try { action(); }
        catch (Exception e)
        {
            _failed = true;
            GD.PushWarning($"[Bloodlust] Fight screenshots disabled after an error: {e}");
        }
    }

    // The event fires mid-frame; wait for that frame to be drawn so the shot shows the new state.
    private static void CaptureNextFrame(string kind) => Safe(() =>
    {
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw,
            Callable.From(() => Safe(() => Capture(kind))), (uint)GodotObject.ConnectFlags.OneShot);
    });

    private static void Capture(string kind)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null || Engine.GetMainLoop() is not SceneTree tree) return;
        if (!ReferenceEquals(run, _run))
        {
            _run = run;
            // One folder per run, named by the run's own start time (saved with the run), so Save & Quit and
            // resuming keeps adding to the same folder.
            var start = DateTimeOffset.FromUnixTimeSeconds(StartTime(RunManager.Instance)).LocalDateTime;
            _directory = Path.Combine(Root, $"{start:yyyyMMdd_HHmmss}_{run.Rng.StringSeed}");
            Directory.CreateDirectory(_directory);
            var existing = Directory.GetFiles(_directory, "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
            _next = existing.Count + 1;
            _taken.Clear();
            foreach (var name in existing)
                _taken.Add(name!.Substring(name.IndexOf('_') + 1));
        }
        // The first shot of each floor and kind is kept; a resumed fight's retaken start is skipped.
        if (!_taken.Add($"floor{run.TotalFloor:00}_{kind}")) return;
        var me = LocalContext.GetMe(run);
        var relic = me?.GetRelic<Relic>();
        string file = $"{_next++:000}_floor{run.TotalFloor:00}_{kind}.png";
        string line = $"{file}\tseed {run.Rng.StringSeed}\tHP {me?.Creature.CurrentHp}/{me?.Creature.MaxHp}" +
                      $"\tVigor {me?.Creature.GetPowerAmount<VigorPower>() ?? 0}" +
                      (relic == null ? "\tno Bloodlust" : $"\tBloodlust stash {relic.StoredVigor}");
        // GPU readback must happen on the main thread; encoding and disk writes need not.
        var image = tree.Root.GetTexture().GetImage();
        string directory = _directory!;
        _writes = _writes.ContinueWith(_ => Safe(() =>
        {
            using (image)
                if (image.SavePng(Path.Combine(directory, file)) != Error.Ok)
                    throw new IOException($"Could not save {file}");
            File.AppendAllText(Path.Combine(directory, "log.txt"), line + "\n");
        }), TaskScheduler.Default);
    }
}
