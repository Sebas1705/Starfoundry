using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0053, stage 2 slice: the world scale (systems and rock clusters kilometres apart), the cruise drive and its approach governor, the legacy flag and log version 30.</summary>
public class WorldScaleTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261128UL;
    private const long Horizon = 4_000_000;
    private static readonly ContentDb C = TestContent.CoreFloor;

    private static SimWorld World(WorldRules? rules = null, ulong seed = Seed, ContentDb? content = null, GameMode mode = GameMode.Sandbox) =>
        SimWorld.CreateGalaxy(content ?? C, mode, seed, threats: false, stationRules: false, structures: true, rules: rules);

    private static JsonObject Root() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();

    /// <summary>Puts the ship at a point in cells, stops it and lets the world settle one tick.</summary>
    private static void Go(SimWorld w, long x, long y, long z)
    {
        int ship = w.Network.ShipStructure(0);
        var s = w.Network.GetStructure(ship);
        var p = w.Network.GetPlayerInfo(0);
        w.Network.SetStructureOrigin(ship, x * Cell - (p.X - s.OriginX), y * Cell - (p.Y - s.OriginY), z * Cell - (p.Z - s.OriginZ));
        w.Step();
    }

    private static (int X, int Y, int Z) Pos(SimWorld w) { var p = w.Network.GetPlayerInfo(0); return (p.X, p.Y, p.Z); }

    private static long Speed(SimWorld w)
    {
        var (x, y, z) = w.Network.ShipVelocityMilli(0);
        return IntMath.ISqrt((long)x * x + (long)y * y + (long)z * z);
    }

    private static void Thrust(SimWorld w, double dx, double dy, double dz)
    {
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        w.Submit(0, len < 1e-9 ? new SetThrust(0, 0, 0) : new SetThrust((int)(dx / len * PlayerRules.ThrustScale * 0.999), (int)(dy / len * PlayerRules.ThrustScale * 0.999), (int)(dz / len * PlayerRules.ThrustScale * 0.999)));
    }

    /// <summary>A point in cells at least <paramref name="clearCells"/> away from every zone, found on a coarse ring around the home system.</summary>
    private static (long X, long Y, long Z) VoidPoint(SimWorld w)
    {
        foreach (long r in new long[] { 20_000, 30_000, 40_000, 15_000, 10_000 })
            foreach (var (dx, dz) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) })
            {
                long x = dx * r, z = dz * r;
                if (w.Network.ZoneEdgeDistance(x * Cell, 0, z * Cell, Horizon) == long.MaxValue) return (x, 0, z);
            }
        throw new InvalidOperationException("no void point");
    }

    // ---- the flag and the log -----------------------------------------------------------------------------------

    [Fact]
    public void The_flag_is_bit_5_of_the_third_flags_byte_roundtrips_and_hashes_as_default_rules()
    {
        var r = new WorldRules().WithLegacyWorldScale(true);
        Assert.True(r.LegacyWorldScale);
        Assert.False(WorldRules.Default.LegacyWorldScale);
        Assert.Equal(r, WorldRules.Deserialize(r.Serialize()));
        Assert.True(r.HashesAsDefault);
        Assert.True(r.WithoutLegacyFlags().IsDefault);
        Assert.False(r.WithoutLegacyFlags().LegacyWorldScale);
        Assert.True(r.WithLegacyLargeModules(true).LegacyWorldScale);
        Assert.True(r.WithLegacyLargeModules(true).WithLegacyWorldScale(false).LegacyLargeModules);
        Assert.True(r.Clamped().LegacyWorldScale);
        Assert.NotEqual(r, WorldRules.Default);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyWorldScale));
    }

    [Fact]
    public void Set_cruise_roundtrips_in_the_codec_the_log_is_version_30_and_older_logs_refuse_it()
    {
        Assert.Contains((byte)73, CommandCodec.ToBytes(new SetCruise(true)));
        Assert.Equal(new SetCruise(true), CommandCodec.FromBytes(CommandCodec.ToBytes(new SetCruise(true))));
        Assert.Equal(new SetCruise(false), CommandCodec.FromBytes(CommandCodec.ToBytes(new SetCruise(false))));
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, new List<LoggedCommand> { new(0, 0, new Batch(new Command[] { new SetCruise(true) })) }, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(30, bytes[4]);
        Assert.Single(CommandCodec.ReadLog(new MemoryStream(bytes)));
        for (byte version = 8; version <= 29; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(old)));
        }
    }

    [Fact]
    public void Logs_of_versions_5_to_29_read_with_the_flag_on_and_a_new_log_reads_without_it()
    {
        var fresh = new MemoryStream();
        CommandCodec.WriteLog(fresh, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, structures: true);
        var bytes = fresh.ToArray();
        CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var current);
        Assert.False(current.LegacyWorldScale);
        for (byte version = 8; version <= 29; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyWorldScale, $"version {version}");
            Assert.True(rules.WithoutLegacyFlags().IsDefault && rules.HashesAsDefault, $"version {version}");
        }
    }

    [Fact]
    public void A_log_of_version_29_replays_byte_for_byte_in_the_legacy_scale_and_never_meets_a_cruise_drive()
    {
        var recorded = World(new WorldRules().WithLegacyWorldScale(true));
        Assert.Null(recorded.Network.Scale);
        recorded.Submit(0, new SetBoost(true));
        recorded.Submit(0, new SetThrust(0, 0, PlayerRules.ThrustScale));
        recorded.Step(600);
        recorded.Submit(0, new SetCruise(true));
        recorded.Step(1);
        Assert.Equal(Reject.NotAllowedHere, recorded.LastResults.Last().Reject); // the legacy world has no cruise drive
        recorded.Step(300);
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log.Where(e => e.Command is not SetCruise).ToList(), C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: recorded.Network.Rules);
        var bytes = ms.ToArray();
        for (byte version = 26; version <= 29; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            var log = CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyWorldScale, $"version {version}");
            if (version != 29) continue; // older versions also imply the older legacy flags, so only the last one is the recorded world
            var replay = SimWorld.Replay(log, recorded.Tick, initial: World(rules));
            Assert.Equal(recorded.Checksum(), replay.Checksum());
            Assert.Null(replay.Network.Scale);
        }
    }

    [Fact]
    public void The_flag_and_content_without_a_scale_block_leave_every_checksum_alone()
    {
        // content without the block (the legacy fixture): the flag changes nothing at all
        var plain = World(content: TestContent.Core);
        var flagged = World(new WorldRules().WithLegacyWorldScale(true), content: TestContent.Core);
        plain.Step(300); flagged.Step(300);
        Assert.Null(plain.Network.Scale);
        Assert.Equal(plain.Checksum(), flagged.Checksum());

        // the shipped content with the flag is the galaxy of before, and without it the scale block moves the systems and changes the content hash
        var legacy = World(new WorldRules().WithLegacyWorldScale(true));
        var scaled = World();
        Assert.NotNull(scaled.Network.Scale);
        Assert.NotEqual(legacy.Checksum(), scaled.Checksum());
        Assert.Equal(Galaxy.Generate(C, Seed, legacyScale: true).Systems.Select(s => (s.X, s.Y, s.Z)), legacy.Network.Galaxy!.Systems.Select(s => (s.X, s.Y, s.Z)));
        var root = Root();
        root["world"]!.AsObject().Remove("scale");
        var without = ContentDb.FromJson(root.ToJsonString());
        Assert.NotEqual(C.Hash, without.Hash);
        Assert.Null(without.World!.Scale);
        var again = Root();
        again["world"]!.AsObject().Remove("scale");
        Assert.Equal(without.Hash, ContentDb.FromJson(again.ToJsonString()).Hash);
    }

    // ---- the content ------------------------------------------------------------------------------------------

    [Fact]
    public void The_shipped_scale_block_has_the_agreed_numbers_and_is_validated()
    {
        var s = C.World!.Scale!;
        Assert.Equal((100_000, 100_000, 0, 5000, 600_000, 37_500), (s.SpacingCells, s.SuperCells, s.BackgroundPermille, s.OreDistancePercent, s.FieldRadiusCells, s.NavRangePercent));
        Assert.Equal((24_000, 360, 180, 960, 2), (s.Cruise.SpeedUnitsPerTick, s.Cruise.SpinUpTicks, s.Cruise.BrakeTicks, s.Cruise.DropUnitsPerTick, s.Cruise.ZoneSectors));
        Assert.Equal(3000, s.Cruise.SpeedUnitsPerTick / 8); // 8 units per tick are 1 m/s: 3 km/s

        void Bad(Action<JsonObject> edit)
        {
            var root = Root();
            edit(root["world"]!["scale"]!.AsObject());
            Assert.ThrowsAny<Exception>(() => ContentDb.FromJson(root.ToJsonString()));
        }
        Bad(o => o["warp"] = 1);                                                    // unknown key
        Bad(o => o["super_cells"] = 300);                                           // under two field sectors
        Bad(o => o["field_radius_cells"] = 5_000_000);                              // beyond the world limit
        Bad(o => o["spacing_cells"] = 100);                                         // systems closer than their own radius
        Bad(o => o["cruise"]!.AsObject()["drop_units_per_tick"] = 99_999);          // dropping out above the cruise speed
        Bad(o => o["cruise"]!.AsObject()["spin_up_ticks"] = 0);
        Bad(o => o["cruise"]!.AsObject().Remove("zone_sectors"));
        Bad(o => o.Remove("cruise"));
    }

    [Fact]
    public void The_ore_thresholds_are_fifty_times_farther_and_the_field_is_bounded_to_the_anchors()
    {
        var n = World().Network;
        var f = C.World!.Field!;
        Assert.Equal(new[] { 125_000, 175_000, 250_000 }, C.World.Scale!.Apply(f).Items.Where(i => i.MinDistanceCells > 0).Select(i => i.MinDistanceCells).ToArray());
        Assert.Equal(f.Items.Select(i => i.MinDistanceCells * 50), C.World.Scale.Apply(f).Items.Select(i => i.MinDistanceCells));
        Assert.Equal(600_000, C.World.Scale.Apply(f).AnchorRadiusCells);
        Assert.Equal(0, f.AnchorRadiusCells);
        Assert.Equal(0, C.World.Scale.Apply(f).Clusters!.BackgroundPermille);
        Assert.Equal(100_000, C.World.Scale.Apply(f).Clusters!.SuperCells);
        Assert.True(n.HasRockClusters);
        Assert.NotNull(n.Scale);
        Assert.Null(World(new WorldRules().WithLegacyWorldScale(true)).Network.Scale);
    }

    [Fact]
    public void The_systems_are_about_a_hundred_kilometres_apart_with_the_scale_and_a_few_kilometres_without_it()
    {
        long Nearest(Galaxy g)
        {
            long best = long.MaxValue;
            for (int i = 0; i < g.Systems.Count; i++)
                for (int j = i + 1; j < g.Systems.Count; j++)
                {
                    long dx = g.Systems[i].X - g.Systems[j].X, dy = g.Systems[i].Y - g.Systems[j].Y, dz = g.Systems[i].Z - g.Systems[j].Z;
                    best = Math.Min(best, IntMath.ISqrt(dx * dx + dy * dy + dz * dz));
                }
            return best;
        }
        foreach (ulong seed in new[] { 20261121UL, 20261120UL, 777UL, Seed })
        {
            var scaled = Galaxy.Generate(C, seed);
            var legacy = Galaxy.Generate(C, seed, legacyScale: true);
            Assert.InRange(Nearest(scaled), 50_000, 160_000);   // 100 km spacing, jittered by a quarter
            Assert.InRange(Nearest(legacy), 500, 3_000);
            Assert.All(scaled.Systems, s => Assert.True(Math.Abs(s.X) < 500_000 && Math.Abs(s.Z) < 500_000));
        }
    }

    [Fact]
    public void No_rock_exists_beyond_the_field_radius_of_every_anchor_and_the_void_has_no_loose_rocks()
    {
        var w = World();
        var g = w.Network.Galaxy!;
        var f = C.World!.Scale!.Apply(C.World.Field!);
        var k = f.Clusters!;
        var unbounded = f with { AnchorRadiusCells = 0 };
        long Nearest(long x, long y, long z) => g.Systems.Min(s => IntMath.ISqrt((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) + (z - s.Z) * (z - s.Z)));
        int inside = 0, beyondChecked = 0, beyondWithoutFilter = 0;
        // every cluster of a wide box around the galaxy: the sectors at its heart
        foreach (var p in ClusterField.ParentsInBox(f, k, Seed, -700_000, 700_000, -2_000, 2_000, -700_000, 700_000))
        {
            long d = Nearest(p.X, p.Y, p.Z);
            if (d > 600_000 + p.Radius + 2_000 && beyondChecked < 6)
            {
                beyondChecked++;
                var rocks = new List<FieldRock>(); var free = new List<FieldRock>();
                int sx = (int)Math.Floor(p.X / 200.0), sy = (int)Math.Floor(p.Y / 200.0), sz = (int)Math.Floor(p.Z / 200.0);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        ClusterField.Generate(f, k, g.Systems, Seed, sx + dx, sy, sz + dz, 100, rocks);
                        ClusterField.Generate(unbounded, k, g.Systems, Seed, sx + dx, sy, sz + dz, 100, free);
                    }
                Assert.Empty(rocks);
                beyondWithoutFilter += free.Count;
            }
            else if (d < 595_000)
            {
                var rocks = new List<FieldRock>();
                int sx = (int)Math.Floor(p.X / 200.0), sy = (int)Math.Floor(p.Y / 200.0), sz = (int)Math.Floor(p.Z / 200.0);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++) ClusterField.Generate(f, k, g.Systems, Seed, sx + dx, sy, sz + dz, 100, rocks);
                foreach (var r in rocks) Assert.True(Nearest(r.X, r.Y, r.Z) <= 600_000);
                inside += rocks.Count;
            }
        }
        Assert.True(inside > 0, "some cluster inside the field holds rocks");
        Assert.True(beyondChecked > 0 && beyondWithoutFilter > 0, "the bound is what empties the far clusters");

        // the void: a sector away from every cluster generates nothing (no background rocks)
        var (vx, _, vz) = VoidPoint(w);
        var none = new List<FieldRock>();
        for (int i = -3; i <= 3; i++)
            for (int j = -3; j <= 3; j++) ClusterField.Generate(f, k, g.Systems, Seed, (int)Math.Floor(vx / 200.0) + i, 0, (int)Math.Floor(vz / 200.0) + j, 100, none);
        Assert.Empty(none);
    }

    [Fact]
    public void The_navigation_range_is_150_km_with_the_scale_and_400_m_without()
    {
        Assert.Equal(150_000, ShieldSensorRules.NavBaseRangeCells * C.World!.Scale!.NavRangePercent / 100);
        Assert.Equal(150_000, World().Network.NavRangeCells(0));
        Assert.Equal(ShieldSensorRules.NavBaseRangeCells, World(new WorldRules().WithLegacyWorldScale(true)).Network.NavRangeCells(0));
        Assert.True(World().Network.NavRangeCells(0) > World().Network.ScanRangeCells(0), "a scan never reveals what the display hides");
    }

    [Fact]
    public void The_cluster_contacts_come_from_the_seed_and_only_inside_the_field()
    {
        var w = World();
        var n = w.Network;
        var contacts = n.ClustersNear(0, 0, 0, 150_000);
        Assert.NotEmpty(contacts);
        Assert.All(contacts, p => Assert.True(Math.Abs(p.X) <= 150_000 && Math.Abs(p.Z) <= 150_000));
        Assert.Equal(contacts.Select(p => p.Id), World().Network.ClustersNear(0, 0, 0, 150_000).Select(p => p.Id)); // no state: the same again
        Assert.Equal(w.Checksum(), World().Checksum()); // asking for contacts hashes nothing
        // a far point beyond the field radius lists none
        Assert.Empty(n.ClustersNear(3_000_000L * Cell, 0, 3_000_000L * Cell, 20_000));
    }

    // ---- the cruise drive -------------------------------------------------------------------------------------

    [Fact]
    public void Cruise_is_refused_without_the_scale_inside_a_system_and_without_the_research_in_the_campaign()
    {
        void Try(SimWorld w, Reject expected)
        {
            w.Submit(0, new SetCruise(true));
            w.Step();
            Assert.Equal(expected, w.LastResults.Last().Reject);
            Assert.False(w.Network.IsCruising(0));
        }
        Try(World(new WorldRules().WithLegacyWorldScale(true)), Reject.NotAllowedHere);  // the legacy flag
        Try(World(content: TestContent.Core), Reject.NotAllowedHere);                    // content without the block
        Try(World(), Reject.NotAllowedHere);                                             // the home system is a zone

        var campaign = World(mode: GameMode.Campaign);
        var (x, y, z) = VoidPoint(campaign);
        Go(campaign, x, y, z);
        Try(campaign, Reject.Locked);                                                    // the campaign needs the research
        campaign.Network.GrantResearch(C.ResearchIndex("core:cruise_drive"));
        campaign.Submit(0, new SetCruise(true));
        campaign.Step();
        Assert.True(campaign.LastResults.Last().Ok, campaign.LastResults.Last().Reject.ToString());
        Assert.True(campaign.Network.IsCruising(0));

        var research = C.Research[C.ResearchIndex("core:cruise_drive")];
        Assert.Equal(new[] { C.ResearchIndex("core:electronics"), C.ResearchIndex("core:jump_drive") }.OrderBy(i => i), research.Requires.OrderBy(i => i));
        Assert.Contains(Ability.Cruise, research.UnlockAbilities);
    }

    [Fact]
    public void Cruise_spins_up_in_about_twelve_seconds_to_3_km_per_second_and_keeps_the_course_when_the_keys_are_released()
    {
        var w = World();
        var (x, y, z) = VoidPoint(w);
        Go(w, x, y, z);
        var start = Pos(w);
        Thrust(w, 1, 0, 0);
        w.Submit(0, new SetCruise(true));
        w.Step();
        Assert.True(w.LastResults.Last().Ok, w.LastResults.Last().Reject.ToString());
        long cap = 24_000L * 1000;
        int ticks = 0;
        while (Speed(w) < cap * 99 / 100 && ticks < 1200) { w.Step(); ticks++; }
        Assert.InRange(ticks, 330, 390);            // 360 ticks: twelve seconds
        Assert.True(w.Network.IsCruising(0));

        w.Submit(0, new SetThrust(0, 0, 0));        // keys released: the drive keeps its course
        w.Step(60);
        Assert.True(Speed(w) > cap * 97 / 100);
        Assert.True(Pos(w).X > start.X);
        Assert.InRange(Math.Abs(Pos(w).Z - start.Z), 0, 4);
        w.Network.ValidateInvariants();

        w.Submit(0, new SetCruise(false));          // released: the drive brakes the ship down to engine speed
        w.Step(1);
        Assert.False(w.Network.IsCruising(0));
        int braking = 0;
        while (Speed(w) > w.Network.ShipTopSpeedMilli(0) + 1000 && braking < 1200) { w.Step(); braking++; }
        Assert.InRange(braking, 30, 200);
    }

    [Fact]
    public void A_cluster_ninety_kilometres_away_is_reached_in_well_under_two_minutes_at_cruise_against_sixteen_at_boost()
    {
        var w = World();
        var n = w.Network;
        var home = n.Galaxy!.Systems[0];
        var cl = n.ClustersNear(home.X * (long)Cell, 0, home.Z * (long)Cell, 140_000)
            .Select(p => (P: p, D: IntMath.ISqrt((long)p.X * p.X + (long)p.Y * p.Y + (long)p.Z * p.Z)))
            .Where(t => t.D is > 60_000 and < 140_000).OrderBy(t => t.D).First();
        double ux = cl.P.X / (double)cl.D, uy = cl.P.Y / (double)cl.D, uz = cl.P.Z / (double)cl.D;
        long rim = C.World!.SystemRadiusCells + 30;
        Go(w, (long)(ux * rim), (long)(uy * rim), (long)(uz * rim));
        Thrust(w, ux, uy, uz);
        w.Submit(0, new SetCruise(true));
        w.Step();
        Assert.True(w.LastResults.Last().Ok, w.LastResults.Last().Reject.ToString());
        long fastest = 0; int ticks = 0;
        while (n.ZoneEdgeDistance(Pos(w).X, Pos(w).Y, Pos(w).Z, Horizon) > 0 && ticks < 30 * 600)
        {
            w.Step(); ticks++;
            fastest = Math.Max(fastest, Speed(w));
            if (ticks == 30 * 60) w.Submit(0, new SetThrust(0, 0, 0)); // no key held after a minute: the drive keeps the course
        }
        double seconds = ticks / 30.0, km = (cl.D - rim) / 1000.0;
        Assert.True(seconds < 120, $"{km:F0} km took {seconds:F0} s");
        Assert.True(seconds > km / 3.0, "never faster than 3 km/s");
        Assert.InRange(fastest, 24_000_000L * 99 / 100, 24_000_000L + 1000);
        Assert.True(km / 0.096 / 60 > 10, "the same trip at boost speed (96 m/s) is more than ten minutes");
        // it arrives at the zone edge at no more than the drop-out speed: the engines take over
        Assert.True(Speed(w) <= n.ShipTopSpeedMilli(0) + 1_000_000, $"arrived at {Speed(w)}");
        Assert.False(n.IsCruising(0));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_governor_never_lets_a_ship_enter_a_zone_fast_in_random_flights()
    {
        int zoneEntries = 0, engaged = 0;
        for (int flight = 0; flight < 14; flight++)
        {
            var rng = new SplitMix64(0xC0DEUL + (ulong)flight * 7919);
            ulong seed = flight % 2 == 0 ? Seed : 777UL;
            var w = World(seed: seed);
            var n = w.Network;
            var g = n.Galaxy!;
            // start near a random system or cluster, so zones are in play, and run with random thrust and cruise toggles
            var anchor = g.Systems[rng.Next(0, g.Systems.Count - 1)];
            var contacts = n.ClustersNear(anchor.X * (long)Cell, 0, anchor.Z * (long)Cell, 120_000);
            long sx = anchor.X, sy = anchor.Y, sz = anchor.Z;
            if (contacts.Count > 0 && flight % 3 != 0) { var p = contacts[rng.Next(0, contacts.Count - 1)]; sx = p.X + 3_000; sy = p.Y; sz = p.Z; }
            else sx += C.World!.SystemRadiusCells + 400;
            Go(w, sx, sy, sz);
            double dx = 1, dy = 0, dz = 0;
            for (int t = 0; t < 40 * 30; t++)
            {
                if (t % 45 == 0)
                {
                    dx = rng.Next(-100, 100); dy = rng.Next(-20, 20); dz = rng.Next(-100, 100);
                    if (rng.Next(0, 5) == 0) { dx = 0; dy = 0; dz = 0; }
                    Thrust(w, dx, dy, dz);
                    if (rng.Next(0, 3) != 0) w.Submit(0, new SetCruise(true)); else if (rng.Next(0, 6) == 0) w.Submit(0, new SetCruise(false));
                }
                w.Step();
                var (px, py, pz) = Pos(w);
                long d = n.ZoneEdgeDistance(px, py, pz, Horizon);
                long speed = Speed(w), full = n.ShipTopSpeedMilli(0);
                long allowed = n.CruiseAllowed(0);
                if (n.IsCruising(0)) engaged++;
                // the speed never exceeds what the engines or the governor allow here, beyond one tick of braking
                Assert.True(speed <= Math.Max(full, allowed) + 2 * C.World!.Scale!.Cruise.BrakeMilli, $"flight {flight} tick {t}: {speed} > max({full}, {allowed}) at {d}");
                if (d == 0) { zoneEntries++; Assert.True(speed <= full + 2 * C.World!.Scale!.Cruise.BrakeMilli, $"flight {flight} tick {t}: inside a zone at {speed} (engines {full})"); }
            }
            w.Network.ValidateInvariants();
        }
        Assert.True(engaged > 200, $"cruise was engaged for {engaged} ticks");
        Assert.True(zoneEntries > 0, "the flights did visit zones");
    }

    [Fact]
    public void A_flight_with_cruise_replays_clones_and_hashes_the_drive_only_while_it_is_engaged()
    {
        var w = World();
        var (x, y, z) = VoidPoint(w);
        Go(w, x, y, z);
        long before = (long)w.Checksum();
        Thrust(w, 1, 0, 0);
        w.Submit(0, new SetCruise(true));
        w.Step(500);
        Assert.True(w.Network.IsCruising(0));
        var cloneSum = w.Network.Clone().Checksum();
        Assert.Equal(w.Network.Checksum(), cloneSum);
        w.Submit(0, new SetCruise(false));
        w.Step(1);
        var off = w.Network.GetPlayerInfo(0);
        Assert.NotEqual(0L, before);

        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: w.Network.Rules);
        var log = CommandCodec.ReadLog(new MemoryStream(ms.ToArray()));
        // the position set by the test is not in the log, so replay from the same start
        var initial = World();
        Go(initial, x, y, z);
        var replay = SimWorld.Replay(log, w.Tick - initial.Tick, initial: initial);
        Assert.Equal(w.Checksum(), replay.Checksum());
        Assert.Equal(off.X, replay.Network.GetPlayerInfo(0).X);
        replay.Network.ValidateInvariants();
    }

    [Fact]
    public void The_cruise_key_is_N_in_flight_only_and_its_row_and_texts_exist_in_both_languages()
    {
        var fly = InputActions.Active(false, false, false, false);
        Assert.Equal("cruise", InputActions.Find("N", fly)!.Id);
        Assert.Equal("kind", InputActions.Find("N", InputActions.Active(false, true, false, false))!.Id); // with a build tool in hand N keeps its old job
        var off = KeyHints.For(new KeyState { HasTurnInertia = true }).SelectMany(g => g.Rows).Select(r => r.Id);
        Assert.DoesNotContain("cruise", off);
        var rows = KeyHints.For(new KeyState { HasTurnInertia = true, HasCruise = true, Cruising = true }).SelectMany(g => g.Rows).ToList();
        Assert.Equal(KeyRowState.Active, rows.Single(r => r.Id == "cruise").State);
        foreach (var key in new[] { "act.cruise.name", "act.cruise.hint", "key.cruise", "hud.cruise", "toast.cruise.on", "toast.cruise.off", "toast.cruise.zone", "toast.cruise.locked", "toast.cruise.none", "toast.cruise.dropped", "research.core:cruise_drive", "research.core:cruise_drive.desc" })
            foreach (var lang in new[] { Language.Es, Language.En })
            {
                Loc.Lang = lang;
                Assert.NotEqual(key, Loc.T(key));
            }
        Loc.Lang = Language.En;
    }
}
