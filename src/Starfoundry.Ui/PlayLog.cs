using System.Globalization;
using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>One line of a play log: when (simulated seconds), what kind of event and a short text.</summary>
public sealed record PlayLogEntry(double Seconds, string Kind, string Text);

/// <summary>
/// Notes the milestones of a played game (guided steps, research, waves, losses, jumps, a full hold) so a play-test
/// leaves numbers behind. The kinds and names match the balance simulator's report, so a person's times can be put next
/// to the scripted player's. Pure logic over the simulation, tested without the engine.
/// </summary>
public sealed class PlayLog
{
    private readonly ContentDb _content;
    private readonly ObjectiveTracker _objectives;
    private readonly List<PlayLogEntry> _entries = new();
    private readonly HashSet<Objective> _seenObjectives = new();
    private readonly int[] _levels;
    private long _waves, _machinesLost, _wallsLost, _shipsLost, _stationsLost, _modulesLost;
    private int _visited = 1;
    private int _holdFullTicks;
    private long _lastTick;
    private bool _warned;

    /// <param name="stationRules">True when the played world uses the station rules (the guided steps then include the core and the cargo port).</param>
    /// <param name="structures">True when the played world uses structures (the starter ship); it wins over the station rules.</param>
    public PlayLog(ContentDb content, bool stationRules = false, bool structures = false)
    {
        _content = content;
        _objectives = new ObjectiveTracker(content, stationRules, structures);
        _levels = new int[content.Research.Count];
    }

    public IReadOnlyList<PlayLogEntry> Entries => _entries;

    /// <summary>Seconds the hold was completely full, a sign of a stalled player.</summary>
    public double HoldFullSeconds => _holdFullTicks / (double)Units.TicksPerSecond;

    /// <summary>Looks at the world and returns the entries that are new since the last call.</summary>
    public IReadOnlyList<PlayLogEntry> Update(BeltNetwork net, int player)
    {
        double now = net.Tick / (double)Units.TicksPerSecond;
        var fresh = new List<PlayLogEntry>();
        void Add(string kind, string text) => fresh.Add(new PlayLogEntry(now, kind, text));

        _objectives.Update(net, player);
        foreach (var s in _objectives.States)
            if (s.Done && _seenObjectives.Add(s.Objective)) Add("Step", s.Objective.ToString());

        foreach (var r in _content.Research)
        {
            int level = net.ResearchLevel(r.Index);
            if (level > _levels[r.Index])
            {
                _levels[r.Index] = level;
                Add("Researched", r.MaxLevels > 1 ? $"{r.Id} level {level}" : r.Id);
            }
        }

        if (net.ThreatActive)
        {
            if (!_warned && net.GetSystemThreat(0).WarningRemaining >= 0) { _warned = true; Add("Wave", "first warning"); }
            if (net.WavesSpawned > _waves)
            {
                _waves = net.WavesSpawned;
                Add("Wave", $"wave {_waves} arrived with {net.EnemyCount} enemies alive");
            }
            if (net.MachinesLost > _machinesLost) { Add("Loss", $"{net.MachinesLost - _machinesLost} machine(s) destroyed ({net.MachinesLost} in total)"); _machinesLost = net.MachinesLost; }
            if (net.WallsLost > _wallsLost) { Add("Loss", $"{net.WallsLost - _wallsLost} wall(s) destroyed ({net.WallsLost} in total)"); _wallsLost = net.WallsLost; }
            if (net.ShipsLost > _shipsLost) { Add("Loss", "the ship was destroyed"); _shipsLost = net.ShipsLost; }
            if (net.StationsLost > _stationsLost) { Add("Loss", $"{net.StationsLost - _stationsLost} station(s) lost with their core ({net.StationsLost} in total)"); _stationsLost = net.StationsLost; }
            if (net.ModulesLost > _modulesLost) { Add("Loss", $"{net.ModulesLost - _modulesLost} module(s) destroyed ({net.ModulesLost} in total)"); _modulesLost = net.ModulesLost; }
        }

        int visited = Enumerable.Range(0, net.SystemCount).Count(net.IsSystemVisited);
        if (visited > _visited) { Add("Jump", $"visited {visited} of {net.SystemCount} systems"); _visited = visited; }
        else if (visited > 0) _visited = visited;

        if (net.GetPlayerInfo(player).HoldTotal >= PlayerRules.HoldCapacity) _holdFullTicks += (int)Math.Max(0, net.Tick - _lastTick);
        _lastTick = net.Tick;

        _entries.AddRange(fresh);
        return fresh;
    }

    /// <summary>Adds an entry of the caller's own (for example a hint that was shown), stamped with the current time.</summary>
    public PlayLogEntry Note(BeltNetwork net, string kind, string text)
    {
        var e = new PlayLogEntry(net.Tick / (double)Units.TicksPerSecond, kind, text);
        _entries.Add(e);
        return e;
    }

    /// <summary>One line, for appending to a file as it happens.</summary>
    public static string Line(PlayLogEntry e) => $"{Format(e.Seconds)}  {e.Kind,-10} {e.Text}";

    /// <summary>The whole log as text, with a one-line summary at the end.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var e in _entries) sb.AppendLine(Line(e));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "-- {0} steps done, {1} research finished, hold full for {2:F0} s", _seenObjectives.Count,
            _levels.Count(l => l > 0), HoldFullSeconds));
        return sb.ToString();
    }

    /// <summary>m:ss, like the balance simulator's tables.</summary>
    public static string Format(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
}
