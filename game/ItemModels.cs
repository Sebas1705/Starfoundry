using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The 3D look of every item (belts, dropped stacks, the inventory preview). Each item kind has its own silhouette and material family
/// (ore chunks per ore, bars, plates, rods, gears, circuit boards, canisters, flasks, crystals, glass...) and <see cref="Variants"/> looks per
/// shape, so a pile never repeats one object. Meshes are procedural (no external file), built once, shared, flat shaded and about one unit
/// wide, standing on y = 0 with their long axis along +X: scale them (a belt uses <see cref="BeltScale"/>). Vertex colour carries brightness
/// only; the item tint comes through the instance colour (MultiMesh), so one mesh serves every item that shares a shape. A shape may have a
/// second surface: glass, glow (emission follows the tint) or plastic.
/// </summary>
public static class ItemModels
{
    public enum Shape
    {
        Ingot, Plate, OreAngular, OreShard, OreNodule, Coal, Crystal, IceChunk, Cell, Circuit, Canister, Jug, Pod, Flask,
        Gear, Atom, Shell, Rods, Wafer, Slab, Billet, Resonant, Lens, Crate,
    }

    /// <summary>The material family of a surface.</summary>
    public enum Look { Metal, Ore, Coal, Crystal, Glass, Plastic, Glow }

    public const int Variants = 3;
    public static readonly int ShapeCount = Enum.GetValues<Shape>().Length;

    /// <summary>Layer index of a shape and variant (one MultiMesh per layer in <see cref="ItemLayers"/>).</summary>
    public static int Layer(Shape shape, int variant) => (int)shape * Variants + Math.Abs(variant) % Variants;

    public static int Layers => ShapeCount * Variants;

    /// <summary>The scale a belt gives an item (a belt is 0.9 cells wide).</summary>
    public const float BeltScale = 0.30f;

    /// <summary>The scale of an item lying about (a bit bigger than on a belt so it can be read from a step away).</summary>
    public const float GroundScale = 0.42f;

    /// <summary>The shape and tint of an item, by content id. Unknown items are crates.</summary>
    public static (Shape Shape, Color Tint) StyleOf(string id) => id switch
    {
        "core:iron_ore" => (Shape.OreAngular, new Color(0.84f, 0.48f, 0.32f)),
        "core:titanium_ore" => (Shape.OreShard, new Color(0.60f, 0.68f, 0.88f)),
        "core:nickel_ore" => (Shape.OreNodule, new Color(0.52f, 0.80f, 0.62f)),
        "core:carbon" => (Shape.Coal, new Color(0.36f, 0.36f, 0.42f)),
        "core:quartz" => (Shape.Crystal, new Color(1f, 0.82f, 0.94f)),
        "core:ice" => (Shape.IceChunk, new Color(0.66f, 0.94f, 1f)),
        "core:iron_ingot" => (Shape.Ingot, new Color(0.92f, 0.64f, 0.50f)),
        "core:titanium_ingot" => (Shape.Ingot, new Color(0.68f, 0.80f, 1f)),
        "core:nickel_ingot" => (Shape.Ingot, new Color(0.78f, 0.94f, 0.78f)),
        "core:silicon" => (Shape.Wafer, new Color(0.50f, 0.56f, 0.88f)),
        "core:iron_plate" => (Shape.Plate, new Color(0.80f, 0.82f, 0.90f)),
        "core:titanium_plate" => (Shape.Plate, new Color(0.62f, 0.74f, 1f)),
        "core:nickel_plate" => (Shape.Plate, new Color(0.66f, 0.90f, 0.80f)),
        "core:titanium_alloy" => (Shape.Billet, new Color(0.80f, 0.55f, 1f)),
        "core:circuit" => (Shape.Circuit, new Color(0.30f, 0.90f, 0.46f)),
        "core:fuel_cell" => (Shape.Cell, new Color(1f, 0.62f, 0.14f)),
        "core:ammo" => (Shape.Shell, new Color(1f, 0.86f, 0.28f)),
        "core:hydrogen" => (Shape.Canister, new Color(0.64f, 0.92f, 1f)),
        "core:biomass" => (Shape.Pod, new Color(0.46f, 0.88f, 0.36f)),
        "core:science_engineering" => (Shape.Gear, new Color(1f, 0.56f, 0.16f)),
        "core:science_biology" => (Shape.Flask, new Color(0.32f, 0.96f, 0.48f)),
        "core:science_physics" => (Shape.Atom, new Color(0.36f, 0.86f, 1f)),
        "core:carbon_fiber" => (Shape.Rods, new Color(0.38f, 0.42f, 0.54f)),
        "core:composite_plate" => (Shape.Slab, new Color(0.50f, 0.56f, 0.74f)),
        "core:coolant" => (Shape.Jug, new Color(0.30f, 0.90f, 1f)),
        "core:resonant_crystal" => (Shape.Resonant, new Color(0.77f, 0.55f, 1f)),
        "core:focus_lens" => (Shape.Lens, new Color(0.89f, 0.77f, 1f)),
        _ => (Shape.Crate, new Color(0.78f, 0.70f, 0.52f)),
    };

    /// <summary>Material family of the main surface and of the optional second one.</summary>
    public static (Look Main, Look? Accent) LooksOf(Shape s) => s switch
    {
        Shape.OreAngular or Shape.OreShard or Shape.OreNodule => (Look.Ore, null),
        Shape.Coal => (Look.Coal, null),
        Shape.Crystal => (Look.Crystal, null),
        Shape.IceChunk => (Look.Glass, null),
        Shape.Cell => (Look.Metal, Look.Glow),
        Shape.Circuit => (Look.Plastic, Look.Metal),
        Shape.Canister => (Look.Metal, Look.Plastic),
        Shape.Jug => (Look.Glass, Look.Glow),
        Shape.Pod => (Look.Plastic, Look.Glow),
        Shape.Flask => (Look.Glass, Look.Glow),
        Shape.Gear => (Look.Metal, Look.Glow),
        Shape.Atom => (Look.Metal, Look.Glow),
        Shape.Rods => (Look.Plastic, Look.Metal),
        Shape.Slab => (Look.Plastic, Look.Metal),
        Shape.Billet => (Look.Metal, Look.Glow),
        Shape.Resonant => (Look.Crystal, Look.Glow),
        Shape.Lens => (Look.Glass, Look.Metal),
        Shape.Crate => (Look.Plastic, Look.Metal),
        _ => (Look.Metal, null),
    };

    public static StandardMaterial3D MaterialOf(Look look) => look switch
    {
        Look.Ore => Materials.Ore(0.8f, 0.5f),
        Look.Coal => Materials.Ore(0.95f, 0.05f),
        Look.Crystal => Materials.Crystal(0.3f, 0.1f),
        Look.Glass => Materials.ItemGlass(),
        Look.Plastic => Materials.ItemPlastic(),
        Look.Glow => Materials.ItemGlow(),
        _ => Materials.ItemMetal(),
    };

    /// <summary>Height of one piece when a pile stacks flat things (ingots, plates...) in layers; 0 for shapes that sit side by side.</summary>
    public static float StackHeight(Shape s) => s switch
    {
        Shape.Ingot => 0.40f, Shape.Plate => 0.17f, Shape.Slab => 0.20f, Shape.Wafer => 0.30f, Shape.Circuit => 0.16f, _ => 0f,
    };

