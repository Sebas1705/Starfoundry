using System.Diagnostics;
using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0040: the infinite rock field. Resource asteroids are generated on demand from the world seed, sector by sector, with a size and a density.</summary>
public class FieldTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static readonly FieldRules F = C.World!.Field!;

    private static SimWorld World(WorldRules? rules = null, ulong seed = Seed, ContentDb? content = null) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, seed, threats: false, stationRules: false, structures: true, rules: rules);

    /// <summary>Puts the pilot at a point (cells) and runs one streaming pass.</summary>
    private static void Go(SimWorld w, long x, long y, long z)
    {
        int ship = w.Network.ShipStructure(0);
        var s = w.Network.GetStructure(ship);
        var p = w.Network.GetPlayerInfo(0);
        long offX = p.X - s.OriginX, offY = p.Y - s.OriginY, offZ = p.Z - s.OriginZ;
        w.Network.SetStructureOrigin(ship, x * Cell - offX, y * Cell - offY, z * Cell - offZ);
        while (w.Network.Tick % F.StepTicks != 0) w.Step();
        w.Step();
    }

    private static List<FieldRock> Sector(ulong seed, int cx, int cy, int cz, int density = 140, Galaxy? g = null)
    {
        var galaxy = g ?? Galaxy.Generate(C, seed);
        var list = new List<FieldRock>();
        SectorField.Generate(F, galaxy.Systems, seed, cx, cy, cz, density, list);
        return list;
    }

    private static List<(int Item, int X, int Y, int Z, int Initial, int Density, int Radius)> Rocks(SimWorld w)
    {
        var n = w.Network;
        var list = new List<(int, int, int, int, int, int, int)>();
        for (int i = 0; i < n.DepositCount; i++)
            if (n.DepositIsFieldRock(i))
            {
                var q = n.DepositPosition(i);
                list.Add((n.DepositItem(i), q.X, q.Y, q.Z, n.DepositInitial(i), n.DepositDensity(i), n.DepositRadiusUnits(i)));
            }
        list.Sort();
        return list;
    }

    // ---- the generator is pure ----------------------------------------------------------------------------

    [Fact]
    public void The_same_sector_gives_the_same_rocks_in_any_visit_order()
    {
        var cells = new List<(int, int, int)>();
        for (int x = -6; x <= 6; x += 3) for (int y = -2; y <= 2; y += 2) for (int z = -6; z <= 6; z += 3) cells.Add((x, y, z));
        var g = Galaxy.Generate(C, Seed);
        var forward = cells.ToDictionary(c => c, c => Sector(Seed, c.Item1, c.Item2, c.Item3, g: g));
        var rng = new SplitMix64(7);
        var shuffled = cells.OrderBy(_ => rng.Next()).ToList();
        foreach (var c in shuffled) Assert.Equal(forward[c], Sector(Seed, c.Item1, c.Item2, c.Item3, g: g));
        for (int i = cells.Count - 1; i >= 0; i--)
        {
            var c = cells[i];
            Assert.Equal(forward[c], Sector(Seed, c.Item1, c.Item2, c.Item3, g: g));
        }
        Assert.Contains(forward.Values, r => r.Count > 0);
    }

    [Fact]
    public void Different_seeds_and_different_sectors_differ()
    {
        Assert.NotEqual(Sector(Seed, 20, 0, 20), Sector(Seed + 1, 20, 0, 20));
        Assert.NotEqual(Sector(Seed, 20, 0, 20), Sector(Seed, 21, 0, 20));
    }

    [Fact]
    public void Sector_keys_roundtrip_including_negative_and_far_sectors()
    {
        foreach (var (x, y, z) in new[] { (0, 0, 0), (-1, 5, -20_000), (20_000, -20_000, 7), (-500_000, 400_000, -1) })
            Assert.Equal((x, y, z), SectorField.Unkey(SectorField.Key(x, y, z)));
        Assert.Equal(-1, SectorField.SectorOf(-1, F.SectorCells));
        Assert.Equal(0, SectorField.SectorOf(F.SectorCells * (long)Cell - 1, F.SectorCells));
        Assert.Equal(1, SectorField.SectorOf(F.SectorCells * (long)Cell, F.SectorCells));
    }

    [Fact]
    public void Rocks_stay_inside_their_sector_clear_of_systems_and_of_each_other()
    {
        var g = Galaxy.Generate(C, Seed);
        int total = 0;
        for (int cx = -14; cx <= 14; cx++)
            for (int cz = -14; cz <= 14; cz++)
                for (int cy = -1; cy <= 1; cy++)
                {
                    var rocks = Sector(Seed, cx, cy, cz, g: g);
                    total += rocks.Count;
                    for (int i = 0; i < rocks.Count; i++)
                    {
                        var r = rocks[i];
                        Assert.InRange(r.X, cx * F.SectorCells + F.MarginCells, (cx + 1) * F.SectorCells - 1 - F.MarginCells);
                        Assert.InRange(r.Y, cy * F.SectorCells + F.MarginCells, (cy + 1) * F.SectorCells - 1 - F.MarginCells);
                        Assert.InRange(r.Z, cz * F.SectorCells + F.MarginCells, (cz + 1) * F.SectorCells - 1 - F.MarginCells);
                        foreach (var s in g.Systems)
                        {
                            double d = Math.Sqrt(Math.Pow(r.X - s.X, 2) + Math.Pow(r.Y - s.Y, 2) + Math.Pow(r.Z - s.Z, 2));
                            Assert.True(d >= F.ExclusionCells, $"rock {d:F0} cells from system {s.Id}");
                        }
                        for (int j = i + 1; j < rocks.Count; j++)
                        {
                            double d = Math.Sqrt(Math.Pow(r.X - rocks[j].X, 2) + Math.Pow(r.Y - rocks[j].Y, 2) + Math.Pow(r.Z - rocks[j].Z, 2)) * Cell;
                            Assert.True(d > (r.Radius + rocks[j].Radius) * 1.6, "two rocks overlap");
                        }
                    }
                }
        Assert.True(total > 500);
    }

    [Fact]
    public void Home_and_its_start_zone_never_hold_a_field_rock()
    {
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var g = Galaxy.Generate(C, seed);
            for (int cx = -3; cx <= 3; cx++)
                for (int cy = -3; cy <= 3; cy++)
                    for (int cz = -3; cz <= 3; cz++)
                        foreach (var r in Sector(seed, cx, cy, cz, g: g))
                            Assert.True((long)r.X * r.X + (long)r.Y * r.Y + (long)r.Z * r.Z >= (long)F.ExclusionCells * F.ExclusionCells);
        }
    }

    [Fact]
    public void Sizes_and_densities_are_spread_and_amount_is_size_times_density()
    {
        var all = new List<FieldRock>();
        for (int cx = 12; cx < 52; cx++) for (int cz = 12; cz < 52; cz++) all.AddRange(Sector(Seed, cx, 0, cz, 100, g: Galaxy.Generate(C, Seed)));
        Assert.True(all.Count > 3000);
        double orePerSector = all.Sum(r => (double)r.Amount) / 1600.0;
        Assert.InRange(orePerSector, 200, 450); // about a system of veins (4 to 6 thousand ore) per scanner sphere: the balance target (ADR 0040)
        double meanCount = all.Count / 1600.0;
        Assert.InRange(meanCount, 2.0, 3.0); // 0..5 per sector, mean 2.5, a few dropped for lack of room
        var masses = all.Select(r => r.Mass).OrderBy(m => m).ToList();
        Assert.InRange(masses[0], F.MassMin, F.MassMin + 40);
        Assert.True(masses[^1] > F.MassMax * 0.8);
        Assert.True(masses[masses.Count / 2] < masses.Average() * 0.6, "many small rocks, a few huge ones");
        Assert.All(all, r => Assert.InRange(r.Density, F.DensityMin, F.DensityMax));
        Assert.Contains(all, r => r.Density < 70);
        Assert.Contains(all, r => r.Density > 150);
        Assert.All(all, r => Assert.Equal(F.AmountOf(r.Mass, r.Density), r.Amount));
        // Bigger and denser rocks hold more; a bigger rock is drawn bigger.
        Assert.True(F.AmountOf(2000, 100) > F.AmountOf(1000, 100));
        Assert.True(F.AmountOf(1000, 160) > F.AmountOf(1000, 80));
        Assert.Equal(F.AmountOf(1000, 100) * 2, F.AmountOf(2000, 100));
        Assert.Equal(F.AmountOf(1000, 100) * 2, F.AmountOf(1000, 200));
        Assert.True(FieldRules.RadiusOfMass(8000) > FieldRules.RadiusOfMass(1000));
        Assert.Equal(DepositRules.BaseRockRadiusUnits, FieldRules.RadiusOfMass(1000));
        Assert.Equal(2 * DepositRules.BaseRockRadiusUnits, FieldRules.RadiusOfMass(8000));
        Assert.Contains(all, r => r.Radius * 1.6 > 3 * Cell);
        // The ores that are far away only show up far away.
        var ti = C.ItemIndex("core:titanium_ore");
        Assert.DoesNotContain(all, r => r.Item == ti && Math.Sqrt((double)r.X * r.X + (double)r.Z * r.Z) < 2500);
    }

    [Fact]
    public void The_resource_density_rule_scales_the_rock_count()
    {
        int Count(int pct)
        {
            var g = Galaxy.Generate(C, Seed);
            int n = 0;
            for (int cx = 12; cx < 40; cx++) for (int cz = 12; cz < 40; cz++) n += Sector(Seed, cx, 0, cz, pct, g).Count;
            return n;
        }
        int low = Count(100), high = Count(150);
        Assert.InRange(high * 100.0 / low, 125, 170);
    }

    [Fact]
    public void Integer_cube_root_is_exact()
    {
        foreach (long v in new long[] { 0, 1, 7, 8, 26, 27, 999_999_999, 1_000_000_000, 8_000_000_000_000L, long.MaxValue })
        {
            long r = IntMath.ICbrt(v);
            Assert.True(r * r * r <= v);
            Assert.True(v == long.MaxValue || (r + 1) * (r + 1) * (r + 1) > v);
        }
    }

    // ---- streaming in a world ---------------------------------------------------------------------------------

    [Fact]
    public void At_home_no_rock_is_in_reach_and_the_scan_finds_none()
    {
        var w = World();
        var n = w.Network;
        Assert.True(n.HasRockField);
        w.Step(600);
        // Whole sectors near the ship load, but no rock lies within the exclusion distance of home: they are beyond the scanner and hidden.
        for (int i = 0; i < n.DepositCount; i++)
            if (n.DepositIsFieldRock(i))
            {
                var q = n.DepositPosition(i);
                Assert.True((long)q.X * q.X + (long)q.Y * q.Y + (long)q.Z * q.Z >= (long)F.ExclusionCells * F.ExclusionCells);
                Assert.False(n.IsDepositRevealed(i));
            }
        w.Submit(0, new Scan());
        w.Step();
        Assert.DoesNotContain(Enumerable.Range(0, n.DepositCount), i => n.DepositIsFieldRock(i) && n.IsDepositRevealed(i));
        w.ValidateInvariants();
    }

    [Fact]
    public void Rocks_load_around_the_ship_as_hidden_deposits_and_a_scan_reveals_them()
    {
        var w = World();
        Go(w, 4000, 20, -3000);
        var n = w.Network;
        Assert.True(n.FieldLoadedRocks > 10, $"loaded {n.FieldLoadedRocks}");
        Assert.True(n.FieldLoadedRocks <= F.MaxLoaded);
        var rock = Enumerable.Range(0, n.DepositCount).First(i => n.DepositIsFieldRock(i));
        Assert.False(n.IsDepositRevealed(rock));
        w.Submit(0, new Scan());
        w.Step();
        Assert.Contains(Enumerable.Range(0, n.DepositCount), i => n.DepositIsFieldRock(i) && n.IsDepositRevealed(i));
        w.ValidateInvariants();
    }

    [Fact]
    public void Two_visit_orders_give_the_same_rocks_at_the_same_place()
    {
        var a = World();
        Go(a, 3000, 0, 3000); Go(a, 9000, 100, -6000); Go(a, -5000, 0, 2500); Go(a, 6000, 0, 6000);
        var b = World();
        Go(b, -9000, 0, -9000); Go(b, 6000, 0, 6000);
        var near = (int X, int Y, int Z) => Math.Abs(X - 6000) < 300 && Math.Abs(Y) < 300 && Math.Abs(Z - 6000) < 300;
        var ra = Rocks(a).Where(r => near(r.X, r.Y, r.Z)).ToList();
        var rb = Rocks(b).Where(r => near(r.X, r.Y, r.Z)).ToList();
        Assert.NotEmpty(ra);
        Assert.Equal(ra, rb);
    }

    [Fact]
    public void Far_sectors_are_forgotten_and_the_slots_are_reused_so_memory_stays_bounded()
    {
        var w = World();
        var n = w.Network;
        int maxSlots = 0, maxLoaded = 0;
        for (int hop = 0; hop < 60; hop++)
        {
            Go(w, 2500 + hop * 700, hop % 5 * 50, -4000 + hop * 300);
            maxSlots = Math.Max(maxSlots, n.DepositCount);
            maxLoaded = Math.Max(maxLoaded, n.FieldLoadedRocks);
        }
        Assert.True(maxLoaded <= F.MaxLoaded);
        // 60 hops of 700 cells would load thousands of rocks if nothing were forgotten.
        Assert.True(maxSlots < 12 + 4 * maxLoaded / 3 + 200, $"slots {maxSlots} loaded {maxLoaded}");
        Assert.True(n.FieldLoadedSectors < 200);
        w.ValidateInvariants();
        var clone = n.Clone();
        Assert.Equal(n.Checksum(), clone.Checksum());
    }

    [Fact]
    public void Flying_on_is_unbounded_past_the_old_world_limit_and_a_legacy_world_still_stops_there()
    {
        long beyond = SimWorld.MaxCoord * 3L; // 30000 cells
        var w = World();
        Go(w, beyond, 0, -beyond);
        var p = w.Network.GetPlayerInfo(0);
        Assert.Equal(beyond * Cell, p.X);
        Assert.True(w.Network.FieldLoadedRocks > 0);
        w.ValidateInvariants();

        var legacy = World(new WorldRules().WithLegacyField(true));
        Assert.False(legacy.Network.HasRockField);
        Go(legacy, beyond, 0, -beyond);
        Assert.Equal(SimWorld.MaxCoord * (long)Cell, legacy.Network.GetPlayerInfo(0).X);
        Assert.Equal(0, legacy.Network.FieldLoadedRocks);
    }

    [Fact]
    public void Mining_a_rock_empties_and_destroys_it_and_the_world_remembers_across_a_reload()
    {
        var w = World();
        Go(w, 4000, 0, 4000);
        var n = w.Network;
        w.Submit(0, new Scan()); w.Step();
        int rock = Enumerable.Range(0, n.DepositCount).Where(i => n.DepositIsFieldRock(i) && n.IsDepositRevealed(i)).OrderBy(i => n.DepositInitial(i)).First();
        int initial = n.DepositInitial(rock), item = n.DepositItem(rock);
        Assert.InRange(initial, 1, 150);
        var q = n.DepositPosition(rock);
        // Reveal it, then hover the ship's laser over it and mine.
        Assert.True(n.IsDepositRevealed(rock));
        var laser = n.MiningLasers(0)[0];
        var ship = n.GetStructure(n.ShipStructure(0));
        long offX = laser.X - ship.OriginX, offY = laser.Y - ship.OriginY, offZ = laser.Z - ship.OriginZ;
        n.SetStructureOrigin(ship.Id, q.X * (long)Cell - offX, q.Y * (long)Cell - offY + 2 * Cell, q.Z * (long)Cell - offZ);
        w.Submit(0, new StartMining(rock)); w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        int before = (int)(n.MinedByHand + n.MinedByDrones);
        for (int i = 0; i < 6000 && n.DepositRemaining(rock) > 0; i++) w.Step();
        Assert.Equal(0, n.DepositRemaining(rock));
        Assert.Equal(initial, (int)(n.MinedByHand + n.MinedByDrones) - before); // every unit left the rock into the hold or the pickups
        w.ValidateInvariants();
        w.Step();
        Assert.Equal(-1, n.MiningDeposit(0)); // the order is dropped on the next tick

        // Fly far away so the sector is forgotten, then come back: the rock stays gone, and the others are as they were.
        bool Near(int X, int Y, int Z) => Math.Abs(X - 4000) < 300 && Math.Abs(Y) < 300 && Math.Abs(Z - 4000) < 300; // well inside the load radius both times
        var others = Rocks(w).Where(r => Near(r.X, r.Y, r.Z) && !(r.X == q.X && r.Y == q.Y && r.Z == q.Z)).ToList();
        Go(w, 4000 + 3000, 0, 4000);
        Assert.DoesNotContain(Rocks(w), r => r.X == q.X && r.Y == q.Y && r.Z == q.Z);
        Assert.True(n.FieldRememberedRocks >= 1);
        w.ValidateInvariants();
        Go(w, 4000, 0, 4000);
        w.ValidateInvariants();
        var back = Rocks(w);
        var again = Enumerable.Range(0, n.DepositCount).Where(i => n.DepositIsFieldRock(i) && n.DepositPosition(i) == q).ToList();
        Assert.Single(again);
        Assert.Equal(0, n.DepositRemaining(again[0])); // destroyed, not regrown
        Assert.Equal(item, n.DepositItem(again[0]));
        Assert.Equal(others, back.Where(r => Near(r.X, r.Y, r.Z) && !(r.X == q.X && r.Y == q.Y && r.Z == q.Z)).ToList());
        w.Step(20000);
        Assert.Equal(0, n.DepositRemaining(again[0])); // field rocks never regrow
    }

    [Fact]
    public void A_partly_mined_rock_keeps_its_remaining_ore_after_being_forgotten()
    {
        var w = World();
        Go(w, 4200, 0, 4200);
        var n = w.Network;
        w.Submit(0, new Scan()); w.Step();
        int rock = Enumerable.Range(0, n.DepositCount).Where(i => n.DepositIsFieldRock(i) && n.IsDepositRevealed(i)).OrderByDescending(i => n.DepositInitial(i)).First();
        var q = n.DepositPosition(rock);
        int initial = n.DepositInitial(rock);
        Assert.True(initial > 50);
        var laser = n.MiningLasers(0)[0];
        var ship = n.GetStructure(n.ShipStructure(0));
        n.SetStructureOrigin(ship.Id, q.X * (long)Cell - (laser.X - ship.OriginX), q.Y * (long)Cell - (laser.Y - ship.OriginY) + 2 * Cell, q.Z * (long)Cell - (laser.Z - ship.OriginZ));
        w.Submit(0, new StartMining(rock)); w.Step(400);
        int left = n.DepositRemaining(rock);
        Assert.InRange(left, 1, initial - 1);
        w.Submit(0, new StopMining()); w.Step();
        Go(w, 4200 + 3500, 0, 4200);
        Go(w, 4200, 0, 4200);
        var again = Enumerable.Range(0, n.DepositCount).Single(i => n.DepositIsFieldRock(i) && n.DepositPosition(i) == q);
        Assert.Equal(left, n.DepositRemaining(again));
        Assert.Equal(initial, n.DepositInitial(again));
        w.ValidateInvariants();
    }

    [Fact]
    public void A_rock_being_mined_is_pinned_and_not_forgotten_when_the_ship_goes_away()
    {
        var w = World();
        Go(w, 4000, 0, 4000);
        var n = w.Network;
        w.Submit(0, new Scan()); w.Step();
        int rock = Enumerable.Range(0, n.DepositCount).Where(i => n.DepositIsFieldRock(i) && n.IsDepositRevealed(i)).OrderByDescending(i => n.DepositInitial(i)).First();
        var q = n.DepositPosition(rock);
        var laser = n.MiningLasers(0)[0];
        var ship = n.GetStructure(n.ShipStructure(0));
        n.SetStructureOrigin(ship.Id, q.X * (long)Cell - (laser.X - ship.OriginX), q.Y * (long)Cell - (laser.Y - ship.OriginY) + 2 * Cell, q.Z * (long)Cell - (laser.Z - ship.OriginZ));
        w.Submit(0, new StartMining(rock)); w.Step();
        Assert.Equal(rock, n.MiningDeposit(0));
        Go(w, 4000 + 3500, 0, 4000);
        Assert.Contains(Rocks(w), r => r.X == q.X && r.Y == q.Y && r.Z == q.Z); // still loaded: the order points at it
        w.ValidateInvariants();
    }

    // ---- determinism, saves and old logs ---------------------------------------------------------------------

    [Fact]
    public void A_flight_through_the_field_replays_and_clones_to_the_same_checksum()
    {
        var w = World();
        w.Submit(0, new SetBoost(true));
        w.Submit(0, new SetMovement(1, 0, 1));
        for (int i = 0; i < 12; i++) w.Step(300);
        w.Submit(0, new Scan()); w.Step(2);
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Step(300);
        w.ValidateInvariants();
        var replay = SimWorld.Replay(w.Log, w.Tick, initial: World());
        Assert.Equal(w.Checksum(), replay.Checksum());
        Assert.Equal(w.Network.Checksum(), w.Network.Clone().Checksum());
        Assert.True(w.Network.FieldLoadedRocks > 0, "the flight should have left the exclusion zone of home");
    }

    [Fact]
    public void A_log_older_than_version_21_reads_with_the_legacy_flag_and_replays_without_the_field()
    {
        var recorded = World(new WorldRules().WithLegacyField(true).WithLegacyDock(true));
        recorded.Submit(0, new SetMovement(1, 0, 1));
        recorded.Step(900);
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: recorded.Network.Rules);
        var bytes = ms.ToArray();
        for (byte version = 17; version <= 20; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            var log = CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyField, $"version {version}");
            var replay = SimWorld.Replay(log, recorded.Tick, initial: World(rules));
            Assert.Equal(recorded.Checksum(), replay.Checksum());
            Assert.False(replay.Network.HasRockField);
        }
        var current = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var cur);
        Assert.Equal(23, bytes[4]);
        Assert.True(cur.LegacyField); // this log was written for a legacy-field world, so its header says so
        var fresh = new MemoryStream();
        CommandCodec.WriteLog(fresh, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, structures: true);
        CommandCodec.ReadLog(new MemoryStream(fresh.ToArray()), out _, out _, out _, out _, out _, out _, out var current2);
        Assert.False(current2.LegacyField);
        var older = fresh.ToArray(); older[4] = 19;
        CommandCodec.ReadLog(new MemoryStream(older), out _, out _, out _, out _, out _, out _, out var old2);
        Assert.True(old2.LegacyField && old2.WithoutLegacyFlags().IsDefault && old2.HashesAsDefault);
    }

    [Fact]
    public void The_legacy_field_flag_roundtrips_and_hashes_as_default_rules_and_no_preset_has_it()
    {
        var r = new WorldRules().WithLegacyField(true);
        Assert.True(r.LegacyField);
        Assert.Equal(r, WorldRules.Deserialize(r.Serialize()));
        Assert.True(r.HashesAsDefault);
        Assert.True(r.WithoutLegacyFlags().IsDefault);
        Assert.True(r.WithLegacyInventory(true).LegacyField);
        Assert.True(r.WithLegacyFeel(true).LegacyField);
        Assert.True(r.Clamped().LegacyField);
        Assert.True(r.WithResourceDensity(120).LegacyField);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyField));
        Assert.NotEqual(r, WorldRules.Default);
    }

    [Fact]
    public void A_legacy_field_world_hashes_as_before_and_the_field_adds_state_only_once_rocks_exist()
    {
        var b = World(new WorldRules().WithLegacyField(true));
        var plain = SimWorld.CreateGalaxy(TestContent.CoreFloor, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        var legacyRules = SimWorld.CreateGalaxy(TestContent.CoreFloor, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: new WorldRules().WithLegacyField(true));
        Assert.Equal(legacyRules.Checksum(), b.Checksum());
        Assert.Equal(0, b.Network.DepositCount - b.Network.Galaxy!.Deposits.Count);
        Assert.Equal(0, plain.Network.FieldLoadedRocks); // before the first streaming pass
        b.Step(300);
        Assert.Equal(0, b.Network.FieldLoadedRocks);
        Assert.Equal(b.Network.Galaxy!.Deposits.Count, b.Network.DepositCount);
    }

    [Fact]
    public void Content_without_a_field_has_no_field_and_the_field_section_is_validated()
    {
        var noField = ContentDb.FromFile(TestContent.LegacyPath);
        Assert.Null(noField.World?.Field);
        ContentDb Variant(Action<JsonObject> edit)
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
            edit(root["world"]!["field"]!.AsObject());
            return ContentDb.FromJson(root.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(() => Variant(f => f["keep_cells"] = 380));          // keep must exceed load
        Assert.Throws<InvalidDataException>(() => Variant(f => f["margin_cells"] = 2));          // margin must hold the biggest rock
        Assert.Throws<InvalidDataException>(() => Variant(f => f["items"] = new JsonArray()));   // needs an item
        Assert.Throws<InvalidDataException>(() => Variant(f => f["bogus"] = 1));                 // unknown key
        Assert.Throws<InvalidDataException>(() => Variant(f => f["items"] = JsonNode.Parse("""[{"item":"core:ghost","weight":1}]""")));
        Assert.NotEqual(C.Hash, Variant(f => f["unit_amount"] = 201).Hash); // the field is part of the content hash
    }

    // ---- budgets (loose: they catch an order of magnitude, not noise) -------------------------------------------

    [Fact]
    public void Generating_sectors_and_streaming_stay_within_a_loose_budget()
    {
        var g = Galaxy.Generate(C, Seed);
        var list = new List<FieldRock>();
        var sw = Stopwatch.StartNew();
        for (int cx = -40; cx < 40; cx++) for (int cz = -40; cz < 40; cz++) for (int cy = -1; cy < 2; cy++) { list.Clear(); SectorField.Generate(F, g.Systems, Seed, cx, cy, cz, 140, list); }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"19200 sectors took {sw.ElapsedMilliseconds} ms"); // about 0.1 ms each in practice
        Assert.True(sw.Elapsed.TotalMilliseconds / 19200 < 0.5);

        var w = World();
        w.Submit(0, new SetBoost(true));
        w.Submit(0, new SetMovement(1, 0, 1));
        w.Step(60); // warm up
        sw.Restart();
        w.Step(6000);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 6000, $"6000 ticks of boosted flight took {sw.ElapsedMilliseconds} ms"); // 1 ms a tick would be a thousand times the real cost
        Assert.True(w.Network.FieldLoadedRocks <= F.MaxLoaded);
        Assert.True(w.Network.DepositCount < 12 + F.MaxLoaded + 50);
    }
}
