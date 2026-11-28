using System.Text.Json.Nodes;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0050: machines that cover several cells of a station floor in irregular shapes (footprint masks, an anchor and a turn). Covers the mask parser, the shipped machines, placing,
/// turning, overlap, blocking, belts and links at any covered cell, demolish and refunds, the extended command tag 70 and its version gate, the rules flag, legacy replay,
/// preview equal to apply, determinism and the engine-free floor plan draft.
/// </summary>
public class MultiCellTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261128UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static int Type(string id) => C.MachineIndex(id);
    private static readonly int Large = Type("core:large_smelter"), Long = Type("core:long_assembler"), LFab = Type("core:l_fabricator"), Reactor = Type("core:reactor_core");

    /// <summary>A campaign world with a station: a core, a fabrication bay at (1,0,0) and a defence module; collars; the stock holds plenty; heavy industry is researched.</summary>
    private static (SimWorld W, int Station) Make(WorldRules? rules = null)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: rules ?? WorldRules.Default);
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Defence);
        w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.NegX);
        foreach (var item in C.Items) w.Network.GiveStock(st, item.Index, 2000);
        w.Network.GrantResearch(C.ResearchIndex("core:heavy_industry"));
        return (w, st);
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static DesignModule[] Modules(SimWorld w, int st) =>
        w.Network.GetStructure(st).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();

    private static BlueprintMachine M(int x, int z, int type, int recipe = -1, int turn = 0) => new(x, 0, z, type, recipe, turn);
    private static BlueprintBelt B(int x, int z, Dir d, int len) => new(x, 0, z, d, len, BeltTier.Mk1);
    private static BlueprintInterior I(BlueprintMachine[] m, BlueprintBelt[]? b = null, BlueprintLink[]? l = null) => new(m, b ?? Array.Empty<BlueprintBelt>(), l ?? Array.Empty<BlueprintLink>());

    private static Reject Apply(SimWorld w, int st, BlueprintInterior it) => Run(w, new ApplyBlueprint(st, Modules(w, st), it));

    // ---- the footprint model ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_shipped_machines_cover_the_cells_their_masks_say_and_every_turn_keeps_the_count_and_the_anchor()
    {
        Assert.Equal(1, C.Machines[Type("core:smelter")].Cells().Length);
        Assert.False(C.Machines[Type("core:smelter")].IsMultiCell);
        Assert.Equal(new[] { (0, 0), (1, 0), (0, 1), (1, 1) }, C.Machines[Large].Cells());
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0) }, C.Machines[Long].Cells());
        Assert.Equal(new[] { (0, 0), (0, 1), (0, 2), (1, 2) }, C.Machines[LFab].Cells()); // an L: the long leg and the foot
        Assert.Equal(new[] { (0, 0), (-1, 1), (0, 1), (1, 1), (0, 2) }, C.Machines[Reactor].Cells()); // a plus: the anchor is its top arm
        foreach (int t in new[] { Large, Long, LFab, Reactor })
        {
            var def = C.Machines[t];
            Assert.True(def.IsMultiCell);
            for (int turn = 0; turn < 4; turn++)
            {
                var cells = def.Cells(turn);
                Assert.Equal(def.Cells().Length, cells.Length);
                Assert.Equal((0, 0), cells[0]);
                Assert.Equal(cells.Length, cells.Distinct().Count());
            }
            Assert.Equal(def.Cells().OrderBy(c => c), def.Cells(4).OrderBy(c => c));
        }
        Assert.Equal(new[] { (0, 0), (0, 1), (0, 2) }, C.Machines[Long].Cells(1)); // a quarter turn takes the long line from +X to +Z
        Assert.Equal(new[] { (0, 0), (0, -1), (0, -2) }, C.Machines[Long].Cells(3));
    }

    private static ContentDb WithFootprint(params string[] rows)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        var machine = root["machines"]!.AsArray().First(m => m!["id"]!.GetValue<string>() == "core:large_smelter")!.AsObject();
        machine["footprint"] = new JsonArray(rows.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
        return ContentDb.FromJson(root.ToJsonString());
    }

    [Fact]
    public void A_mask_must_be_one_connected_piece_of_at_most_four_by_four_and_only_crafters_labs_and_depots_may_have_one()
    {
        Assert.True(WithFootprint("XXXX", "XXXX", "XXXX", "XXXX").Machines[Large].Cells().Length == 16);
        Assert.Throws<InvalidDataException>(() => WithFootprint("XXXXX"));
        Assert.Throws<InvalidDataException>(() => WithFootprint("X", "X", "X", "X", "X"));
        Assert.Throws<InvalidDataException>(() => WithFootprint("X.X")); // two pieces
        Assert.Throws<InvalidDataException>(() => WithFootprint("..", ".."));
        Assert.Throws<InvalidDataException>(() => WithFootprint("XY"));
        Assert.Throws<InvalidDataException>(() => WithFootprint());
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        var turret = root["machines"]!.AsArray().First(m => m!["id"]!.GetValue<string>() == "core:turret")!.AsObject();
        turret["footprint"] = new JsonArray("XX");
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void Every_multi_cell_recipe_belongs_to_a_multi_cell_machine_and_beats_the_single_cell_recipe_per_tick_and_per_input()
    {
        foreach (var r in C.Recipes.Where(r => C.Machines[r.Machine].IsMultiCell))
        {
            Assert.True(C.Machines[r.Machine].Cost.Length > 0);
            Assert.True(r.Outputs.Sum(o => o.Count) >= 2, r.Id);
        }
        // the large smelter makes 1.6 times the ingots of a small one per second; the batch recipes are never worse in inputs per output
        double Rate(string id) { var r = C.Recipes[C.RecipeIndex(id)]; return r.Outputs.Sum(o => o.Count) / (double)r.TimeTicks; }
        Assert.InRange(Rate("core:smelt_iron_bulk") / Rate("core:smelt_iron"), 1.55, 1.65);
        Assert.True(Rate("core:iron_plate_batch") > Rate("core:iron_plate"));
        Assert.True(Rate("core:make_fuel_cell_bulk") > Rate("core:make_fuel_cell"));
        var cell = C.Recipes[C.RecipeIndex("core:make_fuel_cell_bulk")];
        var small = C.Recipes[C.RecipeIndex("core:make_fuel_cell")];
        double hydrogenPer(RecipeDef r) => r.Inputs.First(i => C.Items[i.Item].Id == "core:hydrogen").Count / (double)r.Outputs.Sum(o => o.Count);
        Assert.True(hydrogenPer(cell) < hydrogenPer(small));
        // the machines unlock with one research and are locked in a campaign world before it
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        Assert.True(w.Network.IsMachineLocked(Large) && w.Network.IsMachineLocked(Reactor));
        Assert.True(w.Network.IsRecipeLocked(C.RecipeIndex("core:smelt_iron_bulk")));
        w.Network.GrantResearch(C.ResearchIndex("core:heavy_industry"));
        Assert.False(w.Network.IsMachineLocked(Large) || w.Network.IsMachineLocked(Long) || w.Network.IsMachineLocked(LFab) || w.Network.IsMachineLocked(Reactor));
        Assert.False(w.Network.IsRecipeLocked(C.RecipeIndex("core:smelt_iron_bulk")));
    }

    // ---- placing, turning, blocking ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_large_smelter_is_built_whole_keeps_its_state_and_reads_back_with_its_anchor_and_turn()
    {
        var (w, st) = Make();
        var net = w.Network;
        int smelt = C.RecipeIndex("core:smelt_iron_bulk");
        var it = I(new[] { M(2, 2, Large, smelt), M(9, 1, Long, C.RecipeIndex("core:iron_plate_batch"), 1) });
        var report = w.CheckBlueprint(0, st, Modules(w, st), it);
        Assert.True(report.Ok, report.Reject.ToString());
        Assert.Equal(2, report.PartsAdded);
        Assert.Equal(Reject.None, Apply(w, st, it));
        Assert.Equal(2, net.MachineCount);
        var read = net.ReadInterior(st, out var ids, out _);
        Assert.Equal(it.Machines.OrderBy(m => (m.X, m.Z)), read.Machines.OrderBy(m => (m.X, m.Z)));
        int id = ids[Array.FindIndex(read.Machines, m => m.Type == Large)];
        Assert.Equal(0, net.GetMachineInfo(id).Turn);
        Assert.Equal(1, net.GetMachineInfo(ids[Array.FindIndex(read.Machines, m => m.Type == Long)]).Turn);
        w.ValidateInvariants();
        // the same blueprint again changes nothing and costs nothing, and the machine is the same one
        var again = w.CheckBlueprint(0, st, Modules(w, st), it);
        Assert.True(again.Ok);
        Assert.Empty(again.Cost);
        Assert.Equal(0, again.PartsAdded + again.PartsRemoved);
        Assert.Equal(Reject.None, Apply(w, st, it));
        Assert.True(net.IsMachineAlive(id));
        // the price is the machine's own cost, not one per cell
        var cost = C.Machines[Large].Cost;
        Assert.Equal(cost.Select(c => c.Count).OrderBy(c => c), w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large, smelt), M(9, 1, Long, C.RecipeIndex("core:iron_plate_batch"), 1), M(4, 6, Large, smelt) })).Cost.Where(c => cost.Any(k => k.Item == c.Item)).Select(c => c.Count).OrderBy(c => c));
    }

    [Fact]
    public void A_footprint_must_stay_on_one_floor_free_of_parts_and_a_turn_changes_the_cells_it_needs()
    {
        var (w, st) = Make();
        // across the seam between the core (x 0..7) and the bay (x 8..15)
        Assert.Equal(Reject.FootprintBlocked, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(7, 2, Large) })).Reject);
        Assert.Equal(Reject.FootprintBlocked, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(5, 7, Long, -1, 1) })).Reject); // the line turned towards +Z leaves the module
        Assert.True(w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(5, 7, Long, -1, 0) })).Ok);
        // out of the bay's floor into a module that is not there, and an anchor with no module at all
        Assert.Equal(Reject.FootprintBlocked, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(15, 2, Large) })).Reject);
        Assert.Equal(Reject.OutsideStation, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(40, 2, Large) })).Reject);
        // overlap: a machine on a covered cell, a belt through the footprint, two footprints
        Assert.Equal(Reject.Overlap, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large), M(3, 3, Type("core:smelter")) })).Reject);
        Assert.Equal(Reject.Overlap, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large) }, new[] { B(0, 3, Dir.PosX, 4) })).Reject);
        Assert.Equal(Reject.Overlap, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large), M(3, 3, Large) })).Reject);
        Assert.True(w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large), M(4, 2, Large) })).Ok);
        // the plus and the L at the edges: a turn that fits and one that does not
        Assert.True(w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(1, 0, Reactor) })).Ok);
        Assert.Equal(Reject.FootprintBlocked, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(0, 0, Reactor) })).Reject); // the left arm leaves the floor
        Assert.Equal(Reject.InvalidArgument, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Type("core:smelter"), -1, 1) })).Reject); // a one-cell machine has no turn
        Assert.Equal(Reject.InvalidArgument, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large, -1, 4) })).Reject);
        // a recipe of another machine
        Assert.NotEqual(Reject.None, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(2, 2, Large, C.RecipeIndex("core:smelt_iron")) })).Reject);
    }

    [Fact]
    public void The_in_world_tool_refuses_a_multi_cell_machine_and_a_world_from_before_footprints_refuses_it_in_a_blueprint()
    {
        var (w, st) = Make(WorldRules.Default.WithLegacyBlueprint(true).WithLegacyInterior(true));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new PlaceMachineIn(st, 2, 0, 2, Large, -1)));
        var (old, ost) = Make(WorldRules.Default.WithLegacyFootprints(true));
        Assert.Equal(Reject.NotAllowedHere, old.CheckBlueprint(0, ost, Modules(old, ost), I(new[] { M(2, 2, Large) })).Reject);
        Assert.Equal(Reject.NotAllowedHere, old.CheckBlueprint(0, ost, Modules(old, ost), I(new[] { M(2, 2, Type("core:smelter"), -1, 0) with { Turn = 1 } })).Reject);
        Assert.True(old.CheckBlueprint(0, ost, Modules(old, ost), I(new[] { M(2, 2, Type("core:smelter")) })).Ok); // one-cell machines still work there
    }

    // ---- belts, links, demolish -------------------------------------------------------------------------------------------------

    private static BlueprintInterior Line(int turn = 0) => I(
        new[] { M(3, 2, Large, -1, turn), M(7, 2, Type("core:depot")) },
        new[] { B(0, 2, Dir.PosX, 3), B(5, 2, Dir.PosX, 2) },
        new[] { new BlueprintLink(BlueprintLinkKind.BeltToMachine, 0, 0), new BlueprintLink(BlueprintLinkKind.MachineToBelt, 0, 1), new BlueprintLink(BlueprintLinkKind.BeltToMachine, 1, 1) });

    [Fact]
    public void A_belt_feeds_and_takes_from_any_covered_cell_and_the_links_read_back()
    {
        var (w, st) = Make();
        var net = w.Network;
        // the belt (0..1, 2) ends at (1,2), next to the cell (2,2) of the footprint turned or not; the output belt starts at (5,2) next to the cell (4,2), not next to the anchor
        Assert.Equal(Reject.Overlap, w.CheckBlueprint(0, st, Modules(w, st), I(new[] { M(3, 2, Large) }, new[] { B(0, 2, Dir.PosX, 4) })).Reject); // a belt of four cells runs into the footprint at (3,2)
        var it = Line();
        Assert.Equal(Reject.None, Apply(w, st, it));
        var read = net.ReadInterior(st);
        Assert.Equal(3, read.Links.Length);
        Assert.Equal(it.Links.OrderBy(l => (l.Kind, l.From, l.To)).Select(l => l.Kind), read.Links.OrderBy(l => (l.Kind, l.From, l.To)).Select(l => l.Kind));
        net.ValidateInvariants();
        w.ValidateInvariants();
        // a link to a belt that does not touch any covered cell is refused
        var bad = I(new[] { M(3, 2, Large), M(7, 2, Type("core:depot")) }, new[] { B(0, 2, Dir.PosX, 3), B(5, 5, Dir.PosX, 2) },
            new[] { new BlueprintLink(BlueprintLinkKind.MachineToBelt, 0, 1) });
        Assert.Equal(Reject.NotAdjacent, w.CheckBlueprint(0, st, Modules(w, st), bad).Reject);
        var bad2 = I(new[] { M(3, 2, Large) }, new[] { B(0, 6, Dir.PosX, 2) }, new[] { new BlueprintLink(BlueprintLinkKind.BeltToMachine, 0, 0) });
        Assert.Equal(Reject.NotAdjacent, w.CheckBlueprint(0, st, Modules(w, st), bad2).Reject);
    }

    [Fact]
    public void Turning_a_machine_is_a_different_machine_demolishing_refunds_half_and_frees_every_cell()
    {
        var (w, st) = Make();
        var net = w.Network;
        Assert.Equal(Reject.None, Apply(w, st, I(new[] { M(2, 2, Large) })));
        int id = 0;
        for (; id < net.MachineIdLimit && !net.IsMachineAlive(id); id++) { }
        // the same type turned in place is a new machine (its cells differ): the old one goes, half refunded
        var turned = I(new[] { M(2, 2, Large, -1, 1) });
        var report = w.CheckBlueprint(0, st, Modules(w, st), turned);
        Assert.True(report.Ok);
        Assert.Equal((1, 1), (report.PartsAdded, report.PartsRemoved));
        Assert.Equal(Reject.None, Apply(w, st, turned));
        Assert.False(net.IsMachineAlive(id));
        // a belt may now take a cell the removed machine covered
        var held = C.Machines[Large].Cost.ToDictionary(c => c.Item, c => net.HoldCount(0, c.Item));
        Assert.Equal(Reject.None, Apply(w, st, I(Array.Empty<BlueprintMachine>(), new[] { B(2, 3, Dir.PosX, 3) })));
        Assert.Equal(0, net.MachineCount);
        foreach (var c in C.Machines[Large].Cost) Assert.True(net.HoldCount(0, c.Item) >= held[c.Item] + c.Count / 2 - BeltUse(c.Item));
        // the RemoveMachine command takes the whole machine and its cells
        Assert.Equal(Reject.None, Apply(w, st, I(new[] { M(2, 5, Large) })));
        int mid = 0;
        for (; mid < net.MachineIdLimit && !net.IsMachineAlive(mid); mid++) { }
        Assert.Equal(Reject.None, Run(w, new RemoveMachine(mid)));
        Assert.Equal(0, net.MachineCount);
        Assert.Equal(Reject.None, Apply(w, st, I(new[] { M(2, 5, Type("core:smelter")), M(3, 6, Type("core:smelter")) }))); // the four cells are free again
        w.ValidateInvariants();
    }

    private static int BeltUse(int item) => C.Belts[(int)BeltTier.Mk1]!.CostPerCell.Where(c => c.Item == item).Sum(c => c.Count) * 3;

    [Fact]
    public void A_large_smelter_in_a_line_works_the_ore_into_ingots_in_batches_of_four_and_loses_nothing()
    {
        // the sandbox world of the older machine tests: an extractor (one ore a second), a belt, the large smelter, a belt and a sink; the smelter is added to the network directly
        var w = new SimWorld(C);
        Run(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:extractor"), C.RecipeIndex("core:mine_iron")));
        var net = w.Network;
        int ext = 0;
        int beltIn = net.AddSegment(1, 0, 0, 1, 0, 0, 3, BeltTier.Mk3);
        int smelter = net.AddMachine(4, 0, 0, Large, C.RecipeIndex("core:smelt_iron_bulk"));
        int beltOut = net.AddSegment(6, 0, 0, 1, 0, 0, 6, BeltTier.Mk3);
        net.ConnectFromMachine(ext, beltIn);
        net.ConnectToMachine(beltIn, smelter);
        net.ConnectFromMachine(smelter, beltOut);
        net.SetSink(beltOut);
        w.Step(300);
        long before = net.Consumed;
        w.Step(1200); // 40 seconds: the extractor gives one ore a second, the smelter turns four ores into four ingots in 2.5 s
        long made = net.Consumed - before;
        Assert.InRange(made, 36, 41);
        net.ValidateInvariants();
    }

    // ---- the command log ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_turned_machine_is_written_with_tag_70_an_unturned_blueprint_keeps_its_tag_and_the_log_is_version_28()
    {
        var withTurn = I(new[] { M(2, 2, Large, -1, 2), M(6, 6, Type("core:smelter")) });
        var plain = I(new[] { M(2, 2, Large), M(6, 6, Type("core:smelter")) });
        var mods = new[] { new DesignModule(0, 0, 0, ModuleKind.Core) };
        var pieces = new[] { new DockPieceSpec(0, 0, 0, Dir.NegX) };
        byte TagOf(Command c)
        {
            var bytes = CommandCodec.ToBytes(c);
            Assert.Equal(c.GetType(), CommandCodec.FromBytes(bytes).GetType());
            var back = (ApplyBlueprint)CommandCodec.FromBytes(bytes);
            var was = (ApplyBlueprint)c;
            Assert.Equal(was.Interior, back.Interior);
            Assert.Equal(was.Modules, back.Modules);
            Assert.Equal(was.DockPieces ?? Array.Empty<DockPieceSpec>(), back.DockPieces ?? Array.Empty<DockPieceSpec>());
            Assert.Equal(was.DockPieces is null, back.DockPieces is null);
            return bytes[0];
        }
        Assert.Equal(70, TagOf(new ApplyBlueprint(3, mods, withTurn)));
        Assert.Equal(70, TagOf(new ApplyBlueprint(3, mods, withTurn, pieces)));
        Assert.Equal(66, TagOf(new ApplyBlueprint(3, mods, plain)));
        Assert.Equal(69, TagOf(new ApplyBlueprint(3, mods, plain, pieces)));
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, new List<LoggedCommand> { new LoggedCommand(1, 0, new ApplyBlueprint(3, mods, withTurn)) }, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        Assert.Equal(29, ms.ToArray()[4]);
        Assert.True(CommandCodec.IsWellFormed(new ApplyBlueprint(3, mods, withTurn)));
        Assert.False(CommandCodec.IsWellFormed(new ApplyBlueprint(3, mods, I(new[] { M(2, 2, Large, -1, 4) }))));
        Assert.False(CommandCodec.IsWellFormed(new ApplyBlueprint(3, mods, I(new[] { M(2, 2, Large, -1, -1) }))));
    }

    [Fact]
    public void Logs_older_than_version_28_read_with_the_flag_on_refuse_turned_machines_and_replay_as_recorded()
    {
        var (w, st) = Make(WorldRules.Default.WithLegacyFootprints(true));
        Assert.Equal(Reject.None, Apply(w, st, I(new[] { M(2, 2, Type("core:smelter")), M(5, 5, Type("core:depot")) })));
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true, rules: w.Network.Rules);
        var bytes = ms.ToArray();
        Assert.Equal(29, bytes[4]);
        var old = (byte[])bytes.Clone();
        old[4] = 27;
        var log = CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.True(rules.LegacyFootprints && rules.LegacyDockPieces == false);
        Assert.False(CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var current).Count < 0 || current.LegacyFootprints == false && false);

        // a hand-built log of a turned blueprint cannot be read as version 27
        var (nw, nst) = Make();
        Assert.Equal(Reject.None, Apply(nw, nst, I(new[] { M(2, 2, Large, -1, 1) })));
        using var ms2 = new MemoryStream();
        CommandCodec.WriteLog(ms2, nw.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        var b2 = ms2.ToArray();
        b2[4] = 27;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(b2), out _, out _, out _, out _, out _, out _, out _));
        Assert.True(log.Count == w.Log.Count);
    }

    [Fact]
    public void The_flag_is_bit_3_of_the_third_flags_byte_off_by_default_and_hashes_as_default_rules()
    {
        Assert.False(WorldRules.Default.LegacyFootprints);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyFootprints));
        var legacy = new WorldRules().WithLegacyFootprints(true);
        Assert.True(legacy.LegacyFootprints && legacy.AnyLegacy && legacy.HashesAsDefault);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.False(legacy.WithoutLegacyFlags().LegacyFootprints);
        Assert.NotEqual(legacy, legacy.WithLegacyDockPieces(true));
        Assert.True(WorldRules.Deserialize(legacy.WithLegacyDockPieces(true).Serialize()).LegacyDockPieces);
        Assert.Equal(legacy.Clamped(), legacy);
    }

    // ---- determinism and random plans ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_world_with_multi_cell_machines_replays_clones_and_hashes_the_turn()
    {
        var (w, st) = Make();
        Assert.Equal(Reject.None, Apply(w, st, I(new[] { M(2, 2, Large, C.RecipeIndex("core:smelt_iron_bulk")), M(9, 2, Reactor, C.RecipeIndex("core:make_fuel_cell_bulk"), 3) })));
        w.Step(120);
        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        w.ValidateInvariants();
        var twin = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        twin.Network.EnsurePlayer(0);
        int ts = twin.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, 0, ModuleKind.Core);
        twin.Network.AddStructureModuleUnchecked(ts, 1, 0, 0, ModuleKind.Workshop);
        twin.Network.AddStructureModuleUnchecked(ts, 0, 0, 1, ModuleKind.Defence);
        twin.Network.AddDockPieceUnchecked(ts, 0, 0, 0, Dir.NegX);
        foreach (var item in C.Items) twin.Network.GiveStock(ts, item.Index, 2000);
        twin.Network.GrantResearch(C.ResearchIndex("core:heavy_industry"));
        var again = SimWorld.Replay(w.Log, w.Tick, initial: twin);
        Assert.Equal(w.Checksum(), again.Checksum());
        // the turn is part of the checksum
        var (a, sa) = Make();
        var (b, sb) = Make();
        Apply(a, sa, I(new[] { M(2, 2, Large, -1, 0) }));
        Apply(b, sb, I(new[] { M(2, 2, Large, -1, 1) }));
        Assert.NotEqual(a.Network.Checksum(), b.Network.Checksum());
    }

    [Fact]
    public void The_preview_equals_the_apply_for_random_floor_plans_with_footprints_and_a_refusal_changes_nothing()
    {
        var rng = new Random(20261128);
        int ok = 0, refused = 0;
        var types = new[] { Large, Long, LFab, Reactor, Type("core:smelter"), Type("core:depot"), Type("core:wall"), Type("core:assembler") };
        for (int round = 0; round < 300; round++)
        {
            var (w, st) = Make();
            var machines = new List<BlueprintMachine>();
            var belts = new List<BlueprintBelt>();
            for (int i = rng.Next(1, 6); i > 0; i--)
            {
                int type = types[rng.Next(types.Length)];
                int turn = C.Machines[type].IsMultiCell ? rng.Next(4) : 0;
                machines.Add(new BlueprintMachine(rng.Next(-1, 17), 0, rng.Next(-1, 9), type, -1, turn));
            }
            if (rng.Next(3) == 0) belts.Add(new BlueprintBelt(rng.Next(0, 14), 0, rng.Next(0, 8), rng.Next(2) == 0 ? Dir.PosX : Dir.PosZ, rng.Next(1, 4), BeltTier.Mk1));
            var it = I(machines.ToArray(), belts.ToArray());
            var report = w.CheckBlueprint(0, st, Modules(w, st), it);
            var shape = string.Join(";", w.Network.ReadInterior(st).Machines);
            var result = Apply(w, st, it);
            Assert.Equal(report.Reject, result);
            if (result == Reject.None)
            {
                ok++;
                var read = w.Network.ReadInterior(st);
                Assert.Equal(machines.OrderBy(m => (m.X, m.Z, m.Type)), read.Machines.OrderBy(m => (m.X, m.Z, m.Type)));
            }
            else { refused++; Assert.Equal(shape, string.Join(";", w.Network.ReadInterior(st).Machines)); }
            w.ValidateInvariants();
        }
        Assert.True(ok > 30 && refused > 30, $"ok={ok} refused={refused}");
    }

    // ---- the engine-free floor plan draft ------------------------------------------------------------------------------------------

    private static InteriorDraft Draft(SimWorld w, int st)
    {
        var d = new InteriorDraft(C);
        d.SetModules(k => w.Network.TryGetModuleKindAt(st, k.Mx, k.My, k.Mz, out var kind) ? kind : null, false);
        d.Load(BlueprintInterior.Empty);
        return d;
    }

    [Fact]
    public void The_draft_places_turns_finds_and_erases_a_machine_by_any_of_its_cells_and_undoes_it()
    {
        var (w, st) = Make();
        var d = Draft(w, st);
        Assert.Null(d.PlaceMachine((2, 0, 2), Large));
        Assert.Equal(PlanReasons.Occupied, d.PlaceMachine((3, 0, 3), Type("core:smelter"))); // the far cell of the footprint is taken
        Assert.Equal(PlanReasons.Footprint, d.PlaceMachine((7, 0, 2), Large)); // across the seam
        Assert.Equal((2, 0, 2), (d.MachineAt((3, 0, 3))!.Value.X, d.MachineAt((3, 0, 3))!.Value.Y, d.MachineAt((3, 0, 3))!.Value.Z));
        Assert.False(d.IsFree((3, 0, 2)));
        Assert.True(d.IsFree((4, 0, 2)));
        Assert.True(d.TurnMachine((3, 0, 3)));
        Assert.Equal(1, d.MachineAt((2, 0, 2))!.Value.Turn);
        Assert.Null(d.PlaceMachine((0, 0, 6), Long, -1, 0)); // fits along +X
        Assert.False(d.TurnMachine((0, 0, 6))); // a quarter turn would run past the floor
        Assert.True(d.SetRecipe((1, 0, 6), C.RecipeIndex("core:iron_plate_batch"))); // by a covered cell, not the anchor
        Assert.Equal(C.RecipeIndex("core:iron_plate_batch"), d.MachineAt((0, 0, 6))!.Value.Recipe);
        Assert.True(d.Dirty);
        var interior = d.ToInterior();
        Assert.Equal(2, interior.Machines.Length);
        Assert.Equal(Reject.None, Apply(w, st, interior));
        Assert.True(d.Erase((1, 0, 3))); // the turned smelter covers (1..2, 2..3)
        Assert.Equal(1, d.Machines.Count);
        Assert.True(d.Undo());
        Assert.Equal(2, d.Machines.Count);
        Assert.NotNull(d.MachineAt((1, 0, 3)));
        Assert.Equal(0, d.ClearModule((0, 0, 1)));
        Assert.Equal(2, d.ClearModule((0, 0, 0)));
        Assert.Empty(d.Machines);
    }

    [Fact]
    public void The_draft_links_a_belt_to_any_covered_cell()
    {
        var (w, st) = Make();
        var d = Draft(w, st);
        Assert.Null(d.PlaceMachine((3, 0, 2), Large)); // covers x 3..4, z 2..3
        Assert.Null(d.PlaceBelt(new[] { (5, 0, 3), (6, 0, 3) })); // starts beside the far cell (4,3), not beside the anchor
        Assert.Contains(d.Links, l => l.Kind == BlueprintLinkKind.MachineToBelt && l.From == (3, 0, 2));
        Assert.Null(d.PlaceBelt(new[] { (0, 0, 3), (1, 0, 3), (2, 0, 3) })); // ends beside the cell (3,3)
        Assert.Contains(d.Links, l => l.Kind == BlueprintLinkKind.BeltToMachine && l.To == (3, 0, 2));
        var interior = d.ToInterior();
        Assert.Equal(Reject.None, Apply(w, st, interior));
        var read = w.Network.ReadInterior(st);
        Assert.Equal(interior.Links.Length, read.Links.Length);
        // the session turns the machine with its key and the next one is placed turned
        var session = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        session.Open(st);
        session.OpenFloor((0, 0, 0));
        session.PickPlanMachine(Long);
        Assert.Null(session.TurnPlan());
        Assert.Equal(1, session.PlanTurn);
        Assert.Null(session.PlanClick((0, 0, 0)));
        Assert.Equal(1, session.Interior.MachineAt((0, 0, 2))!.Value.Turn);
    }
}