    /// <summary>The variant a belt shows for an item: stable for a segment and an item (an item must not change look while it moves).</summary>
    public static int VariantFor(int segment, int item) => (int)(Mix((uint)segment * 7919u + (uint)item * 104729u) % Variants);

    private static uint Mix(uint h)
    {
        h = (h ^ (h >> 16)) * 0x45D9F3Bu;
        h = (h ^ (h >> 16)) * 0x45D9F3Bu;
        return h ^ (h >> 16);
    }

    // ---- mesh cache ---------------------------------------------------------------------------------------

    private static readonly Dictionary<int, ArrayMesh> Cache = new();

    /// <summary>The shared mesh of a shape and variant (built on first use).</summary>
    public static ArrayMesh Mesh(Shape shape, int variant)
    {
        variant = Math.Abs(variant) % Variants;
        int key = (int)shape * Variants + variant;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var (mainLook, accentLook) = LooksOf(shape);
        var m = new Models.Mb();
        var a = new Models.Mb();
        m.At(default); a.At(default);
        Build(shape, variant, m, a);
        var mesh = m.ToMesh(MaterialOf(mainLook));
        if (accentLook is { } al) a.AddSurface(mesh, MaterialOf(al));
        mesh.ResourceName = $"item.{shape}.{variant}";
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>Triangles of a shape's variant (both surfaces), for the budget report.</summary>
    public static int Triangles(Shape shape, int variant)
    {
        int n = 0;
        var mesh = Mesh(shape, variant);
        for (int s = 0; s < mesh.GetSurfaceCount(); s++) n += mesh.SurfaceGetArrays(s)[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
        return n;
    }

    // ---- building blocks ----------------------------------------------------------------------------------

    private static Color G(float v) => new(v, v, v);
    private static Color Gl(float v, float alpha) => new(v, v, v, alpha);

    private static readonly Vector3[] IcoV = MakeIco();

    private static readonly int[][] IcoF =
    {
        new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 }, new[] { 1, 5, 9 }, new[] { 5, 11, 4 },
        new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 }, new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 },
        new[] { 3, 8, 9 }, new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
    };

    private static Vector3[] MakeIco()
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new[]
        {
            new Vector3(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i].Normalized();
        return v;
    }

    /// <summary>
    /// A lump of rock or ice: an icosphere (20 faces, or 80 when <paramref name="fine"/>) pushed in and out by hashed noise, shaved by
    /// <paramref name="cuts"/> flat planes and flattened on the ground (y never below 0), so it sits on a belt instead of floating.
    /// </summary>
    private static void Rock(Models.Mb mb, Vector3 c, Vector3 r, int seed, float noise, int cuts, bool fine, float cutDepth = 0.72f)
    {
        var rng = new Models.Rng(seed * 31 + 7);
        var planes = new (Vector3 N, float D)[cuts];
        for (int i = 0; i < cuts; i++)
        {
            var n = new Vector3(rng.F() - 0.5f, (rng.F() - 0.5f) * 0.8f, rng.F() - 0.5f);
            planes[i] = (n.LengthSquared() < 1e-4f ? Vector3.Right : n.Normalized(), cutDepth + 0.12f * rng.F());
        }

        Vector3 P(Vector3 u)
        {
            u = u.Normalized();
            float k = 1f + noise * (Models.Hash3((int)MathF.Round(u.X * 40f), (int)MathF.Round(u.Y * 40f), (int)MathF.Round(u.Z * 40f), seed) - 0.5f) * 2f;
            var p = u * k;
            foreach (var (n, d) in planes)
            {
                float dp = p.Dot(n);
                if (dp > d) p -= n * (dp - d);
            }
            p = new Vector3(p.X * r.X, p.Y * r.Y, p.Z * r.Z) + c;
            if (p.Y < 0f) p.Y = 0f;
            return p;
        }

        foreach (var f in IcoF)
        {
            Vector3 a = IcoV[f[0]], b = IcoV[f[1]], d = IcoV[f[2]];
            if (!fine) { mb.Tri(P(a), P(b), P(d), c); continue; }
            Vector3 ab = (a + b) / 2f, bd = (b + d) / 2f, da = (d + a) / 2f;
            mb.Tri(P(a), P(ab), P(da), c); mb.Tri(P(ab), P(b), P(bd), c); mb.Tri(P(da), P(bd), P(d), c); mb.Tri(P(ab), P(bd), P(da), c);
        }
    }

    /// <summary>A pointed prism (a crystal or a shard) standing on <paramref name="at"/>, leaning by (tiltX, tiltZ) degrees and turned by <paramref name="turn"/>.</summary>
    private static void Spike(Models.Mb mb, Vector3 at, float r, float h, float tiltX, float tiltZ, int sides, float turn = 0f, float tipShare = 0.28f)
    {
        mb.At(at, new Vector3(Mathf.DegToRad(tiltX), Mathf.DegToRad(turn), Mathf.DegToRad(tiltZ)));
        mb.Prism(default, r, r * 0.92f, h * (1f - tipShare), sides);
        mb.Prism(new Vector3(0, h * (1f - tipShare), 0), r * 0.92f, 0f, h * tipShare, sides);
        mb.At(default);
    }

    /// <summary>Spur gear lying in the XZ plane between two heights: <paramref name="teeth"/> trapezoid teeth round a root radius.</summary>
    private static void GearDisc(Models.Mb mb, Vector3 c, float ro, float ri, int teeth, float y0, float y1, float turn = 0f)
    {
        var ring = new List<Vector3>();
        float w = Mathf.Tau / teeth;
        for (int i = 0; i < teeth; i++)
        {
            float a = turn + w * i;
            foreach (var (da, rad) in new[] { (0.02f, ri), (0.16f, ro), (0.42f, ro), (0.56f, ri) })
            {
                float ang = a + w * da;
                ring.Add(new Vector3(c.X + MathF.Cos(ang) * rad, 0f, c.Z + MathF.Sin(ang) * rad));
            }
        }
        var top = new Vector3(c.X, y1, c.Z);
        var mid = new Vector3(c.X, (y0 + y1) / 2f, c.Z);
        for (int i = 0; i < ring.Count; i++)
        {
            var p = ring[i]; var q = ring[(i + 1) % ring.Count];
            var pt = new Vector3(p.X, y1, p.Z); var qt = new Vector3(q.X, y1, q.Z);
            var pb = new Vector3(p.X, y0, p.Z); var qb = new Vector3(q.X, y0, q.Z);
            mb.Tri(top, pt, qt, mid);
            mb.Quad(pb, qb, qt, pt, mid);
        }
    }

    /// <summary>A cylinder lying along X from -len/2 to +len/2 at height y, z offset zc.</summary>
    private static void CylX(Models.Mb mb, float x0, float len, float y, float zc, float r, int sides, float rTop = -1f)
    {
        mb.At(new Vector3(x0 + len, y, zc), new Vector3(0, 0, Mathf.Pi / 2f));
        mb.Prism(default, r, rTop < 0 ? r : rTop, len, sides);
        mb.At(default);
    }

    // ---- the shapes ---------------------------------------------------------------------------------------

    private static void Build(Shape shape, int v, Models.Mb m, Models.Mb a)
    {
        switch (shape)
        {
            case Shape.Ingot: Ingot(v, m); break;
            case Shape.Plate: PlateShape(v, m); break;
            case Shape.OreAngular:
                if (v == 0) { m.C(G(0.9f)); Rock(m, new(0, 0.30f, 0), new(0.46f, 0.34f, 0.40f), 11, 0.16f, 3, true); m.C(G(0.68f)); Rock(m, new(0.36f, 0.12f, 0.24f), new(0.2f, 0.14f, 0.18f), 12, 0.2f, 1, false); m.C(G(0.8f)); Rock(m, new(-0.32f, 0.1f, -0.26f), new(0.17f, 0.12f, 0.15f), 13, 0.2f, 1, false); }
                else if (v == 1) { m.C(G(0.86f)); Rock(m, new(-0.08f, 0.26f, 0), new(0.5f, 0.28f, 0.34f), 21, 0.12f, 4, true); m.C(G(0.62f)); Rock(m, new(0.4f, 0.1f, -0.2f), new(0.2f, 0.13f, 0.17f), 22, 0.2f, 1, false); }
                else { m.C(G(0.92f)); Rock(m, new(0.2f, 0.26f, 0.1f), new(0.34f, 0.3f, 0.3f), 31, 0.18f, 2, false); m.C(G(0.74f)); Rock(m, new(-0.26f, 0.22f, -0.08f), new(0.3f, 0.24f, 0.28f), 32, 0.18f, 2, false); m.C(G(0.6f)); Rock(m, new(0.04f, 0.09f, 0.36f), new(0.16f, 0.1f, 0.14f), 33, 0.2f, 1, false); }
                break;
            case Shape.OreShard:
                m.C(G(0.7f)); Rock(m, new(0, 0.05f, 0), new(0.46f, 0.12f, 0.38f), 41 + v, 0.2f, 2, false);
                if (v == 0) { m.C(G(1f)); Spike(m, new(0, 0.04f, 0), 0.17f, 0.88f, 4, 3, 5); m.C(G(0.82f)); Spike(m, new(0.24f, 0.1f, 0.1f), 0.13f, 0.58f, 24, -16, 5, 20); m.C(G(0.9f)); Spike(m, new(-0.22f, 0.1f, -0.12f), 0.12f, 0.5f, -22, 20, 5, 50); }
                else if (v == 1) { m.C(G(1f)); Spike(m, new(-0.12f, 0.04f, 0), 0.15f, 0.72f, 10, 28, 5, 10); m.C(G(0.85f)); Spike(m, new(0.16f, 0.06f, 0.08f), 0.14f, 0.64f, -8, -30, 5, 40); m.C(G(0.7f)); Spike(m, new(0.02f, 0.05f, -0.2f), 0.1f, 0.4f, -26, 4, 5, 0); }
                else { m.C(G(0.95f)); Spike(m, new(0, 0.04f, 0), 0.2f, 0.62f, 0, 0, 5, 8); m.C(G(0.78f)); Spike(m, new(0.28f, 0.08f, -0.06f), 0.11f, 0.5f, 20, -34, 5, 30); m.C(G(0.86f)); Spike(m, new(-0.26f, 0.08f, 0.08f), 0.12f, 0.55f, -18, 30, 5, 0); m.C(G(0.7f)); Spike(m, new(0.02f, 0.08f, 0.26f), 0.1f, 0.42f, 28, 0, 5, 60); }
                break;
            case Shape.OreNodule:
                if (v == 0) { m.C(G(0.95f)); Rock(m, new(0, 0.28f, 0), new(0.3f, 0.28f, 0.3f), 51, 0.06f, 0, true); m.C(G(0.8f)); Rock(m, new(0.32f, 0.16f, 0.14f), new(0.18f, 0.16f, 0.18f), 52, 0.08f, 0, false); m.C(G(0.9f)); Rock(m, new(-0.28f, 0.17f, 0.12f), new(0.19f, 0.17f, 0.19f), 53, 0.08f, 0, false); m.C(G(0.72f)); Rock(m, new(-0.02f, 0.13f, -0.32f), new(0.16f, 0.14f, 0.16f), 54, 0.08f, 0, false); }
                else if (v == 1) { for (int i = 0; i < 6; i++) { float ang = i * 1.05f + 0.4f, rad = i == 0 ? 0f : 0.3f; m.C(G(0.7f + 0.05f * (i % 4))); Rock(m, new(MathF.Cos(ang) * rad, 0.13f + (i == 0 ? 0.06f : 0f), MathF.Sin(ang) * rad * 0.8f), new(0.17f, 0.15f, 0.17f), 60 + i, 0.08f, 0, false); } }
                else { m.C(G(0.92f)); Rock(m, new(-0.2f, 0.26f, 0), new(0.28f, 0.26f, 0.28f), 71, 0.06f, 0, true); m.C(G(0.78f)); Rock(m, new(0.24f, 0.22f, 0.06f), new(0.24f, 0.22f, 0.24f), 72, 0.06f, 0, true); }
                break;
            case Shape.Coal:
                if (v == 0) { m.C(G(0.85f)); Rock(m, new(-0.04f, 0.26f, 0), new(0.34f, 0.26f, 0.32f), 81, 0.3f, 4, false); m.C(G(1f)); Rock(m, new(0.3f, 0.14f, 0.18f), new(0.2f, 0.15f, 0.18f), 82, 0.3f, 3, false); m.C(G(0.7f)); Rock(m, new(-0.22f, 0.13f, -0.3f), new(0.2f, 0.14f, 0.18f), 83, 0.3f, 3, false); }
                else if (v == 1) { m.C(G(0.9f)); Rock(m, new(0.1f, 0.3f, 0), new(0.4f, 0.3f, 0.34f), 91, 0.26f, 5, false); m.C(G(0.7f)); Rock(m, new(-0.36f, 0.12f, 0.1f), new(0.18f, 0.12f, 0.16f), 92, 0.3f, 2, false); }
                else { for (int i = 0; i < 4; i++) { float ang = i * 1.7f; m.C(G(0.65f + 0.1f * i)); Rock(m, new(MathF.Cos(ang) * 0.27f, 0.13f + (i == 3 ? 0.05f : 0f), MathF.Sin(ang) * 0.25f), new(0.19f, 0.14f, 0.17f), 100 + i, 0.3f, 3, false); } }
                break;
            case Shape.Crystal:
                if (v == 0) { m.C(G(1f)); Spike(m, new(0, 0.02f, 0), 0.19f, 0.92f, 3, -2, 6, 8); m.C(G(0.82f)); Spike(m, new(0.26f, 0.05f, 0.1f), 0.13f, 0.58f, 22, -26, 6, 30); m.C(G(0.92f)); Spike(m, new(-0.24f, 0.05f, -0.12f), 0.12f, 0.5f, -20, 20, 6, 0); }
                else if (v == 1) { m.C(G(0.95f)); Spike(m, new(-0.18f, 0.02f, 0), 0.15f, 0.78f, 6, 12, 6); m.C(G(0.85f)); Spike(m, new(0.14f, 0.02f, 0.06f), 0.16f, 0.66f, -4, -14, 6, 20); m.C(G(0.7f)); Spike(m, new(0.02f, 0.04f, -0.24f), 0.1f, 0.42f, -26, 2, 6, 40); }
                else { m.C(G(0.6f)); m.Block(new(0, 0.05f, 0), new(0.8f, 0.1f, 0.5f), 0.9f, 0.9f); for (int i = 0; i < 5; i++) { m.C(G(0.8f + 0.04f * i)); Spike(m, new(-0.28f + i * 0.14f, 0.09f, (i % 2 == 0 ? -0.08f : 0.1f)), 0.1f, 0.34f + 0.08f * (i % 3), (i - 2) * 9f, (i % 2 == 0 ? 10 : -10), 6, i * 17); } }
                break;
            case Shape.IceChunk:
                if (v == 0) { m.C(Gl(1f, 0.55f)); Rock(m, new(-0.08f, 0.3f, 0), new(0.4f, 0.3f, 0.36f), 111, 0.1f, 3, false); m.C(Gl(0.9f, 0.5f)); Rock(m, new(0.34f, 0.14f, 0.16f), new(0.2f, 0.14f, 0.18f), 112, 0.1f, 2, false); m.C(Gl(1f, 0.9f)); Rock(m, new(-0.08f, 0.28f, 0), new(0.18f, 0.14f, 0.16f), 113, 0.1f, 1, false); }
                else if (v == 1) { m.C(Gl(1f, 0.5f)); Rock(m, new(0, 0.3f, 0), new(0.44f, 0.3f, 0.4f), 121, 0.08f, 5, true, 0.62f); m.C(Gl(1f, 0.9f)); Rock(m, new(0.04f, 0.26f, 0), new(0.16f, 0.12f, 0.14f), 122, 0.1f, 1, false); }
                else { m.C(Gl(1f, 0.55f)); Spike(m, new(0, 0.02f, 0), 0.2f, 0.8f, 6, 4, 5, 10, 0.4f); m.C(Gl(0.9f, 0.5f)); Spike(m, new(0.26f, 0.05f, 0.1f), 0.15f, 0.55f, 20, -24, 5, 20, 0.4f); m.C(Gl(0.95f, 0.6f)); Spike(m, new(-0.24f, 0.05f, -0.1f), 0.14f, 0.5f, -22, 18, 5, 50, 0.4f); }
                break;
            case Shape.Cell: CellShape(v, m, a); break;
            case Shape.Circuit: CircuitShape(v, m, a); break;
            case Shape.Canister: CanisterShape(v, m, a); break;
            case Shape.Jug: JugShape(v, m, a); break;
            case Shape.Pod: PodShape(v, m, a); break;
            case Shape.Flask: FlaskShape(v, m, a); break;
            case Shape.Gear: GearShape(v, m, a); break;
            case Shape.Atom: AtomShape(v, m, a); break;
            case Shape.Shell: ShellShape(v, m); break;
            case Shape.Rods: RodsShape(v, m, a); break;
            case Shape.Wafer: WaferShape(v, m, a); break;
            case Shape.Slab: SlabShape(v, m, a); break;
            case Shape.Billet: BilletShape(v, m, a); break;
            case Shape.Resonant: ResonantShape(v, m, a); break;
            case Shape.Lens: LensShape(v, m, a); break;
            default: CrateShape(v, m, a); break;
        }
    }

    private static void Ingot(int v, Models.Mb m)
    {
        if (v == 0)
        {
            m.C(G(0.82f)).Block(new(0, 0.19f, 0), new(1f, 0.38f, 0.62f), 0.84f, 0.8f);
            m.C(G(0.55f)).Block(new(0, 0.385f, 0), new(0.52f, 0.02f, 0.26f));
            m.C(G(1f)).Block(new(0, 0.397f, 0), new(0.44f, 0.012f, 0.18f));
        }
        else if (v == 1)
        {
            m.C(G(0.78f)).Block(new(0, 0.13f, 0), new(0.96f, 0.26f, 0.54f), 0.86f, 0.82f);
            m.At(new(0.02f, 0.39f, 0), new(0, Mathf.DegToRad(90f), 0)).C(G(0.9f)).Block(default, new(0.86f, 0.26f, 0.5f), 0.86f, 0.82f);
            m.At(default);
        }
        else
        {
            for (int i = -1; i <= 1; i++) m.C(G(0.8f + 0.06f * i)).Block(new(i * 0.34f, 0.17f, 0), new(0.3f, 0.34f, 0.56f), 0.8f, 0.8f);
            m.C(G(0.5f)).Block(new(-0.17f, 0.1f, 0), new(0.06f, 0.2f, 0.34f));
            m.C(G(0.5f)).Block(new(0.17f, 0.1f, 0), new(0.06f, 0.2f, 0.34f));
        }
    }

    private static void PlateShape(int v, Models.Mb m)
    {
        m.C(G(0.78f)).Block(new(0, 0.055f, 0), new(1f, 0.11f, 0.8f), 0.97f, 0.97f);
        m.C(G(1f)).Block(new(0, 0.122f, 0), new(0.82f, 0.03f, 0.62f));
        if (v == 1)
        {
            m.C(G(0.5f));
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f }) m.Block(new(sx * 0.42f, 0.14f, sz * 0.32f), new(0.07f, 0.04f, 0.07f));
        }
        else if (v == 2)
        {
            m.C(G(0.62f));
            foreach (float z in new[] { -0.17f, 0.17f }) m.Block(new(0, 0.15f, z), new(0.78f, 0.04f, 0.06f));
        }
        else m.C(G(0.62f)).Block(new(0.2f, 0.14f, 0), new(0.16f, 0.02f, 0.16f));
    }

    private static void CellShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            m.C(G(0.86f)).Prism(default, 0.28f, 0.28f, 0.78f, 8);
            m.C(G(1f)).Prism(new(0, 0.78f, 0), 0.29f, 0.2f, 0.08f, 8);
            m.C(G(0.6f)).Prism(new(0, 0.86f, 0), 0.08f, 0.08f, 0.1f, 6);
            m.C(G(0.5f)).Prism(default, 0.3f, 0.3f, 0.06f, 8);
            a.C(G(1f)).Prism(new(0, 0.28f, 0), 0.292f, 0.292f, 0.3f, 8);
        }
        else if (v == 1)
        {
            m.C(G(0.9f)).Prism(default, 0.4f, 0.4f, 0.5f, 6, 30f);
            m.C(G(0.55f)).Prism(new(0, 0.5f, 0), 0.4f, 0.32f, 0.07f, 6, 30f);
            a.C(G(1f)).Prism(new(0, 0.57f, 0), 0.26f, 0.26f, 0.03f, 6, 30f);
            a.C(G(1f)).Prism(new(0, 0.1f, 0), 0.412f, 0.412f, 0.14f, 6, 30f);
        }
        else
        {
            foreach (float z in new[] { -0.2f, 0.2f })
            {
                m.C(G(0.86f)).Prism(new(0, 0, z), 0.17f, 0.17f, 0.8f, 6);
                m.C(G(0.6f)).Prism(new(0, 0.8f, z), 0.08f, 0.08f, 0.1f, 5);
                a.C(G(1f)).Prism(new(0, 0.4f, z), 0.176f, 0.176f, 0.14f, 6);
            }
            m.C(G(0.5f)).Block(new(0, 0.55f, 0), new(0.1f, 0.06f, 0.4f));
        }
    }

    private static void CircuitShape(int v, Models.Mb m, Models.Mb a)
    {
        m.C(G(0.62f)).Block(new(0, 0.045f, 0), new(1f, 0.09f, 0.74f));
        a.C(G(0.45f)).Block(new(v == 2 ? -0.1f : 0f, 0.13f, 0), new(v == 2 ? 0.4f : 0.34f, 0.08f, v == 2 ? 0.4f : 0.34f));
        a.C(G(1f));
        foreach (float s in new[] { -1f, 1f }) a.Block(new(v == 2 ? -0.1f : 0f, 0.115f, s * (v == 2 ? 0.215f : 0.19f)), new(v == 2 ? 0.34f : 0.28f, 0.03f, 0.05f));
        a.C(G(0.9f));
        foreach (float z in new[] { -0.26f, 0f, 0.26f }) a.Block(new(-0.32f, 0.095f, z * (v == 1 ? 0.6f : 1f)), new(0.22f, 0.012f, 0.025f)); // gold traces
        if (v == 1)
        {
            foreach (float z in new[] { -0.22f, 0.22f }) a.C(G(0.7f)).Block(new(0.3f, 0.16f, z), new(0.16f, 0.14f, 0.16f), 0.85f, 0.85f);
            a.C(G(0.9f)).Block(new(0.3f, 0.13f, 0), new(0.14f, 0.06f, 0.14f));
        }
        else if (v == 2)
        {
            a.C(G(1f)).Block(new(0.44f, 0.12f, 0), new(0.1f, 0.06f, 0.56f)); // edge connector
            a.C(G(0.55f)).Block(new(0.2f, 0.12f, -0.2f), new(0.2f, 0.06f, 0.14f)).Block(new(0.2f, 0.12f, 0.2f), new(0.2f, 0.06f, 0.14f));
        }
        else
        {
            a.C(G(0.7f)).Prism(new(0.3f, 0.09f, -0.22f), 0.07f, 0.07f, 0.14f, 6);
            a.C(G(0.7f)).Prism(new(0.3f, 0.09f, 0.22f), 0.07f, 0.07f, 0.14f, 6);
        }
    }

    private static void CanisterShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            m.C(G(0.92f)).Prism(new(0, 0.05f, 0), 0.27f, 0.27f, 0.72f, 8);
            m.C(G(0.95f)).Sphere(new(0, 0.77f, 0), new(0.27f, 0.2f, 0.27f), 2, 8, true);
            m.C(G(0.6f)).Prism(new(0, 0.95f, 0), 0.07f, 0.06f, 0.1f, 6);
            m.C(G(0.55f)).Prism(default, 0.29f, 0.29f, 0.07f, 8);
            a.C(G(1f)).Prism(new(0, 0.42f, 0), 0.277f, 0.277f, 0.1f, 8);
            a.C(G(0.8f)).Block(new(0, 1.07f, 0), new(0.22f, 0.04f, 0.05f));
        }
        else if (v == 1)
        {
            m.C(G(0.92f)); CylX(m, -0.4f, 0.8f, 0.3f, 0, 0.27f, 8);
            m.C(G(0.95f)).Sphere(new(0.4f, 0.3f, 0), new(0.1f, 0.27f, 0.27f), 2, 8, true);
            m.C(G(0.55f)).Block(new(-0.2f, 0.07f, 0), new(0.12f, 0.14f, 0.54f)).Block(new(0.2f, 0.07f, 0), new(0.12f, 0.14f, 0.54f));
            m.C(G(0.6f)).Prism(new(0, 0.55f, 0), 0.06f, 0.05f, 0.1f, 6);
            a.C(G(1f));
            CylX(a, -0.06f, 0.12f, 0.3f, 0, 0.277f, 8);
        }
        else
        {
            m.C(G(0.9f)).Prism(new(0, 0.04f, 0), 0.19f, 0.19f, 0.8f, 8);
            m.C(G(0.95f)).Sphere(new(0, 0.84f, 0), new(0.19f, 0.2f, 0.19f), 2, 8, true);
            m.C(G(0.6f)).Prism(new(0, 1.02f, 0), 0.06f, 0.05f, 0.12f, 6);
            m.C(G(0.5f)).Prism(default, 0.21f, 0.21f, 0.06f, 8);
            a.C(G(1f)).Prism(new(0, 0.3f, 0), 0.196f, 0.196f, 0.08f, 8);
            a.C(G(1f)).Prism(new(0, 0.6f, 0), 0.196f, 0.196f, 0.08f, 8);
        }
    }

    private static void JugShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 1)
        {
            m.C(Gl(1f, 0.5f)).Prism(default, 0.36f, 0.34f, 0.6f, 8);
            m.C(Gl(0.9f, 0.95f)).Prism(new(0, 0.6f, 0), 0.34f, 0.16f, 0.08f, 8);
            m.C(Gl(0.7f, 0.98f)).Prism(new(0, 0.68f, 0), 0.12f, 0.12f, 0.1f, 6);
            a.C(G(1f)).Prism(new(0, 0.04f, 0), 0.3f, 0.3f, 0.42f, 8);
        }
        else
        {
            float h = v == 2 ? 0.74f : 0.58f, w = v == 2 ? 0.46f : 0.64f;
            m.C(Gl(1f, 0.5f)).Block(new(0, h / 2, 0), new(w, h, 0.5f), 0.92f, 0.92f);
            m.C(Gl(0.9f, 0.92f)).Prism(new(0, h, 0), 0.13f, 0.12f, 0.16f, 6);
            m.C(Gl(0.65f, 0.98f)).Prism(new(0, h + 0.16f, 0), 0.15f, 0.15f, 0.1f, 6);
            a.C(G(1f)).Block(new(0, h * 0.43f, 0), new(w * 0.82f, h * 0.7f, 0.38f));
            if (v == 0) m.C(Gl(0.8f, 0.9f)).Block(new(w / 2 + 0.05f, h * 0.55f, 0), new(0.12f, h * 0.55f, 0.1f));
        }
    }

    private static void PodShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            foreach (var (x, z, s) in new[] { (-0.2f, 0.1f, 0.3f), (0.2f, 0.12f, 0.27f), (0f, -0.22f, 0.29f) })
            {
                m.C(G(0.9f)).Sphere(new(x, s * 0.9f, z), new(s, s * 0.9f, s), 3, 6);
                a.C(G(1f)).Block(new(x, s * 1.78f, z), new(0.08f, 0.04f, 0.08f));
            }
        }
        else if (v == 1)
        {
            m.C(G(0.9f)).Sphere(new(0, 0.34f, 0), new(0.5f, 0.32f, 0.3f), 3, 7);
            m.C(G(0.6f)).Beam(new(0.45f, 0.4f, 0), new(0.72f, 0.5f, 0.02f), 0.05f);
            a.C(G(1f));
            for (int i = -1; i <= 1; i++) a.Block(new(i * 0.2f, 0.58f, 0), new(0.07f, 0.03f, 0.07f));
        }
        else
        {
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 1.25f;
                float s = 0.17f + 0.02f * (i % 2);
                m.C(G(0.75f + 0.06f * i)).Sphere(new(MathF.Cos(ang) * 0.26f * (i == 4 ? 0f : 1f), s, MathF.Sin(ang) * 0.24f * (i == 4 ? 0f : 1f)), new(s, s, s), 2, 5);
            }
            a.C(G(1f)).Block(new(0, 0.42f, 0), new(0.06f, 0.03f, 0.06f));
        }
    }

    private static void FlaskShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            m.C(Gl(1f, 0.5f)).Prism(default, 0.4f, 0.14f, 0.5f, 8);
            m.C(Gl(0.95f, 0.5f)).Prism(new(0, 0.5f, 0), 0.14f, 0.13f, 0.3f, 8);
            m.C(Gl(0.7f, 0.98f)).Prism(new(0, 0.8f, 0), 0.17f, 0.17f, 0.06f, 6);
            a.C(G(1f)).Prism(new(0, 0.03f, 0), 0.35f, 0.17f, 0.3f, 8);
        }
        else if (v == 1)
        {
            m.C(Gl(1f, 0.5f)).Sphere(new(0, 0.34f, 0), new(0.36f, 0.34f, 0.36f), 4, 8);
            m.C(Gl(0.95f, 0.5f)).Prism(new(0, 0.62f, 0), 0.13f, 0.12f, 0.28f, 8);
            m.C(Gl(0.7f, 0.98f)).Prism(new(0, 0.9f, 0), 0.16f, 0.14f, 0.07f, 6);
            a.C(G(1f)).Sphere(new(0, 0.28f, 0), new(0.3f, 0.2f, 0.3f), 2, 8);
        }
        else
        {
            foreach (var (x, z, h) in new[] { (-0.28f, -0.1f, 0.8f), (0f, 0.14f, 0.95f), (0.28f, -0.1f, 0.7f) })
            {
                m.C(Gl(1f, 0.5f)).Prism(new(x, 0, z), 0.12f, 0.12f, h, 6);
                m.C(Gl(0.7f, 0.98f)).Prism(new(x, h, z), 0.13f, 0.13f, 0.06f, 6);
                a.C(G(1f)).Prism(new(x, 0.03f, z), 0.095f, 0.095f, h * 0.55f, 6);
            }
        }
    }

    private static void GearShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            m.C(G(0.9f)); GearDisc(m, default, 0.47f, 0.37f, 9, 0f, 0.15f);
            m.C(G(0.6f)).Prism(new(0, 0.15f, 0), 0.19f, 0.17f, 0.03f, 8);
            a.C(G(1f)).Prism(new(0, 0.15f, 0), 0.1f, 0.1f, 0.05f, 8);
        }
        else if (v == 1)
        {
            m.C(G(0.9f)); GearDisc(m, new(-0.2f, 0, 0), 0.34f, 0.27f, 8, 0f, 0.13f);
            m.C(G(0.72f)); GearDisc(m, new(0.3f, 0, 0.12f), 0.22f, 0.17f, 6, 0.2f, 0.13f, 0.3f);
            a.C(G(1f)).Prism(new(-0.2f, 0.13f, 0), 0.08f, 0.08f, 0.05f, 6);
        }
        else
        {
            m.At(new(0, 0.5f, 0), new(Mathf.Pi / 2f, 0, 0)).C(G(0.9f));
            GearDisc(m, default, 0.44f, 0.35f, 9, -0.07f, 0.07f);
            m.C(G(0.6f)).Prism(new(0, -0.2f, 0), 0.05f, 0.05f, 0.4f, 6);
            m.At(default).C(G(0.55f)).Block(new(0, 0.04f, 0), new(0.5f, 0.08f, 0.3f));
            a.At(new(0, 0.5f, 0), new(Mathf.Pi / 2f, 0, 0)).C(G(1f)).Prism(new(0, 0.07f, 0), 0.12f, 0.12f, 0.03f, 8);
            a.At(default);
        }
    }

    private static void AtomShape(int v, Models.Mb m, Models.Mb a)
    {
        m.C(G(0.55f)).Prism(default, 0.22f, 0.16f, 0.08f, 6);
        m.C(G(0.6f)).Prism(new(0, 0.08f, 0), 0.035f, 0.035f, 0.18f, 5);
        float cy = 0.56f;
        if (v == 0)
        {
            m.At(new(0, cy, 0), new(Mathf.DegToRad(62f), 0, 0)).C(G(1f)).Torus(default, 0.42f, 0.035f, 12, 3);
            m.At(new(0, cy, 0), new(Mathf.DegToRad(-62f), Mathf.DegToRad(90f), 0)).C(G(0.85f)).Torus(default, 0.42f, 0.035f, 12, 3);
            m.At(default);
            a.C(G(1f)).Sphere(new(0, cy, 0), new(0.14f, 0.14f, 0.14f), 2, 5);
        }
        else if (v == 1)
        {
            for (int i = 0; i < 3; i++)
            {
                m.At(new(0, cy, 0), new(Mathf.DegToRad(60f), Mathf.DegToRad(60f * i), 0)).C(G(0.8f + 0.1f * i)).Torus(default, 0.38f, 0.03f, 10, 3);
            }
            m.At(default);
            a.C(G(1f)).Sphere(new(0, cy, 0), new(0.12f, 0.12f, 0.12f), 2, 5);
        }
        else
        {
            m.At(new(0, cy, 0), new(Mathf.DegToRad(75f), 0, 0)).C(G(1f)).Torus(default, 0.4f, 0.035f, 12, 3);
            m.At(default);
            a.C(G(1f)).Sphere(new(0, cy, 0), new(0.12f, 0.12f, 0.12f), 2, 5);
            a.C(G(0.9f)).Sphere(new(0.38f, cy + 0.1f, 0.0f), new(0.06f, 0.06f, 0.06f), 2, 4);
            a.C(G(0.9f)).Sphere(new(-0.36f, cy - 0.1f, 0.1f), new(0.06f, 0.06f, 0.06f), 2, 4);
        }
    }

    private static void ShellShape(int v, Models.Mb m)
    {
        void Round(Vector3 b, float r)
        {
            m.C(G(0.88f)).Prism(b, r, r, 0.5f, 6);
            m.C(G(0.55f)).Prism(b, r * 1.12f, r * 1.12f, 0.06f, 6);
            m.C(G(1f)).Prism(b + new Vector3(0, 0.5f, 0), r, r * 0.38f, 0.2f, 6);
        }
        if (v == 0) { Round(new(0, 0, -0.26f), 0.19f); Round(new(0, 0, 0.26f), 0.19f); }
        else if (v == 1)
        {
            for (int i = -1; i <= 1; i++)
            {
                float z = i * 0.34f;
                CylX(m, -0.4f, 0.58f, 0.15f, z, 0.15f, 6);
                m.C(G(1f)); CylX(m, 0.18f, 0.22f, 0.15f, z, 0.15f, 6, 0.06f);
                m.C(G(0.55f)); CylX(m, -0.4f, 0.05f, 0.15f, z, 0.17f, 6);
                m.C(G(0.88f));
            }
        }
        else
        {
            m.C(G(0.55f)).Block(new(-0.12f, 0.16f, 0), new(0.7f, 0.32f, 0.6f));
            m.C(G(0.4f)).Block(new(-0.12f, 0.34f, 0), new(0.74f, 0.05f, 0.64f));
            m.C(G(0.9f)).Block(new(-0.12f, 0.2f, 0.305f), new(0.14f, 0.1f, 0.02f));
            Round(new(0.5f, 0, 0.0f), 0.16f);
        }
    }

    private static void RodsShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            var rows = new (float Y, float[] Z)[] { (0.1f, new[] { -0.22f, 0f, 0.22f }), (0.29f, new[] { -0.11f, 0.11f }) };
            int k = 0;
            foreach (var (y, zs) in rows)
                foreach (float z in zs) { float len = 0.96f - 0.04f * (k++ % 3); m.C(G(0.5f + 0.1f * (k % 3))).Block(new(0, y, z), new(len, 0.19f, 0.2f)); }
            a.C(G(0.85f));
            foreach (float x in new[] { -0.26f, 0.26f }) a.Block(new(x, 0.19f, 0), new(0.08f, 0.44f, 0.66f));
        }
        else if (v == 1)
        {
            int k = 0;
            foreach (var (y, z) in new[] { (0.1f, -0.22f), (0.1f, 0f), (0.1f, 0.22f), (0.28f, -0.11f), (0.28f, 0.11f) })
            {
                m.C(G(0.5f + 0.1f * (k++ % 3)));
                CylX(m, -0.48f + 0.03f * k, 0.94f - 0.05f * k, y, z, 0.1f, 5);
            }
            a.C(G(0.85f)).Block(new(0, 0.19f, 0), new(0.08f, 0.44f, 0.66f));
        }
        else
        {
            m.C(G(0.55f)).Block(new(-0.05f, 0.08f, -0.2f), new(0.96f, 0.16f, 0.16f));
            m.At(new(0.05f, 0.24f, 0.12f), new(0, Mathf.DegToRad(14f), 0)).C(G(0.7f)).Block(default, new(0.92f, 0.16f, 0.16f));
            m.At(default);
            a.C(G(0.85f)).Block(new(0.35f, 0.2f, -0.04f), new(0.07f, 0.4f, 0.55f));
        }
    }

    private static void WaferShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            for (int i = 0; i < 4; i++) m.C(G(0.62f + 0.1f * (i % 2))).Prism(new(0, i * 0.075f, 0), 0.44f, 0.44f, 0.07f, 8, 6f * i);
            a.C(G(1f)).Block(new(0, 0.307f, 0), new(0.42f, 0.01f, 0.025f)).Block(new(0, 0.307f, 0), new(0.025f, 0.01f, 0.42f));
        }
        else if (v == 1)
        {
            for (int i = 0; i < 3; i++) m.C(G(0.7f - 0.08f * i)).Prism(new(0.05f * i - 0.05f, i * 0.075f, 0.04f * i), 0.43f, 0.43f, 0.07f, 8, 22f * i);
            a.C(G(1f)).Block(new(0, 0.231f, 0), new(0.4f, 0.01f, 0.025f));
        }
        else
        {
            m.C(G(0.5f)).Block(new(0, 0.03f, 0), new(0.4f, 0.06f, 0.5f));
            m.At(new(0, 0.36f, 0), new(Mathf.DegToRad(75f), 0, 0)).C(G(0.8f)).Prism(new(0, -0.035f, 0), 0.4f, 0.4f, 0.07f, 8);
            m.At(default);
            a.C(G(1f)).Block(new(0, 0.07f, 0.31f), new(0.34f, 0.12f, 0.02f));
        }
    }

    private static void SlabShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 2)
        {
            for (int i = 0; i < 5; i++) m.C(G(i % 2 == 0 ? 0.6f : 0.95f)).Block(new(0, 0.03f + i * 0.045f, 0), new(0.98f - i * 0.01f, 0.044f, 0.8f - i * 0.01f));
        }
        else
        {
            m.C(G(0.62f)).Block(new(0, 0.045f, 0), new(0.98f, 0.09f, 0.8f));
            m.C(G(0.85f)).Block(new(0, 0.115f, 0), new(0.92f, 0.05f, 0.74f));
            m.C(G(1f)).Block(new(0, 0.16f, 0), new(0.86f, 0.04f, 0.68f));
        }
        a.C(G(0.8f));
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f }) a.Block(new(sx * 0.45f, 0.1f, sz * 0.35f), new(0.12f, 0.2f, 0.12f));
        if (v == 1)
        {
            a.C(G(0.95f));
            for (int i = -1; i <= 1; i++) { a.At(new(i * 0.24f, 0.19f, 0), new(0, Mathf.DegToRad(45f), 0)).Block(default, new(0.7f, 0.02f, 0.05f)); }
            a.At(default);
        }
    }

    private static void BilletShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 0)
        {
            m.C(G(0.9f)); CylX(m, -0.48f, 0.96f, 0.27f, 0, 0.27f, 8, 0.27f);
            m.C(G(0.6f)); CylX(m, -0.5f, 0.04f, 0.27f, 0, 0.22f, 8); CylX(m, 0.46f, 0.04f, 0.27f, 0, 0.22f, 8);
            a.C(G(1f)); CylX(a, -0.28f, 0.1f, 0.27f, 0, 0.285f, 8); CylX(a, 0.18f, 0.1f, 0.27f, 0, 0.285f, 8);
        }
        else if (v == 1)
        {
            m.C(G(0.9f));
            foreach (float z in new[] { -0.2f, 0.2f }) CylX(m, -0.46f, 0.92f, 0.19f, z, 0.19f, 6);
            m.C(G(0.8f)); CylX(m, -0.46f, 0.92f, 0.52f, 0f, 0.19f, 6);
            a.C(G(1f)); CylX(a, -0.1f, 0.1f, 0.52f, 0, 0.2f, 6);
        }
        else
        {
            m.C(G(0.9f)); CylX(m, -0.4f, 0.8f, 0.3f, 0, 0.3f, 6);
            m.C(G(0.6f)); CylX(m, -0.46f, 0.06f, 0.3f, 0, 0.32f, 6);
            a.C(G(1f)); CylX(a, 0.4f, 0.03f, 0.3f, 0, 0.24f, 6);
            a.C(G(1f)); CylX(a, -0.12f, 0.12f, 0.3f, 0, 0.31f, 6);
        }
    }

    private static void ResonantShape(int v, Models.Mb m, Models.Mb a)
    {
        m.C(G(0.95f)).Prism(default, 0.2f, 0.17f, 0.14f, 6);
        if (v == 0)
        {
            m.C(G(1f)); Spike(m, new(-0.14f, 0.1f, 0), 0.15f, 0.66f, 4, 10, 6, 10);
            m.C(G(0.85f)); Spike(m, new(0.16f, 0.1f, 0.06f), 0.13f, 0.52f, -6, -22, 6, 30);
        }
        else if (v == 1)
        {
            m.C(G(1f)); Spike(m, new(0, 0.1f, 0), 0.2f, 0.8f, 0, 0, 6, 15);
            m.C(G(0.8f)); Spike(m, new(0.3f, 0.06f, -0.1f), 0.1f, 0.42f, 18, -30, 6, 0);
        }
        else
        {
            m.C(G(1f)); Spike(m, new(-0.2f, 0.1f, 0.06f), 0.13f, 0.6f, 6, 16, 6, 0);
            m.C(G(0.9f)); Spike(m, new(0.2f, 0.1f, -0.04f), 0.13f, 0.6f, -4, -16, 6, 30);
        }
        a.At(new(0, 0.86f, 0), new(Mathf.DegToRad(70f), 0, 0)).C(G(1f)).Torus(default, 0.3f, 0.022f, 10, 3);
        a.At(default).C(G(1f)).Sphere(new(0, 0.86f, 0), new(0.09f, 0.11f, 0.09f), 2, 4);
    }

    private static void LensShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 1)
        {
            a.C(G(0.9f)).Prism(default, 0.46f, 0.44f, 0.1f, 12);
            m.C(Gl(1f, 0.55f)).Sphere(new(0, 0.1f, 0), new(0.4f, 0.17f, 0.4f), 2, 12, true);
        }
        else
        {
            a.At(new(0, 0.5f, 0), new(Mathf.Pi / 2f, 0, 0)).C(G(0.9f)).Prism(new(0, -0.05f, 0), 0.46f, 0.44f, 0.1f, 12);
            m.At(new(0, 0.5f, 0), new(Mathf.Pi / 2f, 0, 0)).C(Gl(1f, 0.55f)).Sphere(default, new(0.4f, v == 2 ? 0.14f : 0.09f, 0.4f), 3, 12);
            m.At(default);
            a.At(default).C(G(0.6f)).Block(new(0, 0.04f, 0), new(0.34f, 0.08f, 0.3f)).Block(new(0, 0.12f, 0), new(0.08f, 0.16f, 0.08f));
        }
        a.At(default);
    }

    private static void CrateShape(int v, Models.Mb m, Models.Mb a)
    {
        if (v == 2)
        {
            m.C(G(0.8f)).Prism(default, 0.3f, 0.3f, 0.64f, 8);
            m.C(G(0.95f)).Prism(new(0, 0.64f, 0), 0.3f, 0.26f, 0.05f, 8);
            a.C(G(0.8f)).Prism(new(0, 0.12f, 0), 0.315f, 0.315f, 0.06f, 8).Prism(new(0, 0.44f, 0), 0.315f, 0.315f, 0.06f, 8);
            return;
        }
        m.C(G(0.85f)).Block(new(0, 0.28f, 0), new(0.82f, 0.56f, 0.64f));
        m.C(G(1f)).Block(new(0, 0.57f, 0), new(0.78f, 0.04f, 0.6f));
        a.C(G(0.7f));
        foreach (float x in new[] { -0.28f, 0.28f }) a.Block(new(x, 0.28f, 0), new(0.09f, 0.58f, 0.66f));
        if (v == 1) a.C(G(0.6f)).Beam(new(-0.4f, 0.04f, 0.33f), new(0.4f, 0.52f, 0.33f), 0.05f);
    }
}

