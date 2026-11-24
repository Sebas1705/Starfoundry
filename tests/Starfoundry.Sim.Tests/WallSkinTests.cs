using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The wall skins with depth (ribbed, stepped, niched): every skin occurs, its furniture is solid for the body on closed walls only, and the joints between skins stay closed.</summary>
public class WallSkinTests
{
    private static readonly int[] DeepSkins = { HullProfile.SkinRibbed, HullProfile.SkinStepped, HullProfile.SkinNiched };

    /// <summary>A place where a hull module has the given skin.</summary>
    private static (int X, int Z) Find(int skin, ModuleKind kind = ModuleKind.Hull)
    {
        for (int x = -6; x < 6; x++)
            for (int z = -6; z < 6; z++)
                if (HullProfile.SkinOf(kind, x, 0, z) == skin) return (x, z);
        throw new Xunit.Sdk.XunitException($"no place with skin {skin}");
    }

    [Fact]
    public void EverySkinOccursOnEveryCorridorKindAndOnlyThere()
    {
        foreach (var kind in new[] { ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.DroneBay })
        {
            var seen = new HashSet<int>();
            for (int x = -8; x < 8; x++)
                for (int z = -8; z < 8; z++) seen.Add(HullProfile.SkinOf(kind, x, 0, z));
            foreach (int s in DeepSkins.Concat(new[] { HullProfile.SkinPlain, HullProfile.SkinArched, HullProfile.SkinBoxy })) Assert.Contains(s, seen);
        }
        for (int x = -4; x < 4; x++)
            foreach (var kind in new[] { ModuleKind.Engine, ModuleKind.Gun, ModuleKind.Dock, ModuleKind.Defence, ModuleKind.Shield })
                Assert.Equal(HullProfile.SkinPlain, HullProfile.SkinOf(kind, x, 0, 0));
    }

    [Fact]
    public void TheSkinSurvivesTheMaskRoundTripAndTheStairBitIsSeparate()
    {
        for (int skin = 0; skin < 7; skin++)
        {
            int m = HullProfile.SkinBits(skin) | HullProfile.StairDown | HullProfile.Left | HullProfile.PodFront;
            Assert.Equal(skin, HullProfile.SkinOfMask(m));
            Assert.NotEqual(0, m & HullProfile.StairDown);
            Assert.Equal(HullProfile.SectionClassOf(skin), HullProfile.SectionOfMask(m));
        }
    }

    [Fact]
    public void WallFurnitureIsSolidOnClosedSidesOnlyAndStaysInsideTheModule()
    {
        foreach (int skin in DeepSkins)
        {
            int closed = HullProfile.SkinBits(skin);
            var all = HullProfile.SkinSolids(closed).ToList();
            Assert.NotEmpty(all);
            Assert.Contains(all, s => s.X0 == 0f);                  // on the left wall
            Assert.Contains(all, s => s.X1 == HullProfile.W);       // and the right one
            foreach (var s in all)
            {
                Assert.InRange(s.X0, 0f, HullProfile.W); Assert.InRange(s.X1, 0f, HullProfile.W);
                Assert.InRange(s.Z0, 0f, HullProfile.D); Assert.InRange(s.Z1, 0f, HullProfile.D);
                Assert.True(s.X1 - s.X0 < 1.0f && s.X1 - s.X0 >= 0.3f, "furniture reaches 0.3 to 1 cell into the module");
            }
            Assert.All(HullProfile.SkinSolids(closed | HullProfile.Left), s => Assert.True(s.X0 > 0f)); // a doorway on the left has no furniture
            Assert.All(HullProfile.SkinSolids(closed | HullProfile.Right), s => Assert.True(s.X1 < HullProfile.W));
            Assert.Empty(HullProfile.SkinSolids(closed | HullProfile.Left | HullProfile.Right));
        }
    }

    [Fact]
    public void FurnitureKeepsClearOfTheStairwellTheWorkbenchAndTheMiddleAisle()
    {
        foreach (int skin in DeepSkins)
            foreach (var s in HullProfile.SkinSolids(HullProfile.SkinBits(skin)))
            {
                bool left = s.X0 == 0f;
                float inner = left ? s.X1 : HullProfile.W - s.X0;
                Assert.True(inner <= 0.7f, $"skin {skin}: furniture reaches {inner} cells in");
                // the stairwell and its approach (a body width round the shaft) are open
                Assert.False(s.X1 > Stairwell.X0 - 0.6f && s.X0 < Stairwell.X1 + 0.6f && s.Z1 > Stairwell.Z0 - 0.6f && s.Z0 < Stairwell.Z1 + 0.6f);
                // the workbench against a wall stands clear (WorkbenchSpot: from 1.15 cells off the wall)
                Assert.True(inner < WorkbenchSpot.MinX);
            }
    }

