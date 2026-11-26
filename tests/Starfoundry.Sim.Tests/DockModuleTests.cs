using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0041: ships and stations each carry a dedicated dock module and dock only through it. The core and the cockpit stop being ports unless a world reads an
/// older log (<see cref="WorldRules.LegacyDock"/>); the starter ship, every scenario start, a founded station and the designer keep a dock module.
/// </summary>
public class DockModuleTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;

    private static readonly ContentDb C = TestContent.Core;
    private static readonly ContentDb Shipped = TestContent.CoreFloor;

    private static SimWorld World(ContentDb? content = null, GameMode mode = GameMode.Sandbox, WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(content ?? C, mode, Seed, threats: false, stationRules: false, structures: true, rules: rules);

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static int Docks(Structure s) => s.Modules.Values.Count(k => k == ModuleKind.Dock);

    /// <summary>The ship docked at a station with a core, a dock module at (0,0,-1) and a shipyard at (0,0,1), the way a founded station is laid out.</summary>
    private static SimWorld Yard(out int ship, out int station)
    {
        var w = World();
        ship = w.Network.ShipStructure(0);
        station = w.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, StationGrid.ModuleZ * (long)Cell);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, -1, ModuleKind.Dock);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Shipyard);
        Assert.Equal(Reject.None, Run(w, new DockAt(station, 0, 0, -1, Dir.PosX)));
        return w;
    }

    // ---- the starter ship and the scenarios -----------------------------------------------------------------------------

    [Fact]
    public void The_starter_ship_has_a_dock_module_on_the_flank_of_its_cockpit()
    {
        foreach (var content in new[] { C, Shipped })
        {
            var w = World(content);
            var ship = w.Network.GetStructure(w.Network.ShipStructure(0));
            Assert.Equal(1, Docks(ship));
            Assert.Equal(ModuleKind.Dock, ship.Modules[(-1, 0, 0)]); // the -X flank of the cockpit, where its old port was
            Assert.Equal(ModuleKind.Cockpit, ship.Modules[(0, 0, 0)]);
            var ports = w.Network.DockPorts(ship.Id).ToList();
            Assert.All(ports, p => Assert.Equal((-1, 0, 0), (p.Mx, p.My, p.Mz)));
            Assert.Contains(ports, p => p.Face == Dir.NegX);
            w.ValidateInvariants();
        }
    }

    [Fact]
    public void Every_scenario_start_ship_and_station_has_a_dock_module()
    {
        var catalog = TestContent.ScenariosFloor;
        int worlds = 0;
        foreach (var def in catalog.All)
        {
            var w = SimWorld.CreateScenario(Shipped, def, Seed);
            foreach (var s in w.Network.Structures())
            {
                Assert.True(Docks(s) >= 1, $"{def.Id}: {s.Kind} {s.Id} has no dock module");
                Assert.NotEmpty(w.Network.DockPorts(s.Id));
            }
            w.ValidateInvariants();
            worlds++;
        }
        Assert.True(worlds >= 8);
    }

    [Fact]
    public void A_legacy_dock_world_keeps_the_old_templates_and_ports()
    {
        var w = World(rules: TestContent.OldDock);
        var ship = w.Network.GetStructure(w.Network.ShipStructure(0));
        Assert.Equal(0, Docks(ship));
        Assert.Equal(new[] { Dir.PosX, Dir.NegX }, w.Network.DockPorts(ship.Id).Select(p => p.Face).ToArray()); // the cockpit's two flanks
        Assert.True(BeltNetwork.HasPorts(StructureKind.Station, ModuleKind.Core));
    }

    // ---- ports ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Only_dock_modules_are_ports_the_core_and_the_cockpit_are_not()
    {
        var w = World();
        int station = w.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        Assert.Empty(w.Network.DockPorts(station)); // a bare core has no port
        Assert.False(w.Network.ModuleHasPorts(StructureKind.Station, ModuleKind.Core));
        Assert.False(w.Network.ModuleHasPorts(StructureKind.Ship, ModuleKind.Cockpit));
        Assert.True(w.Network.ModuleHasPorts(StructureKind.Station, ModuleKind.Dock));
        Assert.True(w.Network.ModuleHasPorts(StructureKind.Ship, ModuleKind.Dock));
        // The ship has a port (its dock module) but the core offers none to join, whatever the face.
        foreach (var face in DockRules.PortFaces)
            Assert.Equal(Reject.NoFreePort, Run(w, new DockAt(station, 0, 0, 0, face)));
        Assert.False(w.Network.IsDocked(0));

        // A dock module on the station opens its free faces; the ship's own dock module joins one.
        w.Network.AddStructureModuleUnchecked(station, 0, 0, -1, ModuleKind.Dock);
        var ports = w.Network.DockPorts(station).ToList();
        Assert.Equal(3, ports.Count); // the face against the core is closed
        Assert.All(ports, p => Assert.Equal((0, 0, -1), (p.Mx, p.My, p.Mz)));
        w.ValidateInvariants();
    }

    [Fact]
    public void A_ship_docks_with_its_dock_module_at_a_station_dock_module_and_the_ports_meet()
    {
        var w = Yard(out int ship, out int station);
        var net = w.Network;
        Assert.True(net.TryGetDock(0, out var dock));
        Assert.Equal((-1, 0, 0, Dir.NegX), (dock.ShipMx, dock.ShipMy, dock.ShipMz, dock.ShipFace));
        Assert.Equal((0, 0, -1, Dir.PosX), (dock.StationMx, dock.StationMy, dock.StationMz, dock.StationFace));
        Assert.Equal(net.PortWorldPoint(station, 0, 0, -1, Dir.PosX), net.PortWorldPoint(ship, -1, 0, 0, Dir.NegX));
        Assert.Equal(ModuleKind.Dock, net.GetStructure(ship).Modules[(dock.ShipMx, dock.ShipMy, dock.ShipMz)]);
        Assert.Equal(ModuleKind.Dock, net.GetStructure(station).Modules[(dock.StationMx, dock.StationMy, dock.StationMz)]);
        w.ValidateInvariants();
    }

    [Fact]
    public void A_ship_without_a_free_dock_port_cannot_dock_even_beside_a_core()
    {
        var w = World();
        int ship = w.Network.ShipStructure(0);
        // Wall the ship's dock module in: nothing touches the ship's free faces any more.
        w.Network.AddStructureModuleUnchecked(ship, -1, 0, 1, ModuleKind.Hull);
        w.Network.AddStructureModuleUnchecked(ship, -1, 0, -1, ModuleKind.Hull);
        w.Network.AddStructureModuleUnchecked(ship, -2, 0, 0, ModuleKind.Hull);
        Assert.Empty(w.Network.DockPorts(ship));
        int station = w.Network.CreateStructure(0, StructureKind.Station, -40L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, -1, ModuleKind.Dock);
        Assert.Equal(Reject.NoFreePort, Run(w, new DockAt(station, 0, 0, -1, Dir.PosX)));
    }

    // ---- founding and removing ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_founded_station_has_its_core_and_a_dock_module_at_no_extra_cost()
    {
        var w = World(Shipped, GameMode.Campaign);
        var core = Shipped.Structures!.ModuleCost(ModuleKind.Core);
        foreach (var c in core) w.Network.GiveItems(0, c.Item, c.Count);
        var before = core.Select(c => w.Network.HoldCount(0, c.Item)).ToArray();
        Assert.Equal(Reject.None, Run(w, new FoundStation(14, 0, 4)));
        var st = w.Network.Structures().Single(s => s.Kind == StructureKind.Station);
        Assert.Equal(2, st.Modules.Count);
        Assert.Equal(ModuleKind.Core, st.Modules[(0, 0, 0)]);
        Assert.Equal(ModuleKind.Dock, st.Modules[(0, 0, -1)]);
        Assert.Equal(3, w.Network.DockPorts(st.Id).Count());
        Assert.All(core, c => Assert.Equal(0, w.Network.HoldCount(0, c.Item))); // only the core was paid
        Assert.Equal(1, w.Network.StationsFounded);
        w.ValidateInvariants();
    }

    [Fact]
    public void Founding_puts_the_dock_on_the_first_free_side_and_refuses_when_none_is_free()
    {
        var w = World();
        // A second station whose core touches the first one: the -Z side of its core is taken by the first station's volume, so the dock goes to +Z.
        Assert.Equal(Reject.None, Run(w, new FoundStation(14, 0, 4)));
        Assert.Equal(Reject.None, Run(w, new FoundStation(14, 0, 4 + StationGrid.ModuleZ)));
        var second = w.Network.Structures().Where(s => s.Kind == StructureKind.Station).OrderBy(s => s.Id).Last();
        var key = second.Modules.Single(kv => kv.Value == ModuleKind.Dock).Key;
        Assert.Equal((0, 0, 1), key);
        w.ValidateInvariants();
    }

    [Fact]
    public void The_last_dock_module_of_a_ship_or_a_station_cannot_be_removed_but_a_second_can()
    {
        var w = World();
        int ship = w.Network.ShipStructure(0);
        Assert.Equal(Reject.LastDockModule, Run(w, new RemoveModuleIn(ship, -1, 0, 0)));
        Assert.Equal(1, Docks(w.Network.GetStructure(ship)));

        Assert.Equal(Reject.None, Run(w, new FoundStation(8, 0, 0)));
        int st = w.Network.Structures().Single(s => s.Kind == StructureKind.Station).Id;
        Assert.Equal(Reject.LastDockModule, Run(w, new RemoveModuleIn(st, 0, 0, -1)));
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(st, 0, 0, 1, ModuleKind.Dock)));
        Assert.Equal(Reject.None, Run(w, new RemoveModuleIn(st, 0, 0, -1)));
        Assert.Equal(Reject.LastDockModule, Run(w, new RemoveModuleIn(st, 0, 0, 1)));
        w.ValidateInvariants();

        // A legacy world has no such rule.
        var legacy = World(rules: TestContent.OldDock);
        int ls = legacy.Network.CreateStructure(0, StructureKind.Station, 8L * Cell, 0, 0);
        legacy.Network.AddStructureModuleUnchecked(ls, 0, 0, 0, ModuleKind.Core);
        legacy.Network.AddStructureModuleUnchecked(ls, 0, 0, 1, ModuleKind.Dock);
        Assert.Equal(Reject.None, Run(legacy, new RemoveModuleIn(ls, 0, 0, 1)));
    }

    // ---- the designer -----------------------------------------------------------------------------------------------------

    private static List<DesignModule> Current(SimWorld w, int ship) =>
        w.Network.GetStructure(ship).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToList();

    [Fact]
    public void A_design_without_a_dock_module_is_refused_and_one_with_a_moved_dock_keeps_the_docked_port()
    {
        var w = Yard(out int ship, out _);
        var noDock = Current(w, ship);
        noDock.RemoveAll(m => m.Kind == ModuleKind.Dock);
        noDock.Add(new DesignModule(-1, 0, 0, ModuleKind.Hull));
        Assert.Equal(Reject.NoDockModule, w.CheckDesign(0, noDock).Reject);
        Assert.Equal(Reject.NoDockModule, Run(w, new DesignerApply(noDock.ToArray())));
        Assert.Equal(1, Docks(w.Network.GetStructure(ship)));

        // A second dock module on the other flank is fine, and the first one may then not move (it is the docked port).
        var two = Current(w, ship);
        two.Add(new DesignModule(1, 0, 0, ModuleKind.Dock));
        Assert.Equal(Reject.None, Run(w, new DesignerApply(two.ToArray())));
        Assert.Equal(2, Docks(w.Network.GetStructure(ship)));
        var moved = Current(w, ship);
        moved.RemoveAll(m => m.Kind == ModuleKind.Dock && m.Mx == -1);
        moved.Add(new DesignModule(-1, 0, 1, ModuleKind.Dock));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new DesignerApply(moved.ToArray())));
        w.ValidateInvariants();
    }

    [Fact]
    public void The_design_editor_and_the_blueprint_draft_cannot_drop_the_last_dock_module()
    {
        var w = Yard(out int ship, out _);
        var editor = new ShipDesignEditor();
        editor.Load(w.Network.GetStructure(ship).Modules);
        Assert.Equal(DesignEditResult.LastDock, editor.Remove((-1, 0, 0)));
        Assert.Equal(DesignEditResult.Ok, editor.Add((1, 0, 0), ModuleKind.Dock, C));
        Assert.Equal(DesignEditResult.Ok, editor.Remove((-1, 0, 0)));
        Assert.Equal(DesignEditResult.LastDock, editor.Remove((1, 0, 0)));
        editor.RequireDock = false;
        Assert.Equal(DesignEditResult.Ok, editor.Remove((1, 0, 0)));

        var session = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        session.Open();
        Assert.DoesNotContain(session.Validation.Entries, e => e.Code == "noDock");
        session.Draft.Remove((-1, 0, 0));
        var entry = Assert.Single(session.Validation.Entries, e => e.Code == "noDock");
        Assert.Equal(ValidationSeverity.Error, entry.Severity);
        Assert.True(entry.Blocks);
        Assert.False(session.CanApply);
        Assert.NotEqual(string.Empty, entry.Text(Language.Es));
        Assert.NotEqual(entry.Text(Language.Es), entry.Text(Language.En));
        Assert.DoesNotContain("⟦", entry.Text(Language.En));
    }

    [Fact]
    public void The_dock_module_texts_exist_in_both_languages()
    {
        foreach (var key in new[] { "bp.val.noDock", "toast.reject.noDockModule", "toast.reject.lastDockModule", "designer.edit.lastDock", "module.dock", "module.dock.purpose" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
        Assert.Equal("toast.reject.noDockModule", RejectTexts.Key(Reject.NoDockModule));
        Assert.Equal("toast.reject.lastDockModule", RejectTexts.Key(Reject.LastDockModule));
    }

    // ---- the rule flag and the command log ----------------------------------------------------------------------------------

    [Fact]
    public void The_flag_is_a_bit_of_the_rules_block_off_by_default_and_hashes_as_default_rules()
    {
        Assert.False(WorldRules.Default.LegacyDock);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyDock));
        var legacy = new WorldRules().WithLegacyDock(true);
        Assert.True(legacy.LegacyDock && legacy.AnyLegacy);
        Assert.NotEqual(WorldRules.Default, legacy);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.True(legacy.HashesAsDefault);
        Assert.False(legacy.WithoutLegacyFlags().LegacyDock);
        Assert.Equal(legacy.WithLegacyField(true).WithLegacyDock(false), new WorldRules().WithLegacyField(true));
        var bad = legacy.Serialize();
        int flags2 = Array.IndexOf(bad, (byte)32);
        Assert.True(flags2 > 0);
        bad[flags2] = 128;
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(bad));
        Assert.Equal(legacy.Clamped(), legacy);
    }

    [Fact]
    public void Logs_older_than_version_22_read_with_the_flag_and_replay_as_recorded_and_new_logs_read_without_it()
    {
        // A world of the old rules: the cockpit and the core are the ports, the starter ship has no dock module.
        var recorded = World(rules: TestContent.OldDock);
        int ship = recorded.Network.ShipStructure(0);
        int station = recorded.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        recorded.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        recorded.Submit(0, new DockAt(station, 0, 0, 0, Dir.PosX));
        recorded.Step(30);
        Assert.True(recorded.Network.IsDocked(0));
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: recorded.Network.Rules);
        var bytes = ms.ToArray();
        Assert.Equal(23, bytes[4]);
        for (byte version = 12; version <= 21; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            var log = CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyDock, $"version {version}");
            if (version != 21) continue; // older headers also read the flags of the rules that came in between, which this hand-built world does not have
            Assert.True(rules.HashesAsDefault);
            var fresh = World(rules: rules);
            int fs = fresh.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0); // the station of the test, built by hand as the recorded one
            fresh.Network.AddStructureModuleUnchecked(fs, 0, 0, 0, ModuleKind.Core);
            var replay = SimWorld.Replay(log, recorded.Tick, initial: fresh);
            Assert.Equal(recorded.Checksum(), replay.Checksum());
            Assert.True(replay.Network.IsDocked(0));
            Assert.Equal(0, Docks(replay.Network.GetStructure(replay.Network.ShipStructure(0))));
        }

        // A log written now for default rules reads with the flag off; the same header at version 21 reads with it on and hashes as default rules.
        var fresh2 = new MemoryStream();
        CommandCodec.WriteLog(fresh2, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, structures: true);
        var current = fresh2.ToArray();
        CommandCodec.ReadLog(new MemoryStream(current), out _, out _, out _, out _, out _, out _, out var cur);
        Assert.False(cur.LegacyDock);
        var older = (byte[])current.Clone();
        older[4] = 21;
        CommandCodec.ReadLog(new MemoryStream(older), out _, out _, out _, out _, out _, out _, out var old2);
        Assert.True(old2.LegacyDock && old2.HashesAsDefault && old2.WithoutLegacyFlags().IsDefault);
        Assert.True(ship > 0);
    }

    [Fact]
    public void A_new_world_replays_and_clones_with_dock_modules_and_a_dock_command()
    {
        var w = Yard(out _, out _);
        w.Step(60);
        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        // The yard is built by hand (not part of the log), so only the ship's own state is compared after the same commands on a hand-built twin.
        var twin = World();
        int ts = twin.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, StationGrid.ModuleZ * (long)Cell);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, 0, ModuleKind.Core);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, -1, ModuleKind.Dock);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, 1, ModuleKind.Shipyard);
        var again = SimWorld.Replay(w.Log, w.Tick, initial: twin);
        Assert.Equal(w.Checksum(), again.Checksum());
    }
}
