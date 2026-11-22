using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Where a goal stands.</summary>
public enum GoalStatus { Pending, Done, Failed }

/// <summary>
/// One goal as the HUD shows it. <see cref="Current"/> and <see cref="Target"/> are the numbers of a progress bar (a keep-alive or keep-fuel
/// goal shows 1 of 1 while it holds and 0 of 1 once failed). Seconds are simulated play time.
/// </summary>
public sealed record GoalState(GoalDef Goal, bool Primary, GoalStatus Status, long Current, long Target, double? CompletedSeconds, double? FailedSeconds)
{
    public string Text(bool spanish) => spanish ? Goal.TextEs : Goal.TextEn;

    /// <summary>"12/60" style text; empty for goals that only hold.</summary>
    public string ProgressText => Goal.IsCondition || Goal.Type == GoalType.Research ? "" : $"{Math.Min(Current, Target)}/{Target}";

    /// <summary>The time limit of the goal in seconds, or 0 when it has none.</summary>
    public int TimeLimitSeconds => Goal.TimeLimitSeconds;
}

/// <summary>A story beat that fired, and when.</summary>
public sealed record StoryEvent(StoryBeat Beat, double Seconds)
{
    public string Text(bool spanish) => spanish ? Beat.TextEs : Beat.TextEn;
}

/// <summary>
/// Evaluates the goals of a scenario against the simulation ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)). Call <see cref="Update"/>
/// every tick-batch (a frame is fine): it marks goals done or failed and remembers when, fires each story beat once, and decides the
/// outcome. A goal that is done stays done; a decided outcome (won or lost) is final, so the game can go on afterwards without the result
/// changing. Pure logic over the simulation, tested without the engine.
/// <list type="bullet">
/// <item>Lost: the ship was lost, a primary keep-alive or keep-fuel goal failed, or a primary goal's time limit passed unfinished.</item>
/// <item>Won: every primary goal is done (a keep goal that never failed counts) with the ship alive.</item>
/// <item>Stars: 1 for a win; 2 and 3 when the win came within the scenario's time bounds with enough secondary goals done.</item>
/// </list>
/// </summary>
public sealed class ScenarioProgress
{
    private readonly ContentDb _content;
    private readonly GoalState[] _goals;
    private readonly List<StoryEvent> _fired = new();
    private readonly HashSet<int> _firedBeats = new();
    private bool _started;
    private double? _decidedAt;

    public ScenarioProgress(ContentDb content, ScenarioDef scenario)
    {
        _content = content;
        Scenario = scenario;
        var list = new List<GoalState>();
        foreach (var g in scenario.Primary) list.Add(new GoalState(g, true, GoalStatus.Pending, 0, 1, null, null));
        foreach (var g in scenario.Secondary) list.Add(new GoalState(g, false, GoalStatus.Pending, 0, 1, null, null));
        _goals = list.ToArray();
    }

    public ScenarioDef Scenario { get; }

    /// <summary>Every goal, primary first.</summary>
    public IReadOnlyList<GoalState> Goals => _goals;

    public IEnumerable<GoalState> Primary => _goals.Where(g => g.Primary);
    public IEnumerable<GoalState> Secondary => _goals.Where(g => !g.Primary);

    /// <summary>Every story beat that has fired so far, in order.</summary>
    public IReadOnlyList<StoryEvent> Story => _fired;

    public OutcomeKind Outcome { get; private set; } = OutcomeKind.Playing;
    public OutcomeReason Reason { get; private set; } = OutcomeReason.None;

    /// <summary>0 while playing or after a loss; 1 to 3 after a win.</summary>
    public int Stars { get; private set; }

    /// <summary>Simulated seconds at which the outcome was decided, or null while playing.</summary>
    public double? DecidedSeconds => _decidedAt;

    /// <summary>Secondary goals done, and how many there are.</summary>
    public (int Done, int Total) SecondaryCount => (_goals.Count(g => !g.Primary && g.Status == GoalStatus.Done), _goals.Count(g => !g.Primary));

