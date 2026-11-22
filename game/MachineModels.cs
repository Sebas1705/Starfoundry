using System;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The machines added to <see cref="Models"/> by the model round: the hydrogen cracker, greenhouse, engineering bench, physics lab,
/// micro refinery and the item-specific unloaders (ports), plus the shared status lamp. Same conventions as Models.cs: origin = centre of a
/// 1x1x1 cell, base at y = -0.45, output facing +X, parts named "Glow*" pulse with the machine, "Piston*", "Fan", "Rocker", "Ring", "Spinner"
/// are animated by <see cref="Models.Animate"/>.
///
/// Colour language for the science machines follows the science items: engineering orange, biology green, physics cyan.
/// </summary>
public static partial class Models
{
    private static readonly Color Teal = new(0.15f, 0.72f, 0.75f);
    private static readonly Color TealDk = new(0.08f, 0.40f, 0.44f);
    private static readonly Color Soil = new(0.30f, 0.20f, 0.12f);
    private static readonly Color Ice = new(0.62f, 0.90f, 1f);

    /// <summary>Adds the status lamp (a pole and a bulb at the back-left corner): green working, amber idle, red blinking without power.</summary>
    private static void AddStatusLamp(Node3D r)
    {
        Add(r, "StatusPole", "status.pole", Body(), mb => mb.C(Steel).Prism(new(-0.405f, -0.39f, -0.405f), 0.022f, 0.016f, 0.2f, 5), default);
        Add(r, "GlowStatus", "status.lamp", Glow(new Color(0.3f, 1f, 0.4f), 3.4f), mb => mb.Sphere(new(-0.405f, -0.17f, -0.405f), new(0.055f, 0.05f, 0.055f), 3, 7));
    }


    // ---- unloaders ("ports"): one look per kind of cargo they pull out of the hold ---------------------

    private enum UnloaderKind { Ingot, IronOre, TitaniumOre, NickelOre, Ice, Quartz, Carbon }

