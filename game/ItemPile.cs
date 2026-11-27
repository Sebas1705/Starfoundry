using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The look of items lying about (dropped by hand, spat out of a rock): a little pile of the item's own model. The number of pieces grows with the
/// stack (1 for one item, up to 6 for sixty or more), flat things (ingots, plates, wafers) are stacked in layers and the rest lie side by side,
/// each piece with its own deterministic turn and variant (from the stack id), so two stacks of the same item do not look alike. One MultiMesh
/// per variant keeps a pile to at most three draw calls.
/// </summary>
public static class ItemPile
{
    /// <summary>Pieces drawn for a stack of <paramref name="count"/> items.</summary>
    public static int PiecesFor(int count) => count <= 1 ? 1 : count <= 3 ? 2 : count <= 9 ? 3 : count <= 24 ? 4 : count <= 59 ? 5 : 6;

    private static uint Hash(int a, int b) => unchecked((uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u) * 2654435761u;

    private static float Unit(uint h) => (h >> 8 & 0xFFFF) / 65535f;

    /// <summary>The placement of every piece: position (feet on y = 0), turn about the vertical and variant, in mesh units times <paramref name="scale"/>.</summary>
    public static (Vector3 Pos, float Yaw, int Variant)[] Layout(ItemModels.Shape shape, int seed, int pieces, float scale)
    {
        var res = new (Vector3, float, int)[pieces];
        float flat = ItemModels.StackHeight(shape) * scale;
        for (int i = 0; i < pieces; i++)
        {
            uint h = Hash(seed, i + 1);
            float yaw = (Unit(h) - 0.5f) * (flat > 0 ? 0.7f : Mathf.Tau);
            int variant = (int)(Hash(seed, i + 17) % ItemModels.Variants);
            Vector3 pos;
            if (flat > 0f)
            {
                // columns of up to three layers, side by side along z; the layers are a little off each other
                int tall = shape == ItemModels.Shape.Plate ? 4 : 3;
                int col = i / tall, layer = i % tall;
                int cols = (pieces + tall - 1) / tall;
                float z = (col - (cols - 1) / 2f) * 0.8f * scale;
                pos = new Vector3((Unit(Hash(seed, i + 41)) - 0.5f) * 0.08f * scale, layer * flat, z + (Unit(Hash(seed, i + 53)) - 0.5f) * 0.06f * scale);
            }
            else if (pieces == 1) pos = Vector3.Zero;
            else if (pieces == 2) pos = new Vector3(0f, 0f, (i == 0 ? -0.34f : 0.34f) * scale);
            else
            {
                // one in the middle (the first of a pile of three or more), the rest round it
                int ring = pieces - 1;
                float ang = Mathf.Tau * (i - 1) / ring + Unit(h) * 0.5f;
                pos = i == 0 ? Vector3.Zero : new Vector3(MathF.Cos(ang) * 0.55f * scale, 0f, MathF.Sin(ang) * 0.55f * scale);
                if (i == pieces - 1 && pieces >= 5) pos = new Vector3(0f, 0.38f * scale, 0f); // a heap: the last one sits on the others
            }
            res[i] = (pos, yaw, variant);
        }
        return res;
    }

    /// <summary>A node with the pile of an item: <paramref name="seed"/> picks the turns and variants (use the stack id).</summary>
    public static Node3D Build(string itemId, int seed, int count, float scale = ItemModels.GroundScale)
    {
        var (shape, tint) = ItemModels.StyleOf(itemId);
        return Build(shape, tint, seed, PiecesFor(count), scale);
    }

    public static Node3D Build(ItemModels.Shape shape, Color tint, int seed, int pieces, float scale)
    {
        var root = new Node3D { Name = "Pile" };
        var layout = Layout(shape, seed, pieces, scale);
        for (int v = 0; v < ItemModels.Variants; v++)
        {
            var mine = new List<(Vector3 Pos, float Yaw, int Variant)>();
            foreach (var p in layout) if (p.Variant == v) mine.Add(p);
            if (mine.Count == 0) continue;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = ItemModels.Mesh(shape, v), InstanceCount = mine.Count };
            for (int i = 0; i < mine.Count; i++)
            {
                mm.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, mine[i].Yaw).Scaled(Vector3.One * scale), mine[i].Pos));
                mm.SetInstanceColor(i, tint);
            }
            root.AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        return root;
    }
}
