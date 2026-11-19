using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Belt prototype: renders the engine-independent simulation with one MultiMesh for belts and one
/// for items. Run with user args after "--": --bench, --shot=path, --layers=N, --rows=N, --cells=N.
/// </summary>
public partial class Main : Node3D
{
    private const int FloatsPerItem = 16; // 3x4 transform + RGBA color
    private const float ItemSize = 0.22f;
    private const float CellUnits = Units.CellUnits;

    private struct Seg
    {
        public float Ox, Oy, Oz, Dx, Dy, Dz;
        public int Length, Speed;
    }

    private static readonly Color[] ItemColors =
    {
        new(0.95f, 0.35f, 0.25f), new(0.30f, 0.80f, 0.45f), new(0.35f, 0.55f, 0.95f), new(0.95f, 0.85f, 0.30f),
    };

    private BeltNetwork _net = null!;
    private Seg[] _segs = Array.Empty<Seg>();
    private ItemSample[] _samples = Array.Empty<ItemSample>();
    private int _count;

    private MultiMesh _items = null!;
    private float[] _buf = Array.Empty<float>();

    private Label _hud = null!;
    private readonly Stopwatch _simWatch = new();
    private double _simMsAvg;

    private Camera3D _camera = null!;
    private float _yaw, _pitch;

    // Benchmark state
    private bool _bench;
    private string? _shotPath;
    private int _frame;
    private const int WarmupFrames = 120;
    private const int MeasureFrames = 600;
    private readonly List<double> _frameMs = new();
    private ulong _lastUsec;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _bench = args.Contains("--bench");
        _shotPath = args.FirstOrDefault(a => a.StartsWith("--shot="))?["--shot=".Length..];
        int layers = IntArg(args, "--layers", 10);
        int rows = IntArg(args, "--rows", 5);
        int cells = IntArg(args, "--cells", 50);

        _net = Scenarios.ParallelRows(layers, rows, cells);
        _net.Step(30 * 60); // fill the belts (the slowest tier needs ~50 s for 50 cells) before the first frame

