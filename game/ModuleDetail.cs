using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The detail pass over every module, called once from <see cref="Build"/> after the shell and the doorways: a light per module with a
/// colour per kind (cool cockpit, warm workshop, red-tinted defence deck, green reactor, orange engine), steady emissive strips on the walls
/// and ceiling, faint light shafts, wall lockers and tanks, floor chevrons, and the outside extras that tell a ship from a station
/// (cargo pods and gun blisters on ships, strobes, roof vents and louvres on stations).
///
/// Everything is built through the shared mesh cache of <see cref="Models"/>, so all modules of a kind with the same neighbour mask share
/// their meshes. Nothing here allocates per frame; the lights fade out with distance.
/// Node names: "ModuleLight" (OmniLight3D, dimmed amber by <see cref="Models.SetAmber"/>), "GlowStrip" (steady strip lights), "Shaft"
/// (translucent light shaft, never animated), "GlowBlink*" (strobes, see <see cref="Models.AnimateBlink"/>).
/// </summary>
public static partial class ModuleModels
{
    private static readonly Color EngineOrange = new(0.97f, 0.42f, 0.07f);

    private static void Details(Cx c)
    {
        Lighting(c);
        // every plain-material piece (lockers, tanks, decals, pods, louvres...) is one mesh per module shape; the lit pieces stay separate
        Models.Add(c.R, "Detail", $"{c.K}.detail", ModuleHull(), mb =>
        {
            PropsBody(c, mb);
            DecalsBody(c, mb);
            OutsideBody(c, mb);
        });
        Emissive(c);
    }

    /// <summary>Lit details: the cockpit chart, reactor screens and the strobes (a white one on each wing of a ship, a red one on a station roof corner).</summary>
    private static void Emissive(Cx c)
    {
        if (c.Kind is ModuleKind.Reactor or ModuleKind.ReactorMk2 && !c.Has(Rear)) // the console stands at the rear wall, and only where there is no doorway
            Models.Add(c.R, "GlowStrip", $"{c.K}.screens", Models.Glow(c.Role, 2.0f), mb =>
            {
                mb.At(default);
                foreach (float x in new[] { 3.0f, 4.0f, 5.0f }) mb.Block(new(x, 1.06f, D - 0.88f), new(0.55f, 0.012f, 0.3f));
            });
        if (c.Ship)
        {
            if (c.Kind is ModuleKind.Hull or ModuleKind.Cargo or ModuleKind.Reactor)
                foreach (float s in new[] { -1f, 1f })
                    if (!(s < 0 ? c.Has(Left) : c.Has(Right))) StrobeAt(c, s < 0 ? "wingL" : "wingR", s < 0 ? -1.9f : W + 1.9f, Starfoundry.Ui.HullProfile.WingY + 0.25f, 5.45f);
            return;
        }
        if (c.Kind is ModuleKind.Workshop or ModuleKind.Defence or ModuleKind.DroneBay || c.Has(Up)) return;
        float top = c.Kind == ModuleKind.Shield || ModuleKindIds.IsAntenna(c.Kind) ? Starfoundry.Ui.HullProfile.RoofOf(c.Kind, c.Ship) : Roof;
        StrobeAt(c, "roof", W - 0.6f, top + 0.12f, 0.6f, new Color(1f, 0.15f, 0.12f));
    }

    // ---- lighting ------------------------------------------------------------------------------------------

    private static (Color Col, float Energy, float Range, float Y) LightFor(ModuleKind kind) => kind switch
    {
        ModuleKind.Cockpit => (new Color(0.55f, 0.80f, 1f), 1.3f, 8f, 3.2f),
        ModuleKind.Workshop => (new Color(1f, 0.80f, 0.52f), 1.9f, 9f, 3.3f),
        ModuleKind.Defence => (new Color(1f, 0.30f, 0.25f), 1.4f, 8f, 2.6f),
        ModuleKind.Reactor => (new Color(0.30f, 1f, 0.50f), 2.4f, 8f, 2.0f),
        ModuleKind.ReactorMk2 => (new Color(0.40f, 0.90f, 1f), 2.4f, 8f, 2.2f),
        ModuleKind.Engine => (new Color(1f, 0.50f, 0.20f), 1.6f, 8f, 2.2f),
        ModuleKind.Cargo => (new Color(1f, 0.85f, 0.60f), 1.0f, 8f, 3.0f),
        ModuleKind.DroneBay => (new Color(0.85f, 0.55f, 1f), 1.3f, 8f, 3.0f),
        ModuleKind.Shield => (new Color(0.35f, 0.65f, 1f), 1.2f, 7f, 2.2f),
        ModuleKind.Sensor or ModuleKind.SensorShort or ModuleKind.SensorLong or ModuleKind.CommsAntenna => (new Color(0.30f, 1f, 0.60f), 1.1f, 7f, 2.6f),
        ModuleKind.Core => (new Color(1f, 0.80f, 0.40f), 1.8f, 9f, 2.4f),
        _ => (new Color(0.75f, 0.85f, 1f), 0.8f, 7f, 3.2f),
    };

