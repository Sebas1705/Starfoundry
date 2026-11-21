using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Draws the player's station: for every module a translucent floor plate with a visible cell grid, the twelve edges of its
/// box, and a floating marker over the core. Three MultiMeshes carry every module, so the cost does not grow with the module
/// count; the whole view is rebuilt when the network's structure version changes.
/// </summary>
public partial class StationView : Node3D
{
    private const float EdgeThickness = 0.07f;
    private const int FloatsPerInstance = 16;

    private static readonly Color CoreEdge = new(1f, 0.78f, 0.3f, 1f);
    private static readonly Color HullEdge = new(0.35f, 0.8f, 1f, 1f);

    private MultiMeshInstance3D? _frames, _floors, _layers;
    private MeshInstance3D? _coreMarker;
    private readonly List<(int Mx, int My, int Mz)> _modules = new();
    private int _version = -1;
    private int _layerY = int.MinValue;
    private ShaderMaterial _gridMaterial = null!, _layerMaterial = null!;
    private StandardMaterial3D _edgeMaterial = null!;
    private double _time;

    public override void _Ready()
    {
        const string code = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform vec4 tint : source_color = vec4(0.35, 0.8, 1.0, 1.0);
uniform float fill = 0.10;
uniform float line_alpha = 0.55;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 g = abs(fract(wpos.xz - 0.5) - 0.5) / fwidth(wpos.xz);
    float line = 1.0 - min(min(g.x, g.y), 1.0);
    float fade = 1.0 - smoothstep(30.0, 90.0, distance(wpos, CAMERA_POSITION_WORLD));
    ALBEDO = tint.rgb;
    ALPHA = clamp(fill + line * line_alpha, 0.0, 1.0) * mix(0.35, 1.0, fade);
}";
        var shader = new Shader { Code = code };
        _gridMaterial = new ShaderMaterial { Shader = shader };
        _layerMaterial = new ShaderMaterial { Shader = shader };
        _layerMaterial.SetShaderParameter("fill", 0.03f);
        _layerMaterial.SetShaderParameter("line_alpha", 0.22f);
        _layerMaterial.SetShaderParameter("tint", new Color(0.7f, 0.9f, 1f));
        _edgeMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _coreMarker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f, RadialSegments = 4, Rings = 2 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = CoreEdge,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        AddChild(_coreMarker);
    }

    /// <summary>The modules currently drawn, with the core flag (for the HUD marker).</summary>
    public IReadOnlyList<(int Mx, int My, int Mz)> Modules => _modules;

    /// <summary>Centre of a module in world cells.</summary>
    public static Vector3 Centre(int mx, int my, int mz) =>
        new(mx * StationGrid.ModuleX + StationGrid.ModuleX / 2f, my * StationGrid.ModuleY + StationGrid.ModuleY / 2f, mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2f);

    /// <summary>Forces a rebuild on the next <see cref="Sync"/> (the world was replaced, so version numbers are not comparable).</summary>
    public void Invalidate() => _version = -1;

    /// <summary>Rebuilds when the structure changed and moves the build-layer plates to the layer the player works on.</summary>
    public void Sync(BeltNetwork net, int player, int layerY)
    {
        if (net.StructureVersion != _version)
        {
            _version = net.StructureVersion;
            Rebuild(net, player);
            _layerY = int.MinValue;
        }
        if (layerY != _layerY) { _layerY = layerY; PlaceLayers(); }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_coreMarker is { Visible: true } m)
        {
            m.Rotation = new Vector3(0f, (float)_time * 0.8f, 0f);
            m.Position = m.Position with { Y = _coreBaseY + 0.25f * MathF.Sin((float)_time * 1.6f) };
        }
    }

    private float _coreBaseY;

    private static void Write(float[] b, int o, Vector3 scale, Vector3 pos, Color c)
    {
        b[o + 0] = scale.X; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = pos.X;
        b[o + 4] = 0; b[o + 5] = scale.Y; b[o + 6] = 0; b[o + 7] = pos.Y;
        b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = scale.Z; b[o + 11] = pos.Z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    private static MultiMeshInstance3D Make(Mesh mesh, int count, float[] buffer, Material material)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = count,
        };
        mm.Buffer = buffer;
        return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    private void Rebuild(BeltNetwork net, int player)
    {
        _frames?.QueueFree();
        _floors?.QueueFree();
        _layers?.QueueFree();
        _frames = _floors = _layers = null;
        _modules.Clear();
        var list = net.Modules(player).ToList();
        foreach (var m in list) _modules.Add((m.Mx, m.My, m.Mz));
        _coreMarker!.Visible = false;
        if (list.Count == 0) return;

        var edgeBuf = new float[list.Count * 12 * FloatsPerInstance];
        var floorBuf = new float[list.Count * FloatsPerInstance];
        float sx = StationGrid.ModuleX, sy = StationGrid.ModuleY, sz = StationGrid.ModuleZ, t = EdgeThickness;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            var centre = Centre(m.Mx, m.My, m.Mz);
            var color = m.Core ? CoreEdge : HullEdge;
            int e = i * 12 * FloatsPerInstance;
            // Four edges along each axis, at the corners of the box.
            foreach (float oy in new[] { -1f, 1f })
                foreach (float oz in new[] { -1f, 1f })
                    { Write(edgeBuf, e, new Vector3(sx + t, t, t), centre + new Vector3(0, oy * sy / 2f, oz * sz / 2f), color); e += FloatsPerInstance; }
            foreach (float ox in new[] { -1f, 1f })
                foreach (float oz in new[] { -1f, 1f })
                    { Write(edgeBuf, e, new Vector3(t, sy + t, t), centre + new Vector3(ox * sx / 2f, 0, oz * sz / 2f), color); e += FloatsPerInstance; }
            foreach (float ox in new[] { -1f, 1f })
                foreach (float oy in new[] { -1f, 1f })
                    { Write(edgeBuf, e, new Vector3(t, t, sz + t), centre + new Vector3(ox * sx / 2f, oy * sy / 2f, 0), color); e += FloatsPerInstance; }

            // Floor plate on the underside of the module, a hair above the cell floor so it does not z-fight.
            Write(floorBuf, i * FloatsPerInstance, new Vector3(sx, 1f, sz), new Vector3(centre.X, m.My * StationGrid.ModuleY + 0.02f, centre.Z), Colors.White);
            if (m.Core)
            {
                _coreMarker.Visible = true;
                _coreBaseY = centre.Y;
                _coreMarker.Position = centre;
            }
        }
        _frames = Make(new BoxMesh { Size = Vector3.One }, list.Count * 12, edgeBuf, _edgeMaterial);
        _floors = Make(new PlaneMesh { Size = Vector2.One }, list.Count, floorBuf, _gridMaterial);
        _layers = Make(new PlaneMesh { Size = Vector2.One }, list.Count, new float[list.Count * FloatsPerInstance], _layerMaterial);
        AddChild(_frames);
        AddChild(_floors);
        AddChild(_layers);
    }

    /// <summary>A faint grid plate at the build layer inside each module (hidden where it would coincide with the floor plate).</summary>
    private void PlaceLayers()
    {
        if (_layers is null) return;
        var mm = _layers.Multimesh;
        var buf = new float[_modules.Count * FloatsPerInstance];
        for (int i = 0; i < _modules.Count; i++)
        {
            var (mx, my, mz) = _modules[i];
            int bottom = my * StationGrid.ModuleY;
            bool inside = _layerY > bottom && _layerY < bottom + StationGrid.ModuleY;
            var c = Centre(mx, my, mz);
            Write(buf, i * FloatsPerInstance, inside ? new Vector3(StationGrid.ModuleX, 1f, StationGrid.ModuleZ) : Vector3.Zero,
                new Vector3(c.X, _layerY + 0.03f, c.Z), Colors.White);
        }
        mm.Buffer = buf;
    }
}
