using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BaseLib.Patches.Content;
using Bloodlust;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Relic = Bloodlust.Relics.Bloodlust;

// Seed search: which seeds make Neow offer Small Capsule, and have its relic reward roll Bloodlust?
// Runs the installed game's own Neow option generator, grab-bag population and RelicReward.Populate.
// Only save access, Godot logging and option text are stubbed.
//   dotnet run -c Release -- [--seed SEED] [--ascension N] [--progress path/to/progress.save] [--count N] [--from N] [--to N]
TestMode.IsOn = true;
var h = new Harmony("Bloodlust.SeedSearch");
h.Patch(AccessTools.PropertyGetter(typeof(SaveManager), "Instance"), prefix: new HarmonyMethod(typeof(Stubs), nameof(Stubs.NoSaveManager)));
h.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.FromRelic)), prefix: new HarmonyMethod(typeof(Stubs), nameof(Stubs.OptionWithoutUi)));
foreach (var m in typeof(MegaCrit.Sts2.Core.Logging.Log).GetMethods(BindingFlags.Public | BindingFlags.Static)
             .Where(m => m.ReturnType == typeof(void) && m.Name != nameof(MegaCrit.Sts2.Core.Logging.Log.InvokeGlobalLogCallback)))
    h.Patch(m, prefix: new HarmonyMethod(typeof(Stubs), nameof(Stubs.Skip)));
ModEntry.Initialize();
h.CreateClassProcessor(typeof(PrefixIdPatch)).Patch();
// In the game, BaseLib registers the relic with its pool before ModelDb builds the pools; do the same.
CustomContentDictionary.AddModel(typeof(Relic));
var types = typeof(ModelDb).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(AbstractModel)) && t.GetConstructor(Type.EmptyTypes) != null)
    .Append(typeof(Relic)).ToArray();
AccessTools.Field(typeof(ModelDb), "_allAbstractModelSubtypes").SetValue(null, types);
ModelDb.Init(types);
var shared = ModelDb.RelicPool<SharedRelicPool>().AllRelics.ToList();
if (shared.Last() is not Relic)
    throw new Exception("Bloodlust is not the last relic in the shared pool, unlike in the game.");

string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
string? requested = args.SkipWhile(a => a != "--seed").Skip(1).FirstOrDefault();
int ascension = int.Parse(Arg("--ascension", "0"));
int wanted = int.Parse(Arg("--count", "5"));
// Default: the most recently written modded-profile progress save (the one a modded run uses).
string? progressPath = args.SkipWhile(a => a != "--progress").Skip(1).FirstOrDefault() ?? new DirectoryInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/SlayTheSpire2"))
    .EnumerateFiles("progress.save", SearchOption.AllDirectories).Where(f => f.FullName.Contains("/modded/"))
    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
    ?? throw new Exception("No modded progress.save found; pass --progress path/to/progress.save.");

// Unlocks decide which relics are in the pools, so mirror the real profile's revealed epochs.
var epochs = JsonDocument.Parse(File.ReadAllText(progressPath)).RootElement.GetProperty("epochs").EnumerateArray()
    .Where(e => e.GetProperty("state").GetString() == "revealed").Select(e => e.GetProperty("id").GetString()!)
    .Where(UnlockState.IsValidEpoch).ToList();
var unlocks = new UnlockState(epochs, [], 0);
Console.WriteLine($"{epochs.Count} revealed epochs from {progressPath}; ascension {ascension}");

CharacterModel[] characters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(), ModelDb.Character<Defect>(),
    ModelDb.Character<Necrobinder>(), ModelDb.Character<Regent>()];

int found = 0;
int from = int.Parse(Arg("--from", "1")), to = int.Parse(Arg("--to", "20000"));
for (int i = from; i <= (requested == null ? to : from) && found < wanted; i++)
{
    string seed = SeedHelper.CanonicalizeSeed(requested ?? $"BLOOD{i}");
    var hits = new List<string>();
    var lines = new List<string>();
    foreach (var character in characters)
    {
        var (options, reward) = Simulate(character, seed);
        bool capsule = options.Any(o => o is SmallCapsule);
        if (capsule && reward is Relic)
            hits.Add(character.Id.Entry);
        lines.Add($"  {character.Id.Entry,-12} Neow=[{string.Join(", ", options.Select(o => o.Id.Entry))}]" +
                  (capsule ? $"; Small Capsule -> {reward!.Id.Entry}" : ""));
    }
    if (hits.Count > 0 || requested != null)
    {
        Console.WriteLine($"{seed}: Bloodlust from Small Capsule for [{string.Join(", ", hits)}]");
        lines.ForEach(Console.WriteLine);
    }
    if (hits.Count > 0)
        found++;
}

(List<RelicModel> options, RelicModel? reward) Simulate(CharacterModel character, string seed)
{
    // NGame.StartNewSingleplayerRun, then RunManager.InitializeNewRun's grab-bag population.
    // Player.CreateForNewRun, minus its save-manager calls (marking starting relics as seen).
    var player = (Player)Activator.CreateInstance(typeof(Player), BindingFlags.Instance | BindingFlags.NonPublic, null,
        [character, 1UL, character.StartingHp, character.StartingHp, character.MaxEnergy, character.StartingGold, 3,
            character.BaseOrbSlotCount, new RelicGrabBag(), unlocks, null, null, null, null, null], null)!;
    AccessTools.Method(typeof(Player), "PopulateStartingDeck").Invoke(player, null);
    foreach (var relic in character.StartingRelics)
        player.AddRelicInternal(relic.ToMutable());
    var run = RunState.CreateForNewRun([player], ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), [],
        GameMode.Standard, ascension, seed);
    run.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(run.UnlockState), run.Rng.UpFront);
    player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);

    var neow = (Neow)ModelDb.Event<Neow>().ToMutable();
    AccessTools.Property(typeof(EventModel), nameof(EventModel.Owner)).SetValue(neow, player);
    AccessTools.Property(typeof(EventModel), nameof(EventModel.Rng)).SetValue(neow,
        new Rng(run.Rng.Seed + StringHelper.GetDeterministicHashCode(neow.Id.Entry)));
    var options = ((IReadOnlyList<EventOption>)AccessTools.Method(typeof(Neow), "GenerateInitialOptions").Invoke(neow, null)!)
        .Select(o => o.Relic!).ToList();
    if (!options.Any(o => o is SmallCapsule))
        return (options, null);
    // SmallCapsule.AfterObtained offers one RelicReward; Populate rolls rarity on the Rewards stream and pulls the front relic.
    var reward = new RelicReward(player);
    reward.Populate();
    return (options, reward.Relic);
}

static class Stubs
{
    public static bool Skip() => false;
    public static bool NoSaveManager(ref SaveManager __result) { __result = null!; return false; }
    public static bool OptionWithoutUi(RelicModel relic, ref EventOption __result)
    {
        __result = ((EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption))).WithRelic(relic);
        return false;
    }
}
