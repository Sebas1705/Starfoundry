using System.Globalization;
using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>What the scripted player was doing with its game time (the telemetry buckets).</summary>
internal enum WaitMode { Other, Acting, Waiting }

/// <summary>One line of the per-minute table of a telemetry run.</summary>
public sealed record MinuteRow(int Minute, string Goal, long HandOre, long DroneOre, int HoldPercentEnd, int HoldFullSeconds,
    int MinPowerPermille, int PowerShortSeconds, int EnemySeconds, int MinShipHpPermille, int Machines, int ActSeconds, int WaitSeconds, int FlySeconds);

/// <summary>
/// Telemetry of a structure-campaign run (simulated playtest study, 2026-11-28): a per-minute table, where the game time went (acting,
/// waiting for items or machines, flying), how long the hold was too full for hand mining, power shortfalls, fights, rejected commands.
/// Collected by <see cref="StructureBot"/> when <see cref="StructureBot.Telemetry"/> is set; costs nothing otherwise.
/// </summary>
public sealed class BotTelemetry
{
    public List<MinuteRow> Minutes { get; } = new();
    public Dictionary<string, int> Rejects { get; } = new();
    public Dictionary<string, (double Act, double Wait, double Other)> Goals { get; } = new();
    public double ActSeconds, WaitSeconds, OtherSeconds;
    public int HoldFullSeconds, PowerShortSeconds, EnemySeconds, AfkSeconds;
    public string CurrentGoal = "";

    internal void Account(WaitMode mode, double seconds)
    {
        var g = Goals.GetValueOrDefault(CurrentGoal);
        switch (mode)
        {
            case WaitMode.Acting: ActSeconds += seconds; g.Act += seconds; break;
            case WaitMode.Waiting: WaitSeconds += seconds; g.Wait += seconds; break;
            default: OtherSeconds += seconds; g.Other += seconds; break;
        }
        Goals[CurrentGoal] = g;
    }

    internal void Reject(string reason) => Rejects[reason] = Rejects.GetValueOrDefault(reason) + 1;

    internal void AddAfk(int seconds) => AfkSeconds += seconds;

    /// <summary>Markdown tables: the per-minute rows, the time budget per goal and the rejected commands.</summary>
    public string Format(double flySeconds, int minutes = int.MaxValue)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| Min | Goal | Hand ore | Drone ore | Hold % | Hold full s | Min power | Power short s | Enemy s | Min ship hp | Machines | Act s | Wait s | Fly s |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in Minutes.Where(m => m.Minute <= minutes))
        {
            string goal = r.Goal.Length > 44 ? r.Goal[..44] : r.Goal;
            sb.AppendLine($"| {r.Minute} | {goal} | {r.HandOre} | {r.DroneOre} | {r.HoldPercentEnd} | {r.HoldFullSeconds} | {r.MinPowerPermille / 10.0:F0}% | {r.PowerShortSeconds} | {r.EnemySeconds} | {r.MinShipHpPermille / 10.0:F0}% | {r.Machines} | {r.ActSeconds} | {r.WaitSeconds} | {r.FlySeconds} |");
        }
        sb.AppendLine();
        double total = ActSeconds + WaitSeconds + OtherSeconds + flySeconds;
        sb.AppendLine(string.Create(inv, $"Time budget: acting {ActSeconds:F0} s, waiting for items or machines {WaitSeconds:F0} s, flying {flySeconds:F0} s, other {OtherSeconds:F0} s (of {total:F0} s). Hold at the mining limit {HoldFullSeconds} s, power short {PowerShortSeconds} s, enemies alive {EnemySeconds} s, away from keyboard {AfkSeconds} s."));
        sb.AppendLine();
        sb.AppendLine("| Goal | Acting s | Waiting s | Other s |");
        sb.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var (name, v) in Goals.OrderByDescending(kv => kv.Value.Wait)) sb.AppendLine(string.Create(inv, $"| {name} | {v.Act:F0} | {v.Wait:F0} | {v.Other:F0} |"));
        if (Rejects.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Rejected commands: " + string.Join(", ", Rejects.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}")));
        }
        return sb.ToString();
    }
}

