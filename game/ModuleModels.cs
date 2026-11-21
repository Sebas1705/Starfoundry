using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Procedural models for structure modules. A module is an 8 x 4 x 8 cell box (X width, Y height, Z depth, one cell = one
/// world unit) and the model's local origin is the box's minimum corner, so it is placed at (mx * 8, my * 4, mz * 8).
/// Everything fits inside the box. The front of a ship is -Z (cockpit), the back is +Z (engines).
///
/// The <c>ship</c> flag chooses the outer shell language: a ship is streamlined (white octagonal plating, a tapered nose at
/// the cockpit, a tapered tail at the engines, blue trim), a station is boxy (grey slab panels, a heavy corner frame, docking
/// collars on the four sides, orange trim). The shells are single-sided: they show from outside and vanish when the camera
/// is inside the module, so the player looks through the hull at the stars and the deck; the frame beams and the deck stay
/// visible from inside. The workshop and the defence deck have no roof, so what stands on them stays in view.
///
/// Parts reuse <see cref="Models"/>' meshes cache, materials and mesh builder, so every module of a kind shares its meshes.
/// Node names the game can drive: "GlowEngine" (flame of an engine nozzle, pulsed by <see cref="Models.Animate"/>, a flame
/// along local +X which is world +Z), "Ring" (the core's gold ring, spun by Animate), "Turret0".. with "Head" and "Head/Barrel"
/// children (<see cref="Models.Aim"/>), "GlowCore" (the reactor).
/// </summary>
public static class ModuleModels
{
    public const float W = StationGrid.ModuleX;
    public const float H = StationGrid.ModuleY;
    public const float D = StationGrid.ModuleZ;

    private const float Roof = 3.88f;     // top of a full-height shell
    private const float StationFace = 0.14f; // a station shell stands this far inside the module box, between the frame beams
    private const float LowRoof = 3.0f;  // top of a shell that carries something on its roof (cupola, reactor drum, core pillar)

    private static readonly Color Dark = new(0.12f, 0.14f, 0.19f);
    private static readonly Color DeckCol = new(0.19f, 0.21f, 0.27f);
    private static readonly Color Steel = new(0.46f, 0.50f, 0.58f);
    private static readonly Color Panel = new(0.72f, 0.76f, 0.83f);
    private static readonly Color White = new(0.90f, 0.93f, 0.98f);
    private static readonly Color Slab = new(0.34f, 0.37f, 0.45f);
    private static readonly Color SlabLt = new(0.44f, 0.48f, 0.57f);
    private static readonly Color Yellow = new(0.98f, 0.86f, 0.18f);
    private static readonly Color Gold = new(1f, 0.78f, 0.30f);
    private static readonly Color Crate = new(0.62f, 0.40f, 0.22f);
    private static readonly Color CrateDk = new(0.40f, 0.27f, 0.17f);
    private static readonly Color Green = new(0.30f, 0.95f, 0.50f);

    private static readonly Color[] ShipAccents = { new(0.24f, 0.48f, 0.94f), new(0.15f, 0.72f, 0.75f), new(0.97f, 0.42f, 0.07f) };
    private static readonly Color[] StationAccents = { new(0.97f, 0.42f, 0.07f), new(0.15f, 0.72f, 0.75f), new(0.64f, 0.34f, 0.90f) };

    private readonly record struct Cx(Node3D R, string K, bool Ship, Color Acc, int V);

    /// <summary>A model of one module of <paramref name="kind"/>; <paramref name="variant"/> (0..2) picks the trim colour and the clutter layout.</summary>
    public static Node3D Build(ModuleKind kind, bool ship, int variant = 0)
    {
        variant = ((variant % 3) + 3) % 3;
        var r = new Node3D { Name = $"{kind}{(ship ? "Ship" : "Station")}" };
        var c = new Cx(r, $"mod.{(ship ? "s" : "t")}{variant}.{kind}", ship, (ship ? ShipAccents : StationAccents)[variant], variant);
        Deck(c);
        switch (kind)
        {
            case ModuleKind.Cockpit: Cockpit(c); break;
            case ModuleKind.Engine: Engine(c); break;
            case ModuleKind.Reactor: Reactor(c); break;
            case ModuleKind.Cargo: Cargo(c); break;
            case ModuleKind.Workshop: Workshop(c); break;
            case ModuleKind.Defence: Defence(c); break;
            case ModuleKind.Core: Core(c); break;
            default: HullModule(c); break;
        }
        return r;
    }

    // ---- shared parts ---------------------------------------------------------------------------------

    private static void Deck(Cx c) => Models.Add(c.R, "Deck", $"mod.deck.{(c.Ship ? "s" : "t")}{c.V}", Models.Body(), mb =>
    {
        mb.At(default).C(Dark).Block(new(4, 0.04f, 4), new(7.9f, 0.08f, 7.9f));
        mb.C(DeckCol).Block(new(4, 0.05f, 4), new(7.3f, 0.07f, 7.3f));
        mb.C(c.Acc);
        foreach (float s in new[] { -1f, 1f })
        {
            mb.Block(new(4 + s * 3.5f, 0.086f, 4), new(0.1f, 0.012f, 7.1f));
            mb.Block(new(4, 0.086f, 4 + s * 3.5f), new(7.1f, 0.012f, 0.1f));
        }
    });

    /// <summary>The cross-section of a ship hull in the XY plane, scaled about a point low in the section.</summary>
    private static Vector2[] Oct(float roofY, float scale)
    {
        float x0 = 0.05f, x1 = W - 0.05f, y0 = 0.08f, ch = MathF.Min(1.0f, (roofY - y0) * 0.3f);
        var p = new[]
        {
            new Vector2(x0 + ch, y0), new Vector2(x1 - ch, y0), new Vector2(x1, y0 + ch), new Vector2(x1, roofY - ch),
            new Vector2(x1 - ch, roofY), new Vector2(x0 + ch, roofY), new Vector2(x0, roofY - ch), new Vector2(x0, y0 + ch),
        };
        var pivot = new Vector2(4f, y0 + (roofY - y0) * 0.35f);
        for (int i = 0; i < p.Length; i++) p[i] = pivot + (p[i] - pivot) * scale;
        return p;
    }

    /// <summary>A convex tube between two cross-sections at depths <paramref name="za"/> and <paramref name="zb"/>.</summary>
    private static void Loft(Models.Mb mb, Vector2[] a, float za, Vector2[] b, float zb, bool capA, bool capB)
    {
        var ca = Vector2.Zero; var cb = Vector2.Zero;
        foreach (var p in a) ca += p;
        foreach (var p in b) cb += p;
        ca /= a.Length; cb /= b.Length;
        var inside = new Vector3((ca.X + cb.X) / 2, (ca.Y + cb.Y) / 2, (za + zb) / 2);
        for (int i = 0; i < a.Length; i++)
        {
            int j = (i + 1) % a.Length;
            var a0 = new Vector3(a[i].X, a[i].Y, za); var a1 = new Vector3(a[j].X, a[j].Y, za);
            var b0 = new Vector3(b[i].X, b[i].Y, zb); var b1 = new Vector3(b[j].X, b[j].Y, zb);
            mb.Quad(a0, a1, b1, b0, inside);
            if (capA) mb.Tri(new(ca.X, ca.Y, za), a0, a1, inside);
            if (capB) mb.Tri(new(cb.X, cb.Y, zb), b0, b1, inside);
        }
    }

    /// <summary>
    /// The outer shell. Ship: an octagonal hull with optional tapered nose and tail, a spine and side stripe in the trim colour.
    /// Station: a slab box with docking collars on the four sides, and raised panels when <paramref name="detail"/>.
    /// </summary>
    private static void Skin(Cx c, float roofY, bool nose = false, bool tail = false, bool detail = false, bool collars = true)
    {
        string key = $"{c.K}.skin.{roofY}.{nose}.{tail}.{detail}";
        Models.Add(c.R, "Skin", key, Models.Body(), mb =>
        {
            mb.At(default);
            if (c.Ship)
            {
                var full = Oct(roofY, 1f);
                float a = nose ? 3.4f : 0.05f, b = tail ? 5.0f : D - 0.05f;
                mb.C(White);
                if (nose) Loft(mb, Oct(roofY, 0.28f), 0.05f, full, a, true, false);
                Loft(mb, full, a, full, b, !nose, !tail);
                if (tail) Loft(mb, full, b, Oct(roofY, 0.8f), 6.1f, false, true);
                float zc = (a + b) / 2, len = b - a;
                float spineTop = full[4].Y;
                mb.C(c.Acc).Block(new(4, spineTop + 0.02f, zc), new(1.1f, 0.06f, len - 0.1f));
                mb.C(Dark).Block(new(4, 0.066f, 4), new(full[1].X - full[0].X - 0.4f, 0.03f, D - 0.4f));
                float mid = (full[2].Y + full[3].Y) / 2;
                foreach (float s in new[] { 0.01f, W - 0.01f })
                    mb.C(c.Acc).Block(new(s, mid, zc), new(0.04f, 0.22f, len - 0.2f));
                if (detail)
                {
                    mb.C(Panel);
                    for (int i = 1; i < 4; i++)
                        mb.Block(new(4, spineTop + 0.012f, i * 2f), new(full[4].X - full[5].X, 0.03f, 0.05f));
                    for (int i = 1; i < 4; i++)
                        foreach (float s in new[] { 0.035f, W - 0.035f })
                            mb.Block(new(s, mid, i * 2f), new(0.04f, full[3].Y - full[2].Y, 0.05f));
                }
            }
            else
            {
                float h = roofY - 0.08f, si = StationFace;
                mb.C(Slab).Block(new(4, 0.08f + h / 2, 4), new(W - 2 * si, h, D - 2 * si));
                mb.C(c.Acc);
                foreach (float s in new[] { si - 0.01f, W - si + 0.01f })
                    mb.Block(new(s, roofY * 0.5f, 4), new(0.03f, 0.2f, D - 0.7f));
                if (detail)
                {
                    mb.C(SlabLt);
                    foreach (float sx in new[] { 2f, 6f })
                        foreach (float sz in new[] { 2f, 6f })
                            mb.Block(new(sx, roofY + 0.012f, sz), new(3.3f, 0.025f, 3.3f));
                    foreach (float sz in new[] { 2f, 6f })
                        foreach (float x in new[] { si - 0.01f, W - si + 0.01f })
                            mb.Block(new(x, roofY * 0.5f + 0.1f, sz), new(0.025f, roofY - 0.9f, 3.3f));
                    foreach (float sx in new[] { 2f, 6f })
                        foreach (float z in new[] { si - 0.01f, D - si + 0.01f })
                            mb.Block(new(sx, roofY * 0.5f + 0.1f, z), new(3.3f, roofY - 0.9f, 0.025f));
                }
                if (collars)
                {
                    // Docking collars: an octagonal flange with a dark hatch, standing out of each face (builder +Y turned to point outwards).
                    (Vector3 pos, Vector3 rot)[] spots =
                    {
                        (new(si, 1.9f, 4), Models.Deg(0, 0, 90)),
                        (new(W - si, 1.9f, 4), Models.Deg(0, 0, -90)),
                        (new(4, 1.9f, si), Models.Deg(-90, 0, 0)),
                        (new(4, 1.9f, D - si), Models.Deg(90, 0, 0)),
                    };
                    foreach (var s in spots)
                    {
                        mb.At(s.pos, s.rot).C(Steel).Prism(default, 1.3f, 1.15f, 0.1f, 8, 22.5f);
                        mb.C(Dark).Prism(new(0, 0.005f, 0), 0.95f, 0.95f, 0.11f, 8, 22.5f);
                        mb.C(c.Acc).Prism(new(0, 0.01f, 0), 0.4f, 0.4f, 0.115f, 8, 22.5f);
                    }
                    mb.At(default);
                }
            }
        });
    }

    /// <summary>The load-bearing frame, visible from inside as well. Ship: eight longitudinal beams along the octagon and a ring at each end. Station: the twelve box edges with trim caps on the corners.</summary>
    private static void Frame(Cx c, float roofY, bool nose = false, bool tail = false)
    {
        Models.Add(c.R, "Frame", $"{c.K}.frame.{roofY}.{nose}.{tail}", Models.Body(), mb =>
        {
            mb.At(default);
            if (c.Ship)
            {
                // The frame follows the lofted hull: sections at (z, scale), so a nose or tail does not leave the frame floating in the air.
                var secs = new System.Collections.Generic.List<(float Z, float S)>();
                if (nose) { secs.Add((0.07f, 0.28f)); secs.Add((3.4f, 1f)); } else secs.Add((0.07f, 1f));
                if (tail) { secs.Add((5.0f, 1f)); secs.Add((6.1f, 0.8f)); } else secs.Add((D - 0.07f, 1f));
                mb.C(Panel);
                for (int s = 0; s + 1 < secs.Count; s++)
                {
                    var pa = Oct(roofY, secs[s].S); var pb = Oct(roofY, secs[s + 1].S);
                    for (int i = 0; i < pa.Length; i++)
                        mb.Beam(new(pa[i].X, pa[i].Y, secs[s].Z), new(pb[i].X, pb[i].Y, secs[s + 1].Z), 0.12f);
                }
                foreach (var sec in new[] { secs[0], secs[^1] })
                {
                    var o = Oct(roofY, sec.S);
                    for (int i = 0; i < o.Length; i++)
                    {
                        int j = (i + 1) % o.Length;
                        mb.Beam(new(o[i].X, o[i].Y, sec.Z), new(o[j].X, o[j].Y, sec.Z), 0.12f);
                    }
                }
            }
            else
            {
                float t = 0.30f, x0 = t / 2, x1 = W - t / 2, y0 = 0.08f + t / 2, y1 = roofY - t / 2, z0 = t / 2, z1 = D - t / 2;
                mb.C(Dark);
                foreach (float y in new[] { y0, y1 })
                    foreach (float z in new[] { z0, z1 })
                        mb.Block(new((x0 + x1) / 2, y, z), new(x1 - x0 + t, t, t));
                foreach (float x in new[] { x0, x1 })
                    foreach (float z in new[] { z0, z1 })
                        mb.Block(new(x, (y0 + y1) / 2, z), new(t, y1 - y0 + t, t));
                foreach (float x in new[] { x0, x1 })
                    foreach (float y in new[] { y0, y1 })
                        mb.Block(new(x, y, (z0 + z1) / 2), new(t, t, z1 - z0 + t));
                mb.C(c.Acc);
                foreach (float x in new[] { x0, x1 })
                    foreach (float y in new[] { y0, y1 })
                        foreach (float z in new[] { z0, z1 })
                            mb.Block(new(x, y, z), new(t + 0.06f, t + 0.06f, t + 0.06f));
            }
        });
    }

    private static void Stripes(Models.Mb mb, float x0, float x1, float z, float w, float y, int n, Color a, Color b)
    {
        float k = w * 0.9f, s = (x1 - x0 - k) / n;
        for (int i = 0; i < n; i++)
        {
            float xa = x0 + i * s;
            mb.C(i % 2 == 0 ? a : b).Extrude(new[] { new Vector2(xa, z), new Vector2(xa + s, z), new Vector2(xa + s + k, z + w), new Vector2(xa + k, z + w) }, y, y + 0.03f);
        }
    }

    /// <summary>The upper half of an ellipsoid without a base cap, so from inside the module the glass is not seen at all.</summary>
    private static void Dome(Models.Mb mb, Vector3 c, Vector3 radii, int rings, int segs)
    {
        mb.At(default);
        Vector3 P(int i, int j)
        {
            float lat = Mathf.Pi / 2 * i / rings, lon = Mathf.Tau * j / segs;
            return c + new Vector3(MathF.Sin(lat) * MathF.Cos(lon) * radii.X, MathF.Cos(lat) * radii.Y, MathF.Sin(lat) * MathF.Sin(lon) * radii.Z);
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
                mb.Quad(P(i, j), P(i, j + 1), P(i + 1, j + 1), P(i + 1, j), c);
    }

    private static void ZPrism(Models.Mb mb, float x, float y, float z0, float rb, float rt, float len, int sides, Color col)
    {
        // A prism along +Z: the builder's Y axis is rotated onto Z (local (x, y, z) becomes world (x, -z, y)).
        mb.At(default, Models.Deg(90, 0, 0)).C(col).Prism(new(x, z0, -y), rb, rt, len, sides);
        mb.At(default);
    }

    // ---- kinds -------------------------------------------------------------------------------------------

    private static void Cockpit(Cx c)
    {
        float roof = c.Ship ? Roof : LowRoof;
        Skin(c, roof, nose: c.Ship, collars: !c.Ship ? true : false);
        Frame(c, roof, nose: c.Ship);
        if (c.Ship)
            Models.Add(c.R, "Canopy", $"{c.K}.canopy", Models.GlassMat(), mb => Dome(mb, new(4, 2.9f, 2.9f), new(1.5f, 1.1f, 2.1f), 4, 10));
        else
            Models.Add(c.R, "Canopy", $"{c.K}.canopy", Models.GlassMat(), mb => Dome(mb, new(4, 3.05f, 4), new(3.4f, 0.85f, 3.4f), 3, 12));

        Models.Add(c.R, "Seat", $"{c.K}.seat", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, 0.4f, 4.6f), new(0.5f, 0.7f, 0.5f));
            mb.C(Steel).Block(new(4, 0.82f, 4.6f), new(1.3f, 0.3f, 1.3f));
            mb.C(c.Acc).Block(new(4, 1.55f, 5.25f), new(1.3f, 1.6f, 0.26f));
            mb.C(Steel).Block(new(4, 2.5f, 5.3f), new(0.8f, 0.45f, 0.2f));
            foreach (float s in new[] { -1f, 1f })
                mb.C(Dark).Block(new(4 + s * 0.78f, 1.15f, 4.6f), new(0.14f, 0.2f, 1.0f));
            mb.C(Dark).Beam(new(3.4f, 0.1f, 3.5f), new(3.4f, 1.2f, 3.5f), 0.1f);
            mb.C(Steel).Block(new(3.4f, 1.28f, 3.5f), new(0.28f, 0.14f, 0.28f));
        });
        Models.Add(c.R, "Console", $"{c.K}.console", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, 0.55f, 2.25f), new(3.6f, 1.0f, 1.0f), 1f, 0.45f);
            mb.C(Steel).Block(new(4, 1.08f, 2.25f), new(3.7f, 0.08f, 0.55f));
        });
        Models.Add(c.R, "GlowDash", $"{c.K}.dash", Models.Glow(new Color(0.3f, 0.8f, 1f), 2.2f), mb =>
        {
            mb.At(default).Block(new(4, 1.14f, 2.2f), new(2.2f, 0.04f, 0.38f));
            foreach (float s in new[] { -1f, 1f }) mb.Block(new(4 + s * 1.4f, 1.14f, 2.2f), new(0.6f, 0.04f, 0.38f));
        });
    }

    private static void Engine(Cx c)
    {
        Skin(c, Roof, tail: c.Ship);
        Frame(c, Roof, tail: c.Ship);
        Models.Add(c.R, "Machinery", $"{c.K}.machinery", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, 0.8f, 2.4f), new(5.2f, 1.4f, 3.2f));
            mb.C(c.Acc).Block(new(4, 1.52f, 2.4f), new(5.3f, 0.1f, 3.3f));
            mb.C(Steel);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Beam(new(4 + s * 1.5f, 1.5f, 3.9f), new(4 + s * 1.5f, 1.5f, 5.3f), 0.35f);
                mb.Beam(new(4 + s * 1.5f, 0.3f, 3.9f), new(4 + s * 1.5f, 0.3f, 5.3f), 0.35f);
            }
        });
        Models.Add(c.R, "GlowVent", $"{c.K}.vent", Models.Glow(new Color(1f, 0.45f, 0.12f), 1.8f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                    mb.Block(new(4 + s * 2.62f, 0.85f, 1.4f + i * 1.0f), new(0.06f, 0.9f, 0.45f));
        });
        foreach (float x in new[] { 2.7f, 5.3f })
            Nozzle(c, x, 1.95f);
    }

    private static void Nozzle(Cx c, float x, float y)
    {
        Models.Add(c.R, "Nozzle", $"{c.K}.nozzle", Models.Body(), mb =>
        {
            ZPrism(mb, x, y, 5.1f, 1.05f, 1.05f, 0.5f, 10, Panel);
            ZPrism(mb, x, y, 5.6f, 0.8f, 1.3f, 1.4f, 10, new Color(0.20f, 0.22f, 0.28f));
            ZPrism(mb, x, y, 6.95f, 1.36f, 1.36f, 0.1f, 10, Steel);
            ZPrism(mb, x, y, 6.98f, 1.1f, 1.1f, 0.06f, 10, new Color(0.75f, 0.26f, 0.08f));
            mb.C(c.Acc);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + 0.4f;
                mb.Beam(new(x + MathF.Cos(a) * 1.0f, y + MathF.Sin(a) * 1.0f, 5.2f), new(x + MathF.Cos(a) * 1.25f, y + MathF.Sin(a) * 1.25f, 6.4f), 0.14f);
            }
        });
        // The flame lies along local +X and the node is turned so that +X points to world +Z: Animate scales it along X.
        Models.Add(c.R, "GlowEngine", "mod.flame", Models.Glow(new Color(0.25f, 0.65f, 1f), 3.5f), mb =>
        {
            mb.At(default, Models.Deg(0, 0, -90)).Prism(default, 1.05f, 0f, 1.0f, 10);
        }, new Vector3(x, y, 7.0f), new Vector3(0, -90, 0));
    }

    private static void Reactor(Cx c)
    {
        Skin(c, LowRoof);
        Frame(c, LowRoof);
        Models.Add(c.R, "Containment", $"{c.K}.containment", Models.Body(), mb =>
        {
            mb.At(default).C(Steel).Prism(new(4, 0.08f, 4), 1.5f, 1.3f, 0.35f, 10);
            foreach (float y in new[] { 1.0f, 2.2f, 3.2f })
                mb.C(y > 3 ? c.Acc : Steel).Torus(new(4, y, 4), 1.0f, 0.14f, 10, 4);
            // four coolant pipes with elbows into the vessel
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    float px = 4 + sx * 2.8f, pz = 4 + sz * 2.8f;
                    mb.C(Panel).Prism(new(px, 0.08f, pz), 0.26f, 0.26f, 2.7f, 8);
                    mb.C(c.Acc).Prism(new(px, 0.9f, pz), 0.3f, 0.3f, 0.14f, 8);
                    mb.C(c.Acc).Prism(new(px, 1.9f, pz), 0.3f, 0.3f, 0.14f, 8);
                    mb.C(Steel).Beam(new(px, 0.5f, pz), new(4 + sx * 0.9f, 0.5f, 4 + sz * 0.9f), 0.2f);
                }
        });
        Models.Add(c.R, "GlowCore", $"{c.K}.core", Models.Glow(Green, 2.4f), mb =>
            mb.At(default).Prism(new(4, 0.4f, 4), 0.85f, 0.85f, 3.45f, 10));
        // vents on the hull sides tell the reactor from outside
        Models.Add(c.R, "GlowVent", $"{c.K}.vent", Models.Glow(Green, 1.4f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { (c.Ship ? 0.05f : StationFace) - 0.02f, W - (c.Ship ? 0.05f : StationFace) + 0.02f })
                for (int i = 0; i < 3; i++)
                    mb.Block(new(s, 1.55f, 1.6f + i * 2.4f), new(0.05f, 0.9f, 0.7f));
        });
    }

    private static void Cargo(Cx c)
    {
        Skin(c, Roof, detail: false);
        Frame(c, Roof);
        Models.Add(c.R, "Hatch", $"{c.K}.hatch", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, Roof + 0.04f, 4), new(3.0f, 0.05f, 3.0f));
            mb.C(Yellow);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Block(new(4 + s * 1.5f, Roof + 0.05f, 4), new(0.2f, 0.07f, 3.2f));
                mb.Block(new(4, Roof + 0.05f, 4 + s * 1.5f), new(3.2f, 0.07f, 0.2f));
            }
            mb.C(Steel).Block(new(4, Roof + 0.08f, 4), new(0.5f, 0.05f, 0.5f));
            // side cargo doors with hazard bars
            foreach (float x in new[] { 0.0f, W })
            {
                float f = c.Ship ? 0.05f : StationFace;
                float o = x == 0 ? f - 0.02f : W - f + 0.02f;
                mb.C(Dark).Block(new(o, 1.95f, 4), new(0.06f, 1.5f, 3.4f));
                mb.C(Yellow);
                for (int i = 0; i < 4; i++) mb.Block(new(o + (x == 0 ? -0.02f : 0.02f), 1.95f, 2.7f + i * 0.87f), new(0.05f, 1.5f, 0.12f));
            }
        });
        Models.Add(c.R, "Cargo", $"{c.K}.cargo", Models.Body(), mb =>
        {
            mb.At(default);
            // a three-level rack along the left wall
            mb.C(Steel);
            for (int i = 0; i < 5; i++) foreach (float s in new[] { 0.35f, 1.55f }) mb.Beam(new(s, 0.1f, 0.5f + i * 1.75f), new(s, 3.1f, 0.5f + i * 1.75f), 0.1f);
            foreach (float y in new[] { 0.9f, 1.9f, 2.9f })
                mb.C(c.Acc).Block(new(0.95f, y, 4), new(1.4f, 0.07f, 7.3f));
            // boxes on the shelves, layout by variant
            for (int lvl = 0; lvl < 3; lvl++)
                for (int i = 0; i < 6; i++)
                {
                    if ((i * 7 + lvl * 3 + c.V) % 5 == 0) continue;
                    float h = 0.45f + ((i + lvl + c.V) % 3) * 0.15f;
                    mb.C(((i + lvl) & 1) == 0 ? Crate : CrateDk).Block(new(0.95f, 0.935f + lvl + h / 2, 0.8f + i * 1.2f), new(0.95f, h, 0.8f));
                }
            // stacked crates against the right wall
            float[] zs = { 1.3f, 2.7f, 4.1f };
            for (int i = 0; i < zs.Length; i++)
            {
                int stack = 1 + (i + c.V) % 3;
                for (int s = 0; s < stack; s++)
                {
                    mb.C((i + s) % 2 == 0 ? Crate : CrateDk).Block(new(6.7f, 0.6f + s * 1.15f, zs[i]), new(1.2f, 1.1f, 1.2f));
                    mb.C(Yellow).Block(new(6.7f, 0.6f + s * 1.15f, zs[i]), new(1.24f, 0.2f, 1.24f));
                }
            }
            mb.C(CrateDk).Block(new(5.5f, 0.55f, 6.5f), new(2.2f, 1.0f, 1.2f));
            mb.C(c.Acc).Block(new(5.5f, 1.06f, 6.5f), new(2.24f, 0.06f, 1.24f));
        });
    }

    private static void Workshop(Cx c)
    {
        Frame(c, Roof);
        Models.Add(c.R, "Rails", $"{c.K}.rails", Models.Body(), mb =>
        {
            mb.At(default).C(Steel);
            foreach (float x in new[] { 1.2f, W - 1.2f })
                mb.Block(new(x, 0.12f, 4), new(0.14f, 0.07f, 7.6f));
            mb.C(Dark);
            for (int i = 0; i < 8; i++) mb.Block(new(4, 0.1f, 0.45f + i * 1.01f), new(W - 2.2f, 0.04f, 0.16f));
            Stripes(mb, 0.4f, W - 0.4f, 0.12f, 0.28f, 0.088f, 9, Yellow, Dark);
            Stripes(mb, 0.4f, W - 0.4f, D - 0.4f, 0.28f, 0.088f, 9, Yellow, Dark);
        });
        Models.Add(c.R, "Truss", $"{c.K}.truss", Models.Body(), mb =>
        {
            mb.At(default).C(c.Ship ? Panel : Steel);
            // two lattice girders along Z with a gantry rail across them
            foreach (float x in new[] { 1.8f, W - 1.8f })
            {
                float yt = Roof - 0.1f, yb = Roof - 0.65f;
                mb.Beam(new(x, yt, 0.2f), new(x, yt, D - 0.2f), 0.14f);
                mb.Beam(new(x, yb, 0.2f), new(x, yb, D - 0.2f), 0.14f);
                for (int i = 0; i < 7; i++)
                {
                    float z0 = 0.2f + i * (D - 0.4f) / 7, z1 = 0.2f + (i + 1) * (D - 0.4f) / 7;
                    mb.Beam(new(x, i % 2 == 0 ? yb : yt, z0), new(x, i % 2 == 0 ? yt : yb, z1), 0.09f);
                }
            }
            mb.C(c.Acc);
            foreach (float z in new[] { 0.3f, D - 0.3f })
                mb.Block(new(4, Roof - 0.1f, z), new(W - 3.4f, 0.16f, 0.16f));
            mb.C(Dark).Block(new(4, Roof - 0.7f, 4), new(W - 3.4f, 0.16f, 0.2f));
            // the trolley and a hook hang from the gantry
            mb.C(Yellow).Block(new(4.6f, Roof - 0.85f, 4), new(0.7f, 0.2f, 0.5f));
            mb.C(Steel).Beam(new(4.6f, Roof - 0.95f, 4), new(4.6f, 2.7f, 4), 0.05f);
            mb.C(Yellow).Block(new(4.6f, 2.62f, 4), new(0.2f, 0.18f, 0.2f));
        });
        Models.Add(c.R, "GlowLamp", $"{c.K}.lamps", Models.Glow(new Color(1f, 0.93f, 0.75f), 1.6f), mb =>
        {
            mb.At(default);
            foreach (float x in new[] { 1.8f, W - 1.8f })
                foreach (float z in new[] { 1.6f, 4.0f, 6.4f })
                    mb.Block(new(x, Roof - 0.78f, z), new(0.4f, 0.06f, 0.4f));
        });
        if (c.Ship)
            Models.Add(c.R, "Skirt", $"{c.K}.skirt", Models.Body(), mb =>
            {
                mb.At(default).C(White);
                foreach (float x in new[] { 0.2f, W - 0.2f })
                    mb.Block(new(x, 0.4f, 4), new(0.3f, 0.7f, D - 0.2f), 0.4f, 1f);
                mb.C(c.Acc);
                foreach (float x in new[] { 0.1f, W - 0.1f })
                    mb.Block(new(x, 0.62f, 4), new(0.08f, 0.1f, D - 0.4f));
            });
        else
            Models.Add(c.R, "Kerb", $"{c.K}.kerb", Models.Body(), mb =>
            {
                mb.At(default).C(Slab);
                foreach (float x in new[] { 0.2f, W - 0.2f })
                    mb.Block(new(x, 0.35f, 4), new(0.3f, 0.55f, D - 0.2f), 0.7f, 1f);
            });
    }

    private static void Defence(Cx c)
    {
        Models.Add(c.R, "Parapet", $"{c.K}.parapet", Models.Body(), mb =>
        {
            mb.At(default);
            float top = c.Ship ? 0.3f : 0.75f;
            mb.C(c.Ship ? White : Slab);
            foreach (float x in new[] { 0.3f, W - 0.3f })
                mb.Block(new(x, 0.62f, 4), new(0.5f, 1.1f, D - 0.1f), top, 1f);
            mb.Block(new(4, 0.62f, D - 0.3f), new(W - 0.9f, 1.1f, 0.5f), 1f, top);
            mb.Block(new(4, 0.62f, 0.3f), new(W - 0.9f, 1.1f, 0.5f), 1f, top);
            mb.C(c.Acc);
            foreach (float x in new[] { 0.3f, W - 0.3f })
                mb.Block(new(x, 1.19f, 4), new(0.18f, 0.06f, D - 0.5f));
            if (!c.Ship)
            {
                mb.C(Steel);
                foreach (float x in new[] { 0.3f, W - 0.3f })
                    for (int i = 0; i < 4; i++) mb.Block(new(x, 1.3f, 0.9f + i * 2.07f), new(0.34f, 0.22f, 0.7f));
            }
            Stripes(mb, 1.0f, W - 1.0f, 1.0f, 0.45f, 0.088f, 8, Yellow, Dark);
            Stripes(mb, 1.0f, W - 1.0f, D - 1.45f, 0.45f, 0.088f, 8, Yellow, Dark);
        });
        Frame(c, Roof);
        var spots = c.Ship
            ? new[] { new Vector3(4, 0, 2.5f), new Vector3(4, 0, 5.5f) }
            : new[] { new Vector3(2.5f, 0, 2.5f), new Vector3(5.5f, 0, 2.5f), new Vector3(2.5f, 0, 5.5f), new Vector3(5.5f, 0, 5.5f) };
        for (int i = 0; i < spots.Length; i++) Turret(c, i, spots[i]);
    }

    private static void Turret(Cx c, int n, Vector3 pos)
    {
        var t = new Node3D { Name = $"Turret{n}", Position = pos };
        c.R.AddChild(t);
        Models.Add(t, "Mount", $"{c.K}.mount", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(0, 0.07f, 0), 0.95f, 0.95f, 0.2f, 8);
            mb.C(Steel).Prism(new(0, 0.27f, 0), 0.8f, 0.55f, 0.35f, 8);
            mb.C(c.Acc).Prism(new(0, 0.26f, 0), 0.82f, 0.82f, 0.03f, 8);
        });
        var head = Models.Pivot(t, "Head", new(0, 0.9f, 0));
        head.RotationDegrees = new Vector3(0, 90, 0); // faces -Z (the ship's front) until something aims it
        Models.Add(head, "HeadMesh", $"{c.K}.head", Models.Body(), mb =>
        {
            mb.At(default).C(c.Ship ? White : Steel).Prism(new(0, -0.2f, 0), 0.62f, 0.5f, 0.45f, 8);
            mb.C(Dark).Sphere(new(0, 0.24f, 0), new(0.4f, 0.26f, 0.4f), 2, 8, dome: true);
            mb.C(c.Acc).Block(new(0.3f, 0.05f, 0), new(0.3f, 0.12f, 0.9f));
        });
        var barrel = Models.Pivot(head, "Barrel", new(0.25f, 0.08f, 0));
        Models.Add(barrel, "BarrelMesh", $"{c.K}.barrel", Models.Body(), mb =>
        {
            mb.At(default).C(Dark);
            foreach (float z in new[] { -0.17f, 0.17f })
            {
                mb.Block(new(0.8f, 0, z), new(1.6f, 0.15f, 0.15f));
                mb.C(Steel).Block(new(1.62f, 0, z), new(0.22f, 0.22f, 0.22f));
                mb.C(Dark);
            }
        });
        Models.Add(barrel, "GlowMuzzle", "mod.muzzle", Models.Glow(new Color(1f, 0.5f, 0.15f), 2f), mb =>
        {
            mb.At(default);
            foreach (float z in new[] { -0.17f, 0.17f }) mb.Block(new(1.75f, 0, z), new(0.04f, 0.1f, 0.1f));
        });
    }

    private static void HullModule(Cx c)
    {
        Skin(c, Roof, detail: true);
        Frame(c, Roof);
    }

    private static void Core(Cx c)
    {
        Skin(c, LowRoof, detail: false);
        Frame(c, LowRoof);
        Models.Add(c.R, "Pillar", $"{c.K}.pillar", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(4, 0.08f, 4), 2.0f, 1.7f, 0.22f, 8);
            mb.C(Steel).Prism(new(4, 0.3f, 4), 1.1f, 0.8f, 0.5f, 8);
            mb.C(Panel).Prism(new(4, 0.8f, 4), 0.55f, 0.5f, 2.6f, 8);
            mb.C(Gold);
            foreach (float y in new[] { 1.1f, 2.0f }) mb.Prism(new(4, y, 4), 0.6f, 0.6f, 0.1f, 8);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                mb.C(c.Ship ? Steel : Slab).Beam(new(4 + MathF.Cos(a) * 2.6f, 0.1f, 4 + MathF.Sin(a) * 2.6f), new(4 + MathF.Cos(a) * 0.6f, 1.2f, 4 + MathF.Sin(a) * 0.6f), 0.2f);
            }
        });
        var ring = Models.Add(c.R, "Ring", "mod.core.ring", Models.Glow(Gold, 1.6f), mb =>
        {
            mb.At(default).Torus(default, 1.25f, 0.1f, 14, 4);
            mb.Torus(new(0, 0.28f, 0), 0.8f, 0.07f, 12, 4);
        }, new Vector3(4, 3.3f, 4));
        ring.RotationDegrees = new Vector3(8, 0, 4);
        Models.Add(c.R, "CoreMarker", "mod.core.marker", Models.Glow(Gold, 2.6f), mb =>
            mb.At(default).Sphere(new(4, 3.58f, 4), new(0.34f, 0.34f, 0.34f), 2, 4));
    }
}
