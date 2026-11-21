using System.Globalization;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class GameSettingsTests
{
    [Fact]
    public void DefaultsAreInsideTheirRanges()
    {
        var d = GameSettings.Defaults();
        Assert.Equal(d, d.Clamped());
        Assert.Equal(1f, d.MouseSensitivity);
        Assert.Equal(80f, d.Fov);
        Assert.False(d.Fullscreen);
        Assert.False(d.InvertMouseY);
    }

    [Theory]
    [InlineData("es_ES", Language.Es)]
    [InlineData("es", Language.Es)]
    [InlineData("en_US", Language.En)]
    [InlineData("de_DE", Language.En)]
    [InlineData(null, Language.Es)]
    [InlineData("", Language.Es)]
    public void DefaultLanguageFollowsTheLocale(string? locale, Language expected) =>
        Assert.Equal(expected, GameSettings.Defaults(locale).Language);

    [Fact]
    public void ValuesAreClamped()
    {
        var low = new GameSettings { MouseSensitivity = 0f, Fov = 10f, MasterVolume = -3f }.Clamped();
        Assert.Equal(GameSettings.MinMouseSensitivity, low.MouseSensitivity);
        Assert.Equal(GameSettings.MinFov, low.Fov);
        Assert.Equal(0f, low.MasterVolume);
        var high = new GameSettings { MouseSensitivity = 9f, Fov = 170f, MasterVolume = 4f }.Clamped();
        Assert.Equal(GameSettings.MaxMouseSensitivity, high.MouseSensitivity);
        Assert.Equal(GameSettings.MaxFov, high.Fov);
        Assert.Equal(1f, high.MasterVolume);
        var nan = new GameSettings { MouseSensitivity = float.NaN, Fov = float.PositiveInfinity }.Clamped();
        Assert.Equal(1f, nan.MouseSensitivity);
        Assert.Equal(80f, nan.Fov);
    }

    [Fact]
    public void SerialisationRoundTrips()
    {
        var s = new GameSettings { Language = Language.En, MouseSensitivity = 1.75f, Fov = 92.5f, MasterVolume = 0.35f, InvertMouseY = true, Fullscreen = true };
        Assert.Equal(s, GameSettings.Parse(s.Serialize()));
        var d = GameSettings.Defaults();
        Assert.Equal(d, GameSettings.Parse(d.Serialize()));
    }

    [Fact]
    public void RoundTripIsStableAcrossCultures()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            var s = new GameSettings { MouseSensitivity = 1.5f };
            Assert.Contains("mouse_sensitivity=1.5", s.Serialize());
            Assert.Equal(s, GameSettings.Parse(s.Serialize()));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        var parsed = GameSettings.Parse("[settings]\nfuture_option=7\nfov=90\n; comment\nlanguage=en\nnonsense\n=5\n");
        Assert.Equal(90f, parsed.Fov);
        Assert.Equal(Language.En, parsed.Language);
        Assert.Equal(1f, parsed.MouseSensitivity);
    }

    [Fact]
    public void OutOfRangeValuesInAFileAreClamped()
    {
        var parsed = GameSettings.Parse("fov=500\nmouse_sensitivity=0.01\nmaster_volume=7");
        Assert.Equal(GameSettings.MaxFov, parsed.Fov);
        Assert.Equal(GameSettings.MinMouseSensitivity, parsed.MouseSensitivity);
        Assert.Equal(1f, parsed.MasterVolume);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("\0\0\u0001\u0002 garbage ===")]
    [InlineData("fov=abc\nlanguage=klingon\nfullscreen=maybe\nmouse_sensitivity=\ninvert_mouse_y=2")]
    public void GarbageGivesTheBaseline(string? text)
    {
        var baseline = GameSettings.Defaults("en_GB");
        Assert.Equal(baseline, GameSettings.Parse(text, baseline));
    }

    [Fact]
    public void GoodKeysSurviveBadNeighbours()
    {
        var parsed = GameSettings.Parse("fov=abc\ninvert_mouse_y=true\nlanguage=klingon\nfullscreen=true");
        Assert.True(parsed.InvertMouseY);
        Assert.True(parsed.Fullscreen);
        Assert.Equal(80f, parsed.Fov);
    }
}
