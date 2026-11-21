using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// A deposit drawn as a procedural asteroid. The rock is seeded by the deposit id and sized by its initial amount;
/// ore chunks protrude from it in a per-item look, and their number, size and glow follow the vein density.
/// As the deposit runs dry the chunks shrink and vanish (in steps of 5 percent), leaving a bare dark rock.
/// Meshes are cached per shape key and materials per (item, density bucket), so dozens of deposits stay cheap.
/// </summary>
public sealed partial class DepositView : Node3D
{
    /// <summary>Item look: chunk shape, colours and material feel.</summary>
    private enum Shape { Lump, Round, Crystal, Plate, Stub, Gem }

    private sealed record Look(Shape Shape, Color Rock, Color Chunk, Color ChunkDark, Color Glow, float Metal, float Rough, float Alpha, float GlowScale);

    private static Look LookOf(string item) => item switch
    {
        "core:iron_ore" => new(Shape.Lump, new(0.30f, 0.22f, 0.18f), new(0.55f, 0.27f, 0.13f), new(0.34f, 0.15f, 0.08f), new(0.95f, 0.38f, 0.10f), 0.2f, 0.75f, 1f, 0.30f),
        "core:carbon" => new(Shape.Round, new(0.40f, 0.38f, 0.38f), new(0.05f, 0.05f, 0.06f), new(0.015f, 0.015f, 0.02f), new(0.35f, 0.40f, 0.55f), 0.15f, 0.2f, 1f, 0.03f),
        "core:ice" => new(Shape.Crystal, new(0.30f, 0.34f, 0.42f), new(0.70f, 0.88f, 1.00f), new(0.45f, 0.70f, 0.95f), new(0.45f, 0.80f, 1.00f), 0.1f, 0.08f, 0.62f, 0.7f),
        "core:titanium_ore" => new(Shape.Plate, new(0.30f, 0.32f, 0.36f), new(0.86f, 0.89f, 0.94f), new(0.58f, 0.62f, 0.70f), new(0.80f, 0.88f, 1.00f), 0.7f, 0.45f, 1f, 0.12f),
        "core:nickel_ore" => new(Shape.Stub, new(0.26f, 0.30f, 0.27f), new(0.50f, 0.62f, 0.52f), new(0.30f, 0.40f, 0.33f), new(0.45f, 0.85f, 0.55f), 0.45f, 0.5f, 1f, 0.4f),
        "core:quartz" => new(Shape.Gem, new(0.30f, 0.28f, 0.36f), new(0.86f, 0.80f, 1.00f), new(0.62f, 0.45f, 0.92f), new(0.72f, 0.50f, 1.00f), 0.05f, 0.06f, 0.78f, 1.0f),
        _ => new(Shape.Lump, new(0.28f, 0.28f, 0.32f), new(0.6f, 0.65f, 0.75f), new(0.35f, 0.4f, 0.5f), new(0.5f, 0.7f, 1f), 0.3f, 0.5f, 1f, 0.5f),
    };

    /// <summary>Density bucket 0 = poor (below 60), 1 = normal (60 to 120), 2 = rich (above 120).</summary>
    public static int Bucket(int density) => density < 60 ? 0 : density <= 120 ? 1 : 2;

    /// <summary>Rock size multiplier from the initial amount: bigger deposits are bigger rocks.</summary>
    public static float RockScale(int initial) => Mathf.Clamp(0.55f + 0.6f * MathF.Sqrt(MathF.Max(0, initial) / 600f), 0.6f, 1.6f);

    /// <summary>Number of chunks a full deposit shows: 2 at density 25, about 9 at 100, about 22 at 200.</summary>
    public static int ChunkCount(int density) => Mathf.Clamp((int)MathF.Round(0.1f * density - 0.5f + density * density * 0.00022f), 2, 26);

    private static readonly Dictionary<string, StandardMaterial3D> ChunkMats = new();
    private static readonly Dictionary<string, ArrayMesh> Meshes = new();

    private readonly string _item;
    private readonly int _density, _seed;
    private readonly float _initial;
    private readonly MeshInstance3D _rock, _chunks;
    private int _step = -1; // remaining in 5% steps (20 = full, 0 = empty)

    public DepositView(string item, int density, int initial, int seed)
    {
        _item = item; _density = density; _initial = Math.Max(1, initial); _seed = seed;
        Name = "Deposit";
        _rock = new MeshInstance3D { Name = "Rock" };
        _chunks = new MeshInstance3D { Name = "GlowCrystals" };
        AddChild(_rock);
        AddChild(_chunks);
        Scale = Vector3.One * RockScale(initial);
        Refresh(initial);
    }

