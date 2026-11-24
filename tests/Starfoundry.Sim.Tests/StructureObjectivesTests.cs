using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class StructureObjectivesTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private const ulong Seed = 20261121UL;

    private static readonly Objective[] StructuresOrder =
    {
        Objective.Fly, Objective.Mine, Objective.Refine, Objective.FabBay, Objective.WatchFactory, Objective.FoundStation, Objective.Dock, Objective.MakeFuel, Objective.AddModule,
        Objective.Research, Objective.Scan, Objective.Defend, Objective.Salvage, Objective.Automate, Objective.Upgrade, Objective.Expand,
    };

    private static SimWorld World(bool structures = true)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: structures);
        w.Network.EnsurePlayer(0);
        return w;
    }

    private static ObjectiveTracker Tracker() => new(C, structures: true);

    [Fact]
    public void Structure_steps_are_ordered()
    {
        var t = Tracker();
        t.Update(World().Network, 0);
        Assert.Equal(StructuresOrder, t.States.Select(s => s.Objective).ToArray());
        Assert.Equal(Objective.Fly, t.Current);
        Assert.All(t.States, s => Assert.Equal(s.TitleKey + ".hint", s.HintKey));
    }

    [Fact]
    public void Old_steps_unchanged_when_flags_off()
    {
        var old = new[]
        {
            Objective.Fly, Objective.Mine, Objective.Refine, Objective.Drill, Objective.Smelter, Objective.Depot,
            Objective.Assembler, Objective.Lab, Objective.Research, Objective.Scan, Objective.Defend, Objective.Expand,
        };
        var t = new ObjectiveTracker(C, stationRules: false, structures: false);
        t.Update(World(false).Network, 0);
        Assert.Equal(old, t.States.Select(s => s.Objective).ToArray());
    }

    [Fact]
    public void Watch_factory_completes_when_a_machine_delivers_not_when_the_workbench_does()
    {
        var w = World();
        var t = Tracker();
        t.Update(w.Network, 0);
        Assert.False(t.IsDone(Objective.WatchFactory));
        w.Network.GiveItems(0, C.ItemIndex("core:iron_plate"), 1); // a plate from the workbench is not the factory
        t.Update(w.Network, 0);
        Assert.False(t.IsDone(Objective.WatchFactory));
        // The pre-0031 starter ship has a working factory: ore through its port, smelter and assembler into the depot.
        var old = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        old.Network.EnsurePlayer(0);
        var t2 = new ObjectiveTracker(TestContent.Core, structures: true);
        old.Network.GiveItems(0, TestContent.Core.ItemIndex("core:iron_ore"), 30);
        for (int i = 0; i < 300 && !t2.IsDone(Objective.WatchFactory); i++) { old.Step(60); t2.Update(old.Network, 0); }
        Assert.True(t2.IsDone(Objective.WatchFactory));
    }

    [Fact]
    public void Fab_bay_step_completes_with_a_buildable_bay_and_the_shipped_starter_ship_has_none()
    {
        var floor = SimWorld.CreateGalaxy(TestContent.CoreFloor, GameMode.Campaign, 20261121UL, threats: false, stationRules: false, structures: true);
        floor.Network.EnsurePlayer(0);
        var t = new ObjectiveTracker(TestContent.CoreFloor, structures: true);
        t.Update(floor.Network, 0);
        Assert.False(t.IsDone(Objective.FabBay));
        floor.Network.AddStructureModuleUnchecked(floor.Network.ShipStructure(0), 1, 0, 1, ModuleKind.Workshop);
        t.Update(floor.Network, 0);
        Assert.True(t.IsDone(Objective.FabBay));
    }

    [Fact]
    public void Found_station_needs_an_own_station_not_a_ship_or_foreign_station()
    {
        var w = World();
        var t = Tracker();
        w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.CreateStructure(1, StructureKind.Station, 0, 0, 0);
        t.Update(w.Network, 0);
        Assert.False(t.IsDone(Objective.FoundStation));
        w.Network.CreateStructure(0, StructureKind.Station, 100, 0, 0);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.FoundStation));
    }

    [Fact]
    public void Add_module_needs_more_than_the_starter_set_or_a_station_beyond_its_core()
    {
        var w = World();
        var t = Tracker();
        int ship = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        for (int i = 0; i < ObjectiveTracker.StarterModuleCount; i++)
            w.Network.AddStructureModuleUnchecked(ship, i, 0, 0, ModuleKind.Hull);
        t.Update(w.Network, 0);
        Assert.False(t.IsDone(Objective.AddModule));
        w.Network.AddStructureModuleUnchecked(ship, ObjectiveTracker.StarterModuleCount, 0, 0, ModuleKind.Workshop);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.AddModule));

        var w2 = World();
        var t2 = Tracker();
        int st = w2.Network.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        w2.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        t2.Update(w2.Network, 0);
        Assert.False(t2.IsDone(Objective.AddModule));
        w2.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Hull);
        t2.Update(w2.Network, 0);
        Assert.True(t2.IsDone(Objective.AddModule));
    }

    [Fact]
    public void Completion_is_sticky()
    {
        var w = World();
        var t = Tracker();
        w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.FoundStation));
        t.Update(World().Network, 0);
        Assert.True(t.IsDone(Objective.FoundStation));
    }

    [Fact]
    public void Every_new_key_exists_in_both_languages()
    {
        var keys = new List<string>
        {
            "structure.ship", "structure.station", "hud.structure", "hud.modules", "learn.needEngine",
            "toast.reject.kindNotAllowed", "toast.reject.noSuchStructure", "toast.reject.noCockpitOrEngine",
        };
        foreach (var k in Enum.GetValues<ModuleKind>())
        {
            string name = char.ToLowerInvariant(k.ToString()[0]) + k.ToString()[1..]; // camel case: droneBay
            keys.Add("module." + name); keys.Add("module." + k.ToString().ToLowerInvariant()); keys.Add("module." + k.ToString().ToLowerInvariant() + ".purpose");
            keys.Add("module." + name + ".purpose");
        }
        foreach (var o in new[] { Objective.WatchFactory, Objective.FoundStation, Objective.MakeFuel, Objective.AddModule, Objective.Automate, Objective.Dock, Objective.Salvage, Objective.Upgrade })
        {
            var s = new ObjectiveState(o, false);
            keys.Add(s.TitleKey);
            keys.Add(s.HintKey);
        }
        foreach (var key in keys)
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
        }
    }

    [Fact]
    public void Reject_texts_exist_for_new_rejects()
    {
        foreach (var r in new[] { Reject.KindNotAllowed, Reject.NoSuchStructure })
        {
            string? key = RejectTexts.Key(r);
            Assert.NotNull(key);
            Assert.True(Loc.Has(key!), key);
        }
    }

    [Fact]
    public void Need_engine_tip_fires_once_and_only_for_an_incomplete_own_ship_in_structures_mode()
    {
        var w = World();
        var tips = new Tips(C);
        Assert.Null(tips.Next(w.Network, 0)); // no ship yet

        int ship = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        Assert.Null(tips.Next(w.Network, 0)); // no modules yet
        w.Network.AddStructureModuleUnchecked(ship, 0, 0, 0, ModuleKind.Cockpit);
        Assert.Equal("learn.needEngine", tips.Next(w.Network, 0)?.Key);

        w.Step(Units.TicksPerSecond * 60);
        Assert.Null(tips.Next(w.Network, 0)); // once only

        // A complete ship and a foreign ship stay silent.
        var w2 = World();
        var tips2 = new Tips(C);
        int ok = w2.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w2.Network.AddStructureModuleUnchecked(ok, 0, 0, 0, ModuleKind.Cockpit);
        w2.Network.AddStructureModuleUnchecked(ok, 1, 0, 0, ModuleKind.Engine);
        int other = w2.Network.CreateStructure(1, StructureKind.Ship, 0, 0, 0);
        w2.Network.AddStructureModuleUnchecked(other, 0, 0, 0, ModuleKind.Hull);
        Assert.Null(tips2.Next(w2.Network, 0));

        // Without the structures mode it never fires.
        var w3 = World(false);
        var tips3 = new Tips(C);
        int s3 = w3.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w3.Network.AddStructureModuleUnchecked(s3, 0, 0, 0, ModuleKind.Cockpit);
        Assert.Null(tips3.Next(w3.Network, 0));
    }
}
