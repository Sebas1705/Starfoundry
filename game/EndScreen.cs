using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The victory and defeat overlays: a dimmed full-screen panel with a title, the stats table and a column of buttons, built in code
/// in the style of <see cref="MenuScreen"/>. The game decides what the buttons do; this class only shows them.
/// </summary>
public partial class EndScreen : Control
{
    /// <summary>One button: its text, what it does, and whether it can be pressed.</summary>
    public sealed record Choice(string Text, Action OnPressed, bool Enabled = true);

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.95f);
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);
    private static readonly Color VictoryColor = new(0.55f, 0.95f, 0.65f);
    private static readonly Color DefeatColor = new(1f, 0.45f, 0.4f);

    public EndScreen()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

    /// <summary>Shows the overlay for a result: <paramref name="victory"/> picks the title and colours.</summary>
    public void Present(OutcomeResult result, bool victory, int goalWaves, string note, IReadOnlyList<Choice> choices)
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, victory ? 0.55f : 0.7f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var accent = victory ? VictoryColor : DefeatColor;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, accent with { A = 0.85f }, 8, 24f));
        centre.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);

        var title = UiKit.Text(40, accent);
        title.Text = Loc.T(victory ? "end.victory.title" : "end.defeat.title");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);
        var sub = UiKit.Text(14, Muted);
        sub.Text = victory ? Loc.T("end.victory.sub", goalWaves) : Loc.T("end.defeat.sub");
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(sub);
        body.AddChild(new HSeparator());

        var st = result.Stats;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 4);
        void Row(string key, string value)
        {
            var k = UiKit.Text(15, Muted);
            k.Text = Loc.T(key);
            k.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var v = UiKit.Text(15, Colors.White);
            v.Text = value;
            v.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(k);
            grid.AddChild(v);
        }
        Row("end.stat.time", PlayLog.Format(st.PlaySeconds));
        Row("end.stat.modules", st.ModulesBuilt.ToString(Loc.Culture));
        Row("end.stat.stations", $"{st.StationsFounded} / {st.StationsLost}");
        Row("end.stat.waves", st.WavesSurvived.ToString(Loc.Culture));
        Row("end.stat.mined", $"{st.MinedByHand} / {st.MinedByDrones}");
        Row("end.stat.lost", $"{st.ModulesLost} / {st.MachinesLost}");
        body.AddChild(grid);
        body.AddChild(new HSeparator());

        if (note.Length > 0)
        {
            var n = UiKit.Text(13, new Color(0.75f, 0.85f, 1f));
            n.Text = note;
            n.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.CustomMinimumSize = new Vector2(500, 0);
            body.AddChild(n);
        }

        Button? first = null;
        foreach (var c in choices)
        {
            var b = UiKit.Button(c.Text, c.OnPressed, 17, 500);
            b.CustomMinimumSize = new Vector2(0, 40);
            b.Disabled = !c.Enabled;
            body.AddChild(b);
            if (first is null && c.Enabled) first = b;
        }
        Visible = true;
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Dismiss() => Visible = false;
}
