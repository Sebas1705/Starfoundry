using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The exterior hardware pass (visual pass on the modules): what makes the outside of each module kind readable at a glance and gives a ship or a
/// station a silhouette beyond the plain tube or box. Roof hardware (cargo containers, reactor cooling spokes and stacks, engine radiator fins, a
/// workshop crane, a core crown, tanks, vents and a radome on hull sections), and on a station a cornice with a light row and conduits on every open flank.
///
/// Nothing here is walkable or solid for the simulation: every piece stands on the roof or on the outside of a wall, a few centimetres embedded in the
/// skin so the audit finds it attached (<see cref="MeshAudit"/>: no floating parts), and nothing stands in a window. A module that has another module on
/// top gets no roof hardware. Parts: "Hardware" (plain structural material) and "GlowStrip" (the lit pieces, steady).
/// </summary>
public static partial class ModuleModels
{
    private const float Embed = 0.02f; // how far hardware sinks into the skin it stands on

    private static void Exterior(Cx c)
    {
        if (c.Kind is ModuleKind.Defence or ModuleKind.DroneBay or ModuleKind.Shield or ModuleKind.Sensor or ModuleKind.SensorShort or ModuleKind.SensorLong
            or ModuleKind.CommsAntenna or ModuleKind.MiningLaser or ModuleKind.MiningLaserMk2 or ModuleKind.Gun or ModuleKind.GunMk2 or ModuleKind.GunHeavy or ModuleKind.Shipyard) return;
        Models.Add(c.R, "Hardware", $"{c.K}.hardware", ModuleStruct(), mb => Hardware(c, mb, false));
        Models.Add(c.R, "GlowStrip", $"{c.K}.hardware.lit", Models.Glow(c.Kind == ModuleKind.Engine ? EngineOrange : c.Kind == ModuleKind.Core ? Gold : c.Role, 2.0f), mb => Hardware(c, mb, true));
    }

    private static void Hardware(Cx c, Models.Mb mb, bool lit)
    {
        mb.At(default);
        if (!c.Has(Up)) RoofHardware(c, mb, lit);
        if (!c.Ship) WallTrim(c, mb, lit);
    }

    // ---- roofs ------------------------------------------------------------------------------------------------------------------

    private static void RoofHardware(Cx c, Models.Mb mb, bool lit)
    {
        switch (c.Kind)
        {
            case ModuleKind.Cargo: RoofContainers(c, mb, lit); break;
            case ModuleKind.Reactor or ModuleKind.ReactorMk2: RoofReactor(c, mb, lit); break;
            case ModuleKind.Engine: RoofRadiators(c, mb, lit); break;
            case ModuleKind.Workshop: RoofCrane(c, mb, lit); break;
            case ModuleKind.Core: RoofCrown(c, mb, lit); break;
            case ModuleKind.Hull: RoofClutter(c, mb, lit); break;
        }
    }

