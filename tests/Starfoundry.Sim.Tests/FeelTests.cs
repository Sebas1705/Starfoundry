using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0036: free fire with projectiles (SetAim), recoil, hold overflow into ore pickups, regrowing deposits and the resource density rule. The shipped content is
/// used (the starter ship has a gun module at (0,1,0) and a laser at (0,0,-1), a 400 item hold and a 250 percent laser).
/// </summary>
public class FeelTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261124UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Carbon = C.ItemIndex("core:carbon");

    private static SimWorld World(WorldRules? rules = null, bool threats = false, ContentDb? content = null) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, Seed, threats, stationRules: false, structures: true, rules: rules);

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    private static ContentDb Variant(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        edit(root);
        return ContentDb.FromJson(root.ToJsonString());
    }

    private static HardpointMount Gun(SimWorld w) => w.Network.Guns(0).First();

    /// <summary>The gun's centre in units relative to the ship's origin: where the ship has to move so the gun is at a given point.</summary>
    private static (long X, long Y, long Z) GunOffset(SimWorld w)
    {
        var s = w.Network.GetStructure(w.Network.ShipStructure(0));
        var g = Gun(w);
        return (g.X - s.OriginX, g.Y - s.OriginY, g.Z - s.OriginZ);
    }

    /// <summary>Moves the ship so its gun sits at the point (units).</summary>
    private static void PutGunAt(SimWorld w, long x, long y, long z)
    {
        var off = GunOffset(w);
        w.Network.SetStructureOrigin(w.Network.ShipStructure(0), x - off.X, y - off.Y, z - off.Z);
    }

    /// <summary>The aim vector that points from the gun at a point (units), in the ship's frame (the ship is level here), at the command's scale.</summary>
    private static SetAim AimAt(SimWorld w, long x, long y, long z)
    {
        var g = Gun(w);
        double dx = x - g.X, dy = y - g.Y, dz = z - g.Z, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return new SetAim((int)Math.Round(dx / len * 1000), (int)Math.Round(dy / len * 1000), (int)Math.Round(dz / len * 1000));
    }

    private static IEnumerable<ImpactEvent> StepCollecting(SimWorld w, int ticks, Func<bool>? until = null)
    {
        var all = new List<ImpactEvent>();
        for (int i = 0; i < ticks; i++)
        {
            w.Step();
            all.AddRange(w.Network.Impacts);
            if (until?.Invoke() == true) break;
        }
        return all;
    }

    // ---- the command ---------------------------------------------------------------------------------------------

    [Fact]
    public void SetAim_is_validated_cleared_by_a_zero_vector_and_refused_in_a_legacy_world()
    {
        var w = World();
        Assert.Equal(Reject.None, Run(w, new SetAim(0, 0, -1024)));
        Assert.Equal((0, 0, -1024), w.Network.AimOf(0));
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetAim(1024, 1024, 0)));   // longer than the scale
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetAim(2000, 0, 0)));      // a component past the scale
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetAim(100, 0, 0)));       // shorter than half the scale: no direction worth the name
        Assert.Equal((0, 0, -1024), w.Network.AimOf(0));                           // refused commands change nothing
        Assert.Equal(Reject.None, Run(w, new SetAim(0, 0, 0)));
        Assert.Null(w.Network.AimOf(0));
        w.ValidateInvariants();

        var old = World(new WorldRules().WithLegacyFeel(true));
        Assert.Equal(Reject.NotAllowedHere, Run(old, new SetAim(0, 0, -1024)));
        Assert.Null(old.Network.AimOf(0));
    }

    [Fact]
    public void SetAim_roundtrips_in_the_codec_and_older_logs_cannot_hold_it()
    {
        var log = new List<LoggedCommand> { new(0, 0, new SetAim(10, -700, 512)), new(1, 0, new Batch(new Command[] { new SetAim(0, 0, -1024), new SetWeapons(true) })) };
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(23, bytes[4]);
        var back = CommandCodec.ReadLog(new MemoryStream(bytes));
        Assert.Equal(new SetAim(10, -700, 512), back[0].Command);
        Assert.Equal(new SetAim(0, 0, -1024), ((Batch)back[1].Command).Commands[0]);
        Assert.Equal(new SetAim(5, 6, 7), CommandCodec.FromBytes(CommandCodec.ToBytes(new SetAim(5, 6, 7))));

        var v16 = (byte[])bytes.Clone();
        v16[4] = 16;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(v16)));

        // An older log reads with the two new legacy flags on and still hashes as default rules.
        using var empty = new MemoryStream();
        CommandCodec.WriteLog(empty, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        var current = CommandCodec.ReadLog(new MemoryStream(empty.ToArray()), out _, out _, out _, out _, out _, out _, out var rules17);
        Assert.False(rules17.LegacyFeel || rules17.LegacyResources);
        Assert.Empty(current);
        for (byte version = 6; version <= 16; version++)
        {
            var old = empty.ToArray();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyFeel && rules.LegacyResources, $"version {version}");
            Assert.True(rules.HashesAsDefault, $"version {version}");
        }
    }

    // ---- free fire -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_shot_leaves_the_gun_with_no_enemy_and_hits_the_first_thing_on_its_path()
    {
        var w = World();
        var g = Gun(w);
        int near = w.Network.AddDeposit(Ore, 1000, (int)(g.X / Cell), (int)(g.Y / Cell), (int)(g.Z / Cell) - 14);
        int far = w.Network.AddDeposit(Ore, 1000, (int)(g.X / Cell), (int)(g.Y / Cell), (int)(g.Z / Cell) - 22);
        Assert.Equal(0, w.Network.EnemyCount);
        Assert.Equal(Reject.None, Run(w, new SetAim(0, 0, -1024)));
        Assert.Equal(Reject.None, Run(w, new SetWeapons(true)));
        var hits = StepCollecting(w, 90).ToList();
        Assert.NotEmpty(hits);
        Assert.Equal((ImpactKind.Deposit, near), (hits[0].Kind, hits[0].Id));      // the nearer rock stops it
        Assert.All(hits, h => Assert.NotEqual(far, h.Id));                          // the far one is behind it
        Assert.True(w.Network.GetPlayerInfo(0).Armed);
        w.ValidateInvariants();
    }

    [Fact]
    public void Without_an_aim_the_guns_still_need_a_target_and_with_one_they_shoot_at_nothing()
    {
        var w = World();
        Run(w, new SetWeapons(true));
        w.Step(120);
        Assert.DoesNotContain(w.Network.Shots, s => s.Kind == ShotKind.Ship);
        Assert.Equal(0, w.Network.ProjectileCount);

        Run(w, new SetAim(0, 0, -1024));
        bool shot = false;
        for (int i = 0; i < 40 && !shot; i++) { w.Step(); shot = w.Network.Shots.Any(s => s.Kind == ShotKind.Ship); }
        Assert.True(shot, "a gun with an aim fires with nothing to shoot at");
        Assert.True(w.Network.ProjectileCount > 0);
        // It flies out of range and is gone; nothing was hit.
        Run(w, new SetWeapons(false));
        var hits = StepCollecting(w, 90).ToList();
        Assert.Empty(hits);
        Assert.Equal(0, w.Network.ProjectileCount);
        w.ValidateInvariants();
    }

    [Fact]
    public void Volleys_come_one_per_interval_and_never_more_than_the_cap_are_in_the_air()
    {
        var w = World();
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        int volleys = 0, peak = 0;
        for (int i = 0; i < 300; i++)
        {
            w.Step();
            if (w.Network.Shots.Any(s => s.Kind == ShotKind.Ship)) volleys++;
            peak = Math.Max(peak, w.Network.ProjectileCount);
        }
        int interval = C.Threat!.ShipWeaponIntervalTicks;
        Assert.InRange(volleys, 300 / interval - 2, 300 / interval + 1);
        Assert.InRange(peak, 1, FeelRules.MaxProjectiles);
    }

    [Fact]
    public void Shots_hurt_an_enemy_and_a_mine_goes_off_and_debris_breaks_up()
    {
        var w = World(threats: true);
        var n = w.Network;
        // An enemy: wait for a wave, put the ship 15 cells in front of the first one with the gun in line, and shoot along -z.
        n.ScheduleWave(0);
        for (int i = 0; i < 2000 && n.EnemyCount == 0; i++) w.Step();
        Assert.True(n.EnemyCount > 0);
        var enemy = n.Enemies()[0];
        PutGunAt(w, enemy.X, enemy.Y, enemy.Z + 15L * Cell);
        Run(w, new SetWeapons(true));
        long killedBefore = n.EnemiesKilled;
        int hp0 = enemy.Hp;
        var hits = new List<ImpactEvent>();
        for (int i = 0; i < 300 && !hits.Any(h => h.Kind == ImpactKind.Enemy); i++)
        {
            var cur = n.Enemies().Where(e => e.Id == enemy.Id).Cast<EnemyInfo?>().FirstOrDefault();
            if (cur is { } c) w.Submit(0, AimAt(w, c.X, c.Y, c.Z)); // the enemy moves: aim again at where it is
            w.Step();
            hits.AddRange(n.Impacts);
        }
        Assert.Contains(hits, h => h.Kind == ImpactKind.Enemy);
        Assert.True(n.EnemiesKilled > killedBefore || n.Enemies().Any(e => e.Id == enemy.Id && e.Hp < hp0) || n.Enemies().All(e => e.Id != enemy.Id), "the enemy took the damage");
        w.ValidateInvariants();

        // A mine: shot from afar it explodes (the blast is the obstacle step's, one tick after the hit).
        var longRange = Variant(r => r["threat"]!["ship_weapon"]!.AsObject()["range_cells"] = 40); // a mine arms when a module is within about 17 cells of it: shoot it from beyond that
        var w2 = World(content: longRange);
        int mine = Enumerable.Range(0, w2.Network.ObstacleCount).First(i => w2.Network.GetObstacle(i).Behaviour == ObstacleBehaviour.Mine);
        var m = w2.Network.GetObstacle(mine);
        PutGunAt(w2, m.X, m.Y, m.Z + 30L * Cell);
        Run(w2, new SetAim(0, 0, -1024));
        Run(w2, new SetWeapons(true));
        var mineHits = new List<ImpactEvent>();
        bool exploded = false;
        for (int i = 0; i < 120 && !exploded; i++)
        {
            w2.Step();
            mineHits.AddRange(w2.Network.Impacts);
            exploded = w2.Network.ObstacleEvents.Any(e => e.Kind == ObstacleEventKind.Explosion && e.Obstacle == mine);
        }
        Assert.Contains(mineHits, h => (h.Kind, h.Id) == (ImpactKind.Mine, mine));
        Assert.True(exploded && w2.Network.GetObstacle(mine).Gone);
        w2.ValidateInvariants();

        // Debris: it breaks up after enough damage and is gone for good.
        var w3 = World();
        int debris = Enumerable.Range(0, w3.Network.ObstacleCount).First(i => w3.Network.GetObstacle(i).Behaviour == ObstacleBehaviour.Drifter);
        Run(w3, new SetWeapons(true));
        for (int i = 0; i < 700 && !w3.Network.GetObstacle(debris).Gone; i++)
        {
            var d = w3.Network.GetObstacle(debris);
            PutGunAt(w3, d.X, d.Y, d.Z + 15L * Cell);
            w3.Submit(0, new SetAim(0, 0, -1024));
            w3.Step();
        }
        Assert.True(w3.Network.GetObstacle(debris).Gone, "debris breaks up under fire");
        w3.ValidateInvariants();
    }

    [Fact]
    public void A_dry_hold_clicks_when_the_content_asks_for_ammo()
    {
        var content = Variant(r =>
        {
            var weapon = r["threat"]!["ship_weapon"]!.AsObject();
            weapon["ammo_item"] = "core:ammo";
            weapon["ammo_per_volley"] = 2;
        });
        var w = World(content: content);
        int ammo = content.ItemIndex("core:ammo");
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        bool dry = false;
        for (int i = 0; i < 30 && !dry; i++) { w.Step(); dry = w.Network.DryFired(0); }
        Assert.True(dry, "no ammo, no shot, a click");
        Assert.Equal(0, w.Network.ProjectileCount);

        w.Network.GiveItems(0, ammo, 5);
        int shots = 0;
        for (int i = 0; i < 100; i++) { w.Step(); if (w.Network.Shots.Any(s => s.Kind == ShotKind.Ship)) shots++; }
        Assert.Equal(2, shots);                                       // 5 ammo, 2 a volley
        Assert.Equal(1, w.Network.HoldCount(0, ammo));
        w.ValidateInvariants();
    }

    // ---- recoil ----------------------------------------------------------------------------------------------------

    private static ContentDb Heavy => Variant(r => r["structures"]!["gun"]!.AsObject()["recoil"] = 12000);

    [Fact]
    public void A_volley_pushes_the_ship_against_the_shot_by_the_content_recoil_scaled_by_mass_and_strength()
    {
        var content = Heavy;
        var w = World(content: content);
        int ship = w.Network.ShipStructure(0);
        int modules = w.Network.GetStructure(ship).Modules.Count;
        Assert.Equal(12000, w.Network.GunRecoil(0));
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        // The tick of the first volley: the velocity is the impulse alone (the engines have not braked it yet).
        for (int i = 0; i < 30 && !w.Network.Shots.Any(s => s.Kind == ShotKind.Ship); i++) w.Step();
        var p = w.Network.GetPlayerInfo(0);
        var expected = BeltNetwork.RecoilImpulse(0, 0, -1024, 1024, 12000, modules);
        Assert.Equal(expected, (p.VelX, p.VelY, p.VelZ));
        Assert.True(p.VelZ > 0 && p.VelX == 0 && p.VelY == 0, "shooting along -z kicks the ship along +z");
        Assert.Equal(12000 * FeelRules.RecoilReferenceMass / modules, p.VelZ);
        w.ValidateInvariants();
    }

    [Fact]
    public void The_recoil_follows_the_ships_attitude_in_the_world()
    {
        var content = Heavy;
        var w = World(content: content);
        int ship = w.Network.ShipStructure(0);
        var att = Attitude.Of(11585, 0, 11585, 0); // a quarter turn about y
        w.Network.SetStructureAttitude(ship, att);
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        for (int i = 0; i < 30 && !w.Network.Shots.Any(s => s.Kind == ShotKind.Ship); i++) w.Step();
        var (wx, wy, wz) = att.Rotate(0, 0, -1024);
        var p = w.Network.GetPlayerInfo(0);
        var expected = BeltNetwork.RecoilImpulse(wx, wy, wz, (long)Math.Round(Math.Sqrt((double)wx * wx + (double)wy * wy + (double)wz * wz)), 12000, w.Network.GetStructure(ship).Modules.Count);
        Assert.Equal(expected, (p.VelX, p.VelY, p.VelZ));
        Assert.True(Math.Abs(p.VelX) > Math.Abs(p.VelZ), "the shot now points along the world x axis, so the kick does too");
        Assert.True(p.VelX * wx <= 0 && p.VelZ * wz <= 0, "opposite to the shot");
    }

    [Fact]
    public void Recoil_zero_leaves_the_ship_alone_and_the_mark_one_gun_has_none()
    {
        var w = World();
        Assert.Equal(0, w.Network.GunRecoil(0));
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        w.Step(100);
        var p = w.Network.GetPlayerInfo(0);
        Assert.Equal((0, 0, 0), (p.VelX, p.VelY, p.VelZ));
        // A mark 2 gun kicks a little (the strength is 170 percent of 3000).
        Assert.Equal(3000 * 170 / 100, C.Structures!.Recoil(ModuleKind.GunMk2) * C.Structures.HardpointPercent(ModuleKind.GunMk2) / 100);
    }

    [Fact]
    public void Recoil_maths_scales_with_strength_and_mass_and_is_zero_without_recoil()
    {
        Assert.Equal((0, 0, 0), BeltNetwork.RecoilImpulse(0, 0, -1024, 1024, 0, 8));
        Assert.Equal((0, 0, 1000), BeltNetwork.RecoilImpulse(0, 0, -1024, 1024, 1000, 8));
        Assert.Equal((0, 0, 500), BeltNetwork.RecoilImpulse(0, 0, -1024, 1024, 1000, 16));   // twice the mass, half the kick
        Assert.Equal((0, 0, 2000), BeltNetwork.RecoilImpulse(0, 0, -1024, 1024, 2000, 8));   // twice the strength, twice the kick
        Assert.Equal((-707, 0, 707), BeltNetwork.RecoilImpulse(724, 0, -724, 1024, 1000, 8)); // diagonal: the unit vector times the kick
    }

    // ---- determinism, replay and the legacy worlds --------------------------------------------------------------------

    private static SimWorld Prepared()
    {
        var w = World(threats: true, content: Heavy);
        w.Network.ScheduleWave(0);
        w.Network.AddDeposit(Ore, 400, 0, 0, -30);
        return w;
    }

    private static void Script(SimWorld w)
    {
        var n = w.Network;
        int dep = 0 + n.DepositCount - 1; // the last deposit: the one Prepared adds
        var rng = new SplitMix64(99);
        w.Submit(0, new SetWeapons(true));
        for (int t = 0; t < 5000; t++)
        {
            if (t % 97 == 0)
            {
                int x = rng.Next(-1024, 1024), y = rng.Next(-300, 300), z = rng.Next(-1024, 0);
                var v = new SetAim(x, y, z);
                if (n.AimInRange(v.Dx, v.Dy, v.Dz)) w.Submit(0, v);
            }
            if (t == 400) w.Submit(0, new StartMining(dep));
            if (t % 700 == 650) w.Submit(0, new SetWeapons(false));
            if (t % 700 == 0 && t > 0) w.Submit(0, new SetWeapons(true));
            w.Step();
        }
    }

    [Fact]
    public void Free_fire_is_deterministic_over_5000_ticks_and_replays_from_the_log()
    {
        var a = Prepared();
        var b = Prepared();
        Script(a); Script(b);
        Assert.Equal(a.Checksum(), b.Checksum());
        a.ValidateInvariants();
        var replay = SimWorld.Replay(a.Log, a.Tick, initial: Prepared());
        Assert.Equal(a.Checksum(), replay.Checksum());
        Assert.Equal(a.Network.GetPlayerInfo(0), replay.Network.GetPlayerInfo(0));
        // The aim and the projectiles are part of the state: a world one tick short differs.
        var shorter = SimWorld.Replay(a.Log, a.Tick - 1, initial: Prepared());
        Assert.NotEqual(a.Checksum(), shorter.Checksum());
    }

    [Fact]
    public void A_clone_keeps_projectiles_pickups_and_aim()
    {
        var w = World();
        Run(w, new SetAim(0, 0, -1024));
        Run(w, new SetWeapons(true));
        w.Step(15);
        Assert.True(w.Network.ProjectileCount > 0);
        var copy = w.Network.Clone();
        Assert.Equal(w.Network.Checksum(), copy.Checksum());
        Assert.Equal(w.Network.AimOf(0), copy.AimOf(0));
        Assert.Equal(w.Network.Projectiles(), copy.Projectiles());
    }

    [Fact]
    public void A_legacy_world_plays_a_version_16_log_exactly_as_recorded()
    {
        var legacy = new WorldRules().WithLegacyFeel(true).WithLegacyResources(true).WithLegacyField(true).WithLegacyDock(true); // a version 16 world had no rock field (ADR 0040) and no dock modules (ADR 0041)
        var recorded = World(legacy, threats: true);
        recorded.Network.ScheduleWave(0);
        var n = recorded.Network;
        int dep = n.AddDeposit(Ore, 400, 0, 0, -20);
        recorded.Submit(0, new SetWeapons(true));
        recorded.Submit(0, new StartMining(dep));
        recorded.Step(3000);
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: true, structures: true);
        var bytes = ms.ToArray();
        bytes[4] = 16;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.True(rules.LegacyFeel && rules.LegacyResources);
        Assert.True(rules.HashesAsDefault);
        // The log has no deposit command, so the world rebuilds with it by hand: what matters is that the rules decide the state.
        var replay = World(rules, threats: true);
        replay.Network.ScheduleWave(0);
        replay.Network.AddDeposit(Ore, 400, 0, 0, -20);
        foreach (var e in log) replay.Submit(e.Player, e.Command);
        replay.Step(3000);
        Assert.Equal(recorded.Checksum(), replay.Checksum());
        // And it plays the old way: no aim, no pickups, no projectiles, nothing regrown.
        Assert.Equal(0, replay.Network.ProjectileCount + replay.Network.PickupCount);
        Assert.Equal(0, replay.Network.RegrownTotal);
    }

    // ---- hold overflow ---------------------------------------------------------------------------------------------------

    private static (SimWorld W, int Deposit) MiningWorld(int amount, WorldRules? rules = null)
    {
        var w = World(rules);
        var laser = w.Network.MiningLasers(0).First();
        int dep = w.Network.AddDeposit(Ore, amount, (int)(laser.X / Cell), (int)(laser.Y / Cell) - 6, (int)(laser.Z / Cell) - 4, density: 100);
        return (w, dep);
    }

    private static void Fill(SimWorld w, int spare = 0)
    {
        int room = w.Network.HoldCapacity(0) - w.Network.HoldTotal(0) - spare;
        if (room > 0) w.Network.GiveItems(0, Carbon, room);
    }

    [Fact]
    public void The_starter_ship_holds_four_hundred_and_mines_two_and_a_half_times_as_fast()
    {
        var (w, dep) = MiningWorld(1000);
        Assert.Equal(400, w.Network.HoldCapacity(0));
        Assert.Equal(6, w.Network.MineTicksFor(0, dep)); // 15 ticks at 250 percent
        Assert.Equal(15, w.Network.MineTicksFor(dep));   // the base rate is unchanged
    }

    [Fact]
    public void Mining_with_a_full_hold_still_breaks_the_rock_and_the_surplus_floats()
    {
        var (w, dep) = MiningWorld(30);
        Fill(w);
        Assert.Equal(w.Network.HoldCapacity(0), w.Network.HoldTotal(0));
        Assert.Equal(Reject.None, Run(w, new StartMining(dep)));
        int created = 0;
        for (int i = 0; i < 2000 && w.Network.DepositRemaining(dep) > 0; i++)
        {
            w.Step();
            created += w.Network.PickupEvents.Count(e => e.Kind == PickupEventKind.Created);
        }
        Assert.Equal(0, w.Network.DepositRemaining(dep));                   // the rock broke
        Assert.Equal(1, created);                                           // one toast per rock
        Assert.Equal(w.Network.HoldCapacity(0), w.Network.HoldTotal(0));    // nothing squeezed in
        var pickup = Assert.Single(w.Network.Pickups());
        Assert.Equal((Ore, dep), (pickup.Item, pickup.Source));
        Assert.Equal(30, pickup.Count + w.Network.HoldCount(0, Ore)); // the reactor burns a little hydrogen, which makes room for what the tractor takes in
        Assert.Equal(30, w.Network.MinedByHand);
        w.ValidateInvariants();
    }

    [Fact]
    public void Nothing_floats_while_the_hold_has_room_and_a_legacy_world_still_stops()
    {
        var (w, dep) = MiningWorld(30);
        Run(w, new StartMining(dep));
        w.Step(1500);
        Assert.Equal(0, w.Network.DepositRemaining(dep));
        Assert.Equal(30, w.Network.HoldCount(0, Ore));
        Assert.Equal(0, w.Network.PickupCount);

        var (old, odep) = MiningWorld(30, new WorldRules().WithLegacyFeel(true));
        Fill(old);
        Run(old, new StartMining(odep));
        old.Step(1500);
        Assert.Equal(30, old.Network.DepositRemaining(odep));               // the hold is full: the old laser waits
        Assert.Equal(0, old.Network.PickupCount);
        old.ValidateInvariants();
    }

    [Fact]
    public void Past_the_soft_limit_the_laser_slows_down_and_drones_still_wait()
    {
        var (w, dep) = MiningWorld(1000);
        int cap = w.Network.HoldCapacity(0);
        Assert.Equal(6, w.Network.MineTicksNow(0, dep));
        Assert.False(w.Network.HandMiningSlowed(0));
        Fill(w, spare: cap - cap * PlayerRules.MiningHoldLimitPercent / 100);   // exactly at the limit
        Assert.True(w.Network.HandMiningSlowed(0));
        Assert.Equal(6 * FeelRules.SoftHoldPenalty, w.Network.MineTicksNow(0, dep));
        Run(w, new StartMining(dep));
        int before = w.Network.HoldTotal(0);
        w.Step(24 * 5);
        Assert.InRange(w.Network.HoldTotal(0) - before, 3, 6);                   // about five items in 120 ticks instead of twenty
        w.ValidateInvariants();
    }

    [Fact]
    public void Pickups_are_collected_when_there_is_room_and_fade_when_there_is_not()
    {
        var (w, dep) = MiningWorld(20);
        Fill(w);
        Run(w, new StartMining(dep));
        w.Step(1000);
        Assert.Equal(20, w.Network.Pickups().Sum(p => p.Count) + w.Network.HoldCount(0, Ore)); // some hydrogen burnt: a little room
        int ore = w.Network.HoldCount(0, Ore);

        // Room opens: the ship is close, so the tractor takes the lot.
        Assert.Equal(Reject.None, Run(w, new DumpItems(Carbon, 60)));
        w.Step(300);
        Assert.True(w.Network.PickupCount == 0, string.Join(";", w.Network.Pickups()) + " hold " + w.Network.HoldTotal(0) + "/" + w.Network.HoldCapacity(0) + " ore " + w.Network.HoldCount(0, Ore));
        Assert.Equal(20, w.Network.HoldCount(0, Ore)); // all of it, what floated and what had made room for itself
        w.ValidateInvariants();

        // A second heap with the hold still full fades after its lifetime and is lost.
        var (w2, dep2) = MiningWorld(20);
        Fill(w2);
        Run(w2, new StartMining(dep2));
        w2.Step(1000);
        Assert.True(w2.Network.PickupCount > 0);
        bool expired = false;
        for (int i = 0; i < FeelRules.PickupLifeTicks + 10 && !expired; i++) { w2.Step(); expired = w2.Network.PickupEvents.Any(e => e.Kind == PickupEventKind.Expired); }
        Assert.True(expired);
        Assert.Equal(0, w2.Network.PickupCount);
        w2.ValidateInvariants();
    }

    // ---- regrowth --------------------------------------------------------------------------------------------------------

    private static void FlyAway(SimWorld w, int cells = 200)
    {
        int ship = w.Network.ShipStructure(0);
        var o = w.Network.GetStructure(ship);
        w.Network.SetStructureOrigin(ship, o.OriginX + (long)cells * Cell, o.OriginY, o.OriginZ);
    }

    [Fact]
    public void An_emptied_deposit_regrows_a_little_after_a_long_delay_and_never_in_a_legacy_world()
    {
        var (w, dep) = MiningWorld(40);
        Run(w, new StartMining(dep));
        for (int i = 0; i < 2000 && w.Network.DepositRemaining(dep) > 0; i++) w.Step();
        FlyAway(w);
        Assert.Equal(0, w.Network.DepositRemaining(dep));
        w.Step(FeelRules.RegrowDelayTicks - 30);
        Assert.Equal(0, w.Network.DepositRemaining(dep));                       // not yet
        w.Step(60);
        Assert.Equal(FeelRules.RegrowMinimum, w.Network.DepositRemaining(dep)); // 12 percent of 40 is 4: the floor of 10 applies
        Assert.Equal(FeelRules.RegrowMinimum, w.Network.RegrownTotal);
        w.ValidateInvariants();                                                  // extracted + regrown = mined

        // It can be mined again and regrows again.
        FlyAway(w, -200); // back to the rock
        Run(w, new StartMining(dep));
        for (int i = 0; i < 2000 && w.Network.DepositRemaining(dep) > 0; i++) w.Step();
        FlyAway(w);
        Assert.Equal(0, w.Network.DepositRemaining(dep));
        w.Step(FeelRules.RegrowDelayTicks + 10);
        Assert.Equal(2L * FeelRules.RegrowMinimum, w.Network.RegrownTotal);
        w.ValidateInvariants();

        var (old, odep) = MiningWorld(40, new WorldRules().WithLegacyResources(true));
        Run(old, new StartMining(odep));
        old.Step(2000 + FeelRules.RegrowDelayTicks + 100);
        Assert.Equal(0, old.Network.DepositRemaining(odep));
        Assert.Equal(0, old.Network.RegrownTotal);
        old.ValidateInvariants();
    }

    [Fact]
    public void A_rock_does_not_regrow_inside_a_ship_but_waits_until_it_is_clear()
    {
        var (w, dep) = MiningWorld(40);
        Run(w, new StartMining(dep));
        for (int i = 0; i < 2000 && w.Network.DepositRemaining(dep) > 0; i++) w.Step();
        Assert.Equal(0, w.Network.DepositRemaining(dep));
        w.Step(FeelRules.RegrowDelayTicks + 3 * FeelRules.RegrowRetryTicks); // the ship still sits beside the old rock
        Assert.Equal(0, w.Network.DepositRemaining(dep));
        Assert.Equal(0, w.Network.RegrownTotal);
        w.ValidateInvariants();
        FlyAway(w);
        w.Step(2 * FeelRules.RegrowRetryTicks);
        Assert.Equal(FeelRules.RegrowMinimum, w.Network.DepositRemaining(dep)); // clear: it comes back
        w.ValidateInvariants();
    }

    [Fact]
    public void A_big_deposit_regrows_twelve_percent_and_the_timer_is_part_of_the_state()
    {
        var (w, dep) = MiningWorld(250);
        Run(w, new StartMining(dep));
        for (int i = 0; i < 9000 && w.Network.DepositRemaining(dep) > 0; i++) w.Step();
        Assert.Equal(0, w.Network.DepositRemaining(dep));
        FlyAway(w);
        w.Step(10);
        ulong counting = w.Checksum();
        w.Step(FeelRules.RegrowDelayTicks + 20);
        Assert.Equal(30, w.Network.DepositRemaining(dep)); // 12 percent of 250
        Assert.NotEqual(counting, w.Checksum());
        w.ValidateInvariants();
    }

    // ---- the resource density rule ------------------------------------------------------------------------------------------

    [Fact]
    public void A_density_of_one_hundred_is_the_base_galaxy_and_more_density_only_appends_deposits()
    {
        var baseline = Galaxy.Generate(C, 20261121UL);
        var same = Galaxy.Generate(C, 20261121UL, resourceDensityPercent: 100);
        Assert.Equal(baseline.Deposits, same.Deposits);
        Assert.Equal(baseline.Obstacles, same.Obstacles);
        Assert.Equal(baseline.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)), same.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)));

        var more = Galaxy.Generate(C, 20261121UL, resourceDensityPercent: 140);
        Assert.Equal(baseline.Deposits, more.Deposits.Take(baseline.Deposits.Count).ToList()); // the ids and the numbers of the base deposits do not move
        Assert.Equal(baseline.Obstacles, more.Obstacles);
        Assert.True(more.Deposits.Count > baseline.Deposits.Count * 12 / 10, $"{more.Deposits.Count} against {baseline.Deposits.Count}");
        Assert.True(more.Deposits.Count < baseline.Deposits.Count * 25 / 10, $"{more.Deposits.Count} against {baseline.Deposits.Count}");
        Assert.Equal(more.Deposits, Galaxy.Generate(C, 20261121UL, resourceDensityPercent: 140).Deposits); // deterministic

        // Every extra keeps clear of the others of its system and sits inside the world.
        foreach (var sys in more.Systems)
        {
            var mine = more.Deposits.Select((d, i) => (d, i)).Where(x => x.d.System == sys.Id && x.i >= baseline.Deposits.Count).ToList();
            foreach (var (d, i) in mine)
                foreach (var (o, j) in more.Deposits.Select((o, j) => (o, j)).Where(x => x.o.System == sys.Id && x.j < i))
                {
                    long dx = d.X - o.X, dy = d.Y - o.Y, dz = d.Z - o.Z;
                    Assert.True(dx * dx + dy * dy + dz * dz >= (long)ResourceGeneratorClear * ResourceGeneratorClear, $"extra deposit {i} is too close to {j}");
                }
        }

        // The home system keeps a station site: the bot of ADR 0036 measures this, the rule keeps the starting vein's neighbourhood free.
        var home = more.Deposits.Select((d, i) => (d, i)).Where(x => x.d.System == 0 && x.i >= baseline.Deposits.Count);
        foreach (var (d, _) in home)
        {
            long dx = d.X - baseline.Deposits[0].X, dy = d.Y - baseline.Deposits[0].Y, dz = d.Z - baseline.Deposits[0].Z;
            Assert.True(dx * dx + dy * dy + dz * dz >= (long)ResourceGeneratorClear * ResourceGeneratorClear, "a home extra crowds the starting vein");
        }
    }

    private const int ResourceGeneratorClear = 44;

    [Fact]
    public void The_legacy_resources_flag_regenerates_the_old_world_byte_for_byte_and_the_density_scales_the_world()
    {
        var old = World(WorldRules.Normal.WithLegacyResources(true));
        var oldDensity100 = World(new WorldRules { ResourceDensityPercent = 100 }.WithLegacyResources(true));
        var baseGalaxy = Galaxy.Generate(C, Seed);
        Assert.Equal(baseGalaxy.Deposits.Count, old.Network.DepositCount);
        Assert.Equal(old.Checksum(), oldDensity100.Checksum());            // the flag ignores the density, and both hash as default rules
        Assert.True(old.Network.Rules.HashesAsDefault);

        var normal = World(WorldRules.Normal);
        var relaxed = World(WorldRules.Relaxed);
        var hard = World(WorldRules.Hard);
        var brutal = World(WorldRules.Brutal);
        Assert.True(relaxed.Network.DepositCount > normal.Network.DepositCount);
        Assert.True(normal.Network.DepositCount > hard.Network.DepositCount);
        Assert.True(hard.Network.DepositCount > brutal.Network.DepositCount);
        Assert.Equal(baseGalaxy.Deposits.Count, brutal.Network.DepositCount); // Brutal's 100 is the base galaxy
        Assert.Equal(World(WorldRules.Normal).Checksum(), normal.Checksum());
        Assert.Equal(new WorldRules().ResourceDensityPercent, WorldRules.Normal.ResourceDensityPercent);
    }

    [Fact]
    public void World_rules_carry_the_density_and_the_two_flags_through_the_codec_and_keep_the_old_bytes()
    {
        Assert.Equal((150, 140, 120, 100), (WorldRules.Relaxed.ResourceDensityPercent, WorldRules.Normal.ResourceDensityPercent, WorldRules.Hard.ResourceDensityPercent, WorldRules.Brutal.ResourceDensityPercent));
        Assert.True(WorldRules.Default.Equals(WorldRules.Normal));
        foreach (var r in new[] { WorldRules.Relaxed, WorldRules.Hard, WorldRules.Brutal, new WorldRules { FreeBuild = true, ResourceDensityPercent = 90 }, WorldRules.Hard.WithLegacyFeel(true), WorldRules.Hard.WithLegacyResources(true) })
            Assert.Equal(r, WorldRules.Deserialize(r.Serialize()));
        // A block in the format that had no density reads as density 100.
        byte[] oldForm = new WorldRules { ResourceDensityPercent = 100, GoalWaves = 7 }.Serialize();
        Assert.Equal(1, oldForm[0]);
        Assert.Equal(100, WorldRules.Deserialize(oldForm).ResourceDensityPercent);
        Assert.Equal(2, WorldRules.Hard.Serialize()[0]);
        Assert.NotNull(new WorldRules { ResourceDensityPercent = 10 }.Validate());
        Assert.NotNull(new WorldRules { ResourceDensityPercent = 400 }.Validate());
        Assert.Equal(WorldRules.MaxDensityPercent, new WorldRules { ResourceDensityPercent = 999 }.Clamped().ResourceDensityPercent);
    }
}
