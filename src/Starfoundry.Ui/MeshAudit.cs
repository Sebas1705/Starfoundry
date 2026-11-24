using System.Numerics;

namespace Starfoundry.Ui;

/// <summary>One triangle of a built model, in a common space, with the outward normal the builder gave it.</summary>
/// <param name="TwoSided">True for a material drawn from both sides (glass): it is seen from either side and never culled.</param>
/// <param name="Part">A label of the part it belongs to (the mesh node name and material), used to aggregate reports.</param>
public readonly record struct AuditTri(Vector3 A, Vector3 B, Vector3 C, Vector3 N, bool TwoSided, string Part);

/// <summary>Two parts whose faces lie in the same plane and overlap: they z-fight (flicker) wherever both are visible.</summary>
public readonly record struct CoplanarOverlap(string PartA, string PartB, float Area, Vector3 Where, float Gap, Vector3 Normal = default);

/// <summary>A ray that finds no visible surface: a hole in the shell.</summary>
public readonly record struct SeeThrough(Vector3 Origin, Vector3 Dir);

/// <summary>
/// Engine-free checks on the triangle soup of a built model: faces that lie in one plane and overlap (the cause of texture flicker, z-fighting),
/// and rays that pass the shell without meeting a surface that would be drawn (holes, culled back faces, light leaks). The game builds the soup
/// from the real module meshes (<c>Gallery --audit</c>); the unit tests feed it small hand-made meshes.
/// </summary>
public static class MeshAudit
{
    // ---- coplanar overlaps (z-fighting) ----------------------------------------------------------------------------------

