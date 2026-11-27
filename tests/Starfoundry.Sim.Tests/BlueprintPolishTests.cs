using System.Diagnostics;
using System.Numerics;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>Phase 5 of the blueprint editor: the walkthrough preview, a large station (60 modules, 400 machines and belts) in the editor model and in the tick loop, and determinism of what the editor sends.</summary>
public class BlueprintPolishTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private readonly ITestOutputHelper _out;

    public BlueprintPolishTests(ITestOutputHelper output) => _out = output;

    // ---- the walkthrough preview ---------------------------------------------------------------------------------------

    private static Dictionary<(int Mx, int My, int Mz), ModuleKind> Row(int n)
    {
        var m = new Dictionary<(int Mx, int My, int Mz), ModuleKind>();
        for (int i = 0; i < n; i++) m[(i, 0, 0)] = ModuleKind.Hull;
        m[(0, 1, 0)] = ModuleKind.Hull;
        return m;
    }

    [Fact]
    public void The_walkthrough_puts_the_eye_inside_a_module_at_eye_height_and_leaves_cleanly()
    {
        var mods = Row(3);
        var cam = new BlueprintCamera();
        cam.SetOrbit();
        float fov = cam.FovDegrees, yaw = cam.Yaw;
        cam.SetWalk((1, 0, 0));
        Assert.Equal(PlanView.Walk, cam.View);
        Assert.Equal(StationGrid.ModuleX * 1.5f, cam.Position.X, 3);
        Assert.Equal(BodyMotion.EyeHeight, cam.Position.Y, 3); // floor of row 0 plus the eye height
        Assert.Equal(StationGrid.ModuleZ * 0.5f, cam.Position.Z, 3);
        Assert.Equal(BlueprintCamera.WalkFov, cam.FovDegrees);
        Assert.True(cam.Position.Y < StationGrid.ModuleY); // inside the module, under its ceiling
        // looking towards the bow (negative z), level
        var fwd = -cam.Back;
        Assert.True(fwd.Z < -0.99f && Math.Abs(fwd.Y) < 1e-4f);
        // the pointer ray through the middle of the screen goes along the look direction from the eye
        var ray = cam.RayAt(640, 360, 1280, 720);
        Assert.True(Vector3.Distance(ray.Origin, cam.Position) < 1e-4f && Vector3.Dot(ray.Direction, fwd) > 0.999f);
        // the decks above the eye are hidden, the ones below stay
        Assert.True(cam.LayerVisible(0));
        Assert.False(cam.LayerVisible(1));
        // look around: the view turns but stays a walkthrough, pitch is clamped
        cam.Orbit(1f, 9f);
        Assert.Equal(PlanView.Walk, cam.View);
        Assert.True(cam.Pitch <= BlueprintCamera.MaxPitch);
        // pan and zoom do nothing in a walkthrough
        var eye = cam.Position;
        cam.Pan(0.3f, 0.3f); cam.Zoom(3f);
        Assert.Equal(eye, cam.Position);
        // leaving by any view button restores the field of view and the orbit angles
        cam.SetTop();
        Assert.Equal(PlanView.Top, cam.View);
        Assert.Equal(fov, cam.FovDegrees);
        cam.SetWalk((0, 0, 0));
        cam.SetOrbit();
        Assert.Equal(fov, cam.FovDegrees);
        Assert.Equal(yaw, cam.Yaw);
    }

    [Fact]
    public void Walking_stays_inside_the_modules_of_the_deck_and_slides_along_a_wall()
    {
        var mods = Row(2); // two modules along x on the deck 0
        var cam = new BlueprintCamera();
        cam.SetWalk((0, 0, 0));
        cam.Orbit(MathF.PI / 2f, 0f); // yaw a quarter turn: forward is now along -x? the test only needs to move about
        float start = cam.Position.X;
        for (int i = 0; i < 400; i++) cam.WalkBy(0.5f, 0f, mods);
        var p = cam.Position;
        Assert.InRange(p.X, 0f, 2 * StationGrid.ModuleX);
        Assert.InRange(p.Z, 0f, StationGrid.ModuleZ);
        for (int i = 0; i < 400; i++) cam.WalkBy(0f, 0.5f, mods);
        p = cam.Position;
        Assert.InRange(p.X, 0f, 2 * StationGrid.ModuleX);
        Assert.InRange(p.Z, 0f, StationGrid.ModuleZ);
        Assert.Equal(BodyMotion.EyeHeight, p.Y, 3);
        Assert.NotEqual(start, p.X); // it did move
        // a walkthrough ignores moves made from another view
        var orbit = new BlueprintCamera();
        var t = orbit.Target;
        orbit.WalkBy(5f, 5f, mods);
        Assert.Equal(t, orbit.Target);
    }

    [Fact]
    public void The_session_starts_the_walk_in_the_cockpit_of_a_ship_or_the_core_of_a_station()
    {
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: TestContent.LegacyShip);
        var s = new BlueprintSession(TestContent.Core, ShipHandling.Default, () => w, 0);
        s.Open();
        var cockpit = s.Draft.Modules.First(kv => kv.Value == ModuleKind.Cockpit).Key;
        Assert.True(s.StartWalk());
        Assert.Equal(PlanView.Walk, s.Camera.View);
        Assert.Equal((cockpit.Mx + 0.5f) * StationGrid.ModuleX, s.Camera.Position.X, 3);
        // an explicit module wins, a cell that is no module falls back
        var other = s.Draft.Modules.Keys.First(k => k != cockpit);
        Assert.True(s.StartWalk(other));
        Assert.Equal((other.Mx + 0.5f) * StationGrid.ModuleX, s.Camera.Position.X, 3);
        Assert.True(s.StartWalk((99, 99, 99)) && s.Camera.View == PlanView.Walk);

        var (sw, st) = Station(8, sandbox: true);
        var t = new BlueprintSession(C, ShipHandling.Default, () => sw, 0);
        t.Open(st);
        Assert.True(t.StartWalk());
        Assert.Equal(PlanView.Walk, t.Camera.View);
        Assert.Equal(0.5f * StationGrid.ModuleX, t.Camera.Position.X, 3); // the core is at module (0,0,0)
    }

    // ---- a large station: the editor model and the tick loop ---------------------------------------------------------

    /// <summary>A station of <paramref name="modules"/> modules: the core, a dock and a field of Fabrication bays on deck 0 (connected, sandbox, so nothing is priced).</summary>
    private static (SimWorld W, int Station) Station(int modules, bool sandbox)
    {
        var w = SimWorld.CreateGalaxy(C, sandbox ? GameMode.Sandbox : GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: WorldRules.Default.WithLegacyDockPieces(true));
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, StationGrid.ModuleZ * (long)Cell);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Dock);
        int placed = 2;
        for (int z = 0; placed < modules; z++)
            for (int x = -1; x >= -10 && placed < modules; x--, placed++)
                w.Network.AddStructureModuleUnchecked(st, x, 0, z, ModuleKind.Workshop);
        foreach (var item in C.Items) w.Network.GiveStock(st, item.Index, 1_000_000);
        // docked at the station (as the editor needs it): a dock lifts the build range, so the whole station can be edited and applied from the dock
        w.Submit(0, new DockAt(st, 0, 0, -1, Dir.PosX));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        return (w, st);
    }

    /// <summary>Fills the floor plan of every bay with a smelter and a one-cell belt until <paramref name="parts"/> parts are drawn.</summary>
    private static int Fill(BlueprintSession s, int parts)
    {
        int smelter = C.MachineIndex("core:smelter"), drawn = 0;
        foreach (var bay in s.Draft.Modules.Where(kv => kv.Value == ModuleKind.Workshop).Select(kv => kv.Key).Order().ToList())
        {
            if (drawn >= parts) break;
            Assert.True(s.OpenFloor(bay));
            int x0 = bay.Mx * StationGrid.ModuleX, z0 = bay.Mz * StationGrid.ModuleZ;
            for (int i = 0; i < 4 && drawn < parts; i++)
            {
                s.PickPlanMachine(smelter);
                Assert.Null(s.PlanClick((x0 + 1 + i * 2, 0, z0 + 1))); drawn++;
                s.SetPlanTool(PlanTool.Belt);
                if (drawn < parts) { Assert.Null(s.PlanClick((x0 + 1 + i * 2, 0, z0 + 3))); drawn++; }
            }
            s.CloseFloor();
        }
        return drawn;
    }

    [Fact]
    public void A_station_of_60_modules_and_400_parts_edits_prices_applies_and_ticks_within_loose_budgets()
    {
        var (w, st) = Station(60, sandbox: true);
        var sw = Stopwatch.StartNew();
        var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        s.Open(st);
        double openMs = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(60, s.Draft.Modules.Count);

        sw.Restart();
        int drawn = Fill(s, 400);
        double drawMs = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(400, drawn);
        Assert.Equal(400, s.Interior.PartCount);

        // one more edit re-derives diff, cost, stats, validation and the verdict from scratch: that is the per-click cost
        sw.Restart();
        const int clicks = 20;
        for (int i = 0; i < clicks; i++) { s.Draft.Add((20 + i, 0, 0), ModuleKind.Hull, C); s.Draft.Undo(); }
        double editMs = sw.Elapsed.TotalMilliseconds / (clicks * 2);

        sw.Restart();
        var cmd = s.ToCommand();
        var report = w.CheckBlueprint(0, st, ((ApplyBlueprint)cmd).Modules, ((ApplyBlueprint)cmd).Interior);
        double checkMs = sw.Elapsed.TotalMilliseconds;
        Assert.True(report.Ok, report.Reject.ToString());

        sw.Restart();
        w.Submit(0, cmd);
        w.Step();
        double applyMs = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(Reject.None, w.LastResults.Last().Reject);
        Assert.Equal(400, w.Network.ReadInterior(st).Machines.Length + w.Network.ReadInterior(st).Belts.Length);

        // a design of this size saves, reads back and loads in well under a second
        sw.Restart();
        var lib = new BlueprintLibrary(new MemoryBlueprintStore(), C);
        var file = lib.Save(s.ToDesign("Big hall"));
        Assert.NotNull(file);
        var back = lib.Load(file!);
        Assert.Equal(s.Interior.ToInterior(), back!.Interior);
        double saveMs = sw.Elapsed.TotalMilliseconds;

        // the simulation keeps its tick budget with the whole factory standing (smelters idle without input; belts empty)
        for (int i = 0; i < 20; i++) w.Step(); // warm up
        sw.Restart();
        const int ticks = 300;
        for (int i = 0; i < ticks; i++) w.Step();
        double tickMs = sw.Elapsed.TotalMilliseconds / ticks;

        _out.WriteLine($"60 modules, 400 parts: open {openMs:F1} ms, draw 400 parts {drawMs:F0} ms, one edit {editMs:F2} ms, check {checkMs:F1} ms, apply {applyMs:F1} ms, save+load {saveMs:F1} ms, tick {tickMs:F3} ms");
        Assert.True(openMs < 2000, $"open {openMs} ms");
        Assert.True(editMs < 250, $"one edit {editMs} ms");
        Assert.True(checkMs < 1000, $"check {checkMs} ms");
        Assert.True(applyMs < 2000, $"apply {applyMs} ms");
        Assert.True(saveMs < 1000, $"save {saveMs} ms");
        Assert.True(tickMs < 20, $"tick {tickMs} ms (the budget is about 33 ms at 30 ticks per second)");
    }

    // ---- determinism ----------------------------------------------------------------------------------------------------

    private static string RunOnce(out byte[] log)
    {
        var (w, st) = Station(20, sandbox: true);
        var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        s.Open(st);
        Fill(s, 60);
        s.StartWalk(); // the preview and the library never reach the simulation
        var lib = new BlueprintLibrary(new MemoryBlueprintStore(), C);
        lib.Save(s.ToDesign("x"));
        w.Submit(0, s.ToCommand());
        for (int i = 0; i < 200; i++) w.Step();
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        log = ms.ToArray();
        return w.Checksum().ToString();
    }

    [Fact]
    public void The_same_editor_session_gives_the_same_command_log_and_world_hash_and_the_log_replays()
    {
        string a = RunOnce(out var la), b = RunOnce(out var lb);
        Assert.Equal(a, b);
        Assert.Equal(la, lb);
        // replay the log into a fresh world: same checksum
        var read = CommandCodec.ReadLog(new MemoryStream(la));
        var (fresh, _) = Station(20, sandbox: true);
        var replay = SimWorld.Replay(read, 200, initial: fresh);
        Assert.Equal(a, replay.Checksum().ToString());
    }
}
