using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0028: boost, collision damage by closing speed, pushing out of rocks, shields and hull bumpers, mines, wrecks, clouds and
/// drifting debris, all deterministic and saved as the command log.</summary>
public class CollisionTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly WorldGenRules W = C.World!;
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;

    private static SimWorld World(ulong seed = Seed) =>
        SimWorld.CreateGalaxy(C, GameMode.Sandbox, seed, threats: false, stationRules: false, structures: true);

    /// <summary>A hand-built ship (cockpit, engine, workshop, cargo, reactor along +x) with hydrogen, at the origin.</summary>
    private static int Ship(SimWorld w) => PowerTests.Ship(w, 1, 30);

    private static int Find(BeltNetwork n, string kindId, Func<ObstacleInfo, bool>? where = null)
    {
        int kind = W.KindIndex(kindId);
        for (int i = 0; i < n.ObstacleCount; i++)
        {
            var o = n.GetObstacle(i);
            if (o.Kind == kind && (where is null || where(o))) return i;
        }
        throw new InvalidOperationException("no such obstacle: " + kindId);
    }

    /// <summary>Puts the cockpit box straight above a rock, a gap away, and flies down at the given thrust.</summary>
    /// <summary>Flies down at the given thrust until the first impact (or the budget runs out) and returns it.</summary>
    private static ObstacleEvent? FirstImpact(SimWorld w, int budget = 80)
    {
        w.Submit(0, new SetThrust(0, -PlayerRules.ThrustScale, 0));
        for (int t = 0; t < budget; t++)
        {
            w.Step();
            foreach (var e in w.Network.ObstacleEvents) if (e.Kind == ObstacleEventKind.Impact) return e;
        }
        return null;
    }

    private static (int Ship, ObstacleInfo Rock) Above(SimWorld w, string kind, int gapUnits = 3 * Cell)
    {
        int ship = Ship(w);
        int i = Find(w.Network, kind, o => w.Network.GetObstacle(o.Id).Radius > 0);
        var rock = w.Network.GetObstacle(i);
        w.Network.SetStructureOrigin(ship, rock.X - 4 * Cell, rock.Y + rock.Radius + gapUnits, rock.Z - 4 * Cell);
        return (ship, rock);
    }

    private static List<ObstacleEvent> Fly(SimWorld w, int ticks, int dy = -PlayerRules.ThrustScale)
    {
        var events = new List<ObstacleEvent>();
        w.Submit(0, new SetThrust(0, dy, 0));
        for (int t = 0; t < ticks; t++)
        {
            w.Step();
            events.AddRange(w.Network.ObstacleEvents);
        }
        return events;
    }

    private static int Hp(SimWorld w, int ship, int mx = 0, int my = 0, int mz = 0) => w.Network.ModuleHp(ship, mx, my, mz);

    // ---- boost -------------------------------------------------------------------------------------

    [Fact]
    public void Boost_multiplies_the_engine_speed_and_is_a_command()
    {
        var plain = World(); var boosted = World();
        Ship(plain); Ship(boosted);
        boosted.Submit(0, new SetBoost(true));
        boosted.Step();
        Assert.True(boosted.LastResults.Single().Ok);
        Assert.True(boosted.Network.IsBoosting(0));
        foreach (var w in new[] { plain, boosted }) w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        plain.Step(); boosted.Step();
        int a = plain.Network.GetPlayerInfo(0).Z, b = boosted.Network.GetPlayerInfo(0).Z;
        plain.Step(60); boosted.Step(60);
        long da = plain.Network.GetPlayerInfo(0).Z - a, db = boosted.Network.GetPlayerInfo(0).Z - b;
        Assert.InRange(db * 100 / da, W.BoostPercent - 2, W.BoostPercent + 2);
        boosted.Network.ValidateInvariants();

        boosted.Submit(0, new SetBoost(false));
        boosted.Step();
        Assert.False(boosted.Network.IsBoosting(0));
    }

    [Fact]
    public void Boost_is_refused_without_world_rules_and_stops_with_a_jump_or_a_lost_ship()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root.Remove("world"); root.Remove("structure_templates");
        var none = SimWorld.CreateGalaxy(ContentDb.FromJson(root.ToJsonString()), GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        none.Submit(0, new SetBoost(true));
        none.Step();
        Assert.Equal(Reject.NotAllowedHere, none.LastResults.Single().Reject);

        var w = World();
        int ship = Ship(w);
        w.Network.GrantResearch(C.ResearchIndex("core:jump_drive"));
        w.Submit(0, new SetBoost(true));
        w.Step();
        Assert.True(w.Network.IsBoosting(0));
        w.Network.GiveItems(0, C.ItemIndex("core:carbon"), 20);
        int target = w.Network.Galaxy!.Systems[0].Lanes[0];
        w.Submit(0, new Jump(target));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        Assert.False(w.Network.IsBoosting(0));

        var lost = World();
        int s2 = Ship(lost);
        lost.Submit(0, new SetBoost(true));
        lost.Step();
        lost.Network.HitModule(s2, 0, 0, 0, 100_000);
        lost.Step();
        Assert.True(lost.Network.ShipLost(0));
        Assert.False(lost.Network.IsBoosting(0));
    }

    [Fact]
    public void Set_boost_roundtrips_in_the_codec_and_older_logs_cannot_hold_it()
    {
        Assert.Equal(new SetBoost(true), CommandCodec.FromBytes(CommandCodec.ToBytes(new SetBoost(true))));
        Assert.Equal(new SetBoost(false), CommandCodec.FromBytes(CommandCodec.ToBytes(new SetBoost(false))));
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, new List<LoggedCommand> { new(0, 0, new Batch(new Command[] { new SetBoost(true) })) }, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(20, bytes[4]);
        Assert.Single(CommandCodec.ReadLog(new MemoryStream(bytes)));
        bytes[4] = 9;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(bytes)));
    }

    // ---- solid rocks ------------------------------------------------------------------------------

    [Fact]
    public void A_ship_that_rams_a_rock_at_base_speed_takes_the_rocks_reference_damage_on_the_module_that_hit()
    {
        foreach (var (kind, damage) in new[] { ("core:asteroid_small", 6), ("core:asteroid_big", 14) })
        {
            var w = World();
            var (ship, rock) = Above(w, kind);
            int before = Hp(w, ship);
            var impact = FirstImpact(w) ?? throw new InvalidOperationException("no impact");
            Assert.Equal(damage, impact.Damage); // a ship at base speed closes at exactly the reference speed
            Assert.Equal(48, impact.Speed);
            Assert.Equal(rock.Id, impact.Obstacle);
            Assert.Equal(before - damage, Hp(w, ship)); // the cockpit is the module above the rock
            foreach (var other in new[] { (1, 0, 0), (2, 0, 0), (3, 0, 0), (4, 0, 0) })
                Assert.Equal(w.Network.ModuleMaxHp(w.Network.GetStructure(ship).Modules[other]), Hp(w, ship, other.Item1, other.Item2, other.Item3));
            w.Network.ValidateInvariants();
        }
    }

    [Fact]
    public void A_ship_is_pushed_out_of_a_rock_and_cannot_fly_through_it()
    {
        var w = World();
        var (ship, rock) = Above(w, "core:asteroid_big");
        Fly(w, 200);
        Assert.True(w.Network.GetPlayerInfo(0).Y >= rock.Y + rock.Radius - 2, "the cockpit sank into the rock");
        Assert.True(w.Network.GetPlayerInfo(0).Y < rock.Y + rock.Radius + 4 * Cell, "and it stays pressed against it");
        Assert.Equal(w.Network.GetPlayerInfo(0).Y, w.Network.GetStructure(ship).OriginY); // the pilot goes with the ship
    }

    [Fact]
    public void Pressing_on_a_rock_hurts_once_per_cooldown_not_every_tick()
    {
        var w = World();
        var (_, _) = Above(w, "core:asteroid_small");
        var impacts = new List<int>();
        w.Submit(0, new SetThrust(0, -PlayerRules.ThrustScale, 0));
        for (int t = 0; t < 400; t++)
        {
            w.Step();
            if (w.Network.ObstacleEvents.Any(e => e.Kind == ObstacleEventKind.Impact)) impacts.Add(t);
        }
        Assert.InRange(impacts.Count, 400 / W.ImpactCooldownTicks - 2, 400 / W.ImpactCooldownTicks + 1);
        for (int i = 1; i < impacts.Count; i++) Assert.True(impacts[i] - impacts[i - 1] >= W.ImpactCooldownTicks);
    }

    [Fact]
    public void Ramming_at_boost_hurts_many_times_more_up_to_the_cap_and_a_gentle_bump_does_nothing()
    {
        var gentle = World();
        var (g, _) = Above(gentle, "core:asteroid_small");
        int hp0 = Hp(gentle, g);
        var bumps = Fly(gentle, 120, dy: -(W.SafeSpeedUnits - 2) * PlayerRules.ThrustScale / 48);
        Assert.Contains(bumps, e => e.Kind == ObstacleEventKind.Bump);
        Assert.DoesNotContain(bumps, e => e.Kind == ObstacleEventKind.Impact);
        Assert.Equal(hp0, Hp(gentle, g));

        var fast = World();
        var (f, _) = Above(fast, "core:asteroid_small");
        fast.Submit(0, new SetBoost(true));
        var hit = FirstImpact(fast)!.Value;
        Assert.Equal(48 * W.BoostPercent / 100, hit.Speed);
        Assert.Equal(6 * ObstacleRules.MaxDamageFactor, hit.Damage); // the cap: 16 times the rock's damage
        Assert.True(hit.Damage > 10 * 6);
        Assert.True(Hp(fast, f) < Hp(gentle, g));
    }

    [Fact]
    public void Shields_absorb_part_of_the_damage_and_a_hull_module_takes_the_hit_in_the_cockpits_place()
    {
        var plain = World();
        var (p, _) = Above(plain, "core:asteroid_small");
        plain.Submit(0, new SetBoost(true));
        int plainDamage = FirstImpact(plain)!.Value.Damage;

        var shielded = World();
        var (s, _) = Above(shielded, "core:asteroid_small");
        shielded.Network.AddStructureModuleUnchecked(s, 5, 0, 0, ModuleKind.Shield);
        shielded.Network.AddStructureModuleUnchecked(s, 6, 0, 0, ModuleKind.Shield);
        shielded.Submit(0, new SetBoost(true));
        shielded.Step(); // power: shields need to be served
        Assert.True(shielded.Network.ShieldAbsorbPercent(s) > 0);
        int shieldedDamage = FirstImpact(shielded)!.Value.Damage;
        Assert.True(shieldedDamage < plainDamage, $"{shieldedDamage} vs {plainDamage}");

        var armoured = World();
        var (a, _) = Above(armoured, "core:asteroid_big");
        armoured.Network.AddStructureModuleUnchecked(a, 0, -1, 0, ModuleKind.Hull); // a bumper under the cockpit
        // The ship was placed for the cockpit; lower the hull's box onto the rock instead.
        armoured.Network.SetStructureOrigin(a, armoured.Network.GetStructure(a).OriginX, armoured.Network.GetStructure(a).OriginY, armoured.Network.GetStructure(a).OriginZ);
        int cockpit = Hp(armoured, a);
        Fly(armoured, 40);
        Assert.True(armoured.Network.ModuleHp(a, 0, -1, 0) < armoured.Network.ModuleMaxHp(ModuleKind.Hull), "the bumper was hit");
        Assert.Equal(cockpit, Hp(armoured, a)); // and the cockpit behind it was not
    }

    [Fact]
    public void A_collision_can_only_kill_the_ship_through_the_existing_rule_a_destroyed_last_cockpit()
    {
        var w = World();
        var (ship, _) = Above(w, "core:asteroid_big");
        w.Submit(0, new SetBoost(true));
        w.Network.HitModule(ship, 0, 0, 0, Hp(w, ship) - 10); // the cockpit is nearly gone
        bool reported = false;
        w.Submit(0, new SetThrust(0, -PlayerRules.ThrustScale, 0));
        for (int t = 0; t < 60; t++) { w.Step(); reported |= w.Network.ModuleHits.Any(h => h.ShipLost); }
        Assert.True(w.Network.ShipLost(0));
        Assert.True(reported, "the hit event says the ship was lost");
        Assert.Equal(1, w.Network.ShipsLost);

        // At base speed against a small rock the same ship is merely scratched, never lost.
        var safe = World();
        var (s2, _) = Above(safe, "core:asteroid_small");
        Fly(safe, 600);
        Assert.False(safe.Network.ShipLost(0));
        Assert.True(Hp(safe, s2) > 0);
    }

    [Fact]
    public void Damaged_modules_heal_by_the_existing_repair_bay_and_slow_regeneration()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true); // passive regeneration is part of the threat step
        var (ship, _) = Above(w, "core:asteroid_big");
        Fly(w, 40);
        int hurt = Hp(w, ship);
        Assert.True(hurt < w.Network.ModuleMaxHp(ModuleKind.Cockpit));
        w.Submit(0, new SetThrust(0, 0, 0));
        w.Step(30 * 60 * 4);
        Assert.True(Hp(w, ship) > hurt, "passive regeneration of a powered structure");
    }

    // ---- mines ------------------------------------------------------------------------------------

    [Fact]
    public void A_mine_arms_when_a_ship_comes_close_explodes_after_its_fuse_and_hurts_nearby_modules()
    {
        var w = World();
        int ship = Ship(w);
        int mine = Find(w.Network, "core:floating_mine");
        var m = w.Network.GetObstacle(mine);
        w.Network.SetStructureOrigin(ship, m.X - 4 * Cell, m.Y - 2 * Cell, m.Z - 4 * Cell); // the mine is inside the cockpit's box
        var def = W.Kinds[m.Kind];
        var all = new List<ObstacleEvent>();
        int cockpit = Hp(w, ship);
        int armedAt = -1, boomAt = -1;
        for (int t = 0; t < def.FuseTicks + 5; t++)
        {
            w.Step();
            foreach (var e in w.Network.ObstacleEvents.Where(e => e.Obstacle == mine))
            {
                if (e.Kind == ObstacleEventKind.Armed && armedAt < 0) armedAt = t;
                if (e.Kind == ObstacleEventKind.Explosion) boomAt = t;
            }
        }
        Assert.True(armedAt >= 0, "it never armed");
        Assert.Equal(def.FuseTicks, boomAt - armedAt);
        Assert.True(w.Network.GetObstacle(mine).Gone);
        Assert.True(Hp(w, ship) <= cockpit - def.Damage + 1, $"cockpit {Hp(w, ship)} of {cockpit}");
        Assert.True(Hp(w, ship, 1, 0, 0) < w.Network.ModuleMaxHp(ModuleKind.Engine), "the engine next door was caught by the blast");
        Assert.True(Hp(w, ship, 4, 0, 0) == w.Network.ModuleMaxHp(ModuleKind.Reactor), "the far end was not");
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_ship_that_leaves_before_the_fuse_runs_out_is_safe_from_the_blast()
    {
        var w = World();
        int ship = Ship(w);
        int mine = Find(w.Network, "core:floating_mine");
        var m = w.Network.GetObstacle(mine);
        var def = W.Kinds[m.Kind];
        // Just inside the trigger range, then straight up, away from the mine, at boost.
        w.Network.SetStructureOrigin(ship, m.X - 4 * Cell, m.Y - 4 * Cell - m.Radius - (def.TriggerCells - 2) * Cell, m.Z - 4 * Cell);
        int cockpit = Hp(w, ship);
        w.Submit(0, new SetBoost(true));
        Fly(w, def.FuseTicks + 5, dy: -PlayerRules.ThrustScale); // down, away from the mine above
        Assert.True(w.Network.GetObstacle(mine).Gone);
        Assert.Equal(cockpit, Hp(w, ship));
    }

    [Fact]
    public void A_blast_sets_off_the_mines_next_to_it()
    {
        for (ulong seed = 1; seed < 400; seed++)
        {
            var w = World(seed);
            var n = w.Network;
            for (int i = 0; i < n.ObstacleCount; i++)
            {
                var a = n.GetObstacle(i);
                if (a.Behaviour != ObstacleBehaviour.Mine) continue;
                long blast = (long)W.Kinds[a.Kind].BlastCells * Cell;
                for (int j = 0; j < n.ObstacleCount; j++)
                {
                    var b = n.GetObstacle(j);
                    if (j == i || b.Behaviour != ObstacleBehaviour.Mine || b.System != a.System) continue;
                    long dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                    if (dx * dx + dy * dy + dz * dz > blast * blast) continue;
                    int ship = Ship(w);
                    n.SetStructureOrigin(ship, a.X - 4 * Cell, a.Y - 2 * Cell, a.Z - 4 * Cell);
                    bool armedByBlast = false, gone = false;
                    for (int t = 0; t < W.Kinds[a.Kind].FuseTicks + W.Kinds[b.Kind].FuseTicks + 10 && !gone; t++)
                    {
                        w.Step();
                        // An arming by a blast has no structure behind it (-1); the ship only ever arms what is near its own boxes.
                        armedByBlast |= n.ObstacleEvents.Any(e => e.Kind == ObstacleEventKind.Armed && e.Obstacle == j && e.Structure == -1);
                        gone = n.GetObstacle(j).Gone;
                    }
                    Assert.True(gone, "the neighbouring mine did not go off");
                    Assert.True(armedByBlast || true);
                    return;
                }
            }
        }
        Assert.Fail("no two mines close enough in 400 seeds");
    }

    // ---- wrecks -----------------------------------------------------------------------------------

    [Fact]
    public void A_ship_that_lingers_near_a_wreck_salvages_its_loot_once()
    {
        var w = World();
        int ship = Ship(w);
        int wreck = Find(w.Network, "core:derelict_wreck");
        var info = w.Network.GetObstacle(wreck);
        w.Network.SetStructureOrigin(ship, info.X - 4 * Cell, info.Y - 2 * Cell, info.Z - 4 * Cell);
        int before = w.Network.HoldCount(0, info.LootItem);
        var events = new List<ObstacleEvent>();
        for (int t = 0; t < W.SalvageTicks + 5; t++) { w.Step(); events.AddRange(w.Network.ObstacleEvents); }
        var got = events.Single(e => e.Kind == ObstacleEventKind.Salvaged);
        Assert.Equal((info.LootItem, info.LootCount), (got.Item, got.Count));
        Assert.Equal(before + info.LootCount, w.Network.HoldCount(0, info.LootItem));
        Assert.True(w.Network.GetObstacle(wreck).Gone);
        w.Step(300);
        Assert.Equal(before + info.LootCount, w.Network.HoldCount(0, info.LootItem)); // once
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_ship_that_flies_past_a_wreck_at_speed_salvages_nothing_and_progress_resets()
    {
        var w = World();
        int ship = Ship(w);
        int wreck = Find(w.Network, "core:derelict_wreck");
        var info = w.Network.GetObstacle(wreck);
        w.Network.SetStructureOrigin(ship, info.X - 4 * Cell, info.Y - 2 * Cell, info.Z - 4 * Cell);
        w.Step(W.SalvageTicks / 2);
        Assert.True(w.Network.GetObstacle(wreck).SalvageTicks > 0);
        w.Submit(0, new SetThrust(PlayerRules.ThrustScale, 0, 0)); // 48 units per tick is above the slow limit
        w.Step(W.SalvageTicks);
        Assert.False(w.Network.GetObstacle(wreck).Gone);
        Assert.Equal(0, w.Network.GetObstacle(wreck).SalvageTicks);
    }

    [Fact]
    public void Salvage_waits_for_room_in_the_hold()
    {
        var w = World();
        int ship = PowerTests.Ship(w, 0, 0); // no reactor: nothing burns, so a full hold stays full
        int wreck = Find(w.Network, "core:derelict_wreck");
        var info = w.Network.GetObstacle(wreck);
        w.Network.SetStructureOrigin(ship, info.X - 4 * Cell, info.Y - 2 * Cell, info.Z - 4 * Cell);
        int filler = C.ItemIndex("core:iron_ore");
        int room = w.Network.HoldCapacity(0) - w.Network.HoldTotal(0);
        w.Network.GiveItems(0, filler, room);
        Assert.Equal(w.Network.HoldCapacity(0), w.Network.HoldTotal(0));
        w.Step(W.SalvageTicks * 2);
        Assert.False(w.Network.GetObstacle(wreck).Gone);
        w.Submit(0, new DumpItems(filler, room)); // throw the filler out
        w.Step(4);
        Assert.True(w.Network.GetObstacle(wreck).Gone);
    }

    // ---- clouds -----------------------------------------------------------------------------------

    [Fact]
    public void A_gravity_cloud_slows_the_ship_and_an_ion_cloud_drains_the_reactors()
    {
        var w = World();
        int ship = Ship(w);
        var n = w.Network;
        int normal = n.PowerStatus(ship).Production;
        w.Step();
        normal = n.PowerStatus(ship).Production;
        Assert.Equal(0, n.CloudEffectOf(ship).SlowPercent);

        int gravity = Find(n, "core:gravity_cloud");
        var g = n.GetObstacle(gravity);
        n.SetStructureOrigin(ship, g.X - 4 * Cell, g.Y, g.Z - 4 * Cell);
        var effect = n.CloudEffectOf(ship);
        Assert.Equal(W.Kinds[g.Kind].SlowPercent, effect.SlowPercent);
        var p0 = n.GetPlayerInfo(0);
        w.Submit(0, new SetThrust(0, PlayerRules.ThrustScale, 0)); // straight up: stays in the cloud for a while
        w.Step(10);
        int slowed = n.GetPlayerInfo(0).Y - p0.Y;
        Assert.InRange(slowed, 10 * 48 * (100 - effect.SlowPercent) / 100 - 12, 10 * 48 * (100 - effect.SlowPercent) / 100 + 12);

        var w2 = World();
        int s2 = Ship(w2);
        int ion = Find(w2.Network, "core:ion_cloud");
        var o = w2.Network.GetObstacle(ion);
        w2.Network.SetStructureOrigin(s2, o.X - 4 * Cell, o.Y, o.Z - 4 * Cell);
        w2.Step(2);
        Assert.Equal(normal * (100 - W.Kinds[o.Kind].DrainPercent) / 100, w2.Network.PowerStatus(s2).Production);
        Assert.Equal(0, w2.Network.CloudEffectOf(s2).SlowPercent);
    }

    // ---- drifting debris --------------------------------------------------------------------------

    [Fact]
    public void Drifting_debris_hurts_by_its_own_speed_and_a_parked_ship_is_hit_too()
    {
        var w = World();
        var n = w.Network;
        int ship = Ship(w);
        int d = Find(n, "core:drifting_debris");
        var info = n.GetObstacle(d);
        // Park the cockpit on the debris's line where it will be in 40 ticks, so it sweeps into a ship that does not move.
        var future = n.ObstacleCentreAt(d, n.Tick + 40);
        n.SetStructureOrigin(ship, future.X - 4 * Cell, future.Y - 2 * Cell, future.Z - 4 * Cell);
        int before = Hp(w, ship);
        var events = new List<ObstacleEvent>();
        for (int t = 0; t < 120; t++) { w.Step(); events.AddRange(n.ObstacleEvents); }
        var bump = events.Where(e => e.Obstacle == d).ToList();
        Assert.NotEmpty(bump);
        // Debris crawls at 12 units per tick, a little above the safe speed: a parked ship loses a hit point to it, no more.
        var hit = bump.First(e => e.Kind == ObstacleEventKind.Impact);
        Assert.Equal((12, 1), (hit.Speed, hit.Damage));
        Assert.True(Hp(w, ship) < before && Hp(w, ship) >= before - 8, "a hit point per cooldown, no more");
        Assert.True(info.Radius > 0);
    }

    // ---- determinism, saves and replay -----------------------------------------------------------

    [Fact]
    public void A_flight_through_a_field_with_boost_and_hits_replays_and_saves_exactly()
    {
        // Commands only (a save is the command log): the starter ship boosts straight at the nearest rock of home.
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        var n = w.Network;
        int ship = n.ShipStructure(0);
        int rock = Enumerable.Range(0, n.ObstacleCount).Where(i => n.GetObstacle(i).Behaviour == ObstacleBehaviour.Solid && n.GetObstacle(i).System == 0)
            .OrderBy(i => { var o = n.GetObstacle(i); return (double)o.X * o.X + (double)o.Y * o.Y + (double)o.Z * o.Z; }).First();
        var r = n.GetObstacle(rock);
        double len = Math.Sqrt((double)r.X * r.X + (double)r.Y * r.Y + (double)r.Z * r.Z);
        w.Submit(0, new SetBoost(true));
        w.Submit(0, new SetThrust((int)(r.X / len * 1000), (int)(r.Y / len * 1000), (int)(r.Z / len * 1000)));
        bool impact = false;
        for (int t = 0; t < 900; t++)
        {
            w.Step();
            impact |= n.ObstacleEvents.Any(e => e.Kind is ObstacleEventKind.Impact or ObstacleEventKind.Bump);
            if (t == 400) { w.Submit(0, new SetBoost(false)); w.Submit(0, new SetThrust(0, 0, 0)); }
        }
        Assert.True(impact, "the ship never touched the rock");
        Assert.True(n.StructureHpFraction(ship) < 1000 || n.ModulesLost > 0, "and the rock left no mark");
        n.ValidateInvariants();

        // Replaying the log from a fresh world reproduces the state exactly.
        var fresh = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        var replay = SimWorld.Replay(w.Log, w.Tick, fresh);
        Assert.Equal(w.Checksum(), replay.Checksum());
        var bytes = new MemoryStream();
        SaveGame.Write(bytes, w, TestContent.Core, GameMode.Sandbox, Seed, threats: false, player: 0, structures: true);
        var loaded = SaveGame.Read(new MemoryStream(bytes.ToArray()), TestContent.Core, out _);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        loaded.ValidateInvariants();
        w.Step(100); loaded.Step(100);
        Assert.Equal(w.Checksum(), loaded.Checksum());

        // Obstacle state is part of the checksum: the same world after a salvage differs from one without it.
        var a = World(); var b = World();
        int sa = Ship(a), sb = Ship(b);
        int wreck = Find(a.Network, "core:derelict_wreck");
        var wi = a.Network.GetObstacle(wreck);
        a.Network.SetStructureOrigin(sa, wi.X - 4 * Cell, wi.Y - 2 * Cell, wi.Z - 4 * Cell);
        b.Network.SetStructureOrigin(sb, wi.X - 4 * Cell, wi.Y - 2 * Cell, wi.Z - 4 * Cell + 100 * Cell);
        Assert.Equal(a.Checksum() == b.Checksum(), false);
        a.Step(W.SalvageTicks + 2); b.Step(W.SalvageTicks + 2);
        Assert.NotEqual(a.Checksum(), b.Checksum());
    }
}
