using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The goal types that only read the world: modules fitted, wrecks salvaged, docking, station stock, engine speed and kills.</summary>
public class GoalTypesTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261121UL;
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly WorldGenRules W = C.World!;

    private const string Head =
        "{\"schema\":1,\"id\":\"t\",\"order\":1,\"name_es\":\"t\",\"name_en\":\"t\",\"description_es\":\"t\",\"description_en\":\"t\",\"briefing_es\":\"t\",\"briefing_en\":\"t\"";

    private static string Goal(string id, string type, string fields = "") =>
        $"{{\"id\":\"{id}\",\"type\":\"{type}\"{fields},\"text_es\":\"es {id}\",\"text_en\":\"en {id}\"}}";

    private static ScenarioDef Def(params string[] primary) =>
        ScenarioCatalog.Parse(C, Head + ",\"goals\":{\"primary\":[" + string.Join(",", primary) + "]}}");

    private static SimWorld World() => SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);

    private static GoalState State(SimWorld w, ScenarioDef def, string id, ScenarioProgress? progress = null)
    {
        progress ??= new ScenarioProgress(C, def);
        progress.Update(w.Network, 0);
        return progress.Find(id)!;
    }

    // ---- parsing ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_new_goal_type_parses_with_its_own_fields()
    {
        var def = Def(
            Goal("second_laser", "have_module", ",\"kind\":\"mining_laser\",\"count\":2"),
            Goal("wreck", "salvage", ",\"count\":1"),
            Goal("dock", "dock"),
            Goal("stock", "stock", ",\"item\":\"core:iron_plate\",\"count\":20"),
            Goal("fast", "ship_speed", ",\"count\":300"),
            Goal("kills", "kill_enemies", ",\"count\":10"));
        Assert.Equal(new[] { GoalType.HaveModule, GoalType.Salvage, GoalType.Dock, GoalType.Stock, GoalType.ShipSpeed, GoalType.KillEnemies }, def.Primary.Select(g => g.Type));
        Assert.Equal(ModuleKind.MiningLaser, def.Primary[0].Kind);
        Assert.Equal(C.ItemIndex("core:iron_plate"), def.Primary[3].Item);
        Assert.True(def.Primary[2].IsOneStep);
    }

    [Theory]
    [InlineData("have_module", ",\"count\":2", "kind")]
    [InlineData("have_module", ",\"kind\":\"gun\"", "count")]
    [InlineData("have_module", ",\"kind\":\"no_such\",\"count\":1", "module kind")]
    [InlineData("stock", ",\"count\":5", "item")]
    [InlineData("dock", ",\"count\":1", "count")]
    [InlineData("ship_speed", ",\"count\":500", "count")]
    [InlineData("salvage", ",\"count\":0", "count")]
    [InlineData("kill_enemies", ",\"item\":\"core:iron_ore\",\"count\":3", "item")]
    public void Bad_fields_of_the_new_goal_types_are_rejected(string type, string fields, string messagePart)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Def(Goal("g", type, fields)));
        Assert.Contains(messagePart, ex.Message);
    }

    // ---- measuring -------------------------------------------------------------------------------------

    [Fact]
    public void A_second_laser_on_the_ship_completes_the_module_goal_and_it_stays_done()
    {
        var w = World();
        int ship = PowerTests.Ship(w, 1, 20);
        var def = Def(Goal("two", "have_module", ",\"kind\":\"mining_laser\",\"count\":2"));
        var p = new ScenarioProgress(C, def);
        Assert.Equal((GoalStatus.Pending, 1L), (State(w, def, "two", p).Status, State(w, def, "two", p).Current));
        w.Network.AddStructureModuleUnchecked(ship, 1, 1, 0, ModuleKind.MiningLaser);
        Assert.Equal(GoalStatus.Done, State(w, def, "two", p).Status);
        Assert.Equal(OutcomeKind.Won, p.Outcome);
        w.Network.RemoveStructureModuleUnchecked(ship, 1, 1, 0);
        Assert.Equal(GoalStatus.Done, State(w, def, "two", p).Status); // done stays done
    }

    [Fact]
    public void The_engines_of_the_ship_set_the_speed_percent_up_to_the_cap()
    {
        var w = World();
        int ship = PowerTests.Ship(w, 1, 20); // one engine
        var def = Def(Goal("fast", "ship_speed", ",\"count\":300"));
        var p = new ScenarioProgress(C, def);
        Assert.Equal(100, State(w, def, "fast", p).Current);
        w.Network.AddStructureModuleUnchecked(ship, 1, 2, 0, ModuleKind.Engine);
        w.Network.AddStructureModuleUnchecked(ship, 2, 2, 0, ModuleKind.Engine);
        Assert.Equal(GoalStatus.Done, State(w, def, "fast", p).Status);
        for (int i = 3; i < 8; i++) w.Network.AddStructureModuleUnchecked(ship, i, 2, 0, ModuleKind.Engine);
        Assert.Equal(StructureFlight.MaxSpeedPercent, p.EnginePercent(w.Network, 0));
    }

    [Fact]
    public void Stock_in_a_station_counts_and_a_ship_alone_does_not()
    {
        var w = World();
        PowerTests.Ship(w, 1, 20);
        var def = Def(Goal("stock", "stock", ",\"item\":\"core:iron_plate\",\"count\":20"));
        var p = new ScenarioProgress(C, def);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_plate"), 30);
        Assert.Equal(0, State(w, def, "stock", p).Current); // the hold is not a station stock
        int station = w.Network.CreateStructure(0, StructureKind.Station, -40 * (long)Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.GiveStock(station, C.ItemIndex("core:iron_plate"), 12);
        Assert.Equal(12, State(w, def, "stock", p).Current);
        w.Network.GiveStock(station, C.ItemIndex("core:iron_plate"), 8);
        Assert.Equal(GoalStatus.Done, State(w, def, "stock", p).Status);
    }

    [Fact]
    public void Docking_completes_the_dock_goal()
    {
        var w = World();
        int ship = PowerTests.Ship(w, 1, 20);
        // The cockpit at the origin docks on its -X flank with a station whose +X port faces it.
        int station = w.Network.CreateStructure(0, StructureKind.Station, -18 * (long)Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        var def = Def(Goal("dock", "dock"));
        var p = new ScenarioProgress(C, def);
        Assert.Equal(GoalStatus.Pending, State(w, def, "dock", p).Status);
        w.Submit(0, new DockAt(station, 0, 0, 0, Dir.PosX));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        Assert.True(w.Network.IsDocked(0));
        Assert.Equal(GoalStatus.Done, State(w, def, "dock", p).Status);
        Assert.Equal("", State(w, def, "dock", p).ProgressText);
        _ = ship;
    }

    [Fact]
    public void A_wreck_taken_apart_counts_as_salvaged()
    {
        var w = World();
        int ship = PowerTests.Ship(w, 1, 30);
        int kind = W.KindIndex("core:derelict_wreck");
        int wreck = Enumerable.Range(0, w.Network.ObstacleCount).First(i => w.Network.GetObstacle(i).Kind == kind);
        var def = Def(Goal("wreck", "salvage", ",\"count\":1"));
        var p = new ScenarioProgress(C, def);
        Assert.Equal(0, w.Network.WrecksSalvaged());
        Assert.Equal(GoalStatus.Pending, State(w, def, "wreck", p).Status);
        var info = w.Network.GetObstacle(wreck);
        w.Network.SetStructureOrigin(ship, info.X - 4 * Cell, info.Y - 2 * Cell, info.Z - 4 * Cell);
        w.Step(W.SalvageTicks + 5);
        Assert.Equal(1, w.Network.WrecksSalvaged());
        Assert.Equal(GoalStatus.Done, State(w, def, "wreck", p).Status);
    }

    [Fact]
    public void Kills_follow_the_enemy_counter()
    {
        var w = World();
        PowerTests.Ship(w, 1, 20);
        var def = Def(Goal("kills", "kill_enemies", ",\"count\":3"));
        Assert.Equal(0, State(w, def, "kills").Current);
        Assert.Equal(3, State(w, def, "kills").Target);
    }
}
