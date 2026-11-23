using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The briefing of a scenario: a dimmed full-screen overlay with the scenario name, its briefing, who is speaking and what they say, and one
/// button ("Intro") that closes it. The game pauses while it is open. Later story beats are non-blocking toasts, not this dialog.
/// </summary>
public partial class StoryDialog : Control
{
    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.96f);
    private static readonly Color Accent = new(0.45f, 0.75f, 1f);
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);

    public event Action? Dismissed;

    public StoryDialog()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready() { Theme = UiTheme.Create(); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }

    public void Present(string scenarioName, string briefing, StoryMessage message)
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, Accent with { A = 0.85f }, 8, 24f));
        centre.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(body);

        var kicker = UiKit.Text(13, Muted);
        kicker.Text = Loc.T("story.briefing");
        body.AddChild(kicker);
        var title = UiKit.Text(30, Colors.White);
        title.Text = scenarioName;
        body.AddChild(title);
        if (briefing.Length > 0)
        {
            var b = UiKit.Text(15, Muted);
            b.Text = briefing;
            b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            b.CustomMinimumSize = new Vector2(670, 0);
            body.AddChild(b);
        }
        body.AddChild(new HSeparator());
        var speaker = UiKit.Text(15, Accent);
        speaker.Text = message.Speaker;
        body.AddChild(speaker);
        var text = UiKit.Text(17, new Color(0.92f, 0.96f, 1f));
        text.Text = message.Text;
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.CustomMinimumSize = new Vector2(670, 0);
        body.AddChild(text);
        var ok = UiKit.Button(Loc.T("story.dismiss"), Dismiss, 17, 200);
        ok.Name = "DismissButton";
        ok.CustomMinimumSize = new Vector2(200, 40);
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        body.AddChild(ok);
        Visible = true;
        ok.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Dismiss()
    {
        if (!Visible) return;
        Visible = false;
        Dismissed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.Enter or Key.KpEnter }) return;
        GetViewport().SetInputAsHandled();
        Dismiss();
    }
}
