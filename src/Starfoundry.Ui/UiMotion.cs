namespace Starfoundry.Ui;

/// <summary>Engine-free helpers of the interface's motion: durations, easing curves, frame-rate independent smoothing and the small trackers behind the HUD numbers.</summary>
public static class UiMotion
{
    /// <summary>Hover and press feedback.</summary>
    public const float Fast = 0.10f;
    /// <summary>A control changing look, a card fading.</summary>
    public const float Normal = 0.13f;
    /// <summary>A panel opening or closing.</summary>
    public const float Slow = 0.16f;

    /// <summary>The share of an animation done after <paramref name="elapsed"/> seconds of <paramref name="duration"/>, 0 to 1 (1 for a zero duration).</summary>
    public static float Progress(float elapsed, float duration) => duration <= 0f ? 1f : Math.Clamp(elapsed / duration, 0f, 1f);

    /// <summary>Starts fast and settles (the default for things appearing).</summary>
    public static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float u = 1f - t;
        return 1f - u * u * u;
    }

    /// <summary>Slow at both ends (a slide that begins and ends at rest).</summary>
    public static float EaseInOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    public static float Lerp(float from, float to, float t) => from + (to - from) * t;

    /// <summary>
    /// Moves <paramref name="current"/> toward <paramref name="target"/> by an exponential approach: <paramref name="rate"/> is per second, the
    /// result does not depend on the frame rate and never overshoots.
    /// </summary>
    public static float Approach(float current, float target, float dt, float rate)
    {
        if (dt <= 0f || rate <= 0f) return current;
        return target + (current - target) * MathF.Exp(-rate * dt);
    }
}

/// <summary>A number shown on screen that eases toward its value instead of jumping (the hold total, a gauge). Snaps when it is close, or when asked to.</summary>
public sealed class ValueTween
{
    private float _target;
    private bool _started;

    /// <summary>The value to draw now.</summary>
    public float Current { get; private set; }

    public float Target => _target;

    public bool Settled => MathF.Abs(Current - _target) < 1e-4f;

    /// <summary>Sets the value to reach. The first call, and any call with <paramref name="instant"/>, shows it at once.</summary>
    public void Set(float target, bool instant = false)
    {
        _target = target;
        if (!_started || instant) { Current = target; _started = true; }
    }

    /// <summary>Advances by <paramref name="dt"/> seconds; <paramref name="rate"/> is the approach rate per second (a value of 12 takes about a quarter of a second to settle). Returns true while it moved.</summary>
    public bool Step(float dt, float rate = 12f, float snap = 0.0005f)
    {
        if (Current == _target) return false;
        Current = UiMotion.Approach(Current, _target, dt, rate);
        if (MathF.Abs(Current - _target) <= snap) Current = _target;
        return true;
    }
}

/// <summary>A whole number that counts toward its value (a hold total or a count): it moves at least one step each update so it never stalls, and snaps when the gap is one.</summary>
public sealed class CounterTween
{
    private int _target;
    private float _shown;
    private bool _started;

    public int Value => (int)MathF.Round(_shown);

    public int Target => _target;

    public void Set(int target, bool instant = false)
    {
        _target = target;
        if (!_started || instant) { _shown = target; _started = true; }
    }

    /// <summary>Advances the count; true while the shown value changed.</summary>
    public bool Step(float dt, float rate = 14f)
    {
        if (Value == _target) { _shown = _target; return false; }
        _shown = UiMotion.Approach(_shown, _target, dt, rate);
        if (MathF.Abs(_shown - _target) < 0.6f) _shown = _target;
        return true;
    }
}

/// <summary>Lets a heavy refresh run at most <c>hz</c> times a second, unless the caller says a value changed (then it runs now).</summary>
public sealed class RefreshGate
{
    private readonly float _interval;
    private float _left;

    public RefreshGate(float hz = 15f) => _interval = hz <= 0f ? 0f : 1f / hz;

    /// <summary>True when the refresh should run this frame. The first call is always due.</summary>
    public bool Due(float dt, bool force = false)
    {
        _left -= dt;
        if (force || _left <= 0f)
        {
            _left = force ? _interval : Math.Max(_left + _interval, 0f); // keep the remainder so 60 frames a second still gives 15 refreshes
            return true;
        }
        return false;
    }

    /// <summary>Makes the next call due (a menu opened, the level changed).</summary>
    public void Invalidate() => _left = 0f;
}

/// <summary>
/// The income of the hold per minute and a forecast of when it is full, from the totals seen over time. Only increases count (unloading is not
/// income) and the rate needs a few seconds of watching before it says anything.
/// </summary>
public sealed class IncomeTracker
{
    public const float WindowSeconds = 30f;
    public const float MinWatchSeconds = 4f;

    private readonly List<(float Time, int Gain)> _gains = new();
    private float _now, _since;
    private int _last = -1;

    /// <summary>Feeds the hold total at time <paramref name="seconds"/> (any origin, increasing).</summary>
    public void Add(float seconds, int total)
    {
        if (_last < 0) _since = seconds;
        else if (total > _last) _gains.Add((seconds, total - _last));
        _last = total;
        _now = seconds;
        _gains.RemoveAll(g => g.Time < seconds - WindowSeconds);
    }

    /// <summary>Forgets everything (a new game, a load).</summary>
    public void Reset()
    {
        _gains.Clear();
        _last = -1;
    }

    /// <summary>Items gained per minute over the window; 0 until it has watched for <see cref="MinWatchSeconds"/>.</summary>
    public float PerMinute
    {
        get
        {
            float watched = Math.Min(WindowSeconds, _now - _since);
            if (_last < 0 || watched < MinWatchSeconds) return 0f;
            int sum = 0;
            foreach (var g in _gains) sum += g.Gain;
            return sum * 60f / watched;
        }
    }

    /// <summary>Seconds until a hold of <paramref name="capacity"/> is full at the current rate; -1 when it is not growing or is already full.</summary>
    public float SecondsToFull(int used, int capacity)
    {
        float rate = PerMinute;
        if (rate < 0.5f || used >= capacity) return -1f;
        return (capacity - used) * 60f / rate;
    }
}

/// <summary>How fast a distance is closing (positive) or opening (negative), smoothed over a fraction of a second.</summary>
public sealed class ClosingSpeed
{
    private float _last = float.NaN;
    private float _speed;

    public float Value => _speed;

    /// <summary>Feeds the distance now; <paramref name="dt"/> is the time since the last call. A jump (a new target) resets it.</summary>
    public float Update(float distance, float dt, float resetJump = 50f)
    {
        if (float.IsNaN(_last) || dt <= 0f || MathF.Abs(distance - _last) > resetJump) _speed = 0f;
        else _speed = UiMotion.Approach(_speed, (_last - distance) / dt, dt, 5f);
        _last = distance;
        return _speed;
    }

    public void Reset()
    {
        _last = float.NaN;
        _speed = 0f;
    }
}
