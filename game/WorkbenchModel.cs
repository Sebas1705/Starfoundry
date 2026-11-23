using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The ship's workbench (ADR 0031): the micro-refinery the starter ship carries instead of a factory, drawn as a compact fabrication bench on the
/// deck of a module of the ship. It is not a machine of the simulation, so it has no cell: <see cref="Models.Workbench"/> builds it in the local
/// cells of a module (origin at the module's back-left corner, like the module models), against the left wall or, mirrored, the right one, and
/// the game hangs it under the structure root next to the module (see <c>Play.Workbench.cs</c>). Its plating uses the planar module materials, so
/// the panel lines of the cabinet fall on the same grid as the hull around it.
///
/// Parts, from the cockpit end of the bench: a control post with three recipe lenses (ingot amber, plate cyan, ammo red; the active one lights) and
/// a status lamp on a pole (green working, amber waiting, red unpowered), a hopper over a small furnace whose mouth glows while it smelts, a
/// stamping press whose head drops while it works, a pegboard with tools on the wall. Animation (lamp pulse, furnace glow, press stroke) runs only while the
/// bench is crafting; idle it is still.
/// </summary>
public static partial class Models
{
    private static readonly Color BenchCase = new(0.74f, 0.78f, 0.85f);
    private static readonly Color BenchDark = new(0.13f, 0.15f, 0.20f);
    private static readonly Color BenchTrim = new(0.98f, 0.80f, 0.15f);

    /// <summary>Lens colours of the recipes: ingots amber, plates cyan, ammo red.</summary>
    public static Color BenchColour(BenchKind kind) => kind switch
    {
        BenchKind.Ingot => new Color(1f, 0.62f, 0.14f),
        BenchKind.Plate => new Color(0.38f, 0.86f, 1f),
        BenchKind.Ammo => new Color(1f, 0.28f, 0.30f),
        _ => new Color(0.4f, 0.42f, 0.5f),
    };

    private const float DeckTop = 0.086f;

    /// <summary>
    /// The workbench model for the left wall (<paramref name="right"/> false) or the right wall (mirrored in X). Children (names the game drives):
    /// "GlowRecipeIngot", "GlowRecipePlate", "GlowRecipeAmmo" (shown only for the active recipe), "GlowFurnace" (shown while smelting),
    /// "GlowStatus" (the status lamp), "Press" (the stamping head, driven by <see cref="AnimateBench"/>).
    /// </summary>
    public static Node3D Workbench(bool right)
    {
        var r = new Node3D { Name = "Workbench" };
        string k = right ? "bench.r." : "bench.l.";
        float X(float x) => right ? ModuleModels.W - x : x;
        float x0 = WorkbenchSpot.MinX, x1 = WorkbenchSpot.MaxX, z0 = WorkbenchSpot.MinZ, z1 = WorkbenchSpot.MaxZ;
        float xm = (x0 + x1) / 2, zm = (z0 + z1) / 2, w = x1 - x0, len = z1 - z0;
        float cab = 0.70f, kick = 0.12f;                      // cabinet height and the dark toe-kick under it
        float top = DeckTop + kick + cab + 0.06f;              // the worktop surface
        Vector3 P(float x, float y, float z) => new(X(x), y, z);

        Add(r, "BenchCase", k + "case", Materials.ModuleHull(), mb =>
        {
            mb.At(default);
            mb.C(BenchDark).Block(P(xm, DeckTop + kick / 2, zm), new(w - 0.1f, kick, len - 0.1f));
            mb.C(BenchCase).Block(P(xm, DeckTop + kick + cab / 2, zm), new(w, cab, len));
            mb.C(BenchTrim).Block(P(xm, DeckTop + kick + 0.05f, zm), new(w + 0.02f, 0.06f, len + 0.02f)); // a trim band along the base
            // drawers and a door on the aisle face (+X, mirrored for the right wall)
            float fx = right ? ModuleModels.W - x1 - 0.012f : x1 + 0.012f;
            mb.C(BenchDark);
            foreach (float zc in new[] { z0 + 0.45f, z0 + 1.0f, z0 + 1.55f })
            {
                mb.Block(new(fx, DeckTop + kick + cab * 0.5f, zc), new(0.03f, cab - 0.16f, 0.46f));
                mb.C(Steel).Block(new(fx + (right ? -0.02f : 0.02f), DeckTop + kick + cab * 0.62f, zc), new(0.03f, 0.04f, 0.2f)); // a handle
                mb.C(BenchDark);
            }
            // the worktop: a steel slab with a raised lip at the wall side
            mb.C(Steel).Block(P(xm, top - 0.03f, zm), new(w + 0.06f, 0.06f, len + 0.06f));
            mb.C(Panel).Block(P(x0 + 0.04f, top + 0.05f, zm), new(0.08f, 0.10f, len));
        });

        // ---- control post with the recipe lenses (front end, towards the cockpit) -----------------------------------
        Add(r, "BenchPost", k + "post", Materials.ModuleStruct(), mb =>
        {
            mb.At(default);
            mb.C(BenchDark).Block(P(1.78f, top + 0.16f, z0 + 0.30f), new(0.62f, 0.32f, 0.44f), 0.9f, 0.55f);
            // three dark lens sockets on the aisle face, one per recipe
            for (int i = 0; i < 3; i++)
                mb.C(Steel).Block(P(2.105f, top + 0.19f, z0 + 0.13f + i * 0.15f), new(0.025f, 0.11f, 0.11f));
            // status lamp pole with a cap, rising beside the post
            mb.C(Steel).Prism(P(1.45f, top, z0 + 0.12f), 0.025f, 0.02f, 0.78f, 6);
            mb.C(BenchTrim).Prism(P(1.45f, top, z0 + 0.12f), 0.045f, 0.045f, 0.05f, 6);
        });
        var kinds = new[] { (BenchKind.Ingot, "GlowRecipeIngot"), (BenchKind.Plate, "GlowRecipePlate"), (BenchKind.Ammo, "GlowRecipeAmmo") };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            Add(r, kinds[i].Item2, k + "lens" + i, Glow(BenchColour(kinds[i].Item1), 3.6f), mb =>
                mb.At(default).Block(P(2.13f, top + 0.19f, z0 + 0.13f + idx * 0.15f), new(0.03f, 0.085f, 0.085f)));
        }
        Add(r, "GlowStatus", k + "status", Glow(new Color(0.3f, 1f, 0.4f), 3.4f), mb =>
            mb.At(default).Sphere(P(1.45f, top + 0.85f, z0 + 0.12f), new(0.075f, 0.075f, 0.075f), 3, 8));

