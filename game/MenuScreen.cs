using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// A full-screen menu built in code: a titled panel with a list of buttons, plus the Settings and Controls pages that the main
/// menu and the pause menu share. Operable with the mouse and with the keyboard (arrows move the focus, Enter presses,
/// left/right change a slider, Esc goes back or asks to close).
/// </summary>
public partial class MenuScreen : Control
{
    public enum Page { Main, Settings, Controls }

    public sealed record Entry(Func<string> Text, Action OnPressed, Func<bool>? Enabled = null, Func<string>? Detail = null);

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.93f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.8f);
    private static readonly Color Accent = new(0.35f, 0.65f, 1f);
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);

    private readonly string _titleKey;
    private readonly bool _dim;
    private readonly List<Entry> _entries = new();
    private PanelContainer _panel = null!;
    private VBoxContainer _body = null!;
    private Label _status = null!;
    private Label _tip = null!;
    private string _statusText = "";
    private string? _focusName;

    public Page Current { get; private set; } = Page.Main;

    /// <summary>An optional line under the title of the main page (the pause menu shows the galaxy seed there).</summary>
    public Func<string>? InfoLine { get; init; }

    /// <summary>Raised when Esc is pressed on the main page (the pause menu uses it to resume).</summary>
    public event Action? CloseRequested;

    public MenuScreen(string titleKey, bool dim)
    {
        _titleKey = titleKey;
        _dim = dim;
    }

    public void AddEntry(Func<string> text, Action onPressed, Func<bool>? enabled = null, Func<string>? detail = null) =>
        _entries.Add(new Entry(text, onPressed, enabled, detail));

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        if (_dim) { var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.62f), MouseFilter = MouseFilterEnum.Ignore }; AddChild(dim); dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, PanelEdge, 8, 22f));
        centre.AddChild(_panel);
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(_body);
        VisibilityChanged += () => { if (Visible) Rebuild(); };
        Rebuild();
    }

    /// <summary>Shows the menu on a page (Main by default) with the first button focused.</summary>
    public void Open(Page page = Page.Main)
    {
        Current = page;
        _focusName = null;
        Visible = true;
        Rebuild();
    }

    public void SetStatus(string text)
    {
        _statusText = text;
        if (_status is not null) _status.Text = text;
    }

    /// <summary>Rebuilds the current page (after a language change, or when it is shown again).</summary>
    public void Rebuild()
    {
        if (_body is null) return;
        foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        switch (Current)
        {
            case Page.Settings: BuildSettings(); break;
            case Page.Controls: BuildControls(); break;
            default: BuildMain(); break;
        }
        _status = UiKit.Text(13, new Color(0.6f, 0.9f, 0.65f));
        _status.Text = _statusText;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(_status);
        CallDeferred(MethodName.GrabInitialFocus);
    }

    private void GrabInitialFocus()
    {
        if (!IsInsideTree() || !Visible) return;
        Control? target = _focusName is not null ? _body.FindChild(_focusName, true, false) as Control : null;
        target ??= FirstFocusable(_body);
        target?.GrabFocus();
    }

    private static Control? FirstFocusable(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control c && c.FocusMode == FocusModeEnum.All && !(c is BaseButton { Disabled: true })) return c;
            if (FirstFocusable(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void Header(string subtitleKey = "")
    {
        var title = UiKit.Text(Current == Page.Main && _titleKey == "menu.title" ? 40 : 28, Colors.White);
        title.Text = Loc.T(Current == Page.Main ? _titleKey : Current == Page.Settings ? "settings.title" : "menu.controls");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(title);
        if (subtitleKey.Length > 0)
        {
            var sub = UiKit.Text(14, Muted);
            sub.Text = Loc.T(subtitleKey);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            _body.AddChild(sub);
        }
        _body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
    }

    private void BuildMain()
    {
        _panel.CustomMinimumSize = new Vector2(420, 0);
        Header(_titleKey == "menu.title" ? "menu.subtitle" : "");
        if (InfoLine?.Invoke() is { Length: > 0 } info)
        {
            var line = UiKit.Text(13, Muted);
            line.Text = info;
            line.HorizontalAlignment = HorizontalAlignment.Center;
            _body.AddChild(line);
        }
        foreach (var e in _entries)
        {
            var b = UiKit.Button(e.Text(), e.OnPressed, 17, 420 - 44);
            b.CustomMinimumSize = new Vector2(0, 40);
            b.Disabled = e.Enabled is not null && !e.Enabled();
            _body.AddChild(b);
            if (e.Detail?.Invoke() is { Length: > 0 } detail)
            {
                var d = UiKit.Text(12, Muted);
                d.Text = detail;
                d.HorizontalAlignment = HorizontalAlignment.Center;
                _body.AddChild(d);
            }
        }
        var hint = UiKit.Text(11, new Color(0.5f, 0.56f, 0.68f));
        hint.Text = Loc.T("menu.hint");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(hint);
    }

    private void BuildControls()
    {
        _panel.CustomMinimumSize = new Vector2(1000, 0);
        Header();
        string text = Loc.T("help.text");
        int firstBlank = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (firstBlank >= 0) text = text[(firstBlank + 2)..]; // the overlay's "(F1 to close)" title makes no sense here
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 440), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = FocusModeEnum.All, Name = "ControlsScroll" };
        var label = UiKit.Text(14);
        label.Text = text;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(label);
        _body.AddChild(scroll);
        _body.AddChild(BackButton());
        _focusName ??= "BackButton";
    }

    private Button BackButton()
    {
        var b = UiKit.Button(Loc.T("menu.back"), () => Open(Page.Main), 16);
        b.Name = "BackButton";
        b.CustomMinimumSize = new Vector2(0, 38);
        return b;
    }

    private void BuildSettings()
    {
        _panel.CustomMinimumSize = new Vector2(560, 0);
        Header();
        var s = SettingsStore.Current;
        _tip = UiKit.Text(12, Muted);
        _tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tip.CustomMinimumSize = new Vector2(0, 34);

        var lang = UiKit.Button(Loc.T("settings.language") + ": " + Loc.T("settings.languageValue"), () =>
        {
            var c = SettingsStore.Current;
            _focusName = "LanguageButton";
            SettingsStore.Set(c with { Language = c.Language == Language.Es ? Language.En : Language.Es });
            CallDeferred(MethodName.Rebuild);
        }, 15);
        lang.Name = "LanguageButton";
        lang.CustomMinimumSize = new Vector2(0, 36);
        WithTip(lang, "settings.tip.language");
        _body.AddChild(lang);

        AddSlider("SensitivitySlider", s.MouseSensitivity, GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, 0.05,
            v => Loc.T("settings.sensitivity", v.ToString("0.00", Loc.Culture)), v => SettingsStore.Current with { MouseSensitivity = (float)v }, "settings.tip.sensitivity");
        AddSlider("FovSlider", s.Fov, GameSettings.MinFov, GameSettings.MaxFov, 1,
            v => Loc.T("settings.fov", (int)Math.Round(v)), v => SettingsStore.Current with { Fov = (float)v }, "settings.tip.fov");
        AddSlider("VolumeSlider", s.MasterVolume * 100, 0, 100, 5,
            v => Loc.T("settings.volume", (int)Math.Round(v)), v => SettingsStore.Current with { MasterVolume = (float)(v / 100.0) }, "settings.tip.volume");
        AddSlider("SfxSlider", s.SfxVolume, 0, 100, 5,
            v => Loc.T("settings.sfxVolume", (int)Math.Round(v)), v => SettingsStore.Current with { SfxVolume = (int)Math.Round(v) }, "settings.tip.sfxVolume");
        AddSlider("AmbienceSlider", s.AmbienceVolume, 0, 100, 5,
            v => Loc.T("settings.ambienceVolume", (int)Math.Round(v)), v => SettingsStore.Current with { AmbienceVolume = (int)Math.Round(v) }, "settings.tip.ambienceVolume");
        AddSlider("UiVolumeSlider", s.UiVolume, 0, 100, 5,
            v => Loc.T("settings.uiVolume", (int)Math.Round(v)), v => SettingsStore.Current with { UiVolume = (int)Math.Round(v) }, "settings.tip.uiVolume");

        AddCheck("InvertCheck", Loc.T("settings.invertY"), s.InvertMouseY, on => SettingsStore.Current with { InvertMouseY = on }, "settings.tip.invertY");
        AddCheck("FullscreenCheck", Loc.T("settings.fullscreen"), s.Fullscreen, on => SettingsStore.Current with { Fullscreen = on }, "settings.tip.fullscreen");
        AddCheck("LowTexturesCheck", Loc.T("settings.lowTextures"), s.LowTextures, on => SettingsStore.Current with { LowTextures = on }, "settings.tip.lowTextures");

        _body.AddChild(_tip);
        var reset = UiKit.Button(Loc.T("settings.reset"), () =>
        {
            _focusName = "ResetButton";
            SettingsStore.Set(GameSettings.Defaults(OS.GetLocale()) with { Language = SettingsStore.Current.Language });
            CallDeferred(MethodName.Rebuild);
        }, 14);
        reset.Name = "ResetButton";
        _body.AddChild(reset);
        _body.AddChild(BackButton());
        var file = UiKit.Text(11, new Color(0.5f, 0.56f, 0.68f));
        file.Text = Loc.T("settings.file");
        file.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(file);
    }

    private void AddSlider(string name, double value, double min, double max, double step, Func<double, string> text, Func<double, GameSettings> apply, string tipKey)
    {
        var label = UiKit.Text(14);
        label.Text = text(value);
        _body.AddChild(label);
        var slider = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = step, Value = value, FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 22) };
        slider.AddThemeStyleboxOverride("slider", UiKit.Box(new Color(0.22f, 0.27f, 0.38f), null, 3, 3f));
        slider.AddThemeStyleboxOverride("grabber_area", UiKit.Box(Accent, null, 3, 3f));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", UiKit.Box(new Color(0.55f, 0.8f, 1f), null, 3, 3f));
        slider.ValueChanged += v =>
        {
            label.Text = text(v);
            SettingsStore.Set(apply(v));
            // A short preview of the bus being set (rate limited by the manager).
            AudioManager.PlayUi(name switch { "SfxSlider" => Sfx.TurretShot, "UiVolumeSlider" => Sfx.ToastPing, "VolumeSlider" => Sfx.UiConfirm, _ => Sfx.UiClick });
        };
        WithTip(slider, tipKey);
        _body.AddChild(slider);
    }

    private void AddCheck(string name, string text, bool value, Func<bool, GameSettings> apply, string tipKey)
    {
        // A toggle button rather than a CheckBox: the default check box is almost invisible on the dark panel.
        string Label(bool on) => (on ? "[x]  " : "[  ]  ") + text;
        var check = UiKit.Button(Label(value), () => { }, 14);
        check.Name = name;
        check.ToggleMode = true;
        check.ButtonPressed = value;
        check.Alignment = HorizontalAlignment.Left;
        check.Toggled += on =>
        {
            check.Text = Label(on);
            SettingsStore.Set(apply(on));
        };
        WithTip(check, tipKey);
        _body.AddChild(check);
    }

    private void WithTip(Control c, string key)
    {
        c.TooltipText = Loc.T(key);
        c.MouseEntered += () => _tip.Text = Loc.T(key);
        c.FocusEntered += () => _tip.Text = Loc.T(key);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        if (Current != Page.Main) Open(Page.Main);
        else CloseRequested?.Invoke();
    }
}