    /// <summary>What a save has to remember (see <see cref="ProgressSnapshot"/>). <paramref name="tick"/> is the tick of the save.</summary>
    public ProgressSnapshot Export(long tick, bool keepPlaying)
    {
        var goals = _goals.Select(g => new GoalSnapshot(g.Goal.Id, g.Status, g.Current, g.CompletedSeconds, g.FailedSeconds)).ToList();
        var beats = new List<(int, double)>();
        foreach (var e in _fired)
            for (int i = 0; i < Scenario.Story.Count; i++)
                if (ReferenceEquals(Scenario.Story[i], e.Beat)) beats.Add((i, e.Seconds));
        return new ProgressSnapshot(tick, goals, beats, Outcome, Reason, Stars, _decidedAt, keepPlaying);
    }

    /// <summary>
    /// Puts a saved state back, before the first <see cref="Update"/> after loading a save: goals keep the seconds they were done or failed at, beats
    /// that fired do not fire again, a decided outcome stays decided. Goals the scenario no longer has and beats out of range are ignored.
    /// </summary>
    public void Restore(ProgressSnapshot snap)
    {
        for (int i = 0; i < _goals.Length; i++)
        {
            var saved = snap.Goals.FirstOrDefault(g => g.Id == _goals[i].Goal.Id);
            if (saved is null) continue;
            _goals[i] = _goals[i] with { Status = saved.Status, Current = Math.Max(0, saved.Current), CompletedSeconds = saved.CompletedSeconds, FailedSeconds = saved.FailedSeconds };
            if (saved.Status == GoalStatus.Done) _goals[i] = _goals[i] with { Current = _goals[i].Target };
        }
        _fired.Clear();
        _firedBeats.Clear();
        foreach (var (beat, seconds) in snap.Beats.OrderBy(b => b.Seconds))
        {
            if (beat < 0 || beat >= Scenario.Story.Count || !_firedBeats.Add(beat)) continue;
            _fired.Add(new StoryEvent(Scenario.Story[beat], seconds));
        }
        _started = true;
        Outcome = snap.Outcome;
        Reason = snap.Reason;
        Stars = snap.Stars;
        _decidedAt = snap.DecidedSeconds;
    }

    /// <summary>The goal with this id, or null.</summary>
    public GoalState? Find(string id) => _goals.FirstOrDefault(g => g.Goal.Id == id);

    /// <summary>
    /// Looks at the world and moves the goals and the outcome on. Returns the story beats that fired during this call (in order); a
    /// start beat fires on the first call.
    /// </summary>
    public IReadOnlyList<StoryEvent> Update(BeltNetwork net, int player)
    {
        double now = net.Tick / (double)Units.TicksPerSecond;
        var fresh = new List<StoryEvent>();
        void Fire(Func<StoryBeat, bool> match)
        {
            for (int i = 0; i < Scenario.Story.Count; i++)
            {
                if (_firedBeats.Contains(i) || !match(Scenario.Story[i])) continue;
                _firedBeats.Add(i);
                var e = new StoryEvent(Scenario.Story[i], now);
                _fired.Add(e);
                fresh.Add(e);
            }
        }

        if (!_started)
        {
            _started = true;
            Fire(b => b.Trigger == "start");
        }

        if (Outcome == OutcomeKind.Playing)
        {
            for (int i = 0; i < _goals.Length; i++)
            {
                var s = _goals[i];
                var (current, target, complete, broken) = Measure(net, s.Goal);
                if (s.Status != GoalStatus.Pending)
                {
                    _goals[i] = s with { Current = Math.Max(s.Current, current), Target = target };
                    continue;
                }
                int limit = s.Goal.TimeLimitSeconds;
                if (broken || (!s.Goal.IsCondition && complete && limit > 0 && now > limit) || (!complete && limit > 0 && now > limit))
                {
                    _goals[i] = s with { Status = GoalStatus.Failed, Current = s.Goal.IsCondition ? 0 : current, Target = target, FailedSeconds = now };
                    Fire(b => b.Trigger == "fail:" + s.Goal.Id);
                }
                else if (!s.Goal.IsCondition && complete)
                {
                    _goals[i] = s with { Status = GoalStatus.Done, Current = target, Target = target, CompletedSeconds = now };
                    Fire(b => b.Trigger == "goal:" + s.Goal.Id);
                }
                else _goals[i] = s with { Current = s.Goal.IsCondition ? 1 : current, Target = target };
            }

            if (net.StructureRules && net.ShipLost(player))
                Decide(OutcomeKind.Lost, OutcomeReason.ShipLost, now);
            else if (_goals.Any(g => g.Primary && g.Status == GoalStatus.Failed))
                Decide(OutcomeKind.Lost, OutcomeReason.GoalFailed, now);
            else if (Scenario.Primary.Count > 0 && net.HasPlayer(player) && _goals.Where(g => g.Primary).All(g => g.Goal.IsCondition || g.Status == GoalStatus.Done))
                Decide(OutcomeKind.Won, Scenario.Primary.Any(g => g.Type == GoalType.SurviveWaves) ? OutcomeReason.WavesSurvived : OutcomeReason.GoalsDone, now);
        }

        Fire(b => b.Trigger.StartsWith("time:", StringComparison.Ordinal) && now >= int.Parse(b.Trigger.AsSpan(5)));
        if (Outcome == OutcomeKind.Won) Fire(b => b.Trigger == "win");
        else if (Outcome == OutcomeKind.Lost) Fire(b => b.Trigger == "lose");
        return fresh;
    }