        BuildGeometry();
        BuildScene(layers, rows, cells);
        RefreshSamples();
        _lastUsec = Time.GetTicksUsec();
    }

    private static int IntArg(string[] args, string name, int fallback)
    {
        string? a = args.FirstOrDefault(x => x.StartsWith(name + "="));
        return a != null && int.TryParse(a[(name.Length + 1)..], out int v) ? v : fallback;
    }

    private void BuildGeometry()
    {
        _segs = new Seg[_net.SegmentCount];
        for (int i = 0; i < _segs.Length; i++)
        {
            var s = _net.GetSegmentInfo(i);
            _segs[i] = new Seg
            {
                Ox = s.OriginX, Oy = s.OriginY, Oz = s.OriginZ,
                Dx = s.DirX, Dy = s.DirY, Dz = s.DirZ,
                Length = s.Length, Speed = s.Speed,
            };
        }

        // Belt slabs: one stretched box per segment, tinted by tier.
        var slabMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexColorMaterial() },
            InstanceCount = _segs.Length,
        };
        var slabBuf = new float[_segs.Length * FloatsPerItem];
        Color[] tierColors = { new(0.35f, 0.40f, 0.50f), new(0.20f, 0.55f, 0.60f), new(0.85f, 0.50f, 0.20f) };
        for (int i = 0; i < _segs.Length; i++)
        {
            var info = _net.GetSegmentInfo(i);
            float len = info.LengthCells;
            float sx = info.DirX != 0 ? len : 1f, sy = info.DirY != 0 ? len : 0.2f, sz = info.DirZ != 0 ? len : 1f;
            float cx = info.OriginX + info.DirX * len / 2f;
            float cy = info.OriginY + info.DirY * len / 2f;
            float cz = info.OriginZ + info.DirZ * len / 2f;
            Write(slabBuf, i * FloatsPerItem, sx, sy, sz, cx, cy, cz, tierColors[(int)info.Tier]);
        }
        slabMesh.Buffer = slabBuf;
        AddChild(new MultiMeshInstance3D { Multimesh = slabMesh });

        // Items.
        int capacity = 0;
        for (int i = 0; i < _net.SegmentCount; i++)
            capacity += _net.GetSegmentInfo(i).Length / Units.ItemSpacing + 2;
        _samples = new ItemSample[capacity];
        _items = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexColorMaterial() },
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        _buf = new float[capacity * FloatsPerItem];
        AddChild(new MultiMeshInstance3D { Multimesh = _items });
    }

    private static StandardMaterial3D VertexColorMaterial() => new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.7f,
    };

    private void BuildScene(int layers, int rows, int cells)
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.04f, 0.05f, 0.09f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
                AmbientLightEnergy = 0.7f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.1f };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-50, -35, 0);

        _camera = new Camera3D { Far = 2000f };
        AddChild(_camera);
        var center = new Vector3(cells / 2f, layers, rows);
        var pos = center + new Vector3(cells * 0.3f, layers * 0.9f + 3f, cells * 0.42f);
        _camera.LookAtFromPosition(pos, center);
        var euler = _camera.GlobalTransform.Basis.GetEuler();
        _pitch = euler.X;
        _yaw = euler.Y;

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_hud);
    }

    private static void Write(float[] b, int o, float sx, float sy, float sz, float x, float y, float z, Color c)
    {
        b[o + 0] = sx; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
        b[o + 4] = 0; b[o + 5] = sy; b[o + 6] = 0; b[o + 7] = y;
        b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = sz; b[o + 11] = z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    private void RefreshSamples() => _count = _net.FillSamples(_samples);

    public override void _PhysicsProcess(double delta)
    {
        _simWatch.Restart();
        _net.Step();
        _simWatch.Stop();
        RefreshSamples();
        _simMsAvg = _simMsAvg * 0.95 + _simWatch.Elapsed.TotalMilliseconds * 0.05;
    }

    public override void _Process(double delta)
    {
        float alpha = (float)Engine.GetPhysicsInterpolationFraction();

        for (int i = 0; i < _count; i++)
        {
            var s = _samples[i];
            ref readonly Seg g = ref _segs[s.Segment];
            float p = MathF.Min(s.Pos + g.Speed * alpha, g.Length) / CellUnits;
            Write(_buf, i * FloatsPerItem, ItemSize, ItemSize, ItemSize,
                g.Ox + g.Dx * p, g.Oy + g.Dy * p + 0.22f, g.Oz + g.Dz * p, ItemColors[s.Type & 3]);
        }
        _items.VisibleInstanceCount = _count;
        _items.Buffer = _buf;

        _hud.Text =
            $"FPS {Engine.GetFramesPerSecond()}   items {_count}   sim {_simMsAvg:F3} ms/tick   segments {_segs.Length}\n" +
            "RMB + mouse: look   WASD/QE: move   Shift: fast";

        MoveCamera((float)delta);
        if (_bench) TickBench();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right)
            Input.MouseMode = mb.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        else if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= mm.Relative.X * 0.003f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.003f, -1.5f, 1.5f);
            _camera.Rotation = new Vector3(_pitch, _yaw, 0);
        }
    }

    private void MoveCamera(float dt)
    {
        var dir = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) dir -= _camera.GlobalTransform.Basis.Z;
        if (Input.IsKeyPressed(Key.S)) dir += _camera.GlobalTransform.Basis.Z;
        if (Input.IsKeyPressed(Key.A)) dir -= _camera.GlobalTransform.Basis.X;
        if (Input.IsKeyPressed(Key.D)) dir += _camera.GlobalTransform.Basis.X;
        if (Input.IsKeyPressed(Key.E)) dir += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) dir += Vector3.Down;
        if (dir == Vector3.Zero) return;
        float speed = Input.IsKeyPressed(Key.Shift) ? 60f : 15f;
        _camera.GlobalPosition += dir.Normalized() * speed * dt;
    }

    private void TickBench()
    {
        _frame++;
        ulong now = Time.GetTicksUsec();
        if (_frame > WarmupFrames) _frameMs.Add((now - _lastUsec) / 1000.0);
        _lastUsec = now;

        if (_frame == WarmupFrames + MeasureFrames / 2 && _shotPath != null)
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);

        if (_frame < WarmupFrames + MeasureFrames) return;

        var sorted = _frameMs.OrderBy(x => x).ToArray();
        double avg = sorted.Average();
        double p99 = sorted[(int)(sorted.Length * 0.99)];
        GD.Print($"BENCH items={_count} segments={_segs.Length} frames={sorted.Length}");
        GD.Print($"BENCH avg_frame_ms={avg:F3} avg_fps={1000.0 / avg:F1} p99_frame_ms={p99:F3} min_fps={1000.0 / sorted[^1]:F1}");
        GD.Print($"BENCH sim_ms_per_tick_avg={_simMsAvg:F4} conserved={_net.Spawned == _net.Consumed + _net.InTransit}");
        GD.Print($"BENCH renderer={RenderingServer.GetVideoAdapterName()}");
        GetTree().Quit();
    }
}
