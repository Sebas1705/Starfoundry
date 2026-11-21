namespace Starfoundry.Ui;

/// <summary>When audio must stay silent: scripted runs (demos, screenshots, benchmarks, the stress test) never play sound.</summary>
public static class AudioFlags
{
    public static bool IsSilent(IEnumerable<string> userArgs)
    {
        foreach (string raw in userArgs)
        {
            string a = raw.Trim();
            if (a is "--demo" or "--explore-demo" or "--threat-demo" or "--station-demo" or "--structure-demo" or "--bench" or "--save-test" or "--commands") return true;
            if (a.StartsWith("--shot", StringComparison.Ordinal) || a.StartsWith("--stress", StringComparison.Ordinal)) return true;
            if (a.StartsWith("--", StringComparison.Ordinal)) // "--hud-shot=path", "--menu-shot=path", "--defence-shot", ...
            {
                int eq = a.IndexOf('=');
                string name = eq < 0 ? a : a[..eq];
                if (name.EndsWith("-shot", StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }
}

/// <summary>The least time between two plays of the same sound id, so an alarm or a burst of events never turns into a wall of noise.</summary>
public sealed class RateLimiter
{
    private readonly double[] _last;

    public RateLimiter(int ids)
    {
        _last = new double[ids];
        Array.Fill(_last, double.NegativeInfinity);
    }

    /// <summary>True (and records the time) when at least <paramref name="minInterval"/> seconds passed since the last allowed play of <paramref name="id"/>.</summary>
    public bool Allow(int id, double now, double minInterval)
    {
        if ((uint)id >= (uint)_last.Length) return false;
        if (now - _last[id] < minInterval) return false;
        _last[id] = now;
        return true;
    }
}

/// <summary>The engine-free mixing rules: how ship state turns into loop volume and pitch, the power stutter, the music crossfade.</summary>
public static class AudioMix
{
    public const float Silent = -80f;

    public static float ToDb(float linear) => linear <= 0.0001f ? Silent : MathF.Max(Silent, 20f * MathF.Log10(linear));

    /// <summary>The bus volume of a 0..100 slider (linear amplitude; 0 is silent).</summary>
    public static float PercentToDb(int percent) => percent <= 0 ? Silent : ToDb(Math.Clamp(percent, 0, 100) / 100f);

    /// <summary>Engine loop pitch scale: 0.75 at rest, up to 1.65 at full speed.</summary>
    public static float EnginePitch(float speed01) => 0.75f + 0.9f * Math.Clamp(speed01, 0f, 1f);

    /// <summary>Engine loop volume: silent when the ship is still, louder with thrust and speed.</summary>
    public static float EngineDb(float thrust01, float speed01)
    {
        float i = Math.Clamp(0.65f * Math.Clamp(thrust01, 0f, 1f) + 0.35f * Math.Clamp(speed01, 0f, 1f), 0f, 1f);
        return i < 0.02f ? Silent : -34f + 24f * MathF.Pow(i, 0.7f);
    }

    /// <summary>Reactor hum pitch: lower when the power is strained.</summary>
    public static float ReactorPitch(float satisfaction01) => 0.7f + 0.35f * Math.Clamp(satisfaction01, 0f, 1f);

    /// <summary>
    /// Reactor hum volume. Silent without a burning reactor; steady at half power or more; below that it stutters: the hum drops out in
    /// short random slots (about nine a second), more often the lower the power. Deterministic in <paramref name="seconds"/>.
    /// </summary>
    public static float ReactorDb(float satisfaction01, bool burning, double seconds)
    {
        if (!burning) return Silent;
        float sat = Math.Clamp(satisfaction01, 0f, 1f);
        float db = -24f + 10f * sat;
        if (sat >= 0.5f) return db;
        long slot = (long)Math.Floor(seconds * 9.0);
        float chance = Math.Min(0.9f, (0.5f - sat) * 1.8f);
        return Hash01(slot) < chance ? db - 26f : db;
    }

    /// <summary>A stable pseudo-random number in [0, 1) for an integer.</summary>
    public static float Hash01(long n)
    {
        ulong x = (ulong)n * 0x9E3779B97F4A7C15UL;
        x ^= x >> 29; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 32;
        return (x >> 40) / 16777216f;
    }

    /// <summary>Moves <paramref name="current"/> towards <paramref name="target"/> at <paramref name="rate"/> per second, never overshooting.</summary>
    public static float Approach(float current, float target, float rate, float dt)
    {
        float step = rate * dt;
        return current < target ? MathF.Min(target, current + step) : MathF.Max(target, current - step);
    }

    /// <summary>The equal-power crossfade of the two music variants for a tension of 0 (calm) to 1 (tense).</summary>
    public static (float CalmDb, float TenseDb) MusicCrossfade(float tension01)
    {
        float t = Math.Clamp(tension01, 0f, 1f);
        return (ToDb(MathF.Cos(t * MathF.PI / 2f)), ToDb(MathF.Sin(t * MathF.PI / 2f)));
    }
}

/// <summary>Keeps the N smallest distances offered, without allocating: used to pick the nearest working machines that get a hum.</summary>
public sealed class NearestSet
{
    private readonly int[] _ids;
    private readonly float[] _d2;

    public NearestSet(int capacity)
    {
        _ids = new int[capacity];
        _d2 = new float[capacity];
    }

    public int Count { get; private set; }

    public int Capacity => _ids.Length;

    public int Id(int i) => _ids[i];

    public float DistanceSquared(int i) => _d2[i];

    public void Clear() => Count = 0;

    public bool Contains(int id)
    {
        for (int i = 0; i < Count; i++) if (_ids[i] == id) return true;
        return false;
    }

    /// <summary>Offers an item; it is kept when the set has room or it is closer than the farthest kept one. Entries stay sorted, nearest first.</summary>
    public void Offer(int id, float distanceSquared)
    {
        int pos = Count;
        if (Count == _ids.Length)
        {
            if (distanceSquared >= _d2[Count - 1]) return;
            pos = Count - 1;
        }
        else Count++;
        while (pos > 0 && _d2[pos - 1] > distanceSquared)
        {
            _ids[pos] = _ids[pos - 1];
            _d2[pos] = _d2[pos - 1];
            pos--;
        }
        _ids[pos] = id;
        _d2[pos] = distanceSquared;
    }
}
