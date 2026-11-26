using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0027: the ship's mining laser is a simulation rule: a trigger (FireLaser), a power draw, research that raises its rate,
/// and old logs that replay as they were recorded.</summary>
public class MiningLaserTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;

    private static SimWorld World(ContentDb? content = null, WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: rules);

    private static int HomeIron(SimWorld w) =>
        Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == Ore && w.Network.IsDepositRevealed(d));

    /// <summary>A ship with a reactor, parked on the home iron deposit, with fuel for a long run. Returns (ship, deposit).</summary>
    private static (int Ship, int Deposit) ParkedShip(SimWorld w, int reactors = 1, int hydrogen = 20)
    {
        int ship = PowerTests.Ship(w, reactors, hydrogen);
        int dep = HomeIron(w);
        var (x, y, z) = w.Network.DepositPosition(dep);
        w.Network.SetStructureOrigin(ship, x * (long)Cell, y * (long)Cell, z * (long)Cell);
        return (ship, dep);
    }

    private static void Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        Assert.True(w.LastResults.Single().Ok, $"{c} was rejected: {w.LastResults.Single().Reject}");
    }

    private static int Mined(SimWorld w) => w.Network.HoldCount(0, Ore);

    // ---- the trigger -----------------------------------------------------------------------------

    [Fact]
    public void Fire_laser_needs_a_mining_order()
    {
        var w = World();
        ParkedShip(w);
        w.Submit(0, new FireLaser(true));
        w.Step();
        Assert.Equal(Reject.InvalidArgument, w.LastResults.Single().Reject);
        Assert.False(w.Network.LaserFiring(0));
    }

    [Fact]
    public void Releasing_the_trigger_stops_mining_keeps_the_order_and_the_partial_item()
    {
        var w = World();
        var (_, dep) = ParkedShip(w);
        Ok(w, new StartMining(dep));
        w.Step(10); // 11 ticks of progress out of 15
        Assert.True(w.Network.LaserFiring(0));
        Assert.Equal(0, Mined(w));
        Ok(w, new FireLaser(false));
        Assert.False(w.Network.LaserFiring(0));
        Assert.Equal(dep, w.Network.MiningDeposit(0));
        w.Step(300);
        Assert.Equal(0, Mined(w)); // nothing is mined while the trigger is released
        Ok(w, new FireLaser(true));
        w.Step(4); // the partial progress was kept: the item arrives after 4 more ticks, not 15
        Assert.Equal(1, Mined(w));
        w.ValidateInvariants();
    }

    [Fact]
    public void Starting_and_stopping_reset_the_trigger()
    {
        var w = World();
        var (_, dep) = ParkedShip(w);
        Ok(w, new StartMining(dep));
        Ok(w, new FireLaser(false));
        Ok(w, new StartMining(dep)); // starting to mine pulls the trigger, as it always did
        Assert.True(w.Network.LaserFiring(0));
        Ok(w, new FireLaser(false));
        Ok(w, new StopMining());
        Assert.Equal(-1, w.Network.MiningDeposit(0));
        Assert.False(w.Network.LaserFiring(0));
        w.ValidateInvariants(); // no laser state without an order
    }

    [Fact]
    public void The_trigger_is_state_that_replays_and_clones()
    {
        var w = World();
        var (_, dep) = ParkedShip(w);
        Ok(w, new StartMining(dep));
        w.Step(20);
        Ok(w, new FireLaser(false));
        w.Step(40);
        Ok(w, new FireLaser(true));
        w.Step(100);
        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        var replay = SimWorld.Replay(w.Log, w.Tick, ParkedWorld());
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }

    private static SimWorld ParkedWorld()
    {
        var w = World();
        ParkedShip(w);
        return w;
    }

    [Fact]
    public void Trigger_state_is_mixed_only_when_set()
    {
        // A release and a pull inside one tick leave the state as it was, so it hashes as if neither had happened...
        var a = World();
        var (_, dep) = ParkedShip(a);
        Ok(a, new StartMining(dep));
        a.Step(50);

        var b = World();
        ParkedShip(b);
        Ok(b, new StartMining(dep));
        b.Submit(0, new Batch(new Command[] { new FireLaser(false), new FireLaser(true) }));
        b.Step(50);
        Assert.Equal(a.Checksum(), b.Checksum());

        // ...while a released trigger is a different state, and so is a partial credit under low power (see the power tests).
        var c = World();
        ParkedShip(c);
        Ok(c, new StartMining(dep));
        c.Submit(0, new FireLaser(false));
        c.Step(50);
        Assert.NotEqual(a.Checksum(), c.Checksum());
    }

    // ---- the command log -----------------------------------------------------------------------------

    [Fact]
    public void Fire_laser_roundtrips_in_the_codec_and_the_log_is_version_9()
    {
        foreach (bool firing in new[] { true, false })
            Assert.Equal(new FireLaser(firing), CommandCodec.FromBytes(CommandCodec.ToBytes(new FireLaser(firing))));
        var batch = new Batch(new Command[] { new StartMining(3), new FireLaser(false) });
        Assert.Equal(batch.Commands, ((Batch)CommandCodec.FromBytes(CommandCodec.ToBytes(batch))).Commands);

        var log = new List<LoggedCommand> { new(0, 0, new StartMining(0)), new(5, 0, new FireLaser(false)), new(9, 0, new Batch(new Command[] { new FireLaser(true) })) };
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(23, bytes[4]);
        var back = CommandCodec.ReadLog(new MemoryStream(bytes));
        Assert.Equal(3, back.Count);
        Assert.Equal(log[1].Command, back[1].Command);
        Assert.Equal(new FireLaser(true), ((Batch)back[2].Command).Commands.Single());
    }

    [Fact]
    public void An_older_log_reads_with_the_legacy_laser_and_cannot_hold_fire_laser()
    {
        var plain = new List<LoggedCommand> { new(0, 0, new StartMining(0)), new(5, 0, new StopMining()) };
        foreach (byte version in new byte[] { 5, 6, 7, 8 })
        {
            var ms = new MemoryStream();
            CommandCodec.WriteLog(ms, plain, C.Hash, GameMode.Sandbox, Seed, structures: version >= 6);
            byte[] bytes = ms.ToArray();
            bytes[4] = version; // the layouts of versions 5 to 9 only differ in what the header may carry
            CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyLaser, $"version {version}");
            Assert.True(rules.LegacyGalaxy, $"version {version}");
            Assert.True(rules.LegacyHardpoints, $"version {version}");
            Assert.True(rules.WithoutLegacyFlags().IsDefault);
        }
        var current = new MemoryStream();
        CommandCodec.WriteLog(current, plain, C.Hash, GameMode.Sandbox, Seed, structures: true);
        CommandCodec.ReadLog(new MemoryStream(current.ToArray()), out _, out _, out _, out _, out _, out _, out var now);
        Assert.False(now.LegacyLaser);
        Assert.True(now.IsDefault);

        var withFire = new MemoryStream();
        CommandCodec.WriteLog(withFire, new List<LoggedCommand> { new(0, 0, new FireLaser(false)) }, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] old = withFire.ToArray();
        old[4] = 8;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(old)));
    }

    [Fact]
    public void A_version_8_log_replays_as_recorded_without_the_power_draw()
    {
        // Recorded the way the previous version did it: no laser draw, hashed as default rules.
        var legacy = new WorldRules().WithLegacyLaser(true).WithLegacyGalaxy(true).WithLegacyHardpoints(true); // a version 8 log reads with all the legacy flags
        var old = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: legacy);
        int dep = HomeIron(old);
        var (x, y, z) = old.Network.DepositPosition(dep);
        old.Network.SetStructureOrigin(old.Network.ShipStructure(0), x * (long)Cell, y * (long)Cell, z * (long)Cell);
        old.Submit(0, new StartMining(dep));
        old.Step(600);
        Assert.Equal(28, old.Network.PowerStatus(old.Network.ShipStructure(0)).Demand); // the starter ship's own machines, no laser
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, old.Log, TestContent.Core.Hash, GameMode.Sandbox, Seed, structures: true, rules: null);
        byte[] bytes = ms.ToArray();
        bytes[4] = 8;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.True(rules.LegacyLaser);

        // The replay is built from the rules the header gave, and reproduces the old state exactly.
        var initial = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: rules);
        initial.Network.SetStructureOrigin(initial.Network.ShipStructure(0), x * (long)Cell, y * (long)Cell, z * (long)Cell);
        var replay = SimWorld.Replay(log, 600, initial);
        Assert.Equal(old.Checksum(), replay.Checksum());

        // A new world draws power for the same commands, so it is a different state.
        var fresh = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        int depFresh = HomeIron(fresh); // the wide galaxy puts the deposit somewhere else
        var (fx, fy, fz) = fresh.Network.DepositPosition(depFresh);
        fresh.Network.SetStructureOrigin(fresh.Network.ShipStructure(0), fx * (long)Cell, fy * (long)Cell, fz * (long)Cell);
        fresh.Submit(0, new StartMining(depFresh));
        fresh.Step(600);
        Assert.Equal(old.Network.HoldCount(0, Ore), fresh.Network.HoldCount(0, Ore)); // the starter ship has power to spare: same ore
        Assert.NotEqual(old.Checksum(), fresh.Checksum());
        Assert.Equal(28 + PlayerRules.LaserPowerUse, fresh.Network.PowerStatus(fresh.Network.ShipStructure(0)).Demand);
    }

    [Fact]
    public void Legacy_laser_flag_roundtrips_and_is_not_part_of_the_presets()
    {
        var legacy = new WorldRules().WithLegacyLaser(true);
        Assert.NotEqual(WorldRules.Default, legacy);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.False(legacy.WithLegacyLaser(false).LegacyLaser);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyLaser));
        Assert.True(legacy.Clamped().LegacyLaser);
        // Idle, a legacy world hashes as a default one: the flag only changes what happens when the laser fires.
        Assert.Equal(World().Checksum(), World(rules: legacy).Checksum());
    }

    // ---- power -------------------------------------------------------------------------------------

    [Fact]
    public void The_laser_draws_power_only_while_it_would_mine()
    {
        var w = World();
        var (ship, dep) = ParkedShip(w);
        int baseDemand = 0;
        w.Step();
        Assert.Equal(baseDemand, w.Network.PowerStatus(ship).Demand);

        Ok(w, new StartMining(dep));
        w.Step();
        Assert.Equal(baseDemand + PlayerRules.LaserPowerUse, w.Network.PowerStatus(ship).Demand);
        Assert.Equal(PowerRules.FullSatisfaction, w.Network.PowerStatus(ship).SatisfactionPermille); // 100 produced, 5 asked

        Ok(w, new FireLaser(false));
        w.Step();
        Assert.Equal(baseDemand, w.Network.PowerStatus(ship).Demand);
        Ok(w, new FireLaser(true));
        w.Step();
        Assert.Equal(baseDemand + PlayerRules.LaserPowerUse, w.Network.PowerStatus(ship).Demand);

        // Out of range: the order stays but nothing fires.
        w.Network.SetStructureOrigin(ship, 5000L * Cell, 0, 0);
        w.Step();
        Assert.Equal(baseDemand, w.Network.PowerStatus(ship).Demand);
    }

    [Fact]
    public void A_full_hold_stops_the_draw()
    {
        var w = World(rules: new WorldRules().WithLegacyFeel(true));
        var (ship, dep) = ParkedShip(w);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_plate"), w.Network.HoldCapacity(0)); // past the mining limit
        Ok(w, new StartMining(dep));
        w.Step();
        Assert.Equal(0, w.Network.PowerStatus(ship).Demand);
    }

    [Fact]
    public void A_ship_without_a_reactor_or_with_the_legacy_laser_draws_nothing()
    {
        var w = World();
        var (ship, dep) = ParkedShip(w, reactors: 0); // hand-built ships need no power model
        Ok(w, new StartMining(dep));
        w.Step(30);
        var p = w.Network.PowerStatus(ship);
        Assert.Equal((0, PowerRules.FullSatisfaction), (p.Demand, p.SatisfactionPermille));
        Assert.Equal(2, Mined(w));

        var old = World(rules: new WorldRules().WithLegacyLaser(true));
        var (oldShip, oldDep) = ParkedShip(old);
        Ok(old, new StartMining(oldDep));
        old.Step(30);
        Assert.Equal(0, old.Network.PowerStatus(oldShip).Demand);
    }

    private static void Load(SimWorld w, int ship, int units)
    {
        for (int i = 0; i < units / 20; i++) w.Network.AddMachine(i, 6, 0, Lab, -1, owner: 0, structure: ship);
    }

    [Fact]
    public void A_brownout_slows_the_machines_not_the_laser()
    {
        var full = World();
        var (_, depF) = ParkedShip(full);
        Ok(full, new StartMining(depF));
        full.Step(599);

        // 180 asked by the labs plus 5 by the laser from 100 produced: the laser is served first, the machines get 95 of 180.
        var weak = World();
        var (ship, dep) = ParkedShip(weak);
        Load(weak, ship, 180);
        Ok(weak, new StartMining(dep));
        weak.Step(599);
        var p = weak.Network.PowerStatus(ship);
        Assert.Equal((100, 185), (p.Production, p.Demand));
        Assert.Equal(527, p.SatisfactionPermille); // (100 - 5) * 1000 / 180

        Assert.Equal(40, Mined(full)); // 600 ticks, 15 per item
        Assert.Equal(40, Mined(weak)); // the same: a starved base can always mine its way out
        weak.Network.ValidateInvariants();
    }

    [Fact]
    public void A_laser_that_gets_less_power_than_it_asks_slows_in_proportion()
    {
        // A reactor that gives only 3 when it has no fuel (the content's emergency output is 15): the laser asks for 5.
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root.Remove("structure_templates");
        root["structures"]!["reactor"]!["emergency_power"] = 3;
        var content = ContentDb.FromJson(root.ToJsonString());
        var w = World(content);
        var (ship, dep) = ParkedShip(w, hydrogen: 0);
        Ok(w, new StartMining(dep));
        w.Step(599);
        var p = w.Network.PowerStatus(ship);
        Assert.Equal((3, PlayerRules.LaserPowerUse), (p.Production, p.Demand));
        Assert.Equal(24, Mined(w)); // 600 ticks at 600 per mille: 360 working ticks, 15 per item
        w.Network.ValidateInvariants();
        // The partial credit is state: it differs from the same world with a full-power laser, and clones and replays keep it.
        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        var replay = SimWorld.Replay(w.Log, w.Tick, WorldWith(content));
        Assert.Equal(w.Checksum(), replay.Checksum());
    }

    private static SimWorld WorldWith(ContentDb content)
    {
        var w = World(content);
        ParkedShip(w, hydrogen: 0);
        return w;
    }

    [Fact]
    public void Mining_stops_with_no_power_at_all()
    {
        var w = World(TestContent.CoreWithoutEmergencyPower);
        var (ship, dep) = ParkedShip(w, hydrogen: 0); // a reactor with no fuel and no emergency output makes nothing
        w.Step();
        Assert.Equal(0, w.Network.PowerStatus(ship).SatisfactionPermille);
        Ok(w, new StartMining(dep));
        w.Step(300);
        Assert.Equal(0, Mined(w));
        Assert.Equal(dep, w.Network.MiningDeposit(0)); // the order stands, it just has no power
        w.ValidateInvariants();
        // Fuel brings it back.
        w.Network.GiveItems(0, Hydrogen, 3);
        w.Step(300);
        Assert.True(Mined(w) > 0);
    }

    // ---- rate and research ---------------------------------------------------------------------------

    [Fact]
    public void Without_research_the_rate_is_exactly_what_it_was()
    {
        var w = World();
        for (int d = 0; d < w.Network.DepositCount; d++)
            Assert.Equal(DepositRules.Scale(PlayerRules.MineTicksPerItem, w.Network.DepositDensity(d)), w.Network.MineTicksFor(d));
    }

    [Fact]
    public void Mining_rate_research_shortens_the_ticks_and_density_still_scales_it()
    {
        var w = World();
        var (_, dep) = ParkedShip(w);
        int n = w.Network.DepositCount;
        Assert.Equal(15, w.Network.MineTicksFor(dep)); // density 100

        w.Network.GrantResearch(C.ResearchIndex("core:laser_focusing"));
        Assert.Equal(25, w.Network.BonusPercent(BonusKind.MiningRate));
        Assert.Equal(12, w.Network.MineTicksFor(dep)); // 15 * 100 / 125

        w.Network.GrantResearch(C.ResearchIndex("core:beam_collimator"));
        Assert.Equal(65, w.Network.BonusPercent(BonusKind.MiningRate));
        Assert.Equal(9, w.Network.MineTicksFor(dep)); // 15 * 100 / 165, rounded down

        for (int d = 0; d < n; d++)
        {
            long expected = Math.Max(1, 15L * 10_000 / ((long)w.Network.DepositDensity(d) * 165));
            Assert.Equal((int)expected, w.Network.MineTicksFor(d));
        }
    }

    [Fact]
    public void Researched_laser_mines_more_ore_in_the_same_time()
    {
        var plain = World();
        var (_, d0) = ParkedShip(plain);
        Ok(plain, new StartMining(d0));
        plain.Step(599);

        var tuned = World();
        var (_, d1) = ParkedShip(tuned);
        tuned.Network.GrantResearch(C.ResearchIndex("core:laser_focusing"));
        Ok(tuned, new StartMining(d1));
        tuned.Step(599);
        Assert.Equal(40, Mined(plain));
        Assert.Equal(50, Mined(tuned)); // 600 / 12
        tuned.ValidateInvariants();
    }

    [Fact]
    public void Research_that_finishes_while_mining_keeps_the_state_valid()
    {
        var w = World();
        var (_, dep) = ParkedShip(w);
        Ok(w, new StartMining(dep));
        w.Step(14); // one tick from an item
        w.Network.GrantResearch(C.ResearchIndex("core:beam_collimator")); // the limit drops below the progress
        w.ValidateInvariants();
        w.Step(2);
        Assert.Equal(1, Mined(w));
        w.ValidateInvariants();
    }

    [Fact]
    public void The_research_nodes_are_content_with_strings_in_both_languages()
    {
        var core = TestContent.Core;
        var focusing = core.Research[core.ResearchIndex("core:laser_focusing")];
        var collimator = core.Research[core.ResearchIndex("core:beam_collimator")];
        Assert.Equal(new[] { new Bonus(BonusKind.MiningRate, 25) }, focusing.Bonuses);
        Assert.Equal(new[] { new Bonus(BonusKind.MiningRate, 40) }, collimator.Bonuses);
        Assert.Contains(core.ResearchIndex("core:laser_focusing"), collimator.Requires);
        foreach (var key in new[] { "research.core:laser_focusing", "research.core:laser_focusing.desc", "research.core:beam_collimator",
            "research.core:beam_collimator.desc", "bonus.mining_rate" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
        var w = SimWorld.CreateGalaxy(core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        var entry = ResearchView.BuildByField(core, w.Network, 0).First(g => g.Field == ResearchField.Basic).Entries.Single(e => e.Id == "core:laser_focusing");
        Loc.Lang = Language.En;
        Assert.Contains("mining laser speed", string.Join(" ", entry.Unlocks.Select(ResearchView.UnlockText)));
        Loc.Lang = Language.Es;
        Assert.Contains("láser minero", string.Join(" ", entry.Unlocks.Select(ResearchView.UnlockText)));
        Loc.Lang = Language.En;
    }
}
