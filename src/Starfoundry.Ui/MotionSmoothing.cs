using System.Numerics;

namespace Starfoundry.Ui;

/// <summary>
/// Client-side easing for the camera and the body: none of it reaches the simulation. Frame-rate independent (every step uses
/// the elapsed time), engine-free and covered by tests.
/// </summary>
public static class MotionSmoothing
{
    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> exponentially: about 63 percent of the way after <c>1 / rate</c> seconds.</summary>
    public static float Damp(float current, float target, float rate, float dt)
    {
        if (rate <= 0f || dt <= 0f) return current;
        return current + (target - current) * (1f - MathF.Exp(-rate * dt));
    }

    /// <summary>Like <see cref="Damp"/> for an angle in radians: takes the short way round.</summary>
    public static float DampAngle(float current, float target, float rate, float dt)
    {
        float diff = MathF.IEEERemainder(target - current, MathF.Tau);
        return current + diff * (1f - MathF.Exp(-Math.Max(0f, rate) * Math.Max(0f, dt)));
    }

    /// <summary>
    /// Moves a velocity toward a wanted one with a limited acceleration: <paramref name="accel"/> (cells per second squared) while speeding up or
    /// turning, <paramref name="decel"/> while slowing down. Reaches the target exactly, never overshoots.
    /// </summary>
    public static Vector3 Approach(Vector3 current, Vector3 target, float accel, float decel, float dt)
    {
        if (dt <= 0f) return current;
        var delta = target - current;
        float dist = delta.Length();
        if (dist < 1e-6f) return target;
        bool slowing = target.LengthSquared() < current.LengthSquared() - 1e-9f;
        float step = (slowing ? decel : accel) * dt;
        return step >= dist ? target : current + delta / dist * step;
    }

    /// <summary>The smoothstep curve: 0 at 0, 1 at 1, flat at both ends.</summary>
    public static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// The eye glides when the pilot sits down or stands up: when the wanted eye position jumps, the old position is kept as an offset that
/// decays, so the camera slides to the new place instead of cutting to it.
/// </summary>
public sealed class EyeBlend
{
    private Vector3 _offset;
    private Vector3 _last;
    private bool _hasLast;

    /// <summary>Added to the wanted eye position (zero at rest).</summary>
    public Vector3 Offset => _offset;

    /// <summary>Call once per frame with the wanted eye and whether it just jumped (sat down, stood up). Returns the eased eye.</summary>
    public Vector3 Update(Vector3 wanted, bool jumped, float dt, float rate = 7f)
    {
        if (jumped && _hasLast) _offset += _last - wanted;
        _last = wanted;
        _hasLast = true;
        float k = MathF.Exp(-Math.Max(0f, rate) * Math.Max(0f, dt));
        _offset *= k;
        if (_offset.LengthSquared() < 1e-6f) _offset = Vector3.Zero;
        return wanted + _offset;
    }

    public void Reset() { _offset = Vector3.Zero; _hasLast = false; }
}
