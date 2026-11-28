using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The one place that makes sound. Every sound is synthesised from code by <see cref="SoundBank"/> (no assets): the float buffers are
/// rendered once on a background thread, turned into <see cref="AudioStreamWav"/>s on first use and cached for the whole run. The
/// manager owns small pools of players (so playing a sound never allocates a node), the loop players (engine, reactor, room tone,
/// mining, music), eight positional hum slots and the audio buses (Master, Sfx, Ambience, Ui; the slider values of the settings are
/// applied to them). A rate limiter per sound id stops bursts. When <see cref="Enabled"/> is false (scripted runs) nothing is created or
/// played; when the audio device is missing the engine's dummy driver swallows everything, so nothing here throws.
/// </summary>
public partial class AudioManager : Node
{
    public const int HumSlots = 8;
    private const int Pool2DSize = 10, Pool3DSize = 12;
    private const float FadeDbPerSecond = 90f;

    /// <summary>Global switch: false in scripted runs (demos, screenshots, benchmarks) so they stay silent and unaffected.</summary>
    public static bool Enabled = true;

    public static AudioManager? Instance { get; private set; }

    private static readonly string[] BusNames = { "Sfx", "Ambience", "Ui" };
    private static readonly ConcurrentDictionary<Sfx, float[]> Rendered = new();
    private readonly Dictionary<Sfx, AudioStreamWav> _streams = new(); // per manager, so the engine resources are freed with it
    private static Task? _warmup;

    private readonly RateLimiter _limiter = new(Enum.GetValues<Sfx>().Length);
    private readonly SynthRng _rng = new(12345);
    private readonly AudioStreamPlayer[] _pool2D = new AudioStreamPlayer[Pool2DSize];
    private readonly AudioStreamPlayer3D[] _pool3D = new AudioStreamPlayer3D[Pool3DSize];
    private int _next2D, _next3D;
    private readonly Dictionary<Sfx, AudioStreamPlayer> _loops = new();
    private readonly HashSet<Sfx> _loopStarted = new();
    private readonly AudioStreamPlayer3D[] _hums = new AudioStreamPlayer3D[HumSlots];
    private readonly Sfx[] _humSound = new Sfx[HumSlots];
    private readonly bool[] _humWanted = new bool[HumSlots];
    private readonly float[] _humDb = new float[HumSlots];
    private bool _loopsPaused;

    // ---- setup ---------------------------------------------------------------------------------------

    /// <summary>Creates the manager under <paramref name="parent"/>, or returns null when audio is disabled.</summary>
    public static AudioManager? Create(Node parent)
    {
        if (!Enabled) return null;
        var m = new AudioManager { Name = "AudioManager", ProcessMode = ProcessModeEnum.Always };
        parent.AddChild(m);
        return m;
    }

    /// <summary>Plays a UI sound through the current manager, if there is one (menus and panels call this without holding a reference).</summary>
    public static void PlayUi(Sfx id) => Instance?.Play(id);

    public override void _EnterTree()
    {
        Instance = this;
        EnsureBuses();
        ApplyBusVolumes(SettingsStore.Current);
        for (int i = 0; i < Pool2DSize; i++)
        {
            _pool2D[i] = new AudioStreamPlayer();
            AddChild(_pool2D[i]);
        }
        for (int i = 0; i < Pool3DSize; i++)
        {
            _pool3D[i] = new AudioStreamPlayer3D { UnitSize = 12f, MaxDistance = 220f, AttenuationFilterCutoffHz = 9000f };
            _pool3D[i].AddToGroup(View.WorldGroup); // a one-shot is placed once, in view space: a rebase of the view origin shifts it (ADR 0053; the hums are moved every frame)
            AddChild(_pool3D[i]);
        }
        for (int i = 0; i < HumSlots; i++)
        {
            _hums[i] = new AudioStreamPlayer3D { UnitSize = 4f, MaxDistance = 40f, AttenuationFilterCutoffHz = 4500f, Bus = "Sfx", VolumeDb = AudioMix.Silent };
            AddChild(_hums[i]);
            _humDb[i] = AudioMix.Silent;
        }
        StartWarmup();
        SettingsStore.Changed += OnSettingsChanged;
    }