        // ---- hopper and furnace ---------------------------------------------------------------------------------------
        float fz = z0 + 0.95f;                                   // furnace centre along the aisle
        Add(r, "BenchFurnace", k + "furnace", Materials.ModuleStruct(), mb =>
        {
            mb.At(default);
            mb.C(new Color(0.30f, 0.20f, 0.17f)).Block(P(1.62f, top + 0.24f, fz), new(0.70f, 0.48f, 0.62f), 0.92f, 0.92f);   // fire-brick body
            mb.C(BenchDark).Block(P(1.62f, top + 0.51f, fz), new(0.74f, 0.05f, 0.66f));
            mb.C(BenchTrim);
            foreach (float s in new[] { -0.22f, 0.22f }) mb.Block(P(1.62f, top + 0.24f, fz + s), new(0.72f, 0.07f, 0.04f)); // bands
            mb.C(Steel).Block(P(1.995f, top + 0.22f, fz), new(0.05f, 0.30f, 0.38f));                                       // door frame
            mb.C(Steel).Prism(P(1.50f, top + 0.53f, fz - 0.12f), 0.07f, 0.06f, 1.35f, 6);                                  // the flue, up the wall
            mb.C(BenchTrim).Prism(P(1.50f, top + 1.50f, fz - 0.12f), 0.085f, 0.085f, 0.04f, 6);
            // hopper: a funnel on four legs feeding the furnace roof, with a lid
            mb.C(Steel);
            foreach (float sx in new[] { -0.15f, 0.15f }) foreach (float sz in new[] { -0.13f, 0.13f })
                mb.Beam(P(1.66f + sx * 0.8f, top + 0.52f, fz + 0.22f + sz), P(1.66f + sx, top + 0.82f, fz + 0.22f + sz), 0.03f);
            mb.C(BenchCase).Prism(P(1.66f, top + 0.76f, fz + 0.22f), 0.09f, 0.27f, 0.30f, 8, 22.5f);
            mb.C(BenchDark).Prism(P(1.66f, top + 1.06f, fz + 0.22f), 0.27f, 0.27f, 0.03f, 8, 22.5f);
            mb.C(Orange).Prism(P(1.66f, top + 0.99f, fz + 0.22f), 0.275f, 0.275f, 0.035f, 8, 22.5f);
        });
        Add(r, "GlowFurnace", k + "mouth", Glow(new Color(1f, 0.55f, 0.12f), 3.4f), mb =>
            mb.At(default).Block(P(2.025f, top + 0.22f, fz), new(0.02f, 0.2f, 0.26f)));

