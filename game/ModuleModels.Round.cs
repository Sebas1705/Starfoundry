using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Rounded shoulders for the ship hull (hull-curves pass, step A of the hull shapes work). The section of a ship hull is an octagon: four long walls
/// joined by 45 degree chamfers (<see cref="HullProfile.Octagon"/>). Seen from outside that reads as a faceted tube. Here every chamfer that is
/// really there (a side that touches another module is squared and has none, so the smoothing follows the neighbours by itself) gets a fairing: a
/// curved strip between the chamfer and the sharp corner it cut off, a quadratic arc that leaves the wall and the roof (or the floor) tangent, so the
/// section reads as a rounded rectangle.
///
/// The fairing only adds material between the chamfer and the module's box corner. It is outside the shell the walking body sees (the liner, the
/// chamfer face itself stays drawn under it), so walkable volumes and <see cref="HullProfile"/> are untouched, and it never leaves the module box: the
/// hit volume of the sim (oriented module boxes) is at least as big as what is drawn, with or without it. Nothing here is solid for the simulation.
/// Edges that carry glass (a canopy) or a viewport wall are left alone.
/// </summary>
public static partial class ModuleModels
{
    /// <summary>Segments of the arc of a fairing, by chamfer size: a small chamfer needs fewer to look round.</summary>
    private static int ArcSegments(float chamfer) => chamfer >= 0.9f ? 5 : chamfer >= 0.45f ? 4 : 3;