    private void Decide(OutcomeKind kind, OutcomeReason reason, double now)
    {
        Outcome = kind;
        Reason = reason;
        _decidedAt = now;
        if (kind != OutcomeKind.Won) return;

        // Goals that only hold are done when the scenario is won without them failing.
        for (int i = 0; i < _goals.Length; i++)
            if (_goals[i].Goal.IsCondition && _goals[i].Status == GoalStatus.Pending)
                _goals[i] = _goals[i] with { Status = GoalStatus.Done, Current = 1, Target = 1, CompletedSeconds = now };

        var rule = Scenario.Stars;
        int secondary = SecondaryCount.Done, total = SecondaryCount.Total;
        // A tier counts only when it asks for something (a time bound or at least one secondary goal); a rule of zeros gives one star.
        bool Tier(int seconds, int needed)
        {
            int need = needed < 0 ? total : needed;
            return (seconds > 0 || need > 0) && (seconds == 0 || now <= seconds) && secondary >= need;
        }
        Stars = Tier(rule.ThreeStarSeconds, rule.ThreeStarSecondary) ? 3 : Tier(rule.TwoStarSeconds, rule.TwoStarSecondary) ? 2 : 1;
    }

    /// <summary>Current, target, whether the goal is met, and (for keep goals) whether it is broken.</summary>
    private (long Current, long Target, bool Complete, bool Broken) Measure(BeltNetwork net, GoalDef g)
    {
        switch (g.Type)
        {
            case GoalType.SurviveWaves:
            {
                long target = g.Count > 0 ? g.Count : net.Rules.GoalWaves;
                long have = GameOutcome.WavesSurvived(net);
                return (have, target, have >= target, false);
            }
            case GoalType.Research: return (net.IsResearched(g.Node) ? 1 : 0, 1, net.IsResearched(g.Node), false);
            case GoalType.Produce: { long have = net.ProducedTotal(g.Item); return (have, g.Count, have >= g.Count, false); }
            case GoalType.Mine: { long have = net.MinedTotal(g.Item); return (have, g.Count, have >= g.Count, false); }
            case GoalType.VisitSystems: { long have = net.VisitedSystemCount; return (have, g.Count, have >= g.Count, false); }
            case GoalType.FoundStations: { long have = net.StationsFounded; return (have, g.Count, have >= g.Count, false); }
            case GoalType.BuildModule: { long have = net.ModulesBuilt(g.Kind!.Value); return (have, g.Count, have >= g.Count, false); }
            case GoalType.KeepAlive:
            {
                long lost = g.Kind is { } k ? net.ModulesLostOf(k) : net.ModulesLost;
                return (lost == 0 ? 1 : 0, 1, false, lost > 0);
            }
            case GoalType.KeepFuel: return (net.FuelStarvedTicks == 0 ? 1 : 0, 1, false, net.FuelStarvedTicks > 0);
            default: return (0, 1, false, false);
        }
    }
}