    public override void _ExitTree()
    {
        SettingsStore.Changed -= OnSettingsChanged;
        if (Instance == this) Instance = null;
        // Release the engine resources now (the managed dictionary would otherwise keep them alive until the process ends).
        foreach (var p in _pool2D) { p.Stop(); p.Stream = null; }
        foreach (var p in _pool3D) { p.Stop(); p.Stream = null; }
        foreach (var p in _hums) { p.Stop(); p.Stream = null; }
        foreach (var p in _loops.Values) { p.Stop(); p.Stream = null; }
        foreach (var s in _streams.Values) s.Dispose();
        _streams.Clear();
    }

    private void OnSettingsChanged() => ApplyBusVolumes(SettingsStore.Current);

    /// <summary>Renders every sound once, off the main thread; <see cref="GetStream"/> hands out the ones that are done.</summary>
    private static void StartWarmup()
    {
        if (_warmup is not null) return;
        _warmup = Task.Run(() =>
        {
            foreach (var spec in SoundCatalog.All)
            {
                try { Rendered[spec.Id] = SoundBank.Render(spec.Id); }
                catch (Exception e) { GD.PrintErr($"AUDIO render {spec.Id} failed: {e.Message}"); }
            }
        });
    }

    private AudioStreamWav? GetStream(Sfx id)
    {
        if (_streams.TryGetValue(id, out var s)) return s;
        if (!Rendered.TryGetValue(id, out var data)) return null; // still rendering: skipped this time
        var spec = SoundCatalog.Get(id);
        s = new AudioStreamWav { Data = WavFile.Pcm16(data), Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = SoundBank.SampleRate, Stereo = false };
        if (spec.Loop)
        {
            s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            s.LoopBegin = 0;
            s.LoopEnd = data.Length;
        }
        _streams[id] = s;
        return s;
    }

    // ---- recorded one-shots (ADR 0037) -------------------------------------------------------------------

    private static SfxMap? _map;
    private readonly Dictionary<string, AudioStream?> _recordedFiles = new();

    /// <summary>A/B listening: true forces the recorded set, false the synthesised one, null follows the setting.</summary>
    public bool? ForceRecorded;

    private static SfxMap Map
    {
        get
        {
            if (_map is not null) return _map;
            string json = FileAccess.FileExists("res://assets/audio/sfx_map.json") ? FileAccess.GetFileAsString("res://assets/audio/sfx_map.json") : "{}";
            return _map = SfxMap.Parse(json);
        }
    }

    /// <summary>Whether a recorded file exists for the effect (so both versions can be compared).</summary>
    public static bool HasRecorded(Sfx id) => !SoundCatalog.Get(id).Loop && Map.TryGet(id, out _);

    /// <summary>The recorded stream for a one-shot (one variant at random) with its table entry, or null to use the synthesised sound.</summary>
    private AudioStream? GetRecorded(Sfx id, out SfxFileEntry entry)
    {
        entry = null!;
        if (!(ForceRecorded ?? SettingsStore.Current.RecordedSounds) || SoundCatalog.Get(id).Loop || !Map.TryGet(id, out entry)) return null;
        string file = entry.Files[entry.Files.Count == 1 ? 0 : (int)(_rng.Next() * entry.Files.Count) % entry.Files.Count];
        if (_recordedFiles.TryGetValue(file, out var cached)) return cached;
        AudioStream? s = null;
        string path = "res://assets/audio/" + file;
        if (FileAccess.FileExists(path))
        {
            byte[] bytes = FileAccess.GetFileAsBytes(path);
            if (bytes is { Length: > 0 }) s = AudioStreamOggVorbis.LoadFromBuffer(bytes);
        }
        if (s is null) GD.PrintErr($"AUDIO recorded file missing or unreadable, using the synthesised sound: {file}");
        _recordedFiles[file] = s;
        return s;
    }

    // ---- buses ---------------------------------------------------------------------------------------

