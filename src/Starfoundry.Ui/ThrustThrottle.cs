using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// Keeps the thrust the game sends to the simulation small enough for the command log. Steering with the mouse changes the thrust
/// vector every frame, and every change is a logged command that a save replays, so the game quantises the vector to steps of
/// <see cref="Step"/> (of 1024) and sends at most <see cref="MaxPerSecond"/> changes a second. A stop is never held back, and
/// the last wanted value is sent as soon as the interval allows (the game offers the wanted thrust every frame). The simulation is
/// untouched: this only decides what the game asks it for.
/// </summary>
public sealed class ThrustThrottle
{
    /// <summary>Granularity of a thrust component: 64/1024 of full thrust, about 1.8 degrees of heading at full speed.</summary>
    public const int Step = 64;

    /// <summary>Most thrust commands per second while the vector keeps changing.</summary>
    public const int MaxPerSecond = 10;

    public const double MinInterval = 1.0 / MaxPerSecond;

    private (int X, int Y, int Z) _last = (0, 0, 0);
    private double _lastAt = double.NegativeInfinity;
    private bool _known = true;

    /// <summary>The thrust last sent (or (0,0,0) at the start).</summary>
    public (int X, int Y, int Z) LastSent => _last;

    /// <summary>How many thrust commands the throttle let through.</summary>
    public int Sent { get; private set; }

    /// <summary>
    /// Rounds each component to the nearest multiple of <see cref="Step"/>. Rounding can make a legal vector a little longer than
    /// 1024, so then every component is truncated towards zero instead (which never lengthens it).
    /// </summary>
    public static (int X, int Y, int Z) Quantize((int X, int Y, int Z) t)
    {
        var r = (Round(t.X), Round(t.Y), Round(t.Z));
        if (PlayerRules.ThrustInRange(r.Item1, r.Item2, r.Item3)) return r;
        var d = (Trunc(t.X), Trunc(t.Y), Trunc(t.Z));
        while (!PlayerRules.ThrustInRange(d.Item1, d.Item2, d.Item3)) d = (d.Item1 * 15 / 16, d.Item2 * 15 / 16, d.Item3 * 15 / 16);
        return d;
    }

    private static int Round(int v) => (int)Math.Round(v / (double)Step, MidpointRounding.AwayFromZero) * Step;

    private static int Trunc(int v) => v / Step * Step;

    /// <summary>Forgets what was sent: the simulation changed the thrust itself (a jump or respawn), so the next offer is sent.</summary>
    public void Invalidate()
    {
        _known = false;
        _lastAt = double.NegativeInfinity;
    }

    /// <summary>True when the simulation's own thrust differs from what was sent (it was cleared by a jump or a respawn).</summary>
    public bool Differs((int X, int Y, int Z) simulation) => _known && simulation != _last;

    /// <summary>
    /// Offers the thrust the player wants at time <paramref name="now"/> (seconds). Returns the quantised thrust to send now, or null
    /// when nothing needs sending: it equals the last one sent, or a change is held back until the interval has passed (offer it again).
    /// <paramref name="force"/> sends a stop at once; any all-zero thrust is treated as a stop.
    /// </summary>
    public (int X, int Y, int Z)? Offer((int X, int Y, int Z) wanted, double now, bool force = false)
    {
        var q = Quantize(wanted);
        if (_known && q == _last) return null;
        bool stop = q == (0, 0, 0);
        if (!stop && !force && now - _lastAt < MinInterval) return null;
        _last = q;
        _lastAt = now;
        _known = true;
        Sent++;
        return q;
    }
}
