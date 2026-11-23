using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The structure campaign must be completable: every research cost can reach a lab, ports are free, the ship cannot be stripped of
/// its essential modules and a ship with no hydrogen still crawls. Content analysis plus scripted loops (ADR 0022).
/// </summary>
public class StructureEconomyTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261121UL;

    // ---- content analysis --------------------------------------------------------------------------

    private static bool HandMinable(int item) => C.Archetypes.Any(a => a.Deposits.Any(d => d.Item == item));

    private static bool HasPort(int item) => C.Machines.Any(m => m.Role == MachineRole.Unloader && m.Unloader!.Item == item);

    /// <summary>True if a machine of this type can stand in a workshop or core module (so a ship or station can craft with it).</summary>
    private static bool PlaceableInModule(MachineDef m) =>
        !m.SandboxOnly && m.Role != MachineRole.Ship
        && (C.Structures!.AllowsRole(ModuleKind.Workshop, m.Role) || C.Structures.AllowsRole(ModuleKind.Core, m.Role));

    /// <summary>
    /// Can <paramref name="item"/> arrive on a belt in a structure world? Either a raw item that can be hand-mined and has a port, or
    /// the output of a recipe of a placeable machine whose inputs are deliverable too. <paramref name="recipeOk"/> says which recipes
    /// count (all of them for the structural check, the unlocked ones for the research graph).
    /// </summary>
    private static bool Deliverable(int item, Func<RecipeDef, bool> recipeOk, HashSet<int> path)
    {
        if (HandMinable(item) && HasPort(item)) return true;
        if (!path.Add(item)) return false;
        try
        {
            foreach (var r in C.Recipes)
            {
                if (!r.Outputs.Any(o => o.Item == item) || !recipeOk(r) || !PlaceableInModule(C.Machines[r.Machine])) continue;
                if (r.Inputs.All(i => Deliverable(i.Item, recipeOk, path))) return true;
            }
            return false;
        }
        finally { path.Remove(item); }
    }

    [Fact]
    public void Every_research_cost_item_is_deliverable_in_structure_worlds()
    {
        var problems = new List<string>();
        foreach (var node in C.Research)
            foreach (var cost in node.Cost)
                if (!Deliverable(cost.Item, _ => true, new HashSet<int>()))
                    problems.Add($"research '{node.Id}' costs '{C.Items[cost.Item].Id}', which no port or placeable crafter can put on a belt");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_science_item_has_a_chain()
    {
        // Each science item is made by its own dedicated machine from items that can be delivered, and no recipe of the basic
        // crafters makes it (the lab is the only place that eats it).
        foreach (var (science, machine) in new[]
        {
            ("core:science_engineering", "core:engineering_bench"), ("core:science_biology", "core:greenhouse"), ("core:science_physics", "core:physics_lab"),
        })
        {
            int item = C.ItemIndex(science);
            Assert.True(Deliverable(item, _ => true, new HashSet<int>()), $"{science} cannot be made from deliverable items");
            var makers = C.Recipes.Where(r => r.Outputs.Any(o => o.Item == item)).ToList();
            Assert.Single(makers);
            Assert.Equal(machine, C.Machines[makers[0].Machine].Id);
            Assert.False(HandMinable(item) || HasPort(item), $"{science} must not come from a port");
            Assert.Contains(C.Research, n => n.Cost.Any(c => c.Item == item)); // and something eats it
        }
        Assert.True(Deliverable(C.ItemIndex("core:biomass"), _ => true, new HashSet<int>()));
    }

    [Fact]
    public void Every_raw_item_in_a_deposit_has_a_port()
    {
        // Hand-mined ores are the only way into the factory, so each needs an unloader that carries it.
        foreach (var item in Enumerable.Range(0, C.Items.Count).Where(i => HandMinable(i)))
            Assert.True(HasPort(item), $"'{C.Items[item].Id}' can be mined by hand but no port carries it");
    }

    [Fact]
    public void Research_graph_is_completable_in_structure_worlds()
    {
        // Fixed point: a node is doable when its prerequisites are done and every cost item can reach a lab with the recipes unlocked
        // so far. Ports and crafters are free in structure worlds, only recipes are gated by research.
        var done = new HashSet<int>();
        bool Unlocked(RecipeDef r) => !C.RecipeGated[r.Index] || C.Research.Any(n => done.Contains(n.Index) && n.UnlockRecipes.Contains(r.Index));
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (var node in C.Research)
            {
                if (done.Contains(node.Index) || !node.Requires.All(done.Contains)) continue;
                if (!node.Cost.All(c => Deliverable(c.Item, Unlocked, new HashSet<int>()))) continue;
                done.Add(node.Index);
                progress = true;
            }
        }
        var stuck = C.Research.Where(n => !done.Contains(n.Index)).Select(n => n.Id).ToList();
        Assert.True(stuck.Count == 0, "research that can never be completed in a structure world: " + string.Join(", ", stuck));
    }

    [Fact]
    public void Every_item_has_a_way_into_the_factory_or_is_made_in_it()
    {
        // The reactor fuel and the plates are crafted; ammo too: nothing in the content is a dead end.
        foreach (var name in new[] { "core:hydrogen", "core:iron_plate", "core:ammo", "core:titanium_plate", "core:nickel_plate" })
            Assert.True(Deliverable(C.ItemIndex(name), _ => true, new HashSet<int>()), name);
    }

    [Fact]
    public void Ports_are_free_in_structure_worlds_and_gated_elsewhere()
    {
        var structure = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, structures: true);
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed);
        foreach (var m in C.Machines.Where(m => m.Role == MachineRole.Unloader))
        {
            Assert.False(structure.Network.IsMachineLocked(m.Index), $"{m.Id} must be free in a structure world");
            if (C.MachineGated[m.Index]) Assert.True(legacy.Network.IsMachineLocked(m.Index), $"{m.Id} stays gated in the legacy campaign");
        }
        Assert.False(structure.Network.IsMachineLocked(C.MachineIndex("core:quartz_unloader")));
        Assert.False(structure.Network.IsMachineLocked(C.MachineIndex("core:carbon_unloader")));
    }

    [Fact]
    public void New_ports_carry_quartz_and_carbon_like_the_ore_port()
    {
        var ore = C.Machines[C.MachineIndex("core:ore_unloader")];
        foreach (var (id, item) in new[] { ("core:quartz_unloader", "core:quartz"), ("core:carbon_unloader", "core:carbon") })
        {
            var m = C.Machines[C.MachineIndex(id)];
            Assert.Equal(MachineRole.Unloader, m.Role);
            Assert.Equal(C.ItemIndex(item), m.Unloader!.Item);
            Assert.Equal(ore.Cost, m.Cost);
            Assert.Equal((ore.Hp, ore.Signature, ore.PowerUse), (m.Hp, m.Signature, m.PowerUse));
            Assert.Equal(2, m.PowerUse);
            Assert.Equal((ore.Unloader!.IntervalTicks, ore.Unloader.Reserve), (m.Unloader.IntervalTicks, m.Unloader.Reserve));
        }
        foreach (var key in new[] { "machine.core:quartz_unloader", "machine.core:carbon_unloader", "toast.reject.lastEssentialModule" })
        {
            Assert.True(Starfoundry.Ui.Loc.Has(key), key);
            var (es, en) = Starfoundry.Ui.Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
        Assert.Equal("toast.reject.lastEssentialModule", Starfoundry.Ui.RejectTexts.Key(Reject.LastEssentialModule));
    }

    [Fact]
    public void Defence_and_reactor_modules_need_no_plates_from_the_late_chain()
    {
        var s = C.Structures!;
        int Cost(ModuleKind k, string item) => s.ModuleCost(k).Where(c => c.Item == C.ItemIndex(item)).Sum(c => c.Count);
        foreach (var item in new[] { "core:titanium_plate", "core:nickel_plate", "core:quartz" })
        {
            Assert.Equal(0, Cost(ModuleKind.Defence, item));
            Assert.Equal(0, Cost(ModuleKind.Reactor, item));
        }
        Assert.Equal(16, Cost(ModuleKind.Defence, "core:iron_plate"));
        Assert.Equal(16, Cost(ModuleKind.Defence, "core:iron_ingot"));
        Assert.Equal(16, Cost(ModuleKind.Reactor, "core:iron_plate"));
        Assert.Equal(8, Cost(ModuleKind.Reactor, "core:carbon"));
        Assert.Equal(0, Cost(ModuleKind.Engine, "core:titanium_plate")); // see StructureRecoveryTests: the engine is rebuilt at home
        Assert.Equal(20, Cost(ModuleKind.Engine, "core:iron_plate"));
        Assert.Equal(4, Cost(ModuleKind.Engine, "core:carbon"));
        Assert.Equal(15, s.EmergencyPower(ModuleKind.Reactor));
        Assert.Equal(0, s.EmergencyPower(ModuleKind.Engine));
    }

    // ---- the quartz loop, for real ---------------------------------------------------------------

    /// <summary>The core content without a starter ship, with advanced plating costing only quartz so the test follows one loop.</summary>
    private static ContentDb QuartzOnly()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root.Remove("structure_templates");
        foreach (var n in root["research"]!.AsArray())
            if (n!["id"]!.GetValue<string>() == "core:advanced_plating")
                n["cost"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"item":"core:quartz","count":30}]""");
        return ContentDb.FromJson(root.ToJsonString());
    }

    [Fact]
    public void Hand_mined_quartz_pays_for_advanced_plating_through_a_quartz_port_in_a_campaign_ship()
    {
        var c = QuartzOnly();
        int quartz = c.ItemIndex("core:quartz"), ingot = c.ItemIndex("core:iron_ingot"), plate = c.ItemIndex("core:iron_plate");
        var w = SimWorld.CreateGalaxy(c, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        var n = w.Network;
        int ship = PowerTests.Ship(w, hydrogen: 10); // workshop in module 2, cells x 16..23
        n.GrantResearch(c.ResearchIndex("core:titanium_smelting"));
        n.GrantResearch(c.ResearchIndex("core:nickel_smelting"));
        int node = c.ResearchIndex("core:advanced_plating");
        Assert.True(n.CanResearch(node));
        Assert.Equal(new[] { new ItemStack(quartz, 30) }, n.ResearchCost(node));

        // The construction budget stands for what the factory delivered; the quartz is the hand-mined item placed in the hold.
        n.GiveItems(0, ingot, 40);
        n.GiveItems(0, plate, 10);
        n.GiveItems(0, quartz, 30);

        CommandResult Do(Command cmd)
        {
            w.Submit(0, cmd);
            w.Step();
            var r = w.LastResults.Single();
            Assert.True(r.Ok, $"{cmd}: {r.Reject}");
            return r;
        }
        var portType = c.MachineIndex("core:quartz_unloader");
        Assert.False(n.IsMachineLocked(portType), "the quartz port needs no research in a structure world");
        int port = Do(new PlaceMachineIn(ship, 16, 1, 1, portType, -1)).CreatedMachines[0];
        int belt = Do(new PlaceSegmentIn(ship, 17, 1, 1, Dir.PosX, 2, BeltTier.Mk1)).Created[0];
        int lab = Do(new PlaceMachineIn(ship, 19, 1, 1, c.MachineIndex("core:research_lab"), -1)).CreatedMachines[0];
        Do(new ConnectFromMachine(port, belt));
        Do(new ConnectToMachine(belt, lab));
        Do(new SetResearch(node));

        for (int i = 0; i < 6000 && !n.IsResearched(node); i++) w.Step();
        Assert.True(n.IsResearched(node), "the lab never got the quartz: hold -> port -> belt -> lab is broken");
        Assert.Equal(0, n.HoldCount(0, quartz));
        Assert.True(n.ResearchItemsUsed >= 30);
        w.ValidateInvariants();
    }

    // ---- last essential module ------------------------------------------------------------------

    private static Reject Remove(SimWorld w, int s, int mx)
    {
        w.Submit(0, new RemoveModuleIn(s, mx, 0, 0));
        w.Step();
        return w.LastResults.Single().Reject;
    }

    [Fact]
    public void The_last_cockpit_engine_reactor_or_cargo_of_the_ship_cannot_be_removed()
    {
        var w = SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        var n = w.Network;
        int s = PowerTests.Ship(w, hydrogen: 5); // cockpit 0, engine 1, workshop 2, cargo 3, reactor 4
        // Extra end modules of each kind next to the first reactor, to remove the spare ones first.
        n.AddStructureModuleUnchecked(s, 5, 0, 0, ModuleKind.Reactor);
        n.AddStructureModuleUnchecked(s, 6, 0, 0, ModuleKind.Engine);
        n.AddStructureModuleUnchecked(s, 7, 0, 0, ModuleKind.Cargo);
        n.AddStructureModuleUnchecked(s, 8, 0, 0, ModuleKind.Cockpit);

        Assert.Equal(Reject.None, Remove(w, s, 8)); // a second cockpit
        Assert.Equal(Reject.LastEssentialModule, Remove(w, s, 0)); // the last cockpit
        Assert.Equal(Reject.None, Remove(w, s, 7)); // a second cargo
        Assert.Equal(Reject.LastEssentialModule, Remove(w, s, 3));
        Assert.Equal(Reject.None, Remove(w, s, 6)); // a second engine
        Assert.Equal(Reject.LastEssentialModule, Remove(w, s, 1));
        Assert.Equal(Reject.None, Remove(w, s, 5)); // a second reactor
        Assert.Equal(Reject.LastEssentialModule, Remove(w, s, 4));
        Assert.Equal(Reject.ModuleOccupied, Remove(w, s, 2)); // the workshop joins the two halves of the ship: the old rule still holds
        w.ValidateInvariants();
    }

    [Fact]
    public void Stations_keep_the_old_removal_rules()
    {
        var w = SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        var n = w.Network;
        PowerTests.Ship(w, hydrogen: 5); // the player needs a ship (and its cockpit) in range
        int st = n.CreateStructure(0, StructureKind.Station, 0, 0, 0);
        n.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        n.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Engine);
        Assert.Equal(Reject.None, Remove(w, st, 1)); // the only engine of a station: no rule against it
        Assert.Equal(Reject.None, Remove(w, st, 0)); // the core when it is the last module
        w.ValidateInvariants();
    }
}
