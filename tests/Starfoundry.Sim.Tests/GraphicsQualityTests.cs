using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class GraphicsQualityTests
{
    [Fact]
    public void DefaultIsMediumAndItRoundTripsThroughTheSettingsFile()
    {
        Assert.Equal(GraphicsQuality.Medium, GameSettings.Defaults().Graphics);
        foreach (var q in new[] { GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High })
        {
            var s = new GameSettings { Graphics = q };
            Assert.Contains("graphics_quality=" + GraphicsQualities.Id(q), s.Serialize());
            Assert.Equal(q, GameSettings.Parse(s.Serialize()).Graphics);
        }
    }

    [Fact]
    public void BadOrMissingValuesKeepTheDefault()
    {
        Assert.Equal(GraphicsQuality.Medium, GameSettings.Parse("graphics_quality=ultra").Graphics);
        Assert.Equal(GraphicsQuality.Medium, GameSettings.Parse("fov=90").Graphics); // an older file without the key
        Assert.Equal(GraphicsQuality.Low, GameSettings.Parse("graphics_quality=LOW").Graphics);
        Assert.Equal(GraphicsQuality.Medium, (new GameSettings { Graphics = (GraphicsQuality)99 }).Clamped().Graphics);
    }

    [Fact]
    public void NextCyclesThroughAllThreeLevels()
    {
        var q = GraphicsQuality.Low;
        var seen = new List<GraphicsQuality>();
        for (int i = 0; i < 3; i++) { seen.Add(q); q = GraphicsQualities.Next(q); }
        Assert.Equal(GraphicsQuality.Low, q);
        Assert.Equal(3, seen.Distinct().Count());
    }

    [Fact]
    public void LowDropsEverythingExpensiveAndHighKeepsTheMost()
    {
        var low = GraphicsQualities.ProfileOf(GraphicsQuality.Low);
        var med = GraphicsQualities.ProfileOf(GraphicsQuality.Medium);
        var high = GraphicsQualities.ProfileOf(GraphicsQuality.High);
        Assert.False(low.Shadows);
        Assert.Equal(0, low.Msaa);
        Assert.False(low.Ssao);
        Assert.False(low.InteriorExtras);
        Assert.True(med.Shadows);
        Assert.Equal(2, med.Msaa);
        Assert.False(med.Ssao);
        Assert.True(high.Shadows);
        Assert.Equal(4, high.Msaa);
        Assert.True(high.Ssao);
        Assert.True(high.ShadowDistance > med.ShadowDistance && high.ShadowAtlas > med.ShadowAtlas && high.ShadowSplits > med.ShadowSplits);
        Assert.True(low.AmbientEnergy > med.AmbientEnergy, "the shadow side only goes dark where shadows exist, so Low keeps the brighter ambient");
    }

    [Fact]
    public void EveryStarTypeOfTheContentHasItsOwnKeyLight()
    {
        var ids = new[] { "core:yellow_dwarf", "core:red_dwarf", "core:orange_star", "core:blue_giant", "core:binary_star", "core:pulsar" };
        var colours = ids.Select(StarLight.For).Select(l => (l.R, l.G, l.B)).Distinct().ToList();
        Assert.Equal(ids.Length, colours.Count);
        Assert.True(StarLight.For("core:red_dwarf").R > StarLight.For("core:red_dwarf").B, "a red dwarf is warm");
        Assert.True(StarLight.For("core:blue_giant").B > StarLight.For("core:blue_giant").R, "a blue giant is cool");
        Assert.True(StarLight.For("core:blue_giant").Energy > StarLight.For("core:red_dwarf").Energy);
        Assert.True(StarLight.For("core:binary_star").Binary);
        Assert.False(StarLight.For("core:yellow_dwarf").Binary);
        Assert.True(StarLight.For("core:pulsar").PulseDepth is > 0f and < 0.15f, "a pulsar flickers, gently");
        Assert.Equal(StarLight.YellowDwarf, StarLight.For("mod:unknown"));
        Assert.Equal(StarLight.YellowDwarf, StarLight.For(null));
    }

    [Fact]
    public void TheTextsExistInBothLanguages()
    {
        foreach (var key in new[] { "settings.graphics", "settings.graphics.low", "settings.graphics.medium", "settings.graphics.high", "settings.tip.graphics" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es), key + " es");
            Assert.False(string.IsNullOrWhiteSpace(en), key + " en");
            Assert.NotEqual(es, en);
        }
    }
}
