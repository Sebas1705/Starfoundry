using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The one look of the interface: colour tokens, type sizes, spacing and the style boxes every screen shares (HUD cards, menu panels, buttons,
/// tooltips, sliders, bars). <see cref="Create"/> returns a <see cref="Theme"/> to set on a screen's root so plain controls match; the helpers
/// build the pieces a theme cannot (a card header, a thin accent line).
/// </summary>
public static class UiTheme
{
    // ---- colour tokens ----
    public static readonly Color Ink = new(0.04f, 0.06f, 0.10f);
    public static readonly Color Glass = new(0.04f, 0.06f, 0.10f, 0.80f);
    public static readonly Color Solid = new(0.05f, 0.07f, 0.11f, 0.95f);
    public static readonly Color Edge = new(0.45f, 0.58f, 0.82f, 0.38f);
    public static readonly Color EdgeStrong = new(0.35f, 0.45f, 0.65f, 0.85f);
    public static readonly Color Accent = new(0.35f, 0.65f, 1f);
    public static readonly Color Gold = new(1f, 0.88f, 0.5f);
    public static readonly Color Text = new(0.94f, 0.96f, 1f);
    public static readonly Color Muted = new(0.62f, 0.7f, 0.84f);
    public static readonly Color Dim = new(0.5f, 0.53f, 0.6f);
    public static readonly Color Good = new(0.45f, 0.85f, 0.5f);
    public static readonly Color Warn = new(0.95f, 0.75f, 0.3f);
    public static readonly Color Bad = new(1f, 0.45f, 0.4f);
    public static readonly Color Track = new(0.12f, 0.14f, 0.2f);

    // ---- type scale (logical pixels; the canvas stretches the whole interface with the window) ----
    public const int Caption = 11, Small = 12, Body = 14, Heading = 16, Title = 22;

    // ---- spacing ----
    public const int Gap = 6, Pad = 10;

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, float margin = 8f, int borderWidth = 1)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(margin);
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(borderWidth); }
        s.AntiAliasing = true;
        return s;
    }

    /// <summary>The HUD card: soft dark glass, a thin cool edge, rounded corners and a faint shadow.</summary>
    public static StyleBoxFlat Card(Color? edge = null, float margin = 10f)
    {
        var s = Box(Glass, edge ?? Edge, 8, margin);
        s.ContentMarginLeft = s.ContentMarginRight = margin + 3f;
        s.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        s.ShadowSize = 8;
        return s;
    }

    /// <summary>A docked menu panel (build, research, pause): near opaque so the game does not show through the text.</summary>
    public static StyleBoxFlat Dock(float margin = 12f) => Box(Solid, EdgeStrong, 8, margin);

    /// <summary>The surface of a tile: normal, selected (gold edge) or locked (dim).</summary>
    public static StyleBoxFlat Tile(bool selected = false, bool locked = false, bool hover = false) =>
        Box(selected ? new Color(0.14f, 0.22f, 0.36f, 0.97f) : hover ? new Color(0.16f, 0.22f, 0.34f, 0.97f) : locked ? new Color(0.07f, 0.08f, 0.11f, 0.9f) : new Color(0.1f, 0.13f, 0.2f, 0.95f),
            selected ? Gold : hover ? Accent : locked ? new Color(0.22f, 0.25f, 0.32f) : new Color(0.35f, 0.45f, 0.65f, 0.7f), 6, 6f, selected ? 2 : 1);

    public static StyleBoxFlat FocusRing()
    {
        var f = Box(new Color(0f, 0f, 0f, 0f), Gold, 6, 6f, 2);
        f.DrawCenter = false;
        return f;
    }

    public static void StyleButton(Button b)
    {
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.1f, 0.13f, 0.2f, 0.95f), new Color(0.35f, 0.45f, 0.65f, 0.7f), 5, 6f));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.16f, 0.22f, 0.34f, 0.97f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.15f, 0.34f, 0.58f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(0.18f, 0.4f, 0.66f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("focus", FocusRing());
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.06f, 0.07f, 0.1f, 0.7f), new Color(0.2f, 0.22f, 0.28f), 5, 6f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.52f, 0.58f));
    }

    /// <summary>A theme for a whole screen: buttons, labels, tooltips, sliders and bars in the shared look.</summary>
    public static Theme Create()
    {
        var t = new Theme { DefaultFontSize = Body };
        foreach (var type in new[] { "Button", "OptionButton", "CheckButton", "CheckBox", "MenuButton" })
        {
            t.SetStylebox("normal", type, Box(new Color(0.1f, 0.13f, 0.2f, 0.95f), new Color(0.35f, 0.45f, 0.65f, 0.7f), 5, 6f));
            t.SetStylebox("hover", type, Box(new Color(0.16f, 0.22f, 0.34f, 0.97f), Accent, 5, 6f));
            t.SetStylebox("pressed", type, Box(new Color(0.15f, 0.34f, 0.58f, 0.98f), Accent, 5, 6f));
            t.SetStylebox("hover_pressed", type, Box(new Color(0.18f, 0.4f, 0.66f, 0.98f), Accent, 5, 6f));
            t.SetStylebox("focus", type, FocusRing());
            t.SetStylebox("disabled", type, Box(new Color(0.06f, 0.07f, 0.1f, 0.7f), new Color(0.2f, 0.22f, 0.28f), 5, 6f));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, new Color(0.5f, 0.52f, 0.58f));
        }
        t.SetColor("font_color", "Label", Text);
        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.03f, 0.04f, 0.07f, 0.97f), Accent, 5, 7f));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", Small + 1);
        t.SetStylebox("background", "ProgressBar", Box(Track, null, 3, 0f));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, null, 3, 0f));
        t.SetStylebox("slider", "HSlider", Box(Track, null, 3, 0f));
        t.SetStylebox("grabber_area", "HSlider", Box(Accent, null, 3, 0f));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Accent.Lightened(0.15f), null, 3, 0f));
        return t;
    }

    /// <summary>A thin horizontal line in the accent colour (under a card title).</summary>
    public static ColorRect Rule(Color? colour = null, float alpha = 0.55f) => new()
    {
        Color = (colour ?? Accent) with { A = alpha },
        CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>The head of a card: a small icon, the title in small caps and a thin accent line under both.</summary>
    public static Control Header(string iconId, Label title, Color? colour = null)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 3);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new IconRect(iconId, 14, colour ?? Muted, tooltip: false));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(title);
        box.AddChild(row);
        box.AddChild(Rule(colour));
        return box;
    }
}