/// <summary>
/// The items of one structure on its belts: one MultiMesh per (shape, variant) layer, created the first time an item needs it and grown on
/// demand (doubling), so the per-frame upload is proportional to the items drawn. Per frame: <see cref="Begin"/>, <see cref="Count"/> for every
/// item, <see cref="Allocate"/>, <see cref="Add"/> for every item (any order), <see cref="Flush"/>. No allocation unless a layer grows.
/// </summary>
public sealed class ItemLayers
{
    private const int Floats = 16;
    private readonly Node3D _parent;
    private readonly MultiMeshInstance3D?[] _nodes = new MultiMeshInstance3D?[ItemModels.Layers];
    private readonly MultiMesh?[] _mm = new MultiMesh?[ItemModels.Layers];
    private readonly float[][] _buf = new float[ItemModels.Layers][];
    private readonly int[] _cap = new int[ItemModels.Layers];
    private readonly int[] _need = new int[ItemModels.Layers];
    private readonly int[] _used = new int[ItemModels.Layers];
    private readonly int[] _shown = new int[ItemModels.Layers];

    public ItemLayers(Node3D parent)
    {
        _parent = parent;
        for (int i = 0; i < _buf.Length; i++) _buf[i] = Array.Empty<float>();
    }

    public int ActiveLayers
    {
        get { int n = 0; foreach (var m in _mm) if (m is not null) n++; return n; }
    }

