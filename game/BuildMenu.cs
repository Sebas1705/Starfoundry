using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>What the menu shows about one entry right now (the game fills it from <see cref="BuildCatalog"/> and the world).</summary>
public sealed record BuildEntryView(string Name, string Key, string Role, string Cost, string Power, string Description, string Status, bool Locked, bool Affordable, bool Selected,
    string IconId = "", IReadOnlyList<(string Icon, int Need, int Have)>? CostItems = null);

/// <summary>
/// The build menu (B), docked at the right of the screen: a tab per category (an icon and a name) over a grid of icon tiles, and under the grid a
/// detail pane for the tile in focus or under the mouse (big icon, name, what it does, cost as icons with counts in red when the hold lacks
/// them, power, and why it is locked or what is missing). Arrows move between tiles, Enter or a click chooses the tool, PgUp and PgDn change
/// tab, B and Esc close it (the game handles those two; the key panel below the menu lists them). A locked tile can still be pressed: the
/// game then says what unlocks it.
/// </summary>
public partial class BuildMenu : Control
{
    private static readonly Color Muted = UiTheme.Muted;

    private readonly Func<IReadOnlyList<BuildEntry>> _entries;
    private readonly Func<BuildEntry, BuildEntryView> _view;
    private PanelContainer _panel = null!;
    private HBoxContainer _tabs = null!;
    private GridContainer _grid = null!;
    private ScrollWithHint _scroll = null!;
    private Label _title = null!, _empty = null!, _banner = null!;
    private readonly Dictionary<BuildCategory, Button> _tabButtons = new();
    private readonly List<Tile> _cells = new();
    private BuildCategory _tab = BuildCategory.Logistics;
    private string _signature = "";
    private Rect2 _dock = new(0, 0, 480, 600);
    private string? _detailId;

    // The detail pane.
    private IconRect _dIcon = null!;
    private BuildPreview _dPreview = null!;
    private PanelContainer _dPane = null!;
    private FitLabel _dName = null!, _dRole = null!, _dDesc = null!, _dStatus = null!;
    private HFlowContainer _dCost = null!;
    private IconCount _dPower = null!;
    private IconRect _dStatusIcon = null!;
    private string _dCostSig = "";

    /// <summary>One tile of the grid: the entry, its button and the parts that change with the world.</summary>
    private sealed class Tile
    {
        public BuildEntry Entry = null!;
        public Button Button = null!;
        public FitLabel Name = null!;
        public Label Key = null!;
        public CostStrip Cost = null!;
        public IconRect Icon = null!, Badge = null!;
    }

    /// <summary>The player chose an entry (Enter or click).</summary>
    public event Action<BuildEntry>? Chosen;

    public event Action? CloseRequested;

    /// <summary>A one-line notice under the header (ADR 0039: "Automation is built in a station" while the ship cannot be built on); empty hides it.</summary>
    public Func<string>? Banner { get; set; }

    public BuildCategory Tab => _tab;

    /// <summary>The rectangle the menu fills: a right-hand column set by the game from the viewport and the key panel below it.</summary>
    public Rect2 Dock
    {
        get => _dock;
        set
        {
            if (_dock == value) return;
            _dock = value;
            ApplyDock();
            _signature = ""; // the number of columns may change
        }
    }

