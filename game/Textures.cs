using System;
using System.Threading.Tasks;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The pixel work behind <see cref="Materials"/>: tileable, deterministic procedural textures (fixed seeds, no assets, no network). Every
/// generator fills a <see cref="Surf"/> (linear albedo, roughness, metallic, height, emission) and <see cref="Surf.Bake"/> turns it into
/// three or four mipmapped textures: albedo (sRGB), a packed map (G = roughness, B = metallic, both relative: the material multiplies them
/// by its own Roughness/Metallic), a normal map built from the height, and optionally an emission mask.
/// </summary>
internal sealed class TexSet
{
    public ImageTexture Albedo = null!, Orm = null!, Normal = null!;
    public ImageTexture? Emission;
}

/// <summary>Tileable noise (period = the texture) used by every generator.</summary>
internal static class Tn
{
    public static float Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263) ^ ((uint)s * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static int Wrap(int v, int p) => ((v % p) + p) % p;

    /// <summary>Value noise on a lattice of <paramref name="px"/> x <paramref name="py"/> cells that wraps; x, y in lattice units.</summary>
    public static float Vn(float x, float y, int px, int py, int s)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px), ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
        float a = Hash(xa, ya, s), b = Hash(xb, ya, s), c = Hash(xa, yb, s), d = Hash(xb, yb, s);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    /// <summary>Fractal noise, 0..1, u and v in [0,1); <paramref name="fx"/> and <paramref name="fy"/> are the lattice cells of the first octave (unequal = stretched).</summary>
    public static float Fbm(float u, float v, int fx, int fy, int oct, int s)
    {
        float sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < oct; o++)
        {
            int ax = fx << o, ay = fy << o;
            sum += amp * Vn(u * ax, v * ay, ax, ay, s + o * 17);
            norm += amp;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    /// <summary>Wrapping Voronoi on <paramref name="cells"/> x <paramref name="cells"/>: distance to the nearest and second nearest feature point (in cells) and the nearest cell's hash.</summary>
    public static (float F1, float F2, float Id) Vor(float u, float v, int cells, int s)
    {
        float x = u * cells, y = v * cells;
        int cx = (int)MathF.Floor(x), cy = (int)MathF.Floor(y);
        float f1 = 9f, f2 = 9f, id = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy;
                int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                float px = gx + Hash(wx, wy, s), py = gy + Hash(wx, wy, s + 101);
                float d = MathF.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, s + 7); }
                else if (d < f2) f2 = d;
            }
        return (f1, f2, id);
    }

    public static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>A square work surface of linear float channels.</summary>
internal sealed class Surf
{
    public readonly int N;
    public readonly float[] H, R, G, B, Ro, Me, Em;

    public Surf(int n)
    {
        N = n;
        int c = n * n;
        H = new float[c]; R = new float[c]; G = new float[c]; B = new float[c]; Ro = new float[c]; Me = new float[c]; Em = new float[c];
        Array.Fill(R, 1f); Array.Fill(G, 1f); Array.Fill(B, 1f); Array.Fill(Ro, 0.8f);
    }

    public int Ix(int x, int y) => ((y % N + N) % N) * N + ((x % N + N) % N);

    /// <summary>Runs <paramref name="f"/> for every pixel, rows in parallel. The callback may only write its own pixel.</summary>
    public void Each(Action<int, int, float, float> f)
    {
        int n = N;
        Parallel.For(0, n, y =>
        {
            for (int x = 0; x < n; x++) f(x, y, (x + 0.5f) / n, (y + 0.5f) / n);
        });
    }

    public void Set(int i, float r, float g, float b) { R[i] = r; G[i] = g; B[i] = b; }

