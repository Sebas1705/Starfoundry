using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Where the three cockpit displays sit on the console, in the local frame of the cockpit module. This is the one attachment point between
/// the console model (<c>ModuleModels.Cockpit</c>) and the displays: when the console geometry moves, change the constants here and nothing else.
/// <list type="bullet">
/// <item>The console deck is a plate tilted <see cref="DeckTiltDegrees"/> about X, with its origin at <see cref="DeckOrigin"/>; a ship squeezes the whole console by <see cref="Squeeze"/>
/// about the middle of the module (the same numbers as the model).</item>
/// <item>A screen is a rectangle on the deck: <c>X</c> and <c>Z</c> are the position of its centre on the deck, <c>Width</c> and <c>Depth</c> its size (deck cells, before the squeeze),
/// <c>PxW</c> and <c>PxH</c> the size of the texture drawn into it.</item>
/// </list>
/// </summary>
public static class CockpitScreenLayout
{
    // The numbers live in Starfoundry.Ui (CockpitShell) so the Blender generator reads the same ones (tools/blender/shell.json).
    public static readonly Vector3 DeckOrigin = new(CockpitShell.DeckOriginX, CockpitShell.DeckOriginY, CockpitShell.DeckOriginZ);
    public const float DeckTiltDegrees = CockpitShell.DeckTiltDegrees;
    public static readonly Vector3 Squeeze = new(CockpitShell.SqueezeX, CockpitShell.SqueezeY, CockpitShell.SqueezeZ);
    /// <summary>The module-local X that the squeeze keeps fixed.</summary>
    public const float SqueezeAboutX = CockpitShell.SqueezeAboutX;
    /// <summary>The height of the screen surface above the deck plate, in deck cells (the bezel is just under it).</summary>
    public const float SurfaceHeight = CockpitShell.SurfaceHeight;
    public const float BezelHeight = CockpitShell.BezelHeight;
    public const float BezelMargin = CockpitShell.BezelMargin;

    public readonly record struct Def(CockpitScreen Screen, float X, float Z, float Width, float Depth, int PxW, int PxH);

    public static readonly Def[] Screens = Array.ConvertAll(CockpitShell.Screens, s => new Def(s.Screen, s.X, s.Z, s.Width, s.Depth, s.PxW, s.PxH));

    public static Def Of(CockpitScreen screen) => Screens[(int)screen];

    /// <summary>The deck frame in the cockpit module's frame: the tilt, then the squeeze about the middle of the module.</summary>
    public static Transform3D DeckToModule
    {
        get
        {
            var rot = new Basis(Vector3.Right, Mathf.DegToRad(DeckTiltDegrees));
            var basis = Basis.FromScale(Squeeze) * rot;
            var origin = Squeeze * DeckOrigin + new Vector3(SqueezeAboutX * (1f - Squeeze.X), 0f, 0f);
            return new Transform3D(basis, origin);
        }
    }

    /// <summary>The frame of a screen quad: X to the pilot's right, Y up the screen (away from the pilot), Z out of the screen.</summary>
    public static Transform3D ScreenToModule(Def d) => DeckToModule * new Transform3D(
        new Basis(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)),
        new Vector3(d.X, SurfaceHeight, d.Z));

    public static Transform3D BezelToModule(Def d) => DeckToModule * new Transform3D(Basis.Identity, new Vector3(d.X, BezelHeight, d.Z));
}

/// <summary>
/// The three emissive displays of the pilot's console. Each is a <see cref="CockpitScreenView"/> drawn into a small SubViewport whose texture
/// lights a quad parented to the cockpit module (<see cref="Attach"/>), so they move with the ship and the module's lights, visibility and
/// lifetime apply to them. The viewports render on demand (<see cref="Refresh"/>), not every frame.
/// </summary>
public partial class CockpitDisplays : Node
{
    /// <summary>Over 1 so that the brightest parts of a display glow.</summary>
    private const float ScreenBrightness = 1.4f;

    public readonly CockpitPages Pages = new();
    private readonly SubViewport[] _viewports = new SubViewport[3];
    private readonly CockpitScreenView[] _views = new CockpitScreenView[3];
    private readonly MeshInstance3D?[] _quads = new MeshInstance3D?[3];
    private readonly StandardMaterial3D[] _materials = new StandardMaterial3D[3];
    private Node3D? _module;
    private readonly Node3D?[] _bezels = new Node3D?[3];

