using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class KeyHintsTests
{
    private static KeyState Flying() => new() { HasGalaxy = true, StructuresOn = true, ModuleToolAvailable = true, CanWalk = true, ThreatActive = true, HasTurnInertia = true };

    private static IReadOnlyList<KeyRow> Rows(KeyState s) => KeyHints.For(s).Last().Rows;

    private static string[] Ids(KeyState s) => Rows(s).Select(r => r.Id).ToArray();

    private static KeyRow Find(KeyState s, string id) => KeyHints.For(s).SelectMany(g => g.Rows).First(r => r.Id == id);

    [Fact]
    public void FlyingShowsTheShipKeysInOrder()
    {
        var s = Flying();
        Assert.Equal(HintContext.Flying, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "interact", "computer", "weapons", "scan", "roll", "assist", "hudDetail", "help" }, Ids(s)); // the fire row gives way first when the panel is full
        Assert.Equal(new[] { "F", "Tab", "V", "K", "Q", "Z", "H", "F1" }, Rows(s).Select(r => r.Caps[0]).ToArray());
        Assert.Equal("Q", Find(s, "roll").Caps[0]);
        Assert.Equal("E", Find(s, "roll").Caps[1]);
        Assert.Equal("key.ctx.flying", KeyHints.For(s).Last().TitleKey);
    }

    [Fact]
    public void FlyingDropsWhatTheWorldDoesNotHave()
    {
        var s = Flying() with { HasTurnInertia = false };
        Assert.DoesNotContain("assist", Ids(s));
        Assert.Contains("hudDetail", Ids(s));
    }

    [Fact]
    public void ActiveStatesLightTheRows()
    {
        var s = Flying() with { WeaponGroup = WeaponGroup.Guns, Armed = true, FlightAssist = false };
        Assert.Equal(KeyRowState.Active, Find(s, "weapons").State);
        Assert.Equal("key.weaponGuns", Find(s, "weapons").LabelKey);
        Assert.Equal(KeyRowState.Active, Find(s, "fireGuns").State);
        Assert.Equal(KeyRowState.Normal, Find(Flying(), "weapons").State);
        Assert.Equal("key.weaponLaser", Find(Flying(), "weapons").LabelKey);
        Assert.Equal("key.assistOff", Find(s, "assist").LabelKey);
        Assert.Equal(KeyRowState.Active, Find(s, "assist").State);
        Assert.Equal("Z", Find(s, "assist").ActionKey);
    }

    [Fact]
    public void TheWeaponInHandAddsItsFireRow()
    {
        var held = Flying() with { HasTurnInertia = false };
        Assert.Contains("mineFire", Ids(held));
        Assert.DoesNotContain("fireGuns", Ids(held));
        Assert.Equal("@hold", Find(held, "mineFire").Caps[0]);
        Assert.Equal(KeyRowState.Active, Find(held with { LaserFiring = true }, "mineFire").State);
        var guns = held with { WeaponGroup = WeaponGroup.Guns };
        Assert.Contains("fireGuns", Ids(guns));
        Assert.DoesNotContain("mineFire", Ids(guns));
        Assert.Equal(new[] { "choose", "mineFire", "drones", "computer" }, Ids(held with { Aim = HintAim.Deposit }));
        Assert.Equal(new[] { "choose", "weapons", "drones", "computer" }, Ids(guns with { Aim = HintAim.Deposit })); // at a deposit with the guns out: V takes the laser
    }

    [Fact]
    public void TheInteractRowSaysWhatFDoes()
    {
        Assert.Equal("key.interact", Find(Flying(), "interact").LabelKey);
        Assert.Equal("F", Find(Flying(), "interact").ActionKey);
        Assert.Equal("key.undock", Find(Flying() with { Interact = HintInteract.Undock }, "interact").LabelKey);
        Assert.Equal("key.dockAssist", Find(Flying() with { Interact = HintInteract.DockAssist }, "interact").LabelKey);
        Assert.Equal(KeyRowState.Active, Find(Flying() with { Interact = HintInteract.DockCancel }, "interact").State);
        Assert.Equal("key.stand", Find(Flying() with { Interact = HintInteract.Stand }, "interact").LabelKey);
    }

    [Fact]
    public void DockedHidesFlightRowsAndShowsTheTransferKeys()
    {
        var s = Flying() with { Dock = DockHint.Docked, Interact = HintInteract.Undock, StockUsed = 3, StockCapacity = 10 };
        Assert.Equal(new[] { "interact", "unload", "load", "stock", "computer", "weapons", "mineFire", "hudDetail", "help" }.Where(i => i != "mineFire").ToArray(), Ids(s));
        Assert.Equal(",", Find(s, "unload").Caps[0]);
        Assert.Equal(".", Find(s, "load").Caps[0]);
        var near = Flying() with { Dock = DockHint.Near, DockDistanceCells = 20, DockSpeedPercent = 50 };
        Assert.Contains("dockGuide", Ids(near));
    }

    [Fact]
    public void UnavailableRowsAreDimmedWithAReason()
    {
        var s = Flying() with { ScanReady = false, ThreatActive = false };
        var scan = Find(s, "scan");
        Assert.Equal(KeyRowState.Disabled, scan.State);
        Assert.Equal("key.why.scanWait", scan.ReasonKey);
        Assert.False(scan.Clickable);
        Assert.Equal("key.why.noGun", Find(s with { Hardpoints = true, HasLaserModule = false, HasGunModule = false }, "weapons").ReasonKey);
        Assert.True(Find(Flying(), "scan").Clickable);
    }

    [Fact]
    public void WalkingKeepsTheWalkKeysAndTheSeatRow()
    {
        var s = Flying() with { Standing = true };
        Assert.Equal(HintContext.Walking, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "walk", "run", "interact", "computer", "hudDetail", "help" }, Ids(s));
        Assert.Equal("W", Find(s, "walk").Caps[0]);
        Assert.Equal(KeyRowState.Info, Find(s, "walk").State);
        var far = Flying() with { Standing = true, Interact = HintInteract.SitFar };
        Assert.Equal(KeyRowState.Disabled, Find(far, "interact").State);
        Assert.Equal("key.why.farSeat", Find(far, "interact").ReasonKey);
        var near = Flying() with { Standing = true, Interact = HintInteract.Sit };
        Assert.True(Find(near, "interact").Clickable);
        Assert.Equal("key.sit", Find(near, "interact").LabelKey);
    }

    [Fact]
    public void TheAimedWorkbenchReplacesTheSeatRowWhileStanding()
    {
        var s = Flying() with { Standing = true, Interact = HintInteract.Bench };
        Assert.Equal(HintContext.Walking, KeyHints.ContextOf(s));
        var row = Find(s, "interact");
        Assert.Equal("key.refine", row.LabelKey);
        Assert.True(row.Clickable);
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
        Assert.DoesNotContain("layer", Ids(s with { FloorBuilding = true }));
    }

    [Fact]
    public void ABeltToolAddsRotateAndTracksTheLine()
    {
        var s = Flying() with { Tool = HintTool.Belt, ToolName = "belt" };
        Assert.Contains("rotate", Ids(s));
        Assert.Equal("R", Find(s, "rotate").ActionKey);
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
        Assert.Equal(new[] { "place", "kind", "layer", "demolish", "quick", "build", "cancel" }, Ids(module));
        Assert.Equal("key.removeModule", Find(module, "demolish").LabelKey);
        Assert.Equal("N", Find(module, "kind").Caps[0]);
        var station = Flying() with { Tool = HintTool.Station, ToolName = "station" };
        Assert.Equal(new[] { "place", "quick", "build", "cancel" }, Ids(station));
    }

    [Fact]
    public void AimingAtAMachine()
    {
        var s = Flying() with { Aim = HintAim.Machine, AimName = "drill", Interact = HintInteract.Machine };
        Assert.Equal(HintContext.AimMachine, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "interact", "inspect", "demolish", "computer" }, Ids(s));
        Assert.Equal("key.interactMachine", Find(s, "interact").LabelKey);
    }

    [Fact]
    public void AimingAtADepositOffersMiningAndDrones()
    {
        var s = Flying() with { Aim = HintAim.Deposit };
        Assert.Equal(HintContext.AimDeposit, KeyHints.ContextOf(s));
        Assert.Equal("key.why.noBay", Find(s, "drones").ReasonKey);
        Assert.Equal("key.why.droneLocked", Find(s with { DroneBayLocked = true }, "drones").ReasonKey);
        Assert.True(Find(s with { DroneBayBuilt = true }, "drones").Clickable);
        Assert.Equal("F", Find(s with { DroneBayBuilt = true }, "drones").ActionKey);
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
    public void TheStarMapShowsArrowsJumpAndTheComputerKeys()
    {
        var s = Flying() with { MapOpen = true, JumpUnlocked = true, SystemChosen = true, JumpCost = "3 carbon" };
        Assert.Equal(HintContext.StarMap, KeyHints.ContextOf(s));
        Assert.Equal(new[] { "choose", "rotate", "jump", "tabs", "close", "esc" }, Ids(s));
        var jump = Find(s, "jump");
        Assert.Equal(new object[] { "3 carbon" }, jump.LabelArgs);
        Assert.Equal("key.jumpCost", jump.LabelKey);
        Assert.True(jump.Clickable);
        Assert.Equal("Enter", jump.ActionKey);
        Assert.Equal("Tab", Find(s, "close").ActionKey);
        Assert.Equal(KeyRowState.Alert, Find(s with { JumpAffordable = false }, "jump").State);
        Assert.Equal("key.why.jumpLocked", Find(s with { JumpUnlocked = false }, "jump").ReasonKey);
        Assert.Equal("key.why.noSystem", Find(s with { SystemChosen = false }, "jump").ReasonKey);
        Assert.Equal(HintContext.Flying, KeyHints.ContextOf(s with { HasGalaxy = false, MapOpen = true }));
    }

    [Fact]
    public void PanelsShowTheirOwnKeysAndBeatEverythingElse()
    {
        var busy = Flying() with { Tool = HintTool.Machine, Aim = HintAim.Machine, MapOpen = true };
        Assert.Equal(new[] { "choose", "pick", "tab", "tabs", "close", "esc" }, Ids(busy with { BuildOpen = true }));
        Assert.Equal(HintContext.BuildMenu, KeyHints.ContextOf(busy with { BuildOpen = true, ResearchOpen = true }));
        Assert.Equal(new[] { "choose", "start", "tabs", "close", "esc" }, Ids(busy with { ResearchOpen = true }));
        var panel = busy with { MapOpen = false, ComputerOpen = true };
        Assert.Equal(new[] { "tabs", "close", "esc" }, Ids(panel));
        Assert.Equal(HintContext.Computer, KeyHints.ContextOf(panel));
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
        Assert.Equal(new[] { "weapons" }, groups[0].Rows.Select(r => r.Id).ToArray());
        Assert.All(groups[0].Rows, r => Assert.Equal(KeyRowState.Alert, r.State));
        Assert.DoesNotContain("weapons", groups[1].Rows.Select(r => r.Id));
        Assert.Equal(KeyRowState.Active, KeyHints.For(s with { WeaponGroup = WeaponGroup.Guns })[0].Rows[0].State);
    }

    [Fact]
    public void TheThreatGroupAppliesInEveryWorldContextButNotInPanels()
    {
        var threat = Flying() with { ThreatNow = true };
        foreach (var s in new[] { threat with { Standing = true }, threat with { Tool = HintTool.Belt }, threat with { Aim = HintAim.Deposit }, threat with { Aim = HintAim.Machine } })
            Assert.Equal("key.ctx.alert", KeyHints.For(s)[0].TitleKey);
        foreach (var s in new[] { threat with { BuildOpen = true }, threat with { ResearchOpen = true }, threat with { MapOpen = true }, threat with { HelpOpen = true }, threat with { ComputerOpen = true } })
            Assert.Single(KeyHints.For(s));
        Assert.Single(KeyHints.For(Flying() with { ThreatActive = false, ThreatNow = true }));
    }

    [Fact]
    public void EveryContextHasUniqueRowsTextsInBothLanguagesAndFitsItsRowBudget()
    {
        var states = new List<KeyState>();
        var flying = Flying();
        foreach (var threat in new[] { false, true })
            foreach (var standing in new[] { false, true })
            foreach (var near in new[] { false, true })
            foreach (var mining in new[] { false, true })
            foreach (var dock in Enum.GetValues<DockHint>())
            {
                var b = flying with { ThreatNow = threat, Standing = standing, NearSeat = near, Mining = mining, AnythingDamaged = near, Dock = dock, HasTurnInertia = near, FlightAssist = mining, Interact = near ? HintInteract.Sit : HintInteract.None };
                states.Add(b);
                foreach (var tool in Enum.GetValues<HintTool>()) states.Add(b with { Tool = tool, ToolHasRecipes = near, BeltAnchored = mining });
                foreach (var aim in Enum.GetValues<HintAim>()) states.Add(b with { Aim = aim, AimHasRecipes = near, AimDamaged = mining, DroneBayBuilt = near, DroneBayLocked = mining });
                states.Add(b with { MapOpen = true, JumpUnlocked = near, SystemChosen = mining, JumpAffordable = standing, JumpCost = threat ? "1 carbon" : "" });
                states.Add(b with { BuildOpen = true });
                states.Add(b with { ResearchOpen = true });
                states.Add(b with { InventoryOpen = true });
                states.Add(b with { ComputerOpen = true });
                states.Add(b with { HelpOpen = true });
                states.Add(b with { PauseOpen = true });
                states.Add(b with { ConsoleFocus = true, HasConsole = true, HudDetail = HudDetail.Detailed });
            }
        var contexts = new HashSet<HintContext>();
        var capKeys = new HashSet<string>(InputActions.All.SelectMany(a => a.Keys));
        foreach (var s in states)
        {
            contexts.Add(KeyHints.ContextOf(s));
            var groups = KeyHints.For(s);
            int total = groups.Sum(g => g.Rows.Count);
            var ctx = KeyHints.ContextOf(s);
            if (ctx is HintContext.Flying or HintContext.Walking or HintContext.AimMachine or HintContext.AimDeposit or HintContext.AimSegment or HintContext.AimModule) Assert.True(total <= KeyHints.MaxRowsFlying, $"{ctx}: {total} rows");
            if (ctx == HintContext.Tool) Assert.True(total <= KeyHints.MaxRowsBuild, $"{ctx}: {total} rows");
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
                    if (r.ActionKey is not null) Assert.True(capKeys.Contains(r.ActionKey), $"{r.Id} presses {r.ActionKey}, which no action binds");
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

    [Fact]
    public void HudRowNamesTheDetailInForceAndLightsWhenNotStandard()
    {
        var s = Flying() with { HasTurnInertia = false };
        Assert.Equal("key.hudStandard", Find(s, "hudDetail").LabelKey);
        Assert.Equal(KeyRowState.Normal, Find(s, "hudDetail").State);
        Assert.Equal("H", Find(s, "hudDetail").ActionKey);
        var detailed = Find(s with { HudDetail = HudDetail.Detailed }, "hudDetail");
        Assert.Equal("key.hudDetailed", detailed.LabelKey);
        Assert.Equal(KeyRowState.Active, detailed.State);
        Assert.Equal("key.hudMinimal", Find(s with { HudDetail = HudDetail.Minimal, Standing = true }, "hudDetail").LabelKey);
    }

    [Fact]
    public void FocusOnADisplaySwitchesTheContext()
    {
        var focus = Flying() with { HasConsole = true, ConsoleFocus = true };
        Assert.Equal(HintContext.Console, KeyHints.ContextOf(focus));
        Assert.Equal(new[] { "press", "nav", "hudDetail", "leave", "esc" }, Ids(focus));
        Assert.Equal("F", Find(focus, "leave").ActionKey);
        Assert.Equal(HintContext.Pause, KeyHints.ContextOf(focus with { PauseOpen = true })); // panels still win
    }
}
