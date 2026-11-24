using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The floating-parts and attachment-through-glass audits on small hand-made meshes.</summary>
public class MeshAuditPartsTests
{
    /// <summary>The twelve triangles of an axis-aligned box.</summary>
    private static List<AuditTri> Box(string part, Vector3 lo, Vector3 hi)
    {
        var l = new List<AuditTri>();
        Vector3 P(int i) => new((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z);
        int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 } };
        foreach (var f in faces)
        {
            var n = Vector3.Normalize(Vector3.Cross(P(f[1]) - P(f[0]), P(f[2]) - P(f[0])));
            l.Add(new(P(f[0]), P(f[1]), P(f[2]), n, false, part));
            l.Add(new(P(f[0]), P(f[2]), P(f[3]), n, false, part));
        }
        return l;
    }

    private static bool Anchor(string p) => p == "Skin";

    [Fact]
    public void AnAnchoredChainIsNotFloatingAndALoosePartIs()
    {
        var tris = Box("Skin", new(0, 0, 0), new(8, 0.1f, 8));
        tris.AddRange(Box("Pipe", new(1, 0.1f, 1), new(1.2f, 1.5f, 1.2f)));        // stands on the floor
        tris.AddRange(Box("Ring", new(0.9f, 1.5f, 0.9f), new(1.3f, 1.6f, 1.3f)));  // sits on the pipe: anchored through it
        tris.AddRange(Box("Loose", new(4, 2, 4), new(4.5f, 2.5f, 4.5f)));          // in the air
        var floating = MeshAudit.FloatingComponents(tris, Anchor);
        Assert.Single(floating);
        Assert.Equal("Loose", floating[0].Parts);
        Assert.Equal(12, floating[0].Triangles);
        Assert.True(floating[0].Gap > 1.5f);
    }

    [Fact]
    public void AGapWithinTheToleranceTouchesAndOneBeyondDoesNot()
    {
        var tris = Box("Skin", new(0, 0, 0), new(4, 0.1f, 4));
        tris.AddRange(Box("Near", new(1, 0.12f, 1), new(1.5f, 0.6f, 1.5f)));   // 2 cm above the floor
        tris.AddRange(Box("Far", new(2, 0.2f, 2), new(2.5f, 0.6f, 2.5f)));     // 10 cm above it
        var floating = MeshAudit.FloatingComponents(tris, Anchor, tol: 0.03f);
        Assert.Single(floating);
        Assert.Equal("Far", floating[0].Parts);
        Assert.Equal(0.1f, floating[0].Gap, 0.01f);
        Assert.Empty(MeshAudit.FloatingComponents(tris, Anchor, tol: 0.15f));
    }

    [Fact]
    public void ARodThroughAWallIsAnchoredEvenWithNoVertexNearIt()
    {
        var tris = Box("Skin", new(0, 0, 0), new(0.1f, 4, 8)); // a wall
        var rod = Box("Rod", new(-1, 2, 3.9f), new(1, 2.1f, 4.0f)); // pierces the wall, its ends far from any wall vertex
        tris.AddRange(rod);
        Assert.Empty(MeshAudit.FloatingComponents(tris, Anchor));
    }

    [Fact]
    public void PointToTriangleDistanceIsExact()
    {
        var t = new AuditTri(new(0, 0, 0), new(2, 0, 0), new(0, 2, 0), Vector3.UnitZ, false, "t");
        Assert.Equal(0f, MeshAudit.PointTriDistSq(new(0.5f, 0.5f, 0), t), 1e-6f);
        Assert.Equal(1f, MeshAudit.PointTriDistSq(new(0.5f, 0.5f, 1), t), 1e-6f);
        Assert.Equal(2f, MeshAudit.PointTriDistSq(new(-1, -1, 0), t), 1e-6f);
    }

    [Fact]
    public void AWingThroughAWindowIsReportedAndOneBelowItIsNot()
    {
        var glass = new List<AuditTri> { new(new(0, 1.3f, 2), new(0, 1.3f, 5), new(0, 2.7f, 5), Vector3.UnitX, true, "ViewGlass"), new(new(0, 1.3f, 2), new(0, 2.7f, 5), new(0, 2.7f, 2), Vector3.UnitX, true, "ViewGlass") };
        var high = new List<AuditTri>(glass);
        high.AddRange(Box("Fins", new(-1.5f, 1.35f, 1.2f), new(1.5f, 1.51f, 6.6f)));  // the old wing: root at the window sill height
        var hits = MeshAudit.AttachmentsThroughGlass(high, p => p == "Fins", p => p.Contains("Glass"));
        Assert.Single(hits);
        Assert.Equal("Fins", hits[0].Attachment);
        var low = new List<AuditTri>(glass);
        low.AddRange(Box("Fins", new(-1.5f, 0.55f, 1.2f), new(1.5f, 0.71f, 6.6f)));   // the new one, under the sill
        Assert.Empty(MeshAudit.AttachmentsThroughGlass(low, p => p == "Fins", p => p.Contains("Glass")));
    }
}
