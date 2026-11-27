using System;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Shared meshes of the rock field (ADR 0046). A <see cref="DepositView"/> builds its own mesh for every deposit id, which is right for the few veins of a system
/// but not for hundreds of rocks of a cluster; the field draws its rocks as MultiMesh instances of the meshes built here: one near mesh (rock plus ore chunks, two
/// surfaces) per ore, size class, richness bucket and depletion level, and one far mesh (80 faces, tinted per instance) per size class. A shape is shared by all
/// ores of a size class; variants only change between sectors, so a rock keeps its look and a sector costs few draw calls.
/// </summary>
public sealed partial class DepositView
{
    /// <summary>Shapes per size class (a sector picks one for each group of its rocks).</summary>
    public const int Variants = 3;

    /// <summary>Depletion looks of the near mesh: chunks at full size, at 60 and at 30 percent (a mined rock changes level, not mesh geometry per rock).</summary>
    public const int Levels = 3;

    /// <summary>Depletion level from what is left: 0 above two thirds, 1 above one third, else 2.</summary>
    public static int LevelOf(int remaining, int initial) => (long)remaining * 3 > (long)initial * 2 ? 0 : (long)remaining * 3 > initial ? 1 : 2;

    private static int ShapeSeed(int cls, int variant) => 40 + cls * 7 + variant;

    /// <summary>The near mesh (rock with vein paint, plus ore chunks) of an ore at a size class, richness bucket (see <see cref="Bucket"/>) and depletion level.</summary>
    internal static ArrayMesh SharedNearMesh(string item, int cls, int bucket, int level, int variant)
    {
        string key = $"rf.near.{item}.{cls}.{bucket}.{level}.{variant}";
        if (Meshes.TryGetValue(key, out var cached)) return cached;
        var look = LookOf(item);
        int seed = ShapeSeed(cls, variant);
        var rock = new Models.Mb();
        Models.BuildRock(rock, look.Rock, seed, 1f, 0.28f, cls, look.ChunkDark.Lerp(look.Chunk, 0.35f), VeinWidth(bucket));
        var chunks = new Models.Mb();
        int density = bucket == 0 ? 45 : bucket == 1 ? 90 : 150; // the chunk count and size follow the bucket's typical density
        BuildChunks(chunks, look, density, seed, level == 0 ? 1f : level == 1 ? 0.6f : 0.3f, cls);
        var mesh = new ArrayMesh();
        rock.AddSurface(mesh, Materials.Rock(1f, 2.4f));
        chunks.AddSurface(mesh, ChunkMaterial(item, look, bucket));
        Meshes[key] = mesh;
        return mesh;
    }

    /// <summary>The far mesh of a size class: an icosphere of one subdivision (80 faces) in neutral grey, tinted per instance with <see cref="FarTint"/>.</summary>
    internal static ArrayMesh SharedFarMesh(int cls, int variant)
    {
        string key = $"rf.far.{cls}.{variant}";
        if (Meshes.TryGetValue(key, out var cached)) return cached;
        var mb = new Models.Mb();
        Models.BuildRock(mb, Colors.White, ShapeSeed(cls, variant), 1.6f, 1f, cls, null, 0f, levels: 1);
        var mesh = mb.ToMesh(Materials.Rock(1f, 2.4f));
        Meshes[key] = mesh;
        return mesh;
    }

    /// <summary>The colour the far mesh of an ore is tinted with: the rock's own colour with the ore's chunks and veins blended in, so the ore still reads from afar.</summary>
    internal static Color FarTint(string item, int bucket)
    {
        var look = LookOf(item);
        var rock = new Color(0.09f, 0.085f, 0.10f).Lerp(look.Rock, 0.28f);
        return rock.Lerp(look.Chunk, 0.22f + 0.10f * bucket) with { A = 1f };
    }

    /// <summary>The glow colour of an ore (the far impostors of a cluster).</summary>
    internal static Color GlowOf(string item) => LookOf(item).Glow;
}
