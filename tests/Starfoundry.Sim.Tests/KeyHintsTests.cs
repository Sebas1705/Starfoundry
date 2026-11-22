using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class KeyHintsTests
{
    private static KeyState Flying() => new() { HasGalaxy = true, StructuresOn = true, ModuleToolAvailable = true, CanWalk = true, ThreatActive = true };

    private static IReadOnlyList<KeyRow> Rows(KeyState s) => KeyHints.For(s).Last().Rows;

    private static string[] Ids(KeyState s) => Rows(s).Select(r => r.Id).ToArray();

    private static KeyRow Find(KeyState s, string id) => KeyHints.For(s).SelectMany(g => g.Rows).First(r => r.Id == id);

    [Fact]
    public void FlyingShowsTheShipKeysInOrder()
    {
        var s = Flying();
        Assert.Equal(HintContext.Flying, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "build", "research", "map", "scan", "mine", "refine", "weapons", "found", "module", "seat", "help" }, Ids(s));
        Assert.Equal(new[] { "B", "T", "G", "K", "M", "F", "V", "I", "N", "E", "F1" }, Rows(s).Select(r => r.Caps[0]).ToArray());
        Assert.Equal("key.ctx.flying", KeyHints.For(s).Last().TitleKey);
    }

    [Fact]
    public void FlyingDropsWhatTheWorldDoesNotHave()
    {
        var s = Flying() with { HasGalaxy = false, StructuresOn = false, ModuleToolAvailable = false, CanWalk = false };
        Assert.Equal(new[] { "build", "research", "scan", "mine", "refine", "weapons", "help" }, Ids(s));
    }

    [Fact]
    public void ActiveStatesLightTheRows()
    {
        var s = Flying() with { Mining = true, Refining = true, Armed = true, MapOpen = false };
        Assert.Equal(KeyRowState.Active, Find(s, "mine").State);
        Assert.Equal("key.mineStop", Find(s, "mine").LabelKey);
        Assert.Equal(KeyRowState.Active, Find(s, "refine").State);
        Assert.Equal(KeyRowState.Active, Find(s, "weapons").State);
        Assert.Equal(KeyRowState.Normal, Find(Flying(), "mine").State);
    }

    [Fact]
    public void UnavailableRowsAreDimmedWithAReason()
    {
        var s = Flying() with { ScanReady = false, ThreatActive = false };
        var scan = Find(s, "scan");
        Assert.Equal(KeyRowState.Disabled, scan.State);
        Assert.Equal("key.why.scanWait", scan.ReasonKey);
        Assert.False(scan.Clickable);
        Assert.Equal("key.why.noThreats", Find(s, "weapons").ReasonKey);
        Assert.True(Find(Flying(), "scan").Clickable);
    }

    [Fact]
    public void WalkingKeepsTheWalkKeysAndTheSeatRow()
    {
        var s = Flying() with { Standing = true };
        Assert.Equal(HintContext.Walking, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "walk", "run", "seat", "build", "research", "found", "module", "help" }, Ids(s));
        Assert.Equal("WASD", Find(s, "walk").Caps[0]);
        Assert.Equal(KeyRowState.Info, Find(s, "walk").State);
        Assert.Equal(KeyRowState.Disabled, Find(s, "seat").State);
        Assert.Equal("key.why.farSeat", Find(s, "seat").ReasonKey);
        var near = s with { NearSeat = true };
        Assert.Equal("E", Find(near, "seat").Caps[0]);
        Assert.True(Find(near, "seat").Clickable);
        Assert.Equal("key.sit", Find(near, "seat").LabelKey);
    }

    [Fact]
    public void AMachineToolShowsPlacementKeys()
    {
        var s = Flying() with { Tool = HintTool.Machine, ToolName = "smelter", ToolHasRecipes = true };
        Assert.Equal(HintContext.Tool, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "place", "layer", "recipe", "demolish", "quick", "build", "cancel" }, Ids(s));
        Assert.Equal(new object[] { "smelter" }, KeyHints.For(s).Last().TitleArgs);
        Assert.Equal(KeyHints.QuickCap, Find(s, "quick").Caps[0]);
        Assert.DoesNotContain("recipe", Ids(s with { ToolHasRecipes = false }));
        Assert.Equal("Escape", Find(s, "cancel").ActionKey);
    }

    [Fact]
    public void ABeltToolAddsRotateAndTracksTheLine()
    {
        var s = Flying() with { Tool = HintTool.Belt, ToolName = "belt" };
        Assert.Contains("rotate", Ids(s));
        Assert.Equal("key.beltStart", Find(s, "place").LabelKey);
        var anchored = s with { BeltAnchored = true };
        Assert.Equal("key.beltEnd", Find(anchored, "place").LabelKey);
        Assert.Equal(KeyRowState.Active, Find(anchored, "place").State);
        Assert.Equal("key.cancelLine", Find(anchored, "demolish").LabelKey);
        Assert.DoesNotContain("rotate", Ids(Flying() with { Tool = HintTool.Machine }));
    }

    [Fact]
    public void ModuleAndStationToolsHaveTheirOwnRows()
    {
        var module = Flying() with { Tool = HintTool.Module, ToolName = "reactor" };
        Assert.Equal(new[] { "place", "kind", "demolish", "quick", "build", "cancel" }, Ids(module));
        Assert.Equal("key.removeModule", Find(module, "demolish").LabelKey);
        Assert.Equal("N", Find(module, "kind").Caps[0]);
        var station = Flying() with { Tool = HintTool.Station, ToolName = "station" };
        Assert.Equal(new[] { "place", "quick", "build", "cancel" }, Ids(station));
    }

    [Fact]
    public void AimingAtAMachine()
    {
        var s = Flying() with { Aim = HintAim.Machine, AimName = "drill", AimHasRecipes = true, AimDamaged = false };
        Assert.Equal(HintContext.AimMachine, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "recipe", "inspect", "demolish", "repair", "build" }, Ids(s));
        Assert.Equal(KeyRowState.Disabled, Find(s, "repair").State);
        Assert.Equal(KeyRowState.Normal, Find(s with { AimDamaged = true }, "repair").State);
        Assert.DoesNotContain("recipe", Ids(s with { AimHasRecipes = false }));
    }

    [Fact]
    public void AimingAtADepositOffersMiningAndDrones()
    {
        var s = Flying() with { Aim = HintAim.Deposit };
        Assert.Equal(HintContext.AimDeposit, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "choose", "mine", "drones", "build" }, Ids(s));
        Assert.Equal("key.why.noBay", Find(s, "drones").ReasonKey);
        Assert.Equal("key.why.droneLocked", Find(s with { DroneBayLocked = true }, "drones").ReasonKey);
        Assert.True(Find(s with { DroneBayBuilt = true }, "drones").Clickable);
        Assert.DoesNotContain("drones", Ids(s with { StructuresOn = false }));
    }

    [Fact]
    public void AimingAtADamagedModuleShowsItsHitPoints()
    {
        var s = Flying() with { Aim = HintAim.Module, AimName = "reactor", AimHp = 40, AimMaxHp = 100 };
        Assert.Equal(HintContext.AimModule, KeyHints.ContextOf(s));
        var hp = Find(s, "hp");
        Assert.Equal(KeyRowState.Info, hp.State);
        Assert.Equal(new object[] { 40, 100 }, hp.LabelArgs);
        Assert.Empty(hp.Caps);
    }

    [Fact]
    public void TheStarMapShowsArrowsJumpAndClose()
    {
        var s = Flying() with { MapOpen = true, JumpUnlocked = true, SystemChosen = true, JumpCost = "3 carbon" };
        Assert.Equal(HintContext.StarMap, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "choose", "jump", "close" }, Ids(s));
        var jump = Find(s, "jump");
        Assert.Equal(new object[] { "3 carbon" }, jump.LabelArgs);
        Assert.Equal("key.jumpCost", jump.LabelKey);
        Assert.True(jump.Clickable);
        Assert.Equal(KeyRowState.Alert, Find(s with { JumpAffordable = false }, "jump").State);
        Assert.Equal("key.why.jumpLocked", Find(s with { JumpUnlocked = false }, "jump").ReasonKey);
        Assert.Equal("key.why.noSystem", Find(s with { SystemChosen = false }, "jump").ReasonKey);
        Assert.Equal(HintContext.Flying, KeyHints.ContextOf(s with { HasGalaxy = false, MapOpen = true }));
    }

    [Fact]
    public void PanelsShowTheirOwnKeysAndBeatEverythingElse()
    {
        var busy = Flying() with { Tool = HintTool.Machine, Aim = HintAim.Machine, MapOpen = true };
        Assert.Equal(new[] { "choose", "pick", "tab", "close", "esc" }, Ids(busy with { BuildOpen = true }));
        Assert.Equal(HintContext.BuildMenu, KeyHints.ContextOf(busy with { BuildOpen = true, ResearchOpen = true }));
        Assert.Equal(new[] { "choose", "start", "close", "esc" }, Ids(busy with { ResearchOpen = true }));
        Assert.Equal(new[] { "close", "esc" }, Ids(busy with { HelpOpen = true }));
        Assert.Equal(HintContext.Pause, KeyHints.ContextOf(busy with { PauseOpen = true, HelpOpen = true }));
        Assert.Equal("PgUp", Find(busy with { BuildOpen = true }, "tab").Caps[0]);
    }

    [Fact]
    public void AThreatPutsAHighlightedGroupOnTop()
    {
        var calm = Flying();
        Assert.Single(KeyHints.For(calm));
        var s = calm with { ThreatNow = true, AnythingDamaged = true };
        var groups = KeyHints.For(s);
        Assert.Equal(2, groups.Count);
        Assert.Equal("key.ctx.alert", groups[0].TitleKey);
        Assert.Equal(new[] { "weapons", "repair" }, groups[0].Rows.Select(r => r.Id).ToArray());
        Assert.All(groups[0].Rows, r => Assert.Equal(KeyRowState.Alert, r.State));
        Assert.DoesNotContain("weapons", groups[1].Rows.Select(r => r.Id));
        Assert.Equal(KeyRowState.Active, KeyHints.For(s with { Armed = true })[0].Rows[0].State);
        Assert.Equal(KeyRowState.Disabled, KeyHints.For(s with { AnythingDamaged = false })[0].Rows[1].State);
    }

    [Fact]
    public void TheThreatGroupAppliesInEveryWorldContextButNotInPanels()
    {
        var threat = Flying() with { ThreatNow = true };
        foreach (var s in new[] { threat with { Standing = true }, threat with { Tool = HintTool.Belt }, threat with { Aim = HintAim.Deposit }, threat with { Aim = HintAim.Machine } })
            Assert.Equal("key.ctx.alert", KeyHints.For(s)[0].TitleKey);
        foreach (var s in new[] { threat with { BuildOpen = true }, threat with { ResearchOpen = true }, threat with { MapOpen = true }, threat with { HelpOpen = true } })
            Assert.Single(KeyHints.For(s));
        Assert.Single(KeyHints.For(Flying() with { ThreatActive = false, ThreatNow = true }));
    }

    [Fact]
    public void EveryContextHasUniqueRowsAndEveryTextExistsInBothLanguages()
    {
        var states = new List<KeyState>();
        var flying = Flying();
        foreach (var threat in new[] { false, true })
            foreach (var standing in new[] { false, true })
            foreach (var near in new[] { false, true })
            foreach (var mining in new[] { false, true })
            {
                var b = flying with { ThreatNow = threat, Standing = standing, NearSeat = near, Mining = mining, AnythingDamaged = near };
                states.Add(b);
                foreach (var tool in Enum.GetValues<HintTool>()) states.Add(b with { Tool = tool, ToolHasRecipes = near, BeltAnchored = mining });
                foreach (var aim in Enum.GetValues<HintAim>()) states.Add(b with { Aim = aim, AimHasRecipes = near, AimDamaged = mining, DroneBayBuilt = near, DroneBayLocked = mining });
                states.Add(b with { MapOpen = true, JumpUnlocked = near, SystemChosen = mining, JumpAffordable = standing, JumpCost = threat ? "1 carbon" : "" });
                states.Add(b with { BuildOpen = true });
                states.Add(b with { ResearchOpen = true });
                states.Add(b with { HelpOpen = true });
                states.Add(b with { PauseOpen = true });
            }
        var contexts = new HashSet<HintContext>();
        foreach (var s in states)
        {
            contexts.Add(KeyHints.ContextOf(s));
            var groups = KeyHints.For(s);
            foreach (var g in groups)
            {
                Assert.True(Loc.Has(g.TitleKey), g.TitleKey);
                Assert.Equal(g.Rows.Count, g.Rows.Select(r => r.Id).Distinct().Count());
                foreach (var r in g.Rows)
                {
                    Assert.True(Loc.Has(r.LabelKey), r.LabelKey);
                    if (r.ReasonKey is not null) Assert.True(Loc.Has(r.ReasonKey), r.ReasonKey);
                    if (r.State == KeyRowState.Disabled) Assert.NotNull(r.ReasonKey);
                    foreach (var cap in r.Caps)
                    {
                        Assert.NotEmpty(cap);
                        if (cap.StartsWith('@')) Assert.True(Loc.Has("key.cap." + cap[1..]), cap);
                    }
                    if (r.ActionKey is not null) Assert.Contains(r.ActionKey, new[] { "B", "T", "G", "K", "M", "F", "V", "I", "N", "E", "R", "Y", "J", "F1", "F7", "Escape", "Backspace" });
                }
            }
        }
        Assert.Equal(Enum.GetValues<HintContext>().Length, contexts.Count);
    }

    [Fact]
    public void LabelsFormatInBothLanguages()
    {
        var old = Loc.Lang;
        try
        {
            foreach (var lang in new[] { Language.Es, Language.En })
            {
                Loc.Lang = lang;
                var jump = Find(Flying() with { MapOpen = true, JumpUnlocked = true, SystemChosen = true, JumpCost = "3 carbon" }, "jump");
                Assert.Contains("3 carbon", Loc.T(jump.LabelKey, jump.LabelArgs!));
                var hp = Find(Flying() with { Aim = HintAim.Module, AimHp = 7, AimMaxHp = 9 }, "hp");
                Assert.Contains("7/9", Loc.T(hp.LabelKey, hp.LabelArgs!));
            }
        }
        finally { Loc.Lang = old; }
    }
}
