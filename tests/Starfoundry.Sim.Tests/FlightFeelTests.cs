using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0033: flight with inertia (a thrust sets a target velocity that the velocity approaches under acceleration and braking limits), solid
/// deposits, hit volumes that follow the drawn size plus the hull padding, and the research that raises the engines. Every rule has a legacy
/// flag so logs of versions before 15 replay as recorded.
/// </summary>
public class FlightFeelTests
{
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;

    /// <summary>The legacy test content (no starter ship) with the ADR 0033 sections of the shipped content added: padding, solid deposits, hit sizes and inertia.</summary>
    private static readonly ContentDb C = Build(collisions: true, flight: true);

    /// <summary>The same with no rocks or hazards at all in the stars (the flight tests fly through open space).</summary>
    private static readonly ContentDb Clean = Build(collisions: true, flight: true, empty: true);

    /// <summary>The same content without any of it: what the old logs were recorded on.</summary>
    private static readonly ContentDb Old = Build(collisions: false, flight: false);

    /// <summary>The old content with a clean sky: what the clean flights are compared with.</summary>
    private static readonly ContentDb OldClean = Build(collisions: false, flight: false, empty: true);

    private static readonly WorldGenRules W = C.World!;

    private static ContentDb Build(bool collisions, bool flight, bool empty = false)
    {
        var root = JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root.Remove("structure_templates");
        var world = root["world"]!.AsObject();
        if (collisions)
        {
            var col = world["collision"]!.AsObject();
            col["hull_pad_units"] = 120; col["deposits_solid"] = true; col["deposit_damage"] = 6; col["deposit_hit_percent"] = 150;
            foreach (var k in world["obstacles"]!.AsArray())
            {
                string b = k!["behaviour"]!.GetValue<string>();
                if (b == "solid") k["hit_percent"] = 155;
                if (b == "drifter") k["hit_percent"] = 110;
            }
        }
        if (flight) world["flight"] = new JsonObject { ["accel_ticks"] = 36, ["brake_ticks"] = 18 };
        if (empty)
        {
            foreach (var st in world["star_types"]!.AsArray()) { st!["asteroid_percent"] = 0; st["hazard_percent"] = 0; }
            world["collision"]!.AsObject()["deposits_solid"] = false; // the deposits of the home system lie 36 cells out: the flights go through them
        }
        return ContentDb.FromJson(root.ToJsonString());
    }

