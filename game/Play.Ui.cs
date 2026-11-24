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
    /// <summary>The mining laser is the weapon in hand (M): holding the left button fires it at the deposit under the crosshair.</summary>
    private bool _laserOn;
    private bool _laserOutOfRangeToasted;
    private const float DefaultDistance = 10f; // overview camera of the scripted modes
    private const float MouseSensitivity = 0.0025f; // radians per pixel
    private const float MaxPitch = 85f * MathF.PI / 180f;
    private bool _wantCapture = true; // the player's wish; the cursor is also released while a panel is open or the window is unfocused
    private bool _mouseLook; // true while the cursor is captured and the mouse turns the ship's view
    private readonly ThrustThrottle _throttle = new(); // what is sent to the simulation: quantised and at most 10 changes a second
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
    private StepStrip _stepStrip = null!;
    private Label _statusLabel = null!, _modeHint = null!, _objectivesText = null!, _tooltipLabel = null!;
    private Label _inspectorTitle = null!, _inspectorBody = null!, _helpText = null!;
    private ScrollContainer _helpScroll = null!;
    private ToolReadout _toolLabel = null!;
    private HoldStrip _holdItems = null!;
    private Label _flightFrame = null!;
    private IconCount _flightSpeed = null!;
    private NavGizmo _navGizmo = null!;
    private Label _targetTitle = null!, _targetRole = null!, _targetStatus = null!, _targetRecipe = null!, _targetHpLabel = null!;
    private ProgressBar _targetHp = null!;
    private PanelContainer _flightPanel = null!, _targetPanel = null!;
    private VBoxContainer _toastBox = null!;
    private readonly Label[] _toastLabels = new Label[ToastQueue.MaxVisible];
    private float _toastMax = 600f;
    private readonly IconRect[] _toastIcons = new IconRect[ToastQueue.MaxVisible];
    private readonly HBoxContainer[] _toastRows = new HBoxContainer[ToastQueue.MaxVisible];
    private MarkerLayer _markers = null!;
    private Button _btnDetails = null!;
    private Crosshair _crosshair = null!;
    private Button _btnMine = null!, _btnRefine = null!, _btnScan = null!, _btnMap = null!, _btnWeapons = null!, _btnResearch = null!;
    private Button _btnRepair = null!;
    private Label _seatPrompt = null!;
    private PanelContainer _tooltipPanel = null!;
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
        stacks.Length == 0 ? L("ui.free") : string.Join(", ", stacks.Select(s => $"{ItemName(s.Item)} x{s.Count}")); // "iron ingot x2": the names have no plural form, so the count goes after

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

    /// <summary>The one look of every HUD card: a soft dark glass with a thin cool edge, rounded corners and a faint shadow.</summary>
    private static StyleBoxFlat HudCard(Color? edge = null)
    {
        return UiTheme.Card(edge);
    }

    private PanelContainer MakePanel(Control content)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", HudCard());
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
        _uiRoot.Theme = UiTheme.Create();
        _uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_uiRoot);

        _objectives = new ObjectiveTracker(_content, _stationRules, _structuresOn);
        SettingsStore.EnsureLoaded(); // language, volume and window mode from user://settings.cfg

        // Status: the essentials (hull, alert, current step); H opens the full block.
        _statusLabel = MakeText(14);
        var statusBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        statusBox.AddThemeConstantOverride("separation", 4);
        BuildShipCardHead(statusBox);
        statusBox.AddChild(_statusLabel);
        BuildPowerBox(statusBox);
        BuildDefenceUi(statusBox);
        _statusPanel = MakePanel(statusBox);
        _statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Flight readout at the top centre: the galactic-frame gizmo (no compass: space has no up) and the speed.
        var flight = new VBoxContainer();
        _flightFrame = MakeText(10, new Color(0.62f, 0.7f, 0.84f));
        _flightFrame.HorizontalAlignment = HorizontalAlignment.Center;
        _navGizmo = new NavGizmo { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _flightSpeed = new IconCount("status:speed", 18, UiTheme.Body) { Alignment = BoxContainer.AlignmentMode.Center };
        flight.AddChild(_flightFrame);
        flight.AddChild(_navGizmo);
        flight.AddChild(_flightSpeed);
        BuildFlightFeelUi();
        flight.AddChild(_speedGauge);
        _flightPanel = MakePanel(flight);
        _flightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Action buttons.
        var actions = new VBoxContainer();
        _btnMine = MakeButton("", ToggleMining, toggle: true, minWidth: 170);
        _btnRefine = MakeButton("", ToggleRefinery, toggle: true, minWidth: 170);
        _btnScan = MakeButton("", () => Send(new Scan()), minWidth: 170);
        _btnMap = MakeButton("", () => _mapOpen = !_mapOpen, toggle: true, minWidth: 170);
        _btnWeapons = MakeButton("", ToggleWeapons, toggle: true, minWidth: 170);
        _btnResearch = MakeButton("", ToggleResearchPanel, toggle: true, minWidth: 170);
        _btnRepair = MakeButton("", RepairAll, minWidth: 170);
        foreach (var b in new[] { _btnMine, _btnRefine, _btnScan, _btnMap, _btnWeapons, _btnRepair, _btnResearch }) actions.AddChild(b);
        _actions = MakePanel(actions);
        _actions.Visible = false; // the key panel at the bottom right replaces these buttons (the objects stay so their state code keeps working)

        // The bottom strip is one compact line at the bottom left: the hold and the tool in hand with its cost. Nothing menu-like lives
        // here; the build menu, the shortcuts and the quick keys are in the key panel at the bottom right (see BuildKeyPanel).
        var infoRow = new HBoxContainer();
        infoRow.AddThemeConstantOverride("separation", 10);
        _holdItems = new HoldStrip();
        _toolLabel = new ToolReadout();
        infoRow.AddChild(_holdItems);
        infoRow.AddChild(_toolLabel);
        _toolbar = MakePanel(infoRow);
        _toolbar.MouseFilter = Control.MouseFilterEnum.Ignore;
        BuildKeyPanel();

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
            // A small icon of the category before the text (the colour alone does not say it to everyone).
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
            row.AddThemeConstantOverride("separation", 6);
            var icon = new IconRect("status:objective", 20, null, tooltip: false);
            _toastIcons[i] = icon;
            _toastRows[i] = row;
            row.AddChild(icon);
            row.AddChild(l);
            _toastBox.AddChild(row);
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
        objButtons.Visible = false; // everything it did has a key (H, O, F1, L) in the key panel or the help; the card stays plain
        obj.AddChild(objButtons);
        _stepStrip = new StepStrip();
        BuildGoalsUi(obj);
        obj.AddChild(_objectivesText);
        obj.AddChild(_stepStrip);
        // The goal stays pinned as the last line, whether the list is open or folded.
        _goalLabel = MakeText(14, new Color(1f, 0.85f, 0.45f));
        _goalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        obj.AddChild(_goalLabel);
        _objectivesPanel = MakePanel(obj);

        // The star map and its list are the existing nodes; the jump button sits under them.
        _map = new MapPanel { Visible = false };
        _map.SystemPicked += id => _selectedSystem = id; // click a dot on the 3D map to choose it
        _uiRoot.AddChild(_map);
        _mapList = MakeText(13);
        _mapList.AddThemeColorOverride("font_outline_color", Colors.Black);
        _mapList.AddThemeConstantOverride("outline_size", 4);
        _mapList.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.02f, 0.03f, 0.06f, 0.7f), ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 });
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
        _helpText = MakeText(14);
        _helpText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _helpScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _helpScroll.AddChild(_helpText);
        _helpPanel = MakePanel(_helpScroll);
        _helpPanel.AddThemeStyleboxOverride("panel", Box(new Color(0.04f, 0.06f, 0.1f, 0.97f), PanelEdge, 8, 10f)); // opaque: the HUD must not show through the text
        _helpPanel.Visible = false;
        _helpPanel.SetMeta(UiAudit.ModalMeta, true);

        _crosshair = new Crosshair { Visible = false };
        _uiRoot.AddChild(_crosshair);

        // World tooltip, anchored just below and right of the crosshair.
        _tooltipLabel = MakeText(13);
        _tooltipPanel = MakePanel(_tooltipLabel);
        _tooltipPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

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
        if (!_demo) { BuildPanels(); BuildDesigner(); }
        if (!_demo) BuildPauseMenu();
        if (!_demo) BuildEndScreen();
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
        var menu = _pauseMenu = new MenuScreen("pause.title", dim: true) { Visible = false, InfoLine = () => L("pause.seed", _galaxySeed) };
        menu.AddEntry(() => L("pause.resume"), ClosePause);
        menu.AddEntry(() => L("pause.save"), () =>
        {
            bool ok = SaveGameToDisk("quicksave", silent: true);
            menu.SetStatus(ok ? L("pause.saved", PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond)) : L("pause.saveFailed"));
            menu.Rebuild();
        });
        menu.AddEntry(() => L("pause.load"), () =>
        {
            if (LoadGameFromDisk()) ClosePause();
            else menu.SetStatus(L("pause.loadFailed"));
        }, () => Launch.NewestSave() is not null);
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

    /// <summary>Palette entries reached by the number keys: 1-9, 0 and Q. The module (N) and station (I) tools have their own key; everything else is in the build menu (B).</summary>
    private const int DirectKeys = 11;

    /// <summary>Width of the hold readout in the bottom strip (ore, ingots and plates fit; longer lists are clipped).</summary>
    private const float HoldItemsWidth = 520f;

    /// <summary>Rebuilds the palette after the rules changed (a loaded save with the other station-rules setting).</summary>
    private void RebuildPalette()
    {
        _palette.Clear();
        BuildPalette();
        _selected = 0;
        _tool = ToolMode.Pointer;
        _catalog = BuildCatalog.Entries(_content, _structuresOn, _stationRules);
    }

    // ---- per-frame refresh --------------------------------------------------------------------------

    private void UpdateUi(float dt)
    {
        UpdateCapture();
        _objectives.Update(Net, Player);
        UpdatePlayLog();
        AutosaveTick();
        UpdateOutcome();
        if (_saveTest) SaveSelfTest();
        if (_stressMachines > 0) StressTick(dt);
        if (!_demo)
        {
            _tips ??= new Tips(_content);
            var tip = _tips.Next(Net, Player, _mapOpen);
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
        UpdatePanels();
        _hudDt = dt;
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

    /// <summary>The galactic-frame gizmo (the fixed X, Y, Z of the galaxy, as the camera sees them) and the speed in cells per second.</summary>
    private void UpdateFlight()
    {
        bool show = !_demo && !Overview && Net.HasPlayer(Player);
        _flightPanel.Visible = show;
        if (!show) return;
        _flightFrame.Text = L("hud.frame").ToUpperInvariant();
        _navGizmo.Set(NavFrame.Project(HudCamera()));
        UpdateFlightReadout(); // the speed against the engines' top speed, the throttle gauge and the velocity markers (Play.FlightFeel.cs)
    }

    private readonly List<Vec3> _baseTmp = new();

    /// <summary>Markers for the selected deposit, the nearest enemy and the base (the nearest cluster of machines, else the anchor).</summary>
    private void UpdateMarkers(Vector2 size)
    {
        _markers.Visible = !_demo && !Overview && Net.HasPlayer(Player) && HudLayersNow.Markers;
        if (!_markers.Visible) return;

        var basis = _camera.GlobalTransform.Basis;
        var cam = new CameraBasis(V(-basis.Z), V(basis.X), V(basis.Y), Mathf.DegToRad(_camera.Fov));
        var eye = _camera.GlobalPosition;
        float left = _actions.Visible ? _actions.Position.X + _actions.Size.X + 14f : 26f;
        // Markers stay above the bottom strip and the notification lines.
        float textTop = _toolbar.Position.Y;
        if (_toastLabels[0].Visible) textTop = Math.Min(textTop, _toastBox.Position.Y);
        if (_modeHint.Text.Length > 0) textTop = Math.Min(textTop, _modeHint.Position.Y);
        float bottom = Math.Max(24f, size.Y - textTop + 10f);
        _markers.RightLimit = ResearchOpen || BuildOpen ? DockRect(size).Position.X - 6f : 0f;
        // Marker labels keep off the cards that carry text over the view (the full info card, the steps card, the star map and its list).
        _markers.KeepOut.Clear();
        if (_detailsOpen) foreach (var card in new Control[] { _statusPanel, _objectivesPanel }) if (card.Visible) _markers.KeepOut.Add(new Rect2(card.Position, card.Size).Grow(4f));
        if (_mapOpen && _map.Visible) { _markers.KeepOut.Add(new Rect2(_map.Position, _map.Size).Grow(4f)); _markers.KeepOut.Add(new Rect2(_mapList.Position, _mapList.GetCombinedMinimumSize()).Grow(4f)); }
        var items = new List<MarkerLayer.Item>();

        void Add(string label, Color color, Vector3 target, float hideBelow = 4f, string? distanceText = null, int priority = 1, string? icon = null)
        {
            var p = Markers.Place(V(target - eye), cam, size.X, size.Y, left, 58f, 24f, bottom, hideBelow);
            // A marker behind the steps panel is unreadable (and hides its text): slide it just below the panel.
            var steps = new Rect2(_objectivesPanel.Position - new Vector2(240f, 0f), _objectivesPanel.Size + new Vector2(240f + 14f, 8f));
            if (_objectivesPanel.Visible && _detailsOpen && steps.HasPoint(new Vector2(p.X, p.Y))) p = p with { Y = steps.End.Y + 22f };
            // And for the flight readout at the top centre: below it.
            var flightBox = new Rect2(_flightPanel.Position, _flightPanel.Size).Grow(130f);
            if (_flightPanel.Visible && flightBox.HasPoint(new Vector2(p.X, p.Y)) && p.Y < _flightPanel.Position.Y + _flightPanel.Size.Y + 40f) p = p with { Y = _flightPanel.Position.Y + _flightPanel.Size.Y + 40f };
            // The same for the key panel in the bottom right corner: the marker slides just above it.
            var keys = _keyRect.Grow(16f);
            if (_keyPanel.Visible && keys.HasPoint(new Vector2(p.X, p.Y))) p = p with { Y = keys.Position.Y - 6f };
            // Off screen the arrow also says how many degrees to turn (the same whichever way up the ship is).
            string turn = p.OnScreen ? "" : $"  {NavFrame.TurnDegrees(V(target - eye), cam):F0}°";
            string dist = distanceText ?? L("mark.dist", p.Distance.ToString("F0", Loc.Culture));
            if (p.Visible && (color == EnemyMarkerColor || ShipHudContext)) items.Add(new MarkerLayer.Item($"{label}  {dist}{turn}", color, p, color == EnemyMarkerColor ? 3 : priority, icon ?? MarkerIcon(label), $"{dist}{turn}"));
        }

        if (Net.IsDeposit(_selectedDeposit) && Net.IsDepositRevealed(_selectedDeposit) && Net.DepositRemaining(_selectedDeposit) > 0)
        {
            var dp = Net.DepositPosition(_selectedDeposit);
            Add($"{ItemName(Net.DepositItem(_selectedDeposit))} - {DensityText(_selectedDeposit)}{(DepositHasDrones(_selectedDeposit) ? " - " + L("mark.drones") : "")}", new Color(1f, 0.8f, 0.35f), new Vector3(dp.X, dp.Y, dp.Z), 6f, null, 2, Icons.ItemId(_content.Items[Net.DepositItem(_selectedDeposit)].Id));
        }

        // Other deposits you know of nearby: a quiet mark each, the two nearest only (the chosen one above keeps its full label).
        {
            var others = new List<(int Id, float D2, Vector3 At)>();
            foreach (int d in RevealedDeposits())
            {
                if (d == _selectedDeposit || Net.DepositRemaining(d) <= 0) continue;
                var q = Net.DepositPosition(d);
                var at = new Vector3(q.X, q.Y, q.Z);
                others.Add((d, (at - eye).LengthSquared(), at));
            }
            others.Sort((a, b) => a.D2.CompareTo(b.D2));
            foreach (var (d, _, at) in others.Take(2)) Add(ItemName(Net.DepositItem(d)), new Color(0.78f, 0.66f, 0.4f), at, 8f, null, 1, Icons.ItemId(_content.Items[Net.DepositItem(d)].Id));
        }

        // The gate: the direction of the destination chosen on the star map, as a mark at the edge of the system (its distance is in map units).
        if (_selectedSystem >= 0 && Net.Galaxy is { } gal && Net.CurrentSystem(Player) is var curSys && curSys >= 0 && _selectedSystem != curSys && Net.IsSystemKnown(_selectedSystem))
        {
            var from = gal.Systems[curSys];
            var to = gal.Systems[_selectedSystem];
            var dir = NavFrame.DirectionTo(new Vec3(from.X, from.Y, from.Z), new Vec3(to.X, to.Y, to.Z));
            float mapDistance = new Vector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z).Length();
            Add(L("mark.gate", to.Name), new Color(1f, 0.55f, 0.25f), eye + new Vector3(dir.X, dir.Y, dir.Z) * 900f, 4f, L("mark.gateDist", mapDistance.ToString("F0", Loc.Culture)), 2, "status:gate");
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
        if (nearest is { } e) Add(Loc.Name("enemy", _content.Enemies[e.Type].Id), EnemyMarkerColor, new Vector3(e.X, e.Y, e.Z) / CellUnits, 3f, null, 3, IconCatalog.ForEnemy(_content.Enemies[e.Type].Id));

        if (_structuresOn) { StructureMarkers((l, c, t, h) => Add(l, c, t, h, null, l == L("mark.station") ? 2 : 1)); _markers.Set(items); return; }

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
            if (nearestModule is { } sm) Add(L("mark.station"), new Color(0.45f, 0.8f, 1f), new Vector3(sm.X, sm.Y, sm.Z), 10f, null, 2);
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
        bool full = total >= cap;
        var held = new List<(string Icon, int Count)>();
        foreach (var item in _content.Items)
        {
            int n = Net.HoldCount(Player, item.Index);
            if (n > 0) held.Add((Icons.ItemId(item.Id), n));
        }
        _holdItems.Set(total, cap, full, HudFormat.Band(cap - total, cap) == HealthBand.Critical, held);

        // Said when it fills and again every 25 s while it stays full: a full hold silently stops mining and depots.
        double now = Time.GetTicksMsec() / 1000.0;
        if (full && (!_holdWasFull || now >= _holdFullAgain))
        {
            ToastLogistics(L("toast.holdFull"), 8.0);
            _holdFullAgain = now + 25.0;
        }
        _holdWasFull = full;

        // Hand mining stops at the mining limit (75 percent) in structure worlds: say so once per stop.
        bool paused = MiningPausedByLimit;
        if (paused && !_miningPausedToasted && !full) ToastLogistics(L("toast.miningPaused"), 8.0);
        _miningPausedToasted = paused;
    }

    private bool _miningPausedToasted;

    /// <summary>True while a mining order is active but the hold is at the mining limit, so nothing is mined.</summary>
    private bool MiningPausedByLimit =>
        _structuresOn && Net.LaserFiring(Player)
        && HudFormat.MiningLimitReached(Net.HoldTotal(Player), Net.HoldCapacity(Player));

    private void UpdateStatus()
    {
        var sb = new StringBuilder();
        var p = Net.GetPlayerInfo(Player);
        _satCache.Clear(); // read again each frame by the machine models
        UpdatePowerHud();
        UpdateHullRow(p);

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
        if (Net.LaserFiring(Player))
        {
            int d = Net.MiningDeposit(Player);
            bool inRange = Net.InMineRange(Player, d);
            depLine = inRange ? L("hud.mining", ItemName(Net.DepositItem(d)), Net.DepositRemaining(d), DensityText(d))
                              : L("hud.miningOut", ItemName(Net.DepositItem(d)));
            if (inRange && p.Hull >= 0 && Net.HoldTotal(Player) >= Net.HoldCapacity(Player)) depLine = L("hud.miningFull");
            else if (inRange && MiningPausedByLimit) depLine = L("hud.miningPaused");
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
        // The hull has its own row in the card head and the wave its banner; the steps and goals live in the info panel (H).
        if (_stationRules) sb.Append(L("hud.modules", Net.ModuleCount(Player))).Append('\n');
        if (_structuresOn) StructureHud(sb, details: false);
        if (MiningPausedByLimit) { sb.Append(L("hud.miningPaused")).Append('\n'); if (colour == Colors.White) colour = WornColor; }
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
        if (name == "autosave" && Net.StructureRules && Net.ShipLost(Player)) return false; // a lost world never replaces the autosave
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://saves"));
            var ms = new System.IO.MemoryStream();
            SaveGame.Write(ms, _world, _content, _gameMode, _galaxySeed, _threatsOn, Player, _stationRules, _structuresOn,
                _scenario is not null || !Net.Rules.IsDefault ? Net.Rules : null, _scenario);
            WriteSlotAtomically(name, ms.ToArray());
            // The goals engine is recomputed from the world on load, so when each goal was done travels beside the save.
            if (_progress is not null) ScenarioStore.WriteSidecar(name, _progress.Export(_world.Tick, _continueEndless));
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

    /// <summary>
    /// Writes a slot without ever leaving it half-written: the bytes go to a temporary file first, the slot's previous contents are kept
    /// (the autosave rotates to <c>autosave.prev</c>, other slots keep a <c>.bak</c>), then the temporary file is renamed over the slot.
    /// </summary>
    private static void WriteSlotAtomically(string name, byte[] bytes)
    {
        string tmp = SavePath(SaveSlots.TempName(name));
        using (var f = Godot.FileAccess.Open(tmp, Godot.FileAccess.ModeFlags.Write))
        {
            if (f is null) throw new System.IO.IOException(Godot.FileAccess.GetOpenError().ToString());
            f.StoreBuffer(bytes);
        }
        if (!Godot.FileAccess.FileExists(tmp) || Godot.FileAccess.GetFileAsBytes(tmp).Length != bytes.Length)
            throw new System.IO.IOException("the temporary save file was not written completely");
        string slot = SavePath(name);
        string G(string p) => ProjectSettings.GlobalizePath(p);
        if (Godot.FileAccess.FileExists(slot))
        {
            var copy = DirAccess.CopyAbsolute(G(slot), G(SavePath(SaveSlots.PreviousName(name))));
            if (copy != Error.Ok) GD.PrintErr($"SAVE could not keep the previous {name}: {copy}");
        }
        var err = DirAccess.RenameAbsolute(G(tmp), G(slot));
        if (err != Error.Ok)
        {
            DirAccess.RemoveAbsolute(G(slot)); // some platforms do not rename over an existing file
            err = DirAccess.RenameAbsolute(G(tmp), G(slot));
            if (err != Error.Ok) throw new System.IO.IOException(err.ToString());
        }
    }

    /// <summary>Loads the newest of the quick save and the autosave (or the named slot) by replaying it; the game freezes for a second or two.</summary>
    private bool LoadGameFromDisk(string? slot = null)
    {
        if (_demo) return false;
        string? name = slot ?? Launch.NewestSave();
        if (name is null || !Godot.FileAccess.FileExists(SavePath(name))) { Toast(L("toast.noSave")); return false; }
        try
        {
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(SavePath(name));
            // A save of another galaxy cannot be loaded into this scene (its deposits and sky are built from the seed): reopen the scene on it.
            var peek = SaveGame.Peek(new System.IO.MemoryStream(bytes));
            if (peek.GalaxySeed != _galaxySeed || peek.ScenarioId != _scenario?.Id)
            {
                FinishPlayLog();
                Launch.Mode = Launch.Intent.Continue;
                Launch.ContinueSave = name;
                Launch.MenuShotPath = null;
                GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
                return true;
            }
            var world = SaveGame.Read(new System.IO.MemoryStream(bytes), _content, out var info, _scenarios);
            _world = world;
            _loadedSnapshot = _scenario is null ? null : ScenarioStore.ReadSidecar(name, info.Ticks);
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
            Toast(L("toast.loadFailed", L(SaveSlots.LoadFailureKey(e))));
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
        if (_structuresOn && _cursorY == 0) _cursorY = DefaultLayer;
        _selMachine = -1;
        _waypoint = null;
        _seated = true; // the loaded world has its own ship and modules: start at the seat again
        _bodyVolumesVersion = -1;
        _attitudeSynced = false; _attitudeThrottle.Reset(); // the loaded ship has its own attitude: adopt it instead of overwriting it with the pre-load one
        _tool = ToolMode.Pointer;
        _selectedDeposit = DefaultDeposit();
        _objectives = new ObjectiveTracker(_content, _stationRules, _structuresOn);
        _tips = null;
        _lowOreWarned.Clear();
        _warned = false;
        _snaps.Clear();
        _hitBars.Clear();
        _vigAlpha = 0f;
        _lastLost = Net.MachinesLost;
        _lastShipsLost = Net.ShipsLost;
        _lastWaves = Net.WavesSpawned;
        _outcomeShown = false;
        _continueEndless = false;
        _endScreen?.Dismiss();
        ResetGoalsAfterLoad();
        if (_playLog is not null)
        {
            _logFile?.StoreLine($"-- loaded a save at game time {PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond)} (galaxy seed {_galaxySeed})");
            _logFile?.Flush();
            _playLog = new PlayLog(_content, _stationRules, _structuresOn);
            _playLog.Update(Net, Player); // what was already done is not news
        }
    }

    /// <summary>Every five minutes of real time a played game saves itself to the autosave slot.</summary>
    private void AutosaveTick()
    {
        if (_demo || EndOpen || (Net.StructureRules && Net.ShipLost(Player))) return; // a lost world never replaces the autosave
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
            _logFile?.StoreLine($"-- galaxy seed {_galaxySeed}"); // the seed rebuilds the same galaxy, so a play log can be compared with another run of it
            _logFile?.Flush();
            GD.Print("PLAYLOG " + ProjectSettings.GlobalizePath(path) + $" (galaxy seed {_galaxySeed})");
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
    private string KeyLabel(int i) => _palette[i].IsModule ? "N" : _palette[i].IsStation ? "I" : i < 9 ? (i + 1).ToString() : i == 9 ? "0" : i == 10 ? "Q" : "";

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
        UpdateToolLabel();

        // Actions.
        var player = Net.GetPlayerInfo(Player);
        bool mining = _laserOn;
        _btnMine.Text = mining ? L("btn.mineStop") : L("btn.mine");
        _btnMine.SetPressedNoSignal(mining);
        _btnMine.TooltipText = MiningPausedByLimit ? L("tip.minePaused") : L("tip.mine");

        bool refining = player.ShipRecipe >= 0;
        string benchName = refining ? L("bench." + _content.Recipes[player.ShipRecipe].Id) : "";
        _btnRefine.Text = refining ? L("btn.refineStop", benchName) : L("btn.refine");
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
        _damagedCount = damagedNow.Count;
        _btnRepair.Visible = Net.ThreatActive;
        _btnRepair.Text = L("btn.repair", damagedNow.Count);
        _btnRepair.Disabled = damagedNow.Count == 0;
        _btnRepair.TooltipText = damagedNow.Count == 0 ? L("tip.repairNone") : L("tip.repair", Stacks(damagedNow.SelectMany(id => Net.RepairCost(id)).GroupBy(s => s.Item).Select(g => new ItemStack(g.Key, g.Sum(s => s.Count))).ToArray()));

        _btnResearch.Text = L("btn.research");
        _btnResearch.SetPressedNoSignal(ResearchOpen);
        _btnResearch.TooltipText = L("tip.research");

        _btnJump.Visible = _mapOpen && Net.Galaxy is not null;
        bool canPayJump = Net.CanAffordJump(Player);
        var jumpCost = Net.JumpCost();
        _btnJump.Text = jumpCost.Length == 0 || _gameMode != GameMode.Campaign
            ? L("btn.jump")
            : L("map.jumpCost", Stacks(jumpCost.ToArray()));
        _btnJump.Disabled = _selectedSystem < 0 || Net.JumpRemaining(Player) > 0;
        _btnJump.AddThemeColorOverride("font_color", canPayJump ? new Color(1, 1, 1) : new Color(1f, 0.35f, 0.3f));
        _btnJump.AddThemeColorOverride("font_hover_color", canPayJump ? new Color(1, 1, 1) : new Color(1f, 0.35f, 0.3f));
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
            _toolLabel.Set("tool:pick", L("hud.tool.pointer"), new Color(0.75f, 0.82f, 0.95f), Array.Empty<(string, int, int)>(), "", UiTheme.Text);
            return;
        }
        var item = _palette[_selected];
        bool locked = ItemLocked(item);
        var cost = ItemCost(item);
        var state = HudFormat.Tool(locked, cost.Length > 0, Net.CanPay(Player, cost));
        string name = ItemName(item);
        string note = state switch
        {
            ToolState.Locked => L("ui.locked"),
            ToolState.Free => L("ui.free"),
            _ => item.IsBelt ? L("ui.perCell") : "",
        };
        var costIcons = state == ToolState.Locked ? new List<(string, int, int)>() : cost.Select(c => (Icons.ItemId(_content.Items[c.Item].Id), c.Count, Net.HoldCount(Player, c.Item))).ToList();
        var nameColour = state switch { ToolState.Short => BadColor, ToolState.Locked => new Color(0.6f, 0.62f, 0.68f), _ => GoodColor };
        _toolLabel.Set(ToolIcon(item), name, nameColour, costIcons, note, state == ToolState.Locked ? new Color(1f, 0.82f, 0.4f) : UiTheme.Muted);
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
        else if (_laserOn && !Standing && _hover.Kind != HoverKind.Deposit && !PanelOpen())
            _modeHint.Text = L("hint.laserNoAim", PlayerRules.MineRangeCells); // the laser is out but nothing is aimed at
        else if (NewPlayerStepHint() is { } step) _modeHint.Text = L("hint.step", step);
        else _modeHint.Text = ""; // nothing to say with the pointer: the key panel lists the keys
    }

    /// <summary>
    /// One line with the guided step in progress, for the first steps only (the goals and the full steps panel live under H now): a new
    /// player always knows what to do next, and the line goes away by itself once the first steps are done.
    /// </summary>
    private string? NewPlayerStepHint()
    {
        if (!GuidedSteps || _detailsOpen || _helpOpen || Standing || _objectives.DoneCount >= 3 || ThreatNowHud() || _objectives.Current is not { } current) return null;
        return L(_objectives.States.First(s => s.Objective == current).TitleKey);
    }

    private void UpdateObjectivesPanel()
    {
        _objectivesPanel.Visible = !(ResearchOpen || BuildOpen); // a docked panel takes its place at the right
        UpdateGoalLine();
        _btnObjectives.Visible = GuidedSteps;
        _stepStrip.Visible = GuidedSteps && _objectivesOpen;
        if (!GuidedSteps) { _objectivesText.Visible = false; return; }
        _goalLabel.CustomMinimumSize = new Vector2(Math.Clamp(GetViewport().GetVisibleRect().Size.X * 0.3f, 240, 420), 0);
        var states = _objectives.States;
        var current = _objectives.Current;
        _stepStrip.Set(states, current);
        var sb = new StringBuilder();
        sb.Append(L("obj.title", _objectives.DoneCount, states.Count)).Append('\n');
        if (_objectivesOpen)
        {
            if (current is null) sb.Append(L(_structuresOn ? "obj.allDone.structures" : "obj.allDone"));
            else
            {
                // The step in progress with its hint, then a short look at what comes next.
                var cur = states.First(s => s.Objective == current);
                sb.Append("> ").Append(L(cur.TitleKey)).Append('\n').Append("   ").Append(L(cur.HintKey)).Append('\n');
            }
            _objectivesText.Text = sb.ToString().TrimEnd('\n');
            _objectivesText.Visible = true;
        }
        else _objectivesText.Visible = false;
    }

    private void UpdateHelp()
    {
        _helpPanel.Visible = _helpOpen;
        if (_helpOpen) _helpText.Text = L("help.text") + L("help.cockpit");
    }

    private void UpdateToasts()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        var visible = _toasts.Visible(now);
        for (int i = 0; i < _toastLabels.Length; i++)
        {
            var label = _toastLabels[i];
            label.Visible = _toastRows[i].Visible = i < visible.Count;
            if (i >= visible.Count) continue;
            var t = visible[i];
            label.Text = t.Display;
            float textWidth = ThemeDB.FallbackFont.GetStringSize(t.Display, HorizontalAlignment.Left, -1, 16).X + 14f;
            label.CustomMinimumSize = new Vector2(Math.Min(_toastMax, textWidth), 0f);
            label.HorizontalAlignment = textWidth > _toastMax ? HorizontalAlignment.Left : HorizontalAlignment.Center; // a wrapped toast reads from its icon
            _toastIcons[i].SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            var color = t.Category switch
            {
                ToastCategory.Production => ToastProductionColor,
                ToastCategory.Logistics => ToastLogisticsColor,
                ToastCategory.Threat => ToastThreatColor,
                _ => ToastInfo,
            };
            label.AddThemeColorOverride("font_color", color);
            _toastIcons[i].Set(t.Category switch
            {
                ToastCategory.Production => "tool:refine",
                ToastCategory.Logistics => "tool:belt",
                ToastCategory.Threat => "status:alert",
                _ => "tool:info",
            }, color);
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
        LayoutGoals(Math.Clamp(size.X * 0.3f, 240, 420));
        _objectivesPanel.ResetSize();
        var objMin = _objectivesPanel.GetCombinedMinimumSize();
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        // The essentials are short and narrow; the full block (H) is wide but never runs under the steps panel.
        _statusLabel.CustomMinimumSize = new Vector2(_detailsOpen ? Math.Clamp(size.X - objMin.X - 60, 260, 760) : Math.Min(300f, Math.Max(180f, size.X * 0.22f)), 0);
        _statusLabel.Visible = _statusLabel.Text.Length > 0;
        _statusPanel.Visible = _statusLabel.Visible || _powerBox.Visible || _integrityBox.Visible || _hullBox.Visible;
        _statusPanel.ResetSize();
        _statusPanel.Position = new Vector2(12, 10);

        _actions.Position = new Vector2(12, _statusPanel.Visible ? _statusPanel.Position.Y + _statusPanel.Size.Y + 10 : 10);

        // The key panel (bottom right) is laid out first: everything else ends where it begins.
        UpdateKeyPanel(size);
        float keyLeft = _keyRect.Position.X, keyTop = _keyRect.Position.Y;
        float freeRight = Math.Max(320f, keyLeft - 12f); // the bottom-left area, clear of the key panel
        var dock = DockRect(size);
        if (_researchPanel is not null) _researchPanel.Dock = dock;
        if (_buildMenu is not null) _buildMenu.Dock = dock;

        // The bottom strip (hold and tool in hand) is one line at the bottom left; it shrinks to fit narrow windows.
        _toolbar.ResetSize();
        var barSize = _toolbar.GetCombinedMinimumSize();
        float barScale = Math.Min(1f, (freeRight - 12f) / Math.Max(1f, barSize.X));
        _toolbar.Scale = new Vector2(barScale, barScale);
        _toolbar.Position = new Vector2(12f, size.Y - barSize.Y * barScale - 8f);
        barSize = new Vector2(barSize.X * barScale, barSize.Y * barScale);

        // The hint wraps to the room left of the key panel (up to a readable 1100 px) and grows upwards, so no tail of it is ever clipped.
        float hintWidth = Math.Clamp(freeRight - 12f, 280f, 1100f);
        _modeHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _modeHint.HorizontalAlignment = HorizontalAlignment.Center;
        _modeHint.CustomMinimumSize = new Vector2(hintWidth, 0);
        _modeHint.ResetSize();
        _modeHint.Position = new Vector2(12f + (freeRight - 12f - hintWidth) / 2f, _toolbar.Position.Y - 10 - _modeHint.Size.Y);

        // Notifications: up to three lines in a column above the hint, wrapped to a readable width and centred on the view.
        float toastWidth = Math.Clamp(size.X * 0.6f, 300f, 760f);
        float toastCentre = size.X / 2f;
        if (ResearchOpen || BuildOpen) { toastCentre = (dock.Position.X - 12f) / 2f; toastWidth = Math.Min(toastWidth, dock.Position.X - 36f); } // the free left side of the view
        else toastWidth = Math.Min(toastWidth, Math.Max(300f, 2f * (keyLeft - 12f - size.X / 2f))); // not under the key panel
        // With the star map open the column narrows so it does not run under the map and its list on the right.
        if (_mapOpen && Net.Galaxy is not null) toastWidth = Math.Min(toastWidth, Math.Max(300f, size.X - 784f));
        _toastMax = toastWidth - 30f; // minus the category icon; each label shrinks to its own text up to this width
        _toastBox.CustomMinimumSize = new Vector2(toastWidth, 0);
        _toastBox.ResetSize();
        _toastBox.Position = new Vector2(toastCentre - toastWidth / 2f, _modeHint.Position.Y - 8 - _toastBox.Size.Y);

        var objSize = _objectivesPanel.GetCombinedMinimumSize();
        _objectivesPanel.Position = new Vector2(size.X - objSize.X - 12, 10);
        float y = _detailsOpen && _objectivesPanel.Visible ? _objectivesPanel.Position.Y + objSize.Y + 10 : 10f; // the steps card is only there with the info panels open
        if (_mapOpen && Net.Galaxy is not null)
        {
            // The map shrinks (down to 160 px) so that the map, the system list and the Jump button all end above the key panel.
            var listSize = _mapList.GetCombinedMinimumSize();
            var jumpSize = _btnJump.GetCombinedMinimumSize();
            float side = Math.Clamp(keyTop - 4f - y - listSize.Y - jumpSize.Y - 12f, 160f, 320f);
            _map.CustomMinimumSize = new Vector2(side, side);
            _map.Size = _map.CustomMinimumSize;
            _map.Position = new Vector2(size.X - side - 12, y);
            _mapList.Position = new Vector2(size.X - 380, y + side + 6);
            _btnJump.Position = new Vector2(size.X - 162, _mapList.Position.Y + listSize.Y + 6);
            // When the column does not fit above the key panel (a tall steps card), the Jump button moves to the left of the map.
            if (_btnJump.Position.Y + jumpSize.Y > keyTop - 4f) _btnJump.Position = new Vector2(_map.Position.X - jumpSize.X - 8f, _map.Position.Y + side - jumpSize.Y);
        }
        _map.Visible = _mapOpen && Net.Galaxy is not null;
        _mapList.Visible = _map.Visible;

        var inspSize = _inspector.GetCombinedMinimumSize();
        _inspector.Position = new Vector2(size.X - inspSize.X - 12, keyTop - inspSize.Y - 8f);

        // The flight readout sits at the top centre, dropping below the status or steps panel when they would meet it.
        _flightPanel.ResetSize();
        var fs = _flightPanel.GetCombinedMinimumSize();
        var fp = new Vector2((size.X - fs.X) / 2f, 8f);
        foreach (var other in new[] { _statusPanel, _objectivesPanel })
        {
            if (!other.Visible || (other == _objectivesPanel && !_detailsOpen) || other == _statusPanel) continue; // the status card in info mode hides the readout instead (ApplyHudContext)
            var o =new Rect2(other.Position, other.Size);
            if (new Rect2(fp, fs).Intersects(o)) fp.Y = Math.Max(fp.Y, o.End.Y + 6f);
        }
        _flightPanel.Position = fp;
        // The boost and hazard banners hang below the readout, wherever it ended up (the gauge made the card taller).
        if (_boostLabel is not null) _boostLabel.OffsetTop = fp.Y + fs.Y + 4f;
        if (_hazardLabel is not null) _hazardLabel.OffsetTop = fp.Y + fs.Y + (Net.IsBoosting(Player) ? 34f : 6f);

        // The help overlay fills the view left of the key panel (it scrolls when the text is taller than the window).
        float helpWidth = Math.Clamp(keyLeft - 36f, 320f, 1000f);
        float helpHeight = Math.Max(200f, size.Y - 24f);
        _helpText.CustomMinimumSize = new Vector2(helpWidth - 36f, 0f);
        _helpScroll.CustomMinimumSize = new Vector2(helpWidth - 20f, helpHeight - 20f);
        _helpPanel.CustomMinimumSize = new Vector2(helpWidth, helpHeight);
        _helpPanel.Size = new Vector2(helpWidth, helpHeight);
        _helpPanel.Position = new Vector2(12f + (keyLeft - 24f - helpWidth) / 2f, 12f);

        // The crosshair sits at the screen centre and the tooltip beside it, kept on screen.
        _crosshair.Visible = AimActive && !_demo;
        _crosshair.Position = (size - _crosshair.Size) / 2f;
        var centre = size / 2f;
        _tooltipPanel.Visible = _tooltipLabel.Text.Length > 0 && !_demo && AimActive && !_helpOpen;
        _tooltipPanel.ResetSize();
        var tipSize = _tooltipPanel.GetCombinedMinimumSize();
        _tooltipPanel.Position = AvoidBottomText(AvoidKeyPanel(new Vector2(Math.Clamp(centre.X + 24, 8, Math.Max(8, size.X - tipSize.X - 8)), Math.Clamp(centre.Y + 24, 8, Math.Max(8, size.Y - tipSize.Y - 8))), tipSize), tipSize);

        _seatPrompt.Text = SeatPromptText();
        _seatPrompt.Visible = _seatPrompt.Text.Length > 0 && AimActive && !_helpOpen;
        _seatPrompt.ResetSize();
        _seatPrompt.Position = new Vector2(centre.X - _seatPrompt.Size.X / 2f, centre.Y - 46f);

        _targetPanel.ResetSize();
        var ts =_targetPanel.GetCombinedMinimumSize();
        _targetPanel.Position = AvoidBottomText(AvoidKeyPanel(new Vector2(Math.Clamp(centre.X + 28, 8, Math.Max(8, size.X - ts.X - 8)), Math.Clamp(centre.Y + 28, 8, Math.Max(8, size.Y - ts.Y - barSize.Y - 14))), ts), ts);
        _markers.Size = size;
        LayoutDefenceUi(size, centre);
        ApplyHudContext(size);
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
        if (ResearchOpen) CloseResearchPanel();
        if (BuildOpen) CloseBuildMenu();
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
        if (_demo || PauseOpen || EndOpen || StoryOpen) return;

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
        if (CockpitMouse(mb)) return; // a click on a cockpit display (Play.Cockpit.cs)
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
                else if (_hover.Kind == HoverKind.Segment && Net.IsAlive(_hover.Id)) Send(new RemoveSegment(_hover.Id)); // a belt is found by a ray at any layer, like a machine
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
        LookBy(mm.Relative); // steers the ship while piloting (free flight), otherwise turns the head
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

    /// <summary>Panels that need the pointer: the help overlay, the research panel, the build menu and the star map.</summary>
    private bool PanelOpen() => PauseOpen || EndOpen || StoryOpen || _helpOpen || ResearchOpen || BuildOpen || DesignerOpen || (_mapOpen && Net.Galaxy is not null);

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
            _dragStart = null;
            SendThrust((0, 0, 0), force: true);
        }
    }

    private void OnKey(InputEventKey k)
    {
        if (CockpitKey(k)) return; // F3 (HUD mode), F4 (look at the displays) and the keys of a focused display
        int digit = k.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)(k.Keycode - Key.Key1) : -1;
        if (k.Keycode == Key.Key0) digit = 9;
        if (k.Keycode == Key.Q) digit = 10;
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
                if (DesignerOpen) CloseDesigner();
                else if (ResearchOpen) CloseResearchPanel();
                else if (BuildOpen) CloseBuildMenu();
                else if (_helpOpen) _helpOpen = false;
                else if (_beltAnchor is not null) { _beltAnchor = null; _dragStart = null; } // first Esc cancels a belt line in progress
                else if (_selMachine >= 0) _selMachine = -1;
                else if (_tool == ToolMode.Build) _tool = ToolMode.Pointer;
                else if (_mapOpen) _mapOpen = false;
                else { _waypoint = null; _wantCapture = false; OpenPause(); } // last: let go of the mouse and open the pause menu
                break;
            case Key.F1: _helpOpen = !_helpOpen; break;
            case Key.F2: ToggleDesigner(); break;
            case Key.F11:
                SettingsStore.Set(SettingsStore.Current with { Fullscreen = DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen });
                break;
            case Key.F6: SaveGameToDisk("quicksave", silent: false); break;
            case Key.F9: LoadGameFromDisk(); break;
            case Key.F7: AssignDronesKey(); break;
            case Key.Delete when k.CtrlPressed: DumpSpareOre(); break;
            case Key.L: ToggleLanguage(); break;
            case Key.O: _objectivesOpen = !_objectivesOpen; break;
            case Key.H: _detailsOpen = !_detailsOpen; break;
            case Key.Tab: _wantCapture = !_wantCapture; break; // free the cursor to use the buttons, or take it back
            case Key.Y: CycleRecipeKey(); break;
            case Key.R: _dir = _dir switch { Dir.PosX => Dir.PosZ, Dir.PosZ => Dir.NegX, Dir.NegX => Dir.NegZ, _ => Dir.PosX }; Toast(L("toast.beltFacing", _dir.ToString())); break;
            case Key.Z when _tool == ToolMode.Build && !(FloorRule && !ModuleSelected): _cursorY--; break; // flying: Z and X roll the ship instead; on the floor rule only the module tool has layers
            case Key.X when _tool == ToolMode.Build && !(FloorRule && !ModuleSelected): _cursorY++; break;
            case Key.M: ToggleMining(); break;
            case Key.E: ToggleSeat(); break;
            case Key.U: DockKey(); break;
            case Key.Comma: UnloadKey(); break;
            case Key.Period: LoadKey(); break;
            case Key.Backspace: RepairAll(); break;
            case Key.Bracketleft: CycleDeposit(-1); break;
            case Key.Bracketright: CycleDeposit(1); break;
            case Key.K: Send(new Scan()); break;
            case Key.V: ToggleWeapons(); break;
            case Key.G: _mapOpen = !_mapOpen; break;
            case Key.J: SendJump(); break;
            case Key.Up: CycleSystem(-1); break;
            case Key.Down: CycleSystem(1); break;
            case Key.Left when _mapOpen: _map.Orbit(-0.25f, 0f); break; // rotate the 3D map (the roll keys of the flight are not in play with the map open)
            case Key.Right when _mapOpen: _map.Orbit(0.25f, 0f); break;
            case Key.Home when _mapOpen: _map.ResetView(); break;
            case Key.F: ToggleRefinery(); break;
            case Key.T: ToggleResearchPanel(); break;
            case Key.B: ToggleBuildMenu(); break;
            case Key.P: _paused = !_paused; break;
            case Key.Equal: case Key.Plus: case Key.KpAdd: _speed = Math.Min(10, _speed + 1); break;
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

    /// <summary>
    /// Sends the wanted thrust through the <see cref="ThrustThrottle"/>: components in steps of 64/1024 and at most ten changes a second,
    /// a stop at once. Every SetThrust is a logged command that a save replays, so mouse steering would otherwise fill the log (and the
    /// load time) with thousands of near-identical commands. The sim is unchanged; the heading error is under 2 degrees for most directions and 5.5 at worst.
    /// </summary>
    private void SendThrust((int X, int Y, int Z) thrust, bool force = false)
    {
        if (_throttle.Offer(thrust, Time.GetTicksMsec() / 1000.0, force) is { } t) Send(new SetThrust(t.X, t.Y, t.Z));
    }

    /// <summary>
    /// W/S fly along the view, A/D strafe along the camera's right, Space/C go up and down; the sum is normalised and sent as thrust
    /// whenever the integer vector changes. M's autopilot steers toward its waypoint the same way.
    /// </summary>
    private void UpdateShipControl()
    {
        if (_demo || _obsShotPath is not null) return; // the obstacle screenshots script their own flight

        // A jump or respawn clears the thrust in the simulation: send held keys again once the ship can move.
        if (Net.HasPlayer(Player) && _world.PendingCommands == 0)
        {
            var pi = Net.GetPlayerInfo(Player);
            if (pi.JumpRemaining == 0 && pi.RespawnRemaining == 0 && _throttle.Differs((pi.ThrustX, pi.ThrustY, pi.ThrustZ))) _throttle.Invalidate();
        }

        if (Standing)
        {
            // Standing: no flight. WASD walk (see UpdateBody); the autopilot is off and the ship holds still.
            _waypoint = null;
            SendThrust((0, 0, 0), force: _throttle.LastSent != (0, 0, 0));
            return;
        }

        if (IsDockedNow) { _dockAssist = false; _waypoint = null; return; } // a docked ship is locked to its port
        var thrust = (0, 0, 0);
        if (_mouseLook)
        {
            int forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
            int right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
            int up = (Input.IsPhysicalKeyPressed(Key.Space) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.C) ? 1 : 0);
            if (forward != 0 || right != 0 || up != 0)
            {
                _waypoint = null; // flying by hand takes over from the autopilot
                _dockAssist = false;
                // Thrust follows the camera's own axes (the ship's own axes while piloting), as a world-space vector: the simulation never
                // sees the attitude, only this integer vector (free flight).
                var cam = _focus is not null ? ShipBasis : CameraWorldBasis(); // looking at a display must not point the thrust at it
                thrust = ToThrust(-cam.Z * forward + cam.X * right + cam.Y * up);
            }
        }
        if (thrust == (0, 0, 0) && _dockAssist) thrust = DockAssistThrust();
        if (thrust == (0, 0, 0) && _waypoint is { } wp)
        {
            var to = wp - ShipPosition(0f);
            if (to.Length() < 0.25f) _waypoint = null; // arrived
            else thrust = ToThrust(to.Normalized());
        }

        UpdateLaser();

        SendThrust(thrust, force: thrust == (0, 0, 0) && _throttle.LastSent != (0, 0, 0));
    }

    /// <summary>The M key and the Mine button: take the mining laser in hand, or stow it. Mining itself is firing it (see <see cref="UpdateLaser"/>).</summary>
    private void ToggleMining()
    {
        if (_laserOn)
        {
            SetLaser(false);
            ToastProduction(L("toast.laserStowed"), 4.0);
            return;
        }
        if (Standing) { ToastLogistics(L("toast.sitToFly")); return; }
        if (HardpointsOn && !Net.HasMiningLaser(Player)) { ToastLogistics(L("toast.reject.noLaser")); return; }
        if (HardpointsOn && _weaponGroup != WeaponGroup.Laser) SetWeaponGroup(WeaponGroup.Laser);
        SetLaser(true);
        ToastProduction(L("toast.laserReady"), 6.0);
    }

    private void SetLaser(bool on)
    {
        _laserOn = on;
        _laserOutOfRangeToasted = false;
        if (on) { _tool = ToolMode.Pointer; _beltAnchor = null; _dragStart = null; }
        else if (Net.HasPlayer(Player) && Net.MiningDeposit(Player) >= 0) Send(new StopMining());
    }

    /// <summary>Per frame: while the laser is in hand and the fire button is held, the deposit under the crosshair (inside the range of the
    /// laser) is mined by the simulation. Letting go, looking away, going out of range or standing up stops it. Only changes are sent.</summary>
    private void UpdateLaser()
    {
        UpdateGuns();
        if (!_laserOn) return;
        if (Standing || _tool == ToolMode.Build || PanelOpen()) { SetLaser(false); return; }
        bool fire = (_mouseLook && Input.IsMouseButtonPressed(MouseButton.Left));
        int want = -1;
        if (fire && _hover.Kind == HoverKind.Deposit && Net.IsDeposit(_hover.Id) && Net.DepositRemaining(_hover.Id) > 0)
        {
            if (Net.InMineRange(Player, _hover.Id)) { want = _hover.Id; _laserOutOfRangeToasted = false; }
            else if (!_laserOutOfRangeToasted) { _laserOutOfRangeToasted = true; ToastLogistics(L("toast.laserTooFar", PlayerRules.MineRangeCells)); }
        }
        if (_world.PendingCommands > 0) return;
        int now = Net.MiningDeposit(Player);
        // Firing is a simulation fact (ADR 0027): releasing the button releases the laser trigger and keeps the order, so a short
        // release does not lose the partial item; a new deposit starts a new order.
        if (want < 0) { if (Net.LaserFiring(Player)) Send(new FireLaser(false)); }
        else if (now != want) Send(new StartMining(want));
        else if (!Net.LaserFiring(Player)) Send(new FireLaser(true));
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
            var (lo, ld) = AimInStruct(m.Structure); // the ray in the machine structure frame
            if (RayHitsBox(lo, ld, new Vector3(m.X, m.Y, m.Z), out float tb) && tb < best)
            {
                best = tb;
                _hover = new Hover(HoverKind.Machine, id, MachineSummary(id));
            }
        }
        // Belts: the ray against the box of each segment, at any layer and from any side (the build plane only finds the cell it crosses).
        for (int id = 0; id < Net.SegmentIdLimit; id++)
        {
            if (!Net.IsAlive(id)) continue;
            var s = Net.GetSegmentInfo(id);
            var (lo, ld) = AimInStruct(s.Structure);
            int ex = s.OriginX + s.DirX * (s.LengthCells - 1), ey = s.OriginY + s.DirY * (s.LengthCells - 1), ez = s.OriginZ + s.DirZ * (s.LengthCells - 1);
            var min = new Vector3(Math.Min(s.OriginX, ex), Math.Min(s.OriginY, ey), Math.Min(s.OriginZ, ez));
            var size = new Vector3(Math.Abs(ex - s.OriginX) + 1f, Math.Abs(ey - s.OriginY) + 0.6f, Math.Abs(ez - s.OriginZ) + 1f);
            if (RayHitsAabb(lo, ld, min, size, out float tb) && tb < best && tb > 0.5f)
            {
                best = tb;
                _hover = new Hover(HoverKind.Segment, id, L("tip.belt", (int)s.Tier + 1, s.LengthCells, Net.CountOn(id)));
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

    private void ChangeSelectedRecipe(int step) => ChangeRecipeOf(_selMachine, step);

    /// <summary>Y: with no tool, the recipe of the machine under the crosshair (or the pinned one); with a tool in hand, the recipe the tool will place.</summary>
    private void CycleRecipeKey()
    {
        int machine = _tool == ToolMode.Pointer ? (_hover.Kind == HoverKind.Machine ? _hover.Id : _selMachine) : -1;
        if (machine >= 0 && Net.IsMachineAlive(machine)) ChangeRecipeOf(machine, 1);
        else CycleRecipe();
    }

    private void ChangeRecipeOf(int machine, int step)
    {
        if (machine < 0 || !Net.IsMachineAlive(machine)) return;
        var m = Net.GetMachineInfo(machine);
        var options = RecipesFor(m.Type);
        if (options.Count < 2) { Toast(L("toast.oneRecipe")); return; }
        int i = options.IndexOf(m.Recipe);
        int next = options[((i < 0 ? 0 : i + step) % options.Count + options.Count) % options.Count];
        Send(new SetRecipe(machine, next));
        // A drill follows its recipe to a deposit of the new ore, when one is within reach.
        if (_content.Machines[m.Type].Role == MachineRole.Drill) RelinkDrill(machine, next);
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
        _cellHelper.Visible = false;
        _crosshair.Tint = Crosshair.Normal;
        if (!build) { _dragGhost.Visible = false; return; }

        if (StationSelected) { UpdateFoundGhost(); return; }
        if (ModuleSelected) { if (_structuresOn) UpdateStructModuleGhost(); else UpdateModuleGhost(); return; }
        _ghost.Scale = Vector3.One;
        var c = _aimCell!.Value;
        _cellHelper.Visible = !FloorRule || CellInBuildableModule(c); // the faint cells show only on a floor that can be built on
        Reparent(_ghost, RootOf(c.S)); // local cells under the structure root
        _ghost.Position = CellCenter(c.X, c.Y, c.Z);
        string? problem = PlacementProblem(c);
        _crosshair.Tint = problem is null ? Crosshair.Normal : Crosshair.Invalid;
        ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = problem is null ? new Color(0.4f, 1f, 0.5f, 0.4f) : new Color(1f, 0.3f, 0.25f, 0.4f);
        if (problem is not null) _tooltipLabel.Text = problem;
        else if (_dragStart is null)
        {
            // What the tool is and costs, next to the crosshair (the quick bar's old tooltip lives here now).
            var item = _palette[_selected];
            var cost = ItemCost(item);
            _tooltipLabel.Text = BuildTooltip(item, cost, ItemLocked(item), Net.CanPay(Player, cost));
        }
        UpdateDragGhost();
    }
}
