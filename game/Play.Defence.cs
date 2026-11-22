using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// What the player sees of structure defence (ADR 0021): hit flashes, damage tint, sparks and smoke on broken modules, explosions
/// when a module or a whole station falls, a red vignette when the ship is hit, the integrity bar, the wave banner, the hit-point
/// bar of the module under the crosshair and the toasts. The simulation reports hits per tick (<see cref="BeltNetwork.ModuleHits"/>
/// is cleared every tick), so <see cref="WatchDefence"/> runs after every tick and never misses one at high speed.
/// </summary>
public partial class Play
{
    private const float VignetteDecayPerSecond = 0.9f;
    private const double HitBarSeconds = 3.5;

    private ColorRect _vignette = null!;
    private ShaderMaterial _vignetteMat = null!;
    private float _vigAlpha;
    private ModuleBarLayer _moduleBars = null!;
    private PanelContainer _modulePanel = null!;
    private Label _moduleTitle = null!, _moduleHpLabel = null!;
    private ProgressBar _moduleHp = null!;
    private HBoxContainer _integrityBox = null!;
    private Label _integrityLabel = null!;
    private ProgressBar _integrityBar = null!;
    private Label _waveBanner = null!, _goalLabel = null!;

    private readonly Dictionary<(int S, int Mx, int My, int Mz), double> _hitBars = new();

    /// <summary>The geometry of a structure as of the last tick with enemies about, so a module or station that is gone can still explode where it stood.</summary>
    private sealed record StructSnap(long X, long Y, long Z, (int Mx, int My, int Mz)[] Modules);

    private readonly Dictionary<int, StructSnap> _snaps = new();

    // ---- interface ------------------------------------------------------------------------------------

    /// <summary>The integrity bar in the status block, the hit vignette (behind every panel), the module bars, the module panel and the wave banner.</summary>
    private void BuildDefenceUi(VBoxContainer statusBox)
    {
        _integrityBox = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _integrityBox.AddThemeConstantOverride("separation", 8);
        _integrityLabel = MakeText(13);
        _integrityBar = new ProgressBar { MinValue = 0, MaxValue = 1000, ShowPercentage = false, CustomMinimumSize = new Vector2(110, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        _integrityBox.AddChild(_integrityLabel);
        _integrityBox.AddChild(_integrityBar);
        statusBox.AddChild(_integrityBox);

        _vignetteMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type canvas_item;
uniform float strength = 0.0;
void fragment() {
    float d = length((UV - 0.5) * vec2(1.25, 1.0)) * 1.6;
    float edge = smoothstep(0.35, 1.05, d);
    COLOR = vec4(0.85, 0.05, 0.04, edge * strength);
}",
            },
        };
        _vignette = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _vignetteMat, Color = Colors.White, Visible = false };
        _vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiRoot.AddChild(_vignette);
        _uiRoot.MoveChild(_vignette, 0);

        _moduleBars = new ModuleBarLayer();
        _uiRoot.AddChild(_moduleBars);

        var box = new VBoxContainer();
        _moduleTitle = MakeText(14, new Color(1f, 0.9f, 0.5f));
        _moduleHp = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(200, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        _moduleHpLabel = MakeText(12);
        box.AddChild(_moduleTitle);
        box.AddChild(_moduleHp);
        box.AddChild(_moduleHpLabel);
        _modulePanel = MakePanel(box);
        _modulePanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _modulePanel.Visible = false;

        _waveBanner = MakeText(26, ToastThreatColor);
        _waveBanner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _waveBanner.AddThemeConstantOverride("outline_size", 8);
        _waveBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _waveBanner.Visible = false;
        _uiRoot.AddChild(_waveBanner);
    }

    private static Color HpColor(float fraction) =>
        fraction > 0.6f ? new Color(0.45f, 0.85f, 0.5f) : fraction > 0.3f ? new Color(0.95f, 0.75f, 0.3f) : new Color(0.92f, 0.3f, 0.25f);

    private void StyleBar(ProgressBar bar, float fraction)
    {
        bar.AddThemeStyleboxOverride("fill", Box(HpColor(fraction), null, 3, 0f));
        bar.AddThemeStyleboxOverride("background", Box(new Color(0.12f, 0.14f, 0.2f), null, 3, 0f));
    }