    /// <summary>Makes sure the Sfx, Ambience and Ui buses exist (each sends to Master) and that Master has a limiter against clipping.</summary>
    public static void EnsureBuses()
    {
        foreach (string name in BusNames)
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int idx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, "Master");
        }
        if (AudioServer.GetBusEffectCount(0) == 0)
            AudioServer.AddBusEffect(0, new AudioEffectLimiter { CeilingDb = -1f });
    }

    /// <summary>Applies the three bus sliders of the settings (and creates the buses when they are missing).</summary>
    public static void ApplyBusVolumes(GameSettings s)
    {
        EnsureBuses();
        SetBus("Sfx", s.SfxVolume);
        SetBus("Ambience", s.AmbienceVolume);
        SetBus("Ui", s.UiVolume);
    }

    private static void SetBus(string name, int percent)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx >= 0) AudioServer.SetBusVolumeDb(idx, AudioMix.PercentToDb(percent));
    }

    private static string BusName(AudioBus bus) => BusNames[(int)bus];

    private static double Now => Time.GetTicksMsec() / 1000.0;

    // ---- one-shots -----------------------------------------------------------------------------------

    /// <summary>
    /// Plays a one-shot sound, rate limited per id. With a position (world units) it comes from there with distance attenuation; without
    /// one it plays flat. Loops are driven with <see cref="SetLoop"/>, hums with <see cref="SetHum"/>.
    /// </summary>
    public void Play(Sfx id, Vector3? pos = null)
    {
        if (!Enabled) return;
        var spec = SoundCatalog.Get(id);
        if (spec.Loop) return;
        if (!_limiter.Allow((int)id, Now, spec.MinInterval)) return;
        AudioStream? stream = GetRecorded(id, out var rec);
        float gainDb = spec.GainDb, jitter = 0.05f;
        if (stream is not null) { gainDb += rec.GainDb; jitter = rec.PitchJitter; }
        else stream = GetStream(id);
        if (stream is null) return;
        float pitch = 1f + jitter * (2f * _rng.Next() - 1f);
        if (pos is { } p)
        {
            var pl = Free3D();
            pl.Stream = stream;
            pl.Bus = BusName(spec.Bus);
            pl.VolumeDb = gainDb;
            pl.PitchScale = pitch;
            pl.GlobalPosition = p;
            pl.Play();
        }
        else
        {
            var pl = Free2D();
            pl.Stream = stream;
            pl.Bus = BusName(spec.Bus);
            pl.VolumeDb = gainDb;
            pl.PitchScale = spec.Bus == AudioBus.Ui ? 1f : pitch;
            pl.Play();
        }
    }

    private AudioStreamPlayer Free2D()
    {
        for (int i = 0; i < Pool2DSize; i++)
            if (!_pool2D[i].Playing) return _pool2D[i];
        return _pool2D[_next2D++ % Pool2DSize]; // all busy: steal the next in turn
    }

    private AudioStreamPlayer3D Free3D()
    {
        for (int i = 0; i < Pool3DSize; i++)
            if (!_pool3D[i].Playing) return _pool3D[i];
        return _pool3D[_next3D++ % Pool3DSize];
    }

    // ---- loops ---------------------------------------------------------------------------------------

    /// <summary>
    /// Drives a looping sound: <paramref name="db"/> is added to the sound's own gain; at or below -60 dB the loop is paused (free).
    /// Returns false while the sound is still being rendered.
    /// </summary>
    public bool SetLoop(Sfx id, float db, float pitch = 1f, bool keepRunning = false)
    {
        if (!Enabled) return false;
        if (!_loops.TryGetValue(id, out var p))
        {
            var stream = GetStream(id);
            if (stream is null) return false;
            p = new AudioStreamPlayer { Stream = stream, Bus = BusName(SoundCatalog.Get(id).Bus), VolumeDb = AudioMix.Silent };
            AddChild(p);
            _loops[id] = p;
        }
        bool want = keepRunning || db > -60f;
        if (want)
        {
            p.VolumeDb = SoundCatalog.Get(id).GainDb + db;
            p.PitchScale = Mathf.Clamp(pitch, 0.1f, 4f);
        }
        p.StreamPaused = _loopsPaused || !want;
        if (want && _loopStarted.Add(id)) p.Play();
        return true;
    }

    /// <summary>Starts the two music variants together (so they stay in step) and crossfades them for a tension of 0 to 1.</summary>
    public void SetMusic(float tension01, float baseDb = 0f)
    {
        if (!Enabled) return;
        var (calm, tense) = AudioMix.MusicCrossfade(tension01);
        bool bothReady = _loops.ContainsKey(Sfx.MusicCalm) && _loops.ContainsKey(Sfx.MusicTense);
        if (!bothReady)
        {
            // Create both players before starting either: only when both streams are rendered.
            if (GetStream(Sfx.MusicCalm) is null || GetStream(Sfx.MusicTense) is null) return;
            CreateLoopPlayer(Sfx.MusicCalm);
            CreateLoopPlayer(Sfx.MusicTense);
        }
        SetLoop(Sfx.MusicCalm, calm + baseDb, 1f, keepRunning: true);
        SetLoop(Sfx.MusicTense, tense + baseDb, 1f, keepRunning: true);
    }

    private void CreateLoopPlayer(Sfx id)
    {
        var p = new AudioStreamPlayer { Stream = GetStream(id), Bus = BusName(SoundCatalog.Get(id).Bus), VolumeDb = AudioMix.Silent };
        AddChild(p);
        _loops[id] = p;
    }

    /// <summary>Pauses or resumes every loop and hum (the pause menu); one-shots and UI sounds are not affected.</summary>
    public void SetLoopsPaused(bool paused)
    {
        if (_loopsPaused == paused) return;
        _loopsPaused = paused;
        // Muted loops are re-paused by their next SetLoop call, so resuming everything here is fine.
        foreach (var (id, p) in _loops)
            if (_loopStarted.Contains(id)) p.StreamPaused = paused;
        for (int i = 0; i < HumSlots; i++) _hums[i].StreamPaused = paused;
    }

    // ---- positional hums -----------------------------------------------------------------------------

    /// <summary>Makes hum slot <paramref name="slot"/> play <paramref name="id"/> at a position (faded in); a different sound restarts it.</summary>
    public void SetHum(int slot, Sfx id, Vector3 pos)
    {
        if (!Enabled || (uint)slot >= HumSlots) return;
        var p = _hums[slot];
        if (!_humWanted[slot] || _humSound[slot] != id || !p.Playing)
        {
            var stream = GetStream(id);
            if (stream is null) return;
            p.Stream = stream;
            _humSound[slot] = id;
            p.PitchScale = 0.96f + 0.08f * _rng.Next();
            p.Play(_rng.Next() * (float)stream.GetLength()); // a random start so two machines of one kind never phase together
            if (!_humWanted[slot]) _humDb[slot] = AudioMix.Silent;
        }
        _humWanted[slot] = true;
        p.GlobalPosition = pos;
    }

    /// <summary>Moves a hum that is already playing (every frame, so a moving ship carries its machines' sound with it).</summary>
    public void MoveHum(int slot, Vector3 pos)
    {
        if ((uint)slot < HumSlots && _humWanted[slot]) _hums[slot].GlobalPosition = pos;
    }

    /// <summary>Fades hum slot <paramref name="slot"/> out.</summary>
    public void ClearHum(int slot)
    {
        if ((uint)slot < HumSlots) _humWanted[slot] = false;
    }

    public override void _Process(double delta)
    {
        float step = FadeDbPerSecond * (float)delta;
        for (int i = 0; i < HumSlots; i++)
        {
            var p = _hums[i];
            float target = _humWanted[i] ? SoundCatalog.Get(_humSound[i]).GainDb : AudioMix.Silent;
            if (!_humWanted[i] && !p.Playing) continue;
            _humDb[i] = _humDb[i] < target ? MathF.Min(target, _humDb[i] + step) : MathF.Max(target, _humDb[i] - step);
            p.VolumeDb = _humDb[i];
            if (!_humWanted[i] && _humDb[i] <= -70f) p.Stop();
        }
    }
}
