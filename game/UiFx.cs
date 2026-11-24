using System;
using System.Runtime.CompilerServices;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The feel of the interface in one place (see <c>docs/prototypes/ui-icons.md</c>, "Reactivity"). Every button, in every screen, gets a hover glow and a
/// press dip (100 to 160 ms, eased), a click sound at the moment of the press (not the release), an optional quiet tick on hover, and a short wobble when a
/// disabled one is pressed. Panels fade and grow in with <see cref="Present"/> and out with <see cref="Dismiss"/>. Nothing here changes what a control does:
/// it only moves <c>SelfModulate</c>, <c>Scale</c>, <c>Rotation</c> and <c>Modulate</c>, which layout code does not write.
/// The headless screenshot and audit runs set <see cref="Instant"/> so they never catch a control half way.
/// </summary>
public static class UiFx
{
    /// <summary>True in the screenshot and audit runs: no transition, the end state at once.</summary>
    public static bool Instant { get; set; } = Array.Exists(OS.GetCmdlineUserArgs(),
        a => a.Contains("-shot") || a.StartsWith("--ui-audit") || a.StartsWith("--hud-sweep") || a.StartsWith("--stress") || a == "--demo");

    private const string Meta = "uifx";
    private static bool _installed;
    private static readonly ConditionalWeakTable<Control, Tween> Tweens = new();

    private static readonly Color HoverTint = new(1.18f, 1.2f, 1.28f);

    /// <summary>Hooks every button added to the tree from now on. Call once, early, from the first screen's <c>_Ready</c>.</summary>
    public static void Install(Node any)
    {
        if (_installed) return;
        _installed = true;
        any.GetTree().NodeAdded += n => { if (n is BaseButton b) Polish(b); };
    }

    /// <summary>Gives one button the hover, press, disabled and sound behaviour. Safe to call twice.</summary>
    public static void Polish(BaseButton b)
    {
        if (b.HasMeta(Meta)) return;
        b.SetMeta(Meta, true);
        b.MouseEntered += () =>
        {
            if (b.Disabled) return;
            Glow(b, true);
            if (SettingsStore.Current.UiHoverSound) AudioManager.PlayUi(Sfx.UiHover);
        };
        b.MouseExited += () => { Glow(b, false); Dip(b, false); };
        b.ButtonDown += () =>
        {
            Dip(b, true);
            if (SettingsStore.Current.UiClickSound) AudioManager.PlayUi(Sfx.UiClick);
        };
        b.ButtonUp += () => Dip(b, false);
        b.GuiInput += e =>
        {
            if (b.Disabled && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Deny(b);
        };
    }

    private static Tween? Fresh(Control c)
    {
        if (!c.IsInsideTree()) return null;
        if (Tweens.TryGetValue(c, out var old) && old.IsValid()) old.Kill();
        var t = c.CreateTween();
        t.SetParallel(true);
        t.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        Tweens.AddOrUpdate(c, t);
        return t;
    }

    private static void Glow(BaseButton b, bool on)
    {
        var target = on ? HoverTint : Colors.White;
        if (Instant || !b.IsInsideTree()) { b.SelfModulate = target; return; }
        // The brightness follows within one frame (the first step of the tween runs on the next frame) and settles in 100 ms.
        Fresh(b)?.TweenProperty(b, "self_modulate", target, UiMotion.Fast);
    }

    private static void Dip(BaseButton b, bool down)
    {
        if (b.Disabled && down) return;
        var scale = down ? new Vector2(0.97f, 0.97f) : Vector2.One;
        b.PivotOffset = b.Size / 2f;
        if (Instant || !b.IsInsideTree()) { b.Scale = scale; return; }
        if (down) Fresh(b)?.TweenProperty(b, "scale", scale, UiMotion.Fast * 0.6f);
        else Fresh(b)?.TweenProperty(b, "scale", scale, UiMotion.Normal);
    }

    /// <summary>A short wobble and a soft error tick for a press on a disabled control.</summary>
    public static void Deny(Control c)
    {
        if (SettingsStore.Current.UiClickSound) AudioManager.PlayUi(Sfx.UiError);
        if (Instant || !c.IsInsideTree()) return;
        c.PivotOffset = c.Size / 2f;
        if (Tweens.TryGetValue(c, out var old) && old.IsValid()) old.Kill();
        var t = c.CreateTween();
        Tweens.AddOrUpdate(c, t);
        t.TweenProperty(c, "rotation", 0.035f, 0.03);
        t.TweenProperty(c, "rotation", -0.035f, 0.05);
        t.TweenProperty(c, "rotation", 0f, 0.04);
    }

    // ---- panels -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Fades a panel in and grows it from 97 percent (160 ms, eased out) every time it becomes visible, instead of popping. Call once when the panel is
    /// built; the panel's own code keeps setting <c>Visible</c> as before.
    /// </summary>
    public static void Present(Control panel)
    {
        if (panel.HasMeta("uifx_present")) return;
        panel.SetMeta("uifx_present", true);
        panel.VisibilityChanged += () =>
        {
            if (!panel.Visible || Instant || !panel.IsInsideTree()) { panel.Modulate = panel.Modulate with { A = 1f }; return; }
            panel.PivotOffset = panel.Size / 2f;
            panel.Modulate = panel.Modulate with { A = 0f };
            panel.Scale = new Vector2(0.97f, 0.97f);
            var t = Fresh(panel);
            if (t is null) return;
            t.TweenProperty(panel, "modulate:a", 1f, UiMotion.Slow);
            t.TweenProperty(panel, "scale", Vector2.One, UiMotion.Slow);
        };
    }

    /// <summary>Stops a fade in or out in progress and puts the panel back to its normal look (call before showing a panel that may be on its way out).</summary>
    public static void Cancel(Control panel)
    {
        if (Tweens.TryGetValue(panel, out var old) && old.IsValid()) old.Kill();
        panel.Modulate = panel.Modulate with { A = 1f };
        panel.Scale = Vector2.One;
    }

    /// <summary>Fades a panel out (120 ms) and then hides it, calling <paramref name="done"/> when it is gone. Instant in the headless runs.</summary>
    public static void Dismiss(Control panel, Action? done = null)
    {
        if (!panel.Visible) { done?.Invoke(); return; }
        if (Instant || !panel.IsInsideTree())
        {
            panel.Visible = false;
            done?.Invoke();
            return;
        }
        var t = Fresh(panel);
        if (t is null) { panel.Visible = false; done?.Invoke(); return; }
        t.TweenProperty(panel, "modulate:a", 0f, UiMotion.Fast + 0.02f);
        t.Chain().TweenCallback(Callable.From(() =>
        {
            panel.Visible = false;
            panel.Modulate = panel.Modulate with { A = 1f };
            panel.Scale = Vector2.One;
            done?.Invoke();
        }));
    }
}