    private static void Lighting(Cx c)
    {
        var (col, energy, range, y) = LightFor(c.Kind);
        c.R.AddChild(new OmniLight3D
        {
            Name = "ModuleLight", Position = new Vector3(4f, y, 4f), LightColor = col, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.3f,
            ShadowEnabled = false, LightSpecular = 0.3f, DistanceFadeEnabled = true, DistanceFadeBegin = 38f, DistanceFadeLength = 22f,
        });

        bool openDeck = c.Kind is ModuleKind.Workshop or ModuleKind.Defence or ModuleKind.DroneBay;
        bool lowShell = c.Kind is ModuleKind.Shield || ModuleKindIds.IsAntenna(c.Kind) || (c.Ship && ModuleKindIds.IsHardpoint(c.Kind)); // pods have no ceiling to hang a strip on
        float roof = Starfoundry.Ui.HullProfile.RoofOf(c.Kind, c.Ship);
        var strip = c.Kind switch { ModuleKind.Defence => new Color(1f, 0.25f, 0.22f), ModuleKind.Workshop => new Color(1f, 0.85f, 0.6f), _ => col };
        float wx = c.Ship ? 0.11f : 0.37f, wy = c.Ship ? 2.55f : 3.2f;
        if (c.Ship && !lowShell) wy = MathF.Min(wy, roof - Starfoundry.Ui.HullProfile.ChamfersOf(Starfoundry.Ui.HullProfile.SectionOfMask(c.M), 1f).Up - 0.2f); // on the vertical wall of this skin, not out in the air beside a sloping one
        if (lowShell) wy = c.Kind == ModuleKind.Shield ? 0.9f : ModuleKindIds.IsHardpoint(c.Kind) ? 0.7f : c.Kind == ModuleKind.Sensor ? 1.7f : roof - MathF.Min(Starfoundry.Ui.HullProfile.MaxChamfer, (roof - Starfoundry.Ui.HullProfile.FloorY) * Starfoundry.Ui.HullProfile.ChamferShare) - 0.2f;
        Models.Add(c.R, "GlowStrip", $"{c.K}.strips", Models.Glow(strip, 1.7f), mb =>
        {
            mb.At(default);
            // a light band along each side wall, split round a doorway
            foreach (float x in new[] { wx, W - wx })
            {
                bool doored = x < 4f ? c.Has(Left) : c.Has(Right);
                if (doored && c.Ship) continue; // a ship has no wall beside a doorway to hang a strip on
                if (doored)
                {
                    mb.Block(new(x, wy, 1.1f), new(0.05f, 0.07f, 1.7f));
                    mb.Block(new(x, wy, 6.9f), new(0.05f, 0.07f, 1.7f));
                }
                else mb.Block(new(x, wy, 4f), new(0.05f, 0.07f, 7.0f));
            }
            if (openDeck || lowShell || c.Has(Up) || (c.Kind == ModuleKind.Cockpit && !c.Has(Up))) return; // the cockpit has a glass roof: no ceiling strips
            // two ceiling strips along Z; a ship's cockpit has a glass roof so its strips hang a little lower
            float cy = (c.Kind == ModuleKind.Cockpit && c.Ship ? roof - 0.5f : roof - 0.1f);
            foreach (float x in new[] { 2.3f, 5.7f }) mb.Block(new(x, cy, 4f), new(0.16f, 0.04f, 5.6f));
        });

        // light shafts: a faint translucent cone under the ceiling lamps of closed modules, so a lit interior has some air in it
        if (!openDeck && !lowShell && c.Kind != ModuleKind.Cockpit)
            Models.Add(c.R, "Shaft", $"{c.K}.shaft", Models.GlowAlpha(col, 0.5f, 0.05f), mb =>
            {
                mb.At(default);
                foreach (float x in new[] { 2.3f, 5.7f })
                    mb.Prism(new(x, 0.12f, 4f), 1.1f, 0.12f, roof - 0.3f, 8);
            });
    }

    // ---- props -----------------------------------------------------------------------------------------------

