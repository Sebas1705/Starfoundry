using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>The mark in front of a goal: a ring for a primary goal, a star for a secondary one; filled when done, crossed out in red when failed.</summary>
public partial class GoalMark : Control
{
    public bool Primary { get; set; } = true;
    public GoalStatus Status { get; set; }

    public GoalMark() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var size = Size.X > 0 ? Size : CustomMinimumSize;
        var c = new Vector2(size.X / 2f, size.Y / 2f);
        float r = Math.Min(size.X, size.Y) / 2f - 2f;
        var done = new Color(0.45f, 0.85f, 0.5f);
        var failed = new Color(0.92f, 0.3f, 0.25f);
        var open = new Color(0.8f, 0.86f, 1f);
        var gold = new Color(1f, 0.82f, 0.25f);
        if (Primary)
        {
            Color col = Status == GoalStatus.Done ? done : Status == GoalStatus.Failed ? failed : open;
            if (Status == GoalStatus.Done) DrawCircle(c, r, col);
            else DrawArc(c, r, 0, MathF.Tau, 24, col, 2f);
        }
        else
        {
            var pts = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                float a = -MathF.PI / 2f + k * MathF.PI / 5f;
                float rr = k % 2 == 0 ? r + 1.5f : (r + 1.5f) / 2.3f;
                pts[k] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
            }
            if (Status == GoalStatus.Done) DrawColoredPolygon(pts, gold);
            else DrawPolyline(pts.Append(pts[0]).ToArray(), Status == GoalStatus.Failed ? failed : gold, 1.6f);
        }
        if (Status == GoalStatus.Failed)
        {
            DrawLine(c + new Vector2(-r, -r), c + new Vector2(r, r), failed, 2f);
            DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), failed, 2f);
        }
    }
}

/// <summary>
/// The goals of a scenario in the HUD (<see cref="GoalsView"/> builds what is shown), the story beats, the scenario's end screens and the
/// goals sidecar of a save. A legacy world (no scenario) keeps the old goal line and <see cref="GameOutcome"/>.
/// </summary>
public partial class Play
{
    private sealed class GoalRowUi
    {
        public string Id = "";
        public VBoxContainer Root = null!;
        public GoalMark Mark = null!;
        public Label Text = null!, Count = null!, Time = null!;
        public ProgressBar Bar = null!;
    }

    private ProgressSnapshot? _loadedSnapshot;
    private VBoxContainer _goalsBox = null!;
    private Label _goalsTitle = null!;
    private VBoxContainer _goalRowsBox = null!;
    private readonly List<GoalRowUi> _goalRows = new();
    private StoryDialog? _storyDialog;
    private bool _storyPausedBefore;
    private bool _briefingShown;
    private GoalsModel? _goalsModel;
    private int _goalsWidth = 300;

    private bool StoryOpen => _storyDialog is { Visible: true };

    /// <summary>Guided steps are for the campaign and the levels; a sandbox without goals hides them.</summary>
    private bool GuidedSteps => GoalsView.ShowGuidedSteps(_scenario);

    // ---- building --------------------------------------------------------------------------------------------------

    private void BuildGoalsUi(VBoxContainer parent)
    {
        _goalsBox = new VBoxContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _goalsBox.AddThemeConstantOverride("separation", 3);
        _goalsTitle = MakeText(15, new Color(1f, 0.85f, 0.45f));
        _goalsBox.AddChild(_goalsTitle);
        _goalRowsBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _goalRowsBox.AddThemeConstantOverride("separation", 4);
        _goalsBox.AddChild(_goalRowsBox);
        parent.AddChild(_goalsBox);

        var layer = new CanvasLayer { Layer = 22 };
        AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        _storyDialog = new StoryDialog();
        root.AddChild(_storyDialog);
        _storyDialog.Dismissed += CloseStory;
        RebuildGoalRows();
    }

