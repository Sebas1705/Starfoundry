using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0042: the antenna variants (scanner and navigation reach, stacking with diminishing returns) and the heavy cannon (slow, strong, big recoil).</summary>
public class AntennaCannonTests
{
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb Shipped = TestContent.CoreFloor;

    private static JsonObject Root() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();

    /// <summary>The shipped content without its starter ship, so a test builds the ship it needs.</summary>
    private static readonly ContentDb NoShip = ContentDb.FromJson(Without(Root()));

    private static string Without(JsonObject root) { root.Remove("structure_templates"); return root.ToJsonString(); }

    /// <summary>The shipped content whose starter ship carries the heavy cannon in place of its gun.</summary>
    private static ContentDb HeavyStarter()
    {
        var root = Root();
        foreach (var template in root["structure_templates"]!.AsObject())
            foreach (var m in template.Value!["modules"]!.AsArray())
                if (m!["kind"]!.GetValue<string>() == "gun") m["kind"] = "gun_heavy";
        return ContentDb.FromJson(root.ToJsonString());
    }

    private static SimWorld World(ContentDb content, WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: rules);

    /// <summary>A powered ship with the given antennas beside its reactor (x = 5, 6, ...), one step run so the power state is real.</summary>
    private static SimWorld Ship(WorldRules? rules, params ModuleKind[] antennas)
    {
        var w = World(NoShip, rules);
        int s = PowerTests.Ship(w, 1, 20);
        for (int i = 0; i < antennas.Length; i++) w.Network.AddStructureModuleUnchecked(s, 5 + i, 0, 0, antennas[i]);
        w.Step();
        return w;
    }

    private static long Base(SimWorld w) => w.Network.Galaxy?.Wide == true ? ExplorationRules.WideScanRangeCells : ExplorationRules.ScanRangeCells;

    // ---- stacking ------------------------------------------------------------------------------------

    [Fact]
    public void Stacking_gives_the_best_in_full_and_halves_every_further_one()
    {
        Assert.Equal(0, ShieldSensorRules.Stack(Array.Empty<long>()));
        Assert.Equal(400, ShieldSensorRules.Stack(new long[] { 400 }));
        Assert.Equal(600, ShieldSensorRules.Stack(new long[] { 400, 400 }));        // 400 + 200: the second gives 50 percent
        Assert.Equal(700, ShieldSensorRules.Stack(new long[] { 400, 400, 400 }));   // + 100
        Assert.Equal(750, ShieldSensorRules.Stack(new long[] { 400, 400, 400, 400 }));
        Assert.Equal(ShieldSensorRules.Stack(new long[] { 100, 500, 300 }), ShieldSensorRules.Stack(new long[] { 500, 300, 100 })); // the order of fitting does not matter
        Assert.Equal(500 + 150 + 25, ShieldSensorRules.Stack(new long[] { 100, 500, 300 }));
        Assert.Equal(400, ShieldSensorRules.Stack(new long[] { 400, 0, -50 }));     // nothing adds nothing
        Assert.True(ShieldSensorRules.Stack(Enumerable.Repeat(400L, 30)) < 800);    // ten times the antennas never doubles the reach
    }

    // ---- content ---------------------------------------------------------------------------------------

