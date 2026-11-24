using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Models of the hardpoint modules and the shipyard (ADR 0032), kept apart from the older module models. Every module is still an 8 x 4 x 8 cell
/// box with its origin at the minimum corner; the front of a ship is -Z. The mining laser carries an emitter along -Z whose muzzle is
/// <see cref="LaserMuzzle"/> (the beam of the game starts there); the gun carries one or two turrets; the shipyard a gantry with a lit cradle.
/// </summary>
public static partial class ModuleModels
{
    private static readonly Color LaserLime = new(0.55f, 1f, 0.25f);
    private static readonly Color LaserCyan = new(0.35f, 1f, 0.85f);
    private static readonly Color GunRed = new(1f, 0.45f, 0.2f);
    private static readonly Color YardOrange = new(1f, 0.62f, 0.15f);

    /// <summary>True for the module kinds this file builds.</summary>
    public static bool IsHardpointModel(ModuleKind kind) =>
        ModuleKindIds.IsHardpoint(kind) || kind == ModuleKind.Shipyard;

    /// <summary>The role colour of a hardpoint or shipyard kind (the hull band, the floor markings and the key of the designer's grid).</summary>
    public static Color HardpointColour(ModuleKind kind) => kind switch
    {
        ModuleKind.MiningLaser => LaserLime,
        ModuleKind.MiningLaserMk2 => LaserCyan,
        ModuleKind.Gun => GunRed,
        ModuleKind.GunMk2 => new Color(1f, 0.30f, 0.45f),
        ModuleKind.Shipyard => YardOrange,
        _ => new Color(0.58f, 0.64f, 0.76f),
    };

    /// <summary>Where a mining laser module's beam leaves it, in cells from the module's minimum corner: the tip of the emitter, ahead of the module's centre.</summary>
    public static Vector3 LaserMuzzle => new(W / 2f, 1.9f, 0.6f);

    private static void HardpointOrYard(Cx c)
    {
        switch (c.Kind)
        {
            case ModuleKind.MiningLaser: MiningLaserModel(c, mk2: false); break;
            case ModuleKind.MiningLaserMk2: MiningLaserModel(c, mk2: true); break;
            case ModuleKind.Gun: GunModel(c, mk2: false); break;
            case ModuleKind.GunMk2: GunModel(c, mk2: true); break;
            default: ShipyardModel(c); break;
        }
    }

