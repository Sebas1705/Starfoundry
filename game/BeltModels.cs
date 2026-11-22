using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Meshes for belts and the items on them, all built once and shared. A belt cell is a tile one cell long along local +X (rails, a dark
/// channel, chevrons pointing the way); a segment is one MultiMesh instance per cell, rotated to its direction. Items come in a handful of
/// silhouettes (ore chunk, ingot bar, plate, crystal cluster, fuel cell, circuit chip, orb, flask, shell) tinted per item through the
/// MultiMesh instance colour, so a belt reads at a glance. Item meshes stand on y = 0 and are about one unit wide: scale them by the
/// item size (the game uses about 0.27).
/// </summary>
public static class BeltModels
{
    public enum Shape { Ingot, Plate, Ore, Crystal, Cell, Chip, Orb, Flask, Shell }

    public static readonly int ShapeCount = Enum.GetValues<Shape>().Length;

    /// <summary>The shape and tint of an item, by content id. Unknown items are grey ingots.</summary>
    public static (Shape Shape, Color Tint) StyleOf(string id) => id switch
    {
        "core:iron_ore" => (Shape.Ore, new Color(0.82f, 0.46f, 0.30f)),
        "core:titanium_ore" => (Shape.Ore, new Color(0.56f, 0.64f, 0.84f)),
        "core:nickel_ore" => (Shape.Ore, new Color(0.50f, 0.76f, 0.60f)),
        "core:carbon" => (Shape.Ore, new Color(0.30f, 0.30f, 0.34f)),
        "core:quartz" => (Shape.Crystal, new Color(1f, 0.80f, 0.92f)),
        "core:ice" => (Shape.Crystal, new Color(0.62f, 0.92f, 1f)),
        "core:iron_ingot" => (Shape.Ingot, new Color(0.92f, 0.64f, 0.50f)),
        "core:titanium_ingot" => (Shape.Ingot, new Color(0.68f, 0.80f, 1f)),
        "core:nickel_ingot" => (Shape.Ingot, new Color(0.78f, 0.94f, 0.78f)),
        "core:silicon" => (Shape.Ingot, new Color(0.40f, 0.46f, 0.74f)),
        "core:iron_plate" => (Shape.Plate, new Color(0.80f, 0.82f, 0.90f)),
        "core:titanium_plate" => (Shape.Plate, new Color(0.62f, 0.74f, 1f)),
        "core:nickel_plate" => (Shape.Plate, new Color(0.66f, 0.90f, 0.80f)),
        "core:titanium_alloy" => (Shape.Plate, new Color(0.80f, 0.55f, 1f)),
        "core:circuit" => (Shape.Chip, new Color(0.30f, 0.90f, 0.46f)),
        "core:fuel_cell" => (Shape.Cell, new Color(1f, 0.62f, 0.14f)),
        "core:ammo" => (Shape.Shell, new Color(1f, 0.86f, 0.28f)),
        "core:hydrogen" => (Shape.Orb, new Color(0.64f, 0.92f, 1f)),
        "core:biomass" => (Shape.Orb, new Color(0.46f, 0.88f, 0.36f)),
        "core:science_engineering" => (Shape.Flask, new Color(1f, 0.56f, 0.16f)),
        "core:science_biology" => (Shape.Flask, new Color(0.32f, 0.96f, 0.48f)),
        "core:science_physics" => (Shape.Flask, new Color(0.36f, 0.86f, 1f)),
        _ => (Shape.Ingot, new Color(0.7f, 0.72f, 0.78f)),
    };

    /// <summary>The material of an item mesh (vertex colour times instance colour, with a texture for the stuff it is made of).</summary>
    public static StandardMaterial3D ItemMaterial(Shape shape) => shape switch
    {
        Shape.Ore => Materials.Ore(0.8f, 0.5f),
        Shape.Crystal => Materials.Crystal(0.3f, 0.1f),
        _ => Materials.ItemMetal(),
    };

    private static readonly Dictionary<string, ArrayMesh> Cache = new();

    private static ArrayMesh Get(string key, Material mat, Action<Models.Mb> build, Action<ArrayMesh>? post = null)
    {
        if (!Cache.TryGetValue(key, out var mesh))
        {
            var mb = new Models.Mb();
            build(mb);
            Cache[key] = mesh = mb.ToMesh(mat);
            post?.Invoke(mesh);
            mesh.ResourceName = key;
        }
        return mesh;
    }

    private static Color G(float v) => new(v, v, v);

    // ---- items ----------------------------------------------------------------------------------------

    public static ArrayMesh Item(Shape shape) => Get("item." + shape, ItemMaterial(shape), mb =>
    {
        mb.At(default);
        switch (shape)
        {
            case Shape.Ingot:
                mb.C(G(0.82f)).Block(new(0, 0.26f, 0), new(1f, 0.52f, 0.56f), 0.78f, 0.62f);
                mb.C(G(1f)).Block(new(0, 0.53f, 0), new(0.72f, 0.03f, 0.34f));
                break;
            case Shape.Plate:
                mb.C(G(0.80f)).Block(new(0, 0.07f, 0), new(0.98f, 0.14f, 0.84f));
                mb.C(G(1f)).Block(new(0, 0.16f, 0), new(0.78f, 0.04f, 0.64f));
                mb.C(G(0.62f)).Block(new(0.0f, 0.19f, 0), new(0.18f, 0.02f, 0.18f));
                break;
            case Shape.Ore:
                mb.C(G(0.85f)).Prism(new(0, 0, 0), 0.5f, 0.30f, 0.6f, 6, 12f);
                mb.C(G(0.66f)).Prism(new(0.26f, 0, 0.2f), 0.30f, 0.16f, 0.42f, 5, 30f);
                mb.C(G(1f)).Prism(new(-0.22f, 0, -0.2f), 0.24f, 0.12f, 0.36f, 4, 8f);
                break;
            case Shape.Crystal:
                mb.C(G(1f)).Prism(new(0, 0, 0), 0.26f, 0f, 1.05f, 4, 15f);
                mb.C(G(0.80f)).Prism(new(0.27f, 0, 0.12f), 0.19f, 0f, 0.66f, 4, 40f);
                mb.C(G(0.92f)).Prism(new(-0.22f, 0, -0.16f), 0.18f, 0f, 0.55f, 4, 70f);
                mb.C(G(0.7f)).Prism(new(0.02f, 0, -0.3f), 0.14f, 0f, 0.4f, 4, 0f);
                break;
            case Shape.Cell:
                mb.C(G(0.86f)).Prism(new(0, 0, 0), 0.34f, 0.34f, 0.82f, 8);
                mb.C(G(1f)).Prism(new(0, 0.82f, 0), 0.34f, 0.22f, 0.10f, 8);
                mb.C(G(1f)).Prism(new(0, 0.30f, 0), 0.355f, 0.355f, 0.12f, 8);
                mb.C(G(0.55f)).Prism(new(0, 0.92f, 0), 0.09f, 0.09f, 0.1f, 6);
                break;
            case Shape.Chip:
                mb.C(G(0.62f)).Block(new(0, 0.05f, 0), new(1f, 0.10f, 0.76f));
                mb.C(G(0.28f)).Block(new(0, 0.14f, 0), new(0.42f, 0.09f, 0.42f));
                mb.C(G(1f));
                for (int i = 0; i < 3; i++)
                    foreach (float s in new[] { -1f, 1f }) mb.Block(new(-0.3f + i * 0.3f, 0.11f, s * 0.30f), new(0.14f, 0.02f, 0.10f));
                mb.C(G(0.95f)).Block(new(0.0f, 0.195f, 0), new(0.14f, 0.015f, 0.14f));
                break;
            case Shape.Orb:
                mb.C(G(1f)).Sphere(new(0, 0.46f, 0), new(0.46f, 0.46f, 0.46f), 4, 8);
                mb.C(G(0.7f)).Torus(new(0, 0.46f, 0), 0.46f, 0.03f, 8, 3);
                break;
            case Shape.Flask:
                mb.C(G(0.95f)).Prism(new(0, 0, 0), 0.44f, 0.20f, 0.52f, 8);
                mb.C(G(1f)).Prism(new(0, 0.52f, 0), 0.14f, 0.14f, 0.30f, 6);
                mb.C(G(0.5f)).Prism(new(0, 0.82f, 0), 0.17f, 0.17f, 0.12f, 6);
                break;
            case Shape.Shell:
                foreach (float z in new[] { -0.26f, 0.26f })
                {
                    mb.C(G(0.88f)).Prism(new(0, 0, z), 0.20f, 0.20f, 0.55f, 6);
                    mb.C(G(1f)).Prism(new(0, 0.55f, z), 0.20f, 0f, 0.35f, 6);
                    mb.C(G(0.55f)).Prism(new(0, 0.0f, z), 0.215f, 0.215f, 0.08f, 6);
                }
                break;
        }
    });

    // ---- belts ------------------------------------------------------------------------------------------

    /// <summary>
    /// One belt cell, 1 long along +X and 0.9 wide, from y = 0 to 0.25 (rails). Tier 0 has one chevron per cell, tier 1 two, tier 2 two with
    /// taller rails and a bright centre line. The tier colour is the instance colour; the shape carries only brightness.
    /// </summary>
    public static ArrayMesh Tile(int tier) => GetTile(tier);

    private static ArrayMesh GetTile(int tier) => Get("belt.tile." + tier, Materials.Structural(), mb =>
    {
        mb.At(default);
        float rail = tier == 2 ? 0.30f : 0.25f;
        mb.C(G(0.50f)).Block(new(0, 0.07f, 0), new(1.0f, 0.14f, 0.86f));
        // the running surface is the second surface of the mesh (belt texture), see Get
        foreach (float s in new[] { -1f, 1f })
        {
            mb.C(G(1f)).Block(new(0, rail / 2, s * 0.385f), new(1.0f, rail, 0.12f));
            mb.C(G(0.80f)).Block(new(0, rail + 0.005f, s * 0.385f), new(1.0f, 0.012f, 0.07f)); // bright rail cap
        }
        // support ribs under the bed every cell so a long belt looks segmented
        mb.C(G(0.34f)).Block(new(0.47f, 0.07f, 0), new(0.06f, 0.15f, 0.9f));
        if (tier == 2) mb.C(G(0.9f)).Block(new(0, 0.168f, 0), new(1.0f, 0.006f, 0.04f));
        float[] xs = tier == 0 ? new[] { 0.0f } : new[] { -0.24f, 0.24f };
        float sz = tier == 0 ? 0.24f : 0.2f;
        mb.C(G(0.95f));
        foreach (float x in xs)
            foreach (float s in new[] { -1f, 1f })
                mb.Extrude(new[] { new Vector2(x - 0.11f, s * sz), new Vector2(x - 0.04f, s * sz), new Vector2(x + 0.10f, 0f), new Vector2(x + 0.03f, 0f) }, 0.165f, 0.178f);
    }, mesh =>
    {
        var bed = new Models.Mb();
        bed.At(default).C(G(0.30f)).Block(new(0, 0.15f, 0), new(1.0f, 0.03f, 0.58f)); // the running surface: belt texture, flow along +X
        bed.AddSurface(mesh, Materials.Belt());
    });

    /// <summary>A flat octagonal cap with a darker inner disc: put on the end of a splitting belt (amber), a sink (red) or a source (green) through the instance colour.</summary>
    public static ArrayMesh Hub() => Get("belt.hub", Materials.Structural(), mb =>
    {
        mb.At(default);
        mb.C(G(1f)).Prism(new(0, 0.168f, 0), 0.34f, 0.34f, 0.014f, 8, 22.5f);
        mb.C(G(0.50f)).Prism(new(0, 0.178f, 0), 0.22f, 0.22f, 0.008f, 8, 22.5f);
        mb.C(G(1f)).Prism(new(0, 0.184f, 0), 0.08f, 0.08f, 0.008f, 8, 22.5f);
    });
}

/// <summary>
/// The items of one structure: one MultiMesh per item shape, each grown on demand (doubling) so the per-frame upload is proportional to the
/// items of that shape, not to the worst case of the whole belt network. Per frame: <see cref="Begin"/>, <see cref="Count"/> for every item,
/// <see cref="Allocate"/>, <see cref="Add"/> for every item (same order irrelevant), <see cref="Flush"/>. No allocation unless a layer grows.
/// </summary>
public sealed class ItemLayers
{
    private const int Floats = 16;
    private readonly MultiMeshInstance3D[] _nodes = new MultiMeshInstance3D[BeltModels.ShapeCount];
    private readonly MultiMesh[] _mm = new MultiMesh[BeltModels.ShapeCount];
    private readonly float[][] _buf = new float[BeltModels.ShapeCount][];
    private readonly int[] _cap = new int[BeltModels.ShapeCount];
    private readonly int[] _need = new int[BeltModels.ShapeCount];
    private readonly int[] _used = new int[BeltModels.ShapeCount];
    private readonly int[] _shown = new int[BeltModels.ShapeCount];

    public ItemLayers(Node3D parent)
    {
        for (int s = 0; s < _nodes.Length; s++)
        {
            _mm[s] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = BeltModels.Item((BeltModels.Shape)s), InstanceCount = 0,
            };
            _buf[s] = Array.Empty<float>();
            _nodes[s] = new MultiMeshInstance3D { Name = "Items" + (BeltModels.Shape)s, Multimesh = _mm[s], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            parent.AddChild(_nodes[s]);
        }
    }

    public void Free()
    {
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
    }

    public void Begin() { Array.Clear(_need); Array.Clear(_used); }

    public void Count(int shape) => _need[shape]++;

    /// <summary>Grows the layers that need more room (to the next power of two, at least 32).</summary>
    public void Allocate()
    {
        for (int s = 0; s < _need.Length; s++)
        {
            if (_need[s] <= _cap[s]) continue;
            int cap = Math.Max(32, _cap[s]);
            while (cap < _need[s]) cap *= 2;
            _cap[s] = cap;
            _mm[s].InstanceCount = cap;
            _buf[s] = new float[cap * Floats];
            _shown[s] = -1;
        }
    }

    /// <summary>Writes one item: uniform <paramref name="scale"/>, position and tint.</summary>
    public void Add(int shape, float scale, Vector3 pos, Color c)
    {
        var b = _buf[shape];
        int o = _used[shape]++ * Floats;
        b[o + 0] = scale; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = pos.X;
        b[o + 4] = 0; b[o + 5] = scale; b[o + 6] = 0; b[o + 7] = pos.Y;
        b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = scale; b[o + 11] = pos.Z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    public void Flush()
    {
        for (int s = 0; s < _mm.Length; s++)
        {
            int n = _used[s];
            if (n == 0 && _shown[s] == 0) continue; // nothing was visible and nothing is
            _mm[s].VisibleInstanceCount = n;
            if (n > 0) _mm[s].Buffer = _buf[s];
            _shown[s] = n;
        }
    }
}
