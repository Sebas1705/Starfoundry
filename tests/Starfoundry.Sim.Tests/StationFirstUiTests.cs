using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0039 in the interface: the station-first rule, the workbench panel logic and the machine panel logic. Engine-free.</summary>
public class StationFirstUiTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;

    [Fact]
    public void FloorBuildingIsBlockedOnlyAboardAShipInAStationFirstWorld()
    {
        Assert.True(StationFirstUi.AutomationBlocked(stationFirst: true, aboardShip: true));
        Assert.False(StationFirstUi.AutomationBlocked(true, aboardShip: false)); // in a station, or docked, everything stays
        Assert.False(StationFirstUi.AutomationBlocked(false, true)); // an old log or a non-structure world
        Assert.False(StationFirstUi.AutomationBlocked(false, false));
    }

    [Fact]
    public void ABlockedShipClosesBeltsMachinesAndTheFabricationBayButNotTheOtherModules()
    {
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        foreach (var e in entries)
        {
            bool closed = e.Kind is BuildEntryKind.Belt or BuildEntryKind.Machine || e.Module == ModuleKind.Workshop;
            Assert.Equal(closed, StationFirstUi.EntryBlocked(e, blocked: true));
            Assert.False(StationFirstUi.EntryBlocked(e, blocked: false));
            Assert.Equal(closed ? Reject.UseStation : (Reject?)null, StationFirstUi.Problem(e, true));
        }
        Assert.Contains(entries, e => e.Kind == BuildEntryKind.Machine && C.Machines[e.Machine].Role == MachineRole.Turret); // turrets and walls are machines
        Assert.Contains(entries, e => e.Kind == BuildEntryKind.Module && !StationFirstUi.EntryBlocked(e, true)); // ship modules stay
        Assert.False(StationFirstUi.NothingToBuild(entries, true));
        Assert.False(StationFirstUi.KindBlocked(ModuleKind.Cargo, true));
        Assert.True(StationFirstUi.KindBlocked(ModuleKind.Workshop, true));
        Assert.False(StationFirstUi.KindBlocked(ModuleKind.Workshop, false));
    }

    [Fact]
    public void TheHintAndTheRejectHaveSpanishAndEnglishText()
    {
        Assert.Equal("", StationFirstUi.Hint(false));
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var key in new[] { "build.stationFirst", "key.stationFirst", "key.why.stationFirst", "toast.reject.useStation" })
            Assert.False(string.IsNullOrWhiteSpace(table[key].Es) || string.IsNullOrWhiteSpace(table[key].En), key);
        Assert.Contains("estación", table["build.stationFirst"].Es);
        Assert.Equal("Automation is built in a station", table["build.stationFirst"].En);
        Assert.Equal("toast.reject.useStation", RejectTexts.Key(Reject.UseStation));
    }

    [Fact]
    public void TheBlockedHintShowsInTheKeyPanelOnlyAboardAShip()
    {
        var s = new KeyState { CanWalk = true, StructuresOn = true, ModuleToolAvailable = true };
        Assert.DoesNotContain(KeyHints.For(s).SelectMany(g => g.Rows), r => r.Id == "stationFirst");
        var blocked = KeyHints.For(s with { AutomationBlocked = true }).SelectMany(g => g.Rows).First(r => r.Id == "stationFirst");
        Assert.Equal(KeyRowState.Disabled, blocked.State);
        Assert.Equal("key.why.stationFirst", blocked.ReasonKey);
        Assert.Contains(KeyHints.For(s with { AutomationBlocked = true, Standing = true }).SelectMany(g => g.Rows), r => r.Id == "stationFirst");
    }

    [Fact]
    public void ThePanelsHaveKeyHintContextsWithTheCloseKeys()
    {
        var bench = new KeyState { BenchOpen = true };
        Assert.Equal(HintContext.BenchPanel, KeyHints.ContextOf(bench));
        Assert.Equal(new[] { "benchQueue", "close", "esc" }, KeyHints.For(bench).Last().Rows.Select(r => r.Id).ToArray());
        var machine = new KeyState { MachineOpen = true };
        Assert.Equal(HintContext.MachinePanel, KeyHints.ContextOf(machine));
        Assert.Equal("F", KeyHints.For(machine).Last().Rows.First(r => r.Id == "close").Caps[0]);
        Assert.Equal("key.interactBench", KeyHints.For(new KeyState { Standing = true, Interact = HintInteract.BenchPanel }).Last().Rows.First(r => r.Id == "interact").LabelKey);
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var key in new[] { "key.interactBench", "key.ctx.bench", "key.ctx.machine", "key.benchQueue", "key.machineRecipe", "key.closePanel" })
            Assert.False(string.IsNullOrWhiteSpace(table[key].Es) || string.IsNullOrWhiteSpace(table[key].En), key);
    }

    [Fact]
    public void TheInteractActionIsDocumentedAsOpeningTheWorkbenchAndMachinePanels()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        Assert.Contains("banco de trabajo", table["act.interact.hint"].Es);
        Assert.Contains("workbench", table["act.interact.hint"].En);
        Assert.Contains("máquina", table["act.interact.hint"].Es);
        Assert.Contains(InputActions.All, a => a.Id == "interact" && a.Keys.Contains("F")); // the one key both panels open with and close with
    }

    // ---- the workbench panel ----

    [Fact]
    public void TheBenchOffersTheThreeRecipesWithTheManualTime()
    {
        var rows = BenchView.Rows(C, stationFirst: true, _ => 0);
        Assert.Equal(BenchView.RecipeIds, rows.Select(r => r.Id).ToArray());
        Assert.All(rows, r => Assert.Equal(C.Recipes[r.Recipe].TimeTicks * PlayerRules.BenchCraftMultiplier / Units.TicksPerSecond, r.Seconds));
        Assert.All(rows, r => Assert.False(r.CanStart));
        var ore = C.Recipes.First(r => r.Id == "core:ship_smelt").Inputs[0].Item;
        var withOre = BenchView.Rows(C, true, item => item == ore ? 5 : 0);
        Assert.True(withOre.First(r => r.Id == "core:ship_smelt").CanStart);
        Assert.False(withOre.First(r => r.Id == "core:ship_press").CanStart);
    }

    [Fact]
    public void TheQueueLimitsAndProgressFollowTheRules()
    {
        Assert.Equal(PlayerRules.BenchQueueMax, BenchView.Room(0));
        Assert.Equal(0, BenchView.Room(PlayerRules.BenchQueueMax));
        Assert.Equal(0, BenchView.Room(PlayerRules.BenchQueueMax + 3));
        Assert.Equal(2, BenchView.Addable(3, 5));
        Assert.Equal(0, BenchView.Addable(5, 1));
        Assert.Equal(1, BenchView.Addable(0, 1));
        Assert.Equal(0f, BenchView.Progress(false, 100, 240));
        Assert.Equal(0.5f, BenchView.Progress(true, 120, 240));
        Assert.Equal(1f, BenchView.Progress(true, 0, 240));
        Assert.Equal(0f, BenchView.Progress(true, 10, 0));
    }

    [Theory]
    [InlineData(true, 0, true, true, BenchState.Crafting)]
    [InlineData(false, 0, true, true, BenchState.Idle)]
    [InlineData(false, 2, false, true, BenchState.WaitingInputs)]
    [InlineData(false, 2, true, false, BenchState.NoRoom)]
    [InlineData(false, 2, true, true, BenchState.Crafting)]
    public void TheBenchStatusIsClassified(bool crafting, int queued, bool inputs, bool room, BenchState expected) =>
        Assert.Equal(expected, BenchView.State(crafting, queued, inputs, room));

    [Fact]
    public void ButtonsSendQueueCommandsInStationFirstWorldsAndTheOldRecipeInLegacyOnes()
    {
        Assert.Equal(new QueueBench(3, 5), BenchView.QueueCommand(true, 3, 5));
        Assert.Equal(new QueueBench(3, 5), BenchView.QueueCommand(true, 3, 99)); // never more than the queue holds
        Assert.Equal(new SetShipRecipe(3), BenchView.QueueCommand(false, 3, 5));
        Assert.IsType<ClearBench>(BenchView.ClearCommand(true));
        Assert.Equal(new SetShipRecipe(-1), BenchView.ClearCommand(false));
        Assert.True(BenchView.UsesPanel(true));
        Assert.False(BenchView.UsesPanel(false)); // F still cycles recipes there
    }

    [Fact]
    public void TheSlowLabelSaysSecondsInBothLanguages()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("Manual, slow: {0} s per craft", table["bench.panel.slow"].En);
        Assert.Contains("lento", table["bench.panel.slow"].Es);
        Assert.Contains("{0}", table["bench.panel.slow"].Es);
        Assert.Contains("8", BenchView.SlowLabel(8));
    }

    [Fact]
    public void ABenchCraftTakesEightTimesTheRecipeInStationFirstWorlds()
    {
        var r = C.Recipes.First(x => x.Id == "core:ship_smelt");
        Assert.Equal(r.TimeTicks * PlayerRules.BenchCraftMultiplier, BenchView.CraftTicks(r, true));
        Assert.Equal(r.TimeTicks * PlayerRules.ShipCraftMultiplier, BenchView.CraftTicks(r, false));
    }

    // ---- the machine panel ----

    [Theory]
    [InlineData(false, true, 1000, false, 0, MachineRun.NoRecipe)]
    [InlineData(true, true, 0, true, 0, MachineRun.NoPower)]
    [InlineData(true, false, 0, true, 0, MachineRun.Running)] // a machine that draws no power never shows as unpowered
    [InlineData(true, true, 600, true, 0, MachineRun.Running)]
    [InlineData(true, true, 1000, false, 3, MachineRun.OutputFull)]
    [InlineData(true, true, 1000, false, 0, MachineRun.WaitingInput)]
    public void TheMachineStatusIsClassified(bool recipe, bool power, int sat, bool crafting, int output, MachineRun expected) =>
        Assert.Equal(expected, MachineView.Classify(recipe, power, sat, crafting, output));

    [Fact]
    public void ThroughputIsPerMinuteFromTheRecipeAndTheStatus()
    {
        Assert.Equal(60.0, MachineView.NominalPerMinute(1, 30), 6); // one item a second
        Assert.Equal(120.0, MachineView.NominalPerMinute(4, 60), 6);
        Assert.Equal(0.0, MachineView.NominalPerMinute(1, 0));
        Assert.Equal(30.0, MachineView.CurrentPerMinute(MachineRun.Running, 1, 30, 500), 6); // half the power, half the speed
        Assert.Equal(0.0, MachineView.CurrentPerMinute(MachineRun.WaitingInput, 1, 30, 1000));
        Assert.Equal(0.0, MachineView.CurrentPerMinute(MachineRun.NoPower, 1, 30, 0));
        Assert.Equal(60.0, MachineView.CurrentPerMinute(MachineRun.Running, 1, 30, 5000), 6); // never above full power
        Assert.Equal("60", MachineView.PerMinuteText(60.0));
        Assert.Equal(7.5.ToString("0.#", Loc.Culture), MachineView.PerMinuteText(7.5)); // the decimal separator follows the language
    }

    [Fact]
    public void SwitchingOffRemembersTheRecipeAndSwitchingOnRestoresIt()
    {
        Assert.Equal(4, MachineView.Remember(4, 2));
        Assert.Equal(2, MachineView.Remember(-1, 2)); // already off: keep the one remembered
        Assert.Equal(4, MachineView.RestoreRecipe(4, new[] { 1, 4, 6 }));
        Assert.Equal(1, MachineView.RestoreRecipe(9, new[] { 1, 4, 6 })); // no longer offered: the first
        Assert.Equal(1, MachineView.RestoreRecipe(-1, new[] { 1, 4, 6 }));
        Assert.Equal(-1, MachineView.RestoreRecipe(4, Array.Empty<int>()));
    }

    [Fact]
    public void EveryPanelStringHasBothLanguages()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, value) in table.Where(p => p.Key.StartsWith("bench.panel.") || p.Key.StartsWith("machine.panel.")))
            Assert.False(string.IsNullOrWhiteSpace(value.Es) || string.IsNullOrWhiteSpace(value.En), key);
        foreach (var s in new[] { MachineRun.NoRecipe, MachineRun.NoPower, MachineRun.Running, MachineRun.WaitingInput, MachineRun.OutputFull })
            Assert.True(table.ContainsKey(MachineView.StatusKey(s)), s.ToString());
        foreach (var s in new[] { BenchState.Idle, BenchState.Crafting, BenchState.WaitingInputs, BenchState.NoRoom })
            Assert.True(table.ContainsKey(BenchView.StateKey(s)), s.ToString());
    }
}
