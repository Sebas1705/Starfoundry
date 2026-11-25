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

/// <summary>
/// An icon with a number beside it ("iron plate 12"): the tooltip names the thing, the colour says whether you have enough. Every setter only touches
/// the controls when something changed (a theme override makes a label measure itself again), and <see cref="Animate"/> makes the number count up or
/// down instead of jumping.
/// </summary>
public partial class IconCount : HBoxContainer
{
    private readonly IconRect _icon;
    private readonly Label _label;
    private Color? _colour;
    private string _tip = "";
    private bool _limited;
    private CounterTween? _count;
    private int _countNeed = -1;
    private Color? _countNormal;
    private string? _countExtra;
    private string _countIcon = "";

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
        SetProcess(false);
    }

    public string Id { get; private set; }

    /// <summary>The number counts toward its value (about a quarter of a second) instead of jumping. Only <see cref="Set"/> animates.</summary>
    public bool Animate { get; set; }

    /// <summary>Keeps the text to at most <paramref name="pixels"/> wide: longer text is cut with an ellipsis and the full text is the tooltip.</summary>
    public void LimitWidth(float pixels)
    {
        _limited = true;
        _label.ClipText = true;
        _label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _label.CustomMinimumSize = new Vector2(pixels, 0);
    }

    private void Apply(string text, Color colour, string tooltip)
    {
        if (_label.Text != text)
        {
            _label.Text = text;
            if (!_limited) // reserve the measured width (plus a pixel of slack) so a wider face never clips the last digit
                _label.CustomMinimumSize = new Vector2(Mathf.Ceil(_label.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, _label.GetThemeFontSize("font_size")).X) + 2f, 0);
        }
        if (_colour != colour) { _colour = colour; _label.AddThemeColorOverride("font_color", colour); }
        if (_tip != tooltip) { _tip = tooltip; TooltipText = tooltip; }
    }

    /// <summary>Shows <paramref name="have"/> (or <c>have/need</c> when <paramref name="need"/> is given); red when short of it.</summary>
    public void Set(string id, int have, int need = -1, Color? normal = null, string? extra = null)
    {
        Id = id;
        _icon.Set(id);
        _countIcon = id; _countNeed = need; _countNormal = normal; _countExtra = extra;
        if (Animate && need < 0)
        {
            _count ??= new CounterTween();
            _count.Set(have, UiFx.Instant);
            SetProcess(!UiFx.Instant && _count.Value != have);
        }
        else SetProcess(false);
        ShowCount(have);
    }

    private void ShowCount(int have)
    {
        bool animated = Animate && _count is not null && _countNeed < 0;
        int shown = animated ? _count!.Value : have;
        bool isShort = _countNeed >= 0 && have < _countNeed;
        string text = _countNeed >= 0 ? have + "/" + _countNeed : shown.ToString(Loc.Culture);
        string full = _countNeed >= 0 ? text : have.ToString(Loc.Culture);
        Apply(text, isShort ? UiTheme.Bad : _countNormal ?? UiTheme.Text,
            Icons.Name(_countIcon) + ": " + full + (_countExtra is { Length: > 0 } ? "\n" + _countExtra : ""));
    }

    public override void _Process(double delta)
    {
        if (_count is null) { SetProcess(false); return; }
        if (!_count.Step((float)delta)) SetProcess(false);
        ShowCount(_count.Target);
    }

    /// <summary>Shows a price: the count needed, red when the hold has less; the tooltip says how many you have.</summary>
    public void SetCost(string id, int need, int have)
    {
        Id = id;
        _icon.Set(id);
        SetProcess(false);
        Apply(need.ToString(Loc.Culture), have < need ? UiTheme.Bad : UiTheme.Text,
            Icons.Name(id) + ": " + need + "\n" + Loc.T("research.costHold", "", have).Trim());
    }

    /// <summary>Shows the icon with a text instead of a count (a time left, a percentage).</summary>
    public void SetText(string id, string text, Color? colour = null, string? tooltip = null)
    {
        Id = id;
        _icon.Set(id);
        SetProcess(false);
        Apply(text, colour ?? UiTheme.Text, tooltip ?? (_limited ? Icons.Name(id) + ": " + text : Icons.Name(id)));
    }
}

/// <summary>
/// A flat gauge: a rounded track and a fill, with a mark at the warning level when given. The state colour comes from the caller. The fill eases toward
/// a new value (a short transition) instead of jumping; <see cref="Fraction"/> is always the value it is heading for.
/// </summary>
public partial class GaugeBar : Control
{
    public float Fraction { get; private set; }
    public Color Fill { get; private set; } = UiTheme.Good;
    private float _mark = -1f;
    private readonly ValueTween _shown = new();

    public GaugeBar(float width = 96f, float height = 8f)
    {
        CustomMinimumSize = new Vector2(width, height);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
    }

    public void Set(float fraction, Color fill, float mark = -1f)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (Math.Abs(fraction - Fraction) < 0.0005f && fill == Fill && mark == _mark) return;
        Fraction = fraction;
        Fill = fill;
        _mark = mark;
        _shown.Set(fraction, UiFx.Instant);
        SetProcess(!_shown.Settled);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _shown.Step((float)delta, 14f);
        QueueRedraw();
        if (_shown.Settled) SetProcess(false);
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        int rad = (int)(r.Size.Y / 2f);
        DrawStyleBox(UiTheme.Box(UiTheme.Track, new Color(1f, 1f, 1f, 0.06f), rad, 0f), r);
        if (_shown.Current > 0.001f)
        {
            var f = new Rect2(0, 0, Math.Max(r.Size.Y, r.Size.X * _shown.Current), r.Size.Y);
            DrawStyleBox(UiTheme.Box(Fill, null, rad, 0f), f);
        }
        if (_mark > 0f && _mark < 1f) DrawLine(new Vector2(r.Size.X * _mark, -1), new Vector2(r.Size.X * _mark, r.Size.Y + 1), new Color(1f, 1f, 1f, 0.55f), 1f);
    }
}

/// <summary>An icon, a gauge and a short value ("100/100"): one line of a status card. The tooltip carries the full sentence. Setters touch the controls only on a change.</summary>
public partial class GaugeRow : HBoxContainer
{
    private readonly IconRect _icon;
    private readonly GaugeBar _bar;
    private readonly Label _value;
    private Color? _colour;

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
        if (_value.Text != value) _value.Text = value;
        if (_colour != colour) { _colour = colour; _value.AddThemeColorOverride("font_color", colour); }
        if (TooltipText != tooltip) TooltipText = tooltip;
    }
}
