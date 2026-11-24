using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Face-framed UVs for planar-mapped materials (<see cref="Materials.IsPlanar"/>). The mesh builder hands over its flat-shaded triangle
/// soup and this works out the UVs after the fact, so no geometry code has to know about textures:
/// <list type="number">
/// <item>Triangles that share an edge and lie in one plane form a <em>face</em> (a wall with a window hole, a deck with a hatch, a chamfer strip: one face).</item>
/// <item>Every face gets its own frame: u and v run along the face (a world axis for a wall, floor or roof; the contour line and the slant for a chamfer or a nose slope), the origin sits on a corner of the face and the longer side is u.</item>
/// <item>The face is measured in panels (<c>tile</c> world units each) and the UVs are scaled so a whole number of panels fits each side. The panel seams therefore fall exactly on the face's borders, whatever the size of the face, and a chamfer carries as many panels along its length as the wall next to it.</item>
/// <item>A sliver narrower than half a panel is not stretched: it keeps the world scale and shows the middle of the texture (a beam, a rail, the edge of a slab).</item>
/// </list>
/// The UVs are counted in panels (one texture repeat is one panel), so a planar material uses a UV scale of 1.
/// </summary>
internal static class FaceUv
{
    /// <summary>Fills <paramref name="uv"/> (one per vertex) and <paramref name="tangent"/> (four floats per vertex) for a triangle soup with flat per-triangle normals.</summary>
    public static void Compute(IReadOnlyList<Vector3> v, IReadOnlyList<Vector3> n, float tile, out Vector2[] uv, out float[] tangent)
    {
        int tris = v.Count / 3;
        uv = new Vector2[v.Count];
        tangent = new float[v.Count * 4];

        // weld the vertices so triangles of one face share ids
        var ids = new Dictionary<(int, int, int), int>();
        var vid = new int[v.Count];
        for (int i = 0; i < v.Count; i++)
        {
            var key = (Q(v[i].X), Q(v[i].Y), Q(v[i].Z));
            if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
            vid[i] = id;
        }

        // union triangles that share an edge and face the same way
        var parent = new int[tris];
        for (int i = 0; i < tris; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var edges = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t < tris; t++)
            for (int e = 0; e < 3; e++)
            {
                int a = vid[t * 3 + e], b = vid[t * 3 + (e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                foreach (int o in list)
                    if (n[o * 3].Dot(n[t * 3]) > 0.9999f) parent[Find(o)] = Find(t);
                list.Add(t);
            }

        var islands = new Dictionary<int, List<int>>();
        for (int t = 0; t < tris; t++)
        {
            int r = Find(t);
            if (!islands.TryGetValue(r, out var l)) islands[r] = l = new List<int>();
            l.Add(t);
        }

        foreach (var tl in islands.Values) Frame(v, n, vid, tl, tile, uv, tangent);
    }

    private static int Q(float x) => (int)MathF.Round(x * 1000f);

    /// <summary>The u and v axes of a face with normal <paramref name="nrm"/> (before the longer side is chosen).</summary>
    internal static (Vector3 U, Vector3 V) Axes(Vector3 nrm, IReadOnlyList<Vector3> v, int[] vid, List<int> tris)
    {
        float ax = MathF.Abs(nrm.X), ay = MathF.Abs(nrm.Y), az = MathF.Abs(nrm.Z);
        if (ax >= 0.9995f) return (Vector3.Back, Vector3.Up);
        if (ay >= 0.9995f) return (Vector3.Right, Vector3.Back);
        if (az >= 0.9995f) return (Vector3.Right, Vector3.Up);
        // a slope (chamfer, nose, taper): u along the longest border edge of the face, so at least that border is a panel seam
        var count = new Dictionary<(int, int), int>();
        foreach (int t in tris)
            for (int e = 0; e < 3; e++)
            {
                int a = vid[t * 3 + e], b = vid[t * 3 + (e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        float best = 0f;
        var tu = Vector3.Up.Cross(nrm).Normalized();
        foreach (int t in tris)
            for (int e = 0; e < 3; e++)
            {
                int a = vid[t * 3 + e], b = vid[t * 3 + (e + 1) % 3];
                if (count[a < b ? (a, b) : (b, a)] != 1) continue;
                var d = v[t * 3 + (e + 1) % 3] - v[t * 3 + e];
                d -= nrm * d.Dot(nrm);
                float len = d.Length();
                if (len > best + 1e-4f) { best = len; tu = d / len; }
            }
        return (tu, nrm.Cross(tu).Normalized());
    }

    private static void Frame(IReadOnlyList<Vector3> v, IReadOnlyList<Vector3> n, int[] vid, List<int> tris, float tile, Vector2[] uv, float[] tangent)
    {
        var nrm = n[tris[0] * 3];
        var (tu, tv) = Axes(nrm, v, vid, tris);
        Measure(v, tris, tu, tv, out float u0, out float u1, out float v0, out float v1);
        if (v1 - v0 > (u1 - u0) * 1.001f)
        {
            (tu, tv) = (tv, tu); // the longer side runs along u (brushed metal, rails)
            Measure(v, tris, tu, tv, out u0, out u1, out v0, out v1);
        }
        Fit(u1 - u0, u0, tile, out float su, out float ou);
        Fit(v1 - v0, v0, tile, out float sv, out float ov);
        float sign = nrm.Cross(tu).Dot(tv) >= 0f ? 1f : -1f;
        foreach (int t in tris)
            for (int k = 0; k < 3; k++)
            {
                int i = t * 3 + k;
                var p = v[i];
                uv[i] = new Vector2(p.Dot(tu) * su + ou, p.Dot(tv) * sv + ov);
                tangent[i * 4] = tu.X; tangent[i * 4 + 1] = tu.Y; tangent[i * 4 + 2] = tu.Z; tangent[i * 4 + 3] = sign;
            }
    }

    private static void Measure(IReadOnlyList<Vector3> v, List<int> tris, Vector3 tu, Vector3 tv, out float u0, out float u1, out float v0, out float v1)
    {
        u0 = v0 = float.MaxValue; u1 = v1 = float.MinValue;
        foreach (int t in tris)
            for (int k = 0; k < 3; k++)
            {
                var p = v[t * 3 + k];
                float a = p.Dot(tu), b = p.Dot(tv);
                u0 = MathF.Min(u0, a); u1 = MathF.Max(u1, a);
                v0 = MathF.Min(v0, b); v1 = MathF.Max(v1, b);
            }
    }

    /// <summary>Scale and offset for one side of a face: a whole number of panels, or (for a sliver) the world scale centred on the middle of the texture.</summary>
    internal static void Fit(float len, float min, float tile, out float scale, out float offset)
    {
        if (len >= 0.5f * tile)
        {
            int panels = Math.Max(1, (int)MathF.Round(len / tile));
            scale = panels / len;
            offset = -min * scale;
        }
        else
        {
            scale = 1f / tile;
            offset = 0.5f - (min + len / 2f) * scale;
        }
    }
}
