using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>What the menu shows about one entry right now (the game fills it from <see cref="BuildCatalog"/> and the world).</summary>
public sealed record BuildEntryView(string Name, string Key, string Role, string Cost, string Power, string Description, string Status, bool Locked, bool Affordable, bool Selected);

/// <summary>
/// The build menu (B), docked at the right of the screen: a tab per category with a list of entries, each showing its name, quick key, cost,
/// power use, a one-line description and whether it is locked or unaffordable. The left and the centre of the view stay visible. Arrows move
/// between entries, Enter or a click chooses the tool, PgUp and PgDn change tab, B and Esc close it (the game handles those two; the key panel
/// below the menu lists them). A locked entry can still be pressed: the game then says what unlocks it.
/// </summary>
public partial class BuildMenu : Control
{
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);
    private static readonly Color Good = new(0.5f, 0.9f, 0.55f);
    private static readonly Color Bad = new(1f, 0.55f, 0.45f);

    private readonly Func<IReadOnlyList<BuildEntry>> _entries;
    private readonly Func<BuildEntry, BuildEntryView> _view;
    private PanelContainer _panel = null!;
    private HBoxContainer _tabs = null!;
    private GridContainer _grid = null!;
    private Label _title = null!, _empty = null!;
    private readonly Dictionary<BuildCategory, Button> _tabButtons = new();
    private readonly List<(BuildEntry Entry, Button Button, Label Name, Label Cost, Label Power, Label Detail, Label Status)> _cells = new();
    private BuildCategory _tab = BuildCategory.Logistics;
    private string _signature = "";
    private Rect2 _dock = new(0, 0, 480, 600);

    /// <summary>The player chose an entry (Enter or click).</summary>
    public event Action<BuildEntry>? Chosen;

    public event Action? CloseRequested;

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
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        // The root only hosts the docked panel: every click outside the panel goes through to the game.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.05f, 0.07f, 0.11f, 0.95f), new Color(0.35f, 0.45f, 0.65f, 0.85f), 8, 12f));
        AddChild(_panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(root);

        var head = new HBoxContainer();
        _title = UiKit.Text(22, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        var close = UiKit.Button("X", () => CloseRequested?.Invoke(), 13);
        close.FocusMode = FocusModeEnum.None;
        head.AddChild(close);
        root.AddChild(head);

        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 4);
        root.AddChild(_tabs);

        _grid = new GridContainer { Columns = 1 };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 6);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true, Name = "EntryScroll", SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(_grid);
        root.AddChild(scroll);
        _empty = UiKit.Text(14, Muted);
        root.AddChild(_empty);
        ApplyDock();
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
        Visible = true;
        _signature = "";
        Refresh(Size);
        FocusFirst();
    }

    public void Close() => Visible = false;

    public void SetTab(BuildCategory tab)
    {
        _tab = tab;
        _signature = "";
        Refresh(Size);
        FocusFirst();
    }

    private void FocusFirst()
    {
        var selected = _cells.FirstOrDefault(c => _view(c.Entry).Selected);
        var target = selected.Button ?? _cells.FirstOrDefault().Button;
        target?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void StepTab(int step)
    {
        var tabs = _tabButtons.Keys.OrderBy(k => (int)k).ToList();
        if (tabs.Count == 0) return;
        int i = Math.Max(0, tabs.IndexOf(_tab));
        SetTab(tabs[((i + step) % tabs.Count + tabs.Count) % tabs.Count]);
    }

    /// <summary>One column in the usual docked width, two when the dock is wide (a big window).</summary>
    private int Columns() => _dock.Size.X >= 720f ? 2 : 1;

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

        string signature = cols + "|" + string.Join(",", categories) + "|" + _tab + "|" + string.Join(",", all.Where(e => e.Category == _tab).Select(e => e.Id)) + Loc.Lang;
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild(categories, all);
        }
        foreach (var kv in _tabButtons) { kv.Value.Text = Loc.T(TabKey(kv.Key)); kv.Value.SetPressedNoSignal(kv.Key == _tab); }
        foreach (var cell in _cells) Update(cell);
        _empty.Text = _cells.Count == 0 ? Loc.T("build.empty") : "";
        _empty.Visible = _cells.Count == 0;
    }

    private void Rebuild(List<BuildCategory> categories, IReadOnlyList<BuildEntry> all)
    {
        foreach (var child in _tabs.GetChildren()) { _tabs.RemoveChild(child); child.QueueFree(); }
        _tabButtons.Clear();
        foreach (var c in categories)
        {
            var category = c;
            var b = UiKit.Button(Loc.T(TabKey(c)), () => SetTab(category), 14);
            b.ToggleMode = true;
            b.ClipText = true;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.FocusMode = FocusModeEnum.None; // PgUp and PgDn (or a click) change tab; the arrows stay on the entries
            _tabButtons[c] = b;
            _tabs.AddChild(b);
        }

        foreach (var child in _grid.GetChildren()) { _grid.RemoveChild(child); child.QueueFree(); }
        _cells.Clear();
        int cols = Columns();
        float width = Math.Clamp((_dock.Size.X - 24f - 12f - 8f * (cols - 1)) / cols, 200f, 470f); // panel margins, scroll bar and gaps
        foreach (var entry in all.Where(e => e.Category == _tab))
        {
            var b = UiKit.Button("", () => { }, 13);
            b.CustomMinimumSize = new Vector2(width, 64); // grown to fit the text in Update
            b.Alignment = HorizontalAlignment.Left;
            var e = entry;
            b.Pressed += () => Chosen?.Invoke(e);
            var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
            box.AddThemeConstantOverride("separation", 1);
            Label L(int size, Color? color = null)
            {
                var l = UiKit.Text(size, color);
                l.ClipText = true;
                l.CustomMinimumSize = new Vector2(width - 18f, 0);
                box.AddChild(l);
                return l;
            }
            var name = L(16);
            var cost = L(13);
            cost.ClipText = false;
            cost.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            cost.CustomMinimumSize = new Vector2(width - 18f, 17f);
            var power = L(12, new Color(0.95f, 0.82f, 0.5f));
            var detail = L(12, Muted);
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            detail.ClipText = false;
            detail.CustomMinimumSize = new Vector2(width - 18f, 0f);
            var status = L(12);
            b.AddChild(box);
            _grid.AddChild(b);
            _cells.Add((entry, b, name, cost, power, detail, status));
        }
    }

    private void Update((BuildEntry Entry, Button Button, Label Name, Label Cost, Label Power, Label Detail, Label Status) cell)
    {
        var v = _view(cell.Entry);
        cell.Name.Text = (v.Key.Length > 0 ? $"[{v.Key}]  " : "") + Capitalise(v.Name) + (v.Selected ? "   <" : "");
        cell.Name.AddThemeColorOverride("font_color", v.Locked ? new Color(0.6f, 0.62f, 0.7f) : v.Selected ? new Color(1f, 0.9f, 0.5f) : Colors.White);
        cell.Cost.Text = v.Cost;
        cell.Power.Text = v.Power;
        cell.Power.Visible = v.Power.Length > 0;
        cell.Cost.AddThemeColorOverride("font_color", v.Locked ? new Color(0.55f, 0.57f, 0.64f) : v.Affordable ? Good : Bad);
        cell.Detail.Text = v.Description;
        cell.Status.Text = v.Status;
        cell.Status.Visible = v.Status.Length > 0;
        cell.Detail.Visible = v.Description.Length > 0;
        // The card is as tall as its text: no reserved blank lines under a short description.
        float textWidth = Math.Max(120f, cell.Button.CustomMinimumSize.X - 18f);
        int Lines(string s, float charWidth) => s.Length == 0 ? 0 : (int)Math.Ceiling(s.Length * charWidth / textWidth);
        float h = 14f + 22f + 18f * Math.Max(1, Lines(v.Cost, 7f)) + (v.Power.Length > 0 ? 17f : 0f) + 16f * Lines(v.Description, 6.4f) + (v.Status.Length > 0 ? 18f * Lines(v.Status, 6.4f) : 0f);
        cell.Button.CustomMinimumSize = new Vector2(cell.Button.CustomMinimumSize.X, Math.Max(60f, h));
        cell.Status.AddThemeColorOverride("font_color", v.Locked ? new Color(1f, 0.82f, 0.4f) : Bad);
        cell.Button.Modulate = v.Locked ? new Color(0.78f, 0.78f, 0.85f) : Colors.White;
        cell.Button.TooltipText = v.Role + (v.Status.Length > 0 ? "\n" + v.Status : "");
    }

    public override void _Input(InputEvent ev)
    {
        if (!Visible || ev is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Pageup) { GetViewport().SetInputAsHandled(); StepTab(-1); }
        else if (k.Keycode == Key.Pagedown) { GetViewport().SetInputAsHandled(); StepTab(1); }
    }
}