    [Fact]
    public void TheBodyNeverEntersTheFurnitureOfAnySkin()
    {
        foreach (int skin in DeepSkins)
        {
            var (px, pz) = Find(skin);
            var mods = new Dictionary<(int, int, int), ModuleKind> { [(px, 0, pz)] = ModuleKind.Hull, [(px, 0, pz + 1)] = ModuleKind.Hull };
            ModuleKind? KindAt(int x, int y, int z) => mods.TryGetValue((x, y, z), out var k) ? k : null;
            var vols = BodyMotion.Volumes(mods, k => HullProfile.MaskOf(KindAt, k.Mx, k.My, k.Mz), true);
            var b = vols.First(v => v.MinZ < (pz + 1) * HullProfile.D - 0.5f && v.MinZ >= pz * HullProfile.D - 0.5f);
            int mask = b.Shell!.Value.Mask;
            Assert.Equal(skin, HullProfile.SkinOfMask(mask));
            foreach (var s in HullProfile.SkinSolids(mask))
                for (float x = s.X0 + 0.05f; x < s.X1; x += 0.1f)
                    for (float z = s.Z0 + 0.05f; z < s.Z1; z += 0.1f)
                        Assert.False(BodyMotion.Contains(new Vector3(b.MinX + x, 0f, b.MinZ + z), vols), $"skin {skin}: the body fits in furniture at {x},{z}");
            // a hundred random walks never end inside any piece
            var rng = new Random(skin);
            var p = BodyMotion.Nearest(new Vector3(b.MinX + 4f, 0f, b.MinZ + 4f), vols);
            for (int i = 0; i < 2000; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2);
                p = BodyMotion.Step(p, new Vector3(MathF.Cos(a), 0, MathF.Sin(a)), 7f, 0.05f + (float)rng.NextDouble() * 0.5f, vols);
                Assert.True(BodyMotion.Contains(p, vols));
                if (p.Z >= b.MinZ && p.Z <= b.MaxZ)
                    foreach (var s in HullProfile.SkinSolids(mask))
                        Assert.False(p.X - b.MinX > s.X0 && p.X - b.MinX < s.X1 && p.Z - b.MinZ > s.Z0 && p.Z - b.MinZ < s.Z1, $"skin {skin}: centre inside a solid at {p}");
            }
        }
    }

    [Fact]
    public void JointsBetweenEverySkinPairHaveMatchingSectionsOrAreSealedByAPlate()
    {
        // A neighbour's skin is seen through its section family only: the new skins reuse boxy and arched, so the existing joint plates cover every pair.
        for (int a = 0; a < 7; a++)
            for (int b = 0; b < 7; b++)
            {
                int ma = HullProfile.SkinBits(a) | HullProfile.Rear | (HullProfile.SectionClassOf(b) << HullProfile.RearSkinShift);
                var nb = HullProfile.NeighbourMask(ma, front: false);
                Assert.NotNull(nb);
                Assert.Equal(HullProfile.SectionClassOf(b), HullProfile.SkinOfMask(nb!.Value));
                var own = HullProfile.Octagon(HullProfile.Roof, 1f, ma);
                var other = HullProfile.Octagon(HullProfile.Roof, 1f, nb.Value);
                Assert.Equal(own.Length, other.Length); // every pair yields rings the joint plate can bridge
                foreach (var v in own.Concat(other)) { Assert.InRange(v.X, 0f, HullProfile.W); Assert.InRange(v.Y, 0f, HullProfile.Roof); }
            }
    }

    [Fact]
    public void NoLockersStandOnAWallThatCarriesRibsAPlinthOrNiches()
    {
        foreach (int skin in DeepSkins)
            Assert.Empty(HullProfile.SolidsOf(Fixture.Lockers, HullProfile.SkinBits(skin)));
        Assert.NotEmpty(HullProfile.SolidsOf(Fixture.Lockers, HullProfile.SkinBits(HullProfile.SkinPlain)));
    }
}
