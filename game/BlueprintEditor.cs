using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The blueprint editor of ships (docs/prototypes/blueprint-editor.md): a full-screen mode with a 3D plan of the structure (orbit, pan, zoom, a top-down plan
/// and a deck-by-deck section), ghosts of what the draft adds, removes and moves, a palette and tools, a cost and stats panel that is always on screen, a
/// validation list whose entries frame the part, and Apply and Revert. The logic lives in the engine-free <see cref="BlueprintSession"/>; this class only draws
/// it and forwards the mouse and the keys. Applying sends one <see cref="DesignerApply"/>. Every part has a name so the agent harness can find it.
/// </summary>
public partial class BlueprintEditor : Control
{
    private static readonly Color Muted = UiTheme.Muted, Good = UiTheme.Good, Bad = UiTheme.Bad, Warn = UiTheme.Warn;

    private readonly ContentDb _content;
    private readonly BlueprintSession _s;
    private readonly Func<SimWorld> _world;
    private readonly int _player;
    private readonly Func<ItemStack[], string> _stacks;

    // layout and panels
    private SubViewportContainer _container = null!;
    private SubViewport _viewport = null!;
    private PanelContainer _topBar = null!, _palettePanel = null!, _sidePanel = null!, _footBar = null!, _costPanel = null!, _statsPanel = null!, _validationPanel = null!;
    private FitLabel _title = null!, _status = null!, _legend = null!, _deckLabel = null!, _selectionLabel = null!, _costSummary = null!, _costMissing = null!, _valSummary = null!;
    private VBoxContainer _paletteList = null!, _statsList = null!, _valList = null!;
    private GridContainer _costGrid = null!;
    private HSlider _deck = null!;
    private Button _apply = null!, _revert = null!, _save = null!, _load = null!, _close = null!, _undo = null!, _redo = null!, _paste = null!, _upgrade = null!, _frameAll = null!;
    private readonly Dictionary<BlueprintTool, Button> _toolButtons = new();
    private readonly Dictionary<PlanView, Button> _viewButtons = new();
    private readonly List<(ModuleKind Kind, Button Button)> _palette = new();
    private IconRect _statusIcon = null!;
    private Vector2 _lastSize;
    private bool _refresh = true, _sceneDirty = true, _discardArmed;

    /// <summary>Space reserved above the editor (the ship computer's tab strip when it is open); the editor normally covers the whole window.</summary>
    public float TopInset { get; set; }

    /// <summary>The player pressed Apply: the game submits the command.</summary>
    public event Action<DesignerApply>? ApplyRequested;

    public event Action? CloseRequested;

    /// <summary>A refused edit or a result the game should say aloud (a toast).</summary>
    public event Action<string>? Say;

    public BlueprintEditor(ContentDb content, ShipHandling handling, Func<SimWorld> world, int player, Func<ItemStack[], string> stacks)
    {
        _content = content;
        _world = world;
        _player = player;
        _stacks = stacks;
        _s = new BlueprintSession(content, handling, world, player);
        _s.Changed += () => { _refresh = true; _sceneDirty = true; };
        Name = "BlueprintEditor";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public BlueprintSession Session => _s;

    public bool IsOpen => Visible;

    private static string KindName(ModuleKind k) => Loc.T("module." + k.ToString().ToLowerInvariant());

    private static string Cap(string actionId) => string.Join(" ", InputActions.Caps(actionId).Select(c => c.StartsWith('@') ? Loc.T("key.cap." + c[1..]) : c));

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildScene();

        // the 3D plan fills the whole window; the panels float over it
        _container = new SubViewportContainer { Name = "PlanView", Stretch = true, MouseFilter = MouseFilterEnum.Stop };
        _container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _container.GuiInput += OnPlanInput;
        _container.MouseExited += () => { _hoverCell = null; _sceneDirty = true; };
        _viewport = new SubViewport { Name = "PlanViewport", OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I(1280, 720) };
        _container.AddChild(_viewport);
        AddChild(_container);
        _viewport.AddChild(_planRoot);
        _viewport.AddChild(_camera3D);

        BuildTopBar();
        BuildPalette();
        BuildSidePanel();
        BuildFootBar();
        foreach (var p in new Control[] { _topBar, _palettePanel, _sidePanel, _footBar }) AddChild(p);
    }

    // ---- building the panels ----------------------------------------------------------------------------------------

    private static Button IconButton(string iconId, string tooltip, Action pressed, string name, bool toggle = false, int size = 34)
    {
        var b = new Button { Name = name, CustomMinimumSize = new Vector2(size, size), FocusMode = FocusModeEnum.All, TooltipText = tooltip, ToggleMode = toggle, ExpandIcon = false };
        UiTheme.StyleButton(b);
        b.Icon = Icons.Get(iconId, 22);
        b.AddThemeConstantOverride("icon_max_width", 22);
        b.AddThemeColorOverride("icon_normal_color", Colors.White);
        b.AddThemeColorOverride("icon_hover_color", Colors.White);
        b.AddThemeColorOverride("icon_pressed_color", UiTheme.Gold);
        b.AddThemeColorOverride("icon_hover_pressed_color", UiTheme.Gold);
        b.AddThemeColorOverride("icon_disabled_color", UiTheme.Dim);
        b.Pressed += pressed;
        return b;
    }

    private static PanelContainer Panel(string name, Color? edge = null)
    {
        var p = new PanelContainer { Name = name, MouseFilter = MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.04f, 0.06f, 0.10f, 0.9f), edge ?? UiTheme.EdgeStrong, 8, 8f));
        return p;
    }

    private FitLabel Label(string name, int maxLines, int maxFont, int minFont, Color? colour = null)
    {
        var l = new FitLabel(maxLines, maxFont, minFont) { Name = name };
        l.AddThemeColorOverride("font_color", colour ?? Colors.White);
        return l;
    }

    private void BuildTopBar()
    {
        _topBar = Panel("TopBar");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _topBar.AddChild(row);
        row.AddChild(new IconRect("tool:designer", 26, UiTheme.Gold, tooltip: false));
        _title = Label("Title", 1, UiTheme.Heading + 2, UiTheme.Small);
        _title.CustomMinimumSize = new Vector2(170, 0);
        _title.FitWidth = 170;
        _title.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(_title);

        foreach (var (view, icon, key) in new[] { (PlanView.Orbit, "tool:look", "bp.view.orbit"), (PlanView.Top, "tool:map", "bp.view.top"), (PlanView.Section, "tool:layer", "bp.view.section") })
        {
            var v = view;
            var b = IconButton(icon, Loc.T(key) + " (" + Cap("bpView") + ")", () => SetView(v), "View" + view, toggle: true);
            _viewButtons[view] = b;
            row.AddChild(b);
        }
        _frameAll = IconButton("tool:inspect", Loc.T("bp.view.frame") + " (" + Cap("bpFrame") + ")", () => FrameAll(), "FrameButton");
        row.AddChild(_frameAll);

        _deckLabel = Label("DeckLabel", 1, UiTheme.Small, UiTheme.Caption);
        _deckLabel.FitWidth = 96; _deckLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _deckLabel.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(_deckLabel);
        _deck = new HSlider { Name = "DeckSlider", MinValue = -1, MaxValue = 1, Step = 1, CustomMinimumSize = new Vector2(150, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.All, TooltipText = Loc.T("bp.view.section") + " (" + Cap("bpLayer") + ")" };
        _deck.ValueChanged += v => { if (!_settingDeck) { _s.Camera.SetSection((int)v); SyncViewButtons(); _sceneDirty = true; } };
        row.AddChild(_deck);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(spacer);
        _selectionLabel = Label("SelectionLabel", 1, UiTheme.Small, UiTheme.Caption, Muted);
        _selectionLabel.FitWidth = 170; _selectionLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _selectionLabel.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(_selectionLabel);
        _close = UiKit.Button(Loc.T("bp.close"), () => RequestClose(), 14, 90);
        _close.Name = "CloseButton";
        _close.TooltipText = Loc.T("bp.close") + " (" + InputActions.PrimaryCap("cancel") + ")";
        row.AddChild(_close);
    }

    private bool _settingDeck;

    private void BuildPalette()
    {
        _palettePanel = Panel("PalettePanel");
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        _palettePanel.AddChild(col);
        var toolsTitle = Label("ToolsTitle", 1, UiTheme.Body, UiTheme.Small, Muted); toolsTitle.SetFit(Loc.T("bp.tools"));
        col.AddChild(toolsTitle);

        var tools = new HFlowContainer { Name = "Tools", MouseFilter = MouseFilterEnum.Pass };
        tools.AddThemeConstantOverride("h_separation", 4);
        tools.AddThemeConstantOverride("v_separation", 4);
        void Tool(BlueprintTool tool, string icon, string key, string action)
        {
            var b = IconButton(icon, Loc.T(key) + " (" + Cap(action) + ")", () => SetTool(tool), "Tool" + tool, toggle: true);
            _toolButtons[tool] = b;
            tools.AddChild(b);
        }
        Tool(BlueprintTool.Select, "tool:pick", "bp.tool.select", "bpSelect");
        Tool(BlueprintTool.Place, "tool:place", "bp.tool.place", "bpPlace");
        Tool(BlueprintTool.Move, "tool:move", "bp.tool.move", "bpMove");
        Tool(BlueprintTool.Rotate, "tool:rotate", "bp.tool.rotate", "bpRotate");
        Tool(BlueprintTool.Delete, "tool:demolish", "bp.tool.delete", "bpDelete");
        tools.AddChild(IconButton("tool:mirror", Loc.T("bp.tool.mirror") + " (" + Cap("bpMirror") + ")", () => Mirror(), "ToolMirror"));
        tools.AddChild(IconButton("tool:copy", Loc.T("bp.tool.copy") + " (" + Cap("bpCopy") + ")", () => Copy(), "ToolCopy"));
        _paste = IconButton("tool:paste", Loc.T("bp.tool.paste") + " (" + Cap("bpPaste") + ")", () => Paste(), "ToolPaste");
        tools.AddChild(_paste);
        _upgrade = IconButton("tool:add", Loc.T("bp.tool.upgrade") + " (" + Cap("bpUpgrade") + ")", () => Upgrade(), "ToolUpgrade");
        tools.AddChild(_upgrade);
        _undo = IconButton("tool:undo", Loc.T("bp.tool.undo") + " (" + Cap("bpUndo") + ")", () => Undo(), "ToolUndo");
        _redo = IconButton("tool:redo", Loc.T("bp.tool.redo") + " (" + Cap("bpRedo") + ")", () => Redo(), "ToolRedo");
        tools.AddChild(_undo); tools.AddChild(_redo);
        col.AddChild(tools);

        var palTitle = Label("PaletteTitle", 1, UiTheme.Body, UiTheme.Small, Muted); palTitle.SetFit(Loc.T("bp.palette"));
        col.AddChild(palTitle);
        _paletteList = new VBoxContainer { Name = "PaletteList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _paletteList.AddThemeConstantOverride("separation", 3);
        foreach (var kind in ShipDesignEditor.Catalogue(_content))
        {
            var k = kind;
            var row = new HBoxContainer { Name = "PaletteRow_" + k, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 4);
            var b = new Button { Name = "Palette_" + k, Text = KindName(k), Alignment = HorizontalAlignment.Left, ToggleMode = true, ExpandIcon = false, FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30), ClipText = true };
            UiTheme.StyleButton(b);
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Icon = Icons.Get(IconCatalog.ForModule(k), 22);
            b.AddThemeConstantOverride("icon_max_width", 22);
            b.AddThemeConstantOverride("h_separation", 6);
            b.AddThemeColorOverride("icon_normal_color", ModuleModels.RoleColour(k).Lightened(0.3f));
            b.AddThemeColorOverride("icon_hover_color", Colors.White);
            b.AddThemeColorOverride("icon_pressed_color", Colors.White);
            b.AddThemeColorOverride("icon_hover_pressed_color", Colors.White);
            b.Pressed += () => PickKind(k);
            b.TooltipText = Loc.T("module." + k.ToString().ToLowerInvariant() + ".purpose") + (_content.Structures is { } sc && sc.ModuleCost(k).Length > 0 ? "\n" + _stacks(sc.ModuleCost(k)) : "");
            row.AddChild(b);
            var costs = new HBoxContainer { Name = "Cost_" + k, MouseFilter = MouseFilterEnum.Ignore };
            costs.AddThemeConstantOverride("separation", 4);
            if (_content.Structures is { } kinds)
                foreach (var st in kinds.ModuleCost(k))
                {
                    var ic = new IconCount(IconCatalog.ForItem(_content.Items[st.Item].Id), 16, UiTheme.Caption);
                    ic.SetText(IconCatalog.ForItem(_content.Items[st.Item].Id), st.Count.ToString(), Muted);
                    costs.AddChild(ic);
                }
            row.AddChild(costs);
            _paletteList.AddChild(row);
            _palette.Add((k, b));
        }
        col.AddChild(new ScrollWithHint(_paletteList) { Name = "PaletteScroll" });
    }

    private PanelContainer SideSection(string name, string titleKey, string titleIcon, out VBoxContainer body, float ratio)
    {
        var p = Panel(name, UiTheme.Edge);
        p.SizeFlagsVertical = SizeFlags.ExpandFill;
        p.SizeFlagsStretchRatio = ratio;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        p.AddChild(col);
        var head = new HBoxContainer();
        head.AddChild(new IconRect(titleIcon, 18, UiTheme.Gold, tooltip: false));
        var t = Label(name + "Title", 1, UiTheme.Heading, UiTheme.Body, Colors.White); t.SetFit(Loc.T(titleKey)); t.SizeFlagsHorizontal = SizeFlags.ExpandFill; t.AutowrapMode = TextServer.AutowrapMode.Off;
        head.AddChild(t);
        col.AddChild(head);
        body = col;
        return p;
    }

    private void BuildSidePanel()
    {
        _sidePanel = new PanelContainer { Name = "SidePanel", MouseFilter = MouseFilterEnum.Ignore };
        _sidePanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        _sidePanel.AddChild(col);

        _costPanel = SideSection("CostPanel", "bp.cost.title", "status:cargo", out var cost, 4f);
        _costSummary = Label("CostSummary", 2, UiTheme.Body, UiTheme.Small); cost.AddChild(_costSummary);
        _costGrid = new GridContainer { Name = "CostGrid", Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _costGrid.AddThemeConstantOverride("h_separation", 8);
        _costGrid.AddThemeConstantOverride("v_separation", 2);
        cost.AddChild(new ScrollWithHint(_costGrid) { Name = "CostScroll" });
        _costMissing = Label("CostMissing", 3, UiTheme.Body, UiTheme.Small, Bad); cost.AddChild(_costMissing);
        col.AddChild(_costPanel);

        _statsPanel = SideSection("StatsPanel", "bp.stats.title", "status:speed", out var stats, 3.5f);
        _statsList = new VBoxContainer { Name = "StatsList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _statsList.AddThemeConstantOverride("separation", 2);
        stats.AddChild(new ScrollWithHint(_statsList) { Name = "StatsScroll" });
        col.AddChild(_statsPanel);

        _validationPanel = SideSection("ValidationPanel", "bp.val.title", "status:warning", out var val, 4.5f);
        _valSummary = Label("ValidationSummary", 1, UiTheme.Small, UiTheme.Caption, Muted); val.AddChild(_valSummary);
        _valList = new VBoxContainer { Name = "ValidationList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _valList.AddThemeConstantOverride("separation", 3);
        val.AddChild(new ScrollWithHint(_valList) { Name = "ValidationScroll" });
        col.AddChild(_validationPanel);
    }

    private void BuildFootBar()
    {
        _footBar = Panel("FootBar");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _footBar.AddChild(row);
        _statusIcon = new IconRect("tool:info", 22, Muted, tooltip: false);
        row.AddChild(_statusIcon);
        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        texts.AddThemeConstantOverride("separation", 1);
        _status = Label("Status", 1, UiTheme.Body, UiTheme.Small); texts.AddChild(_status);
        _legend = Label("Legend", 2, UiTheme.Caption, UiTheme.Caption - 1, Muted); _legend.SetFit(Loc.T("bp.legend")); texts.AddChild(_legend);
        row.AddChild(texts);
        _save = UiKit.Button(Loc.T("bp.save"), () => SaveSlot(1), 13, 120); _save.Name = "SaveButton"; _save.TooltipText = Loc.T("bp.save") + " (" + Cap("bpSave") + ")";
        _load = UiKit.Button(Loc.T("bp.load"), () => LoadSlot(1), 13, 120); _load.Name = "LoadButton";
        _revert = UiKit.Button(Loc.T("bp.revert"), () => Revert(), 14, 120); _revert.Name = "RevertButton"; _revert.TooltipText = Loc.T("bp.revert") + " (" + Cap("bpRevert") + ")";
        _apply = UiKit.Button(Loc.T("bp.apply"), () => Apply(), 16, 150); _apply.Name = "ApplyButton"; _apply.TooltipText = Loc.T("bp.apply") + " (" + Cap("bpApply") + ")";
        foreach (var b in new[] { _save, _load, _revert, _apply }) { b.ClipText = true; row.AddChild(b); }
    }

    // ---- state -----------------------------------------------------------------------------------------------------

    /// <summary>Opens the editor on the ship's current layout.</summary>
    public void Open()
    {
        _discardArmed = false;
        _s.Open();
        _hoverCell = null;
        _lastSize = Vector2.Zero;
        _title.SetFit(Loc.T("bp.title"));
        UiFx.Cancel(this);
        Visible = true;
        _refresh = true; _sceneDirty = true;
        SyncViewButtons();
    }

    public void Close()
    {
        Visible = false;
        _hoverCell = null;
    }

    /// <summary>Loads the ship's layout again (after an apply).</summary>
    public void Reload()
    {
        _s.Open();
        _discardArmed = false;
        SyncViewButtons();
    }

    public void SetTool(BlueprintTool tool) { _s.SetTool(tool); _discardArmed = false; }

    public void PickKind(ModuleKind kind) { _s.PickKind(kind); _discardArmed = false; }

    public void SetView(PlanView view)
    {
        switch (view)
        {
            case PlanView.Orbit: _s.Camera.SetOrbit(); break;
            case PlanView.Top: _s.Camera.SetTop(); break;
            default: _s.Camera.SetSection(_s.DefaultSectionLayer()); break;
        }
        SyncViewButtons();
        _sceneDirty = true; _refresh = true;
    }

    private void SyncViewButtons()
    {
        foreach (var (v, b) in _viewButtons) b.SetPressedNoSignal(_s.Camera.View == v);
    }

    public void FrameAll()
    {
        var (min, max) = _s.Draft.Bounds(1);
        _s.Camera.Frame(min, max);
    }

    private void Report(string? key) { if (key is not null) Say?.Invoke(Loc.T(key)); }

    public void Mirror()
    {
        var r = _s.MirrorSelection();
        Say?.Invoke(Loc.T("bp.toast.mirrored", r.Done, r.Skipped));
    }

    public void Copy()
    {
        int n = _s.CopySelection();
        Say?.Invoke(n > 0 ? Loc.T("bp.toast.copied", n) : Loc.T("bp.toast.nothingToCopy"));
        _refresh = true;
    }

    public void Paste()
    {
        if (_s.Draft.ClipboardCount == 0) { Say?.Invoke(Loc.T("bp.toast.noClipboard")); return; }
        var r = _s.Paste(_hoverCell);
        Say?.Invoke(Loc.T("bp.toast.pasted", r.Done, r.Skipped));
    }

    public void Upgrade() => Report(BlueprintSession.EditKey(_s.UpgradeSelection()));

    public void Undo() { if (!_s.Undo()) Say?.Invoke(Loc.T("bp.toast.nothingToUndo")); }

    public void Redo() { if (!_s.Redo()) Say?.Invoke(Loc.T("bp.toast.nothingToRedo")); }

    public void Revert() { _s.Revert(); _discardArmed = false; }

    public void Apply()
    {
        if (!_s.CanApply) return;
        ApplyRequested?.Invoke(_s.ToCommand());
    }

    public void RequestClose()
    {
        if (_s.Dirty && !_discardArmed) { _discardArmed = true; Say?.Invoke(Loc.T("bp.discard")); return; }
        CloseRequested?.Invoke();
    }

    // ---- the save-as-blueprint slot hook (a stub: one file per slot under user://; slots, presets and variants come later) ----------

    private static string SlotPath(int slot) => $"user://blueprints/ship_slot{slot}.txt";

    public bool SaveSlot(int slot)
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute("user://blueprints");
            using var f = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write);
            if (f is null) { Say?.Invoke(Loc.T("bp.saveFailed")); return false; }
            f.StoreString(BlueprintFormat.Serialize(_s.Draft, "ship"));
            Say?.Invoke(Loc.T("bp.saved", slot));
            return true;
        }
        catch (Exception) { Say?.Invoke(Loc.T("bp.saveFailed")); return false; }
    }

    public bool LoadSlot(int slot)
    {
        if (!FileAccess.FileExists(SlotPath(slot))) { Say?.Invoke(Loc.T("bp.loadNone", slot)); return false; }
        using var f = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Read);
        var parsed = f is null ? null : BlueprintFormat.Parse(f.GetAsText());
        if (parsed is null) { Say?.Invoke(Loc.T("bp.loadNone", slot)); return false; }
        var r = _s.Draft.ReplaceWith(parsed.Value.Parts);
        Say?.Invoke(Loc.T("bp.loaded", r.Done));
        return true;
    }
}
