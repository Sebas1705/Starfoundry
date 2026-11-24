using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// A small turning 3D preview of the thing a build entry places (a machine or a module), for the detail pane of the build menu. It has its own
/// world, shows one model at a time (each is built the first time it is asked for and kept), frames it from its bounds and turns it slowly.
/// Belts and anything without a model get no preview: the pane keeps its big icon then.
/// </summary>
public partial class BuildPreview : SubViewportContainer
{
    private readonly SubViewport _view;
    private readonly Node3D _pivot = new() { Name = "Pivot" };
    private readonly Camera3D _camera = new() { Fov = 38f };
    private readonly Dictionary<string, (Node3D Node, float Radius, Vector3 Centre)> _models = new();
    private string? _shown;

    public BuildPreview(int size = 92)
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _view = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa2X, Size = new Vector2I(size, size), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0f, 0f, 0f, 0f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
                AmbientLightEnergy = 0.9f,
            },
        });
        _view.AddChild(new DirectionalLight3D { LightEnergy = 1.5f, RotationDegrees = new Vector3(-40, 35, 0) });
        _view.AddChild(_pivot);
        _view.AddChild(_camera);
    }

    /// <summary>True when the entry has a model to show.</summary>
    public static bool Has(BuildEntry e) => e.Kind is BuildEntryKind.Machine or BuildEntryKind.Module or BuildEntryKind.Station;

    /// <summary>Shows the model of an entry (building it on first use); false when it has none.</summary>
    public bool Show(BuildEntry entry)
    {
        if (!Has(entry)) { Clear(); return false; }
        string key = entry.Id;
        if (_shown == key) return true;
        if (!_models.TryGetValue(key, out var m))
        {
            Node3D node = entry.Kind switch
            {
                BuildEntryKind.Machine => Models.Machine(entry.Id),
                BuildEntryKind.Station => ModuleModels.Build(ModuleKind.Core, false),
                _ => ModuleModels.Build(entry.Module!.Value, !ModuleKindIds.AllowedOn(StructureKind.Station, entry.Module.Value)),
            };
            var (lo, hi) = Bounds(node, Transform3D.Identity) ?? (Vector3.Zero, Vector3.One);
            var centre = (lo + hi) / 2f;
            node.Position = -centre;
            node.Visible = false;
            _pivot.AddChild(node);
            m = (node, Math.Max((hi - lo).Length() / 2f, 0.3f), centre);
            _models[key] = m;
        }
        if (_shown is not null && _models.TryGetValue(_shown, out var old)) old.Node.Visible = false;
        m.Node.Visible = true;
        _shown = key;
        float dist = m.Radius / MathF.Sin(Mathf.DegToRad(_camera.Fov / 2f)) * 0.85f;
        _camera.Position = new Vector3(0f, dist * 0.42f, dist * 0.91f);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        return true;
    }

    public void Clear() { _shown = null; foreach (var m in _models.Values) m.Node.Visible = false; }

    public override void _Process(double delta)
    {
        if (_shown is not null && IsVisibleInTree()) _pivot.RotateY((float)delta * 0.7f);
    }

    /// <summary>The bounds of every mesh under a node, in the node's parent frame.</summary>
    private static (Vector3 Lo, Vector3 Hi)? Bounds(Node3D node, Transform3D parent)
    {
        var t = parent * node.Transform;
        Vector3? lo = null, hi = null;
        if (node is MeshInstance3D { Mesh: not null } mi)
        {
            var box = t * mi.Mesh.GetAabb();
            lo = box.Position;
            hi = box.End;
        }
        foreach (var child in node.GetChildren())
        {
            if (child is not Node3D n || n is Light3D) continue;
            if (Bounds(n, t) is not { } b) continue;
            lo = lo is null ? b.Lo : new Vector3(Math.Min(lo.Value.X, b.Lo.X), Math.Min(lo.Value.Y, b.Lo.Y), Math.Min(lo.Value.Z, b.Lo.Z));
            hi = hi is null ? b.Hi : new Vector3(Math.Max(hi.Value.X, b.Hi.X), Math.Max(hi.Value.Y, b.Hi.Y), Math.Max(hi.Value.Z, b.Hi.Z));
        }
        return lo is { } l && hi is { } h ? (l, h) : null;
    }
}
