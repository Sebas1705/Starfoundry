using System;
using System.Linq;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Models of the machines that cover several cells (ADR 0050): the large smelter (2 x 2), the long assembler (1 x 3), the L fabricator and the plus-shaped reactor core. The model is
/// built from the footprint, a list of cell offsets from the anchor already turned by the machine's own turn, so a turned machine needs no second model: a plinth under every cell,
/// the type's housing over the footprint's bounding centre and the animated parts ("Glow*") on it. The origin is the centre of the anchor cell, like every machine model; one-cell
/// models are not touched.
/// </summary>
public static partial class Models
{
    /// <summary>A machine model for a content id and the cells it covers (anchor first, offsets in cells). One cell gives the usual model.</summary>
    public static Node3D Machine(string machineId, (int Dx, int Dz)[] cells)
    {
        if (cells.Length <= 1) return Machine(machineId);
        var m = machineId switch
        {
            "core:large_smelter" => BuildLargeSmelter(cells),
            "core:long_assembler" => BuildLongAssembler(cells),
            "core:l_fabricator" => BuildFabricator(cells),
            "core:reactor_core" => BuildReactorCore(cells),
            _ => BuildFootprintCrate(cells),
        };
        AddStatusLamp(m);
        return m;
    }

    /// <summary>The footprint of a multi-cell machine as the content declares it (turn 0), for the previews and the gallery, which have no content at hand; one cell for the rest.</summary>
    public static (int Dx, int Dz)[] PreviewCells(string machineId) => machineId switch
    {
        "core:large_smelter" => new[] { (0, 0), (1, 0), (0, 1), (1, 1) },
        "core:long_assembler" => new[] { (0, 0), (1, 0), (2, 0) },
        "core:l_fabricator" => new[] { (0, 0), (0, 1), (0, 2), (1, 2) },
        "core:reactor_core" => new[] { (0, 0), (-1, 1), (0, 1), (1, 1), (0, 2) },
        _ => new[] { (0, 0) },
    };

    private static string FootKey(string name, (int Dx, int Dz)[] cells) => name + "." + string.Join("_", cells.Select(c => $"{c.Dx},{c.Dz}"));

    /// <summary>The centre of the footprint and its extent in cells along X and Z.</summary>
    private static (float Cx, float Cz, float W, float D) FootBox((int Dx, int Dz)[] cells)
    {
        int x0 = cells.Min(c => c.Dx), x1 = cells.Max(c => c.Dx), z0 = cells.Min(c => c.Dz), z1 = cells.Max(c => c.Dz);
        return ((x0 + x1) / 2f, (z0 + z1) / 2f, x1 - x0 + 1, z1 - z0 + 1);
    }

    private static void FootPlinth(Mb mb, (int Dx, int Dz)[] cells, Color accent)
    {
        var set = cells.ToHashSet();
        foreach (var (dx, dz) in cells)
        {
            mb.C(Hull).Block(new(dx, Y0 - 0.07f, dz), new(0.98f, 0.14f, 0.98f));
            mb.C(accent).Block(new(dx, Y0 - 0.005f, dz), new(0.90f, 0.01f, 0.90f));
            // a seam strip towards each covered neighbour so the plinths read as one slab
            if (set.Contains((dx + 1, dz))) mb.C(Hull).Block(new(dx + 0.5f, Y0 - 0.07f, dz), new(0.06f, 0.14f, 0.98f));
            if (set.Contains((dx, dz + 1))) mb.C(Hull).Block(new(dx, Y0 - 0.07f, dz + 0.5f), new(0.98f, 0.14f, 0.06f));
        }
    }

