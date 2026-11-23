using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>A drawn icon: an id from <see cref="IconCatalog"/>, a logical size and a tint (the material's colour when none is given). Hovering it names it.</summary>
public partial class IconRect : Control
{
    private string _id;
    private readonly int _size;
    private Color? _tint;

    public IconRect(string id, int size = 20, Color? tint = null, bool tooltip = true)
    {
        _id = id;
        _size = size;
        _tint = tint;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = tooltip ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
    }

    public string Id => _id;

    /// <summary>Swaps the icon and, when given, the tint (null keeps the material's colour).</summary>
    public void Set(string id, Color? tint = null)
    {
        if (_id == id && _tint == tint) return;
        _id = id;
        _tint = tint;
        QueueRedraw();
    }

    public override string _GetTooltip(Vector2 atPosition) => MouseFilter == MouseFilterEnum.Ignore ? "" : Icons.Name(_id);

    public override void _Draw()
    {
        var tex = Icons.Get(_id, _size);
        var tint = _tint ?? Icons.TintOf(_id);
        DrawTextureRect(tex, new Rect2(Vector2.Zero, new Vector2(_size, _size)), false, tint);
    }
}

/// <summary>An icon with a number beside it ("iron plate 12"): the tooltip names the thing, the colour says whether you have enough.</summary>
public partial class IconCount : HBoxContainer
{
    private readonly IconRect _icon;
    private readonly Label _label;

    public IconCount(string id, int size = 18, int fontSize = UiTheme.Body)
    {
        Id = id;
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeConstantOverride("separation", 3);
        _icon = new IconRect(id, size, null, tooltip: false);
        _label = new Label { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        _label.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(_icon);
        AddChild(_label);
    }

    public string Id { get; private set; }

    /// <summary>Shows <paramref name="have"/> (or <c>have/need</c> when <paramref name="need"/> is given); red when short of it.</summary>
    public void Set(string id, int have, int need = -1, Color? normal = null, string? extra = null)
    {
        Id = id;
        _icon.Set(id);
        bool isShort = need >= 0 && have < need;
        _label.Text = need >= 0 ? $"{have}/{need}" : have.ToString(Loc.Culture);
        _label.AddThemeColorOverride("font_color", isShort ? UiTheme.Bad : normal ?? UiTheme.Text);
        TooltipText = Icons.Name(id) + ": " + _label.Text + (extra is { Length: > 0 } ? "\n" + extra : "");
    }

    /// <summary>Shows the icon with a text instead of a count (a time left, a percentage).</summary>
    /// <summary>Shows a price: the count needed, red when the hold has less; the tooltip says how many you have.</summary>
    public void SetCost(string id, int need, int have)
    {
        Id = id;
        _icon.Set(id);
        _label.Text = need.ToString(Loc.Culture);
        _label.AddThemeColorOverride("font_color", have < need ? UiTheme.Bad : UiTheme.Text);
        TooltipText = Icons.Name(id) + ": " + need + "\n" + Loc.T("research.costHold", "", have).Trim();
    }

    /// <summary>Shows the icon with a text instead of a count (a time left, a percentage).</summary>
    public void SetText(string id, string text, Color? colour = null, string? tooltip = null)
    {
        Id = id;
        _icon.Set(id);
        _label.Text = text;
        _label.AddThemeColorOverride("font_color", colour ?? UiTheme.Text);
        TooltipText = tooltip ?? Icons.Name(id);
    }
}

/// <summary>A flat gauge: a rounded track and a fill, with a mark at the warning level when given. The state colour comes from the caller.</summary>
public partial class GaugeBar : Control
{
    public float Fraction { get; private set; }
    public Color Fill { get; private set; } = UiTheme.Good;
    private float _mark = -1f;

    public GaugeBar(float width = 96f, float height = 8f)
    {
        CustomMinimumSize = new Vector2(width, height);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(float fraction, Color fill, float mark = -1f)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (Math.Abs(fraction - Fraction) < 0.0005f && fill == Fill && mark == _mark) return;
        Fraction = fraction;
        Fill = fill;
        _mark = mark;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        int rad = (int)(r.Size.Y / 2f);
        DrawStyleBox(UiTheme.Box(UiTheme.Track, new Color(1f, 1f, 1f, 0.06f), rad, 0f), r);
        if (Fraction > 0.001f)
        {
            var f = new Rect2(0, 0, Math.Max(r.Size.Y, r.Size.X * Fraction), r.Size.Y);
            DrawStyleBox(UiTheme.Box(Fill, null, rad, 0f), f);
        }
        if (_mark > 0f && _mark < 1f) DrawLine(new Vector2(r.Size.X * _mark, -1), new Vector2(r.Size.X * _mark, r.Size.Y + 1), new Color(1f, 1f, 1f, 0.55f), 1f);
    }
}

/// <summary>An icon, a gauge and a short value ("100/100"): one line of a status card. The tooltip carries the full sentence.</summary>
public partial class GaugeRow : HBoxContainer
{
    private readonly IconRect _icon;
    private readonly GaugeBar _bar;
    private readonly Label _value;

    public GaugeRow(string iconId, float barWidth = 96f, int valueWidth = 64)
    {
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeConstantOverride("separation", 6);
        _icon = new IconRect(iconId, 18, null, tooltip: false);
        _bar = new GaugeBar(barWidth);
        _value = new Label { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(valueWidth, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _value.AddThemeFontSizeOverride("font_size", UiTheme.Body);
        AddChild(_icon);
        AddChild(_bar);
        AddChild(_value);
    }

    public void Set(string iconId, float fraction, string value, Color colour, string tooltip, float mark = -1f)
    {
        _icon.Set(iconId, colour);
        _bar.Set(fraction, colour, mark);
        _value.Text = value;
        _value.AddThemeColorOverride("font_color", colour);
        TooltipText = tooltip;
    }
}