    /// <summary>Per frame: effects, the integrity bar, the banner, the module under the crosshair and the floating bars.</summary>
    private void UpdateDefenceUi(float dt)
    {
        _vigAlpha = MathF.Max(0f, _vigAlpha - dt * VignetteDecayPerSecond);
        _vignette.Visible = _vigAlpha > 0.01f;
        _vignetteMat.SetShaderParameter("strength", Math.Clamp(_vigAlpha, 0f, 1f));

        UpdateBursts(dt);
        bool structures = _structuresOn && Net.StructureRules && !_demo;
        if (_structuresOn) _stationView.UpdateDamage(Net, dt);

        // Integrity of the ship.
        int ship = ShipStruct;
        _integrityBox.Visible = structures && ship >= 0 && Net.ThreatActive && Net.GetStructure(ship).Modules.Count > 0;
        if (_integrityBox.Visible)
        {
            int permille = Net.StructureHpFraction(ship);
            float f = permille / 1000f;
            _integrityLabel.Text = L("hud.integrity", permille / 10);
            _integrityLabel.AddThemeColorOverride("font_color", HpColor(f));
            _integrityBar.Value = permille;
            StyleBar(_integrityBar, f);
        }

        // The wave: a big countdown while the warning runs, the enemy count while it is fought.
        string banner = "";
        if (Net.ThreatActive && !_demo && Net.HasPlayer(Player))
        {
            var th = Net.GetSystemThreat(Math.Max(0, Net.CurrentSystem(Player)));
            if (th.WarningRemaining >= 0) banner = L("hud.banner.wave", th.Wave + 1, Math.Ceiling(th.WarningRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture));
            else if (th.Enemies > 0) banner = L("hud.banner.fight", Net.WavesSpawned, th.Enemies);
        }
        _waveBanner.Text = banner;
        _waveBanner.Visible = banner.Length > 0;

        UpdateModuleAim(structures);
    }

    /// <summary>The module of a structure whose cell the crosshair is on, or null.</summary>
    private (int S, (int Mx, int My, int Mz) Key, ModuleKind Kind)? AimedModule()
    {
        if (!_structuresOn || _aimCell is not { } c || c.S == 0) return null;
        var key = StationGrid.ModuleOfCell(c.X, c.Y, c.Z);
        foreach (var st in Net.Structures())
            if (st.Id == c.S) return st.Modules.TryGetValue(key, out var kind) ? (st.Id, key, kind) : null;
        return null;
    }

    private void UpdateModuleAim(bool structures)
    {
        var bars = new List<ModuleBarLayer.Item>();
        _modulePanel.Visible = false;
        if (!structures || !Net.ThreatActive)
        {
            _moduleBars.Set(bars);
            return;
        }
        double now = Time.GetTicksMsec() / 1000.0;
        var aimed = AimActive && _tool == ToolMode.Pointer ? AimedModule() : null;
        if (aimed is { } a)
        {
            int max = Net.ModuleMaxHp(a.Kind), hp = Net.ModuleHp(a.S, a.Key.Mx, a.Key.My, a.Key.Mz);
            if (max > 0 && hp < max)
            {
                float f = hp / (float)max;
                _moduleTitle.Text = KindName(a.Kind);
                _moduleHp.MaxValue = max;
                _moduleHp.Value = hp;
                StyleBar(_moduleHp, f);
                _moduleHpLabel.Text = L("tip.hp", hp, max);
                _moduleHpLabel.AddThemeColorOverride("font_color", HpColor(f));
                _modulePanel.Visible = true;
                _hitBars[(a.S, a.Key.Mx, a.Key.My, a.Key.Mz)] = now + 0.2; // the floating bar follows the aim
            }
        }

        // Floating bars: the aimed module and every module hit in the last few seconds, while damaged and in front of the camera.
        foreach (var k in _hitBars.Where(p => p.Value < now).Select(p => p.Key).ToList()) _hitBars.Remove(k);
        foreach (var (k, _) in _hitBars)
        {
            var st = Net.Structures().FirstOrDefault(s => s.Id == k.S);
            if (st is null || !st.Modules.TryGetValue((k.Mx, k.My, k.Mz), out var kind)) continue;
            int max = Net.ModuleMaxHp(kind), hp = Net.ModuleHp(k.S, k.Mx, k.My, k.Mz);
            if (max <= 0 || hp >= max) continue;
            var world = StructOrigin(k.S) + StationView.Centre(k.Mx, k.My, k.Mz) + new Vector3(0f, StationGrid.ModuleY / 2f, 0f);
            if (_camera.IsPositionBehind(world)) continue;
            var screen = _camera.UnprojectPosition(world);
            bars.Add(new ModuleBarLayer.Item(screen, $"{KindName(kind)} {hp}/{max}", hp / (float)max, HpColor(hp / (float)max)));
        }
        _moduleBars.Set(bars);
    }

