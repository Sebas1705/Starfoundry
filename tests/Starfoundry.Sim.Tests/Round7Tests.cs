using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Checks from the seventh polish round: what every scenario starts with is usable in the game's UI.</summary>
public class Round7Tests
{
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static readonly ScenarioCatalog Catalog = TestContent.ScenariosFloor;

    [Fact]
    public void EveryScenarioShipHasAWorkbenchSpotAndALaserAndAGunToStartWith()
    {
        foreach (var s in Catalog.All)
        {
            var w = SimWorld.CreateScenario(C, Catalog, s.Id, seedOverride: 20261122UL);
            int ship = w.Network.ShipStructure(0);
            Assert.True(ship >= 0, s.Id);
            var modules = w.Network.GetStructure(ship).Modules;
            Assert.NotNull(WorkbenchSpot.Pick(modules));
            Assert.True(w.Network.HasMiningLaser(0), s.Id);
            Assert.True(w.Network.HasGun(0), s.Id);
        }
    }
}
