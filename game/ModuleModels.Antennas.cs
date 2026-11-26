using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Models of the antenna variants (ADR 0042), kept apart from the standard sensor. Each reads from outside by its silhouette: the short-range sensor is a squat
/// mast under a wide flat fan, the long-range sensor a tall guyed mast with a long narrow dish, the comms antenna a mast with a yagi boom and four whips.
/// Every piece overlaps the one it stands on (the audit rejects a part in the air). Parts: "Mast", "Dish" (the turning pivot, as on the sensor) and "GlowLamp".
/// </summary>
public static partial class ModuleModels
{
    private static readonly Color CommsAmber = new(1f, 0.70f, 0.25f);

    private static void AntennaModel(Cx c)
    {
        float roof = HullProfile.RoofOf(c.Kind, c.Ship);
        Skin(c, roof, nose: false, tail: false, detail: true, collars: false);
        Frame(c, roof);
        bool shortK = c.Kind == ModuleKind.SensorShort, longK = c.Kind == ModuleKind.SensorLong;
        float mastH = shortK ? 0.45f : longK ? 1.5f : 1.1f;
        float top = roof + 0.1f + mastH;
        var glow = c.Kind == ModuleKind.CommsAntenna ? CommsAmber : RadarGreen;
        Models.Add(c.R, "Mast", $"{c.K}.mast", ModuleStruct(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(4, roof, 4), shortK ? 1.5f : 1.2f, shortK ? 1.3f : 1.0f, 0.14f, 8);
            mb.C(Steel).Prism(new(4, roof + 0.1f, 4), longK ? 0.34f : 0.26f, longK ? 0.16f : 0.2f, mastH, 8);
            mb.C(c.Acc).Prism(new(4, top - 0.06f, 4), 0.34f, 0.34f, 0.07f, 8);
            if (longK) // three guy struts, from the base up the mast
                foreach (float a in new[] { 0f, 2.09f, 4.19f })
                    mb.C(Steel).Beam(new(4 + MathF.Cos(a) * 1.0f, roof + 0.12f, 4 + MathF.Sin(a) * 1.0f), new(4, roof + 0.1f + mastH * 0.7f, 4), 0.045f);
            if (c.Kind == ModuleKind.CommsAntenna) // four whips, each rooted in the base plate
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                        mb.C(Steel).Beam(new(4 + sx * 0.6f, roof + 0.1f, 4 + sz * 0.6f), new(4 + sx * 0.75f, roof + 1.5f, 4 + sz * 0.75f), 0.035f);
        });
        var dish = Models.Pivot(c.R, "Dish", new(4, top - 0.05f, 4));
        if (shortK)
        {
            // a wide, shallow fan: a flat bowl that sees a broad slice of sky, with a short feed on a single strut
            Models.Add(dish, "Bowl", $"{c.K}.bowl", ModuleHull(), mb =>
            {
                mb.At(default).C(c.Ship ? White : Panel).Prism(new(0, 0, 0), 0.3f, 2.1f, 0.4f, 16);
                mb.C(c.Acc).Prism(new(0, 0.4f, 0), 2.1f, 2.1f, 0.05f, 16);
                mb.C(Dark).Prism(new(0, 0.405f, 0), 1.85f, 1.85f, 0.06f, 16);
                mb.C(Steel).Beam(new(0, 0.4f, 0), new(0, 1.0f, 0), 0.06f);
                mb.C(c.Acc).Sphere(new(0, 1.0f, 0), new(0.12f, 0.12f, 0.12f), 2, 6);
            });
        }
        else if (longK)
        {
            // a long narrow dish tilted at the horizon: a tall cone that sees far in one direction, with a feed horn on three struts
            Models.Add(dish, "Bowl", $"{c.K}.bowl", ModuleHull(), mb =>
            {
                mb.At(default, Models.Deg(0, 0, -70)).C(c.Ship ? White : Panel).Prism(new(0, 0, 0), 0.2f, 0.95f, 1.5f, 16);
                mb.C(c.Acc).Prism(new(0, 1.5f, 0), 0.95f, 0.95f, 0.05f, 16);
                mb.C(Dark).Prism(new(0, 1.505f, 0), 0.8f, 0.8f, 0.06f, 16);
                mb.C(Steel);
                foreach (float a in new[] { 0f, 2.09f, 4.19f })
                    mb.Beam(new(MathF.Cos(a) * 0.9f, 1.52f, MathF.Sin(a) * 0.9f), new(0, 2.3f, 0), 0.04f);
                mb.C(c.Acc).Sphere(new(0, 2.3f, 0), new(0.1f, 0.1f, 0.1f), 2, 6);
            });
        }
        else
        {
            // a yagi boom across the mast head: a spine with a reflector and four director rods, a sphere at its middle
            Models.Add(dish, "Boom", $"{c.K}.boom", ModuleHull(), mb =>
            {
                mb.At(default).C(Steel).Beam(new(-1.4f, 0.12f, 0), new(1.4f, 0.12f, 0), 0.05f);
                mb.C(c.Acc).Sphere(new(0, 0.1f, 0), new(0.28f, 0.28f, 0.28f), 2, 8);
                foreach (float x in new[] { -1.1f, -0.4f, 0.3f, 0.95f })
                    mb.C(Panel).Block(new(x, 0.12f, 0), new(0.05f, 0.05f, x < -1f ? 1.3f : 0.9f - 0.15f * (x + 1.1f)));
            });
        }
        Models.Add(c.R, "GlowLamp", $"{c.K}.lamp", Models.Glow(glow, 2.4f), mb =>
            mb.At(default).Sphere(new(4, roof + 0.15f, 4.9f), new(0.12f, 0.12f, 0.12f), 2, 6));
    }
}