    private static void MiningLaserModel(Cx c, bool mk2)
    {
        const float roof = HullProfile.MiningRoof;
        Skin(c, roof, nose: false, tail: false, detail: true, collars: false);
        Frame(c, roof);
        var glow = mk2 ? LaserCyan : LaserLime;
        // a turntable on the roof and the housing of the emitter
        Models.Add(c.R, "Housing", $"{c.K}.laser.housing", Models.HullMat(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(4, roof, 4), 1.5f, 1.35f, 0.25f, 12);
            mb.C(c.Ship ? White : Steel).Prism(new(4, roof + 0.2f, 4), 1.25f, 1.0f, 0.55f, 12);
            mb.C(c.Acc).Prism(new(4, roof + 0.22f, 4), 1.27f, 1.27f, 0.05f, 12);
            mb.C(Dark).Sphere(new(4, roof + 0.78f, 4), new(0.85f, 0.5f, 0.85f), 3, 12, dome: true);
        });
        // the emitter: a barrel along -Z with a focusing ring and a lit lens at its tip (two barrels side by side on the mark 2)
        Models.Add(c.R, "Emitter", $"{c.K}.laser.emitter.{mk2}", Models.HullMat(), mb =>
        {
            mb.At(default);
            foreach (float x in mk2 ? new[] { 3.45f, 4.55f } : new[] { 4f })
            {
                float r = mk2 ? 0.2f : 0.28f;
                mb.C(Steel).Beam(new(x, LaserMuzzle.Y, 4.2f), new(x, LaserMuzzle.Y, 0.9f), r);
                mb.C(c.Acc).Beam(new(x, LaserMuzzle.Y, 1.9f), new(x, LaserMuzzle.Y, 1.55f), r + 0.1f);
                mb.C(Dark).Beam(new(x, LaserMuzzle.Y, 1.1f), new(x, LaserMuzzle.Y, 0.8f), r + 0.07f);
            }
        });
        Models.Add(c.R, "GlowLens", $"{c.K}.laser.lens.{mk2}", Models.Glow(glow, 2.6f), mb =>
        {
            mb.At(default);
            foreach (float x in mk2 ? new[] { 3.45f, 4.55f } : new[] { 4f })
                mb.Sphere(new(x, LaserMuzzle.Y, 0.72f), new(0.17f, 0.17f, 0.12f), 2, 8);
        });
        Models.Add(c.R, "GlowLamp", $"{c.K}.laser.lamp", Models.Glow(glow, 2f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { 1.0f, W - 1.0f })
                for (int i = 0; i < 3; i++) mb.Block(new(s, roof + 0.04f, 1.8f + i * 2.0f), new(0.12f, 0.05f, 0.5f));
        });
    }

    private static void GunModel(Cx c, bool mk2)
    {
        const float roof = HullProfile.GunRoof;
        Skin(c, roof, nose: false, tail: false, detail: true, collars: false);
        Frame(c, roof);
        var spots = mk2 ? new[] { new Vector3(2.5f, roof, 4f), new Vector3(5.5f, roof, 4f) } : new[] { new Vector3(4f, roof, 4f) };
        for (int i = 0; i < spots.Length; i++) Turret(c, i, spots[i]);
        if (mk2)
            Models.Add(c.R, "GunTrim", $"{c.K}.gun.trim", Models.Glow(new Color(1f, 0.3f, 0.45f), 1.8f), mb =>
            {
                mb.At(default);
                foreach (float s in new[] { 1.0f, W - 1.0f })
                    for (int i = 0; i < 3; i++) mb.Block(new(s, roof + 0.04f, 1.6f + i * 2.2f), new(0.12f, 0.05f, 0.5f));
            });
    }

    private static void ShipyardModel(Cx c)
    {
        const float roof = HullProfile.ShipyardRoof;
        Skin(c, roof, nose: false, tail: false, detail: true, collars: true);
        Frame(c, roof);
        // a gantry: two pylons and a cross beam over the cradle, a crane arm with a hook, and a lit cradle on the deck
        Models.Add(c.R, "Gantry", $"{c.K}.yard.gantry", Models.Struct(), mb =>
        {
            mb.At(default);
            mb.C(Steel);
            foreach (float x in new[] { 1.2f, W - 1.2f })
                mb.Block(new(x, roof + 0.25f, 4f), new(0.45f, 1.0f, 0.45f));
            mb.C(c.Acc).Block(new(4f, roof + 0.85f, 4f), new(W - 2.0f, 0.35f, 0.5f));
            mb.C(Dark).Block(new(4f, roof + 0.62f, 4f), new(1.1f, 0.3f, 0.9f));
            mb.C(Steel).Beam(new(4f, roof + 0.5f, 4f), new(4f, roof - 0.7f, 4f), 0.07f);
            mb.C(Yellow).Block(new(4f, roof - 0.78f, 4f), new(0.5f, 0.2f, 0.5f));
        });
        Models.Add(c.R, "Cradle", $"{c.K}.yard.cradle", Models.HullMat(), mb =>
        {
            mb.At(default);
            mb.C(Dark).Block(new(4f, 0.12f, 4f), new(W - 2.4f, 0.12f, D - 2.4f));
            mb.C(Yellow);
            foreach (float z in new[] { 1.4f, D - 1.4f })
                mb.Block(new(4f, 0.2f, z), new(W - 2.6f, 0.04f, 0.18f));
        });
        Models.Add(c.R, "GlowYard", $"{c.K}.yard.glow", Models.Glow(YardOrange, 2.2f), mb =>
        {
            mb.At(default);
            foreach (float x in new[] { 1.2f, W - 1.2f })
                mb.Sphere(new(x, roof + 0.85f, 4f), new(0.16f, 0.16f, 0.16f), 2, 6);
            foreach (float z in new[] { 0.9f, D - 0.9f })
                mb.Block(new(4f, 0.26f, z), new(W - 3.0f, 0.04f, 0.1f));
        });
    }
}