    private static SimWorld World(ContentDb? content = null, WorldRules? rules = null, ulong seed = Seed) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, seed, threats: false, stationRules: false, structures: true, rules: (rules ?? WorldRules.Default).WithLegacyBlueprint(true).WithLegacyInterior(true));

    /// <summary>A hand-built ship (cockpit, engine, workshop, cargo, reactor and a mining laser) at the origin with hydrogen.</summary>
    private static int Ship(SimWorld w) => PowerTests.Ship(w, 1, 30);

    /// <summary>A world in open space with a ship at the origin, the unlogged setup the scripts start from.</summary>
    private static SimWorld Flyer(WorldRules? rules = null, ContentDb? content = null) { var w = World(content ?? Clean, rules); Ship(w); return w; }

    private static (int X, int Y, int Z) Vel(SimWorld w) => w.Network.ShipVelocityMilli(0);

    private static long Speed((int X, int Y, int Z) v) => (long)Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);

    // ---- the ramp -----------------------------------------------------------------------------------

    [Fact]
    public void Content_defines_inertia_and_the_shipped_content_does_too()
    {
        Assert.True(W.HasInertia);
        Assert.Equal(36, W.AccelTicks);
        Assert.Equal(18, W.BrakeTicks);
        Assert.False(Old.World!.HasInertia);
        var shipped = TestContent.CoreFloor.World!;
        Assert.True(shipped.HasInertia);
        Assert.True(shipped.BrakeTicks < shipped.AccelTicks, "braking is stronger than accelerating");
        Assert.True(shipped.DepositsSolid && shipped.HullPadUnits >= 120 && shipped.HullPadUnits <= 240);
        foreach (var k in shipped.Kinds.Where(k => k.Behaviour == ObstacleBehaviour.Solid)) Assert.InRange(k.HitPercent, 140, 190);
        Assert.NotEqual(C.Hash, Old.Hash); // the new sections are part of the content hash
    }

    [Fact]
    public void Speed_ramps_up_progressively_to_the_engine_speed()
    {
        var w = Flyer();
        w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        var speeds = new List<long>();
        for (int t = 0; t < 60; t++) { w.Step(); speeds.Add(Speed(Vel(w))); }
        long top = w.Network.ShipTopSpeedMilli(0);
        Assert.Equal(48_000, top);
        Assert.InRange(speeds[0], 1, top / 20); // the first tick is a small step, not the full speed
        for (int i = 1; i < 30; i++) Assert.True(speeds[i] > speeds[i - 1], $"tick {i}");
        Assert.Equal(top, speeds[W.AccelTicks + 1]); // reached after the acceleration time (and the last step is clipped to the target)
        Assert.All(speeds.Skip(W.AccelTicks + 1), s => Assert.Equal(top, s));
        // Every tick adds at most the acceleration limit.
        for (int i = 1; i < speeds.Count; i++) Assert.True(speeds[i] - speeds[i - 1] <= w.Network.ShipAccelMilli(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void A_ship_with_inertia_covers_less_ground_than_one_without_in_the_same_time()
    {
        var inert = Flyer(); var legacy = Flyer(new WorldRules().WithLegacyFlight(true));
        foreach (var w in new[] { inert, legacy }) w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        inert.Step(30); legacy.Step(30);
        long a = inert.Network.GetPlayerInfo(0).Z, b = legacy.Network.GetPlayerInfo(0).Z;
        Assert.True(a > 0 && a < b * 7 / 10, $"{a} against {b}");
        inert.Step(60); legacy.Step(60);
        Assert.Equal(48_000, Speed(Vel(inert)));
        Assert.Equal((0, 0, 0), Vel(legacy)); // legacy flight never has a velocity
    }

    [Fact]
    public void Braking_is_faster_than_accelerating_and_stops_exactly()
    {
        var w = Flyer();
        w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        w.Step(60);
        Assert.Equal(48_000, Speed(Vel(w)));
        w.Submit(0, new SetThrust(0, 0, 0));
        int ticks = 0;
        long previous = 48_000;
        while (Speed(Vel(w)) > 0 && ticks < 200)
        {
            w.Step();
            ticks++;
            long now = Speed(Vel(w));
            Assert.True(now < previous);
            previous = now;
        }
        Assert.InRange(ticks, W.BrakeTicks, W.BrakeTicks + 2);
        Assert.True(ticks < W.AccelTicks);
        long z = w.Network.GetPlayerInfo(0).Z;
        w.Step(20);
        Assert.Equal(z, w.Network.GetPlayerInfo(0).Z); // at rest: no drift
        Assert.Equal((0, 0, 0), Vel(w));
        w.ValidateInvariants();
    }

    [Fact]
    public void Turning_round_brakes_first_and_a_diagonal_thrust_has_the_same_top_speed()
    {
        var w = Flyer();
        w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        w.Step(50);
        w.Submit(0, new SetThrust(0, 0, -PlayerRules.ThrustScale));
        long lowest = long.MaxValue;
        for (int t = 0; t < 120; t++) { w.Step(); lowest = Math.Min(lowest, Math.Abs(Vel(w).Z)); }
        Assert.True(Vel(w).Z < 0);
        Assert.Equal(48_000, Speed(Vel(w)));
        Assert.True(lowest < 2_800); // it passed through (almost) zero on the way

        var d = Flyer();
        int h = 724; // 1024 / sqrt 2
        d.Submit(0, new SetThrust(h, 0, h));
        d.Step(80);
        Assert.InRange(Speed(Vel(d)), 47_000, 48_100);
    }

    [Fact]
    public void Boost_raises_both_the_target_speed_and_the_acceleration()
    {
        var plain = Flyer(); var boosted = Flyer();
        boosted.Submit(0, new SetBoost(true));
        foreach (var w in new[] { plain, boosted }) w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        plain.Step(); boosted.Step();
        Assert.Equal(W.BoostPercent * plain.Network.ShipTopSpeedMilli(0) / 100, boosted.Network.ShipTopSpeedMilli(0));
        Assert.True(boosted.Network.ShipAccelMilli(0) > plain.Network.ShipAccelMilli(0) * 3);
        Assert.True(Speed(Vel(boosted)) > Speed(Vel(plain)) * 3);
        boosted.Step(80);
        Assert.Equal(boosted.Network.ShipTopSpeedMilli(0), Speed(Vel(boosted)));
        // The boost ends: the ship sheds the extra speed in about the braking time and settles on the engine speed.
        boosted.Submit(0, new SetBoost(false));
        long before = Speed(Vel(boosted));
        boosted.Step(W.BrakeTicks * 3);
        Assert.Equal(48_000, Speed(Vel(boosted)));
        Assert.True(before > 48_000 * 3);
    }

    [Fact]
    public void The_axis_intent_ramps_like_the_thrust()
    {
        var w = Flyer();
        w.Network.SetMovement(0, 0, 0, 1);
        w.Step(5);
        Assert.InRange(Speed(Vel(w)), 1, 10_000);
        w.Step(60);
        Assert.Equal(48_000, Speed(Vel(w)));
        w.Network.SetMovement(0, 0, 0, 0);
        w.Step(W.BrakeTicks + 2);
        Assert.Equal((0, 0, 0), Vel(w));
    }

    [Fact]
    public void A_jump_charge_and_a_lost_ship_zero_the_velocity()
    {
        var w = Flyer();
        w.Network.GrantResearch(C.ResearchIndex("core:jump_drive"));
        w.Network.GiveItems(0, C.ItemIndex("core:carbon"), 20);
        w.Submit(0, new SetThrust(0, PlayerRules.ThrustScale, 0));
        w.Step(60);
        Assert.True(Speed(Vel(w)) > 0);
        w.Submit(0, new Jump(w.Network.Galaxy!.Systems[0].Lanes[0]));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        Assert.Equal((0, 0, 0), Vel(w));
        w.ValidateInvariants();

        var lost = World(Clean);
        int s = Ship(lost);
        lost.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        lost.Step(40);
        lost.Network.HitModule(s, 0, 0, 0, 100_000);
        lost.Step();
        Assert.True(lost.Network.ShipLost(0));
        Assert.Equal((0, 0, 0), Vel(lost));
    }

    [Fact]
    public void Docking_speed_is_the_real_speed_so_a_coasting_ship_is_too_fast()
    {
        var w = Flyer();
        w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        w.Step(60);
        w.Submit(0, new SetThrust(0, 0, 0));
        w.Step(2);
        Assert.True(Speed(Vel(w)) > 0);
        Assert.True(w.Network.ShipSpeedMilli(0) >= Speed(Vel(w))); // the commanded speed is zero: the real one counts
        Assert.True(w.Network.ShipSpeedMilli(0) > DockRules.MaxSpeedUnitsPerTick * 1000 / 2);
        w.Step(60);
        Assert.Equal(0, w.Network.ShipSpeedMilli(0));
    }

    // ---- determinism and the legacy path ----------------------------------------------------------------

    private static void Script(SimWorld w)
    {
        var rng = new SplitMix64(99);
        for (int i = 0; i < 40; i++)
        {
            int ax = rng.Next(-1024, 1025), ay = rng.Next(-1024, 1025), az = rng.Next(-1024, 1025);
            while (!PlayerRules.ThrustInRange(ax, ay, az)) { ax /= 2; ay /= 2; az /= 2; }
            w.Submit(0, new SetThrust(ax, ay, az));
            if (i % 7 == 3) w.Submit(0, new SetBoost(i % 14 == 3));
            w.Step(rng.Next(5, 40));
        }
        w.Submit(0, new SetThrust(0, 0, 0));
        w.Step(100);
    }

    [Fact]
    public void Flight_with_inertia_is_deterministic_and_replays_from_the_log()
    {
        var a = Flyer(content: C); var b = Flyer(content: C);
        Script(a); Script(b);
        Assert.Equal(a.Checksum(), b.Checksum());
        a.ValidateInvariants();
        var replay = SimWorld.Replay(a.Log, a.Tick, initial: Flyer(content: C));
        Assert.Equal(a.Checksum(), replay.Checksum());
        Assert.Equal(a.Network.GetPlayerInfo(0), replay.Network.GetPlayerInfo(0));
        // Velocity is part of the state: a world one tick short differs.
        var shorter = SimWorld.Replay(a.Log, a.Tick - 1, initial: Flyer(content: C));
        Assert.NotEqual(a.Checksum(), shorter.Checksum());
    }

    [Fact]
    public void The_legacy_flight_flag_flies_exactly_like_content_without_inertia()
    {
        var legacy = Flyer(new WorldRules().WithLegacyFlight(true), Clean);
        var old = Flyer(content: OldClean);
        Script(legacy); Script(old);
        Assert.Equal(old.Network.GetPlayerInfo(0), legacy.Network.GetPlayerInfo(0));
        Assert.Equal(old.Network.GetStructure(old.Network.ShipStructure(0)).OriginX, legacy.Network.GetStructure(legacy.Network.ShipStructure(0)).OriginX);
        Assert.Equal(old.Network.ModuleHp(old.Network.ShipStructure(0), 0, 0, 0), legacy.Network.ModuleHp(legacy.Network.ShipStructure(0), 0, 0, 0));
        // And the same flag on the same content hashes like default rules until something differs.
        var a = Flyer(new WorldRules().WithLegacyFlight(true)); var b = Flyer();
        a.Step(30); b.Step(30);
        Assert.Equal(a.Checksum(), b.Checksum());
    }

    [Fact]
    public void An_old_log_replays_with_the_legacy_flags_and_the_new_header_reads_without_them()
    {
        var legacyRules = new WorldRules().WithLegacyFlight(true).WithLegacyCollisions(true).WithLegacyFeel(true).WithLegacyResources(true); // what a build of that time recorded: the ADR 0036 rules did not exist
        var recorded = Flyer(legacyRules, C);
        Script(recorded);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(27, bytes[4]);
        var empty = new MemoryStream();
        CommandCodec.WriteLog(empty, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var current);
        Assert.False(current.LegacyFlight);
        Assert.False(current.LegacyCollisions);
        for (byte version = 6; version <= 14; version++)
        {
            var old = empty.ToArray();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyFlight && rules.LegacyCollisions, $"version {version}");
            Assert.True(rules.WithoutLegacyFlags().IsDefault);
        }
        // A version 14 log (all the commands it can hold) replays with the legacy flags to the very same state.
        var v14 = (byte[])bytes.Clone();
        v14[4] = 14;
        var log14 = CommandCodec.ReadLog(new MemoryStream(v14), out _, out _, out _, out _, out _, out _, out var rules14);
        var replay = SimWorld.Replay(log14, recorded.Tick, initial: Flyer(rules14, C));
        Assert.Equal(recorded.Checksum(), replay.Checksum());
        Assert.Equal(recorded.Network.GetPlayerInfo(0), replay.Network.GetPlayerInfo(0));
        // The same log under the new rules ends elsewhere: the ramp changes where the ship is.
        var strict = SimWorld.Replay(recorded.Log, recorded.Tick, initial: Flyer(content: C));
        Assert.NotEqual(recorded.Network.GetPlayerInfo(0).Z, strict.Network.GetPlayerInfo(0).Z);
    }

    [Fact]
    public void The_two_new_flags_roundtrip_and_hash_as_default_rules()
    {
        var both = new WorldRules().WithLegacyCollisions(true).WithLegacyFlight(true);
        Assert.True(both.LegacyCollisions && both.LegacyFlight);
        Assert.Equal(64 | 128, both.Serialize()[1 + 8 * 4] & (64 | 128));
        Assert.Equal(both, WorldRules.Deserialize(both.Serialize()));
        Assert.True(both.WithoutLegacyFlags().IsDefault);
        Assert.True(both.WithLegacyCollisions(false).LegacyFlight); // the helpers keep each other's flags
        Assert.True(both.WithLegacyLaser(true).LegacyCollisions);
        Assert.True(both.Clamped().LegacyFlight);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyCollisions || p.Rules.LegacyFlight));
    }

    // ---- solid deposits ---------------------------------------------------------------------------------

    private static (int Id, int X, int Y, int Z) HomeDeposit(SimWorld w)
    {
        var g = w.Network.Galaxy!;
        for (int i = 0; i < g.Deposits.Count; i++)
            if (g.Deposits[i].System == 0 && w.Network.IsDepositRevealed(i)) return (i, g.Deposits[i].X, g.Deposits[i].Y, g.Deposits[i].Z);
        throw new InvalidOperationException("no home deposit");
    }

    /// <summary>The ship's first module box (the cockpit) against the centre of a deposit: the distance from the centre to the box, in units.</summary>
    private static double DistanceToCockpit(SimWorld w, int ship, int cx, int cy, int cz)
    {
        var s = w.Network.GetStructure(ship);
        double best = double.MaxValue;
        foreach (var key in s.Modules.Keys)
        {
            var (min, max) = w.Network.ModuleWorldBounds(s, key);
            double dx = Math.Clamp((double)cx, min.X, max.X) - cx, dy = Math.Clamp((double)cy, min.Y, max.Y) - cy, dz = Math.Clamp((double)cz, min.Z, max.Z) - cz;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        return best;
    }

    /// <summary>Puts the ship straight above a deposit, a gap away, and flies down onto it. Returns the impact events seen.</summary>
    private static List<ObstacleEvent> DiveOnto(SimWorld w, int ship, (int Id, int X, int Y, int Z) dep, int ticks, int gapCells = 25)
    {
        long cx = (long)dep.X * Cell, cy = (long)dep.Y * Cell, cz = (long)dep.Z * Cell;
        w.Network.SetStructureOrigin(ship, cx - 4 * Cell, cy + gapCells * Cell, cz - 4 * Cell);
        w.Submit(0, new SetThrust(0, -PlayerRules.ThrustScale, 0));
        var events = new List<ObstacleEvent>();
        for (int t = 0; t < ticks; t++) { w.Step(); events.AddRange(w.Network.ObstacleEvents); }
        return events;
    }

    [Fact]
    public void A_deposit_is_a_solid_rock_that_stops_the_ship_and_hurts_the_module_that_hits_it()
    {
        var w = World();
        int ship = Ship(w);
        var dep = HomeDeposit(w);
        long hit = w.Network.DepositRockRadius(dep.Id) + W.HullPadUnits;
        Assert.True(w.Network.DepositRockRadius(dep.Id) > 0);
        int hp = w.Network.ModuleHp(ship, 0, 0, 0);
        var events = DiveOnto(w, ship, dep, 400);
        var impact = events.FirstOrDefault(e => e.Kind == ObstacleEventKind.Impact);
        Assert.Equal(dep.Id, impact.Deposit);
        Assert.Equal(-1, impact.Obstacle);
        Assert.True(impact.Speed > W.SafeSpeedUnits);
        // It rests on the rock: no module reaches into the hit radius, none is far from it.
        double d = DistanceToCockpit(w, ship, dep.X * Cell, dep.Y * Cell, dep.Z * Cell);
        Assert.InRange(d, hit - 4, hit + 6);
        // The speed into the rock was lost: it does not strike again and again.
        Assert.Equal(1, events.Count(e => e.Kind == ObstacleEventKind.Impact));
        Assert.True(Math.Abs(Vel(w).Y) < 6_000, $"{Vel(w)}");
        Assert.True(w.Network.ModuleHp(ship, 0, 0, 0) <= hp);
        w.ValidateInvariants();
    }

    [Fact]
    public void A_deposit_is_not_solid_with_the_legacy_flag_and_not_in_content_without_the_rule()
    {
        foreach (var w in new[] { World(rules: new WorldRules().WithLegacyCollisions(true)), World(Old) })
        {
            int ship = Ship(w);
            var dep = HomeDeposit(w);
            Assert.Equal(0, w.Network.DepositRockRadius(dep.Id));
            var events = DiveOnto(w, ship, dep, 400);
            Assert.DoesNotContain(events, e => e.Deposit >= 0);
            Assert.True(w.Network.GetStructure(ship).OriginY < dep.Y * (long)Cell - 8 * Cell, "the ship went through the rock");
        }
    }

    [Fact]
    public void A_hidden_deposit_and_an_exhausted_one_are_not_solid()
    {
        var w = World();
        int ship = Ship(w);
        var g = w.Network.Galaxy!;
        int hidden = Enumerable.Range(0, g.Deposits.Count).First(i => !w.Network.IsDepositRevealed(i));
        // Park the ship in the middle of the hidden deposit's rock (in its own system): nothing pushes it.
        long hx = (long)g.Deposits[hidden].X * Cell, hy = (long)g.Deposits[hidden].Y * Cell, hz = (long)g.Deposits[hidden].Z * Cell;
        w.Network.SetStructureOrigin(ship, hx - 4 * Cell, hy - 2 * Cell, hz - 4 * Cell);
        w.Step(5);
        Assert.False(w.Network.IsDepositRevealed(hidden) && w.Network.ObstacleEvents.Any(e => e.Deposit == hidden));
        Assert.Equal(hx - 4 * Cell, w.Network.GetStructure(ship).OriginX);

        // A deposit of one item is gone after one mined ore; then it is passable.
        var small = World();
        int s2 = Ship(small);
        var home = HomeDeposit(small);
        int extra = small.Network.AddDeposit(small.Network.DepositItem(home.Id), 1, home.X + 40, home.Y, home.Z);
        long ex = (home.X + 40L) * Cell, ey = (long)home.Y * Cell, ez = (long)home.Z * Cell;
        small.Network.SetStructureOrigin(s2, ex - 4 * Cell, ey - 12 * Cell, ez - 4 * Cell); // below it: the laser module (above the cockpit) is 6 cells from the centre
        small.Submit(0, new StartMining(extra));
        small.Step();
        Assert.True(small.LastResults.Single().Ok, small.LastResults.Single().Reject.ToString());
        small.Step(120);
        Assert.Equal(0, small.Network.DepositRemaining(extra));
        small.Network.SetStructureOrigin(s2, ex - 4 * Cell, ey - 2 * Cell, ez - 4 * Cell); // inside the (exhausted) rock
        small.Step(5);
        Assert.Equal(ex - 4 * Cell, small.Network.GetStructure(s2).OriginX);
    }

    [Fact]
    public void Mining_stays_possible_from_beside_a_solid_deposit()
    {
        var w = World();
        int ship = Ship(w);
        var dep = HomeDeposit(w);
        long hit = w.Network.DepositRockRadius(dep.Id) + W.HullPadUnits;
        // The ship sits flush against the rock on its +x side, its laser module (module (0,1,0), centre (4,6,4) cells) reaching the deposit.
        w.Network.SetStructureOrigin(ship, dep.X * (long)Cell + hit + 1, dep.Y * (long)Cell - 6 * Cell, dep.Z * (long)Cell - 4 * Cell);
        w.Step(3);
        Assert.True(w.Network.InMineRange(0, dep.Id));
        w.Submit(0, new StartMining(dep.Id));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        int before = w.Network.HoldCount(0, w.Network.DepositItem(dep.Id));
        w.Step(90);
        Assert.True(w.Network.HoldCount(0, w.Network.DepositItem(dep.Id)) > before);
        Assert.DoesNotContain(w.Network.ObstacleEvents, e => e.Kind == ObstacleEventKind.Impact);
        // And the laser reaches further than the rock plus the padding: mining never needs a touch (10 cells against a rock under 4).
        Assert.True(10L * Cell > 2 * (w.Network.DepositRockRadius(dep.Id) + W.HullPadUnits));
    }

    [Fact]
    public void Stations_may_not_be_founded_or_extended_inside_a_deposit_rock_but_may_beside_it()
    {
        var w = World();
        int ship = Ship(w);
        var dep = HomeDeposit(w);
        w.Network.SetStructureOrigin(ship, dep.X * (long)Cell - 4 * Cell, dep.Y * (long)Cell + 8 * Cell, dep.Z * (long)Cell - 4 * Cell);
        w.Step();
        w.Submit(0, new FoundStation(dep.X - 4, dep.Y - 2, dep.Z - 4)); // the core's box has the deposit centre inside
        w.Step();
        Assert.Equal(Reject.OverlapsDeposit, w.LastResults.Single().Reject);

        // Beside the rock the core is fine; a module that would reach into the rock is not.
        int beside = (int)(dep.X - 12);
        w.Submit(0, new FoundStation(beside, dep.Y - 2, dep.Z - 4));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        int station = w.Network.Structures().Single(s => s.Kind == StructureKind.Station).Id;
        w.Submit(0, new PlaceModuleIn(station, 1, 0, 0, ModuleKind.Hull)); // the next module's box starts 4 cells from the deposit centre
        w.Step();
        Assert.Equal(Reject.OverlapsDeposit, w.LastResults.Single().Reject);
        w.Submit(0, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Hull)); // sideways it is clear
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());

        // The same world under the legacy flag has no such rule.
        var old = World(rules: new WorldRules().WithLegacyCollisions(true));
        int s2 = Ship(old);
        old.Network.SetStructureOrigin(s2, dep.X * (long)Cell - 4 * Cell, dep.Y * (long)Cell + 8 * Cell, dep.Z * (long)Cell - 4 * Cell);
        old.Step();
        old.Submit(0, new FoundStation(dep.X - 4, dep.Y - 2, dep.Z - 4));
        old.Step();
        Assert.True(old.LastResults.Single().Ok, old.LastResults.Single().Reject.ToString());
    }

    [Fact]
    public void Drills_and_drones_still_reach_a_deposit_from_a_module_beside_the_rock()
    {
        // The closest a module can stand to a deposit is its rock; the drill range covers that plus a whole module.
        long rock = (long)DepositRules.NominalRadiusUnits(10_000) * W.DepositHitPercent / 100;
        Assert.True((long)ExplorationRules.DrillRangeCells * Cell > rock + StationGrid.ModuleX * (long)Cell);
        Assert.Equal(1600, DepositRules.RockScalePermille(1_000_000));
        Assert.Equal(600, DepositRules.RockScalePermille(0));
        Assert.InRange(DepositRules.RockScalePermille(600), 1149, 1151);
    }

    // ---- hit volumes: drawn size and padding --------------------------------------------------------------

    [Fact]
    public void The_hit_radius_follows_the_drawn_size_and_the_hull_pad_and_the_legacy_flag_restores_the_radius()
    {
        var w = World();
        var n = w.Network;
        int small = Enumerable.Range(0, n.ObstacleCount).First(i => n.GetObstacle(i).Kind == W.KindIndex("core:asteroid_small"));
        int radius = n.GetObstacle(small).Radius;
        Assert.Equal(radius * 155L / 100, n.ObstacleHitRadius(small, padded: false));
        Assert.Equal(radius * 155L / 100 + W.HullPadUnits, n.ObstacleHitRadius(small));
        var legacy = World(rules: new WorldRules().WithLegacyCollisions(true));
        Assert.Equal(radius, legacy.Network.ObstacleHitRadius(small));
        // Every solid kind of the shipped content is drawn larger than its nominal radius: its hit radius is too.
        foreach (var k in TestContent.CoreFloor.World!.Kinds.Where(k => k.Behaviour is ObstacleBehaviour.Solid or ObstacleBehaviour.Drifter)) Assert.True(k.HitPercent > 100, k.Id);
    }

    [Fact]
    public void A_module_inside_the_padding_but_outside_the_nominal_radius_is_pushed_out()
    {
        var w = World();
        int ship = Ship(w);
        var n = w.Network;
        int rock = Enumerable.Range(0, n.ObstacleCount).Select(n.GetObstacle).First(o => o.Kind == W.KindIndex("core:asteroid_big")).Id;
        var o = n.GetObstacle(rock);
        long hit = n.ObstacleHitRadius(rock);
        Assert.True(hit > o.Radius + W.HullPadUnits);
        // The cockpit box sits just under the hit radius straight above the rock centre: inside the drawn rock, outside the nominal one.
        long gap = o.Radius + (hit - o.Radius) / 2;
        n.SetStructureOrigin(ship, o.X - 4 * Cell, o.Y + gap, o.Z - 4 * Cell);
        w.Step();
        Assert.True(n.GetStructure(ship).OriginY >= o.Y + hit - 1, "pushed to the hit radius");
        // The legacy flag keeps the nominal radius: the same pose is clear and nothing moves.
        var old = World(rules: new WorldRules().WithLegacyCollisions(true));
        int s2 = Ship(old);
        old.Network.SetStructureOrigin(s2, o.X - 4 * Cell, o.Y + gap, o.Z - 4 * Cell);
        old.Step();
        Assert.Equal(o.Y + gap, old.Network.GetStructure(s2).OriginY);
    }

    [Fact]
    public void A_ship_closing_on_a_rock_at_a_tilt_is_stopped_at_the_same_hit_radius_whatever_the_attitude()
    {
        foreach (bool turned in new[] { false, true })
        {
            var w = World();
            int ship = Ship(w);
            if (turned) w.Network.SetStructureAttitude(ship, Attitude.Of(11585, 0, 0, 11585)); // a quarter roll (the boxes stay axis aligned)
            var n = w.Network;
            int rock = Enumerable.Range(0, n.ObstacleCount).Select(n.GetObstacle).First(o => o.Kind == W.KindIndex("core:asteroid_small")).Id;
            var o = n.GetObstacle(rock);
            n.SetStructureOrigin(ship, o.X - 4 * Cell, o.Y + 30 * Cell, o.Z - 4 * Cell);
            w.Submit(0, new SetThrust(0, -PlayerRules.ThrustScale, 0));
            w.Step(400);
            double d = DistanceToCockpit(w, ship, (int)o.X, (int)o.Y, (int)o.Z);
            Assert.True(d >= n.ObstacleHitRadius(rock) - 8, $"turned {turned}: {d} against {n.ObstacleHitRadius(rock)}");
            w.ValidateInvariants();
        }
    }

    // ---- the research ------------------------------------------------------------------------------------

    [Fact]
    public void Engine_research_raises_the_top_speed_and_the_acceleration_and_is_deliverable()
    {
        var c = TestContent.CoreFloor;
        foreach (var id in new[] { "core:ion_thrusters", "core:thrust_vectoring", "core:plasma_drive" })
        {
            var node = c.Research[c.ResearchIndex(id)];
            Assert.NotEmpty(node.Bonuses);
            Assert.All(node.Bonuses, b => Assert.True(b.Kind is BonusKind.EngineSpeed or BonusKind.EngineAccel));
            Assert.All(node.Cost, s => Assert.Contains(c.Items[s.Item].Id, new[] { "core:iron_plate", "core:carbon", "core:science_engineering" }));
        }

        var w = SimWorld.CreateGalaxy(c, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        w.Step(5);
        int top0 = w.Network.ShipTopSpeedMilli(0), acc0 = w.Network.ShipAccelMilli(0);
        Assert.True(top0 > 0 && acc0 > 0);
        w.Network.GrantResearch(c.ResearchIndex("core:ion_thrusters"));
        Assert.Equal(25, w.Network.BonusPercent(BonusKind.EngineSpeed));
        Assert.Equal(top0 * 125 / 100, w.Network.ShipTopSpeedMilli(0));
        Assert.True(w.Network.ShipAccelMilli(0) > acc0); // a higher top speed also accelerates faster (the same time to reach it)
        int acc1 = w.Network.ShipAccelMilli(0);
        w.Network.GrantResearch(c.ResearchIndex("core:thrust_vectoring"));
        Assert.Equal(40, w.Network.BonusPercent(BonusKind.EngineAccel));
        Assert.Equal(top0 * 125 / 100, w.Network.ShipTopSpeedMilli(0));
        Assert.True(w.Network.ShipAccelMilli(0) > acc1 * 13 / 10);
        w.Network.GrantResearch(c.ResearchIndex("core:plasma_drive"));
        Assert.Equal(60, w.Network.BonusPercent(BonusKind.EngineSpeed));
        Assert.True(w.Network.ShipTopSpeedMilli(0) > top0 * 155 / 100);
        w.Network.ValidateInvariants();
        // The bonus is part of the state, so the same research gives the same flight: a flight with it is faster over a minute.
        w.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        w.Step(100);
        Assert.True(Speed(Vel(w)) > top0 * 155 / 100);
    }

    [Fact]
    public void Engine_research_does_nothing_in_a_world_without_inertia()
    {
        var c = TestContent.CoreFloor;
        var w = SimWorld.CreateGalaxy(c, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: new WorldRules().WithLegacyFlight(true));
        w.Step(5);
        int top0 = w.Network.ShipSpeedUnitsPerTick(0);
        w.Network.GrantResearch(c.ResearchIndex("core:ion_thrusters"));
        Assert.Equal(top0, w.Network.ShipSpeedUnitsPerTick(0));
    }

    [Fact]
    public void The_designer_reports_the_engines_of_the_layout_with_the_research_bonuses()
    {
        var shipped = TestContent.CoreFloor;
        var editor = new Starfoundry.Ui.ShipDesignEditor();
        editor.Load(shipped.StarterShip!.Modules.ToDictionary(m => (m.Mx, m.My, m.Mz), m => m.Kind));
        var plain = editor.Flight(shipped, 0, 0)!.Value;
        Assert.Equal(1, plain.Engines);
        Assert.Equal(6.0, plain.TopCellsPerSecond, 3);
        Assert.Equal(shipped.World!.AccelTicks / 30.0, plain.SecondsToTop, 3);
        var upgraded = editor.Flight(shipped, 60, 80)!.Value;
        Assert.Equal(9.6, upgraded.TopCellsPerSecond, 3);
        Assert.Equal(plain.SecondsToTop / 1.8, upgraded.SecondsToTop, 3);
        // Without inertia (content without the section) only the top speed is known; with no engine there is nothing to report.
        Assert.Equal(0.0, editor.Flight(Old, 0, 0)!.Value.SecondsToTop);
        var none = new Starfoundry.Ui.ShipDesignEditor();
        none.Load(new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit });
        Assert.Null(none.Flight(shipped, 0, 0));
    }

    [Fact]
    public void The_new_texts_exist_in_both_languages()
    {
        foreach (var key in new[] { "toast.reject.overlapsDeposit", "bonus.engine_speed", "bonus.engine_accel", "research.core:ion_thrusters", "research.core:ion_thrusters.desc",
            "research.core:thrust_vectoring", "research.core:thrust_vectoring.desc", "research.core:plasma_drive", "research.core:plasma_drive.desc",
            "hud.flightSpeedOf", "hud.flightGaugeTip", "obstacle.depositRock", "designer.flight", "designer.flightPlain" })
        {
            Assert.True(Starfoundry.Ui.Loc.Has(key), key);
            var (es, en) = Starfoundry.Ui.Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
        Assert.Equal("toast.reject.overlapsDeposit", Starfoundry.Ui.RejectTexts.Key(Reject.OverlapsDeposit));
    }
}
