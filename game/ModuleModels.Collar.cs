using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The docking collar (ADR 0047), the small dock piece: a plated flange with a dark hatch, eight capture clamps and a ring of lamps on a horizontal face of an ordinary module. It
/// replaces the dock module's collars: a structure's docks are these pieces, drawn on top of the module they sit on (the module itself is not rebuilt), so the model files of the
/// modules do not change. Parts: "Collars" and "GlowStrip" (the lamps), like the old dock module's.
/// </summary>
public static partial class ModuleModels
{
    /// <summary>
    /// One docking collar on the <paramref name="face"/> of a module, in that module's own box (the node sits at the module's origin corner, like a module model does). A ship's collar
    /// stands a hair outside its hull, a station's against the slab.
    /// </summary>
    public static Node3D CollarPiece(Dir face, bool ship)
    {
        var r = new Node3D { Name = "DockCollar_" + face };
        var role = RoleColour(ModuleKind.Dock);
        var c = new Cx(r, $"collar.{(ship ? "s" : "t")}.{face}", ship, ship ? ShipAccents[0] : StationAccents[0], 0, ModuleKind.Dock, 0, role);
        var (side, _, pos, rot) = Array.Find(CollarSpots(ship ? -0.03f : StationFace), s => s.Side == PortBits(face).Neighbour);
        Models.Add(r, "Collars", $"{c.K}.collar", ModuleStruct(), mb =>
        {
            mb.At(pos, rot).C(Steel).Prism(default, 1.3f, 1.15f, 0.1f, 8, 22.5f);
            mb.C(Dark).Prism(new(0, 0.005f, 0), 0.95f, 0.95f, 0.11f, 8, 22.5f);
            mb.C(role).Prism(new(0, 0.01f, 0), 0.4f, 0.4f, 0.115f, 8, 22.5f);
            mb.C(Panel).Prism(new(0, 0.07f, 0), 1.42f, 1.3f, 0.1f, 8, 22.5f); // a raised docking ring with eight capture clamps round the rim
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Tau * i / 8f;
                mb.C(Steel).Block(new(MathF.Cos(a) * 1.15f, 0.17f, MathF.Sin(a) * 1.15f), new(0.3f, 0.14f, 0.3f), 0.8f, 0.8f);
            }
            mb.At(default);
        });
        Models.Add(r, "GlowStrip", $"{c.K}.collarlights", Models.Glow(role, 2.4f), mb =>
        {
            mb.At(pos, rot);
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Tau * i / 8f;
                mb.Block(new(MathF.Cos(a) * 1.15f, 0.27f, MathF.Sin(a) * 1.15f), new(0.1f, 0.1f, 0.1f)); // a lamp on each clamp
            }
            mb.At(default);
        });
        _ = side;
        return r;
    }
}
