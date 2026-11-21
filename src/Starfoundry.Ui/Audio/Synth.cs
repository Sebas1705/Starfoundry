namespace Starfoundry.Ui;

/// <summary>A tiny deterministic random generator (xorshift32), so every rendered sound is identical on every run.</summary>
public sealed class SynthRng
{
    private uint _s;

    public SynthRng(uint seed) => _s = seed == 0 ? 1u : seed;

    /// <summary>A number in [0, 1).</summary>
    public float Next()
    {
        _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
        return (_s >> 8) / 16777216f;
    }

    /// <summary>A number in [-1, 1).</summary>
    public float Signed() => Next() * 2f - 1f;
}

/// <summary>Engine-free synthesis primitives: mono float buffers at <see cref="SampleRate"/>, built from oscillators, noise and one-pole filters.</summary>
public static class Synth
{
    public const int SampleRate = 22050;
    private const double Tau = Math.PI * 2.0;

    public static float[] Buffer(double seconds) => new float[Math.Max(1, (int)Math.Round(seconds * SampleRate))];

    /// <summary>The nearest frequency that fits a whole number of cycles in <paramref name="loopSeconds"/>, so a tone of it loops without a click.</summary>
    public static double Snap(double hz, double loopSeconds) => Math.Max(1.0, Math.Round(hz * loopSeconds)) / loopSeconds;

    /// <summary>
    /// Adds a tone with up to two extra harmonics. The pitch glides exponentially from <paramref name="f0"/> to <paramref name="f1"/> over
    /// <paramref name="dur"/>; the level decays as exp(-decay t) after a short attack. With <paramref name="wrap"/> the tail continues at the
    /// start of the buffer (for loops).
    /// </summary>
    public static void AddTone(float[] b, double start, double dur, double f0, double f1, double amp, double decay,
        double h2 = 0, double h3 = 0, bool wrap = false, double attack = 0.003)
    {
        int i0 = (int)Math.Round(start * SampleRate), n = (int)Math.Round(dur * SampleRate);
        double phase = 0, ratio = f1 / f0;
        for (int k = 0; k < n; k++)
        {
            double t = k / (double)SampleRate;
            double f = Math.Abs(ratio - 1.0) < 1e-9 ? f0 : f0 * Math.Pow(ratio, t / dur);
            phase += Tau * f / SampleRate;
            double env = Math.Exp(-decay * t) * (attack > 0 ? Math.Min(1.0, t / attack) : 1.0);
            double v = Math.Sin(phase) + h2 * Math.Sin(2 * phase) + h3 * Math.Sin(3 * phase);
            int idx = i0 + k;
            if (idx >= b.Length)
            {
                if (!wrap) break;
                idx %= b.Length;
            }
            b[idx] += (float)(amp * env * v);
        }
    }

    /// <summary>Adds a burst of band-limited noise (a high-pass at <paramref name="hpHz"/> and a low-pass at <paramref name="lpHz"/>, both one-pole) with an exponential decay.</summary>
    public static void AddNoise(float[] b, SynthRng rng, double start, double dur, double amp, double decay, double lpHz, double hpHz = 0,
        bool wrap = false, double attack = 0.002)
    {
        int i0 = (int)Math.Round(start * SampleRate), n = (int)Math.Round(dur * SampleRate);
        double a = Coef(lpHz), h = hpHz > 0 ? Coef(hpHz) : 0, lp = 0, hpState = 0;
        for (int k = 0; k < n; k++)
        {
            double t = k / (double)SampleRate;
            double x = rng.Signed();
            lp += a * (x - lp);
            double y = lp;
            if (hpHz > 0) { hpState += h * (y - hpState); y -= hpState; }
            double env = Math.Exp(-decay * t) * (attack > 0 ? Math.Min(1.0, t / attack) : 1.0);
            int idx = i0 + k;
            if (idx >= b.Length)
            {
                if (!wrap) break;
                idx %= b.Length;
            }
            b[idx] += (float)(amp * env * y);
        }
    }

    private static double Coef(double hz) => 1.0 - Math.Exp(-Tau * hz / SampleRate);

    /// <summary>
    /// White noise filtered to a band (low-pass at <paramref name="lpHz"/>, optional high-pass at <paramref name="hpHz"/>) and scaled to a peak of 1.
    /// With <paramref name="cyclic"/> the filters run round the buffer until their state settles, so the end flows into the start.
    /// </summary>
    public static float[] Noise(SynthRng rng, double seconds, double lpHz, double hpHz = 0, bool cyclic = true)
    {
        var src = Buffer(seconds);
        for (int i = 0; i < src.Length; i++) src[i] = rng.Signed();
        var outp = new float[src.Length];
        double a = Coef(lpHz), a2 = Coef(lpHz), h = hpHz > 0 ? Coef(hpHz) : 0;
        double lp1 = 0, lp2 = 0, hs = 0;
        int passes = cyclic ? 4 : 1;
        for (int p = 0; p < passes; p++)
            for (int i = 0; i < src.Length; i++)
            {
                lp1 += a * (src[i] - lp1);
                lp2 += a2 * (lp1 - lp2);
                double y = lp2;
                if (hpHz > 0) { hs += h * (y - hs); y -= hs; }
                outp[i] = (float)y;
            }
        NormalizeTo(outp, 1f);
        return outp;
    }