    private static Node3D BuildLargeSmelter((int Dx, int Dz)[] cells)
    {
        var r = NewMachine("LargeSmelter");
        var (cx, cz, w, d) = FootBox(cells);
        Add(r, "Body", FootKey("lsmelter.body", cells), Body(), mb =>
        {
            FootPlinth(mb, cells, Red);
            mb.C(Red).Block(new(cx, Y0 + 0.26f, cz), new(w - 0.18f, 0.52f, d - 0.18f), 0.9f, 0.9f);
            mb.C(Hull).Block(new(cx, Y0 + 0.54f, cz), new(w - 0.45f, 0.03f, d - 0.45f));
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 3; k++)
                    mb.C(RedDk).Block(new(cx, Y0 + 0.09f + k * 0.11f, cz + s * (d / 2f - 0.07f)), new(w - 0.5f, 0.04f, 0.03f));
            // two stacks and a hopper
            mb.C(Steel).Prism(new(cx - 0.32f, Y0 + 0.52f, cz - 0.30f), 0.17f, 0.12f, 0.55f, 8);
            mb.C(Hull).Prism(new(cx - 0.32f, Y0 + 1.07f, cz - 0.30f), 0.14f, 0.14f, 0.04f, 8);
            mb.C(Steel).Prism(new(cx + 0.18f, Y0 + 0.52f, cz - 0.30f), 0.12f, 0.09f, 0.42f, 8);
            mb.C(RedDk).Block(new(cx + 0.22f, Y0 + 0.66f, cz + 0.28f), new(0.5f, 0.28f, 0.4f), 0.6f, 0.6f);
        });
        Add(r, "GlowMouth", FootKey("lsmelter.mouth", cells), Glow(new Color(1f, 0.52f, 0.1f), 3.2f), mb =>
        {
            mb.Block(new(cx + w / 2f - 0.07f, Y0 + 0.20f, cz), new(0.03f, 0.26f, d * 0.5f));
            mb.Block(new(cx, Y0 + 0.56f, cz), new(w * 0.4f, 0.012f, 0.08f));
        });
        Add(r, "GlowRim", FootKey("lsmelter.rim", cells), Glow(new Color(1f, 0.55f, 0.15f), 2.8f), mb => mb.Prism(new(cx - 0.32f, Y0 + 1.115f, cz - 0.30f), 0.10f, 0.10f, 0.012f, 8));
        var ram = Pivot(r, "Piston", new(cx + 0.1f, Y0 + 0.60f, cz));
        Add(ram, "RamMesh", "lsmelter.ram", Body(), mb =>
        {
            mb.C(Steel).Prism(new(0, 0, 0), 0.05f, 0.05f, 0.16f, 6);
            mb.C(RedDk).Block(new(0, 0.17f, 0), new(0.18f, 0.07f, 0.18f));
        });
        return r;
    }

    private static Node3D BuildLongAssembler((int Dx, int Dz)[] cells)
    {
        var r = NewMachine("LongAssembler");
        var (cx, cz, w, d) = FootBox(cells);
        bool alongX = w >= d;
        Add(r, "Body", FootKey("lasm.body", cells), Body(), mb =>
        {
            FootPlinth(mb, cells, Blue);
            mb.C(BlueDk).Block(new(cx, Y0 + 0.08f, cz), new(w - 0.1f, 0.16f, d - 0.1f), 0.95f, 0.95f);
            // two gantry rails the whole length and a post every cell
            foreach (float s in new[] { -1f, 1f })
            {
                var a = alongX ? new Vector3(cx - w / 2f + 0.1f, Y0 + 0.62f, cz + s * 0.36f) : new Vector3(cx + s * 0.36f, Y0 + 0.62f, cz - d / 2f + 0.1f);
                var b = alongX ? new Vector3(cx + w / 2f - 0.1f, Y0 + 0.62f, cz + s * 0.36f) : new Vector3(cx + s * 0.36f, Y0 + 0.62f, cz + d / 2f - 0.1f);
                mb.C(Blue).Beam(a, b, 0.08f);
            }
            foreach (var (dx, dz) in cells)
                foreach (float s in new[] { -1f, 1f })
                    mb.C(Blue).Block(alongX ? new(dx, Y0 + 0.38f, cz + s * 0.36f) : new(cx + s * 0.36f, Y0 + 0.38f, dz), new(0.09f, 0.48f, 0.09f), 0.9f, 0.9f);
            mb.C(Hull).Block(new(cx, Y0 + 0.19f, cz), alongX ? new(w - 0.3f, 0.06f, 0.22f) : new(0.22f, 0.06f, d - 0.3f)); // the work belt
        });
        Add(r, "GlowStrip", FootKey("lasm.strip", cells), Glow(new Color(0.4f, 0.8f, 1f), 2.6f), mb =>
            mb.Block(new(cx, Y0 + 0.225f, cz), alongX ? new(w - 0.34f, 0.01f, 0.05f) : new(0.05f, 0.01f, d - 0.34f)));
        int i = 0;
        foreach (var (dx, dz) in cells) // one arm per cell
        {
            var arm = Pivot(r, i == 0 ? "Piston" : "Piston" + (i + 1), new(dx, Y0 + 0.56f, dz));
            Add(arm, "ArmMesh", "lasm.arm", Body(), mb =>
            {
                mb.C(Steel).Prism(new(0, 0, 0), 0.04f, 0.04f, 0.14f, 6);
                mb.C(BlueDk).Block(new(0, -0.1f, 0), new(0.1f, 0.06f, 0.1f));
            });
            i++;
        }
        return r;
    }

    private static Node3D BuildFabricator((int Dx, int Dz)[] cells)
    {
        var r = NewMachine("LFabricator");
        Add(r, "Body", FootKey("fab.body", cells), Body(), mb =>
        {
            FootPlinth(mb, cells, Purple);
            foreach (var (dx, dz) in cells)
            {
                mb.C(PurpleDk).Block(new(dx, Y0 + 0.07f, dz), new(0.84f, 0.14f, 0.84f), 0.95f, 0.95f);
                mb.C(Purple).Prism(new(dx, Y0 + 0.14f, dz), 0.30f, 0.27f, 0.42f, 10); // a tank on every cell
                mb.C(Hull).Prism(new(dx, Y0 + 0.56f, dz), 0.22f, 0.22f, 0.04f, 10);
            }
            foreach (var (dx, dz) in cells) // pipes between neighbouring tanks
            {
                if (cells.Contains((dx + 1, dz))) mb.C(Steel).Beam(new(dx, Y0 + 0.42f, dz), new(dx + 1, Y0 + 0.42f, dz), 0.07f);
                if (cells.Contains((dx, dz + 1))) mb.C(Steel).Beam(new(dx, Y0 + 0.42f, dz), new(dx, Y0 + 0.42f, dz + 1), 0.07f);
            }
        });
        Add(r, "GlowRing", FootKey("fab.ring", cells), Glow(new Color(0.8f, 0.5f, 1f), 2.8f), mb =>
        {
            foreach (var (dx, dz) in cells) mb.Prism(new(dx, Y0 + 0.50f, dz), 0.285f, 0.285f, 0.015f, 10);
        });
        return r;
    }

    private static Node3D BuildReactorCore((int Dx, int Dz)[] cells)
    {
        var r = NewMachine("ReactorCore");
        var (cx, cz, _, _) = FootBox(cells);
        Add(r, "Body", FootKey("rcore.body", cells), Body(), mb =>
        {
            FootPlinth(mb, cells, Green);
            foreach (var (dx, dz) in cells)
            {
                mb.C(GreenDk).Block(new(dx, Y0 + 0.08f, dz), new(0.84f, 0.16f, 0.84f), 0.95f, 0.95f);
                if (Math.Abs(dx - cx) + Math.Abs(dz - cz) > 0.01f) // the arms: a cooling drum on each, a spoke to the centre
                {
                    mb.C(Steel).Prism(new(dx, Y0 + 0.16f, dz), 0.26f, 0.24f, 0.34f, 8);
                    mb.C(Green).Prism(new(dx, Y0 + 0.50f, dz), 0.27f, 0.27f, 0.03f, 8);
                    mb.C(Hull).Beam(new(dx, Y0 + 0.32f, dz), new(cx, Y0 + 0.32f, cz), 0.09f);
                }
            }
            mb.C(Hull).Prism(new(cx, Y0 + 0.16f, cz), 0.40f, 0.36f, 0.50f, 12); // the vessel
            mb.C(Steel).Prism(new(cx, Y0 + 0.64f, cz), 0.34f, 0.30f, 0.06f, 12);
        });
        Add(r, "GlowCore", FootKey("rcore.glow", cells), Glow(new Color(0.35f, 1f, 0.6f), 3.6f), mb => mb.Sphere(new(cx, Y0 + 0.40f, cz), new(0.22f, 0.22f, 0.22f), 3, 10));
        Add(r, "GlowRing", FootKey("rcore.ring", cells), Glow(new Color(0.5f, 1f, 0.8f), 2.6f), mb => mb.Prism(new(cx, Y0 + 0.66f, cz), 0.31f, 0.31f, 0.012f, 12));
        return r;
    }

    private static Node3D BuildFootprintCrate((int Dx, int Dz)[] cells)
    {
        var r = NewMachine("FootprintCrate");
        Add(r, "Body", FootKey("crate.body", cells), Body(), mb =>
        {
            FootPlinth(mb, cells, Yellow);
            foreach (var (dx, dz) in cells) mb.C(Steel).Block(new(dx, Y0 + 0.25f, dz), new(0.8f, 0.5f, 0.8f));
        });
        return r;
    }
}
