using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Meshes for belts and the items on them, all built once and shared. A belt cell is a tile one cell long along local +X (rails, a dark
/// channel, chevrons pointing the way); a segment is one MultiMesh instance per cell, rotated to its direction. The items that ride on it live in
/// <see cref="ItemModels"/>.
/// </summary>
public static class BeltModels
{
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
        // a bracket plate on the outer face of each rail, so a long run reads as bolted sections
        foreach (float s in new[] { -1f, 1f })
            mb.C(G(0.30f)).Block(new(0.2f, rail * 0.5f, s * 0.447f), new(0.2f, rail * 0.8f, 0.02f));
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
        // a ring of eight bolts and four short flow ribs on the inner disc, so a hub reads as a machined junction
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f;
            mb.C(G(0.8f)).Prism(new(MathF.Cos(a) * 0.29f, 0.178f, MathF.Sin(a) * 0.29f), 0.032f, 0.026f, 0.014f, 6);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4f + Mathf.Pi / 4f;
            mb.C(G(0.75f)).Beam(new(MathF.Cos(a) * 0.1f, 0.19f, MathF.Sin(a) * 0.1f), new(MathF.Cos(a) * 0.2f, 0.19f, MathF.Sin(a) * 0.2f), 0.03f);
        }
    });
}