    /// <summary>A bank of wall lockers: tall boxes with a door seam and a role-colour tag; hung on the wall of a ship, standing on the floor of a station.</summary>
    private static void Lockers(Models.Mb mb, Cx c, bool left, float z0, int count)
    {
        float x = left ? (c.Ship ? 0.40f : 0.64f) : W - (c.Ship ? 0.40f : 0.64f);
        float h = c.Ship ? 1.55f : 1.9f, y = c.Ship ? 1.95f : 0.1f + 0.95f;
        float s = left ? 1f : -1f;
        for (int i = 0; i < count; i++)
        {
            float z = z0 + 0.25f + i * 0.5f;
            mb.C(c.Ship ? Panel : Slab).Block(new(x, y, z), new(0.46f, h, 0.46f));
            mb.C(Dark).Block(new(x + s * 0.235f, y, z), new(0.02f, h - 0.12f, 0.4f));
            mb.C(c.Role).Block(new(x + s * 0.24f, y + h * 0.3f, z), new(0.02f, 0.14f, 0.3f));
            mb.C(Steel).Block(new(x + s * 0.245f, y - 0.1f, z + 0.14f), new(0.02f, 0.2f, 0.04f));
        }
    }

    private static void PropsBody(Cx c, Models.Mb mb)
    {
        switch (c.Kind)
        {
            case ModuleKind.Hull:
                {
                    mb.At(default);
                    if (!c.Has(Left) && !(c.Ship && Starfoundry.Ui.HullProfile.HasWallFurniture(c.M))) Lockers(mb, c, true, 0.35f, 3); // wall lockers hang only on a closed wall: an open side is a doorway into the next module
                    if (!c.Has(Right) && !(c.Ship && Starfoundry.Ui.HullProfile.HasWallFurniture(c.M))) Lockers(mb, c, false, 6.1f, 3);
                    // an extinguisher and a first-aid box by the left doorway
                    if (!c.Has(Left))
                    {
                        mb.C(new Color(0.85f, 0.15f, 0.12f)).Prism(new(c.Ship ? 0.30f : 0.55f, c.Ship ? 1.2f : 0.9f, 2.2f), 0.07f, 0.07f, 0.4f, 6);
                    mb.C(White).Block(new(c.Ship ? 0.22f : 0.50f, c.Ship ? 1.55f : 1.25f, 5.9f), new(0.08f, 0.3f, 0.3f));
                    mb.C(new Color(0.85f, 0.15f, 0.12f)).Block(new(c.Ship ? 0.255f : 0.535f, c.Ship ? 1.55f : 1.25f, 5.9f), new(0.02f, 0.18f, 0.06f));
                    mb.C(new Color(0.85f, 0.15f, 0.12f)).Block(new(c.Ship ? 0.255f : 0.535f, c.Ship ? 1.55f : 1.25f, 5.9f), new(0.02f, 0.06f, 0.18f));
                    }
                }
                break;
            case ModuleKind.Engine:
                {
                    mb.At(default);
                    // coolant tanks along the walls, in the engine's orange
                    foreach (float s in new[] { -1f, 1f })
                        for (int i = 0; i < 2; i++)
                        {
                            float x = s < 0 ? 1.15f : W - 1.15f, z = 0.9f + i * 1.1f;
                            mb.C(Panel).Prism(new(x, 0.09f, z), 0.4f, 0.4f, 1.2f, 8);
                            mb.C(EngineOrange).Prism(new(x, 0.5f, z), 0.42f, 0.42f, 0.12f, 8);
                            mb.C(Steel).Prism(new(x, 1.29f, z), 0.2f, 0.2f, 0.12f, 6);
                        }
                }
                break;
            case ModuleKind.Defence:
                {
                    mb.At(default);
                    // ammo racks at the back corners: hazard-banded boxes with a shell row
                    foreach (float x in new[] { 1.0f, W - 1.0f })
                    {
                        mb.C(Slab).Block(new(x, 0.55f, D - 1.0f), new(1.0f, 0.9f, 0.9f));
                        mb.C(Yellow).Block(new(x, 0.55f, D - 1.0f), new(1.04f, 0.14f, 0.94f));
                        for (int i = 0; i < 4; i++) mb.C(i % 2 == 0 ? Amber : Steel).Prism(new(x - 0.36f + i * 0.24f, 1.0f, D - 1.0f), 0.08f, 0.04f, 0.28f, 5);
                    }
                }
                break;
            case ModuleKind.Workshop:
                {
                    mb.At(default);
                    // tool lockers flush against the side walls (the floor cells stay free for machines)
                    if (!c.Has(Left) && !(c.Ship && Starfoundry.Ui.HullProfile.HasWallFurniture(c.M))) Lockers(mb, c, true, 0.35f, 3);
                    if (!c.Has(Right) && !(c.Ship && Starfoundry.Ui.HullProfile.HasWallFurniture(c.M))) Lockers(mb, c, false, 4.9f, 5);
                    mb.C(Yellow);
                    for (int i = 0; i < 4; i++) mb.Block(new(4, Roof - 0.35f, 1.0f + i * 2f), new(W - 3.6f, 0.05f, 0.16f)); // cable trays under the gantry
                }
                break;
            case ModuleKind.Reactor:
            case ModuleKind.ReactorMk2:
                {
                    mb.At(default);
                    // a control console at the rear wall facing the vessel
                    if (!c.Has(Rear)) // a doorway behind leaves no wall for it
                    mb.C(Dark).Block(new(4, 0.5f, D - 0.7f), new(2.6f, 0.9f, 0.6f), 1f, 0.5f);
                    if (!c.Has(Rear)) // a doorway behind leaves no wall for it
                    mb.C(Steel).Block(new(4, 1.0f, D - 0.72f), new(2.7f, 0.07f, 0.45f));
                    if (!c.Has(Rear)) // a doorway behind leaves no wall for it
                    foreach (float x in new[] { 3.0f, 5.0f }) mb.C(c.Role).Block(new(x, 0.96f, D - 0.95f), new(0.3f, 0.02f, 0.2f));
                }
                break;
            case ModuleKind.Cargo:
                {
                    mb.At(default).C(Steel);
                    // a cargo lift marker and a pallet jack near the door
                    mb.Block(new(4, 0.1f, 4), new(2.2f, 0.03f, 2.2f));
                    mb.C(Yellow).Block(new(4, 0.115f, 4), new(2.3f, 0.01f, 0.1f));
                    mb.Block(new(4, 0.115f, 4), new(0.1f, 0.01f, 2.3f));
                }
                break;
        }
    }