    /// <summary>
    /// Pairs of triangles facing the same way, lying within <paramref name="planeEps"/> of one plane and overlapping by more than
    /// <paramref name="minArea"/>. Back-to-back faces (opposite normals) are not reported: one of them is always culled. Results are merged
    /// per pair of parts (largest overlap kept, areas summed).
    /// </summary>
    public static List<CoplanarOverlap> CoplanarOverlaps(IReadOnlyList<AuditTri> tris, float planeEps = 0.004f, float minArea = 2e-4f)
    {
        const float q = 50f; // normal quantisation: components rounded to 0.02
        var buckets = new Dictionary<(int, int, int, int), List<int>>();
        var plane = new (Vector3 N, float D)[tris.Count];
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            var n = Vector3.Normalize(t.N);
            float d = Vector3.Dot(n, t.A);
            plane[i] = (n, d);
            var key = (R(n.X * q), R(n.Y * q), R(n.Z * q), (int)MathF.Floor(d / planeEps));
            if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<int>();
            l.Add(i);
        }
        var found = new Dictionary<(string, string), CoplanarOverlap>();
        for (int i = 0; i < tris.Count; i++)
        {
            var (n, d) = plane[i];
            int qx = R(n.X * q), qy = R(n.Y * q), qz = R(n.Z * q), qd = (int)MathF.Floor(d / planeEps);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dd = -1; dd <= 1; dd++)
                        {
                            if (!buckets.TryGetValue((qx + dx, qy + dy, qz + dz, qd + dd), out var list)) continue;
                            foreach (int j in list)
                            {
                                if (j <= i) continue;
                                var (n2, d2) = plane[j];
                                if (Vector3.Dot(n, n2) < 0.9995f || MathF.Abs(d - d2) > planeEps) continue;
                                float area = OverlapArea(tris[i], tris[j], n);
                                if (area < minArea) continue;
                                string a = tris[i].Part, b = tris[j].Part;
                                var pk = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
                                var where = (tris[i].A + tris[i].B + tris[i].C + tris[j].A + tris[j].B + tris[j].C) / 6f;
                                found[pk] = found.TryGetValue(pk, out var old)
                                    ? old with { Area = old.Area + area, Where = area > old.Area ? where : old.Where, Gap = MathF.Max(old.Gap, MathF.Abs(d - d2)), Normal = area > old.Area ? n : old.Normal }
                                    : new CoplanarOverlap(pk.Item1, pk.Item2, area, where, MathF.Abs(d - d2), n);
                            }
                        }
        }
        return found.Values.OrderByDescending(o => o.Area).ToList();
    }

    private static int R(float v) => (int)MathF.Round(v);

    /// <summary>The area of the intersection of two coplanar triangles (convex polygon clipping in the plane of <paramref name="n"/>).</summary>
    public static float OverlapArea(in AuditTri a, in AuditTri b, Vector3 n)
    {
        // an orthonormal basis of the plane
        var u = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY)) : Vector3.Normalize(Vector3.Cross(n, Vector3.UnitX));
        var v = Vector3.Cross(n, u);
        Vector2 P(Vector3 p) => new(Vector3.Dot(p, u), Vector3.Dot(p, v));
        var subject = new List<Vector2> { P(a.A), P(a.B), P(a.C) };
        var clip = new[] { P(b.A), P(b.B), P(b.C) };
        if (Signed(clip) < 0) Array.Reverse(clip);
        if (Signed(subject.ToArray()) < 0) subject.Reverse();
        for (int i = 0; i < 3 && subject.Count > 0; i++)
        {
            var c0 = clip[i]; var c1 = clip[(i + 1) % 3];
            var input = subject; subject = new List<Vector2>();
            for (int k = 0; k < input.Count; k++)
            {
                var s = input[k]; var e = input[(k + 1) % input.Count];
                bool sin = Side(c0, c1, s) >= -1e-6f, ein = Side(c0, c1, e) >= -1e-6f;
                if (sin != ein) subject.Add(Cross(c0, c1, s, e));
                if (ein) subject.Add(e);
            }
        }
        return subject.Count < 3 ? 0f : MathF.Abs(Signed(subject.ToArray()));
    }

    private static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static Vector2 Cross(Vector2 c0, Vector2 c1, Vector2 s, Vector2 e)
    {
        float s0 = Side(c0, c1, s), s1 = Side(c0, c1, e);
        float t = s0 / (s0 - s1);
        return s + (e - s) * t;
    }

    private static float Signed(Vector2[] p)
    {
        float a = 0;
        for (int i = 0; i < p.Length; i++) { var q = p[(i + 1) % p.Length]; a += p[i].X * q.Y - q.X * p[i].Y; }
        return a / 2f;
    }

    // ---- rays and holes ----------------------------------------------------------------------------------------------------

    /// <summary>A triangle soup with a uniform grid over it, for ray casts.</summary>
    public sealed class Scene
    {
        private readonly AuditTri[] _tris;
        private readonly Dictionary<(int, int, int), List<int>> _grid = new();
        private const float Cell = 1f;
        public readonly Vector3 Min, Max;

        public Scene(IReadOnlyList<AuditTri> tris)
        {
            _tris = tris.ToArray();
            Min = new Vector3(float.MaxValue); Max = new Vector3(float.MinValue);
            for (int i = 0; i < _tris.Length; i++)
            {
                var t = _tris[i];
                var lo = Vector3.Min(t.A, Vector3.Min(t.B, t.C)); var hi = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
                Min = Vector3.Min(Min, lo); Max = Vector3.Max(Max, hi);
                for (int x = (int)MathF.Floor(lo.X / Cell); x <= (int)MathF.Floor(hi.X / Cell); x++)
                    for (int y = (int)MathF.Floor(lo.Y / Cell); y <= (int)MathF.Floor(hi.Y / Cell); y++)
                        for (int z = (int)MathF.Floor(lo.Z / Cell); z <= (int)MathF.Floor(hi.Z / Cell); z++)
                        {
                            if (!_grid.TryGetValue((x, y, z), out var l)) _grid[(x, y, z)] = l = new List<int>();
                            l.Add(i);
                        }
            }
        }

        /// <summary>
        /// The nearest triangle along the ray that blocks the sight of a viewer: <paramref name="viewerAtOrigin"/> means the eye is at the origin
        /// (a triangle blocks when it faces the ray or is two-sided); otherwise the eye is far along the ray looking back, so a triangle blocks when it
        /// faces along the ray (its outer side is turned to the eye) or is two-sided. Returns the distance, or null when nothing blocks within <paramref name="maxT"/>.
        /// </summary>
        public (float T, int Tri)? FirstBlocker(Vector3 o, Vector3 d, float maxT, bool viewerAtOrigin)
        {
            float best = float.MaxValue; int bi = -1;
            // exact grid walk (Amanatides and Woo): every cell the ray passes through, in order
            int cx = (int)MathF.Floor(o.X / Cell), cy = (int)MathF.Floor(o.Y / Cell), cz = (int)MathF.Floor(o.Z / Cell);
            int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
            float tx = Next(o.X, d.X, cx, sx), ty = Next(o.Y, d.Y, cy, sy), tz = Next(o.Z, d.Z, cz, sz);
            float dx = MathF.Abs(d.X) < 1e-9f ? float.MaxValue : Cell / MathF.Abs(d.X), dy = MathF.Abs(d.Y) < 1e-9f ? float.MaxValue : Cell / MathF.Abs(d.Y), dz = MathF.Abs(d.Z) < 1e-9f ? float.MaxValue : Cell / MathF.Abs(d.Z);
            while (true)
            {
                if (_grid.TryGetValue((cx, cy, cz), out var list))
                    foreach (int i in list)
                    {
                        var tri = _tris[i];
                        float facing = Vector3.Dot(tri.N, d);
                        bool blocks = tri.TwoSided || (viewerAtOrigin ? facing < 0f : facing > 0f);
                        if (!blocks) continue;
                        float hit = Intersect(o, d, tri);
                        if (hit > 1e-3f && hit < best && hit <= maxT) { best = hit; bi = i; }
                    }
                float exit = MathF.Min(tx, MathF.Min(ty, tz));
                if (bi >= 0 && best <= exit) break; // nothing nearer can lie in a later cell
                if (exit > maxT) break;
                if (tx <= ty && tx <= tz) { cx += sx; tx += dx; }
                else if (ty <= tz) { cy += sy; ty += dy; }
                else { cz += sz; tz += dz; }
            }
            return bi < 0 ? null : (best, bi);
        }

        private static float Next(float o, float d, int cell, int s)
        {
            if (MathF.Abs(d) < 1e-9f) return float.MaxValue;
            float edge = (s > 0 ? cell + 1 : cell) * Cell;
            return (edge - o) / d;
        }

        /// <summary>Every triangle the line from <paramref name="o"/> along <paramref name="d"/> crosses within <paramref name="maxT"/> (both sides), nearest first; for debugging a hole.</summary>
        public List<(float T, AuditTri Tri)> AllHits(Vector3 o, Vector3 d, float maxT)
        {
            var hits = new List<(float, AuditTri)>();
            for (int i = 0; i < _tris.Length; i++)
            {
                float t = Intersect(o, d, _tris[i]);
                if (t > 1e-3f && t <= maxT) hits.Add((t, _tris[i]));
            }
            hits.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return hits;
        }

        public AuditTri Tri(int i) => _tris[i];

        private static float Intersect(Vector3 o, Vector3 d, in AuditTri t)
        {
            var e1 = t.B - t.A; var e2 = t.C - t.A;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-9f) return -1f;
            float inv = 1f / det;
            var s = o - t.A;
            float u = Vector3.Dot(s, p) * inv;
            if (u < -1e-4f || u > 1f + 1e-4f) return -1f;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < -1e-4f || u + v > 1f + 1e-4f) return -1f;
            return Vector3.Dot(e2, q) * inv;
        }
    }

    /// <summary>The unit directions of a sphere sampled by a latitude/longitude grid (about <c>rings * 2 * rings</c> of them).</summary>
    public static List<Vector3> Directions(int rings)
    {
        var l = new List<Vector3>();
        for (int i = 0; i <= rings; i++)
        {
            float lat = MathF.PI * i / rings, y = MathF.Cos(lat), r = MathF.Sin(lat);
            int n = Math.Max(1, (int)MathF.Round(r * 2 * rings));
            for (int j = 0; j < n; j++) { float lon = MathF.Tau * (j + 0.37f * i % 1f) / n; l.Add(new Vector3(r * MathF.Cos(lon), y, r * MathF.Sin(lon))); }
        }
        return l;
    }
}
