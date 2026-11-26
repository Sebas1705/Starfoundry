using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The ship moves and the station is fixed; a ship docks at a station port, is locked to its pose, joins it and exchanges items with its stock
/// (ADR 0029, docs/prototypes/docking.md).
/// </summary>
public class DockingTests
{
    private const int Cell = Units.CellUnits;
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;

    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");

    private static SimWorld NewWorld(ContentDb? content = null, bool threats = false, WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, Seed, threats: threats, stationRules: false, structures: true, rules: rules ?? TestContent.OldDock); // these tests pin the ADR 0029 geometry (core and cockpit ports); ADR 0041 is in DockModuleTests

    /// <summary>The starter ship at the origin and a core-only station whose +X port is <paramref name="gapCells"/> cells from the ship's -X port.</summary>
    private static SimWorld Dockable(out int ship, out int station, int gapCells = 10, ContentDb? content = null)
    {
        var w = NewWorld(content, threats: true);
        ship = w.Network.ShipStructure(0);
        station = w.Network.CreateStructure(0, StructureKind.Station, (-8 - gapCells) * (long)Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        return w;
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    private static void Ok(SimWorld w, Command c) => Assert.Equal(Reject.None, Run(w, c));

    private static DockAt DockWest(int station) => new(station, 0, 0, 0, Dir.PosX);

    // ---- ports ----------------------------------------------------------------------------------------

    [Fact]
    public void Ports_are_the_free_horizontal_faces_of_core_dock_and_cockpit_modules()
    {
        var w = Dockable(out int ship, out int station);
        var stationPorts = w.Network.DockPorts(station).ToList();
        Assert.Equal(4, stationPorts.Count);
        Assert.All(stationPorts, p => Assert.True(p.Free));

        // The starter ship's only port module is the cockpit, which docks on its two flanks (its nose and its back are no ports).
        var shipPorts = w.Network.DockPorts(ship).Select(p => p.Face).ToList();
        Assert.Equal(new[] { Dir.PosX, Dir.NegX }, shipPorts);

        // Building against a face closes its port.
        Ok(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Workshop));
        Assert.DoesNotContain(w.Network.DockPorts(station), p => p.Face == Dir.PosZ && p.Mz == 0);
        // A workshop has no ports; a dock module has.
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 2, ModuleKind.Dock);
        Assert.Equal(3, w.Network.DockPorts(station).Count(p => p.Mz == 2));
        Assert.True(BeltNetwork.HasPorts(StructureKind.Ship, ModuleKind.Dock));
        Assert.False(BeltNetwork.HasPorts(StructureKind.Ship, ModuleKind.Core));
        Assert.False(BeltNetwork.HasPorts(StructureKind.Station, ModuleKind.Cockpit));
        Assert.False(BeltNetwork.HasPorts(StructureKind.Station, ModuleKind.Workshop));
        w.ValidateInvariants();
    }

    [Fact]
    public void Port_geometry_is_the_middle_of_the_module_face()
    {
        var w = Dockable(out int ship, out int station);
        var (x, y, z) = w.Network.PortWorldPoint(station, 0, 0, 0, Dir.PosX);
        long o = (-18) * (long)Cell;
        Assert.Equal(o + 8 * Cell, x);
        Assert.Equal(2 * Cell, y);
        Assert.Equal(4 * Cell, z);
        var (sx, _, sz) = w.Network.PortWorldPoint(ship, 0, 0, 0, Dir.NegX);
        Assert.Equal(0, sx);
        Assert.Equal(4 * Cell, sz);
    }

