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

    public override void _Ready() { Theme = UiTheme.Create(); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }

    /// <summary>Shows the overlay for a result: <paramref name="victory"/> picks the title and colours.</summary>
    /// <summary>What a scenario result adds to the overlay: the scenario, its goals, the stars and the best-result line.</summary>
    public sealed record ScenarioInfo(string Name, GoalsModel Goals, int Stars, string StarHint, string RecordLine, bool NewRecord);

    public void Present(OutcomeResult result, bool victory, int goalWaves, string note, IReadOnlyList<Choice> choices, ulong? seed = null, ScenarioInfo? scenario = null)
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
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(scenario is not null ? 680 : 560, 0) };
        panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, accent with { A = 0.85f }, 8, 24f));
        centre.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);

        var title = UiKit.Text(40, accent);
        title.Text = Loc.T(scenario is not null ? (victory ? "end.s.victory" : "end.s.defeat") : victory ? "end.victory.title" : "end.defeat.title");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);
        var sub = UiKit.Text(14, Muted);
        sub.Text = scenario is not null ? scenario.Name : victory ? Loc.T("end.victory.sub", goalWaves) : Loc.T("end.defeat.sub");
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(sub);
        if (scenario is not null) AddScenarioBlock(body, scenario, victory);
        body.AddChild(new HSeparator());

        var st = result.Stats;
        var grid = new GridContainer { Columns = scenario is not null ? 4 : 2 };
        grid.AddThemeConstantOverride("h_separation", scenario is not null ? 20 : 28);
        grid.AddThemeConstantOverride("v_separation", 4);
        void Row(string key, string value)
        {
            var k = UiKit.Text(scenario is not null ? 13 : 15, Muted);
            k.Text = Loc.T(key);
            k.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var v = UiKit.Text(scenario is not null ? 13 : 15, Colors.White);
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
        if (seed is { } s) Row("end.stat.seed", s.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    private static void AddScenarioBlock(VBoxContainer body, ScenarioInfo info, bool victory)
    {
        if (victory)
        {
            var stars = new StarRow { Stars = info.Stars, Max = 3, StarSize = 30, Align = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(0, 34), Name = "Stars" };
            body.AddChild(stars);
            if (info.StarHint.Length > 0)
            {
                var hint = UiKit.Text(12, Muted);
                hint.Text = info.StarHint;
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                body.AddChild(hint);
            }
            if (info.RecordLine.Length > 0)
            {
                var rec = UiKit.Text(14, info.NewRecord ? new Color(1f, 0.85f, 0.4f) : new Color(0.75f, 0.85f, 1f));
                rec.Text = info.RecordLine;
                rec.HorizontalAlignment = HorizontalAlignment.Center;
                body.AddChild(rec);
            }
        }
        body.AddChild(new HSeparator());
        foreach (var g in info.Goals.Rows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var mark = new GoalMark { Primary = g.Primary, Status = g.Status, CustomMinimumSize = new Vector2(18, 18) };
            row.AddChild(mark);
            Color color = g.Status == GoalStatus.Done ? VictoryColor : g.Status == GoalStatus.Failed ? DefeatColor : new Color(0.8f, 0.84f, 0.92f);
            var text = UiKit.Text(14, color);
            text.Text = g.Text;
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(text);
            var state = UiKit.Text(13, color);
            state.Text = Loc.T(g.Status == GoalStatus.Done ? "goal.status.done" : g.Status == GoalStatus.Failed ? "goal.status.failed" : "goal.status.pending")
                + (g.ShowBar && g.Status == GoalStatus.Pending ? "  " + g.ProgressText : "");
            state.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(state);
            body.AddChild(row);
        }
    }

    public void Dismiss() => Visible = false;
}
