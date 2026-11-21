namespace Starfoundry.Ui;

/// <summary>
/// The recipes of every sound in <see cref="Sfx"/>: pure, deterministic synthesis into mono float buffers. Loops are built so their
/// end flows into their start (whole cycles per loop for tones, cyclic filters for noise, wrapped tails for events); one-shots fade at
/// both ends. Every buffer is normalised to the <see cref="SoundSpec.Peak"/> of its sound, so none clips.
/// </summary>
public static class SoundBank
{
    public const int SampleRate = Synth.SampleRate;

    /// <summary>Renders one sound. The result is new on every call (callers cache it).</summary>
    public static float[] Render(Sfx id)
    {
        var spec = SoundCatalog.Get(id);
        var rng = new SynthRng(0x5EED0000u + (uint)id * 7919u);
        float[] b = id switch
        {
            Sfx.UiClick => UiClick(),
            Sfx.UiConfirm => UiConfirm(),
            Sfx.UiError => UiError(rng),
            Sfx.ToastPing => ToastPing(),
            Sfx.ToastThreat => ToastThreat(),
            Sfx.ResearchDone => ResearchDone(),
            Sfx.EngineLoop => EngineLoop(rng),
            Sfx.ReactorLoop => ReactorLoop(),
            Sfx.RoomTone => RoomTone(rng),
            Sfx.FuelBeep => FuelBeep(),
            Sfx.MachSmelter => Smelter(rng),
            Sfx.MachAssembler => Assembler(rng),
            Sfx.MachCracker => Cracker(rng),
            Sfx.MachDepot => Depot(rng),
            Sfx.MachHum => Hum(),
            Sfx.MineLoop => MineLoop(rng),
            Sfx.MinePop => MinePop(),
            Sfx.DroneWhoosh => DroneWhoosh(rng),
            Sfx.PlaceThunk => PlaceThunk(rng),
            Sfx.DemolishCrunch => DemolishCrunch(rng),
            Sfx.ModuleSnap => ModuleSnap(rng),
            Sfx.WaveAlarm => WaveAlarm(),
            Sfx.EnemyShot => EnemyShot(rng),
            Sfx.TurretShot => TurretShot(rng),
            Sfx.ModuleHit => ModuleHit(rng),
            Sfx.Explosion => Explosion(rng),
            Sfx.HullRumble => HullRumble(rng),
            Sfx.MusicCalm => Music(rng, tense: false),
            Sfx.MusicTense => Music(rng, tense: true),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        if (!spec.Loop) Synth.FadeEdges(b);
        Synth.NormalizeTo(b, spec.Peak);
        return b;
    }

    // ---- UI ----------------------------------------------------------------------------------------

    private static float[] UiClick()
    {
        var b = Synth.Buffer(0.06);
        Synth.AddTone(b, 0, 0.06, 2200, 1500, 1.0, 70, h2: 0.2);
        return b;
    }

    private static float[] UiConfirm()
    {
        var b = Synth.Buffer(0.26);
        Synth.AddTone(b, 0, 0.14, 660, 660, 0.8, 14, h2: 0.25);
        Synth.AddTone(b, 0.08, 0.18, 990, 990, 0.8, 14, h2: 0.25);
        return b;
    }

    private static float[] UiError(SynthRng rng)
    {
        var b = Synth.Buffer(0.3);
        Synth.AddTone(b, 0, 0.12, 220, 200, 0.9, 10, h2: 0.6, h3: 0.4);
        Synth.AddTone(b, 0.14, 0.16, 170, 150, 0.9, 9, h2: 0.6, h3: 0.4);
        Synth.AddNoise(b, rng, 0, 0.05, 0.1, 40, 3000);
        return b;
    }

    private static float[] ToastPing()
    {
        var b = Synth.Buffer(0.4);
        Synth.AddTone(b, 0, 0.4, 1320, 1320, 0.9, 9, h2: 0.3);
        Synth.AddTone(b, 0, 0.4, 1980, 1980, 0.25, 12);
        return b;
    }

    private static float[] ToastThreat()
    {
        var b = Synth.Buffer(0.5);
        Synth.AddTone(b, 0, 0.22, 880, 880, 0.8, 10, h2: 0.5, h3: 0.3);
        Synth.AddTone(b, 0.2, 0.3, 622, 622, 0.9, 8, h2: 0.5, h3: 0.3);
        return b;
    }

    private static float[] ResearchDone()
    {
        var b = Synth.Buffer(1.5);
        double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
        for (int i = 0; i < notes.Length; i++)
        {
            Synth.AddTone(b, 0.11 * i, 0.9, notes[i], notes[i], 0.6, 4.5, h2: 0.3, h3: 0.1);
            Synth.AddTone(b, 0.11 * i + 0.17, 0.7, notes[i], notes[i], 0.2, 5.5, h2: 0.3); // a soft echo
        }
        // A last, held chord.
        foreach (double f in new[] { 523.25, 659.25, 783.99 })
            Synth.AddTone(b, 0.5, 1.0, f, f, 0.35, 3.2, h2: 0.2);
        return b;
    }

    // ---- ship ambience -------------------------------------------------------------------------------

    private static float[] EngineLoop(SynthRng rng)
    {
        const double L = 2.0;
        var b = Synth.Buffer(L);
        Synth.Mix(b, Synth.Noise(rng, L, 320), 0.9);
        double f = Synth.Snap(55, L);
        Synth.AddTone(b, 0, L, f, f, 0.55, 0, h2: 0.6, h3: 0.35, attack: 0);
        Synth.AddTone(b, 0, L, f * 2.5, f * 2.5, 0.15, 0, attack: 0);
        Synth.Tremolo(b, 6, 0.18);
        return b;
    }

    private static float[] ReactorLoop()
    {
        const double L = 2.0;
        var b = Synth.Buffer(L);
        foreach (var (hz, amp) in new[] { (60.0, 0.7), (120.0, 0.4), (181.0, 0.2), (302.0, 0.08) })
        {
            double f = Synth.Snap(hz, L);
            Synth.AddTone(b, 0, L, f, f, amp, 0, attack: 0);
        }
        Synth.Tremolo(b, 1, 0.25);
        return b;
    }

    private static float[] RoomTone(SynthRng rng)
    {
        const double L = 4.0;
        var b = Synth.Noise(rng, L, 420, 60);
        for (int i = 0; i < b.Length; i++) b[i] *= 0.7f;
        double f = Synth.Snap(48, L);
        Synth.AddTone(b, 0, L, f, f, 0.3, 0, attack: 0);
        Synth.Tremolo(b, 0.25, 0.2);
        return b;
    }

    private static float[] FuelBeep()
    {
        var b = Synth.Buffer(0.42);
        Synth.AddTone(b, 0, 0.14, 1000, 1000, 0.8, 5, h3: 0.35);
        Synth.AddTone(b, 0.22, 0.14, 1000, 1000, 0.8, 5, h3: 0.35);
        return b;
    }

    // ---- machines ---------------------------------------------------------------------------------

    private static float[] Smelter(SynthRng rng)
    {
        const double L = 2.0;
        var b = Synth.Buffer(L);
        Synth.Mix(b, Synth.Noise(rng, L, 200), 1.0);
        double f = Synth.Snap(44, L);
        Synth.AddTone(b, 0, L, f, f, 0.45, 0, h2: 0.4, attack: 0);
        for (int i = 0; i < 9; i++) // sparse crackle of the furnace
            Synth.AddNoise(b, rng, rng.Next() * L, 0.03, 0.35, 90, 3500, 700, wrap: true);
        Synth.Tremolo(b, 2, 0.15);
        return b;
    }

    private static float[] Assembler(SynthRng rng)
    {
        const double L = 1.0;
        var b = Synth.Buffer(L);
        for (int i = 0; i < 4; i++) // tick-tick ... tick-tick
        {
            double t = i < 2 ? 0.0 + 0.12 * (i % 2) : 0.5 + 0.12 * (i % 2);
            Synth.AddTone(b, t, 0.04, 2400, 1900, 0.6, 90, wrap: true);
            Synth.AddNoise(b, rng, t, 0.015, 0.25, 150, 5000, 1500, wrap: true);
        }
        double f = Synth.Snap(90, L);
        Synth.AddTone(b, 0, L, f, f, 0.08, 0, attack: 0);
        return b;
    }

    private static float[] Cracker(SynthRng rng)
    {
        const double L = 2.0;
        var b = Synth.Noise(rng, L, 7000, 2500);
        Synth.Tremolo(b, 1, 0.35);
        double f = Synth.Snap(310, L);
        Synth.AddTone(b, 0, L, f, f, 0.06, 0, attack: 0);
        return b;
    }

    private static float[] Depot(SynthRng rng)
    {
        const double L = 1.2;
        var b = Synth.Buffer(L);
        foreach (double t in new[] { 0.0, 0.6 })
        {
            Synth.AddTone(b, t, 0.05, 1500, 900, 0.7, 60, wrap: true);
            Synth.AddNoise(b, rng, t, 0.02, 0.2, 120, 4000, 800, wrap: true);
        }
        double f = Synth.Snap(70, L);
        Synth.AddTone(b, 0, L, f, f, 0.12, 0, h2: 0.3, attack: 0);
        return b;
    }

    private static float[] Hum()
    {
        const double L = 2.0;
        var b = Synth.Buffer(L);
        double f = Synth.Snap(100, L);
        Synth.AddTone(b, 0, L, f, f, 0.6, 0, h2: 0.3, attack: 0);
        double g = Synth.Snap(200.5, L);
        Synth.AddTone(b, 0, L, g, g, 0.15, 0, attack: 0);
        Synth.Tremolo(b, 3, 0.12);
        return b;
    }

    // ---- mining -----------------------------------------------------------------------------------

    private static float[] MineLoop(SynthRng rng)
    {
        const double L = 0.5;
        var b = Synth.Buffer(L);
        double f = Synth.Snap(220, L);
        Synth.AddTone(b, 0, L, f, f, 0.6, 0, h2: 0.7, h3: 0.5, attack: 0);
        Synth.Mix(b, Synth.Noise(rng, L, 3500, 800), 0.25);
        Synth.Tremolo(b, 40, 0.5);
        return b;
    }

    private static float[] MinePop()
    {
        var b = Synth.Buffer(0.14);
        Synth.AddTone(b, 0, 0.14, 950, 320, 1.0, 28, h2: 0.2);
        return b;
    }

    private static float[] DroneWhoosh(SynthRng rng)
    {
        const double L = 0.9;
        var b = Synth.Buffer(L);
        var n = Synth.Noise(rng, L, 3200, 350, cyclic: false);
        // A sweep: bell-shaped level, brighter in the middle (a faster-moving filter is faked by blending two bands).
        var low = Synth.Noise(rng, L, 700, 150, cyclic: false);
        for (int i = 0; i < b.Length; i++)
        {
            double t = i / (double)b.Length;
            double bell = Math.Sin(Math.PI * t);
            bell *= bell;
            double bright = Math.Sin(Math.PI * Math.Min(1.0, t * 1.3));
            b[i] = (float)(bell * (n[i] * bright + low[i] * (1 - bright) * 0.8));
        }
        return b;
    }

    // ---- building ---------------------------------------------------------------------------------

    private static float[] PlaceThunk(SynthRng rng)
    {
        var b = Synth.Buffer(0.3);
        Synth.AddTone(b, 0, 0.3, 120, 48, 1.0, 14, h2: 0.3);
        Synth.AddNoise(b, rng, 0, 0.08, 0.5, 40, 900);
        Synth.AddTone(b, 0, 0.03, 1500, 800, 0.25, 90);
        return b;
    }

    private static float[] DemolishCrunch(SynthRng rng)
    {
        var b = Synth.Buffer(0.45);
        for (int i = 0; i < 6; i++)
            Synth.AddNoise(b, rng, 0.03 * i + 0.02 * rng.Next(), 0.07, 0.6 - 0.07 * i, 30, 4500, 400);
        Synth.AddTone(b, 0, 0.35, 90, 40, 0.8, 9, h2: 0.4);
        return b;
    }

    private static float[] ModuleSnap(SynthRng rng)
    {
        var b = Synth.Buffer(0.24);
        Synth.AddTone(b, 0, 0.12, 1250, 1250, 0.7, 40, h2: 0.5, h3: 0.2);
        Synth.AddTone(b, 0.07, 0.15, 2500, 2400, 0.55, 45, h2: 0.2);
        Synth.AddTone(b, 0.07, 0.15, 110, 70, 0.6, 22);
        Synth.AddNoise(b, rng, 0, 0.01, 0.2, 200, 6000, 1500);
        return b;
    }

    // ---- threat -----------------------------------------------------------------------------------

    private static float[] WaveAlarm()
    {
        var b = Synth.Buffer(1.8);
        for (int i = 0; i < 4; i++)
        {
            double f = i % 2 == 0 ? 466.0 : 349.0;
            Synth.AddTone(b, 0.42 * i, 0.44, f, f, 0.8, 2.5, h2: 0.55, h3: 0.35, attack: 0.02);
        }
        return b;
    }

    private static float[] EnemyShot(SynthRng rng)
    {
        var b = Synth.Buffer(0.32);
        Synth.AddTone(b, 0, 0.3, 1300, 260, 0.9, 11, h2: 0.7, h3: 0.5);
        Synth.AddNoise(b, rng, 0, 0.06, 0.2, 40, 6000, 1500);
        return b;
    }

    private static float[] TurretShot(SynthRng rng)
    {
        var b = Synth.Buffer(0.26);
        Synth.AddNoise(b, rng, 0, 0.12, 0.8, 28, 3500, 300);
        Synth.AddTone(b, 0, 0.2, 640, 140, 0.7, 16, h2: 0.3);
        Synth.AddTone(b, 0, 0.14, 75, 45, 0.7, 22);
        return b;
    }

    private static float[] ModuleHit(SynthRng rng)
    {
        var b = Synth.Buffer(0.5);
        double[] partials = { 310, 517, 823, 1290 };
        for (int i = 0; i < partials.Length; i++)
            Synth.AddTone(b, 0, 0.5, partials[i], partials[i], 0.7 / (1 + i * 0.6), 7 + i * 3);
        Synth.AddNoise(b, rng, 0, 0.04, 0.6, 60, 5000, 500);
        Synth.AddTone(b, 0, 0.2, 90, 55, 0.6, 18);
        return b;
    }

    private static float[] Explosion(SynthRng rng)
    {
        const double L = 1.8;
        var b = Synth.Buffer(L);
        // Noise whose brightness falls: a blend of three low-pass bands, the brighter ones fading first.
        var hi = Synth.Noise(rng, L, 4200, 0, cyclic: false);
        var mid = Synth.Noise(rng, L, 1200, 0, cyclic: false);
        var lo = Synth.Noise(rng, L, 260, 0, cyclic: false);
        for (int i = 0; i < b.Length; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Min(1.0, t / 0.004) * Math.Exp(-2.6 * t);
            b[i] = (float)(env * (hi[i] * Math.Exp(-9 * t) * 0.8 + mid[i] * Math.Exp(-4 * t) * 0.9 + lo[i] * 1.0));
        }
        Synth.AddTone(b, 0, L, 62, 28, 0.9, 2.2, h2: 0.3);
        return b;
    }

    private static float[] HullRumble(SynthRng rng)
    {
        const double L = 1.1;
        var b = Synth.Buffer(L);
        var n = Synth.Noise(rng, L, 140, 0, cyclic: false);
        for (int i = 0; i < b.Length; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Min(1.0, t / 0.03) * Math.Exp(-3.2 * t);
            b[i] = (float)(env * n[i]);
        }
        Synth.AddTone(b, 0, L, 40, 30, 0.7, 3.2);
        return b;
    }

    // ---- music ------------------------------------------------------------------------------------

    /// <summary>Length of a music loop in seconds.</summary>
    public const double MusicSeconds = 32.0;

    /// <summary>Four chords of eight seconds (Am, F, C, G over a low root); each voice is three detuned sines.</summary>
    private static readonly double[][] Chords =
    {
        new[] { 110.00, 164.81, 220.00, 261.63, 329.63 }, // A minor
        new[] { 87.31, 174.61, 220.00, 261.63, 349.23 },  // F major
        new[] { 130.81, 196.00, 261.63, 329.63, 392.00 }, // C major
        new[] { 98.00, 196.00, 246.94, 293.66, 392.00 },  // G major
    };

    private static float[] Music(SynthRng rng, bool tense)
    {
        double L = MusicSeconds;
        var b = Synth.Buffer(L);
        for (int c = 0; c < Chords.Length; c++)
        {
            // A raised-cosine window 16 s wide, centred on the middle of the chord's 8 s slot; neighbours overlap by half, so they sum to 1.
            double centre = c * 8.0 + 4.0;
            var voice = Synth.Buffer(L);
            double[] notes = Chords[c];
            for (int v = 0; v < notes.Length; v++)
            {
                double amp = v == 0 ? 0.5 : 0.28 / (1 + 0.15 * v);
                if (tense && v >= 3) amp *= 0.5;
                foreach (double detune in new[] { 0.997, 1.0, 1.003 })
                {
                    double f = Synth.Snap(notes[v] * detune, L);
                    Synth.AddTone(voice, 0, L, f, f, amp / 3.0, 0, h2: v == 0 ? 0.25 : 0.05, attack: 0);
                }
            }
            if (tense)
            {
                // A cold minor second on top of every chord.
                double f = Synth.Snap(notes[2] * 1.0595 * 2, L);
                Synth.AddTone(voice, 0, L, f, f, 0.1, 0, attack: 0);
            }
            for (int i = 0; i < b.Length; i++)
            {
                double t = i / (double)SampleRate;
                double x = ((t - (centre - 8.0)) % L + L) % L; // time since the window started, wrapped
                if (x >= 16.0) continue;
                double w = Math.Sin(Math.PI * x / 16.0);
                b[i] += (float)(voice[i] * w * w);
            }
        }
        Synth.Tremolo(b, 0.125, 0.18); // one slow swell every eight seconds
        // A sparse pentatonic bell now and then, its tail wrapping round the loop.
        double[] bells = { 880.0, 987.77, 1174.66, 1318.51, 1567.98 };
        int count = tense ? 5 : 8;
        for (int i = 0; i < count; i++)
        {
            double f = bells[(int)(rng.Next() * bells.Length) % bells.Length];
            Synth.AddTone(b, rng.Next() * L, 3.0, f, f, tense ? 0.035 : 0.05, 1.6, h2: 0.1, wrap: true, attack: 0.02);
        }
        if (tense)
        {
            // A pulsing low note: a heartbeat every 1.6 s (twenty a loop), a softer second beat 0.45 s later.
            double f = Synth.Snap(55, L);
            var pulse = Synth.Buffer(L);
            Synth.AddTone(pulse, 0, L, f, f, 1.0, 0, h2: 0.3, attack: 0);
            for (int i = 0; i < pulse.Length; i++)
            {
                double t = i / (double)SampleRate, p = t % 1.6;
                double env = Math.Exp(-5.0 * p) + 0.45 * (p >= 0.45 ? Math.Exp(-6.0 * (p - 0.45)) : 0.0);
                b[i] += (float)(pulse[i] * env * 0.35);
            }
        }
        Synth.CyclicReverb(b, 0.55, 0.9);
        return b;
    }
}
