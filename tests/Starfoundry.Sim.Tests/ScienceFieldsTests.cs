using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The science fields (ADR 0026): three science items made by dedicated machines, the research tree extended with 15 nodes in three
/// fields, and the lab that takes them by belt. The end-to-end tests give raw ore only and build every chain with sim commands.
/// </summary>
public class ScienceFieldsTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private const ulong Seed = 20261121UL;
    private static readonly string[] Science = { "core:science_engineering", "core:science_biology", "core:science_physics" };

    private static int R(string id) => C.ResearchIndex(id);

    // ---- content ------------------------------------------------------------------------------------

    [Fact]
    public void Science_recipes_exist_and_use_the_dedicated_machines()
    {
        void Check(string recipe, string machine, string inputs, string output, int seconds)
        {
            var r = C.Recipes[C.RecipeIndex(recipe)];
            Assert.Equal(machine, C.Machines[r.Machine].Id);
            Assert.Equal(inputs, string.Join(",", r.Inputs.Select(i => $"{C.Items[i.Item].Id}:{i.Count}")));
            Assert.Equal(output, string.Join(",", r.Outputs.Select(i => $"{C.Items[i.Item].Id}:{i.Count}")));
            Assert.Equal(seconds, r.TimeTicks / Units.TicksPerSecond);
        }
        Check("core:make_engineering_science", "core:engineering_bench", "core:iron_plate:2,core:circuit:1", "core:science_engineering:1", 6);
        Check("core:make_physics_science", "core:physics_lab", "core:silicon:1,core:nickel_plate:1", "core:science_physics:1", 8);
        Check("core:grow_biomass", "core:greenhouse", "core:ice:2,core:carbon:1", "core:biomass:1", 10);
        Check("core:make_biology_science", "core:greenhouse", "core:biomass:1,core:iron_plate:1", "core:science_biology:1", 6);
        foreach (var (m, power) in new[] { ("core:engineering_bench", 15), ("core:physics_lab", 18), ("core:greenhouse", 12) })
            Assert.Equal(power, C.Machines[C.MachineIndex(m)].PowerUse);
    }

    [Fact]
    public void The_extended_tree_has_five_nodes_per_field_that_cost_only_their_science()
    {
        foreach (var (field, item) in new[] { (ResearchField.Engineering, Science[0]), (ResearchField.Biology, Science[1]), (ResearchField.Physics, Science[2]) })
        {
            var nodes = C.Research.Where(n => n.Field == field).ToList();
            Assert.Equal(5, nodes.Count);
            foreach (var n in nodes)
            {
                Assert.Contains(n.Cost, c => c.Item == C.ItemIndex(item));
                Assert.All(n.Cost, c => Assert.Contains(C.Items[c.Item].Id, new[] { item, "core:iron_plate" }));
                Assert.NotEmpty(n.Requires);
            }
            Assert.Equal(1, nodes.Count(n => n.Requires.All(r => C.Research[r].Field != field))); // one entry into each field, the rest is a chain
        }
        Assert.Equal(24, C.Research.Count(n => n.Field == ResearchField.Basic)); // 22 plus the two mining laser nodes (ADR 0027)
    }

    // ---- bonuses ------------------------------------------------------------------------------------

    private static SimWorld World()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        return w;
    }

    [Fact]
    public void Structural_plating_raises_module_hit_points()
    {
        var w = World();
        int before = w.Network.ModuleMaxHp(ModuleKind.Workshop);
        w.Network.GrantResearch(R("core:structural_plating"));
        Assert.Equal(before * 125 / 100, w.Network.ModuleMaxHp(ModuleKind.Workshop));
        Assert.Equal(25, w.Network.BonusPercent(BonusKind.ModuleHp));
    }

    [Fact]
    public void Efficient_reactors_and_algae_fuel_change_output_and_burn_time()
    {
        var w = World();
        w.Network.GrantResearch(R("core:efficient_reactors"));
        w.Network.GrantResearch(R("core:algae_fuel"));
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Reactor);
        w.Network.GiveItems(0, C.ItemIndex("core:hydrogen"), 5);
        Assert.Equal(900 * 125 / 100, w.Network.BurnInterval(ModuleKind.Reactor));
        w.Step(2);
        Assert.Equal(100 * 115 / 100, w.Network.PowerStatus(s).Production);
        w.Step(900 * 125 / 100 - 10); // still burning the first unit
        Assert.Equal(1, 5 - w.Network.HoldCount(0, C.ItemIndex("core:hydrogen")));
    }

    [Fact]
    public void Long_range_jump_takes_a_carbon_off_the_jump_cost_and_field_theory_adds_shield_points()
    {
        var w = World();
        Assert.Equal(5, w.Network.JumpCost().Single().Count);
        w.Network.GrantResearch(R("core:long_range_jump"));
        Assert.Equal(4, w.Network.JumpCost().Single().Count);
        Assert.Equal(5, C.JumpCost.Single().Count); // the content is untouched

        var w2 = World();
        int s = w2.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w2.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Workshop);
        w2.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Shield);
        w2.Network.AddStructureModuleUnchecked(s, 2, 0, 0, ModuleKind.Shield);
        w2.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor);
        w2.Network.GiveItems(0, C.ItemIndex("core:hydrogen"), 20);
        w2.Step(60);
        int basic = w2.Network.ShieldAbsorbPercent(s);
        Assert.Equal(40, basic); // 30 for the first shield and 10 for the second
        w2.Network.GrantResearch(R("core:field_theory"));
        Assert.Equal(basic + 10, w2.Network.ShieldAbsorbPercent(s)); // points on top of the stack
    }

    [Fact]
    public void Cargo_sensor_and_drone_bonuses_apply()
    {
        var w = World();
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 1, ModuleKind.Cargo);
        int cap = w.Network.HoldCapacity(0);
        Assert.Equal(100, cap);
        w.Network.GrantResearch(R("core:cargo_expansion"));
        Assert.Equal(150, w.Network.HoldCapacity(0));
        long cells = w.Network.SensorRangeCells(ModuleKind.Sensor);
        w.Network.GrantResearch(R("core:bio_sensors"));
        w.Network.GrantResearch(R("core:quantum_sensors"));
        Assert.Equal(cells * 2, w.Network.SensorRangeCells(ModuleKind.Sensor));
        w.Network.GrantResearch(R("core:symbiotic_drones"));
        Assert.Equal(30, w.Network.BonusPercent(BonusKind.DroneRate));
    }

    // ---- the research panel -------------------------------------------------------------------------

    [Fact]
    public void Research_view_groups_nodes_by_field_and_names_them_in_both_languages()
    {
        var w = World();
        var groups = ResearchView.BuildByField(C, w.Network, 0);
        Assert.Equal(new[] { ResearchField.Basic, ResearchField.Engineering, ResearchField.Biology, ResearchField.Physics }, groups.Select(g => g.Field));
        Assert.Equal(C.Research.Count, groups.Sum(g => g.Entries.Count));
        Assert.All(groups, g => Assert.All(g.Entries, e => Assert.Equal(g.Field, e.Field)));
        Assert.Equal(ResearchState.Locked, groups[1].Entries.Single(e => e.Id == "core:structural_plating").State);
        foreach (var f in ResearchView.Fields) Assert.True(Loc.Has("research.field." + f.ToString().ToLowerInvariant()));
        Assert.Equal(("Ciencia de ingeniería", "Engineering science"), Loc.All.Single(p => p.Key == "item.core:science_engineering").Value);
        Assert.Equal(("Ciencia de biología", "Biology science"), Loc.All.Single(p => p.Key == "item.core:science_biology").Value);
        Assert.Equal(("Ciencia de física", "Physics science"), Loc.All.Single(p => p.Key == "item.core:science_physics").Value);
        Assert.All(Science, s => Assert.NotNull(ResearchView.ScienceColour(s)));
        Assert.Null(ResearchView.ScienceColour("core:iron_plate"));
        var distinct = Science.Select(ResearchView.ScienceColour).Distinct().Count();
        Assert.Equal(3, distinct);
    }

    [Fact]
    public void Every_new_string_exists_in_both_languages()
    {
        var keys = new List<string>();
        foreach (var n in C.Research.Where(n => n.Field != ResearchField.Basic))
        {
            keys.Add("research." + n.Id); keys.Add("research." + n.Id + ".desc");
            foreach (var b in n.Bonuses) keys.Add("bonus." + ResearchView.Unlocks(C, n).First(u => u.Kind == ResearchUnlockKind.Bonus).Id);
        }
        foreach (var m in new[] { "core:engineering_bench", "core:physics_lab", "core:greenhouse" }) { keys.Add("machine." + m); keys.Add("build.desc." + m); }
        foreach (var r in new[] { "make_engineering_science", "make_physics_science", "grow_biomass", "make_biology_science", "biomass_to_hydrogen" }) keys.Add("recipe.core:" + r);
        keys.AddRange(Science.Select(s => "item." + s)); keys.Add("item.core:biomass");
        keys.Add("enemy.core:bulwark");
        foreach (var key in keys)
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
        }
    }

    // ---- chains, end to end ------------------------------------------------------------------------

    private sealed class Rig
    {
        public readonly SimWorld W = World();
        public readonly int S;
        public Rig(params string[] grants)
        {
            var n = W.Network;
            S = n.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
            n.AddStructureModuleUnchecked(S, 0, 0, 0, ModuleKind.Workshop);
            n.AddStructureModuleUnchecked(S, 0, 0, -1, ModuleKind.Cockpit);
            n.AddStructureModuleUnchecked(S, 0, 0, -2, ModuleKind.Engine);
            for (int i = 0; i < 4; i++) n.AddStructureModuleUnchecked(S, -1 - i, 0, 0, ModuleKind.Cargo);
            for (int i = 0; i < 3; i++) n.AddStructureModuleUnchecked(S, 20 + i, 0, 0, ModuleKind.Reactor);
            n.GiveItems(0, C.ItemIndex("core:hydrogen"), 300);
            foreach (var g in grants) n.GrantResearch(R(g));
        }

        public int Place(int x, int y, int z, string machine, string? recipe = null)
        {
            W.Submit(0, new PlaceMachineIn(S, x, y, z, C.MachineIndex(machine), recipe is null ? -1 : C.RecipeIndex(recipe)));
            W.Step();
            var r = W.LastResults.Single();
            Assert.True(r.Ok, $"{machine} at {x},{y},{z}: {r.Reject}");
            return r.CreatedMachines[0];
        }

        private int Do(Command c)
        {
            W.Submit(0, c);
            W.Step();
            var r = W.LastResults.Single();
            Assert.True(r.Ok, $"{c}: {r.Reject}");
            return r.Created.Count > 0 ? r.Created[0] : -1;
        }

        /// <summary>A belt from a producer into a consumer: the belt starts next to <paramref name="from"/> and ends next to <paramref name="to"/>.</summary>
        public int Link(int from, int x, int y, int z, Dir d, int len, int to)
        {
            int b = Do(new PlaceSegmentIn(S, x, y, z, d, len, BeltTier.Mk1));
            Do(new ConnectFromMachine(from, b));
            Do(new ConnectToMachine(b, to));
            return b;
        }

        /// <summary>Port at x0, belt, smelter at x3, belt along the row to <paramref name="endX"/>; returns the last belt, not yet connected.</summary>
        public int SmeltRow(int y, int z, int endX, string port, string recipe)
        {
            int p = Place(0, y, z, port), s = Place(3, y, z, "core:smelter", recipe);
            Link(p, 1, y, z, Dir.PosX, 2, s);
            int b = Do(new PlaceSegmentIn(S, 4, y, z, Dir.PosX, endX - 3, BeltTier.Mk1));
            Do(new ConnectFromMachine(s, b));
            return b;
        }

        public void To(int belt, int machine) => Do(new ConnectToMachine(belt, machine));

        /// <summary>Two iron smelter rows (z and z+1) into a plate assembler at (7,1,z). Returns the assembler.</summary>
        public int PlateAssembler(int z)
        {
            int asm = Place(7, 1, z, "core:assembler", "core:iron_plate");
            To(SmeltRow(1, z, 6, "core:ore_unloader", "core:smelt_iron"), asm);
            To(SmeltRow(1, z + 1, 7, "core:ore_unloader", "core:smelt_iron"), asm);
            return asm;
        }

        public void Give(string item, int n) => W.Network.GiveItems(0, C.ItemIndex(item), n);

        public bool Research(string node, int ticks)
        {
            W.Submit(0, new SetResearch(R(node)));
            W.Step();
            for (int i = 0; i < ticks && !W.Network.IsResearched(R(node)); i += 30) W.Step(30);
            return W.Network.IsResearched(R(node));
        }
    }

    /// <summary>A lab fed only by the plates of its own plate line (the node's iron plates) at (7,3,6).</summary>
    private static void PlateLab(Rig r)
    {
        int asm = r.PlateAssembler(6);
        int lab = r.Place(7, 3, 6, "core:research_lab");
        r.Link(asm, 7, 2, 6, Dir.PosY, 1, lab);
    }

    [Fact]
    public void Engineering_chain_delivers_science_to_the_lab_by_belt_and_researches_structural_plating()
    {
        var r = new Rig("core:electronics", "core:silicon_processing", "core:titanium_smelting", "core:nickel_smelting", "core:advanced_plating");
        // Nickel ore -> ingots -> plates and quartz -> silicon into the circuit assembler at (7,1,0); iron plates into the bench at (7,1,2).
        int circuit = r.Place(7, 1, 0, "core:assembler", "core:make_circuit");
        int plate = r.Place(5, 1, 0, "core:assembler", "core:nickel_plate");
        r.To(r.SmeltRow(1, 0, 4, "core:nickel_ore_unloader", "core:smelt_nickel"), plate);
        r.Link(plate, 6, 1, 0, Dir.PosX, 1, circuit);
        r.To(r.SmeltRow(2, 0, 7, "core:quartz_unloader", "core:smelt_silicon"), circuit);
        int bench = r.Place(7, 1, 2, "core:engineering_bench", "core:make_engineering_science");
        r.Link(circuit, 7, 1, 1, Dir.PosZ, 1, bench);
        int iron = r.PlateAssembler(4);
        r.Link(iron, 7, 1, 3, Dir.NegZ, 1, bench);
        int lab = r.Place(7, 3, 2, "core:research_lab");
        r.Link(bench, 7, 2, 2, Dir.PosY, 1, lab);
        PlateLab(r);
        r.Give("core:iron_ore", 400); r.Give("core:nickel_ore", 80); r.Give("core:quartz", 80);

        Assert.True(r.Research("core:structural_plating", 30 * 60 * 10), "structural plating never finished");
        Assert.Equal(25, r.W.Network.BonusPercent(BonusKind.ModuleHp));
        Assert.True(r.W.Network.ResearchItemsUsed >= 110);
    }

    [Fact]
    public void Physics_chain_delivers_science_to_the_lab_by_belt_and_researches_field_theory()
    {
        var r = new Rig("core:shielding", "core:electronics", "core:silicon_processing", "core:titanium_smelting", "core:nickel_smelting", "core:advanced_plating");
        int phys = r.Place(7, 1, 0, "core:physics_lab", "core:make_physics_science");
        int plate = r.Place(5, 1, 0, "core:assembler", "core:nickel_plate");
        r.To(r.SmeltRow(1, 0, 4, "core:nickel_ore_unloader", "core:smelt_nickel"), plate);
        r.Link(plate, 6, 1, 0, Dir.PosX, 1, phys);
        r.To(r.SmeltRow(2, 0, 7, "core:quartz_unloader", "core:smelt_silicon"), phys);
        int lab = r.Place(7, 1, 2, "core:research_lab");
        r.Link(phys, 7, 1, 1, Dir.PosZ, 1, lab);
        PlateLab(r);
        r.Give("core:iron_ore", 300); r.Give("core:nickel_ore", 60); r.Give("core:quartz", 60);

        Assert.True(r.Research("core:field_theory", 30 * 60 * 10), "field theory never finished");
        Assert.Equal(10, r.W.Network.BonusPercent(BonusKind.ShieldAbsorb));
    }

    [Fact]
    public void Biology_chain_delivers_science_to_the_lab_by_belt_and_researches_algae_fuel()
    {
        var r = new Rig("core:advanced_plating", "core:titanium_smelting", "core:nickel_smelting");
        // Ice (west) and carbon (above) into the first greenhouse, biomass on to the second, plates in from the south.
        int g1 = r.Place(3, 1, 0, "core:greenhouse", "core:grow_biomass");
        int ice = r.Place(0, 1, 0, "core:ice_unloader");
        r.Link(ice, 1, 1, 0, Dir.PosX, 2, g1);
        int carbon = r.Place(0, 2, 0, "core:carbon_unloader");
        r.Link(carbon, 1, 2, 0, Dir.PosX, 3, g1);
        int g2 = r.Place(7, 1, 0, "core:greenhouse", "core:make_biology_science");
        r.Link(g1, 4, 1, 0, Dir.PosX, 3, g2);
        int iron = r.PlateAssembler(2);
        r.Link(iron, 7, 1, 1, Dir.NegZ, 1, g2);
        int lab = r.Place(7, 3, 0, "core:research_lab");
        r.Link(g2, 7, 2, 0, Dir.PosY, 1, lab);
        PlateLab(r);
        r.Give("core:iron_ore", 600); r.Give("core:ice", 60); r.Give("core:carbon", 30);

        Assert.True(r.Research("core:algae_fuel", 30 * 60 * 10), "algae fuel never finished: " + string.Join(", ", C.Research[R("core:algae_fuel")].Cost.Select(c => C.Items[c.Item].Id + " " + r.W.Network.ResearchProgress(R("core:algae_fuel"), c.Item))) + $" ice {r.W.Network.HoldCount(0, C.ItemIndex("core:ice"))} carbon {r.W.Network.HoldCount(0, C.ItemIndex("core:carbon"))}");
        Assert.Equal(25, r.W.Network.BonusPercent(BonusKind.FuelBurn));
    }

    [Fact]
    public void The_lab_takes_science_but_not_an_item_the_current_research_does_not_cost()
    {
        var w = World();
        w.Network.GrantResearch(R("core:electronics"));
        // With no research chosen, or a node that costs another field's science, a belt of science finds the lab closed.
        Assert.Contains(C.Research[R("core:structural_plating")].Cost, c => c.Item == C.ItemIndex("core:science_engineering"));
        Assert.DoesNotContain(C.Research[R("core:structural_plating")].Cost, c => c.Item == C.ItemIndex("core:science_biology"));
    }

    [Fact]
    public void Closed_loop_unlocks_hydrogen_from_biomass_and_nothing_else_does()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        int rec = C.RecipeIndex("core:biomass_to_hydrogen");
        Assert.True(w.Network.IsRecipeLocked(rec));
        w.Network.GrantResearch(R("core:closed_loop"));
        Assert.False(w.Network.IsRecipeLocked(rec));
        Assert.False(w.Network.IsRecipeLocked(C.RecipeIndex("core:grow_biomass"))); // the science chains need no unlock
    }
}
