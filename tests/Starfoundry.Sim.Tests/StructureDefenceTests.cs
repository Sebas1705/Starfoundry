using Starfoundry.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Defence and survival of structures: enemies attack modules, modules have hit points and fall, bays and time mend them,
/// defence modules strengthen turrets, a station dies with its core and a ship with its last cockpit. See ADR 0021 and
/// docs/prototypes/defence.md.
/// </summary>
[Trait("Tier", "Slow")]
public class StructureDefenceTests
{
    private const int Tps = Units.TicksPerSecond;
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261121UL;

    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int Wall = C.MachineIndex("core:wall");
    private static readonly int Bay = C.MachineIndex("core:repair_bay");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Depot = C.MachineIndex("core:depot");
    private static readonly int OrePort = C.MachineIndex("core:ore_unloader");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Ammo = C.ItemIndex("core:ammo");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");

    private readonly ITestOutputHelper _out;
    public StructureDefenceTests(ITestOutputHelper output) => _out = output;

    private static int Hp(ModuleKind kind) => C.Structures!.ModuleHp(kind);

    private static SimWorld World(ContentDb? content = null) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true, rules: TestContent.LegacyShip);

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

    /// <summary>A ship at the world origin with the given modules (added without rule checks), a reactor module appended
    /// when <paramref name="powered"/> and hydrogen in the hold for it.</summary>
    private static int Ship(SimWorld w, bool powered, params (int Mx, ModuleKind Kind)[] modules)
    {
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        foreach (var (mx, kind) in modules) w.Network.AddStructureModuleUnchecked(s, mx, 0, 0, kind);
        if (powered)
        {
            w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor); // far along the row: never the nearest module
            w.Network.GiveItems(0, Hydrogen, 60);
        }
        return s;
    }

    private static readonly (int, ModuleKind)[] FlyingShip =
    {
        (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine), (3, ModuleKind.Cargo),
    };

    /// <summary>A line of belts with an ammo source into <paramref name="count"/> turrets: belts in the workshop module (default 1), turrets
    /// in the first cells of the defence module next to it, one per row.</summary>
    private static void Battery(SimWorld w, int s, int count, int ws = 1)
    {
        for (int i = 0; i < count; i++)
        {
            int belt = Ok(w, new PlaceSegmentIn(s, ws * 8 + 4, 1, i, Dir.PosX, 4, BeltTier.Mk2));
            Ok(w, new SetSource(belt, new ushort[] { (ushort)Ammo }));
            int turret = Ok(w, new PlaceMachineIn(s, ws * 8 + 8, 1, i, Turret, -1));
            Ok(w, new ConnectToMachine(belt, turret));
        }
    }

    private static (long X, long Y, long Z) Centre(SimWorld w, int s, (int Mx, int My, int Mz) k) => w.Network.ModuleWorldCentre(s, k.Mx, k.My, k.Mz);

    // ---- targeting ----------------------------------------------------------------------------

    [Fact]
    public void Enemy_attacks_the_nearest_module()
    {
        var w = World();
        int ship = Ship(w, powered: true, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine), (3, ModuleKind.Cargo));
        int station = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 16 * Cell);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        Ok(w, new PlaceMachineIn(station, 1, 1, 1, Lab, -1)); // a station is a target only once it holds a machine

        w.Network.ScheduleWave(0);
        while (w.Network.EnemyCount == 0) w.Step();
        for (int round = 0; round < 6; round++)
        {
            var before = w.Network.Enemies();
            w.Step();
            foreach (var e in w.Network.Enemies())
            {
                if (!before.Any(b => b.Id == e.Id)) continue; // spawned this tick
                var was = before.First(b => b.Id == e.Id);
                // The nearest module centre of every structure in the system, ships and stations alike.
                (int S, (int, int, int) K, long D) best = (-1, default, long.MaxValue);
                foreach (var st in w.Network.Structures())
                    foreach (var k in st.Modules.Keys)
                    {
                        var c = Centre(w, st.Id, k);
                        long dx = c.X - was.X, dy = c.Y - was.Y, dz = c.Z - was.Z, d = dx * dx + dy * dy + dz * dz;
                        if (d < best.D) best = (st.Id, k, d);
                    }
                Assert.Equal(best.S, e.TargetStructure);
                Assert.Equal(best.K, (e.TargetMx, e.TargetMy, e.TargetMz));
            }
        }
        Assert.True(w.Network.ShipStructure(0) == ship);
    }

    [Fact]
    public void A_station_without_machines_is_not_a_target_but_a_ship_always_is()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        int station = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.ScheduleWave(0);
        w.Step(Tps * 60);
        Assert.Equal(1000, w.Network.StructureHpFraction(station));
        Assert.All(w.Network.Enemies(), e => Assert.Equal(-1, e.TargetStructure));
        w.Network.ScheduleWave(0); // a second warning does nothing while enemies are around, but a ship draws them
        int ship = w.Network.CreateStructure(0, StructureKind.Ship, 30 * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(ship, 0, 0, 0, ModuleKind.Cockpit);
        w.Step(Tps);
        Assert.Contains(w.Network.Enemies(), e => e.TargetStructure == ship);
    }

    [Fact]
    public void Walls_still_act_as_decoys()
    {
        var w = World();
        int s = Ship(w, powered: true, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine), (5, ModuleKind.Defence));
        int wall = Ok(w, new PlaceMachineIn(s, 41, 1, 1, Wall, -1));
        w.Network.ScheduleWave(0);
        int guard = 0;
        while (w.Network.IsMachineAlive(wall) && guard++ < 20_000)
        {
            w.Step();
            Assert.Empty(w.Network.ModuleHits); // enemies hit the wall, never a module, while a wall stands
        }
        Assert.False(w.Network.IsMachineAlive(wall));
        Assert.Equal(1, w.Network.WallsLost);
        Assert.Equal(0, w.Network.MachinesLost);
        Assert.Equal(1000, w.Network.StructureHpFraction(s));
        w.ValidateInvariants();
    }

    // ---- destruction -----------------------------------------------------------------------------

    /// <summary>Two workshops, a cockpit, an engine; an ore line (port, belt, smelter, belt, depot) in the first and a lab in the second.</summary>
    private static (SimWorld W, int S, int Port, int Smelter, int Depot, int Lab, int B1, int B2) Factory()
    {
        var w = World();
        int s = Ship(w, powered: true, (0, ModuleKind.Workshop), (1, ModuleKind.Workshop), (2, ModuleKind.Cockpit), (3, ModuleKind.Engine));
        w.Network.GiveItems(0, Ore, 30);
        int port = Ok(w, new PlaceMachineIn(s, 1, 1, 1, OrePort, -1));
        int b1 = Ok(w, new PlaceSegmentIn(s, 2, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        int smelter = Ok(w, new PlaceMachineIn(s, 5, 1, 1, Smelter, SmeltIron));
        int b2 = Ok(w, new PlaceSegmentIn(s, 6, 1, 1, Dir.PosX, 2, BeltTier.Mk1));
        int depot = Ok(w, new PlaceMachineIn(s, 7, 1, 2, Depot, -1));
        Ok(w, new ConnectFromMachine(port, b1));
        Ok(w, new ConnectToMachine(b1, smelter));
        Ok(w, new ConnectFromMachine(smelter, b2));
        Ok(w, new ConnectToMachine(b2, depot));
        int lab = Ok(w, new PlaceMachineIn(s, 9, 1, 1, Lab, -1));
        w.Step(240); // items are on the belts
        Assert.True(w.Network.InTransit > 0);
        return (w, s, port, smelter, depot, lab, b1, b2);
    }

    [Fact]
    public void Module_destroyed_removes_machines_and_belts_inside_it()
    {
        var (w, s, port, smelter, depot, lab, b1, b2) = Factory();
        var net = w.Network;
        long destroyedBefore = net.Destroyed;

        net.HitModule(s, 0, 0, 0, Hp(ModuleKind.Workshop) - 1);
        Assert.Equal(1, net.ModuleHp(s, 0, 0, 0));
        Assert.True(net.IsMachineAlive(port), "a damaged module keeps what is inside it");
        net.HitModule(s, 0, 0, 0, 5);

        Assert.False(net.Structures().First().Modules.ContainsKey((0, 0, 0)));
        Assert.All(new[] { port, smelter, depot }, m => Assert.False(net.IsMachineAlive(m)));
        Assert.False(net.IsAlive(b1));
        Assert.False(net.IsAlive(b2));
        Assert.True(net.IsMachineAlive(lab), "the other workshop is untouched");
        Assert.Equal(3, net.MachinesLost);
        Assert.Equal(1, net.ModulesLost);
        Assert.True(net.Destroyed > destroyedBefore, "items on the belts and in the machines are lost");
        var hit = Assert.Single(net.ModuleHits, h => h.Destroyed);
        Assert.Equal((s, 0, 0, 0, ModuleKind.Workshop), (hit.Structure, hit.Mx, hit.My, hit.Mz, hit.Kind));
        Assert.Throws<ArgumentOutOfRangeException>(() => net.ModuleHp(s, 0, 0, 0));
        w.Step(60);
        net.ValidateInvariants();
        w.ValidateInvariants();
    }

    [Fact]
    public void Occupancy_and_invariants_valid_after_destruction()
    {
        var (w, s, _, _, _, lab, _, _) = Factory();
        w.Network.HitModule(s, 0, 0, 0, 10_000);
        w.Step(10);
        w.ValidateInvariants(); // the cell index no longer holds the destroyed machines and belts

        // The freed cells can be built again: put the module back and place into the very cells the smelter and belts used.
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachineIn(s, 5, 1, 1, Smelter, SmeltIron)).Reject);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceMachineIn(s, 5, 1, 1, Smelter, SmeltIron));
        Ok(w, new PlaceSegmentIn(s, 2, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        Assert.Equal(C.Structures!.ModuleHp(ModuleKind.Workshop), w.Network.ModuleHp(s, 0, 0, 0)); // a new module starts whole
        w.Step(120);
        Assert.True(w.Network.IsMachineAlive(lab));
        w.ValidateInvariants();
    }

    [Fact]
    public void Modules_left_disconnected_stay()
    {
        var w = World();
        int s = Ship(w, powered: false, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine));
        w.Network.HitModule(s, 1, 0, 0, 10_000);
        w.Step(5);
        Assert.Equal(new[] { (0, 0, 0), (2, 0, 0) }, w.Network.GetStructure(s).Modules.Keys.ToArray());
        w.ValidateInvariants();
    }

    [Fact]
    public void Station_destroyed_when_core_falls()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        int a = Ok(w, new PlaceMachineIn(st, 1, 1, 1, Lab, -1));
        int b = Ok(w, new PlaceMachineIn(st, 9, 1, 1, Lab, -1));
        int belt = Ok(w, new PlaceSegmentIn(st, 2, 1, 3, Dir.PosX, 3, BeltTier.Mk1));

        w.Network.HitModule(st, 0, 0, 0, Hp(ModuleKind.Core));
        var hit = Assert.Single(w.Network.ModuleHits);
        Assert.True(hit.Destroyed && hit.StationLost);
        Assert.Equal(1, w.Network.StationsLost);
        Assert.Equal(2, w.Network.ModulesLost);
        Assert.False(w.Network.StructureExists(st));
        Assert.All(new[] { a, b }, m => Assert.False(w.Network.IsMachineAlive(m)));
        Assert.False(w.Network.IsAlive(belt));
        Assert.Equal(2, w.Network.MachinesLost);
        w.Step(5);
        Assert.Equal(Reject.NoSuchStructure, Run(w, new PlaceMachineIn(st, 1, 1, 1, Lab, -1)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void Enemies_destroy_an_undefended_station()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        Ok(w, new PlaceMachineIn(st, 1, 1, 1, Lab, -1));
        Ok(w, new PlaceMachineIn(st, 9, 1, 1, Lab, -1));
        w.Network.ScheduleWave(0);
        long start = w.Tick;
        while (w.Network.StationsLost == 0 && w.Tick - start < 20_000) w.Step();
        Assert.Equal(1, w.Network.StationsLost);
        Assert.Equal(2, w.Network.MachinesLost);
        _out.WriteLine($"a wave of two scouts razes a two-module station in {w.Tick - start} ticks");
        w.ValidateInvariants();
    }

    // ---- the ship -------------------------------------------------------------------------------

    [Fact]
    public void Ship_lost_when_cockpit_falls()
    {
        var w = World();
        int s = Ship(w, powered: true, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine), (3, ModuleKind.Cargo));
        w.Submit(0, new SetMovement(1, 0, 0));
        w.Step(20);
        Assert.True(w.Network.GetPlayerInfo(0).X > 0);
        Assert.False(w.Network.ShipLost(0));

        w.Network.HitModule(s, 1, 0, 0, 10_000);
        Assert.True(w.Network.ShipLost(0));
        Assert.True(w.Network.ModuleHits.Single().ShipLost);
        Assert.Equal(1, w.Network.ShipsLost);
        Assert.False(w.Network.CanFly(0));
        int x = w.Network.GetPlayerInfo(0).X;
        w.Step(60);
        var p = w.Network.GetPlayerInfo(0);
        Assert.Equal(x, p.X);
        Assert.Equal((0, 0, 0), (p.MoveX, p.MoveY, p.MoveZ));
        Assert.Equal((0, 0, 0), (p.ThrustX, p.ThrustY, p.ThrustZ));
        Assert.Equal(Reject.NoCockpitOrEngine, Run(w, new SetMovement(1, 0, 0)).Reject);
        Assert.Equal(0, p.RespawnRemaining); // no respawn in structure worlds
        w.ValidateInvariants();
    }

    [Fact]
    public void A_second_cockpit_keeps_the_ship()
    {
        var w = World();
        int s = Ship(w, powered: true, (0, ModuleKind.Cockpit), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine));
        w.Network.HitModule(s, 0, 0, 0, 10_000);
        Assert.False(w.Network.ShipLost(0));
        w.Network.HitModule(s, 1, 0, 0, 10_000);
        Assert.True(w.Network.ShipLost(0));
    }

    [Fact]
    public void Enemies_hunt_the_ship_down()
    {
        var w = World();
        Ship(w, powered: true, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine));
        w.Network.ScheduleWave(0);
        long start = w.Tick;
        while (!w.Network.ShipLost(0) && w.Tick - start < 30_000) w.Step();
        Assert.True(w.Network.ShipLost(0), "an undefended ship is eventually lost");
        _out.WriteLine($"a wave of two scouts takes down an undefended three-module ship in {w.Tick - start} ticks");
        w.ValidateInvariants();
    }

    // ---- repair --------------------------------------------------------------------------------

    private static int BayShip(SimWorld w, bool withBay, bool powered)
    {
        int s = Ship(w, powered, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Engine));

        if (withBay)
        {
            int belt = Ok(w, new PlaceSegmentIn(s, 1, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
            Ok(w, new SetSource(belt, new ushort[] { (ushort)Ingot }));
            int bay = Ok(w, new PlaceMachineIn(s, 4, 1, 1, Bay, -1));
            Ok(w, new ConnectToMachine(belt, bay));
        }
        return s;
    }

    [Fact]
    public void Repair_bay_heals_modules()
    {
        int max = Hp(ModuleKind.Workshop), heal = C.Machines[Bay].Repairer!.Heal;
        var with = World();
        int s = BayShip(with, withBay: true, powered: true);
        var without = World();
        int s2 = BayShip(without, withBay: false, powered: true);
        with.Step(60); without.Step(60);

        with.Network.HitModule(s, 0, 0, 0, 120);
        without.Network.HitModule(s2, 0, 0, 0, 120);
        with.Network.HitModule(s, 1, 0, 0, 30); // the cockpit next door is mended too, after the workshop (module order)
        Assert.Equal(max - 120, with.Network.ModuleHp(s, 0, 0, 0));
        with.Step(5 * Tps); without.Step(5 * Tps);

        Assert.True(with.Network.ModuleHp(s, 0, 0, 0) >= max - 120 + 3 * heal, "a powered bay with ingots heals the module");
        Assert.True(without.Network.ModuleHp(s2, 0, 0, 0) <= max - 120 + 1, "without a bay only the slow passive repair acts");
        Assert.True(with.Network.AutoRepaired > 0);
        with.Step(20 * Tps);
        Assert.Equal(max, with.Network.ModuleHp(s, 0, 0, 0));
        Assert.Equal(Hp(ModuleKind.Cockpit), with.Network.ModuleHp(s, 1, 0, 0));
        Assert.Equal(1000, with.Network.StructureHpFraction(s));
        with.ValidateInvariants();
    }

    [Fact]
    public void A_bay_without_power_does_not_heal()
    {
        var w = World(TestContent.CoreWithoutEmergencyPower);
        // A reactor, no hydrogen and no emergency output: nothing is powered, so the bay never gets its work credit.
        int s = BayShip(w, withBay: true, powered: false);
        w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor);
        w.Step(30);
        w.Network.HitModule(s, 0, 0, 0, 100);
        w.Step(10 * Tps);
        Assert.Equal(Hp(ModuleKind.Workshop) - 100, w.Network.ModuleHp(s, 0, 0, 0));
        Assert.Equal(0, w.Network.AutoRepaired);
    }

    [Fact]
    public void Passive_regeneration_needs_full_power()
    {
        int every = C.Threat!.ModuleRegenTicks;
        Assert.Equal(10 * Tps, every);

        // Nothing to power and no reactor: full satisfaction, one hit point per interval.
        var ok = World();
        int s = Ship(ok, powered: false, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit));
        ok.Network.HitModule(s, 1, 0, 0, 10);
        ok.Step(3 * every);
        Assert.Equal(Hp(ModuleKind.Cockpit) - 10 + 3, ok.Network.ModuleHp(s, 1, 0, 0));
        ok.Step(8 * every);
        Assert.Equal(Hp(ModuleKind.Cockpit), ok.Network.ModuleHp(s, 1, 0, 0)); // and stops at full (no damage entry left)
        ok.ValidateInvariants();

        // A reactor without hydrogen (and no emergency output): satisfaction 0, nothing regenerates.
        var dark = World(TestContent.CoreWithoutEmergencyPower);
        int d = Ship(dark, powered: false, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit), (2, ModuleKind.Reactor));
        dark.Network.HitModule(d, 1, 0, 0, 10);
        dark.Step(3 * every);
        Assert.Equal(Hp(ModuleKind.Cockpit) - 10, dark.Network.ModuleHp(d, 1, 0, 0));

        // The same with fuel: regenerates again.
        var lit = World();
        int l = Ship(lit, powered: true, (0, ModuleKind.Workshop), (1, ModuleKind.Cockpit));
        lit.Network.HitModule(l, 1, 0, 0, 10);
        lit.Step(3 * every);
        Assert.Equal(Hp(ModuleKind.Cockpit) - 10 + 3, lit.Network.ModuleHp(l, 1, 0, 0));
    }

    // ---- defence ------------------------------------------------------------------------------

    [Fact]
    public void Turrets_in_defence_modules_kill_enemies()
    {
        var w = World();
        int s = Ship(w, powered: true, (0, ModuleKind.Cockpit), (1, ModuleKind.Workshop), (2, ModuleKind.Defence), (3, ModuleKind.Engine));
        Battery(w, s, 2);
        w.Step(Tps);
        w.Network.ScheduleWave(0);
        w.Step(Tps * 90);
        Assert.Equal(2, w.Network.EnemiesSpawned);
        Assert.Equal(2, w.Network.EnemiesKilled);
        Assert.Equal(0, w.Network.ModulesLost);
        Assert.True(w.Network.StructureHpFraction(s) >= 900, $"the base took little damage ({w.Network.StructureHpFraction(s)} per mille)");
        Assert.True(w.Network.AmmoFired > 0);
        w.ValidateInvariants();
    }

    [Fact]
    public void Defence_bonus_stacks_and_caps()
    {
        int per = C.Structures!.DefenceBonusPercent(ModuleKind.Defence);
        Assert.Equal(10, per);
        Assert.Equal(0, C.Structures.DefenceBonusPercent(ModuleKind.Hull));

        var w = World();
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        Assert.Equal(0, w.Network.DefenceBonusPercent(s));
        for (int n = 1; n <= 7; n++)
        {
            w.Network.AddStructureModuleUnchecked(s, n, 0, 0, ModuleKind.Defence);
            Assert.Equal(Math.Min(n * per, DefenceRules.MaxBonusPercent), w.Network.DefenceBonusPercent(s));
        }
        Assert.Equal(50, w.Network.DefenceBonusPercent(s));
        Assert.Equal(0, w.Network.DefenceBonusPercent(0));

        // Legacy worlds ignore the bonus even if a structure has defence modules.
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: false);
        legacy.Network.EnsurePlayer(0);
        int ls = legacy.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        legacy.Network.AddStructureModuleUnchecked(ls, 0, 0, 0, ModuleKind.Defence);
        Assert.Equal(0, legacy.Network.DefenceBonusPercent(ls));
    }

    [Fact]
    public void Defence_bonus_makes_turrets_hit_harder()
    {
        // The same battery with and without extra defence modules: more modules, fewer shots to kill the scouts.
        long Shots(int extra)
        {
            var w = World();
            int s = Ship(w, powered: true, (0, ModuleKind.Cockpit), (1, ModuleKind.Workshop), (2, ModuleKind.Defence), (3, ModuleKind.Engine));
            for (int i = 0; i < extra; i++) w.Network.AddStructureModuleUnchecked(s, 4 + i, 0, 0, ModuleKind.Defence);
            Battery(w, s, 3);
            w.Step(Tps);
            for (int wave = 0; wave < 3; wave++)
            {
                w.Network.ScheduleWave(0);
                for (int i = 0; i < 20_000 && (w.Network.EnemyCount > 0 || w.Network.WavesSpawned <= wave); i++) w.Step();
                Assert.Equal(0, w.Network.EnemyCount);
            }
            return w.Network.AmmoFired;
        }
        long plain = Shots(0), boosted = Shots(4);
        _out.WriteLine($"shots to kill waves 0 to 2: {plain} with one defence module, {boosted} with five");
        Assert.True(boosted < plain);
    }

    [Fact]
    public void Measure_what_a_wave_does_and_what_holds_it()
    {
        // (a) Ticks for the first wave (two scouts) to destroy a lone undefended module, counted from the spawn, and from the first hit.
        foreach (var kind in new[] { ModuleKind.Cockpit, ModuleKind.Workshop, ModuleKind.Core, ModuleKind.Defence })
        {
            var w = World();
            w.Network.EnsurePlayer(0);
            int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
            w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, kind);
            w.Network.ScheduleWave(0);
            while (w.Network.EnemyCount == 0) w.Step();
            long spawn = w.Tick, firstHit = -1;
            while (w.Network.ModulesLost == 0 && w.Tick - spawn < 20_000)
            {
                w.Step();
                if (firstHit < 0 && w.Network.ModuleHits.Count > 0) firstHit = w.Tick;
            }
            Assert.Equal(1, w.Network.ModulesLost);
            _out.WriteLine($"{kind} ({Hp(kind)} hp): destroyed {w.Tick - spawn} ticks ({(w.Tick - spawn) / (double)Tps:F1} s) after the spawn, {w.Tick - firstHit} ticks after the first hit");
        }

        // (b) Waves 0 to 4 against n ammo-fed turrets in one defence module, fought in sequence in one world.
        for (int n = 0; n <= 8; n++)
        {
            var w = World();
            int s = Ship(w, powered: true, (0, ModuleKind.Cockpit), (1, ModuleKind.Workshop), (2, ModuleKind.Defence), (3, ModuleKind.Engine));
            Battery(w, s, n);
            w.Step(Tps);
            var line = new System.Text.StringBuilder($"{n} turrets:");
            for (int wave = 0; wave < 5; wave++)
            {
                long lost = w.Network.ModulesLost;
                int fraction = w.Network.StructureHpFraction(s);
                w.Network.ScheduleWave(0);
                for (int i = 0; i < 20_000 && (w.Network.EnemyCount > 0 || w.Network.WavesSpawned <= wave); i++) w.Step();
                bool cleared = w.Network.EnemyCount == 0;
                line.Append($"  w{wave}: {(cleared && w.Network.ModulesLost == lost ? "held" : "FELL")} ({w.Network.ModulesLost - lost} lost, hp {w.Network.StructureHpFraction(s)}/1000)");
                if (!cleared || w.Network.ModulesLost > lost) break;
                _ = fraction;
                w.Step(Tps * 120); // a pause: the slow repair works, bays are not built here
            }
            _out.WriteLine(line.ToString());
        }
    }

    // ---- waves ------------------------------------------------------------------------------

    /// <summary>The starter ship of the real content with six labs added to its workshop (loud: 4 signature each).</summary>
    private static SimWorld LoudStarter(ContentDb content)
    {
        var w = World(content);
        int s = w.Network.ShipStructure(0);
        for (int i = 0; i < 6; i++) Ok(w, new PlaceMachineIn(s, i, 2, 12, content.MachineIndex("core:research_lab"), -1));
        return w;
    }

    [Fact]
    public void First_wave_not_before_eight_minutes_in_structure_worlds()
    {
        var core = TestContent.Core;
        Assert.Equal(8 * 60 * Tps, core.Threat!.FirstWaveGraceTicks);
        var w = LoudStarter(core);
        long alertFull = -1, warning = -1, spawned = -1;
        for (int i = 0; i < 60_000 && spawned < 0; i++)
        {
            w.Step();
            var t = w.Network.GetSystemThreat(0);
            if (alertFull < 0 && t.Alert >= t.AlertMax) alertFull = w.Tick;
            if (warning < 0 && t.WarningRemaining >= 0) warning = w.Tick;
            if (w.Network.WavesSpawned > 0) spawned = w.Tick;
        }
        _out.WriteLine($"signature {w.Network.SystemSignature(0)}; alert full at {alertFull} ticks, first warning at {warning}, first wave at {spawned}");
        Assert.True(alertFull >= 0 && alertFull < 8 * 60 * Tps, "the alert alone would have warned earlier");
        Assert.True(warning >= 8 * 60 * Tps, $"first warning at tick {warning}");
        Assert.True(spawned >= warning + core.Threat.WarningTicks - 1);
        w.ValidateInvariants();
    }

    [Fact]
    public void Legacy_worlds_keep_their_first_wave_timing()
    {
        // Without structure rules the grace does not apply: a loud base warns as soon as the alert is full.
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: true);
        var smelter = TestContent.Core.MachineIndex("core:smelter");
        for (int i = 0; i < 6; i++) Ok(w, new PlaceMachine(i * 2, 0, 0, TestContent.Core.MachineIndex("core:research_lab"), -1));
        _ = smelter;
        long warning = -1;
        for (int i = 0; i < 20_000 && warning < 0; i++)
        {
            w.Step();
            if (w.Network.GetSystemThreat(0).WarningRemaining >= 0) warning = w.Tick;
        }
        Assert.True(warning > 0 && warning < 8 * 60 * Tps, $"warned at {warning}");
    }

    // ---- Ui ------------------------------------------------------------------------------------

    [Fact]
    public void Defend_objective_needs_a_powered_turret_in_a_defence_module_with_ammo()
    {
        var tracker = new Starfoundry.Ui.ObjectiveTracker(C, structures: true);
        var w = World(TestContent.CoreWithoutEmergencyPower);
        int s = Ship(w, powered: false, (0, ModuleKind.Cockpit), (1, ModuleKind.Workshop), (2, ModuleKind.Defence), (3, ModuleKind.Engine));
        w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor); // a reactor, no hydrogen and no emergency output: no power
        Battery(w, s, 1);
        w.Step(Tps * 3);
        Assert.True(w.Network.MachineInCount(Enumerable.Range(0, w.Network.MachineIdLimit).First(id => w.Network.IsMachineAlive(id) && C.Machines[w.Network.GetMachineInfo(id).Type].Role == MachineRole.Turret), Ammo) > 0);
        tracker.Update(w.Network, 0);
        Assert.False(tracker.IsDone(Starfoundry.Ui.Objective.Defend), "an unpowered turret does not defend");

        w.Network.GiveItems(0, Hydrogen, 5);
        w.Step(Tps * 3);
        tracker.Update(w.Network, 0);
        Assert.True(tracker.IsDone(Starfoundry.Ui.Objective.Defend));
    }

    [Fact]
    public void Defence_tip_fires_once_at_the_first_warning_when_there_is_no_turret()
    {
        var core = TestContent.Core;
        var w = SimWorld.CreateGalaxy(core, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true, rules: TestContent.LegacyShip);
        var tips = new Starfoundry.Ui.Tips(core);
        Assert.Null(tips.Next(w.Network, 0));
        w.Network.ScheduleWave(0);
        w.Step(Tps);
        Assert.Equal("learn.defence", tips.Next(w.Network, 0)?.Key);
        Assert.Null(tips.Next(w.Network, 0));

        var armed = World();
        int s = Ship(armed, powered: true, (0, ModuleKind.Cockpit), (1, ModuleKind.Workshop), (2, ModuleKind.Defence), (3, ModuleKind.Engine));
        Battery(armed, s, 1);
        armed.Network.ScheduleWave(0);
        armed.Step(Tps);
        Assert.Null(new Starfoundry.Ui.Tips(C).Next(armed.Network, 0));
    }

    [Fact]
    public void Defence_strings_exist_in_both_languages()
    {
        foreach (var key in new[] { "toast.moduleDestroyed", "toast.stationLost", "toast.shipLostCockpit", "toast.shipLost", "toast.waveWarning", "learn.defence" })
        {
            Assert.True(Starfoundry.Ui.Loc.Has(key), key);
            var (es, en) = Starfoundry.Ui.Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
    }

    // ---- determinism and compatibility ----------------------------------------------------------------

    private static ContentDb Stripped()
    {
        // The core content as it was before structure defence: no new keys, the old defence module hit points.
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        // And before scenario start structures existed: the starter ship is the only template.
        foreach (var tid in new[] { "cold_ship", "siege_ship", "siege_station", "blank_station", "breach_ship" }) root["structure_templates"]!.AsObject().Remove(tid);
        root["threat"]!.AsObject().Remove("module_regen_seconds");
        root["threat"]!.AsObject().Remove("first_wave_grace_seconds");
        root["structures"]!["defence"]!.AsObject().Remove("defence_bonus_percent");
        root["structures"]!["defence"]!["hp"] = 250;
        // And before the structure economy fixes: the two extra ports, the old module costs and no emergency output.
        var machines = root["machines"]!.AsArray();
        foreach (var id in new[] { "core:quartz_unloader", "core:carbon_unloader" })
            machines.Remove(machines.First(m => m!["id"]!.GetValue<string>() == id));
        root["structures"]!["reactor"]!.AsObject().Remove("emergency_power");
        root["structures"]!["reactor"]!["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:iron_plate","count":16},{"item":"core:carbon","count":8},{"item":"core:nickel_plate","count":6}]""");
        root["structures"]!["defence"]!["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:iron_plate","count":12},{"item":"core:iron_ingot","count":16},{"item":"core:titanium_plate","count":4}]""");
        // And before the second audit: nickel fields and titanium veins had no carbon deposit.
        foreach (var id in new[] { "core:nickel_field", "core:titanium_vein" })
        {
            var deposits = root["archetypes"]!.AsArray().First(a => a!["id"]!.GetValue<string>() == id)!["deposits"]!.AsArray();
            deposits.Remove(deposits.First(d => d!["item"]!.GetValue<string>() == "core:carbon"));
        }
        // And before the wave balance pass: no stronger ship cannon in structure worlds and the same regeneration for every module.
        root["threat"]!.AsObject().Remove("structure_ship_damage_percent");
        root["structures"]!["defence"]!.AsObject().Remove("regen_hp");
        // And before carbon drilling became carbon processing: 135 plates and no bonus.
        var cd = root["research"]!.AsArray().First(r => r!["id"]!.GetValue<string>() == "core:carbon_drilling")!.AsObject();
        cd.Remove("bonuses");
        cd["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:iron_plate","count":135}]""");
        // And before the engine became rebuildable from home items: it cost titanium plates.
        root["structures"]!["engine"]!["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:iron_plate","count":16},{"item":"core:carbon","count":4},{"item":"core:titanium_plate","count":6}]""");
        // And before the resource expansion: no silicon, circuit, alloy or fuel cell, no electronics research and none of the three new
        // module kinds (their entries are optional). Items and research are part of the hold and research hashes, so they must be
        // absent for the golden checksums to hold.
        foreach (var kind in new[] { "shield", "sensor", "reactor_mk2", "dock" })
            root["structures"]!.AsObject().Remove(kind);
        // And before the hardpoint modules and the shipyard (ADR 0032): none of the five kinds, and the starter ship has no laser and no gun module.
        foreach (var kind in new[] { "mining_laser", "mining_laser_mk2", "gun", "gun_mk2", "shipyard" })
            root["structures"]!.AsObject().Remove(kind);
        TestContent.WithoutHardpointModules(root);
        void RemoveWhere(string section, params string[] ids)
        {
            var arr = root[section]!.AsArray();
            foreach (var id in ids) arr.Remove(arr.First(e => e!["id"]!.GetValue<string>() == id));
        }
        RemoveWhere("research", "core:advanced_power", "core:sensors", "core:shielding", "core:electronics", "core:silicon_processing");
        RemoveWhere("recipes", "core:smelt_silicon", "core:make_circuit", "core:make_alloy", "core:make_fuel_cell");
        RemoveWhere("items", "core:silicon", "core:circuit", "core:titanium_alloy", "core:fuel_cell");
        // And before the science fields (ADR 0026): no science items, biomass, science machines, recipes or research, so the item,
        // machine and research tables (and everything hashed from them) are as they were.
        RemoveWhere("research", "core:structural_plating", "core:alloy_hulls", "core:repair_drones", "core:automated_turrets", "core:cargo_expansion",
            "core:algae_fuel", "core:closed_loop", "core:medical_bay", "core:bio_sensors", "core:symbiotic_drones",
            "core:field_theory", "core:efficient_reactors", "core:long_range_jump", "core:targeting_lasers", "core:quantum_sensors");
        // And before the mining laser (ADR 0027): no laser research (research is part of the hashed state).
        RemoveWhere("research", "core:laser_focusing", "core:beam_collimator");
        RemoveWhere("recipes", "core:make_engineering_science", "core:make_physics_science", "core:grow_biomass", "core:make_biology_science", "core:biomass_to_hydrogen");
        RemoveWhere("machines", "core:engineering_bench", "core:physics_lab", "core:greenhouse");
        RemoveWhere("items", "core:science_engineering", "core:science_biology", "core:science_physics", "core:biomass");
        // And before the optional material chains: composite research and its two recipes, and the three items the legacy snapshot carries.
        RemoveWhere("research", "core:composites");
        RemoveWhere("recipes", "core:make_carbon_fiber", "core:make_composite_plate");
        RemoveWhere("items", "core:carbon_fiber", "core:composite_plate", "core:resonant_crystal");
        // And before the Normal-mode hardening: no bulwark and the original wave table.
        TestContent.ClassicWaves(root);
        // And before the second balance pass: the small container held 60.
        root["structures"]!["cargo"]!["hold_capacity"] = 60;
        // And the drone chain: a slow bay made of titanium and nickel plates, and automation behind advanced plating.
        root["structures"]!["drone_bay"]!["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:iron_plate","count":12},{"item":"core:titanium_plate","count":8},{"item":"core:nickel_plate","count":8}]""");
        root["structures"]!["drone_bay"]!["drone_interval_ticks"] = 90;
        var da = root["research"]!.AsArray().First(r => r!["id"]!.GetValue<string>() == "core:drone_automation")!.AsObject();
        da["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:iron_plate","count":150},{"item":"core:titanium_plate","count":20},{"item":"core:quartz","count":20}]""");
        da["requires"] = System.Text.Json.Nodes.JsonNode.Parse("""["core:logistics","core:advanced_plating"]""");
        // And before the wide galaxy (ADR 0028): no world section (obstacles, star types, boost), no wide distances, and the drone bays reached 60 cells.
        root.Remove("world");
        foreach (var a in root["archetypes"]!.AsArray()) a!.AsObject().Remove("wide_distance_cells");
        root["structures"]!["drone_bay"]!["drone_range_cells"] = 60;
        // And the home iron deposit held 4000.
        root["archetypes"]!.AsArray().First(a => a!["id"]!.GetValue<string>() == "core:home")!["deposits"]!.AsArray()
            .First(d => d!["item"]!.GetValue<string>() == "core:iron_ore")!["amount"] = System.Text.Json.Nodes.JsonNode.Parse("[4000,4000]");
        // And the home ice deposit held 600 to 800.
        var home = root["archetypes"]!.AsArray().First(a => a!["id"]!.GetValue<string>() == "core:home")!["deposits"]!.AsArray();
        home.First(d => d!["item"]!.GetValue<string>() == "core:ice")!["amount"] = System.Text.Json.Nodes.JsonNode.Parse("[600,800]");
        return ContentDb.FromJson(root.ToJsonString());
    }

    [Fact]
    public void Legacy_worlds_unchanged()
    {
        // Golden checksums recorded before structure defence existed (on the stripped content): neither a legacy world with a wave
        // nor an undamaged structure world may hash differently now.
        var c = Stripped();
        var legacy = SimWorld.CreateGalaxy(c, GameMode.Sandbox, Seed, threats: true);
        foreach (var (name, x) in new[] { ("core:smelter", 0), ("core:assembler", 3), ("core:research_lab", 6), ("core:research_lab", 9) })
            Ok(legacy, new PlaceMachine(x, 0, 0, c.MachineIndex(name), name == "core:smelter" ? c.RecipeIndex("core:smelt_iron") : name == "core:assembler" ? c.RecipeIndex("core:iron_plate") : -1));
        legacy.Network.ScheduleWave(0);
        legacy.Step(4000);
        Assert.True(legacy.Network.WavesSpawned > 0 && legacy.Network.MachinesLost > 0);
        Assert.Equal(LegacyGolden, legacy.Checksum());

        var structure = SimWorld.CreateGalaxy(c, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true, rules: TestContent.LegacyShip);
        structure.Step(3000);
        Assert.Equal(StructureGolden, structure.Checksum());
    }

    private const ulong LegacyGolden = 6434738610222952427UL;
    private const ulong StructureGolden = 9473227326653151047UL;

    private static SimWorld DamagedStarterWorld(out int ticks)
    {
        var w = LoudStarter(TestContent.Core);
        int guard = 0;
        while (w.Network.ModulesLost == 0 && w.Network.StructureHpFraction(w.Network.ShipStructure(0)) == 1000 && guard++ < 80_000) w.Step();
        Assert.True(w.Network.StructureHpFraction(w.Network.ShipStructure(0)) < 1000 || w.Network.ModulesLost > 0, "the ship was never hit");
        w.Step(100);
        ticks = (int)w.Tick;
        return w;
    }

    [Fact]
    public void Replay_and_save_roundtrip_with_damage()
    {
        var w = DamagedStarterWorld(out _);
        var net = w.Network;
        int ship = net.ShipStructure(0);
        Assert.True(net.StructureHpFraction(ship) < 1000 || net.ModulesLost > 0);

        // A clone hashes the same, damage included.
        Assert.Equal(w.Checksum(), net.Clone().Checksum());

        // Replaying the command log reproduces the damaged state.
        var replay = SimWorld.Replay(w.Log, w.Tick, SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true, rules: TestContent.LegacyShip));
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();

        // And so does a save file.
        var bytes = new MemoryStream();
        SaveGame.Write(bytes, w, TestContent.Core, GameMode.Sandbox, Seed, threats: true, player: 0, structures: true, rules: TestContent.LegacyShip);
        var loaded = SaveGame.Read(new MemoryStream(bytes.ToArray()), TestContent.Core, out _);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal(net.StructureHpFraction(ship), loaded.Network.StructureHpFraction(ship));
        loaded.ValidateInvariants();

        // Damage changes the hash.
        var other = net.Clone();
        var first = other.Structures().First(s => s.Id == ship).Modules.Keys.First();
        other.HitModule(ship, first.Mx, first.My, first.Mz, 1);
        Assert.NotEqual(net.Checksum(), other.Checksum());
    }
}
