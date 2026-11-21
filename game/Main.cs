using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Belt prototype: renders the engine-independent simulation with one MultiMesh for belts and one
/// for items. All changes go through simulation commands. Run with user args after "--":
/// --bench, --commands (bench only: exercise commands), --shot=path, --layers=N, --rows=N, --cells=N.
/// Keys: B adds a belt row on top, X removes the last one added.
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

    private static readonly Color[] TierColors =
        { new(0.35f, 0.40f, 0.50f), new(0.20f, 0.55f, 0.60f), new(0.85f, 0.50f, 0.20f) };

    private static readonly Color[] MachineColors =
        { new(0.85f, 0.55f, 0.20f), new(0.80f, 0.25f, 0.20f), new(0.25f, 0.45f, 0.85f) };

    private SimWorld _world = null!;
    private ContentDb? _content;
    private int _chainSink = -1;
    private MultiMeshInstance3D? _machineNode;
    private MultiMesh? _machines;
    private float[] _machineBuf = Array.Empty<float>();
    private int[] _machineIds = Array.Empty<int>();
    private BeltNetwork Net => _world.Network;
    private Seg[] _segs = Array.Empty<Seg>();
    private ItemSample[] _samples = Array.Empty<ItemSample>();
    private int _count;
    private int _structureVersion = -1;

    private MultiMeshInstance3D? _slabNode;
    private MultiMeshInstance3D? _itemNode;
    private MultiMesh _items = null!;
    private float[] _buf = Array.Empty<float>();

    private Label _hud = null!;
    private readonly Stopwatch _simWatch = new();
    private double _simMsAvg;

    // Command demo
    private int _layers, _rows, _cells;
    private int _nextDemoRow;
    private readonly Stack<int> _demoIds = new();
    private int _commandsOk, _commandsRejected;

    private Camera3D _camera = null!;
    private float _yaw, _pitch;

    // Benchmark state
    private bool _bench;
    private bool _benchCommands;
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
        _benchCommands = args.Contains("--commands");
        _shotPath = args.FirstOrDefault(a => a.StartsWith("--shot="))?["--shot=".Length..];
        _layers = IntArg(args, "--layers", 10);
        _rows = IntArg(args, "--rows", 5);
        _cells = IntArg(args, "--cells", 50);

        _content = LoadContent();
        if (_content is null) { SetProcess(false); SetPhysicsProcess(false); SetProcessUnhandledInput(false); return; }
        _world = new SimWorld(Scenarios.ParallelRows(_layers, _rows, _cells, _content));
        SubmitChain();
        _world.Step(); // applies the chain batch
        CollectResults();
        _world.Step(30 * 60 - 1); // fill the belts (the slowest tier needs ~50 s for 50 cells) before the first frame

        BuildScene();
        RebuildGeometry();
        RefreshSamples();
        _lastUsec = Time.GetTicksUsec();
    }

    private static int IntArg(string[] args, string name, int fallback)
    {
        string? a = args.FirstOrDefault(x => x.StartsWith(name + "="));
        return a != null && int.TryParse(a[(name.Length + 1)..], out int v) ? v : fallback;
    }

    /// <summary>Loads core.json through ContentSource (flag, project resource, next to the executable, repository); shows the error screen when missing.</summary>
    private ContentDb? LoadContent()
    {
        var db = ContentSource.Load(OS.GetCmdlineUserArgs(), out var lookup, out string? error);
        if (db is null) ContentSource.ShowError(this, lookup, error);
        return db;
    }

    /// <summary>Builds an extractor, smelter and assembler chain with belts between them, as one atomic batch.</summary>
    private void SubmitChain()
    {
        if (_content is null) return;
        const int z = -6;
        int extractor = _content.MachineIndex("core:extractor");
        int smelter = _content.MachineIndex("core:smelter");
        int assembler = _content.MachineIndex("core:assembler");
        _world.Submit(0, new Batch(new Command[]
        {
            new PlaceMachine(0, 0, z, extractor, _content.RecipeIndex("core:mine_iron")),   // machine -1
            new PlaceMachine(5, 0, z, smelter, _content.RecipeIndex("core:smelt_iron")),    // machine -2
            new PlaceMachine(10, 0, z, assembler, _content.RecipeIndex("core:iron_plate")), // machine -3
            new PlaceSegment(1, 0, z, Dir.PosX, 4, BeltTier.Mk2),                           // segment -1
            new PlaceSegment(6, 0, z, Dir.PosX, 4, BeltTier.Mk2),                           // segment -2
            new PlaceSegment(11, 0, z, Dir.PosX, 4, BeltTier.Mk2),                          // segment -3
            new ConnectFromMachine(-1, -1), new ConnectToMachine(-1, -2),
            new ConnectFromMachine(-2, -2), new ConnectToMachine(-2, -3),
            new ConnectFromMachine(-3, -3),
            new SetSink(-3),
        }));
    }

    /// <summary>Rebuilds belt slabs and the item buffer. Called at start and whenever the network structure changes.</summary>
    private void RebuildGeometry()
    {
        _slabNode?.QueueFree();
        _itemNode?.QueueFree();
        _machineNode?.QueueFree();

        _segs = new Seg[Net.SegmentIdLimit];
        var alive = new List<int>();
        int capacity = 0;
        for (int id = 0; id < _segs.Length; id++)
        {
            if (!Net.IsAlive(id)) continue;
            var s = Net.GetSegmentInfo(id);
            _segs[id] = new Seg
            {
                Ox = s.OriginX, Oy = s.OriginY, Oz = s.OriginZ,
                Dx = s.DirX, Dy = s.DirY, Dz = s.DirZ,
                Length = s.Length, Speed = s.Speed,
            };
            alive.Add(id);
            capacity += s.Length / Units.ItemSpacing + 2;
        }

        // Belt slabs: one stretched box per segment, tinted by tier.
        var slabs = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexColorMaterial() },
            InstanceCount = alive.Count,
        };
        var slabBuf = new float[alive.Count * FloatsPerItem];
        for (int i = 0; i < alive.Count; i++)
        {
            var info = Net.GetSegmentInfo(alive[i]);
            float len = info.LengthCells;
            float sx = info.DirX != 0 ? len : 1f, sy = info.DirY != 0 ? len : 0.2f, sz = info.DirZ != 0 ? len : 1f;
            float cx = info.OriginX + info.DirX * len / 2f;
            float cy = info.OriginY + info.DirY * len / 2f;
            float cz = info.OriginZ + info.DirZ * len / 2f;
            Write(slabBuf, i * FloatsPerItem, sx, sy, sz, cx, cy, cz, TierColors[(int)info.Tier]);
        }
        slabs.Buffer = slabBuf;
        _slabNode = new MultiMeshInstance3D { Multimesh = slabs };
        AddChild(_slabNode);

        // Items.
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
        _itemNode = new MultiMeshInstance3D { Multimesh = _items };
        AddChild(_itemNode);

        // Machines: one box each, tinted by type; brightness shows whether it is crafting (updated every frame).
        var machineIds = Enumerable.Range(0, Net.MachineIdLimit).Where(Net.IsMachineAlive).ToArray();
        _machineIds = machineIds;
        _machines = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexColorMaterial() },
            InstanceCount = machineIds.Length,
        };
        _machineBuf = new float[machineIds.Length * FloatsPerItem];
        _machineNode = new MultiMeshInstance3D { Multimesh = _machines };
        AddChild(_machineNode);

        _structureVersion = Net.StructureVersion;
    }

    private void UpdateMachines()
    {
        if (_machines is null || _machineIds.Length == 0) return;
        for (int i = 0; i < _machineIds.Length; i++)
        {
            var m = Net.GetMachineInfo(_machineIds[i]);
            var c = MachineColors[m.Type % MachineColors.Length];
            float k = m.Crafting ? 1f : 0.45f;
            Write(_machineBuf, i * FloatsPerItem, 0.9f, 0.9f, 0.9f, m.X + 0.5f, m.Y + 0.45f, m.Z,
                new Color(c.R * k, c.G * k, c.B * k));
        }
        _machines.Buffer = _machineBuf;
    }

    private static StandardMaterial3D VertexColorMaterial() => new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.7f,
    };

    private void BuildScene()
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
        var center = new Vector3(_cells / 2f, _layers, _rows);
        var pos = center + new Vector3(_cells * 0.3f, _layers * 0.9f + 3f, _cells * 0.42f);
        if (OS.GetCmdlineUserArgs().Contains("--view=chain"))
        {
            center = new Vector3(7.5f, 0.5f, -6f);
            pos = center + new Vector3(0f, 6f, -9f);
        }
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

    private void RefreshSamples() => _count = Net.FillSamples(_samples);

    // ---- commands ------------------------------------------------------------------------------

    /// <summary>Adds a fed and drained Mk3 belt on top of the stack, as one atomic batch.</summary>
    private void SubmitAddRow()
    {
        int z = _nextDemoRow * 2;
        _nextDemoRow = (_nextDemoRow + 1) % Math.Max(1, _rows);
        _world.Submit(0, new Batch(new Command[]
        {
            new PlaceSegment(0, _layers * 2, z, Dir.PosX, _cells, BeltTier.Mk3),
            new SetSource(-1, new ushort[] { 0, 1, 2, 3 }),
            new SetSink(-1),
        }));
    }

    private void SubmitRemoveLast()
    {
        if (_demoIds.Count > 0) _world.Submit(0, new RemoveSegment(_demoIds.Pop()));
    }

    private void CollectResults()
    {
        foreach (var r in _world.LastResults)
        {
            if (r.Ok) _commandsOk++; else _commandsRejected++;
            if (r.Ok && r.Command is Batch b && r.Created.Count == 3 && r.CreatedMachines.Count == 3 && _chainSink < 0)
                _chainSink = r.Created[2]; // the machine chain batch
            else if (r.Ok && r.Command is Batch && r.Created.Count > 0) _demoIds.Push(r.Created[0]);
            GD.Print($"COMMAND tick={r.Tick} {r.Command.GetType().Name} -> {(r.Ok ? "ok" : r.Reject.ToString())}" +
                     (r.Created.Count > 0 ? $" created=[{string.Join(",", r.Created)}]" : ""));
        }
    }

    // ---- frame loop ----------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        _simWatch.Restart();
        _world.Step();
        _simWatch.Stop();
        CollectResults();
        if (Net.StructureVersion != _structureVersion) RebuildGeometry();
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
        UpdateMachines();

        _hud.Text =
            $"FPS {Engine.GetFramesPerSecond()}   items {_count}   sim {_simMsAvg:F3} ms/tick   segments {Net.SegmentCount}\n" +
            $"commands ok {_commandsOk}  rejected {_commandsRejected}   machines {Net.MachineCount}   " +
            $"plates delivered {(_chainSink >= 0 && Net.IsAlive(_chainSink) ? Net.ConsumedBy(_chainSink) : 0)}\n" +
            "RMB + mouse: look   WASD/QE: move   Shift: fast   B: add belt row   X: remove it";

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
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.B) SubmitAddRow();
            else if (key.Keycode == Key.X) SubmitRemoveLast();
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

        if (_benchCommands)
        {
            // Exercise the command layer while measuring: add a row, remove one, and try an invalid command.
            if (_frame == WarmupFrames + 60) SubmitAddRow();
            if (_frame == WarmupFrames + 120) _world.Submit(0, new RemoveSegment(0));
            if (_frame == WarmupFrames + 180) _world.Submit(0, new RemoveSegment(0)); // already gone: rejected
            if (_frame == WarmupFrames + 240) SubmitAddRow();
        }

        if (_frame == WarmupFrames + MeasureFrames / 2 && _shotPath != null)
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);

        if (_frame < WarmupFrames + MeasureFrames) return;

        var sorted = _frameMs.OrderBy(x => x).ToArray();
        double avg = sorted.Average();
        double p99 = sorted[(int)(sorted.Length * 0.99)];
        bool conserved = Net.Spawned + Net.MachineOutputsCreated ==
            Net.Consumed + Net.Destroyed + Net.MachineInputsConsumed + Net.InTransit + Net.MachineHeldItems;
        GD.Print($"BENCH chain_plates={(_chainSink >= 0 && Net.IsAlive(_chainSink) ? Net.ConsumedBy(_chainSink) : -1)} " +
                 $"machines={Net.MachineCount} crafted={Net.MachineOutputsCreated}");
        GD.Print($"BENCH items={_count} segments={Net.SegmentCount} frames={sorted.Length}");
        GD.Print($"BENCH avg_frame_ms={avg:F3} avg_fps={1000.0 / avg:F1} p99_frame_ms={p99:F3} min_fps={1000.0 / sorted[^1]:F1}");
        GD.Print($"BENCH sim_ms_per_tick_avg={_simMsAvg:F4} conserved={conserved}");
        GD.Print($"BENCH commands_ok={_commandsOk} commands_rejected={_commandsRejected} destroyed={Net.Destroyed} structure_version={Net.StructureVersion}");
        GD.Print($"BENCH renderer={RenderingServer.GetVideoAdapterName()}");
        GetTree().Quit();
    }
}
