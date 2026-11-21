using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The player interface of <see cref="Play"/>: a build bar, action buttons, hover tooltips, an inspector for
/// machines, a guided-steps panel, mouse camera and click-to-fly, all in Spanish and English.
/// </summary>
public partial class Play
{
    private enum ToolMode { Pointer, Build }

    private enum HoverKind { None, Ground, Machine, Segment, Deposit, Enemy }

    private readonly record struct Hover(HoverKind Kind, int Id, string Text);

    // ---- state -------------------------------------------------------------------------------------

    private ToolMode _tool = ToolMode.Pointer;
    private Hover _hover = new(HoverKind.None, -1, "");
    private int _selMachine = -1;
    private Vector3? _waypoint;
    private bool _pendingMine;
    private const float DefaultDistance = 10f; // overview camera of the scripted modes
    private const float MouseSensitivity = 0.0025f; // radians per pixel
    private const float MaxPitch = 85f * MathF.PI / 180f;
    private bool _wantCapture = true; // the player's wish; the cursor is also released while a panel is open or the window is unfocused
    private bool _mouseLook; // true while the cursor is captured and the mouse turns the ship's view
    private (int X, int Y, int Z) _lastThrust;
    private double _lastThrustAt;
    private bool _helpOpen;
    private bool _objectivesOpen = true;
    private bool _holdWasFull;
    private double _holdFullAgain;
    private bool _detailsOpen; // H: the full status block instead of the essentials
    private ObjectiveTracker _objectives = null!;

    // Colours of the notification categories and of the target panel.
    private static readonly Color ToastInfo = new(0.95f, 0.97f, 1f);
    private static readonly Color ToastProductionColor = new(0.55f, 0.92f, 0.65f);
    private static readonly Color ToastLogisticsColor = new(1f, 0.82f, 0.4f);
    private static readonly Color ToastThreatColor = new(1f, 0.45f, 0.4f);
    private static readonly Color GoodColor = new(0.45f, 0.85f, 0.5f);
    private static readonly Color WornColor = new(0.95f, 0.75f, 0.3f);
    private static readonly Color BadColor = new(0.92f, 0.3f, 0.25f);

    // ---- nodes -------------------------------------------------------------------------------------

    private CanvasLayer _layer = null!;
    private Control _uiRoot = null!;
    private PanelContainer _statusPanel = null!, _toolbar = null!, _actions = null!, _objectivesPanel = null!, _inspector = null!, _helpPanel = null!;
    private Label _statusLabel = null!, _holdItems = null!, _modeHint = null!, _objectivesText = null!, _tooltipLabel = null!;
    private Label _inspectorTitle = null!, _inspectorBody = null!, _helpText = null!;
    private Label _toolLabel = null!, _flightHeading = null!, _flightSpeed = null!;
    private Label _targetTitle = null!, _targetRole = null!, _targetStatus = null!, _targetRecipe = null!, _targetHpLabel = null!;
    private ProgressBar _holdBar = null!, _targetHp = null!;
    private PanelContainer _flightPanel = null!, _targetPanel = null!;
    private VBoxContainer _toastBox = null!;
    private readonly Label[] _toastLabels = new Label[ToastQueue.MaxVisible];
    private MarkerLayer _markers = null!;
    private Button _btnDetails = null!;
    private Crosshair _crosshair = null!;
    private readonly List<Button> _buildButtons = new();
    private Button _btnMine = null!, _btnRefine = null!, _btnScan = null!, _btnMap = null!, _btnWeapons = null!, _btnResearch = null!;
    private Button _btnRepair = null!;
    private Label _seatPrompt = null!;
    private Button _btnJump = null!, _btnLang = null!, _btnHelp = null!, _btnObjectives = null!;
    private Button _inspPrev = null!, _inspNext = null!, _inspRemove = null!, _inspRepair = null!;

    private static string L(string key, params object[] args) => Loc.T(key, args);

    /// <summary>Vein richness as a word plus percent, e.g. "Rich vein (150 %)".</summary>
    private string DensityText(int deposit)
    {
        int d = Net.DepositDensity(deposit);
        return L(d < 60 ? "density.poor" : d <= 120 ? "density.normal" : "density.rich", d);
    }

    private string ItemName(int item) => Loc.Name("item", _content.Items[item].Id);

    private string MachineName(int type) => Loc.Name("machine", _content.Machines[type].Id);

    private string Stacks(ItemStack[] stacks) =>
        stacks.Length == 0 ? L("ui.free") : string.Join(", ", stacks.Select(s => $"{s.Count} {ItemName(s.Item)}"));

    // ---- building the interface ---------------------------------------------------------------------

