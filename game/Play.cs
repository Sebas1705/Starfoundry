using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The playable loop: mine ore by hand, refine it on the ship, build drills, smelters, assemblers, labs and
/// depots with belts, feed the research lab and unlock more content. Every change goes through simulation
/// commands. Controls are listed on screen. User args after "--": --demo (a scripted bot plays the loop),
/// --speed=N (simulation ticks per frame in demo), --shot=path.
/// </summary>
public partial class Play : Node3D
{
    private const int Player = 0;
    private const int FloatsPerItem = 16;
    private const float CellUnits = Units.CellUnits;

    private static readonly Color[] ItemColors =
        { new(0.75f, 0.45f, 0.30f), new(0.95f, 0.70f, 0.30f), new(0.60f, 0.80f, 0.95f), new(0.35f, 0.35f, 0.40f) };

    private static readonly Color[] TierColors =
        { new(0.35f, 0.40f, 0.50f), new(0.20f, 0.55f, 0.60f), new(0.85f, 0.50f, 0.20f) };

    private record BuildItem(string Label, bool IsBelt, BeltTier Tier, int Machine);

    private ContentDb _content = null!;
    private SimWorld _world = null!;
    private BeltNetwork Net => _world.Network;

    // Rendering
    private Camera3D _camera = null!;
    private float _yaw = 0.2f, _distance = 15f;
    private Vector3 _target = new(7f, 0f, 2f);
    private MultiMeshInstance3D? _slabNode, _itemNode, _machineNode;
    private MultiMesh _items = null!;
    private MultiMesh? _machines;
    private float[] _itemBuf = Array.Empty<float>();
    private float[] _machineBuf = Array.Empty<float>();
    private ItemSample[] _samples = Array.Empty<ItemSample>();
    private int _sampleCount;
    private int[] _machineIds = Array.Empty<int>();
    private (float Ox, float Oy, float Oz, float Dx, float Dy, float Dz, int Length, int Speed)[] _segs = Array.Empty<(float, float, float, float, float, float, int, int)>();
    private int _structureVersion = -1;
    private readonly List<MeshInstance3D> _depositNodes = new();
    private MeshInstance3D _ghost = null!, _dragGhost = null!;
    private Label _hud = null!, _toastLabel = null!, _help = null!;

    // Interaction
    private readonly List<BuildItem> _palette = new();
    private int _selected;
    private int _cursorY;
    private (int X, int Y, int Z) _cursor;
    private Dir _dir = Dir.PosX;
    private (int X, int Y, int Z)? _dragStart;
    private int _selectedDeposit;
    private readonly Dictionary<int, int> _recipeChoice = new();
    private readonly List<(double Until, string Text)> _toasts = new();
    private int _speed = 1;
    private bool _paused;

    // Demo
    private bool _demo;
    private string? _shotPath;
    private int _demoSpeed = 30;
    private int _botStep;
    private long _botStartedAt;
    private bool _botDone;
    private bool _outOfRangeSeen;
    private (int X, int Y, int Z) _botIntent;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _demo = args.Contains("--demo");
        _shotPath = args.FirstOrDefault(a => a.StartsWith("--shot="))?["--shot=".Length..];
        string? sp = args.FirstOrDefault(a => a.StartsWith("--speed="));
        if (sp != null && int.TryParse(sp["--speed=".Length..], out int s)) _demoSpeed = Math.Clamp(s, 1, 200);

        string path = ProjectSettings.GlobalizePath("res://../content/core.json");
        _content = ContentDb.FromFile(path);
        _world = new SimWorld(_content, GameMode.Campaign);
        _world.Network.AddDeposit(_content.ItemIndex("core:iron_ore"), 4000, -7, 1, 3);
        _world.Network.AddDeposit(_content.ItemIndex("core:carbon"), 1500, -7, 1, -6);
        _world.Network.EnsurePlayer(Player); // the ship starts at the origin