    private void LayoutDefenceUi(Vector2 size, Vector2 centre)
    {
        _modulePanel.ResetSize();
        var ms = _modulePanel.GetCombinedMinimumSize();
        _modulePanel.Position = new Vector2(Math.Clamp(centre.X + 28, 8, Math.Max(8, size.X - ms.X - 8)), Math.Clamp(centre.Y - 28 - ms.Y, 8, Math.Max(8, size.Y - ms.Y - 8)));
        _waveBanner.ResetSize();
        _waveBanner.Position = new Vector2((size.X - _waveBanner.Size.X) / 2f, _flightPanel.Position.Y + _flightPanel.Size.Y + 6f);
    }

    // ---- the goal line -----------------------------------------------------------------------------------

    private GoalSettings _goal = new();

    private void UpdateGoalLine()
    {
        bool show = !_demo && _gameMode == GameMode.Campaign && Net.ThreatActive;
        _goalLabel.Visible = show;
        if (!show) return;
        int done = Math.Min(GameOutcome.WavesSurvived(Net), _goal.WavesToWin);
        bool station = !Net.StructureRules || GameOutcome.HasBase(Net, Player);
        bool explored = GameOutcome.HasExplored(Net);
        _goalLabel.Text = Net.StructureRules
            ? L("goal.line", _goal.WavesToWin, done, GameOutcome.JumpsDone(Net), GoalSettings.JumpsToWin)
            : L("goal.lineNoStation", _goal.WavesToWin, done);
        if (!station && done >= _goal.WavesToWin) _goalLabel.Text += "\n" + L("goal.needStation");
        else if (station && !explored && done >= _goal.WavesToWin) _goalLabel.Text += "\n" + L("goal.needJump");
        _goalLabel.AddThemeColorOverride("font_color", done >= _goal.WavesToWin && station && explored ? GoodColor : new Color(1f, 0.85f, 0.45f));
    }

    // ---- per tick: hits, toasts and effects ----------------------------------------------------------------------

    private long _lastDefShips;

    /// <summary>Called after every simulation tick: reads the module hits of that tick (they are gone after the next one).</summary>
    private void WatchDefence()
    {
        if (!_structuresOn || !Net.StructureRules) return;
        double now = Time.GetTicksMsec() / 1000.0;
        int myShip = Net.ShipStructure(Player);
        foreach (var e in Net.ModuleHits)
        {
            bool mine = e.Structure == myShip;
            if (mine) _vigAlpha = MathF.Max(_vigAlpha, e.Destroyed ? 1f : 0.55f);
            if (!e.Destroyed)
            {
                _stationView.Flash(e.Structure, e.Mx, e.My, e.Mz);
                _hitBars[(e.Structure, e.Mx, e.My, e.Mz)] = now + HitBarSeconds;
                continue;
            }
            var at = CentreOfLost(e);
            if (e.StationLost)
            {
                ToastThreat(L("toast.stationLost"), 9.0);
                if (at is { } c) CollapseStation(e.Structure, c);
            }
            else
            {
                if (!e.ShipLost) ToastThreat(L("toast.moduleDestroyed", KindName(e.Kind)));
                if (at is { } c) Explode(c, 6f, 0f);
                _hitBars.Remove((e.Structure, e.Mx, e.My, e.Mz));
            }
        }
        // Remember where things are while enemies are about, for the next tick's events.
        if (Net.EnemyCount > 0 || Net.ModuleHits.Count > 0) SnapStructures();
    }

    private void SnapStructures()
    {
        _snaps.Clear();
        foreach (var st in Net.Structures()) _snaps[st.Id] = new StructSnap(st.OriginX, st.OriginY, st.OriginZ, st.Modules.Keys.ToArray());
    }

    /// <summary>World position in cells of the module an event says was destroyed (from the snapshot, as the structure may be gone).</summary>
    private Vector3? CentreOfLost(ModuleHitEvent e)
    {
        if (!_snaps.TryGetValue(e.Structure, out var s)) return null;
        // Structures are only translated, so the ship's position now is its snapshot plus at most a tick of flight: use the live origin when it exists.
        var origin = new Vector3(s.X, s.Y, s.Z) / CellUnits;
        if (Net.Structures().FirstOrDefault(st => st.Id == e.Structure) is { } live) origin = new Vector3(live.OriginX, live.OriginY, live.OriginZ) / CellUnits;
        return origin + StationView.Centre(e.Mx, e.My, e.Mz);
    }

    // ---- explosions ------------------------------------------------------------------------------------------------

    private sealed class Burst
    {
        public Node3D Root = null!;
        public MeshInstance3D? Ball, Ring;
        public StandardMaterial3D? BallMat, RingMat;
        public OmniLight3D? Light;
        public float Age, Life, Size;
    }

