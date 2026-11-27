using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The hull curves pass (visual pass 3): rounded shoulders over the chamfers of a ship section and a chamfered roof edge on stations. Both are drawn
/// only, so what is pinned here is that they can never reach the walkable volume and never leave the module box: the visible hull is at most as big as
/// the hit volume of the simulation and the walking body (<see cref="HullProfile.Clear"/>) cannot meet them.
/// </summary>
public class HullShouldersTests
{
    private static readonly int[] Skins = { HullProfile.SkinPlain, HullProfile.SkinWide, HullProfile.SkinArched, HullProfile.SkinBoxy, HullProfile.SkinRibbed, HullProfile.SkinStepped, HullProfile.SkinNiched, HullProfile.SkinFlared };

    /// <summary>Every section a ship module can have: each neighbour combination (left, right, up, down) of each skin, at body height and for the low pods, plain and tapered.</summary>
    private static IEnumerable<(float Roof, int Mask, float Scale, float Pivot)> Sections()
    {
        foreach (float roof in new[] { HullProfile.Roof, HullProfile.GunRoof, HullProfile.SensorRoof })
            foreach (int skin in Skins)
                for (int nb = 0; nb < 16; nb++)
                {
                    int mask = ((nb & 1) != 0 ? HullProfile.Left : 0) | ((nb & 2) != 0 ? HullProfile.Right : 0) | ((nb & 4) != 0 ? HullProfile.Up : 0) | ((nb & 8) != 0 ? HullProfile.Down : 0) | HullProfile.SkinBits(skin);
                    yield return (roof, mask, 1f, HullProfile.DefaultPivot);
                    yield return (roof, mask, HullProfile.NoseTipScale, 0f);
                    yield return (roof, mask, HullProfile.TailScale, HullProfile.DefaultPivot);
                }
    }

    [Fact]
    public void ARoundedShoulderStaysInsideTheModuleBox()
    {
        int arcs = 0;
        foreach (var (roof, mask, scale, pivot) in Sections())
        {
            var s = HullProfile.Octagon(roof, scale, mask, pivot);
            foreach (int i in new[] { 1, 3, 5, 7 })
            {
                var a = s[i]; var b = s[(i + 1) % s.Length];
                if (Vector2.Distance(a, b) < 0.12f) continue; // a squared corner has no shoulder
                arcs++;
                for (int k = 0; k <= 20; k++)
                {
                    var p = HullProfile.ShoulderPoint(a, b, i, k / 20f);
                    Assert.InRange(p.X, 0f, HullProfile.W);
                    Assert.InRange(p.Y, HullProfile.Roof - HullProfile.H, roof); // from the floor of the module (a module above another starts where the lower roof ends) to its roof: never outside the module
                }
            }
        }
        Assert.True(arcs > 1000, "the sweep must exercise many shoulders");
    }

    [Fact]
    public void ARoundedShoulderIsOutsideTheWalkableSection()
    {
        foreach (var (roof, mask, scale, pivot) in Sections())
        {
            var s = HullProfile.Octagon(roof, scale, mask, pivot);
            var liner = HullProfile.Liner(s, mask);
            foreach (int i in new[] { 1, 3, 5, 7 })
            {
                var a = s[i]; var b = s[(i + 1) % s.Length];
                if (Vector2.Distance(a, b) < 0.12f) continue;
                var mid = (a + b) / 2f;
                var chord = b - a;
                for (int k = 0; k <= 20; k++)
                {
                    var p = HullProfile.ShoulderPoint(a, b, i, k / 20f);
                    // never inside the liner polygon the body walks in
                    Assert.False(HullProfile.InsideSection(liner, mask, p.X, p.Y), $"shoulder point {p} of edge {i} is inside the walkable section (roof {roof}, mask {mask})");
                    // and on the outer side of the chamfer: the chamfer is counter-clockwise, so the inside is to the left of the chord and the arc is to its right
                    float side = chord.X * (p.Y - a.Y) - chord.Y * (p.X - a.X);
                    Assert.True(side <= 1e-4f, $"shoulder point {p} of edge {i} is on the inner side of its chamfer");
                }
                // it does bulge: the middle of the arc is half way from the chord to the corner
                var m = HullProfile.ShoulderPoint(a, b, i, 0.5f);
                Assert.Equal(Vector2.Distance(mid, HullProfile.ShoulderCorner(a, b, i)) / 2f, Vector2.Distance(mid, m), 3);
            }
        }
    }

    [Fact]
    public void TheShoulderMeetsTheWallAndTheRoofAtTheEndsOfTheChamfer()
    {
        var s = HullProfile.Octagon(HullProfile.Roof, 1f);
        foreach (int i in new[] { 1, 3, 5, 7 })
        {
            var a = s[i]; var b = s[(i + 1) % s.Length];
            Assert.Equal(a, HullProfile.ShoulderPoint(a, b, i, 0f));
            Assert.Equal(b, HullProfile.ShoulderPoint(a, b, i, 1f));
            // tangent to the wall / roof it joins: the first step runs along the line the neighbouring edge lies on
            var d0 = HullProfile.ShoulderPoint(a, b, i, 0.001f) - a;
            var along = HullProfile.ShoulderCorner(a, b, i) - a;
            Assert.True(Math.Abs(d0.X * along.Y - d0.Y * along.X) < 1e-4f, "the arc leaves its first end along the corner direction");
        }
    }

    [Fact]
    public void TheStationRoofChamferStaysAboveTheBodyAndTheViewports()
    {
        const float top = HullProfile.Roof - HullProfile.StationRoofBevel;
        Assert.True(top >= HullProfile.BodyHeight + 1.0f, "the cut starts at least a cell above the head");
        Assert.True(top > 3.0f, "above the viewport frames of the station walls (their top is 2.84 cells)");
        // the chamfer of a station is 45 degrees and the cornice that stands under it fits between the window frames (2.84) and the foot of the chamfer
        Assert.InRange(top, 3.1f, 3.5f);
        // the flat part of the roof stays wide enough for what stands on it (the containers span 1.2 to 6.9 cells across)
        Assert.True(HullProfile.W - 2 * (HullProfile.StationFace + HullProfile.StationRoofBevel) >= 6.0f);
    }
}