    /// <summary>A thin wrapping line stamped on albedo (multiplied), roughness (offset), metallic and height: scratches, cracks.</summary>
    public void Line(float x0, float y0, float x1, float y1, float albedo, float rough, float metal, float height)
    {
        int steps = (int)MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0)) + 1;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int k = Ix((int)(x0 + (x1 - x0) * t), (int)(y0 + (y1 - y0) * t));
            R[k] = Math.Clamp(R[k] * albedo, 0, 2); G[k] = Math.Clamp(G[k] * albedo, 0, 2); B[k] = Math.Clamp(B[k] * albedo, 0, 2);
            Ro[k] = Math.Clamp(Ro[k] + rough, 0.05f, 1f);
            Me[k] = Math.Clamp(Me[k] + metal, 0f, 1f);
            H[k] += height;
        }
    }

    /// <summary>Normal map from the height field (wrapping central differences).</summary>
    private byte[] NormalBytes(float strength)
    {
        var data = new byte[N * N * 3];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = H[Ix(x + 1, y)] - H[Ix(x - 1, y)];
                float dy = H[Ix(x, y + 1)] - H[Ix(x, y - 1)];
                var n = new Vector3(-dx * strength, dy * strength, 1f).Normalized();
                int o = (y * N + x) * 3;
                data[o] = (byte)Math.Clamp((int)((n.X * 0.5f + 0.5f) * 255f + 0.5f), 0, 255);
                data[o + 1] = (byte)Math.Clamp((int)((n.Y * 0.5f + 0.5f) * 255f + 0.5f), 0, 255);
                data[o + 2] = (byte)Math.Clamp((int)((n.Z * 0.5f + 0.5f) * 255f + 0.5f), 0, 255);
            }
        return data;
    }

    private static byte Srgb(float lin) => (byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(lin, 0f, 1f), 1f / 2.2f) * 255f + 0.5f), 0, 255);

    private static ImageTexture Make(int n, Image.Format fmt, byte[] data, bool renormalize = false)
    {
        var img = Image.CreateFromData(n, n, false, fmt, data);
        img.GenerateMipmaps(renormalize);
        return ImageTexture.CreateFromImage(img);
    }

    public TexSet Bake(float normalStrength, bool emission = false)
    {
        int c = N * N;
        var alb = new byte[c * 3];
        var orm = new byte[c * 4];
        for (int i = 0; i < c; i++)
        {
            alb[i * 3] = Srgb(R[i]); alb[i * 3 + 1] = Srgb(G[i]); alb[i * 3 + 2] = Srgb(B[i]);
            orm[i * 4] = 255;
            orm[i * 4 + 1] = (byte)Math.Clamp((int)(Ro[i] * 255f + 0.5f), 0, 255);
            orm[i * 4 + 2] = (byte)Math.Clamp((int)(Me[i] * 255f + 0.5f), 0, 255);
            orm[i * 4 + 3] = 255;
        }
        var set = new TexSet
        {
            Albedo = Make(N, Image.Format.Rgb8, alb),
            Orm = Make(N, Image.Format.Rgba8, orm),
            Normal = Make(N, Image.Format.Rgb8, NormalBytes(normalStrength), true),
        };
        if (emission)
        {
            var em = new byte[c * 3];
            for (int i = 0; i < c; i++) { byte v = (byte)Math.Clamp((int)(Em[i] * 255f + 0.5f), 0, 255); em[i * 3] = v; em[i * 3 + 1] = v; em[i * 3 + 2] = v; }
            set.Emission = Make(N, Image.Format.Rgb8, em);
        }
        return set;
    }
}

/// <summary>The generators. Each is deterministic (fixed seed) and cached by <see cref="Materials"/>.</summary>
internal static class Gen
{
    private const float Tau = MathF.PI * 2f;

    // ---- plating -------------------------------------------------------------------------------------