    public CockpitDisplays()
    {
        for (int i = 0; i < 3; i++)
        {
            var def = CockpitScreenLayout.Screens[i];
            var vp = new SubViewport
            {
                Size = new Vector2I(def.PxW, def.PxH),
                Disable3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                GuiDisableInput = true,
                Name = "CockpitScreen" + def.Screen,
            };
            var view = new CockpitScreenView(def.Screen, Pages, vp.Size);
            vp.AddChild(view);
            AddChild(vp);
            _viewports[i] = vp;
            _views[i] = view;
            // Unshaded and a little over-bright: the screen is its own light (the sun and the sky must not wash it out, which an emissive material
            // did), and its bright lines and text pass the environment's glow threshold, so they bloom.
            _materials[i] = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(ScreenBrightness, ScreenBrightness, ScreenBrightness),
                AlbedoTexture = vp.GetTexture(),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear, // a render target has no mip levels
            };
        }
    }

    public CockpitScreenView View(CockpitScreen screen) => _views[(int)screen];

    /// <summary>The picture a display shows now (for the screenshot runs).</summary>
    public Image Capture(CockpitScreen screen) => _viewports[(int)screen].GetTexture().GetImage();

    /// <summary>The cockpit module the displays are on, or null.</summary>
    public Node3D? Module => _module is not null && GodotObject.IsInstanceValid(_module) ? _module : null;

    public bool IsAttached => Module is not null && _quads[0] is not null && GodotObject.IsInstanceValid(_quads[0]);

    /// <summary>Puts the quads on a cockpit module (a rebuilt module is a new node, so call this every frame with the current one; it does nothing when nothing changed).</summary>
    public void Attach(Node3D? module)
    {
        if (module is not null && module == Module && IsAttached) return;
        for (int i = 0; i < 3; i++)
        {
            if (_quads[i] is { } q && GodotObject.IsInstanceValid(q)) q.QueueFree();
            if (_bezels[i] is { } b && GodotObject.IsInstanceValid(b)) b.QueueFree();
            _quads[i] = null;
            _bezels[i] = null;
        }
        _module = module;
        if (module is null) return;
        var bezelMat = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.055f, 0.07f), Metallic = 0.6f, Roughness = 0.4f };
        for (int i = 0; i < 3; i++)
        {
            var def = CockpitScreenLayout.Screens[i];
            var quad = new MeshInstance3D
            {
                Name = "CockpitDisplay" + def.Screen,
                Mesh = new QuadMesh { Size = new Vector2(def.Width, def.Depth) },
                MaterialOverride = _materials[i],
                Transform = CockpitScreenLayout.ScreenToModule(def),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            module.AddChild(quad);
            _quads[i] = quad;
            var bezel = new MeshInstance3D
            {
                Name = "CockpitBezel" + def.Screen,
                Mesh = new BoxMesh { Size = new Vector3(def.Width + CockpitScreenLayout.BezelMargin * 2f, 0.03f, def.Depth + CockpitScreenLayout.BezelMargin * 2f) },
                MaterialOverride = bezelMat,
                Transform = CockpitScreenLayout.BezelToModule(def),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            module.AddChild(bezel);
            _bezels[i] = bezel;
        }
        Refresh();
    }

    /// <summary>Redraws every display once (the viewports render in the next frame).</summary>
    /// <summary>Dims the three screens (1 = full brightness); a ship low on power has duller displays. The values above 1 that make the text bloom are kept in proportion.</summary>
    public void SetDim(float factor)
    {
        float b = ScreenBrightness * Math.Clamp(factor, 0.1f, 1f);
        foreach (var m in _materials) if (m is not null) m.AlbedoColor = new Color(b, b, b);
    }

    public void Refresh()
    {
        for (int i = 0; i < 3; i++)
        {
            _views[i].QueueRedraw();
            _viewports[i].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
    }

    /// <summary>Redraws one display (the focused one, every frame).</summary>
    public void Refresh(CockpitScreen screen)
    {
        _views[(int)screen].QueueRedraw();
        _viewports[(int)screen].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    /// <summary>The quad of a screen, or null when the displays are not attached.</summary>
    public MeshInstance3D? Quad(CockpitScreen screen) => _quads[(int)screen] is { } q && GodotObject.IsInstanceValid(q) && q.IsInsideTree() ? q : null;

    /// <summary>The middle of a screen in world space, and the direction that points out of it.</summary>
    public bool TryGetCentre(CockpitScreen screen, out Vector3 centre, out Vector3 normal)
    {
        centre = normal = default;
        if (Quad(screen) is not { } q) return false;
        centre = q.GlobalTransform.Origin;
        normal = q.GlobalTransform.Basis.Z.Normalized();
        return true;
    }

    /// <summary>The size of a screen in world units (cells), measured from its quad as placed.</summary>
    public Vector2 WorldSize(CockpitScreen screen)
    {
        if (Quad(screen) is not { } q) return Vector2.One;
        var b = q.GlobalTransform.Basis;
        var def = CockpitScreenLayout.Of(screen);
        return new Vector2(b.X.Length() * def.Width, b.Y.Length() * def.Depth);
    }

    /// <summary>
    /// The screen a ray (world space, <paramref name="dir"/> a unit vector) meets first: which one and where on its texture, in pixels. The ray is taken into
    /// the quad's own frame through the inverse of its global transform, so the ship's attitude and the console's tilt and squeeze need no special care.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 dir, out CockpitScreen screen, out Vector2 pixel, out float distance, float maxDistance = 30f)
    {
        screen = CockpitScreen.Center;
        pixel = default;
        distance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < 3; i++)
        {
            if (Quad((CockpitScreen)i) is not { } q) continue;
            var inv = q.GlobalTransform.AffineInverse();
            var o = inv * origin;
            var d = inv.Basis * dir;
            if (MathF.Abs(d.Z) < 1e-6f) continue;
            float t = -o.Z / d.Z;
            if (t < 0f || t > maxDistance || t >= distance) continue;
            var p = o + d * t;
            var def = CockpitScreenLayout.Screens[i];
            if (MathF.Abs(p.X) > def.Width / 2f || MathF.Abs(p.Y) > def.Depth / 2f) continue;
            distance = t;
            screen = (CockpitScreen)i;
            pixel = new Vector2((p.X / def.Width + 0.5f) * def.PxW, (0.5f - p.Y / def.Depth) * def.PxH);
            found = true;
        }
        return found;
    }

    /// <summary>The button of a screen under a pixel, or null.</summary>
    public CockpitButton? ButtonAt(CockpitScreen screen, Vector2 pixel)
    {
        var buttons = _views[(int)screen].Buttons;
        for (int i = buttons.Count - 1; i >= 0; i--)
            if (buttons[i].Contains(pixel.X, pixel.Y)) return buttons[i];
        return null;
    }
}
