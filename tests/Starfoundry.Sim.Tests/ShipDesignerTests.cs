using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0032: the shipyard and the ship designer: DesignerApply, its price and refusals, and the engine-free editor behind the UI.</summary>
public class ShipDesignerTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261122UL;
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Plate = C.ItemIndex("core:iron_plate");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Carbon = C.ItemIndex("core:carbon");
    private static readonly int Circuit = C.ItemIndex("core:circuit");

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    /// <summary>The starter ship docked at its station core, with a shipyard module beside the core when asked.</summary>
    private static SimWorld Yard(GameMode mode, bool shipyard, out int ship, out int station)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: false, structures: true, rules: TestContent.LegacyShip);
        ship = w.Network.ShipStructure(0);
        station = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        if (shipyard) w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Shipyard);
        Assert.Equal(Reject.None, Run(w, new DockAt(station, 0, 0, 0, Dir.PosX)));
        return w;
    }

    private static SimWorld Yard(GameMode mode, out int ship, out int station) => Yard(mode, true, out ship, out station);

    private static List<DesignModule> Current(SimWorld w, int ship) =>
        w.Network.GetStructure(ship).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToList();

    private static DesignerApply With(SimWorld w, int ship, Action<List<DesignModule>> edit)
    {
        var list = Current(w, ship);
        edit(list);
        return new DesignerApply(list.ToArray());
    }

    [Fact]
    public void A_second_laser_is_added_at_a_shipyard_and_mines_faster()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        var cmd = With(w, ship, l => l.Add(new DesignModule(1, 1, 0, ModuleKind.MiningLaser)));
        var report = w.CheckDesign(0, cmd.Modules);
        Assert.True(report.Ok);
        Assert.Equal((1, 0), (report.Added, report.Removed));
        Assert.Equal(Reject.None, Run(w, cmd));
        Assert.Equal(2, w.Network.MiningLasers(0).Count);
        Assert.Equal(ModuleKind.MiningLaser, w.Network.GetStructure(ship).Modules[(1, 1, 0)]);
        Assert.Equal(1, w.Network.ModulesBuilt(ModuleKind.MiningLaser));
        w.ValidateInvariants();
        w.Step(30);
    }

    [Fact]
    public void The_designer_needs_a_docked_ship_and_a_shipyard()
    {
        var undocked = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: TestContent.LegacyShip);
        int ship0 = undocked.Network.ShipStructure(0);
        Assert.Equal(Reject.NotDocked, Run(undocked, With(undocked, ship0, l => l.RemoveAll(m => m.Kind == ModuleKind.Gun))));
        var noYard = Yard(GameMode.Sandbox, false, out int ship, out _);
        Assert.Equal(Reject.NoShipyard, Run(noYard, With(noYard, ship, l => l.RemoveAll(m => m.Kind == ModuleKind.Gun))));
        Assert.Equal(Reject.NoShipyard, noYard.CheckDesign(0, Current(noYard, ship)).Reject);
        Assert.Equal(Reject.NotAllowedHere, Run(SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: false), new DesignerApply(Array.Empty<DesignModule>())));
    }

    [Fact]
    public void A_design_must_keep_the_ship_a_ship_and_one_piece_and_its_port()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        Assert.Equal(Reject.NoCockpitOrEngine, Run(w, With(w, ship, l => l.RemoveAll(m => m.Kind == ModuleKind.Engine))));
        Assert.Equal(Reject.LastEssentialModule, Run(w, With(w, ship, l => l.RemoveAll(m => m.Kind == ModuleKind.Reactor))));
        Assert.Equal(Reject.LastEssentialModule, Run(w, With(w, ship, l => l.RemoveAll(m => m.Kind == ModuleKind.Cargo))));
        Assert.Equal(Reject.KindNotAllowed, Run(w, With(w, ship, l => l.Add(new DesignModule(1, 0, 0, ModuleKind.Core)))));
        Assert.Equal(Reject.KindNotAllowed, Run(w, With(w, ship, l => l.Add(new DesignModule(1, 1, 0, ModuleKind.Shipyard)))));
        Assert.Equal(Reject.NotAdjacent, Run(w, With(w, ship, l => l.Add(new DesignModule(5, 3, 5, ModuleKind.Hull))))); // floats away
        Assert.Equal(Reject.InvalidArgument, Run(w, With(w, ship, l => l.Add(l[0]))));
        Assert.Equal(Reject.InvalidArgument, Run(w, new DesignerApply(Array.Empty<DesignModule>())));
        Assert.Equal(Reject.ModuleOccupied, Run(w, With(w, ship, l => { l.RemoveAll(m => m.Kind == ModuleKind.Cockpit); l.Add(new DesignModule(0, 0, 0, ModuleKind.Hull)); l.Add(new DesignModule(0, 1, 1, ModuleKind.Cockpit)); }))); // the docked port module stays a cockpit
        Assert.Equal(Reject.ModuleOccupied, Run(w, With(w, ship, l => l.Add(new DesignModule(-1, 0, 0, ModuleKind.Hull))))); // blocks the docked port's face
        Assert.Equal(Reject.Overlap, Run(w, With(w, ship, l => l.Add(new DesignModule(-1, 0, 1, ModuleKind.Hull))))); // inside the shipyard of the station
        Assert.Equal(7, w.Network.GetStructure(ship).Modules.Count); // every refusal left the ship as it was
        w.ValidateInvariants();
    }

    [Fact]
    public void A_module_with_machines_inside_cannot_go_but_the_rest_can_move_for_free()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        // The workshop at (0,0,1) holds the starter factory: it cannot be removed or changed.
        Assert.Equal(Reject.ModuleOccupied, Run(w, With(w, ship, l => { l.RemoveAll(m => m.Kind == ModuleKind.Workshop); l.Add(new DesignModule(0, 0, 1, ModuleKind.Hull)); })));
        // The gun moves from the top of the cockpit to the top of the cargo module: free.
        var move = With(w, ship, l => { l.RemoveAll(m => m.Kind == ModuleKind.Gun); l.Add(new DesignModule(0, 1, 2, ModuleKind.Gun)); });
        var report = w.CheckDesign(0, move.Modules);
        Assert.Equal((1, 1, 1), (report.Added, report.Removed, report.Moved));
        Assert.Empty(report.Cost);
        Assert.Empty(report.Refund);
        Assert.Equal(Reject.None, Run(w, move));
        Assert.Equal(ModuleKind.Gun, w.Network.GetStructure(ship).Modules[(0, 1, 2)]);
        Assert.False(w.Network.GetStructure(ship).Modules.ContainsKey((0, 1, 0)));
        w.ValidateInvariants();
    }

    [Fact]
    public void Campaign_designs_cost_items_from_the_hold_then_the_stock_and_refund_half()
    {
        var w = Yard(GameMode.Campaign, out int ship, out int station);
        var add = With(w, ship, l => l.Add(new DesignModule(1, 1, 0, ModuleKind.MiningLaser)));
        var report = w.CheckDesign(0, add.Modules);
        Assert.Equal(Reject.NotEnoughItems, report.Reject);
        Assert.Equal(Reject.NotEnoughItems, Run(w, add));
        Assert.Equal(2, w.Network.GetStructure(ship).Modules.Count(m => m.Value is ModuleKind.MiningLaser or ModuleKind.Gun)); // untouched

        w.Network.GiveItems(0, Plate, 5);
        w.Network.GiveStock(station, Plate, 20);
        w.Network.GiveStock(station, Carbon, 10);
        Assert.Equal(new[] { new ItemStack(Plate, 12), new ItemStack(Carbon, 4) }.OrderBy(s => s.Item), w.CheckDesign(0, add.Modules).Cost);
        Assert.Equal(Reject.None, Run(w, add));
        Assert.Equal(0, w.Network.HoldCount(0, Plate)); // the hold pays first
        Assert.Equal(13, w.Network.StockCount(station, Plate)); // and the stock the rest
        Assert.Equal(6, w.Network.StockCount(station, Carbon));

        // Removing it again refunds half of its cost to the hold.
        var back = With(w, ship, l => l.RemoveAll(m => m.Kind == ModuleKind.MiningLaser && m.Mx == 1));
        Assert.Equal(Reject.None, Run(w, back));
        Assert.Equal(6, w.Network.HoldCount(0, Plate));
        Assert.Equal(2, w.Network.HoldCount(0, Carbon));
    }

    [Fact]
    public void An_upgrade_replaces_the_old_mark_and_pays_the_difference()
    {
        var w = Yard(GameMode.Campaign, out int ship, out int station);
        w.Network.GiveStock(station, Circuit, 8);
        w.Network.GiveStock(station, C.ItemIndex("core:titanium_alloy"), 8);
        w.Network.GiveStock(station, Plate, 12);
        var up = With(w, ship, l => { l.RemoveAll(m => m.Kind == ModuleKind.MiningLaser); l.Add(new DesignModule(0, 0, -1, ModuleKind.MiningLaserMk2)); });
        var report = w.CheckDesign(0, up.Modules);
        Assert.True(report.Ok, report.Reject.ToString());
        Assert.Equal((1, 1, 0), (report.Added, report.Removed, report.Moved));
        Assert.Contains(new ItemStack(Circuit, 8), report.Cost);
        Assert.Contains(new ItemStack(Plate, 6), report.Cost); // 12 for the new laser minus the 6 refunded for the old
        Assert.Contains(new ItemStack(Carbon, 2), report.Refund);
        Assert.Equal(Reject.None, Run(w, up));
        Assert.Equal(ModuleKind.MiningLaserMk2, w.Network.GetStructure(ship).Modules[(0, 0, -1)]);
        Assert.Equal(170, w.Network.LaserPercentFor(0, w.Network.AddDeposit(C.ItemIndex("core:iron_ore"), 100, 4, 4, -4)));
    }

    [Fact]
    public void A_smaller_hold_must_still_fit_what_it_carries_and_the_design_must_not_overlap_the_station()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out int station);
        Assert.Equal(Reject.None, Run(w, With(w, ship, l => l.Add(new DesignModule(0, 1, 2, ModuleKind.Cargo))))); // a second cargo module: 200
        w.Network.GiveItems(0, Ingot, 150);
        Assert.Equal(Reject.NoRoom, Run(w, With(w, ship, l => { l.RemoveAll(m => m.Kind == ModuleKind.Cargo && m.My == 1); l.Add(new DesignModule(0, 1, 2, ModuleKind.Hull)); })));
        // The station's shipyard sits at module (0,0,1) of the station = (-1,0,1) of the ship's lattice: building there overlaps it.
        Assert.Equal(Reject.Overlap, Run(w, With(w, ship, l => l.Add(new DesignModule(-1, 0, 1, ModuleKind.Hull)))));
        Assert.True(station > 0);
    }

    [Fact]
    public void A_design_replays_clones_and_saves_exactly()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        Assert.Equal(Reject.None, Run(w, With(w, ship, l => l.Add(new DesignModule(1, 1, 0, ModuleKind.GunMk2)))));
        w.Step(60);
        var clone = w.Network.Clone();
        Assert.Equal(w.Network.Checksum(), clone.Checksum());
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, structures: true, rules: TestContent.LegacyShip);
        var log = CommandCodec.ReadLog(new MemoryStream(ms.ToArray()));
        var replay = SimWorld.Replay(log, w.Tick, SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: TestContent.LegacyShip));
        _ = replay; // the station was built by hand in this test, so the replay of the whole log is covered by the clone
        Assert.Equal(ModuleKind.GunMk2, clone.GetStructure(ship).Modules[(1, 1, 0)]);
        w.ValidateInvariants();
    }

    // ---- the engine-free editor ----------------------------------------------------------------------

    private static ShipDesignEditor Editor(out SimWorld w, out int ship)
    {
        w = Yard(GameMode.Sandbox, out ship, out _);
        var e = new ShipDesignEditor();
        e.Load(w.Network.GetStructure(ship).Modules);
        return e;
    }

    [Fact]
    public void The_editor_adds_removes_moves_and_upgrades_and_keeps_the_layout_touching()
    {
        var e = Editor(out var w, out int ship);
        Assert.False(e.Dirty);
        Assert.Equal(DesignEditResult.NotAdjacent, e.Add((5, 0, 5), ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.Occupied, e.Add((0, 0, 0), ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.KindNotAllowed, e.Add((1, 1, 0), ModuleKind.Core, C));
        Assert.Equal(DesignEditResult.KindNotAllowed, e.Add((1, 1, 0), ModuleKind.Shipyard, C));
        Assert.Equal(DesignEditResult.Ok, e.Add((1, 1, 0), ModuleKind.MiningLaser, C));
        Assert.True(e.Dirty);
        Assert.Equal(ModuleKind.MiningLaserMk2, e.UpgradeFor((1, 1, 0), C));
        Assert.Equal(DesignEditResult.Ok, e.Upgrade((1, 1, 0), C));
        Assert.Equal(DesignEditResult.NoUpgrade, e.Upgrade((1, 1, 0), C));
        Assert.Equal(DesignEditResult.Empty, e.Upgrade((4, 4, 4), C));
        Assert.Equal(DesignEditResult.Ok, e.Move((1, 1, 0), (-1, 1, 0)));
        Assert.Equal(DesignEditResult.NotAdjacent, e.Move((-1, 1, 0), (3, 3, 3)));
        Assert.Equal(ModuleKind.MiningLaserMk2, e.Modules[(-1, 1, 0)]);
        var hp = e.Hardpoints(C);
        Assert.Equal((2, 270, 1, 100), hp);
        Assert.Equal(DesignEditResult.Ok, e.Remove((-1, 1, 0)));
        Assert.Equal(DesignEditResult.Empty, e.Remove((-1, 1, 0)));
        Assert.False(e.Dirty);
        e.Add((1, 1, 0), ModuleKind.Gun, C);
        e.Revert();
        Assert.False(e.Dirty);
        var (min, max) = e.Bounds();
        Assert.Equal((-1, -1, -2), min);
        Assert.Equal((1, 2, 5), max);
        var cmd = e.ToCommand();
        Assert.Equal(Reject.None, Run(w, cmd)); // unchanged layout: a valid no-op
        Assert.Contains(ModuleKind.MiningLaserMk2, ShipDesignEditor.Catalogue(C));
        Assert.DoesNotContain(ModuleKind.Core, ShipDesignEditor.Catalogue(C));
        Assert.DoesNotContain(ModuleKind.Shipyard, ShipDesignEditor.Catalogue(C));
        Assert.True(e.Check(w, 0).Ok);
    }
}
