using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// Between the client's free-turning float quaternion (<see cref="FlightOrientation"/>) and the simulation's fixed-point <see cref="Attitude"/> (ADR 0030).
/// The client quantises what the pilot has turned to and sends it as <see cref="SetAttitude"/>; the ship is drawn from the same quantised value once it is
/// sent, so what is drawn is what collides. Engine-free.
/// </summary>
public static class ShipAttitude
{
    /// <summary>Rounds a quaternion to the simulation's fixed point: normalised, in the canonical sign, and always a valid attitude.</summary>
    public static Attitude Quantize(Quaternion q)
    {
        q = FlightOrientation.Normalize(q);
        if (q.W < 0f) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        int s = AttitudeRules.Scale;
        int w = (int)MathF.Round(q.W * s), x = (int)MathF.Round(q.X * s), y = (int)MathF.Round(q.Y * s), z = (int)MathF.Round(q.Z * s);
        return Attitude.TryCreate(w, x, y, z, out var a) ? a : Attitude.Identity;
    }

    /// <summary>The unit quaternion of a simulation attitude.</summary>
    public static Quaternion ToQuaternion(Attitude a) =>
        a.IsIdentity ? Quaternion.Identity : FlightOrientation.Normalize(new Quaternion((float)a.X / AttitudeRules.Scale, (float)a.Y / AttitudeRules.Scale, (float)a.Z / AttitudeRules.Scale, (float)a.W / AttitudeRules.Scale));

    /// <summary>The command that sets this attitude.</summary>
    public static SetAttitude ToCommand(Attitude a) => new(a.W, a.X, a.Y, a.Z);

    /// <summary>The angle in radians between two attitudes (0 to pi).</summary>
    public static float AngleBetween(Attitude a, Attitude b)
    {
        double na = Math.Sqrt((double)a.W * a.W + (double)a.X * a.X + (double)a.Y * a.Y + (double)a.Z * a.Z), nb = Math.Sqrt((double)b.W * b.W + (double)b.X * b.X + (double)b.Y * b.Y + (double)b.Z * b.Z);
        double dot = ((double)a.W * b.W + (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z) / (na * nb); // the stored quaternions are unit within a tolerance, not exactly
        return (float)(2.0 * Math.Acos(Math.Clamp(Math.Abs(dot), 0.0, 1.0)));
    }
}

/// <summary>
/// Decides when the client tells the simulation its attitude: every logged command is replayed by saves, so a mouse that turns the ship continuously
/// must not fill the log. A change of at least <see cref="MinAngle"/> is sent at most every <see cref="MinInterval"/> seconds; a smaller change that
/// has waited <see cref="SettleInterval"/> is sent too, so the simulation always ends up with the exact quantised attitude the pilot stopped at.
/// </summary>
public sealed class AttitudeThrottle
{
    public const float MinAngle = 0.004f; // radians, about a quarter of a degree
    public const double MinInterval = 0.1;
    public const double SettleInterval = 0.3;

    private double _lastSent = double.NegativeInfinity;

    /// <summary>Forgets the last send (a new ship, a jump or a load): the next different attitude goes out at once.</summary>
    public void Reset() => _lastSent = double.NegativeInfinity;

    /// <summary>The command to send now, or null. <paramref name="known"/> is what the simulation has; <paramref name="wanted"/> is the quantised attitude of the pilot.</summary>
    public SetAttitude? Offer(Attitude known, Attitude wanted, double now)
    {
        if (known == wanted) return null;
        double since = now - _lastSent;
        bool big = ShipAttitude.AngleBetween(known, wanted) >= MinAngle;
        if (!((big && since >= MinInterval) || since >= SettleInterval)) return null;
        _lastSent = now;
        return ShipAttitude.ToCommand(wanted);
    }
}
