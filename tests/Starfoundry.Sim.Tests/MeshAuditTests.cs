using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class MeshAuditTests
{
    /// <summary>A flat quad (two triangles) in the plane y = <paramref name="y"/> facing up (or down).</summary>
    private static List<AuditTri> Floor(string part, float x0, float x1, float z0, float z1, float y, bool up = true, bool twoSided = false)
    {
        var n = up ? Vector3.UnitY : -Vector3.UnitY;
        return new()
        {
            new(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), n, twoSided, part),
            new(new(x0, y, z0), new(x1, y, z1), new(x0, y, z1), n, twoSided, part),
        };
    }

    [Fact]
    public void TwoPlatesInOnePlaneAreReportedAndASeparatedPlateIsNot()
    {
        var tris = Floor("deck", 0, 4, 0, 4, 0.1f);
        tris.AddRange(Floor("stripe", 1, 2, 1, 3, 0.1f));          // coplanar and inside: flickers
        tris.AddRange(Floor("lifted", 1, 2, 1, 3, 0.12f));         // 2 cm above: a real offset, no flicker
        tris.AddRange(Floor("elsewhere", 5, 6, 0, 4, 0.1f));       // same plane, no overlap
        var hits = MeshAudit.CoplanarOverlaps(tris);
        Assert.Single(hits);
        Assert.Equal(("deck", "stripe"), (hits[0].PartA, hits[0].PartB));
        Assert.Equal(2f, hits[0].Area, 0.01f);
    }

    [Fact]
    public void BackToBackFacesAndTouchingEdgesAreNotAFlicker()
    {
        var tris = Floor("a", 0, 2, 0, 2, 0f);
        tris.AddRange(Floor("b", 0, 2, 0, 2, 0f, up: false)); // opposite facing: one is always culled
        tris.AddRange(Floor("c", 2, 4, 0, 2, 0f));            // shares only an edge
        Assert.Empty(MeshAudit.CoplanarOverlaps(tris));
    }

    [Fact]
    public void ASlopedOverlapIsFoundToo()
    {
        var n = Vector3.Normalize(new Vector3(0, 1, 1));
        // two triangles of the same slanted plane, one smaller
        var hits = MeshAudit.CoplanarOverlaps(new List<AuditTri> { new(new(0, 0, 0), new(4, 0, 0), new(0, 2, -2), n, false, "big"), new(new(0.5f, 0.5f, -0.5f), new(2, 0.5f, -0.5f), new(0.5f, 1.2f, -1.2f), n, false, "small") });
        Assert.Single(hits);
    }

    [Fact]
    public void ARayFindsOnlyTheSurfacesAViewerSees()
    {
        // a roof facing down (seen from below) at y = 2: an eye under it is blocked, an eye above it is not (it is culled from above)
        var roof = new Dictionary<string, List<AuditTri>> { ["r"] = Floor("roof", -5, 5, -5, 5, 2f, up: false) }["r"];
        var scene = new MeshAudit.Scene(roof);
        Assert.NotNull(scene.FirstBlocker(new(0, 1, 0), Vector3.UnitY, 10f, viewerAtOrigin: true));
        Assert.Null(scene.FirstBlocker(new(0, 3, 0), -Vector3.UnitY, 10f, viewerAtOrigin: true));
        // seen from outside: a roof whose normal faces down is the underside; an eye far above it (looking down) does not see that face
        Assert.Null(scene.FirstBlocker(new(0, 1, 0), Vector3.UnitY, 10f, viewerAtOrigin: false));
        var glass = new MeshAudit.Scene(Floor("glass", -5, 5, -5, 5, 2f, twoSided: true));
        Assert.NotNull(glass.FirstBlocker(new(0, 1, 0), Vector3.UnitY, 10f, viewerAtOrigin: false));
        Assert.NotNull(glass.FirstBlocker(new(0, 1, 0), Vector3.UnitY, 10f, viewerAtOrigin: true));
    }

    [Fact]
    public void TheDirectionSetCoversTheSphere()
    {
        var d = MeshAudit.Directions(6);
        Assert.True(d.Count > 40);
        Assert.All(d, v => Assert.Equal(1f, v.Length(), 0.001f));
        Assert.Contains(d, v => v.Y > 0.99f);
        Assert.Contains(d, v => v.Y < -0.99f);
    }
}