public sealed partial class StructureBot
{
    /// <summary>When set, the run fills this collector (per-minute table, time budget, rejections).</summary>
    public BotTelemetry? Telemetry { get => _telemetry; init => _telemetry = value; }
    private BotTelemetry? _telemetry;
    private WaitMode _mode = WaitMode.Other;

    /// <summary>A player who never builds turrets (fuzzing: is the loss fair, is there a way out).</summary>
    public bool NoTurrets { get; init; }

    /// <summary>A player who walks away for <see cref="AfkSeconds"/> every <see cref="AfkEverySeconds"/> (no plan step, no maintenance).</summary>
    public int AfkSeconds { get; init; }
    public int AfkEverySeconds { get; init; } = 600;
    private double _nextAfk = -1;
    private bool _afkActive;

    private bool AfkNow() => _afkActive;

    private void MaybeAfk()
    {
        if (AfkSeconds <= 0 || _afkActive) return;
        if (_nextAfk < 0) _nextAfk = AfkEverySeconds;
        if (Now < _nextAfk) return;
        _nextAfk = Now + AfkEverySeconds;
        _afkActive = true;
        _telemetry?.AddAfk(AfkSeconds);
        try { Wait(AfkSeconds); }
        finally { _afkActive = false; }
    }

    private int _tMinute = 0, _tHoldFull, _tPowerMin = 1000, _tPowerShort, _tEnemy, _tShipMin = 1000;
    private long _tHand, _tDrone;
    private double _tAct, _tWait, _tFly;

    private void SampleTelemetry()
    {
        var t = _telemetry;
        if (t is null || !Net.HasPlayer(0)) return;
        t.CurrentGoal = _goal.Length > 0 ? _goal : "start";
        int cap = Math.Max(1, Net.HoldCapacity(0));
        bool full = Net.HoldTotal(0) >= cap * PlayerRules.MiningHoldLimitPercent / 100 - 1;
        if (full) { t.HoldFullSeconds++; _tHoldFull++; }
        int sat = 1000;
        bool hasMachines = Net.MachineCount > 0;
        if (hasMachines)
            foreach (var s in Net.Structures())
                if (s.Owner == 0 && s.Kind == StructureKind.Station) sat = Math.Min(sat, Net.PowerStatus(s.Id).SatisfactionPermille);
        if (hasMachines && sat < 1000) { t.PowerShortSeconds++; _tPowerShort++; }
        _tPowerMin = Math.Min(_tPowerMin, sat);
        if (Net.EnemyCount > 0) { t.EnemySeconds++; _tEnemy++; }
        _tShipMin = Math.Min(_tShipMin, Net.StructureHpFraction(_ship));
        if (NowSeconds > 0 && NowSeconds % 60 == 0 && NowSeconds / 60 > _tMinute)
        {
            _tMinute = NowSeconds / 60;
            double act = t.ActSeconds, wait = t.WaitSeconds, fly = FlightSeconds;
            t.Minutes.Add(new MinuteRow(_tMinute, _goal, Net.MinedByHand - _tHand, Net.MinedByDrones - _tDrone, (int)(100L * Net.HoldTotal(0) / cap), _tHoldFull,
                _tPowerMin, _tPowerShort, _tEnemy, _tShipMin, Net.MachineCount, (int)(act - _tAct), (int)(wait - _tWait), (int)(fly - _tFly)));
            _tHand = Net.MinedByHand; _tDrone = Net.MinedByDrones; _tAct = act; _tWait = wait; _tFly = fly;
            _tHoldFull = 0; _tPowerMin = 1000; _tPowerShort = 0; _tEnemy = 0; _tShipMin = 1000;
        }
    }
}
