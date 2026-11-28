using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0047: the dock is a small docking collar on a horizontal face of an ordinary module, not a dock module. Covers the rule, the commands that carry collars (tags 67 to 69),
/// the price, determinism and replay, the rules flag that keeps the dock-module worlds of older logs, and the engine-free draft of the blueprint editor.
/// </summary>
public class DockPieceTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261127UL;

    private static readonly ContentDb C = TestContent.CoreFloor;
    private static int Item(string id) => C.ItemIndex(id);

    private static SimWorld World(GameMode mode = GameMode.Campaign, WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: false, structures: true, rules: rules ?? WorldRules.Default);

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static void Stock(SimWorld w, int st)
    {
        foreach (var item in new[] { "core:iron_ingot", "core:iron_plate", "core:carbon", "core:circuit", "core:titanium_alloy", "core:silicon", "core:fuel_cell" })
            w.Network.GiveStock(st, Item(item), 1000);
    }

    /// <summary>A station beside the starter ship: a core with a collar on its +X face facing the ship's -X flank collar, a shipyard on its +Z side and a bay on its -X side; docked.</summary>
    private static (SimWorld W, int Ship, int Station) Yard(GameMode mode = GameMode.Campaign)
    {
        var w = World(mode);
        int ship = w.Network.ShipStructure(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -16L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Shipyard);
        w.Network.AddStructureModuleUnchecked(st, -1, 0, 0, ModuleKind.Workshop);
        w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.PosX);
        Stock(w, st);
        Assert.Equal(Reject.None, Run(w, new DockAt(st, 0, 0, 0, Dir.PosX)));
        return (w, ship, st);
    }

    private static string Shape(SimWorld w, int s) =>
        string.Join(";", w.Network.GetStructure(s).Modules.Select(kv => $"{kv.Key}{kv.Value}")) + "|" + string.Join(";", w.Network.DockPieces(s).Select(p => $"{p.Mx},{p.My},{p.Mz}{p.Face}"));

    private static DesignModule[] Modules(SimWorld w, int s) =>
        w.Network.GetStructure(s).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();

    // ---- the rule ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_starter_ship_and_every_scenario_start_dock_through_collars_and_no_structure_has_a_dock_module()
    {
        var w = World();
        var ship = w.Network.GetStructure(w.Network.ShipStructure(0));
        Assert.DoesNotContain(ModuleKind.Dock, ship.Modules.Values);
        Assert.Equal(new[] { Dir.NegX, Dir.PosX }.OrderBy(d => (int)d), w.Network.DockPieces(ship.Id).Select(p => p.Face).OrderBy(d => (int)d)); // both flanks of the cockpit: the -X one is where the old port was
        Assert.All(w.Network.DockPieces(ship.Id), p => Assert.Equal((0, 0, 0), (p.Mx, p.My, p.Mz)));
        Assert.Equal(2, w.Network.DockPorts(ship.Id).Count());
        w.ValidateInvariants();

        int worlds = 0;
        foreach (var def in TestContent.ScenariosFloor.All)
        {
            var s = SimWorld.CreateScenario(C, def, Seed);
            foreach (var st in s.Network.Structures())
            {
                Assert.DoesNotContain(ModuleKind.Dock, st.Modules.Values);
                Assert.True(s.Network.DockPieces(st.Id).Any(), $"{def.Id}: {st.Kind} {st.Id} has no docking collar");
                Assert.NotEmpty(s.Network.DockPorts(st.Id));
            }
            s.ValidateInvariants();
            worlds++;
        }
        Assert.True(worlds >= 8);
    }

    [Fact]
    public void A_bare_core_is_no_port_until_it_wears_a_collar_and_a_module_against_the_face_closes_it()
    {
        var w = World();
        int st = w.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Hull);
        Assert.Empty(w.Network.DockPorts(st));
        foreach (var face in DockRules.PortFaces) Assert.Equal(Reject.NoFreePort, Run(w, new DockAt(st, 0, 0, 0, face)));
        w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.NegZ);
        Assert.Equal(Dir.NegZ, Assert.Single(w.Network.DockPorts(st)).Face);
        w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.PosX); // the hull module stands against that face: a collar there is no port
        Assert.Single(w.Network.DockPorts(st));
        Assert.Throws<InvalidOperationException>(() => w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.PosX)); // the same face twice
        Assert.False(DockRules.CanHostPiece(ModuleKind.Core, Dir.PosY));
        Assert.False(DockRules.CanHostPiece(ModuleKind.Engine, Dir.PosX));
        Assert.False(DockRules.CanHostPiece(ModuleKind.Defence, Dir.PosX));
        Assert.False(DockRules.CanHostPiece(ModuleKind.Cockpit, Dir.PosZ));
        Assert.True(DockRules.CanHostPiece(ModuleKind.Cockpit, Dir.NegX));
        Assert.True(DockRules.CanHostPiece(ModuleKind.Hull, Dir.NegZ));
        Assert.True(w.Network.HasDockPiece(st, 0, 0, 0, Dir.NegZ));
    }

    [Fact]
    public void A_ship_docks_collar_to_collar_and_the_ports_meet()
    {
        var (w, ship, st) = Yard();
        var net = w.Network;
        Assert.True(net.TryGetDock(0, out var dock));
        Assert.Equal((0, 0, 0, Dir.NegX), (dock.ShipMx, dock.ShipMy, dock.ShipMz, dock.ShipFace));
        Assert.Equal((0, 0, 0, Dir.PosX), (dock.StationMx, dock.StationMy, dock.StationMz, dock.StationFace));
        Assert.Equal(net.PortWorldPoint(st, 0, 0, 0, Dir.PosX), net.PortWorldPoint(ship, 0, 0, 0, Dir.NegX));
        Assert.Equal(ship, net.DockedShips(st).Single());
        Assert.False(Assert.Single(net.DockPorts(st), p => p.Face == Dir.PosX).Free);
        w.Step(30);
        w.ValidateInvariants();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        w.ValidateInvariants();
    }

    [Fact]
    public void The_dock_module_is_retired_in_a_new_world()
    {
        var (w, ship, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(ship, 1, 0, 0, ModuleKind.Dock)));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new ApplyBlueprint(st, Modules(w, st).Append(new DesignModule(1, 0, 0, ModuleKind.Dock)).ToArray())));
        Assert.Empty(w.Network.GetStructure(ship).Modules.Values.Where(k => k == ModuleKind.Dock));
    }

    [Fact]
    public void A_collar_goes_with_its_module_and_the_last_one_cannot_be_taken_off_the_structure()
    {
        var w = World(GameMode.Sandbox, WorldRules.Default.WithLegacyBlueprint(true).WithLegacyInterior(true));
        int st = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Hull);
        w.Network.AddDockPieceUnchecked(st, 1, 0, 0, Dir.PosX);
        Assert.Equal(Reject.LastDockModule, Run(w, new RemoveModuleIn(st, 1, 0, 0)));
        w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.NegZ);
        Assert.Equal(Reject.None, Run(w, new RemoveModuleIn(st, 1, 0, 0))); // the other collar is left
        Assert.Equal(new[] { new DockPieceSpec(0, 0, 0, Dir.NegZ) }, w.Network.DockPieces(st).ToArray());
        w.ValidateInvariants();
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(st, 1, 0, 0, ModuleKind.Hull)));
        Assert.Single(w.Network.DockPieces(st)); // a module put back does not bring the collar back
    }

    [Fact]
    public void A_founded_station_comes_with_a_collar_on_its_core_for_free_and_needs_no_clear_side()
    {
        var w = World();
        var core = C.Structures!.ModuleCost(ModuleKind.Core);
        foreach (var c in core) w.Network.GiveItems(0, c.Item, c.Count);
        Assert.Equal(Reject.None, Run(w, new FoundStation(14, 0, 4)));
        var st = w.Network.Structures().Single(s => s.Kind == StructureKind.Station);
        Assert.Equal(new[] { ModuleKind.Core }, st.Modules.Values.ToArray());
        Assert.Equal(new[] { Dir.NegX, Dir.PosX }.OrderBy(d => (int)d), w.Network.DockPieces(st.Id).Select(p => p.Face).OrderBy(d => (int)d)); // a ship docks axis-aligned: it meets an X face
        Assert.All(core, c => Assert.Equal(0, w.Network.HoldCount(0, c.Item))); // only the core was paid
        Assert.Equal(DockRules.CoreStockCapacity + 2 * DockRules.DockStockCapacity, w.Network.StockCapacity(st.Id)); // a collar adds what a dock module did
        w.ValidateInvariants();
    }

    // ---- designs and blueprints that carry collars ---------------------------------------------------------------------

    [Fact]
    public void A_blueprint_without_collars_keeps_them_and_one_that_lists_them_sets_them_exactly()
    {
        var (w, _, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        var mods = Modules(w, st);
        // null: the collars that still have a host stay
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, mods.Append(new DesignModule(1, 0, 0, ModuleKind.Hull)).ToArray())));
        Assert.Single(w.Network.DockPieces(st));
        // an explicit set: the old collar goes, two new ones come
        var set = new[] { new DockPieceSpec(1, 0, 0, Dir.PosX), new DockPieceSpec(0, 0, 0, Dir.NegZ) };
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), null, set)));
        Assert.Equal(set.OrderBy(p => (p.Mx, p.My, p.Mz, (int)p.Face)).ToArray(), w.Network.DockPieces(st).ToArray());
        // an empty set: refused, a structure keeps a dock
        Assert.Equal(Reject.NoDockModule, Run(w, new ApplyBlueprint(st, Modules(w, st), null, Array.Empty<DockPieceSpec>())));
        // a blueprint that lists a collar on a module it removes is refused as a bad collar
        Assert.Equal(Reject.BadDockPiece, Run(w, new ApplyBlueprint(st, Modules(w, st).Where(m => (m.Mx, m.My, m.Mz) != (1, 0, 0)).ToArray(), null, new[] { set[0] })));
        w.ValidateInvariants();
    }

    [Fact]
    public void Collars_that_cannot_be_carried_are_refused_and_change_nothing()
    {
        var (w, _, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        var mods = Modules(w, st);
        var before = Shape(w, st);
        Assert.Equal(Reject.BadDockPiece, Run(w, new ApplyBlueprint(st, mods, null, new[] { new DockPieceSpec(5, 0, 5, Dir.PosX) }))); // no module there
        Assert.Equal(Reject.BadDockPiece, Run(w, new ApplyBlueprint(st, mods.Append(new DesignModule(1, 0, 0, ModuleKind.Defence)).ToArray(), null, new[] { new DockPieceSpec(1, 0, 0, Dir.PosX) }))); // a defence deck cannot host one
        Assert.Equal(Reject.InvalidArgument, Run(w, new ApplyBlueprint(st, mods, null, new[] { new DockPieceSpec(0, 0, 0, Dir.PosX), new DockPieceSpec(0, 0, 0, Dir.PosX) })));
        Assert.Throws<ArgumentException>(() => w.Submit(0, new ApplyBlueprint(st, mods, null, new[] { new DockPieceSpec(0, 0, 0, Dir.PosY) }))); // not a horizontal face: not even a well-formed command
        Assert.Equal(before, Shape(w, st));
        w.ValidateInvariants();
    }

    [Fact]
    public void A_collar_a_ship_is_docked_at_cannot_move_or_go_but_another_can()
    {
        var (w, ship, st) = Yard();
        var mods = Modules(w, st);
        // the docked collar leaves its face: refused on both sides
        Assert.Equal(Reject.ModuleOccupied, Run(w, new ApplyBlueprint(st, mods, null, new[] { new DockPieceSpec(0, 0, 0, Dir.NegZ) })));
        var shipMods = Modules(w, ship);
        Assert.Equal(Reject.ModuleOccupied, Run(w, new ApplyBlueprint(ship, shipMods, null, new[] { new DockPieceSpec(0, 0, 0, Dir.PosX) })));
        // a second collar is fine, the docked one stays
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, mods, null, new[] { new DockPieceSpec(0, 0, 0, Dir.PosX), new DockPieceSpec(0, 0, 0, Dir.NegZ) })));
        Assert.Equal(2, w.Network.DockPieces(st).Count());
        Assert.True(w.Network.IsDocked(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void A_collar_costs_half_a_dock_module_a_moved_one_is_free_and_a_removed_one_refunds_half_and_the_preview_agrees()
    {
        var (w, _, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        var dockCost = C.Structures!.ModuleCost(ModuleKind.Dock);
        var mods = Modules(w, st);
        var more = new[] { new DockPieceSpec(0, 0, 0, Dir.PosX), new DockPieceSpec(0, 0, 0, Dir.NegZ) };
        var add = w.CheckBlueprint(0, st, mods, null, more);
        Assert.True(add.Ok);
        foreach (var c in dockCost) Assert.Equal((c.Count + 1) / 2, add.Cost.Where(x => x.Item == c.Item).Sum(x => x.Count));
        var move = w.CheckBlueprint(0, st, mods, null, new[] { new DockPieceSpec(0, 0, 0, Dir.NegZ) });
        Assert.True(move.Ok);
        Assert.Empty(move.Cost); // one collar off a face and onto another costs nothing
        Assert.Empty(move.Refund);
        // pay then take off: the refund is half of the price
        var before = dockCost.ToDictionary(c => c.Item, c => w.Network.StockCount(st, c.Item));
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, mods, null, more)));
        foreach (var c in dockCost) Assert.Equal(before[c.Item] - (c.Count + 1) / 2, w.Network.StockCount(st, c.Item));
        var back = w.CheckBlueprint(0, st, mods, null, new[] { more[0] });
        Assert.True(back.Ok);
        foreach (var c in dockCost) Assert.Equal((c.Count + 1) / 2 / 2, back.Refund.Where(x => x.Item == c.Item).Sum(x => x.Count));
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, mods, null, new[] { more[0] })));
    }

    [Fact]
    public void The_designer_keeps_a_ship_docking_and_a_ship_design_may_set_its_collars()
    {
        var (w, ship, st) = Yard();
        var mods = Modules(w, ship).ToList();
        // the cockpit's other flank gets a collar too; the docked one stays
        var two = new[] { new DockPieceSpec(0, 0, 0, Dir.NegX), new DockPieceSpec(0, 0, 0, Dir.PosX) };
        Assert.True(w.CheckDesign(0, mods, two).Ok);
        Assert.Equal(Reject.None, Run(w, new DesignerApply(mods.ToArray(), two)));
        Assert.Equal(2, w.Network.DockPieces(ship).Count());
        Assert.Equal(Reject.NoDockModule, w.CheckDesign(0, mods, Array.Empty<DockPieceSpec>()).Reject);
        // a design without collars given keeps them
        Assert.Equal(Reject.None, Run(w, new DesignerApply(mods.ToArray())));
        Assert.Equal(2, w.Network.DockPieces(ship).Count());
        w.ValidateInvariants();
        Assert.True(w.Network.IsDocked(0) && st > 0);
    }

    [Fact]
    public void The_preview_equals_the_apply_for_random_blueprints_with_collars_and_a_refused_one_changes_nothing()
    {
        var rng = new Random(20261127);
        int ok = 0, refused = 0;
        var kinds = new[] { ModuleKind.Hull, ModuleKind.Reactor, ModuleKind.Cargo, ModuleKind.Defence, ModuleKind.Workshop, ModuleKind.Shield };
        for (int round = 0; round < 120; round++)
        {
            var (w, _, st) = Yard();
            Assert.Equal(Reject.None, Run(w, new Undock()));
            var mods = Modules(w, st).ToList();
            for (int i = rng.Next(0, 3); i > 0; i--)
            {
                var anchor = mods[rng.Next(mods.Count)];
                var (dx, dz) = rng.Next(4) switch { 0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1) };
                var cell = (anchor.Mx + dx, anchor.My, anchor.Mz + dz);
                if (mods.All(m => (m.Mx, m.My, m.Mz) != cell)) mods.Add(new DesignModule(cell.Item1, cell.Item2, cell.Item3, kinds[rng.Next(kinds.Length)]));
            }
            DockPieceSpec[]? pieces = null;
            if (rng.Next(3) > 0)
                pieces = Enumerable.Range(0, rng.Next(0, 4)).Select(_ => { var m = mods[rng.Next(mods.Count)]; return new DockPieceSpec(m.Mx, m.My, m.Mz, DockRules.PortFaces[rng.Next(4)]); }).ToArray();
            var report = w.CheckBlueprint(0, st, mods, null, pieces);
            var shape = Shape(w, st);
            var result = Run(w, new ApplyBlueprint(st, mods.ToArray(), null, pieces));
            Assert.Equal(report.Reject, result);
            if (result == Reject.None) ok++; else { refused++; }
            w.ValidateInvariants();
            if (result != Reject.None) Assert.Equal(shape, Shape(w, st)); // a refusal leaves the modules and the collars as they were
        }
        Assert.True(ok > 10 && refused > 10, $"ok={ok} refused={refused}");
    }

    // ---- the command log ------------------------------------------------------------------------------------------------

    [Fact]
    public void Tags_67_to_69_roundtrip_the_log_is_version_27_and_older_logs_refuse_collars()
    {
        var pieces = new[] { new DockPieceSpec(0, 0, 0, Dir.PosX), new DockPieceSpec(1, 0, 2, Dir.NegZ) };
        var mods = new[] { new DesignModule(0, 0, 0, ModuleKind.Core), new DesignModule(1, 0, 2, ModuleKind.Hull) };
        var interior = new BlueprintInterior(Array.Empty<BlueprintMachine>(), Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        var commands = new Command[]
        {
            new DesignerApply(mods, pieces), new ApplyBlueprint(3, mods, null, pieces), new ApplyBlueprint(3, mods, interior, pieces),
            new DesignerApply(mods), new ApplyBlueprint(3, mods), new ApplyBlueprint(3, mods, interior),
            new Batch(new Command[] { new ApplyBlueprint(3, mods, null, pieces) }),
        };
        foreach (var c in commands)
        {
            Assert.True(CommandCodec.IsWellFormed(c));
            using var ms = new MemoryStream();
            CommandCodec.WriteLog(ms, new List<LoggedCommand> { new LoggedCommand(5, 0, c) }, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
            var bytes = ms.ToArray();
            Assert.Equal(29, bytes[4]);
            var read = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.False(rules.LegacyDockPieces);
            Assert.Equivalent(c, read.Single().Command, strict: true);
            bool carries = c is DesignerApply { DockPieces: not null } or ApplyBlueprint { DockPieces: not null } or Batch;
            var old = (byte[])bytes.Clone();
            old[4] = 26;
            if (carries) Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out _));
            else Assert.True(CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var oldRules).Count == 1 && oldRules.LegacyDockPieces);
        }
        Assert.False(CommandCodec.IsWellFormed(new ApplyBlueprint(3, mods, null, Enumerable.Repeat(pieces[0], DockRules.MaxPieces + 1).ToArray())));
        Assert.False(CommandCodec.IsWellFormed(new DesignerApply(mods, new[] { new DockPieceSpec(0, 0, 0, Dir.PosY) })));
    }

    [Fact]
    public void A_world_with_collars_replays_clones_and_hashes_them_and_a_world_without_hashes_as_before()
    {
        var (w, _, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), null, new[] { new DockPieceSpec(0, 0, 0, Dir.PosX), new DockPieceSpec(0, 0, 0, Dir.NegZ) })));
        w.Step(60);
        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        Assert.Equal(2, w.Network.Clone().DockPieces(st).Count());
        var twin = World();
        int ts = twin.Network.CreateStructure(0, StructureKind.Station, -16L * Cell, 0, 0);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, 0, ModuleKind.Core);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, 1, ModuleKind.Shipyard);
        twin.Network.AddStructureModuleUnchecked(ts, -1, 0, 0, ModuleKind.Workshop);
        twin.Network.AddDockPieceUnchecked(ts, 0, 0, 0, Dir.PosX);
        Stock(twin, ts);
        var again = SimWorld.Replay(w.Log, w.Tick, initial: twin);
        Assert.Equal(w.Checksum(), again.Checksum());
        Assert.Equal(w.Network.DockPieces(st).ToArray(), again.Network.DockPieces(ts).ToArray());

        // a structure without collars hashes without them: the old worlds keep their checksums
        var legacy = World(rules: WorldRules.Default.WithLegacyDockPieces(true));
        var legacyShip = legacy.Network.GetStructure(legacy.Network.ShipStructure(0));
        Assert.Equal(ModuleKind.Dock, legacyShip.Modules[(-1, 0, 0)]);
        Assert.Empty(legacy.Network.DockPieces(legacyShip.Id));
    }

    [Fact]
    public void The_rules_flag_is_a_bit_of_the_third_flags_byte_and_hashes_as_default_rules()
    {
        Assert.False(WorldRules.Default.LegacyDockPieces);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyDockPieces));
        var legacy = new WorldRules().WithLegacyDockPieces(true);
        Assert.True(legacy.LegacyDockPieces && legacy.AnyLegacy && legacy.HashesAsDefault);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.False(legacy.WithoutLegacyFlags().LegacyDockPieces);
        Assert.Equal(legacy.WithLegacyInterior(true).WithLegacyDockPieces(false), new WorldRules().WithLegacyInterior(true));
        Assert.Equal(legacy.Clamped(), legacy);
        // the other legacy dock flag keeps the ADR 0029 ports and no collars
        var old = World(rules: new WorldRules().WithLegacyDock(true));
        Assert.False(old.Network.DockPieceRules);
        Assert.Empty(old.Network.DockPieces(old.Network.ShipStructure(0)));
    }

    [Fact]
    public void Logs_of_versions_22_to_26_read_with_the_flag_on_and_replay_with_dock_modules()
    {
        var recorded = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true,
            rules: TestContent.LegacyShip);
        int station = recorded.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, StationGrid.ModuleZ * (long)Cell);
        recorded.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        recorded.Network.AddStructureModuleUnchecked(station, 0, 0, -1, ModuleKind.Dock);
        recorded.Submit(0, new DockAt(station, 0, 0, -1, Dir.PosX));
        recorded.Step(60);
        Assert.True(recorded.Network.IsDocked(0));
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, TestContent.Core.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: recorded.Network.Rules);
        var bytes = ms.ToArray();
        for (byte version = 22; version <= 26; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            var log = CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyDockPieces, $"version {version}");
            Assert.False(rules.LegacyDock);
            var fresh = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: rules.WithLegacyShipAutomation(true).WithLegacyBlueprint(true).WithLegacyInterior(true));
            int fs = fresh.Network.CreateStructure(0, StructureKind.Station, -26L * Cell, 0, StationGrid.ModuleZ * (long)Cell);
            fresh.Network.AddStructureModuleUnchecked(fs, 0, 0, 0, ModuleKind.Core);
            fresh.Network.AddStructureModuleUnchecked(fs, 0, 0, -1, ModuleKind.Dock);
            var replay = SimWorld.Replay(log, recorded.Tick, initial: fresh);
            Assert.Equal(recorded.Checksum(), replay.Checksum());
            Assert.True(replay.Network.IsDocked(0));
            Assert.Equal(ModuleKind.Dock, replay.Network.GetStructure(replay.Network.ShipStructure(0)).Modules[(-1, 0, 0)]);
        }
    }

    [Fact]
    public void The_dock_texts_exist_in_both_languages()
    {
        foreach (var key in new[] { "bp.val.noDock", "bp.val.noDockStation", "toast.reject.noDockModule", "toast.reject.lastDockModule", "toast.reject.badDockPiece", "bp.tool.collar", "bp.tool.collar.hint" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
        Assert.Equal("toast.reject.badDockPiece", RejectTexts.Key(Reject.BadDockPiece));
    }

    // ---- the draft of the blueprint editor -------------------------------------------------------------------------------------

    [Fact]
    public void The_draft_carries_the_collars_cycles_them_with_the_tool_and_undoes_them()
    {
        var (w, _, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        var session = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        session.Open(st);
        var d = session.Draft;
        Assert.True(d.UsesPieces);
        Assert.DoesNotContain(ModuleKind.Dock, d.Catalogue(C));
        Assert.Equal(DesignEditResult.KindNotAllowed, d.Add((2, 0, 0), ModuleKind.Dock, C));
        Assert.False(d.Dirty);
        Assert.IsType<ApplyBlueprint>(d.ToCommand());
        Assert.Equal(Dir.PosX, d.PieceOn((0, 0, 0)));

        // each click on the core moves the collar to the next free face, then takes it off
        var seen = new List<Dir?>();
        for (int i = 0; i < 5; i++) { Assert.Equal(DesignEditResult.Ok, d.CycleCollar((0, 0, 0))); seen.Add(d.PieceOn((0, 0, 0))); }
        Assert.Equal(new Dir?[] { Dir.NegZ, null, Dir.PosX, Dir.NegZ, null }, seen); // the +Z side is the shipyard and -X the workshop: closed
        Assert.True(d.Dirty);
        var carried = ((ApplyBlueprint)d.ToCommand()).DockPieces;
        Assert.NotNull(carried);
        Assert.Empty(carried!);
        Assert.Contains(session.Validation.Entries, e => e.Code == "noDock" && e.Blocks);
        Assert.False(session.CanApply);
        Assert.True(d.Undo());
        Assert.Equal(Dir.NegZ, d.PieceOn((0, 0, 0)));

        // a module with no free host face refuses; a hull module takes one; a removed host takes its collar along
        Assert.Equal(DesignEditResult.Ok, d.Add((2, 0, 0), ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.Ok, d.CycleCollar((2, 0, 0)));
        Assert.NotNull(d.PieceOn((2, 0, 0)));
        Assert.Equal(DesignEditResult.Ok, d.Remove((2, 0, 0)));
        Assert.Null(d.PieceOn((2, 0, 0)));
        Assert.Equal(DesignEditResult.Empty, d.CycleCollar((2, 0, 0)));
        Assert.Equal(DesignEditResult.Ok, d.Add((2, 0, 1), ModuleKind.Defence, C));
        Assert.Equal(DesignEditResult.KindNotAllowed, d.CycleCollar((2, 0, 1))); // an open deck cannot host a collar
    }

    [Fact]
    public void A_moved_module_carries_its_collar_a_reverted_draft_restores_it_and_the_session_applies_it_through_the_simulation()
    {
        var (w, _, st) = Yard();
        Assert.Equal(Reject.None, Run(w, new Undock()));
        var session = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        session.Open(st);
        var d = session.Draft;
        Assert.Equal(DesignEditResult.Ok, d.Add((1, 0, 0), ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.Ok, d.CycleCollar((1, 0, 0)));
        var face = d.PieceOn((1, 0, 0))!.Value;
        Assert.True(d.MoveMany(new[] { (1, 0, 0) }, (0, 0, -1)).Any);
        Assert.Equal(face, d.PieceOn((1, 0, -1)));
        Assert.Null(d.PieceOn((1, 0, 0)));
        d.Revert();
        Assert.Equal(new[] { new DockPieceSpec(0, 0, 0, Dir.PosX) }, d.Pieces.ToArray());
        Assert.False(d.Dirty);

        Assert.Equal(DesignEditResult.Ok, d.Add((1, 0, 0), ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.Ok, d.CycleCollar((1, 0, 0)));
        Assert.True(session.Report.Ok, session.Report.Reject.ToString());
        Assert.True(session.CanApply);
        Assert.Equal(Reject.None, Run(w, session.ToCommand()));
        Assert.Equal(2, w.Network.DockPieces(st).Count());
        w.ValidateInvariants();
    }

    [Fact]
    public void The_docked_collar_cannot_leave_its_face_in_the_editor_and_the_ship_draft_keeps_a_dock()
    {
        var (w, ship, st) = Yard();
        var session = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        session.Open(st);
        Assert.Equal(DesignEditResult.Ok, session.Draft.CycleCollar((0, 0, 0)));
        Assert.Contains(session.Validation.Entries, e => e.Code == "port" && e.Blocks);
        Assert.False(session.CanApply);
        session.Open(ship);
        var d = session.Draft;
        Assert.True(d.UsesPieces);
        Assert.Equal(2, d.Pieces.Count()); // both flanks of the cockpit
        Assert.IsType<DesignerApply>(d.ToCommand());
        for (int i = 0; i < 4 && d.Pieces.Any(p => p.Face == Dir.NegX); i++) Assert.Equal(DesignEditResult.Ok, d.CycleCollar((0, 0, 0)));
        Assert.DoesNotContain(d.Pieces, p => p.Face == Dir.NegX);
        Assert.Contains(session.Validation.Entries, e => e.Code == "port" && e.Blocks); // the ship is docked with that collar
    }
}
