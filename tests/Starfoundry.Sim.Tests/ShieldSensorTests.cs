using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Shield modules (damage absorption), sensors (automatic reveal, next wave data) and their power use (ADR 0024).</summary>
public class ShieldSensorTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private const ulong Seed = 20261121UL;

    private static SimWorld World(ContentDb? content = null, bool threats = false) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, Seed, threats, stationRules: false, structures: true);

    /// <summary>A powered ship (cockpit 0, engine 1, workshop 2, cargo 3, reactor 4 along x) plus the given extra modules at x = 5, 6, ...;
    /// one step has run so the power state is real.</summary>
    private static int Ship(SimWorld w, int shields = 0, int sensors = 0, int hydrogen = 6)
    {
        int s = PowerTests.Ship(w, hydrogen: hydrogen);
        int x = 5;
        for (int i = 0; i < shields; i++) w.Network.AddStructureModuleUnchecked(s, x++, 0, 0, ModuleKind.Shield);
        for (int i = 0; i < sensors; i++) w.Network.AddStructureModuleUnchecked(s, x++, 0, 0, ModuleKind.Sensor);
        w.Step();
        return s;
    }

    // ---- shields -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 70)]
    [InlineData(2, 60)]
    [InlineData(3, 50)]
    [InlineData(4, 40)]
    [InlineData(5, 40)] // capped at 60 percent
    [InlineData(6, 40)]
    public void Shields_absorb_thirty_percent_plus_ten_per_extra_shield_up_to_sixty(int shields, int taken)
    {
        var w = World();
        int s = Ship(w, shields: shields);
        int hp = w.Network.ModuleHp(s, 0, 0, 0);
        w.Network.HitModule(s, 0, 0, 0, 100);
        Assert.Equal(hp - taken, w.Network.ModuleHp(s, 0, 0, 0));
        w.ValidateInvariants();
    }

    [Fact]
    public void Without_a_shield_the_full_damage_lands()
    {
        var w = World();
        int s = Ship(w);
        w.Network.HitModule(s, 0, 0, 0, 100);
        Assert.Equal(w.Network.ModuleMaxHp(ModuleKind.Cockpit) - 100, w.Network.ModuleHp(s, 0, 0, 0));
    }

    [Fact]
    public void A_lone_shield_takes_full_damage_but_two_protect_each_other()
    {
        var w = World();
        int s = Ship(w, shields: 1);
        w.Network.HitModule(s, 5, 0, 0, 100);
        Assert.Equal(250 - 100, w.Network.ModuleHp(s, 5, 0, 0));

        var w2 = World();
        int s2 = Ship(w2, shields: 2);
        w2.Network.HitModule(s2, 5, 0, 0, 100);
        Assert.Equal(250 - 70, w2.Network.ModuleHp(s2, 5, 0, 0)); // the other shield absorbs 30 percent
    }

    [Fact]
    public void A_hit_never_drops_below_one_damage_and_rounds_the_absorption_down()
    {
        var w = World();
        int s = Ship(w, shields: 1);
        w.Network.HitModule(s, 0, 0, 0, 1);
        Assert.Equal(149, w.Network.ModuleHp(s, 0, 0, 0)); // 30 percent of 1 is 0: the hit lands whole
        w.Network.HitModule(s, 0, 0, 0, 4);
        Assert.Equal(149 - 3, w.Network.ModuleHp(s, 0, 0, 0)); // a scout hit: 4 - 1
    }

    [Fact]
    public void A_shield_does_nothing_without_power()
    {
        var w = World(TestContent.CoreWithoutEmergencyPower);
        int s = Ship(w, shields: 1, hydrogen: 0);
        Assert.Equal(0, w.Network.PowerStatus(s).SatisfactionPermille);
        Assert.Equal(0, w.Network.ShieldAbsorbPercent(s));
        w.Network.HitModule(s, 0, 0, 0, 100);
        Assert.Equal(50, w.Network.ModuleHp(s, 0, 0, 0));
    }

    [Fact]
    public void A_shield_still_works_at_half_power_and_stops_below_it()
    {
        // Shield 25 against emergency power 15: satisfaction 600, above the half-power line.
        var w = World();
        int s = Ship(w, shields: 1, hydrogen: 0);
        Assert.Equal(600, w.Network.PowerStatus(s).SatisfactionPermille);
        Assert.Equal(30, w.Network.ShieldAbsorbPercent(s));

        // Four shields (100) against 15: satisfaction 150, below it.
        var w2 = World();
        int s2 = Ship(w2, shields: 4, hydrogen: 0);
        Assert.Equal(150, w2.Network.PowerStatus(s2).SatisfactionPermille);
        Assert.Equal(0, w2.Network.ShieldAbsorbPercent(s2));
        w2.Network.HitModule(s2, 0, 0, 0, 100);
        Assert.Equal(50, w2.Network.ModuleHp(s2, 0, 0, 0));
    }

    [Fact]
    public void Shields_and_sensors_draw_their_power_use()
    {
        var w = World();
        int s = Ship(w, shields: 2, sensors: 1);
        Assert.Equal(25 * 2 + 10, w.Network.PowerStatus(s).Demand);
        Assert.Equal(1000, w.Network.PowerStatus(s).SatisfactionPermille);
    }

    [Fact]
    public void A_shield_trims_every_enemy_hit_in_a_real_wave()
    {
        // Two shields cover each other; a scout (4), striker (12) and brute (30) hit the other modules for 30 percent less, rounded down.
        var (w, s) = SensorShipAtHome(C, sensor: false);
        var n = w.Network;
        n.AddStructureModuleUnchecked(s, 4, 0, 0, ModuleKind.Shield);
        n.AddStructureModuleUnchecked(s, 5, 0, 0, ModuleKind.Shield);
        w.Step();
        n.ScheduleWave(0);
        var seen = new List<ModuleHitEvent>();
        for (int i = 0; i < 12_000 && seen.Count < 12 && n.GetStructure(s).Modules.Count > 0; i++)
        {
            w.Step();
            seen.AddRange(n.ModuleHits);
        }
        Assert.True(seen.Count >= 12, "the wave never hit the ship");
        var enemyDamage = new[] { 4, 12, 30 };
        foreach (var hit in seen)
        {
            // Every module here has another shield beside it, so every hit lands for 70 percent of the enemy damage (at least 1).
            Assert.Contains(enemyDamage.Select(d => Math.Max(1, d - d * 30 / 100)), taken => taken == hit.Damage);
        }
    }

    // ---- sensors -------------------------------------------------------------------------------------

    private static int Hidden(SimWorld w, int cellsFromOrigin) =>
        w.Network.AddDeposit(w.Network.Content!.ItemIndex("core:iron_ore"), 100, cellsFromOrigin, 0, 0, revealed: false);

    [Fact]
    public void A_sensor_reveals_deposits_in_range_and_not_outside_it()
    {
        var w = World();
        int s = Ship(w, sensors: 1); // the sensor is module 5: its centre is 44 cells along x
        int near = Hidden(w, 60), edge = Hidden(w, 44 + 119), far = Hidden(w, 44 + 125), behind = Hidden(w, -70), distant = Hidden(w, 600);
        Assert.False(w.Network.IsDepositRevealed(near));
        w.Step(40);
        Assert.True(w.Network.IsDepositRevealed(near));
        Assert.True(w.Network.IsDepositRevealed(edge));
        Assert.True(w.Network.IsDepositRevealed(behind)); // 114 cells behind the sensor
        Assert.False(w.Network.IsDepositRevealed(far));
        Assert.False(w.Network.IsDepositRevealed(distant));
        w.ValidateInvariants();
    }

    [Fact]
    public void Nothing_is_revealed_without_a_sensor_or_without_power()
    {
        var w = World();
        Ship(w);
        int d = Hidden(w, 60);
        w.Step(100);
        Assert.False(w.Network.IsDepositRevealed(d));

        var dark = World(TestContent.CoreWithoutEmergencyPower);
        int s = Ship(dark, sensors: 1, hydrogen: 0);
        int d2 = Hidden(dark, 60);
        dark.Step(100);
        Assert.False(dark.Network.SensorActive(s));
        Assert.False(dark.Network.IsDepositRevealed(d2));
    }

    [Fact]
    public void Sensor_reveals_survive_a_deep_copy()
    {
        var w = World();
        int s = Ship(w, sensors: 1);
        Hidden(w, 60); Hidden(w, 300);
        w.Step(100);
        var copy = w.Network.Clone();
        Assert.Equal(w.Network.Checksum(), copy.Checksum());
        w.Step(100); copy.Step(100);
        Assert.Equal(w.Network.Checksum(), copy.Checksum());
        Assert.True(s > 0);
    }

    // ---- next wave data ------------------------------------------------------------------------------

    /// <summary>The core content with enemies that never get to shoot, so several waves can be watched.</summary>
    private static ContentDb Harmless()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        root.Remove("structure_templates");
        foreach (var e in root["enemies"]!.AsArray()) { e!["interval_ticks"] = 600; e["damage"] = 1; }
        // Mix the wave table so several types and the from_wave rule show up in the first waves.
        root["waves"] = JsonNode.Parse("""
            [{"enemy":"core:scout","base":2,"per_wave":1},
             {"enemy":"core:striker","base":0,"per_wave":1,"from_wave":1},
             {"enemy":"core:brute","base":1,"per_wave":0,"from_wave":2}]
            """);
        return ContentDb.FromJson(root.ToJsonString());
    }

    private static (SimWorld W, int Ship) SensorShipAtHome(ContentDb c, bool sensor = true, int hydrogen = 20)
    {
        var w = World(c, threats: true);
        var n = w.Network;
        var home = n.Galaxy!.Systems[0];
        n.EnsurePlayer(0);
        int s = n.CreateStructure(0, StructureKind.Ship, home.X * (long)Units.CellUnits, home.Y * (long)Units.CellUnits, home.Z * (long)Units.CellUnits);
        n.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        n.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Engine);
        n.AddStructureModuleUnchecked(s, 2, 0, 0, ModuleKind.Reactor);
        n.AddStructureModuleUnchecked(s, 3, 0, 0, ModuleKind.Cargo);
        if (sensor) n.AddStructureModuleUnchecked(s, 4, 0, 0, ModuleKind.Sensor);
        if (hydrogen > 0) n.GiveItems(0, c.ItemIndex("core:hydrogen"), hydrogen);
        w.Step();
        return (w, s);
    }

    [Fact]
    public void Next_wave_data_is_exact_once_the_warning_runs_and_matches_what_spawns()
    {
        var c = Harmless();
        var (w, s) = SensorShipAtHome(c);
        var n = w.Network;
        int scout = c.Enemies.ToList().FindIndex(e => e.Id == "core:scout"), striker = c.Enemies.ToList().FindIndex(e => e.Id == "core:striker"),
            brute = c.Enemies.ToList().FindIndex(e => e.Id == "core:brute");

        // Wave table: scouts 2 + 1 per wave; strikers 1 per wave from index 1; one brute from index 2.
        var expected = new[]
        {
            new[] { new WaveEnemyCount(scout, 2) },
            new[] { new WaveEnemyCount(scout, 3) },
            new[] { new WaveEnemyCount(scout, 4), new WaveEnemyCount(striker, 1), new WaveEnemyCount(brute, 1) },
        };
        int spawnedBefore = 0;
        for (int wave = 1; wave <= 3; wave++)
        {
            var before = n.NextWaveInfo(s);
            Assert.NotNull(before);
            Assert.Equal(wave, before!.Value.Wave);
            n.ScheduleWave(0);
            w.Step(100);
            var info = n.NextWaveInfo(s)!.Value;
            Assert.True(info.WarningActive);
            int warning = n.GetSystemThreat(0).WarningRemaining;
            Assert.Equal(warning, info.TicksToArrival);
            Assert.Equal(before.Value.Enemies, info.Enemies);
            Assert.Equal(expected[wave - 1], info.Enemies.ToArray());

            var idsBefore = n.Enemies().Select(e => e.Id).ToHashSet();
            w.Step(warning + 2);
            Assert.Equal(wave, (int)n.WavesSpawned);
            var fresh = n.Enemies().Where(e => !idsBefore.Contains(e.Id)).GroupBy(e => e.Type).OrderBy(g => g.Key)
                .Select(g => new WaveEnemyCount(g.Key, g.Count())).ToArray();
            Assert.Equal(info.Enemies.ToArray(), fresh);
            Assert.Equal(info.TotalEnemies, (int)n.EnemiesSpawned - spawnedBefore);
            spawnedBefore = (int)n.EnemiesSpawned;
        }
        Assert.Equal(4, n.NextWaveInfo(s)!.Value.Wave);
    }

    [Fact]
    public void Next_wave_data_needs_a_powered_sensor_and_threats()
    {
        var c = Harmless();
        var (w, s) = SensorShipAtHome(c, sensor: false);
        Assert.Null(w.Network.NextWaveInfo(s));

        var (lit, ls) = SensorShipAtHome(c);
        Assert.NotNull(lit.Network.NextWaveInfo(ls)); // fuelled: works

        var (dark, ds) = SensorShipAtHome(TestContent.CoreWithoutEmergencyPower, hydrogen: 0);
        Assert.False(dark.Network.SensorActive(ds));
        Assert.Null(dark.Network.NextWaveInfo(ds));

        var quiet = World(threats: false);
        int qs = Ship(quiet, sensors: 1);
        Assert.Null(quiet.Network.NextWaveInfo(qs));
    }

    [Fact]
    public void Next_wave_estimate_before_the_warning_is_unknown_for_a_quiet_system_and_finite_for_a_loud_one()
    {
        var c = Harmless();
        var (w, s) = SensorShipAtHome(c);
        var n = w.Network;
        var quiet = n.NextWaveInfo(s)!.Value;
        Assert.False(quiet.WarningActive);
        Assert.Equal(-1, quiet.TicksToArrival); // nothing in the system makes noise yet
        Assert.Equal(1, quiet.Wave);

        // Machines make the system loud: the estimate becomes a number that shrinks as the alert grows.
        for (int i = 0; i < 6; i++) n.AddMachine(i, 2, 0, c.MachineIndex("core:research_lab"), -1, owner: 0, structure: s);
        w.Step(10);
        var loud = n.NextWaveInfo(s)!.Value;
        Assert.True(loud.TicksToArrival > 0);
        w.Step(300);
        Assert.True(n.NextWaveInfo(s)!.Value.TicksToArrival < loud.TicksToArrival);
    }
}
