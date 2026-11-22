using System.Globalization;
using System.Text;

namespace Starfoundry.Ui;

/// <summary>One goal's saved state.</summary>
public sealed record GoalSnapshot(string Id, GoalStatus Status, long Current, double? CompletedSeconds, double? FailedSeconds);

/// <summary>
/// What a save needs to remember of a <see cref="ScenarioProgress"/>, because the tracker is otherwise recomputed from the world: when each goal
/// was done or failed, which story beats fired and when, the decided outcome and whether the player chose to keep playing after it.
/// <see cref="Tick"/> is the tick of the save it belongs to, so a stale file is ignored.
/// </summary>
public sealed record ProgressSnapshot(long Tick, IReadOnlyList<GoalSnapshot> Goals, IReadOnlyList<(int Beat, double Seconds)> Beats,
    OutcomeKind Outcome, OutcomeReason Reason, int Stars, double? DecidedSeconds, bool KeepPlaying);

/// <summary>The text form of a <see cref="ProgressSnapshot"/>: the <c>&lt;slot&gt;.goals</c> file next to a save. A bad file reads as null.</summary>
public static class ProgressSnapshotText
{
    private const string Header = "SFGOALS 1";

    public static string Serialize(ProgressSnapshot s)
    {
        var inv = CultureInfo.InvariantCulture;
        static string D(double? d) => d is { } v ? v.ToString("R", CultureInfo.InvariantCulture) : "-";
        var sb = new StringBuilder(Header).Append('\n');
        sb.Append("tick ").Append(s.Tick.ToString(inv)).Append('\n');
        sb.Append("outcome ").Append((int)s.Outcome).Append(' ').Append((int)s.Reason).Append(' ').Append(s.Stars).Append(' ').Append(D(s.DecidedSeconds)).Append(' ').Append(s.KeepPlaying ? 1 : 0).Append('\n');
        foreach (var g in s.Goals)
            sb.Append("goal ").Append(g.Id).Append(' ').Append((int)g.Status).Append(' ').Append(g.Current.ToString(inv)).Append(' ').Append(D(g.CompletedSeconds)).Append(' ').Append(D(g.FailedSeconds)).Append('\n');
        foreach (var (beat, seconds) in s.Beats) sb.Append("beat ").Append(beat).Append(' ').Append(D(seconds)).Append('\n');
        return sb.ToString();
    }

    private static bool Dbl(string t, out double? v)
    {
        v = null;
        if (t == "-") return true;
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || !double.IsFinite(d) || d < 0) return false;
        v = d;
        return true;
    }

    public static ProgressSnapshot? Parse(string? text)
    {
        var inv = CultureInfo.InvariantCulture;
        if (text is null) return null;
        var lines = text.Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Header) return null;
        long tick = -1;
        var outcome = OutcomeKind.Playing;
        var reason = OutcomeReason.None;
        int stars = 0;
        double? decided = null;
        bool keep = false;
        var goals = new List<GoalSnapshot>();
        var beats = new List<(int, double)>();
        try
        {
            foreach (string raw in lines.Skip(1))
            {
                var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                switch (p[0])
                {
                    case "tick" when p.Length == 2: tick = long.Parse(p[1], inv); break;
                    case "outcome" when p.Length == 6:
                        outcome = (OutcomeKind)int.Parse(p[1], inv);
                        reason = (OutcomeReason)int.Parse(p[2], inv);
                        stars = Math.Clamp(int.Parse(p[3], inv), 0, 3);
                        if (!Dbl(p[4], out decided)) return null;
                        keep = p[5] == "1";
                        if (!Enum.IsDefined(outcome) || !Enum.IsDefined(reason)) return null;
                        break;
                    case "goal" when p.Length == 6:
                    {
                        var status = (GoalStatus)int.Parse(p[2], inv);
                        if (!Enum.IsDefined(status) || !Dbl(p[4], out var done) || !Dbl(p[5], out var failed)) return null;
                        goals.Add(new GoalSnapshot(p[1], status, long.Parse(p[3], inv), done, failed));
                        break;
                    }
                    case "beat" when p.Length == 3:
                        if (!Dbl(p[2], out var at) || at is null) return null;
                        beats.Add((int.Parse(p[1], inv), at.Value));
                        break;
                }
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException) { return null; }
        return tick < 0 ? null : new ProgressSnapshot(tick, goals, beats, outcome, reason, stars, decided, keep);
    }
}