    public BuildMenu(Func<IReadOnlyList<BuildEntry>> entries, Func<BuildEntry, BuildEntryView> view)
    {
        _entries = entries;
        _view = view;
        Visible = false;
        UiFx.Present(this); // fades and grows in instead of popping
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        // The root only hosts the docked panel: every click outside the panel goes through to the game.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Dock());
        AddChild(_panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(new IconRect("tool:build", 26, UiTheme.Gold, tooltip: false));
        _title = UiKit.Text(UiTheme.Title, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        var close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        close.Icon = Icons.Get("tool:close", 16);
        close.ExpandIcon = false;
        close.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
        close.FocusMode = FocusModeEnum.None;
        close.TooltipText = Loc.T("icon.tool:close");
        head.AddChild(close);
        root.AddChild(head);
        root.AddChild(UiTheme.Rule());

        _banner = UiKit.Text(UiTheme.Body, UiTheme.Warn);
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _banner.CustomMinimumSize = new Vector2(10, 0);
        _banner.Visible = false;
        root.AddChild(_banner);

        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 4);
        root.AddChild(_tabs);

        _grid = new GridContainer { Columns = 1 };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        _scroll = new ScrollWithHint(_grid);
        _scroll.Scroll.Name = "EntryScroll";
        root.AddChild(_scroll);
        _empty = UiKit.Text(14, Muted);
        root.AddChild(_empty);
        root.AddChild(BuildDetailPane());
        ApplyDock();
    }

    private Control BuildDetailPane()
    {
        var pane = _dPane = new PanelContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(0, 118) };
        pane.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.07f, 0.1f, 0.16f, 0.95f), new Color(0.3f, 0.4f, 0.6f, 0.6f), 6, 8f));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        pane.AddChild(row);
        _dIcon = new IconRect("tool:build", 56, null, tooltip: false) { SizeFlagsVertical = SizeFlags.ShrinkBegin };
        row.AddChild(_dIcon);
        _dPreview = new BuildPreview(104) { Visible = false };
        row.AddChild(_dPreview);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 3);
        row.AddChild(col);
        _dName = new FitLabel(1, UiTheme.Heading + 2, UiTheme.Body) { AutowrapMode = TextServer.AutowrapMode.Off };
        _dName.AddThemeColorOverride("font_color", Colors.White);
        _dRole = new FitLabel(2, UiTheme.Small, UiTheme.Caption);
        _dRole.AddThemeColorOverride("font_color", Muted);
        _dDesc = new FitLabel(3, UiTheme.Small + 1, UiTheme.Caption);
        _dDesc.AddThemeColorOverride("font_color", UiTheme.Text);
        _dCost = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
        _dCost.AddThemeConstantOverride("h_separation", 12);
        _dCost.AddThemeConstantOverride("v_separation", 2);
        _dPower = new IconCount("status:power", 18, UiTheme.Body);
        var status = new HBoxContainer();
        status.AddThemeConstantOverride("separation", 5);
        _dStatusIcon = new IconRect("status:locked", 16, UiTheme.Warn, tooltip: false);
        _dStatus = new FitLabel(2, UiTheme.Small + 1, UiTheme.Caption);
        _dStatus.AddThemeColorOverride("font_color", UiTheme.Warn);
        _dStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        status.AddChild(_dStatusIcon);
        status.AddChild(_dStatus);
        foreach (var c in new Control[] { _dName, _dRole, _dDesc, _dCost, status }) col.AddChild(c);
        _dCost.AddChild(_dPower);
        return pane;
    }

    private void ApplyDock()
    {
        if (_panel is null) return;
        _panel.CustomMinimumSize = _dock.Size;
        _panel.Position = _dock.Position;
        _panel.Size = _dock.Size;
    }

    /// <summary>Shows the menu on a tab (the current one by default) with the first entry focused.</summary>
    public void Open(BuildCategory? tab = null)
    {
        if (tab is { } t) _tab = t;
        UiFx.Cancel(this);
        Visible = true;
        _signature = "";
        _detailId = null;
        Refresh(Size);
        FocusFirst();
    }

    public void Close() => UiFx.Dismiss(this);

    public void SetTab(BuildCategory tab)
    {
        _tab = tab;
        _signature = "";
        _detailId = null;
        Refresh(Size);
        FocusFirst();
    }

    private void FocusFirst()
    {
        var selected = _cells.FirstOrDefault(c => _view(c.Entry).Selected);
        var target = selected?.Button ?? _cells.FirstOrDefault()?.Button;
        target?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void StepTab(int step)
    {
        var tabs = _tabButtons.Keys.OrderBy(k => (int)k).ToList();
        if (tabs.Count == 0) return;
        int i = Math.Max(0, tabs.IndexOf(_tab));
        SetTab(tabs[((i + step) % tabs.Count + tabs.Count) % tabs.Count]);
    }

    private const float TileMinWidth = 92f, Gap = 8f, TilePad = 5f, IconArea = 38f, CostHeight = 14f, NameLines = 2;

    /// <summary>The tile height comes from its content (padding, icon row, two name lines at the largest name size, the price row), so no tile can be too short for its text.</summary>
    private static float TileHeight
    {
        get
        {
            var font = UiFit.DefaultFont;
            return TilePad * 2 + IconArea + 2 + UiFit.Height(font, UiTheme.Small, (int)NameLines) + 2 + CostHeight;
        }
    }

    /// <summary>Test hook of the text audit: appended to every name so the tiles are checked against names longer than the real ones.</summary>
    public static string? StressSuffix;

    private float InnerWidth => _dock.Size.X - 24f - 16f; // panel margins and the scroll bar

    /// <summary>As many columns as fit at the minimum tile width.</summary>
    private int Columns() => Math.Max(2, (int)((InnerWidth + Gap) / (TileMinWidth + Gap)));

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string TabKey(BuildCategory c) => "build.tab." + c.ToString().ToLowerInvariant();

    /// <summary>Re-reads the world: rebuilds the grid when the entries or the tab changed, otherwise only updates texts and colours.</summary>
    public void Refresh(Vector2 viewport)
    {
        if (!Visible) return;
        var all = _entries();
        var categories = BuildCatalog.Categories.Where(c => all.Any(e => e.Category == c)).ToList();
        if (categories.Count > 0 && !categories.Contains(_tab)) _tab = categories[0];
        int cols = Columns();
        _grid.Columns = cols;
        _title.Text = Loc.T("build.title");
        string banner = Banner?.Invoke() ?? "";
        _banner.Text = banner;
        _banner.Visible = banner.Length > 0;

        string signature = cols + "|" + string.Join(",", categories) + "|" + _tab + "|" + string.Join(",", all.Where(e => e.Category == _tab).Select(e => e.Id)) + Loc.Lang;
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild(categories, all);
        }
        foreach (var kv in _tabButtons) { kv.Value.SetPressedNoSignal(kv.Key == _tab); }
        foreach (var cell in _cells) Update(cell);
        _empty.Text = _cells.Count == 0 ? Loc.T("build.empty") : "";
        _empty.Visible = _cells.Count == 0;
        FillDetail();
    }

    private void Rebuild(List<BuildCategory> categories, IReadOnlyList<BuildEntry> all)
    {
        foreach (var child in _tabs.GetChildren()) { _tabs.RemoveChild(child); child.QueueFree(); }
        _tabButtons.Clear();
        float tabWidth = categories.Count == 0 ? 0f : (_dock.Size.X - 24f - 4f * (categories.Count - 1)) / categories.Count;
        foreach (var c in categories)
        {
            var category = c;
            var b = UiKit.Button("", () => SetTab(category), 14);
            b.ToggleMode = true;
            b.Icon = Icons.Get(IconCatalog.ForBuildTab(c), 20);
            b.ExpandIcon = false;
            b.IconAlignment = HorizontalAlignment.Left;
            b.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
            b.AddThemeColorOverride("icon_pressed_color", Colors.White);
            b.AddThemeColorOverride("icon_hover_color", Colors.White);
            b.AddThemeColorOverride("icon_hover_pressed_color", Colors.White);
            b.AddThemeConstantOverride("icon_max_width", 20);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.FocusMode = FocusModeEnum.None; // PgUp and PgDn (or a click) change tab; the arrows stay on the tiles
            UiFit.ShrinkButton(b, Loc.T(TabKey(c)), tabWidth, 14, 10, iconWidth: 20f + 4f);
            _tabButtons[c] = b;
            _tabs.AddChild(b);
        }

        foreach (var child in _grid.GetChildren()) { _grid.RemoveChild(child); child.QueueFree(); }
        _cells.Clear();
        int cols = Columns();
        float width = (InnerWidth - Gap * (cols - 1)) / cols;
        float inner = width - TilePad * 2 - 2f; // the tile's border takes a pixel on each side
        foreach (var entry in all.Where(e => e.Category == _tab))
        {
            var tile = new Tile { Entry = entry };
            var b = UiKit.Button("", () => { }, 13);
            b.CustomMinimumSize = new Vector2(width, TileHeight);
            b.ClipContents = true; // nothing a tile holds can draw over its neighbour
            var e = entry;
            b.Pressed += () => Chosen?.Invoke(e);
            b.FocusEntered += () => { _detailId = e.Id; FillDetail(); };
            b.MouseEntered += () => { _detailId = e.Id; FillDetail(); };
            // Keyboard focus: a white ring (the gold edge already means "the tool in your hand").
            var ring = UiTheme.FocusRing();
            ring.BorderColor = Colors.White;
            b.AddThemeStyleboxOverride("focus", ring);
            var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, (int)TilePad);
            box.AddThemeConstantOverride("separation", 2);
            // Icon row: the quick key at the left corner, the state badge at the right, the icon in the middle.
            var iconRow = new Control { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(inner, IconArea) };
            tile.Key = UiKit.Text(UiTheme.Caption, UiTheme.Muted);
            tile.Key.Position = new Vector2(1, 0);
            tile.Badge = new IconRect("status:locked", 14, UiTheme.Warn, tooltip: false) { Visible = false, Position = new Vector2(inner - 14, 0) };
            tile.Icon = new IconRect("generic", 36, null, tooltip: false) { Position = new Vector2((inner - 36) / 2f, 1) };
            iconRow.AddChild(tile.Icon);
            iconRow.AddChild(tile.Key);
            iconRow.AddChild(tile.Badge);
            box.AddChild(iconRow);
            tile.Name = new FitLabel((int)NameLines, UiTheme.Small, UiTheme.Caption - 1) { FitWidth = inner, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            box.AddChild(tile.Name);
            tile.Cost = new CostStrip(inner, CostHeight);
            box.AddChild(tile.Cost);
            b.AddChild(box);
            tile.Button = b;
            _grid.AddChild(b);
            _cells.Add(tile);
        }
        _scroll.UpdateHints();
    }

    private void Update(Tile cell)
    {
        var v = _view(cell.Entry);
        string name = Capitalise(v.Name) + StressSuffix;
        cell.Key.Text = v.Key;
        cell.Name.SetFit(name);
        // Locked tiles stay readable: the name keeps the muted blue (not the old near-black grey) and the padlock badge says why.
        cell.Name.AddThemeColorOverride("font_color", v.Locked ? UiTheme.Muted : v.Selected ? UiTheme.Gold : UiTheme.Text);
        cell.Icon.Set(v.IconId.Length > 0 ? v.IconId : "generic", v.Locked ? new Color(0.55f, 0.6f, 0.72f) : null);
        // The corner badge: a padlock when locked, a red cross when the hold cannot pay.
        cell.Badge.Visible = v.Locked || !v.Affordable;
        if (cell.Badge.Visible) cell.Badge.Set(v.Locked ? "status:locked" : "status:cross", v.Locked ? UiTheme.Warn : UiTheme.Bad);
        cell.Cost.Set(v.Locked ? null : v.CostItems, v.Locked ? "" : Loc.T("ui.free"));
        var b = cell.Button;
        b.AddThemeStyleboxOverride("normal", UiTheme.Tile(v.Selected, v.Locked));
        b.AddThemeStyleboxOverride("hover", UiTheme.Tile(v.Selected, v.Locked, hover: true));
        // The tooltip always carries the full name, so a name that had to be cut is still readable.
        b.TooltipText = name + (v.Role.Length > 0 ? "  -  " + v.Role : "") + (v.Status.Length > 0 ? "\n" + v.Status : "");
    }

    private BuildEntry? DetailEntry()
    {
        var shown = _cells.FirstOrDefault(c => c.Entry.Id == _detailId)
            ?? _cells.FirstOrDefault(c => c.Button.HasFocus())
            ?? _cells.FirstOrDefault(c => _view(c.Entry).Selected)
            ?? _cells.FirstOrDefault();
        return shown?.Entry;
    }

    /// <summary>Fills the pane under the grid for the focused or hovered tile.</summary>
    private void FillDetail()
    {
        if (_dName is null) return;
        var entry = DetailEntry();
        if (entry is null)
        {
            _dName.SetFit("");
            _dRole.SetFit("");
            _dDesc.SetFit("");
            _dCost.Visible = false;
            _dStatus.SetFit("");
            _dStatusIcon.Visible = false;
            return;
        }
        var v = _view(entry);
        _dIcon.Set(v.IconId.Length > 0 ? v.IconId : "generic", v.Locked ? new Color(0.5f, 0.54f, 0.64f) : null);
        bool model = _dPreview.Show(entry); // a turning model of machines and modules; belts keep the big icon
        _dPreview.Visible = model;
        _dIcon.Visible = !model;
        string full = (v.Key.Length > 0 ? $"[{v.Key}]  " : "") + Capitalise(v.Name) + StressSuffix;
        _dName.SetFit(full);
        _dName.AddThemeColorOverride("font_color", v.Locked ? UiTheme.Dim : v.Selected ? UiTheme.Gold : Colors.White);
        _dRole.SetFit(v.Role);
        _dDesc.SetFit(v.Description == v.Role ? "" : v.Description); // the role line already says it when both are the same

        // Cost as icons with counts; the power it draws or makes after them.
        var items = v.CostItems ?? Array.Empty<(string Icon, int Need, int Have)>();
        string sig = string.Join(",", items.Select(i => i.Icon));
        if (sig != _dCostSig)
        {
            _dCostSig = sig;
            foreach (var c in _dCost.GetChildren().OfType<IconCount>().Where(c => c != _dPower).ToList()) { _dCost.RemoveChild(c); c.QueueFree(); }
            for (int i = 0; i < items.Count; i++) { var ic = new IconCount(items[i].Icon, 20, UiTheme.Body); _dCost.AddChild(ic); _dCost.MoveChild(ic, i); }
        }
        var costNodes = _dCost.GetChildren().OfType<IconCount>().Where(c => c != _dPower).ToList();
        for (int i = 0; i < costNodes.Count && i < items.Count; i++) costNodes[i].SetCost(items[i].Icon, items[i].Need, items[i].Have);
        if (items.Count == 0 && v.Cost.Length > 0) { /* free: the text says so */ }
        if (entry.PowerUse > 0) { _dPower.SetText("status:power", "-" + entry.PowerUse, UiTheme.Warn, v.Power); _dPower.Visible = true; }
        else if (entry.PowerOutput > 0) { _dPower.SetText("status:power", "+" + entry.PowerOutput, UiTheme.Good, v.Power); _dPower.Visible = true; }
        else _dPower.Visible = false;
        _dCost.Visible = items.Count > 0 || _dPower.Visible;

        bool problem = v.Status.Length > 0;
        _dStatus.SetFit(v.Status);
        _dStatusIcon.Visible = problem;
        if (problem) _dStatusIcon.Set(v.Locked ? "status:locked" : "status:cross", v.Locked ? UiTheme.Warn : UiTheme.Bad);
        _dStatus.AddThemeColorOverride("font_color", v.Locked ? UiTheme.Warn : UiTheme.Bad);
        _dPane.TooltipText = string.Join((char)10, new[] { full, v.Role, v.Description == v.Role ? "" : v.Description, v.Status }.Where(t => t.Length > 0));
    }

    public override void _Input(InputEvent ev)
    {
        if (!Visible || ev is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Pageup) { GetViewport().SetInputAsHandled(); StepTab(-1); }
        else if (k.Keycode == Key.Pagedown) { GetViewport().SetInputAsHandled(); StepTab(1); }
    }
}