    private static Node3D BuildUnloader(UnloaderKind kind)
    {
        // heap colour, accent colour, whether the heap is crystals, whether it glows
        (Color heap, Color accent, bool crystal) = kind switch
        {
            UnloaderKind.IronOre => (new Color(0.62f, 0.33f, 0.20f), Orange, false),
            UnloaderKind.TitaniumOre => (new Color(0.50f, 0.58f, 0.74f), new Color(0.55f, 0.65f, 0.95f), false),
            UnloaderKind.NickelOre => (new Color(0.45f, 0.62f, 0.52f), Teal, false),
            UnloaderKind.Ice => (Ice, new Color(0.45f, 0.85f, 1f), true),
            UnloaderKind.Quartz => (new Color(0.96f, 0.76f, 0.88f), new Color(0.95f, 0.55f, 0.80f), true),
            UnloaderKind.Carbon => (new Color(0.12f, 0.12f, 0.14f), new Color(0.55f, 0.55f, 0.60f), false),
            _ => (new Color(0.78f, 0.80f, 0.86f), Green, false),
        };
        string k = "unloader." + kind;
        var r = NewMachine("Unloader");
        Add(r, "Body", k + ".body", Body(), mb =>
        {
            Plinth(mb, accent);
            mb.C(GreenDk).Block(new(-0.10f, Y0 + 0.14f, 0), new(0.60f, 0.28f, 0.70f), 0.94f, 0.94f);
            mb.C(accent).Block(new(-0.10f, Y0 + 0.055f, 0), new(0.62f, 0.035f, 0.72f), 0.94f, 0.94f); // port colour band
            mb.C(Hull).Block(new(-0.10f, Y0 + 0.29f, 0), new(0.44f, 0.04f, 0.50f));
            // hold hatch on the roof: a dark frame with hazard corners and the suction grate
            mb.C(Hull).Block(new(-0.14f, Y0 + 0.315f, 0), new(0.34f, 0.015f, 0.34f));
            // a chute towards +X where the belt starts, with side walls and a lip
            mb.C(Steel).Block(new(0.30f, Y0 + 0.12f, 0), new(0.30f, 0.14f, 0.30f), 0.8f, 0.8f);
            mb.C(Panel).Block(new(0.30f, Y0 + 0.20f, 0), new(0.34f, 0.025f, 0.34f));
            foreach (float s in new[] { -1f, 1f })
            {
                mb.C(Steel).Block(new(0.30f, Y0 + 0.245f, s * 0.165f), new(0.34f, 0.07f, 0.025f));
                mb.C(Steel).Prism(new(-0.30f, Y0, 0.30f * s), 0.05f, 0.04f, 0.40f, 6);
            }
            mb.C(Yellow).Block(new(-0.43f, Y0 + 0.14f, 0), new(0.012f, 0.18f, 0.20f)); // hazard plate on the back
            // the cargo that comes out: a small heap on the pad, shaped like the item
            var rng = new Rng(kind.GetHashCode() + 7);
            for (int i = 0; i < 7; i++)
            {
                float x = 0.24f + rng.F() * 0.13f, z = (rng.F() - 0.5f) * 0.20f, s = 0.045f + rng.F() * 0.035f;
                float y = Y0 + 0.2125f + s / 2 + (i > 4 ? 0.04f : 0f);
                mb.C(crystal ? heap : new Color(heap.R * (0.8f + 0.4f * rng.F()), heap.G * (0.8f + 0.4f * rng.F()), heap.B * (0.8f + 0.4f * rng.F())));
                if (crystal) mb.Prism(new(x, y - s / 2, z), s * 0.7f, 0f, s * 2.4f, 4, rng.F() * 90f);
                else if (kind == UnloaderKind.Ingot) mb.Block(new(x, y, z), new(s * 1.9f, s * 0.8f, s * 1.0f), 0.85f, 0.9f);
                else mb.Prism(new(x, y - s / 2, z), s * 0.95f, s * 0.55f, s, 5, rng.F() * 70f);
            }
        });
        // grate fan on the hold hatch: spins while an item is being pulled out
        var fan = Pivot(r, "Fan", new(-0.14f, Y0 + 0.335f, 0));
        Add(fan, "FanMesh", "unloader.fan", Body(), mb =>
        {
            mb.C(Hull).Prism(new(0, -0.015f, 0), 0.15f, 0.15f, 0.018f, 8);
            mb.C(Panel);
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Tau * i / 3;
                mb.Beam(new(0, 0.012f, 0), new(MathF.Cos(a) * 0.13f, 0.012f, MathF.Sin(a) * 0.13f), 0.035f);
            }
            mb.C(accent).Prism(new(0, 0.0f, 0), 0.03f, 0.03f, 0.03f, 6);
        });
        Add(r, "GlowLight", k + ".light", Glow(accent, 2.4f), mb =>
        {
            mb.Sphere(new(-0.30f, Y0 + 0.43f, 0.30f), new(0.04f, 0.03f, 0.04f), 3, 6);
            mb.Sphere(new(-0.30f, Y0 + 0.43f, -0.30f), new(0.04f, 0.03f, 0.04f), 3, 6);
            mb.Block(new(0.30f, Y0 + 0.215f, 0.11f), new(0.16f, 0.012f, 0.03f));
            mb.Block(new(0.30f, Y0 + 0.215f, -0.11f), new(0.16f, 0.012f, 0.03f));
        });
        if (crystal)
            Add(r, "GlowItem", k + ".crystal", Glow(heap, 1.6f), mb =>
            {
                var rng = new Rng(31);
                for (int i = 0; i < 3; i++)
                    mb.Prism(new(0.24f + rng.F() * 0.12f, Y0 + 0.215f, (rng.F() - 0.5f) * 0.18f), 0.02f, 0f, 0.09f + rng.F() * 0.05f, 4);
            });
        return r;
    }

    // ---- hydrogen cracker -----------------------------------------------------------------------------------

    /// <summary>The hydrogen cracker: a reaction sphere with a cooling fan and a tall fractionating column joined by a catwalk pipe; cyan bands breathe while it works.</summary>
    private static Node3D BuildCracker()
    {
        var r = NewMachine("Cracker");
        Add(r, "Body", "cracker2.body", Body(), mb =>
        {
            Plinth(mb, Blue);
            // reaction sphere on a ring of legs
            mb.C(BlueDk).Sphere(new(-0.14f, Y0 + 0.31f, 0.10f), new(0.29f, 0.27f, 0.29f), 4, 10);
            mb.C(Steel);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + 0.78f;
                mb.Beam(new(-0.14f + MathF.Cos(a) * 0.27f, Y0, 0.10f + MathF.Sin(a) * 0.27f), new(-0.14f + MathF.Cos(a) * 0.18f, Y0 + 0.2f, 0.10f + MathF.Sin(a) * 0.18f), 0.045f);
            }
            mb.C(Panel).Prism(new(-0.14f, Y0 + 0.55f, 0.10f), 0.11f, 0.08f, 0.05f, 8); // fan housing
            // the tall column: stacked drums narrowing upwards, with cage rungs
            mb.C(Steel).Prism(new(0.20f, Y0, -0.20f), 0.17f, 0.15f, 0.40f, 10);
            mb.C(Panel).Prism(new(0.20f, Y0 + 0.40f, -0.20f), 0.15f, 0.12f, 0.30f, 10);
            mb.C(Steel).Prism(new(0.20f, Y0 + 0.70f, -0.20f), 0.12f, 0.07f, 0.16f, 10);
            mb.C(Hull).Prism(new(0.20f, Y0 + 0.86f, -0.20f), 0.075f, 0.05f, 0.06f, 8);
            mb.C(Blue);
            foreach (float y in new[] { 0.15f, 0.40f, 0.68f }) mb.Prism(new(0.20f, Y0 + y, -0.20f), 0.172f - y * 0.06f, 0.172f - y * 0.06f, 0.035f, 10);
            // catwalk pipe between the sphere and the column, and an output block at +X
            mb.C(Steel).Beam(new(-0.12f, Y0 + 0.42f, 0.10f), new(0.20f, Y0 + 0.55f, -0.20f), 0.05f);
            mb.C(Panel).Beam(new(0.20f, Y0 + 0.2f, -0.05f), new(0.40f, Y0 + 0.14f, 0.0f), 0.045f);
            mb.C(Blue).Block(new(0.36f, Y0 + 0.10f, 0.10f), new(0.18f, 0.12f, 0.2f), 0.85f, 0.85f);
        });
        var fan = Pivot(r, "Fan", new(-0.14f, Y0 + 0.585f, 0.10f));
        Add(fan, "FanMesh", "cracker.fan", Body(), mb =>
        {
            mb.C(Hull).Prism(new(0, -0.01f, 0), 0.03f, 0.03f, 0.03f, 6);
            mb.C(Steel);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4;
                mb.Beam(new(0, 0.01f, 0), new(MathF.Cos(a) * 0.10f, 0.01f, MathF.Sin(a) * 0.10f), 0.03f);
            }
        });
        Add(r, "GlowRim", "cracker2.ring", Glow(new Color(0.4f, 0.9f, 1f), 2.8f), mb =>
        {
            mb.Torus(new(-0.14f, Y0 + 0.31f, 0.10f), 0.292f, 0.016f, 12, 3);
            mb.Prism(new(0.20f, Y0 + 0.575f, -0.20f), 0.12f, 0.12f, 0.02f, 10);
            mb.Prism(new(0.20f, Y0 + 0.27f, -0.20f), 0.15f, 0.15f, 0.015f, 10);
        });
        Add(r, "GlowLight", "cracker2.light", Glow(new Color(0.45f, 1f, 0.9f), 3f), mb =>
        {
            mb.Block(new(0.455f, Y0 + 0.14f, 0.1f), new(0.012f, 0.05f, 0.08f));
            mb.Sphere(new(0.20f, Y0 + 0.93f, -0.20f), new(0.05f, 0.05f, 0.05f), 3, 6); // flare tip
        });
        return r;
    }

    // ---- greenhouse ---------------------------------------------------------------------------------------------

    private static Node3D BuildGreenhouse()
    {
        var r = NewMachine("Greenhouse");
        Add(r, "Body", "greenhouse.body", Body(), mb =>
        {
            Plinth(mb, Green);
            mb.C(GreenDk).Block(new(0, Y0 + 0.05f, 0), new(0.78f, 0.10f, 0.78f), 0.96f, 0.96f);
            mb.C(Soil).Block(new(0, Y0 + 0.105f, 0), new(0.62f, 0.02f, 0.62f));
            // frame: four corner posts and a ring beam under the glass
            mb.C(Steel);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    mb.Beam(new(sx * 0.33f, Y0 + 0.10f, sz * 0.33f), new(sx * 0.27f, Y0 + 0.46f, sz * 0.27f), 0.04f);
            mb.C(Panel).Block(new(0, Y0 + 0.46f, 0), new(0.60f, 0.03f, 0.60f), 0.95f, 0.95f);
            // plants: stalks with leaf cones and a few spheres of fruit, heights vary
            var rng = new Rng(5);
            for (int i = 0; i < 9; i++)
            {
                float x = -0.22f + (i % 3) * 0.22f + (rng.F() - 0.5f) * 0.05f, z = -0.22f + (i / 3) * 0.22f + (rng.F() - 0.5f) * 0.05f;
                float h = 0.14f + rng.F() * 0.16f;
                mb.C(GreenDk).Prism(new(x, Y0 + 0.115f, z), 0.014f, 0.01f, h, 4);
                mb.C(i % 2 == 0 ? Green : new Color(0.55f, 0.88f, 0.30f)).Prism(new(x, Y0 + 0.115f + h * 0.55f, z), 0.075f, 0f, h * 0.9f, 5, rng.F() * 70f);
                if (i % 3 == 1) mb.C(new Color(0.95f, 0.45f, 0.30f)).Sphere(new(x + 0.04f, Y0 + 0.115f + h * 0.5f, z), new(0.03f, 0.03f, 0.03f), 2, 5);
            }
            // water tank and pipe at the back, with a blue band
            mb.C(Steel).Prism(new(-0.35f, Y0 + 0.10f, 0.0f), 0.085f, 0.085f, 0.36f, 8);
            mb.C(Blue).Prism(new(-0.35f, Y0 + 0.24f, 0.0f), 0.09f, 0.09f, 0.04f, 8);
            mb.C(Steel).Beam(new(-0.35f, Y0 + 0.44f, 0), new(-0.20f, Y0 + 0.40f, 0.12f), 0.03f);
        });
        // the glass: a pulsing translucent dome, so the plants stay visible
        Add(r, "GlowDome", "greenhouse.glass", GlowAlpha(new Color(0.55f, 1f, 0.75f), 0.5f, 0.16f), mb =>
            mb.Sphere(new(0, Y0 + 0.46f, 0), new(0.34f, 0.28f, 0.34f), 4, 10, true));
        // grow lights: a magenta bar under the roof ring
        Add(r, "GlowLight", "greenhouse.grow", Glow(new Color(1f, 0.35f, 0.85f), 3f), mb =>
        {
            mb.Block(new(0.0f, Y0 + 0.43f, 0.12f), new(0.36f, 0.014f, 0.04f));
            mb.Block(new(0.0f, Y0 + 0.43f, -0.12f), new(0.36f, 0.014f, 0.04f));
            mb.Block(new(0.405f, Y0 + 0.14f, 0.0f), new(0.012f, 0.06f, 0.10f));
        });
        var fan = Pivot(r, "Fan", new(0f, Y0 + 0.755f, 0f));
        Add(fan, "FanMesh", "greenhouse.fan", Body(), mb =>
        {
            mb.C(Hull).Prism(new(0, -0.03f, 0), 0.05f, 0.04f, 0.03f, 6);
            mb.C(Panel);
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Tau * i / 3;
                mb.Beam(new(0, 0, 0), new(MathF.Cos(a) * 0.12f, 0, MathF.Sin(a) * 0.12f), 0.035f);
            }
        });
        Add(r, "Vent", "greenhouse.vent", Body(), mb => mb.C(Steel).Prism(new(0, Y0 + 0.62f, 0), 0.07f, 0.06f, 0.10f, 8));
        return r;
    }

    // ---- engineering bench ------------------------------------------------------------------------------------

    private static Node3D BuildBench()
    {
        var r = NewMachine("EngineeringBench");
        Add(r, "Body", "bench.body", Body(), mb =>
        {
            Plinth(mb, Orange);
            // a long low workbench: legs, a thick top, a vise and an anvil
            mb.C(OrangeDk).Block(new(0.10f, Y0 + 0.10f, 0.02f), new(0.62f, 0.20f, 0.56f), 0.96f, 0.96f);
            mb.C(Steel).Block(new(0.10f, Y0 + 0.215f, 0.02f), new(0.66f, 0.03f, 0.60f));
            mb.C(Hull).Block(new(0.10f, Y0 + 0.235f, 0.02f), new(0.40f, 0.012f, 0.30f)); // the work mat
            mb.C(Panel).Block(new(0.34f, Y0 + 0.27f, 0.22f), new(0.10f, 0.08f, 0.09f)); // vise
            mb.C(Yellow).Block(new(0.34f, Y0 + 0.315f, 0.22f), new(0.11f, 0.012f, 0.025f));
            mb.C(Hull).Block(new(0.24f, Y0 + 0.25f, -0.18f), new(0.14f, 0.05f, 0.07f), 0.7f, 1f); // anvil
            // tool wall at the back with hanging tools
            mb.C(HullLight).Block(new(-0.12f, Y0 + 0.32f, -0.39f), new(0.70f, 0.50f, 0.04f));
            mb.C(Orange).Block(new(-0.12f, Y0 + 0.57f, -0.39f), new(0.72f, 0.04f, 0.06f));
            for (int i = 0; i < 5; i++)
            {
                mb.C(i % 2 == 0 ? Steel : Panel).Block(new(-0.38f + i * 0.14f, Y0 + 0.30f, -0.355f), new(0.025f, 0.20f - (i % 3) * 0.03f, 0.02f));
                mb.C(Yellow).Block(new(-0.38f + i * 0.14f, Y0 + 0.40f - (i % 3) * 0.015f, -0.355f), new(0.045f, 0.035f, 0.025f));
            }
            // base column of the swing arm, and a parts bin with crates
            mb.C(Steel).Prism(new(-0.30f, Y0, 0.20f), 0.10f, 0.08f, 0.30f, 8);
            mb.C(Orange).Prism(new(-0.30f, Y0 + 0.30f, 0.20f), 0.105f, 0.105f, 0.03f, 8);
            mb.C(Crate()).Block(new(-0.28f, Y0 + 0.09f, -0.12f), new(0.2f, 0.16f, 0.2f));
            mb.C(Yellow).Block(new(-0.28f, Y0 + 0.17f, -0.12f), new(0.21f, 0.02f, 0.21f));
        });
        // the swing arm: shoulder pivot rocks around Z; the forearm carries a welding head with a spark
        var arm = Pivot(r, "Rocker", new(-0.30f, Y0 + 0.40f, 0.20f));
        Add(arm, "RockerMesh", "bench.arm", Body(), mb =>
        {
            mb.C(Panel).Prism(new(0, -0.03f, 0), 0.07f, 0.07f, 0.06f, 8);
            mb.C(Orange).Beam(new(0, 0, 0), new(0.34f, 0.12f, -0.02f), 0.06f);
            mb.C(Steel).Prism(new(0.34f, 0.07f, -0.02f), 0.05f, 0.05f, 0.1f, 6);
            mb.C(Panel).Beam(new(0.34f, 0.06f, -0.02f), new(0.38f, -0.12f, -0.02f), 0.04f);
            mb.C(Hull).Block(new(0.38f, -0.14f, -0.02f), new(0.06f, 0.05f, 0.06f));
        });
        Add(arm, "GlowSpark", "bench.spark", Glow(new Color(0.6f, 0.85f, 1f), 4.5f), mb => mb.Sphere(new(0.385f, -0.185f, -0.02f), new(0.035f, 0.03f, 0.035f), 2, 5));
        Add(r, "GlowLight", "bench.light", Glow(new Color(1f, 0.6f, 0.15f), 3f), mb =>
        {
            mb.Block(new(0.435f, Y0 + 0.12f, 0.15f), new(0.012f, 0.04f, 0.06f));
            mb.Block(new(-0.12f, Y0 + 0.55f, -0.355f), new(0.60f, 0.012f, 0.014f)); // lamp strip over the tool wall
        });
        Add(r, "GlowItem", "bench.item", Glow(new Color(0.4f, 0.95f, 1f), 2.2f), mb => mb.Block(new(0.12f, Y0 + 0.25f, 0.02f), new(0.09f, 0.03f, 0.09f), 0.8f, 0.8f));
        return r;
    }

    private static Color Crate() => new(0.62f, 0.40f, 0.22f);

    // ---- physics lab -------------------------------------------------------------------------------------------

    private static Node3D BuildPhysicsLab()
    {
        var r = NewMachine("PhysicsLab");
        Add(r, "Body", "physlab.body", Body(), mb =>
        {
            Plinth(mb, Teal);
            mb.C(TealDk).Prism(new(0, Y0, 0), 0.40f, 0.37f, 0.12f, 12);
            mb.C(Steel).Prism(new(0, Y0 + 0.12f, 0), 0.38f, 0.38f, 0.03f, 12);
            // the collider track: a dark toroid on short posts, with four magnet blocks round it
            mb.C(Hull).Torus(new(0, Y0 + 0.215f, 0), 0.30f, 0.045f, 16, 5);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                float x = MathF.Cos(a) * 0.30f, z = MathF.Sin(a) * 0.30f;
                mb.C(Teal).Block(new(x, Y0 + 0.215f, z), new(0.13f, 0.13f, 0.13f), 0.9f, 0.9f);
                mb.C(Panel).Beam(new(x, Y0 + 0.15f, z), new(x * 0.7f, Y0 + 0.12f, z * 0.7f), 0.035f);
            }
            // central tower with a cap, and a cryo tank with a gauge
            mb.C(Steel).Prism(new(0, Y0 + 0.15f, 0), 0.09f, 0.07f, 0.34f, 8);
            mb.C(Teal).Prism(new(0, Y0 + 0.27f, 0), 0.095f, 0.095f, 0.03f, 8);
            mb.C(Panel).Prism(new(-0.30f, Y0 + 0.12f, -0.30f), 0.075f, 0.075f, 0.26f, 8);
            mb.C(Blue).Prism(new(-0.30f, Y0 + 0.23f, -0.30f), 0.08f, 0.08f, 0.035f, 8);
            mb.C(Steel).Prism(new(0.30f, Y0 + 0.12f, -0.34f), 0.025f, 0.015f, 0.55f, 5); // antenna
        });
        // beads running round the track (the Ring spins) and a halo round the tower (the Spinner)
        var ring = Pivot(r, "Ring", new(0, Y0 + 0.215f, 0));
        Add(ring, "GlowRing", "physlab.beads", Glow(new Color(0.5f, 1f, 1f), 4.5f), mb =>
        {
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Tau * i / 3;
                mb.Sphere(new(MathF.Cos(a) * 0.30f, 0.055f, MathF.Sin(a) * 0.30f), new(0.032f, 0.032f, 0.032f), 2, 6);
            }
            mb.Torus(new(0, 0.052f, 0), 0.30f, 0.008f, 16, 3);
        });
        var spin = Pivot(r, "Spinner", new(0, Y0 + 0.62f, 0));
        Add(spin, "GlowSpin", "physlab.halo", Glow(new Color(0.8f, 0.5f, 1f), 3f), mb => mb.Torus(Vector3.Zero, 0.14f, 0.013f, 12, 3), default, Deg(0, 0, 90));
        Add(r, "GlowCore", "physlab.core", Glow(new Color(0.55f, 1f, 1f), 3.4f), mb =>
        {
            mb.Sphere(new(0, Y0 + 0.62f, 0), new(0.075f, 0.075f, 0.075f), 3, 8);
            mb.Prism(new(0, Y0 + 0.43f, 0), 0.018f, 0.018f, 0.12f, 5);
        });
        Add(r, "GlowLight", "physlab.light", Glow(new Color(0.4f, 1f, 0.95f), 3f), mb =>
        {
            mb.Block(new(0.455f, Y0 + 0.14f, 0.1f), new(0.012f, 0.05f, 0.08f));
            mb.Sphere(new(0.30f, Y0 + 0.83f, -0.34f), new(0.03f, 0.03f, 0.03f), 2, 5);
        });
        return r;
    }

    // ---- micro refinery ----------------------------------------------------------------------------------------------

    private static Node3D BuildMicroRefinery()
    {
        var r = NewMachine("MicroRefinery");
        Add(r, "Body", "refinery.body", Body(), mb =>
        {
            Plinth(mb, Orange);
            mb.C(White).Block(new(-0.05f, Y0 + 0.06f, 0), new(0.78f, 0.12f, 0.64f), 0.96f, 0.96f);
            // a horizontal drum furnace along X with orange bands, a hopper at the back and a flue
            mb.At(default, Deg(0, 0, -90));
            mb.C(Panel).Prism(new(0.11f, -0.31f, 0), 0.22f, 0.22f, 0.62f, 10);
            mb.C(Orange).Prism(new(0.11f, -0.20f, 0), 0.232f, 0.232f, 0.05f, 10);
            mb.C(Orange).Prism(new(0.11f, 0.17f, 0), 0.232f, 0.232f, 0.05f, 10);
            mb.At(default);
            mb.C(Steel).Prism(new(-0.30f, Y0 + 0.40f, 0), 0.14f, 0.05f, 0.14f, 8); // hopper funnel
            mb.C(Hull).Prism(new(0.05f, Y0 + 0.40f, 0.1f), 0.06f, 0.045f, 0.28f, 8);
            mb.C(Orange).Prism(new(0.05f, Y0 + 0.68f, 0.1f), 0.07f, 0.07f, 0.03f, 8);
            foreach (float s in new[] { -1f, 1f }) mb.C(Steel).Block(new(-0.05f, Y0 + 0.14f, s * 0.20f), new(0.50f, 0.04f, 0.04f));
        });
        Add(r, "GlowMouth", "refinery.mouth", Glow(new Color(1f, 0.55f, 0.12f), 3.2f), mb =>
        {
            mb.At(default, Deg(0, 0, -90)).Prism(new(0.11f, 0.305f, 0), 0.17f, 0.17f, 0.012f, 10);
            mb.At(default).Block(new(-0.1f, Y0 + 0.435f, 0.1f), new(0.10f, 0.012f, 0.05f));
        });
        var rem = Pivot(r, "Piston", new(-0.30f, Y0 + 0.56f, 0));
        Add(rem, "RamMesh", "refinery.ram", Body(), mb =>
        {
            mb.C(Steel).Prism(new(0, 0, 0), 0.03f, 0.03f, 0.12f, 5);
            mb.C(Orange).Prism(new(0, -0.03f, 0), 0.09f, 0.09f, 0.03f, 6);
        });
        return r;
    }
}
