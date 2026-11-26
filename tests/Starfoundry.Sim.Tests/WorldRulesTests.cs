using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>World rules (ADR 0023): defaults, presets, validation, the header block and what each rule changes in the simulation.</summary>
public class WorldRulesTests
{
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;

    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");

    private static SimWorld World(WorldRules? rules, bool threats = true, GameMode mode = GameMode.Sandbox, bool structures = true, ContentDb? content = null) =>
        SimWorld.CreateGalaxy(content ?? C, mode, Seed, threats, stationRules: false, structures: structures, rules: rules);

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static void Ok(SimWorld w, Command c)
    {
        var r = Run(w, c);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
    }

    // ---- values, presets, validation ----------------------------------------------------------------

    [Fact]
    public void Defaults_equal_the_game_as_it_was()
    {
        var d = new WorldRules();
        Assert.Equal((100, 100, 100, 100, -1, 100, 100, true, false, 10),
            (d.EnemyDamagePercent, d.EnemyHpPercent, d.WaveSizePercent, d.WaveIntervalPercent, d.FirstWaveGraceSeconds, d.DepositRichnessPercent,
                d.PowerOutputPercent, d.ThreatsEnabled, d.FreeBuild, d.GoalWaves));
        Assert.Empty(d.StartingItems);
        Assert.True(d.IsDefault);
        Assert.Equal(WorldRules.Default, WorldRules.Normal);
        Assert.Null(d.Validate());
    }

    [Fact]
    public void Preset_values_are_fixed()
    {
        static (int, int, int, int, int, int, int, int) V(WorldRules r) => (r.EnemyDamagePercent, r.EnemyHpPercent, r.WaveSizePercent,
            r.WaveIntervalPercent, r.FirstWaveGraceSeconds, r.DepositRichnessPercent, r.PowerOutputPercent, r.GoalWaves);
        Assert.Equal((70, 80, 70, 130, 600, 150, 110, 8), V(WorldRules.Relaxed));
        Assert.Equal((100, 100, 100, 100, -1, 100, 100, 10), V(WorldRules.Normal));
        Assert.Equal((120, 120, 120, 80, 300, 80, 100, 13), V(WorldRules.Hard));
        Assert.Equal((170, 170, 170, 65, 120, 60, 90, 15), V(WorldRules.Brutal));
        foreach (var (id, rules) in WorldRules.Presets)
        {
            Assert.Null(rules.Validate());
            Assert.Equal(id, rules.PresetId());
            Assert.Equal(rules, WorldRules.Preset(id.ToUpperInvariant()));
            Assert.Empty(rules.StartingItems);
            Assert.True(rules.ThreatsEnabled);
            Assert.False(rules.FreeBuild);
        }
        Assert.Equal(new[] { "relaxed", "normal", "hard", "brutal" }, WorldRules.Presets.Select(p => p.Id));
        Assert.Null(WorldRules.Preset("nightmare"));
        Assert.Null(new WorldRules { GoalWaves = 5 }.PresetId());
    }

