namespace Starfoundry.Ui;

/// <summary>The observable facts the checkpoint watcher compares from one moment to the next. Client-side only: nothing here is part of the simulation state.</summary>
public readonly record struct CheckpointSample(bool Docked, bool WaveWarning, int System, int Stations, int Researched);

/// <summary>
/// Decides when the game takes an automatic checkpoint (ADR 0047). A checkpoint is a normal save written without the player asking, at a moment worth
/// coming back to: <b>a wave about to arrive</b> (its warning starts), <b>a station founded</b>, <b>a research finished</b>, <b>docking</b>, <b>right before
/// a jump</b> (asked for explicitly), and <b>every five minutes</b> when nothing else happened. Events never write more often than <see cref="MinGapSeconds"/>:
/// an event that comes sooner waits (the highest-priority one of the burst) and is written when the gap is over, or dropped after <see cref="PendingSeconds"/>.
/// The first sample after a load or a new game only sets the baseline, so loading never writes a checkpoint of what was just loaded.
/// </summary>
public sealed class CheckpointWatcher
{
    public const double TimerSeconds = 300, MinGapSeconds = 30, PendingSeconds = 90, JumpGapSeconds = 8;

    private bool _hasBaseline;
    private CheckpointSample _last;
    private double _lastWrite = double.NegativeInfinity;
    private string? _pending;
    private double _pendingSince;

    /// <summary>Forgets the baseline (a game was loaded or started): the next sample is only remembered. The timer starts at <paramref name="now"/>.</summary>
    public void Reset(double now)
    {
        _hasBaseline = false;
        _pending = null;
        _lastWrite = now;
    }

    /// <summary>Tells the watcher that a save was written (by the player too): the timer and the gap restart.</summary>
    public void Wrote(double now) => _lastWrite = now;

    /// <summary>The reason to write a checkpoint now (one of <see cref="SaveReasons"/>), or null. <paramref name="now"/> is real time in seconds, monotonic.</summary>
    public string? Observe(CheckpointSample s, double now)
    {
        if (!_hasBaseline) { _hasBaseline = true; _last = s; return null; }
        string? event_ = null;
        if (s.WaveWarning && !_last.WaveWarning) event_ = SaveReasons.Wave;
        else if (s.Stations > _last.Stations) event_ = SaveReasons.Station;
        else if (s.Researched > _last.Researched) event_ = SaveReasons.Research;
        else if (s.Docked && !_last.Docked) event_ = SaveReasons.Dock;
        _last = s;
        if (event_ is not null && (_pending is null || Priority(event_) > Priority(_pending))) { _pending = event_; _pendingSince = now; }
        if (_pending is not null && now - _pendingSince > PendingSeconds) _pending = null;
        if (_pending is not null && now - _lastWrite >= MinGapSeconds)
        {
            string reason = _pending;
            _pending = null;
            _lastWrite = now;
            return reason;
        }
        if (_pending is null && now - _lastWrite >= TimerSeconds)
        {
            _lastWrite = now;
            return SaveReasons.Timer;
        }
        return null;
    }

    /// <summary>An explicit checkpoint (before a jump): allowed unless one was written a moment ago. Returns whether to write it.</summary>
    public bool Request(double now)
    {
        if (now - _lastWrite < JumpGapSeconds) return false;
        _lastWrite = now;
        return true;
    }

    private static int Priority(string reason) => reason switch
    {
        SaveReasons.Wave => 4,
        SaveReasons.Station => 3,
        SaveReasons.Research => 2,
        _ => 1,
    };
}
