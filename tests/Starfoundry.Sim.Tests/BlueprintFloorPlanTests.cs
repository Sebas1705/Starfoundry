using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The engine-free floor plan of the blueprint editor (ADR 0044): the interior draft, its ghost rules, belt paths, and the session that prices and applies it.</summary>
public partial class BlueprintInteriorTests
{
    private static InteriorDraft Draft(SimWorld w, int st)
    {
        var d = new InteriorDraft(C);
        var map = w.Network.GetStructure(st).Modules;
        d.SetModules(k => map.TryGetValue(k, out var kind) ? kind : null, w.Network.FreeBuilding);
        d.Load(w.Network.ReadInterior(st));
        return d;
    }

    [Fact]
    public void A_dragged_belt_snaps_to_the_grid_splits_at_bends_and_links_what_it_touches()
    {
        var (w, st) = Make();
        var d = Draft(w, st);
        Assert.Null(d.PlaceMachine((9, 0, 2), Type("core:smelter")));
        Assert.Null(d.PlaceMachine((13, 0, 4), Type("core:depot")));
        // from beside the smelter, along X and then Z, ending beside the depot: two segments, the bend belongs to the first
        var path = InteriorDraft.DragPath((10, 0, 2), (12, 0, 4));
        Assert.Equal(new[] { (10, 0, 2), (11, 0, 2), (12, 0, 2), (12, 0, 3), (12, 0, 4) }, path);
        Assert.Null(d.PlaceBelt(path));
        var it = d.ToInterior();
        Assert.Equal(new[] { B(10, 0, 2, Dir.PosX, 3), new BlueprintBelt(12, 0, 3, Dir.PosZ, 2, BeltTier.Mk1) }, it.Belts);
        Assert.Equal(2, it.Machines.Length);
        // smelter -> first belt, first belt -> second belt; the second belt ends beside (13,0,4), so it feeds the depot
        int smelter = Array.FindIndex(it.Machines, m => m.Type == Type("core:smelter"));
        int depot = Array.FindIndex(it.Machines, m => m.Type == Type("core:depot"));
        Assert.Contains(new BlueprintLink(BlueprintLinkKind.MachineToBelt, smelter, 0), it.Links);
        Assert.Contains(new BlueprintLink(BlueprintLinkKind.BeltToBelt, 0, 1), it.Links);
        Assert.Contains(new BlueprintLink(BlueprintLinkKind.BeltToMachine, 1, depot), it.Links);
        var report = w.CheckBlueprint(0, st, Modules(w, st), it);
        Assert.True(report.Ok, report.Reject.ToString());
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), it)));
        Assert.Equal(it.Links.Count(), w.Network.ReadInterior(st).Links.Length);
        // a gap in the path and a taken cell refuse the whole belt
        Assert.Equal(PlanReasons.Gap, d.PlaceBelt(new[] { (9, 0, 6), (11, 0, 6) }));
        Assert.Equal(PlanReasons.Occupied, d.PlaceBelt(new[] { (9, 0, 6), (9, 0, 5), (9, 0, 4), (9, 0, 3), (9, 0, 2) }));
        Assert.True(d.Erase((11, 0, 2))); // erasing a cell of a belt removes the segment and its links
        Assert.DoesNotContain(d.Links, l => l.From == (10, 0, 2) || l.To == (10, 0, 2));
        Assert.True(d.Undo());
        Assert.Contains(d.Links, l => l.To == (10, 0, 2));
    }

    [Fact]
    public void The_ghost_rules_say_why_a_part_cannot_go_there()
    {
        var (w, st) = Make();
        var d = Draft(w, st);
        Assert.Equal(PlanReasons.NoModule, d.PlaceProblem((40, 0, 2), belt: false));
        Assert.Equal(PlanReasons.NotFloor, d.PlaceProblem((10, 1, 2), belt: false));
        Assert.Equal(PlanReasons.NotAllowed, d.PlaceProblem((10, 0, 2), belt: false, MachineRole.Turret)); // the bay takes no turret
        Assert.Null(d.PlaceProblem((2, 0, 10), belt: false, MachineRole.Turret));
        Assert.Null(d.PlaceMachine((10, 0, 2), Type("core:smelter")));
        Assert.Equal(PlanReasons.Occupied, d.PlaceMachine((10, 0, 2), Type("core:depot")));
        Assert.Equal(PlanReasons.NotAllowed, d.PlaceMachine((10, 0, 3), 9999));
        Assert.Null(d.PlaceBelt(new[] { (11, 0, 2) }));
        Assert.Equal(PlanReasons.Occupied, d.PlaceBelt(new[] { (11, 0, 2) }));
        var (dead, unfed) = d.BeltGaps();
        Assert.Single(dead);
        Assert.Empty(unfed); // the smelter beside it feeds it
        Assert.True(d.PowerUse() >= 0);
    }

    [Fact]
    public void The_session_draws_a_factory_on_a_floor_prices_it_like_the_simulation_and_applies_it()
    {
        var (w, st) = Make();
        var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        s.Open(st);
        Assert.False(s.CanOpenFloor((0, 0, -1))); // the dock module takes nothing on its floor
        Assert.True(s.CanOpenFloor((1, 0, 0)));
        Assert.True(s.OpenFloor((1, 0, 0)));
        Assert.Equal((1, 0, 0), s.FloorModule);
        Assert.Contains(Type("core:smelter"), s.PlanMachines());
        Assert.False(s.CanApply);
        s.PickPlanMachine(Type("core:smelter"));
        Assert.Null(s.PlanClick((9, 0, 2)));
        s.PickPlanMachine(Type("core:depot"));
        Assert.Null(s.PlanClick((13, 0, 2)));
        s.SetPlanTier(BeltTier.Mk1);
        Assert.Null(s.PlanDrag((10, 0, 2), (12, 0, 2)));
        s.SetPlanTool(PlanTool.Belt);
        Assert.Equal(PlanReasons.NotFloor, s.PlanClick((10, 1, 5)));
        Assert.True(s.Dirty);
        Assert.True(s.Report.Ok, s.Report.Reject.ToString());
        Assert.Equal(3, s.Report.PartsAdded);
        Assert.Equal(s.Report.Cost, s.Cost.ToPay);
        Assert.Equal(s.Report.Refund, s.Cost.ToRefund);
        Assert.True(s.CanApply, string.Join(",", s.Validation.Entries.Select(e => e.Code)));
        var cmd = Assert.IsType<ApplyBlueprint>(s.ToCommand());
        Assert.NotNull(cmd.Interior);
        Assert.Equal(2, cmd.Interior!.Machines.Length);
        // undo in the plan takes the last stroke back; the 3D draft is untouched
        Assert.True(s.Undo());
        Assert.False(s.Draft.Dirty);
        Assert.Equal(2, s.Report.PartsAdded);
        Assert.True(s.Redo());
        // selecting a machine and giving it a recipe (the machine panel of the plan)
        s.SetPlanTool(PlanTool.Select);
        Assert.Null(s.PlanClick((9, 0, 2)));
        Assert.Equal((9, 0, 2), s.PlanSelection);
        Assert.True(s.Interior.SetRecipe((9, 0, 2), C.RecipeIndex("core:smelt_iron")));
        Assert.Contains(C.RecipeIndex("core:smelt_iron"), s.RecipesOf(Type("core:smelter")));
        Assert.Equal(Reject.None, Run(w, s.ToCommand()));
        s.Open(st);
        Assert.False(s.Dirty);
        Assert.Equal(2, s.Interior.Machines.Count);
        Assert.Equal(C.RecipeIndex("core:smelt_iron"), s.Interior.MachineAt((9, 0, 2))!.Value.Recipe);
        // a station blueprint without a floor plan edit carries no interior (tag 65)
        s.PickKind(ModuleKind.Reactor);
        Assert.Null(s.Click((1, 0, 1)));
        Assert.Null(Assert.IsType<ApplyBlueprint>(s.ToCommand()).Interior);
    }

    [Fact]
    public void The_floor_plan_cost_equals_the_simulations_and_the_validation_warns_about_gaps_and_orphans()
    {
        int compared = 0, priced = 0, refunded = 0;
        for (int seed = 0; seed < 120; seed++)
        {
            var rng = new Random(seed + 7);
            var (w, st) = Make();
            var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
            s.Open(st);
            s.OpenFloor((1, 0, 0));
            for (int round = 0; round < 2; round++)
            {
                int ops = rng.Next(1, 7);
                for (int i = 0; i < ops; i++)
                {
                    int x = rng.Next(8, 16), z = rng.Next(0, 8);
                    switch (rng.Next(4))
                    {
                        case 0: s.PickPlanMachine(Type(rng.Next(2) == 0 ? "core:smelter" : "core:depot")); s.PlanClick((x, 0, z)); break;
                        case 1: s.SetPlanTier((BeltTier)rng.Next(0, 2)); s.PlanDrag((x, 0, z), (rng.Next(8, 16), 0, rng.Next(0, 8)), rng.Next(2) == 0); break;
                        case 2: s.SetPlanTool(PlanTool.Erase); s.PlanClick((x, 0, z)); break;
                        default:
                            s.SetPlanTool(PlanTool.Select);
                            s.PlanClick((x, 0, z));
                            if (s.PlanSelection is { } sel && s.Interior.MachineAt(sel) is { } m && s.RecipesOf(m.Type).Count > 0) s.Interior.SetRecipe(sel, s.RecipesOf(m.Type)[rng.Next(s.RecipesOf(m.Type).Count)]);
                            break;
                    }
                }
                if (!s.Report.Ok) continue;
                compared++;
                Assert.Equal(s.Report.Cost, s.Cost.ToPay);
                Assert.Equal(s.Report.Refund, s.Cost.ToRefund);
                Assert.Equal(s.Report.PartsAdded, s.InteriorChange.Added);
                Assert.Equal(s.Report.PartsRemoved, s.InteriorChange.Removed);
                if (s.Report.Cost.Length > 0) priced++;
                if (s.Report.Refund.Length > 0) refunded++;
                Assert.Equal(Reject.None, Run(w, s.ToCommand()));
                s.Open(st);
                s.OpenFloor((1, 0, 0));
            }
        }
        Assert.True(compared >= 100 && priced >= 30 && refunded >= 1, $"compared={compared} priced={priced} refunded={refunded}");

        // a belt that ends in nothing and a start nobody feeds are warnings that do not block; a missing module is an error
        var (w2, st2) = Make();
        var t = new BlueprintSession(C, ShipHandling.Default, () => w2, 0);
        t.Open(st2);
        t.OpenFloor((1, 0, 0));
        t.SetPlanTool(PlanTool.Belt);
        Assert.Null(t.PlanDrag((9, 0, 5), (12, 0, 5)));
        Assert.Contains(t.Validation.Entries, e => e.Code == "beltEnd" && e.Severity == ValidationSeverity.Warning);
        Assert.Contains(t.Validation.Entries, e => e.Code == "beltStart" && e.Severity == ValidationSeverity.Warning);
        Assert.True(t.CanApply);
        t.CloseFloor();
        t.SetTool(BlueprintTool.Delete);
        t.Click((1, 0, 0));
        Assert.Contains(t.Validation.Entries, e => e.Code == "orphans" && e.Blocks);
        Assert.False(t.CanApply);
        foreach (var e in t.Validation.Entries) Assert.True(e.Text(Language.Es).Length > 3 && e.Text(Language.En).Length > 3 && !e.Text(Language.Es).StartsWith("⟦"));
        // machines draw power: filling a bay with them raises the draw the stats show
        var (w3, st3) = Make();
        var p = new BlueprintSession(C, ShipHandling.Default, () => w3, 0);
        p.Open(st3);
        p.OpenFloor((1, 0, 0));
        p.PickPlanMachine(Type("core:smelter"));
        for (int x = 8; x < 16; x++) for (int z = 0; z < 8; z++) p.PlanClick((x, 0, z));
        Assert.True(p.Stats.PowerUse > p.StatsBefore.PowerUse);
    }
}
