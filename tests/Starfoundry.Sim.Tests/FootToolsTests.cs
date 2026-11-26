using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Phase 5 of the blueprint editor: on foot, machines and belts are closed in a world with the floor plan, the repair and undo shortcuts stay (no new command).</summary>
public class FootToolsTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;

    [Fact]
    public void The_floor_plan_closes_belts_and_machines_but_leaves_modules_and_the_founder()
    {
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        foreach (var e in entries)
        {
            bool closed = e.Kind is BuildEntryKind.Belt or BuildEntryKind.Machine;
            Assert.Equal(closed, FootTools.EntryBlocked(e, true));
            Assert.False(FootTools.EntryBlocked(e, false));
            Assert.Equal(closed ? Reject.UseBlueprint : (Reject?)null, FootTools.Problem(e, false, true));
            if (closed || e.Module == ModuleKind.Workshop) Assert.Equal(Reject.UseStation, FootTools.Problem(e, true, false)); // the ship rule keeps its own text
        }
        Assert.Contains(entries, e => e.Kind == BuildEntryKind.Module && !FootTools.EntryBlocked(e, true));
        Assert.True(FootTools.ItemBlocked(isBelt: true, isModule: false, isStation: false, interiorBlueprint: true));
        Assert.True(FootTools.ItemBlocked(false, false, false, true)); // a machine
        Assert.False(FootTools.ItemBlocked(false, true, false, true)); // a module
        Assert.False(FootTools.ItemBlocked(false, false, true, true)); // the station founder
        Assert.False(FootTools.ItemBlocked(true, false, false, false)); // a world from before the floor plan keeps the tool
    }

    [Fact]
    public void The_hint_points_at_the_plan_in_both_languages_and_the_ship_hint_wins()
    {
        Assert.Equal("", FootTools.Hint(false, false));
        Assert.Equal(StationFirstUi.Hint(true), FootTools.Hint(true, true));
        var t = Loc.All.ToDictionary(p => p.Key, p => p.Value)["build.useBlueprint"];
        Assert.False(string.IsNullOrWhiteSpace(t.Es) || string.IsNullOrWhiteSpace(t.En));
        Assert.Equal("toast.reject.useBlueprint", RejectTexts.Key(Reject.UseBlueprint));
    }

    [Fact]
    public void Demolish_and_repair_work_on_foot_in_a_floor_plan_world_with_the_commands_that_already_exist()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261126UL, threats: false, stationRules: false, structures: true, rules: WorldRules.Default);
        w.Network.EnsurePlayer(0);
        const int cell = Units.CellUnits;
        int st = w.Network.CreateStructure(0, StructureKind.Station, -18L * cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Dock);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        foreach (var item in C.Items) w.Network.GiveStock(st, item.Index, 2000);
        // a machine placed the new way
        var it = new BlueprintInterior(new[] { new BlueprintMachine(10, 0, 2, C.MachineIndex("core:smelter"), -1) }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        var mods = w.Network.GetStructure(st).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();
        w.Submit(0, new ApplyBlueprint(st, mods, it));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Last().Reject);
        // building by hand is refused (so the tool is closed), removing by hand is not
        w.Submit(0, new PlaceMachineIn(st, 12, 0, 4, C.MachineIndex("core:smelter"), -1));
        w.Step();
        Assert.Equal(Reject.UseBlueprint, w.LastResults.Last().Reject);
        int id = Enumerable.Range(0, w.Network.MachineIdLimit).First(i => w.Network.IsMachineAlive(i));
        w.Submit(0, new Repair(id));
        w.Step();
        Assert.NotEqual(Reject.UseBlueprint, w.LastResults.Last().Reject);
        w.Submit(0, new RemoveMachine(id));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Last().Reject);
        Assert.False(w.Network.IsMachineAlive(id));
    }
}