    [Fact]
    public void A_station_is_fixed_it_takes_no_cockpit_or_engine_and_a_ship_takes_no_core()
    {
        var w = Dockable(out int ship, out int station);
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Engine)));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Cockpit)));
        Ok(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Reactor));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(ship, 1, 0, 0, ModuleKind.Core)));
        // Nothing moves a station: it has no player and no thrust, and a station's origin is the one it was founded at.
        var o = w.Network.GetStructure(station);
        var before = (o.OriginX, o.OriginY, o.OriginZ);
        Ok(w, new SetThrust(1024, 0, 0));
        w.Step(Tps * 2);
        Assert.Equal(before, (o.OriginX, o.OriginY, o.OriginZ));
        Assert.NotEqual(0, w.Network.GetStructure(ship).OriginX);
    }

    // ---- docking --------------------------------------------------------------------------------------

    [Fact]
    public void Docking_locks_the_ship_to_the_port_and_keeps_its_id()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, DockWest(station));

        var net = w.Network;
        Assert.True(net.IsDocked(0));
        Assert.Equal(ship, net.ShipStructure(0));
        Assert.True(net.TryGetDock(0, out var dock));
        Assert.Equal(station, dock.Station);
        Assert.Equal(Dir.NegX, dock.ShipFace);
        Assert.Equal(Dir.PosX, dock.StationFace);

        // The ship's cockpit sits on the module cell beyond the station's port: one module along +X of the station.
        var s = net.GetStructure(station);
        var sh = net.GetStructure(ship);
        Assert.Equal(s.OriginX + 8 * Cell, sh.OriginX);
        Assert.Equal(s.OriginY, sh.OriginY);
        Assert.Equal(s.OriginZ, sh.OriginZ);
        // The pilot goes with the ship, and the two ports meet.
        var info = net.GetPlayerInfo(0);
        Assert.Equal((int)sh.OriginX, info.X);
        Assert.Equal(net.PortWorldPoint(station, 0, 0, 0, Dir.PosX), net.PortWorldPoint(ship, 0, 0, 0, Dir.NegX));
        Assert.Equal(new[] { ship }, net.DockedShips(station).ToArray());
        w.ValidateInvariants();
    }

    [Fact]
    public void A_docked_ship_cannot_thrust_or_jump_and_stays_put()
    {
        var w = Dockable(out int ship, out _, gapCells: 10);
        Ok(w, DockWest(w.Network.Structures().First(s => s.Kind == StructureKind.Station).Id));
        var origin = (w.Network.GetStructure(ship).OriginX, w.Network.GetStructure(ship).OriginY, w.Network.GetStructure(ship).OriginZ);

        Assert.Equal(Reject.Docked, Run(w, new SetThrust(0, 0, 1024)));
        Assert.Equal(Reject.Docked, Run(w, new SetMovement(1, 0, 0)));
        Assert.Equal(Reject.Docked, Run(w, new Jump(1)));
        Ok(w, new SetThrust(0, 0, 0)); // stopping is always allowed
        Ok(w, new SetMovement(0, 0, 0));
        Assert.False(w.Network.CanFly(0));
        Assert.Equal(0, w.Network.ShipSpeedUnitsPerTick(0));

        w.Step(Tps * 3);
        var sh = w.Network.GetStructure(ship);
        Assert.Equal(origin, (sh.OriginX, sh.OriginY, sh.OriginZ));
        w.ValidateInvariants();
    }

    [Fact]
    public void Even_a_stale_thrust_is_ignored_while_docked()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, new SetThrust(0, 0, 100)); // slow enough to dock
        Ok(w, DockWest(station)); // docking clears the intent
        var o = w.Network.GetStructure(ship).OriginX;
        w.Step(Tps);
        Assert.Equal(o, w.Network.GetStructure(ship).OriginX);
        Assert.Equal(0, w.Network.ShipSpeedMilli(0));
    }

    [Fact]
    public void Undocking_gives_the_thrust_back_and_keeps_the_pose()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, DockWest(station));
        var docked = w.Network.GetStructure(ship).OriginX;
        Ok(w, new Undock());
        Assert.False(w.Network.IsDocked(0));
        Assert.Equal(docked, w.Network.GetStructure(ship).OriginX);
        Assert.True(w.Network.CanFly(0));

        Ok(w, new SetThrust(0, 0, 1024));
        w.Step(Tps);
        Assert.NotEqual(0, w.Network.GetStructure(ship).OriginZ);
        Assert.Equal(Reject.NotDocked, Run(w, new Undock()));
        w.ValidateInvariants();
    }

    [Fact]
    public void Docking_needs_range_speed_and_a_free_port()
    {
        var w = Dockable(out _, out int station, gapCells: 40);
        Assert.Equal(Reject.OutOfRange, Run(w, DockWest(station)));

        var near = Dockable(out _, out int st, gapCells: DockRules.RangeCells + 1);
        Assert.Equal(Reject.OutOfRange, Run(near, DockWest(st)));
        var edge = Dockable(out _, out int st2, gapCells: DockRules.RangeCells);
        Ok(edge, DockWest(st2)); // exactly at range counts

        // Too fast: full thrust is 48 units a tick, the limit is a quarter of that.
        var fast = Dockable(out _, out int st3);
        Ok(fast, new SetThrust(0, 0, 1024));
        fast.Network.SetStructureOrigin(fast.Network.ShipStructure(0), 0, 0, 0);
        Assert.Equal(Reject.TooFast, Run(fast, DockWest(st3)));
        Ok(fast, new SetThrust(0, 0, 200)); // about 9 units a tick: allowed
        fast.Network.SetStructureOrigin(fast.Network.ShipStructure(0), 0, 0, 0);
        Ok(fast, DockWest(st3));

        // No free port on the face the ship would need.
        var blocked = Dockable(out _, out int st4);
        Ok(blocked, new PlaceModuleIn(st4, 1, 0, 0, ModuleKind.Hull));
        Assert.Equal(Reject.NoFreePort, Run(blocked, DockWest(st4)));
        // A face that is not horizontal, or a module with no port.
        Assert.Equal(Reject.NotAllowedHere, Run(blocked, new DockAt(st4, 0, 0, 0, Dir.PosY)));
        Assert.Equal(Reject.NoFreePort, Run(blocked, new DockAt(st4, 1, 0, 0, Dir.PosX)));
    }

    [Fact]
    public void Docking_rejects_bad_targets_and_a_second_dock()
    {
        var w = Dockable(out int ship, out int station);
        Assert.Equal(Reject.NoSuchStructure, Run(w, new DockAt(99, 0, 0, 0, Dir.PosX)));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new DockAt(ship, 0, 0, 0, Dir.PosX))); // a ship is no station
        int foreign = w.Network.CreateStructure(7, StructureKind.Station, -18 * (long)Cell, 400 * (long)Cell, 0);
        w.Network.AddStructureModuleUnchecked(foreign, 0, 0, 0, ModuleKind.Core);
        Assert.Equal(Reject.NotAllowedHere, Run(w, DockWest(foreign)));

        Ok(w, DockWest(station));
        Assert.Equal(Reject.AlreadyDocked, Run(w, DockWest(station)));
    }

    [Fact]
    public void Docking_refuses_when_the_ship_would_overlap_the_station()
    {
        // A module of the station where the ship's modules would land: the ship runs along +Z from the dock cell.
        var w2 = Dockable(out _, out int st2);
        Ok(w2, new PlaceModuleIn(st2, 0, 0, 1, ModuleKind.Hull));
        Ok(w2, new PlaceModuleIn(st2, 1, 0, 1, ModuleKind.Hull)); // the ship's workshop cell
        Assert.Equal(Reject.Overlap, Run(w2, DockWest(st2)));
    }

    [Fact]
    public void Docking_is_refused_without_structure_rules_and_without_a_ship()
    {
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed);
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new DockAt(1, 0, 0, 0, Dir.PosX)));
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new Undock()));
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new TransferItems(0, 1, true)));
        Assert.False(legacy.Network.IsDocked(0));
    }

    [Fact]
    public void A_nearby_port_is_offered_with_its_range_and_speed()
    {
        var w = Dockable(out _, out int station, gapCells: 30);
        Assert.False(w.Network.TryFindDockCandidate(0, 20, out _));
        Assert.True(w.Network.TryFindDockCandidate(0, 40, out var c));
        Assert.Equal(station, c.Station);
        Assert.Equal(Dir.PosX, c.Face);
        Assert.Equal(30 * Cell, c.DistanceUnits);
        Assert.False(c.InRange);
        Assert.True(c.SlowEnough);

        Ok(w, new SetThrust(0, 0, 1024));
        w.Network.SetStructureOrigin(w.Network.ShipStructure(0), 0, 0, 0);
        Assert.True(w.Network.TryFindDockCandidate(0, 40, out c));
        Assert.False(c.SlowEnough);
        Assert.Equal(48000, c.SpeedMilli);
    }

    [Fact]
    public void Speed_follows_the_thrust_vector_and_the_axis_intent()
    {
        var w = NewWorld();
        Assert.Equal(0, w.Network.ShipSpeedMilli(0));
        Ok(w, new SetThrust(512, 0, 0));
        Assert.Equal(24000, w.Network.ShipSpeedMilli(0));
        Ok(w, new SetThrust(0, 0, 0));
        Ok(w, new SetMovement(1, 0, 0));
        Assert.Equal(48000, w.Network.ShipSpeedMilli(0));
        Ok(w, new SetMovement(1, 1, 0));
        Assert.Equal(34000, w.Network.ShipSpeedMilli(0));
    }

    // ---- the ship's interior meets the station's ------------------------------------------------------

    [Fact]
    public void Docked_ports_share_one_world_point_and_a_second_ship_port_stays_free()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, DockWest(station));
        // The station's port is taken now; the ship's other ports (the cockpit's +X and -Z) stay free.
        Assert.Contains(w.Network.DockPorts(station), p => p.Face == Dir.PosX && !p.Free && p.DockedShip == ship);
        Assert.Equal(3, w.Network.DockPorts(station).Count(p => p.Free));
        // Nobody else can use the taken port.
        Assert.False(w.Network.TryDockCandidate(0, station, 0, 0, 0, Dir.PosX, out _));
    }

    [Fact]
    public void A_dock_module_gives_a_ship_more_ports()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, new PlaceModuleIn(ship, 1, 0, 0, ModuleKind.Dock)); // beside the cockpit, on its +X side
        Assert.Equal(new[] { Dir.PosX, Dir.PosZ, Dir.NegZ }, w.Network.DockPorts(ship).Where(p => p.Mx == 1).Select(p => p.Face).ToArray()); // a dock module opens on every free horizontal face
        // Docking with the dock module: the cockpit's -X face is now the nearest free -X port, but the dock module is on the far side.
        Ok(w, DockWest(station));
        Assert.True(w.Network.TryGetDock(0, out var d));
        Assert.Equal((0, 0, 0), (d.ShipMx, d.ShipMy, d.ShipMz));
    }

    // ---- the station stock ----------------------------------------------------------------------------

    [Fact]
    public void Transfers_move_items_between_the_hold_and_the_stock()
    {
        var w = Dockable(out _, out int station);
        Assert.Equal(Reject.NotDocked, Run(w, new TransferItems(Ore, 1, true)));
        Ok(w, DockWest(station));
        w.Network.GiveItems(0, Ingot, 30);
        int hold = w.Network.HoldTotal(0);

        Ok(w, new TransferItems(Ingot, 12, true));
        Assert.Equal(12, w.Network.StockCount(station, Ingot));
        Assert.Equal(18, w.Network.HoldCount(0, Ingot));
        Assert.Equal(hold - 12, w.Network.HoldTotal(0));
        Assert.Equal(12, w.Network.StockTotal(station));

        Ok(w, new TransferItems(Ingot, 0, true)); // zero moves everything
        Assert.Equal(30, w.Network.StockCount(station, Ingot));
        Assert.Equal(0, w.Network.HoldCount(0, Ingot));

        Ok(w, new TransferItems(Ingot, 5, false));
        Assert.Equal(25, w.Network.StockCount(station, Ingot));
        Assert.Equal(5, w.Network.HoldCount(0, Ingot));
        Ok(w, new TransferItems(Ingot, 999, false)); // more than there is: all of it
        Assert.Equal(0, w.Network.StockCount(station, Ingot));
        Assert.Equal(30, w.Network.HoldCount(0, Ingot));

        Assert.Equal(Reject.NotEnoughItems, Run(w, new TransferItems(Ingot, 1, false)));
        Assert.Equal(Reject.NotEnoughItems, Run(w, new TransferItems(Ore, 1, true)));
        Assert.Equal(Reject.InvalidArgument, Run(w, new TransferItems(9999, 1, true)));
        w.ValidateInvariants(); // the hold accounting still balances
    }

    [Fact]
    public void The_stock_has_a_capacity_that_grows_with_cargo_and_dock_modules()
    {
        var w = Dockable(out _, out int station);
        Assert.Equal(DockRules.CoreStockCapacity, w.Network.StockCapacity(station));
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Dock);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 2, ModuleKind.Cargo);
        Assert.Equal(DockRules.CoreStockCapacity + DockRules.DockStockCapacity + C.Structures!.HoldCapacity(ModuleKind.Cargo), w.Network.StockCapacity(station));

        var tight = Dockable(out _, out int st);
        Ok(tight, DockWest(st));
        tight.Network.GiveItems(0, Ingot, 70);
        Ok(tight, new TransferItems(Ingot, 0, true)); // the core holds 60: the rest stays
        Assert.Equal(60, tight.Network.StockCount(st, Ingot));
        Assert.Equal(10, tight.Network.HoldCount(0, Ingot));
        Assert.Equal(Reject.NoRoom, Run(tight, new TransferItems(Ingot, 5, true)));
    }

    [Fact]
    public void Loading_is_limited_by_the_free_hold_space()
    {
        var w = Dockable(out _, out int station);
        Ok(w, DockWest(station));
        w.Network.GiveStock(station, Ingot, 50);
        int free = w.Network.HoldCapacity(0) - w.Network.HoldTotal(0);
        w.Network.GiveItems(0, C.ItemIndex("core:quartz"), free - 3); // three slots left (quartz: nothing in the starter factory uses it)
        Ok(w, new TransferItems(Ingot, 0, false));
        Assert.Equal(3, w.Network.HoldCount(0, Ingot));
        Assert.Equal(47, w.Network.StockCount(station, Ingot));
        Assert.Equal(Reject.NoRoom, Run(w, new TransferItems(Ingot, 1, false)));
        w.ValidateInvariants();
    }

    // ---- refuel, power, repairs, defence -----------------------------------------------------------------

    private static int HydrogenAfterOneStep(bool docked, int stock)
    {
        var w = Dockable(out _, out int station);
        if (stock > 0) w.Network.GiveStock(station, Hydrogen, stock);
        if (docked) Ok(w, DockWest(station)); else w.Step();
        return w.Network.HoldCount(0, Hydrogen);
    }

    [Fact]
    public void A_docked_reactor_burns_the_station_stock_before_the_hold()
    {
        int start = C.StarterShip!.Hold.Single(h => h.Item == Hydrogen).Count;
        Assert.Equal(start - 1, HydrogenAfterOneStep(docked: false, stock: 5)); // free: the hold pays
        Assert.Equal(start, HydrogenAfterOneStep(docked: true, stock: 5)); // docked: the stock pays
        Assert.Equal(start - 1, HydrogenAfterOneStep(docked: true, stock: 0)); // an empty stock falls back to the hold

        var w = Dockable(out _, out int station);
        w.Network.GiveStock(station, Hydrogen, 2);
        Ok(w, DockWest(station));
        w.Step(BurnTicks() * 3);
        Assert.Equal(0, w.Network.StockCount(station, Hydrogen));
        Assert.True(w.Network.HoldCount(0, Hydrogen) < start);
        w.ValidateInvariants();
    }

    private static int BurnTicks() => C.Structures!.BurnIntervalTicks(ModuleKind.Reactor);

    [Fact]
    public void A_station_reactor_burns_its_own_stock()
    {
        var w = NewWorld();
        int station = w.Network.CreateStructure(0, StructureKind.Station, -100 * (long)Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Reactor);
        w.Network.GiveStock(station, Hydrogen, 1);
        int hold = w.Network.HoldCount(0, Hydrogen);
        w.Step(1);
        Assert.Equal(0, w.Network.StockCount(station, Hydrogen));
        Assert.Equal(hold - 1, w.Network.HoldCount(0, Hydrogen)); // only the ship's own reactor burned from the hold
        Assert.True(w.Network.PowerStatus(station).Burning);
    }

    [Fact]
    public void A_docked_ship_is_repaired_several_times_as_fast()
    {
        int Healed(bool docked)
        {
            var w = Dockable(out int ship, out int station);
            w.Network.GiveStock(station, Hydrogen, 20);
            w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Reactor); // the station is powered
            w.Network.GiveStock(station, Hydrogen, 20);
            if (docked) Ok(w, DockWest(station));
            w.Network.HitModule(ship, 0, 0, 2, 100); // the cargo module
            int before = w.Network.ModuleHp(ship, 0, 0, 2);
            int ticks = (C.Threat?.ModuleRegenTicks ?? ThreatRules.DefaultModuleRegenTicks) * 6;
            w.Step(ticks);
            return w.Network.ModuleHp(ship, 0, 0, 2) - before;
        }
        int free = Healed(docked: false), docked2 = Healed(docked: true);
        Assert.True(free > 0);
        Assert.True(docked2 >= free * 2, $"docked {docked2} vs free {free}");
    }

    [Fact]
    public void The_stations_shields_cover_a_docked_ship()
    {
        int Damage(bool docked)
        {
            var w = Dockable(out int ship, out int station);
            w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Reactor);
            w.Network.AddStructureModuleUnchecked(station, 0, 0, 2, ModuleKind.Shield);
            w.Network.GiveStock(station, Hydrogen, 5);
            if (docked) Ok(w, DockWest(station));
            else w.Step();
            Assert.True(w.Network.PowerStatus(station).SatisfactionPermille >= ShieldSensorRules.MinSatisfaction);
            int max = w.Network.ModuleMaxHp(ModuleKind.Cargo);
            w.Network.HitModule(ship, 0, 0, 2, 100);
            return max - w.Network.ModuleHp(ship, 0, 0, 2);
        }
        Assert.Equal(100, Damage(docked: false));
        Assert.True(Damage(docked: true) < 100);
    }

    [Fact]
    public void The_stations_defence_modules_strengthen_the_turrets_of_a_docked_ship()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Defence));
        Assert.Equal(0, w.Network.DefenceBonusPercent(ship));
        Ok(w, DockWest(station));
        Assert.Equal(w.Network.DefenceBonusPercent(station), w.Network.DefenceBonusPercent(ship));
        Assert.True(w.Network.DefenceBonusPercent(ship) > 0);
    }

    // ---- losing the station or the port -------------------------------------------------------------------

    [Fact]
    public void Destroying_the_station_core_frees_the_ship()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, DockWest(station));
        w.Network.HitModule(station, 0, 0, 0, 100_000);
        Assert.False(w.Network.StructureExists(station));
        Assert.False(w.Network.IsDocked(0));
        Assert.True(w.Network.CanFly(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void Destroying_the_station_port_module_frees_the_ship_and_players_cannot_remove_a_port_in_use()
    {
        var w = Dockable(out int ship, out int station, gapCells: 6);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Dock);
        Ok(w, new DockAt(station, 0, 0, 1, Dir.PosX));
        Assert.True(w.Network.IsDocked(0));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModuleIn(station, 0, 0, 1)));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModuleIn(ship, 0, 0, 0)));

        w.Network.HitModule(station, 0, 0, 1, 100_000); // the dock module falls
        Assert.False(w.Network.IsDocked(0));
        Assert.True(w.Network.StructureExists(station));
        w.ValidateInvariants();
    }

    [Fact]
    public void Nothing_can_be_built_where_a_docked_ship_sits()
    {
        var w = Dockable(out int ship, out int station);
        Ok(w, DockWest(station));
        Assert.Equal(Reject.Overlap, Run(w, new PlaceModuleIn(station, 1, 0, 0, ModuleKind.Hull)));
        Assert.Equal(Reject.Overlap, Run(w, new PlaceModuleIn(station, 1, 0, 2, ModuleKind.Hull)));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModuleIn(ship, -2, 1, 0, ModuleKind.Hull)));
        Ok(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Hull)); // the station can still grow away from the ship
        w.ValidateInvariants();
    }

    // ---- determinism and compatibility ---------------------------------------------------------------------

    [Fact]
    public void Docking_is_a_deterministic_logged_command_that_replays_and_clones()
    {
        var w = Dockable(out _, out int station);
        w.Network.GiveItems(0, Ingot, 20);
        Ok(w, DockWest(station));
        Ok(w, new TransferItems(Ingot, 15, true));
        w.Step(Tps * 5);
        Ok(w, new Undock());
        Ok(w, new SetThrust(0, 0, 200));
        w.Step(Tps);
        Ok(w, new SetThrust(0, 0, 0));
        Ok(w, DockWest(station) with { Station = station }); // a second approach works too (the ship slid 6 units: well in range)

        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        var fresh = Dockable(out _, out _);
        fresh.Network.GiveItems(0, Ingot, 20);
        var replay = SimWorld.Replay(w.Log, w.Tick, fresh);
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }

    [Fact]
    public void Docking_state_and_stock_change_the_checksum_and_unused_state_does_not()
    {
        var a = Dockable(out _, out int st);
        var b = Dockable(out _, out _);
        Assert.Equal(a.Checksum(), b.Checksum());
        Ok(a, DockWest(st));
        Assert.NotEqual(a.Checksum(), b.Checksum());
        var c = Dockable(out _, out int st2);
        var before = c.Checksum();
        c.Network.GiveStock(st2, Ingot, 1);
        Assert.NotEqual(before, c.Checksum());
    }

    [Fact]
    public void Commands_roundtrip_in_the_codec_and_logs_are_version_10()
    {
        var commands = new Command[] { new DockAt(3, -1, 2, 5, Dir.NegZ), new Undock(), new TransferItems(4, -2, true), new TransferItems(0, 9, false) };
        foreach (var c in commands)
            Assert.Equal(c, CommandCodec.FromBytes(CommandCodec.ToBytes(c)));
        var batch = new Batch(commands);
        Assert.Equal(commands, ((Batch)CommandCodec.FromBytes(CommandCodec.ToBytes(batch))).Commands);

        var log = commands.Select((c, i) => new LoggedCommand(i, 0, c)).ToList();
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(25, bytes[4]);
        Assert.Equal(log, CommandCodec.ReadLog(new MemoryStream(bytes)));

        // Older logs cannot hold them: the reader refuses, even inside a batch.
        foreach (byte old in new byte[] { 5, 7, 9, 10 })
        {
            var copy = (byte[])bytes.Clone();
            copy[4] = old;
            Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(copy)));
        }
    }

    [Fact]
    public void An_older_log_still_replays_unchanged()
    {
        // A version 10 log has no docking command; it replays to the state the same commands give now.
        var w = NewWorld(rules: new WorldRules().WithLegacyFeel(true).WithLegacyResources(true).WithLegacyDock(true)); // what a build of that time recorded (ADR 0036 did not exist)
        Ok(w, new SetThrust(0, 0, 512));
        w.Step(Tps * 2);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        bytes[4] = 10;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        var replay = SimWorld.Replay(log, w.Tick, SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: true, rules: rules));
        Assert.Equal(w.Checksum(), replay.Checksum());
    }

    [Fact]
    public void Content_without_the_dock_module_cannot_place_it_and_still_loads()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root["structures"]!.AsObject().Remove("dock");
        var content = ContentDb.FromJson(root.ToJsonString());
        Assert.False(content.Structures!.IsDefined(ModuleKind.Dock));
        var w = NewWorld(content);
        int station = w.Network.CreateStructure(0, StructureKind.Station, -20 * (long)Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Dock)));
        Ok(w, DockWest(station)); // cores and cockpits still dock
    }

    // ---- the key panel shows docking only by context --------------------------------------------------------

    private static string[] KeyIds(Starfoundry.Ui.KeyState s) => Starfoundry.Ui.KeyHints.For(s).Last().Rows.Select(r => r.Id).ToArray();

    [Fact]
    public void Docking_rows_appear_only_near_a_port_or_while_docked()
    {
        var far = new Starfoundry.Ui.KeyState { StructuresOn = true, CanWalk = true };
        Assert.DoesNotContain("dockGuide", KeyIds(far));
        Assert.DoesNotContain("unload", KeyIds(far));

        var near = far with { Dock = Starfoundry.Ui.DockHint.Near, DockDistanceCells = 14, DockSpeedPercent = 60 };
        var nearAssist = near with { Interact = Starfoundry.Ui.HintInteract.DockAssist };
        Assert.Equal("interact", KeyIds(nearAssist)[0]);
        Assert.Equal("key.dockAssist", Starfoundry.Ui.KeyHints.For(nearAssist).Last().Rows[0].LabelKey);
        var guide = Starfoundry.Ui.KeyHints.For(near).Last().Rows.Single(r => r.Id == "dockGuide");
        Assert.Equal(new object[] { 14, 60 }, guide.LabelArgs);
        Assert.Equal(Starfoundry.Ui.KeyRowState.Info, guide.State);
        Assert.Equal("key.dock", Starfoundry.Ui.KeyHints.For(near with { DockReady = true, Interact = Starfoundry.Ui.HintInteract.Dock }).Last().Rows[0].LabelKey);
        var assisting = Starfoundry.Ui.KeyHints.For(near with { DockAssisting = true, Interact = Starfoundry.Ui.HintInteract.DockCancel }).Last().Rows[0];
        Assert.Equal("key.dockCancel", assisting.LabelKey);
        Assert.Equal(Starfoundry.Ui.KeyRowState.Active, assisting.State);

        var docked = far with { Dock = Starfoundry.Ui.DockHint.Docked, StockUsed = 3, StockCapacity = 60, Interact = Starfoundry.Ui.HintInteract.Undock };
        Assert.Equal(new[] { "interact", "unload", "load", "stock" }, KeyIds(docked).Take(4).ToArray());
        Assert.Equal("key.undock", Starfoundry.Ui.KeyHints.For(docked).Last().Rows[0].LabelKey);
        // On foot the same rows show too.
        var walking = docked with { Standing = true };
        Assert.Contains("unload", KeyIds(walking));
        Assert.Contains("stock", KeyIds(walking));
    }

    [Fact]
    public void The_dock_tip_shows_once_when_a_free_port_is_close()
    {
        var far = Dockable(out _, out _, gapCells: 80);
        far.Step(Tps);
        Assert.Null(new Starfoundry.Ui.Tips(C).Next(far.Network, 0));

        var w = Dockable(out _, out _, gapCells: 30);
        w.Step(Tps);
        var tips = new Starfoundry.Ui.Tips(C);
        Assert.Equal("learn.dock", tips.Next(w.Network, 0)?.Key);
        Assert.Null(tips.Next(w.Network, 0)); // once
    }

    [Fact]
    public void Dock_strings_exist_in_both_languages()
    {
        foreach (var key in new[] { "learn.dock", "hud.flightDocked", "mark.dockPort", "why.stationFixed", "toast.docked", "toast.undocked", "toast.undockAboard",
                     "toast.noPortNear", "toast.approachStarted", "toast.approachCancelled", "toast.enterStation", "toast.backAboard", "toast.stockNow" })
        {
            Assert.True(Starfoundry.Ui.Loc.Has(key), key);
            var (es, en) = Starfoundry.Ui.Loc.All.First(p => p.Key == key).Value;
            Assert.NotEqual(es, en);
        }
        foreach (var key in new[] { "key.dock", "key.dockAssist", "key.dockCancel", "key.dockGuide", "key.undock", "key.unload", "key.load", "key.stock" })
        {
            Assert.True(Starfoundry.Ui.Loc.Has(key), key);
            var (es, en) = Starfoundry.Ui.Loc.All.First(p => p.Key == key).Value;
            Assert.NotEqual(es, en);
        }
        foreach (var key in new[] { "module.dock", "module.dock.purpose" })
        {
            Assert.True(Starfoundry.Ui.Loc.Has(key), key);
            var (es, en) = Starfoundry.Ui.Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            Assert.NotEqual(es, en);
        }
    }
}
