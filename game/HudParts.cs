using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>A label with an icon drawn at its left (a banner, a line of a card). The icon takes the label's colour unless it has its own tint.</summary>
public partial class IconLabel : Label
{
    private string _icon = "";
    private readonly int _iconSize;
    private Color? _tint;

    public IconLabel(int fontSize = UiTheme.Body, int iconSize = 18, Color? colour = null)
    {
        _iconSize = iconSize;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeFontSizeOverride("font_size", fontSize);
        AddThemeColorOverride("font_color", colour ?? UiTheme.Text);
        var pad = new StyleBoxEmpty { ContentMarginLeft = iconSize + 6 };
        AddThemeStyleboxOverride("normal", pad);
    }

    public void SetIcon(string icon, Color? tint = null)
    {
        if (_icon == icon && _tint == tint) return;
        _icon = icon;
        _tint = tint;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_icon.Length == 0) return;
        float y = (Size.Y - _iconSize) / 2f;
        var tint = _tint ?? GetThemeColor("font_color");
        // A thin dark halo keeps the icon readable over bright space.
        DrawTextureRect(Icons.Get(_icon, _iconSize), new Rect2(0.5f, y + 1f, _iconSize, _iconSize), false, new Color(0f, 0f, 0f, 0.55f));
        DrawTextureRect(Icons.Get(_icon, _iconSize), new Rect2(0, y, _iconSize, _iconSize), false, tint);
    }
}

/// <summary>
/// The hold in the bottom strip: a cargo icon, a fill gauge with <c>used/capacity</c>, and an icon with a count for every kind of item in the hold.
/// The gauge turns amber when the hold is nearly full and red when it is.
/// </summary>
public partial class HoldStrip : HBoxContainer
{
    private readonly IconRect _icon = new("status:cargo", 20, null, tooltip: false);
    private readonly GaugeBar _bar = new(96f, 10f);
    private readonly Label _total = new() { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
    private readonly HBoxContainer _items = new() { MouseFilter = MouseFilterEnum.Pass };
    private readonly Dictionary<string, IconCount> _counts = new();
    private string _signature = "";

    public HoldStrip()
    {
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeConstantOverride("separation", 8);
        _total.AddThemeFontSizeOverride("font_size", UiTheme.Body);
        _items.AddThemeConstantOverride("separation", 12);
        AddChild(_icon);
        AddChild(_bar);
        AddChild(_total);
        AddChild(_items);
    }

    public void Set(int used, int capacity, bool full, bool nearlyFull, IReadOnlyList<(string Icon, int Count)> items)
    {
        var colour = full ? UiTheme.Bad : nearlyFull ? UiTheme.Warn : UiTheme.Good;
        _bar.Set(capacity <= 0 ? 0f : used / (float)capacity, colour);
        _total.Text = $"{used}/{capacity}";
        _total.AddThemeColorOverride("font_color", full ? UiTheme.Bad : UiTheme.Text);
        TooltipText = Loc.T("hud.hold", used, capacity);
        _icon.Set("status:cargo", colour);

        string signature = string.Join(",", System.Linq.Enumerable.Select(items, i => i.Icon));
        if (signature != _signature)
        {
            _signature = signature;
            foreach (var c in _items.GetChildren()) { _items.RemoveChild(c); c.QueueFree(); }
            _counts.Clear();
            foreach (var (icon, _) in items)
            {
                var ic = new IconCount(icon, 20);
                _counts[icon] = ic;
                _items.AddChild(ic);
            }
        }
        foreach (var (icon, count) in items) _counts[icon].Set(icon, count);
    }
}

/// <summary>The tool in hand in the bottom strip: its icon, its name and what it costs as icons with counts (red for what the hold lacks).</summary>
public partial class ToolReadout : HBoxContainer
{
    private readonly IconRect _icon = new("tool:pick", 22, null, tooltip: false);
    private readonly Label _name = new() { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
    private readonly HBoxContainer _cost = new() { MouseFilter = MouseFilterEnum.Pass };
    private readonly Label _note = new() { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
    private string _signature = "";

    public ToolReadout()
    {
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeConstantOverride("separation", 8);
        _name.AddThemeFontSizeOverride("font_size", UiTheme.Heading);
        _note.AddThemeFontSizeOverride("font_size", UiTheme.Body);
        _cost.AddThemeConstantOverride("separation", 10);
        AddChild(_icon);
        AddChild(_name);
        AddChild(_cost);
        AddChild(_note);
    }

    /// <summary>Shows the tool; <paramref name="cost"/> items are (icon, needed, in the hold); <paramref name="note"/> is a word after the cost ("free", "locked", "per cell").</summary>
    public void Set(string iconId, string name, Color nameColour, IReadOnlyList<(string Icon, int Need, int Have)> cost, string note, Color noteColour)
    {
        _icon.Set(iconId, nameColour);
        _name.Text = name;
        _name.AddThemeColorOverride("font_color", nameColour);
        string signature = string.Join(",", System.Linq.Enumerable.Select(cost, c => c.Icon));
        if (signature != _signature)
        {
            _signature = signature;
            foreach (var c in _cost.GetChildren()) { _cost.RemoveChild(c); c.QueueFree(); }
            foreach (var c in cost) _cost.AddChild(new IconCount(c.Icon, 20));
        }
        for (int i = 0; i < cost.Count; i++) ((IconCount)_cost.GetChild(i)).SetCost(cost[i].Icon, cost[i].Need, cost[i].Have);
        _cost.Visible = cost.Count > 0;
        _note.Text = note;
        _note.Visible = note.Length > 0;
        _note.AddThemeColorOverride("font_color", noteColour);
    }
}
