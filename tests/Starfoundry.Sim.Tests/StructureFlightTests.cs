using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Moving ships: the structure origin follows the pilot, machines take world positions from it, and flight and hold
/// capacity come from modules.</summary>
public class StructureFlightTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Depot = C.MachineIndex("core:depot");
    private static readonly int OrePort = C.MachineIndex("core:ore_unloader");
    private static readonly int Drill = C.MachineIndex("core:drill");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int DrillIron = C.RecipeIndex("core:drill_iron");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;

    private static SimWorld World(bool threats = false, bool structures = true) =>
        SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats, stationRules: false, structures: structures);

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int Ok(SimWorld w, Command c)
    {
        var r = Run(w, c);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>A ship at the world origin with a workshop module (0,0,0) and the given extra modules, each in its own place
    /// (modules are added without rule checks, so their coordinates only need to differ).</summary>
    private static int Ship(SimWorld w, int cockpits = 1, int engines = 1, int cargos = 1, bool powered = false)
    {
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Workshop);
        int next = 1;
        for (int i = 0; i < cockpits; i++) w.Network.AddStructureModuleUnchecked(s, next++, 0, 0, ModuleKind.Cockpit);
        for (int i = 0; i < engines; i++) w.Network.AddStructureModuleUnchecked(s, next++, 0, 0, ModuleKind.Engine);
        for (int i = 0; i < cargos; i++) w.Network.AddStructureModuleUnchecked(s, next++, 0, 0, ModuleKind.Cargo);
        if (powered)
        {
            // Machines need power: a reactor and some hydrogen in the hold (without them a ship with machines stands still).
            w.Network.AddStructureModuleUnchecked(s, next, 0, 0, ModuleKind.Reactor);
            w.Network.GiveItems(0, C.ItemIndex("core:hydrogen"), 20);
        }
        return s;
    }

    /// <summary>Ore port, belt, smelter, belt, depot inside the workshop of the ship. Returns the depot's belt.</summary>
    private static int Factory(SimWorld w, int s)
    {
        int port = Ok(w, new PlaceMachineIn(s, 1, 1, 1, OrePort, -1));
        int b1 = Ok(w, new PlaceSegmentIn(s, 2, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        int smelter = Ok(w, new PlaceMachineIn(s, 5, 1, 1, Smelter, SmeltIron));
        int b2 = Ok(w, new PlaceSegmentIn(s, 6, 1, 1, Dir.PosX, 2, BeltTier.Mk1));
        int depot = Ok(w, new PlaceMachineIn(s, 7, 1, 2, Depot, -1));
        Ok(w, new ConnectFromMachine(port, b1));
        Ok(w, new ConnectToMachine(b1, smelter));
        Ok(w, new ConnectFromMachine(smelter, b2));
        Ok(w, new ConnectToMachine(b2, depot));
        return b2;
    }

    private static PlayerInfo Pilot(SimWorld w) => w.Network.GetPlayerInfo(0);

    // ---- the factory keeps working while the ship flies ---------------------------------------

    private static (SimWorld World, int Belt) FactoryShip(int cargos)
    {
        var w = World();
        int s = Ship(w, cargos: cargos, powered: true);
        int belt = Factory(w, s);
        int dep = w.Network.AddDeposit(Ore, 1000, 0, 0, 0);
        Ok(w, new StartMining(dep));
        w.Step(15 * 25); // hand-mine into the hold while the ship still sits at the deposit
        Ok(w, new StopMining());
        Assert.True(w.Network.MinedByHand >= 20); // the ore port moves the ore on as it arrives
        return (w, belt);
    }

    [Fact]
    public void Factory_runs_while_the_ship_flies()
    {
        var (parked, pBelt) = FactoryShip(cargos: 2);
        var (flying, fBelt) = FactoryShip(cargos: 2);
        Assert.Equal(parked.Checksum(), flying.Checksum());

        Ok(flying, new SetThrust(PlayerRules.ThrustScale, 0, 0));
        parked.Step(5_000);
        flying.Step(5_000 - 1);
        parked.ValidateInvariants();
        flying.ValidateInvariants();

        Assert.True(Pilot(flying).X > 200_000, "the ship did not fly");
        Assert.Equal(0, Pilot(parked).X);
        Assert.True(flying.Network.DepotDelivered > 10, "the factory produced nothing");
        Assert.Equal(parked.Network.DepotDelivered, flying.Network.DepotDelivered);
        Assert.Equal(parked.Network.HoldCount(0, Ingot), flying.Network.HoldCount(0, Ingot));
        Assert.Equal(parked.Network.HoldCount(0, Ore), flying.Network.HoldCount(0, Ore));
        Assert.Equal(parked.Network.PassedBy(pBelt), flying.Network.PassedBy(fBelt));
        Assert.Equal(parked.Network.MachineOutputsCreated, flying.Network.MachineOutputsCreated);
        Assert.Equal(parked.Network.HoldTotal(0), flying.Network.HoldTotal(0));
    }

    [Fact]
    public void Machines_move_with_the_ship_world_centre()
    {
        var w = World();
        int s = Ship(w, powered: true);
        int m = Ok(w, new PlaceMachineIn(s, 3, 1, 2, Smelter, SmeltIron));
        var before = w.Network.WorldCentre(s, 3, 1, 2);
        Ok(w, new SetThrust(PlayerRules.ThrustScale, 0, 0)); // 48 units a tick along x
        w.Step(99);
        var after = w.Network.WorldCentre(s, 3, 1, 2);
        Assert.Equal((before.X + 100 * 48, before.Y, before.Z), after);
        var st = w.Network.GetStructure(s);
        Assert.Equal((100 * 48L, 0L, 0L), (st.OriginX, st.OriginY, st.OriginZ)); // origin is the source of truth ...
        Assert.Equal((4800, 0, 0), (Pilot(w).X, Pilot(w).Y, Pilot(w).Z));        // ... and the player mirrors it
        var info = w.Network.GetMachineInfo(m);
        Assert.Equal((3, 1, 2), (info.X, info.Y, info.Z)); // the local cell does not change
        Assert.Equal(s, w.Network.ShipStructure(0));
        w.ValidateInvariants();

        // Moving the origin from setup drags the pilot along too.
        w.Network.SetStructureOrigin(s, -2_400, 480, 960);
        Assert.Equal((-2_400, 480, 960), (Pilot(w).X, Pilot(w).Y, Pilot(w).Z));
        w.ValidateInvariants();
    }

    // ---- flight rules ----------------------------------------------------------------------

    [Fact]
    public void Flight_needs_cockpit_and_engine()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Workshop);
        Assert.False(w.Network.CanFly(0));
        Assert.Equal(Reject.NoCockpitOrEngine, Run(w, new SetThrust(1024, 0, 0)).Reject);
        Assert.Equal(Reject.NoCockpitOrEngine, Run(w, new SetMovement(1, 0, 0)).Reject);
        Assert.Equal(Reject.None, Run(w, new SetThrust(0, 0, 0)).Reject); // stopping is always fine
        Assert.Equal(Reject.None, Run(w, new SetMovement(0, 0, 0)).Reject);

        w.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Cockpit);
        Assert.False(w.Network.CanFly(0));
        Assert.Equal(Reject.NoCockpitOrEngine, Run(w, new SetThrust(1024, 0, 0)).Reject);

        w.Network.RemoveStructureModuleUnchecked(s, 1, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 2, 0, 0, ModuleKind.Engine);
        Assert.Equal(Reject.NoCockpitOrEngine, Run(w, new SetMovement(0, 0, 1)).Reject); // engine alone is not enough

        w.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Cockpit);
        Assert.True(w.Network.CanFly(0));
        Assert.Equal(Reject.None, Run(w, new SetThrust(1024, 0, 0)).Reject);
        Assert.Equal(Reject.None, Run(w, new SetMovement(0, 1, 0)).Reject);

        // Legacy worlds (flag off) fly without any structure, and a world with the flag on but no ship structure does too.
        var legacy = World(structures: false);
        legacy.Network.EnsurePlayer(0);
        Assert.Equal(Reject.None, Run(legacy, new SetThrust(1024, 0, 0)).Reject);
        var noShip = World();
        noShip.Network.EnsurePlayer(0);
        Assert.Equal(Reject.None, Run(noShip, new SetMovement(1, 0, 0)).Reject);
        Assert.Equal(-1, noShip.Network.ShipStructure(0));
    }

    [Fact]
    public void Engine_count_scales_speed()
    {
        int Travel(int engines, Action<SimWorld>? command = null)
        {
            var w = World();
            Ship(w, engines: engines);
            (command ?? (x => Ok(x, new SetMovement(1, 0, 0))))(w);
            w.Step(99);
            w.ValidateInvariants();
            return Pilot(w).X;
        }
        Assert.Equal(100 * 48, Travel(1));
        Assert.Equal(100 * 96, Travel(2));
        Assert.Equal(100 * 144, Travel(3));
        Assert.Equal(100 * 192, Travel(4));
        Assert.Equal(100 * 192, Travel(9)); // clamped to 400 percent

        var w = World();
        Ship(w, engines: 3);
        Assert.Equal(144, w.Network.ShipSpeedUnitsPerTick(0));

        // A tiny thrust still moves exactly: the remainder is carried, nothing is lost (48 * 100 / 102400 units a tick).
        var slow = World();
        Ship(slow);
        Ok(slow, new SetThrust(1, 0, 0));
        slow.Step(10_240 - 1);
        Assert.Equal(480, Pilot(slow).X);
        slow.ValidateInvariants();

        // Diagonal axis intent keeps its remainder too (34 units per axis at 100 percent).
        var diag = World();
        Ship(diag, engines: 2);
        Ok(diag, new SetMovement(1, 1, 0));
        diag.Step(49);
        Assert.Equal((50 * 68, 50 * 68, 0), (Pilot(diag).X, Pilot(diag).Y, Pilot(diag).Z));
        diag.ValidateInvariants();
    }

    [Fact]
    public void Hold_capacity_comes_from_cargo_modules()
    {
        var w = World();
        int s = Ship(w, cargos: 0);
        Assert.Equal(0, w.Network.HoldCapacity(0));
        w.Network.AddStructureModuleUnchecked(s, 10, 0, 0, ModuleKind.Cargo);
        Assert.Equal(60, w.Network.HoldCapacity(0));
        w.Network.AddStructureModuleUnchecked(s, 11, 0, 0, ModuleKind.Cargo);
        Assert.Equal(120, w.Network.HoldCapacity(0));
        w.Network.RemoveStructureModuleUnchecked(s, 10, 0, 0);
        Assert.Equal(60, w.Network.HoldCapacity(0));

        // Legacy worlds and players without a ship keep the constant.
        Assert.Equal(PlayerRules.HoldCapacity, World(structures: false).Network.HoldCapacity(0));
        Assert.Equal(PlayerRules.HoldCapacity, World().Network.HoldCapacity(3));

        // Hand mining stops at the reserve limit (75 percent) so the rest stays free for the factory, and does not start without cargo.
        var full = World();
        Ship(full, cargos: 1);
        int dep = full.Network.AddDeposit(Ore, 1000, 0, 0, 0);
        Ok(full, new StartMining(dep));
        full.Step(15 * 90);
        Assert.Equal(60 * PlayerRules.MiningHoldLimitPercent / 100, full.Network.HoldTotal(0));

        var none = World();
        Ship(none, cargos: 0);
        int dep2 = none.Network.AddDeposit(Ore, 1000, 0, 0, 0);
        Ok(none, new StartMining(dep2));
        none.Step(15 * 10);
        Assert.Equal(0, none.Network.HoldTotal(0));
        none.ValidateInvariants();
    }

    [Fact]
    public void Removing_last_engine_stops_the_ship()
    {
        foreach (var lose in new[] { ModuleKind.Engine, ModuleKind.Cockpit })
        {
            foreach (bool thrust in new[] { true, false })
            {
                var w = World();
                int s = Ship(w, engines: 2);
                Ok(w, thrust ? new SetThrust(600, 300, 0) : new SetMovement(1, 1, 0));
                w.Step(50);
                int x = Pilot(w).X;
                Assert.True(x > 0);

                // Losing one engine of two only slows it down.
                if (lose == ModuleKind.Engine)
                {
                    w.Network.RemoveStructureModuleUnchecked(s, 2, 0, 0);
                    w.Step(10);
                    Assert.True(Pilot(w).X > x);
                    x = Pilot(w).X;
                }

                foreach (var (m, _) in w.Network.GetStructure(s).Modules.Where(kv => kv.Value == lose).ToList())
                    w.Network.RemoveStructureModuleUnchecked(s, m.Mx, m.My, m.Mz);
                w.Step(1); // no exception, no movement
                var info = Pilot(w);
                Assert.Equal(x, info.X);
                Assert.Equal((0, 0, 0), (info.ThrustX, info.ThrustY, info.ThrustZ));
                Assert.Equal((0, 0, 0), (info.MoveX, info.MoveY, info.MoveZ));
                w.Step(200);
                Assert.Equal(x, Pilot(w).X);
                Assert.False(w.Network.CanFly(0));
                w.ValidateInvariants();
            }
        }
    }

    // ---- world positions in the threat phase and drills --------------------------------------

    [Fact]
    public void Enemy_targets_use_world_positions_of_a_moving_ship()
    {
        var w = World(threats: true);
        int s = Ship(w);
        w.Network.AddStructureModuleUnchecked(s, 10, 0, 0, ModuleKind.Workshop); // cells x 80..87, far from the pilot
        w.Network.SetStructureOrigin(s, 0, 0, -20 * Cell);                      // the ship sits 20 cells off the world origin
        int lab = Ok(w, new PlaceMachineIn(s, 82, 1, 1, Lab, -1));
        var world = w.Network.WorldCentre(s, 82, 1, 1);
        Assert.Equal((82 * Cell + Cell / 2, 1 * Cell + Cell / 2, -20 * Cell + Cell + Cell / 2), world);

        w.Network.ScheduleWave(0);
        // Enemies hit modules, not the machines inside: every shot lands on the world centre of a module of the ship
        // where the ship put it, never on a local cell. (Changed with structure defence: it used to hit the machine.)
        var centres = w.Network.GetStructure(s).Modules.Keys
            .Select(k => w.Network.ModuleWorldCentre(s, k.Mx, k.My, k.Mz))
            .Select(c => ((int)c.X, (int)c.Y, (int)c.Z)).ToHashSet();
        int fired = 0;
        for (int i = 0; i < 3_000 && fired < 5; i++)
        {
            w.Step();
            foreach (var shot in w.Network.Shots)
            {
                if (shot.Kind != ShotKind.Enemy) continue;
                fired++;
                Assert.Contains((shot.ToX, shot.ToY, shot.ToZ), centres);
            }
        }
        Assert.True(fired >= 5, "the enemies never attacked");
        Assert.True(w.Network.IsMachineAlive(lab), "a hit on the module does not hurt the machine inside");
        w.ValidateInvariants();

        // The system of a structure machine follows the ship: carry the ship out of the home system and the signature leaves it.
        var w2 = World(threats: true);
        int s2 = Ship(w2);
        Ok(w2, new PlaceMachineIn(s2, 1, 1, 1, Lab, -1));
        Assert.Equal(4, w2.Network.SystemSignature(0));
        var far = w2.Network.Galaxy!.Systems.First(sys => sys.Id != 0);
        w2.Network.SetStructureOrigin(s2, (long)far.X * Cell, (long)far.Y * Cell, (long)far.Z * Cell);
        Assert.Equal(0, w2.Network.SystemSignature(0));
        Assert.Equal(4, w2.Network.SystemSignature(far.Id));
    }

    [Fact]
    public void Drill_link_dropped_when_the_structure_leaves_range()
    {
        var net = new BeltNetwork(C, GameMode.Sandbox, threats: false, stationRules: false, structures: true);
        int dep = net.AddDeposit(Ore, 100_000, 0, 0, 0);
        int st = net.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        int drill = net.AddMachine(1, 1, 1, Drill, DrillIron, owner: 0, structure: st);
        var w = new SimWorld(net);

        w.Submit(0, new LinkDeposit(drill, dep));
        w.Step();
        Assert.True(w.LastResults.Single().Ok);
        Assert.Equal(dep, net.GetMachineInfo(drill).Deposit);
        w.Step(600);
        long drilled = net.DrilledTotal;
        Assert.True(drilled > 0);

        // Move the station 40 cells away through setup: the next tick drops the link and drilling stops.
        net.SetStructureOrigin(st, 40L * Cell, 0, 0);
        w.Step();
        Assert.Equal(-1, net.GetMachineInfo(drill).Deposit);
        w.Step(600);
        Assert.Equal(drilled, net.DrilledTotal);
        w.ValidateInvariants();

        // Linking from there is refused; moving back makes it possible again.
        w.Submit(0, new LinkDeposit(drill, dep));
        w.Step();
        Assert.Equal(Reject.OutOfRange, w.LastResults.Single().Reject);
        net.SetStructureOrigin(st, 10L * Cell, 0, 0);
        w.Submit(0, new LinkDeposit(drill, dep));
        w.Step();
        Assert.True(w.LastResults.Single().Ok);

        // A drill within range of its deposit keeps the link while its station stays put.
        w.Step(300);
        Assert.Equal(dep, net.GetMachineInfo(drill).Deposit);
    }

    // ---- replay --------------------------------------------------------------------------------

    [Fact]
    public void Replay_with_flight_matches_checksum()
    {
        // The ship is setup (not a command); the factory is built with commands, which the log replays.
        (SimWorld World, int Ship) Fresh()
        {
            var w = World(threats: true);
            int s = Ship(w, engines: 2, cargos: 2, powered: true);
            w.Network.AddDeposit(Ore, 1000, 0, 0, 0);
            return (w, s);
        }

        var (a, ship) = Fresh();
        Factory(a, ship);
        int dep = a.Network.DepositCount - 1;
        a.Submit(0, new StartMining(dep));
        a.Step(15 * 20);
        a.Submit(0, new StopMining());
        a.Submit(0, new SetThrust(800, -200, 300));
        a.Step(1_500);
        a.Submit(0, new SetMovement(0, 0, 1));
        a.Step(700);
        a.Submit(0, new SetThrust(0, 0, 0));
        a.Step(2_000);
        a.ValidateInvariants();
        Assert.True(Pilot(a).X > 0 && Pilot(a).Z > 0);

        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, a.Log, C.Hash, GameMode.Sandbox, Seed, threats: true, structures: true);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out _, out _, out _, out _, out _);
        var b = SimWorld.Replay(log, a.Tick, Fresh().World);
        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.Equal(Pilot(a), Pilot(b));
        Assert.Equal(a.Network.GetStructure(1).OriginX, b.Network.GetStructure(1).OriginX);

        // A clone keeps the ship link and hashes the same.
        Assert.Equal(a.Network.Clone().Checksum(), a.Checksum());
        Assert.Equal(1, a.Network.Clone().ShipStructure(0));
    }
}
