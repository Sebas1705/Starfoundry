using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class AudioTests
{
    private static readonly Dictionary<Sfx, float[]> Cache = new();

    private static float[] Rendered(Sfx id)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(id, out var b)) Cache[id] = b = SoundBank.Render(id);
            return b;
        }
    }

    public static IEnumerable<object[]> AllSounds() => Enum.GetValues<Sfx>().Select(s => new object[] { s });

    public static IEnumerable<object[]> Loops() => SoundCatalog.All.Where(s => s.Loop).Select(s => new object[] { s.Id });

    public static IEnumerable<object[]> OneShots() => SoundCatalog.All.Where(s => !s.Loop).Select(s => new object[] { s.Id });

    [Fact]
    public void EverySoundHasASpecInOrder()
    {
        Assert.Equal(Enum.GetValues<Sfx>().Length, SoundCatalog.All.Count);
        foreach (var id in Enum.GetValues<Sfx>()) Assert.Equal(id, SoundCatalog.Get(id).Id);
    }

    [Theory, MemberData(nameof(AllSounds))]
    public void SoundsHaveASaneLengthAndLevel(Sfx id)
    {
        var spec = SoundCatalog.Get(id);
        var b = Rendered(id);
        var st = SoundStats.Measure(b);
        Assert.InRange(st.Seconds, 0.05, 40.0);
        Assert.True(st.Peak <= 0.95f, $"{id} peaks at {st.Peak}");
        Assert.True(st.Peak >= spec.Peak - 0.01f, $"{id} is too quiet: {st.Peak}");
        Assert.True(st.Rms > 0.01f, $"{id} is nearly silent: rms {st.Rms}");
        Assert.All(b, v => Assert.True(float.IsFinite(v)));
    }

    [Theory, MemberData(nameof(AllSounds))]
    public void RenderingIsDeterministic(Sfx id) => Assert.Equal(Rendered(id), SoundBank.Render(id));

    [Theory, MemberData(nameof(Loops))]
    public void LoopsAreSeamless(Sfx id)
    {
        var st = SoundStats.Measure(Rendered(id));
        Assert.True(st.Seamless, $"{id}: seam {st.SeamStep} vs max step {st.MaxStep}");
    }

    [Theory, MemberData(nameof(OneShots))]
    public void OneShotsStartAndEndInSilence(Sfx id)
    {
        var b = Rendered(id);
        Assert.True(MathF.Abs(b[0]) < 0.02f);
        Assert.True(MathF.Abs(b[^1]) < 0.02f);
    }

    [Fact]
    public void MusicVariantsHaveTheSameLength() =>
        Assert.Equal(Rendered(Sfx.MusicCalm).Length, Rendered(Sfx.MusicTense).Length);

    [Fact]
    public void TheTenseMusicDiffersFromTheCalmOne() =>
        Assert.NotEqual(Rendered(Sfx.MusicCalm), Rendered(Sfx.MusicTense));

    [Fact]
    public void WavFileHasAValidHeader()
    {
        var wav = WavFile.Encode(new float[100]);
        Assert.Equal(44 + 200, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(200, BitConverter.ToInt32(wav, 40));
    }

    [Fact]
    public void Pcm16ClampsAndIsLittleEndian()
    {
        var pcm = WavFile.Pcm16(new[] { 0f, 1f, -1f, 5f });
        Assert.Equal(8, pcm.Length);
        Assert.Equal(32767, BitConverter.ToInt16(pcm, 2));
        Assert.Equal(-32767, BitConverter.ToInt16(pcm, 4));
        Assert.Equal(32767, BitConverter.ToInt16(pcm, 6));
    }

    [Theory]
    [InlineData("--demo", true)]
    [InlineData("--threat-demo", true)]
    [InlineData("--shot=a.png", true)]
    [InlineData("--hud-shot=a.png", true)]
    [InlineData("--menu-shot=a.png", true)]
    [InlineData("--defence-shot=a.png", true)]
    [InlineData("--stress=100", true)]
    [InlineData("--bench", true)]
    [InlineData("--audio-test", false)]
    [InlineData("--play", false)]
    public void SilentFlags(string arg, bool silent) => Assert.Equal(silent, AudioFlags.IsSilent(new[] { arg }));

    [Fact]
    public void NoFlagsIsNotSilent() => Assert.False(AudioFlags.IsSilent(Array.Empty<string>()));

    [Fact]
    public void RateLimiterBlocksRepeatsUntilTheIntervalPasses()
    {
        var r = new RateLimiter(4);
        Assert.True(r.Allow(1, 10.0, 0.5));
        Assert.False(r.Allow(1, 10.2, 0.5));
        Assert.True(r.Allow(2, 10.2, 0.5)); // another id is independent
        Assert.True(r.Allow(1, 10.5, 0.5));
        Assert.False(r.Allow(99, 11.0, 0.5)); // unknown id
    }

    [Fact]
    public void EngineFollowsThrustAndSpeed()
    {
        Assert.Equal(AudioMix.Silent, AudioMix.EngineDb(0, 0));
        Assert.True(AudioMix.EngineDb(1, 1) > AudioMix.EngineDb(0.3f, 0.1f));
        Assert.True(AudioMix.EnginePitch(1) > AudioMix.EnginePitch(0));
        Assert.InRange(AudioMix.EnginePitch(5), 0.7f, 1.7f);
    }

    [Fact]
    public void ReactorIsSteadyWhenPoweredAndStuttersWhenStrained()
    {
        Assert.Equal(AudioMix.Silent, AudioMix.ReactorDb(1f, false, 3.0));
        var steady = Enumerable.Range(0, 100).Select(i => AudioMix.ReactorDb(0.9f, true, i * 0.05)).Distinct().ToList();
        Assert.Single(steady);
        var strained = Enumerable.Range(0, 200).Select(i => AudioMix.ReactorDb(0.1f, true, i * 0.05)).Distinct().Count();
        Assert.Equal(2, strained); // two levels: hum and drop-out
        Assert.Equal(AudioMix.ReactorDb(0.1f, true, 1.234), AudioMix.ReactorDb(0.1f, true, 1.234)); // deterministic
    }

    [Fact]
    public void MusicCrossfadeIsEqualPower()
    {
        var (c0, t0) = AudioMix.MusicCrossfade(0f);
        Assert.Equal(0f, c0, 3);
        Assert.Equal(AudioMix.Silent, t0);
        var (c1, t1) = AudioMix.MusicCrossfade(1f);
        Assert.Equal(AudioMix.Silent, c1);
        Assert.Equal(0f, t1, 3);
        var (cm, tm) = AudioMix.MusicCrossfade(0.5f);
        Assert.Equal(cm, tm, 3);
    }

    [Fact]
    public void BusVolumeMapsPercentToDb()
    {
        Assert.Equal(AudioMix.Silent, AudioMix.PercentToDb(0));
        Assert.Equal(0f, AudioMix.PercentToDb(100), 3);
        Assert.Equal(-6.02f, AudioMix.PercentToDb(50), 2);
        Assert.Equal(0f, AudioMix.PercentToDb(500), 3);
    }

    [Fact]
    public void ApproachNeverOvershoots()
    {
        Assert.Equal(0.5f, AudioMix.Approach(0f, 1f, 1f, 0.5f));
        Assert.Equal(1f, AudioMix.Approach(0.9f, 1f, 1f, 0.5f));
        Assert.Equal(0f, AudioMix.Approach(0.1f, 0f, 1f, 0.5f));
    }

    [Fact]
    public void NearestSetKeepsTheClosestSorted()
    {
        var set = new NearestSet(3);
        foreach (var (id, d) in new[] { (1, 50f), (2, 10f), (3, 30f), (4, 5f), (5, 70f), (6, 20f) }) set.Offer(id, d);
        Assert.Equal(3, set.Count);
        Assert.Equal(new[] { 4, 2, 6 }, Enumerable.Range(0, 3).Select(set.Id));
        Assert.True(set.Contains(2));
        Assert.False(set.Contains(1));
        set.Clear();
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void VolumeSettingsAreClampedStoredAndDefaulted()
    {
        var d = GameSettings.Defaults();
        Assert.Equal((80, 60, 80), (d.SfxVolume, d.AmbienceVolume, d.UiVolume));
        var low = new GameSettings { SfxVolume = -5, AmbienceVolume = -1, UiVolume = -100 }.Clamped();
        Assert.Equal((0, 0, 0), (low.SfxVolume, low.AmbienceVolume, low.UiVolume));
        var high = new GameSettings { SfxVolume = 500, AmbienceVolume = 101, UiVolume = 1000 }.Clamped();
        Assert.Equal((100, 100, 100), (high.SfxVolume, high.AmbienceVolume, high.UiVolume));
        var s = new GameSettings { SfxVolume = 33, AmbienceVolume = 0, UiVolume = 100 };
        Assert.Equal(s, GameSettings.Parse(s.Serialize()));
        var parsed = GameSettings.Parse("sfx_volume=abc\nambience_volume=250\nui_volume=7");
        Assert.Equal((80, 100, 7), (parsed.SfxVolume, parsed.AmbienceVolume, parsed.UiVolume));
    }

    [Fact]
    public void VolumeStringsExistInBothLanguages()
    {
        foreach (string key in new[] { "settings.sfxVolume", "settings.ambienceVolume", "settings.uiVolume", "settings.tip.sfxVolume", "settings.tip.ambienceVolume", "settings.tip.uiVolume" })
        {
            var old = Loc.Lang;
            try
            {
                Loc.Lang = Language.Es; string es = Loc.T(key, 50);
                Loc.Lang = Language.En; string en = Loc.T(key, 50);
                Assert.NotEqual(key, es);
                Assert.NotEqual(es, en);
            }
            finally { Loc.Lang = old; }
        }
    }
}
