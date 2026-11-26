using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>The 3D plan of the blueprint editor: the real structure, the ghosts of the draft, the grid, the selection and the camera.</summary>
public partial class BlueprintEditor
{
    private Node3D _planRoot = null!, _real = null!, _ghosts = null!, _marks = null!;
    private Camera3D _camera3D = null!;
    private (int Mx, int My, int Mz)? _hoverCell;

    /// <summary>Colours of the ghosts: added green, removed red, moved yellow, invalid red hatch (the real structure shows underneath).</summary>
    public static readonly Color AddedColour = new(0.30f, 0.95f, 0.45f), RemovedColour = new(1f, 0.30f, 0.28f), MovedColour = new(1f, 0.85f, 0.25f), InvalidColour = new(1f, 0.15f, 0.15f);

    private static StandardMaterial3D? _addedMat, _removedMat, _movedMat, _movedFromMat, _hoverMat;
    private static ShaderMaterial? _hatchMat;

    private static StandardMaterial3D Tint(Color c, float alpha) => new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(c.R, c.G, c.B, alpha),
    };

    private const string HatchShader = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform vec4 colour : source_color = vec4(1.0, 0.15, 0.15, 0.9);
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    float s = fract((wpos.x + wpos.y + wpos.z) * 0.45);
    ALBEDO = colour.rgb;
    ALPHA = s < 0.4 ? colour.a : 0.10;
}";

    private static void EnsureMaterials()
    {
        _addedMat ??= Tint(AddedColour, 0.38f);
        _removedMat ??= Tint(RemovedColour, 0.45f);
        _movedMat ??= Tint(MovedColour, 0.40f);
        _movedFromMat ??= Tint(MovedColour, 0.16f);
        _hoverMat ??= Tint(Colors.White, 0.22f);
        _hatchMat ??= new ShaderMaterial { Shader = new Shader { Code = HatchShader } };
    }

    private static Vector3 V(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private void BuildScene()
    {
        EnsureMaterials();
        _planRoot = new Node3D { Name = "PlanRoot" };
        _planRoot.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.015f, 0.025f, 0.05f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.6f, 0.65f, 0.8f),
                AmbientLightEnergy = 0.95f,
            },
        });
        _planRoot.AddChild(new DirectionalLight3D { LightEnergy = 1.5f, RotationDegrees = new Vector3(-50, 35, 0) });
        _real = new Node3D { Name = "Real" };
        _ghosts = new Node3D { Name = "Ghosts" };
        _marks = new Node3D { Name = "Marks" };
        _planRoot.AddChild(_real); _planRoot.AddChild(_ghosts); _planRoot.AddChild(_marks);
        _camera3D = new Camera3D { Name = "PlanCamera", Fov = _s.Camera.FovDegrees, Far = 2000f };
    }

    private static void Overlay(Node n, Material m)
    {
        if (n is MeshInstance3D mi) mi.MaterialOverlay = m;
        foreach (var c in n.GetChildren()) Overlay(c, m);
    }

    private int ShipVariant => _s.StructureId >= 0 ? _s.StructureId : Math.Max(0, _world().Network.ShipStructure(_player));

    /// <summary>One module drawn as it would stand: a holder at the cell's centre (so a quarter turn pivots about it) around the real module model.</summary>
    private Node3D Part(ModuleKind kind, (int Mx, int My, int Mz) cell, Func<int, int, int, ModuleKind?> kindAt, int rotation, Material? overlay, float scale = 1f)
    {
        var holder = new Node3D { Name = $"{kind}_{cell.Mx}_{cell.My}_{cell.Mz}", Position = V(BlueprintCamera.CellCentre(cell)), RotationDegrees = new Vector3(0f, rotation * 90f, 0f), Scale = Vector3.One * scale };
        var model = ModuleModels.Build(kind, true, ShipVariant, ModuleModels.NeighbourMask(kindAt, cell.Mx, cell.My, cell.Mz));
        model.Position = -new Vector3(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ) / 2f;
        if (overlay is not null) Overlay(model, overlay);
        holder.AddChild(model);
        holder.Visible = _s.Camera.LayerVisible(cell.My);
        return holder;
    }

    private static void Clear(Node root)
    {
        foreach (var c in root.GetChildren()) { root.RemoveChild(c); c.QueueFree(); }
    }

    /// <summary>Redraws the structure, the ghosts, the grid and the selection from the session (after a change).</summary>
    private void RebuildScene()
    {
        _sceneDirty = false;
        Clear(_real); Clear(_ghosts); Clear(_marks);
        var d = _s.Draft;
        var diff = _s.Diff;
        ModuleKind? OrigAt(int x, int y, int z) => d.Original.TryGetValue((x, y, z), out var k) ? k : null;
        ModuleKind? DraftAt(int x, int y, int z) => d.Modules.TryGetValue((x, y, z), out var k) ? k : null;

        var removed = new HashSet<(int, int, int)>(diff.Removed.Select(r => r.Cell));
        var movedFrom = new HashSet<(int, int, int)>(diff.Moved.Select(m => m.From));
        // The real structure, always: the parts that go carry a red tint, the parts that move a faint yellow one, so you see what will change.
        foreach (var (cell, kind) in d.Original)
        {
            int rot = d.Modules.TryGetValue(cell, out var dk) && dk == kind ? d.RotationOf(cell) : 0;
            Material? tint = removed.Contains(cell) ? _removedMat : movedFrom.Contains(cell) ? _movedFromMat : null;
            var p = Part(kind, cell, OrigAt, rot, tint);
            p.Name = "Real_" + p.Name;
            _real.AddChild(p);
        }
        // The new parts and the moved ones at their target, as tinted ghosts.
        foreach (var (cell, kind) in diff.Added)
        {
            bool over = removed.Contains(cell);
            var p = Part(kind, cell, DraftAt, d.RotationOf(cell), _addedMat, over ? 0.9f : 1f);
            p.Name = "Added_" + p.Name;
            _ghosts.AddChild(p);
        }
        foreach (var m in diff.Moved)
        {
            var p = Part(m.Kind, m.To, DraftAt, d.RotationOf(m.To), _movedMat);
            p.Name = "Moved_" + p.Name;
            _ghosts.AddChild(p);
        }
        // Invalid parts: a red hatch box over the cell.
        foreach (var cell in diff.Invalid)
        {
            if (!d.Modules.ContainsKey(cell)) continue;
            var box = new MeshInstance3D
            {
                Name = $"Invalid_{cell.Mx}_{cell.My}_{cell.Mz}",
                Mesh = new BoxMesh { Size = new Vector3(StationGrid.ModuleX + 0.3f, StationGrid.ModuleY + 0.3f, StationGrid.ModuleZ + 0.3f) },
                MaterialOverride = _hatchMat,
                Position = V(BlueprintCamera.CellCentre(cell)),
                Visible = _s.Camera.LayerVisible(cell.My),
            };
            _ghosts.AddChild(box);
        }

        _marks.AddChild(LineMesh("Trails", diff.Moved.Select(m => (BlueprintCamera.CellCentre(m.From), BlueprintCamera.CellCentre(m.To), MovedColour)).ToList()));
        _marks.AddChild(LineMesh("Grid", GridLines()));
        var sel = new List<(System.Numerics.Vector3, System.Numerics.Vector3, Color)>();
        foreach (var c in _s.Selection) BoxEdges(sel, c, Colors.White, 0.15f);
        if (_s.Carrying is { } carry) BoxEdges(sel, carry, MovedColour, 0.3f);
        _marks.AddChild(LineMesh("Selection", sel));

        // The cell under the pointer: an outline, and with the Place tool the module that would go there.
        if (_hoverCell is { } h)
        {
            var lines = new List<(System.Numerics.Vector3, System.Numerics.Vector3, Color)>();
            bool free = !d.Modules.ContainsKey(h);
            BoxEdges(lines, h, free ? AddedColour : Colors.White, 0.05f);
            _marks.AddChild(LineMesh("Hover", lines));
            if (_s.Tool == BlueprintTool.Place && _s.Kind is { } k && free)
            {
                var p = Part(k, h, DraftAt, 0, _hoverMat);
                p.Name = "Cursor";
                _marks.AddChild(p);
            }
        }
    }

    private static void BoxEdges(List<(System.Numerics.Vector3, System.Numerics.Vector3, Color)> into, (int Mx, int My, int Mz) cell, Color colour, float grow)
    {
        var lo = new System.Numerics.Vector3(cell.Mx * StationGrid.ModuleX, cell.My * StationGrid.ModuleY, cell.Mz * StationGrid.ModuleZ) - System.Numerics.Vector3.One * grow;
        var hi = lo + new System.Numerics.Vector3(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ) + System.Numerics.Vector3.One * (grow * 2f);
        System.Numerics.Vector3 P(int x, int y, int z) => new(x == 0 ? lo.X : hi.X, y == 0 ? lo.Y : hi.Y, z == 0 ? lo.Z : hi.Z);
        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++)
            {
                into.Add((P(0, a, b), P(1, a, b), colour));
                into.Add((P(a, 0, b), P(a, 1, b), colour));
                into.Add((P(a, b, 0), P(a, b, 1), colour));
            }
    }

    /// <summary>The grid of the deck being edited: lines along every module boundary across the draft's bounds, widened by two modules.</summary>
    private List<(System.Numerics.Vector3, System.Numerics.Vector3, Color)> GridLines()
    {
        var lines = new List<(System.Numerics.Vector3, System.Numerics.Vector3, Color)>();
        var (min, max) = _s.Draft.Bounds(2);
        int layer = _s.Camera.View == PlanView.Section ? _s.Camera.SectionLayer : _s.Draft.Bounds(0).Min.Y;
        float y = layer * StationGrid.ModuleY;
        var c = new Color(0.35f, 0.5f, 0.8f, 0.5f);
        float x0 = min.X * StationGrid.ModuleX, x1 = (max.X + 1) * StationGrid.ModuleX, z0 = min.Z * StationGrid.ModuleZ, z1 = (max.Z + 1) * StationGrid.ModuleZ;
        for (int x = min.X; x <= max.X + 1; x++) lines.Add((new System.Numerics.Vector3(x * StationGrid.ModuleX, y, z0), new System.Numerics.Vector3(x * StationGrid.ModuleX, y, z1), c));
        for (int z = min.Z; z <= max.Z + 1; z++) lines.Add((new System.Numerics.Vector3(x0, y, z * StationGrid.ModuleZ), new System.Numerics.Vector3(x1, y, z * StationGrid.ModuleZ), c));
        return lines;
    }

    private static MeshInstance3D LineMesh(string name, List<(System.Numerics.Vector3 A, System.Numerics.Vector3 B, Color C)> lines)
    {
        var mi = new MeshInstance3D { Name = name };
        if (lines.Count == 0) return mi;
        var im = new ImmediateMesh();
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (var (a, b, c) in lines)
        {
            im.SurfaceSetColor(c); im.SurfaceAddVertex(V(a));
            im.SurfaceSetColor(c); im.SurfaceAddVertex(V(b));
        }
        im.SurfaceEnd();
        mi.Mesh = im;
        mi.MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        return mi;
    }

    private void ApplyCamera()
    {
        var cam = _s.Camera;
        _camera3D.Fov = cam.FovDegrees;
        _camera3D.Position = V(cam.Position);
        var up = V(cam.Up);
        var look = V(cam.Target);
        if ((look - _camera3D.Position).LengthSquared() < 1e-6f) return;
        _camera3D.LookAt(look, up);
    }
}