    private readonly List<Burst> _bursts = new();
    private readonly List<(float Delay, Vector3 At, float Size, bool Ring)> _pendingBursts = new();
    private Gradient? _burstRamp;
    private Mesh? _burstMesh;

    private void Explode(Vector3 at, float size, float delay, bool ring = false)
    {
        if (delay > 0f) { _pendingBursts.Add((delay, at, size, ring)); return; }
        if (_bursts.Count >= 24) return;
        _burstRamp ??= new Gradient
        {
            Offsets = new[] { 0f, 0.2f, 0.55f, 1f },
            Colors = new[] { new Color(1f, 0.95f, 0.7f, 1f), new Color(1f, 0.55f, 0.12f, 1f), new Color(0.35f, 0.18f, 0.1f, 0.7f), new Color(0.1f, 0.1f, 0.1f, 0f) },
        };
        _burstMesh ??= new SphereMesh
        {
            Radius = 0.22f, Height = 0.44f, RadialSegments = 6, Rings = 3,
            Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
        };
        var b = new Burst { Root = new Node3D { Position = at }, Life = 1.3f, Size = size };
        b.BallMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1f, 0.7f, 0.25f, 0.9f) };
        b.Ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 14, Rings = 7 }, MaterialOverride = b.BallMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        b.Root.AddChild(b.Ball);
        if (ring)
        {
            b.RingMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.75f, 0.9f, 1f, 0.7f) };
            b.Ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 48, RingSegments = 6 }, MaterialOverride = b.RingMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            b.Root.AddChild(b.Ring);
        }
        b.Root.AddChild(new CpuParticles3D
        {
            Amount = (int)(34 + size * 4), Lifetime = 1.25f, OneShot = true, Explosiveness = 1f, Emitting = true, LocalCoords = false,
            Mesh = _burstMesh, ColorRamp = _burstRamp, Direction = Vector3.Up, Spread = 180f,
            InitialVelocityMin = size * 0.8f, InitialVelocityMax = size * 2.6f, Gravity = Vector3.Zero, ScaleAmountMin = 0.6f, ScaleAmountMax = 2.2f * size / 4f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        b.Light = new OmniLight3D { OmniRange = size * 3.5f, LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = 5f, ShadowEnabled = false };
        b.Root.AddChild(b.Light);
        AddChild(b.Root);
        _bursts.Add(b);
    }

    /// <summary>A station falls: a chain of blasts over its footprint, ending in a big one with a shockwave ring.</summary>
    private void CollapseStation(int structure, Vector3 coreAt)
    {
        var rng = new Random(structure * 7919 + _bursts.Count);
        if (_snaps.TryGetValue(structure, out var s))
        {
            var origin = new Vector3(s.X, s.Y, s.Z) / CellUnits;
            int n = 0;
            foreach (var m in s.Modules)
            {
                var c = origin + StationView.Centre(m.Mx, m.My, m.Mz);
                Explode(c + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 4f, 4.8f, 0.12f + 0.22f * n++);
            }
        }
        Explode(coreAt, 7.5f, 0f, ring: true);
        Explode(coreAt + new Vector3(0f, 2f, 0f), 11f, 0.9f, ring: true);
    }

    private void UpdateBursts(float dt)
    {
        for (int i = _pendingBursts.Count - 1; i >= 0; i--)
        {
            var p = _pendingBursts[i];
            p.Delay -= dt;
            if (p.Delay > 0f) { _pendingBursts[i] = p; continue; }
            _pendingBursts.RemoveAt(i);
            Explode(p.At, p.Size, 0f, p.Ring);
        }
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            b.Age += dt;
            float t = b.Age / b.Life;
            if (t >= 1f) { b.Root.QueueFree(); _bursts.RemoveAt(i); continue; }
            float grow = 1f - MathF.Pow(1f - MathF.Min(1f, t * 2.2f), 2f);
            b.Ball!.Scale = Vector3.One * (0.4f + b.Size * 0.5f * grow);
            b.BallMat!.AlbedoColor = new Color(1f, 0.75f - 0.4f * t, 0.3f - 0.2f * t, 0.9f * MathF.Pow(1f - t, 1.6f));
            if (b.Ring is not null)
            {
                b.Ring.Scale = Vector3.One * (1f + b.Size * 2.2f * grow);
                b.RingMat!.AlbedoColor = new Color(0.75f, 0.9f, 1f, 0.7f * (1f - t));
            }
            b.Light!.LightEnergy = 5f * MathF.Pow(1f - t, 2f);
        }
    }
}
