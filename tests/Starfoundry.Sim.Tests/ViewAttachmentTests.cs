using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Wings under the windows, skylights in gun decks and the designer's view warnings.</summary>
public class ViewAttachmentTests
{
    [Fact]
    public void WingsStandBelowTheLowestWindowSillAndAboveTheDeck()
    {
        Assert.InRange(HullProfile.WingY, 0.4f, 0.9f);
        Assert.True(HullProfile.WingTop + 0.3f < 1.3f, "the lowest viewport sill is 1.3 above the deck");
        Assert.True(HullProfile.WingY > HullProfile.DeckTop);
    }

    [Fact]
    public void AGunOnTopOfAnythingGetsSkylightsOthersDoNot()
    {
        Assert.True(HullProfile.HasSkylight(ModuleKind.Gun, true, HullProfile.Down));
        Assert.True(HullProfile.HasSkylight(ModuleKind.GunMk2, true, HullProfile.Down | HullProfile.Left));
        Assert.False(HullProfile.HasSkylight(ModuleKind.Gun, true, 0));
        Assert.False(HullProfile.HasSkylight(ModuleKind.Hull, true, HullProfile.Down));
        Assert.False(HullProfile.HasSkylight(ModuleKind.Gun, false, HullProfile.Down));
        foreach (var p in HullProfile.SkylightPanes)
        {
            Assert.InRange(p.X0, 0.5f, 7.5f); Assert.InRange(p.X1, 0.5f, 7.5f); Assert.True(p.X1 > p.X0 + 1f && p.Z1 > p.Z0 + 1f);
            // clear of the gun mounts of a ship (discs of radius 1.0 about x = 4)
            Assert.True(p.X1 < 4f - 1.0f || p.X0 > 4f + 1.0f);
        }
    }

    private static Dictionary<(int, int, int), ModuleKind> Ship(params ((int, int, int), ModuleKind)[] parts) => parts.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void PodsOnAroundOrInFrontOfACockpitAreReportedUnlessTheirSkylightClearsTheView()
    {
        var gunOn = Ship(((0, 0, 0), ModuleKind.Cockpit), ((0, 1, 0), ModuleKind.Gun));
        Assert.Empty(DesignWarnings.ViewBlockers(gunOn)); // the gun deck is glazed under and over
        var shieldOn = Ship(((0, 0, 0), ModuleKind.Cockpit), ((0, 1, 0), ModuleKind.Shield));
        Assert.Equal(ViewWarning.BlocksCanopy, Assert.Single(DesignWarnings.ViewBlockers(shieldOn)).Why);
        var laserFront = Ship(((0, 0, 0), ModuleKind.Cockpit), ((0, 0, -1), ModuleKind.MiningLaser));
        Assert.Equal(ViewWarning.BlocksWindscreen, Assert.Single(DesignWarnings.ViewBlockers(laserFront)).Why);
        var sideSensor = Ship(((0, 0, 0), ModuleKind.Cockpit), ((1, 0, 0), ModuleKind.Sensor));
        Assert.Equal(ViewWarning.BlocksFlankWindows, Assert.Single(DesignWarnings.ViewBlockers(sideSensor)).Why);
        var fine = Ship(((0, 0, 0), ModuleKind.Cockpit), ((0, 0, 1), ModuleKind.Hull), ((1, 0, 1), ModuleKind.Cargo));
        Assert.Empty(DesignWarnings.ViewBlockers(fine));
    }
}