    // ---- floor decals --------------------------------------------------------------------------------------------

    /// <summary>Chevron marks along the centre lane of a ship deck pointing to the nose, and hazard corner marks on a station deck.</summary>
    private static void DecalsBody(Cx c, Models.Mb mb)
    {
        if (c.Kind is ModuleKind.Workshop or ModuleKind.Defence or ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Core or ModuleKind.Shield || ModuleKindIds.IsAntenna(c.Kind)) return;
        {
            mb.At(default);
            const float y0 = 0.095f, y1 = 0.104f;
            var tint = new Color(c.Role.R * 0.7f, c.Role.G * 0.7f, c.Role.B * 0.7f);
            if (c.Ship)
            {
                mb.C(tint);
                foreach (float z in new[] { 1.3f, 2.1f, 5.9f, 6.7f })
                    if ((c.M & Starfoundry.Ui.HullProfile.StairDown) == 0 || z > 3f) // none over the stairwell
                    foreach (float s in new[] { -1f, 1f })
                        mb.Extrude(new[] { new Vector2(4 + s * 0.45f, z + 0.18f), new Vector2(4 + s * 0.45f - s * 0.1f, z + 0.18f), new Vector2(4, z - 0.12f), new Vector2(4 + s * 0.1f, z - 0.12f) }, y0, y1);
            }
            else
            {
                foreach (float x in new[] { 0.9f, W - 0.9f })
                    foreach (float z in new[] { 0.9f, D - 0.9f })
                        for (int i = 0; i < 3; i++)
                        {
                            mb.C(i % 2 == 0 ? Yellow : Dark);
                            mb.Extrude(new[] { new Vector2(x - 0.4f + i * 0.28f, z - 0.4f), new Vector2(x - 0.2f + i * 0.28f, z - 0.4f), new Vector2(x - 0.1f + i * 0.28f, z - 0.2f), new Vector2(x - 0.3f + i * 0.28f, z - 0.2f) }, y0, y1);
                        }
            }
        }
    }

    // ---- the outside: what tells a ship from a station -----------------------------------------------------------------