    /// <summary>Multiplies the buffer by a slow periodic level: 1 - depth + depth * (0.5 + 0.5 sin). The rate is snapped so it loops.</summary>
    public static void Tremolo(float[] b, double hz, double depth, double phase = 0)
    {
        double secs = b.Length / (double)SampleRate, f = Snap(hz, secs);
        for (int i = 0; i < b.Length; i++)
            b[i] *= (float)(1.0 - depth + depth * (0.5 + 0.5 * Math.Sin(Tau * f * i / SampleRate + phase)));
    }

    /// <summary>dst += src * gain (the shorter length wins).</summary>
    public static void Mix(float[] dst, float[] src, double gain)
    {
        int n = Math.Min(dst.Length, src.Length);
        for (int i = 0; i < n; i++) dst[i] += (float)(src[i] * gain);
    }

    public static float Peak(float[] b)
    {
        float p = 0;
        foreach (float v in b) p = MathF.Max(p, MathF.Abs(v));
        return p;
    }

    /// <summary>Scales the buffer so its peak is <paramref name="peak"/>; also guards against NaN or infinity.</summary>
    public static void NormalizeTo(float[] b, float peak)
    {
        for (int i = 0; i < b.Length; i++) if (!float.IsFinite(b[i])) b[i] = 0f;
        float p = Peak(b);
        if (p < 1e-9f) return;
        float g = peak / p;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
    }

    /// <summary>Fades the start and the end to silence so a one-shot never clicks.</summary>
    public static void FadeEdges(float[] b, double inSeconds = 0.0015, double outSeconds = 0.01)
    {
        int fi = Math.Min(b.Length / 2, (int)(inSeconds * SampleRate)), fo = Math.Min(b.Length / 2, (int)(outSeconds * SampleRate));
        for (int i = 0; i < fi; i++) b[i] *= i / (float)fi;
        for (int i = 0; i < fo; i++) b[b.Length - 1 - i] *= i / (float)fo;
    }

    /// <summary>
    /// A reverb-like tail for a looping buffer: four feedback combs and two all-passes, all wrapping round the buffer and run for three
    /// passes so the tail of the end flows into the start. <paramref name="wet"/> is the level of the reverb added to the dry signal.
    /// </summary>
    public static void CyclicReverb(float[] b, double wet, double feedback = 0.9)
    {
        int n = b.Length;
        int[] delays = { (int)(0.0897 * SampleRate), (int)(0.1127 * SampleRate), (int)(0.1371 * SampleRate), (int)(0.1613 * SampleRate) };
        var acc = new float[n];
        foreach (int d in delays)
        {
            var y = new float[n];
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < n; i++)
                {
                    int j = i - d; if (j < 0) j += n;
                    y[i] = b[i] + (float)feedback * y[j];
                }
            float scale = 1f / (1f + (float)feedback * 2f);
            for (int i = 0; i < n; i++) acc[i] += y[i] * scale;
        }
        foreach (int d in new[] { (int)(0.0050 * SampleRate), (int)(0.0017 * SampleRate) })
        {
            var y = new float[n];
            var x = (float[])acc.Clone();
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < n; i++)
                {
                    int j = i - d; if (j < 0) j += n;
                    // Schroeder all-pass: y[n] = -g x[n] + x[n-d] + g y[n-d]
                    y[i] = -0.5f * x[i] + x[j] + 0.5f * y[j];
                }
            acc = y;
        }
        for (int i = 0; i < n; i++) b[i] = b[i] + (float)wet * acc[i];
    }
}

/// <summary>Plain measurements of a rendered buffer, for the tests and the <c>--audio-test</c> report.</summary>
public readonly record struct SoundStats(float Peak, float Rms, double Seconds, float SeamStep, float MaxStep)
{
    public static SoundStats Measure(float[] b)
    {
        double sum = 0;
        float peak = 0, maxStep = 0;
        for (int i = 0; i < b.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(b[i]));
            sum += (double)b[i] * b[i];
            if (i > 0) maxStep = MathF.Max(maxStep, MathF.Abs(b[i] - b[i - 1]));
        }
        float seam = b.Length > 1 ? MathF.Abs(b[0] - b[^1]) : 0f;
        return new SoundStats(peak, b.Length == 0 ? 0f : (float)Math.Sqrt(sum / b.Length), b.Length / (double)Synth.SampleRate, seam, maxStep);
    }

    /// <summary>True when the jump from the last sample to the first is no bigger than the biggest step inside the buffer (plus a small margin).</summary>
    public bool Seamless => SeamStep <= MaxStep * 1.25f + 0.002f;
}

/// <summary>16-bit mono PCM: the raw bytes for an engine audio stream and a complete WAV file (for <c>--audio-test</c>).</summary>
public static class WavFile
{
    public static byte[] Pcm16(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp((int)Math.Round(samples[i] * 32767.0), -32768, 32767);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return data;
    }

    public static byte[] Encode(float[] samples, int sampleRate = Synth.SampleRate)
    {
        byte[] pcm = Pcm16(samples);
        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);    // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }
}
