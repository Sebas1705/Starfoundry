using System.Text.RegularExpressions;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

[Collection("LocalisedText")]
public class UiTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly Regex Placeholder = new(@"\{(\d+)\}");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starfoundry.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Starfoundry.sln not found");
    }

    private static string Slots(string text) =>
        string.Join(",", Placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s));

    [Fact]
    public void EveryKeyHasBothLanguagesWithMatchingPlaceholders()
    {
        foreach (var (key, (es, en)) in Loc.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(es), $"{key}: empty Spanish text");
            Assert.False(string.IsNullOrWhiteSpace(en), $"{key}: empty English text");
            Assert.True(Slots(es) == Slots(en), $"{key}: placeholders differ between es and en");
        }
    }

    [Fact]
    public void GameDataHasNames()
    {
        foreach (var i in C.Items) Assert.True(Loc.Has("item." + i.Id), "item " + i.Id);
        foreach (var m in C.Machines) Assert.True(Loc.Has("machine." + m.Id), "machine " + m.Id);
        foreach (var r in C.Recipes) Assert.True(Loc.Has("recipe." + r.Id), "recipe " + r.Id);
        foreach (var r in C.Research) Assert.True(Loc.Has("research." + r.Id), "research " + r.Id);
        foreach (var e in C.Enemies) Assert.True(Loc.Has("enemy." + e.Id), "enemy " + e.Id);
    }

    [Fact]
    public void EveryLiteralKeyUsedByTheGameExists()
    {
        var pattern = new Regex(@"\b(?:L|Loc\.T)\(\s*""([^""]+)""(?!\s*\+)");
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "game"), "*.cs"))
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                if (!Loc.Has(m.Groups[1].Value)) missing.Add(Path.GetFileName(file) + ": " + m.Groups[1].Value);
        Assert.True(missing.Count == 0, "Missing keys:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryMachineRoleHasATooltip()
    {
        foreach (var role in Enum.GetValues<MachineRole>())
            Assert.True(Loc.Has("tip.role." + role.ToString().ToLowerInvariant()), role.ToString());
    }

    [Fact]
    public void ObjectiveKeysExist()
    {
        Assert.True(Loc.Has("obj.title"));
        Assert.True(Loc.Has("obj.allDone"));
        foreach (var s in new ObjectiveTracker(C).States)
        {
            Assert.True(Loc.Has(s.TitleKey), s.TitleKey);
            Assert.True(Loc.Has(s.HintKey), s.HintKey);
        }
    }

    [Fact]
    public void TheRetiredGoalSliderHasNoTextLeft()
    {
        Assert.False(Loc.Has("settings.goalWaves"));
        Assert.False(Loc.Has("settings.tip.goalWaves"));
        Assert.False(Loc.Has("build.bar"));
        Assert.False(Loc.Has("build.barTip"));
    }

    [Fact]
    public void UnknownKeysAreVisibleAndNamesFallBack()
    {
        Assert.Equal("⟦nope⟧", Loc.T("nope"));
        Assert.Equal("some thing", Loc.Name("item", "mod:some_thing"));
    }

    [Fact]
    public void LanguageSwitchesTextAndNumberFormat()
    {
        var old = Loc.Lang;
        try
        {
            Loc.Lang = Language.Es;
            Assert.Equal("Bodega 3/10", Loc.T("hud.hold", 3, 10));
            Assert.Equal("1,5", string.Format(Loc.Culture, "{0}", 1.5));
            Loc.Lang = Language.En;
            Assert.Equal("Hold 3/10", Loc.T("hud.hold", 3, 10));
            Assert.Equal(Language.Es, Loc.FromLocale("es_ES"));
            Assert.Equal(Language.En, Loc.FromLocale("fr"));
            Assert.Equal(Language.En, Loc.FromLocale(null));
        }
        finally { Loc.Lang = old; }
    }

    [Fact]
    public void TheOreRunwayShrinksAsTheDepositIsMinedAndWarnsAtAQuarter()
    {
        var w = new SimWorld(C, GameMode.Campaign);
        int ore = C.ItemIndex("core:iron_ore"), carbon = C.ItemIndex("core:carbon");
        w.Network.AddDeposit(ore, 60); // the hold keeps 60, so mining can take it all
        Assert.Equal(100, OreRunway.PercentLeft(w.Network, 0, ore));
        Assert.Equal(-1, OreRunway.PercentLeft(w.Network, 0, carbon)); // no carbon deposit is known
        Assert.False(OreRunway.IsLow(-1));
        Assert.False(OreRunway.IsLow(100));

        w.Submit(0, new StartMining(0));
        w.Step(15 * 30); // 30 ore at 2 a second is 15 seconds
        Assert.InRange(OreRunway.PercentLeft(w.Network, 0, ore), 48, 51);
        Assert.False(OreRunway.IsLow(OreRunway.PercentLeft(w.Network, 0, ore)));

        w.Step(15 * 20); // 20 more
        int pct = OreRunway.PercentLeft(w.Network, 0, ore);
        Assert.InRange(pct, 15, 18);
        Assert.True(OreRunway.IsLow(pct));
        Assert.True(OreRunway.IsLow(OreRunway.LowPercent));
        Assert.False(OreRunway.IsLow(OreRunway.LowPercent + 1));
    }

    [Fact]
    public void TheOreRunwayOnlyCountsTheSystemTheShipIsIn()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 20261120UL);
        w.Network.EnsurePlayer(0);
        int ore = C.ItemIndex("core:iron_ore");
        Assert.Equal(100, OreRunway.PercentLeft(w.Network, 0, ore)); // the home iron, untouched

        // Jump to a neighbour that makes iron and scan it.
        var g = w.Network.Galaxy!;
        int target = g.Systems[0].Lanes.First(n => C.Archetypes[g.Systems[n].Archetype].Deposits.Any(d => d.Item == ore));
        w.Submit(0, new Jump(target));
        w.Step(ExplorationRules.JumpChargeTicks + 5);
        Assert.Equal(target, w.Network.CurrentSystem(0));
        Assert.Equal(-1, OreRunway.PercentLeft(w.Network, 0, ore)); // nothing revealed here yet: home's ore does not leak in

        w.Submit(0, new Scan());
        w.Step();
        Assert.Equal(100, OreRunway.PercentLeft(w.Network, 0, ore)); // untouched ore of the new system, not the mined home deposit
    }

    [Fact]
    public void FreshWorldStartsAtTheFirstObjective()
    {
        var w = new SimWorld(C, GameMode.Campaign);
        var t = new ObjectiveTracker(C);
        t.Update(w.Network, 0);
        Assert.Equal(0, t.DoneCount);
        Assert.Equal(Objective.Fly, t.Current);
    }

    [Fact]
    public void ObjectivesCompleteAndStayDone()
    {
        var w = new SimWorld(C, GameMode.Campaign);
        w.Network.AddDeposit(C.ItemIndex("core:iron_ore"), 500);
        var t = new ObjectiveTracker(C);
        t.Update(w.Network, 0);
        // The hand starts at the origin, in range of the first deposit.
        Assert.True(t.IsDone(Objective.Fly) || t.Current == Objective.Fly);
        bool wasDone = t.IsDone(Objective.Fly);
        t.Update(w.Network, 0);
        Assert.Equal(wasDone, t.IsDone(Objective.Fly)); // stable between updates
        Assert.False(t.IsDone(Objective.Defend));
        Assert.Equal(t.States.Count(s => s.Done), t.DoneCount);
    }
}
