using System.Diagnostics;
using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0046: the clustered rock field. Clusters, belts and shells of rocks with voids between them, size classes and rare giants, all from integer hashes.</summary>
public class ClusterFieldTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static readonly FieldRules F = C.World!.Field!;
    private static readonly ClusterRules K = F.Clusters!;
    private static readonly Galaxy G = Galaxy.Generate(C, Seed, legacyScale: true); // these tests are about the field as generated before the world scale (ADR 0053); WorldScaleTests covers the new one

    private static SimWorld World(WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: (rules ?? new WorldRules()).WithLegacyWorldScale(true));

    private static List<FieldRock> Sector(int cx, int cy, int cz, int density = 100, ulong seed = Seed)
    {
        var list = new List<FieldRock>();
        ClusterField.Generate(F, K, G.Systems, seed, cx, cy, cz, density, list);
        return list;
    }

    private static void Go(SimWorld w, long x, long y, long z)
    {
        int ship = w.Network.ShipStructure(0);
        var s = w.Network.GetStructure(ship);
        var p = w.Network.GetPlayerInfo(0);
        w.Network.SetStructureOrigin(ship, x * Cell - (p.X - s.OriginX), y * Cell - (p.Y - s.OriginY), z * Cell - (p.Z - s.OriginZ));
        while (w.Network.Tick % F.StepTicks != 0) w.Step();
        w.Step();
    }

    /// <summary>The richest cluster in a box far from home, with every ore allowed (richness 5, the biggest radius).</summary>
    private static ClusterParent RichCluster(int minRichness = 5)
    {
        var all = ClusterField.ParentsInBox(F, K, Seed, 6000, 24000, -300, 300, 6000, 24000);
        return all.Where(p => p.Richness >= minRichness).OrderByDescending(p => p.Radius).First();
    }

    private static int Fl(int cells) => (int)Math.Floor(cells / 200.0);

    private static List<FieldRock> Cluster(ClusterParent p)
    {
        var res = new List<FieldRock>();
        int r = p.Radius + 40;
        for (int x = Fl(p.X - r); x <= Fl(p.X + r); x++)
            for (int y = Fl(p.Y - r); y <= Fl(p.Y + r); y++)
                for (int z = Fl(p.Z - r); z <= Fl(p.Z + r); z++)
                    res.AddRange(Sector(x, y, z, WorldRules.DefaultDensityPercent).Where(k => k.Cluster == p.Id));
        return res;
    }

    // ---- the generator is pure -------------------------------------------------------------------------------

    [Fact]
    public void The_same_sector_gives_the_same_rocks_in_any_visit_order()
    {
        var p = RichCluster();
        int bx = p.X / 200, by = p.Y / 200, bz = p.Z / 200;
        var cells = new List<(int, int, int)>();
        for (int x = -4; x <= 4; x++) for (int y = -1; y <= 1; y++) for (int z = -4; z <= 4; z++) cells.Add((bx + x, by + y, bz + z));
        var forward = cells.ToDictionary(c => c, c => Sector(c.Item1, c.Item2, c.Item3));
        var rng = new SplitMix64(9);
        foreach (var c in cells.OrderBy(_ => rng.Next())) Assert.Equal(forward[c], Sector(c.Item1, c.Item2, c.Item3));
        for (int i = cells.Count - 1; i >= 0; i--) Assert.Equal(forward[cells[i]], Sector(cells[i].Item1, cells[i].Item2, cells[i].Item3));
        Assert.True(forward.Values.Sum(l => l.Count) > 150, "the cluster should fill its neighbourhood");
        Assert.NotEqual(Sector(bx, by, bz), Sector(bx, by, bz, seed: Seed + 1));
    }

    [Fact]
    public void Parents_are_a_pure_function_of_the_super_sector_and_their_id_names_them()
    {
        var a = ClusterField.ParentsInBox(F, K, Seed, -20000, 20000, -2000, 2000, -20000, 20000);
        var b = ClusterField.ParentsInBox(F, K, Seed, -20000, 20000, -2000, 2000, -20000, 20000);
        Assert.Equal(a, b);
        Assert.True(a.Count > 100);
        Assert.Equal(a.Count, a.Select(p => p.Id).Distinct().Count());
        foreach (var p in a.Take(60))
        {
            Assert.Equal(p, ClusterField.ParentById(F, K, Seed, p.Id));
            Assert.InRange(p.Richness, 1, 5);
            Assert.InRange(p.Shape, 0, 2);
        }
        Assert.Null(ClusterField.ParentById(F, K, Seed, -1));
        // more density, more rocks per sector at the core, the same places
        var dense = ClusterField.ParentsInBox(F, K, Seed, -20000, 20000, -2000, 2000, -20000, 20000, 140);
        Assert.Equal(a.Select(p => (p.Id, p.X, p.Y, p.Z)), dense.Select(p => (p.Id, p.X, p.Y, p.Z)));
    }

    [Fact]
    public void Every_rock_stays_inside_its_sector_and_inside_the_world_clear_of_systems_and_of_every_neighbour()
    {
        var p = RichCluster(4);
        int bx = p.X / 200, by = p.Y / 200, bz = p.Z / 200;
        var bySector = new Dictionary<(int, int, int), List<FieldRock>>();
        for (int x = -3; x <= 3; x++) for (int y = -1; y <= 1; y++) for (int z = -3; z <= 3; z++) bySector[(bx + x, by + y, bz + z)] = Sector(bx + x, by + y, bz + z, 140);
        int checkedRocks = 0;
        foreach (var ((cx, cy, cz), rocks) in bySector)
        {
            Assert.True(rocks.Count <= K.MaxRocksPerSector);
            for (int i = 0; i < rocks.Count; i++)
            {
                var r = rocks[i];
                int reach = ClusterRules.ReachCellsOf(r.Radius);
                Assert.InRange(r.X - reach, cx * 200, (cx + 1) * 200 - 1);
                Assert.InRange(r.Y - reach, cy * 200, (cy + 1) * 200 - 1);
                Assert.InRange(r.Z - reach, cz * 200, (cz + 1) * 200 - 1);
                Assert.InRange(r.X + reach, cx * 200, (cx + 1) * 200 - 1);
                Assert.InRange(r.Y + reach, cy * 200, (cy + 1) * 200 - 1);
                Assert.InRange(r.Z + reach, cz * 200, (cz + 1) * 200 - 1);
                Assert.Equal(F.AmountOf(r.Mass, r.Density), r.Amount);
                foreach (var s in G.Systems)
                    Assert.True(Math.Sqrt(Math.Pow(r.X - s.X, 2) + Math.Pow(r.Y - s.Y, 2) + Math.Pow(r.Z - s.Z, 2)) >= F.ExclusionCells);
                // against every rock of this and the 26 neighbouring sectors: the drawn rocks (1.6 radii) never overlap, across faces either
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                {
                    if (!bySector.TryGetValue((cx + dx, cy + dy, cz + dz), out var other)) continue;
                    foreach (var o in other)
                    {
                        if ((dx == 0 && dy == 0 && dz == 0 && o.Index == r.Index)) continue;
                        double d = Math.Sqrt(Math.Pow(r.X - o.X, 2) + Math.Pow(r.Y - o.Y, 2) + Math.Pow(r.Z - o.Z, 2)) * Cell;
                        Assert.True(d > (r.Radius + o.Radius) * 1.6, "two rocks overlap");
                    }
                }
                checkedRocks++;
            }
        }
        Assert.True(checkedRocks > 200);
    }

    [Fact]
    public void Rocks_near_systems_keep_the_clearance_and_the_start_zone_stays_empty()
    {
        int near = 0, total = 0;
        foreach (var s in G.Systems)
        {
            int cx0 = (int)Math.Floor((s.X - 700) / 200.0), cx1 = (s.X + 700) / 200, cz0 = (int)Math.Floor((s.Z - 700) / 200.0), cz1 = (s.Z + 700) / 200;
            for (int cx = cx0; cx <= cx1; cx++) for (int cz = cz0; cz <= cz1; cz++) for (int cy = -2; cy <= 2; cy++)
                foreach (var r in Sector(cx, cy, cz, 140))
                {
                    total++;
                    foreach (var t in G.Systems)
                        Assert.True(Math.Sqrt(Math.Pow(r.X - t.X, 2) + Math.Pow(r.Y - t.Y, 2) + Math.Pow(r.Z - t.Z, 2)) >= F.ExclusionCells, "a rock inside the clearance of a system");
                    if (Math.Sqrt(Math.Pow(r.X - s.X, 2) + Math.Pow(r.Y - s.Y, 2) + Math.Pow(r.Z - s.Z, 2)) < F.ExclusionCells + 150) near++;
                }
        }
        Assert.True(total > 0);
        var w = World();
        w.Step(600);
        var n = w.Network;
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
    }

    // ---- statistics: voids, concentrations, size classes, giants -----------------------------------------------

    [Fact]
    public void Most_sectors_are_void_the_ore_sits_in_few_of_them_and_the_sizes_are_spread_with_rare_giants()
    {
        int empty = 0, sectors = 0;
        long totalOre = 0;
        var perSector = new List<long>();
        var radii = new List<int>();
        int giants = 0, rocks = 0, maxAmount = 0;
        for (int x = 60; x < 100; x++) for (int y = -3; y < 3; y++) for (int z = 60; z < 100; z++)
        {
            var list = Sector(x, y, z);
            sectors++;
            if (list.Count == 0) empty++;
            long ore = 0;
            foreach (var r in list)
            {
                ore += r.Amount; rocks++; radii.Add(r.Radius); maxAmount = Math.Max(maxAmount, r.Amount);
                if (r.Mass >= K.GiantMass.Min) giants++;
            }
            totalOre += ore; perSector.Add(ore);
        }
        double emptyShare = empty / (double)sectors;
        Assert.InRange(emptyShare, 0.55, 0.80); // a void between clusters (the uniform field had 17 percent)
        perSector.Sort((a, b) => b.CompareTo(a));
        long acc = 0; int n80 = 0;
        foreach (long v in perSector) { acc += v; n80++; if (acc * 100 >= totalOre * 80) break; }
        Assert.True(n80 * 100.0 / sectors < 8, $"80 percent of the ore in {n80 * 100.0 / sectors:F1} percent of the sectors (the uniform field needed 38)");
        radii.Sort();
        double medianCells = radii[radii.Count / 2] / (double)Cell, p99 = radii[radii.Count * 99 / 100] / (double)Cell;
        Assert.InRange(medianCells, 2.5, 4.5); // the uniform field's median was 1.2 cells
        Assert.InRange(p99, 6, 9);
        Assert.InRange(radii[^1] / (double)Cell, 12, 17); // a giant is a landmark
        Assert.InRange(giants / (double)rocks, 0.001, 0.02);
        Assert.InRange(maxAmount, 4000, 20000); // the biggest rock held 851 before
    }

    [Fact]
    public void A_cluster_holds_rocks_and_ore_in_the_range_of_its_richness()
    {
        var inner = ClusterField.ParentsInBox(F, K, Seed, 66 * 200, 94 * 200, -300, 300, 66 * 200, 94 * 200);
        Assert.True(inner.Count >= 25);
        var byId = inner.ToDictionary(p => p.Id, _ => new List<FieldRock>());
        for (int x = 60; x < 100; x++) for (int y = -3; y < 3; y++) for (int z = 60; z < 100; z++)
            foreach (var r in Sector(x, y, z)) if (r.Cluster >= 0 && byId.TryGetValue(r.Cluster, out var l)) l.Add(r);
        foreach (var grp in inner.GroupBy(p => p.Richness))
        {
            var counts = grp.Select(p => byId[p.Id].Count).OrderBy(v => v).ToList();
            var ore = grp.Select(p => byId[p.Id].Sum(r => (long)r.Amount)).OrderBy(v => v).ToList();
            int rocksP50 = counts[counts.Count / 2];
            long oreP50 = ore[ore.Count / 2];
            switch (grp.Key)
            {
                case 1: Assert.InRange(rocksP50, 5, 30); Assert.InRange(oreP50, 300, 3000); break;
                case 2: Assert.InRange(rocksP50, 15, 90); Assert.InRange(oreP50, 1500, 15000); break;
                case 3: Assert.InRange(rocksP50, 40, 230); Assert.InRange(oreP50, 6000, 60000); break;
                case 4: Assert.InRange(rocksP50, 90, 480); Assert.InRange(oreP50, 15000, 120000); break;
                case 5: Assert.InRange(rocksP50, 200, 700); Assert.InRange(oreP50, 40000, 200000); break;
            }
        }
        var all = inner.Select(p => byId[p.Id].Count).OrderBy(v => v).ToList();
        Assert.InRange(all[all.Count / 2], 15, 120);
        Assert.InRange(all[^1], 150, 700);
        // rich clusters pay better (density follows richness) and their dominant ore is the most common
        var rich = inner.Where(p => p.Richness >= 4).SelectMany(p => byId[p.Id]).ToList();
        var poor = inner.Where(p => p.Richness == 1).SelectMany(p => byId[p.Id]).ToList();
        Assert.True(rich.Average(r => r.Density) > poor.Average(r => r.Density) + 30);
        foreach (var p in inner.Where(q => byId[q.Id].Count >= 20))
            Assert.True(byId[p.Id].Count(r => r.Item == p.Item) * 100 >= byId[p.Id].Count * 55, "a cluster is mostly its dominant ore");
    }

    // ---- clusters cross sector borders ------------------------------------------------------------------------

    [Fact]
    public void A_cluster_crosses_sector_borders_and_the_sim_loads_the_same_rocks_as_the_generator()
    {
        var p = RichCluster();
        var rocks = Cluster(p);
        Assert.True(rocks.Count > 150);
        var sectorsHit = rocks.Select(r => (r.X / 200 - (r.X < 0 ? 1 : 0), r.Y / 200 - (r.Y < 0 ? 1 : 0), r.Z / 200 - (r.Z < 0 ? 1 : 0))).Distinct().Count();
        Assert.True(sectorsHit >= 8, $"the cluster spans {sectorsHit} sectors");
        foreach (var r in rocks) // a rock belongs to a cluster that reaches it
        {
            long dx = r.X - p.X, dy = r.Y - p.Y, dz = r.Z - p.Z;
            Assert.True(dx * dx + dy * dy + dz * dz <= (long)p.Radius * p.Radius * 4 * (p.Shape == 1 ? 16 : 1) + 4000, "a rock far outside its cluster");
        }

        var w = World();
        Go(w, p.X, p.Y, p.Z);
        var n = w.Network;
        Assert.True(n.HasRockClusters);
        var loaded = new List<(int, int, int, int)>();
        for (int i = 0; i < n.DepositCount; i++)
            if (n.DepositIsFieldRock(i) && n.DepositClusterId(i) == p.Id) { var q = n.DepositPosition(i); loaded.Add((q.X, q.Y, q.Z, n.DepositInitial(i))); }
        Assert.True(loaded.Count > 60, $"{loaded.Count} cluster rocks loaded around its centre");
        // every loaded rock of the cluster is a rock of the generator, with its amount, and the readers name the cluster
        var expected = rocks.Select(r => (r.X, r.Y, r.Z, Math.Max(1, WorldRules.Scale(r.Amount, 100)))).ToHashSet();
        foreach (var r in loaded) Assert.Contains(r, expected);
        int any = Enumerable.Range(0, n.DepositCount).First(i => n.DepositIsFieldRock(i) && n.DepositClusterId(i) == p.Id);
        Assert.Equal(p.Item, n.DepositCluster(any)!.Value.Item);
        Assert.True(n.FieldLoadedRocks <= F.MaxLoaded);
        w.ValidateInvariants();
        Assert.Equal(w.Network.Checksum(), w.Network.Clone().Checksum());
    }

    // ---- streaming stays bounded -------------------------------------------------------------------------------

    [Fact]
    public void Flying_through_a_rich_cluster_stays_under_the_cap_and_frees_slots_behind()
    {
        var p = RichCluster();
        var w = World();
        var n = w.Network;
        int peak = 0, slotsPeak = 0;
        for (int step = -12; step <= 12; step++)
        {
            Go(w, p.X + step * 80, p.Y, p.Z);
            peak = Math.Max(peak, n.FieldLoadedRocks);
            slotsPeak = Math.Max(slotsPeak, n.DepositCount);
            Assert.True(n.FieldLoadedRocks <= F.MaxLoaded);
        }
        Assert.True(peak > 150, $"peak {peak}");
        w.ValidateInvariants();
        int deposits = n.DepositCount;
        Go(w, p.X + 20000, p.Y, p.Z + 20000);
        Assert.True(n.FieldLoadedRocks < peak / 3, "the cluster was forgotten behind the ship");
        Assert.Equal(deposits, n.DepositCount); // slots are reused, the list does not grow back
        w.ValidateInvariants();
    }

    [Fact]
    public void A_full_cap_waits_without_breaking_and_a_mined_sector_stays_pinned()
    {
        var p = RichCluster();
        // a tiny cap: sectors that do not fit wait and the world stays valid
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        root["world"]!["field"]!["max_loaded"] = 40;
        var small = ContentDb.FromJson(root.ToJsonString());
        var w = SimWorld.CreateGalaxy(small, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: new WorldRules().WithLegacyWorldScale(true));
        var n = w.Network;
        Go(w, p.X, p.Y, p.Z);
        Assert.InRange(n.FieldLoadedRocks, 1, 40);
        w.ValidateInvariants();

        var w2 = World();
        var n2 = w2.Network;
        Go(w2, p.X, p.Y, p.Z);
        w2.Submit(0, new Scan()); w2.Step();
        int rock = Enumerable.Range(0, n2.DepositCount).Where(i => n2.DepositIsFieldRock(i) && n2.IsDepositRevealed(i) && n2.DepositClusterId(i) == p.Id).OrderBy(i => n2.DepositInitial(i)).First();
        var q = n2.DepositPosition(rock);
        var laser = n2.MiningLasers(0)[0];
        var ship = n2.GetStructure(n2.ShipStructure(0));
        n2.SetStructureOrigin(ship.Id, q.X * (long)Cell - (laser.X - ship.OriginX), q.Y * (long)Cell - (laser.Y - ship.OriginY) + (long)(n2.DepositRadiusUnits(rock) * 1.6) + 2 * Cell, q.Z * (long)Cell - (laser.Z - ship.OriginZ));
        w2.Submit(0, new StartMining(rock)); w2.Step();
        Assert.Equal(rock, n2.MiningDeposit(0));
        Go(w2, p.X + 9000, p.Y, p.Z);
        Assert.Contains(Enumerable.Range(0, n2.DepositCount), i => n2.DepositIsFieldRock(i) && n2.DepositPosition(i) == q && n2.DepositInitial(i) > 0); // pinned by the mining order
        w2.ValidateInvariants();
    }

    [Fact]
    public void The_laser_reaches_the_surface_of_a_big_rock_in_the_clustered_field_only()
    {
        var p = RichCluster();
        foreach (bool legacy in new[] { false, true })
        {
            var w = World(new WorldRules().WithLegacyRockClusters(legacy));
            var n = w.Network;
            Go(w, p.X, p.Y, p.Z);
            w.Submit(0, new Scan()); w.Step();
            if (legacy) { Assert.False(n.HasRockClusters); continue; }
            int big = Enumerable.Range(0, n.DepositCount).Where(i => n.DepositIsFieldRock(i) && n.IsDepositRevealed(i)).OrderByDescending(i => n.DepositRadiusUnits(i)).First();
            long hit = (long)n.DepositRadiusUnits(big) * 150 / 100;
            Assert.True(hit > 8L * Cell, "the biggest rock of a rich cluster is wider than the laser's reach");
            var q = n.DepositPosition(big);
            var laser = n.MiningLasers(0)[0];
            var ship = n.GetStructure(n.ShipStructure(0));
            // the laser stands just outside the hit surface, on the +Y side
            n.SetStructureOrigin(ship.Id, q.X * (long)Cell - (laser.X - ship.OriginX), q.Y * (long)Cell - (laser.Y - ship.OriginY) + hit + 2 * Cell, q.Z * (long)Cell - (laser.Z - ship.OriginZ));
            Assert.True(n.LasersReaching(0, big).Count > 0, "a laser at the surface of a giant reaches it");
            n.SetStructureOrigin(ship.Id, q.X * (long)Cell - (laser.X - ship.OriginX), q.Y * (long)Cell - (laser.Y - ship.OriginY) + hit + 40 * Cell, q.Z * (long)Cell - (laser.Z - ship.OriginZ));
            Assert.Empty(n.LasersReaching(0, big));
        }
    }

    // ---- compatibility -------------------------------------------------------------------------------------------

    [Fact]
    public void The_legacy_flag_roundtrips_hashes_as_default_rules_and_no_preset_has_it()
    {
        var r = new WorldRules().WithLegacyRockClusters(true);
        Assert.True(r.LegacyRockClusters);
        Assert.Equal(r, WorldRules.Deserialize(r.Serialize()));
        Assert.True(r.HashesAsDefault && r.AnyLegacy);
        Assert.True(r.WithoutLegacyFlags().IsDefault);
        Assert.True(r.WithLegacyInterior(true).LegacyRockClusters && r.WithLegacyInterior(true).LegacyInterior);
        Assert.True(r.WithLegacyField(true).LegacyRockClusters);
        Assert.True(r.Clamped().LegacyRockClusters && r.WithResourceDensity(120).LegacyRockClusters);
        Assert.False(r.WithLegacyRockClusters(false).LegacyRockClusters);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyRockClusters));
        Assert.NotEqual(r, WorldRules.Default);
        var both = new WorldRules().WithLegacyInterior(true).WithLegacyRockClusters(true);
        Assert.Equal(both, WorldRules.Deserialize(both.Serialize()));
    }

    [Fact]
    public void Logs_of_versions_21_to_25_read_with_the_flag_on_and_replay_byte_for_byte_with_the_uniform_field()
    {
        var recorded = World(new WorldRules().WithLegacyRockClusters(true).WithLegacyDock(true).WithLegacyBlueprint(true).WithLegacyInterior(true));
        recorded.Submit(0, new SetBoost(true));
        recorded.Submit(0, new SetMovement(1, 0, 1));
        for (int i = 0; i < 8; i++) recorded.Step(300);
        Assert.True(recorded.Network.FieldLoadedRocks > 0);
        Assert.False(recorded.Network.HasRockClusters);
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true, rules: recorded.Network.Rules);
        var bytes = ms.ToArray();
        Assert.Equal(30, bytes[4]);
        for (byte version = 21; version <= 25; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            var log = CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyRockClusters, $"version {version}");
            Assert.False(rules.LegacyField); // the field itself exists from version 21
            var replay = SimWorld.Replay(log, recorded.Tick, initial: World(rules));
            Assert.Equal(recorded.Checksum(), replay.Checksum());
            Assert.False(replay.Network.HasRockClusters);
        }
        // a log written now for a clustered world is not flagged, and one of version 20 or less is a legacy field world too
        var fresh = new MemoryStream();
        CommandCodec.WriteLog(fresh, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, structures: true);
        CommandCodec.ReadLog(new MemoryStream(fresh.ToArray()), out _, out _, out _, out _, out _, out _, out var current);
        Assert.False(current.LegacyRockClusters);
        var older = fresh.ToArray(); older[4] = 25;
        CommandCodec.ReadLog(new MemoryStream(older), out _, out _, out _, out _, out _, out _, out var r25);
        Assert.True(r25.LegacyRockClusters && !r25.LegacyField && r25.HashesAsDefault);
        older[4] = 20;
        CommandCodec.ReadLog(new MemoryStream(older), out _, out _, out _, out _, out _, out _, out var r20);
        Assert.True(r20.LegacyRockClusters && r20.LegacyField);
    }

    [Fact]
    public void A_clustered_flight_replays_and_clones_to_the_same_checksum_and_differs_from_the_legacy_field()
    {
        var p = RichCluster(4);
        SimWorld Fly()
        {
            var w = World();
            Go(w, p.X - 600, p.Y, p.Z);
            w.Submit(0, new SetBoost(true));
            w.Submit(0, new SetMovement(1, 0, 0));
            for (int i = 0; i < 8; i++) w.Step(240);
            w.Submit(0, new Scan()); w.Step(2);
            return w;
        }
        var a = Fly();
        var b = Fly();
        a.ValidateInvariants();
        Assert.True(a.Network.FieldLoadedRocks > 0 && a.Network.HasRockClusters);
        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.Equal(a.Network.Checksum(), a.Network.Clone().Checksum());
        var legacy = World(new WorldRules().WithLegacyRockClusters(true));
        Go(legacy, p.X - 600, p.Y, p.Z);
        var plain = World();
        Go(plain, p.X, p.Y, p.Z);
        Go(legacy, p.X, p.Y, p.Z);
        Assert.NotEqual(plain.Checksum(), legacy.Checksum());
    }

    [Fact]
    public void Cluster_content_is_validated_and_part_of_the_content_hash_only_when_present()
    {
        ContentDb Variant(Action<JsonObject> edit)
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
            edit(root["world"]!["field"]!["clusters"]!.AsObject());
            return ContentDb.FromJson(root.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(() => Variant(c => c["margin_cells"] = 4));            // must hold the biggest regular rock
        Assert.Throws<InvalidDataException>(() => Variant(c => c["giant_margin_cells"] = 10));     // and the biggest giant
        Assert.Throws<InvalidDataException>(() => Variant(c => c["bogus"] = 1));                   // unknown key
        Assert.Throws<InvalidDataException>(() => Variant(c => c["richness_weights"] = JsonNode.Parse("[1,2,3]")));
        Assert.Throws<InvalidDataException>(() => Variant(c => c["super_cells"] = 300));           // at least two sectors
        Assert.Throws<InvalidDataException>(() => Variant(c => c["medium_permille"] = 990));       // the shares must fit 1000
        Assert.NotEqual(C.Hash, Variant(c => c["radius_percent"] = 200).Hash);
        var noClusters = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        noClusters["world"]!["field"]!.AsObject().Remove("clusters");
        var content = ContentDb.FromJson(noClusters.ToJsonString());
        Assert.Null(content.World!.Field!.Clusters);
        var w = SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        Assert.False(w.Network.HasRockClusters);
        Assert.True(w.Network.HasRockField);
    }

    // ---- budgets (loose: they catch an order of magnitude, not noise) ----------------------------------------

    [Fact]
    public void Generating_sectors_and_flying_through_a_cluster_stay_within_a_loose_budget()
    {
        var p = RichCluster();
        var list = new List<FieldRock>();
        var sw = Stopwatch.StartNew();
        for (int x = p.X / 200 - 10; x < p.X / 200 + 10; x++) for (int y = -3; y < 4; y++) for (int z = p.Z / 200 - 10; z < p.Z / 200 + 10; z++) { list.Clear(); ClusterField.Generate(F, K, G.Systems, Seed, x, y, z, 140, list); }
        sw.Stop();
        Assert.True(sw.Elapsed.TotalMilliseconds / 2800 < 1.0, $"{sw.Elapsed.TotalMilliseconds / 2800:F3} ms per sector"); // about 0.02 ms in practice, a dense sector 0.3

        var w = World();
        Go(w, p.X - 700, p.Y, p.Z);
        w.Submit(0, new SetBoost(true));
        w.Submit(0, new SetMovement(1, 0, 0));
        w.Step(60);
        sw.Restart();
        w.Step(3000);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 6000, $"3000 ticks through a cluster took {sw.ElapsedMilliseconds} ms");
        Assert.True(w.Network.FieldLoadedRocks <= F.MaxLoaded);
    }
}