/// <summary>The look of the game interface (same colours and boxes as the HUD) for the menus.</summary>
public static class UiKit
{
    private static readonly Color Edge = new(0.35f, 0.45f, 0.65f, 0.7f);
    private static readonly Color Accent = new(0.35f, 0.65f, 1f);

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, float margin = 8f)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(margin);
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(1); }
        return s;
    }

    public static Label Text(int size = 14, Color? color = null)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeColorOverride("font_color", color ?? Colors.White);
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    public static Button Button(string text, Action onPressed, int fontSize = 13, int minWidth = 0)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = new Vector2(minWidth, 0) };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.1f, 0.13f, 0.2f, 0.95f), Edge, 5, 6f));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.16f, 0.22f, 0.34f, 0.97f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.15f, 0.34f, 0.58f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(0.18f, 0.4f, 0.66f, 0.98f), Accent, 5, 6f));
        var focus = Box(new Color(0f, 0f, 0f, 0f), new Color(1f, 0.85f, 0.4f), 5, 6f);
        focus.SetBorderWidthAll(2);
        b.AddThemeStyleboxOverride("focus", focus);
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.06f, 0.07f, 0.1f, 0.7f), new Color(0.2f, 0.22f, 0.28f), 5, 6f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.52f, 0.58f));
        b.Pressed += () => AudioManager.PlayUi(Sfx.UiClick);
        b.Pressed += onPressed;
        return b;
    }
}