    [Fact]
    public void Validation_and_clamping()
    {
        Assert.NotNull(new WorldRules { EnemyDamagePercent = 24 }.Validate());
        Assert.NotNull(new WorldRules { EnemyHpPercent = 301 }.Validate());
        Assert.NotNull(new WorldRules { WaveSizePercent = 0 }.Validate());
        Assert.NotNull(new WorldRules { WaveIntervalPercent = 1000 }.Validate());
        Assert.NotNull(new WorldRules { PowerOutputPercent = 10 }.Validate());
        Assert.NotNull(new WorldRules { DepositRichnessPercent = 24 }.Validate());
        Assert.NotNull(new WorldRules { DepositRichnessPercent = 401 }.Validate());
        Assert.NotNull(new WorldRules { FirstWaveGraceSeconds = 3601 }.Validate());
        Assert.NotNull(new WorldRules { FirstWaveGraceSeconds = -2 }.Validate());
        Assert.NotNull(new WorldRules { GoalWaves = 0 }.Validate());
        Assert.Null(new WorldRules { EnemyDamagePercent = 25, EnemyHpPercent = 300, DepositRichnessPercent = 400, FirstWaveGraceSeconds = 0 }.Validate());
        Assert.NotNull(new WorldRules { StartingItems = new[] { new ItemStack(Ore, 0) } }.Validate());
        Assert.NotNull(new WorldRules { StartingItems = new[] { new ItemStack(9999, 1) } }.Validate(C));
        Assert.Null(new WorldRules { StartingItems = new[] { new ItemStack(Ore, 5) } }.Validate(C));

        var wild = new WorldRules
        {
            EnemyDamagePercent = 5, EnemyHpPercent = 9999, WaveSizePercent = -4, WaveIntervalPercent = 301, FirstWaveGraceSeconds = 99999,
            DepositRichnessPercent = 1, PowerOutputPercent = 400, GoalWaves = 500, FreeBuild = true, ThreatsEnabled = false,
            StartingItems = new[] { new ItemStack(Ore, 5), new ItemStack(-1, 3), new ItemStack(Ingot, 0), new ItemStack(Ingot, 20000) },
        };
        var c = wild.Clamped(C);
        Assert.Null(c.Validate(C));
        Assert.Equal((25, 300, 25, 300, 3600, 25, 300, 30), (c.EnemyDamagePercent, c.EnemyHpPercent, c.WaveSizePercent, c.WaveIntervalPercent,
            c.FirstWaveGraceSeconds, c.DepositRichnessPercent, c.PowerOutputPercent, c.GoalWaves));
        Assert.True(c.FreeBuild);
        Assert.False(c.ThreatsEnabled);
        Assert.Equal(new[] { new ItemStack(Ore, 5), new ItemStack(Ingot, 9999) }, c.StartingItems);
        Assert.Equal(-1, new WorldRules { FirstWaveGraceSeconds = -50 }.Clamped().FirstWaveGraceSeconds);

        // The world refuses invalid rules instead of running them.
        Assert.Throws<ArgumentException>(() => World(wild));
        Assert.Throws<ArgumentException>(() => new BeltNetwork(C, rules: wild));
    }

    [Fact]
    public void Scaling_rounds_half_up_with_a_floor_of_one()
    {
        Assert.Equal(7, WorldRules.Scale(5, 130)); // 6.5
        Assert.Equal(1, WorldRules.Scale(1, 70)); // 0.7
        Assert.Equal(0, WorldRules.Scale(1, 40));
        Assert.Equal(1, WorldRules.ScaleMin1(1, 40));
        Assert.Equal(0, WorldRules.ScaleMin1(0, 300));
        Assert.Equal(3, WorldRules.ScaleMin1(2, 170)); // 3.4
        Assert.Equal(WorldRules.Scale(12345, 100), 12345);
    }

