using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0028: the wide galaxy (star types, belts, clusters, nebulae, wrecks, mines, drifting debris, richer far veins), its legacy path
/// and the guarantees that keep the campaign winnable.</summary>
public class WorldGenerationTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly WorldGenRules W = C.World!;
    private const int Cell = Units.CellUnits;

    private static IEnumerable<ulong> Seeds(int n = 60) => Enumerable.Range(0, n).Select(i => (ulong)(1000 + i * 37));

    private static ContentDb WithoutWorld()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root.Remove("world");
        return ContentDb.FromJson(root.ToJsonString());
    }

    // ---- determinism and the legacy path ------------------------------------------------------------

    [Fact]
    public void A_seed_always_gives_the_same_wide_galaxy()
    {
        var a = Galaxy.Generate(C, 4242);
        var b = Galaxy.Generate(C, 4242);
        Assert.True(a.Wide);
        Assert.Equal(a.Deposits, b.Deposits);
        Assert.Equal(a.Obstacles, b.Obstacles);
        Assert.Equal(a.Systems.Select(s => (s.Name, s.X, s.Y, s.Z, s.Archetype, s.Star)), b.Systems.Select(s => (s.Name, s.X, s.Y, s.Z, s.Archetype, s.Star)));
        Assert.False(Galaxy.Generate(C, 4243).Obstacles.SequenceEqual(a.Obstacles));
    }

    [Fact]
    public void The_legacy_flag_and_content_without_a_world_section_generate_the_old_galaxy()
    {
        var legacy = Galaxy.Generate(C, 4242, legacy: true);
        var stripped = Galaxy.Generate(WithoutWorld(), 4242);
        Assert.False(legacy.Wide);
        Assert.Empty(legacy.Obstacles);
        Assert.Equal(legacy.Deposits, stripped.Deposits);
        Assert.Equal(legacy.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)), stripped.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)));
        Assert.Equal(C.Galaxy!.SpacingCells, legacy.Rules.SpacingCells);
        Assert.Equal(W.SpacingCells, Galaxy.Generate(C, 4242).Rules.SpacingCells);
    }

    [Fact]
    public void Worlds_without_structures_and_worlds_with_the_legacy_flag_keep_the_old_galaxy()
    {
        var plain = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 4242, threats: false, stationRules: false, structures: false);
        Assert.False(plain.Network.Galaxy!.Wide);
        var flagged = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 4242, threats: false, stationRules: false, structures: true, rules: new WorldRules().WithLegacyGalaxy(true));
        Assert.False(flagged.Network.Galaxy!.Wide);
        Assert.Equal(0, flagged.Network.ObstacleCount);
        var wide = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 4242, threats: false, stationRules: false, structures: true);
        Assert.True(wide.Network.Galaxy!.Wide);
        Assert.True(wide.Network.ObstacleCount > 100);
        wide.Network.ValidateInvariants();
    }

    [Fact]
    public void The_legacy_flag_alone_hashes_as_default_rules()
    {
        var a = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 4242, threats: false, stationRules: false, structures: false);
        var b = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 4242, threats: false, stationRules: false, structures: false, rules: new WorldRules().WithLegacyGalaxy(true));
        a.Step(30); b.Step(30);
        Assert.Equal(a.Checksum(), b.Checksum());
    }

    [Fact]
    public void The_world_section_is_part_of_the_content_hash_and_is_validated()
    {
        Assert.NotEqual(WithoutWorld().Hash, C.Hash);
        string json = File.ReadAllText(TestContent.LegacyPath);
        void Bad(string from, string to) => Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(json.Replace(from, to)));
        Bad("\"home_star\": \"core:yellow_dwarf\"", "\"home_star\": \"core:nope\"");
        Bad("\"safe_speed_units\": 10", "\"safe_speed_units\": 60");
        Bad("\"damage\": 6 }", "\"damage\": 0 }");
        Bad("\"item\": \"core:ammo\", \"count\": [8, 20]", "\"item\": \"core:nothing\", \"count\": [8, 20]");
        Bad("\"system_radius_cells\": 320", "\"system_radius_cells\": 900");
    }

    // ---- the shape of the wide galaxy -----------------------------------------------------------------

    [Fact]
    public void Systems_are_farther_apart_and_wider_and_deposits_lie_farther_from_home()
    {
        foreach (ulong seed in Seeds(20))
        {
            var wide = Galaxy.Generate(C, seed);
            var old = Galaxy.Generate(C, seed, legacy: true);
            Assert.True(wide.Rules.SystemRadiusCells > old.Rules.SystemRadiusCells);
            foreach (var d in wide.DepositsOf(0).Select(i => wide.Deposits[i]))
            {
                double horizontal = Math.Sqrt((double)d.X * d.X + (double)d.Z * d.Z);
                Assert.InRange(horizontal, 34, 62); // the home archetype's wide range, 36 to 60 cells
            }
            // Every far system is reached by lanes and its deposits stay inside the wide radius.
            foreach (var s in wide.Systems)
                foreach (var d in wide.DepositsOf(s.Id).Select(i => wide.Deposits[i]))
                {
                    double dx = d.X - s.X, dz = d.Z - s.Z;
                    Assert.True(Math.Sqrt(dx * dx + dz * dz) < wide.Rules.SystemRadiusCells);
                }
            Assert.All(wide.Systems, s => Assert.InRange(Math.Abs(s.X), 0, 9000));
            Assert.All(wide.Systems, s => Assert.InRange(Math.Abs(s.Z), 0, 9000));
        }
    }

    [Fact]
    public void Home_always_holds_the_iron_ice_and_carbon_the_campaign_needs_and_is_connected_to_every_system()
    {
        int iron = C.ItemIndex("core:iron_ore"), carbon = C.ItemIndex("core:carbon"), ice = C.ItemIndex("core:ice");
        foreach (ulong seed in Seeds(200))
        {
            var g = Galaxy.Generate(C, seed);
            var home = g.DepositsOf(0).Select(i => g.Deposits[i]).ToList();
            Assert.Equal(12000, home.Single(d => d.Item == iron).Amount);
            Assert.Equal(1500, home.Single(d => d.Item == carbon).Amount);
            Assert.InRange(home.Single(d => d.Item == ice).Amount, 1500, 2000);
            Assert.Equal(W.HomeStar, g.Systems[0].Star);

            // Reachable: every system is in one component and the jump goals (a second system, titanium, nickel, quartz) have targets.
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>(new[] { 0 });
            while (queue.Count > 0)
                foreach (int l in g.Systems[queue.Dequeue()].Lanes)
                    if (seen.Add(l)) queue.Enqueue(l);
            Assert.Equal(g.Systems.Count, seen.Count);
            foreach (string id in new[] { "core:titanium_ore", "core:nickel_ore", "core:quartz", "core:carbon" })
                Assert.Contains(g.Deposits, d => d.Item == C.ItemIndex(id) && d.System != 0);
            Assert.True(g.Systems[0].Lanes.Length >= 1);
        }
    }

    [Fact]
    public void Star_types_scale_the_veins_and_respect_their_minimum_depth()
    {
        var seenStars = new HashSet<int>();
        foreach (ulong seed in Seeds(120))
        {
            var g = Galaxy.Generate(C, seed);
            var depth = new Dictionary<int, int> { [0] = 0 };
            var queue = new Queue<int>(new[] { 0 });
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                foreach (int v in g.Systems[u].Lanes) if (depth.TryAdd(v, depth[u] + 1)) queue.Enqueue(v);
            }
            foreach (var s in g.Systems)
            {
                seenStars.Add(s.Star);
                Assert.True(W.StarTypes[s.Star].MinDepth <= depth[s.Id], $"star {W.StarTypes[s.Star].Id} at depth {depth[s.Id]}");
            }
        }
        Assert.Equal(W.StarTypes.Length, seenStars.Count); // every type shows up somewhere
    }

    [Fact]
    public void Far_veins_are_richer_than_near_ones()
    {
        // The same archetype ranges are scaled by the star, the jumps from home and the distance from the anchor.
        var near = new List<double>(); var far = new List<double>();
        foreach (ulong seed in Seeds(150))
        {
            var g = Galaxy.Generate(C, seed);
            foreach (var s in g.Systems.Skip(1))
            {
                var arch = C.Archetypes[s.Archetype];
                int lo = arch.HasWide ? arch.WideDistanceMin : arch.DistanceMin * 4, hi = arch.HasWide ? arch.WideDistanceMax : arch.DistanceMax * 4;
                foreach (var d in g.DepositsOf(s.Id).Select(i => g.Deposits[i]))
                {
                    double dist = Math.Sqrt(Math.Pow(d.X - s.X, 2) + Math.Pow(d.Z - s.Z, 2));
                    var spec = arch.Deposits.First(x => x.Item == d.Item);
                    double rel = d.Amount / ((spec.AmountMin + spec.AmountMax) / 2.0);
                    (dist < lo + (hi - lo) / 3.0 ? near : far).Add(rel);
                }
            }
        }
        Assert.True(far.Average() > near.Average() * 1.1, $"far {far.Average():F2} near {near.Average():F2}");
    }

    // ---- obstacles ------------------------------------------------------------------------------------

    [Fact]
    public void Every_system_has_obstacles_and_all_five_kinds_of_behaviour_appear()
    {
        var kinds = new HashSet<ObstacleBehaviour>();
        foreach (ulong seed in Seeds(40))
        {
            var g = Galaxy.Generate(C, seed);
            foreach (var s in g.Systems) Assert.True(g.ObstaclesOf(s.Id).Count >= 15, $"system {s.Id} of seed {seed} has {g.ObstaclesOf(s.Id).Count} obstacles");
            foreach (var o in g.Obstacles) kinds.Add(W.Kinds[o.Kind].Behaviour);
        }
        Assert.Equal(5, kinds.Count);
    }

    [Fact]
    public void Home_has_no_mines_and_no_drifting_debris_and_clouds_keep_clear_of_its_deposits()
    {
        foreach (ulong seed in Seeds(80))
        {
            var g = Galaxy.Generate(C, seed);
            foreach (int i in g.ObstaclesOf(0))
            {
                var o = g.Obstacles[i];
                var b = W.Kinds[o.Kind].Behaviour;
                Assert.True(b is ObstacleBehaviour.Solid or ObstacleBehaviour.Cloud or ObstacleBehaviour.Wreck, $"home has a {b}");
            }
        }
    }

    [Fact]
    public void Solid_rocks_and_hazards_keep_clear_of_anchors_and_deposits_and_rocks_leave_gaps()
    {
        foreach (ulong seed in Seeds(60))
        {
            var g = Galaxy.Generate(C, seed);
            foreach (var s in g.Systems)
            {
                var keep = new List<(double X, double Y, double Z, int Clear)> { ((double)s.X * Cell, (double)s.Y * Cell, (double)s.Z * Cell, W.AnchorClearCells) };
                foreach (var d in g.DepositsOf(s.Id).Select(i => g.Deposits[i])) keep.Add((d.X * (double)Cell + Cell / 2, d.Y * (double)Cell + Cell / 2, d.Z * (double)Cell + Cell / 2, W.DepositClearCells));
                var solids = new List<GeneratedObstacle>();
                foreach (int i in g.ObstaclesOf(s.Id))
                {
                    var o = g.Obstacles[i];
                    var b = W.Kinds[o.Kind].Behaviour;
                    if (b is ObstacleBehaviour.Wreck) continue;
                    if (b == ObstacleBehaviour.Drifter) continue; // its whole path is checked at generation; the stored centre is the middle of the line
                    foreach (var (kx, ky, kz, clear) in keep)
                    {
                        double dist = Math.Sqrt(Math.Pow(o.X - kx, 2) + Math.Pow(o.Y - ky, 2) + Math.Pow(o.Z - kz, 2));
                        bool anchor = clear == W.AnchorClearCells && kx == (double)s.X * Cell && kz == (double)s.Z * Cell;
                        double need = b switch
                        {
                            ObstacleBehaviour.Solid => o.Radius + clear * (double)Cell,
                            ObstacleBehaviour.Mine => o.Radius + W.HazardClearCells * (double)Cell,
                            _ => anchor ? o.Radius + clear * (double)Cell : s.Id == 0 ? o.Radius + (clear + 40) * (double)Cell : 0, // clouds may cover deposits away from home
                        };
                        Assert.True(dist >= need - 1, $"{W.Kinds[o.Kind].Id} of system {s.Id} is {dist / Cell:F1} cells from a keep-clear point (needs {need / Cell:F1})");
                    }
                    if (b == ObstacleBehaviour.Solid) solids.Add(o);
                }
                for (int a = 0; a < solids.Count; a++)
                    for (int b2 = a + 1; b2 < solids.Count; b2++)
                    {
                        double dist = Math.Sqrt(Math.Pow(solids[a].X - solids[b2].X, 2) + Math.Pow(solids[a].Y - solids[b2].Y, 2) + Math.Pow(solids[a].Z - solids[b2].Z, 2));
                        Assert.True(dist - solids[a].Radius - solids[b2].Radius >= 10 * Cell - 1, "two rocks leave less than a ship's width between them");
                    }
            }
        }
    }

    [Fact]
    public void Wrecks_hold_loot_from_their_table_and_drifters_stay_inside_their_system()
    {
        foreach (ulong seed in Seeds(40))
        {
            var g = Galaxy.Generate(C, seed);
            foreach (var o in g.Obstacles)
            {
                var def = W.Kinds[o.Kind];
                if (def.Behaviour == ObstacleBehaviour.Wreck)
                {
                    var spec = def.Loot.Single(l => l.Item == o.LootItem);
                    Assert.InRange(o.LootCount, spec.CountMin, spec.CountMax);
                }
                else Assert.Equal(-1, o.LootItem);
                if (def.Behaviour == ObstacleBehaviour.Drifter)
                {
                    Assert.True(o.Amplitude > 0);
                    var s = g.Systems[o.System];
                    long reach = o.Axis switch { 0 => Math.Abs(o.X - s.X * (long)Cell), 1 => Math.Abs(o.Y - s.Y * (long)Cell), _ => Math.Abs(o.Z - s.Z * (long)Cell) } + o.Amplitude;
                    Assert.True(reach < (long)g.Rules.SystemRadiusCells * Cell);
                }
            }
        }
    }

    [Fact]
    public void A_drifter_moves_in_a_triangle_wave_along_its_axis_and_everything_else_stands_still()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 4242, threats: false, stationRules: false, structures: true);
        var n = w.Network;
        int drifter = Enumerable.Range(0, n.ObstacleCount).First(i => n.GetObstacle(i).Behaviour == ObstacleBehaviour.Drifter);
        var g = n.Galaxy!.Obstacles[drifter];
        var speed = W.Kinds[g.Kind].DriftUnitsPerTick;
        long period = 4L * g.Amplitude;
        var seen = new List<long>();
        for (long t = 0; t < period / speed + 2; t += Math.Max(1, period / speed / 40))
        {
            var (x, y, z) = n.ObstacleCentreAt(drifter, t);
            long off = g.Axis == 0 ? x - g.X : g.Axis == 1 ? y - g.Y : z - g.Z;
            Assert.InRange(off, -g.Amplitude, g.Amplitude);
            if (g.Axis != 0) Assert.Equal(g.X, x);
            if (g.Axis != 1) Assert.Equal(g.Y, y);
            if (g.Axis != 2) Assert.Equal(g.Z, z);
            seen.Add(off);
        }
        Assert.True(seen.Max() - seen.Min() > g.Amplitude, "it should sweep most of its line");
        // After a whole period it is back where it started; a step is at most the drift speed.
        Assert.Equal(n.ObstacleCentreAt(drifter, 7), n.ObstacleCentreAt(drifter, 7 + period)); // speed * period is a whole number of periods
        var p0 = n.ObstacleCentreAt(drifter, 100); var p1 = n.ObstacleCentreAt(drifter, 101);
        Assert.True(Math.Abs(p1.X - p0.X) + Math.Abs(p1.Y - p0.Y) + Math.Abs(p1.Z - p0.Z) <= speed);
        int rock = Enumerable.Range(0, n.ObstacleCount).First(i => n.GetObstacle(i).Behaviour == ObstacleBehaviour.Solid);
        Assert.Equal(n.ObstacleCentreAt(rock, 0), n.ObstacleCentreAt(rock, 99999));
    }
}
