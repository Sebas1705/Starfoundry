using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Reactors, hydrogen burn, power satisfaction and what it scales (crafting, depots, engines, turrets).</summary>
public class PowerTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ammo = C.ItemIndex("core:ammo");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private const ulong Seed = 20261121UL;
    private const int Tps = Units.TicksPerSecond;

    private static SimWorld World(bool threats = false, bool emergency = true) =>
        SimWorld.CreateGalaxy(emergency ? C : TestContent.CoreWithoutEmergencyPower, GameMode.Sandbox, Seed, threats, stationRules: false, structures: true);

    /// <summary>A ship at the origin: cockpit, engine, workshop, cargo and the given number of reactors, with some hydrogen in the hold.</summary>
    internal static int Ship(SimWorld w, int reactors = 1, int hydrogen = 0)
    {
        var n = w.Network;
        n.EnsurePlayer(0);
        int s = n.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        n.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        n.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Engine);
        n.AddStructureModuleUnchecked(s, 2, 0, 0, ModuleKind.Workshop);
        n.AddStructureModuleUnchecked(s, 3, 0, 0, ModuleKind.Cargo);
        for (int i = 0; i < reactors; i++) n.AddStructureModuleUnchecked(s, 4 + i, 0, 0, ModuleKind.Reactor);
        if (hydrogen > 0) n.GiveItems(0, Hydrogen, hydrogen);
        return s;
    }

    /// <summary>Adds laboratories (20 power each) so the structure's demand grows by <paramref name="units"/> (a multiple of 20).</summary>
    private static void Load(BeltNetwork n, int s, int units)
    {
        for (int i = 0; i < units / 20; i++) n.AddMachine(i, 6, 0, Lab, -1, owner: 0, structure: s);
    }

    /// <summary>Source belt of <paramref name="item"/>, a machine and an output belt into a sink (or a depot), all at column x. Returns the machine.</summary>
    private static int Line(BeltNetwork n, int s, int x, int item, int type, int recipe, bool depot = false)
    {
        int input = n.AddSegment(x, 3, 0, 0, 0, 1, 3, BeltTier.Mk1, s);
        n.SetSource(input, new ushort[] { (ushort)item });
        int m = n.AddMachine(x, 3, 3, type, recipe, owner: 0, structure: s);
        n.ConnectToMachine(input, m);
        int output = n.AddSegment(x, 3, 4, 0, 0, 1, 3, BeltTier.Mk1, s);
        n.ConnectFromMachine(m, output);
        if (depot) n.ConnectToMachine(output, n.AddMachine(x, 3, 7, C.MachineIndex("core:depot"), -1, owner: 0, structure: s));
        else n.SetSink(output);
        return m;
    }

    [Fact]
    public void Reactor_without_fuel_yields_only_the_emergency_output()
    {
        var w = World();
        int s = Ship(w);
        Assert.Equal(15, C.Structures!.EmergencyPower(ModuleKind.Reactor));
        w.Network.AddMachine(0, 0, 0, Smelter, -1, owner: 0, structure: s);
        w.Step(10);
        var p = w.Network.PowerStatus(s);
        Assert.Equal(15, p.Production); // not burning: the emergency trickle
        Assert.False(p.Burning);
        Assert.Equal(0, p.BurnRemainingTicks);
        Assert.Equal(0, p.HydrogenInHold);
        Assert.Equal(10, p.Demand);
        Assert.Equal(1000, p.SatisfactionPermille);
        w.Network.ValidateInvariants();

        // With emergency power 0 in the content the old blackout is back.
        var dark = World(emergency: false);
        int d = Ship(dark);
        dark.Network.AddMachine(0, 0, 0, Smelter, -1, owner: 0, structure: d);
        dark.Step(10);
        Assert.Equal(0, dark.Network.PowerStatus(d).Production);
        Assert.Equal(0, dark.Network.PowerStatus(d).SatisfactionPermille);
    }

    [Fact]
    public void Zero_fuel_ship_is_not_dead()
    {
        // No hydrogen, no burning reactor: the smelter line and the engines still run, slowly.
        var w = World();
        int s = Ship(w);
        for (int i = 1; i < 4; i++) w.Network.AddMachine(i, 5, 0, Smelter, -1, owner: 0, structure: s); // 40 demand in all with the line below
        Line(w.Network, s, 0, Ore, Smelter, SmeltIron);
        w.Network.SetThrust(0, PlayerRules.ThrustScale, 0, 0);
        w.Step(1200);
        var p = w.Network.PowerStatus(s);
        Assert.Equal(40, p.Demand);
        Assert.Equal(15, p.Production);
        Assert.Equal(375, p.SatisfactionPermille); // 15 * 1000 / 40
        Assert.False(p.Burning);
        Assert.True(w.Network.MachineOutputsCreated >= 4, "the smelter crawls instead of stopping");
        long x = w.Network.GetPlayerInfo(0).X;
        Assert.InRange(x, 1200L * PlayerRules.MoveUnitsPerTick * 3 / 8 - 2, 1200L * PlayerRules.MoveUnitsPerTick * 3 / 8 + 2); // 37.5 percent of full speed
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Reactor_burns_one_hydrogen_per_interval()
    {
        var w = World();
        int s = Ship(w, hydrogen: 3);
        int interval = C.Structures!.BurnIntervalTicks(ModuleKind.Reactor);
        int output = C.Structures.PowerOutput(ModuleKind.Reactor);
        Assert.Equal((900, 100), (interval, output));

        w.Step();
        var p = w.Network.PowerStatus(s);
        Assert.True(p.Burning);
        Assert.Equal(output, p.Production);
        Assert.Equal(2, p.HydrogenInHold);
        Assert.Equal(interval - 1, p.BurnRemainingTicks);
        Assert.Equal(1000, p.SatisfactionPermille); // nothing to power: full

        w.Step(interval - 1);
        Assert.Equal(2, w.Network.HoldCount(0, Hydrogen)); // the first burn just ended; the next hydrogen is taken at the next tick
        w.Step();
        Assert.Equal(1, w.Network.HoldCount(0, Hydrogen));
        w.Step(2 * interval - 1);
        Assert.Equal(0, w.Network.HoldCount(0, Hydrogen));
        Assert.Equal(output, w.Network.PowerStatus(s).Production); // the last burn is still running
        w.Step();
        Assert.Equal(15, w.Network.PowerStatus(s).Production); // the emergency output, not a burning reactor
        Assert.False(w.Network.PowerStatus(s).Burning);
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Two_reactors_burn_separately_and_add_up()
    {
        var w = World();
        int s = Ship(w, reactors: 2, hydrogen: 4);
        w.Step();
        Assert.Equal(200, w.Network.PowerStatus(s).Production);
        Assert.Equal(2, w.Network.HoldCount(0, Hydrogen));
    }

    [Fact]
    public void Satisfaction_scales_crafting_speed()
    {
        // Ten smelters (100) plus five labs (100) against one reactor (100): satisfaction 500, one working tick in two.
        long Crafts(bool loaded, int ticks)
        {
            var w = World();
            int s = Ship(w, hydrogen: 5);
            for (int i = 1; i < 10; i++) w.Network.AddMachine(i, 5, 0, Smelter, -1, owner: 0, structure: s);
            if (loaded) Load(w.Network, s, 100);
            Line(w.Network, s, 0, Ore, Smelter, SmeltIron);
            w.Step(ticks);
            Assert.Equal(loaded ? 500 : 1000, w.Network.PowerStatus(s).SatisfactionPermille);
            Assert.Equal(loaded ? 200 : 100, w.Network.PowerStatus(s).Demand);
            w.Network.ValidateInvariants();
            return w.Network.MachineOutputsCreated;
        }

        // Compare at the same points of the craft cycle: 1200 ticks is 20 crafts at full power, 40 ticks of belt lead-in are the same for both.
        long full = Crafts(false, 1200), half = Crafts(true, 1200);
        Assert.InRange(full, 18, 20);
        Assert.InRange(half, full / 2 - 1, full / 2 + 1);
    }

    [Fact]
    public void Satisfaction_scales_engine_speed()
    {
        long Travelled(int extraLoad)
        {
            var w = World();
            int s = Ship(w, hydrogen: 5);
            // Demand from labs only: 5 labs (100) against 100 production is full; 10 labs (200) is half.
            Load(w.Network, s, extraLoad);
            w.Network.SetThrust(0, PlayerRules.ThrustScale, 0, 0);
            w.Step(1000);
            w.Network.ValidateInvariants();
            return w.Network.GetPlayerInfo(0).X;
        }

        long full = Travelled(100), half = Travelled(200), quarter = Travelled(400);
        Assert.Equal(1000L * PlayerRules.MoveUnitsPerTick, full); // full power: 48 units a tick (engine 100 percent)
        Assert.InRange(half, full / 2 - 1, full / 2 + 1);
        Assert.InRange(quarter, full / 4 - 1, full / 4 + 1);

        var w2 = World();
        int s2 = Ship(w2, hydrogen: 5);
        Load(w2.Network, s2, 200);
        w2.Step();
        Assert.Equal(PlayerRules.MoveUnitsPerTick / 2, w2.Network.ShipSpeedUnitsPerTick(0));
    }

    [Fact]
    public void Zero_power_stops_machines_and_engines()
    {
        // A full blackout needs content without the emergency output (see Zero_fuel_ship_is_not_dead for the shipped content).
        var w = World(emergency: false);
        int s = Ship(w); // a reactor but no hydrogen
        Line(w.Network, s, 0, Ore, Smelter, SmeltIron);
        w.Network.SetThrust(0, PlayerRules.ThrustScale, 0, 0);
        w.Step(600);
        Assert.Equal(0, w.Network.MachineOutputsCreated);
        Assert.Equal(0, w.Network.GetPlayerInfo(0).X); // SetThrust was accepted but the ship cannot accelerate
        Assert.Equal(0, w.Network.ShipSpeedUnitsPerTick(0));
        w.Network.ValidateInvariants();

        // Fuel brings both back.
        w.Network.GiveItems(0, Hydrogen, 2);
        w.Step(600);
        Assert.True(w.Network.MachineOutputsCreated > 5);
        Assert.True(w.Network.GetPlayerInfo(0).X > 20_000);

        // An engine-only ship with a reactor and no fuel is dead too, although it has nothing to power.
        var bare = World(emergency: false);
        int b = Ship(bare);
        Assert.Equal(0, bare.Network.PowerStatus(b).Demand);
        bare.Network.SetThrust(0, PlayerRules.ThrustScale, 0, 0);
        bare.Step(100);
        Assert.Equal(0, bare.Network.GetPlayerInfo(0).X);
    }

    [Fact]
    public void Structures_without_any_reactor_and_without_machines_still_fly()
    {
        var w = World();
        var n = w.Network;
        n.EnsurePlayer(0);
        int s = n.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        n.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        n.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Engine);
        n.SetThrust(0, PlayerRules.ThrustScale, 0, 0);
        w.Step(10);
        Assert.Equal(10 * PlayerRules.MoveUnitsPerTick, n.GetPlayerInfo(0).X);
        Assert.Equal(1000, n.PowerStatus(s).SatisfactionPermille);
    }

    [Fact]
    public void Depots_and_unloaders_only_work_on_credited_ticks()
    {
        // Hold -> ore port -> belt -> depot -> hold, the loop of a starter-style factory, with and without fuel.
        (long Delivered, long Spawned) Loop(int hydrogen)
        {
            var w = World(emergency: hydrogen > 0); // the no-fuel case needs a real blackout, not the emergency trickle
            int s = Ship(w, hydrogen: hydrogen);
            w.Network.GiveItems(0, Ore, 10);
            int port = w.Network.AddMachine(0, 3, 0, C.MachineIndex("core:ore_unloader"), -1, owner: 0, structure: s);
            int belt = w.Network.AddSegment(1, 3, 0, 1, 0, 0, 3, BeltTier.Mk1, s);
            w.Network.ConnectFromMachine(port, belt);
            w.Network.ConnectToMachine(belt, w.Network.AddMachine(4, 3, 0, C.MachineIndex("core:depot"), -1, owner: 0, structure: s));
            w.Step(600);
            w.Network.ValidateInvariants();
            return (w.Network.DepotDelivered, w.Network.Spawned);
        }

        Assert.Equal((0L, 0L), Loop(0)); // no fuel: the port and the depot do nothing
        var (delivered, spawned) = Loop(5);
        Assert.True(delivered > 0);
        Assert.True(spawned >= delivered && spawned > 5);
    }

    [Fact]
    public void Starter_ship_runs_at_full_power_with_starting_hydrogen()
    {
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, structures: true);
        var n = w.Network;
        Assert.Equal(20, n.HoldCount(0, TestContent.Core.FuelItem));
        w.Step();
        var p = n.ShipPower(0);
        Assert.Equal(100, p.Production);
        Assert.Equal(28, p.Demand); // ore port 2 + smelter 10 + assembler 15 + depot 1
        Assert.Equal(1000, p.SatisfactionPermille);
        Assert.True(p.Burning);
        Assert.Equal(19, p.HydrogenInHold);
        Assert.Equal(p, n.PowerStatus(n.ShipStructure(0)));
        w.Step(3000);
        Assert.Equal(1000, n.ShipPower(0).SatisfactionPermille);
        w.ValidateInvariants();
    }

    [Fact]
    public void Starter_ship_runs_out_of_hydrogen_after_the_expected_time()
    {
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, structures: true);
        var n = w.Network;
        int interval = TestContent.Core.Structures!.BurnIntervalTicks(ModuleKind.Reactor);
        long firstShort = -1;
        for (int t = 1; t <= 20 * interval + 100; t++)
        {
            w.Step();
            if (firstShort < 0 && n.ShipPower(0).SatisfactionPermille < 1000) firstShort = t;
        }
        Assert.Equal(20L * interval + 1, firstShort); // 20 hydrogen x 900 ticks = 18000 ticks = 10 minutes
        Assert.Equal(535, n.ShipPower(0).SatisfactionPermille); // the emergency 15 against a demand of 28
        Assert.Equal(0, n.HoldCount(0, TestContent.Core.FuelItem));
        Assert.Equal(10, 20 * interval / Tps / 60);
        w.ValidateInvariants();
    }

    [Fact]
    public void Turrets_need_power()
    {
        (bool Fired, int Ticks, bool Lost) Fired(int loadUnits)
        {
            var w = World(threats: true);
            int s = Ship(w, hydrogen: 40);
            Load(w.Network, s, loadUnits);
            w.Network.AddStructureModuleUnchecked(s, 0, 1, 0, ModuleKind.Defence);
            int turret = w.Network.AddMachine(1, 9, 1, Turret, -1, owner: 0, structure: s);
            int belt = w.Network.AddSegment(1, 9, 3, 0, 0, -1, 2, BeltTier.Mk1, s);
            w.Network.SetSource(belt, new ushort[] { (ushort)Ammo });
            w.Network.ConnectToMachine(belt, turret);
            w.Step(300);
            Assert.True(w.Network.MachineInCount(turret, Ammo) > 0);
            w.Network.ScheduleWave(0);
            int machines = w.Network.MachineCount;
            // Run until the first shot, or the first machine lost (that would change the demand), or the time is up.
            int t = 0;
            while (t < 6000 && w.Network.AmmoFired == 0 && w.Network.MachineCount == machines) { w.Step(); t++; }
            return (w.Network.AmmoFired > 0, t, w.Network.MachineCount < machines);
        }

        // Five labs plus the turret: 105 against 100, satisfaction 952, the turret fires once the enemies are in range.
        var (fired, shotTick, _) = Fired(100);
        Assert.True(fired, "a turret at 95 percent power should fire");

        // Eleven labs plus the turret: 225 against 100, satisfaction 444: no shot while the enemies have reached the same spot.
        var (firedLow, ticks, lost) = Fired(220);
        Assert.False(firedLow);
        Assert.True(ticks > shotTick || lost, "the test must run past the moment the powered turret fired");
        Assert.True(lost || ticks == 6000);
        if (lost) Assert.True(ticks > shotTick, $"a machine was lost at {ticks}, before the powered turret's first shot at {shotTick}: the test proves nothing");
    }

    [Fact]
    public void Legacy_worlds_unchanged_by_power()
    {
        // The same scenario hashed before power existed: a world without structure rules must hash the same forever.
        var c = ContentDb.FromJson(System.Text.RegularExpressions.Regex.Replace("""
        {"schema":1,
         "items":[{"id":"t:a"},{"id":"t:b"}],
         "machines":[{"id":"t:m"},{"id":"t:drill","role":"drill"},{"id":"t:dep","role":"depot"}],
         "recipes":[{"id":"t:r","machine":"t:m","inputs":[{"item":"t:a","count":1}],"outputs":[{"item":"t:b","count":1}],"time_s":1},
                    {"id":"t:dr","machine":"t:drill","inputs":[],"outputs":[{"item":"t:a","count":1}],"time_s":1}]}
        """, @"\s+", ""));
        var net = new BeltNetwork(c);
        int dep = net.AddDeposit(0, 500, 0, 0, 0);
        int drill = net.AddMachine(0, 0, 0, 1, 1, owner: 0);
        net.LinkDeposit(drill, dep);
        int m = net.AddMachine(5, 0, 0, 0, 0, owner: 0);
        int depot = net.AddMachine(10, 0, 0, 2, -1, owner: 0);
        int s1 = net.AddSegment(1, 0, 0, 1, 0, 0, 4, BeltTier.Mk2);
        int s2 = net.AddSegment(6, 0, 0, 1, 0, 0, 4, BeltTier.Mk2);
        net.ConnectFromMachine(drill, s1); net.ConnectToMachine(s1, m);
        net.ConnectFromMachine(m, s2); net.ConnectToMachine(s2, depot);
        net.Step(600);
        Assert.Equal(6617541375898655779UL, net.Checksum());
    }

    [Fact]
    public void Structures_without_the_rules_ignore_power_and_hydrogen()
    {
        // Structures exist but the structure rules are off: a reactor with nothing to burn stops nobody, and hydrogen is not touched.
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: false);
        var n = w.Network;
        n.EnsurePlayer(0);
        int s = n.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        n.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Reactor);
        n.GiveItems(0, Hydrogen, 5);
        Line(n, s, 0, Ore, Smelter, SmeltIron);
        w.Step(1200);
        Assert.Equal(5, n.HoldCount(0, Hydrogen));
        Assert.True(n.MachineOutputsCreated >= 18);
        Assert.Equal(1000, n.PowerStatus(s).SatisfactionPermille);
    }

    [Fact]
    public void Power_state_roundtrips_replay_and_save()
    {
        var content = TestContent.Core;
        SimWorld Fresh() => SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        int lab = content.MachineIndex("core:research_lab");
        int ship = 1;

        var a = Fresh();
        a.Submit(0, new SetThrust(600, 200, 0));
        // Six labs push the demand to 148: satisfaction 675, so credits carry over between ticks.
        for (int i = 0; i < 6; i++) a.Submit(0, new PlaceMachineIn(ship, i, 3, 13, lab, -1));
        a.Step();
        Assert.All(a.LastResults, r => Assert.True(r.Ok, r.Reject.ToString()));
        a.Step(1999);
        Assert.Equal(675, a.Network.ShipPower(0).SatisfactionPermille);

        // Save in the middle of a burn with a partial satisfaction.
        var bytes = new MemoryStream();
        SaveGame.Write(bytes, a, content, GameMode.Sandbox, Seed, threats: false, player: 0, structures: true);
        var loaded = SaveGame.Read(new MemoryStream(bytes.ToArray()), content, out _);
        Assert.Equal(a.Checksum(), loaded.Checksum());
        Assert.Equal(a.Network.ShipPower(0), loaded.Network.ShipPower(0));
        loaded.ValidateInvariants();

        // Run both past the end of the fuel: the checksums keep matching (credits, burns and thrust remainders included).
        a.Step(18_500);
        loaded.Step(18_500);
        Assert.Equal(a.Checksum(), loaded.Checksum());
        Assert.Equal(101, a.Network.ShipPower(0).SatisfactionPermille); // the emergency 15 against a demand of 148

        // A clone and a replay of the log agree too.
        Assert.Equal(a.Checksum(), a.Network.Clone().Checksum());
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, a.Log, content.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out _, out _, out _, out _, out _);
        var b = SimWorld.Replay(log, a.Tick, Fresh());
        Assert.Equal(a.Checksum(), b.Checksum());
        b.ValidateInvariants();
    }

    [Fact]
    public void A_removed_reactor_module_forgets_its_burn()
    {
        var w = World();
        int s = Ship(w, hydrogen: 2);
        w.Step(10);
        Assert.True(w.Network.PowerStatus(s).Burning);
        w.Network.RemoveStructureModuleUnchecked(s, 4, 0, 0);
        w.Network.ValidateInvariants();
        w.Step();
        Assert.Equal(0, w.Network.PowerStatus(s).Production);
        Assert.Equal(1000, w.Network.PowerStatus(s).SatisfactionPermille); // no reactor, nothing to power
    }
}