    private static void OutsideBody(Cx c, Models.Mb mb)
    {
        if (c.Ship)
        {
            // Cargo modules carry external pods on exposed flanks; a defence deck shows gun blisters; a sensor a pair of masts.
            if (c.Kind == ModuleKind.Cargo)
                {
                    mb.At(default);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        if (s < 0 ? c.Has(Left) : c.Has(Right)) continue;
                        float x = s < 0 ? -0.55f : W + 0.55f;
                        ZPrism(mb, x, 1.0f, 1.2f, 0.5f, 0.5f, 5.0f, 8, White);
                        ZPrism(mb, x, 1.0f, 0.8f, 0.2f, 0.5f, 0.4f, 8, Panel);
                        ZPrism(mb, x, 1.0f, 6.2f, 0.5f, 0.28f, 0.45f, 8, Panel);
                        ZPrism(mb, x, 1.0f, 2.4f, 0.52f, 0.52f, 0.25f, 8, c.Acc);
                        ZPrism(mb, x, 1.0f, 4.6f, 0.52f, 0.52f, 0.25f, 8, c.Acc);
                        mb.At(default).C(Steel);
                        foreach (float z in new[] { 1.8f, 5.0f }) mb.Beam(new(s < 0 ? 0.05f : W - 0.05f, 1.0f, z), new(x, 1.0f, z), 0.14f);
                    }
                }
            if (c.Kind == ModuleKind.Defence)
                {
                    mb.At(default);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        if (s < 0 ? c.Has(Left) : c.Has(Right)) continue;
                        float x = s < 0 ? -0.25f : W + 0.25f;
                        mb.C(White).Block(new(x, 0.9f, 3.0f), new(0.6f, 0.5f, 1.3f), 0.7f, 0.8f);
                        mb.C(c.Role).Block(new(x + s * 0.2f, 0.92f, 3.0f), new(0.05f, 0.12f, 1.2f));
                        ZPrism(mb, x, 0.9f, 1.0f, 0.07f, 0.07f, 1.9f, 6, Dark);
                        ZPrism(mb, x, 0.9f, 0.7f, 0.1f, 0.1f, 0.3f, 6, Steel);
                    }
                }
            if (c.Kind == ModuleKind.Sensor && !c.Has(Up))
                {
                    mb.At(default).C(Steel);
                    foreach (float x in new[] { 0.9f, W - 0.9f })
                    {
                        mb.Prism(new(x, 2.2f, 0.9f), 0.05f, 0.03f, 2.2f, 5);
                        mb.C(Panel).Block(new(x, 3.4f, 0.9f), new(0.5f, 0.04f, 0.04f));
                        mb.C(Panel).Block(new(x, 3.9f, 0.9f), new(0.3f, 0.04f, 0.04f));
                        mb.C(Steel);
                    }
                }
            return;
        }

        // station: louvres and roof vents on the exposed walls, a red aviation strobe on a roof corner
        if (c.Kind is ModuleKind.Workshop or ModuleKind.Defence or ModuleKind.DroneBay) return;
        float top = c.Kind == ModuleKind.Shield || ModuleKindIds.IsAntenna(c.Kind) ? Starfoundry.Ui.HullProfile.RoofOf(c.Kind, c.Ship) : Roof;
        {
            mb.At(default);
            foreach (float s in new[] { -1f, 1f })
            {
                if (s < 0 ? c.Has(Left) : c.Has(Right)) continue;
                float x = s < 0 ? StationFace - 0.05f : W - StationFace + 0.05f;
                // two louvre panels and a conduit run on the wall
                foreach (float z in new[] { 1.2f, 6.8f })
                {
                    mb.C(Dark).Block(new(x, top * 0.3f, z), new(0.1f, 0.9f, 1.3f));
                    mb.C(Steel);
                    for (int i = 0; i < 5; i++) mb.Block(new(x + s * -0.02f, top * 0.3f - 0.32f + i * 0.16f, z), new(0.12f, 0.04f, 1.2f));
                }
                mb.C(SlabLt).Block(new(x, top * 0.16f, 4f), new(0.08f, 0.14f, 7.0f));
            }
            if (!c.Has(Up))
            {
                // roof vents: two boxes with a dark grille
                foreach (float z in new[] { 2.2f, 5.0f })
                {
                    mb.C(SlabLt).Block(new(2.0f, top + 0.18f, z), new(1.2f, 0.36f, 0.9f));
                    mb.C(Dark).Block(new(2.0f, top + 0.37f, z), new(0.9f, 0.02f, 0.6f));
                }
                mb.C(Steel).Beam(new(2.0f, top + 0.3f, 2.2f), new(2.0f, top + 0.3f, 5.0f), 0.12f);
            }
        }
    }

    private static void StrobeAt(Cx c, string name, float x, float y, float z, Color? colour = null)
    {
        var col = colour ?? new Color(0.9f, 0.95f, 1f);
        Models.Add(c.R, "GlowBlink" + name, $"{c.K}.strobe.{name}.{col.ToHtml()}.{x}.{y}.{z}", Models.Glow(col, 4.5f), mb =>
            mb.At(default).Sphere(new(x, y, z), new(0.12f, 0.1f, 0.12f), 2, 6));
    }
}