    [Fact]
    public void Equality_ignores_nothing_and_compares_stacks_by_value()
    {
        var a = new WorldRules { StartingItems = new[] { new ItemStack(Ore, 5) } };
        var b = new WorldRules { StartingItems = new[] { new ItemStack(Ore, 5) } };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);
        Assert.NotEqual(a, WorldRules.Default);
        Assert.NotEqual(a, new WorldRules { StartingItems = new[] { new ItemStack(Ore, 6) } });
        Assert.NotEqual(WorldRules.Default, new WorldRules { FreeBuild = true });
        Assert.NotEqual(WorldRules.Default, new WorldRules { ThreatsEnabled = false });
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Serialization_roundtrips_and_rejects_bad_data()
    {
        var rules = new WorldRules
        {
            EnemyDamagePercent = 111, EnemyHpPercent = 122, WaveSizePercent = 133, WaveIntervalPercent = 144, FirstWaveGraceSeconds = 155,
            DepositRichnessPercent = 166, PowerOutputPercent = 177, GoalWaves = 18, FreeBuild = true, ThreatsEnabled = false,
            StartingItems = new[] { new ItemStack(Ore, 12), new ItemStack(Ingot, 7) },
        };
        foreach (var r in new[] { rules, WorldRules.Default, WorldRules.Hard, WorldRules.Brutal })
        {
            var back = WorldRules.Deserialize(r.Serialize());
            Assert.Equal(r, back);
            Assert.Equal(r.Serialize(), back.Serialize());
        }
        // The form is stable: a fixed rule set always gives the same bytes.
        Assert.Equal(1 + 8 * 4 + 1 + 1 + 4 + 4, WorldRules.Default.Serialize().Length); // the density (140) makes it the second format: a flags byte and the density follow the first flags byte
        Assert.Equal(2, WorldRules.Default.Serialize()[0]);
        Assert.Equal(1 + 8 * 4 + 1 + 4, new WorldRules { ResourceDensityPercent = 100 }.Serialize().Length); // a density of 100 keeps the first format (the bytes before ADR 0036)
        Assert.Equal(1, new WorldRules { ResourceDensityPercent = 100 }.Serialize()[0]);

        byte[] bytes = rules.Serialize();
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(bytes.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(Array.Empty<byte>()));
        var badVersion = (byte[])bytes.Clone(); badVersion[0] = 9;
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(badVersion));
        var badValue = (byte[])bytes.Clone(); badValue[1] = 1; badValue[2] = badValue[3] = badValue[4] = 0; // enemy damage 1
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(badValue));
        // Every flag bit is used now (bit 6 legacy collisions, bit 7 legacy flight, ADR 0033): any flags byte reads back.
        var both = new WorldRules().WithLegacyCollisions(true).WithLegacyFlight(true);
        Assert.Equal(both, WorldRules.Deserialize(both.Serialize()));
        Assert.Equal(64 | 128, both.Serialize()[1 + 8 * 4] & (64 | 128));
    }

    // ---- defaults change nothing ----------------------------------------------------------------------

    [Fact]
    public void Default_rules_play_and_hash_exactly_like_no_rules()
    {
        var none = World(null);
        var normal = World(WorldRules.Normal);
        Ship(none, 3, 20); Ship(normal, 3, 20);
        none.Network.ScheduleWave(0); normal.Network.ScheduleWave(0);
        none.Step(Tps * 120); normal.Step(Tps * 120);
        Assert.True(none.Network.WavesSpawned > 0);
        Assert.Equal(none.Checksum(), normal.Checksum());
        Assert.Equal(none.Network.EnemiesSpawned, normal.Network.EnemiesSpawned);

        // Rules that differ hash differently, even before anything has happened.
        Assert.NotEqual(World(null).Checksum(), World(new WorldRules { GoalWaves = 12 }).Checksum());
        Assert.NotEqual(World(null).Checksum(), World(new WorldRules { FreeBuild = true }).Checksum());
    }

    private static int Ship(SimWorld w, int reactors = 1, int hydrogen = 0)
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

    // ---- waves --------------------------------------------------------------------------------------------

    private static int ExpectedEnemies(ContentDb content, int waveIndex, int sizePercent)
    {
        int total = 0;
        foreach (var spec in content.Waves)
        {
            int b = waveIndex < spec.FromWave ? 0 : spec.Base + spec.PerWave * (waveIndex - spec.FromWave);
            total += b < 1 ? 0 : Math.Max(1, (b * sizePercent + 50) / 100);
        }
        return total;
    }

    private static SimWorld AfterWaves(WorldRules? rules, int waves)
    {
        var w = World(rules);
        for (int i = 0; i < waves; i++)
        {
            w.Network.ScheduleWave(0);
            w.Step(C.Threat!.WarningTicks + 2);
        }
        Assert.Equal(waves, w.Network.WavesSpawned);
        return w;
    }

    [Fact]
    public void Wave_size_scales_the_enemy_counts()
    {
        foreach (int size in new[] { 70, 100, 130, 170 })
        {
            var w = AfterWaves(new WorldRules { WaveSizePercent = size }, 4);
            int expected = Enumerable.Range(0, 4).Sum(i => ExpectedEnemies(C, i, size));
            Assert.Equal(expected, w.Network.EnemiesSpawned);
            Assert.Equal(expected, w.Network.EnemyCount);
        }
        var normal = AfterWaves(WorldRules.Normal, 4);
        var hard = AfterWaves(WorldRules.Hard, 4);
        Assert.True(hard.Network.EnemiesSpawned > normal.Network.EnemiesSpawned);
        Assert.True(AfterWaves(WorldRules.Relaxed, 4).Network.EnemiesSpawned < normal.Network.EnemiesSpawned);

        // The first wave of the content has scouts only (base 2), 25 percent still leaves one of a type that has any.
        var tiny = AfterWaves(new WorldRules { WaveSizePercent = 25 }, 1);
        Assert.Equal(1, tiny.Network.EnemiesSpawned);
        w_valid(tiny);
    }

    private static void w_valid(SimWorld w) => w.ValidateInvariants();

    [Fact]
    public void Enemy_hp_scales_at_spawn()
    {
        var normal = AfterWaves(WorldRules.Normal, 4).Network.Enemies();
        var hard = AfterWaves(WorldRules.Hard, 4);
        var relaxed = AfterWaves(WorldRules.Relaxed, 4);
        foreach (var e in normal) Assert.Equal(C.Enemies[e.Type].Hp, e.Hp);
        foreach (var e in hard.Network.Enemies()) Assert.Equal(WorldRules.ScaleMin1(C.Enemies[e.Type].Hp, 120), e.Hp);
        foreach (var e in relaxed.Network.Enemies()) Assert.Equal(WorldRules.ScaleMin1(C.Enemies[e.Type].Hp, 80), e.Hp);
        Assert.Contains(hard.Network.Enemies(), e => e.Hp > C.Enemies[e.Type].Hp);
        hard.ValidateInvariants();
        relaxed.ValidateInvariants();
    }

    /// <summary>The damage of the first hit a wave lands on an unpowered ship's cockpit.</summary>
    private static int FirstHitDamage(WorldRules rules)
    {
        var w = World(rules);
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 60 * Tps * 5; i++)
        {
            w.Step();
            if (w.Network.ModuleHits.Count > 0) return w.Network.ModuleHits[0].Damage;
        }
        throw new Xunit.Sdk.XunitException("the wave never hit the ship");
    }

    [Fact]
    public void Enemy_damage_scales_every_hit()
    {
        int scout = C.Enemies[C.Waves[0].Enemy].Damage;
        Assert.Equal(scout, FirstHitDamage(WorldRules.Normal));
        Assert.Equal(WorldRules.ScaleMin1(scout, 130), FirstHitDamage(WorldRules.Hard));
        Assert.Equal(WorldRules.ScaleMin1(scout, 170), FirstHitDamage(WorldRules.Brutal));
        Assert.Equal(WorldRules.ScaleMin1(scout, 70), FirstHitDamage(WorldRules.Relaxed));
        Assert.True(FirstHitDamage(WorldRules.Hard) > FirstHitDamage(WorldRules.Normal));
        Assert.True(FirstHitDamage(WorldRules.Relaxed) < FirstHitDamage(WorldRules.Normal));
    }

    /// <summary>Ticks until the first warning of a system loud enough to fill its alert meter, with ten smelters on a ship.</summary>
    private static long TicksToFirstWarning(WorldRules rules, out SystemThreatInfo info)
    {
        var w = World(rules);
        int s = Ship(w);
        for (int i = 0; i < 10; i++) w.Network.AddMachine(i % 8, 5 + i / 8, 0, Smelter, -1, owner: 0, structure: s);
        Assert.True(w.Network.SystemSignature(0) > C.Threat!.SignatureThreshold);
        for (long t = 0; t < 400_000; t++)
        {
            w.Step();
            info = w.Network.GetSystemThreat(0);
            if (info.WarningRemaining >= 0) return w.Tick;
        }
        info = w.Network.GetSystemThreat(0);
        return -1;
    }

    [Fact]
    public void Wave_interval_scales_the_alert_meter()
    {
        var none = new WorldRules { FirstWaveGraceSeconds = 0 };
        long normal = TicksToFirstWarning(none, out var nInfo);
        long slow = TicksToFirstWarning(new WorldRules { FirstWaveGraceSeconds = 0, WaveIntervalPercent = 200 }, out var sInfo);
        long fast = TicksToFirstWarning(new WorldRules { FirstWaveGraceSeconds = 0, WaveIntervalPercent = 50 }, out var fInfo);
        Assert.True(normal > 0 && slow > 0 && fast > 0);
        Assert.Equal(C.Threat!.AlertMax, nInfo.AlertMax);
        Assert.Equal(C.Threat.AlertMax * 2, sInfo.AlertMax);
        Assert.Equal(C.Threat.AlertMax / 2, fInfo.AlertMax);
        Assert.InRange(slow, normal * 2 - 2, normal * 2 + 2);
        Assert.InRange(fast, normal / 2 - 2, normal / 2 + 2);
    }

    [Fact]
    public void Wave_interval_scales_the_time_between_waves_as_a_whole()
    {
        // After a wave the meter restarts at the scaled "after wave" value, so the next wave is also stretched.
        var rules = new WorldRules { FirstWaveGraceSeconds = 0, WaveIntervalPercent = 200 };
        var w = World(rules);
        w.Network.ScheduleWave(0);
        w.Step(C.Threat!.WarningTicks + 2);
        Assert.Equal(1, w.Network.WavesSpawned);
        Assert.Equal(C.Threat.AlertAfterWave * 2, w.Network.GetSystemThreat(0).Alert);
        w.ValidateInvariants();
    }

    [Fact]
    public void First_wave_grace_overrides_the_content_value()
    {
        long noGrace = TicksToFirstWarning(new WorldRules { FirstWaveGraceSeconds = 0 }, out _);
        long grace = TicksToFirstWarning(new WorldRules { FirstWaveGraceSeconds = 600 }, out _);
        long content = TicksToFirstWarning(WorldRules.Normal, out _); // the content's 480 s
        Assert.True(noGrace > 0 && noGrace < 480 * Tps, "the alert alone fills before the content's grace ends");
        // The grace counts from when the system first got loud and the alert fills inside it, so the warning waits for the grace to end.
        Assert.InRange(grace, 600 * Tps, 600 * Tps + 3);
        Assert.InRange(content, C.Threat!.FirstWaveGraceTicks, C.Threat.FirstWaveGraceTicks + 3);
        Assert.NotEqual(content, grace);
        Assert.True(grace > content);
    }

    [Fact]
    public void Threats_disabled_in_the_rules_means_no_waves()
    {
        var off = World(new WorldRules { ThreatsEnabled = false });
        Assert.False(off.Network.ThreatActive);
        Assert.Throws<InvalidOperationException>(() => off.Network.ScheduleWave(0));
        int s = Ship(off);
        for (int i = 0; i < 10; i++) off.Network.AddMachine(i % 8, 5 + i / 8, 0, Smelter, -1, owner: 0, structure: s);
        off.Step(Tps * 900);
        Assert.Equal(0, off.Network.WavesSpawned);
        Assert.Equal(0, off.Network.EnemyCount);
        off.Network.ValidateInvariants();

        // The world's own bool still wins: rules cannot switch threats on.
        var bool_off = World(new WorldRules(), threats: false);
        Assert.False(bool_off.Network.ThreatActive);
        Assert.True(World(new WorldRules()).Network.ThreatActive);

        // It behaves exactly like the old bool: the same commands and ticks give the same state apart from the rules themselves.
        var legacy = World(null, threats: false);
        Ship(legacy);
        legacy.Step(Tps * 5);
        var viaRules = World(new WorldRules { ThreatsEnabled = false });
        Ship(viaRules);
        viaRules.Step(Tps * 5);
        Assert.Equal(legacy.Network.WavesSpawned, viaRules.Network.WavesSpawned);
        Assert.Equal(legacy.Network.EnemyCount, viaRules.Network.EnemyCount);
    }

    // ---- economy and power ----------------------------------------------------------------------------------

    [Fact]
    public void Deposit_richness_scales_generated_deposits_only()
    {
        var normal = World(null, content: TestContent.Core);
        var rich = World(new WorldRules { DepositRichnessPercent = 200 }, content: TestContent.Core);
        var poor = World(new WorldRules { DepositRichnessPercent = 25 }, content: TestContent.Core);
        Assert.Equal(normal.Network.DepositCount, rich.Network.DepositCount);
        Assert.True(normal.Network.DepositCount > 5);
        for (int d = 0; d < normal.Network.DepositCount; d++)
        {
            int baseAmount = normal.Network.DepositInitial(d);
            Assert.Equal(baseAmount * 2, rich.Network.DepositInitial(d));
            Assert.Equal(baseAmount * 2, rich.Network.DepositRemaining(d));
            Assert.Equal(Math.Max(1, (baseAmount * 25 + 50) / 100), poor.Network.DepositInitial(d));
        }
        // The starter ship and its hold are not deposits: they are the same.
        foreach (int item in Enumerable.Range(0, TestContent.Core.Items.Count))
            Assert.Equal(normal.Network.HoldCount(0, item), rich.Network.HoldCount(0, item));
        Assert.Equal(normal.Network.Structures().Sum(s => s.Modules.Count), rich.Network.Structures().Sum(s => s.Modules.Count));
        rich.ValidateInvariants();
        poor.ValidateInvariants();
    }

    private static PowerStatus Powered(WorldRules rules, int loadUnits)
    {
        var w = World(rules, threats: false);
        int s = Ship(w, reactors: 1, hydrogen: 3);
        for (int i = 0; i < loadUnits / 20; i++) w.Network.AddMachine(i, 6, 0, Lab, -1, owner: 0, structure: s);
        w.Step();
        return w.Network.PowerStatus(s);
    }

    [Fact]
    public void Power_output_scales_reactor_production_and_satisfaction()
    {
        int output = C.Structures!.PowerOutput(ModuleKind.Reactor);
        Assert.Equal(100, output);
        var normal = Powered(WorldRules.Normal, 200);
        Assert.Equal((100, 200, 500), (normal.Production, normal.Demand, normal.SatisfactionPermille));
        var weak = Powered(new WorldRules { PowerOutputPercent = 50 }, 200);
        Assert.Equal((50, 200, 250), (weak.Production, weak.Demand, weak.SatisfactionPermille));
        var strong = Powered(new WorldRules { PowerOutputPercent = 200 }, 200);
        Assert.Equal((200, 200, 1000), (strong.Production, strong.Demand, strong.SatisfactionPermille));
        Assert.Equal(110, Powered(WorldRules.Relaxed, 0).Production);
        Assert.Equal(90, Powered(WorldRules.Brutal, 0).Production);
        Assert.True(weak.Burning);
    }

    [Fact]
    public void Starting_items_fill_the_hold_and_clamp_at_capacity()
    {
        var rules = new WorldRules { StartingItems = new[] { new ItemStack(Ore, 30), new ItemStack(Ingot, 20) } };
        var w = World(rules, threats: false, structures: false);
        Assert.Equal(30, w.Network.HoldCount(0, Ore));
        Assert.Equal(20, w.Network.HoldCount(0, Ingot));
        Assert.Equal(0, w.Network.StartingItemsClamped);

        // The legacy hold takes 60: the second stack is cut to what fits and the rest is counted.
        var big = new WorldRules { StartingItems = new[] { new ItemStack(Ore, 30), new ItemStack(Ingot, 40), new ItemStack(Hydrogen, 5) } };
        var c = World(big, threats: false, structures: false);
        Assert.Equal(30, c.Network.HoldCount(0, Ore));
        Assert.Equal(30, c.Network.HoldCount(0, Ingot));
        Assert.Equal(0, c.Network.HoldCount(0, Hydrogen));
        Assert.Equal(60, c.Network.HoldTotal(0));
        Assert.Equal(15, c.Network.StartingItemsClamped);
        c.ValidateInvariants();

        // With the starter ship the hold is the ship's cargo.
        var ship = World(new WorldRules { StartingItems = new[] { new ItemStack(Ore, 9999) } }, threats: false, content: TestContent.Core);
        Assert.True(ship.Network.HoldCount(0, Ore) > 0);
        Assert.Equal(9999, ship.Network.HoldCount(0, Ore) + ship.Network.StartingItemsClamped);

        Assert.Throws<ArgumentException>(() => World(new WorldRules { StartingItems = new[] { new ItemStack(9999, 1) } }));
    }

    // ---- free build -------------------------------------------------------------------------------------------

    private static int ShipStructure(SimWorld w) => w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);

    [Fact]
    public void Free_build_costs_nothing_but_keeps_the_placement_rules()
    {
        var w = World(new WorldRules { FreeBuild = true }, threats: false, mode: GameMode.Campaign, structures: false);
        w.Network.EnsurePlayer(0);
        int s = ShipStructure(w);
        Assert.Equal(0, w.Network.HoldTotal(0));
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull));
        Ok(w, new PlaceMachineIn(s, 1, 1, 1, Smelter, -1));
        Ok(w, new PlaceSegmentIn(s, 0, 0, 0, Dir.PosX, 4, BeltTier.Mk1));
        Assert.Equal(0, w.Network.HoldTotal(0));

        // Placement rules still apply.
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModuleIn(s, 5, 0, 0, ModuleKind.Hull)).Reject);
        Assert.Equal(Reject.Overlap, Run(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull)).Reject);
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 9, 1, 1, Smelter, -1)).Reject); // hull
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachineIn(s, 100, 1, 1, Smelter, -1)).Reject);
        Assert.Equal(Reject.Overlap, Run(w, new PlaceMachineIn(s, 1, 1, 1, Smelter, -1)).Reject);
        int far = w.Network.CreateStructure(0, StructureKind.Station, 1_000_000, 0, 0);
        Assert.Equal(Reject.OutOfRange, Run(w, new PlaceModuleIn(far, 0, 0, 0, ModuleKind.Core)).Reject);

        // Removing refunds nothing: free building must not make items.
        Ok(w, new RemoveModuleIn(s, 1, 0, 0));
        Assert.Equal(0, w.Network.HoldTotal(0));
        w.ValidateInvariants();

        // The same actions without free build are refused for lack of items.
        var paid = World(null, threats: false, mode: GameMode.Campaign, structures: false);
        paid.Network.EnsurePlayer(0);
        int ps = ShipStructure(paid);
        Assert.Equal(Reject.NotEnoughItems, Run(paid, new PlaceModuleIn(ps, 0, 0, 0, ModuleKind.Workshop)).Reject);
    }

    [Fact]
    public void Free_build_founds_stations_and_repairs_for_nothing()
    {
        var w = World(new WorldRules { FreeBuild = true }, threats: false, mode: GameMode.Campaign, content: TestContent.Core);
        w.Network.EnsurePlayer(0);
        var coreCost = TestContent.Core.Structures!.ModuleCost(ModuleKind.Core);
        int[] before = coreCost.Select(c => w.Network.HoldCount(0, c.Item)).ToArray();
        Ok(w, new FoundStation(12, 0, 0));
        Assert.Equal(before, coreCost.Select(c => w.Network.HoldCount(0, c.Item)).ToArray());
        Assert.Equal(1, w.Network.Structures().Count(s => s.Kind == StructureKind.Station));
        w.ValidateInvariants();

        // Without free build the same founding is refused by the empty hold, as before.
        var paid = World(null, threats: false, mode: GameMode.Campaign, content: TestContent.Core);
        paid.Network.EnsurePlayer(0);
        Assert.Equal(Reject.NotEnoughItems, Run(paid, new FoundStation(12, 0, 0)).Reject);
    }

    // ---- header, save and replay ------------------------------------------------------------------------------

    private static byte[] LogBytes(IReadOnlyList<LoggedCommand> log, WorldRules? rules, bool structures = true)
    {
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Campaign, Seed, threats: true, stationRules: false, structures: structures, rules: rules);
        return ms.ToArray();
    }

    [Fact]
    public void Header_version_7_carries_non_default_rules_only()
    {
        var log = new List<LoggedCommand> { new(0, 0, new SetMovement(1, 0, 0)) };
        byte[] plain = LogBytes(log, null);
        byte[] normal = LogBytes(log, WorldRules.Normal);
        Assert.Equal(plain, normal); // default rules write no block
        Assert.Equal(20, plain[4]); // version 20 added the workbench queue and the station-first rules (ADR 0039); 19 added drop and pick-up commands (ADR 0038); 18 added the personal inventory (a legacy flag, ADR 0035); the rules block is unchanged for every other rule set
        Assert.Equal(0, plain[4 + 1 + 8 + 1 + 8] & 8);

        byte[] hard = LogBytes(log, WorldRules.Hard);
        Assert.Equal(8, hard[4 + 1 + 8 + 1 + 8] & 8);
        Assert.True(hard.Length > plain.Length);

        foreach (var rules in new[] { WorldRules.Hard, WorldRules.Relaxed, WorldRules.Brutal, new WorldRules { FreeBuild = true, StartingItems = new[] { new ItemStack(Ore, 4) } } })
        {
            var back = CommandCodec.ReadLog(new MemoryStream(LogBytes(log, rules)), out ulong hash, out var mode, out ulong seed, out bool threats,
                out bool station, out bool structures, out var read);
            Assert.Equal(rules, read);
            Assert.Equal((C.Hash, GameMode.Campaign, Seed, true, false, true), (hash, mode, seed, threats, station, structures));
            Assert.Single(back);
        }
        CommandCodec.ReadLog(new MemoryStream(plain), out _, out _, out _, out _, out _, out _, out var none);
        Assert.True(none.IsDefault);

        // A damaged rules block or an unknown flag is refused.
        var cut = hard.Take(4 + 1 + 8 + 1 + 8 + 1 + 4 + 5).ToArray();
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(cut), out _, out _, out _, out _, out _, out _, out _));
        var unknown = (byte[])plain.Clone(); unknown[4 + 1 + 8 + 1 + 8] |= 32; // bit 4 (16) is the scenario flag since version 8
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(unknown), out _, out _, out _, out _, out _, out _, out _));
        var badLength = (byte[])hard.Clone(); badLength[4 + 1 + 8 + 1 + 8 + 1 + 3] = 0x7f;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(badLength), out _, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void Versions_5_and_6_still_read_as_default_rules()
    {
        foreach (byte version in new byte[] { 5, 6 })
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("SFCL"));
                w.Write(version);
                w.Write(9UL); w.Write((byte)1); w.Write(3UL);
                w.Write((byte)1);
                w.Write(0);
            }
            ms.Position = 0;
            CommandCodec.ReadLog(ms, out ulong hash, out var mode, out ulong seed, out bool threats, out _, out _, out var rules);
            Assert.Equal((9UL, GameMode.Campaign, 3UL, true), (hash, mode, seed, threats));
            Assert.True(rules.LegacyLaser); // recorded before the laser drew power (ADR 0027)
            Assert.True(rules.LegacyGalaxy); // and before the wide galaxy (ADR 0028)
            Assert.True(rules.LegacyHardpoints); // and before the hardpoint modules (ADR 0032)
            Assert.True(rules.WithoutLegacyFlags().IsDefault);
        }
        // Older versions cannot carry the rules flag.
        using var bad = new MemoryStream();
        using (var w = new BinaryWriter(bad, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("SFCL"));
            w.Write((byte)6);
            w.Write(9UL); w.Write((byte)0); w.Write(3UL); w.Write((byte)8); w.Write(0);
        }
        bad.Position = 0;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(bad, out _, out _, out _, out _, out _, out _, out _));
    }

    private static SimWorld Played(WorldRules rules, out ContentDb content)
    {
        content = TestContent.Core;
        var w = SimWorld.CreateGalaxy(content, GameMode.Campaign, Seed, threats: true, stationRules: false, structures: true, rules: rules);
        w.Network.EnsurePlayer(0);
        w.Submit(0, new SetThrust(PlayerRules.ThrustScale, 0, 0));
        w.Step(300);
        w.Submit(0, new SetThrust(0, 0, 0));
        w.Step(300);
        return w;
    }

    [Fact]
    public void Saves_and_replays_reproduce_non_default_rules()
    {
        var rules = new WorldRules
        {
            EnemyDamagePercent = 130, WaveSizePercent = 170, DepositRichnessPercent = 250, PowerOutputPercent = 120, FirstWaveGraceSeconds = 90,
            StartingItems = new[] { new ItemStack(Ore, 12) }, FreeBuild = true, GoalWaves = 7,
        };
        var w = Played(rules, out var content);
        using var ms = new MemoryStream();
        SaveGame.Write(ms, w, content, GameMode.Campaign, Seed, threats: true, player: 0, structures: true, rules: rules);

        ms.Position = 0;
        var info = SaveGame.Peek(ms);
        Assert.Equal(rules, info.Rules);
        Assert.Equal(w.Tick, info.Ticks);

        ms.Position = 0;
        var loaded = SaveGame.Read(ms, content, out var readInfo);
        Assert.Equal(rules, readInfo.Rules);
        Assert.Equal(rules, loaded.Network.Rules);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal(w.Network.HoldCount(0, Ore), loaded.Network.HoldCount(0, Ore));
        loaded.ValidateInvariants();

        // Rules that do not match the world make the replay diverge, which the save check catches.
        using var other = new MemoryStream();
        SaveGame.Write(other, w, content, GameMode.Campaign, Seed, threats: true, player: 0, structures: true, rules: WorldRules.Hard);
        other.Position = 0;
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(other, content, out _));

        // A default-rules save reads back with default rules.
        var plain = Played(WorldRules.Normal, out _);
        using var p = new MemoryStream();
        SaveGame.Write(p, plain, content, GameMode.Campaign, Seed, threats: true, player: 0, structures: true);
        p.Position = 0;
        SaveGame.Read(p, content, out var pInfo);
        Assert.True(pInfo.Rules!.IsDefault);

        // Replay from the log and the header rules.
        var replay = SimWorld.Replay(w.Log, w.Tick, initial: SimWorld.CreateGalaxy(content, GameMode.Campaign, Seed, true, false, true, rules));
        Assert.Equal(w.Checksum(), replay.Checksum());
        var wrong = SimWorld.Replay(w.Log, w.Tick, initial: SimWorld.CreateGalaxy(content, GameMode.Campaign, Seed, true, false, true));
        Assert.NotEqual(w.Checksum(), wrong.Checksum());
    }

    [Fact]
    public void Clone_keeps_the_rules()
    {
        var w = World(WorldRules.Hard);
        var clone = w.Network.Clone();
        Assert.Equal(WorldRules.Hard, clone.Rules);
        Assert.Equal(w.Network.Checksum(), clone.Checksum());
    }
}
