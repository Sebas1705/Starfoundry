using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Shared text fitting: instead of hoping a long Spanish name fits, every tile, card and row asks for the largest font size (between a maximum
/// and a minimum) at which its text fits the width it is given in at most N lines, wraps at word boundaries, and falls back to an ellipsis
/// (with the full text as the tooltip) only when even the minimum size does not fit. <see cref="Fit"/> is the pure measurement, <see cref="FitLabel"/>
/// is the label that uses it, <see cref="ShrinkButton"/> does the same for a single-line button.
/// </summary>
public static class UiFit
{
    /// <summary>The theme constant Godot adds between the lines of a label.</summary>
    public const int LineSpacing = 3;

    /// <summary>What fitting chose: the font size, the number of lines it needs, whether the text is still cut and the height those lines take.</summary>
    public readonly record struct Result(int FontSize, int Lines, bool Truncated, float Height);

    public static Font DefaultFont => ThemeDB.FallbackFont;

    /// <summary>Width of the longest word at a size (a word wider than the box would be broken in the middle).</summary>
    private static float LongestWord(Font font, string text, int size) =>
        text.Split(new[] { ' ', '\n', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(w => font.GetStringSize(w, HorizontalAlignment.Left, -1, size).X).DefaultIfEmpty(0f).Max();

    /// <summary>Number of lines the text takes when wrapped to <paramref name="width"/> at <paramref name="size"/>.</summary>
    public static int LineCount(Font font, string text, float width, int size)
    {
        if (text.Length == 0) return 0;
        var m = font.GetMultilineStringSize(text, HorizontalAlignment.Left, width, size);
        return Math.Max(1, (int)Math.Round(m.Y / font.GetHeight(size)));
    }

    /// <summary>The largest size from <paramref name="maxSize"/> down to <paramref name="minSize"/> at which the text wraps to at most <paramref name="maxLines"/> lines of <paramref name="width"/>.</summary>
    public static Result Fit(Font font, string text, float width, int maxLines, int maxSize, int minSize)
    {
        if (text.Length == 0 || width <= 1f) return new Result(maxSize, 0, false, 0f);
        for (int size = maxSize; size >= minSize; size--)
        {
            if (LongestWord(font, text, size) > width + 0.5f) continue;
            int lines = LineCount(font, text, width, size);
            if (lines <= maxLines) return new Result(size, lines, false, Height(font, size, lines));
        }
        int shown = Math.Min(maxLines, Math.Max(1, LineCount(font, text, width, minSize)));
        return new Result(minSize, shown, true, Height(font, minSize, shown));
    }

    public static float Height(Font font, int size, int lines) => lines * font.GetHeight(size) + Math.Max(0, lines - 1) * LineSpacing;

    /// <summary>
    /// A single-line button (a tab, an action): the font shrinks until the text and the icon fit the width; below the minimum the text is
    /// clipped with an ellipsis and the full text is the tooltip. <paramref name="iconWidth"/> is what the icon and its gap take.
    /// </summary>
    public static bool ShrinkButton(Button b, string text, float width, int maxSize, int minSize, float iconWidth = 0f)
    {
        var font = b.GetThemeFont("font") ?? DefaultFont;
        var box = b.GetThemeStylebox("normal");
        float room = width - iconWidth - (box?.GetMinimumSize().X ?? 10f) - 2f;
        var r = Fit(font, text, room, 1, maxSize, minSize);
        b.AddThemeFontSizeOverride("font_size", r.FontSize);
        b.Text = text;
        b.ClipText = true;
        b.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        if (r.Truncated || b.TooltipText.Length == 0) b.TooltipText = text;
        return r.Truncated;
    }
}

/// <summary>
/// A label that fits its text: wraps at words to at most <see cref="MaxLines"/> lines, shrinks the font from <see cref="MaxFont"/> to <see cref="MinFont"/>
/// to make it fit the width (<see cref="FitWidth"/>, or its own width when that is 0) and, when even the minimum does not fit, ends the last line with an
/// ellipsis and sets <see cref="Truncated"/>. Its minimum height is the height of the lines it needs, so a tile built around it is sized by its content.
/// </summary>
public partial class FitLabel : Label
{
    public int MaxLines = 2, MaxFont = UiTheme.Small, MinFont = UiTheme.Caption - 1;
    /// <summary>Width the text may use; 0 means the label's own width (it then refits when the container resizes it).</summary>
    public float FitWidth;
    public bool Truncated { get; private set; }
    public int Lines { get; private set; }
    private string _fitText = "";
    private float _fitW = -1f;

    public FitLabel(int maxLines = 2, int maxFont = UiTheme.Small, int minFont = UiTheme.Caption - 1)
    {
        MaxLines = maxLines;
        MaxFont = maxFont;
        MinFont = minFont;
        MouseFilter = MouseFilterEnum.Ignore;
        AutowrapMode = TextServer.AutowrapMode.WordSmart;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ClipText = true;
    }

    /// <summary>Sets the text and fits it (no work when nothing changed).</summary>
    public void SetFit(string text)
    {
        if (text == _fitText && Text == text && _fitW == CurrentWidth()) return;
        _fitText = text;
        Text = text;
        Refit();
    }

    private float CurrentWidth() => FitWidth > 0f ? FitWidth : Size.X;

    private void Refit()
    {
        float w = CurrentWidth();
        _fitW = w;
        if (w <= 1f) return;
        var r = UiFit.Fit(GetThemeFont("font") ?? UiFit.DefaultFont, _fitText, w, MaxLines, MaxFont, MinFont);
        Truncated = r.Truncated;
        Lines = r.Lines;
        AddThemeFontSizeOverride("font_size", r.FontSize);
        MaxLinesVisible = MaxLines;
        float min = r.Height > 0 ? r.Height : 0f;
        CustomMinimumSize = new Vector2(FitWidth > 0f ? FitWidth : 0f, min);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && FitWidth <= 0f && Math.Abs(Size.X - _fitW) > 0.5f && _fitText.Length > 0) CallDeferred(nameof(Refit));
    }
}

/// <summary>
/// A scroll container with a visible "there is more" cue: a soft fade and a small chevron at the top and the bottom edge while content is scrolled
/// out of view (the thin scroll bar alone is easy to miss). Wraps any content; the scroll bar itself is styled by <see cref="UiTheme.Create"/>.
/// </summary>
public partial class ScrollWithHint : Control
{
    public ScrollContainer Scroll { get; } = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
    private readonly Edge _top = new(true), _bottom = new(false);

    public ScrollWithHint(Control content)
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        Scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Scroll.AddChild(content);
        AddChild(Scroll);
        AddChild(_top);
        AddChild(_bottom);
        Scroll.GetVScrollBar().ValueChanged += _ => UpdateHints();
        Scroll.GetVScrollBar().Resized += UpdateHints;
        Resized += UpdateHints;
        Scroll.GetVScrollBar().Changed += UpdateHints;
    }

    public void UpdateHints()
    {
        var bar = Scroll.GetVScrollBar();
        bool scrollable = bar.MaxValue - bar.Page > 1.0;
        _top.Visible = scrollable && bar.Value > 1.0;
        _bottom.Visible = scrollable && bar.Value + bar.Page < bar.MaxValue - 1.0;
        float h = 26f;
        _top.Position = Vector2.Zero; _top.Size = new Vector2(Size.X, h);
        _bottom.Position = new Vector2(0, Size.Y - h); _bottom.Size = new Vector2(Size.X, h);
    }

    private partial class Edge : Control
    {
        private readonly bool _isTop;
        public Edge(bool top) { _isTop = top; MouseFilter = MouseFilterEnum.Ignore; Visible = false; }

        public override void _Draw()
        {
            var solid = UiTheme.Solid;
            var clear = solid with { A = 0f };
            float w = Size.X, h = Size.Y;
            var pts = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
            DrawPolygon(pts, _isTop ? new[] { solid, solid, clear, clear } : new[] { clear, clear, solid, solid });
            float cx = w / 2f, cy = _isTop ? 8f : h - 8f, d = _isTop ? -1f : 1f;
            DrawPolyline(new[] { new Vector2(cx - 6, cy - 2.5f * d), new Vector2(cx, cy + 2.5f * d), new Vector2(cx + 6, cy - 2.5f * d) }, UiTheme.Gold, 2f, true);
        }

        public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
    }
}

/// <summary>
/// The price of a tile as a row of small icon-and-number pairs. It shows as many as fit the width, ends with "+n" when some do not (the detail pane
/// lists them all), and colours a number red when the hold cannot pay it.
/// </summary>
public partial class CostStrip : Control
{
    private const int IconSize = 12, Font = UiTheme.Caption, Gap = 6;
    private (string Icon, int Need, int Have)[] _items = Array.Empty<(string, int, int)>();
    private string _free = "";

    public CostStrip(float width, float height = 14f)
    {
        CustomMinimumSize = new Vector2(width, height);
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public void Set(IReadOnlyList<(string Icon, int Need, int Have)>? items, string free)
    {
        var arr = items?.ToArray() ?? Array.Empty<(string, int, int)>();
        if (arr.SequenceEqual(_items) && free == _free) return;
        _items = arr;
        _free = free;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = UiFit.DefaultFont;
        float y = Size.Y / 2f + font.GetAscent(Font) / 2f - font.GetDescent(Font) / 2f;
        if (_items.Length == 0)
        {
            if (_free.Length > 0) DrawString(font, new Vector2(0, y), _free, HorizontalAlignment.Center, Size.X, Font, UiTheme.Dim);
            return;
        }
        float PairWidth(int i) => IconSize + 2 + font.GetStringSize(_items[i].Need.ToString(Starfoundry.Ui.Loc.Culture), HorizontalAlignment.Left, -1, Font).X;
        float more = font.GetStringSize("+9", HorizontalAlignment.Left, -1, Font).X;
        int shown = 0;
        float total = 0f;
        for (int i = 0; i < _items.Length; i++)
        {
            float w = PairWidth(i) + (i > 0 ? Gap : 0f);
            bool last = i == _items.Length - 1;
            if (total + w + (last ? 0f : Gap + more) > Size.X + 0.5f) break;
            total += w;
            shown++;
        }
        shown = Math.Max(shown, 1);
        float x = Math.Max(0f, (Size.X - total) / 2f);
        for (int i = 0; i < shown; i++)
        {
            var (icon, need, have) = _items[i];
            DrawTextureRect(Icons.Get(icon, IconSize), new Rect2(x, (Size.Y - IconSize) / 2f, IconSize, IconSize), false, Icons.TintOf(icon));
            x += IconSize + 2;
            string t = need.ToString(Starfoundry.Ui.Loc.Culture);
            DrawString(font, new Vector2(x, y), t, HorizontalAlignment.Left, -1, Font, have < need ? UiTheme.Bad : UiTheme.Text);
            x += font.GetStringSize(t, HorizontalAlignment.Left, -1, Font).X + Gap;
        }
        if (shown < _items.Length) DrawString(font, new Vector2(x, y), "+" + (_items.Length - shown), HorizontalAlignment.Left, -1, Font, UiTheme.Muted);
    }
}