    [Fact]
    public void The_shipped_content_defines_every_variant_and_the_old_content_does_not()
    {
        var k = Shipped.Structures!;
        foreach (var kind in new[] { ModuleKind.SensorShort, ModuleKind.SensorLong, ModuleKind.CommsAntenna, ModuleKind.GunHeavy })
        {
            Assert.True(k.IsDefined(kind), kind.ToString());
            Assert.True(ModuleKindIds.IsArmsKind(kind));
            Assert.NotEmpty(k.ModuleCost(kind));
        }
        Assert.True(k.SensorRangeCells(ModuleKind.SensorShort) < k.SensorRangeCells(ModuleKind.Sensor));
        Assert.True(k.SensorRangeCells(ModuleKind.Sensor) < k.SensorRangeCells(ModuleKind.SensorLong));
        Assert.True(k.NavRangeCells(ModuleKind.CommsAntenna) > k.NavRangeCells(ModuleKind.SensorLong)); // the comms antenna is the one that widens the display
        Assert.True(k.ModulePowerUse(ModuleKind.SensorShort) < k.ModulePowerUse(ModuleKind.SensorLong));
        Assert.All(new[] { ModuleKind.Sensor, ModuleKind.SensorShort, ModuleKind.SensorLong, ModuleKind.CommsAntenna }, a => Assert.True(ModuleKindIds.IsAntenna(a)));
        Assert.Equal(Ability.Sensor, ModuleKindIds.RequiredAbility(ModuleKind.CommsAntenna)); // the research gate of the sensor covers the family
        Assert.False(TestContent.Core.Structures!.IsDefined(ModuleKind.SensorLong)); // content that predates them loads and cannot place them
        Assert.NotEqual(TestContent.Core.Hash, Shipped.Hash);
    }

    [Fact]
    public void The_antenna_fields_are_validated()
    {
        ContentDb Variant(Action<JsonObject> edit) { var r = Root(); edit(r["structures"]!.AsObject()); return ContentDb.FromJson(r.ToJsonString()); }
        Assert.Throws<InvalidDataException>(() => Variant(s => s["gun"]!.AsObject()["nav_range_cells"] = 10));       // only antennas widen the display
        Assert.Throws<InvalidDataException>(() => Variant(s => s["sensor_long"]!.AsObject()["cooldown_percent"] = 200)); // only guns have a cooldown
        Assert.Throws<InvalidDataException>(() => Variant(s => s["gun_heavy"]!.AsObject()["cooldown_percent"] = 50));    // never faster than the base interval
        Assert.Equal(100, Shipped.Structures!.CooldownPercent(ModuleKind.Gun));
    }

    // ---- scanner and navigation reach ---------------------------------------------------------------------------

    [Fact]
    public void Without_an_antenna_the_scanner_and_the_display_keep_their_base_reach()
    {
        var w = Ship(null);
        Assert.Equal(Base(w), w.Network.ScanRangeCells(0));
        Assert.Equal(ShieldSensorRules.NavBaseRangeCells, w.Network.NavRangeCells(0));
        Assert.True(w.Network.NavRangeCells(0) > w.Network.ScanRangeCells(0), "a scan never reveals what the display hides");
    }

    [Fact]
    public void The_scanner_reaches_as_far_as_the_fitted_antennas_stack()
    {
        var k = Shipped.Structures!;
        long longRange = k.SensorRangeCells(ModuleKind.SensorLong), shortRange = k.SensorRangeCells(ModuleKind.SensorShort);
        Assert.True(longRange > Base(Ship(null)), "a long-range sensor must outreach the base scanner");
        Assert.Equal(longRange, Ship(null, ModuleKind.SensorLong).Network.ScanRangeCells(0));
        Assert.Equal(longRange + longRange / 2, Ship(null, ModuleKind.SensorLong, ModuleKind.SensorLong).Network.ScanRangeCells(0));
        Assert.Equal(longRange + shortRange / 2, Ship(null, ModuleKind.SensorShort, ModuleKind.SensorLong).Network.ScanRangeCells(0));
        Assert.Equal(Base(Ship(null)), Ship(null, ModuleKind.SensorShort).Network.ScanRangeCells(0)); // a short-range sensor is below the base scanner
        Assert.Equal(longRange + longRange / 2 + longRange / 4, Ship(null, ModuleKind.SensorLong, ModuleKind.SensorLong, ModuleKind.SensorLong).Network.ScanRangeCells(0));
    }