        // ---- press ----------------------------------------------------------------------------------------------------
        float pz = z1 - 0.34f;
        Add(r, "BenchPress", k + "press", Materials.ModuleStruct(), mb =>
        {
            mb.At(default);
            mb.C(Steel).Block(P(1.74f, top + 0.05f, pz), new(0.52f, 0.10f, 0.40f));                                         // anvil plate
            mb.C(BenchDark).Block(P(1.74f, top + 0.115f, pz), new(0.30f, 0.03f, 0.22f));
            foreach (float s in new[] { -1f, 1f }) mb.C(Steel).Block(P(1.74f, top + 0.40f, pz + s * 0.19f), new(0.16f, 0.62f, 0.05f)); // uprights
            mb.C(BenchTrim).Block(P(1.74f, top + 0.73f, pz), new(0.20f, 0.08f, 0.46f));                                     // the crosshead
            // a small tray with the finished piece
            mb.C(Panel).Block(P(2.08f, top + 0.015f, pz), new(0.20f, 0.03f, 0.30f));
        });
        var press = Pivot(r, "Press", P(1.74f, top + 0.58f, pz));
        Add(press, "PressHead", k + "head", Body(), mb =>
        {
            mb.At(default);
            mb.C(Steel).Prism(new(0, 0.07f, 0), 0.035f, 0.035f, 0.07f, 6);
            mb.C(Orange).Block(new(0, 0.03f, 0), new(0.15f, 0.07f, 0.12f));
            mb.C(Hull).Block(new(0, -0.02f, 0), new(0.11f, 0.03f, 0.09f));
        });

        // ---- pegboard with tools, on the wall above the bench -----------------------------------------------------------
        Add(r, "BenchRack", k + "rack", Materials.ModuleStruct(), mb =>
        {
            mb.At(default);
            float bx = 0.16f;                                                        // against the sloped wall
            mb.C(BenchDark).Block(P(bx, top + 0.62f, zm), new(0.05f, 0.55f, len - 0.2f));
            mb.C(BenchTrim).Block(P(bx + 0.03f, top + 0.92f, zm), new(0.03f, 0.04f, len - 0.2f));
            mb.C(Steel);
            // a hammer, a wrench and a pair of tongs hanging on pegs
            mb.Beam(P(bx + 0.08f, top + 0.52f, zm - 0.55f), P(bx + 0.08f, top + 0.82f, zm - 0.55f), 0.035f);
            mb.C(Hot).Block(P(bx + 0.08f, top + 0.86f, zm - 0.55f), new(0.07f, 0.09f, 0.20f));
            mb.C(Steel).Beam(P(bx + 0.08f, top + 0.40f, zm), P(bx + 0.08f, top + 0.80f, zm), 0.04f);
            mb.Block(P(bx + 0.08f, top + 0.84f, zm), new(0.06f, 0.08f, 0.14f));
            mb.Beam(P(bx + 0.08f, top + 0.45f, zm + 0.5f), P(bx + 0.08f, top + 0.82f, zm + 0.45f), 0.025f);
            mb.Beam(P(bx + 0.08f, top + 0.45f, zm + 0.58f), P(bx + 0.08f, top + 0.82f, zm + 0.52f), 0.025f);
        });
        return r;
    }

    /// <summary>
    /// Drives the bench. <paramref name="kind"/> is what it makes (off hides the recipe lenses), <paramref name="crafting"/> says it is working:
    /// the lamps pulse, the furnace glows while smelting and the press head drops while stamping plates or ammo; a bench that is set but has
    /// nothing to work on shows its lens dim and the status lamp amber, and a bench that is off is dark and still.
    /// </summary>
    public static void AnimateBench(Node3D bench, double timeSeconds, BenchKind kind, bool crafting)
    {
        var a = GetAnim(bench);
        foreach (var rec in a.Glows)
        {
            if (!Valid(rec.Mi)) continue;
            string n = rec.Mi.Name;
            rec.Mi.Visible = n switch
            {
                "GlowRecipeIngot" => kind == BenchKind.Ingot,
                "GlowRecipePlate" => kind == BenchKind.Plate,
                "GlowRecipeAmmo" => kind == BenchKind.Ammo,
                "GlowFurnace" => kind == BenchKind.Ingot && crafting,
                _ => rec.Mi.Visible,
            };
        }
        Animate(bench, timeSeconds, crafting);
        var press = bench.GetNodeOrNull<Node3D>("Press");
        if (press is not null)
        {
            bool stamp = crafting && kind != BenchKind.Off;
            float drop = stamp ? MathF.Max(0f, MathF.Sin(a.Work * (kind == BenchKind.Ammo ? 7f : 4.4f))) * 0.2f : 0f;
            press.Position = new Vector3(press.Position.X, BenchPressRest(bench) - drop, press.Position.Z);
        }
    }

    private static float BenchPressRest(Node3D bench)
    {
        const string meta = "rest_y";
        if (bench.HasMeta(meta)) return (float)bench.GetMeta(meta);
        float y = bench.GetNode<Node3D>("Press").Position.Y;
        bench.SetMeta(meta, y);
        return y;
    }
}
