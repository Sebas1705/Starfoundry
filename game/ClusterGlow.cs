using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Far impostors of the clusters of the rock field (ADR 0046): a soft glow tinted by the dominant ore for every cluster beyond the sectors that are loaded, so a
/// concentration over there is something to fly to. The cluster parents are a pure function of the seed (<see cref="BeltNetwork.ClustersNear"/>), so no simulation
/// state is read. The camera's far plane is 500 cells, so each glow is drawn on a shell 470 cells away in the direction of its cluster, with the size the cluster
/// would have at its true distance; it fades out as the real rocks load (the sector load radius) and with distance.
/// </summary>
public sealed partial class ClusterGlow : Node3D
{
    private const float Shell = 470f, MaxDistance = 2800f, RefreshSeconds = 1.0f;
    private const int Capacity = 192;

    private sealed class Layer { public MultiMeshInstance3D Node = null!; public MultiMesh Mm = null!; public List<(Vector3 C, float R, float Strength)> Live = new(); }

    private readonly Dictionary<string, Layer> _layers = new();
    private IReadOnlyList<ClusterParent> _parents = Array.Empty<ClusterParent>();
    private double _clock = RefreshSeconds;
    private ShaderMaterial? _template;

    /// <summary>Glows drawn this frame (for the measurement).</summary>
    public int Drawn { get; private set; }

    public ClusterGlow() { Name = "ClusterGlow"; }

    private static Shader MakeShader() => new()
    {
        Code = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, skip_vertex_transform, fog_disabled;
uniform vec4 tint : source_color = vec4(1.0);
void vertex() {
    vec3 centre = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    float size = length(MODEL_MATRIX[0].xyz);
    vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
    vec3 up = normalize(INV_VIEW_MATRIX[1].xyz);
    vec3 world = centre + (right * VERTEX.x + up * VERTEX.y) * size;
    VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
}
void fragment() {
    float r = length(UV * 2.0 - 1.0);
    float a = pow(clamp(1.0 - r, 0.0, 1.0), 2.2);
    ALBEDO = tint.rgb;
    ALPHA = a * tint.a;
}
",
    };

    private Layer LayerOf(string item)
    {
        if (_layers.TryGetValue(item, out var l)) return l;
        _template ??= new ShaderMaterial { Shader = MakeShader() };
        var mat = (ShaderMaterial)_template.Duplicate();
        var glow = DepositView.GlowOf(item);
        mat.SetShaderParameter("tint", new Color(glow.R, glow.G, glow.B, 0.55f));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new QuadMesh { Size = Vector2.One }, InstanceCount = Capacity, VisibleInstanceCount = 0 };
        var node = new MultiMeshInstance3D { Name = "Glow" + item, Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 16384f };
        AddChild(node);
        return _layers[item] = new Layer { Node = node, Mm = mm };
    }

    /// <summary>Call every frame with the camera position in cells; refreshes the cluster list about once a second.</summary>
    public void Update(BeltNetwork net, ContentDb content, Vector3 camera, double delta, int loadCells)
    {
        _clock += delta;
        if (_clock >= RefreshSeconds)
        {
            _clock = 0;
            var units = View.ToUnitsTrunc(camera); // the camera is in view space: back to simulation units
            _parents = net.ClustersNear(units.X, units.Y, units.Z, (int)MaxDistance);
        }
        foreach (var l in _layers.Values) l.Live.Clear();
        foreach (var p in _parents)
        {
            var c = View.FromCells(p.X, p.Y, p.Z); // view space; the glow quads are rewritten from the camera every frame, so nothing here is shifted
            float dist = (c - camera).Length();
            float r = p.Radius * (p.Shape == 1 ? 0.8f : 1f);
            float inner = r * 0.6f + loadCells * 0.7f; // inside this the real rocks are loaded around you
            float fadeIn = Mathf.Clamp((dist - inner) / 260f, 0f, 1f);
            float fadeFar = Mathf.Clamp(1f - dist / MaxDistance, 0f, 1f);
            float strength = fadeIn * fadeFar * (0.45f + 0.11f * p.Richness);
            if (strength <= 0.01f) continue;
            LayerOf(content.Items[p.Item].Id).Live.Add((c, r, strength));
        }
        Drawn = 0;
        foreach (var l in _layers.Values)
        {
            int n = Math.Min(Capacity, l.Live.Count);
            for (int i = 0; i < n; i++)
            {
                var (c, r, strength) = l.Live[i];
                var dir = (c - camera).Normalized();
                float dist = (c - camera).Length();
                float diameter = Math.Min(Shell * 1.2f, 2f * r * Shell / MathF.Max(dist, 1f)) * (0.55f + 0.45f * strength);
                var t = new Transform3D(Basis.Identity.Scaled(Vector3.One * diameter), camera + dir * Shell);
                l.Mm.SetInstanceTransform(i, t);
            }
            l.Mm.VisibleInstanceCount = n;
            Drawn += n;
        }
    }
}