    public void Free()
    {
        foreach (var n in _nodes) if (n is not null && GodotObject.IsInstanceValid(n)) n.QueueFree();
    }

    public void Begin() { Array.Clear(_need); Array.Clear(_used); }

    public void Count(int layer) => _need[layer]++;

    /// <summary>Creates the layers that are needed for the first time and grows those that need more room (to the next power of two, at least 32).</summary>
    public void Allocate()
    {
        for (int s = 0; s < _need.Length; s++)
        {
            if (_need[s] <= _cap[s]) continue;
            if (_mm[s] is null)
            {
                _mm[s] = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                    Mesh = ItemModels.Mesh((ItemModels.Shape)(s / ItemModels.Variants), s % ItemModels.Variants), InstanceCount = 0,
                };
                _nodes[s] = new MultiMeshInstance3D { Name = $"Items{(ItemModels.Shape)(s / ItemModels.Variants)}{s % ItemModels.Variants}", Multimesh = _mm[s], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                _parent.AddChild(_nodes[s]);
            }
            int cap = Math.Max(32, _cap[s]);
            while (cap < _need[s]) cap *= 2;
            _cap[s] = cap;
            _mm[s]!.InstanceCount = cap;
            _buf[s] = new float[cap * Floats];
            _shown[s] = -1;
        }
    }

    /// <summary>Writes one item: uniform <paramref name="scale"/>, a turn about the vertical axis, position and tint.</summary>
    public void Add(int layer, float scale, Vector3 pos, float yaw, Color c)
    {
        var b = _buf[layer];
        int o = _used[layer]++ * Floats;
        float cs = MathF.Cos(yaw) * scale, sn = MathF.Sin(yaw) * scale;
        b[o + 0] = cs; b[o + 1] = 0; b[o + 2] = sn; b[o + 3] = pos.X;
        b[o + 4] = 0; b[o + 5] = scale; b[o + 6] = 0; b[o + 7] = pos.Y;
        b[o + 8] = -sn; b[o + 9] = 0; b[o + 10] = cs; b[o + 11] = pos.Z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    public void Flush()
    {
        for (int s = 0; s < _mm.Length; s++)
        {
            var mm = _mm[s];
            if (mm is null) continue;
            int n = _used[s];
            if (n == 0 && _shown[s] == 0) continue; // nothing was visible and nothing is
            mm.VisibleInstanceCount = n;
            if (n > 0) mm.Buffer = _buf[s];
            _shown[s] = n;
        }
    }
}