        BuildPalette();
        BuildScene();
        RebuildGeometry();
        Toast("Mine ore with M, refine it with F, build with 1-9. Everything costs items.");
    }

    // ---- setup ---------------------------------------------------------------------------------

    private void BuildPalette()
    {
        _palette.Add(new BuildItem("Belt Mk1", true, BeltTier.Mk1, -1));
        _palette.Add(new BuildItem("Belt Mk2", true, BeltTier.Mk2, -1));
        _palette.Add(new BuildItem("Belt Mk3", true, BeltTier.Mk3, -1));
        foreach (var m in _content.Machines)
        {
            if (m.Role == MachineRole.Ship || m.SandboxOnly) continue;
            _palette.Add(new BuildItem(Nice(m.Id), false, BeltTier.Mk1, m.Index));
        }
    }

    private void BuildScene()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.03f, 0.04f, 0.08f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
                AmbientLightEnergy = 0.75f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.1f };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-55, -30, 0);

        _camera = new Camera3D { Far = 500f };
        AddChild(_camera);
        PlaceCamera();

        // A faint floor grid so the build layer is readable.
        var grid = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(80, 80) }, Position = new Vector3(20, -0.02f, 10) };
        grid.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.08f, 0.12f), Roughness = 1f };
        AddChild(grid);

        var ghostMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.95f, 0.4f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _ghost = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 0.96f }, MaterialOverride = ghostMat };
        _dragGhost = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = ghostMat, Visible = false };
        AddChild(_ghost);
        AddChild(_dragGhost);

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = MakeLabel(layer, new Vector2(12, 8));
        _toastLabel = MakeLabel(layer, new Vector2(12, 540));
        _help = MakeLabel(layer, new Vector2(12, 640));
        _help.Text = "LMB drag: belt   LMB: place   RMB: remove   1-9: build item   Tab: recipe   R: rotate   Z/X: layer\n" +
                     "WASD: fly   Space/C: up/down   wheel: zoom   M: mine   [ ]: deposit   F: refinery   T: research   P: pause   +/-: speed";

        // Deposits: spheres where the simulation says they are, shrinking as they run dry.
        for (int d = 0; d < Net.DepositCount; d++)
        {
            var color = Net.DepositItem(d) == _content.ItemIndex("core:iron_ore") ? new Color(0.65f, 0.4f, 0.3f) : new Color(0.25f, 0.25f, 0.28f);
            var (dx, dy, dz) = Net.DepositPosition(d);
            var n = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 1.5f, Height = 3f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f },
                Position = new Vector3(dx, dy, dz),
            };
            AddChild(n);
            _depositNodes.Add(n);
        }

        // The ship, with discs showing where it can build (big) and mine (small).
        _ship = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.35f, 1.3f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.9f, 0.95f), Roughness = 0.4f },
        };
        AddChild(_ship);
        _buildRange = RangeDisc(PlayerRules.BuildRangeCells, new Color(0.4f, 0.7f, 1f, 0.07f));
        _mineRange = RangeDisc(PlayerRules.MineRangeCells, new Color(1f, 0.6f, 0.2f, 0.12f));
    }

    private MeshInstance3D RangeDisc(float radius, Color color)
    {
        var disc = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f, RadialSegments = 64 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(disc);
        return disc;
    }

    /// <summary>Ship position in world coordinates, extrapolated by <paramref name="alpha"/> ticks of movement.</summary>
    private Vector3 ShipPosition(float alpha)
    {
        if (!Net.HasPlayer(Player)) return Vector3.Zero;
        var p = Net.GetPlayerInfo(Player);
        int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
        float speed = axes switch { 0 => 0, 1 => PlayerRules.MoveUnitsPerTick, 2 => PlayerRules.MoveUnitsPerTickDiagonal2, _ => PlayerRules.MoveUnitsPerTickDiagonal3 };
        return new Vector3(p.X + p.MoveX * speed * alpha, p.Y + p.MoveY * speed * alpha, p.Z + p.MoveZ * speed * alpha) / CellUnits;
    }

    private void UpdateShip(float alpha, float dt)
    {
        var pos = ShipPosition(_demo ? 0f : alpha);
        _ship.Position = pos;
        _buildRange.Position = pos;
        _mineRange.Position = pos;
        _target = _target.Lerp(pos, 1f - MathF.Exp(-6f * dt));
        PlaceCamera();

        if (_demo) return;
        // Held keys become movement intents; only changes are sent, so a held key is one command on press and one on release.
        int dx = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        int dz = (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
        int dy = (Input.IsKeyPressed(Key.Space) ? 1 : 0) - (Input.IsKeyPressed(Key.C) ? 1 : 0);
        if ((dx, dy, dz) != _lastIntent)
        {
            _lastIntent = (dx, dy, dz);
            Send(new SetMovement(dx, dy, dz));
        }
    }

    private MeshInstance3D _ship = null!, _buildRange = null!, _mineRange = null!;
    private (int X, int Y, int Z) _lastIntent;

    private static Label MakeLabel(CanvasLayer layer, Vector2 pos)
    {
        var l = new Label { Position = pos };
        l.AddThemeColorOverride("font_color", Colors.White);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 5);
        l.AddThemeFontSizeOverride("font_size", 15);
        layer.AddChild(l);
        return l;
    }

    private void PlaceCamera()
    {
        const float pitch = 0.95f; // radians above the horizon
        var offset = new Vector3(MathF.Sin(_yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(_yaw) * MathF.Cos(pitch)) * _distance;
        _camera.LookAtFromPosition(_target + offset, _target);
    }

    private static string Nice(string id) => id[(id.IndexOf(':') + 1)..].Replace('_', ' ');

    // ---- geometry ------------------------------------------------------------------------------

    private static Vector3 CellCenter(float x, float y, float z) => new(x + 0.5f, y + 0.5f, z + 0.5f);

    private static StandardMaterial3D VertexMaterial() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.7f };

    private static void Write(float[] b, int o, Vector3 scale, Vector3 pos, Color c)
    {
        b[o + 0] = scale.X; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = pos.X;
        b[o + 4] = 0; b[o + 5] = scale.Y; b[o + 6] = 0; b[o + 7] = pos.Y;
        b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = scale.Z; b[o + 11] = pos.Z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    private void RebuildGeometry()
    {
        _slabNode?.QueueFree();
        _itemNode?.QueueFree();
        _machineNode?.QueueFree();

        _segs = new (float, float, float, float, float, float, int, int)[Net.SegmentIdLimit];
        var alive = new List<int>();
        int capacity = 0;
        for (int id = 0; id < _segs.Length; id++)
        {
            if (!Net.IsAlive(id)) continue;
            var s = Net.GetSegmentInfo(id);
            _segs[id] = (s.OriginX, s.OriginY, s.OriginZ, s.DirX, s.DirY, s.DirZ, s.Length, s.Speed);
            alive.Add(id);
            capacity += s.Length / Units.ItemSpacing + 2;
        }

        var slabs = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexMaterial() }, InstanceCount = alive.Count,
        };
        var slabBuf = new float[alive.Count * FloatsPerItem];
        for (int i = 0; i < alive.Count; i++)
        {
            var s = Net.GetSegmentInfo(alive[i]);
            float len = s.LengthCells;
            var origin = CellCenter(s.OriginX, s.OriginY, s.OriginZ);
            var center = origin + new Vector3(s.DirX, s.DirY, s.DirZ) * (len - 1) / 2f;
            var scale = new Vector3(s.DirX != 0 ? len : 0.9f, s.DirY != 0 ? len : 0.2f, s.DirZ != 0 ? len : 0.9f);
            if (s.DirY == 0) center.Y = s.OriginY + 0.1f;
            Write(slabBuf, i * FloatsPerItem, scale, center, TierColors[(int)s.Tier]);
        }
        slabs.Buffer = slabBuf;
        _slabNode = new MultiMeshInstance3D { Multimesh = slabs };
        AddChild(_slabNode);

        _samples = new ItemSample[capacity];
        _items = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexMaterial() },
            InstanceCount = capacity, VisibleInstanceCount = 0,
        };
        _itemBuf = new float[capacity * FloatsPerItem];
        _itemNode = new MultiMeshInstance3D { Multimesh = _items };
        AddChild(_itemNode);

        _machineIds = Enumerable.Range(0, Net.MachineIdLimit).Where(Net.IsMachineAlive).ToArray();
        _machines = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = VertexMaterial() }, InstanceCount = _machineIds.Length,
        };
        _machineBuf = new float[_machineIds.Length * FloatsPerItem];
        _machineNode = new MultiMeshInstance3D { Multimesh = _machines };
        AddChild(_machineNode);

        _structureVersion = Net.StructureVersion;
    }

    private Color MachineColor(MachineInfo m, bool dim)
    {
        var role = _content.Machines[m.Type].Role;
        var c = role switch
        {
            MachineRole.Drill => new Color(0.85f, 0.55f, 0.20f),
            MachineRole.Lab => new Color(0.65f, 0.35f, 0.85f),
            MachineRole.Depot => new Color(0.30f, 0.75f, 0.45f),
            _ => _content.Machines[m.Type].Id == "core:smelter" ? new Color(0.80f, 0.25f, 0.20f) : new Color(0.25f, 0.45f, 0.85f),
        };
        float k = dim ? 0.45f : 1f;
        return new Color(c.R * k, c.G * k, c.B * k);
    }

    private void UpdateMachines()
    {
        if (_machines is null) return;
        for (int i = 0; i < _machineIds.Length; i++)
        {
            var m = Net.GetMachineInfo(_machineIds[i]);
            var role = _content.Machines[m.Type].Role;
            bool active = m.Crafting || (role == MachineRole.Lab && Net.CurrentResearch >= 0) || role == MachineRole.Depot;
            Write(_machineBuf, i * FloatsPerItem, Vector3.One * 0.9f, CellCenter(m.X, m.Y, m.Z), MachineColor(m, !active));
        }
        _machines.Buffer = _machineBuf;
    }

    private void UpdateDeposits()
    {
        for (int d = 0; d < _depositNodes.Count; d++)
        {
            float f = Net.DepositInitial(d) == 0 ? 0 : (float)Net.DepositRemaining(d) / Net.DepositInitial(d);
            _depositNodes[d].Scale = Vector3.One * Mathf.Max(0.15f, 0.35f + 0.65f * f);
            _depositNodes[d].Visible = Net.DepositRemaining(d) > 0;
        }
    }

    // ---- commands and results --------------------------------------------------------------------

    private void Send(Command c) => _world.Submit(Player, c);

    private void Toast(string text) => _toasts.Add((Time.GetTicksMsec() / 1000.0 + 6.0, text));

    private static string Why(Reject r) => r switch
    {
        Reject.NotEnoughItems => "not enough items in the hold",
        Reject.Locked => "locked: research it first",
        Reject.Overlap => "that cell is taken",
        Reject.OutOfRange => "too far from the ship",
        Reject.OutOfBounds => "out of bounds",
        Reject.UnknownDeposit => "no such deposit",
        Reject.Depleted => "that deposit is empty",
        Reject.ResearchUnavailable => "research not available",
        _ => r.ToString(),
    };

    private void CollectResults()
    {
        foreach (var r in _world.LastResults)
        {
            if (!r.Ok)
            {
                if (r.Reject == Reject.OutOfRange) _outOfRangeSeen = true;
                if (r.Command is PlaceSegment or PlaceMachine or RemoveSegment or RemoveMachine or StartMining or SetResearch or SetShipRecipe)
                    Toast($"{r.Command.GetType().Name}: {Why(r.Reject)}");
                continue;
            }
            if (r.Command is PlaceSegment && r.Created.Count == 1) AutoLinkSegment(r.Created[0]);
            else if (r.Command is PlaceMachine pm && r.CreatedMachines.Count == 1) AutoLinkMachine(r.CreatedMachines[0], pm);
        }
    }

    private static bool Touches((int X, int Y, int Z) a, (int X, int Y, int Z) b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) == 1;

    private static (int X, int Y, int Z) LastCell(SegmentInfo s) =>
        (s.OriginX + s.DirX * (s.LengthCells - 1), s.OriginY + s.DirY * (s.LengthCells - 1), s.OriginZ + s.DirZ * (s.LengthCells - 1));

    /// <summary>Links a freshly placed belt to whatever it touches, one input and one output at most. Each link is its own command.</summary>
    private void AutoLinkSegment(int id)
    {
        var n = Net.GetSegmentInfo(id);
        var origin = (n.OriginX, n.OriginY, n.OriginZ);
        var last = LastCell(n);
        bool haveIn = false, haveOut = false;

        for (int s = 0; s < Net.SegmentIdLimit; s++)
        {
            if (s == id || !Net.IsAlive(s)) continue;
            var si = Net.GetSegmentInfo(s);
            if (!haveIn && Touches(origin, LastCell(si)) && Net.OutputCount(s) == 0 && !Net.IsSink(s) && !Net.FeedsAnyMachine(s))
            {
                Send(new ConnectSegments(s, id));
                haveIn = true;
            }
            if (!haveOut && Touches((si.OriginX, si.OriginY, si.OriginZ), last) && Net.InputCount(s) == 0
                && !Net.IsFedByAnyMachine(s) && !Net.HasSource(s))
            {
                Send(new ConnectSegments(id, s));
                haveOut = true;
            }
        }

        for (int m = 0; m < Net.MachineIdLimit; m++)
        {
            if (!Net.IsMachineAlive(m)) continue;
            var mi = Net.GetMachineInfo(m);
            var role = _content.Machines[mi.Type].Role;
            var pos = (mi.X, mi.Y, mi.Z);
            if (!haveIn && role is MachineRole.Crafter or MachineRole.Drill && Touches(pos, origin) && Net.MachineOutputCount(m) < 2)
            {
                Send(new ConnectFromMachine(m, id));
                haveIn = true;
            }
            if (!haveOut && Touches(pos, last) && role != MachineRole.Drill)
            {
                Send(new ConnectToMachine(id, m));
                haveOut = true;
            }
        }
    }

    private void AutoLinkMachine(int machine, PlaceMachine pm)
    {
        var mi = Net.GetMachineInfo(machine);
        var role = _content.Machines[mi.Type].Role;
        var pos = (mi.X, mi.Y, mi.Z);

        if (role == MachineRole.Drill)
        {
            var item = _content.Recipes[pm.Recipe].Outputs[0].Item;
            int best = -1;
            for (int d = 0; d < Net.DepositCount; d++)
                if (Net.DepositItem(d) == item && (best < 0 || Net.DepositRemaining(d) > Net.DepositRemaining(best))) best = d;
            if (best >= 0) Send(new LinkDeposit(machine, best));
        }

        bool fed = false;
        int outs = 0;
        for (int s = 0; s < Net.SegmentIdLimit; s++)
        {
            if (!Net.IsAlive(s)) continue;
            var si = Net.GetSegmentInfo(s);
            if (!fed && role != MachineRole.Drill && Touches(pos, LastCell(si)) && Net.OutputCount(s) == 0
                && !Net.IsSink(s) && !Net.FeedsAnyMachine(s))
            {
                Send(new ConnectToMachine(s, machine));
                fed = true;
            }
            if (role is MachineRole.Crafter or MachineRole.Drill && outs < 2 && Touches(pos, (si.OriginX, si.OriginY, si.OriginZ))
                && Net.InputCount(s) == 0 && !Net.IsFedByAnyMachine(s) && !Net.HasSource(s))
            {
                Send(new ConnectFromMachine(machine, s));
                outs++;
            }
        }
    }

    private int ChosenRecipe(int machineType)
    {
        var options = _content.Recipes.Where(r => r.Machine == machineType && !Net.IsRecipeLocked(r.Index)).Select(r => r.Index).ToList();
        if (options.Count == 0) return -1;
        if (_recipeChoice.TryGetValue(machineType, out int cur) && options.Contains(cur)) return cur;
        return options[0];
    }

    private void CycleRecipe()
    {
        var item = _palette[_selected];
        if (item.IsBelt) return;
        var options = _content.Recipes.Where(r => r.Machine == item.Machine && !Net.IsRecipeLocked(r.Index)).Select(r => r.Index).ToList();
        if (options.Count < 2) { Toast("This machine has only one recipe available."); return; }
        int cur = ChosenRecipe(item.Machine);
        _recipeChoice[item.Machine] = options[(options.IndexOf(cur) + 1) % options.Count];
        Toast($"Recipe: {Nice(_content.Recipes[_recipeChoice[item.Machine]].Id)}");
    }

    private void PlaceSelectedAt((int X, int Y, int Z) cell, (int X, int Y, int Z)? dragStart)
    {
        var item = _palette[_selected];
        if (item.IsBelt)
        {
            var start = dragStart ?? cell;
            int dx = cell.X - start.X, dz = cell.Z - start.Z;
            Dir dir = _dir;
            int len = 1;
            if (Math.Abs(dx) >= Math.Abs(dz) && dx != 0) { dir = dx > 0 ? Dir.PosX : Dir.NegX; len = Math.Abs(dx) + 1; }
            else if (dz != 0) { dir = dz > 0 ? Dir.PosZ : Dir.NegZ; len = Math.Abs(dz) + 1; }
            Send(new PlaceSegment(start.X, start.Y, start.Z, dir, Math.Min(len, SimWorld.MaxSegmentCells), item.Tier));
        }
        else
        {
            int recipe = ChosenRecipe(item.Machine);
            var role = _content.Machines[item.Machine].Role;
            Send(new PlaceMachine(cell.X, cell.Y, cell.Z, item.Machine, role is MachineRole.Lab or MachineRole.Depot ? -1 : recipe));
        }
    }

    private void RemoveAt((int X, int Y, int Z) cell)
    {
        var (kind, id) = _world.EntityAt(cell.X, cell.Y, cell.Z);
        if (kind == SimWorld.EntityKind.Segment) Send(new RemoveSegment(id));
        else if (kind == SimWorld.EntityKind.Machine) Send(new RemoveMachine(id));
    }

    // ---- input ---------------------------------------------------------------------------------

    private (int X, int Y, int Z)? CellUnderMouse()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);
        if (MathF.Abs(dir.Y) < 1e-4f) return null;
        float planeY = _cursorY + 0.5f;
        float t = (planeY - origin.Y) / dir.Y;
        if (t < 0) return null;
        var hit = origin + dir * t;
        return ((int)MathF.Floor(hit.X), _cursorY, (int)MathF.Floor(hit.Z));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_demo) return;

        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _distance = Mathf.Max(6f, _distance * 0.9f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _distance = Mathf.Min(80f, _distance * 1.1f);
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) _dragStart = CellUnderMouse();
                else if (_dragStart is { } start && CellUnderMouse() is { } end)
                {
                    PlaceSelectedAt(end, _palette[_selected].IsBelt ? start : null);
                    _dragStart = null;
                }
                else _dragStart = null;
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && CellUnderMouse() is { } cell) RemoveAt(cell);
            PlaceCamera();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            int digit = (int)(k.Keycode - Key.Key1);
            if (digit >= 0 && digit < _palette.Count && digit < 9) _selected = digit;
            switch (k.Keycode)
            {
                case Key.Tab: CycleRecipe(); break;
                case Key.R: _dir = _dir switch { Dir.PosX => Dir.PosZ, Dir.PosZ => Dir.NegX, Dir.NegX => Dir.NegZ, _ => Dir.PosX }; Toast($"Single belts face {_dir}"); break;
                case Key.Z: _cursorY--; break;
                case Key.X: _cursorY++; break;
                case Key.M: ToggleMining(); break;
                case Key.Bracketleft: _selectedDeposit = (_selectedDeposit + Net.DepositCount - 1) % Net.DepositCount; break;
                case Key.Bracketright: _selectedDeposit = (_selectedDeposit + 1) % Net.DepositCount; break;
                case Key.F: ToggleRefinery(); break;
                case Key.T: CycleResearch(); break;
                case Key.P: _paused = !_paused; break;
                case Key.Equal: case Key.KpAdd: _speed = Math.Min(10, _speed + 1); break;
                case Key.Minus: case Key.KpSubtract: _speed = Math.Max(1, _speed - 1); break;
            }
        }
    }

    private void ToggleMining()
    {
        if (Net.MiningDeposit(Player) == _selectedDeposit) Send(new StopMining());
        else Send(new StartMining(_selectedDeposit));
    }

    private void ToggleRefinery()
    {
        int recipe = _content.RecipeIndex("core:ship_smelt");
        bool on = Net.HasPlayer(Player) && Net.GetPlayerInfo(Player).ShipRecipe == recipe;
        Send(new SetShipRecipe(on ? -1 : recipe));
    }

    private void CycleResearch()
    {
        var options = Enumerable.Range(0, _content.Research.Count).Where(Net.CanResearch).ToList();
        if (options.Count == 0) { Send(new SetResearch(-1)); Toast("Nothing left to research."); return; }
        int cur = Net.CurrentResearch;
        int next = cur < 0 ? options[0] : options[(options.IndexOf(cur) + 1) % options.Count];
        Send(new SetResearch(next));
    }

    // ---- frame loop ----------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_paused && !_demo) return;
        int ticks = _demo ? _demoSpeed : _speed;
        for (int i = 0; i < ticks; i++)
        {
            if (_demo) BotThink();
            _world.Step();
            CollectResults();
            if (_botDone) break;
        }
        if (Net.StructureVersion != _structureVersion) RebuildGeometry();
        _sampleCount = Net.FillSamples(_samples);
        if (_botDone && _finishCountdown == 0) _finishCountdown = 4; // let a few frames render before the screenshot
    }

    private int _finishCountdown;

    public override void _Process(double delta)
    {
        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        for (int i = 0; i < _sampleCount; i++)
        {
            var s = _samples[i];
            var g = _segs[s.Segment];
            float p = MathF.Min(s.Pos + g.Speed * alpha, g.Length) / CellUnits;
            var pos = new Vector3(g.Ox + 0.5f + g.Dx * (p - 0.5f), g.Oy + (g.Dy != 0 ? 0.5f + g.Dy * (p - 0.5f) : 0.3f), g.Oz + 0.5f + g.Dz * (p - 0.5f));
            Write(_itemBuf, i * FloatsPerItem, Vector3.One * 0.22f, pos, ItemColors[s.Type % ItemColors.Length]);
        }
        _items.VisibleInstanceCount = _sampleCount;
        _items.Buffer = _itemBuf;
        UpdateMachines();
        UpdateDeposits();
        UpdateShip(alpha, (float)delta);

        if (!_demo)
        {
            if (CellUnderMouse() is { } c)
            {
                _cursor = c;
                _ghost.Position = CellCenter(c.X, c.Y, c.Z);
                bool reachable = _world.InBuildRange(Player, c.X, c.Y, c.Z);
                ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = reachable ? new Color(1f, 0.95f, 0.4f, 0.35f) : new Color(1f, 0.25f, 0.2f, 0.35f);
            }
            UpdateDragGhost();
        }
        UpdateHud();
        if (_finishCountdown > 0 && --_finishCountdown == 0) FinishDemo();
    }

    private void UpdateDragGhost()
    {
        bool show = _dragStart is not null && _palette[_selected].IsBelt;
        _dragGhost.Visible = show;
        if (!show) return;
        var a = _dragStart!.Value;
        int dx = _cursor.X - a.X, dz = _cursor.Z - a.Z;
        bool alongX = Math.Abs(dx) >= Math.Abs(dz);
        int len = (alongX ? Math.Abs(dx) : Math.Abs(dz)) + 1;
        int sign = (alongX ? dx : dz) >= 0 ? 1 : -1;
        var from = CellCenter(a.X, a.Y, a.Z);
        _dragGhost.Position = from + (alongX ? new Vector3(sign * (len - 1) / 2f, 0, 0) : new Vector3(0, 0, sign * (len - 1) / 2f));
        _dragGhost.Scale = alongX ? new Vector3(len, 0.4f, 0.96f) : new Vector3(0.96f, 0.4f, len);
    }

    private string Items(ItemStack[] stacks) =>
        stacks.Length == 0 ? "free" : string.Join(", ", stacks.Select(s => $"{s.Count} {Nice(_content.Items[s.Item].Id)}"));

    private void UpdateHud()
    {
        var sb = new StringBuilder();
        int cap = PlayerRules.HoldCapacity;
        sb.Append($"HOLD {Net.HoldTotal(Player)}/{cap}:");
        foreach (var item in _content.Items)
        {
            int n = Net.HoldCount(Player, item.Index);
            if (n > 0) sb.Append($"  {Nice(item.Id)} {n}");
        }
        sb.Append('\n');

        string mining = Net.MiningDeposit(Player) >= 0
            ? $"mining deposit {Net.MiningDeposit(Player)} ({Nice(_content.Items[Net.DepositItem(Net.MiningDeposit(Player))].Id)}, {Net.DepositRemaining(Net.MiningDeposit(Player))} left)"
            : $"hands idle, deposit {_selectedDeposit} selected ({Nice(_content.Items[Net.DepositItem(_selectedDeposit)].Id)}, {Net.DepositRemaining(_selectedDeposit)} left)";
        string refinery = Net.HasPlayer(Player) && Net.GetPlayerInfo(Player).ShipRecipe >= 0
            ? (Net.GetPlayerInfo(Player).ShipCrafting ? "refinery working" : "refinery waiting for ore") : "refinery off";
        sb.Append($"{mining}   |   {refinery}   |   delivered by depots: {Net.DepotDelivered}").Append('\n');

        var ship = ShipPosition(0f);
        var dep = Net.DepositPosition(_selectedDeposit);
        float distance = (new Vector3(dep.X, dep.Y, dep.Z) - ship).Length();
        bool inRange = Net.InMineRange(Player, _selectedDeposit);
        sb.Append($"SHIP ({ship.X:F1}, {ship.Y:F1}, {ship.Z:F1})   deposit {_selectedDeposit} is {distance:F1} cells away (mining range {PlayerRules.MineRangeCells}): " +
                  $"{(inRange ? "in range" : "out of range, fly closer")}   build range {PlayerRules.BuildRangeCells}").Append('\n');

        if (Net.CurrentResearch >= 0)
        {
            var node = _content.Research[Net.CurrentResearch];
            sb.Append("RESEARCH " + Nice(node.Id) + ": " + string.Join(", ", node.Cost.Select(c =>
                $"{Net.ResearchProgress(node.Index, c.Item)}/{c.Count} {Nice(_content.Items[c.Item].Id)}")) + "   (build a lab and feed it)\n");
        }
        else sb.Append("RESEARCH none selected (T to choose). Done: " +
            (Net.ResearchedCount == 0 ? "nothing" : string.Join(", ", _content.Research.Where(r => Net.IsResearched(r.Index)).Select(r => Nice(r.Id)))) + "\n");

        sb.Append('\n');
        for (int i = 0; i < _palette.Count && i < 9; i++)
        {
            var b = _palette[i];
            bool locked = b.IsBelt ? Net.IsBeltLocked(b.Tier) : Net.IsMachineLocked(b.Machine);
            string cost = b.IsBelt ? Items(_content.Belts[(int)b.Tier]?.CostPerCell ?? Array.Empty<ItemStack>()) + " per cell" : Items(_content.Machines[b.Machine].Cost);
            string recipe = !b.IsBelt && ChosenRecipe(b.Machine) >= 0 ? $" [{Nice(_content.Recipes[ChosenRecipe(b.Machine)].Id)}]" : "";
            sb.Append($"{(i == _selected ? ">" : " ")} {i + 1}  {b.Label}{recipe}  ({cost}){(locked ? "  LOCKED" : "")}").Append('\n');
        }
        sb.Append($"layer {_cursorY}   cell ({_cursor.X}, {_cursor.Y}, {_cursor.Z})   speed x{(_demo ? _demoSpeed : _speed)}{(_paused ? "  PAUSED" : "")}");
        _hud.Text = sb.ToString();

        double now = Time.GetTicksMsec() / 1000.0;
        _toasts.RemoveAll(t => t.Until < now);
        _toastLabel.Text = string.Join("\n", _toasts.TakeLast(5).Select(t => t.Text));
    }

    // ---- demo bot ----------------------------------------------------------------------------------
    // A scripted player that uses the same commands as a human: mine, refine, build an ingot line with a depot,
    // build a plate line, swap its depot for a research lab, and research Logistics.

    private int Hold(string item) => Net.HoldCount(Player, _content.ItemIndex(item));
    private bool Affordable(params (string Item, int Count)[] cost) => cost.All(c => Hold(c.Item) >= c.Count);
    private bool CellFree(int x, int y, int z) => _world.EntityAt(x, y, z).Kind == SimWorld.EntityKind.None;

    private void BotThink()
    {
        if (_botDone) return;
        string ingot = "core:iron_ingot", plate = "core:iron_plate";
        int drillIron = _content.RecipeIndex("core:drill_iron"), smelt = _content.RecipeIndex("core:smelt_iron"), asm = _content.RecipeIndex("core:iron_plate");
        int drill = _content.MachineIndex("core:drill"), smelter = _content.MachineIndex("core:smelter"), assembler = _content.MachineIndex("core:assembler");
        int depot = _content.MachineIndex("core:depot"), lab = _content.MachineIndex("core:research_lab");
        int logistics = _content.ResearchIndex("core:logistics");

        bool Step(bool ready, Action act)
        {
            if (!ready) return false;
            act();
            _botStep++;
            return true;
        }

        if (_world.PendingCommands > 0) return; // one action at a time, so each result and auto-link is applied before the next
        switch (_botStep)
        {
            // Fly to the iron deposit, then mine it and start the refinery.
            case 0:
                if (!Net.InMineRange(Player, 0)) { BotSteer(Net.DepositPosition(0)); break; }
                Step(true, () =>
                {
                    _botIntent = (0, 0, 0);
                    Send(new SetMovement(0, 0, 0));
                    Send(new StartMining(0));
                    Send(new SetShipRecipe(_content.RecipeIndex("core:ship_smelt")));
                    _botStartedAt = _world.Tick;
                });
                break;
            // Line 1: iron drill -> smelter -> depot, delivering ingots into the hold.
            case 1: Step(Affordable((ingot, 6)), () => Send(new PlaceMachine(0, 0, 0, drill, drillIron))); break;
            // These two go through the same code as the mouse: a belt drag from cell 1 to 3, a machine click on the palette selection.
            case 2: Step(Affordable((ingot, 3)), () => { _selected = 0; PlaceSelectedAt((3, 0, 0), (1, 0, 0)); }); break;
            case 3: Step(Affordable((ingot, 8)), () => { _selected = _palette.FindIndex(b => b.Machine == smelter); PlaceSelectedAt((4, 0, 0), null); }); break;
            case 4: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(5, 0, 0, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 5:
                Step(Affordable((ingot, 5)), () =>
                {
                    Send(new PlaceMachine(8, 0, 0, depot, -1));
                    Send(new StopMining());
                    Send(new PlaceSegment(60, 0, 0, Dir.PosX, 2, BeltTier.Mk1)); // far from the ship: must be refused
                });
                break;
            // Line 2: drill -> smelter -> assembler -> depot, delivering plates.
            case 6: Step(Affordable((ingot, 6)), () => Send(new PlaceMachine(0, 0, 3, drill, drillIron))); break;
            case 7: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(1, 0, 3, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 8: Step(Affordable((ingot, 8)), () => Send(new PlaceMachine(4, 0, 3, smelter, smelt))); break;
            case 9: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(5, 0, 3, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 10: Step(Affordable((ingot, 12)), () => Send(new PlaceMachine(8, 0, 3, assembler, asm))); break;
            case 11: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(9, 0, 3, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 12: Step(Affordable((ingot, 5)), () => Send(new PlaceMachine(12, 0, 3, depot, -1))); break;
            // Plates reach the hold; swap the depot for a research lab, which needs 4 of them.
            case 13: Step(Affordable((plate, 4)), () => RemoveAt((12, 0, 3))); break;
            case 14: Step(CellFree(12, 0, 3) && Affordable((plate, 4), (ingot, 10)), () => Send(new PlaceMachine(12, 0, 3, lab, -1))); break;
            case 15: Step(true, () => Send(new SetResearch(logistics))); break;
            case 16: Step(Net.IsResearched(logistics), () => { _botDone = true; }); break;
        }
    }

    /// <summary>Flies the ship towards a cell (cells), sending a movement command only when the intent changes.</summary>
    private void BotSteer((int X, int Y, int Z) target)
    {
        var p = Net.GetPlayerInfo(Player);
        static int Axis(int diffUnits) => Math.Abs(diffUnits) > Units.CellUnits / 2 ? Math.Sign(diffUnits) : 0;
        var intent = (Axis(target.X * Units.CellUnits - p.X), Axis(target.Y * Units.CellUnits - p.Y), Axis(target.Z * Units.CellUnits - p.Z));
        if (intent == _botIntent) return;
        _botIntent = intent;
        Send(new SetMovement(intent.Item1, intent.Item2, intent.Item3));
    }

    private void FinishDemo()
    {
        var net = Net;
        double minutes = (_world.Tick - _botStartedAt) / (double)Units.TicksPerSecond / 60.0;
        GD.Print($"PLAY researched_logistics={net.IsResearched(_content.ResearchIndex("core:logistics"))} sim_minutes={minutes:F1} " +
                 $"belt_mk2_locked={net.IsBeltLocked(BeltTier.Mk2)} machines={net.MachineCount} segments={net.SegmentCount}");
        GD.Print($"PLAY mined_by_hand={net.MinedByHand} drilled={net.DrilledTotal} depot_delivered={net.DepotDelivered} research_items={net.ResearchItemsUsed}");
        GD.Print($"PLAY hold_ingots={Hold("core:iron_ingot")} hold_plates={Hold("core:iron_plate")} hold_total={net.HoldTotal(Player)}");
        var ship = net.GetPlayerInfo(Player);
        GD.Print($"PLAY ship_cells=({ship.X / (float)Units.CellUnits:F1}, {ship.Y / (float)Units.CellUnits:F1}, {ship.Z / (float)Units.CellUnits:F1}) out_of_range_seen={_outOfRangeSeen}");
        try { _world.ValidateInvariants(); GD.Print("PLAY invariants=ok"); }
        catch (Exception e) { GD.PrintErr("PLAY invariants=FAILED " + e.Message); }

        UpdateHud();
        if (_shotPath != null) GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        GetTree().Quit();
    }
}
