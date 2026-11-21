using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class StationObjectivesTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261120UL;

    private static readonly Objective[] StationOrder =
    {
        Objective.Fly, Objective.Mine, Objective.Refine, Objective.Core, Objective.CargoPort, Objective.Smelter, Objective.Depot,
        Objective.Assembler, Objective.Lab, Objective.Research, Objective.Scan, Objective.Defend, Objective.Expand,
    };

    private static readonly Objective[] OldOrder =
    {
        Objective.Fly, Objective.Mine, Objective.Refine, Objective.Drill, Objective.Smelter, Objective.Depot,
        Objective.Assembler, Objective.Lab, Objective.Research, Objective.Scan, Objective.Defend, Objective.Expand,
    };

    private static SimWorld World()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, false);
        w.Network.EnsurePlayer(0);
        return w;
    }

    private static ObjectiveTracker Station() => new(C, stationRules: true);

    private static void Add(SimWorld w, string machine, int recipe = -1) =>
        w.Network.AddMachine(0, 0, 0, C.MachineIndex(machine), recipe, 0);

    [Fact]
    public void Station_steps_are_ordered_and_have_no_drill()
    {
        var t = Station();
        t.Update(World().Network, 0);
        Assert.Equal(StationOrder, t.States.Select(s => s.Objective).ToArray());
        Assert.DoesNotContain(t.States, s => s.Objective == Objective.Drill);
        Assert.Equal(Objective.Fly, t.Current);
    }

    [Fact]
    public void Old_steps_unchanged_when_rules_off()
    {
        foreach (var t in new[] { new ObjectiveTracker(C), new ObjectiveTracker(C, stationRules: false) })
        {
            t.Update(World().Network, 0);
            Assert.Equal(OldOrder, t.States.Select(s => s.Objective).ToArray());
            Assert.All(t.States, s => Assert.Equal(s.TitleKey + ".hint", s.HintKey));
        }
    }

    [Fact]
    public void Core_and_cargo_port_steps_complete_from_the_world()
    {
        var w = World();
        var t = Station();
        w.Network.GiveItems(0, C.ItemIndex("core:iron_ingot"), 3);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.Refine));
        Assert.False(t.IsDone(Objective.Core));

        w.Network.AddModuleUnchecked(0, 0, 0, 0);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.Core));
        Assert.False(t.IsDone(Objective.CargoPort));

        Add(w, "core:unloader");
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.CargoPort));
    }

    [Fact]
    public void Core_step_ignores_other_players_modules()
    {
        var w = World();
        w.Network.EnsurePlayer(1);
        w.Network.AddModuleUnchecked(1, 0, 0, 0);
        var t = Station();
        t.Update(w.Network, 0);
        Assert.False(t.IsDone(Objective.Core));
    }

    [Fact]
    public void Later_station_steps_complete_in_a_scripted_campaign()
    {
        var w = World();
        var t = Station();
        w.Network.GrantResearch(C.ResearchIndex("core:logistics"));
        Add(w, "core:smelter", C.RecipeIndex("core:smelt_iron"));
        Add(w, "core:assembler", C.RecipeIndex("core:iron_plate"));
        Add(w, "core:research_lab");
        w.Submit(0, new Scan());
        w.Step();
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.Smelter));
        Assert.True(t.IsDone(Objective.Assembler));
        Assert.True(t.IsDone(Objective.Lab));
        Assert.True(t.IsDone(Objective.Research));
        Assert.True(t.IsDone(Objective.Scan));
        Assert.False(t.IsDone(Objective.Depot));
        Assert.False(t.IsDone(Objective.Defend));
        Assert.False(t.IsDone(Objective.Expand));
        Assert.Equal(t.States.Count(s => s.Done), t.DoneCount);
        Assert.Equal(5, t.DoneCount);
    }

    [Fact]
    public void Current_is_the_first_open_step_and_completion_is_sticky()
    {
        var w = World();
        var t = Station();
        int ore = Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == C.ItemIndex("core:iron_ore"));
        var (x, y, z) = w.Network.DepositPosition(ore);
        for (int i = 0; i < 30 * 15 && !w.Network.InMineRange(0, ore); i++)
        {
            var p = w.Network.GetPlayerInfo(0);
            int dx = Math.Sign(x * Units.CellUnits - p.X), dz = Math.Sign(z * Units.CellUnits - p.Z);
            w.Submit(0, new SetMovement(Math.Abs(x * Units.CellUnits - p.X) > 120 ? dx : 0, 0, Math.Abs(z * Units.CellUnits - p.Z) > 120 ? dz : 0));
            w.Step();
        }
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(ore));
        w.Step(30 * 10);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.Fly));
        Assert.True(t.IsDone(Objective.Mine));
        Assert.Equal(Objective.Refine, t.Current);

        w.Network.GiveItems(0, C.ItemIndex("core:iron_ingot"), 3);
        t.Update(w.Network, 0);
        Assert.Equal(Objective.Core, t.Current);
        w.Network.AddModuleUnchecked(0, 0, 0, 0);
        t.Update(w.Network, 0);
        Assert.Equal(Objective.CargoPort, t.Current);
        w.Network.RemoveModuleUnchecked(0, 0, 0, 0);
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.Core)); // sticky
        Assert.Equal(Objective.CargoPort, t.Current);
    }

    [Fact]
    public void Station_hints_use_the_station_variant_where_the_rule_changes_the_advice()
    {
        var t = Station();
        t.Update(World().Network, 0);
        foreach (var s in t.States)
        {
            bool variant = s.Objective is Objective.Mine or Objective.Refine or Objective.Smelter or Objective.Depot;
            Assert.Equal(variant ? s.TitleKey + ".hint.station" : s.TitleKey + ".hint", s.HintKey);
        }
    }

    [Fact]
    public void Every_new_key_exists_in_both_languages()
    {
        var t = Station();
        t.Update(World().Network, 0);
        var keys = t.States.SelectMany(s => new[] { s.TitleKey, s.HintKey }).Concat(new[]
        {
            "obj.core", "obj.core.hint", "obj.cargoPort", "obj.cargoPort.hint",
            "obj.mine.hint.station", "obj.refine.hint.station", "obj.smelter.hint.station", "obj.depot.hint.station",
            "learn.deliver",
        });
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
        foreach (var r in new[] { Reject.OutsideStation, Reject.ModuleOccupied, Reject.NotAdjacent, Reject.NotAllowedHere,
                                  Reject.Overlap, Reject.OutOfBounds, Reject.NotEnoughItems })
        {
            string? key = RejectTexts.Key(r);
            Assert.NotNull(key);
            Assert.True(Loc.Has(key!), key);
        }
        Assert.Null(RejectTexts.Key(Reject.None));
    }

    private static Tips WithCoreAndFullHold(SimWorld w, bool port)
    {
        w.Network.AddModuleUnchecked(0, 0, 0, 0);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_ore"), PlayerRules.HoldCapacity);
        if (port) Add(w, "core:unloader");
        return new Tips(C);
    }

    [Fact]
    public void Hand_delivery_tip_fires_once_with_core_full_hold_and_no_port()
    {
        var w = World();
        var tips = WithCoreAndFullHold(w, port: false);
        Assert.Equal("learn.deliver", tips.Next(w.Network, 0)?.Key);
        w.Step(Units.TicksPerSecond * 60);
        Assert.Null(tips.Next(w.Network, 0));
    }

    [Fact]
    public void Hand_delivery_tip_stays_quiet_in_other_states()
    {
        var noCore = World();
        noCore.Network.GiveItems(0, C.ItemIndex("core:iron_ore"), PlayerRules.HoldCapacity);
        Assert.Null(new Tips(C).Next(noCore.Network, 0));

        var notFull = World();
        notFull.Network.AddModuleUnchecked(0, 0, 0, 0);
        notFull.Network.GiveItems(0, C.ItemIndex("core:iron_ore"), 10);
        Assert.Null(new Tips(C).Next(notFull.Network, 0));

        var withPort = World();
        Assert.Null(WithCoreAndFullHold(withPort, port: true).Next(withPort.Network, 0));
    }
}
