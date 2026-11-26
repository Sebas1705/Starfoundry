using System.Collections.Generic;
using System.Linq;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class WorkbenchSpotTests
{
    private static IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> Ship(params ((int, int, int) At, ModuleKind Kind)[] modules) =>
        modules.Select(m => new KeyValuePair<(int Mx, int My, int Mz), ModuleKind>(m.At, m.Kind));

    private static readonly ((int, int, int), ModuleKind)[] Starter =
    {
        ((0, 0, 0), ModuleKind.Cockpit), ((0, 0, 1), ModuleKind.Hull), ((0, 0, 2), ModuleKind.Cargo),
        ((0, 0, 3), ModuleKind.Reactor), ((0, 0, 4), ModuleKind.Engine), ((0, 0, -1), ModuleKind.MiningLaser), ((0, 1, 0), ModuleKind.Gun),
    };

    [Fact]
    public void TheStarterShipPutsTheBenchInItsHullOnTheLeftWall()
    {
        var spot = WorkbenchSpot.Pick(Ship(Starter));
        Assert.Equal(new BenchSpot((0, 0, 1), BenchWall.Left), spot);
    }

    [Fact]
    public void AHullBeatsCargoAndTheLowestDeckWins()
    {
        var spot = WorkbenchSpot.Pick(Ship(((0, 1, 1), ModuleKind.Hull), ((0, 0, 3), ModuleKind.Hull), ((0, 0, 1), ModuleKind.Cargo)));
        Assert.Equal((0, 0, 3), spot!.Value.Module);
    }

    [Fact]
    public void WithoutAHullTheNextKindIsUsedAndEnginesAreNeverPicked()
    {
        var cargoShip = Ship(((0, 0, 0), ModuleKind.Cockpit), ((0, 0, 1), ModuleKind.Workshop), ((0, 0, 2), ModuleKind.Cargo), ((0, 0, 3), ModuleKind.Engine)).ToList();
        var spot = WorkbenchSpot.Pick(cargoShip);
        Assert.Equal(ModuleKind.Cargo, ModuleKindAt(cargoShip, spot));
        Assert.Equal(WorkbenchSpot.CargoInset, spot!.Value.Inset); // beside the shelves
        Assert.Null(WorkbenchSpot.Pick(Ship(((0, 0, 0), ModuleKind.Workshop), ((0, 0, 1), ModuleKind.Cockpit)))); // a fabrication bay is a floor to build on
        Assert.Null(WorkbenchSpot.Pick(Ship(((0, 0, 0), ModuleKind.Engine), ((0, 0, 1), ModuleKind.Reactor))));
        Assert.Null(WorkbenchSpot.Pick(Ship()));
    }

    private static ModuleKind ModuleKindAt(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules, BenchSpot? spot) =>
        modules.First(m => m.Key == spot!.Value.Module).Value;

    [Fact]
    public void ADoorwayOnTheLeftMovesTheBenchToTheRightWall()
    {
        var spot = WorkbenchSpot.Pick(Ship(((0, 0, 0), ModuleKind.Hull), ((-1, 0, 0), ModuleKind.Cargo)));
        Assert.Equal(new BenchSpot((0, 0, 0), BenchWall.Right), spot);
    }

    [Fact]
    public void AModuleWithDoorwaysOnBothSidesHasNoWallSoAnotherModuleIsUsed()
    {
        var spot = WorkbenchSpot.Pick(Ship(((0, 0, 0), ModuleKind.Hull), ((-1, 0, 0), ModuleKind.Cargo), ((1, 0, 0), ModuleKind.Cargo)));
        Assert.Equal((-1, 0, 0), spot!.Value.Module); // the cargo module on the outside
    }

    [Fact]
    public void TheFootprintMirrorsAcrossTheModuleAndKeepsTheFrontEnd()
    {
        var left = WorkbenchSpot.Footprint(BenchWall.Left);
        var right = WorkbenchSpot.Footprint(BenchWall.Right);
        Assert.Equal(8f - left.MaxX, right.MinX, 3);
        Assert.Equal(8f - left.MinX, right.MaxX, 3);
        Assert.Equal(left.MinZ, right.MinZ);
        Assert.Equal(left.MaxZ, right.MaxZ);
        Assert.True(left.MinX >= 1.1f, "the bench stands on the deck, not on the sloped hull side");
        Assert.True(left.MaxX < 3f, "the doorway in the middle stays clear");
    }

    [Theory]
    [InlineData("core:ship_smelt", BenchKind.Ingot)]
    [InlineData("core:ship_press", BenchKind.Plate)]
    [InlineData("core:ship_ammo", BenchKind.Ammo)]
    [InlineData("core:smelt_iron", BenchKind.Off)]
    [InlineData(null, BenchKind.Off)]
    public void RecipesMapToLamps(string? id, BenchKind kind) => Assert.Equal(kind, WorkbenchSpot.KindOf(id));

    [Fact]
    public void ShippedShipRecipesAllHaveALamp()
    {
        var content = TestContent.Core;
        foreach (var r in content.Recipes.Where(r => content.Machines[r.Machine].Role == MachineRole.Ship))
            Assert.NotEqual(BenchKind.Off, WorkbenchSpot.KindOf(r.Id));
    }

    [Theory]
    [InlineData(false, 0, 60, 0f)]
    [InlineData(true, 60, 60, 0f)]
    [InlineData(true, 30, 60, 0.5f)]
    [InlineData(true, 0, 60, 1f)]
    [InlineData(true, 90, 60, 0f)]
    [InlineData(true, 10, 0, 0f)]
    public void ProgressIsTheShareOfTheCraftDone(bool crafting, int remaining, int total, float expected) =>
        Assert.Equal(expected, WorkbenchSpot.Progress(crafting, remaining, total), 3);
}

public class WorkbenchFootprintTests
{
    [Fact]
    public void AnInsetMovesTheFootprintTowardsTheMiddleOfTheModule()
    {
        var left = WorkbenchSpot.Footprint(new BenchSpot((0, 0, 0), BenchWall.Left, 0.5f));
        var right = WorkbenchSpot.Footprint(new BenchSpot((0, 0, 0), BenchWall.Right, 0.5f));
        Assert.Equal(WorkbenchSpot.MinX + 0.5f, left.MinX, 3);
        Assert.Equal(8f - WorkbenchSpot.MaxX - 0.5f, right.MinX, 3);
        Assert.Equal(8f - WorkbenchSpot.MinX - 0.5f, right.MaxX, 3);
        Assert.True(left.MinX >= 1.65f, "clear of a cargo module's shelves");
    }
}

public class WorkbenchShippedShipsTests
{
    private static BenchSpot? PickFor(StructureTemplate t) =>
        WorkbenchSpot.Pick(t.Modules.Select(m => new KeyValuePair<(int Mx, int My, int Mz), ModuleKind>((m.Mx, m.My, m.Mz), m.Kind)));

    [Fact]
    public void TheStarterShipAndEveryScenarioShipGetABench()
    {
        var c = TestContent.CoreFloor;
        Assert.Equal(ModuleKind.Hull, c.StarterShip!.Modules.Single(m => (m.Mx, m.My, m.Mz) == PickFor(c.StarterShip)!.Value.Module).Kind);
        foreach (var name in new[] { "cold_ship", "siege_ship" })
        {
            var spot = PickFor(c.Templates[name]);
            Assert.NotNull(spot);
            Assert.Equal(ModuleKind.Hull, c.Templates[name].Modules.Single(m => (m.Mx, m.My, m.Mz) == spot!.Value.Module).Kind); // ADR 0039: no bay on a ship, so the bench goes in the hull module
        }
    }
}
