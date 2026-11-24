using System.Numerics;

namespace Starfoundry.Ui;

/// <summary>A connected piece of a module's mesh that touches nothing anchored to the hull: it floats.</summary>
public readonly record struct FloatingPiece(string Parts, int Triangles, Vector3 Lo, Vector3 Hi, float Gap = 0f);

/// <summary>Attachment triangles that cut through glazing: how many triangle pairs and where.</summary>
public readonly record struct GlassHit(string Attachment, string Glass, int Pairs, Vector3 Where);

public static partial class MeshAudit
{
    // ---- floating components -----------------------------------------------------------------------------------------

    /// <summary>
    /// The connected pieces of a triangle soup (usually one module's) that do not touch an anchor. Two triangles are connected when they are closer than
    /// <paramref name="tol"/> (a vertex near the other triangle, or an edge through it); a piece is anchored when it holds a triangle of a part for which
    /// <paramref name="isAnchor"/> is true (the hull skin, the deck). Everything else that stays unreached is a part standing in the air: a ring, a pipe, a rail
    /// or a pillar that touches neither the hull nor another part.
    /// </summary>
    public static List<FloatingPiece> FloatingComponents(IReadOnlyList<AuditTri> tris, Func<string, bool> isAnchor, float tol = 0.03f)
    {
        int n = tris.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var lo = new Vector3[n]; var hi = new Vector3[n];
        const float Cell = 1f;
        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var t = tris[i];
            lo[i] = Vector3.Min(Vector3.Min(t.A, t.B), t.C) - new Vector3(tol);
            hi[i] = Vector3.Max(Vector3.Max(t.A, t.B), t.C) + new Vector3(tol);
            for (int x = (int)MathF.Floor(lo[i].X / Cell); x <= (int)MathF.Floor(hi[i].X / Cell); x++)
                for (int y = (int)MathF.Floor(lo[i].Y / Cell); y <= (int)MathF.Floor(hi[i].Y / Cell); y++)
                    for (int z = (int)MathF.Floor(lo[i].Z / Cell); z <= (int)MathF.Floor(hi[i].Z / Cell); z++)
                    {
                        if (!grid.TryGetValue((x, y, z), out var l)) grid[(x, y, z)] = l = new List<int>();
                        l.Add(i);
                    }
        }
        var stamp = new int[n];
        for (int i = 0; i < n; i++)
        {
            for (int x = (int)MathF.Floor(lo[i].X / Cell); x <= (int)MathF.Floor(hi[i].X / Cell); x++)
                for (int y = (int)MathF.Floor(lo[i].Y / Cell); y <= (int)MathF.Floor(hi[i].Y / Cell); y++)
                    for (int z = (int)MathF.Floor(lo[i].Z / Cell); z <= (int)MathF.Floor(hi[i].Z / Cell); z++)
                        foreach (int j in grid[(x, y, z)])
                        {
                            if (j <= i || stamp[j] == i + 1) continue;
                            stamp[j] = i + 1;
                            if (Find(i) == Find(j)) continue;
                            if (lo[i].X > hi[j].X || hi[i].X < lo[j].X || lo[i].Y > hi[j].Y || hi[i].Y < lo[j].Y || lo[i].Z > hi[j].Z || hi[i].Z < lo[j].Z) continue;
                            if (Touch(tris[i], tris[j], tol)) parent[Find(i)] = Find(j);
                        }
        }
        var anchored = new HashSet<int>();
        for (int i = 0; i < n; i++) if (isAnchor(tris[i].Part)) anchored.Add(Find(i));
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (anchored.Contains(r)) continue;
            if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
            l.Add(i);
        }
        var result = new List<FloatingPiece>();
        foreach (var g in groups.Values)
        {
            var a = new Vector3(float.MaxValue); var b = new Vector3(float.MinValue);
            foreach (int i in g) { a = Vector3.Min(a, lo[i] + new Vector3(tol)); b = Vector3.Max(b, hi[i] - new Vector3(tol)); }
            // how far the piece is from the nearest anchored triangle: a gap of a few centimetres is a part that nearly touches
            float best = float.MaxValue;
            foreach (int i in g)
                foreach (var v in new[] { tris[i].A, tris[i].B, tris[i].C })
                    for (int k = 0; k < n; k++)
                        if (anchored.Contains(Find(k))) best = MathF.Min(best, PointTriDistSq(v, tris[k]));
            result.Add(new FloatingPiece(string.Join(",", g.Select(i => tris[i].Part).Distinct().OrderBy(s => s).Take(4)), g.Count, a, b, MathF.Sqrt(best)));
        }
        return result.OrderByDescending(p => p.Triangles).ToList();
    }

    private static bool Touch(in AuditTri a, in AuditTri b, float tol)
    {
        foreach (var v in new[] { a.A, a.B, a.C }) if (PointTriDistSq(v, b) <= tol * tol) return true;
        foreach (var v in new[] { b.A, b.B, b.C }) if (PointTriDistSq(v, a) <= tol * tol) return true;
        return EdgesCut(a, b) || EdgesCut(b, a);
    }

    private static bool EdgesCut(in AuditTri a, in AuditTri b) =>
        SegmentHitsTri(a.A, a.B, b) || SegmentHitsTri(a.B, a.C, b) || SegmentHitsTri(a.C, a.A, b);

    /// <summary>True when the segment from p to q passes through the triangle (Moller-Trumbore on the segment).</summary>
    public static bool SegmentHitsTri(Vector3 p, Vector3 q, in AuditTri t)
    {
        var d = q - p;
        var e1 = t.B - t.A; var e2 = t.C - t.A;
        var pv = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, pv);
        if (MathF.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        var s = p - t.A;
        float u = Vector3.Dot(s, pv) * inv;
        if (u < 0f || u > 1f) return false;
        var qv = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, qv) * inv;
        if (v < 0f || u + v > 1f) return false;
        float s2 = Vector3.Dot(e2, qv) * inv;
        return s2 >= 0f && s2 <= 1f;
    }

    /// <summary>Squared distance from a point to a triangle (Ericson, Real-Time Collision Detection).</summary>
    public static float PointTriDistSq(Vector3 p, in AuditTri t)
    {
        Vector3 a = t.A, b = t.B, c = t.C;
        var ab = b - a; var ac = c - a; var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return (p - a).LengthSquared();
        var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return (p - b).LengthSquared();
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); return (p - (a + v * ab)).LengthSquared(); }
        var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return (p - c).LengthSquared();
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); return (p - (a + w * ac)).LengthSquared(); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return (p - (b + w * (c - b))).LengthSquared(); }
        float denom = 1f / (va + vb + vc);
        float vv = vb * denom, ww = vc * denom;
        return (p - (a + ab * vv + ac * ww)).LengthSquared();
    }

    // ---- attachments through glazing ----------------------------------------------------------------------------------

    /// <summary>
    /// Triangles of attachments (wings, antennas, pods) that cut through triangles of glazing: a solid part standing in a window. Results are merged
    /// per pair of parts. Only real crossings count (an edge of one through the other); parts that merely touch at an edge do not.
    /// </summary>
    public static List<GlassHit> AttachmentsThroughGlass(IReadOnlyList<AuditTri> tris, Func<string, bool> isAttachment, Func<string, bool> isGlass)
    {
        var att = tris.Where(t => isAttachment(t.Part)).ToList();
        var glass = tris.Where(t => isGlass(t.Part)).ToList();
        var found = new Dictionary<(string, string), (int Count, Vector3 Where)>();
        foreach (var a in att)
        {
            var alo = Vector3.Min(Vector3.Min(a.A, a.B), a.C); var ahi = Vector3.Max(Vector3.Max(a.A, a.B), a.C);
            foreach (var g in glass)
            {
                var glo = Vector3.Min(Vector3.Min(g.A, g.B), g.C); var ghi = Vector3.Max(Vector3.Max(g.A, g.B), g.C);
                if (alo.X > ghi.X || ahi.X < glo.X || alo.Y > ghi.Y || ahi.Y < glo.Y || alo.Z > ghi.Z || ahi.Z < glo.Z) continue;
                if (!EdgesCutStrict(a, g) && !EdgesCutStrict(g, a)) continue;
                var key = (a.Part, g.Part);
                found[key] = found.TryGetValue(key, out var old) ? (old.Count + 1, old.Where) : (1, (a.A + a.B + a.C) / 3f);
            }
        }
        return found.Select(kv => new GlassHit(kv.Key.Item1, kv.Key.Item2, kv.Value.Count, kv.Value.Where)).OrderByDescending(h => h.Pairs).ToList();
    }

    /// <summary>An edge of <paramref name="a"/> pierces <paramref name="b"/> well inside it (not just at its boundary).</summary>
    private static bool EdgesCutStrict(in AuditTri a, in AuditTri b)
    {
        var c = (b.A + b.B + b.C) / 3f;
        // shrink b a little towards its centre, so a shared boundary does not count
        var s = new AuditTri(c + (b.A - c) * 0.97f, c + (b.B - c) * 0.97f, c + (b.C - c) * 0.97f, b.N, b.TwoSided, b.Part);
        return SegmentHitsTri(a.A, a.B, s) || SegmentHitsTri(a.B, a.C, s) || SegmentHitsTri(a.C, a.A, s);
    }
}
