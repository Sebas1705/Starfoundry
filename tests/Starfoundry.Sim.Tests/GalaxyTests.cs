using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class GalaxyTests
{
    private static readonly ContentDb C = TestContent.Core;

    [Fact]
    public void TheRandomGeneratorGivesTheSameSequenceEverywhere()
    {
        // Reference values of SplitMix64 seeded with 0: any other platform or refactor must match them.
        var rng = new SplitMix64(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, rng.Next());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.Next());
        Assert.Equal(0x06C45D188009454FUL, rng.Next());
    }

    [Fact]
    public void RangesStayInsideTheirBoundsAndCoverThem()
    {
        var rng = new SplitMix64(42);
        var seen = new HashSet<int>();
        for (int i = 0; i < 2000; i++)
        {
            int v = rng.Next(-3, 4);
            Assert.InRange(v, -3, 4);
            seen.Add(v);
        }
        Assert.Equal(8, seen.Count);
        Assert.Equal(7, rng.Next(7, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(5, 4));
        var big = new SplitMix64(1);
        Assert.InRange(big.Next(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue);
    }

    [Fact]
    public void ASeedAlwaysGivesTheSameGalaxy()
    {
        var a = Galaxy.Generate(C, 12345);
        var b = Galaxy.Generate(C, 12345);

        Assert.Equal(a.Systems.Count, b.Systems.Count);
        for (int i = 0; i < a.Systems.Count; i++)
        {
            Assert.Equal((a.Systems[i].Name, a.Systems[i].X, a.Systems[i].Y, a.Systems[i].Z, a.Systems[i].Archetype),
                (b.Systems[i].Name, b.Systems[i].X, b.Systems[i].Y, b.Systems[i].Z, b.Systems[i].Archetype));
            Assert.Equal(a.Systems[i].Lanes, b.Systems[i].Lanes);
        }
        Assert.Equal(a.Deposits, b.Deposits);

        // Another seed gives another galaxy.
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var other = Galaxy.Generate(C, 12345 + seed);
            Assert.False(other.Deposits.SequenceEqual(a.Deposits), $"seed {12345 + seed} repeated the galaxy");
        }
    }

    [Fact]
    public void HomeSitsAtTheOriginWithTheStarterField()
    {
        var g = Galaxy.Generate(C, 7);
        var home = g.Systems[0];
        Assert.Equal("Home", home.Name);
        Assert.Equal((0, 0, 0), (home.X, home.Y, home.Z));
        Assert.True(C.Archetypes[home.Archetype].IsHome);

        var ds = g.DepositsOf(0).Select(i => g.Deposits[i]).ToList();
        Assert.Equal(2, ds.Count);
        Assert.Equal(C.ItemIndex("core:iron_ore"), ds[0].Item);
        Assert.Equal(4000, ds[0].Amount);
        Assert.Equal(C.ItemIndex("core:carbon"), ds[1].Item);
        Assert.Equal(1500, ds[1].Amount);
        foreach (var d in ds)
        {
            double horizontal = Math.Sqrt((double)d.X * d.X + (double)d.Z * d.Z);
            Assert.InRange(horizontal, 5, 13); // within the home archetype's distance, so a drill at the origin can reach it
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(99UL)]
    [InlineData(20261120UL)]
    public void SystemsAreSpreadInsideTheWorld(ulong seed)
    {
        var g = Galaxy.Generate(C, seed);
        Assert.Equal(C.Galaxy!.SystemCount, g.Systems.Count);
        foreach (var s in g.Systems)
        {
            Assert.InRange(Math.Abs(s.X), 0, SimWorld.MaxCoord - 500);
            Assert.InRange(Math.Abs(s.Z), 0, SimWorld.MaxCoord - 500);
            Assert.InRange(Math.Abs(s.Y), 0, 100);
        }
        foreach (var d in g.Deposits)
        {
            Assert.InRange(Math.Abs(d.X), 0, SimWorld.MaxCoord);
            Assert.InRange(Math.Abs(d.Y), 0, SimWorld.MaxCoord);
            Assert.InRange(Math.Abs(d.Z), 0, SimWorld.MaxCoord);
        }

        for (int a = 0; a < g.Systems.Count; a++)
            for (int b = a + 1; b < g.Systems.Count; b++)
            {
                double dx = g.Systems[a].X - g.Systems[b].X, dz = g.Systems[a].Z - g.Systems[b].Z;
                Assert.True(Math.Sqrt(dx * dx + dz * dz) >= C.Galaxy.SpacingCells / 2.0 - 1, $"systems {a} and {b} are too close");
            }
    }

    [Fact]
    public void LanesAlwaysFormAConnectedSymmetricGraphForManySeeds()
    {
        int minDegree = int.MaxValue, maxDegree = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            var g = Galaxy.Generate(C, seed);
            int n = g.Systems.Count;
            foreach (var s in g.Systems)
            {
                Assert.DoesNotContain(s.Id, s.Lanes);
                Assert.Equal(s.Lanes.Distinct().Count(), s.Lanes.Length);
                Assert.Equal(s.Lanes.OrderBy(x => x), s.Lanes);
                foreach (int other in s.Lanes) Assert.True(g.HasLane(other, s.Id), $"seed {seed}: lane {s.Id}-{other} is one-way");
                Assert.True(s.Lanes.Length >= C.Galaxy!.LanesPerSystem, $"seed {seed}: system {s.Id} has {s.Lanes.Length} lanes");
                minDegree = Math.Min(minDegree, s.Lanes.Length);
                maxDegree = Math.Max(maxDegree, s.Lanes.Length);
            }

            var reached = new HashSet<int> { 0 };
            var queue = new Queue<int>(new[] { 0 });
            while (queue.Count > 0)
                foreach (int next in g.Systems[queue.Dequeue()].Lanes)
                    if (reached.Add(next)) queue.Enqueue(next);
            Assert.Equal(n, reached.Count);
        }
        Assert.True(minDegree >= 2);
        Assert.True(maxDegree <= 11);
    }

    [Fact]
    public void DepositsFollowTheirArchetypeAndStayNearTheirSystem()
    {
        for (ulong seed = 0; seed < 60; seed++)
        {
            var g = Galaxy.Generate(C, seed);
            foreach (var system in g.Systems)
            {
                var arch = C.Archetypes[system.Archetype];
                var mine = g.Deposits.Where(d => d.System == system.Id).ToList();
                foreach (var spec in arch.Deposits)
                {
                    int count = mine.Count(d => d.Item == spec.Item);
                    // Items may appear in more than one spec of an archetype; the counts then add up.
                    int min = arch.Deposits.Where(s => s.Item == spec.Item).Sum(s => s.CountMin);
                    int max = arch.Deposits.Where(s => s.Item == spec.Item).Sum(s => s.CountMax);
                    Assert.InRange(count, min, max);
                }
                foreach (var d in mine)
                {
                    var spec = arch.Deposits.First(s => s.Item == d.Item);
                    Assert.InRange(d.Amount, arch.Deposits.Where(s => s.Item == d.Item).Min(s => s.AmountMin),
                        arch.Deposits.Where(s => s.Item == d.Item).Max(s => s.AmountMax));
                    double dx = d.X - system.X, dz = d.Z - system.Z;
                    double horizontal = Math.Sqrt(dx * dx + dz * dz);
                    Assert.InRange(horizontal, arch.DistanceMin - 1.5, arch.DistanceMax + 1.5);
                    Assert.True(horizontal < C.Galaxy!.SystemRadiusCells, "deposits stay inside the system radius");
                    Assert.NotNull(spec);
                }
            }
        }
    }

    [Fact]
    public void ArchetypesAreChosenByWeight()
    {
        var counts = new Dictionary<string, int>();
        int total = 0;
        for (ulong seed = 0; seed < 400; seed++)
        {
            var g = Galaxy.Generate(C, seed);
            foreach (var s in g.Systems.Skip(1))
            {
                string id = C.Archetypes[s.Archetype].Id;
                counts[id] = counts.GetValueOrDefault(id) + 1;
                total++;
            }
        }
        Assert.False(counts.ContainsKey("core:home"), "home is never picked for another system");
        Assert.InRange(counts["core:dense_belt"] / (double)total, 3.0 / 7 - 0.05, 3.0 / 7 + 0.05);
        Assert.InRange(counts["core:sparse_belt"] / (double)total, 2.0 / 7 - 0.05, 2.0 / 7 + 0.05);
        Assert.InRange(counts["core:carbon_rich"] / (double)total, 2.0 / 7 - 0.05, 2.0 / 7 + 0.05);
    }

    [Fact]
    public void SystemAtFindsTheSystemAroundAPosition()
    {
        var g = Galaxy.Generate(C, 5);
        foreach (var s in g.Systems)
        {
            Assert.Equal(s.Id, g.SystemAt(s.X * (long)Units.CellUnits, s.Y * (long)Units.CellUnits, s.Z * (long)Units.CellUnits));
            Assert.Equal(s.Id, g.SystemAt((s.X + 100) * (long)Units.CellUnits, s.Y * (long)Units.CellUnits, s.Z * (long)Units.CellUnits));
            Assert.Equal(-1, g.SystemAt((s.X + 400) * (long)Units.CellUnits + 1, (s.Y + 5000) * (long)Units.CellUnits, s.Z * (long)Units.CellUnits));
        }
        Assert.Equal(-1, g.SystemAt(0, 100_000L * Units.CellUnits, 0));
    }

    [Fact]
    public void ContentWithoutAGalaxyCannotGenerateOne()
    {
        var plain = ContentDb.FromJson("{\"schema\":1,\"items\":[{\"id\":\"t:a\"}],\"machines\":[],\"recipes\":[]}");
        Assert.Throws<InvalidOperationException>(() => Galaxy.Generate(plain, 1));
    }
}
