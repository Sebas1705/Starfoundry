using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Where the displays sit on the console of the cockpit: the numbers come from the content (<c>content/cockpit_layouts.json</c>, <see cref="CockpitLayout"/>), one
/// layout per cockpit style, so a style with a wide console and one big navigation screen is a data entry, not code. This class only turns the engine-free
/// matrices into Godot transforms. The console model (<c>ModuleModels.Cockpit</c>) and the layout share the deck origin, tilt and squeeze: when the model's
/// geometry moves, change the layout file and nothing else.
/// </summary>
public static class CockpitScreenLayout
{
    /// <summary>The layout in use: the starter console (the cockpit styles of the designer will pick their own).</summary>
    public static CockpitLayout Current => ClientContent.Layouts.Get(CockpitLayouts.StarterId);

    public static ScreenRect Of(CockpitScreen screen) => Current.Of(screen);

    private static Transform3D ToGodot(System.Numerics.Matrix4x4 m) => new(
        new Basis(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33)),
        new Vector3(m.M41, m.M42, m.M43));

    public static Transform3D ScreenToModule(CockpitLayout l, CockpitScreen screen) => ToGodot(l.ScreenToModule(l.Of(screen)));

    public static Transform3D BezelToModule(CockpitLayout l, CockpitScreen screen) => ToGodot(l.BezelToModule(l.Of(screen)));
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

    /// <summary>The console layout these displays were built for (content, see <see cref="CockpitScreenLayout"/>).</summary>
    public readonly CockpitLayout Layout = CockpitScreenLayout.Current;
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
            var def = Layout.Of((CockpitScreen)i);
            var vp = new SubViewport
            {
                Size = new Vector2I(def.PxW, def.PxH),
                Disable3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                GuiDisableInput = true,
                Name = "CockpitScreen" + (CockpitScreen)i,
            };
            var view = new CockpitScreenView((CockpitScreen)i, Pages, vp.Size);
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
        bool recessed = BlenderCockpit.IsOn(module); // the Blender console has recessed wells with their own bezels
        var bezelMat = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.055f, 0.07f), Metallic = 0.6f, Roughness = 0.4f };
        for (int i = 0; i < 3; i++)
        {
            var def = Layout.Of((CockpitScreen)i);
            var screenId = (CockpitScreen)i;
            var quad = new MeshInstance3D
            {
                Name = "CockpitDisplay" + screenId,
                Mesh = new QuadMesh { Size = new Vector2(def.Width, def.Depth) },
                MaterialOverride = _materials[i],
                Transform = CockpitScreenLayout.ScreenToModule(Layout, screenId),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            module.AddChild(quad);
            _quads[i] = quad;
            if (recessed) continue;
            var bezel = new MeshInstance3D
            {
                Name = "CockpitBezel" + screenId,
                Mesh = new BoxMesh { Size = new Vector3(def.Width + Layout.BezelMargin * 2f, 0.03f, def.Depth + Layout.BezelMargin * 2f) },
                MaterialOverride = bezelMat,
                Transform = CockpitScreenLayout.BezelToModule(Layout, screenId),
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
        var def = Layout.Of(screen);
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
            var def = Layout.Of((CockpitScreen)i);
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
