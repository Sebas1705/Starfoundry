using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class TipsTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261120UL;

    private static SimWorld World(GameMode mode = GameMode.Sandbox, bool threats = false) => SimWorld.CreateGalaxy(C, mode, Seed, threats);

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : -1;
    }

    [Fact]
    public void EveryTipHasTextInBothLanguages()
    {
        foreach (var key in new[] { "learn.ammo", "learn.repair", "learn.walls", "learn.research", "learn.upgrades", "learn.port" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es));
            Assert.False(string.IsNullOrWhiteSpace(en));
        }
    }

    [Fact]
    public void NothingIsShownInAQuietNewGame()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        for (int i = 0; i < 10; i++) { w.Step(Units.TicksPerSecond * 10); Assert.Null(tips.Next(w.Network, 0)); }
    }

    [Fact]
    public void ADamagedMachineTeachesRepairOnceAndAWaveWithoutWallsTeachesWalls()
    {
        var w = World(threats: true);
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        Ok(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:smelter"), C.RecipeIndex("core:smelt_iron")));
        w.Network.ScheduleWave(0);
        Tip? first = null;
        for (int i = 0; i < 20_000 && first is null; i++) { w.Step(); first = tips.Next(w.Network, 0); }
        Assert.Equal("learn.repair", first?.Key); // the smelter is hurt before anything is lost

        // Not again, and not within the gap.
        for (int i = 0; i < Units.TicksPerSecond * 10; i++) { w.Step(); Assert.Null(tips.Next(w.Network, 0)); }

        // Let the wave kill the lab: with no wall, the walls tip follows once the gap has passed.
        for (int i = 0; i < 8; i++) { w.Network.ScheduleWave(0); w.Step(C.Threat!.WarningTicks); }
        Tip? walls = null;
        for (int i = 0; i < 60_000 && walls is null; i++) { w.Step(); walls = tips.Next(w.Network, 0); }
        Assert.Equal("learn.walls", walls?.Key);
        Assert.True(w.Network.MachinesLost > 0);
    }

    [Fact]
    public void AnUnfedTurretUnderAttackTeachesAmmoFirst()
    {
        var w = World(threats: true);
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        Ok(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:smelter"), C.RecipeIndex("core:smelt_iron")));
        Ok(w, new PlaceMachine(6, 0, 0, C.MachineIndex("core:turret"), -1)); // never gets ammo
        w.Network.ScheduleWave(0);
        Tip? first = null;
        for (int i = 0; i < 20_000 && first is null; i++) { w.Step(); first = tips.Next(w.Network, 0); }
        Assert.Equal("learn.ammo", first?.Key);
    }

    [Fact]
    public void AnIdleLabAsksForAResearchChoiceAfterTwentySeconds()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        Ok(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:research_lab"), -1));
        for (int i = 0; i < Units.TicksPerSecond * 19; i++) { w.Step(); Assert.Null(tips.Next(w.Network, 0)); }
        Tip? tip = null;
        for (int i = 0; i < Units.TicksPerSecond * 5 && tip is null; i++) { w.Step(); tip = tips.Next(w.Network, 0); }
        Assert.Equal("learn.research", tip?.Key);

        // Choosing a research resets the wait and the tip is not repeated.
        Ok(w, new SetResearch(C.ResearchIndex("core:logistics")));
        for (int i = 0; i < Units.TicksPerSecond * 60; i++) { w.Step(); Assert.Null(tips.Next(w.Network, 0)); }
    }

    [Fact]
    public void DefenceUpgradesAreSuggestedAfterLogisticsWhenTheLabIsFree()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        Ok(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:research_lab"), -1));
        w.Network.GrantResearch(C.ResearchIndex("core:logistics"));
        Ok(w, new SetResearch(C.ResearchIndex("core:carbon_drilling"))); // keep the lab busy: the research tip must not fire
        for (int i = 0; i < Units.TicksPerSecond * 100; i++) { w.Step(); Assert.Null(tips.Next(w.Network, 0)); }
    }

    [Fact]
    public void AFreeLabAfterLogisticsGetsTheResearchTipThenTheUpgradesTip()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        Ok(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:research_lab"), -1));
        w.Network.GrantResearch(C.ResearchIndex("core:logistics"));
        var seen = new List<string>();
        for (int i = 0; i < Units.TicksPerSecond * 130; i++) { w.Step(); if (tips.Next(w.Network, 0) is { } t) seen.Add(t.Key); }
        Assert.Equal(new[] { "learn.research", "learn.upgrades" }, seen);
    }

    [Fact]
    public void TheCargoPortTipFollowsTheJumpDriveWhenTheOreRunsLow()
    {
        var w = new SimWorld(C, GameMode.Sandbox); // one small deposit and no galaxy: the whole ore counts
        w.Network.EnsurePlayer(0);
        w.Network.AddDeposit(C.ItemIndex("core:iron_ore"), 60);
        var tips = new Tips(C);
        w.Network.GrantResearch(C.ResearchIndex("core:jump_drive"));
        Assert.Null(tips.Next(w.Network, 0)); // plenty of ore left

        w.Submit(0, new StartMining(0));
        w.Step(15 * 50); // 50 of the 60 ore
        Assert.Equal("learn.port", tips.Next(w.Network, 0)?.Key);
        w.Step(Units.TicksPerSecond * 60);
        Assert.Null(tips.Next(w.Network, 0)); // once only
    }

    [Fact]
    public void TheCargoPortIsSuggestedWhenTheJumpDriveIsDoneAndTheHomeOreIsLow()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var tips = new Tips(C);
        w.Network.GrantResearch(C.ResearchIndex("core:jump_drive"));
        // Home ore is full: no suggestion yet.
        for (int i = 0; i < Units.TicksPerSecond * 30; i++) { w.Step(); Assert.NotEqual("learn.port", tips.Next(w.Network, 0)?.Key); }
    }

    private static (SimWorld W, int Station) StationWithSmelter()
    {
        var content = TestContent.CoreWithoutStarterShip;
        var w = SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        Ok(w, new PlaceModuleIn(st, 0, 0, 0, ModuleKind.Core));
        Ok(w, new PlaceModuleIn(st, 1, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceMachineIn(st, 9, 1, 1, content.MachineIndex("core:smelter"), -1));
        return (w, st);
    }

    [Fact]
    public void AStationWithMachinesButNoReactorTeachesTheReactorOnce()
    {
        var (w, st) = StationWithSmelter();
        var tips = new Tips(TestContent.CoreWithoutStarterShip);
        var tip = tips.Next(w.Network, 0);
        Assert.Equal("learn.noReactor", tip?.Key);
        w.Step(Units.TicksPerSecond * 60);
        Assert.NotEqual("learn.noReactor", tips.Next(w.Network, 0)?.Key); // once only
        Assert.True(Loc.Has("learn.noReactor"));
    }

    [Fact]
    public void AStationWithAReactorGetsNoReactorTip()
    {
        var (w, st) = StationWithSmelter();
        Ok(w, new PlaceModuleIn(st, 0, 0, 1, ModuleKind.Reactor));
        var tips = new Tips(TestContent.CoreWithoutStarterShip);
        Assert.NotEqual("learn.noReactor", tips.Next(w.Network, 0)?.Key);
    }

    [Fact]
    public void OpeningTheMapWithoutJumpCarbonTeachesTheCostOnce()
    {
        var content = TestContent.CoreWithoutStarterShip;
        var w = SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        var tips = new Tips(content);
        Assert.NotEqual("learn.jumpCarbon", tips.Next(w.Network, 0, mapOpen: false)?.Key);
        var tip = tips.Next(w.Network, 0, mapOpen: true);
        Assert.Equal("learn.jumpCarbon", tip?.Key);
        Assert.Equal(content.JumpCost[0].Count, tip!.Args[0]);
        w.Step(Units.TicksPerSecond * 60);
        Assert.NotEqual("learn.jumpCarbon", tips.Next(w.Network, 0, mapOpen: true)?.Key);
    }

    [Fact]
    public void NoJumpCarbonTipWhenTheHoldPaysForTheJump()
    {
        var content = TestContent.CoreWithoutStarterShip;
        var w = SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        foreach (var stack in content.JumpCost) w.Network.GiveItems(0, stack.Item, stack.Count);
        var tips = new Tips(content);
        Assert.NotEqual("learn.jumpCarbon", tips.Next(w.Network, 0, mapOpen: true)?.Key);
    }
}
