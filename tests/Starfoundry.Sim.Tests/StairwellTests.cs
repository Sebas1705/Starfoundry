using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Moving between decks: the stairwell rule of the masks, the climb, and the connectivity of a multi-level ship or station by flood fill.</summary>
public class StairwellTests
{
    private static BodyBox[] Layout(Dictionary<(int Mx, int My, int Mz), ModuleKind> mods, bool ship) =>
        BodyMotion.Volumes(mods, k => HullProfile.MaskOf((x, y, z) => mods.TryGetValue((x, y, z), out var kind) ? kind : null, k.Mx, k.My, k.Mz), ship);

    private static Dictionary<(int, int, int), ModuleKind> Tower(params ModuleKind[] levels)
    {
        var d = new Dictionary<(int, int, int), ModuleKind>();
        for (int i = 0; i < levels.Length; i++) d[(0, i, 0)] = levels[i];
        return d;
    }

    private static readonly ModuleKind[] ShipCapable = { ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop };

    [Fact]
    public void OnlyCapableKindsStackedGetAStairwell()
    {
        foreach (var kind in Enum.GetValues<ModuleKind>())
            foreach (var other in Enum.GetValues<ModuleKind>())
            {
                var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = other, [(0, 1, 0)] = kind };
                ModuleKind? KindAt(int x, int y, int z) => mods.TryGetValue((x, y, z), out var k) ? k : null;
                int mask = HullProfile.MaskOf(KindAt, 0, 1, 0);
                bool expected = Stairwell.Capable(kind) && Stairwell.Capable(other);
                Assert.Equal(expected, (mask & HullProfile.StairDown) != 0);
                Assert.Equal(0, HullProfile.MaskOf(KindAt, 0, 0, 0) & HullProfile.StairDown); // the lower module has no opening of its own
            }
    }

    [Fact]
    public void TheShaftAndTheLandingAreFreeOfFurnitureInEveryCapableKind()
    {
        float cx = (Stairwell.X0 + Stairwell.X1) / 2;
        foreach (var ship in new[] { true, false })
            foreach (var kind in Enum.GetValues<ModuleKind>().Where(Stairwell.Capable))
                foreach (var s in HullProfile.SolidsOf(HullProfile.FixtureOf(kind, ship), 0))
                {
                    for (float x = Stairwell.X0 - 0.1f; x <= Stairwell.X1 + 0.1f; x += 0.1f)
                        for (float z = Stairwell.Z0 - 0.1f; z <= Stairwell.Z1 + 0.1f; z += 0.1f)
                            Assert.False(s.Contains(x, z), $"{kind} furniture stands in the shaft at {x},{z}");
                    // the body stepping off the ladder (a 0.6 square round the landing spot)
                    for (float x = cx - 0.3f; x <= cx + 0.3f; x += 0.1f)
                        for (float z = Stairwell.Z1 + Stairwell.LandingGap - 0.3f; z <= Stairwell.Z1 + Stairwell.LandingGap + 0.3f; z += 0.1f)
                            Assert.False(s.Contains(x, z), $"{kind} furniture stands on the landing at {x},{z}");
                }
    }

    [Fact]
    public void ClimbingUpAndDownRoundTripsOnEveryCapableStack()
    {
        foreach (bool ship in new[] { true, false })
        {
            var kinds = Enum.GetValues<ModuleKind>().Where(k => Stairwell.Capable(k) && ModuleKindIds.AllowedOn(ship ? StructureKind.Ship : StructureKind.Station, k)).ToArray();
            foreach (var a in kinds)
                foreach (var b in kinds)
                {
                    var vols = Layout(Tower(a, b), ship);
                    var links = Stairwell.Links(vols);
                    Assert.Single(links);
                    var l = links[0];
                    var below = new Vector3(l.CentreX, l.LowerY, l.CentreZ);
                    Assert.True(BodyMotion.Contains(below, vols), $"{a} under {b}: the foot of the ladder is not free");
                    var up = Stairwell.Try(below, true, vols);
                    Assert.NotNull(up);
                    Assert.Equal(l.UpperY, up!.Value.Destination.Y);
                    Assert.True(BodyMotion.Contains(up.Value.Destination, vols));
                    var down = Stairwell.Try(up.Value.Destination, false, vols);
                    Assert.NotNull(down);
                    Assert.Equal(l.LowerY, down!.Value.Destination.Y);
                    Assert.True(BodyMotion.Contains(down.Value.Destination, vols));
                    Assert.Null(Stairwell.Try(below, false, vols)); // no way down from the bottom deck
                }
        }
    }

    [Fact]
    public void TheClimbNeedsTheLadderWithinReachAndTheLandingFree()
    {
        var vols = Layout(Tower(ModuleKind.Hull, ModuleKind.Hull), true);
        var far = new Vector3(1.0f, 0f, 6.5f);
        Assert.True(BodyMotion.Contains(far, vols));
        Assert.Null(Stairwell.Try(far, true, vols));
        var l = Stairwell.Links(vols)[0];
        var at = new Vector3(l.CentreX, 0f, l.CentreZ);
        // a machine standing on the landing refuses the climb, and a body not on a floor does not climb
        var block = new[] { new BodySolid(l.X0 - 1f, l.UpperY, l.Z0 - 1f, l.X1 + 1f, l.UpperY + 1f, l.Z1 + 1.5f) };
        Assert.Null(Stairwell.Try(at, true, vols, block));
        Assert.Null(Stairwell.Try(at + new Vector3(0, 1.3f, 0), true, vols));
    }

    private static HashSet<(int, int, int)> Flood(BodyBox[] vols, Vector3 start, BodySolid[]? solids = null)
    {
        const float G = 0.5f;
        var seen = new HashSet<(int, int, int)>();
        var q = new Queue<Vector3>();
        (int, int, int) Key(Vector3 p) => ((int)MathF.Round(p.X / G), (int)MathF.Round(p.Y), (int)MathF.Round(p.Z / G));
        seen.Add(Key(start)); q.Enqueue(start);
        var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var (dx, dz) in dirs)
            {
                var n = new Vector3(p.X + dx * G, p.Y, p.Z + dz * G);
                if (!BodyMotion.Contains(n, vols, solids) || seen.Contains(Key(n))) continue;
                var moved = BodyMotion.Step(p, new Vector3(dx, 0, dz), 1f, G, vols, solids); // the step must be walkable, not only the spot
                if (Vector3.DistanceSquared(moved, n) > 1e-3f) continue;
                seen.Add(Key(n)); q.Enqueue(n);
            }
            foreach (bool up in new[] { true, false })
                if (Stairwell.Try(p, up, vols, solids) is { } m && seen.Add(Key(m.Destination))) q.Enqueue(m.Destination);
        }
        return seen;
    }

    [Fact]
    public void EveryDeckOfAThreeLevelShipIsReachableFromTheBottom()
    {
        var mods = new Dictionary<(int, int, int), ModuleKind>
        {
            [(0, 0, 0)] = ModuleKind.Hull, [(0, 0, 1)] = ModuleKind.Cargo, [(0, 1, 0)] = ModuleKind.Workshop, [(0, 1, 1)] = ModuleKind.Hull,
            [(0, 2, 1)] = ModuleKind.Cargo, [(0, 2, 0)] = ModuleKind.Hull,
        };
        var vols = Layout(mods, true);
        var reach = Flood(vols, BodyMotion.Nearest(new Vector3(4f, 0f, 4f), vols));
        foreach (var level in new[] { 0, 1, 2 })
            Assert.Contains(reach, k => k.Item2 == level * 4);
        // both modules of the top deck are entered, not only the one above the shaft
        Assert.Contains(reach, k => k.Item2 == 8 && k.Item3 * 0.5f > 8f);
        Assert.Contains(reach, k => k.Item2 == 8 && k.Item3 * 0.5f < 8f);
    }

    [Fact]
    public void AStationCoreStackReachesItsUpperDeck()
    {
        var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Core, [(0, 1, 0)] = ModuleKind.Hull, [(0, 1, 1)] = ModuleKind.Cargo, [(0, 0, 1)] = ModuleKind.Workshop };
        var vols = Layout(mods, false);
        var reach = Flood(vols, BodyMotion.Nearest(new Vector3(2f, 0f, 2f), vols));
        Assert.True(reach.Any(k => k.Item2 == 4), "no upper deck: " + string.Join(";", reach.GroupBy(k => k.Item2).Select(g => g.Key + ":" + g.Count())));
        Assert.Contains(reach, k => k.Item2 == 4 && k.Item3 * 0.5f > 8f); // the cargo behind the upper hull
    }

    [Fact]
    public void RandomStacksKeepEveryClimbInsideTheShell()
    {
        var rng = new Random(11);
        for (int n = 0; n < 40; n++)
        {
            var mods = new Dictionary<(int, int, int), ModuleKind>();
            for (int i = 0; i < 8; i++) mods[(rng.Next(0, 2), rng.Next(0, 3), rng.Next(0, 2))] = ShipCapable[rng.Next(ShipCapable.Length)];
            var vols = Layout(mods, true);
            foreach (var l in Stairwell.Links(vols))
                foreach (bool up in new[] { true, false })
                {
                    var from = new Vector3(l.CentreX, up ? l.LowerY : l.UpperY, l.CentreZ);
                    if (!BodyMotion.Contains(from, vols)) continue;
                    if (Stairwell.Try(from, up, vols) is { } m) Assert.True(BodyMotion.Contains(m.Destination, vols));
                }
        }
    }
}