    private static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);

    /// <summary>The corner of the box a chamfer cuts off, for the odd edge <paramref name="i"/> of a section (the numbers live in <see cref="HullProfile.ShoulderCorner"/>, which the tests pin).</summary>
    private static Vector2 CutCorner(Vector2[] s, int i)
    {
        var k = HullProfile.ShoulderCorner(N(s[i]), N(s[(i + 1) % s.Length]), i);
        return new Vector2(k.X, k.Y);
    }

    /// <summary>The point <paramref name="t"/> of the way along the arc of the chamfer edge <paramref name="i"/> of a section (<see cref="HullProfile.ShoulderPoint"/>).</summary>
    private static Vector2 ArcPoint(Vector2[] s, int i, float t)
    {
        var p = HullProfile.ShoulderPoint(N(s[i]), N(s[(i + 1) % s.Length]), i, t);
        return new Vector2(p.X, p.Y);
    }

    /// <summary>
    /// The fairings of a loft between the sections <paramref name="a"/> (at depth <paramref name="za"/>) and <paramref name="b"/> (at <paramref name="zb"/>).
    /// <paramref name="skip"/> is the loft's own skip mask (an edge that is glass or a wall that is a viewport gets none); the end caps close the strip where
    /// it is not joined to another one, and are always drawn so a step to a neighbour of another section never shows the inside of the strip.
    /// </summary>
    private static void RoundedShoulders(Models.Mb mb, Vector2[] a, float za, Vector2[] b, float zb, int skip, bool capA, bool capB, Vector2[]? nbA, Vector2[]? nbB)
    {
        const float capInset = 0f;
        // Across a joint the neighbour's section covers this strip's end where its own arc is as full or fuller (the same strip, or a squared corner): no cap
        // then, and no two caps lie back to back in the joint plane. Only the fuller of two different strips is capped.
        static bool Same(Vector2[]? n, Vector2[] s, int i) => n is not null && ArcPoint(s, i, 0.5f).DistanceTo(CutCorner(s, i)) > ArcPoint(n, i, 0.5f).DistanceTo(CutCorner(s, i)) - 0.01f;
        foreach (int i in new[] { 1, 3, 5, 7 })
        {
            if ((skip & (1 << i)) != 0) continue;
            int j = (i + 1) % a.Length;
            float chord = MathF.Max(a[i].DistanceTo(a[j]), b[i].DistanceTo(b[j]));
            if (chord < 0.12f) continue; // a squared corner (a neighbour touches there) or a tiny one: nothing to round
            float ch = chord / 1.4142f;
            int n = ArcSegments(ch);
            // a point inside the strip (between the chord and the arc, half way along), to orient every face outwards
            var ma = (a[i] + a[j]) / 2f; var ka = ArcPoint(a, i, 0.5f);
            var mbp = (b[i] + b[j]) / 2f; var kb = ArcPoint(b, i, 0.5f);
            var inside = new Vector3((ma.X + ka.X + mbp.X + kb.X) / 4f, (ma.Y + ka.Y + mbp.Y + kb.Y) / 4f, (za + zb) / 2f);
            for (int k = 0; k < n; k++)
            {
                var a0 = ArcPoint(a, i, k / (float)n); var a1 = ArcPoint(a, i, (k + 1) / (float)n);
                var b0 = ArcPoint(b, i, k / (float)n); var b1 = ArcPoint(b, i, (k + 1) / (float)n);
                mb.Quad(new(a0.X, a0.Y, za), new(a1.X, a1.Y, za), new(b1.X, b1.Y, zb), new(b0.X, b0.Y, zb), inside);
                if (k == 0) continue;
                // the caps: a fan from the first point of the arc, so the segment between chord and arc is closed at a free end
                var p0a = ArcPoint(a, i, 0f); var p0b = ArcPoint(b, i, 0f);
                if (capA && !Same(nbA, a, i)) mb.Tri(new(p0a.X, p0a.Y, za + capInset), new(a0.X, a0.Y, za + capInset), new(a1.X, a1.Y, za + capInset), inside);
                if (capB && !Same(nbB, b, i)) mb.Tri(new(p0b.X, p0b.Y, zb - capInset), new(b0.X, b0.Y, zb - capInset), new(b1.X, b1.Y, zb - capInset), inside);
            }
        }
    }

    /// <summary>
    /// A swept wing for a ship (hull, cargo and reactor sections): a tapered plate whose leading edge is swept back and whose tip is raked, a lower raised
    /// root strake, and a canted winglet at the tip. It replaces the old rectangular stub. <paramref name="s"/> is -1 for the left wing and +1 for the right,
    /// <paramref name="x0"/> the root on the hull and <paramref name="x1"/> the tip. Same footprint as before (root z 1.2 to 6.6, tip x at 1.9 cells from the
    /// box): thin attachments, like the antennas and solar arrays, are the only things that leave the module box, and they are not solid for the sim.
    /// </summary>
    private static void SweptWing(Models.Mb mb, Cx c, float s, float x0, float x1, float y, float th)
    {
        // plate: root leading edge at z 1.2, swept to the tip leading edge at z 4.3; the tip is raked back in to the trailing edge (convex)
        mb.C(White).Extrude(new[] { new Vector2(x0, 1.2f), new Vector2(x1, 4.3f), new Vector2(x1 - s * 0.35f, 5.75f), new Vector2(x0, 6.6f) }, y, y + th);
        // the strake: a lower, narrower plate on the plate that blends the root into the hull, in the trim colour
        mb.C(c.Acc).Extrude(new[] { new Vector2(x0 - s * 0.02f, 1.7f), new Vector2(x0 + s * 1.15f, 3.1f), new Vector2(x0 + s * 1.0f, 5.3f), new Vector2(x0 - s * 0.02f, 6.0f) }, y + th - 0.01f, y + th + 0.05f);
        // the winglet: a canted fin standing on the tip, leaning outwards, swept back (a skewed box: four base points, four top points)
        float xf = x1 - s * 0.14f, hw = 0.045f, cant = s * 0.5f, h = 1.15f, yb = y + th * 0.5f;
        var b = new[] { new Vector3(xf - hw, yb, 4.55f), new Vector3(xf + hw, yb, 4.55f), new Vector3(xf + hw, yb, 5.75f), new Vector3(xf - hw, yb, 5.75f) };
        var t = new[] { new Vector3(xf - hw + cant, yb + h, 5.35f), new Vector3(xf + hw + cant, yb + h, 5.35f), new Vector3(xf + hw + cant, yb + h, 5.8f), new Vector3(xf - hw + cant, yb + h, 5.8f) };
        var mid = new Vector3(xf + cant / 2f, yb + h / 2f, 5.3f);
        mb.C(c.Acc);
        mb.Quad(b[0], b[1], b[2], b[3], mid); mb.Quad(t[0], t[1], t[2], t[3], mid);
        for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; mb.Quad(b[i], b[j], t[j], t[i], mid); }
        mb.C(c.Role).Block(new(xf + cant * 0.97f, yb + h - 0.1f, 5.55f), new(0.14f, 0.14f, 0.3f)); // a light on the tip, its top a hair under the fin's
    }

    /// <summary>A round strut between two points: a prism of <paramref name="sides"/> sides with the axis from <paramref name="a"/> to <paramref name="b"/> (the builder's Beam, but round).</summary>
    private static void Pipe(Models.Mb mb, Vector3 a, Vector3 b, float radius, int sides = 8)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        mb.At(a, new Quaternion(Vector3.Up, d / len).GetEuler()).Prism(default, radius, radius, len, sides);
        mb.At(default);
    }

    /// <summary>The 45 degree chamfer of a station roof edge, in cells.</summary>
    private const float RoofBevel = HullProfile.StationRoofBevel;

    /// <summary>
    /// The size of the roof chamfer of a station module, or 0 when its roof stays flat. The roof edge of every exposed side of a hull, cargo, reactor or
    /// core module is cut at 45 degrees (the stations stop being boxes; a side that touches another module keeps its square edge, so the modules still
    /// meet flush). The cut stays above 3.1 cells: the walking body (head at 1.8) never reaches it, so no collision changes; the module box of the sim is
    /// still the bigger shape, so the visible hull stays inside the hit volume. A module with another on top has no roof of its own.
    /// </summary>
    private static float StationBevel(Cx c, float roofY) =>
        (!c.Ship && roofY >= 3.5f && !c.Has(Up) && (c.Kind is ModuleKind.Hull or ModuleKind.Cargo or ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Core)) ? RoofBevel : 0f;

    /// <summary>
    /// The roof of a station module with chamfered edges: a hollow cap of a top plate and a 45 degree slope on every exposed side (<paramref name="bl"/>,
    /// <paramref name="br"/>, <paramref name="bf"/>, <paramref name="bk"/>: left, right, front, rear), as an outer skin and a ceiling a plate thickness
    /// <paramref name="t"/> below it, so it is opaque from inside too. The walls of those sides stop at the foot of the slope (<c>roofY - ch</c>); the
    /// ceiling meets the top of a wall exactly at its inner face (the skin rises as far as it goes in, so a vertical offset of the wall thickness lands there).
    /// </summary>
    private static void BevelCap(Models.Mb mb, float roofY, float ch, float si, float t, bool bl, bool br, bool bf, bool bk)
    {
        float xl = si, xr = W - si, zf = si, zk = D - si, yA = roofY - ch;
        float x0 = xl + (bl ? ch : 0f), x1 = xr - (br ? ch : 0f), z0 = zf + (bf ? ch : 0f), z1 = zk - (bk ? ch : 0f);
        var room = new Vector3(4f, 2f, 4f);
        mb.C(Slab);
        Vector3 A(float x, float z) => new(x, yA, z);
        Vector3 B(float x, float z) => new(x, roofY, z);
        // the flat part is a solid plate like the old roof (the ceiling lamps are embedded in it, as before); the slopes are hollow skins that end on its sides
        mb.Block(new((x0 + x1) / 2, roofY - t / 2, (z0 + z1) / 2), new(x1 - x0, t, z1 - z0));
        // the outer skin: one slope per exposed side (a side that touches a module continues as a wall up to the roof)
        if (bl) mb.Quad(A(xl, zf), A(xl, zk), B(x0, z1), B(x0, z0), room);
        if (br) mb.Quad(A(xr, zf), A(xr, zk), B(x1, z1), B(x1, z0), room);
        if (bf) mb.Quad(A(xl, zf), A(xr, zf), B(x1, z0), B(x0, z0), room);
        if (bk) mb.Quad(A(xl, zk), A(xr, zk), B(x1, z1), B(x0, z1), room);
        // the ceiling: the same shape a plate thickness lower, every face turned into the room
        Vector3 Ai(float x, float z) => A(x, z) - new Vector3(0, t, 0);
        Vector3 Bi(float x, float z) => B(x, z) - new Vector3(0, t, 0);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 toRoom) => mb.Quad(a, b, c, d, (a + b + c + d) / 4f - toRoom);
        const float k = 0.70710678f;
        if (bl) Face(Ai(xl, zf), Ai(xl, zk), Bi(x0, z1), Bi(x0, z0), new(k, -k, 0));
        if (br) Face(Ai(xr, zf), Ai(xr, zk), Bi(x1, z1), Bi(x1, z0), new(-k, -k, 0));
        if (bf) Face(Ai(xl, zf), Ai(xr, zf), Bi(x1, z0), Bi(x0, z0), new(0, -k, k));
        if (bk) Face(Ai(xl, zk), Ai(xr, zk), Bi(x1, z1), Bi(x0, z1), new(0, -k, -k));
    }
}