    /// <summary>Creates one row per goal of the scenario (they never change during a game).</summary>
    private void RebuildGoalRows()
    {
        if (_goalRowsBox is null) return;
        foreach (var child in _goalRowsBox.GetChildren()) { _goalRowsBox.RemoveChild(child); child.QueueFree(); }
        _goalRows.Clear();
        if (_progress is null) return;
        foreach (var g in _progress.Goals)
        {
            var row = new GoalRowUi { Id = g.Goal.Id };
            row.Root = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.Root.AddThemeConstantOverride("separation", 1);
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 6);
            row.Mark = new GoalMark { Primary = g.Primary, CustomMinimumSize = new Vector2(16, 16) };
            row.Mark.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            row.Text = MakeText(13);
            row.Text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.Text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.Count = MakeText(13, new Color(0.8f, 0.86f, 1f));
            row.Count.HorizontalAlignment = HorizontalAlignment.Right;
            row.Time = MakeText(13, new Color(1f, 0.85f, 0.45f));
            row.Time.HorizontalAlignment = HorizontalAlignment.Right;
            line.AddChild(row.Mark);
            line.AddChild(row.Text);
            line.AddChild(row.Count);
            line.AddChild(row.Time);
            row.Bar = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 1, CustomMinimumSize = new Vector2(0, 5), MouseFilter = Control.MouseFilterEnum.Ignore };
            row.Bar.AddThemeStyleboxOverride("background", Box(new Color(0.12f, 0.14f, 0.2f), null, 2, 0f));
            row.Root.AddChild(line);
            row.Root.AddChild(row.Bar);
            _goalRowsBox.AddChild(row.Root);
            _goalRows.Add(row);
        }
        _goalsModel = null;
    }

    /// <summary>Per frame: draws the goals of the scenario (or hides the box for a legacy world and for a game without goals).</summary>
    private void UpdateGoalsPanel()
    {
        if (_progress is null || _goalRows.Count != _progress.Goals.Count)
        {
            _goalsBox.Visible = false;
            return;
        }
        var model = GoalsView.Build(_progress, Net.Tick / (double)Units.TicksPerSecond, Loc.Lang == Language.Es);
        _goalsBox.Visible = !model.IsEmpty;
        if (model.IsEmpty) return;
        string title = L("goals.title", model.PrimaryDone, model.PrimaryTotal);
        if (model.SecondaryTotal > 0) title += "   " + L("goals.bonus", model.SecondaryDone, model.SecondaryTotal);
        _goalsTitle.Text = title;
        for (int i = 0; i < model.Rows.Count; i++)
        {
            var g = model.Rows[i];
            var ui = _goalRows[i];
            Color color = g.Status == GoalStatus.Done ? GoodColor : g.Status == GoalStatus.Failed ? BadColor : g.Primary ? Colors.White : new Color(0.88f, 0.9f, 0.78f);
            if (ui.Text.Text != g.Text) ui.Text.Text = g.Text;
            ui.Text.AddThemeColorOverride("font_color", color);
            if (ui.Mark.Status != g.Status) { ui.Mark.Status = g.Status; ui.Mark.QueueRedraw(); }
            string count = g.ShowBar && g.Status != GoalStatus.Failed ? g.ProgressText : "";
            if (ui.Count.Text != count) ui.Count.Text = count;
            ui.Count.AddThemeColorOverride("font_color", color);
            string time = g.Status == GoalStatus.Failed ? L("goal.status.failed") : g.TimeText;
            if (ui.Time.Text != time) ui.Time.Text = time;
            ui.Time.AddThemeColorOverride("font_color", g.Status == GoalStatus.Failed || g.Urgent ? BadColor : WornColor);
            bool bar = g.ShowBar && g.Status != GoalStatus.Failed;
            ui.Bar.Visible = bar;
            ui.Bar.Value = g.Fraction;
            ui.Bar.AddThemeStyleboxOverride("fill", Box(g.Status == GoalStatus.Done ? GoodColor : Accent, null, 2, 0f));
        }
        _goalsModel = model;
    }

    /// <summary>Called from the layout pass: sizes the goal rows to the steps panel's width.</summary>
    private void LayoutGoals(float width)
    {
        _goalsWidth = (int)width;
        foreach (var r in _goalRows)
        {
            r.Text.CustomMinimumSize = new Vector2(width - 16 - 46 - 54 - 18, 0);
            r.Count.CustomMinimumSize = new Vector2(46, 0);
            r.Time.CustomMinimumSize = new Vector2(54, 0);
        }
    }

    // ---- story -----------------------------------------------------------------------------------------------------

    private void HandleBeats(IReadOnlyList<StoryEvent> beats)
    {
        bool es = Loc.Lang == Language.Es;
        foreach (var e in beats)
        {
            var m = GoalsView.Message(e, es);
            if (m.IsStart && !_briefingShown && _storyDialog is not null && _scenario is not null && !_demo && _defenceShotPath is null)
            {
                _briefingShown = true;
                _storyPausedBefore = _paused;
                _paused = true;
                _wantCapture = false;
                _storyDialog.Present(_scenario.Name(es), _scenario.Briefing(es), m);
            }
            else Toast($"{m.Speaker}: {m.Text}", 14.0);
        }
    }

    private void CloseStory()
    {
        _paused = _storyPausedBefore;
        _wantCapture = true;
    }

    // ---- after a load ----------------------------------------------------------------------------------------------

    /// <summary>A loaded world has its own goals state: the tracker starts again from the sidecar of the save, or from the world when there is none.</summary>
    private void ResetGoalsAfterLoad()
    {
        if (_scenario is null) { _progress = null; return; }
        _progress = new ScenarioProgress(_content, _scenario);
        if (_loadedSnapshot is { } snap)
        {
            _progress.Restore(snap);
            _continueEndless = snap.KeepPlaying;
        }
        else _progress.Update(Net, Player); // no sidecar: what was already done is not news (its times restart, the known gap)
        _briefingShown = true; // a loaded game does not open the briefing again
        _loadedSnapshot = null;
        RebuildGoalRows();
        LayoutGoals(_goalsWidth);
    }

    // ---- the outcome of a scenario ---------------------------------------------------------------------------------

    private void UpdateScenarioOutcome()
    {
        if (_progress is null || _scenario is null) return;
        HandleBeats(_progress.Update(Net, Player));
        if (_outcomeShown || _defenceShotPath is not null || !Net.HasPlayer(Player)) return;
        var kind = _progress.Outcome;
        if (kind == OutcomeKind.Playing) return;
        if (kind == OutcomeKind.Won && _continueEndless) return;
        ShowScenarioOutcome();
    }

    private void ShowScenarioOutcome()
    {
        if (_endScreen is null || _progress is null || _scenario is null) return;
        _outcomeShown = true;
        _paused = true;
        _wantCapture = false;
        bool victory = _progress.Outcome == OutcomeKind.Won;
        bool es = Loc.Lang == Language.Es;
        var stats = GameOutcome.Stats(Net, Player);
        var result = new OutcomeResult(_progress.Outcome, _progress.Reason, stats);
        LogOutcome(result);
        var kind = ScenarioKinds.Of(_scenario);
        bool story = kind == ScenarioKind.Story;
        var model = GoalsView.Build(_progress, _progress.DecidedSeconds ?? stats.PlaySeconds, es);
        string recordLine = "";
        bool newRecord = false;
        var nextLevel = story ? ScenarioKinds.NextStory(_scenarios, _scenario) : null;
        if (victory)
        {
            double seconds = _progress.DecidedSeconds ?? stats.PlaySeconds;
            var progress = ScenarioStore.Progress;
            RecordResult rec = _endShotPath is not null
                ? new RecordResult(false, true, true, seconds + 95, 2)
                : progress.RecordWin(_scenario.Id, _progress.Stars, seconds);
            if (_endShotPath is null) ScenarioStore.SaveProgress();
            newRecord = rec.FirstCompletion || rec.NewBestTime || rec.NewBestStars;
            var best = progress.Get(_scenario.Id);
            recordLine = rec.FirstCompletion ? L("end.record.first", PlayLog.Format(seconds))
                : rec.NewBestTime ? L("end.record.newTime", PlayLog.Format(seconds), PlayLog.Format(rec.PreviousBestSeconds ?? seconds))
                : L("end.record.best", PlayLog.Format(seconds), PlayLog.Format(best?.BestSeconds ?? seconds));
        }

        var choices = new List<EndScreen.Choice>();
        string note = "";
        if (victory)
        {
            if (!story && _endShotPath is null) SaveGameToDisk("victory", silent: true);
            if (nextLevel is not null)
                choices.Add(new EndScreen.Choice(L("end.nextLevel"), () => StartScenarioFromEnd(nextLevel.Id, null, 0)));
            if (!story) choices.Add(new EndScreen.Choice(L("end.continue"), ContinueEndless));
            choices.Add(new EndScreen.Choice(L("end.mainMenu"), () => { if (!story) SaveGameToDisk("autosave", silent: true); GoToMainMenu(); }));
        }
        else
        {
            string? last = Launch.NewestSave();
            note = L(last is null ? "end.defeat.noSave" : "end.defeat.note");
            choices.Add(new EndScreen.Choice(L("end.retry"), () => StartScenarioFromEnd(_scenario.Id, _scenario.PlayerRules ? Net.Rules : null, _galaxySeed)));
            choices.Add(new EndScreen.Choice(L("end.loadLast"), () => LoadLastAfterDefeat(), last is not null));
            choices.Add(new EndScreen.Choice(L("end.mainMenu"), GoToMainMenu));
        }
        string starHint = victory ? GoalsView.StarHint(_scenario, _progress.Stars) : "";
        var info = new EndScreen.ScenarioInfo(_scenario.Name(es), model, _progress.Stars, starHint, recordLine, newRecord);
        _endScreen.Present(result, victory, _progress.Goals.Count, note, choices, _galaxySeed, info);
    }

    /// <summary>Starts a scenario again from the end screen (retry or the next level): the scene reloads with the choice set.</summary>
    private void StartScenarioFromEnd(string id, WorldRules? rules, ulong seed)
    {
        FinishPlayLog();
        Launch.StartScenario(id, rules, seed);
        Launch.MenuShotPath = null;
        GetTree().ReloadCurrentScene();
    }

    // ---- --goals-shot ----------------------------------------------------------------------------------------------

    private int _goalsFrame;

    /// <summary>Makes the picture: level 3 with two waves cleared, a failed bonus goal and the story dialog still to come.</summary>
    private void GoalsShotSetup(string[] args)
    {
        _wantCapture = false;
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (_progress is null || _scenario is null) return;
        var goals = _progress.Goals.Select(g => g.Goal.Id == "hold_the_line"
            ? new GoalSnapshot(g.Goal.Id, GoalStatus.Pending, 2, null, null)
            : g.Goal.Id == "no_losses" ? new GoalSnapshot(g.Goal.Id, GoalStatus.Failed, 0, null, 412.0)
            : new GoalSnapshot(g.Goal.Id, GoalStatus.Pending, 1, null, null)).ToList();
        _progress.Restore(new ProgressSnapshot(0, goals, new List<(int, double)>(), OutcomeKind.Playing, OutcomeReason.None, 0, null, false));
        _briefingShown = false;
    }

    private void GoalsShotFrame()
    {
        if (_goalsShotPath is not { } path) return;
        _goalsFrame++;
        _wantCapture = false;
        if (_goalsFrame == 25) Save(path + ".png");
        if (_goalsFrame == 26)
        {
            // The briefing: the start beat of the level fires on the first update of a fresh tracker.
            var fresh = new ScenarioProgress(_content, _scenario!);
            HandleBeats(fresh.Update(Net, Player));
        }
        if (_goalsFrame == 50) Save(path + ".story.png");
        if (_goalsFrame == 51) GetTree().Quit();
    }
}
