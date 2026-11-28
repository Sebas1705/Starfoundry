using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The frame of a large module (ADR 0051): a heavy girder frame round the whole square of module cells, with corner posts and a cross girder over every seam inside it, so the cells read as one
/// hall under one roof line. The module models of the cells are not touched (their doorways between the cells stay, and the floor is joined in the simulation); the frame is drawn on top of them,
/// at the origin corner of the span like a module model.
/// </summary>
public static partial class ModuleModels
{
    public static Node3D SpanFrame(int size, bool ship)
    {
        var r = new Node3D { Name = $"SpanFrame{size}" };
        float w = size * W, d = size * D, h = H;
        const float t = 0.18f, e = 0.06f;
        Models.Add(r, "Frame", $"span.frame.{size}.{ship}", ModuleStruct(), mb =>
        {
            mb.At(default).C(ship ? Panel : Slab);
            // the twelve edges of the box
            foreach (float y in new[] { e, h - e })
            {
                mb.Beam(new(e, y, e), new(w - e, y, e), t); mb.Beam(new(e, y, d - e), new(w - e, y, d - e), t);
                mb.Beam(new(e, y, e), new(e, y, d - e), t); mb.Beam(new(w - e, y, e), new(w - e, y, d - e), t);
            }
            foreach (float x in new[] { e, w - e })
                foreach (float z in new[] { e, d - e })
                    mb.Beam(new(x, e, z), new(x, h - e, z), t);
            // a girder over every seam inside the span, along both axes, so the joined floor reads as one room
            for (int i = 1; i < size; i++)
            {
                mb.C(Steel).Beam(new(i * W, h - e, e), new(i * W, h - e, d - e), t * 0.7f);
                mb.Beam(new(e, h - e, i * D), new(w - e, h - e, i * D), t * 0.7f);
            }
        });
        return r;
    }
}