    [Fact]
    public void The_scan_command_reveals_a_deposit_only_a_long_antenna_reaches()
    {
        long far = Base(Ship(null)) + 60; // beyond the base scanner, inside the long-range sensor
        foreach (bool antenna in new[] { false, true })
        {
            var w = antenna ? Ship(null, ModuleKind.SensorLong) : Ship(null);
            var o = w.Network.GetStructure(w.Network.ShipStructure(0));
            int id = w.Network.AddDeposit(NoShip.ItemIndex("core:iron_ore"), 5000, (int)(o.OriginX / Units.CellUnits) + (int)far, (int)(o.OriginY / Units.CellUnits), (int)(o.OriginZ / Units.CellUnits), density: 100, revealed: false);
            var p = w.Network.GetPlayerInfo(0);
            Assert.False(w.Network.IsDepositRevealed(id));
            w.Submit(0, new Scan());
            w.Step();
            Assert.Equal(antenna, w.Network.IsDepositRevealed(id));
            w.ValidateInvariants();
        }
    }

    [Fact]
    public void The_navigation_display_widens_with_comms_antennas_and_stacks_them()
    {
        var k = Shipped.Structures!;
        long comms = k.NavRangeCells(ModuleKind.CommsAntenna), nb = ShieldSensorRules.NavBaseRangeCells;
        Assert.Equal(nb + comms, Ship(null, ModuleKind.CommsAntenna).Network.NavRangeCells(0));
        Assert.Equal(nb + comms + comms / 2, Ship(null, ModuleKind.CommsAntenna, ModuleKind.CommsAntenna).Network.NavRangeCells(0));
        // the best antenna counts in full whatever the order, and a mix takes the strongest first
        Assert.Equal(nb + comms + k.NavRangeCells(ModuleKind.SensorLong) / 2, Ship(null, ModuleKind.SensorLong, ModuleKind.CommsAntenna).Network.NavRangeCells(0));
        Assert.True(Ship(null, ModuleKind.CommsAntenna, ModuleKind.CommsAntenna, ModuleKind.CommsAntenna, ModuleKind.CommsAntenna).Network.NavRangeCells(0) < nb + 2 * comms);
    }

    [Fact]
    public void An_unpowered_antenna_gives_nothing()
    {
        var w = World(NoShip);
        int s = PowerTests.Ship(w, 1, 0);
        w.Network.RemoveStructureModuleUnchecked(s, 4, 0, 0); // no reactor: no power at all
        w.Network.AddStructureModuleUnchecked(s, 5, 0, 0, ModuleKind.SensorLong);
        w.Network.AddStructureModuleUnchecked(s, 6, 0, 0, ModuleKind.CommsAntenna);
        w.Step();
        Assert.Equal(Base(w), w.Network.ScanRangeCells(0));
        Assert.Equal(ShieldSensorRules.NavBaseRangeCells, w.Network.NavRangeCells(0));
    }

    [Fact]
    public void A_legacy_arms_world_ignores_antennas_refuses_the_new_kinds_and_lists_everything()
    {
        var legacy = new WorldRules().WithLegacyArms(true);
        var w = Ship(legacy, ModuleKind.SensorLong, ModuleKind.CommsAntenna);
        Assert.Equal(Base(w), w.Network.ScanRangeCells(0));
        Assert.Equal(long.MaxValue, w.Network.NavRangeCells(0));
        int s = w.Network.ShipStructure(0);
        w.Submit(0, new PlaceModuleIn(s, 5 + 2, 0, 0, ModuleKind.SensorShort));
        w.Step();
        Assert.Equal(Reject.KindNotAllowed, w.LastResults.Single().Reject);
        w.Submit(0, new PlaceModuleIn(s, 5 + 2, 0, 0, ModuleKind.GunHeavy));
        w.Step();
        Assert.Equal(Reject.KindNotAllowed, w.LastResults.Single().Reject);
        var current = World(NoShip);
        Assert.False(current.Network.Rules.LegacyArms);
    }

