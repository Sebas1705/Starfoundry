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
    public void Invalidate() { _version = -1; _structVersion = -1; }

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

    // ---- structures: one root per ship or station, holding one model per module ------------------------------

    private sealed class StructRoot
    {
        public Node3D Root = null!;
        public readonly Dictionary<(int Mx, int My, int Mz), (ModuleKind Kind, Node3D Node)> Modules = new();
    }

    private readonly Dictionary<int, StructRoot> _structs = new();
    private readonly Dictionary<int, bool> _thrust = new();
    private int _structVersion = -1;

    /// <summary>The root node of a structure (move it to move the ship), or null if it is not drawn.</summary>
    public Node3D? StructureRoot(int id) => _structs.TryGetValue(id, out var s) ? s.Root : null;

    /// <summary>Shows or hides the pilot seat model of a structure's cockpit (hidden while seated, so it does not block the view).</summary>
    public void SetSeatVisible(int structureId, bool visible)
    {
        if (!_structs.TryGetValue(structureId, out var sr)) return;
        foreach (var (kind, node) in sr.Modules.Values)
            if (kind == ModuleKind.Cockpit && node.GetNodeOrNull<Node3D>("Seat") is { } seat) seat.Visible = visible;
    }

    private readonly Dictionary<int, (int Satisfaction, bool Burning)> _power = new();

    /// <summary>Power state of a structure for its lights: satisfaction per mille and whether a reactor burns. Structures never set count as fully powered.</summary>
    public void SetPower(int structureId, int satisfactionPermille, bool burning) => _power[structureId] = (satisfactionPermille, burning);

    /// <summary>Lights the engine flames of a structure.</summary>
    public void SetEngines(int structureId, bool active) => _thrust[structureId] = active;

    /// <summary>Position of a structure's origin corner in game cells.</summary>
    public static Vector3 OriginCells(BeltNetwork net, int id)
    {
        var (x, y, z) = net.WorldCentre(id, 0, 0, 0);
        float half = Units.CellUnits / 2f;
        return new Vector3((x - half) / Units.CellUnits, (y - half) / Units.CellUnits, (z - half) / Units.CellUnits);
    }

    /// <summary>Keeps one root Node3D per structure at its world origin and one model per module, by kind. Cheap per frame: modules are rebuilt only when the structure version changes.</summary>
    public void SyncStructures(BeltNetwork net)
    {
        bool rebuild = net.StructureVersion != _structVersion;
        _structVersion = net.StructureVersion;
        var alive = new HashSet<int>();
        foreach (var st in net.Structures())
        {
            alive.Add(st.Id);
            if (!_structs.TryGetValue(st.Id, out var sr))
            {
                sr = new StructRoot { Root = new Node3D { Name = $"Structure{st.Id}" } };
                _structs[st.Id] = sr;
                AddChild(sr.Root);
                rebuild = true;
            }
            sr.Root.Position = OriginCells(net, st.Id);
            if (!rebuild) continue;
            bool ship = st.Kind == StructureKind.Ship;
            foreach (var key in sr.Modules.Keys.ToList())
                if (!st.Modules.TryGetValue(key, out var k) || k != sr.Modules[key].Kind)
                {
                    sr.Modules[key].Node.QueueFree();
                    sr.Modules.Remove(key);
                }
            foreach (var (key, kind) in st.Modules)
            {
                if (sr.Modules.ContainsKey(key)) continue;
                var node = ModuleModels.Build(kind, ship, st.Id);
                node.Position = new Vector3(key.Mx * StationGrid.ModuleX, key.My * StationGrid.ModuleY, key.Mz * StationGrid.ModuleZ);
                sr.Root.AddChild(node);
                sr.Modules[key] = (kind, node);
            }
        }
        foreach (int id in _structs.Keys.Where(i => !alive.Contains(i)).ToList())
        {
            _structs[id].Root.QueueFree();
            _structs.Remove(id);
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        foreach (var (id, sr) in _structs)
        {
            bool thrust = _thrust.TryGetValue(id, out bool t) && t;
            var (sat, burning) = _power.TryGetValue(id, out var pw) ? pw : (1000, true);
            bool amber = sat < PowerRules.FullSatisfaction;
            foreach (var (kind, node) in sr.Modules.Values)
                switch (kind)
                {
                    case ModuleKind.Engine:
                        Models.SetPower(node, 0.2f + 0.8f * sat / 1000f); // the flame and vents dim with the power
                        Models.Animate(node, _time, thrust);
                        break;
                    case ModuleKind.Shield:
                    case ModuleKind.Sensor:
                        Models.SetPower(node, sat / 1000f); // the field and the sweep dim with the power and stop below half power
                        Models.Animate(node, _time, sat >= ShieldSensorRules.MinSatisfaction);
                        break;
                    case ModuleKind.Reactor:
                    case ModuleKind.ReactorMk2:
                        Models.SetReactor(node, burning);
                        Models.SetPower(node, burning ? 1f : 0.1f); // vents go dark with the core
                        Models.Animate(node, _time, burning);
                        break;
                    case ModuleKind.Core:
                        Models.Animate(node, _time, false);
                        break;
                    case ModuleKind.Cockpit:
                    case ModuleKind.Cargo:
                    case ModuleKind.DroneBay:
                        Models.SetAmber(node, amber);
                        break;
                }
        }
        if (_coreMarker is { Visible: true } m)
        {
            m.Rotation = new Vector3(0f, (float)_time * 0.8f, 0f);
            m.Position = m.Position with { Y = _coreBaseY + 0.25f * MathF.Sin((float)_time * 1.6f) };
        }
    }

    // ---- damage: tint, hit flash, sparks and smoke -------------------------------------------------------------

    private const float FlashSeconds = 0.3f;

    private sealed class ModuleFx
    {
        public CpuParticles3D Sparks = null!;
        public OmniLight3D Light = null!;
        public MeshInstance3D? Flash;
        public StandardMaterial3D? FlashMat;
    }

    private readonly Dictionary<(int S, int Mx, int My, int Mz), ModuleFx> _fx = new();
    private readonly Dictionary<(int S, int Mx, int My, int Mz), float> _flash = new();
    private readonly List<(int, int, int, int)> _fxGone = new();
    private Gradient? _sparkRamp;
    private Mesh? _sparkMesh;

    /// <summary>Lights a module's box for a moment (a hit). Cheap; call it for every hit event.</summary>
    public void Flash(int structure, int mx, int my, int mz, float strength = 1f) =>
        _flash[(structure, mx, my, mz)] = FlashSeconds * Math.Clamp(strength, 0.3f, 2f);

    /// <summary>Hit points of a module as a 0..1 fraction, or 1 when the module is unknown.</summary>
    public static float HpFraction(BeltNetwork net, int structure, int mx, int my, int mz, ModuleKind kind)
    {
        int max = net.ModuleMaxHp(kind);
        return max <= 0 ? 1f : Math.Clamp(net.ModuleHp(structure, mx, my, mz) / (float)max, 0f, 1f);
    }

    /// <summary>
    /// Per frame: darkens and reddens damaged modules, gives those below half their hit points flickering light, sparks and smoke, and
    /// plays the hit flashes. Nothing happens for undamaged modules, so a quiet base costs only the hit point lookups.
    /// </summary>
    public void UpdateDamage(BeltNetwork net, float dt)
    {
        if (!net.StructureRules) return;
        float t = (float)_time;
        foreach (var st in net.Structures())
        {
            if (!_structs.TryGetValue(st.Id, out var sr)) continue;
            foreach (var (key, (kind, node)) in sr.Modules)
            {
                if (!GodotObject.IsInstanceValid(node)) continue;
                float frac = HpFraction(net, st.Id, key.Mx, key.My, key.Mz, kind);
                var fxKey = (st.Id, key.Mx, key.My, key.Mz);
                Models.SetDamage(node, 1f - frac);
                bool burning = frac < 0.5f;
                bool flashing = _flash.ContainsKey(fxKey);
                if (!burning && !flashing)
                {
                    if (_fx.Remove(fxKey, out var old)) { old.Sparks.QueueFree(); old.Light.QueueFree(); old.Flash?.QueueFree(); }
                    continue;
                }
                if (!_fx.TryGetValue(fxKey, out var fx) || !GodotObject.IsInstanceValid(fx.Sparks) || fx.Sparks.GetParent() != node)
                {
                    fx = MakeFx(node);
                    _fx[fxKey] = fx;
                }
                fx.Sparks.Emitting = burning;
                // The worse the damage, the more sparks and the stronger the flicker.
                float sev = burning ? Math.Clamp((0.5f - frac) * 2f, 0f, 1f) : 0f;
                fx.Sparks.Amount = 10 + (int)(18 * sev);
                float flick = 0.55f + 0.45f * MathF.Sin(t * 31f + key.Mx * 1.7f) * MathF.Sin(t * 13f + key.Mz);
                fx.Light.Visible = burning;
                fx.Light.LightEnergy = (0.4f + 1.2f * sev) * Math.Clamp(flick, 0.05f, 1f);

                if (flashing)
                {
                    float left = _flash[fxKey] - dt;
                    if (left <= 0f) { _flash.Remove(fxKey); fx.Flash!.Visible = false; }
                    else
                    {
                        _flash[fxKey] = left;
                        fx.Flash!.Visible = true;
                        fx.FlashMat!.AlbedoColor = new Color(1f, 0.42f, 0.28f, 0.34f * Math.Clamp(left / FlashSeconds, 0f, 1f));
                    }
                }
                else if (fx.Flash is { Visible: true }) fx.Flash.Visible = false;
            }
        }
        // Flashes for modules that no longer exist, and effects of modules that were rebuilt or removed.
        _fxGone.Clear();
        foreach (var k in _flash.Keys) if (!net.Structures().Any(s => s.Id == k.S && s.Modules.ContainsKey((k.Mx, k.My, k.Mz)))) _fxGone.Add(k);
        foreach (var k in _fxGone) _flash.Remove(k);
        _fxGone.Clear();
        foreach (var (k, fx) in _fx) if (!GodotObject.IsInstanceValid(fx.Sparks)) _fxGone.Add(k);
        foreach (var k in _fxGone) _fx.Remove(k);
    }

    private ModuleFx MakeFx(Node3D module)
    {
        _sparkRamp ??= new Gradient
        {
            Offsets = new[] { 0f, 0.25f, 0.6f, 1f },
            Colors = new[] { new Color(1f, 0.9f, 0.5f, 1f), new Color(1f, 0.45f, 0.1f, 0.95f), new Color(0.25f, 0.22f, 0.22f, 0.6f), new Color(0.12f, 0.12f, 0.12f, 0f) },
        };
        _sparkMesh ??= new SphereMesh
        {
            Radius = 0.14f, Height = 0.28f, RadialSegments = 6, Rings = 3,
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        var centre = new Vector3(ModuleModels.W / 2f, ModuleModels.H / 2f, ModuleModels.D / 2f);
        var sparks = new CpuParticles3D
        {
            Name = "DamageSparks",
            Position = centre + new Vector3(0f, 0.6f, 0f),
            Amount = 12, Lifetime = 1.5f, Emitting = true, LocalCoords = false,
            Mesh = _sparkMesh, ColorRamp = _sparkRamp,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(2.8f, 0.4f, 2.8f),
            Direction = Vector3.Up, Spread = 40f, InitialVelocityMin = 0.7f, InitialVelocityMax = 2.4f,
            Gravity = new Vector3(0f, 0.4f, 0f), ScaleAmountMin = 0.5f, ScaleAmountMax = 1.7f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var light = new OmniLight3D
        {
            Name = "DamageLight", Position = centre + new Vector3(0f, 0.8f, 0f), OmniRange = 7f, LightColor = new Color(1f, 0.5f, 0.2f), ShadowEnabled = false,
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = new Color(1f, 0.4f, 0.3f, 0f),
        };
        var flash = new MeshInstance3D
        {
            Name = "HitFlash", Position = centre, Visible = false, MaterialOverride = mat,
            Mesh = new BoxMesh { Size = new Vector3(ModuleModels.W, ModuleModels.H, ModuleModels.D) * 1.01f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        module.AddChild(sparks);
        module.AddChild(light);
        module.AddChild(flash);
        return new ModuleFx { Sparks = sparks, Light = light, Flash = flash, FlashMat = mat };
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
