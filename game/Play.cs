using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

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
    private const ulong GalaxySeed = 20261120UL;
    private const int FloatsPerItem = 16;
    private const float CellUnits = Units.CellUnits;

    private static readonly Color[] ItemColors =
        { new(0.75f, 0.45f, 0.30f), new(0.95f, 0.70f, 0.30f), new(0.60f, 0.80f, 0.95f), new(0.35f, 0.35f, 0.40f) };

    private static readonly Color[] TierColors =
        { new(0.35f, 0.40f, 0.50f), new(0.20f, 0.55f, 0.60f), new(0.85f, 0.50f, 0.20f) };

    private record BuildItem(string Label, bool IsBelt, BeltTier Tier, int Machine, bool IsModule = false, bool IsStation = false);

    private ContentDb _content = null!;
    private SimWorld _world = null!;
    private BeltNetwork Net => _world.Network;

    // Rendering
    private Camera3D _camera = null!;
    // First person: the camera sits in the cockpit and looks along yaw/pitch (radians). Yaw 0 looks along -Z; the start faces +X.
    private float _yaw = -MathF.PI / 2f, _pitch = -0.3f;
    // Only the scripted modes (--demo, --stress) use an overview camera that follows the base from above; normal play never does.
    private float _overviewDistance = DefaultDistance;
    private Vector3 _overviewTarget = new(7f, 0f, 2f);
    private MeshInstance3D _grid = null!;
    private bool Overview => _demo || _stressMachines > 0 || _structureDemo;
    private const float CockpitFov = 80f;
    private const uint ShipLayer = 1u << 1; // render layer 2: the ship model, hidden from the cockpit camera
    private static readonly Vector3 CockpitOffset = new(0f, 1.8f, 0f); // eye height above the ship: near cells stay small enough to aim at
    // Structures: the pilot station inside the cockpit module, in cells from the ship structure origin (the seat model is at z 4.6; the eye
    // stands just behind the seat, so the workshop stays in view when looking aft). The eye sits about 1.7 above the
    // workshop build layer (y = 1), as in the legacy view, so near cells stay small enough to aim at.
    private static readonly Vector3 SeatOffset = new(4f, 2.7f, 6.6f);
    private const int DefaultStructureLayer = 1; // the workshop machines of the starter ship sit in local layer 1
    private readonly Dictionary<int, Node3D> _machineModels = new();
    private double _animTime;
    private ItemSample[] _samples = Array.Empty<ItemSample>();
    private int _sampleCount;
    private int[] _machineIds = Array.Empty<int>();
    private (float Ox, float Oy, float Oz, float Dx, float Dy, float Dz, int Length, int Speed)[] _segs = Array.Empty<(float, float, float, float, float, float, int, int)>();
    private int _structureVersion = -1;

    /// <summary>
    /// What is drawn for one structure: slabs, the item MultiMesh and the machine models, all children of the structure's root
    /// node (structure 0 is the world root), so a flying ship carries its factory and item positions are local to it.
    /// </summary>
    private sealed class Geo
    {
        public Node3D Machines = null!;
        public MultiMeshInstance3D? Slabs, ItemNode;
        public MultiMesh Items = null!;
        public float[] Buf = Array.Empty<float>();
        public int Count, Capacity;
    }

    private readonly Dictionary<int, Geo> _geos = new();
    private Geo?[] _segGeo = Array.Empty<Geo?>(); // by segment id
    private readonly List<Node3D> _depositNodes = new();
    private MeshInstance3D _ghost = null!, _dragGhost = null!;

    // Interaction
    private readonly List<BuildItem> _palette = new();
    private int _selected;
    private int _cursorY;
    private Cell _cursor;
    private Dir _dir = Dir.PosX;
    private Cell? _dragStart;
    private Cell? _beltAnchor; // start of a belt line begun with a click, waiting for the second click
    private int _selectedDeposit;
    private readonly Dictionary<int, int> _recipeChoice = new();
    private readonly ToastQueue _toasts = new();
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
    private int _scansDone, _depositsRevealed, _jumpsStarted;
    private (int X, int Y, int Z) _botIntent;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _threatDemo = args.Contains("--threat-demo");
        _demo = args.Contains("--demo") || args.Contains("--explore-demo") || _threatDemo;
        _shotPath = args.FirstOrDefault(a => a.StartsWith("--shot="))?["--shot=".Length..];
        string? sp = args.FirstOrDefault(a => a.StartsWith("--speed="));
        if (sp != null && int.TryParse(sp["--speed=".Length..], out int s)) _demoSpeed = Math.Clamp(s, 1, 200);

        string path = ProjectSettings.GlobalizePath("res://../content/core.json");
        _content = ContentDb.FromFile(path);
        _exploreDemo = args.Contains("--explore-demo");
        _saveTest = args.Contains("--save-test");
        string? st = args.FirstOrDefault(a => a.StartsWith("--stress="));
        _stressActive = args.Contains("--stress-active");
        _stressClose = args.Contains("--stress-close");
        if (st is not null && int.TryParse(st["--stress=".Length..], out int sn)) _stressMachines = Math.Clamp(sn, 1, 5000);
        // A generated galaxy: the same seed always gives the same systems. Home's deposits start revealed; scan to find the rest.
        // The Remnant are on in the game and the threat demo; the production and exploration demos run without them.
        _structuresShotPath = args.FirstOrDefault(a => a.StartsWith("--structures-shot="))?["--structures-shot=".Length..];
        _structuresShot = _structuresShotPath is not null;
        _walkShotPath = args.FirstOrDefault(a => a.StartsWith("--walk-shot="))?["--walk-shot=".Length..];
        _walkShot = _walkShotPath is not null;
        bool threats = !(args.Contains("--demo") || _exploreDemo) && _stressMachines == 0 && !((_structuresShot || _walkShot) && !args.Contains("--threats"));
        _stationDemo = args.Contains("--station-demo");
        // --station-demo: a sandbox world with the station rules on and a scripted station, for screenshots of the build rules.
        _gameMode = _exploreDemo || _threatDemo || _stressMachines > 0 || (_stationDemo && !args.Contains("--station-campaign")) || args.Contains("--structure-demo")
            || args.Contains("--shot-sandbox") ? GameMode.Sandbox : GameMode.Campaign;
        _threatsOn = threats;
        // The campaign builds only inside station modules; the scripted demos and the stress test keep the free build, and
        // --station-rules=false starts a campaign with the old rules for comparison.
        // The campaign starts in the starter ship (structures); --structures=false falls back to the station rules. Sandbox, the
        // demos and the stress test keep the free build. Structures win over the station rules.
        _structuresOn = !_demo && _stressMachines == 0 && !_stationDemo && !args.Contains("--structure-demo") && !args.Contains("--structures=false")
            && (_gameMode == GameMode.Campaign || _structuresShot) && _content.StarterShip is not null;
        _stationRules = !_structuresOn && (_stationDemo
            || (_gameMode == GameMode.Campaign && !_demo && _stressMachines == 0 && !args.Contains("--station-rules=false")));
        _world = SimWorld.CreateGalaxy(_content, _gameMode, GalaxySeed, threats, _stationRules, _structuresOn);
        if (_structuresOn) { _yaw = MathF.PI; _pitch = -0.35f; _cursorY = DefaultStructureLayer; } // face the workshop from the pilot seat
        _world.Network.EnsurePlayer(Player); // the ship starts at the origin, inside Home
        if (args.Contains("--structure-demo")) BuildStructureDemo(args);
        // For screenshots: finish every research that carries a bonus, so the upgrade line of the status panel shows.
        if (args.Contains("--grant-upgrades"))
            foreach (var node in _content.Research.Where(r => r.Bonuses.Length > 0)) _world.Network.GrantResearch(node.Index);
        if (_stressMachines > 0) BuildStressBase();
        if (_stationDemo) BuildStationDemo(args);
        _selectedDeposit = RevealedDeposits().FirstOrDefault();

        BuildPalette();
        BuildScene();
        BuildUi();
        BuildThreatVisuals();
        RebuildGeometry();
        if (_stationDemo) StationDemoSelect(args);
        if (!_demo && !_stationDemo && !_structuresShot) StartPlayLog();
        if (!_demo) Toast(L("toast.welcome"));
        if (_structuresShot) StructuresShotSetup(args);
    }

    private bool _structuresOn;
    private bool _structuresShot;
    private string? _structuresShotPath;

    /// <summary>The ship structure the player flies and works in, or -1 (legacy world or no ship).</summary>
    private int ShipStruct => _structuresOn ? Net.ShipStructure(Player) : -1;

    /// <summary>World origin of a structure in cells: the live root position (the ship's is interpolated per frame), 0 for structure 0.</summary>
    private Vector3 StructOrigin(int s) => s == 0 ? Vector3.Zero : _stationView.StructureRoot(s)?.Position ?? StationView.OriginCells(Net, s);

    // ---- setup ---------------------------------------------------------------------------------

    private void BuildPalette()
    {
        _palette.Add(new BuildItem("Belt Mk1", true, BeltTier.Mk1, -1));
        _palette.Add(new BuildItem("Belt Mk2", true, BeltTier.Mk2, -1));
        _palette.Add(new BuildItem("Belt Mk3", true, BeltTier.Mk3, -1));
        foreach (var m in _content.Machines)
        {
            if (m.Role == MachineRole.Ship || m.SandboxOnly) continue;
            if ((_stationRules || _structuresOn) && m.Role == MachineRole.Drill) continue; // drills are always rejected under the station and structure rules
            _palette.Add(new BuildItem(Nice(m.Id), false, BeltTier.Mk1, m.Index));
        }
        // The station module is the last entry and has its own key (N), so it never shifts the number keys.
        if (_stationRules && _content.Modules is not null) _palette.Add(new BuildItem("Module", false, BeltTier.Mk1, -1, IsModule: true));
        // Structures: the module tool (N picks it, then cycles the kind) and the found-a-station tool (I), after the numbered entries.
        if (_structuresOn)
        {
            _palette.Add(new BuildItem("Module", false, BeltTier.Mk1, -1, IsModule: true));
            _palette.Add(new BuildItem("Station", false, BeltTier.Mk1, -1, IsStation: true));
        }
    }

    // ---- station modules -------------------------------------------------------------------------

    private bool _stationRules;
    private bool _stationDemo;
    private StationView _stationView = null!;
    private (int Mx, int My, int Mz)? _moduleAim;
    private ModuleKind _moduleKind = ModuleKind.Workshop; // the kind the module tool places (N cycles it)

    private bool ModuleSelected => _tool == ToolMode.Build && _palette[_selected].IsModule;

    /// <summary>
    /// The module a module ghost snaps to: with no station, the module under the aim cell; with one, the aim cell's module
    /// when it touches the station, otherwise the free face-adjacent module whose centre is nearest the aim ray.
    /// </summary>
    private (int Mx, int My, int Mz)? ModuleUnderAim()
    {
        var origin = AimOrigin();
        var dir = AimDirection();
        var cell = CellUnderAim();
        (int Mx, int My, int Mz)? hit = cell is { } c ? StationGrid.ModuleOfCell(c.X, c.Y, c.Z) : null;
        if (Net.ModuleCount(Player) == 0) return hit is { } h && StationGrid.IsValidModule(h.Mx, h.My, h.Mz) ? hit : null;
        if (hit is { } m && !Net.HasModule(Player, m.Mx, m.My, m.Mz) && Net.ModuleTouchesStation(Player, m.Mx, m.My, m.Mz)) return m;

        Vector3? planeHit = cell is not null ? origin + dir * ((_cursorY - origin.Y) / dir.Y) : null;
        (int, int, int)? best = null;
        float bestD = float.MaxValue;
        foreach (var (mx, my, mz, _) in Net.Modules(Player))
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                int nx = mx + dx, ny = my + dy, nz = mz + dz;
                if (Net.HasModule(Player, nx, ny, nz) || !StationGrid.IsValidModule(nx, ny, nz)) continue;
                var centre = StationView.Centre(nx, ny, nz);
                float d;
                if (planeHit is { } ph) d = (centre - ph).LengthSquared(); // nearest to where the aim meets the build layer
                else
                {
                    // Aim not on the layer (looking up or too far): nearest to the ray itself.
                    var to = centre - origin;
                    float along = to.Dot(dir);
                    if (along <= 0f) continue;
                    d = (to - dir * along).LengthSquared();
                }
                if (d < bestD) { bestD = d; best = (nx, ny, nz); }
            }
        return best is { } b ? (b.Item1, b.Item2, b.Item3) : null;
    }

    /// <summary>Why <see cref="PlaceModule"/> would be refused for this module right now (the sim's own checks, as text), or null if it would succeed.</summary>
    private string? ModuleProblem((int Mx, int My, int Mz) m)
    {
        if (!StationGrid.IsValidModule(m.Mx, m.My, m.Mz)) return Why(Reject.OutOfBounds);
        if (Net.HasModule(Player, m.Mx, m.My, m.Mz)) return L("why.moduleExists");
        if (Net.ModuleCount(Player) > 0 && !Net.ModuleTouchesStation(Player, m.Mx, m.My, m.Mz)) return Why(Reject.NotAdjacent);
        if (_gameMode == GameMode.Campaign)
        {
            if (!Net.ModuleCentreInRange(Player, m.Mx, m.My, m.Mz, PlayerRules.BuildRangeCells)) return Why(Reject.OutOfRange);
            var cost = Net.NextModuleCost(Player);
            if (!Net.CanPay(Player, cost))
            {
                var missing = cost.Where(s => Net.HoldCount(Player, s.Item) < s.Count)
                    .Select(s => new ItemStack(s.Item, s.Count - Net.HoldCount(Player, s.Item))).ToArray();
                return L("why.moduleMissing", Stacks(missing));
            }
        }
        return null;
    }

    private string ModuleName() => _structuresOn ? L("module." + _moduleKind.ToString().ToLowerInvariant()) : Net.ModuleCount(Player) == 0 ? L("ui.core") : L("ui.module");

    /// <summary>A scripted station for screenshots: a core, a hull module beside it and a few machines and belts inside.</summary>
    private void BuildStationDemo(string[] args)
    {
        bool campaign = _gameMode == GameMode.Campaign; // --station-campaign: costs apply, so the station is added without commands
        foreach (var (mx, my, mz) in new[] { (2, 0, 0), (3, 0, 0), (3, 1, 0) })
        {
            if (campaign) Net.AddModuleUnchecked(Player, mx, my, mz);
            else _world.Submit(Player, new PlaceModule(mx, my, mz));
        }
        _world.Step();
        int Machine(string id) => _content.MachineIndex(id);
        int Recipe(int machine) => _content.Recipes.FirstOrDefault(r => r.Machine == machine)?.Index ?? -1;
        foreach (var (id, x, y, z) in new[] { ("core:smelter", 18, 0, 1), ("core:assembler", 21, 0, 1), ("core:unloader", 17, 0, 2), ("core:depot", 25, 0, 2) })
        {
            int mt = Machine(id);
            var role = _content.Machines[mt].Role;
            if (campaign) continue; // machines only in the sandbox variant
            _world.Submit(Player, new PlaceMachine(x, y, z, mt, role is MachineRole.Lab or MachineRole.Depot or MachineRole.Unloader ? -1 : Recipe(mt)));
        }
        if (!campaign) _world.Submit(Player, new PlaceSegment(19, 0, 1, Dir.PosX, 2, BeltTier.Mk1));
        _world.Step();
        _yaw = -MathF.PI / 2f - 0.12f;
        _pitch = -0.12f;
        if (args.Contains("--station-high"))
        {
            // Rise above the floor and look down, to see the floor plates and their grid.
            _world.Submit(Player, new SetMovement(0, 1, 0));
            for (int i = 0; i < 25; i++) _world.Step();
            _world.Submit(Player, new SetMovement(0, 0, 0));
            _world.Step();
            _pitch = -0.55f;
        }
    }

    /// <summary>--station-select=module|N picks the build tool of a station demo (default: the module).</summary>
    private void StationDemoSelect(string[] args)
    {
        string? sel = args.FirstOrDefault(a => a.StartsWith("--station-select="))?["--station-select=".Length..];
        int index = sel is null || sel == "module" ? _palette.FindIndex(b => b.IsModule) : int.TryParse(sel, out int n) ? n - 1 : -1;
        if (index >= 0) SelectBuild(index);
    }

    private void BuildScene()
    {
        Ambience.Build(this, Ambience.SystemSeed(GalaxySeed, Net.CurrentSystem(Player))); // sky, sun light and dust

        _camera = new Camera3D { Far = 500f, Fov = Overview ? 75f : CockpitFov, CullMask = Overview ? 0xFFFFFu : 0xFFFFFu & ~ShipLayer };
        AddChild(_camera);
        PlaceCamera(ShipPosition(0f));

        // A faint floor so the build layer is readable; it follows the ship so the ground never ends under the cockpit.
        var grid = _grid = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, Position = new Vector3(20, -0.02f, 10) };
        // Unlit dark floor with faint cell lines (world-space, so the lines stay put while the plane follows the ship) fading into the distance.
        var floorShader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 g = abs(fract(wpos.xz - 0.5) - 0.5) / fwidth(wpos.xz);
    float line = 1.0 - min(min(g.x, g.y), 1.0);
    float fade = 1.0 - smoothstep(25.0, 70.0, distance(wpos.xz, CAMERA_POSITION_WORLD.xz));
    vec3 col = mix(vec3(0.035, 0.04, 0.06), vec3(0.18, 0.24, 0.36), line * fade * 0.8);
    ALBEDO = col;
}",
        };
        grid.MaterialOverride = new ShaderMaterial { Shader = floorShader };
        AddChild(grid);

        _stationView = new StationView();
        AddChild(_stationView);

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

        // Deposits: spheres where the simulation says they are, shrinking as they run dry.
        for (int d = 0; d < Net.DepositCount; d++)
        {
            var color = Net.DepositItem(d) == _content.ItemIndex("core:iron_ore") ? new Color(0.65f, 0.4f, 0.3f) : new Color(0.25f, 0.25f, 0.28f);
            var (dx, dy, dz) = Net.DepositPosition(d);
            var n = Models.Deposit(color, d);
            n.Position = new Vector3(dx, dy, dz);
            AddChild(n);
            _depositNodes.Add(n);
        }

        // The ship, with discs showing where it can build (big) and mine (small).
        _ship = Models.Ship();
        if (!Overview) SetLayers(_ship, ShipLayer);
        AddChild(_ship);
        _buildRange = RangeDisc(PlayerRules.BuildRangeCells, new Color(0.4f, 0.7f, 1f, 0.07f));
        _mineRange = RangeDisc(PlayerRules.MineRangeCells, new Color(1f, 0.6f, 0.2f, 0.12f));
    }

    private MeshInstance3D RangeDisc(float radius, Color color)
    {
        var disc = new MeshInstance3D
        {
            // A thin ring on the ground: a filled translucent disc washed out the whole floor seen from the cockpit.
            Mesh = new TorusMesh { InnerRadius = radius - 0.12f, OuterRadius = radius, Rings = 96, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(color.R, color.G, color.B, 1f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Scale = new Vector3(1f, 0.2f, 1f),
        };
        AddChild(disc);
        return disc;
    }

    /// <summary>Ship position in world coordinates, extrapolated by <paramref name="alpha"/> ticks of movement.</summary>
    private Vector3 ShipPosition(float alpha)
    {
        if (!Net.HasPlayer(Player)) return Vector3.Zero;
        var p = Net.GetPlayerInfo(Player);
        // With structures the engines set the speed (a share of the base speed); without a ship structure it is the base speed.
        float engine = ShipStruct >= 0 ? Net.ShipSpeedUnitsPerTick(Player) / (float)PlayerRules.MoveUnitsPerTick : 1f;
        if ((p.ThrustX | p.ThrustY | p.ThrustZ) != 0)
        {
            float step = PlayerRules.MoveUnitsPerTick * engine * alpha / 1024f;
            return new Vector3(p.X + p.ThrustX * step, p.Y + p.ThrustY * step, p.Z + p.ThrustZ * step) / CellUnits;
        }
        int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
        float speed = engine * axes switch { 0 => 0, 1 => PlayerRules.MoveUnitsPerTick, 2 => PlayerRules.MoveUnitsPerTickDiagonal2, _ => PlayerRules.MoveUnitsPerTickDiagonal3 };
        return new Vector3(p.X + p.MoveX * speed * alpha, p.Y + p.MoveY * speed * alpha, p.Z + p.MoveZ * speed * alpha) / CellUnits;
    }

    private static void SetLayers(Node node, uint layers)
    {
        if (node is VisualInstance3D v) v.Layers = layers;
        foreach (var c in node.GetChildren()) SetLayers(c, layers);
    }

    /// <summary>Keeps every structure root at its world origin; the player's ship structure follows the interpolated ship position and lights its engines.</summary>
    private void UpdateStructureRoots(float alpha)
    {
        if (!_structuresOn) return;
        _stationView.SyncStructures(Net);
        int ship = ShipStruct;
        if (ship < 0 || !Net.HasPlayer(Player)) return;
        if (_stationView.StructureRoot(ship) is { } root) root.Position = ShipPosition(_demo ? 0f : alpha);
        var pi = Net.GetPlayerInfo(Player);
        bool thrusting = Net.CanFly(Player) && (pi.MoveX | pi.MoveY | pi.MoveZ | pi.ThrustX | pi.ThrustY | pi.ThrustZ) != 0;
        _stationView.SetEngines(ship, thrusting);
    }

    private void UpdateShip(float alpha, float dt)
    {
        var pos = ShipPosition(_demo ? 0f : alpha);
        // The nose points where the ship is heading; it eases round and holds its heading while parked.
        if (Net.HasPlayer(Player))
        {
            var pi = Net.GetPlayerInfo(Player);
            int hx = pi.MoveX + pi.ThrustX, hz = pi.MoveZ + pi.ThrustZ; // only one of the two intents is ever non-zero
            if (hx != 0 || hz != 0)
                _shipYaw = Mathf.LerpAngle(_shipYaw, MathF.Atan2(-hz, hx), 1f - MathF.Exp(-10f * dt));
            bool thrusting = (pi.MoveX | pi.MoveY | pi.MoveZ | pi.ThrustX | pi.ThrustY | pi.ThrustZ) != 0;
            Models.Animate(_ship, _animTime, thrusting);
            Models.SetDamage(_ship, 0f);
        }
        _ship.Rotation = new Vector3(0, _shipYaw, 0);
        _ship.Position = pos;
        // With structures the ship structure is the ship; the small legacy model stays hidden.
        _ship.Visible = !_structuresOn && !(Net.HasPlayer(Player) && Net.GetPlayerInfo(Player).RespawnRemaining > 0); // hidden while being rebuilt
        Ambience.Retint(Ambience.SystemSeed(GalaxySeed, Net.CurrentSystem(Player))); // no-op unless the system changed
        // The range discs lie on the build plane (a disc at the ship's own height would be edge-on to the cockpit camera).
        // With structures the layer is local to the ship, so its world height is the ship's origin plus the layer.
        float layerWorldY = _cursorY + (ShipStruct >= 0 ? pos.Y : 0f);
        float floorY = layerWorldY + 0.02f;
        float dy = Math.Abs(pos.Y - (layerWorldY + 0.5f));
        foreach (var (disc, radius) in new[] { (_buildRange, (float)PlayerRules.BuildRangeCells), (_mineRange, (float)PlayerRules.MineRangeCells) })
        {
            float r = MathF.Sqrt(MathF.Max(0.01f, radius * radius - dy * dy)) / radius;
            disc.Position = new Vector3(pos.X, Overview ? pos.Y : floorY, pos.Z);
            disc.Scale = new Vector3(r, 0.2f, r);
        }
        _grid.Position = new Vector3(pos.X, -0.02f, pos.Z);
        if (Overview)
        {
            // After a jump the camera snaps to the ship instead of flying hundreds of cells.
            var focus = _structureDemo ? _demoFocus : pos;
            // With a deposit chosen close by, look between the ship and the deposit so what you build around it stays on screen.
            if (Net.IsDeposit(_selectedDeposit) && Net.IsDepositRevealed(_selectedDeposit))
            {
                var dp = Net.DepositPosition(_selectedDeposit);
                var dep = new Vector3(dp.X, dp.Y, dp.Z);
                if ((dep - pos).LengthSquared() < 40f * 40f) focus = pos.Lerp(dep, 0.5f);
            }
            _overviewTarget = (_overviewTarget - focus).LengthSquared() > 100f * 100f ? focus : _overviewTarget.Lerp(focus, 1f - MathF.Exp(-6f * dt));
        }
        PlaceCamera(pos);
        UpdateShipControl();
    }

    private Node3D _ship = null!;
    private MeshInstance3D _buildRange = null!, _mineRange = null!;
    private float _shipYaw;

    /// <summary>Unit view direction from the cockpit yaw and pitch (Godot convention: yaw 0 looks along -Z).</summary>
    private Vector3 CockpitForward() =>
        new(-MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), -MathF.Cos(_yaw) * MathF.Cos(_pitch));

    private Vector3 CockpitRight() => new(MathF.Cos(_yaw), 0f, -MathF.Sin(_yaw));

    private void PlaceCamera(Vector3 shipPos)
    {
        if (Overview)
        {
            float yaw = _structureDemo ? _demoYaw : 0.2f, pitch = _structureDemo ? _demoPitch : 1.15f; // fixed look, for the scripted modes only
            var offset = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * _overviewDistance;
            _camera.LookAtFromPosition(_overviewTarget + offset, _overviewTarget);
            return;
        }
        if (_shotOutside && ShipStruct >= 0)
        {
            // --structures-shot: a view from outside the ship, looking at its middle (the ship is 40 cells long, the workshop is in the middle).
            var mid = shipPos + new Vector3(4f, 1.5f, 17f);
            _camera.LookAtFromPosition(mid + new Vector3(30f, 15f, 14f), mid);
            return;
        }
        _camera.Position = shipPos + (ShipStruct >= 0 ? EyeLocal() : CockpitOffset);
        _camera.Rotation = new Vector3(_pitch, _yaw, 0f);
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
        if (_structuresOn) _stationView.SyncStructures(Net); // makes sure every structure has its root before children are added
        foreach (var old in _geos.Values)
        {
            old.Slabs?.QueueFree();
            old.ItemNode?.QueueFree();
            old.Machines.QueueFree();
        }
        _geos.Clear();

        Node3D ParentOf(int structure) => structure == 0 ? this : _stationView.StructureRoot(structure) ?? this;
        Geo GeoFor(int structure)
        {
            if (_geos.TryGetValue(structure, out var g)) return g;
            g = new Geo { Machines = new Node3D() };
            ParentOf(structure).AddChild(g.Machines);
            _geos[structure] = g;
            return g;
        }
        GeoFor(0);
        foreach (var st in Net.Structures()) GeoFor(st.Id);

        _segs = new (float, float, float, float, float, float, int, int)[Net.SegmentIdLimit];
        _segGeo = new Geo?[Net.SegmentIdLimit];
        var alive = new Dictionary<int, List<int>>();
        for (int id = 0; id < _segs.Length; id++)
        {
            if (!Net.IsAlive(id)) continue;
            var s = Net.GetSegmentInfo(id);
            _segs[id] = (s.OriginX, s.OriginY, s.OriginZ, s.DirX, s.DirY, s.DirZ, s.Length, s.Speed);
            var geo = GeoFor(s.Structure);
            _segGeo[id] = geo;
            geo.Capacity += s.Length / Units.ItemSpacing + 2;
            if (!alive.TryGetValue(s.Structure, out var list)) alive[s.Structure] = list = new List<int>();
            list.Add(id);
        }

        int total = 0;
        foreach (var (structure, geo) in _geos)
        {
            var ids = alive.TryGetValue(structure, out var l) ? l : new List<int>();
            var parent = ParentOf(structure);

            var slabs = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new BoxMesh { Size = Vector3.One, Material = VertexMaterial() }, InstanceCount = ids.Count,
            };
            var slabBuf = new float[ids.Count * FloatsPerItem];
            for (int i = 0; i < ids.Count; i++)
            {
                var s = Net.GetSegmentInfo(ids[i]);
                float len = s.LengthCells;
                var origin = CellCenter(s.OriginX, s.OriginY, s.OriginZ);
                var center = origin + new Vector3(s.DirX, s.DirY, s.DirZ) * (len - 1) / 2f;
                var scale = new Vector3(s.DirX != 0 ? len : 0.9f, s.DirY != 0 ? len : 0.2f, s.DirZ != 0 ? len : 0.9f);
                if (s.DirY == 0) center.Y = s.OriginY + 0.1f;
                Write(slabBuf, i * FloatsPerItem, scale, center, TierColors[(int)s.Tier]);
            }
            slabs.Buffer = slabBuf;
            geo.Slabs = new MultiMeshInstance3D { Multimesh = slabs };
            parent.AddChild(geo.Slabs);

            geo.Items = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new BoxMesh { Size = Vector3.One, Material = VertexMaterial() },
                InstanceCount = geo.Capacity, VisibleInstanceCount = 0,
            };
            geo.Buf = new float[geo.Capacity * FloatsPerItem];
            geo.Count = 0;
            geo.ItemNode = new MultiMeshInstance3D { Multimesh = geo.Items };
            parent.AddChild(geo.ItemNode);
            total += geo.Capacity;
        }
        _samples = new ItemSample[total];

        _machineIds = Enumerable.Range(0, Net.MachineIdLimit).Where(Net.IsMachineAlive).ToArray();
        _machineModels.Clear();
        foreach (int id in _machineIds)
        {
            var m = Net.GetMachineInfo(id);
            var model = Models.Machine(_content.Machines[m.Type].Id);
            model.Position = CellCenter(m.X, m.Y, m.Z); // local to the structure's root
            GeoFor(m.Structure).Machines.AddChild(model);
            _machineModels[id] = model;
        }

        _structureVersion = Net.StructureVersion;
    }

    /// <summary>True unless the point is more than a cell and a half outside one of the frustum planes (their normals point outwards).</summary>
    private static bool InView(Plane[] planes, Vector3 p)
    {
        foreach (var plane in planes)
            if (plane.DistanceTo(p) > 1.5f) return false;
        return true;
    }

    private int _machinesInView;

    private void UpdateMachines()
    {
        if (_geos.Count == 0) return;
        var planes = _camera.GetFrustum().ToArray();
        int visible = 0;
        var enemies = _content.Threat is null ? null : Net.Enemies();
        foreach (int id in _machineIds)
        {
            if (!_machineModels.TryGetValue(id, out var model) || !Net.IsMachineAlive(id)) continue;
            // Machines outside the camera's view are not animated: nobody sees them, and a big base is mostly out of view.
            // The frustum planes are fetched once a frame and tested here in managed code; a margin of a cell and a half keeps
            // the machines about to enter the view already moving.
            if (!InView(planes, model.GlobalPosition)) continue; // the model is local to its structure: the frustum needs its world position
            visible++;
            var m = Net.GetMachineInfo(id);
            var def = _content.Machines[m.Type];
            bool active = m.Crafting || (def.Role == MachineRole.Lab && Net.CurrentResearch >= 0) || def.Role == MachineRole.Depot
                || (def.Role == MachineRole.Drill && m.Deposit >= 0);
            if (def.Role == MachineRole.Turret) active = m.InputItems > 0; // a dark turret has no ammo
            if (_stressActive) active = true; // --stress-active: every machine at work, the worst case for animation
            Models.Animate(model, _animTime, active);
            float max = Net.MaxHp(m.Type);
            Models.SetDamage(model, max <= 0 ? 0f : 1f - Math.Clamp(m.Hp / max, 0f, 1f));

            if (def.Role == MachineRole.Turret && enemies is not null && enemies.Count > 0)
            {
                // Turrets track the nearest enemy; they hold their last aim when none is around.
                var at = model.GlobalPosition;
                float best = float.MaxValue;
                Vector3 target = at;
                foreach (var e in enemies)
                {
                    var p = new Vector3(e.X, e.Y, e.Z) / CellUnits;
                    float d2 = (p - at).LengthSquared();
                    if (d2 < best) { best = d2; target = p; }
                }
                if (best < 30f * 30f) Models.Aim(model, target);
            }
        }
        _machinesInView = visible;
    }

    private void UpdateDeposits()
    {
        for (int d = 0; d < _depositNodes.Count; d++)
        {
            float f = Net.DepositInitial(d) == 0 ? 0 : (float)Net.DepositRemaining(d) / Net.DepositInitial(d);
            _depositNodes[d].Scale = Vector3.One * Mathf.Max(0.15f, 0.35f + 0.65f * f);
            _depositNodes[d].Visible = Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 0; // hidden until scanned
        }
    }

    /// <summary>Deposits the player knows about, nearest first, so the selection starts on the closest one.</summary>
    private List<int> RevealedDeposits()
    {
        var ship = ShipPosition(0f);
        return Enumerable.Range(0, Net.DepositCount)
            .Where(d => Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 0)
            .OrderBy(d => { var p = Net.DepositPosition(d); return (new Vector3(p.X, p.Y, p.Z) - ship).LengthSquared(); })
            .ThenBy(d => d)
            .ToList();
    }

    private void CycleDeposit(int step)
    {
        var list = RevealedDeposits().OrderBy(d => d).ToList();
        if (list.Count == 0) { ToastLogistics(L("toast.noDeposits")); return; }
        int i = list.IndexOf(_selectedDeposit);
        _selectedDeposit = list[((i < 0 ? 0 : i + step) % list.Count + list.Count) % list.Count];
    }

    // ---- exploration --------------------------------------------------------------------------------

    private MapPanel _map = null!;
    private Label _mapList = null!;
    private bool _mapOpen;
    private int _selectedSystem = -1;
    private bool _exploreDemo;

    private IEnumerable<int> KnownSystems() => Enumerable.Range(0, Net.SystemCount).Where(Net.IsSystemKnown);

    private void CycleSystem(int step)
    {
        var list = KnownSystems().ToList();
        if (list.Count == 0) return;
        int i = list.IndexOf(_selectedSystem);
        _selectedSystem = list[((i < 0 ? 0 : i + step) % list.Count + list.Count) % list.Count];
    }

    private void SendJump()
    {
        if (_selectedSystem < 0) { Toast(L("toast.chooseSystem")); return; }
        Send(new Jump(_selectedSystem));
    }

    private string SystemLine(int s)
    {
        var g = Net.Galaxy!;
        var sys = g.Systems[s];
        int cur = Net.CurrentSystem(Player);
        var ship = ShipPosition(0f);
        float dist = (new Vector3(sys.X, sys.Y, sys.Z) - ship).Length();
        string status = Net.IsSystemVisited(s) ? L("map.visited") : L("map.known");
        string lane = cur >= 0 && g.HasLane(cur, s) ? L("map.lane") : "";
        return $"{(s == _selectedSystem ? ">" : " ")}{(s == cur ? "*" : " ")} {sys.Name,-9} {Loc.Name("arch", _content.Archetypes[sys.Archetype].Id),-14} {dist.ToString("F0", Loc.Culture),5} {status,-9} {lane}";
    }

    private void UpdateMap()
    {
        _map.Visible = _mapOpen;
        _mapList.Visible = _mapOpen;
        if (!_mapOpen || Net.Galaxy is null) return;
        if (_selectedSystem < 0 || !Net.IsSystemKnown(_selectedSystem)) _selectedSystem = KnownSystems().FirstOrDefault();
        _map.Show(Net.Galaxy, Net.IsSystemKnown, Net.IsSystemVisited, Net.CurrentSystem(Player), _selectedSystem);
        _mapList.Text = L("map.title") + "\n" + string.Join("\n", KnownSystems().Select(SystemLine));
    }

    // ---- commands and results --------------------------------------------------------------------

    private void Send(Command c) => _world.Submit(Player, c);

    private void Toast(string text, double seconds = 6.0, ToastCategory category = ToastCategory.Info) => _toasts.Add(text, category, Time.GetTicksMsec() / 1000.0, seconds);

    private void ToastThreat(string text, double seconds = 6.0) => Toast(text, seconds, ToastCategory.Threat);

    private void ToastLogistics(string text, double seconds = 6.0) => Toast(text, seconds, ToastCategory.Logistics);

    private void ToastProduction(string text, double seconds = 6.0) => Toast(text, seconds, ToastCategory.Production);

    private static string Why(Reject r) => RejectTexts.Key(r) is { } key ? L(key) : WhyLegacy(r);

    private static string WhyLegacy(Reject r) => r switch
    {
        Reject.NotEnoughItems => L("why.notEnough"),
        Reject.Locked => L("why.locked"),
        Reject.Overlap => L("why.overlap"),
        Reject.OutOfRange => L("why.outOfRange"),
        Reject.OutOfBounds => L("why.outOfBounds"),
        Reject.UnknownDeposit => L("why.unknownDeposit"),
        Reject.Depleted => L("why.depleted"),
        Reject.ResearchUnavailable => L("why.researchUnavailable"),
        Reject.NotDiscovered => L("why.notDiscovered"),
        Reject.NotDamaged => L("why.notDamaged"),
        Reject.OnCooldown => L("why.onCooldown"),
        Reject.NoLane => L("why.noLane"),
        Reject.NotInASystem => L("why.notInSystem"),
        Reject.Busy => L("why.busy"),
        _ => r.ToString(),
    };

    private void CollectResults()
    {
        foreach (var r in _world.LastResults)
        {
            if (!r.Ok)
            {
                if (r.Reject == Reject.OutOfRange) _outOfRangeSeen = true;
                if (r.Command is PlaceSegment or PlaceMachine or PlaceModule or RemoveModule or RemoveSegment or RemoveMachine or StartMining or SetResearch or SetShipRecipe
                    or PlaceSegmentIn or PlaceMachineIn or PlaceModuleIn or RemoveModuleIn or FoundStation
                    or Scan or Jump)
                    ToastLogistics(Why(r.Reject));
                else if (r.Command is SetThrust or SetMovement && r.Reject == Reject.NoCockpitOrEngine)
                    ToastLogistics(Why(r.Reject)); // the ship has lost its cockpit or last engine
                continue;
            }
            if (r.Command is Scan)
            {
                _scansDone++;
                _depositsRevealed += r.RevealedDeposits.Count;
                ToastProduction(r.RevealedDeposits.Count == 0 ? L("toast.scanNone") : L("toast.scanFound", r.RevealedDeposits.Count));
                if (r.RevealedDeposits.Count > 0) _selectedDeposit = RevealedDeposits().FirstOrDefault();
            }
            else if (r.Command is Jump j)
            {
                _jumpsStarted++;
                Toast(L("toast.jumpCharging", Net.Galaxy!.Systems[j.System].Name));
            }
            if (r.Command is PlaceSegment or PlaceSegmentIn && r.Created.Count == 1) AutoLinkSegment(r.Created[0]);
            else if (r.Command is PlaceMachineIn pmi && r.CreatedMachines.Count == 1) AutoLinkMachine(r.CreatedMachines[0], pmi.Recipe);
            else if (r.Command is FoundStation) ToastProduction(L("toast.stationFounded"));
            else if (r.Command is PlaceMachine pm && r.CreatedMachines.Count == 1) AutoLinkMachine(r.CreatedMachines[0], pm.Recipe);
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
            if (si.Structure != n.Structure) continue; // cells are local: only neighbours in the same structure touch
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
            if (mi.Structure != n.Structure) continue;
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

    private void AutoLinkMachine(int machine, int placedRecipe)
    {
        var mi = Net.GetMachineInfo(machine);
        var role = _content.Machines[mi.Type].Role;
        var pos = (mi.X, mi.Y, mi.Z);

        if (role == MachineRole.Drill)
        {
            var item = _content.Recipes[placedRecipe].Outputs[0].Item;
            // The best known deposit of that item that is within the drill's reach.
            int best = -1;
            for (int d = 0; d < Net.DepositCount; d++)
            {
                if (Net.DepositItem(d) != item || !Net.IsDepositRevealed(d) || Net.DepositRemaining(d) <= 0) continue;
                var dp = Net.DepositPosition(d);
                long dx = dp.X - mi.X, dy = dp.Y - mi.Y, dz = dp.Z - mi.Z;
                if (dx * dx + dy * dy + dz * dz > (long)ExplorationRules.DrillRangeCells * ExplorationRules.DrillRangeCells) continue;
                if (best < 0 || Net.DepositRemaining(d) > Net.DepositRemaining(best)) best = d;
            }
            if (best >= 0) Send(new LinkDeposit(machine, best));
            else ToastLogistics(L("toast.noDepositForDrill"));
        }

        bool fed = false;
        int outs = 0;
        for (int s = 0; s < Net.SegmentIdLimit; s++)
        {
            if (!Net.IsAlive(s)) continue;
            var si = Net.GetSegmentInfo(s);
            if (si.Structure != mi.Structure) continue;
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
        if (item.IsBelt || item.IsModule) return;
        var options = _content.Recipes.Where(r => r.Machine == item.Machine && !Net.IsRecipeLocked(r.Index)).Select(r => r.Index).ToList();
        if (options.Count < 2) { Toast(L("toast.oneRecipe")); return; }
        int cur = ChosenRecipe(item.Machine);
        _recipeChoice[item.Machine] = options[(options.IndexOf(cur) + 1) % options.Count];
        ToastProduction(L("toast.recipe", Loc.Name("recipe", _content.Recipes[_recipeChoice[item.Machine]].Id)));
    }

    // ---- input ---------------------------------------------------------------------------------

    /// <summary>The crosshair ray starts at the camera and runs through the screen centre.</summary>
    private Vector3 AimOrigin() => _camera.GlobalPosition;

    private Vector3 AimDirection() => -_camera.GlobalTransform.Basis.Z;

    /// <summary>Cells farther than this from the camera are not aimed at at all (the build range is 24; the margin lets the ghost show red beyond it).</summary>
    private const float AimMaxCells = PlayerRules.BuildRangeCells + 16;
    private const float AimMinCells = 2.5f;


    private void ToggleRefinery()
    {
        int recipe = _content.RecipeIndex("core:ship_smelt");
        bool on = Net.HasPlayer(Player) && Net.GetPlayerInfo(Player).ShipRecipe == recipe;
        Send(new SetShipRecipe(on ? -1 : recipe));
    }

    private void CycleResearch()
    {
        var options = Enumerable.Range(0, _content.Research.Count).Where(Net.CanResearch).ToList();
        if (options.Count == 0) { Send(new SetResearch(-1)); ToastProduction(L("toast.nothingToResearch")); return; }
        int cur = Net.CurrentResearch;
        int next = cur < 0 ? options[0] : options[(options.IndexOf(cur) + 1) % options.Count];
        Send(new SetResearch(next));
    }

    // ---- frame loop ----------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_paused && !_demo) return;
        int ticks = _demo || (_structuresShot && _shotPhase < 3) ? _demoSpeed : _speed;
        for (int i = 0; i < ticks; i++)
        {
            if (_demo) { if (_threatDemo) ThreatThink(); else if (_exploreDemo) ExploreThink(); else BotThink(); }
            else if (_structuresShot) StructuresShotThink();
            _world.Step();
            CollectResults();
            WatchThreat();
            if (_botDone) break;
        }
        if (Net.StructureVersion != _structureVersion) RebuildGeometry();
        _sampleCount = Net.FillSamples(_samples);
        if (_botDone && _finishCountdown == 0)
        {
            if (_exploreDemo) _mapOpen = true;
            _finishCountdown = 6; // let a few frames render before the screenshot
        }
    }

    private int _finishCountdown;

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) FinishPlayLog();
    }

    public override void _Process(double delta)
    {
        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        UpdateStructureRoots(alpha); // first: the machine and item nodes are children of these roots
        foreach (var geo in _geos.Values) geo.Count = 0;
        for (int i = 0; i < _sampleCount; i++)
        {
            var s = _samples[i];
            var geo = _segGeo[s.Segment];
            if (geo is null || geo.Count >= geo.Capacity) continue;
            var g = _segs[s.Segment];
            float p = MathF.Min(s.Pos + g.Speed * alpha, g.Length) / CellUnits;
            // Local to the segment's structure: the root node carries the item to its world position.
            var pos = new Vector3(g.Ox + 0.5f + g.Dx * (p - 0.5f), g.Oy + (g.Dy != 0 ? 0.5f + g.Dy * (p - 0.5f) : 0.3f), g.Oz + 0.5f + g.Dz * (p - 0.5f));
            Write(geo.Buf, geo.Count++ * FloatsPerItem, Vector3.One * 0.22f, pos, ItemColors[s.Type % ItemColors.Length]);
        }
        foreach (var geo in _geos.Values)
        {
            if (geo.Items is null) continue;
            geo.Items.VisibleInstanceCount = geo.Count;
            geo.Items.Buffer = geo.Buf;
        }
        _animTime += delta;
        UpdateMachines();
        UpdateEnemies((float)delta);
        UpdateDeposits();
        UpdateBody((float)delta); // before the camera is placed by UpdateShip
        UpdateShip(alpha, (float)delta);
        if (_walkShot) WalkShotFrame();
        if (!_structuresOn) _stationView.Sync(Net, Player, _cursorY);
        if (_structureDemo) StructureDemoTick();
        if (_structuresShot) StructuresShotFrame();
        UpdateMap();

        UpdateHover();
        UpdateGhost();
        UpdateUi((float)delta);
        if (_shotCountdown > 0 && --_shotCountdown == 0 && _shotPath != null)
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        if (_finishCountdown > 0 && --_finishCountdown == 0)
        {
            if (_threatDemo) FinishThreat();
            else if (_exploreDemo) FinishExplore();
            else FinishDemo();
        }
    }

    private void UpdateDragGhost()
    {
        // The preview lives under its structure root, in local cells, so it rides along with a flying ship.
        bool show = _dragStart is not null && _palette[_selected].IsBelt && _dragStart.Value.S == _cursor.S;
        _dragGhost.Visible = show;
        if (!show) return;
        Reparent(_dragGhost, RootOf(_cursor.S));
        var a = _dragStart!.Value;
        int dx = _cursor.X - a.X, dz = _cursor.Z - a.Z;
        bool alongX = Math.Abs(dx) >= Math.Abs(dz);
        int len = (alongX ? Math.Abs(dx) : Math.Abs(dz)) + 1;
        int sign = (alongX ? dx : dz) >= 0 ? 1 : -1;
        var from = CellCenter(a.X, a.Y, a.Z);
        _dragGhost.Position = from + (alongX ? new Vector3(sign * (len - 1) / 2f, 0, 0) : new Vector3(0, 0, sign * (len - 1) / 2f));
        _dragGhost.Scale = alongX ? new Vector3(len, 0.4f, 0.96f) : new Vector3(0.96f, 0.4f, len);
    }

    // ---- demo bot ----------------------------------------------------------------------------------
    // A scripted player that uses the same commands as a human: mine, refine, build an ingot line with a depot,
    // build a plate line, swap its depot for a research lab, and research Logistics.

    private int Hold(string item) => Net.HoldCount(Player, _content.ItemIndex(item));
    private bool Affordable(params (string Item, int Count)[] cost) => cost.All(c => Hold(c.Item) >= c.Count);
    private int Need(int machine, string item) => _content.Machines[machine].Cost.Where(s => _content.Items[s.Item].Id == item).Sum(s => s.Count);
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
            case 1: Step(Affordable((ingot, Need(drill, ingot))), () => Send(new PlaceMachine(0, 0, 0, drill, drillIron))); break;
            // These two go through the same code as the mouse: a belt drag from cell 1 to 3, a machine click on the palette selection.
            case 2: Step(Affordable((ingot, 3)), () => { _selected = 0; PlaceSelectedAt(new Cell(0, 3, 0, 0), new Cell(0, 1, 0, 0)); }); break;
            case 3: Step(Affordable((ingot, Need(smelter, ingot))), () => { _selected = _palette.FindIndex(b => b.Machine == smelter); PlaceSelectedAt(new Cell(0, 4, 0, 0), null); }); break;
            case 4: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(5, 0, 0, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 5:
                Step(Affordable((ingot, Need(depot, ingot))), () =>
                {
                    Send(new PlaceMachine(8, 0, 0, depot, -1));
                    Send(new StopMining());
                    Send(new PlaceSegment(60, 0, 0, Dir.PosX, 2, BeltTier.Mk1)); // far from the ship: must be refused
                });
                break;
            // Line 2: drill -> smelter -> assembler -> depot, delivering plates.
            case 6: Step(Affordable((ingot, Need(drill, ingot))), () => Send(new PlaceMachine(0, 0, 3, drill, drillIron))); break;
            case 7: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(1, 0, 3, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 8: Step(Affordable((ingot, Need(smelter, ingot))), () => Send(new PlaceMachine(4, 0, 3, smelter, smelt))); break;
            case 9: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(5, 0, 3, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 10: Step(Affordable((ingot, Need(assembler, ingot))), () => Send(new PlaceMachine(8, 0, 3, assembler, asm))); break;
            case 11: Step(Affordable((ingot, 3)), () => Send(new PlaceSegment(9, 0, 3, Dir.PosX, 3, BeltTier.Mk1))); break;
            case 12: Step(Affordable((ingot, Need(depot, ingot))), () => Send(new PlaceMachine(12, 0, 3, depot, -1))); break;
            // Plates reach the hold; swap the depot for a research lab, which needs 4 of them.
            case 13: Step(Affordable((plate, Need(lab, plate))), () => RemoveAt(new Cell(0, 12, 0, 3))); break;
            case 14: Step(CellFree(12, 0, 3) && Affordable((plate, Need(lab, plate)), (ingot, Need(lab, ingot))), () => Send(new PlaceMachine(12, 0, 3, lab, -1))); break;
            case 15: Step(true, () => Send(new SetResearch(logistics))); break;
            case 16: Step(Net.IsResearched(logistics), () => { _botDone = true; }); break;
        }
    }

    // ---- threat: enemies, shots and warnings ---------------------------------------------------------------

    private bool _threatDemo;
    private Node3D? _enemyRoot;
    private readonly List<(int Type, Node3D Node)> _enemyModels = new();
    private readonly List<(MeshInstance3D Node, float Life)> _fx = new();
    private StandardMaterial3D _fxTurret = null!, _fxShip = null!, _fxEnemy = null!;
    private long _lastWaves, _lastLost, _lastShipsLost;
    private bool _warned;
    private int _shotCountdown;
    private bool _shotTaken;

    private void BuildThreatVisuals()
    {
        if (_content.Threat is null) return;
        _enemyRoot = new Node3D();
        AddChild(_enemyRoot);

        StandardMaterial3D Beam(Color c) => new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _fxTurret = Beam(new Color(1f, 0.9f, 0.3f));
        _fxShip = Beam(new Color(0.5f, 0.9f, 1f));
        _fxEnemy = Beam(new Color(1f, 0.25f, 0.2f));
    }

    private void UpdateEnemies(float dt)
    {
        if (_enemyRoot is null) return;
        var list = Net.Enemies();
        var shipPos = ShipPosition(0f);
        // A small pool: slot i shows enemy i; a slot is rebuilt only when the enemy type in it changes.
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            while (_enemyModels.Count <= i) _enemyModels.Add((-1, null!));
            var (type, node) = _enemyModels[i];
            if (type != e.Type)
            {
                node?.QueueFree();
                node = Models.Enemy(e.Type);
                _enemyRoot.AddChild(node);
                _enemyModels[i] = (e.Type, node);
            }
            var pos = new Vector3(e.X, e.Y, e.Z) / CellUnits;
            var toShip = shipPos - pos;
            if (new Vector2(toShip.X, toShip.Z).LengthSquared() > 0.01f)
                node.Rotation = new Vector3(0, Mathf.LerpAngle(node.Rotation.Y, MathF.Atan2(-toShip.Z, toShip.X), 1f - MathF.Exp(-8f * dt)), 0);
            node.Position = pos;
            node.Visible = true;
            Models.Animate(node, _animTime, true);
        }
        for (int i = list.Count; i < _enemyModels.Count; i++)
            if (_enemyModels[i].Node is not null) _enemyModels[i].Node.Visible = false;

        // Beams from the shots of the last tick fade quickly.
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var (node, life) = _fx[i];
            life -= dt;
            if (life <= 0) { node.QueueFree(); _fx.RemoveAt(i); }
            else _fx[i] = (node, life);
        }
    }

    private void SpawnShotFx()
    {
        foreach (var s in Net.Shots)
        {
            if (_fx.Count >= 40) return;
            var from = new Vector3(s.FromX, s.FromY, s.FromZ) / CellUnits;
            var to = new Vector3(s.ToX, s.ToY, s.ToZ) / CellUnits;
            float length = (to - from).Length();
            if (length < 0.01f) continue;
            var beam = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, length) },
                MaterialOverride = s.Kind == ShotKind.Turret ? _fxTurret : s.Kind == ShotKind.Ship ? _fxShip : _fxEnemy,
            };
            AddChild(beam);
            beam.LookAtFromPosition((from + to) / 2f, to);
            _fx.Add((beam, 0.14f));
        }
    }

    /// <summary>Called after every simulation tick: warnings, losses and shot effects.</summary>
    private void WatchThreat()
    {
        if (!Net.ThreatActive) return;
        SpawnShotFx();

        var home = Net.GetSystemThreat(0);
        if (home.WarningRemaining >= 0 && !_warned)
        {
            _warned = true;
            ToastThreat(L("toast.waveWarning", (home.WarningRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture)));
        }
        else if (home.WarningRemaining < 0) _warned = false;

        if (Net.WavesSpawned > _lastWaves) { _lastWaves = Net.WavesSpawned; ToastThreat(L("toast.waveArrived", Net.WavesSpawned, Net.EnemyCount)); }
        if (Net.MachinesLost > _lastLost) { ToastThreat(L("toast.machineLost", Net.MachinesLost)); _lastLost = Net.MachinesLost; }
        if (Net.ShipsLost > _lastShipsLost) { ToastThreat(L("toast.shipLost")); _lastShipsLost = Net.ShipsLost; }
    }

    private void ToggleWeapons()
    {
        bool on = Net.HasPlayer(Player) && Net.GetPlayerInfo(Player).Armed;
        Send(new SetWeapons(!on));
    }


    // ---- threat demo -------------------------------------------------------------------------------
    // --threat-demo: a sandbox bot builds a loud base with two ammo-fed turrets, arms the ship, forces a wave and watches it.

    private void ThreatThink()
    {
        if (_botDone || _world.PendingCommands > 0) return;
        if (_botStep == 0)
        {
            int lab = _content.MachineIndex("core:research_lab"), smelter = _content.MachineIndex("core:smelter");
            int assembler = _content.MachineIndex("core:assembler"), turret = _content.MachineIndex("core:turret");
            int ammo = _content.ItemIndex("core:ammo");
            Send(new PlaceMachine(0, 0, 0, lab, -1));
            Send(new PlaceMachine(0, 0, 3, smelter, _content.RecipeIndex("core:smelt_iron")));
            Send(new PlaceMachine(0, 0, 6, assembler, _content.RecipeIndex("core:iron_plate")));
            foreach (int z in new[] { 0, 6 })
            {
                Send(new Batch(new Command[]
                {
                    new PlaceMachine(10, 0, z, turret, -1),
                    new PlaceSegment(7, 0, z, Dir.PosX, 3, BeltTier.Mk2),
                    new SetSource(-1, new ushort[] { (ushort)ammo }),
                    new ConnectToMachine(-1, -1),
                }));
            }
            // The ship stays out of it so the turrets' work shows (the ship weapon is covered by tests).
            Send(new SetWeapons(false));
            _overviewDistance = 34f;
            _botStartedAt = Math.Max(1, _world.Tick);
            _botStep = 1;
            return;
        }

        if (_botStep == 1)
        {
            if (_world.Tick - _botStartedAt < 600) return; // let the turrets fill with ammo
            Net.ScheduleWave(0); // setup for the demo: a wave arrives without waiting for the alert
            _botStep = 2;
            return;
        }

        if (!_shotTaken && Net.Shots.Any(s => s.Kind == ShotKind.Turret) && Net.EnemyCount > 0)
        {
            _shotTaken = true;
            _shotCountdown = 3;
        }
        if (Net.EnemiesSpawned > 0 && Net.EnemyCount == 0) _botDone = true;
        if (_world.Tick - _botStartedAt > 8000) _botDone = true;
    }

    private void FinishThreat()
    {
        var net = Net;
        GD.Print($"THREAT waves={net.WavesSpawned} spawned={net.EnemiesSpawned} killed={net.EnemiesKilled} machines_lost={net.MachinesLost} " +
                 $"ships_lost={net.ShipsLost} ammo_fired={net.AmmoFired} sim_seconds={(_world.Tick - _botStartedAt) / (double)Units.TicksPerSecond:F0}");
        try { _world.ValidateInvariants(); GD.Print("THREAT invariants=ok"); }
        catch (Exception e) { GD.PrintErr("THREAT invariants=FAILED " + e.Message); }
        GetTree().Quit();
    }

    // ---- explore demo ------------------------------------------------------------------------------
    // --explore-demo: a sandbox bot that scans, jumps lane by lane until seven systems are visited, then shows the map.

    private readonly HashSet<int> _scannedHere = new();
    private const int ExploreTarget = 7;

    private void ExploreThink()
    {
        if (_botDone || _world.PendingCommands > 0 || Net.JumpRemaining(Player) > 0) return;
        int cur = Net.CurrentSystem(Player);
        if (cur < 0) { _botDone = true; return; }
        if (_botStartedAt == 0) _botStartedAt = Math.Max(1, _world.Tick);

        if (!_scannedHere.Contains(cur))
        {
            if (Net.ScanCooldown(Player) > 0) return; // wait for the scanner
            _scannedHere.Add(cur);
            Send(new Scan());
            return;
        }

        int visited = Enumerable.Range(0, Net.SystemCount).Count(Net.IsSystemVisited);
        if (visited >= ExploreTarget) { _botDone = true; return; }

        int next = NextHop(cur);
        if (next < 0) { _botDone = true; return; }
        Send(new Jump(next));
    }

    /// <summary>First jump on the shortest path (over visited systems) to the nearest known system that has not been visited.</summary>
    private int NextHop(int from)
    {
        var g = Net.Galaxy!;
        var parent = new Dictionary<int, int> { [from] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int s = queue.Dequeue();
            foreach (int n in g.Systems[s].Lanes)
            {
                if (parent.ContainsKey(n) || !Net.IsSystemKnown(n)) continue;
                parent[n] = s;
                if (!Net.IsSystemVisited(n))
                {
                    int hop = n;
                    while (parent[hop] != from) hop = parent[hop];
                    return hop;
                }
                queue.Enqueue(n);
            }
        }
        return -1;
    }

    private void FinishExplore()
    {
        var net = Net;
        int visited = Enumerable.Range(0, net.SystemCount).Count(net.IsSystemVisited);
        int known = Enumerable.Range(0, net.SystemCount).Count(net.IsSystemKnown);
        int revealed = Enumerable.Range(0, net.DepositCount).Count(net.IsDepositRevealed);
        double seconds = (_world.Tick - _botStartedAt) / (double)Units.TicksPerSecond;
        GD.Print($"EXPLORE visited={visited}/{net.SystemCount} known={known} scans={_scansDone} jumps={_jumpsStarted} " +
                 $"deposits_revealed={revealed}/{net.DepositCount} sim_seconds={seconds:F0}");
        try { _world.ValidateInvariants(); GD.Print("EXPLORE invariants=ok"); }
        catch (Exception e) { GD.PrintErr("EXPLORE invariants=FAILED " + e.Message); }

        _mapOpen = true;
        UpdateMap();
        UpdateUi(0f);
        if (_shotPath != null) GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        GetTree().Quit();
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

        UpdateUi(0f);
        if (_shotPath != null) GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        GetTree().Quit();
    }
}