    /// <summary>Updates the look for the remaining amount; rebuilds only when it crossed a 5 percent step.</summary>
    public void Refresh(int remaining)
    {
        float f = Mathf.Clamp(remaining / _initial, 0f, 1f);
        int step = remaining <= 0 ? 0 : Math.Max(1, (int)MathF.Ceiling(f * 20f));
        if (step == _step) return;
        _step = step;
        var look = LookOf(_item);
        bool dry = step == 0;
        string rockKey = $"dep.rock.{_seed}.{_item}.{(dry ? "dry" : "full")}";
        if (!Meshes.TryGetValue(rockKey, out var rock))
        {
            var mb = new Models.Mb();
            Models.BuildRock(mb, look.Rock, _seed, dry ? 0.45f : 1f, dry ? 0.05f : 0.28f);
            Meshes[rockKey] = rock = mb.ToMesh(Models.Body());
        }
        _rock.Mesh = rock;
        if (dry) { _chunks.Mesh = null; return; }

        string key = $"dep.chunks.{_seed}.{_item}.{_density}.{step}";
        if (!Meshes.TryGetValue(key, out var mesh))
        {
            var mb = new Models.Mb();
            BuildChunks(mb, look, _density, _seed, step / 20f);
            Meshes[key] = mesh = mb.ToMesh(ChunkMaterial(_item, look, Bucket(_density)));
        }
        _chunks.Mesh = mesh;
    }

    private static StandardMaterial3D ChunkMaterial(string item, Look look, int bucket)
    {
        string key = $"{item}.{bucket}";
        if (ChunkMats.TryGetValue(key, out var m)) return m;
        float energy = look.GlowScale * (bucket == 0 ? 0.25f : bucket == 1 ? 0.6f : 1.3f);
        m = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White,
            Metallic = look.Metal,
            Roughness = look.Rough,
            EmissionEnabled = true,
            Emission = look.Glow,
            EmissionEnergyMultiplier = energy,
        };
        if (look.Alpha < 1f)
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }
        ChunkMats[key] = m;
        return m;
    }

    private static void BuildChunks(Models.Mb mb, Look look, int density, int seed, float fraction)
    {
        var rng = new Models.Rng(seed + 977);
        int total = ChunkCount(density);
        int shown = Math.Max(1, (int)MathF.Ceiling(total * fraction));
        // Chunk size grows with density (tiny crumbs when poor, big slabs when rich) and shrinks as the vein is mined out.
        float size = 2.3f * (0.42f + 0.9f * Mathf.Clamp((density - 25) / 175f, 0f, 1f)) * (0.55f + 0.45f * fraction);
        for (int k = 0; k < total; k++)
        {
            // Always advance the generator so a chunk keeps its place while others vanish.
            float y = -0.35f + rng.F() * 1.35f, ang = rng.F() * Mathf.Tau;
            float j1 = rng.F(), j2 = rng.F(), j3 = rng.F(), sz = rng.F(), spin = rng.F();
            if (k >= shown) continue;
            float rr = MathF.Sqrt(MathF.Max(0f, 1f - Mathf.Clamp(y, -1f, 1f) * Mathf.Clamp(y, -1f, 1f)));
            var dir = new Vector3(MathF.Cos(ang) * rr, y, MathF.Sin(ang) * rr).Normalized();
            var p = Models.RockPoint(dir, seed) - dir * 0.12f;
            var axis = (dir + new Vector3(j1 - 0.5f, j2 - 0.5f, j3 - 0.5f) * 0.6f).Normalized();
            mb.At(p, new Quaternion(Vector3.Up, axis).GetEuler());
            float s = size * (0.75f + 0.5f * sz);
            Color c = look.Chunk.Lerp(look.ChunkDark, spin * 0.8f);
            c.A = look.Alpha;
            mb.C(c);
            switch (look.Shape)
            {
                case Shape.Lump: // blunt rusty lumps
                    mb.Prism(Vector3.Zero, 0.20f * s, 0.12f * s, 0.24f * s, 5, spin * 60f);
                    break;
                case Shape.Round: // glossy low domes
                    mb.Sphere(Vector3.Zero, new Vector3(0.20f, 0.15f, 0.20f) * s, 2, 6);
                    break;
                case Shape.Crystal: // tall pale spikes
                    mb.Prism(Vector3.Zero, 0.085f * s, 0.07f * s, 0.34f * s, 6, spin * 60f);
                    mb.Prism(new(0, 0.34f * s, 0), 0.07f * s, 0f, 0.15f * s, 6, spin * 60f);
                    break;
                case Shape.Plate: // angular silver slabs
                    mb.Block(new(0, 0.07f * s, 0), new(0.30f * s, 0.14f * s, 0.20f * s), 0.7f, 0.8f);
                    break;
                case Shape.Stub: // short stubby greenish nuggets
                    mb.Prism(Vector3.Zero, 0.17f * s, 0.13f * s, 0.16f * s, 6, spin * 60f);
                    mb.Prism(new(0, 0.16f * s, 0), 0.13f * s, 0.04f * s, 0.07f * s, 6, spin * 60f);
                    break;
                case Shape.Gem: // clear violet crystals
                    mb.Prism(Vector3.Zero, 0.07f * s, 0.06f * s, 0.30f * s, 6, spin * 60f);
                    mb.Prism(new(0, 0.30f * s, 0), 0.06f * s, 0f, 0.11f * s, 6, spin * 60f);
                    break;
            }
        }
    }
}
