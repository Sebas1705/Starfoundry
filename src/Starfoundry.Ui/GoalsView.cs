using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>One line of the HUD goals section.</summary>
/// <param name="Fraction">0 to 1, for the progress bar.</param>
/// <param name="ShowBar">False for goals that only hold (keep alive, keep fuel) and for one-step goals (research).</param>
/// <param name="RemainingSeconds">Seconds left of the goal's time limit while it is pending; null when it has none or is settled.</param>
public sealed record GoalRow(string Id, string Text, bool Primary, GoalStatus Status, long Current, long Target, double Fraction, string ProgressText,
    bool ShowBar, int? RemainingSeconds, string TimeText, bool Urgent);

/// <summary>A story message ready to show.</summary>
public sealed record StoryMessage(string Speaker, string Text, bool IsStart);

/// <summary>The goals section as a model: the rows, in display order (primary first), and the counts for its title.</summary>
public sealed record GoalsModel(IReadOnlyList<GoalRow> Rows, int PrimaryDone, int PrimaryTotal, int SecondaryDone, int SecondaryTotal)
{
    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>
/// Builds what the HUD shows of a <see cref="ScenarioProgress"/>: text in the current language, progress fractions, countdowns of time limits
/// and the story messages. Engine-free, so it is tested; the game only draws it.
/// </summary>
public static class GoalsView
{
    /// <summary>Seconds left under which a countdown is flagged as urgent.</summary>
    public const int UrgentSeconds = 60;

    public static GoalsModel Build(ScenarioProgress progress, double nowSeconds, bool spanish)
    {
        var rows = new List<GoalRow>();
        foreach (var g in progress.Goals)
        {
            int? remaining = null;
            if (g.Status == GoalStatus.Pending && g.TimeLimitSeconds > 0) remaining = Math.Max(0, (int)Math.Ceiling(g.TimeLimitSeconds - nowSeconds));
            bool bar = !g.Goal.IsCondition && !g.Goal.IsOneStep;
            double fraction = g.Target <= 0 ? 0 : Math.Clamp(g.Current / (double)g.Target, 0, 1);
            if (g.Status == GoalStatus.Done) fraction = 1;
            rows.Add(new GoalRow(g.Goal.Id, g.Text(spanish), g.Primary, g.Status, g.Current, g.Target, fraction, g.ProgressText, bar, remaining,
                remaining is { } r ? Clock(r) : "", remaining is { } u && u <= UrgentSeconds));
        }
        var (sd, st) = progress.SecondaryCount;
        return new GoalsModel(rows, progress.Primary.Count(g => g.Status == GoalStatus.Done), progress.Primary.Count(), sd, st);
    }

    /// <summary>m:ss, or h:mm:ss from an hour on.</summary>
    public static string Clock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>The guided steps stay for the campaign, the levels and the costed custom game; a sandbox without goals hides them.</summary>
    public static bool ShowGuidedSteps(ScenarioDef? scenario) => scenario is null || !(scenario.IsOpenEnded && scenario.SandboxMode);

    public static StoryMessage Message(StoryEvent e, bool spanish) => new(e.Beat.Speaker, e.Text(spanish), e.Beat.Trigger == "start");

    /// <summary>Stars as text, filled and empty: for example <c>***</c> as three filled and <c>**-</c> as two of three.</summary>
    public static string StarsText(int stars, int max = 3) => new string('*', Math.Clamp(stars, 0, max)) + new string('-', Math.Max(0, max - Math.Clamp(stars, 0, max)));

    /// <summary>How the stars of a result were earned, for the end screen: three flags (win, bound or secondary goals for two, all for three).</summary>
    public static string StarHint(ScenarioDef s, int stars)
    {
        var r = s.Stars;
        if (stars >= 3 || (r.TwoStarSeconds == 0 && r.ThreeStarSeconds == 0 && r.TwoStarSecondary == 0 && r.ThreeStarSecondary == 0)) return "";
        var parts = new List<string>();
        int next = stars + 1;
        int seconds = next == 3 ? r.ThreeStarSeconds : r.TwoStarSeconds;
        int secondary = next == 3 ? r.ThreeStarSecondary : r.TwoStarSecondary;
        if (seconds > 0) parts.Add(Loc.T("end.starHint.within", Clock(seconds)));
        if (secondary < 0) parts.Add(Loc.T("end.starHint.secondaryAll"));
        else if (secondary > 0) parts.Add(Loc.T("end.starHint.secondaryN", secondary));
        return parts.Count == 0 ? "" : Loc.T("end.starHint", next, string.Join(Loc.T("end.starHint.and"), parts));
    }
}
