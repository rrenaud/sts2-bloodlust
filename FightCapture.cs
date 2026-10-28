using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Relic = Bloodlust.Relics.Bloodlust;

namespace Bloodlust;

// Screenshots at the start of each fight (once the first player turn is ready, after Bloodlust's Vigor)
// and at its end, into user://bloodlust_screenshots/<run start>/, with a log.txt of Vigor and stash per shot.
// Purely observational: any error turns capturing off for the session instead of touching the game.
internal static class FightCapture
{
    internal static string Root => Path.Combine(ProjectSettings.GlobalizePath("user://"), "bloodlust_screenshots");

    private static RunState? _run;
    private static string? _directory;
    private static int _next;
    private static bool _failed;
    // PNG encoding hitches a frame, so saves run in order on one background chain.
    private static Task _writes = Task.CompletedTask;

    internal static void Initialize()
    {
        CombatManager.Instance.TurnStarted += state =>
        {
            if (state.RoundNumber == 1 && state.CurrentSide == CombatSide.Player)
                CaptureNextFrame("start");
        };
        CombatManager.Instance.CombatEnded += _ => CaptureNextFrame("end");
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
            _directory = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_directory);
            _next = 1;
        }
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
