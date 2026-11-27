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
    /// <summary>The galaxy seed of the scripted runs (demos, screenshots, benchmarks): fixed, so they stay comparable. A played new game picks a random one.</summary>
    private const ulong ScriptedSeed = 20261120UL;
    private ulong _galaxySeed = ScriptedSeed;
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
    // First person: the camera sits in the cockpit. Free flight (see Play.Flight.cs): the ship's attitude is a full 6-DOF rotation turned about
    // its own axes (_shipQuat, no up, no pitch limit), and the head look below is relative to the ship: yaw/pitch (radians) with yaw 0 along the
    // ship's nose (-Z). While piloting the head rests on the nose; with a build tool in hand, Alt held or standing it turns upright in the ship's
    // frame (its own floor), clamped like the old cockpit view.
    private float _yaw = -MathF.PI / 2f, _pitch = -0.3f;
    // Only the scripted modes (--demo, --stress) use an overview camera that follows the base from above; normal play never does.
    private float _overviewDistance = DefaultDistance;
    private Vector3 _overviewTarget = new(7f, 0f, 2f);
    private MeshInstance3D _cellHelper = null!; // faint build cells around the aimed cell, only with a cell build tool in hand
    private bool Overview => _demo || _stressMachines > 0 || _structureDemo;
    private const float CockpitFov = 80f;
    private const uint ShipLayer = 1u << 1; // render layer 2: the ship model, hidden from the cockpit camera
    private static readonly Vector3 CockpitOffset = new(0f, 1.8f, 0f); // eye height above the ship: near cells stay small enough to aim at
    // Structures: the pilot station inside the cockpit module, in cells from the ship structure origin (the seat model is at z 4.6; the eye
    // stands just behind the seat, so the workshop stays in view when looking aft). The eye sits about 1.7 above the
    // workshop build layer (y = 1), as in the legacy view, so near cells stay small enough to aim at.
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
        public readonly List<MultiMeshInstance3D> BeltNodes = new(); // belt tiles (one MultiMesh per tier) and the end caps
        public ItemLayers? Items;
        public int Capacity;
    }

    // Per content item index: the item's mesh shape and tint (see BeltModels), filled when the geometry is rebuilt.
    private int[] _itemShape = Array.Empty<int>();
    private Color[] _itemTint = Array.Empty<Color>();

    private readonly Dictionary<int, Geo> _geos = new();
    private Geo?[] _segGeo = Array.Empty<Geo?>(); // by segment id
    private readonly List<DepositView?> _depositNodes = new();
    private RockField? _rockField; // the rocks of the streamed field as MultiMesh batches per sector (ADR 0046); --rock-nodes draws them one node each, as before
    private ClusterGlow? _clusterGlow; // far glow of the clusters beyond the loaded sectors
    private bool _rockNodes;
    private readonly List<int> _depositSerials = new(); // which rock of the infinite field a slot held when its node was built (ADR 0040)
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
        UiFx.Install(this); // hover, press and click feedback for every button of every screen
        InitAudio(args);
        _threatDemo = args.Contains("--threat-demo");
        _demo = args.Contains("--demo") || args.Contains("--explore-demo") || _threatDemo;
        _shotPath = args.FirstOrDefault(a => a.StartsWith("--shot="))?["--shot=".Length..];
        string? sp = args.FirstOrDefault(a => a.StartsWith("--speed="));
        if (sp != null && int.TryParse(sp["--speed=".Length..], out int s)) _demoSpeed = Math.Clamp(s, 1, 200);

        var content = ContentSource.Load(args, out var contentLookup, out string? contentError);
        if (content is null) { ContentSource.ShowError(this, contentLookup, contentError); SetProcess(false); SetPhysicsProcess(false); SetProcessUnhandledInput(false); return; }
        _content = content;
        ClientContent.Load(contentLookup.Path);
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es; // any screenshot or scripted run may pin the language (after the settings load)
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        // --window=WxH puts any scripted run or screenshot in a window of that size (the shots saw the maximised window before, so 1024x600 was never looked at).
        if (args.FirstOrDefault(a => a.StartsWith("--window="))?["--window=".Length..].Split('x') is { Length: 2 } wh
            && int.TryParse(wh[0], out int ww) && int.TryParse(wh[1], out int wht) && ww >= 320 && wht >= 240)
            SetWindowSize(GetWindow(), ww, wht);
        _scenarios = ScenarioStore.Catalog(content, contentLookup.Path);
        _exploreDemo = args.Contains("--explore-demo");
        _saveTest = args.Contains("--save-test");
        if (args.Contains("--measure-models")) { ModelMeasure.Run(this, content); GetTree().Quit(); return; } // how far the drawn rocks and deposits reach (ADR 0033), then quits
        if (args.Contains("--load-bench")) { RunLoadBench(); GetTree().Quit(); return; } // measures the command log and the load time, then quits
        _uiShotPath = args.FirstOrDefault(a => a.StartsWith("--ui-shot="))?["--ui-shot=".Length..];
        _uiAudit = args.Contains("--ui-audit");
        _layoutShotPath = args.FirstOrDefault(a => a.StartsWith("--layout-shot="))?["--layout-shot=".Length..];
        _keysShotPath = args.FirstOrDefault(a => a.StartsWith("--keys-shot="))?["--keys-shot=".Length..];
        _inventoryShotPath = args.FirstOrDefault(a => a.StartsWith("--inventory-shot="))?["--inventory-shot=".Length..];
        _cockpitShotPath = args.FirstOrDefault(a => a.StartsWith("--cockpit-shot="))?["--cockpit-shot=".Length..];
        _turnShotPath = args.FirstOrDefault(a => a.StartsWith("--turn-shot="))?["--turn-shot=".Length..];
        // --continue=slot (quicksave, autosave, ...): continue that save, as the Continue button does; the galaxy comes from its header.
        if (args.FirstOrDefault(a => a.StartsWith("--continue="))?["--continue=".Length..] is { Length: > 0 } cont) { Launch.Mode = Launch.Intent.Continue; Launch.ContinueSave = cont; }
        _galaxySeed = ChooseSeed(args);
        string? st = args.FirstOrDefault(a => a.StartsWith("--stress="));
        _stressActive = args.Contains("--stress-active");
        _stressClose = args.Contains("--stress-close");
        _rockNodes = args.Contains("--rock-nodes");
        if (st is not null && int.TryParse(st["--stress=".Length..], out int sn)) _stressMachines = Math.Clamp(sn, 1, 5000);
        // A generated galaxy: the same seed always gives the same systems. Home's deposits start revealed; scan to find the rest.
        // The Remnant are on in the game and the threat demo; the production and exploration demos run without them.
        _structuresShotPath = args.FirstOrDefault(a => a.StartsWith("--structures-shot="))?["--structures-shot=".Length..];
        _powerShotPath = args.FirstOrDefault(a => a.StartsWith("--power-shot="))?["--power-shot=".Length..];
        _droneShotPath = args.FirstOrDefault(a => a.StartsWith("--drone-shot="))?["--drone-shot=".Length..];
        _defenceShotPath = args.FirstOrDefault(a => a.StartsWith("--defence-shot="))?["--defence-shot=".Length..];
        _endShotPath = args.FirstOrDefault(a => a.StartsWith("--end-shot="))?["--end-shot=".Length..];
        _structuresShot = _structuresShotPath is not null || _powerShotPath is not null || _droneShotPath is not null || _defenceShotPath is not null || _endShotPath is not null || args.Any(a => a.StartsWith("--obstacle-shot=")) || args.Contains("--field-perf");
        _walkShotPath = args.FirstOrDefault(a => a.StartsWith("--walk-shot="))?["--walk-shot=".Length..];
        _walkShot = _walkShotPath is not null;
        _flightShotPath = args.FirstOrDefault(a => a.StartsWith("--flight-shot="))?["--flight-shot=".Length..];
        _streakShotPath = args.FirstOrDefault(a => a.StartsWith("--streak-shot="))?["--streak-shot=".Length..];
        _flightShot = _flightShotPath is not null || _streakShotPath is not null;
        _dockShotPath = args.FirstOrDefault(a => a.StartsWith("--dock-shot="))?["--dock-shot=".Length..];
        _dockShot = _dockShotPath is not null;
        _designerShotPath = args.FirstOrDefault(a => a.StartsWith("--designer-shot="))?["--designer-shot=".Length..];
        _laserShotPath = args.FirstOrDefault(a => a.StartsWith("--laser-shot="))?["--laser-shot=".Length..];
        BlueprintSetup(args);
        FeelShotSetup(args);
        ArsenalShotSetup(args);
        bool threats = !(args.Contains("--demo") || _exploreDemo) && _stressMachines == 0 && !((_structuresShot || _walkShot || _dockShot || _flightShot || _designerShotPath is not null || _laserShotPath is not null || BlueprintScripted || args.Any(a => a.StartsWith("--gun-shot=") || a.StartsWith("--arsenal-shot=") || a.StartsWith("--overflow-shot=") || a.StartsWith("--drop-shot="))) && !args.Contains("--threats") && _defenceShotPath is null && _endShotPath is null);
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
        _goalsShotPath = args.FirstOrDefault(a => a.StartsWith("--goals-shot="))?["--goals-shot=".Length..];
        var plan = ChooseScenario(args);
        if (plan is not null)
        {
            // A scenario game (played from the New Game screen, continued from a scenario save, or a screenshot flag that asks for one).
            _scenario = plan.Def;
            _galaxySeed = plan.Seed;
            _gameMode = plan.Def.SandboxMode ? GameMode.Sandbox : GameMode.Campaign;
            _threatsOn = true;
            _structuresOn = true;
            _stationRules = false;
            _world = SimWorld.CreateScenario(_content, plan.Def, plan.Seed, plan.Rules);
            _progress = new ScenarioProgress(_content, plan.Def);
        }
        else
            _world = SimWorld.CreateGalaxy(_content, _gameMode, _galaxySeed, threats, _stationRules, _structuresOn);
        if (_structuresOn) { _yaw = MathF.PI; _pitch = -0.35f; _cursorY = DefaultLayer; } // face aft from the pilot seat
        _world.Network.EnsurePlayer(Player); // the ship starts at the origin, inside Home
        if (args.Contains("--structure-demo")) BuildStructureDemo(args);
        // For screenshots: finish every research that carries a bonus, so the upgrade line of the status panel shows.
        if (args.Contains("--grant-upgrades"))
            foreach (var node in _content.Research.Where(r => r.Bonuses.Length > 0)) _world.Network.GrantResearch(node.Index);
        if (_stressMachines > 0) BuildStressBase();
        if (_stationDemo) BuildStationDemo(args);
        _selectedDeposit = DefaultDeposit();

        BuildPalette();
        BuildScene();
        BuildUi();
        BuildThreatVisuals();
        BuildDroneVisuals();
        RebuildGeometry();
        if (_stationDemo) StationDemoSelect(args);
        if (!_demo && !_stationDemo && !_structuresShot && Launch.MenuShotPath is null && _uiShotPath is null && !_uiAudit && _layoutShotPath is null && _keysShotPath is null && _cockpitShotPath is null && _turnShotPath is null && _inventoryShotPath is null && !args.Any(a => a.StartsWith("--hud-sweep="))) StartPlayLog();
        if (_uiShotPath is not null || _uiAudit) UiShotSetup(args);
        if (_uiAudit) UiAuditSetup(args);
        if (_layoutShotPath is not null) LayoutShotSetup(args);
        if (_keysShotPath is not null) KeysShotSetup(args);
        if (_inventoryShotPath is not null) InventoryShotSetup(args);
        if (_cockpitShotPath is not null) CockpitShotSetup(args);
        if (_turnShotPath is not null) TurnShotSetup(args);
        if (!_demo) Toast(L("toast.welcome"));
        if (_structuresShot) StructuresShotSetup(args);
        if (_defenceShotPath is not null) DefenceShotSetup();
        if (args.Any(a => a.StartsWith("--obstacle-shot="))) ObstacleShotSetup(args);
        if (args.Contains("--field-perf")) FieldPerfSetup(args);
        if (_endShotPath is not null) EndShotSetup(args);
        if (_goalsShotPath is not null) GoalsShotSetup(args);
        if (Launch.Mode == Launch.Intent.Continue && Launch.ContinueSave is { } slot)
        {
            Launch.Mode = Launch.Intent.New;
            LoadGameFromDisk(slot);
        }
        if (Launch.MenuShotPath is not null) _wantCapture = false;
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
        Ambience.Build(this, Ambience.SystemSeed(_galaxySeed, Net.CurrentSystem(Player))); // sky, sun light and dust

        _camera = new Camera3D { Far = 500f, Fov = Overview ? 75f : CockpitFov, CullMask = Overview ? 0xFFFFFu : 0xFFFFFu & ~ShipLayer };
        AddChild(_camera);
        PlaceCamera(ShipPosition(0f));

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
        _cellHelper = BuildCellHelper();
        _ghost.AddChild(_cellHelper);

        // Deposits: spheres where the simulation says they are, shrinking as they run dry. Rocks of the infinite field come and go (ADR 0040): UpdateDeposits keeps the nodes in step.
        if (!_rockNodes && Net.HasRockField)
        {
            _rockField = new RockField();
            AddChild(_rockField);
            if (Net.HasRockClusters) { _clusterGlow = new ClusterGlow(); AddChild(_clusterGlow); }
        }
        for (int d = 0; d < Net.DepositCount; d++) SyncDepositNode(d);

        // The ship, with discs showing where it can build (big) and mine (small).
        _ship = Models.Ship();
        if (!Overview) SetLayers(_ship, ShipLayer);
        AddChild(_ship);
        _buildRange = RangeDisc(PlayerRules.BuildRangeCells, new Color(0.4f, 0.7f, 1f, 0.07f));
        _mineRange = RangeDisc(PlayerRules.MineRangeCells, new Color(1f, 0.6f, 0.2f, 0.12f));
    }

    /// <summary>A 9x9 patch of faint cell lines under the aimed cell (child of the ghost, so it lives in the structure's local cells).</summary>
    private static MeshInstance3D BuildCellHelper()
    {
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
void fragment() {
    vec2 p = UV * 9.0;
    vec2 d = abs(fract(p + 0.5) - 0.5) / fwidth(p);
    float line = 1.0 - min(min(d.x, d.y), 1.0);
    float fade = 1.0 - smoothstep(1.2, 4.5, length(p - 4.5));
    ALBEDO = vec3(0.35, 0.6, 1.0) * line * fade * 0.5;
}",
        };
        return new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(9f, 9f) },
            Position = new Vector3(0f, -0.49f, 0f),
            MaterialOverride = new ShaderMaterial { Shader = shader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
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
    private Vector3 ShipPosition(float alpha) => ShipBasePosition(alpha) + _dockSlideOffset;

    /// <summary>The simulated ship position, without the view's settling slide of a ship that just docked.</summary>
    private Vector3 ShipBasePosition(float alpha)
    {
        if (!Net.HasPlayer(Player)) return Vector3.Zero;
        var p = Net.GetPlayerInfo(Player);
        if (Net.HasFlightInertia && ShipStruct >= 0)
        {
            // Flight with inertia (ADR 0033): the ship keeps moving by its velocity (thousandths of a unit per tick), whatever the thrust says.
            return new Vector3(p.X + p.VelX * alpha / 1000f, p.Y + p.VelY * alpha / 1000f, p.Z + p.VelZ * alpha / 1000f) / CellUnits;
        }
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
        UpdateStructurePower();
        int ship = ShipStruct;
        if (ship < 0 || !Net.HasPlayer(Player)) return;
        if (_stationView.StructureRoot(ship) is { } root) root.Transform = ShipRootTransform(ShipPosition(_demo ? 0f : alpha)); // turned about the pilot seat
        var pi = Net.GetPlayerInfo(Player);
        bool thrusting = Net.CanFly(Player) && (pi.MoveX | pi.MoveY | pi.MoveZ | pi.ThrustX | pi.ThrustY | pi.ThrustZ) != 0;
        // Throttle (analog thrust, 1024 = full) sets the flame size; a keyboard move is full thrust.
        float level = (pi.ThrustX | pi.ThrustY | pi.ThrustZ) != 0
            ? Math.Clamp(MathF.Sqrt((float)pi.ThrustX * pi.ThrustX + (float)pi.ThrustY * pi.ThrustY + (float)pi.ThrustZ * pi.ThrustZ) / 1024f, 0.25f, 1f)
            : 1f;
        if (Net.HasFlightInertia)
        {
            // With inertia the flame follows the push of the engines (demand, acceleration, boost), and burns while the ship still brakes (Play.FlightFeel.cs).
            thrusting = Net.CanFly(Player) && (thrusting || _accelNorm > 0.05f);
            level = Math.Max(0.15f, _flameLevel);
        }
        _stationView.SetEngines(ship, thrusting, level);
    }

    private void UpdateShip(float alpha, float dt)
    {
        var pos = ShipPosition(_demo ? 0f : alpha);
        Ambience.Track(pos);
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
        Ambience.Retint(Ambience.SystemSeed(_galaxySeed, Net.CurrentSystem(Player))); // no-op unless the system changed
        // The range discs lie on the build plane (a disc at the ship's own height would be edge-on to the cockpit camera).
        // With structures the layer is local to the ship, so its world height is the ship's origin plus the layer.
        float layerWorldY = _cursorY + (ShipStruct >= 0 ? pos.Y : 0f);
        float floorY = layerWorldY + 0.02f;
        float dy = Math.Abs(pos.Y - (layerWorldY + 0.5f));
        // Free flight: a flat ring on a world plane would be a horizon, so the range rings exist only in the scripted overview camera.
        // In the cockpit the ranges are read from the distance on the markers and the tooltips.
        _buildRange.Visible = _mineRange.Visible = Overview;
        foreach (var (disc, radius) in new[] { (_buildRange, (float)PlayerRules.BuildRangeCells), (_mineRange, (float)PlayerRules.MineRangeCells) })
        {
            float r = MathF.Sqrt(MathF.Max(0.01f, radius * radius - dy * dy)) / radius;
            disc.Position = new Vector3(pos.X, Overview ? pos.Y : floorY, pos.Z);
            disc.Scale = new Vector3(r, 0.2f, r);
        }
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

    /// <summary>The head's right-hand side on the ship's floor plane, in the ship's frame (walking; the head is upright in the ship's own frame).</summary>
    private Vector3 CockpitRight() => new(MathF.Cos(_yaw), 0f, -MathF.Sin(_yaw));

    private void PlaceCamera(Vector3 shipPos)
    {
        if (Overview)
        {
            if (_structureDemo && _demoOrbit != 0f) _demoYaw += _demoOrbit * (float)GetProcessDeltaTime(); // --demo-orbit: a slow camera orbit for movies and flicker checks
            float yaw = _structureDemo ? _demoYaw : 0.2f, pitch = _structureDemo ? _demoPitch : 1.15f; // fixed look, for the scripted modes only
            var offset = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * _overviewDistance;
            _camera.LookAtFromPosition(_overviewTarget + offset, _overviewTarget);
            return;
        }
        if (_shotOutside && _shotCam is { } sc) { _camera.LookAtFromPosition(sc.From, sc.At); return; }
        if (_shotOutside && ShipStruct >= 0)
        {
            // --structures-shot: a view from outside the ship, looking at its middle (the ship is 40 cells long, the workshop is in the middle).
            var mid = shipPos + new Vector3(4f, 1.5f, 17f);
            _camera.LookAtFromPosition(mid + new Vector3(30f, 15f, 14f), mid);
            return;
        }
        var eye = ShipStruct >= 0 ? ShipRootTransform(shipPos) * EyeLocal() : shipPos + CockpitOffset;
        _camera.Transform = new Transform3D(CameraWorldBasis(), eye + ShakeOffset());
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
        if (_structuresOn || _structureDemo) _stationView.SyncStructures(Net); // makes sure every structure has its root before children are added
        foreach (var old in _geos.Values)
        {
            foreach (var n in old.BeltNodes) n.QueueFree();
            old.Items?.Free();
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

        _itemShape = new int[_content.Items.Count];
        _itemTint = new Color[_content.Items.Count];
        for (int i = 0; i < _itemShape.Length; i++)
        {
            var (shape, tint) = BeltModels.StyleOf(_content.Items[i].Id);
            _itemShape[i] = (int)shape;
            _itemTint[i] = tint;
        }

        int total = 0;
        foreach (var (structure, geo) in _geos)
        {
            var ids = alive.TryGetValue(structure, out var l) ? l : new List<int>();
            var parent = ParentOf(structure);

            // belts: one MultiMesh of tiles per tier (a tile per cell, turned to the segment's direction), plus end caps
            var infos = new List<SegmentInfo>(ids.Count);
            foreach (int sid in ids) infos.Add(Net.GetSegmentInfo(sid));
            for (int tier = 0; tier < 3; tier++)
            {
                int cells = 0;
                foreach (var s in infos) if ((int)s.Tier == tier) cells += s.LengthCells;
                if (cells == 0) continue;
                var tiles = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                    Mesh = BeltModels.Tile(tier), InstanceCount = cells,
                };
                var tileBuf = new float[cells * FloatsPerItem];
                int k = 0;
                foreach (var s in infos)
                {
                    if ((int)s.Tier != tier) continue;
                    var basis = s.DirY != 0 ? Basis.FromEuler(new Vector3(0, 0, s.DirY * Mathf.Pi / 2f)) : new Basis(Vector3.Up, MathF.Atan2(-s.DirZ, s.DirX));
                    for (int i = 0; i < s.LengthCells; i++)
                    {
                        var c = CellCenter(s.OriginX + s.DirX * i, s.OriginY + s.DirY * i, s.OriginZ + s.DirZ * i);
                        // a flat belt lies on the floor of its cell; a vertical one is a rail beside the item path, on the side its surface faces
                        var pos = s.DirY == 0 ? new Vector3(c.X, s.OriginY, c.Z) : new Vector3(c.X + 0.285f * s.DirY, c.Y, c.Z);
                        WriteXf(tileBuf, k++ * FloatsPerItem, basis, pos, TierColors[tier]);
                    }
                }
                tiles.Buffer = tileBuf;
                var tn = new MultiMeshInstance3D { Name = "BeltTier" + tier, Multimesh = tiles };
                parent.AddChild(tn);
                geo.BeltNodes.Add(tn);
            }
            // end caps on belts (flat or vertical): amber where a belt splits in two, red on a sink, green on a source
            var caps = new List<(Vector3, Color)>();
            for (int i = 0; i < infos.Count; i++)
            {
                var s = infos[i];
                if (s.DirY != 0)
                {
                    // a vertical run: the cap lies flat across the end of the item path (top end when it climbs, bottom end when it drops)
                    var c0 = CellCenter(s.OriginX, s.OriginY, s.OriginZ);
                    var c1 = CellCenter(s.OriginX, s.OriginY + s.DirY * (s.LengthCells - 1), s.OriginZ);
                    if (Net.OutputCount(s.Id) >= 2) caps.Add((new Vector3(c1.X, c1.Y + 0.5f * s.DirY, c1.Z), new Color(1f, 0.78f, 0.2f)));
                    else if (Net.IsSink(s.Id)) caps.Add((new Vector3(c1.X, c1.Y + 0.5f * s.DirY, c1.Z), new Color(0.95f, 0.3f, 0.25f)));
                    if (Net.HasSource(s.Id)) caps.Add((new Vector3(c0.X, c0.Y - 0.5f * s.DirY, c0.Z), new Color(0.35f, 0.95f, 0.55f)));
                    continue;
                }
                var first = new Vector3(s.OriginX + 0.5f, s.OriginY, s.OriginZ + 0.5f);
                var last = first + new Vector3(s.DirX, 0, s.DirZ) * (s.LengthCells - 1);
                if (Net.OutputCount(s.Id) >= 2) caps.Add((last, new Color(1f, 0.78f, 0.2f)));
                else if (Net.IsSink(s.Id)) caps.Add((last, new Color(0.95f, 0.3f, 0.25f)));
                if (Net.HasSource(s.Id)) caps.Add((first, new Color(0.35f, 0.95f, 0.55f)));
            }
            if (caps.Count > 0)
            {
                var hubs = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BeltModels.Hub(), InstanceCount = caps.Count };
                var hubBuf = new float[caps.Count * FloatsPerItem];
                for (int i = 0; i < caps.Count; i++) WriteXf(hubBuf, i * FloatsPerItem, Basis.Identity, caps[i].Item1, caps[i].Item2);
                hubs.Buffer = hubBuf;
                var hn = new MultiMeshInstance3D { Name = "BeltCaps", Multimesh = hubs };
                parent.AddChild(hn);
                geo.BeltNodes.Add(hn);
            }

            geo.Items = new ItemLayers(parent);
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

        BuildSupports();
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
            // Below half power a machine looks dead: its animation stops and its lights dim with the satisfaction.
            int sat = NeedsPower(m.Type) ? SatisfactionOf(m.Structure) : PowerRules.FullSatisfaction;
            if (sat < PowerRules.TurretMinSatisfaction)
            {
                active = false;
                Models.SetPower(model, 0.08f + 0.1f * sat / PowerRules.TurretMinSatisfaction);
            }
            else Models.SetPower(model, 1f);
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

    /// <summary>Builds, rebuilds or drops the node of one deposit slot: a slot of the infinite field holds another rock after its sector was forgotten and loaded again, or none (initial 0).</summary>
    private void SyncDepositNode(int d)
    {
        while (_depositNodes.Count <= d) { _depositNodes.Add(null); _depositSerials.Add(-1); }
        if (_rockField is not null && Net.DepositIsFieldRock(d)) return; // drawn by the RockField batches
        int serial = Net.DepositSerial(d);
        bool free = Net.DepositInitial(d) <= 0;
        if (_depositNodes[d] is { } old && (free || _depositSerials[d] != serial)) { old.QueueFree(); _depositNodes[d] = null; }
        if (free || _depositNodes[d] is not null) return;
        var (dx, dy, dz) = Net.DepositPosition(d);
        var n = new DepositView(_content.Items[Net.DepositItem(d)].Id, Net.DepositDensity(d), Net.DepositInitial(d), d, Net.DepositRockScalePermille(d));
        n.Refresh(Net.DepositRemaining(d));
        n.Position = new Vector3(dx, dy, dz);
        AddChild(n);
        _depositNodes[d] = n;
        _depositSerials[d] = serial;
    }

    private void UpdateDeposits(double delta = 0.0)
    {
        var eye = _camera.GlobalPosition;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _rockField?.Sync(Net, _content, eye);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _clusterGlow?.Update(Net, _content, eye, delta, _content.World?.Field?.LoadCells ?? 360);
        if (_fieldPerf && _fieldPerfFrame > 4 + 120) { _fpSyncMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds; _fpGlowMs += System.Diagnostics.Stopwatch.GetElapsedTime(t1).TotalMilliseconds; }
        for (int d = 0; d < Net.DepositCount; d++)
        {
            SyncDepositNode(d);
            if (_depositNodes[d] is not { } node) continue;
            node.Refresh(Net.DepositRemaining(d)); // chunks shrink in 5% steps; the rock keeps its size
            node.Visible = Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 0; // hidden until scanned; an emptied rock is destroyed
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

    /// <summary>The deposit selected at the start: the nearest iron one (the guided steps assume iron), else the nearest of any item.</summary>
    private int DefaultDeposit()
    {
        var list = RevealedDeposits();
        int iron = _content.ItemIndex("core:iron_ore");
        foreach (int d in list) if (Net.DepositItem(d) == iron) return d;
        return list.FirstOrDefault();
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
    private readonly bool _mapAllKnown = OS.GetCmdlineUserArgs().Contains("--map-all-known"); // a screenshot aid: the star map shows every system
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
        string star = _content.World is { } wg && Net.IsSystemVisited(s) ? Loc.Name("star", wg.StarTypes[sys.Star].Id) : "";
        return $"{(s == _selectedSystem ? ">" : " ")}{(s == cur ? "*" : " ")} {sys.Name,-8} {Loc.Name("arch", _content.Archetypes[sys.Archetype].Id),-15} {star,-14} {dist.ToString("F0", Loc.Culture),4} {status,-8} {lane}";
    }

    private void UpdateMap()
    {
        _map.Visible = _mapOpen;
        _mapList.Visible = _mapOpen;
        if (!_mapOpen || Net.Galaxy is null) return;
        if (_selectedSystem < 0 || !Net.IsSystemKnown(_selectedSystem)) _selectedSystem = KnownSystems().FirstOrDefault();
        var nose = -_camera.GlobalTransform.Basis.Z;
        _map.Show(Net.Galaxy, _mapAllKnown ? _ => true : Net.IsSystemKnown, Net.IsSystemVisited, Net.CurrentSystem(Player), _selectedSystem, new System.Numerics.Vector3(nose.X, nose.Y, nose.Z));
        _mapList.Text = L("map.title") + "\n" + string.Join("\n", KnownSystems().Select(SystemLine));
    }

    // ---- commands and results --------------------------------------------------------------------

    private void Send(Command c) => _world.Submit(Player, c);

    private void Toast(string text, double seconds = 6.0, ToastCategory category = ToastCategory.Info)
    {
        _toasts.Add(text, category, Time.GetTicksMsec() / 1000.0, seconds);
        AudioToast(category);
    }

    private void ToastThreat(string text, double seconds = 6.0) => Toast(text, seconds, ToastCategory.Threat);

    private void ToastLogistics(string text, double seconds = 6.0) => Toast(text, seconds, ToastCategory.Logistics);

    private void ToastProduction(string text, double seconds = 6.0) => Toast(text, seconds, ToastCategory.Production);

    /// <summary>The player-facing reason of a reject; every value has a text (a test checks), and the enum name is never shown.</summary>
    private static string Why(Reject r) => RejectTexts.Key(r) is { } key ? L(key) : L("toast.reject.unknown");

    private void CollectResults()
    {
        foreach (var r in _world.LastResults)
        {
            AudioResult(r);
            if (r.Command is DesignerApply or ApplyBlueprint) { _designerRejected = !r.Ok; if (!r.Ok) ToastLogistics(Why(r.Reject)); continue; } // the designer panel says "applied" only when it was
            if (r.Command is DockAt or Undock or TransferItems) { DockResult(r); continue; }
            if (r.Command is MoveItems or SetFeedSource or SortInventory) { InventoryResult(r); continue; }
            if (r.Command is DropItems or PickUp) { GroundResult(r); continue; }
            if (!r.Ok)
            {
                if (r.Reject == Reject.OutOfRange) _outOfRangeSeen = true;
                if (r.Command is PlaceSegment or PlaceMachine or PlaceModule or RemoveModule or RemoveSegment or RemoveMachine or StartMining or SetResearch or SetShipRecipe
                    or PlaceSegmentIn or PlaceMachineIn or PlaceModuleIn or RemoveModuleIn or FoundStation
                    or Scan or Jump or AssignDrones or DumpItems)
                    ToastLogistics(Why(r.Reject));
                else if (r.Command is ConnectSegments or ConnectFromMachine or ConnectToMachine or LinkDeposit)
                    ToastLogistics(L("toast.autoLinkFailed", Why(r.Reject))); // an automatic link that did not work: say why
                else if (r.Command is SetThrust or SetMovement && r.Reject == Reject.NoCockpitOrEngine)
                    ToastLogistics(Why(r.Reject)); // the ship has lost its cockpit or last engine
                continue;
            }
            if (r.Command is Scan)
            {
                _scansDone++;
                _depositsRevealed += r.RevealedDeposits.Count;
                ToastProduction(r.RevealedDeposits.Count == 0 ? L("toast.scanNone") : ScanClusterName(r.RevealedDeposits) is { } scanned ? L("toast.scanCluster", r.RevealedDeposits.Count, scanned) : L("toast.scanFound", r.RevealedDeposits.Count));
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
            else if (r.Command is AssignDrones ad) DronesAssignedToast(ad);
            else if (r.Command is DumpItems di) ToastProduction(L("toast.dumped", ItemName(di.Item), di.Count));
            else if (r.Command is PlaceMachine pm && r.CreatedMachines.Count == 1) AutoLinkMachine(r.CreatedMachines[0], pm.Recipe);
        }
        FlushTransferToast();
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


    /// <summary>The ship workbench's recipes in the order F cycles them: ingots from ore, plates from ingots, ammo from ingots (ADR 0031).</summary>
    private static readonly string[] BenchRecipes = { "core:ship_smelt", "core:ship_press", "core:ship_ammo" };

    /// <summary>F: switches the workbench to the next recipe (ingots, plates, ammo) and off after the last one.</summary>
    private void ToggleRefinery()
    {
        if (BenchView.UsesPanel(StationFirstWorld)) { BenchKey(); return; } // ADR 0039: the workbench has a panel and a queue; F no longer cycles recipes
        int current = Net.HasPlayer(Player) ? Net.GetPlayerInfo(Player).ShipRecipe : -1;
        int at = Array.FindIndex(BenchRecipes, id => _content.RecipeIndex(id) == current);
        int next = current < 0 ? 0 : at + 1; // an unknown recipe restarts at the first
        if (next >= BenchRecipes.Length) next = -1;
        Send(new SetShipRecipe(next < 0 ? -1 : _content.RecipeIndex(BenchRecipes[next])));
        ToastLogistics(next < 0 ? L("toast.benchOff") : L("toast.bench", L("bench." + BenchRecipes[next])));
    }

    // ---- frame loop ----------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_paused && !_demo) return;
        int ticks = _demo || (_structuresShot && _shotPhase < 3) ? _demoSpeed : _defenceShotPath is not null ? _dfTicksPerFrame : _speed;
        for (int i = 0; i < ticks; i++)
        {
            if (_demo) { if (_threatDemo) ThreatThink(); else if (_exploreDemo) ExploreThink(); else BotThink(); }
            else if (_structuresShot) StructuresShotThink();
            _world.Step();
            CollectResults();
            WatchThreat();
            WatchFeel();
            WatchDefence();
            WatchObstacles();
            AudioTick();
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
    private int _menuShotFrame;

    /// <summary><c>--menu-shot=path</c> (the game half): opens the pause overlay, saves <c>path.pause.png</c> and quits.</summary>
    private void MenuShotTick()
    {
        if (Launch.MenuShotPath is not { } path) return;
        _menuShotFrame++;
        if (_menuShotFrame == 40) OpenPause();
        if (_menuShotFrame == 80)
        {
            GetViewport().GetTexture().GetImage().SavePng(path + ".pause.png");
            GetTree().Quit();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) FinishPlayLog();
    }

    private const float ItemScale = 0.30f;

    /// <summary>Puts every item on its belt: two passes over the samples (count per shape, then write) so each shape's MultiMesh is exactly as big as needed.</summary>
    private void DrawItems(float alpha)
    {
        foreach (var geo in _geos.Values) geo.Items?.Begin();
        for (int i = 0; i < _sampleCount; i++)
        {
            var s = _samples[i];
            if (_segGeo[s.Segment] is { Items: { } layers } && s.Type < _itemShape.Length) layers.Count(_itemShape[s.Type]);
        }
        foreach (var geo in _geos.Values) geo.Items?.Allocate();
        for (int i = 0; i < _sampleCount; i++)
        {
            var s = _samples[i];
            if (_segGeo[s.Segment] is not { Items: { } layers } || s.Type >= _itemShape.Length) continue;
            var g = _segs[s.Segment];
            float p = MathF.Min(s.Pos + g.Speed * alpha, g.Length) / CellUnits;
            // Local to the segment's structure: the root node carries the item to its world position. Flat belts: the item stands on the
            // belt surface; vertical ones: it is centred on the path.
            var pos = new Vector3(g.Ox + 0.5f + g.Dx * (p - 0.5f), g.Oy + (g.Dy != 0 ? 0.5f + g.Dy * (p - 0.5f) - 0.1f : 0.172f), g.Oz + 0.5f + g.Dz * (p - 0.5f));
            layers.Add(_itemShape[s.Type], ItemScale, pos, _itemTint[s.Type]);
        }
        foreach (var geo in _geos.Values) geo.Items?.Flush();
    }

    private static void WriteXf(float[] b, int o, Basis basis, Vector3 pos, Color c)
    {
        var r0 = basis.Row0; var r1 = basis.Row1; var r2 = basis.Row2;
        b[o + 0] = r0.X; b[o + 1] = r0.Y; b[o + 2] = r0.Z; b[o + 3] = pos.X;
        b[o + 4] = r1.X; b[o + 5] = r1.Y; b[o + 6] = r1.Z; b[o + 7] = pos.Y;
        b[o + 8] = r2.X; b[o + 9] = r2.Y; b[o + 10] = r2.Z; b[o + 11] = pos.Z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    public override void _Process(double delta)
    {
        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        UpdateAttitude((float)delta); // free flight: roll, the settle onto a port, the head back to the nose
        SyncAttitude(); // ADR 0030: the simulation learns the ship attitude
        UpdateDock((float)delta); // the docking guidance and the settling slide: before anything reads the ship position
        UpdateStructureRoots(alpha); // first: the machine and item nodes are children of these roots
        UpdateWorkbench(); // the ship's workbench model, its animation and its card (Play.Workbench.cs)
        DrawItems(alpha);
        _animTime += delta;
        UpdateMachines();
        UpdateEnemies((float)delta);
        UpdateDeposits(delta);
        UpdateDrones((float)delta);
        UpdateFlightFeel((float)delta); // the smoothed speed, acceleration, field-of-view kick, rumble and flame length (Play.FlightFeel.cs)
        UpdateCockpitHead((float)delta); // the field of view eases; a focused display narrows it
        UpdateBody((float)delta); // before the camera is placed by UpdateShip
        UpdateShip(alpha, (float)delta);
        UpdateCockpitDisplays((float)delta); // after the camera: what the crosshair is on, the screens redraw
        UpdateObstacles((float)delta);
        if (_walkShot) WalkShotFrame();
        if (_streakShotPath is not null) StreakShotFrame();
        else if (_flightShot) FlightShotFrame();
        if (_dockShot) DockShotFrame();
        if (_designerShotPath is not null || _laserShotPath is not null) DesignerShotFrame();
        if (FeelShotOn) FeelShotFrame();
        if (_arsenalShotPath is not null) ArsenalShotFrame();
        if (BlueprintScripted) BlueprintShotFrame();
        UpdateBlueprint();
        UpdateDesigner();
        if (!_structuresOn) _stationView.Sync(Net, Player, _cursorY);
        if (_structureDemo) StructureDemoTick();
        if (_structuresShot) StructuresShotFrame();
        if (_powerShotPath is not null || _droneShotPath is not null) PowerShotFrame();
        UpdateMap();

        UpdateHover();
        UpdateLaserFx();
        UpdateWeaponFx((float)delta);
        UpdateGhost();
        UpdateDefenceUi((float)delta);
        UpdateUi((float)delta);
        UpdateAudio((float)delta);
        MenuShotTick();
        EndShotFrame();
        GoalsShotFrame();
        UiShotFrame();
        UiAuditFrame();
        LayoutShotFrame();
        KeysShotFrame();
        InventoryShotFrame();
        CockpitShotFrame();
        TurnShotFrame();
        DefenceShotFrame();
        ObstacleShotFrame();
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
                    if (Net.StationFirst) Send(new QueueBench(_content.RecipeIndex("core:ship_smelt"), PlayerRules.BenchQueueMax)); else Send(new SetShipRecipe(_content.RecipeIndex("core:ship_smelt"))); // ADR 0039: the bench takes a queue
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
                Mesh = new BoxMesh { Size = s.Kind == ShotKind.Enemy ? new Vector3(0.14f, 0.14f, length) : new Vector3(0.07f, 0.07f, length) }, // enemy beams are fat: they end in the module they hit
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
        // A module that falls says so itself (with its contents lost): no extra line about the machines inside it.
        if (Net.MachinesLost > _lastLost)
        {
            if (!(_structuresOn && Net.ModuleHits.Any(h => h.Destroyed))) ToastThreat(L("toast.machineLost", Net.MachinesLost));
            _lastLost = Net.MachinesLost;
        }
        if (Net.ShipsLost > _lastShipsLost) { ToastThreat(L(_structuresOn ? "toast.shipLostCockpit" : "toast.shipLost"), 10.0); _lastShipsLost = Net.ShipsLost; }
    }

    private void ToggleWeapons() => CycleWeaponGroup(); // ADR 0036: V and the weapons button switch the weapon in hand too


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
