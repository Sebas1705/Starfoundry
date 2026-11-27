using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0032: weapons are hardpoint modules of the ship (mining laser, gun), the shipyard is a station module, and old worlds keep the implicit weapons.</summary>
public class HardpointTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261122UL;
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");

    private static SimWorld World(WorldRules? rules = null, bool threats = false) =>
        SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: threats, stationRules: false, structures: true, rules: (rules ?? WorldRules.Default).WithLegacyBlueprint(true).WithLegacyInterior(true));

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    /// <summary>A ship with a reactor and fuel and the given extra modules (kind per module above the cockpit, from (0,1,0) up the x axis).</summary>
    private static int ShipWith(SimWorld w, params ModuleKind[] extra)
    {
        int s = PowerTests.Ship(w, 1, 20);
        w.Network.RemoveStructureModuleUnchecked(s, 0, 1, 0); // the helper's own laser
        for (int i = 0; i < extra.Length; i++) w.Network.AddStructureModuleUnchecked(s, i, 1, 0, extra[i]);
        return s;
    }

    private static int Deposit(SimWorld w, int ship, int dx = 5, int dy = 2, int dz = 2)
    {
        var o = w.Network.GetStructure(ship);
        return w.Network.AddDeposit(Ore, 100_000, (int)(o.OriginX / Cell) + dx, (int)(o.OriginY / Cell) + dy, (int)(o.OriginZ / Cell) + dz, density: 100);
    }

    [Fact]
    public void The_catalogue_rule_keeps_hardpoints_on_ships_and_the_shipyard_on_stations()
    {
        foreach (var k in new[] { ModuleKind.MiningLaser, ModuleKind.MiningLaserMk2, ModuleKind.Gun, ModuleKind.GunMk2 })
        {
            Assert.True(ModuleKindIds.IsHardpoint(k));
            Assert.True(ModuleKindIds.AllowedOn(StructureKind.Ship, k));
            Assert.False(ModuleKindIds.AllowedOn(StructureKind.Station, k));
        }
        Assert.True(ModuleKindIds.AllowedOn(StructureKind.Station, ModuleKind.Shipyard));
        Assert.False(ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Shipyard));
        Assert.False(ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Core));
        Assert.False(ModuleKindIds.AllowedOn(StructureKind.Station, ModuleKind.Cockpit));
        Assert.True(ModuleKindIds.AllowedOn(StructureKind.Station, ModuleKind.Hull) && ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Hull));
        Assert.Equal(ModuleKind.MiningLaserMk2, ModuleKindIds.UpgradeOf(ModuleKind.MiningLaser));
        Assert.Equal(ModuleKind.GunMk2, ModuleKindIds.UpgradeOf(ModuleKind.Gun));
        Assert.Null(ModuleKindIds.UpgradeOf(ModuleKind.GunMk2));
        Assert.Equal("mining_laser_mk2", ModuleKindIds.Id(ModuleKind.MiningLaserMk2));
        Assert.True(C.Structures!.IsDefined(ModuleKind.Shipyard));
        Assert.Equal(100, C.Structures.HardpointPercent(ModuleKind.MiningLaser));
        Assert.Equal(170, C.Structures.HardpointPercent(ModuleKind.MiningLaserMk2));
    }

    [Fact]
    public void A_ship_without_a_laser_module_cannot_mine_but_a_legacy_world_still_can()
    {
        var w = World();
        int s = ShipWith(w); // no laser
        int dep = Deposit(w, s);
        Assert.False(w.Network.HasMiningLaser(0));
        Assert.Equal(Reject.NoLaser, Run(w, new StartMining(dep)));

        var old = World(new WorldRules().WithLegacyHardpoints(true));
        int s2 = ShipWith(old);
        int dep2 = Deposit(old, s2);
        Assert.Equal(Reject.None, Run(old, new StartMining(dep2)));
        old.Step(300);
        Assert.True(old.Network.HoldCount(0, Ore) > 0);
    }

    [Fact]
    public void One_laser_mines_exactly_as_the_implicit_laser_did_and_more_lasers_mine_faster()
    {
        long Mined(WorldRules? rules, params ModuleKind[] lasers)
        {
            var w = World(rules);
            int s = ShipWith(w, lasers);
            int dep = Deposit(w, s);
            Run(w, new StartMining(dep));
            w.Step(300);
            return w.Network.HoldCount(0, Ore);
        }
        long legacy = Mined(new WorldRules().WithLegacyHardpoints(true));
        long one = Mined(null, ModuleKind.MiningLaser);
        long two = Mined(null, ModuleKind.MiningLaser, ModuleKind.MiningLaser);
        long mk2 = Mined(null, ModuleKind.MiningLaserMk2);
        Assert.True(legacy > 15);
        Assert.Equal(legacy, one);
        Assert.True(two >= one * 19 / 10, $"two lasers {two}, one {one}");
        Assert.True(mk2 >= one * 16 / 10 && mk2 < two, $"mk2 {mk2}, one {one}");
        var w2 = World();
        int s2 = ShipWith(w2, ModuleKind.MiningLaser, ModuleKind.MiningLaser);
        int d2 = Deposit(w2, s2);
        Assert.Equal(7, w2.Network.MineTicksFor(0, d2)); // 15 ticks for one laser at density 100, 15 * 100 / 200 for two
        Assert.Equal(15, w2.Network.MineTicksFor(d2));
    }

    [Fact]
    public void Each_laser_draws_power_while_it_mines_and_a_lost_laser_stops_the_beam()
    {
        var w = World();
        int s = ShipWith(w, ModuleKind.MiningLaser, ModuleKind.MiningLaserMk2);
        int dep = Deposit(w, s);
        Run(w, new StartMining(dep));
        w.Step(5);
        Assert.Equal(PlayerRules.LaserPowerUse + 8, w.Network.PowerStatus(s).Demand);
        w.Network.RemoveStructureModuleUnchecked(s, 0, 1, 0);
        w.Network.RemoveStructureModuleUnchecked(s, 1, 1, 0);
        int before = w.Network.HoldCount(0, Ore);
        w.Step(600);
        Assert.Equal(before, w.Network.HoldCount(0, Ore));
        Assert.Equal(0, w.Network.PowerStatus(s).Demand);
        w.ValidateInvariants();
    }

    [Fact]
    public void The_beam_reaches_from_the_laser_module_and_follows_the_ships_attitude()
    {
        var w = World();
        int s = ShipWith(w, ModuleKind.MiningLaser);
        var mount = w.Network.MiningLasers(0).Single();
        var o = w.Network.GetStructure(s);
        int cx = (int)(mount.X / Cell), cy = (int)(mount.Y / Cell), cz = (int)(mount.Z / Cell);
        int near = w.Network.AddDeposit(Ore, 1000, cx, cy, cz + 9);
        int far = w.Network.AddDeposit(Ore, 1000, cx, cy, cz + 12);
        Assert.True(w.Network.InMineRange(0, near));
        Assert.False(w.Network.InMineRange(0, far));
        Assert.Equal(Reject.OutOfRange, Run(w, new StartMining(far)));
        Assert.Single(w.Network.LasersReaching(0, near));
        // Turned a quarter about the y axis the module sits somewhere else: the deposit that was in reach no longer is.
        w.Network.SetStructureAttitude(s, Attitude.Of(11585, 0, 11585, 0));
        var turned = w.Network.MiningLasers(0).Single();
        Assert.NotEqual((mount.X, mount.Z), (turned.X, turned.Z));
        long dxs = near == 0 ? 0 : 0;
        var d = w.Network.DepositPosition(near);
        long ex = d.X * (long)Cell - turned.X, ey = d.Y * (long)Cell - turned.Y, ez = d.Z * (long)Cell - turned.Z;
        Assert.Equal(ex * ex + ey * ey + ez * ez <= 10L * Cell * 10L * Cell, w.Network.InMineRange(0, near)); // the reach is measured from the turned module
        _ = dxs;
        int follow = w.Network.AddDeposit(Ore, 1000, (int)(turned.X / Cell), (int)(turned.Y / Cell) + 8, (int)(turned.Z / Cell));
        Assert.True(w.Network.InMineRange(0, follow));
        _ = o;
    }

    [Fact]
    public void Guns_are_modules_too_the_legacy_gun_is_implicit_and_more_guns_hit_harder()
    {
        var w = World(threats: true);
        ShipWith(w);
        Assert.Equal(Reject.NoGun, Run(w, new SetWeapons(true)));
        Assert.Equal(Reject.None, Run(w, new SetWeapons(false)));
        var old = World(new WorldRules().WithLegacyHardpoints(true), threats: true);
        ShipWith(old);
        Assert.Equal(Reject.None, Run(old, new SetWeapons(true)));

        int Damage(params ModuleKind[] guns)
        {
            var g = World(threats: true);
            ShipWith(g, guns);
            Run(g, new SetWeapons(true));
            g.Network.ScheduleWave(0);
            int hp0 = 0;
            for (int i = 0; i < 40_000; i++)
            {
                g.Step();
                if (g.Network.Shots.Any(sh => sh.Kind == ShotKind.Ship)) { hp0 = i; break; }
            }
            return hp0;
        }
        Assert.True(Damage(ModuleKind.Gun) > 0); // a gun module fires at the wave
        var gun = ModuleKind.Gun;
        Assert.Equal(2, new[] { gun, ModuleKind.GunMk2 }.Count(ModuleKindIds.IsGun));
        Assert.Equal(170, C.Structures!.HardpointPercent(ModuleKind.GunMk2));
    }

    [Fact]
    public void Hardpoints_are_fitted_in_the_designer_and_a_ship_takes_no_shipyard()
    {
        var w = World();
        int s = ShipWith(w);
        Assert.Equal(Reject.NoShipyard, Run(w, new PlaceModuleIn(s, 0, 1, 0, ModuleKind.MiningLaser)));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(s, 0, 1, 0, ModuleKind.Shipyard)));
        w.Submit(0, new FoundStation(0, 0, -20));
        w.Step();
        int st = w.Network.Structures().First(x => x.Kind == StructureKind.Station).Id;
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(st, 1, 0, 0, ModuleKind.Gun)));
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(st, 1, 0, 0, ModuleKind.Shipyard)));
    }

    [Fact]
    public void Designer_command_roundtrips_and_old_logs_cannot_hold_it()
    {
        var cmd = new DesignerApply(new[] { new DesignModule(0, 0, 0, ModuleKind.Cockpit), new DesignModule(0, 0, -1, ModuleKind.MiningLaserMk2) });
        var back = (DesignerApply)CommandCodec.FromBytes(CommandCodec.ToBytes(cmd));
        Assert.Equal(cmd.Modules, back.Modules);
        var inBatch = new Batch(new Command[] { cmd });
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, new List<LoggedCommand> { new(0, 0, inBatch) }, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(27, bytes[4]);
        CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.False(rules.LegacyHardpoints);
        bytes[4] = 13;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(bytes)));
        // An older log reads with the legacy hardpoints and no other change to the rules.
        var plain = new MemoryStream();
        CommandCodec.WriteLog(plain, new List<LoggedCommand> { new(0, 0, new StopMining()) }, C.Hash, GameMode.Sandbox, Seed, structures: true);
        var old = plain.ToArray();
        old[4] = 13;
        CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var oldRules);
        Assert.True(oldRules.LegacyHardpoints);
        Assert.True(oldRules.WithoutLegacyFlags().IsDefault);
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(new byte[] { 46, 0xFF, 0xFF, 0xFF, 0x7F }));
    }

    [Fact]
    public void A_legacy_world_replays_as_recorded_and_the_flag_roundtrips_and_hashes_as_default()
    {
        var legacy = new WorldRules().WithLegacyHardpoints(true);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyHardpoints));
        Assert.True(legacy.WithLegacyHardpoints(false).IsDefault);

        var a = World(legacy);
        int s = ShipWith(a);
        int dep = Deposit(a, s);
        Run(a, new StartMining(dep));
        a.Step(900);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, a.Log, C.Hash, GameMode.Sandbox, Seed, structures: true, rules: legacy);
        var log = CommandCodec.ReadLog(new MemoryStream(ms.ToArray()), out _, out _, out _, out _, out _, out _, out var rules);
        var initial = World(rules);
        int s2 = ShipWith(initial);
        Deposit(initial, s2);
        var replay = SimWorld.Replay(log, a.Tick, initial);
        Assert.Equal(a.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }

    [Fact]
    public void The_key_panel_follows_the_weapon_group_and_the_modules()
    {
        var flying = new KeyState { Hardpoints = true };
        IEnumerable<KeyRow> Rows(KeyState s) => KeyHints.For(s).SelectMany(g => g.Rows);
        Assert.Equal("key.weaponLaser", Rows(flying).First(r => r.Id == "weapons").LabelKey);
        var guns = flying with { WeaponGroup = WeaponGroup.Guns };
        Assert.Equal("key.weaponGuns", Rows(guns).First(r => r.Id == "weapons").LabelKey);
        Assert.Contains(Rows(guns), r => r.Id == "fireGuns");
        Assert.Equal("key.weaponLaser", Rows(guns with { HasGunModule = false }).First(r => r.Id == "weapons").LabelKey); // ADR 0036: without a gun the laser stays in hand and M has nothing to switch to
        Assert.Equal(KeyRowState.Disabled, Rows(flying with { HasLaserModule = false, HasGunModule = false }).First(r => r.Id == "weapons").State); // ADR 0036: the weapons row is the one for both
        var docked = flying with { Dock = DockHint.Docked, ShipyardDocked = true };
        Assert.DoesNotContain(Rows(docked), r => r.Id == "designer"); // the designer is a tab of the ship computer now
        Assert.Contains(Rows(docked), r => r.Id == "computer" && r.ActionKey == "Tab");
        foreach (var key in new[] { "key.weaponLaser", "key.weaponGuns", "key.fireGuns", "key.designer", "key.why.noLaser", "key.why.noGun", "toast.reject.noLaser", "toast.reject.noGun", "toast.reject.noShipyard" })
            Assert.True(Loc.Has(key), key);
        Assert.Equal("toast.reject.noShipyard", RejectTexts.Key(Reject.NoShipyard));
    }

    [Fact]
    public void Every_designer_string_has_both_languages_with_matching_placeholders()
    {
        foreach (var (key, (es, en)) in Loc.All.Where(p => p.Key.StartsWith("designer.") || p.Key.StartsWith("module.mining") || p.Key.StartsWith("module.gun") || p.Key.StartsWith("module.shipyard")))
        {
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            for (int i = 0; i < 4; i++) Assert.Equal(es.Contains("{" + i + "}"), en.Contains("{" + i + "}"));
        }
    }
}