    /// <summary>
    /// Hull plating: <paramref name="rows"/> rows of panels of uneven width, groove seams with a worn bevel, rivets along every panel edge,
    /// some inset hatches and louvres, edge dirt, a few scratches. Brightness stays near 0.9 so the vertex colour decides the hue.
    /// </summary>
    public static Surf HullPlating(int n, int rows, int seed, float rivet)
    {
        var s = new Surf(n);
        int cell = n / rows;
        // per row: 2..3 panels, cut positions jittered, the whole row shifted
        var cuts = new float[rows][];
        var cols = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            int c = rows >= 4 ? 2 + (int)(Tn.Hash(r, 1, seed) * 1.99f) : 1 + (int)(Tn.Hash(r, 1, seed) * 1.99f);
            cols[r] = c;
            cuts[r] = new float[c + 1];
            float w = n / (float)c, off = Tn.Hash(r, 2, seed) * w;
            for (int i = 0; i < c; i++) cuts[r][i] = off + (i + (Tn.Hash(r, 10 + i, seed) - 0.5f) * (c > 1 ? 0.4f : 0f)) * w;
            cuts[r][c] = cuts[r][0] + n;
        }
        float rr = rivet;
        s.Each((x, y, u, v) =>
        {
            int r = y / cell, ly = y - r * cell;
            float dY = MathF.Min(ly, cell - 1 - ly);
            int c = cols[r];
            float xr = x;
            // find the panel of this row that contains x (cuts wrap once)
            int idx = 0;
            float xs = xr;
            while (xs < cuts[r][0]) xs += n;
            for (int i = 0; i < c; i++) if (xs >= cuts[r][i] && xs < cuts[r][i + 1]) { idx = i; break; }
            float x0 = cuts[r][idx], x1 = cuts[r][idx + 1];
            float lx = xs - x0, pw = x1 - x0;
            float dX = MathF.Min(lx, pw - 1 - lx);
            float d = MathF.Min(dX, dY);
            int i0 = s.Ix(x, y);
            float pid = Tn.Hash(r, idx, seed + 3);
            float tone = pid * 2f - 1f;
            // groove and bevel
            float groove = 1f - Tn.Smooth(0f, 5f, d);
            float h = -0.8f * groove + pid * 0.12f;
            float albedo = 0.90f * (1f + 0.07f * tone) * (1f - 0.55f * (1f - Tn.Smooth(0f, 3.2f, d)));
            float rough = 0.80f + 0.08f * tone;
            float metal = 0.45f;
            // large soft dirt and edge dirt
            float dirt = Tn.Fbm(u, v, 5, 5, 4, seed + 40);
            albedo *= 1f - 0.16f * Tn.Smooth(0.45f, 0.85f, dirt) - 0.10f * MathF.Exp(-d / 12f);
            rough += 0.12f * Tn.Smooth(0.5f, 0.9f, dirt);
            // worn bevel: bare bright metal on the lip of the seam
            float lip = Tn.Smooth(3.5f, 5f, d) * (1f - Tn.Smooth(7f, 10f, d));
            albedo += 0.07f * lip; rough -= 0.28f * lip; metal += 0.5f * lip;
            // fine grain
            float grain = Tn.Vn(x * 0.5f, y * 0.5f, n / 2, n / 2, seed + 5) - 0.5f;
            albedo *= 1f + 0.05f * grain; h += 0.03f * grain;
            // rivets along the panel border
            float ph = cell, spx = pw / MathF.Round(pw / (rr * 11f)), spy = ph / MathF.Round(ph / (rr * 11f));
            float inset = rr * 3f;
            float ex = Math.Clamp(MathF.Round((lx - inset) / spx) * spx + inset, inset, pw - 1 - inset);
            float ey = ly < ph / 2 ? inset : ph - 1 - inset;
            float d1 = MathF.Sqrt((lx - ex) * (lx - ex) + (ly - ey) * (ly - ey));
            float ey2 = Math.Clamp(MathF.Round((ly - inset) / spy) * spy + inset, inset, ph - 1 - inset);
            float ex2 = lx < pw / 2 ? inset : pw - 1 - inset;
            float d2 = MathF.Sqrt((lx - ex2) * (lx - ex2) + (ly - ey2) * (ly - ey2));
            float rd = MathF.Min(d1, d2);
            if (rd < rr)
            {
                float k = 1f - rd / rr;
                h += 0.55f * MathF.Sqrt(k); albedo *= 1.06f; rough = 0.45f; metal = 1f;
            }
            else if (rd < rr + 1.6f) { albedo *= 0.8f; h -= 0.1f; } // dark ring around the head
            // some panels carry an inset hatch (a groove rectangle) or louvres
            float hatch = Tn.Hash(r, idx, seed + 9);
            if (hatch < 0.32f)
            {
                float ix = lx - pw * 0.18f, iy = ly - ph * 0.22f, iw = pw * 0.64f, ih = ph * 0.56f;
                if (ix > 0 && iy > 0 && ix < iw && iy < ih)
                {
                    float e = MathF.Min(MathF.Min(ix, iw - ix), MathF.Min(iy, ih - iy));
                    if (e < 2f) { h -= 0.5f; albedo *= 0.6f; }
                    else if (hatch < 0.14f && ((int)(iy / 6f) & 1) == 0 && e > 6f) { h -= 0.45f; albedo *= 0.5f; } // louvre slits
                    else albedo *= 0.96f;
                }
            }
            s.H[i0] = h; s.Ro[i0] = Math.Clamp(rough, 0.2f, 1f); s.Me[i0] = Math.Clamp(metal, 0f, 1f);
            s.R[i0] = albedo * 1.00f; s.G[i0] = albedo * 1.01f; s.B[i0] = albedo * 1.03f;
        });
        var rng = new Models.Rng(seed + 77);
        for (int i = 0; i < n / 5; i++)
        {
            float x = rng.F() * n, y = rng.F() * n, a = rng.F() * Tau, len = 14 + rng.F() * 70;
            s.Line(x, y, x + MathF.Cos(a) * len, y + MathF.Sin(a) * len, 1.06f, -0.18f, 0.25f, -0.03f);
        }
        return s;
    }

    /// <summary>Rust over plating (wrecks, debris): blotchy orange-brown with deep pits, bare steel in the gaps.</summary>
    public static Surf RustedPlating(int n, int seed)
    {
        var s = HullPlating(n, 2, seed, 3.2f);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float m = Tn.Fbm(u, v, 4, 4, 5, seed + 60);
            float warp = Tn.Fbm(u, v, 9, 9, 3, seed + 61);
            float rust = Tn.Smooth(0.42f, 0.62f, m * 0.75f + warp * 0.25f);
            float pit = Tn.Vor(u, v, 22, seed + 62).F1;
            float pits = (1f - Tn.Smooth(0.08f, 0.26f, pit)) * rust;
            // colours: rusty orange-brown vs weathered steel
            float rr = 0.62f + 0.18f * warp, rg = 0.30f + 0.10f * warp, rb = 0.15f;
            float sr = s.R[i] * 0.80f, sg = s.G[i] * 0.84f, sb = s.B[i] * 0.88f;
            s.R[i] = sr + (rr - sr) * rust; s.G[i] = sg + (rg - sg) * rust; s.B[i] = sb + (rb - sb) * rust;
            s.R[i] *= 1f - 0.35f * pits; s.G[i] *= 1f - 0.4f * pits; s.B[i] *= 1f - 0.4f * pits;
            s.Ro[i] = s.Ro[i] + (1f - s.Ro[i]) * rust * 0.9f;
            s.Me[i] *= 1f - 0.9f * rust;
            s.H[i] += -0.5f * pits - 0.2f * rust * m + 0.15f * Tn.Fbm(u, v, 24, 24, 2, seed + 63) * rust;
        });
        return s;
    }

    /// <summary>Dark structural metal: brushed streaks along u, bolt heads in rows, a pair of long grooves.</summary>
    public static Surf Structural(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float brush = Tn.Fbm(u, v, 3, 96, 3, seed) - 0.5f;
            float streak = Tn.Fbm(u, v, 2, 24, 2, seed + 4);
            float a = 0.86f + 0.16f * brush + 0.06f * (streak - 0.5f);
            float h = 0.10f * brush;
            float rough = 0.72f + 0.35f * brush;
            float metal = 0.85f;
            // two long grooves
            float g1 = MathF.Abs(v - 0.25f) * n, g2 = MathF.Abs(v - 0.75f) * n;
            float g = MathF.Min(g1, g2);
            if (g < 2.2f) { h -= 0.6f; a *= 0.6f; }
            else if (g < 6f) { a *= 1f + 0.05f * (1f - (g - 2.2f) / 3.8f); }
            // bolt heads in two rows
            float bx = (u * 8f) - MathF.Floor(u * 8f) - 0.5f, by = MathF.Min(MathF.Abs(v - 0.5f), 0.5f) ;
            float bd = MathF.Sqrt(bx * bx * (n / 8f) * (n / 8f) + (by * n) * (by * n));
            if (by < 0.5f && MathF.Abs(v - 0.5f) < 0.02f && bd < n / 90f) { h += 0.5f; a *= 1.12f; rough = 0.4f; }
            // wear: light scuffs
            float scuff = Tn.Fbm(u, v, 14, 14, 3, seed + 9);
            a *= 1f - 0.10f * Tn.Smooth(0.55f, 0.85f, scuff);
            s.H[i] = h; s.Ro[i] = Math.Clamp(rough, 0.2f, 1f); s.Me[i] = metal;
            s.Set(i, a, a * 1.01f, a * 1.04f);
        });
        return s;
    }

    /// <summary>Burnt metal for engine nozzles and weapon barrels: soot, heat bands (gold and blue), rings.</summary>
    public static Surf Burnt(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float m = Tn.Fbm(u, v, 3, 3, 5, seed);
            float bands = 0.5f + 0.5f * MathF.Sin((v * 5f + m * 1.4f) * Tau);
            float soot = Tn.Smooth(0.35f, 0.8f, m);
            float a = 0.62f + 0.3f * (1f - soot);
            float r = a, g = a, b = a;
            // heat tint: straw yellow then blue along the bands
            float heat = Tn.Smooth(0.7f, 1f, bands) * (1f - soot * 0.6f);
            r += 0.20f * heat; g += 0.10f * heat; b -= 0.18f * heat;
            float blue = Tn.Smooth(0.0f, 0.25f, bands) * (1f - Tn.Smooth(0.25f, 0.5f, bands)) * 0.5f;
            b += 0.18f * blue; r -= 0.06f * blue;
            float ring = MathF.Abs(((v * 16f) % 1f) - 0.5f);
            float h = 0.12f * m;
            if (ring > 0.46f) { h -= 0.45f; a *= 0.7f; r *= 0.7f; g *= 0.7f; b *= 0.7f; }
            s.H[i] = h; s.Ro[i] = 0.55f + 0.4f * soot; s.Me[i] = 0.9f - 0.4f * soot;
            s.Set(i, r, g, b);
        });
        return s;
    }

    /// <summary>Cockpit glass: nearly clear, diagonal sheen bands and smudges (a texture, not a reflection).</summary>
    public static Surf Glass(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float band = 0.5f + 0.5f * MathF.Sin((u + v) * Tau * 2f + Tn.Fbm(u, v, 3, 3, 2, seed) * 2f);
            float sheen = Tn.Smooth(0.82f, 1f, band);
            float smudge = Tn.Fbm(u, v, 6, 6, 4, seed + 3);
            float a = 0.78f + 0.22f * sheen - 0.18f * Tn.Smooth(0.45f, 0.9f, smudge);
            s.H[i] = 0.04f * smudge;
            s.Ro[i] = 0.45f + 0.55f * Tn.Smooth(0.45f, 0.9f, smudge);
            s.Me[i] = 1f;
            s.Set(i, a, a, a * 1.02f);
        });
        var rng = new Models.Rng(seed + 5);
        for (int i = 0; i < 18; i++)
        {
            float x = rng.F() * n, y = rng.F() * n, a = rng.F() * Tau, len = 10 + rng.F() * 40;
            s.Line(x, y, x + MathF.Cos(a) * len, y + MathF.Sin(a) * len, 1.0f, -0.3f, 0f, -0.02f);
        }
        return s;
    }

    // ---- light mask textures -------------------------------------------------------------------------

    /// <summary>Lit panel: bright, with a soft hot centre, fine cell lines and a little noise so a lamp or window strip is not one flat colour.</summary>
    public static Surf LightPanel(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float nz = Tn.Fbm(u, v, 4, 4, 3, seed);
            float line = MathF.Min(MathF.Abs(((u * 4f) % 1f) - 0.5f), MathF.Abs(((v * 4f) % 1f) - 0.5f));
            float e = 0.66f + 0.34f * nz;
            if (line > 0.455f) e *= 0.4f;
            s.Em[i] = e;
            float a = 0.9f - 0.1f * (1f - nz);
            s.Set(i, a, a, a);
            s.Ro[i] = 0.7f; s.Me[i] = 0.1f;
            s.H[i] = line > 0.455f ? -0.3f : 0f;
        });
        return s;
    }

    /// <summary>Vent grill: slats across v with a dark frame; emission only in the gaps.</summary>
    public static Surf Grill(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float slat = ((v * 8f) % 1f);
            bool bar = slat < 0.38f;
            float shade = bar ? 0.0f : 1f;
            s.Em[i] = bar ? 0.12f : 0.7f + 0.3f * Tn.Vn(u * 6f, v * 6f, 6, 6, seed);
            s.Set(i, bar ? 0.45f : 0.85f, bar ? 0.45f : 0.85f, bar ? 0.47f : 0.85f);
            s.Ro[i] = bar ? 0.6f : 0.4f; s.Me[i] = bar ? 0.9f : 0.2f;
            s.H[i] = bar ? 0.5f * MathF.Sin(slat / 0.38f * MathF.PI) : -0.3f;
            _ = shade;
        });
        return s;
    }

    // ---- natural -------------------------------------------------------------------------------------

    /// <summary>Rock: lumpy height, craters with raised rims, small pits and cracks, mottled grey with lighter peaks and dark cavities.</summary>
    public static Surf Rock(int n, int seed)
    {
        var s = new Surf(n);
        var rng = new Models.Rng(seed);
        const int Craters = 14;
        var cx = new float[Craters]; var cy = new float[Craters]; var cr = new float[Craters]; var cd = new float[Craters];
        for (int i = 0; i < Craters; i++) { cx[i] = rng.F() * n; cy[i] = rng.F() * n; cr[i] = n * (0.05f + rng.F() * 0.10f); cd[i] = 0.5f + rng.F() * 0.5f; }
        float half = n / 2f;
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float warp = Tn.Fbm(u, v, 3, 3, 3, seed + 20);
            float h = Tn.Fbm(u + warp * 0.04f, v, 3, 3, 6, seed) * 1.4f;
            float cav = 0f;
            for (int k = 0; k < Craters; k++)
            {
                float dx = MathF.Abs(x - cx[k]); if (dx > half) dx = n - dx;
                float dy = MathF.Abs(y - cy[k]); if (dy > half) dy = n - dy;
                float t = MathF.Sqrt(dx * dx + dy * dy) / cr[k];
                if (t < 1f) { float bowl = (1f - t * t); h -= cd[k] * 0.9f * bowl; cav = MathF.Max(cav, bowl * cd[k]); }
                else if (t < 1.25f) h += cd[k] * 0.22f * (1f - MathF.Abs(t - 1.08f) / 0.17f);
            }
            var (f1, f2, id) = Tn.Vor(u + 0.05f * (warp - 0.5f), v + 0.05f * (warp - 0.5f), 26, seed + 31);
            float pit = 1f - Tn.Smooth(0.05f, 0.22f, f1);
            if (id > 0.8f) { h -= pit * 0.5f; cav = MathF.Max(cav, pit * 0.8f); }
            var (g1, g2, gid) = Tn.Vor(u + 0.12f * (warp - 0.5f), v + 0.12f * (warp - 0.5f), 7, seed + 41);
            float crack = (g2 - g1) < 0.03f && gid > 0.7f ? 1f : 0f;
            h -= crack * 0.5f; cav = MathF.Max(cav, crack * 0.7f);
            float mott = Tn.Fbm(u, v, 5, 5, 5, seed + 50);
            float streak = 0.5f + 0.5f * MathF.Sin((u * 2f + v * 0.7f + warp * 0.9f) * Tau * 3f);
            float a = 0.86f * (0.80f + 0.34f * mott) * (0.82f + 0.26f * Tn.Smooth(-0.2f, 1f, h));
            a *= 1f + 0.10f * Tn.Smooth(0.8f, 1f, streak);
            a *= 1f - 0.45f * cav;
            float tint = Tn.Fbm(u, v, 3, 3, 2, seed + 55) - 0.5f;
            s.H[i] = h;
            s.Ro[i] = 0.88f + 0.12f * mott;
            s.Me[i] = 0f;
            s.Set(i, a * (1f + 0.10f * tint), a, a * (1f - 0.10f * tint));
        });
        return s;
    }

    /// <summary>Ice: pale blue-white, fracture lines between big cells, frosted patches, clear glassy gaps.</summary>
    public static Surf Ice(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            var (f1, f2, id) = Tn.Vor(u, v, 8, seed);
            var (g1, g2, gid) = Tn.Vor(u + 0.31f, v + 0.17f, 15, seed + 9);
            float e = f2 - f1, e2 = g2 - g1;
            float crack = (1f - Tn.Smooth(0.0f, 0.06f, e)) + 0.6f * (1f - Tn.Smooth(0f, 0.04f, e2));
            crack = Math.Clamp(crack, 0f, 1f);
            float frost = Tn.Smooth(0.5f, 0.85f, Tn.Fbm(u, v, 6, 6, 5, seed + 12));
            float a = 0.90f + 0.06f * (id - 0.5f) + 0.05f * frost - 0.30f * crack;
            s.H[i] = -0.5f * crack + 0.08f * id + 0.1f * frost * Tn.Fbm(u, v, 40, 40, 1, seed + 3);
            s.Ro[i] = 0.35f + 0.55f * frost;
            s.Me[i] = 0f;
            s.Set(i, a * 0.90f, a * 0.97f, a * 1.0f);
        });
        return s;
    }

    /// <summary>Ore: dark mottled rock with lighter veins and bright metallic flecks.</summary>
    public static Surf Ore(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float m = Tn.Fbm(u, v, 5, 5, 5, seed);
            float warp = Tn.Fbm(u, v, 4, 4, 3, seed + 3);
            float vein = Tn.Smooth(0.86f, 1f, 0.5f + 0.5f * MathF.Sin((u * 1.0f + v * 2.0f + warp * 1.6f) * Tau * 2f));
            float fleck = Tn.Vn(x * 0.35f, y * 0.35f, (int)(n * 0.35f), (int)(n * 0.35f), seed + 8);
            float fl = Tn.Smooth(0.74f, 0.8f, fleck);
            var (f1, f2, id) = Tn.Vor(u, v, 14, seed + 12);
            float pit = 1f - Tn.Smooth(0.05f, 0.25f, f1);
            float a = 0.6f + 0.36f * m + 0.28f * vein + 0.26f * fl - 0.32f * pit * (id > 0.4f ? 1f : 0f);
            s.H[i] = 0.9f * m - 0.5f * pit * (id > 0.4f ? 1f : 0f) + 0.15f * vein;
            s.Ro[i] = 0.85f - 0.55f * fl - 0.25f * vein;
            s.Me[i] = Math.Clamp(0.55f + 0.5f * fl + 0.25f * vein, 0f, 1f);
            s.Set(i, a, a, a);
        });
        return s;
    }

    /// <summary>Crystal: faceted cells with bright edges and cloudy interior; also an emission mask of glowing veins for unstable crystals.</summary>
    public static Surf Crystal(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            var (f1, f2, id) = Tn.Vor(u, v, 6, seed);
            float edge = 1f - Tn.Smooth(0f, 0.08f, f2 - f1);
            float cloud = Tn.Fbm(u, v, 4, 4, 4, seed + 4);
            float core = Tn.Smooth(0.35f, 0.9f, cloud);
            float a = 0.80f + 0.14f * (id - 0.5f) * 2f + 0.14f * edge - 0.14f * core;
            s.H[i] = 0.35f * id - 0.25f * edge;
            s.Ro[i] = 0.3f + 0.4f * core;
            s.Me[i] = 0.1f;
            s.Em[i] = 0.55f + 0.45f * Math.Clamp(edge + core * 0.7f, 0f, 1f);
            s.Set(i, a, a, a);
        });
        return s;
    }

    // ---- logistics -----------------------------------------------------------------------------------

    /// <summary>
    /// Conveyor running surface, flow along +u: dark rubber with transverse cleats and lighter chevrons pointing along +u, worn edges.
    /// One tile is one cell.
    /// </summary>
    public static Surf Belt(int n, int seed)
    {
        var s = new Surf(n);
        s.Each((x, y, u, v) =>
        {
            int i = s.Ix(x, y);
            float cleat = 0.5f + 0.5f * MathF.Cos(u * 4f * Tau);
            float a = 0.55f + 0.06f * (1f - cleat);
            float h = 0.18f * cleat;
            // chevrons: two per tile, tips towards +u
            float dv = MathF.Abs(v - 0.5f);
            float chev = 0f;
            for (int k = 0; k < 2; k++)
            {
                float tip = 0.34f + 0.5f * k;
                float line = tip - dv * 0.55f;
                float du = u - line; du -= MathF.Round(du);
                float w = 0.045f;
                if (MathF.Abs(du) < w && dv < 0.42f) chev = MathF.Max(chev, 1f - MathF.Abs(du) / w);
            }
            a = a * (1f - chev) + 1.0f * chev;
            h += 0.25f * chev;
            float wear = Tn.Fbm(u, v, 6, 6, 4, seed);
            a *= 0.85f + 0.2f * wear;
            float edge = Tn.Smooth(0.0f, 0.12f, MathF.Min(v, 1f - v));
            a *= 0.7f + 0.3f * edge;
            s.H[i] = h;
            s.Ro[i] = 0.9f - 0.3f * chev;
            s.Me[i] = 0.3f * chev;
            s.Set(i, a, a, a);
        });
        return s;
    }
}
