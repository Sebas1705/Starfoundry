using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Silicon, circuits, titanium alloy and fuel cells: the recipes and their research, a real factory chain in a campaign structure
/// world, fuel cells in the reactor, the mark 2 reactor and the research gate of the new modules (ADR 0024).
/// </summary>
public class ResourceExpansionTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private const ulong Seed = 20261121UL;

    private static int Item(string id) => C.ItemIndex(id);

    private static SimWorld Campaign() =>
        SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);

    // ---- content ---------------------------------------------------------------------------------

    [Fact]
    public void The_new_items_are_appended_after_the_old_ones()
    {
        Assert.Equal(new[] { "core:silicon", "core:circuit", "core:titanium_alloy", "core:fuel_cell" },
            C.Items.Skip(14).Take(4).Select(i => i.Id).ToArray());
        Assert.Equal("core:hydrogen", C.Items[13].Id);
        Assert.Equal(Item("core:fuel_cell"), C.FuelCellItem);
    }

    [Theory]
    [InlineData("core:smelt_silicon", "core:smelter", "core:quartz:2", "core:silicon:1", 5)]
    [InlineData("core:make_circuit", "core:assembler", "core:silicon:1,core:nickel_plate:1", "core:circuit:1", 6)]
    [InlineData("core:make_alloy", "core:assembler", "core:titanium_plate:1,core:carbon:1", "core:titanium_alloy:1", 5)]
    [InlineData("core:make_fuel_cell", "core:hydrogen_cracker", "core:hydrogen:2,core:nickel_plate:1", "core:fuel_cell:1", 8)]
    public void The_new_recipes_exist_with_the_designed_numbers_and_are_gated(string id, string machine, string inputs, string outputs, int seconds)
    {
        ItemStack[] Parse(string s) => s.Split(',').Select(p => { int at = p.LastIndexOf(':'); return new ItemStack(Item(p[..at]), int.Parse(p[(at + 1)..])); }).ToArray();
        var r = C.Recipes[C.RecipeIndex(id)];
        Assert.Equal(C.MachineIndex(machine), r.Machine);
        Assert.Equal(Parse(inputs), r.Inputs);
        Assert.Equal(Parse(outputs), r.Outputs);
        Assert.Equal(seconds * Units.TicksPerSecond, r.TimeTicks);
        Assert.True(C.RecipeGated[r.Index], id + " must be locked behind research");
        Assert.Contains(C.Research, n => n.UnlockRecipes.Contains(r.Index));
    }

    [Fact]
    public void The_research_chain_is_wired_as_designed()
    {
        ResearchDef N(string id) => C.Research[C.ResearchIndex(id)];
        Assert.Equal(new[] { C.ResearchIndex("core:advanced_plating") }, N("core:silicon_processing").Requires);
        Assert.Equal(new[] { C.ResearchIndex("core:silicon_processing") }, N("core:electronics").Requires);
        foreach (var id in new[] { "core:shielding", "core:sensors", "core:advanced_power" })
            Assert.Equal(new[] { C.ResearchIndex("core:electronics") }, N(id).Requires);

        Assert.Equal(new[] { C.RecipeIndex("core:smelt_silicon") }, N("core:silicon_processing").UnlockRecipes);
        Assert.Equal(new[] { C.RecipeIndex("core:make_circuit"), C.RecipeIndex("core:make_alloy") }, N("core:electronics").UnlockRecipes);
        Assert.Equal(new[] { Ability.Shield }, N("core:shielding").UnlockAbilities);
        Assert.Equal(new[] { Ability.Sensor }, N("core:sensors").UnlockAbilities);
        Assert.Equal(new[] { Ability.ReactorMk2 }, N("core:advanced_power").UnlockAbilities);
        Assert.Equal(new[] { C.RecipeIndex("core:make_fuel_cell") }, N("core:advanced_power").UnlockRecipes);

        Assert.Equal(new[] { new ItemStack(Item("core:iron_plate"), 200), new ItemStack(Item("core:quartz"), 30), new ItemStack(Item("core:nickel_plate"), 20) },
            N("core:silicon_processing").Cost);
        Assert.Equal(new[] { new ItemStack(Item("core:iron_plate"), 250), new ItemStack(Item("core:silicon"), 40), new ItemStack(Item("core:titanium_plate"), 20) },
            N("core:electronics").Cost);
    }

    [Fact]
    public void The_new_module_kinds_have_the_designed_costs_and_numbers()
    {
        var s = C.Structures!;
        ItemStack[] Cost(params (string, int)[] c) => c.Select(p => new ItemStack(Item(p.Item1), p.Item2)).ToArray();
        Assert.Equal(Cost(("core:circuit", 12), ("core:titanium_alloy", 12), ("core:iron_plate", 16)), s.ModuleCost(ModuleKind.Shield));
        Assert.Equal(Cost(("core:circuit", 8), ("core:silicon", 10), ("core:iron_plate", 12)), s.ModuleCost(ModuleKind.Sensor));
        Assert.Equal(Cost(("core:circuit", 20), ("core:titanium_alloy", 20), ("core:iron_plate", 20)), s.ModuleCost(ModuleKind.ReactorMk2));
        Assert.Equal((250, 120, 180), (s.ModuleHp(ModuleKind.Shield), s.ModuleHp(ModuleKind.Sensor), s.ModuleHp(ModuleKind.ReactorMk2)));
        Assert.Equal((25, 10), (s.ModulePowerUse(ModuleKind.Shield), s.ModulePowerUse(ModuleKind.Sensor)));
        Assert.Equal((250, 900, 15), (s.PowerOutput(ModuleKind.ReactorMk2), s.BurnIntervalTicks(ModuleKind.ReactorMk2), s.EmergencyPower(ModuleKind.ReactorMk2)));
        Assert.Equal(120, s.SensorRangeCells(ModuleKind.Sensor));
        Assert.Equal((30, 10, 60), (s.Get(ModuleKind.Shield).ShieldPercent, s.Get(ModuleKind.Shield).ShieldStackPercent, s.Get(ModuleKind.Shield).ShieldMaxPercent));
        Assert.Equal("reactor_mk2", ModuleKindIds.Id(ModuleKind.ReactorMk2));
    }

    [Fact]
    public void Every_new_text_exists_in_both_languages()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        var keys = new List<string> { "hud.nextWave", "hud.nextWaveUnknown", "ability.shield", "ability.sensor", "ability.reactor_mk2" };
        foreach (var id in new[] { "silicon", "circuit", "titanium_alloy", "fuel_cell" }) keys.Add("item.core:" + id);
        foreach (var id in new[] { "smelt_silicon", "make_circuit", "make_alloy", "make_fuel_cell" }) keys.Add("recipe.core:" + id);
        foreach (var id in new[] { "silicon_processing", "electronics", "shielding", "sensors", "advanced_power" }) { keys.Add("research.core:" + id); keys.Add("research.core:" + id + ".desc"); }
        foreach (var k in new[] { ModuleKind.Shield, ModuleKind.Sensor, ModuleKind.ReactorMk2 })
            foreach (var name in new[] { k.ToString().ToLowerInvariant(), char.ToLowerInvariant(k.ToString()[0]) + k.ToString()[1..] })
            { keys.Add("module." + name); keys.Add("module." + name + ".purpose"); }
        foreach (var key in keys)
        {
            Assert.True(table.ContainsKey(key), key);
            var (es, en) = table[key];
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
        }
    }

    [Fact]
    public void Content_without_the_new_module_kinds_still_loads_and_cannot_place_them()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        root.Remove("structure_templates");
        foreach (var kind in new[] { "shield", "sensor", "reactor_mk2" }) root["structures"]!.AsObject().Remove(kind);
        var old = ContentDb.FromJson(root.ToJsonString());
        Assert.False(old.Structures!.IsDefined(ModuleKind.Shield));
        Assert.True(old.Structures.IsDefined(ModuleKind.DroneBay));
        Assert.NotEqual(C.Hash, old.Hash);
        Assert.Empty(Starfoundry.Ui.BuildCatalog.Entries(old, structures: true, stationRules: false).Where(e => e.Module is ModuleKind.Shield or ModuleKind.Sensor or ModuleKind.ReactorMk2));

        var w = SimWorld.CreateGalaxy(old, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        int s = PowerTests.Ship(w, hydrogen: 5);
        Assert.Equal(Reject.KindNotAllowed, Place(w, s, 5, ModuleKind.Shield));
    }

    // ---- research gate ---------------------------------------------------------------------------

    private static Reject Place(SimWorld w, int s, int mx, ModuleKind kind)
    {
        w.Submit(0, new PlaceModuleIn(s, mx, 0, 0, kind));
        w.Step();
        return w.LastResults.Single().Reject;
    }

    [Theory]
    [InlineData(ModuleKind.Shield, "core:shielding")]
    [InlineData(ModuleKind.Sensor, "core:sensors")]
    [InlineData(ModuleKind.ReactorMk2, "core:advanced_power")]
    public void The_new_modules_are_locked_in_campaign_until_researched(ModuleKind kind, string research)
    {
        var w = Campaign();
        int s = PowerTests.Ship(w, hydrogen: 5); // modules 0..4
        Assert.Equal(Reject.Locked, Place(w, s, 5, kind));
        Assert.False(w.Network.GetStructure(s).Modules.ContainsKey((5, 0, 0)));
        w.Network.GrantResearch(C.ResearchIndex(research));
        foreach (var c in C.Structures!.ModuleCost(kind)) w.Network.GiveItems(0, c.Item, c.Count);
        Assert.Equal(Reject.None, Place(w, s, 5, kind));
        Assert.Equal(kind, w.Network.GetStructure(s).Modules[(5, 0, 0)]);
        w.ValidateInvariants();
    }

    [Fact]
    public void Sandbox_needs_no_research_for_the_new_modules()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        int s = PowerTests.Ship(w, hydrogen: 5);
        Assert.Equal(Reject.None, Place(w, s, 5, ModuleKind.Shield));
    }

    [Fact]
    public void The_new_recipes_are_locked_until_their_research()
    {
        var w = Campaign();
        var n = w.Network;
        Assert.True(n.IsRecipeLocked(C.RecipeIndex("core:smelt_silicon")));
        n.GrantResearch(C.ResearchIndex("core:silicon_processing"));
        Assert.False(n.IsRecipeLocked(C.RecipeIndex("core:smelt_silicon")));
        Assert.True(n.IsRecipeLocked(C.RecipeIndex("core:make_circuit")));
        Assert.True(n.IsRecipeLocked(C.RecipeIndex("core:make_fuel_cell")));
        n.GrantResearch(C.ResearchIndex("core:advanced_power"));
        Assert.False(n.IsRecipeLocked(C.RecipeIndex("core:make_circuit")));
        Assert.False(n.IsRecipeLocked(C.RecipeIndex("core:make_alloy")));
        Assert.False(n.IsRecipeLocked(C.RecipeIndex("core:make_fuel_cell")));
    }

    // ---- a real chain ------------------------------------------------------------------------------

    /// <summary>Places a machine in the structure, no module checks (the layout is a test fixture, not a build).</summary>
    private static int Put(BeltNetwork n, int s, string machine, int x, int y, int z, string recipe = "")
        => n.AddMachine(x, y, z, C.MachineIndex(machine), recipe == "" ? -1 : C.RecipeIndex(recipe), owner: 0, structure: s);

    /// <summary>A straight belt from one machine to another (they must share two axes), wired both ends.</summary>
    private static void Link(BeltNetwork n, int s, (int Machine, int X, int Y, int Z) from, (int Machine, int X, int Y, int Z) to)
    {
        int dx = Math.Sign(to.X - from.X), dy = Math.Sign(to.Y - from.Y), dz = Math.Sign(to.Z - from.Z);
        int length = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) + Math.Abs(to.Z - from.Z) - 1;
        int seg = n.AddSegment(from.X + dx, from.Y + dy, from.Z + dz, dx, dy, dz, length, BeltTier.Mk1, s);
        n.ConnectFromMachine(from.Machine, seg);
        n.ConnectToMachine(seg, to.Machine);
    }

    private static (int, int, int, int) At(int m, int x, int y, int z) => (m, x, y, z);

    [Fact]
    public void Quartz_becomes_silicon_and_circuits_and_titanium_becomes_alloy_through_real_ports_and_belts()
    {
        var w = Campaign();
        var n = w.Network;
        int s = PowerTests.Ship(w, reactors: 2, hydrogen: 20);
        n.GrantResearch(C.ResearchIndex("core:electronics"));
        n.GrantResearch(C.ResearchIndex("core:nickel_smelting"));
        n.GrantResearch(C.ResearchIndex("core:titanium_smelting"));

        // Only raw ore is placed in the hold; ports, belts, smelters and assemblers do the rest.
        n.GiveItems(0, Item("core:quartz"), 12);
        n.GiveItems(0, Item("core:nickel_ore"), 8);
        n.GiveItems(0, Item("core:titanium_ore"), 6);
        n.GiveItems(0, Item("core:carbon"), 4);

        // Circuit chain on layer y=1: quartz -> silicon, nickel ore -> ingot -> plate, both into the circuit assembler.
        int qPort = Put(n, s, "core:quartz_unloader", 16, 1, 2), siSmelter = Put(n, s, "core:smelter", 19, 1, 2, "core:smelt_silicon");
        int circuit = Put(n, s, "core:assembler", 22, 1, 2, "core:make_circuit");
        int nPort = Put(n, s, "core:nickel_ore_unloader", 16, 1, 5), niSmelter = Put(n, s, "core:smelter", 19, 1, 5, "core:smelt_nickel");
        int niPlate = Put(n, s, "core:assembler", 22, 1, 5, "core:nickel_plate");
        int cDepot = Put(n, s, "core:depot", 22, 1, 0);
        Link(n, s, At(qPort, 16, 1, 2), At(siSmelter, 19, 1, 2));
        Link(n, s, At(siSmelter, 19, 1, 2), At(circuit, 22, 1, 2));
        Link(n, s, At(nPort, 16, 1, 5), At(niSmelter, 19, 1, 5));
        Link(n, s, At(niSmelter, 19, 1, 5), At(niPlate, 22, 1, 5));
        Link(n, s, At(niPlate, 22, 1, 5), At(circuit, 22, 1, 2));
        Link(n, s, At(circuit, 22, 1, 2), At(cDepot, 22, 1, 0));

        // Alloy chain on layer y=2: titanium ore -> ingot -> plate, plus carbon, into the alloy assembler.
        int tPort = Put(n, s, "core:titanium_ore_unloader", 16, 2, 2), tiSmelter = Put(n, s, "core:smelter", 19, 2, 2, "core:smelt_titanium");
        int tiPlate = Put(n, s, "core:assembler", 22, 2, 2, "core:titanium_plate");
        int alloy = Put(n, s, "core:assembler", 25, 2, 2, "core:make_alloy");
        int cPort = Put(n, s, "core:carbon_unloader", 25, 2, 5);
        int aDepot = Put(n, s, "core:depot", 25, 2, 0);
        Link(n, s, At(tPort, 16, 2, 2), At(tiSmelter, 19, 2, 2));
        Link(n, s, At(tiSmelter, 19, 2, 2), At(tiPlate, 22, 2, 2));
        Link(n, s, At(tiPlate, 22, 2, 2), At(alloy, 25, 2, 2));
        Link(n, s, At(cPort, 25, 2, 5), At(alloy, 25, 2, 2));
        Link(n, s, At(alloy, 25, 2, 2), At(aDepot, 25, 2, 0));

        w.Step(9000);
        Assert.Equal(4, n.HoldCount(0, Item("core:circuit"))); // 8 nickel ore -> 4 plates; 12 quartz -> 6 silicon
        Assert.Equal(3, n.HoldCount(0, Item("core:titanium_alloy"))); // 6 titanium ore -> 3 plates; 4 carbon
        Assert.Equal(0, n.HoldCount(0, Item("core:quartz")));
        n.ValidateInvariants(); // the fixture bypasses the world's cell index, so only the network is validated
    }

    // ---- fuel cells and the reactors -----------------------------------------------------------------

    private static SimWorld Sandbox(ContentDb? content = null) =>
        SimWorld.CreateGalaxy(content ?? C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);

    [Fact]
    public void A_fuel_cell_burns_for_six_intervals_and_is_preferred_over_hydrogen()
    {
        var w = Sandbox();
        var n = w.Network;
        int s = PowerTests.Ship(w, hydrogen: 5);
        n.GiveItems(0, Item("core:fuel_cell"), 1);
        int interval = C.Structures!.BurnIntervalTicks(ModuleKind.Reactor);

        w.Step();
        Assert.Equal(0, n.HoldCount(0, Item("core:fuel_cell")));
        Assert.Equal(5, n.HoldCount(0, Item("core:hydrogen"))); // the cell went first
        var p = n.PowerStatus(s);
        Assert.True(p.Burning);
        Assert.Equal(100, p.Production);
        Assert.Equal(6 * interval - 1, p.BurnRemainingTicks);

        w.Step(6 * interval - 1); // the whole cell is spent, the hydrogen not yet touched
        Assert.Equal(5, n.HoldCount(0, Item("core:hydrogen")));
        w.Step();
        Assert.Equal(4, n.HoldCount(0, Item("core:hydrogen"))); // only now the reactor takes hydrogen
        Assert.Equal(interval - 1, n.PowerStatus(s).BurnRemainingTicks);
        w.ValidateInvariants();
    }

    [Fact]
    public void A_cell_lasts_exactly_as_long_as_six_hydrogen()
    {
        int FullPowerTicks(bool cell)
        {
            var w = Sandbox();
            int s = PowerTests.Ship(w, hydrogen: cell ? 0 : 6);
            if (cell) w.Network.GiveItems(0, Item("core:fuel_cell"), 1);
            int full = 0;
            for (int i = 0; i < 8_000; i++) { w.Step(); if (w.Network.PowerStatus(s).Production == 100) full++; }
            return full;
        }
        int interval = C.Structures!.BurnIntervalTicks(ModuleKind.Reactor);
        Assert.Equal(6 * interval, FullPowerTicks(false));
        Assert.Equal(6 * interval, FullPowerTicks(true));
    }

    [Fact]
    public void Fuel_cells_and_the_mark_2_reactor_survive_a_deep_copy()
    {
        var w = Sandbox();
        int s = PowerTests.Ship(w, hydrogen: 3);
        w.Network.AddStructureModuleUnchecked(s, 9, 0, 0, ModuleKind.ReactorMk2);
        w.Network.GiveItems(0, Item("core:fuel_cell"), 2);
        w.Step(1500);
        var copy = w.Network.Clone(); // a deep copy must carry the burn state of cells and the mark 2 reactor
        Assert.Equal(w.Network.Checksum(), copy.Checksum());
        w.Network.Step(3000); copy.Step(3000);
        Assert.Equal(w.Network.Checksum(), copy.Checksum());
        copy.ValidateInvariants();
    }

    [Fact]
    public void The_mark_2_reactor_makes_250_power_and_burns_like_a_reactor()
    {
        var w = Sandbox();
        var n = w.Network;
        n.EnsurePlayer(0);
        int s = n.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        n.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        n.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.ReactorMk2);
        n.GiveItems(0, Item("core:hydrogen"), 2);
        w.Step();
        Assert.Equal(250, n.PowerStatus(s).Production);
        Assert.Equal(1, n.HoldCount(0, Item("core:hydrogen")));
        Assert.Equal(899, n.PowerStatus(s).BurnRemainingTicks);
        Assert.False(n.StationHasNoReactor(s));
        w.Step(2000); // both hydrogen burned: only the emergency output is left
        Assert.Equal(15, n.PowerStatus(s).Production);
        w.ValidateInvariants();
    }

    [Fact]
    public void A_mark_2_reactor_and_a_reactor_together_add_up()
    {
        var w = Sandbox();
        int s = PowerTests.Ship(w, hydrogen: 4);
        w.Network.AddStructureModuleUnchecked(s, 9, 0, 0, ModuleKind.ReactorMk2);
        w.Step();
        Assert.Equal(350, w.Network.PowerStatus(s).Production);
        Assert.Equal(2, 4 - w.Network.HoldCount(0, Item("core:hydrogen")));
    }

    private static Reject Remove(SimWorld w, int s, int mx, int my)
    {
        w.Submit(0, new RemoveModuleIn(s, mx, my, 0));
        w.Step();
        return w.LastResults.Single().Reject;
    }

    [Fact]
    public void The_mark_2_reactor_counts_as_a_reactor_for_the_last_essential_module_rule()
    {
        var w = Sandbox();
        int s = PowerTests.Ship(w, hydrogen: 5); // cockpit 0, engine 1, workshop 2, cargo 3, reactor 4 along x
        w.Network.AddStructureModuleUnchecked(s, 3, 1, 0, ModuleKind.ReactorMk2); // above the cargo module
        Assert.Equal(Reject.None, Remove(w, s, 3, 1)); // the reactor stays, so the mark 2 may go
        w.Network.AddStructureModuleUnchecked(s, 3, 1, 0, ModuleKind.ReactorMk2);
        Assert.Equal(Reject.None, Remove(w, s, 4, 0)); // and the other way round
        Assert.Equal(Reject.LastEssentialModule, Remove(w, s, 3, 1)); // the last reactor of either kind stays
        w.ValidateInvariants();
    }
}
