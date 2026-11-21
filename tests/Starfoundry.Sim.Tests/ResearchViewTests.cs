using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ResearchViewTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static int R(string id) => C.ResearchIndex(id);

    private static BeltNetwork Net()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261121UL, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        return w.Network;
    }

    private static ResearchEntry Get(BeltNetwork net, string id) => ResearchView.Entry(C, net, 0, C.Research[R(id)]);

    [Fact]
    public void EveryNodeIsListedOnceAndGroupedByState()
    {
        var net = Net();
        net.GrantResearch(R("core:logistics"));
        net.SetResearch(R("core:carbon_drilling"));
        var all = ResearchView.Build(C, net, 0);
        Assert.Equal(C.Research.Count, all.Count);
        Assert.Equal(C.Research.Count, all.Select(e => e.Index).Distinct().Count());
        // The groups come in the panel's order: researching, available, locked, done.
        for (int i = 1; i < all.Count; i++) Assert.True(all[i - 1].State <= all[i].State);
        Assert.Equal(ResearchState.Researching, all[0].State);
        Assert.Equal(R("core:carbon_drilling"), all[0].Index);
    }

    [Fact]
    public void StatesFollowRequirementsAndTheChosenNode()
    {
        var net = Net();
        Assert.Equal(ResearchState.Available, Get(net, "core:logistics").State);
        Assert.Equal(ResearchState.Available, Get(net, "core:carbon_drilling").State);
        Assert.Equal(ResearchState.Locked, Get(net, "core:advanced_logistics").State);
        Assert.Equal(ResearchState.Locked, Get(net, "core:turret_calibration").State);
        Assert.True(Get(net, "core:logistics").CanStart);
        Assert.False(Get(net, "core:advanced_logistics").CanStart);

        net.SetResearch(R("core:logistics"));
        Assert.Equal(ResearchState.Researching, Get(net, "core:logistics").State);
        Assert.False(Get(net, "core:logistics").CanStart); // already the current one

        net.GrantResearch(R("core:logistics"));
        Assert.Equal(ResearchState.Done, Get(net, "core:logistics").State);
        Assert.Equal(ResearchState.Available, Get(net, "core:turret_calibration").State);
        Assert.Equal(ResearchState.Locked, Get(net, "core:advanced_logistics").State); // still needs carbon drilling
    }

    [Fact]
    public void PrerequisitesListTheirNodesAndWhatIsMissing()
    {
        var net = Net();
        var adv = Get(net, "core:advanced_logistics");
        Assert.Equal(new[] { "core:logistics", "core:carbon_drilling" }, adv.Requires.Select(r => r.Id));
        Assert.All(adv.Requires, r => Assert.False(r.Done));
        Assert.Equal(2, adv.Missing.Count());
        Assert.Empty(Get(net, "core:logistics").Requires);

        net.GrantResearch(R("core:logistics"));
        adv = Get(net, "core:advanced_logistics");
        Assert.Equal(new[] { "core:carbon_drilling" }, adv.Missing.Select(r => r.Id));
        Assert.True(adv.Requires.Single(r => r.Id == "core:logistics").Done);
    }

    [Fact]
    public void LockedReasonOnlyExistsForLockedNodes()
    {
        var net = Net();
        string reason = ResearchView.LockedReason(Get(net, "core:advanced_logistics"));
        Assert.NotEmpty(reason);
        Assert.False(reason.StartsWith("⟦"), "missing Loc key: " + reason);
        Assert.Equal("", ResearchView.LockedReason(Get(net, "core:logistics")));
        net.GrantResearch(R("core:advanced_logistics"));
        Assert.Equal("", ResearchView.LockedReason(Get(net, "core:advanced_logistics")));
    }

    [Fact]
    public void CostLinesCarryTheNeedTheLabProgressAndTheHold()
    {
        var net = Net();
        var plate = C.ItemIndex("core:iron_plate");
        var carbon = C.ItemIndex("core:carbon");
        net.GiveItems(0, plate, 12);
        var adv = Get(net, "core:advanced_logistics");
        Assert.Equal(2, adv.Cost.Count);
        var p = adv.Cost.Single(c => c.Item == plate);
        Assert.Equal((300, 0, 12), (p.Needed, p.Delivered, p.InHold));
        Assert.False(p.Have);
        var c75 = adv.Cost.Single(c => c.Item == carbon);
        Assert.Equal((75, 0, 0), (c75.Needed, c75.Delivered, c75.InHold));
        Assert.Equal(0.0, adv.Progress);
        Assert.Equal(C.Research[R("core:logistics")].Cost, new[] { new ItemStack(plate, 90) });
        Assert.Equal(90, Get(net, "core:logistics").Cost.Single().Needed);
    }

    [Fact]
    public void ProgressFollowsWhatAFedLabTakes()
    {
        // A sandbox world: a source belt of plates feeds a lab, so the panel's progress is the real one.
        var w = new SimWorld(C, GameMode.Sandbox);
        w.Network.EnsurePlayer(0);
        int lab = w.Network.AddMachine(3, 0, 0, C.MachineIndex("core:research_lab"), -1);
        int belt = w.Network.AddSegment(0, 0, 0, 1, 0, 0, 3, BeltTier.Mk1);
        w.Network.SetSource(belt, new ushort[] { (ushort)C.ItemIndex("core:iron_plate") });
        w.Network.ConnectToMachine(belt, lab);
        w.Network.SetResearch(R("core:logistics"));
        var node = C.Research[R("core:logistics")];

        ResearchEntry E() => ResearchView.Entry(C, w.Network, 0, node);
        Assert.Equal(0.0, E().Progress);
        w.Step(Units.TicksPerSecond * 8);
        double early = E().Progress;
        Assert.InRange(early, 0.05, 0.99);
        Assert.Equal(ResearchState.Researching, E().State);
        Assert.False(E().Cost.Single().Have);
        Assert.Equal(early, E().Cost.Single().Delivered / 90.0, 6);

        for (int i = 0; i < 60 && !w.Network.IsResearched(R("core:logistics")); i++) w.Step(Units.TicksPerSecond);
        Assert.True(w.Network.IsResearched(R("core:logistics")));
        var done = E();
        Assert.Equal(ResearchState.Done, done.State);
        Assert.Equal(1.0, done.Progress);
    }

    [Fact]
    public void SwitchingAwayKeepsAndShowsTheProgressOfTheNode()
    {
        var w = new SimWorld(C, GameMode.Sandbox);
        w.Network.EnsurePlayer(0);
        int lab = w.Network.AddMachine(3, 0, 0, C.MachineIndex("core:research_lab"), -1);
        int belt = w.Network.AddSegment(0, 0, 0, 1, 0, 0, 3, BeltTier.Mk1);
        w.Network.SetSource(belt, new ushort[] { (ushort)C.ItemIndex("core:iron_plate") });
        w.Network.ConnectToMachine(belt, lab);
        w.Network.SetResearch(R("core:logistics"));
        w.Step(Units.TicksPerSecond * 6);
        w.Network.SetResearch(R("core:carbon_drilling"));
        var logistics = ResearchView.Entry(C, w.Network, 0, C.Research[R("core:logistics")]);
        Assert.Equal(ResearchState.Available, logistics.State);
        Assert.True(logistics.Progress > 0);
    }

    [Fact]
    public void UnlocksListMachinesRecipesBeltsAbilitiesAndBonuses()
    {
        var net = Net();
        var logistics = Get(net, "core:logistics");
        Assert.Equal(new[] { new ResearchUnlock(ResearchUnlockKind.Belt, "mk2") }, logistics.Unlocks);

        var jump = Get(net, "core:jump_drive").Unlocks;
        Assert.Contains(new ResearchUnlock(ResearchUnlockKind.Ability, "jump"), jump);
        Assert.Contains(new ResearchUnlock(ResearchUnlockKind.Machine, "core:unloader"), jump);
        Assert.Contains(new ResearchUnlock(ResearchUnlockKind.Machine, "core:ore_unloader"), jump);

        var ti = Get(net, "core:titanium_smelting").Unlocks;
        Assert.Contains(new ResearchUnlock(ResearchUnlockKind.Recipe, "core:smelt_titanium"), ti);
        Assert.Contains(new ResearchUnlock(ResearchUnlockKind.Machine, "core:titanium_ore_unloader"), ti);

        Assert.Equal(new[] { new ResearchUnlock(ResearchUnlockKind.Bonus, "turret_damage", 25) }, Get(net, "core:turret_calibration").Unlocks);
        Assert.Equal(new[] { new ResearchUnlock(ResearchUnlockKind.Ability, "drone_bay") }, Get(net, "core:drone_automation").Unlocks);
        Assert.All(C.Research, r => Assert.NotEmpty(ResearchView.Unlocks(C, r))); // every node gives something
    }

    [Fact]
    public void RepeatableNodesShowLevelsAndOnlyFinishAtTheLast()
    {
        var net = Net();
        net.GrantResearch(R("core:turret_calibration"));
        var overclock = Get(net, "core:overclock");
        Assert.True(overclock.Repeatable);
        Assert.Equal((0, 8), (overclock.Level, overclock.MaxLevels));
        Assert.Equal(ResearchState.Available, overclock.State);

        net.GrantResearch(R("core:overclock"), 3);
        overclock = Get(net, "core:overclock");
        Assert.Equal(3, overclock.Level);
        Assert.Equal(ResearchState.Available, overclock.State); // the next level can be researched
        Assert.True(overclock.Cost[0].Needed > C.Research[R("core:overclock")].Cost[0].Count); // and costs more

        net.GrantResearch(R("core:overclock"), 8);
        Assert.Equal(ResearchState.Done, Get(net, "core:overclock").State);
    }

    [Fact]
    public void EveryNodeHasADescriptionAndEveryStateAName()
    {
        foreach (var r in C.Research) Assert.True(Loc.Has(Get(Net(), r.Id).DescriptionKey), r.Id);
        foreach (var s in Enum.GetValues<ResearchState>()) Assert.True(Loc.Has("research.state." + s.ToString().ToLowerInvariant()), s.ToString());
        var net = Net();
        foreach (var e in ResearchView.Build(C, net, 0))
        {
            foreach (var u in e.Unlocks) Assert.DoesNotContain("⟦", ResearchView.UnlockText(u));
            Assert.DoesNotContain("⟦", ResearchView.Name(e));
        }
    }
}
