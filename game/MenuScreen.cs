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
    public enum Page { Main, Settings, Controls, Credits }

    public sealed record Entry(Func<string> Text, Action OnPressed, Func<bool>? Enabled = null, Func<string>? Detail = null);

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.93f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.8f);
    private static readonly Color Accent = new(0.35f, 0.65f, 1f);
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);

    private readonly string _titleKey;
    private readonly bool _dim;
    private readonly List<Entry> _entries = new();
    private PanelContainer _panel = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _body = null!;
    private Label _status = null!;
    private Label _tip = null!;
    private string _statusText = "";
    private string? _focusName;

    public Page Current { get; private set; } = Page.Main;

    /// <summary>Embedded in another panel (the Settings tab of the ship computer): no dim, no title, no back button, sized by its owner.</summary>
    public bool Embedded { get; init; }

    /// <summary>An optional line under the title of the main page (the pause menu shows the galaxy seed there).</summary>
    public Func<string>? InfoLine { get; init; }

    /// <summary>Raised when Esc is pressed on the main page (the pause menu uses it to resume).</summary>
    public event Action? CloseRequested;

    public MenuScreen(string titleKey, bool dim)
    {
        _titleKey = titleKey;
        UiFx.Present(this); // fades and grows in instead of popping
        _dim = dim;
    }

    /// <summary>Embedded: puts the page in a rectangle of its owner (the panel fills it).</summary>
    public void Place(Rect2 rect)
    {
        Position = rect.Position;
        Size = rect.Size;
        if (_panel is not null) { _panel.Position = Vector2.Zero; _panel.Size = rect.Size; }
        if (Current == Page.Controls) CallDeferred(MethodName.FitKeysPage);
    }

    public void AddEntry(Func<string> text, Action onPressed, Func<bool>? enabled = null, Func<string>? detail = null) =>
        _entries.Add(new Entry(text, onPressed, enabled, detail));

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        if (!Embedded) SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        if (_dim && !Embedded) { var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.62f), MouseFilter = MouseFilterEnum.Ignore }; AddChild(dim); dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, PanelEdge, 8, Embedded ? 14f : 22f));
        if (Embedded) { AddChild(_panel); _panel.ClipContents = true; }
        else
        {
            var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(centre);
            centre.AddChild(_panel);
        }
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        // A page taller than the window (Settings on a small screen) scrolls instead of running off it.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        if (Embedded) _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll.AddChild(_body);
        _panel.AddChild(_scroll);
        VisibilityChanged += () => { if (Visible) Rebuild(); };
        Rebuild();
    }

    /// <summary>Shows the menu on a page (Main by default) with the first button focused.</summary>
    public void Open(Page page = Page.Main)
    {
        if (page == Page.Controls && Current != Page.Controls) _controlsFrom = Current;
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
        _keysPage = null;
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        switch (Current)
        {
            case Page.Settings: BuildSettings(); break;
            case Page.Controls: BuildControls(); break;
            case Page.Credits: BuildCredits(); break;
            default: BuildMain(); break;
        }
        _status = UiKit.Text(13, new Color(0.6f, 0.9f, 0.65f));
        _status.Text = _statusText;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(_status);
        CallDeferred(MethodName.FitKeysPage);
        CallDeferred(MethodName.FitScroll);
        CallDeferred(MethodName.GrabInitialFocus);
    }

    /// <summary>Limits the page to the window height (less the panel margins) so a long page scrolls.</summary>
    private void FitScroll()
    {
        if (!IsInsideTree() || _scroll is null || Embedded) return;
        float room = GetViewportRect().Size.Y - 2f * 22f - 24f;
        _scroll.CustomMinimumSize = new Vector2(_scroll.CustomMinimumSize.X, Math.Min(_body.GetCombinedMinimumSize().Y, Math.Max(200f, room)));
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
        if (Embedded) return;
        var title = UiKit.Text(Current == Page.Main && _titleKey == "menu.title" ? 40 : 28, Colors.White);
        title.Text = Loc.T(Current == Page.Main ? _titleKey : Current == Page.Settings ? "settings.title" : Current == Page.Credits ? "menu.credits" : "menu.controls");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(title);
        _body.AddChild(UiTheme.Rule(UiTheme.Accent, 0.4f));
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

    private KeyBindingsPage? _keysPage;
    private Page _controlsFrom = Page.Main;

    private void BuildControls()
    {
        if (!Embedded) _panel.CustomMinimumSize = new Vector2(900, 0);
        Header();
        _keysPage = new KeyBindingsPage { Name = "KeyBindingsPage" };
        _body.AddChild(_keysPage);
        var back = UiKit.Button(Loc.T("controls.back"), () => Open(_controlsFrom), 16);
        back.Name = "BackButton";
        back.CustomMinimumSize = new Vector2(0, 38);
        _body.AddChild(back);
        _focusName ??= "BackButton";
    }

    /// <summary>Gives the key page the height the window leaves, so its list scrolls inside the page instead of the whole menu scrolling.</summary>
    private void FitKeysPage()
    {
        if (_keysPage is null || !IsInstanceValid(_keysPage) || !IsInsideTree() || Current != Page.Controls) return;
        float avail = Embedded ? Size.Y - 2f * 14f : GetViewportRect().Size.Y - 2f * 22f - 24f;
        _keysPage.CustomMinimumSize = Vector2.Zero;
        float other = _body.GetCombinedMinimumSize().Y - _keysPage.GetCombinedMinimumSize().Y;
        _keysPage.CustomMinimumSize = new Vector2(0, Math.Max(_keysPage.GetCombinedMinimumSize().Y, avail - other - 4f));
    }

    private Button BackButton()
    {
        var b = UiKit.Button(Loc.T("menu.back"), () => Open(BackTarget), 16);
        b.Name = "BackButton";
        b.CustomMinimumSize = new Vector2(0, 38);
        return b;
    }

    private void BuildSettings()
    {
        if (!Embedded) _panel.CustomMinimumSize = new Vector2(560, 0);
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

        var detailButton = UiKit.Button(Loc.T("settings.hudDetail", Loc.T("hud.detail." + HudDetails.Id(s.HudDetail))), () =>
        {
            var c = SettingsStore.Current;
            _focusName = "HudDetailButton";
            SettingsStore.Set(c with { HudDetail = HudDetails.Next(c.HudDetail) });
            CallDeferred(MethodName.Rebuild);
        }, 14);
        detailButton.Name = "HudDetailButton";
        detailButton.Alignment = HorizontalAlignment.Left;
        WithTip(detailButton, "settings.tip.hudDetail");
        _body.AddChild(detailButton);
        var hudModeButton = UiKit.Button(Loc.T("settings.hudMode", Loc.T("cp.hud." + HudModes.Id(s.HudMode))), () =>
        {
            var c = SettingsStore.Current;
            _focusName = "HudModeButton";
            SettingsStore.Set(c with { HudMode = HudModes.Next(c.HudMode) });
            CallDeferred(MethodName.Rebuild);
        }, 14);
        hudModeButton.Name = "HudModeButton";
        hudModeButton.Alignment = HorizontalAlignment.Left;
        WithTip(hudModeButton, "settings.tip.hudMode");
        _body.AddChild(hudModeButton);
        AddCheck("UiClickCheck", Loc.T("settings.uiClick"), s.UiClickSound, on => SettingsStore.Current with { UiClickSound = on }, "settings.tip.uiClick");
        var soundSet = UiKit.Button(Loc.T("settings.soundSet", Loc.T("settings.soundSet." + (s.RecordedSounds ? "recorded" : "synthetic"))), () =>
        {
            var c = SettingsStore.Current;
            _focusName = "SoundSetButton";
            SettingsStore.Set(c with { RecordedSounds = !c.RecordedSounds });
            CallDeferred(MethodName.Rebuild);
        }, 14);
        soundSet.Name = "SoundSetButton";
        soundSet.Alignment = HorizontalAlignment.Left;
        WithTip(soundSet, "settings.tip.soundSet");
        _body.AddChild(soundSet);
        AddCheck("UiHoverCheck", Loc.T("settings.uiHover"), s.UiHoverSound, on => SettingsStore.Current with { UiHoverSound = on }, "settings.tip.uiHover");

        AddCheck("InvertCheck", Loc.T("settings.invertY"), s.InvertMouseY, on => SettingsStore.Current with { InvertMouseY = on }, "settings.tip.invertY");
        var inertia = UiKit.Button(Loc.T("settings.inertia") + ": " + Loc.T("settings.inertia." + ShipInertias.Id(s.ShipInertia)), () =>
        {
            var c = SettingsStore.Current;
            _focusName = "InertiaButton";
            SettingsStore.Set(c with { ShipInertia = ShipInertias.Next(c.ShipInertia) });
            CallDeferred(MethodName.Rebuild);
        }, 14);
        inertia.Name = "InertiaButton";
        inertia.CustomMinimumSize = new Vector2(0, 34);
        WithTip(inertia, "settings.tip.inertia");
        _body.AddChild(inertia);
        AddCheck("FlightAssistCheck", Loc.T("settings.flightAssist"), s.FlightAssist, on => SettingsStore.Current with { FlightAssist = on }, "settings.tip.flightAssist");
        AddCheck("AimAssistCheck", Loc.T("settings.aimAssist"), s.AimAssist, on => SettingsStore.Current with { AimAssist = on }, "settings.tip.aimAssist");
        AddCheck("SmoothLookCheck", Loc.T("settings.smoothLook"), s.SmoothLook, on => SettingsStore.Current with { SmoothLook = on }, "settings.tip.smoothLook");
        AddCheck("FullscreenCheck", Loc.T("settings.fullscreen"), s.Fullscreen, on => SettingsStore.Current with { Fullscreen = on }, "settings.tip.fullscreen");
        AddCheck("LowTexturesCheck", Loc.T("settings.lowTextures"), s.LowTextures, on => SettingsStore.Current with { LowTextures = on }, "settings.tip.lowTextures");

        _body.AddChild(_tip);
        var keys = UiKit.Button(Loc.T("settings.keys"), () => Open(Page.Controls), 14);
        keys.Name = "KeysButton";
        keys.TooltipText = Loc.T("settings.tip.keys");
        _body.AddChild(keys);
        var reset = UiKit.Button(Loc.T("settings.reset"), () =>
        {
            _focusName = "ResetButton";
            SettingsStore.Set(GameSettings.Defaults(OS.GetLocale()) with { Language = SettingsStore.Current.Language, Bindings = SettingsStore.Current.Bindings }); // the keys have their own reset
            CallDeferred(MethodName.Rebuild);
        }, 14);
        reset.Name = "ResetButton";
        _body.AddChild(reset);
        var credits = UiKit.Button(Loc.T("menu.credits"), OpenCredits, 14);
        credits.Name = "CreditsButton";
        _body.AddChild(credits);
        if (!Embedded) _body.AddChild(BackButton());
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
        if (Embedded || !Visible || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        if (Current != Page.Main) Open(BackTarget);
        else CloseRequested?.Invoke();
    }
}

/// <summary>The look of the game interface (same colours and boxes as the HUD) for the menus.</summary>
public static class UiKit
{
    private static readonly Color Edge = new(0.35f, 0.45f, 0.65f, 0.7f);
    private static readonly Color Accent = new(0.35f, 0.65f, 1f);

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, float margin = 8f) => UiTheme.Box(bg, border, radius, margin);

    public static Label Text(int size = 14, Color? color = null)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeColorOverride("font_color", color ?? Colors.White);
        l.AddThemeFontSizeOverride("font_size", size);
        if (size >= UiTheme.Heading) UiFonts.Title(l);
        return l;
    }

    public static Button Button(string text, Action onPressed, int fontSize = 13, int minWidth = 0)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = new Vector2(minWidth, 0) };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        UiTheme.StyleButton(b);
        b.Pressed += onPressed; // the click sound is UiFx's, at the press
        return b;
    }
}