    /// <summary>A shipping container lying on the roof: corrugated long sides, corner posts, a colour band, a role-colour door plate on each end and two clamps to the roof.</summary>
    private static void Container(Models.Mb mb, bool lit, float x, float z, float sx, float sz, Color body, Color band, Color role, bool alongX = true)
    {
        const float y0 = Roof - Embed, h = 1.04f;
        float cy = y0 + h / 2;
        // the long axis is the one the caller gave the larger size on; ribs run across it
        float lx = alongX ? sx : sz, lz = alongX ? sz : sx; // (lx along the container, lz across)
        Vector3 P(float along, float y, float across) => alongX ? new Vector3(x + along, y, z + across) : new Vector3(x + across, y, z + along);
        Vector3 S(float along, float y, float across) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);
        if (lit)
        {
            foreach (float e in new[] { -1f, 1f }) mb.C(role).Block(P(e * (lx / 2 + 0.012f), cy + 0.06f, 0f), S(0.03f, 0.18f, 0.7f));
            return;
        }
        mb.C(body).Block(P(0, cy, 0), S(lx, h, lz));
        int ribs = Math.Max(2, (int)(lx / 0.42f));
        mb.C(new Color(body.R * 0.8f, body.G * 0.8f, body.B * 0.8f));
        for (int i = 0; i < ribs; i++)
            foreach (float side in new[] { -1f, 1f })
                mb.Block(P(-lx / 2 + (i + 0.5f) * lx / ribs, cy, side * (lz / 2 + 0.012f)), S(0.12f, h - 0.16f, 0.036f));
        mb.C(band).Block(P(0, cy + h / 2 - 0.22f, 0), S(lx + 0.03f, 0.1f, lz + 0.03f)); // the band round the top
        mb.C(Dark);
        foreach (float e in new[] { -1f, 1f })
        {
            foreach (float side in new[] { -1f, 1f })
                mb.Block(P(e * (lx / 2 - 0.05f), cy, side * (lz / 2 - 0.05f)), S(0.14f, h + 0.02f, 0.14f)); // corner posts
            mb.Block(P(e * (lx / 2 + 0.004f), cy - 0.12f, 0f), S(0.02f, 0.62f, lz - 0.34f)); // the door of the end face
            mb.C(Steel).Block(P(e * (lx / 2 + 0.02f), cy - 0.12f, -0.08f), S(0.02f, 0.5f, 0.05f)); // lock bars
            mb.Block(P(e * (lx / 2 + 0.02f), cy - 0.12f, 0.08f), S(0.02f, 0.5f, 0.05f));
            mb.C(Dark);
        }
        mb.C(Steel);
        foreach (float a in new[] { -lx / 2 + 0.6f, lx / 2 - 0.6f }) mb.Block(P(a, Roof + 0.03f, 0f), S(0.3f, 0.08f, lz + 0.16f)); // clamps to the roof
    }

    private static void RoofContainers(Cx c, Models.Mb mb, bool lit)
    {
        Color[] bodies = { Crate, new Color(0.34f, 0.46f, 0.58f), new Color(0.50f, 0.52f, 0.30f) };
        var b0 = c.Ship ? bodies[c.V % 3] : Steel;
        var b1 = bodies[(c.V + 1) % 3];
        if (c.Ship)
        {
            Container(mb, lit, 4f, 1.4f, 4.2f, 2.0f, b0, c.Acc, c.Role);
            Container(mb, lit, 4f, 6.6f, 4.2f, 2.0f, b1, c.Acc, c.Role);
        }
        else
        {
            Container(mb, lit, 2.5f, 6.3f, 2.6f, 1.5f, b0, c.Acc, c.Role);
            Container(mb, lit, 5.6f, 6.3f, 2.6f, 1.5f, b1, c.Acc, c.Role);
        }
    }

    /// <summary>
    /// A ship reactor: a ring main with eight cooling spokes round the hatch and four exhaust stacks with a lit tip. A station reactor: a cooling tower with a fin
    /// ring and a lit top plate.
    /// </summary>
    private static void RoofReactor(Cx c, Models.Mb mb, bool lit)
    {
        var plate = c.Ship ? Panel : SlabLt;
        if (c.Ship)
        {
            if (lit)
            {
                for (int i = 0; i < 4; i++)
                {
                    float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                    mb.C(c.Role).Prism(new(4 + MathF.Cos(a) * 2.35f, Roof + 0.52f, 4 + MathF.Sin(a) * 2.35f), 0.12f, 0.1f, 0.05f, 6);
                }
                return;
            }
            mb.C(Steel);
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Tau * i / 8 + Mathf.Pi / 8;
                mb.Beam(new(4 + MathF.Cos(a) * 1.5f, Roof + 0.2f, 4 + MathF.Sin(a) * 1.5f), new(4 + MathF.Cos(a) * 2.35f, Roof + 0.1f, 4 + MathF.Sin(a) * 2.35f), 0.12f);
            }
            mb.C(c.Acc).Torus(new(4, Roof + 0.1f, 4), 2.35f, 0.07f, 24, 4);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                float sx = 4 + MathF.Cos(a) * 2.35f, sz = 4 + MathF.Sin(a) * 2.35f;
                mb.C(plate).Prism(new(sx, Roof - Embed, sz), 0.22f, 0.17f, 0.52f, 8);
                mb.C(Dark).Prism(new(sx, Roof + 0.46f, sz), 0.19f, 0.19f, 0.05f, 8);
            }
            return;
        }
        if (lit)
        {
            mb.C(c.Role).Prism(new(4, Roof + 0.93f, 4), 0.85f, 0.85f, 0.03f, 12);
            mb.Torus(new(4, Roof + 0.7f, 4), 1.28f, 0.035f, 24, 4);
            return;
        }
        mb.C(Slab).Prism(new(4, Roof - Embed, 4), 1.7f, 1.45f, 0.55f, 12);
        mb.C(SlabLt).Prism(new(4, Roof + 0.5f, 4), 1.45f, 1.1f, 0.4f, 12);
        mb.C(Dark).Prism(new(4, Roof + 0.9f, 4), 1.1f, 1.1f, 0.04f, 12);
        mb.C(c.Acc).Prism(new(4, Roof + 0.45f, 4), 1.5f, 1.5f, 0.07f, 12);
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8 + Mathf.Pi / 8;
            mb.C(Steel).Beam(new(4 + MathF.Cos(a) * 1.75f, Roof - 0.02f, 4 + MathF.Sin(a) * 1.75f), new(4 + MathF.Cos(a) * 1.25f, Roof + 0.62f, 4 + MathF.Sin(a) * 1.25f), 0.12f);
        }
        foreach (float sx in new[] { 0.9f, W - 0.9f }) // two stub stacks at the front corners
        {
            mb.C(Steel).Prism(new(sx, Roof - Embed, 1.0f), 0.2f, 0.15f, 0.7f, 8);
            mb.C(Dark).Prism(new(sx, Roof + 0.64f, 1.0f), 0.17f, 0.17f, 0.05f, 8);
        }
    }

    /// <summary>An engine's radiator: a plate on the roof carrying a row of tall fins with a lit orange edge, so the heat-rejection end of a ship is easy to find.</summary>
    private static void RoofRadiators(Cx c, Models.Mb mb, bool lit)
    {
        const float z = 2.2f;
        if (lit)
        {
            for (int i = 0; i < 6; i++) mb.Block(new(2.3f + i * 0.68f, Roof + 0.68f, z), new(0.09f, 0.035f, 2.5f));
            return;
        }
        mb.C(Dark).Block(new(4, Roof + 0.05f, z), new(4.1f, 0.12f, 3.0f));
        for (int i = 0; i < 6; i++)
        {
            float x = 2.3f + i * 0.68f;
            mb.C(c.Ship ? Panel : Slab).Block(new(x, Roof + 0.35f, z), new(0.07f, 0.64f, 2.8f), 1f, 0.92f);
            mb.C(Steel).Block(new(x, Roof + 0.12f, z - 0.55f), new(0.16f, 0.16f, 0.16f)); // a standoff
            mb.Block(new(x, Roof + 0.12f, z + 0.55f), new(0.16f, 0.16f, 0.16f));
        }
    }

    /// <summary>A gantry crane over a workshop: two rails on the roof, a cross beam on end trucks, a trolley with a cab and a jib with a hanging hook.</summary>
    private static void RoofCrane(Cx c, Models.Mb mb, bool lit)
    {
        const float zg = 3.5f;
        if (lit)
        {
            mb.Block(new(3.3f, Roof + 0.62f, zg - 0.31f), new(0.5f, 0.12f, 0.02f)); // the cab window
            mb.Block(new(5.6f, Roof + 1.08f, zg), new(0.1f, 0.06f, 0.1f)); // jib lamp
            return;
        }
        mb.C(Dark);
        foreach (float x in new[] { 1.7f, W - 1.7f }) mb.Block(new(x, Roof + 0.05f, 4), new(0.16f, 0.12f, 6.6f));
        mb.C(Yellow);
        foreach (float x in new[] { 1.7f, W - 1.7f }) mb.Block(new(x, Roof + 0.2f, zg), new(0.3f, 0.3f, 0.6f)); // end trucks
        mb.Block(new(4, Roof + 0.3f, zg), new(4.6f, 0.2f, 0.3f));
        mb.C(c.Ship ? White : SlabLt).Block(new(3.3f, Roof + 0.62f, zg), new(0.9f, 0.5f, 0.6f), 0.8f, 1f);
        mb.C(Dark).Block(new(4.7f, Roof + 0.5f, zg), new(0.7f, 0.18f, 0.5f)); // trolley
        mb.C(Steel).Beam(new(4.7f, Roof + 0.5f, zg), new(5.6f, Roof + 1.05f, zg), 0.09f); // jib
        mb.Beam(new(5.6f, Roof + 1.05f, zg), new(5.6f, Roof + 0.45f, zg), 0.035f); // cable
        mb.C(Yellow).Block(new(5.6f, Roof + 0.42f, zg), new(0.18f, 0.14f, 0.18f)); // hook block
    }

    /// <summary>The crown of a core: an octagonal drum with buttresses, a gold band and a spire with a lit orb and halo, so the heart of a station is the tallest thing on it.</summary>
    private static void RoofCrown(Cx c, Models.Mb mb, bool lit)
    {
        if (lit)
        {
            mb.C(Gold).Sphere(new(4, Roof + 2.0f, 4), new(0.26f, 0.26f, 0.26f), 3, 8);
            mb.Torus(new(4, Roof + 1.25f, 4), 0.85f, 0.045f, 18, 4);
            for (int i = 0; i < 4; i++) // four spokes tie the halo to the spire
            {
                float a = Mathf.Tau * i / 4;
                mb.Beam(new(4, Roof + 1.25f, 4), new(4 + MathF.Cos(a) * 0.85f, Roof + 1.25f, 4 + MathF.Sin(a) * 0.85f), 0.05f);
            }
            return;
        }
        mb.C(c.Ship ? Steel : Slab).Prism(new(4, Roof - Embed, 4), 2.3f, 1.9f, 0.45f, 8, 22.5f);
        mb.C(SlabLt).Prism(new(4, Roof + 0.4f, 4), 1.55f, 1.15f, 0.4f, 8, 22.5f);
        mb.C(Gold).Prism(new(4, Roof + 0.36f, 4), 1.62f, 1.62f, 0.07f, 8, 22.5f);
        mb.C(Dark).Prism(new(4, Roof + 0.8f, 4), 1.15f, 1.15f, 0.04f, 8, 22.5f);
        mb.C(Panel).Prism(new(4, Roof + 0.82f, 4), 0.34f, 0.12f, 1.2f, 8);
        mb.C(Gold).Prism(new(4, Roof + 1.0f, 4), 0.42f, 0.42f, 0.06f, 8); // a collar round the spire
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8 + Mathf.Pi / 8;
            mb.C(Steel).Beam(new(4 + MathF.Cos(a) * 2.1f, Roof - 0.02f, 4 + MathF.Sin(a) * 2.1f), new(4 + MathF.Cos(a) * 1.2f, Roof + 0.62f, 4 + MathF.Sin(a) * 1.2f), 0.16f);
        }
    }

    /// <summary>Roof clutter of a hull section, one of three by variant: a radome on a mast, a pair of vent boxes with a duct, or a pair of banded tanks. They sit in the aft-starboard corner, clear of the strobe and the station roof vents.</summary>
    private static void RoofClutter(Cx c, Models.Mb mb, bool lit)
    {
        var hull = c.Ship ? White : SlabLt;
        switch (c.V)
        {
            case 0:
                if (lit) { mb.C(c.Role).Sphere(new(5.4f, Roof + 1.6f, 2.2f), new(0.08f, 0.08f, 0.08f), 2, 6); return; }
                mb.C(Dark).Block(new(5.4f, Roof + 0.04f, 2.2f), new(1.1f, 0.12f, 1.1f));
                mb.C(Steel).Prism(new(5.4f, Roof + 0.08f, 2.2f), 0.17f, 0.1f, 1.1f, 6);
                mb.C(hull).Sphere(new(5.4f, Roof + 1.15f, 2.2f), new(0.55f, 0.3f, 0.55f), 3, 10, dome: true);
                mb.C(c.Acc).Torus(new(5.4f, Roof + 1.17f, 2.2f), 0.55f, 0.03f, 14, 4);
                mb.C(Steel).Beam(new(5.4f, Roof + 1.4f, 2.2f), new(5.4f, Roof + 1.62f, 2.2f), 0.04f);
                break;
            case 1:
                if (lit) { foreach (float z in new[] { 1.8f, 3.4f }) mb.C(c.Acc).Block(new(5.4f, Roof + 0.38f, z), new(0.7f, 0.02f, 0.05f)); return; }
                foreach (float z in new[] { 1.8f, 3.4f })
                {
                    mb.C(hull).Block(new(5.4f, Roof + 0.16f, z), new(1.15f, 0.4f, 0.8f), 0.92f, 0.92f);
                    mb.C(Dark).Block(new(5.4f, Roof + 0.37f, z), new(0.9f, 0.03f, 0.58f));
                    for (int i = 0; i < 4; i++) mb.C(Steel).Block(new(5.4f, Roof + 0.4f, z - 0.24f + i * 0.16f), new(0.84f, 0.025f, 0.05f));
                }
                mb.C(Steel).Beam(new(5.4f, Roof + 0.2f, 1.8f), new(5.4f, Roof + 0.2f, 3.4f), 0.14f);
                break;
            default:
                if (lit) return;
                foreach (float x in new[] { 4.85f, 5.95f })
                {
                    ZPrism(mb, x, Roof + 0.36f, 1.2f, 0.42f, 0.42f, 2.6f, 10, hull);
                    foreach (float z in new[] { 1.65f, 2.5f, 3.35f }) ZPrism(mb, x, Roof + 0.36f, z, 0.45f, 0.45f, 0.1f, 10, c.Acc);
                    ZPrism(mb, x, Roof + 0.36f, 3.78f, 0.42f, 0.2f, 0.18f, 10, Steel);
                }
                mb.At(default).C(Dark).Block(new(5.4f, Roof + 0.04f, 2.5f), new(1.9f, 0.1f, 2.4f));
                break;
        }
    }

    // ---- station walls ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A cornice with a row of small lights under it along every open side wall of a station module, and conduits with brackets between the
    /// windows and the louvres. They stand on the outside of the slab (embedded 2 cm), clear of the viewports, doors and louvres of each kind.
    /// </summary>
    private static void WallTrim(Cx c, Models.Mb mb, bool lit)
    {
        if (c.Kind is ModuleKind.Dock) return;
        float bev = StationBevel(c, Roof), dy = bev > 0f ? -0.41f : 0f; // under a roof chamfer the cornice drops between the window frames and the wall top, and the lamp row and conduit strip go
        foreach (float sd in new[] { -1f, 1f }) // sd = outward direction along x: -1 on the left wall
        {
            if (sd < 0 ? c.Has(Left) : c.Has(Right)) continue;
            float face = sd < 0 ? StationFace : W - StationFace;
            if (lit)
            {
                if (bev == 0f) for (int i = 0; i < 9; i++) mb.C(c.Acc).Block(new(face + sd * 0.145f, 3.14f, 0.9f + i * 0.77f), new(0.03f, 0.06f, 0.14f));
                continue;
            }
            mb.C(SlabLt).Block(new(face + sd * 0.06f, 3.36f + dy, 4f), new(0.16f, 0.22f, 6.9f), 1f, 1f);
            if (bev == 0f) mb.C(Dark).Block(new(face + sd * 0.1f, 3.22f, 4f), new(0.08f, 0.05f, 6.9f));
            bool front = c.Kind is ModuleKind.Hull or ModuleKind.Workshop or ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Core or ModuleKind.Cockpit;
            bool back = c.Kind is ModuleKind.Hull or ModuleKind.Workshop or ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Core;
            foreach (float z in new[] { 2.15f, 5.85f })
            {
                if (z < 4f ? !front : !back) continue;
                mb.C(Steel).Prism(new(face + sd * 0.07f, 0.35f, z), 0.085f, 0.085f, bev > 0f ? 2.55f : 2.95f, 6);
                mb.C(c.Acc).Prism(new(face + sd * 0.07f, 1.2f, z), 0.115f, 0.115f, 0.08f, 6);
                mb.C(Dark);
                foreach (float y in new[] { 0.7f, 1.7f, 2.7f }) mb.Block(new(face + sd * 0.05f, y, z), new(0.1f, 0.06f, 0.26f));
            }
        }
    }
}