    private static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, float margin = 8f)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(margin);
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(1); }
        return s;
    }

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.86f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.7f);
    private static readonly Color Accent = new(0.35f, 0.65f, 1f);

    private PanelContainer MakePanel(Control content)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Box(PanelBg, PanelEdge));
        p.AddChild(content);
        p.MouseFilter = Control.MouseFilterEnum.Stop;
        _uiRoot.AddChild(p);
        return p;
    }

    private Label MakeText(int size = 14, Color? color = null)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeColorOverride("font_color", color ?? Colors.White);
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    private Button MakeButton(string text, Action onPressed, bool toggle = false, int minWidth = 0)
    {
        var b = new Button { Text = text, ToggleMode = toggle, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(minWidth, 0) };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.1f, 0.13f, 0.2f, 0.95f), PanelEdge, 5, 6f));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.16f, 0.22f, 0.34f, 0.97f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.15f, 0.34f, 0.58f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(0.18f, 0.4f, 0.66f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.06f, 0.07f, 0.1f, 0.7f), new Color(0.2f, 0.22f, 0.28f), 5, 6f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.52f, 0.58f));
        b.Pressed += onPressed;
        return b;
    }

    private void BuildUi()
    {
        _layer = new CanvasLayer();
        AddChild(_layer);
        _uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_uiRoot);

        _objectives = new ObjectiveTracker(_content, _stationRules, _structuresOn);
        SettingsStore.EnsureLoaded(); // language, volume and window mode from user://settings.cfg

        // Status: the essentials (hull, alert, current step); H opens the full block.
        _statusLabel = MakeText(14);
        var statusBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        statusBox.AddChild(_statusLabel);
        BuildPowerBox(statusBox);
        _statusPanel = MakePanel(statusBox);
        _statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Flight readout at the top centre: heading strip and speed.
        var flight = new VBoxContainer();
        _flightHeading = MakeText(15, new Color(0.85f, 0.93f, 1f));
        _flightHeading.HorizontalAlignment = HorizontalAlignment.Center;
        _flightSpeed = MakeText(13, new Color(0.7f, 0.8f, 0.95f));
        _flightSpeed.HorizontalAlignment = HorizontalAlignment.Center;
        flight.AddChild(_flightHeading);
        flight.AddChild(_flightSpeed);
        _flightPanel = MakePanel(flight);
        _flightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Action buttons.
        var actions = new VBoxContainer();
        _btnMine = MakeButton("", ToggleMining, toggle: true, minWidth: 170);
        _btnRefine = MakeButton("", ToggleRefinery, toggle: true, minWidth: 170);
        _btnScan = MakeButton("", () => Send(new Scan()), minWidth: 170);
        _btnMap = MakeButton("", () => _mapOpen = !_mapOpen, toggle: true, minWidth: 170);
        _btnWeapons = MakeButton("", ToggleWeapons, toggle: true, minWidth: 170);
        _btnResearch = MakeButton("", CycleResearch, minWidth: 170);
        _btnRepair = MakeButton("", RepairAll, minWidth: 170);
        foreach (var b in new[] { _btnMine, _btnRefine, _btnScan, _btnMap, _btnWeapons, _btnRepair, _btnResearch }) actions.AddChild(b);
        _actions = MakePanel(actions);

        // The bottom strip: the hold on the left, the selected tool and its cost on the right, and below them the compact
        // build palette (keys 1-9, 0, Q, B). It hugs the bottom edge so the view stays free.
        var strip = new VBoxContainer();
        strip.AddThemeConstantOverride("separation", 4);
        var infoRow = new HBoxContainer();
        infoRow.AddThemeConstantOverride("separation", 10);
        _holdBar = new ProgressBar { MinValue = 0, MaxValue = Net.HoldCapacity(Player), ShowPercentage = false, CustomMinimumSize = new Vector2(120, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        _holdItems = MakeText(13);
        _holdItems.ClipText = true;
        _holdItems.CustomMinimumSize = new Vector2(660, 0); // wide enough for ore, ingots and plates
        _toolLabel = MakeText(14);
        _toolLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _toolLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        infoRow.AddChild(_holdBar);
        infoRow.AddChild(_holdItems);
        infoRow.AddChild(_toolLabel);
        strip.AddChild(infoRow);
        _buildBar = new VBoxContainer();
        _buildBar.AddThemeConstantOverride("separation", 3);
        FillBuildBar();
        strip.AddChild(_buildBar);
        _toolbar = MakePanel(strip);

        _modeHint = MakeText(14, new Color(0.85f, 0.92f, 1f));
        _modeHint.AddThemeColorOverride("font_outline_color", Colors.Black);
        _modeHint.AddThemeConstantOverride("outline_size", 5);
        _uiRoot.AddChild(_modeHint);

        // At most three notifications, each coloured by its category.
        _toastBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        for (int i = 0; i < _toastLabels.Length; i++)
        {
            var l = MakeText(16);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.AddThemeColorOverride("font_outline_color", Colors.Black);
            l.AddThemeConstantOverride("outline_size", 5);
            l.Visible = false;
            _toastLabels[i] = l;
            _toastBox.AddChild(l);
        }
        _uiRoot.AddChild(_toastBox);

        // Guided steps.
        _objectivesText = MakeText(14);
        var obj = new VBoxContainer();
        var objButtons = new HBoxContainer();
        _btnObjectives = MakeButton("", () => _objectivesOpen = !_objectivesOpen);
        _btnHelp = MakeButton("", () => _helpOpen = !_helpOpen);
        _btnLang = MakeButton("", ToggleLanguage);
        _btnDetails = MakeButton("", () => _detailsOpen = !_detailsOpen);
        objButtons.AddChild(_btnObjectives);
        objButtons.AddChild(_btnDetails);
        objButtons.AddChild(_btnHelp);
        objButtons.AddChild(_btnLang);
        obj.AddChild(objButtons);
        obj.AddChild(_objectivesText);
        _objectivesPanel = MakePanel(obj);

        // The star map and its list are the existing nodes; the jump button sits under them.
        _map = new MapPanel { Visible = false };
        _uiRoot.AddChild(_map);
        _mapList = MakeText(13);
        _mapList.AddThemeColorOverride("font_outline_color", Colors.Black);
        _mapList.AddThemeConstantOverride("outline_size", 4);
        _mapList.Visible = false;
        _uiRoot.AddChild(_mapList);
        _btnJump = MakeButton("", SendJump, minWidth: 150);
        _btnJump.Visible = false;
        _uiRoot.AddChild(_btnJump);

        // Inspector for a selected machine.
        var insp = new VBoxContainer();
        _inspectorTitle = MakeText(16, new Color(1f, 0.9f, 0.5f));
        _inspectorBody = MakeText(13);
        var inspButtons = new HBoxContainer();
        _inspPrev = MakeButton("<", () => ChangeSelectedRecipe(-1), minWidth: 34);
        _inspNext = MakeButton(">", () => ChangeSelectedRecipe(1), minWidth: 34);
        _inspRemove = MakeButton("", () => { if (_selMachine >= 0) Send(new RemoveMachine(_selMachine)); _selMachine = -1; }, minWidth: 100);
        inspButtons.AddChild(_inspPrev);
        inspButtons.AddChild(_inspNext);
        _inspRepair = MakeButton("", () => { if (_selMachine >= 0) Send(new Repair(_selMachine)); }, minWidth: 100);
        inspButtons.AddChild(_inspRepair);
        inspButtons.AddChild(_inspRemove);
        insp.AddChild(_inspectorTitle);
        insp.AddChild(_inspectorBody);
        insp.AddChild(inspButtons);
        _inspector = MakePanel(insp);
        _inspector.Visible = false;

        // Help overlay.
        _helpText = MakeText(15);
        _helpPanel = MakePanel(_helpText);
        _helpPanel.Visible = false;

        _crosshair = new Crosshair { Visible = false };
        _uiRoot.AddChild(_crosshair);

        // World tooltip, anchored just below and right of the crosshair.
        _tooltipLabel = MakeText(13);
        _tooltipLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _tooltipLabel.AddThemeConstantOverride("outline_size", 5);
        _uiRoot.AddChild(_tooltipLabel);

        // "E: sit" / "E: stand", centred just above the crosshair.
        _seatPrompt = MakeText(15, new Color(1f, 0.9f, 0.5f));
        _seatPrompt.AddThemeColorOverride("font_outline_color", Colors.Black);
        _seatPrompt.AddThemeConstantOverride("outline_size", 6);
        _seatPrompt.MouseFilter = Control.MouseFilterEnum.Ignore;
        _uiRoot.AddChild(_seatPrompt);

        // Contextual panel for the machine under the crosshair: name, role, status, recipe and a hit-point bar.
        var target = new VBoxContainer();
        _targetTitle = MakeText(15, new Color(1f, 0.9f, 0.5f));
        _targetRole = MakeText(12, new Color(0.7f, 0.76f, 0.88f));
        _targetRole.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _targetRole.CustomMinimumSize = new Vector2(250, 0);
        _targetStatus = MakeText(13);
        _targetStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _targetStatus.CustomMinimumSize = new Vector2(250, 0);
        _targetRecipe = MakeText(12, new Color(0.8f, 0.88f, 1f));
        _targetRecipe.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _targetRecipe.CustomMinimumSize = new Vector2(250, 0);
        _targetHp = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(250, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        _targetHpLabel = MakeText(12);
        _targetPower = MakeText(12);
        _targetPower.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _targetPower.CustomMinimumSize = new Vector2(250, 0);
        target.AddChild(_targetTitle);
        target.AddChild(_targetRole);
        target.AddChild(_targetStatus);
        target.AddChild(_targetRecipe);
        target.AddChild(_targetPower);
        target.AddChild(_targetHp);
        target.AddChild(_targetHpLabel);
        _targetPanel = MakePanel(target);
        _targetPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _targetPanel.Visible = false;

        var userArgs = OS.GetCmdlineUserArgs();
        _hudShotPath = userArgs.FirstOrDefault(a => a.StartsWith("--hud-shot="))?["--hud-shot=".Length..];
        _detailsOpen = userArgs.Contains("--hud-details");

        // Directional markers go last so they are never hidden under a panel.
        _markers = new MarkerLayer { Visible = false };
        _uiRoot.AddChild(_markers);
        if (!_demo) BuildPauseMenu();
    }

    // ---- pause menu ------------------------------------------------------------------------------------

    private MenuScreen? _pauseMenu;
    private bool _pausedBefore;

    private bool PauseOpen => _pauseMenu is { Visible: true };

    /// <summary>The pause overlay (above the HUD, hidden): Resume, quick save and load, Settings, Controls, Main menu and Quit.</summary>
    private void BuildPauseMenu()
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var menu = _pauseMenu = new MenuScreen("pause.title", dim: true) { Visible = false };
        menu.AddEntry(() => L("pause.resume"), ClosePause);
        menu.AddEntry(() => L("pause.save"), () =>
        {
            bool ok = SaveGameToDisk("quicksave", silent: true);
            menu.SetStatus(ok ? L("pause.saved", PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond)) : L("pause.saveFailed"));
            menu.Rebuild();
        });
        menu.AddEntry(() => L("pause.load"), () =>
        {
            if (LoadGameFromDisk("quicksave")) ClosePause();
            else menu.SetStatus(L("pause.loadFailed"));
        }, () => Godot.FileAccess.FileExists(SavePath("quicksave")));
        menu.AddEntry(() => L("menu.settings"), () => menu.Open(MenuScreen.Page.Settings));
        menu.AddEntry(() => L("menu.controls"), () => menu.Open(MenuScreen.Page.Controls));
        menu.AddEntry(() => L("pause.mainMenu"), () =>
        {
            SaveGameToDisk("autosave", silent: true);
            FinishPlayLog();
            Launch.MenuShotPath = null;
            GetTree().ChangeSceneToFile("res://MainMenu.tscn");
        });
        menu.AddEntry(() => L("menu.quit"), () => GetTree().Quit());
        menu.CloseRequested += ClosePause;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); // the menu fills its parent, so the parent must fill the screen
        layer.AddChild(root);
        root.AddChild(menu);
    }

    private void OpenPause()
    {
        if (_pauseMenu is null || PauseOpen) return;
        _pausedBefore = _paused;
        _paused = true;
        _wantCapture = false;
        _pauseMenu.SetStatus("");
        _pauseMenu.Open(MenuScreen.Page.Main);
    }

    private void ClosePause()
    {
        if (_pauseMenu is null) return;
        _pauseMenu.Visible = false;
        _paused = _pausedBefore;
        _wantCapture = true;
    }

    private VBoxContainer _buildBar = null!;

    /// <summary>Entries reached by the number keys (1-9, 0, Q, B, U); the next ones take F2 to F5, the module (N) and station (I) entries have their own key.</summary>
    private const int DirectKeys = 13;

    /// <summary>
    /// One button per palette entry, in two rows so the strip stays inside 1280 pixels with the whole palette (belts, every machine, the
    /// module and the station tool): the first half in the top row, the rest below.
    /// </summary>
    private void FillBuildBar()
    {
        foreach (var old in _buildBar.GetChildren()) old.QueueFree();
        _buildButtons.Clear();
        int perRow = (_palette.Count + 1) / 2;
        HBoxContainer? row = null;
        for (int i = 0; i < _palette.Count; i++)
        {
            if (i % perRow == 0)
            {
                row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 4);
                _buildBar.AddChild(row);
            }
            int index = i;
            var b = MakeButton("", () => SelectBuild(index), toggle: true);
            b.AddThemeFontSizeOverride("font_size", 12);
            b.CustomMinimumSize = new Vector2(0, 26);
            _buildButtons.Add(b);
            row!.AddChild(b);
        }
    }

    /// <summary>Rebuilds the palette after the rules changed (a loaded save with the other station-rules setting).</summary>
    private void RebuildPalette()
    {
        _palette.Clear();
        BuildPalette();
        _selected = 0;
        _tool = ToolMode.Pointer;
        FillBuildBar();
    }

    // ---- per-frame refresh --------------------------------------------------------------------------

    private void UpdateUi(float dt)
    {
        UpdateCapture();
        _objectives.Update(Net, Player);
        UpdatePlayLog();
        AutosaveTick();
        if (_saveTest) SaveSelfTest();
        if (_stressMachines > 0) StressTick(dt);
        if (!_demo)
        {
            _tips ??= new Tips(_content);
            var tip = _tips.Next(Net, Player);
            if (tip is not null)
            {
                Toast(L(tip.Key, tip.Args), 14.0); // longer than a toast: these are lessons
                if (_playLog is not null)
                {
                    _logFile?.StoreLine(PlayLog.Line(_playLog.Note(Net, "Tip", tip.Key)));
                    _logFile?.Flush();
                }
            }
        }

        var size = GetViewport().GetVisibleRect().Size;
        UpdateHold();
        UpdateStatus();
        UpdateButtons();
        UpdateObjectivesPanel();
        UpdateInspector();
        UpdateTarget();
        UpdateFlight();
        UpdateHelp();
        UpdateToasts();
        UpdateModeHint();
        LayoutUi(size);
        UpdateMarkers(size);
        HudShotTick();
    }

    private string? _hudShotPath;
    private int _hudShotFrame;

    /// <summary><c>--hud-shot=path</c> (with <c>--hud-details</c> for the full status): saves the HUD of the played game after a second and quits. For layout checks.</summary>
    private void HudShotTick()
    {
        if (_hudShotPath is null || ++_hudShotFrame != 90) return;
        GetViewport().GetTexture().GetImage().SavePng(_hudShotPath);
        GetTree().Quit();
    }

    private static Vec3 V(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The heading strip and the speed in cells per second, from the view direction and the ship's movement.</summary>
    private void UpdateFlight()
    {
        bool show = !_demo && !Overview && Net.HasPlayer(Player);
        _flightPanel.Visible = show;
        if (!show) return;
        var fwd = CockpitForward();
        _flightHeading.Text = Compass.Strip(Compass.HeadingDegrees(fwd.X, fwd.Z));
        float speed = (ShipPosition(1f) - ShipPosition(0f)).Length() * Units.TicksPerSecond;
        _flightSpeed.Text = L("hud.flightSpeed", speed.ToString("F1", Loc.Culture));
    }

    private readonly List<Vec3> _baseTmp = new();

    /// <summary>Markers for the selected deposit, the nearest enemy and the base (the nearest cluster of machines, else the anchor).</summary>
    private void UpdateMarkers(Vector2 size)
    {
        _markers.Visible = !_demo && !Overview && Net.HasPlayer(Player);
        if (!_markers.Visible) return;

        var basis = _camera.GlobalTransform.Basis;
        var cam = new CameraBasis(V(-basis.Z), V(basis.X), V(basis.Y), Mathf.DegToRad(_camera.Fov));
        var eye = _camera.GlobalPosition;
        float left = _actions.Position.X + _actions.Size.X + 14f;
        float bottom = Math.Max(24f, size.Y - _toolbar.Position.Y + 10f);
        var items = new List<MarkerLayer.Item>();

        void Add(string label, Color color, Vector3 target, float hideBelow = 4f)
        {
            var p = Markers.Place(V(target - eye), cam, size.X, size.Y, left, 58f, 24f, bottom, hideBelow);
            // A marker behind the steps panel is unreadable (and hides its text): slide it just below the panel.
            var steps = new Rect2(_objectivesPanel.Position - new Vector2(240f, 0f), _objectivesPanel.Size + new Vector2(240f + 14f, 8f));
            if (_objectivesPanel.Visible && steps.HasPoint(new Vector2(p.X, p.Y))) p = p with { Y = steps.End.Y + 22f };
            if (p.Visible) items.Add(new MarkerLayer.Item($"{label}  {L("mark.dist", p.Distance.ToString("F0", Loc.Culture))}", color, p));
        }

        if (Net.IsDeposit(_selectedDeposit) && Net.IsDepositRevealed(_selectedDeposit) && Net.DepositRemaining(_selectedDeposit) > 0)
        {
            var dp = Net.DepositPosition(_selectedDeposit);
            Add($"{ItemName(Net.DepositItem(_selectedDeposit))} - {DensityText(_selectedDeposit)}{(DepositHasDrones(_selectedDeposit) ? " - " + L("mark.drones") : "")}", new Color(1f, 0.8f, 0.35f), new Vector3(dp.X, dp.Y, dp.Z), 6f);
        }

        int system = Net.CurrentSystem(Player);
        EnemyInfo? nearest = null;
        float best = float.MaxValue;
        foreach (var en in Net.Enemies())
        {
            if (system >= 0 && en.System != system) continue;
            float d = (new Vector3(en.X, en.Y, en.Z) / CellUnits - eye).LengthSquared();
            if (d < best) { best = d; nearest = en; }
        }
        if (nearest is { } e) Add(Loc.Name("enemy", _content.Enemies[e.Type].Id), new Color(1f, 0.35f, 0.3f), new Vector3(e.X, e.Y, e.Z) / CellUnits, 3f);

        if (_structuresOn) { StructureMarkers(Add); _markers.Set(items); return; }

        _baseTmp.Clear();
        foreach (int id in _machineIds)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            _baseTmp.Add(new Vec3(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f));
        }
        if (_stationRules && Net.ModuleCount(Player) > 0)
        {
            // The station: the centre of the module nearest the ship.
            _baseTmp.Clear();
            foreach (var (mx, my, mz, _) in Net.Modules(Player)) _baseTmp.Add(V(StationView.Centre(mx, my, mz)));
            var nearestModule = Markers.NearestCluster(_baseTmp, V(ShipPosition(0f)), 0.01f);
            if (nearestModule is { } sm) Add(L("mark.station"), new Color(0.45f, 0.8f, 1f), new Vector3(sm.X, sm.Y, sm.Z), 10f);
            _markers.Set(items);
            return;
        }
        var baseAt = Markers.NearestCluster(_baseTmp, V(ShipPosition(0f)), 14f);
        if (baseAt is null && Net.Galaxy is { } galaxy && system >= 0) baseAt = new Vec3(galaxy.Systems[system].X, galaxy.Systems[system].Y, galaxy.Systems[system].Z);
        if (baseAt is { } b) Add(L("mark.base"), new Color(0.45f, 0.8f, 1f), new Vector3(b.X, b.Y, b.Z), 10f);

        _markers.Set(items);
    }

    private void UpdateHold()
    {
        int total = Net.HoldTotal(Player), cap = Net.HoldCapacity(Player);
        _holdBar.MaxValue = Math.Max(1, cap);
        _holdBar.Value = total;
        bool full = total >= cap;
        _holdBar.AddThemeStyleboxOverride("fill", Box(full ? BadColor : HudFormat.Band(cap - total, cap) == HealthBand.Critical ? WornColor : GoodColor, null, 3, 0f));
        _holdBar.AddThemeStyleboxOverride("background", Box(new Color(0.12f, 0.14f, 0.2f), null, 3, 0f));

        var items = new StringBuilder();
        items.Append(L("hud.hold", total, cap));
        foreach (var item in _content.Items)
        {
            int n = Net.HoldCount(Player, item.Index);
            if (n > 0) items.Append($"  {ItemName(item.Index)} {n}");
        }
        _holdItems.Text = items.ToString();
        _holdItems.AddThemeColorOverride("font_color", full ? new Color(1f, 0.55f, 0.45f) : Colors.White);

        // Said when it fills and again every 25 s while it stays full: a full hold silently stops mining and depots.
        double now = Time.GetTicksMsec() / 1000.0;
        if (full && (!_holdWasFull || now >= _holdFullAgain))
        {
            ToastLogistics(L("toast.holdFull"), 8.0);
            _holdFullAgain = now + 25.0;
        }
        _holdWasFull = full;
    }

    private void UpdateStatus()
    {
        var sb = new StringBuilder();
        var p = Net.GetPlayerInfo(Player);
        _satCache.Clear(); // read again each frame by the machine models
        UpdatePowerHud();

        if (!_detailsOpen)
        {
            UpdateEssentials(sb, p);
            return;
        }
        _statusLabel.AddThemeColorOverride("font_color", Colors.White);
        if (_stationRules) sb.Append(L("hud.modules", Net.ModuleCount(Player))).Append('\n');
        if (_structuresOn) StructureHud(sb, details: true);

        int dep = _selectedDeposit;
        string depLine;
        if (Net.MiningDeposit(Player) >= 0)
        {
            int d = Net.MiningDeposit(Player);
            bool inRange = Net.InMineRange(Player, d);
            depLine = inRange ? L("hud.mining", ItemName(Net.DepositItem(d)), Net.DepositRemaining(d), DensityText(d))
                              : L("hud.miningOut", ItemName(Net.DepositItem(d)));
            if (inRange && p.Hull >= 0 && Net.HoldTotal(Player) >= Net.HoldCapacity(Player)) depLine = L("hud.miningFull");
        }
        else if (Net.IsDeposit(dep) && Net.IsDepositRevealed(dep))
        {
            var pos = Net.DepositPosition(dep);
            float dist = (new Vector3(pos.X, pos.Y, pos.Z) - ShipPosition(0f)).Length();
            depLine = L("hud.depositSelected", ItemName(Net.DepositItem(dep)), Net.DepositRemaining(dep), dist.ToString("F0", Loc.Culture), DensityText(dep));
        }
        else depLine = L("hud.noDeposit");
        sb.Append(depLine).Append('\n');

        if (Net.Galaxy is { } galaxy)
        {
            int cur = Net.CurrentSystem(Player);
            string where = cur >= 0 ? $"{galaxy.Systems[cur].Name} ({Loc.Name("arch", _content.Archetypes[galaxy.Systems[cur].Archetype].Id)})" : L("hud.deepSpace");
            string jump = Net.JumpRemaining(Player) > 0
                ? L("hud.jumping", galaxy.Systems[Net.JumpTarget(Player)].Name, (Net.JumpRemaining(Player) / (float)Units.TicksPerSecond).ToString("F1", Loc.Culture))
                : Net.HasAbility(Ability.Jump) ? L("hud.jumpReady") : L("hud.jumpLocked");
            sb.Append(L("hud.system", where, Enumerable.Range(0, galaxy.Systems.Count).Count(Net.IsSystemVisited), galaxy.Systems.Count, jump)).Append('\n');
            ThreatHud(sb, cur);
            OreWarning(sb);
        }

        if (Net.CurrentResearch >= 0)
        {
            var node = _content.Research[Net.CurrentResearch];
            sb.Append(L("hud.research", ResearchName(node, next: true),
                string.Join(", ", Net.ResearchCost(node.Index).Select(c => $"{Net.ResearchProgress(node.Index, c.Item)}/{c.Count} {ItemName(c.Item)}")))).Append('\n');
        }
        else sb.Append(Net.ResearchedCount == 0 ? L("hud.researchNone") : L("hud.researchDone",
            string.Join(", ", _content.Research.Where(r => Net.IsResearched(r.Index)).Select(r => ResearchName(r, next: false))))).Append('\n');

        int bDmg = Net.BonusPercent(BonusKind.TurretDamage), bRate = Net.BonusPercent(BonusKind.TurretRate),
            bHp = Net.BonusPercent(BonusKind.MachineHp), bHeal = Net.BonusPercent(BonusKind.RepairHeal),
            bHull = Net.BonusPercent(BonusKind.ShipHull), bGun = Net.BonusPercent(BonusKind.ShipDamage);
        if (bDmg + bRate + bHp + bHeal + bHull + bGun > 0) sb.Append(L("hud.defense", bDmg, bRate, bHp, bHeal, bHull, bGun)).Append('\n');

        if (_demo) sb.Append(L("hud.speed", _demoSpeed));
        else if (_speed != 1 || _paused) sb.Append(_paused ? L("hud.paused") : L("hud.speed", _speed));
        _statusLabel.Text = sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// The default status: hull and alert (when there are threats), the step in progress while the steps panel is folded,
    /// and the pause or speed. The hold has its own bar at the bottom. H shows the full block.
    /// </summary>
    private void UpdateEssentials(StringBuilder sb, PlayerInfo p)
    {
        var colour = Colors.White;
        if (Net.ThreatActive)
        {
            int sys = Math.Max(0, Net.CurrentSystem(Player));
            var th = Net.GetSystemThreat(sys);
            string ship = p.RespawnRemaining > 0 ? L("hud.rebuilding", (p.RespawnRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture))
                : L("hud.ess.hull", p.Hull, Net.ShipMaxHull);
            string state = th.WarningRemaining >= 0 ? L("hud.waveIncoming", th.Wave + 1, (th.WarningRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture))
                : th.Enemies > 0 ? L("hud.enemies", th.Enemies)
                : L("hud.alert", 100 * th.Alert / Math.Max(1, th.AlertMax));
            sb.Append(ship).Append("   |   ").Append(state).Append('\n');
            if (th.WarningRemaining >= 0 || th.Enemies > 0 || p.RespawnRemaining > 0) colour = ToastThreatColor;
            else if (HudFormat.Band(p.Hull, Net.ShipMaxHull) != HealthBand.Good) colour = WornColor;
        }
        if (_stationRules) sb.Append(L("hud.modules", Net.ModuleCount(Player))).Append('\n');
        if (_structuresOn) StructureHud(sb, details: false);
        if (!_objectivesOpen && _objectives.Current is { } cur)
            sb.Append(L("hud.ess.step", L(_objectives.States.First(s => s.Objective == cur).TitleKey))).Append('\n');
        if (_demo) sb.Append(L("hud.speed", _demoSpeed));
        else if (_speed != 1 || _paused) sb.Append(_paused ? L("hud.paused") : L("hud.speed", _speed));
        _statusLabel.Text = sb.ToString().TrimEnd('\n');
        _statusLabel.AddThemeColorOverride("font_color", colour);
    }

    private void ThreatHud(StringBuilder sb, int currentSystem)
    {
        if (!Net.ThreatActive) return;
        var rules = _content.Threat!;
        int sys = currentSystem >= 0 ? currentSystem : 0;
        var th = Net.GetSystemThreat(sys);
        string state = th.WarningRemaining >= 0 ? L("hud.waveIncoming", th.Wave + 1, (th.WarningRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture))
            : th.Enemies > 0 ? L("hud.enemies", th.Enemies)
            : L("hud.alert", 100 * th.Alert / Math.Max(1, th.AlertMax));
        var p = Net.GetPlayerInfo(Player);
        string ship = p.RespawnRemaining > 0 ? L("hud.rebuilding", (p.RespawnRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture))
            : L("hud.hull", p.Hull, Net.ShipMaxHull);
        sb.Append(L("hud.threat", th.Signature, rules.SignatureThreshold, state, ship)).Append('\n');
    }

    // ---- saving and loading -------------------------------------------------------------------

    private GameMode _gameMode = GameMode.Campaign;
    private bool _threatsOn = true;
    private double _lastAutosave;
    private bool _saveTest;

    private static string SavePath(string name) => $"user://saves/{name}.sfsave";

    /// <summary>Writes the game (its command log) to <c>user://saves</c>. A save is small and loads by replaying it.</summary>
    private bool SaveGameToDisk(string name, bool silent)
    {
        if (_demo) return false;
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://saves"));
            var ms = new System.IO.MemoryStream();
            SaveGame.Write(ms, _world, _content, _gameMode, GalaxySeed, _threatsOn, Player, _stationRules, _structuresOn);
            using var f = Godot.FileAccess.Open(SavePath(name), Godot.FileAccess.ModeFlags.Write);
            if (f is null) throw new System.IO.IOException(Godot.FileAccess.GetOpenError().ToString());
            f.StoreBuffer(ms.ToArray());
            if (!silent) Toast(L("toast.saved", PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond)));
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr("SAVE failed: " + e.Message);
            Toast(L("toast.saveFailed", e.Message));
            return false;
        }
    }

    /// <summary>Loads the quick save (or the autosave when there is none) by replaying it; the game freezes for a second or two.</summary>
    private bool LoadGameFromDisk(string? slot = null)
    {
        if (_demo) return false;
        string name = slot ?? (Godot.FileAccess.FileExists(SavePath("quicksave")) ? "quicksave" : "autosave");
        if (!Godot.FileAccess.FileExists(SavePath(name))) { Toast(L("toast.noSave")); return false; }
        try
        {
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(SavePath(name));
            var world = SaveGame.Read(new System.IO.MemoryStream(bytes), _content, out var info);
            if (info.GalaxySeed != GalaxySeed) throw new System.IO.InvalidDataException("The save is from another galaxy.");
            _world = world;
            _gameMode = info.Mode;
            _threatsOn = info.Threats;
            // The save decides the rules, whatever the running game started with.
            bool structures = info.Structures && _content.StarterShip is not null;
            bool rules = info.StationRules && !structures; // structures win over the station rules
            bool rulesChanged = _stationRules != rules || _structuresOn != structures;
            _stationRules = rules;
            _structuresOn = structures;
            if (rulesChanged) RebuildPalette();
            _stationView.Invalidate();
            ResetAfterLoad();
            Toast(L("toast.loaded", PlayLog.Format(info.Ticks / (double)Units.TicksPerSecond)));
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr("LOAD failed: " + e.Message);
            Toast(L("toast.loadFailed", e.Message));
            return false;
        }
    }

    // ---- stress test ---------------------------------------------------------------------------

    private int _stressMachines;
    private bool _stressActive;
    private bool _stressClose;
    private int _stressFrame;
    private readonly List<double> _stressFrames = new();
    private readonly List<double> _stressProcess = new();

    /// <summary>
    /// <c>--stress=N</c>: a sandbox base of N machines of every kind in a grid and belts with moving items, seen from far enough
    /// to take it all in, to measure what the renderer costs. Prints the average and worst frame time and quits.
    /// </summary>
    private void BuildStressBase()
    {
        var kinds = _content.Machines.Where(m => m.Role != MachineRole.Ship).ToList();
        int side = (int)Math.Ceiling(Math.Sqrt(_stressMachines));
        int placed = 0;
        for (int i = 0; i < _stressMachines; i++)
        {
            var def = kinds[i % kinds.Count];
            int recipe = _content.Recipes.FirstOrDefault(r => r.Machine == def.Index)?.Index ?? -1;
            _world.Submit(Player, new PlaceMachine((i % side) * 2 - side, 0, (i / side) * 2 - side, def.Index, recipe));
            if (++placed % 200 == 0) _world.Step();
        }
        _world.Step();
        // Belts with sources and sinks between the rows, so items move on screen.
        for (int row = 0; row < Math.Min(40, side); row++)
        {
            _world.Submit(Player, new PlaceSegment(-side, 0, row * 2 * 2 + 1 - side, Dir.PosX, Math.Min(side * 2, 60), BeltTier.Mk1));
        }
        _world.Step();
        for (int id = 0; id < _world.Network.SegmentIdLimit; id++)
            if (_world.Network.IsAlive(id))
            {
                _world.Submit(Player, new SetSource(id, new ushort[] { 0 }));
                _world.Submit(Player, new SetSink(id));
            }
        _world.Step();
        _overviewDistance = _stressClose ? 22f : 40 + side * 1.6f; // --stress-close: the view a player really has, a small part of a big base
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); // measure what the work costs, not the refresh rate
        GD.Print($"STRESS built machines={_world.Network.MachineCount} segments={_world.Network.SegmentCount}");
    }

    private void StressTick(double delta)
    {
        _stressFrame++;
        if (_stressFrame > 60) { _stressFrames.Add(delta * 1000.0); _stressProcess.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0); } // the first frames build the scene
        if (_stressFrames.Count < 300) return;
        var sorted = _stressFrames.OrderBy(x => x).ToList();
        GD.Print($"STRESS machines={_world.Network.MachineCount} in_view={_machinesInView} frames={sorted.Count} avg_ms={sorted.Average():F2} process_ms={_stressProcess.Average():F2} p95_ms={sorted[(int)(sorted.Count * 0.95)]:F2} worst_ms={sorted[^1]:F2} fps={1000.0 / sorted.Average():F0}");
        GetTree().Quit();
    }

    private int _saveTestFrame;
    private ulong _saveTestChecksum;
    private long _saveTestTick;

    /// <summary>
    /// <c>--save-test</c>: plays a few commands, saves, changes the world, loads, and prints whether the loaded world is
    /// the saved one. A way to see saving and loading work in the real game without pressing keys.
    /// </summary>
    private void SaveSelfTest()
    {
        _saveTestFrame++;
        if (_saveTestFrame == 20) Send(new Scan());
        if (_saveTestFrame == 80)
        {
            _saveTestChecksum = _world.Checksum();
            _saveTestTick = _world.Tick;
            SaveGameToDisk("selftest", silent: true);
        }
        if (_saveTestFrame == 100) ToggleRefinery(); // the world moves on after the save
        if (_saveTestFrame == 140)
        {
            bool changed = _world.Checksum() != _saveTestChecksum;
            LoadGameFromDisk("selftest");
            bool same = _world.Checksum() == _saveTestChecksum && _world.Tick == _saveTestTick;
            GD.Print($"SAVETEST changed_after_save={changed} loaded_matches_save={same} tick={_world.Tick}");
            GetTree().Quit();
        }
    }

    /// <summary>The interface state that belonged to the old world goes; what the new world shows is rebuilt.</summary>
    private void ResetAfterLoad()
    {
        _structureVersion = -1; // rebuilds belts, machines and deposits from the loaded world
        _beltAnchor = null;
        _dragStart = null;
        if (_structuresOn && _cursorY == 0) _cursorY = DefaultStructureLayer;
        _selMachine = -1;
        _waypoint = null;
        _pendingMine = false;
        _seated = true; // the loaded world has its own ship and modules: start at the seat again
        _bodyVolumesVersion = -1;
        _tool = ToolMode.Pointer;
        _selectedDeposit = DefaultDeposit();
        _objectives = new ObjectiveTracker(_content, _stationRules, _structuresOn);
        _tips = null;
        _lowOreWarned.Clear();
        _warned = false;
        if (_playLog is not null)
        {
            _logFile?.StoreLine($"-- loaded a save at game time {PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond)}");
            _logFile?.Flush();
            _playLog = new PlayLog(_content, _stationRules, _structuresOn);
            _playLog.Update(Net, Player); // what was already done is not news
        }
    }

    /// <summary>Every five minutes of real time a played game saves itself to the autosave slot.</summary>
    private void AutosaveTick()
    {
        if (_demo) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastAutosave == 0) { _lastAutosave = now; return; }
        if (now - _lastAutosave < 300) return;
        _lastAutosave = now;
        SaveGameToDisk("autosave", silent: true);
    }

    // ---- play log -----------------------------------------------------------------------------

    private Tips? _tips;
    private PlayLog? _playLog;
    private Godot.FileAccess? _logFile;

    /// <summary>
    /// A played game (not a demo) writes its milestones to <c>user://playlogs</c> as they happen, in the same names as the
    /// balance simulator's report, so a play-test leaves numbers behind. The path is printed once.
    /// </summary>
    private void StartPlayLog()
    {
        _playLog = new PlayLog(_content, _stationRules, _structuresOn);
        try
        {
            string dir = ProjectSettings.GlobalizePath("user://playlogs");
            DirAccess.MakeDirRecursiveAbsolute(dir);
            string path = $"user://playlogs/playlog-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            _logFile = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            GD.Print("PLAYLOG " + ProjectSettings.GlobalizePath(path));
        }
        catch (Exception e) { GD.PrintErr("PLAYLOG unavailable: " + e.Message); _logFile = null; }
    }

    private void UpdatePlayLog()
    {
        if (_playLog is null) return;
        foreach (var e in _playLog.Update(Net, Player))
        {
            _logFile?.StoreLine(PlayLog.Line(e));
            _logFile?.Flush();
        }
    }

    private void FinishPlayLog()
    {
        if (_playLog is null || _logFile is null) return;
        _logFile.StoreLine($"-- ended at {PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond)}, hold full for {_playLog.HoldFullSeconds:F0} s");
        _logFile.Flush();
        _logFile.Close();
        _logFile = null;
    }

    private readonly HashSet<(int System, int Item)> _lowOreWarned = new();

    /// <summary>A status line, and a toast the first time, when an ore of the current system is running low.</summary>
    private void OreWarning(StringBuilder sb)
    {
        int system = Net.CurrentSystem(Player);
        foreach (var item in _content.Items)
        {
            int pct = OreRunway.PercentLeft(Net, Player, item.Index);
            if (!OreRunway.IsLow(pct)) continue;
            string name = Loc.Name("item", item.Id);
            sb.Append(L("hud.oreLow", name, pct)).Append('\n');
            if (_lowOreWarned.Add((system, item.Index))) ToastLogistics(L("toast.oreLow", name, pct));
        }
    }

    /// <summary>A research name, with the level for repeatable nodes (the level being researched when next is true, the level reached otherwise).</summary>
    private string ResearchName(ResearchDef r, bool next)
    {
        string name = Loc.Name("research", r.Id);
        if (r.MaxLevels <= 1) return name;
        return L("hud.level", name, next ? Net.ResearchLevel(r.Index) + 1 : Net.ResearchLevel(r.Index), r.MaxLevels);
    }

    /// <summary>Damaged machines the ship can reach, worst first.</summary>
    private List<int> DamagedInRange()
    {
        var list = new List<int>();
        for (int id = 0; id < Net.MachineIdLimit; id++)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            if (m.Hp >= Net.MaxHp(m.Type)) continue;
            if (!_world.InBuildRange(Player, m.Structure, m.X, m.Y, m.Z)) continue;
            list.Add(id);
        }
        return list.OrderBy(id => Net.GetMachineInfo(id).Hp / (float)Net.MaxHp(Net.GetMachineInfo(id).Type)).ToList();
    }

    /// <summary>Repairs every damaged machine in reach, worst first; the hold pays and what it cannot pay for stays damaged.</summary>
    private void RepairAll()
    {
        var targets = DamagedInRange();
        if (targets.Count == 0) { ToastLogistics(L("toast.nothingToRepair")); return; }
        foreach (int id in targets) Send(new Repair(id));
    }

    /// <summary>The key that picks build item i: 1 to 9, then 0, then Q.</summary>
    private string KeyLabel(int i) => _palette[i].IsModule ? "N" : _palette[i].IsStation ? "I" : i < 9 ? (i + 1).ToString() : i == 9 ? "0" : i == 10 ? "Q" : i == 11 ? "B" : i == 12 ? "U" : i < DirectKeys + 4 ? $"F{i - DirectKeys + 2}" : "";

    private string ItemName(BuildItem item) =>
        item.IsStation ? L("ui.foundStation") : item.IsModule ? ModuleName() : item.IsBelt ? L("ui.belt", (int)item.Tier + 1) : MachineName(item.Machine);

    private bool ItemLocked(BuildItem item) =>
        !item.IsModule && !item.IsStation && (item.IsBelt ? Net.IsBeltLocked(item.Tier) : Net.IsMachineLocked(item.Machine));

    private ItemStack[] ItemCost(BuildItem item) =>
        item.IsStation ? _content.Structures?.ModuleCost(ModuleKind.Core) ?? Array.Empty<ItemStack>()
        : item.IsModule ? (_structuresOn ? _content.Structures?.ModuleCost(_moduleKind) ?? Array.Empty<ItemStack>() : Net.NextModuleCost(Player))
        : item.IsBelt ? _content.Belts[(int)item.Tier]?.CostPerCell ?? Array.Empty<ItemStack>() : _content.Machines[item.Machine].Cost;

    private void UpdateButtons()
    {
        // Build bar.
        for (int i = 0; i < _buildButtons.Count; i++)
        {
            var item = _palette[i];
            bool locked = ItemLocked(item);
            var cost = ItemCost(item);
            bool affordable = Net.CanPay(Player, cost);
            string name = ItemName(item);
            var b = _buildButtons[i];
            b.Text = $"{KeyLabel(i)} {name}";
            b.Disabled = locked;
            b.SetPressedNoSignal(_tool == ToolMode.Build && _selected == i);
            b.Modulate = !locked && !affordable ? new Color(1f, 0.75f, 0.7f) : Colors.White;
            b.TooltipText = BuildTooltip(item, cost, locked, affordable);
        }
        UpdateToolLabel();

        // Actions.
        var player = Net.GetPlayerInfo(Player);
        bool mining = Net.MiningDeposit(Player) >= 0 || _pendingMine;
        _btnMine.Text = mining ? L("btn.mineStop") : L("btn.mine");
        _btnMine.SetPressedNoSignal(mining);
        _btnMine.TooltipText = L("tip.mine");

        int shipRecipe = _content.RecipeIndex("core:ship_smelt");
        bool refining = player.ShipRecipe == shipRecipe;
        _btnRefine.Text = refining ? L("btn.refineStop") : L("btn.refine");
        _btnRefine.SetPressedNoSignal(refining);
        _btnRefine.TooltipText = L("tip.refine");

        int cooldown = Net.ScanCooldown(Player);
        _btnScan.Text = cooldown > 0 ? L("btn.scanWait", (cooldown / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture)) : L("btn.scan");
        _btnScan.Disabled = cooldown > 0;
        _btnScan.TooltipText = L("tip.scan");

        _btnMap.Text = L("btn.map");
        _btnMap.SetPressedNoSignal(_mapOpen);
        _btnMap.Visible = Net.Galaxy is not null;
        _btnMap.TooltipText = L("tip.map");

        _btnWeapons.Visible = Net.ThreatActive;
        _btnWeapons.Text = player.Armed ? L("btn.weaponsOff") : L("btn.weapons");
        _btnWeapons.SetPressedNoSignal(player.Armed);
        _btnWeapons.TooltipText = L("tip.weapons");

        var damagedNow = DamagedInRange();
        _btnRepair.Visible = Net.ThreatActive;
        _btnRepair.Text = L("btn.repair", damagedNow.Count);
        _btnRepair.Disabled = damagedNow.Count == 0;
        _btnRepair.TooltipText = damagedNow.Count == 0 ? L("tip.repairNone") : L("tip.repair", Stacks(damagedNow.SelectMany(id => Net.RepairCost(id)).GroupBy(s => s.Item).Select(g => new ItemStack(g.Key, g.Sum(s => s.Count))).ToArray()));

        _btnResearch.Text = Net.CurrentResearch >= 0 ? L("btn.researchNext") : L("btn.research");
        _btnResearch.TooltipText = L("tip.research");

        _btnJump.Visible = _mapOpen && Net.Galaxy is not null;
        _btnJump.Text = L("btn.jump");
        _btnJump.Disabled = _selectedSystem < 0 || Net.JumpRemaining(Player) > 0;
        _btnJump.TooltipText = L("tip.jump");

        _btnObjectives.Text = _objectivesOpen ? L("btn.hideSteps") : L("btn.showSteps");
        _btnDetails.Text = _detailsOpen ? L("btn.detailsHide") : L("btn.details");
        _btnHelp.Text = L("btn.help");
        _btnLang.Text = Loc.Lang == Language.Es ? "ES | en" : "es | EN";
    }

    /// <summary>The selected tool and what it costs, green when the hold can pay, red when it cannot, grey when locked.</summary>
    private void UpdateToolLabel()
    {
        if (_tool != ToolMode.Build)
        {
            _toolLabel.Text = L("hud.tool.pointer");
            _toolLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.95f));
            return;
        }
        var item = _palette[_selected];
        bool locked = ItemLocked(item);
        var cost = ItemCost(item);
        var state = HudFormat.Tool(locked, cost.Length > 0, Net.CanPay(Player, cost));
        string name = ItemName(item);
        string costText = state switch
        {
            ToolState.Locked => L("ui.locked"),
            ToolState.Free => L("ui.free"),
            _ => Stacks(cost) + (item.IsBelt ? L("ui.perCell") : ""),
        };
        _toolLabel.Text = L("hud.tool", name, costText);
        _toolLabel.AddThemeColorOverride("font_color", state switch
        {
            ToolState.Short => BadColor,
            ToolState.Locked => new Color(0.6f, 0.62f, 0.68f),
            _ => GoodColor,
        });
    }

    private string BuildTooltip(BuildItem item, ItemStack[] cost, bool locked, bool affordable)
    {
        var sb = new StringBuilder();
        sb.Append(ItemName(item)).Append('\n');
        sb.Append(L("tip.cost", cost.Length == 0 ? L("ui.free") : Stacks(cost) + (item.IsBelt ? L("ui.perCell") : ""))).Append('\n');
        if (item.IsStation) sb.Append(L("tip.foundStation")).Append('\n');
        else if (item.IsModule && _structuresOn) sb.Append(L("module." + _moduleKind.ToString().ToLowerInvariant() + ".purpose")).Append('\n');
        else if (item.IsModule) sb.Append(L("tip.module", ModuleName())).Append('\n');
        else if (!item.IsBelt)
        {
            var def = _content.Machines[item.Machine];
            sb.Append(L("tip.role." + def.Role.ToString().ToLowerInvariant())).Append('\n');
            if (def.Signature > 0) sb.Append(L("tip.signature", def.Signature)).Append('\n');
        }
        else sb.Append(L("tip.beltSpeed", BeltTiers.ItemsPerSecond(item.Tier).ToString("F0", Loc.Culture))).Append('\n');
        if (locked) sb.Append(L("tip.locked"));
        else if (!affordable) sb.Append(L("tip.needItems"));
        return sb.ToString().TrimEnd('\n');
    }

    private void UpdateModeHint()
    {
        if (_demo) { _modeHint.Text = ""; return; }
        if (_tool == ToolMode.Build)
        {
            var item = _palette[_selected];
            string name = ItemName(item);
            _modeHint.Text = L(item.IsStation ? "hint.foundStation" : item.IsModule && _structuresOn ? "hint.buildModuleKind" : item.IsModule ? "hint.buildModule" : item.IsBelt ? "hint.buildBelt" : "hint.buildMachine", name);
        }
        else _modeHint.Text = L(Standing ? "hint.pointerWalk" : "hint.pointer");
    }

    private void UpdateObjectivesPanel()
    {
        _objectivesPanel.Visible = true;
        var states = _objectives.States;
        var current = _objectives.Current;
        var sb = new StringBuilder();
        sb.Append(L("obj.title", _objectives.DoneCount, states.Count)).Append('\n');
        if (_objectivesOpen)
        {
            if (current is null) sb.Append(L("obj.allDone"));
            else
            {
                // The step in progress with its hint, then a short look at what comes next.
                var cur = states.First(s => s.Objective == current);
                sb.Append("> ").Append(L(cur.TitleKey)).Append('\n').Append("   ").Append(L(cur.HintKey)).Append('\n');
                foreach (var s in states.Where(s => !s.Done && s.Objective != current).Take(3))
                    sb.Append("  ○ ").Append(L(s.TitleKey)).Append('\n');
            }
            _objectivesText.Text = sb.ToString().TrimEnd('\n');
            _objectivesText.Visible = true;
        }
        else _objectivesText.Visible = false;
    }

    private void UpdateHelp()
    {
        _helpPanel.Visible = _helpOpen;
        if (_helpOpen) _helpText.Text = L("help.text");
    }

    private void UpdateToasts()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        var visible = _toasts.Visible(now);
        for (int i = 0; i < _toastLabels.Length; i++)
        {
            var label = _toastLabels[i];
            label.Visible = i < visible.Count;
            if (i >= visible.Count) continue;
            var t = visible[i];
            label.Text = (t.Category == ToastCategory.Threat ? "! " : "") + t.Display;
            label.AddThemeColorOverride("font_color", t.Category switch
            {
                ToastCategory.Production => ToastProductionColor,
                ToastCategory.Logistics => ToastLogisticsColor,
                ToastCategory.Threat => ToastThreatColor,
                _ => ToastInfo,
            });
        }
    }

    /// <summary>The panel for the machine under the crosshair (pointer tool only; a pinned machine shows in the inspector instead).</summary>
    private void UpdateTarget()
    {
        bool show = !_demo && _mouseLook && _tool == ToolMode.Pointer && _hover.Kind == HoverKind.Machine
            && _hover.Id != _selMachine && Net.IsMachineAlive(_hover.Id);
        _targetPanel.Visible = show;
        if (!show) return;

        var m = Net.GetMachineInfo(_hover.Id);
        var def = _content.Machines[m.Type];
        int max = Net.MaxHp(m.Type);
        _targetTitle.Text = MachineName(m.Type);
        _targetRole.Text = L("tip.role." + def.Role.ToString().ToLowerInvariant());
        _targetStatus.Text = MachineStatus(m);
        _targetStatus.AddThemeColorOverride("font_color", m.Crafting ? GoodColor : Colors.White);
        _targetRecipe.Visible = m.Recipe >= 0;
        if (m.Recipe >= 0)
        {
            var r = _content.Recipes[m.Recipe];
            _targetRecipe.Text = $"{Loc.Name("recipe", r.Id)}: {(r.Inputs.Length == 0 ? "-" : Stacks(r.Inputs))} > {Stacks(r.Outputs)}";
        }
        // The power of the structure the machine stands in (the ship's own power is also in the status panel).
        _targetPower.Visible = _structuresOn && m.Structure != 0;
        if (_targetPower.Visible)
        {
            var pst = Net.PowerStatus(m.Structure);
            _targetPower.Text = StructurePowerLine(m.Structure, L(Net.GetStructure(m.Structure).Kind == StructureKind.Ship ? "structure.ship" : "structure.station"));
            _targetPower.AddThemeColorOverride("font_color", BandColor(PowerFormat.Band(pst.SatisfactionPermille)));
        }
        _targetHp.MaxValue = Math.Max(1, max);
        _targetHp.Value = m.Hp;
        var band = HudFormat.Band(m.Hp, max);
        var bandColor = band == HealthBand.Good ? GoodColor : band == HealthBand.Worn ? WornColor : BadColor;
        _targetHp.AddThemeStyleboxOverride("fill", Box(bandColor, null, 3, 0f));
        _targetHp.AddThemeStyleboxOverride("background", Box(new Color(0.12f, 0.14f, 0.2f), null, 3, 0f));
        _targetHpLabel.Text = L("tip.hp", m.Hp, max);
        _targetHpLabel.AddThemeColorOverride("font_color", bandColor);
    }

    private void LayoutUi(Vector2 size)
    {
        // The status text wraps so it never runs under the steps panel at the top right.
        _objectivesText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _objectivesText.CustomMinimumSize = new Vector2(Math.Clamp(size.X * 0.3f, 240, 420), 0);
        _objectivesPanel.ResetSize();
        var objMin = _objectivesPanel.GetCombinedMinimumSize();
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        // The essentials are short and narrow; the full block (H) is wide but never runs under the steps panel.
        _statusLabel.CustomMinimumSize = new Vector2(_detailsOpen ? Math.Clamp(size.X - objMin.X - 60, 260, 760) : Math.Min(300f, Math.Max(180f, size.X * 0.22f)), 0);
        _statusLabel.Visible = _statusLabel.Text.Length > 0;
        _statusPanel.Visible = _statusLabel.Visible || _powerBox.Visible;
        _statusPanel.ResetSize();
        _statusPanel.Position = new Vector2(12, 10);

        _actions.Position = new Vector2(12, _statusPanel.Visible ? _statusPanel.Position.Y + _statusPanel.Size.Y + 10 : 10);

        // The bottom strip shrinks to fit narrow windows instead of running off the screen, and hugs the bottom edge.
        _toolbar.ResetSize();
        var barSize = _toolbar.GetCombinedMinimumSize();
        float barScale = Math.Min(1f, (size.X - 24f) / Math.Max(1f, barSize.X));
        _toolbar.Scale = new Vector2(barScale, barScale);
        _toolbar.Position = new Vector2((size.X - barSize.X * barScale) / 2f, size.Y - barSize.Y * barScale - 6);
        barSize = new Vector2(barSize.X * barScale, barSize.Y * barScale);

        _modeHint.Position = new Vector2(Math.Max(12, (size.X - _modeHint.Size.X) / 2f), _toolbar.Position.Y - 24);

        // Notifications: up to three lines in a column above the hint, wrapped to a readable width.
        float toastWidth = Math.Clamp(size.X * 0.6f, 300f, 760f);
        foreach (var l in _toastLabels) l.CustomMinimumSize = new Vector2(toastWidth, 0);
        _toastBox.ResetSize();
        _toastBox.Position = new Vector2((size.X - toastWidth) / 2f, _modeHint.Position.Y - 8 - _toastBox.Size.Y);

        var objSize = _objectivesPanel.GetCombinedMinimumSize();
        _objectivesPanel.Position = new Vector2(size.X - objSize.X - 12, 10);
        float y = _objectivesPanel.Position.Y + objSize.Y + 10;
        if (_mapOpen && Net.Galaxy is not null)
        {
            _map.Position = new Vector2(size.X - _map.Size.X - 12, y);
            _mapList.Position = new Vector2(size.X - 380, _map.Position.Y + _map.Size.Y + 6);
            _btnJump.Position = new Vector2(size.X - 162, _mapList.Position.Y + _mapList.Size.Y + 6);
        }
        _map.Visible = _mapOpen && Net.Galaxy is not null;
        _mapList.Visible = _map.Visible;

        var inspSize = _inspector.GetCombinedMinimumSize();
        _inspector.Position = new Vector2(size.X - inspSize.X - 12, size.Y - inspSize.Y - barSize.Y - 14);

        // The flight readout sits at the top centre, dropping below the status or steps panel when they would meet it.
        _flightPanel.ResetSize();
        var fs = _flightPanel.GetCombinedMinimumSize();
        var fp = new Vector2((size.X - fs.X) / 2f, 8f);
        foreach (var other in new[] { _statusPanel, _objectivesPanel })
        {
            if (!other.Visible) continue;
            var o = new Rect2(other.Position, other.Size);
            if (new Rect2(fp, fs).Intersects(o)) fp.Y = Math.Max(fp.Y, o.End.Y + 6f);
        }
        _flightPanel.Position = fp;

        var helpSize = _helpPanel.GetCombinedMinimumSize();
        _helpPanel.Position = new Vector2((size.X - helpSize.X) / 2f, (size.Y - helpSize.Y) / 2f - 40);

        // The crosshair sits at the screen centre and the tooltip beside it, kept on screen.
        _crosshair.Visible = AimActive && !_demo;
        _crosshair.Position = (size - _crosshair.Size) / 2f;
        var centre = size / 2f;
        _tooltipLabel.Visible = _tooltipLabel.Text.Length > 0 && !_demo && AimActive;
        _tooltipLabel.Position = new Vector2(Math.Clamp(centre.X + 24, 8, Math.Max(8, size.X - _tooltipLabel.Size.X - 8)), Math.Clamp(centre.Y + 24, 8, Math.Max(8, size.Y - _tooltipLabel.Size.Y - 8)));

        _seatPrompt.Text = SeatPromptText();
        _seatPrompt.Visible = _seatPrompt.Text.Length > 0 && AimActive;
        _seatPrompt.ResetSize();
        _seatPrompt.Position = new Vector2(centre.X - _seatPrompt.Size.X / 2f, centre.Y - 46f);

        _targetPanel.ResetSize();
        var ts =_targetPanel.GetCombinedMinimumSize();
        _targetPanel.Position = new Vector2(Math.Clamp(centre.X + 28, 8, Math.Max(8, size.X - ts.X - 8)), Math.Clamp(centre.Y + 28, 8, Math.Max(8, size.Y - ts.Y - barSize.Y - 14)));
        _markers.Size = size;
    }

    // ---- selecting tools ------------------------------------------------------------------------------

    private void SelectBuild(int index)
    {
        if (index < 0 || index >= _palette.Count) return;
        // Pressing the same number again keeps it selected (Esc puts it away): a double press while building must not undo the choice.
        _selected = index;
        _beltAnchor = null;
        _dragStart = null;
        _tool = ToolMode.Build;
        _selMachine = -1;
        _waypoint = null;
    }

    private void ToggleLanguage()
    {
        Loc.Lang = Loc.Lang == Language.Es ? Language.En : Language.Es;
        SettingsStore.Set(SettingsStore.Current with { Language = Loc.Lang });
        Toast(L("toast.language"));
    }

    // ---- mouse and keyboard ------------------------------------------------------------------------------

    private bool OverUi() => !_mouseLook && GetViewport().GuiGetHoveredControl() is { } c && c != _uiRoot;

    public override void _UnhandledInput(InputEvent e)
    {
        if (_demo || PauseOpen) return;

        switch (e)
        {
            case InputEventMouseButton mb:
                OnMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                OnMouseMotion(mm);
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                OnKey(k);
                break;
        }
    }

    private void OnMouseButton(InputEventMouseButton mb)
    {
        if (!mb.Pressed && mb.ButtonIndex != MouseButton.Left) return;
        // A click while the cursor is free (and no panel is open) takes the mouse back instead of acting in the world.
        if (!_mouseLook)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left && !PanelOpen()) _wantCapture = true;
            _dragStart = null;
            return;
        }
        switch (mb.ButtonIndex)
        {
            case MouseButton.Right:
                if (_beltAnchor is not null) { _beltAnchor = null; _dragStart = null; break; } // cancels a belt line in progress
                // Right click demolishes what the crosshair is on.
                if (_hover.Kind == HoverKind.Machine) RemoveAt(CellOfMachine(_hover.Id));
                else if (CellUnderAim() is { } cell) RemoveAt(cell);
                break;
            case MouseButton.Left:
                if (mb.Pressed) _dragStart = _tool == ToolMode.Build ? (_beltAnchor ?? CellUnderAim()) : null;
                else OnLeftRelease();
                break;
        }
    }

    private Cell CellOfMachine(int id)
    {
        var m = Net.GetMachineInfo(id);
        return new Cell(m.Structure, m.X, m.Y, m.Z);
    }

    private void OnMouseMotion(InputEventMouseMotion mm)
    {
        if (!_mouseLook) return;
        var settings = SettingsStore.Current;
        float sens = MouseSensitivity * settings.MouseSensitivity;
        _yaw -= mm.Relative.X * sens;
        _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * sens * (settings.InvertMouseY ? -1f : 1f), -MaxPitch, MaxPitch);
    }

    private void OnLeftRelease()
    {
        if (_tool == ToolMode.Build)
        {
            if (StationSelected)
            {
                if (_foundAim is { } f && FoundCellUnderAim() is { } now) Send(new FoundStation(now.X, now.Y, now.Z));
                _dragStart = null;
                return;
            }
            if (ModuleSelected)
            {
                if (_structuresOn)
                {
                    if (StructModuleUnderAim() is { } sm) Send(new PlaceModuleIn(sm.S, sm.Mx, sm.My, sm.Mz, _moduleKind));
                }
                else if (ModuleUnderAim() is { } m) Send(new PlaceModule(m.Mx, m.My, m.Mz));
                _dragStart = null;
                return;
            }
            if (_dragStart is { } start && CellUnderAim() is { } end)
            {
                bool belt = _palette[_selected].IsBelt;
                if (belt && start.S != end.S) { _beltAnchor = null; _dragStart = null; return; } // a belt line stays inside one structure
                if (belt && _beltAnchor is null && end == start)
                {
                    // A plain click starts a line: aim at the end and click again (dragging still works too).
                    _beltAnchor = start;
                    _dragStart = start;
                    return;
                }
                PlaceSelectedAt(end, belt ? start : null);
            }
            _beltAnchor = null;
            _dragStart = null;
            return;
        }

        // Pointer: inspect machines and pick deposits.
        switch (_hover.Kind)
        {
            case HoverKind.Machine:
                _selMachine = _hover.Id;
                break;
            case HoverKind.Deposit:
                _selectedDeposit = _hover.Id;
                _selMachine = -1;
                ToastProduction(L("toast.depositSelected", ItemName(Net.DepositItem(_hover.Id))));
                break;
            default:
                _selMachine = -1;
                break;
        }
    }

    // ---- cursor capture ------------------------------------------------------------------------------------

    /// <summary>Panels that need the pointer: the help overlay and the star map.</summary>
    private bool PanelOpen() => PauseOpen || _helpOpen || (_mapOpen && Net.Galaxy is not null);

    /// <summary>Releases or captures the mouse so it matches what the player wants and what is on screen.</summary>
    private void UpdateCapture()
    {
        bool interactive = !_demo && _stressMachines == 0 && !_saveTest;
        bool focused = DisplayServer.WindowIsFocused();
        if (!focused) _wantCapture = false; // clicking in the window takes it back
        bool want = interactive && _wantCapture && focused && !PanelOpen();
        if (want == _mouseLook) return;
        SetMouseCapture(want);
    }

    private void SetMouseCapture(bool on)
    {
        _mouseLook = on;
        Input.MouseMode = on ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (!on)
        {
            // Letting go of the mouse stops the ship and any autopilot run: no thrust is left running unattended.
            _waypoint = null;
            _beltAnchor = null;
            _pendingMine = false;
            _dragStart = null;
            SendThrust((0, 0, 0), force: true);
        }
    }

    private void OnKey(InputEventKey k)
    {
        int digit = k.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)(k.Keycode - Key.Key1) : -1;
        if (k.Keycode == Key.Key0) digit = 9;
        if (k.Keycode == Key.Q) digit = 10;
        if (k.Keycode == Key.B) digit = 11;
        if (k.Keycode == Key.U) digit = 12; // the thirteenth entry, the ore port
        if (k.Keycode is >= Key.F2 and <= Key.F5) digit = DirectKeys + (int)(k.Keycode - Key.F2); // the entries past the direct keys
        if (digit >= 0 && digit < _palette.Count && !_palette[digit].IsModule && !_palette[digit].IsStation) { SelectBuild(digit); return; }
        if (k.Keycode == Key.N)
        {
            if (_structuresOn) { CycleModuleKind(); return; } // first press: the module tool; next ones: the kind
            int module = _palette.FindIndex(b => b.IsModule);
            if (module >= 0) SelectBuild(module);
            return;
        }
        if (k.Keycode == Key.I && _structuresOn)
        {
            int station = _palette.FindIndex(b => b.IsStation);
            if (station >= 0) SelectBuild(station);
            return;
        }

        switch (k.Keycode)
        {
            case Key.Escape:
                if (_helpOpen) _helpOpen = false;
                else if (_beltAnchor is not null) { _beltAnchor = null; _dragStart = null; } // first Esc cancels a belt line in progress
                else if (_selMachine >= 0) _selMachine = -1;
                else if (_tool == ToolMode.Build) _tool = ToolMode.Pointer;
                else if (_mapOpen) _mapOpen = false;
                else { _waypoint = null; _pendingMine = false; _wantCapture = false; OpenPause(); } // last: let go of the mouse and open the pause menu
                break;
            case Key.F1: _helpOpen = !_helpOpen; break;
            case Key.F11:
                SettingsStore.Set(SettingsStore.Current with { Fullscreen = DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen });
                break;
            case Key.F6: SaveGameToDisk("quicksave", silent: false); break;
            case Key.F9: LoadGameFromDisk(); break;
            case Key.F7: AssignDronesKey(); break;
            case Key.L: ToggleLanguage(); break;
            case Key.O: _objectivesOpen = !_objectivesOpen; break;
            case Key.H: _detailsOpen = !_detailsOpen; break;
            case Key.Tab: _wantCapture = !_wantCapture; break; // free the cursor to use the buttons, or take it back
            case Key.Y: CycleRecipe(); break;
            case Key.R: _dir = _dir switch { Dir.PosX => Dir.PosZ, Dir.PosZ => Dir.NegX, Dir.NegX => Dir.NegZ, _ => Dir.PosX }; Toast(L("toast.beltFacing", _dir.ToString())); break;
            case Key.Z: _cursorY--; break;
            case Key.X: _cursorY++; break;
            case Key.M: ToggleMining(); break;
            case Key.E: ToggleSeat(); break;
            case Key.Backspace: RepairAll(); break;
            case Key.Bracketleft: CycleDeposit(-1); break;
            case Key.Bracketright: CycleDeposit(1); break;
            case Key.K: Send(new Scan()); break;
            case Key.V: ToggleWeapons(); break;
            case Key.G: _mapOpen = !_mapOpen; break;
            case Key.J: SendJump(); break;
            case Key.Up: CycleSystem(-1); break;
            case Key.Down: CycleSystem(1); break;
            case Key.F: ToggleRefinery(); break;
            case Key.T: CycleResearch(); break;
            case Key.P: _paused = !_paused; break;
            case Key.Equal: case Key.KpAdd: _speed = Math.Min(10, _speed + 1); break;
            case Key.Minus: case Key.KpSubtract: _speed = Math.Max(1, _speed - 1); break;
        }
    }

    // ---- ship control ------------------------------------------------------------------------------------

    /// <summary>
    /// Turns a direction into the integer thrust of <see cref="SetThrust"/>: normalised so it is never longer than 1, scaled by 1024,
    /// and guaranteed to satisfy <see cref="PlayerRules.ThrustInRange"/> whatever the float rounding does.
    /// </summary>
    private static (int X, int Y, int Z) ToThrust(Vector3 v)
    {
        if (v.LengthSquared() > 1f) v = v.Normalized();
        var t = ((int)MathF.Round(v.X * 1024f), (int)MathF.Round(v.Y * 1024f), (int)MathF.Round(v.Z * 1024f));
        if (!PlayerRules.ThrustInRange(t.Item1, t.Item2, t.Item3)) t = ((int)(v.X * 1024f), (int)(v.Y * 1024f), (int)(v.Z * 1024f)); // truncate: never longer
        while (!PlayerRules.ThrustInRange(t.Item1, t.Item2, t.Item3)) t = (t.Item1 * 15 / 16, t.Item2 * 15 / 16, t.Item3 * 15 / 16);
        return t;
    }

    private void SendThrust((int X, int Y, int Z) thrust, bool force = false)
    {
        if (!force && thrust == _lastThrust) return;
        double now = Time.GetTicksMsec() / 1000.0;
        // The simulation takes one command per tick at most (30 a second): keep a changing direction from flooding the queue; a stop is never held back.
        if (!force && thrust != (0, 0, 0) && now - _lastThrustAt < 1.0 / 30.0) return;
        _lastThrust = thrust;
        _lastThrustAt = now;
        Send(new SetThrust(thrust.X, thrust.Y, thrust.Z));
    }

    /// <summary>
    /// W/S fly along the view, A/D strafe along the camera's right, Space/C go up and down; the sum is normalised and sent as thrust
    /// whenever the integer vector changes. M's autopilot steers toward its waypoint the same way.
    /// </summary>
    private void UpdateShipControl()
    {
        if (_demo) return;

        // A jump or respawn clears the thrust in the simulation: send held keys again once the ship can move.
        if (Net.HasPlayer(Player) && _world.PendingCommands == 0)
        {
            var pi = Net.GetPlayerInfo(Player);
            if (pi.JumpRemaining == 0 && pi.RespawnRemaining == 0 && (pi.ThrustX, pi.ThrustY, pi.ThrustZ) != _lastThrust) _lastThrust = (int.MinValue, 0, 0);
        }

        if (Standing)
        {
            // Standing: no flight. WASD walk (see UpdateBody); the autopilot is off and the ship holds still.
            _waypoint = null;
            _pendingMine = false;
            SendThrust((0, 0, 0), force: _lastThrust != (0, 0, 0));
            return;
        }

        var thrust = (0, 0, 0);
        if (_mouseLook)
        {
            int forward = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
            int right = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            int up = (Input.IsKeyPressed(Key.Space) ? 1 : 0) - (Input.IsKeyPressed(Key.C) ? 1 : 0);
            if (forward != 0 || right != 0 || up != 0)
            {
                _waypoint = null; // flying by hand takes over from the autopilot
                _pendingMine = false;
                thrust = ToThrust(CockpitForward() * forward + CockpitRight() * right + Vector3.Up * up);
            }
        }
        if (thrust == (0, 0, 0) && _waypoint is { } wp)
        {
            var to = wp - ShipPosition(0f);
            if (to.Length() < 0.25f) _waypoint = null; // arrived
            else thrust = ToThrust(to.Normalized());
        }

        if (_pendingMine && Net.IsDeposit(_selectedDeposit) && Net.InMineRange(Player, _selectedDeposit))
        {
            _pendingMine = false;
            _waypoint = null;
            thrust = (0, 0, 0);
            Send(new StartMining(_selectedDeposit));
        }

        SendThrust(thrust, force: thrust == (0, 0, 0) && _lastThrust != (0, 0, 0));
    }

    /// <summary>The M button: fly to the selected deposit if needed, then mine it.</summary>
    private void ToggleMining()
    {
        if (Net.MiningDeposit(Player) >= 0 || _pendingMine)
        {
            _pendingMine = false;
            Send(new StopMining());
            ToastProduction(L("toast.miningStopped"), 4.0);
            return;
        }
        if (Standing) { ToastLogistics(L("toast.sitToFly")); return; } // mining may need the autopilot: sit at the pilot seat first
        if (!Net.IsDeposit(_selectedDeposit) || !Net.IsDepositRevealed(_selectedDeposit))
        {
            var nearest = RevealedDeposits().FirstOrDefault(-1);
            if (nearest < 0) { ToastLogistics(L("toast.noDeposits")); return; }
            _selectedDeposit = nearest;
        }

        if (Net.InMineRange(Player, _selectedDeposit)) { Send(new StartMining(_selectedDeposit)); return; }

        // Out of range: fly to a point a few cells from the deposit on the ship's side, mine on arrival.
        var pos = Net.DepositPosition(_selectedDeposit);
        var dep = new Vector3(pos.X, pos.Y, pos.Z);
        var ship = ShipPosition(0f);
        var dir = (ship - dep);
        dir = dir.LengthSquared() < 0.01f ? Vector3.Right : dir.Normalized();
        _waypoint = dep + dir * (PlayerRules.MineRangeCells - 2);
        _pendingMine = true;
        ToastProduction(L("toast.flyingToMine", ItemName(Net.DepositItem(_selectedDeposit))));
    }

    // ---- hover and inspection ----------------------------------------------------------------------------

    private static bool RayHitsSphere(Vector3 origin, Vector3 dir, Vector3 centre, float radius, out float t)
    {
        var oc = centre - origin;
        t = oc.Dot(dir);
        if (t < 0) return false;
        float d2 = oc.LengthSquared() - t * t;
        return d2 <= radius * radius;
    }

    /// <summary>Ray against the axis-aligned box of one grid cell whose minimum corner is <paramref name="min"/>.</summary>
    private static bool RayHitsBox(Vector3 origin, Vector3 dir, Vector3 min, out float t)
    {
        float t0 = 0f, t1 = float.MaxValue;
        t = 0f;
        for (int a = 0; a < 3; a++)
        {
            float o = origin[a], d = dir[a], lo = min[a], hi = min[a] + 1f;
            if (MathF.Abs(d) < 1e-6f) { if (o < lo || o > hi) return false; continue; }
            float ta = (lo - o) / d, tb = (hi - o) / d;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta);
            t1 = MathF.Min(t1, tb);
            if (t0 > t1) return false;
        }
        t = t0;
        return true;
    }

    private Cell? _aimCell;

    private void UpdateHover()
    {
        _tooltipLabel.Text = "";
        _hover = new Hover(HoverKind.None, -1, "");
        _aimCell = null;
        if (_demo || !AimActive) return; // only the crosshair aims; a free cursor is for the buttons

        var origin = AimOrigin();
        var dir = AimDirection();

        // Machines first: the ray against each machine's cell box (the sphere tests below suit the small, round things).
        float best = float.MaxValue;
        foreach (int id in _machineIds)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            if (RayHitsBox(origin, dir, new Vector3(m.X, m.Y, m.Z) + StructOrigin(m.Structure), out float tb) && tb < best) // local cell + structure origin = world
            {
                best = tb;
                _hover = new Hover(HoverKind.Machine, id, MachineSummary(id));
            }
        }
        // Then enemies (small and dangerous) and deposits, whichever is nearer along the ray.
        foreach (var en in Net.Enemies())
        {
            var c = new Vector3(en.X, en.Y, en.Z) / CellUnits;
            if (RayHitsSphere(origin, dir, c, 0.7f, out float t) && t < best)
            {
                best = t;
                _hover = new Hover(HoverKind.Enemy, en.Id, L("tip.enemy", Loc.Name("enemy", _content.Enemies[en.Type].Id), en.Hp));
            }
        }
        for (int d = 0; d < Net.DepositCount; d++)
        {
            if (!Net.IsDepositRevealed(d) || Net.DepositRemaining(d) <= 0) continue;
            var pos = Net.DepositPosition(d);
            float scale = DepositView.RockScale(Net.DepositInitial(d));
            if (RayHitsSphere(origin, dir, new Vector3(pos.X, pos.Y, pos.Z), 1.5f * scale, out float t) && t < best)
            {
                best = t;
                _hover = new Hover(HoverKind.Deposit, d, L("tip.deposit", ItemName(Net.DepositItem(d)), Net.DepositRemaining(d), Net.DepositInitial(d), DensityText(d)));
            }
        }

        if (CellUnderAim() is { } cell)
        {
            _cursor = cell;
            _aimCell = cell;
        }
        if (_hover.Kind == HoverKind.None && _aimCell is { } aimed)
        {
            var (kind, id) = _world.EntityAt(aimed.S, aimed.X, aimed.Y, aimed.Z);
            if (kind == SimWorld.EntityKind.Machine) _hover = new Hover(HoverKind.Machine, id, MachineSummary(id));
            else if (kind == SimWorld.EntityKind.Segment)
            {
                var s = Net.GetSegmentInfo(id);
                _hover = new Hover(HoverKind.Segment, id, L("tip.belt", (int)s.Tier + 1, s.LengthCells, Net.CountOn(id)));
            }
            else _hover = new Hover(HoverKind.Ground, -1, "");
        }

        if (_tool == ToolMode.Build) return; // the ghost explains placement instead
        _tooltipLabel.Text = _hover.Kind == HoverKind.Machine ? "" : _hover.Text; // a machine gets the target panel instead
    }

    private string MachineStatus(MachineInfo m)
    {
        var def = _content.Machines[m.Type];
        if (NeedsPower(m.Type) && SatisfactionOf(m.Structure) == 0) return L("status.noPower");
        switch (def.Role)
        {
            case MachineRole.Drill:
                if (m.Deposit < 0) return L("status.drillUnlinked");
                if (Net.DepositRemaining(m.Deposit) <= 0) return L("status.drillEmpty");
                return m.Crafting ? L("status.working") : m.OutputItems > 0 ? L("status.outputBlocked") : L("status.drillIdle");
            case MachineRole.Lab:
                return Net.CurrentResearch < 0 ? L("status.labNone") : L("status.labWorking", Loc.Name("research", _content.Research[Net.CurrentResearch].Id));
            case MachineRole.Depot:
                if (Net.HoldTotal(Player) >= Net.HoldCapacity(Player)) return L("status.depotFull");
                foreach (var item in _content.Items)
                    if (Net.HoldCount(Player, item.Index) >= PlayerRules.DepotItemCap)
                        return L("status.depotCap", Loc.Name("item", item.Id), PlayerRules.DepotItemCap);
                return L("status.depotOk");
            case MachineRole.Wall:
                return L("status.wall");
            case MachineRole.Unloader:
            {
                var un = def.Unloader!;
                if (Net.MachineOutputCount(m.Id) == 0) return L("status.unloaderNoBelt");
                int held = Net.HoldCount(Player, un.Item);
                return held > un.Reserve ? L("status.unloaderReady", held, un.Reserve) : L("status.unloaderEmpty", un.Reserve);
            }
            case MachineRole.Repairer:
            {
                var rep = def.Repairer!;
                return m.InputItems > 0 ? L("status.repairerReady", rep.RangeCells, m.InputItems, rep.ItemCap) : L("status.repairerEmpty");
            }
            case MachineRole.Turret:
                var t = def.Turret!;
                return m.InputItems > 0 ? L("status.turretReady", m.InputItems, t.AmmoCap) : L("status.turretEmpty");
            default:
                if (m.Recipe < 0) return L("status.noRecipe");
                if (m.Crafting) return L("status.working");
                if (m.OutputItems > 0) return L("status.outputBlocked");
                return L("status.waitingInputs");
        }
    }

    private string MachineSummary(int id)
    {
        var m = Net.GetMachineInfo(id);
        var def = _content.Machines[m.Type];
        string recipe = m.Recipe >= 0 ? " - " + Loc.Name("recipe", _content.Recipes[m.Recipe].Id) : "";
        return $"{MachineName(m.Type)}{recipe}\n{MachineStatus(m)}   {L("tip.hp", m.Hp, Net.MaxHp(m.Type))}";
    }

    private void UpdateInspector()
    {
        if (_selMachine >= 0 && !Net.IsMachineAlive(_selMachine)) _selMachine = -1;
        _inspector.Visible = _selMachine >= 0 && !_demo;
        if (_selMachine < 0) return;

        var m = Net.GetMachineInfo(_selMachine);
        var def = _content.Machines[m.Type];
        _inspectorTitle.Text = MachineName(m.Type);

        var sb = new StringBuilder();
        sb.Append(MachineStatus(m)).Append('\n');
        sb.Append(L("tip.hp", m.Hp, Net.MaxHp(m.Type))).Append('\n');
        if (NeedsPower(m.Type) && SatisfactionOf(m.Structure) is > 0 and < PowerRules.FullSatisfaction)
            sb.Append(L("insp.power", PowerFormat.Percent(SatisfactionOf(m.Structure)))).Append('\n');
        if (m.Recipe >= 0)
        {
            var r = _content.Recipes[m.Recipe];
            sb.Append(L("insp.recipe", Loc.Name("recipe", r.Id))).Append('\n');
            sb.Append(L("insp.takes", r.Inputs.Length == 0 ? "-" : Stacks(r.Inputs))).Append('\n');
            sb.Append(L("insp.makes", Stacks(r.Outputs), (r.TimeTicks / (float)Units.TicksPerSecond).ToString("F1", Loc.Culture))).Append('\n');
        }
        if (def.Signature > 0) sb.Append(L("tip.signature", def.Signature)).Append('\n');
        _inspectorBody.Text = sb.ToString().TrimEnd('\n');

        var options = RecipesFor(m.Type);
        _inspPrev.Visible = _inspNext.Visible = options.Count > 1;
        _inspRemove.Text = L("insp.remove", Stacks(def.Cost));
        bool damaged = m.Hp < Net.MaxHp(m.Type);
        _inspRepair.Visible = damaged;
        if (damaged) _inspRepair.Text = L("insp.repair", Stacks(Net.RepairCost(_selMachine)));
    }

    private List<int> RecipesFor(int machineType) =>
        _content.Recipes.Where(r => r.Machine == machineType && !Net.IsRecipeLocked(r.Index)).Select(r => r.Index).ToList();

    private void ChangeSelectedRecipe(int step)
    {
        if (_selMachine < 0 || !Net.IsMachineAlive(_selMachine)) return;
        var m = Net.GetMachineInfo(_selMachine);
        var options = RecipesFor(m.Type);
        if (options.Count < 2) return;
        int i = options.IndexOf(m.Recipe);
        int next = options[((i < 0 ? 0 : i + step) % options.Count + options.Count) % options.Count];
        Send(new SetRecipe(_selMachine, next));
        // A drill follows its recipe to a deposit of the new ore, when one is within reach.
        if (_content.Machines[m.Type].Role == MachineRole.Drill) RelinkDrill(_selMachine, next);
    }

    private void RelinkDrill(int machine, int recipe)
    {
        var mi = Net.GetMachineInfo(machine);
        var item = _content.Recipes[recipe].Outputs[0].Item;
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

    // ---- build ghost ----------------------------------------------------------------------------------------

    /// <summary>Why the selected item cannot be placed in this cell right now, or null if it can.</summary>
    private string? PlacementProblem(Cell cell)
    {
        if (_structuresOn) return StructurePlacementProblem(cell);
        var item = _palette[_selected];
        if (ItemLocked(item)) return L("why.locked");
        if (!_world.InBuildRange(Player, cell.X, cell.Y, cell.Z)) return L("why.outOfRange");
        if (_stationRules && !Net.CellInStation(Player, cell.X, cell.Y, cell.Z)) return Why(Reject.OutsideStation);
        if (_world.EntityAt(cell.X, cell.Y, cell.Z).Kind != SimWorld.EntityKind.None) return L("why.overlap");
        var cost = item.IsBelt ? _content.Belts[(int)item.Tier]?.CostPerCell ?? Array.Empty<ItemStack>() : _content.Machines[item.Machine].Cost;
        if (!Net.CanPay(Player, cost)) return L("why.notEnough");
        return null;
    }

    /// <summary>The module ghost fills the whole 8 x 8 x 4 box it would occupy; green when PlaceModule would succeed, red with the reason otherwise.</summary>
    private void UpdateModuleGhost()
    {
        _dragGhost.Visible = false;
        _moduleAim = ModuleUnderAim();
        if (_moduleAim is not { } m) { _ghost.Visible = false; _tooltipLabel.Text = L("why.outOfRange"); _crosshair.Tint = Crosshair.Invalid; return; }
        _ghost.Scale = new Vector3(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ) * 0.99f;
        _ghost.Position = StationView.Centre(m.Mx, m.My, m.Mz);
        string? problem = ModuleProblem(m);
        _crosshair.Tint = problem is null ? Crosshair.Normal : Crosshair.Invalid;
        ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = problem is null ? new Color(0.4f, 1f, 0.5f, 0.22f) : new Color(1f, 0.3f, 0.25f, 0.22f);
        _tooltipLabel.Text = problem ?? $"{ModuleName()}: {L("tip.cost", Stacks(Net.NextModuleCost(Player)))}";
    }

    private void UpdateGhost()
    {
        bool structTool = _structuresOn && (ModuleSelected || StationSelected); // these aim at structures or free space, not at one layer cell
        bool build = _tool == ToolMode.Build && !_demo && AimActive && (_aimCell is not null || structTool);
        _ghost.Visible = build;
        _crosshair.Tint = Crosshair.Normal;
        if (!build) { _dragGhost.Visible = false; return; }

        if (StationSelected) { UpdateFoundGhost(); return; }
        if (ModuleSelected) { if (_structuresOn) UpdateStructModuleGhost(); else UpdateModuleGhost(); return; }
        _ghost.Scale = Vector3.One;
        var c = _aimCell!.Value;
        Reparent(_ghost, RootOf(c.S)); // local cells under the structure root
        _ghost.Position = CellCenter(c.X, c.Y, c.Z);
        string? problem = PlacementProblem(c);
        _crosshair.Tint = problem is null ? Crosshair.Normal : Crosshair.Invalid;
        ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = problem is null ? new Color(0.4f, 1f, 0.5f, 0.4f) : new Color(1f, 0.3f, 0.25f, 0.4f);
        if (problem is not null) _tooltipLabel.Text = problem;
        else if (_dragStart is null && _palette[_selected].IsBelt)
        {
            var cost = _content.Belts[(int)_palette[_selected].Tier]?.CostPerCell ?? Array.Empty<ItemStack>();
            _tooltipLabel.Text = L("tip.cost", Stacks(cost) + L("ui.perCell"));
        }
        UpdateDragGhost();
    }
}