    [Fact]
    public void The_nav_list_keeps_what_is_in_reach_the_gate_the_port_and_the_selection()
    {
        NavTarget Row(NavKind kind, float d, bool selected = false) => new(kind, 0, "x", d, 0f, "i", selected);
        var rows = new[] { Row(NavKind.Deposit, 100), Row(NavKind.Deposit, 900), Row(NavKind.Enemy, 450), Row(NavKind.Gate, 5000), Row(NavKind.Port, 2000), Row(NavKind.Deposit, 3000, selected: true) };
        var at400 = NavList.Within(rows, 400).ToList();
        Assert.Equal(4, at400.Count); // the near deposit, the gate, the port and the selected one
        Assert.DoesNotContain(at400, t => t.Distance == 900f || t.Distance == 450f);
        Assert.Equal(5, NavList.Within(rows, 500).Count());                                    // + the enemy at 450
        Assert.Equal(rows.Length, NavList.Within(rows, long.MaxValue).Count());
    }

    // ---- the replay idiom: old logs read with the flag and replay as recorded ----------------------------------

    [Fact]
    public void A_log_older_than_version_23_reads_with_the_legacy_flag_and_replays_as_recorded()
    {
        var recorded = Ship(new WorldRules().WithLegacyArms(true), ModuleKind.Sensor);
        recorded.Submit(0, new Scan());
        recorded.Step(200);
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, NoShip.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: recorded.Network.Rules);
        var bytes = ms.ToArray();
        Assert.Equal(28, bytes[4]);
        for (byte version = 17; version <= 22; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyArms, $"version {version}");
        }
        CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var cur);
        Assert.True(cur.LegacyArms); // written for a legacy-arms world, so its header says so
        var fresh = new MemoryStream();
        CommandCodec.WriteLog(fresh, new List<LoggedCommand>(), NoShip.Hash, GameMode.Sandbox, Seed, structures: true);
        CommandCodec.ReadLog(new MemoryStream(fresh.ToArray()), out _, out _, out _, out _, out _, out _, out var current2);
        Assert.False(current2.LegacyArms);
        var older = fresh.ToArray(); older[4] = 21;
        CommandCodec.ReadLog(new MemoryStream(older), out _, out _, out _, out _, out _, out _, out var old2);
        Assert.True(old2.LegacyArms && old2.WithoutLegacyFlags().IsDefault && old2.HashesAsDefault);
    }

    [Fact]
    public void The_legacy_arms_flag_roundtrips_hashes_as_default_rules_and_no_preset_has_it()
    {
        var r = new WorldRules().WithLegacyArms(true);
        Assert.True(r.LegacyArms);
        Assert.Equal(r, WorldRules.Deserialize(r.Serialize()));
        Assert.True(r.HashesAsDefault);
        Assert.True(r.WithoutLegacyFlags().IsDefault);
        Assert.True(r.WithLegacyField(true).LegacyArms && r.WithLegacyInventory(true).LegacyArms && r.WithResourceDensity(120).LegacyArms && r.Clamped().LegacyArms);
        Assert.True(new WorldRules().WithLegacyField(true).WithLegacyArms(true).LegacyField);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyArms));
        Assert.NotEqual(r, WorldRules.Default);
    }

    [Fact]
    public void A_world_with_antennas_replays_and_clones_exactly()
    {
        var w = Ship(null, ModuleKind.SensorLong, ModuleKind.CommsAntenna, ModuleKind.SensorShort);
        w.Submit(0, new Scan());
        w.Step(400);
        w.ValidateInvariants();
        Assert.Equal(w.Network.Checksum(), w.Network.Clone().Checksum());
    }

    // ---- the heavy cannon --------------------------------------------------------------------------------------

    private static Reject Run(SimWorld w, Command c) { w.Submit(0, c); w.Step(); return w.LastResults.Single().Reject; }

    private static int Volleys(SimWorld w, int ticks)
    {
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        int volleys = 0;
        for (int i = 0; i < ticks; i++) { w.Step(); if (w.Network.Shots.Any(s => s.Kind == ShotKind.Ship)) volleys++; }
        return volleys;
    }

    [Fact]
    public void The_heavy_cannon_fires_slowly_hits_hard_and_kicks_the_ship_hard()
    {
        var k = Shipped.Structures!;
        Assert.Equal(300, k.CooldownPercent(ModuleKind.GunHeavy));
        Assert.True(k.HardpointPercent(ModuleKind.GunHeavy) > 2 * k.HardpointPercent(ModuleKind.GunMk2));         // a big hit
        Assert.True(k.Recoil(ModuleKind.GunHeavy) * k.HardpointPercent(ModuleKind.GunHeavy) > 2L * k.Recoil(ModuleKind.GunMk2) * k.HardpointPercent(ModuleKind.GunMk2)); // a big kick
        Assert.True(ModuleKindIds.IsGun(ModuleKind.GunHeavy) && ModuleKindIds.IsHardpoint(ModuleKind.GunHeavy));
        Assert.Equal("gun_heavy", ModuleKindIds.Id(ModuleKind.GunHeavy));

        var plain = World(Shipped);
        var heavy = World(HeavyStarter());
        Assert.Equal(ModuleKind.GunHeavy, heavy.Network.Guns(0).Single().Kind);
        int ordinary = Volleys(plain, 600), slow = Volleys(heavy, 600);
        int interval = Shipped.Threat!.ShipWeaponIntervalTicks;
        Assert.InRange(slow, 600 / (3 * interval) - 2, 600 / (3 * interval) + 1);
        Assert.True(slow * 2 < ordinary, $"heavy {slow} volleys, ordinary {ordinary}");
        // the damage per second is a little better, not a multiple: the heavy cannon wins on the size of the hit
        long ordinaryDps = (long)ordinary * k.HardpointPercent(ModuleKind.Gun), heavyDps = (long)slow * k.HardpointPercent(ModuleKind.GunHeavy);
        Assert.InRange(heavyDps, ordinaryDps / 2, ordinaryDps * 2);

        // the push of the first volley is the content recoil at the strength of the cannon, scaled by the mass of the ship
        var w = World(HeavyStarter());
        int modules = w.Network.GetStructure(w.Network.ShipStructure(0)).Modules.Count;
        long recoil = (long)k.Recoil(ModuleKind.GunHeavy) * k.HardpointPercent(ModuleKind.GunHeavy) / 100;
        Assert.Equal((int)recoil, w.Network.GunRecoil(0));
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        for (int i = 0; i < 60 && !w.Network.Shots.Any(s => s.Kind == ShotKind.Ship); i++) w.Step();
        var p = w.Network.GetPlayerInfo(0);
        Assert.Equal(recoil * FeelRules.RecoilReferenceMass / modules, p.VelZ);
        w.ValidateInvariants();
    }

    [Fact]
    public void The_heavy_cannon_slows_a_mixed_battery_by_its_share_of_the_strength()
    {
        // a gun (100 percent, 100) and a heavy cannon (450 percent, 300): the interval follows the strength-weighted cooldown
        var w = World(NoShip);
        int s = PowerTests.Ship(w, 1, 20);
        w.Network.RemoveStructureModuleUnchecked(s, 0, 1, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 1, 0, ModuleKind.Gun);
        w.Network.AddStructureModuleUnchecked(s, 1, 1, 0, ModuleKind.GunHeavy);
        w.Network.AddStructureModuleUnchecked(s, 2, 1, 0, ModuleKind.MiningLaser);
        w.Step();
        int interval = NoShip.Threat!.ShipWeaponIntervalTicks;
        int volleys = Volleys(w, 600);
        long weighted = (100L * 100 + 450L * 300) / 550; // percent of the base interval
        int expected = (int)(600 / (interval * weighted / 100));
        Assert.InRange(volleys, expected - 2, expected + 2);
    }
}
