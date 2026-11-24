using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The optional material chains of the shipped content (docs/prototypes/materials.md): carbon fibre and composite plates, coolant, and the
/// resonant crystal that only wrecks give, ground into focus lenses. Content analysis against <see cref="TestContent.CoreFloor"/>: every item
/// is obtainable and has a use, every new recipe is gated by research that can be done, texts and icons exist.
/// </summary>
public class MaterialsExpansionTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;
    private readonly ITestOutputHelper _out;
    public MaterialsExpansionTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] NewItems = { "core:carbon_fiber", "core:composite_plate", "core:coolant", "core:resonant_crystal", "core:focus_lens" };

    private static bool HandMinable(int item) => C.Archetypes.Any(a => a.Deposits.Any(d => d.Item == item));
    private static bool HasPort(int item) => C.Machines.Any(m => m.Role == MachineRole.Unloader && m.Unloader!.Item == item);
    private static bool InWrecks(int item) => C.World!.Kinds.Any(k => k.Behaviour == ObstacleBehaviour.Wreck && k.Loot.Any(l => l.Item == item));

    private static bool PlaceableInModule(MachineDef m) =>
        !m.SandboxOnly && m.Role != MachineRole.Ship
        && (C.Structures!.AllowsRole(ModuleKind.Workshop, m.Role) || C.Structures.AllowsRole(ModuleKind.Core, m.Role));

    /// <summary>Can the item reach a belt? A raw item the pilot can bring (mined or salvaged) with a port, or the output of a placeable crafter whose inputs can.</summary>
    private static bool Deliverable(int item, HashSet<int>? path = null)
    {
        if ((HandMinable(item) || InWrecks(item)) && HasPort(item)) return true;
        path ??= new HashSet<int>();
        if (!path.Add(item)) return false;
        try
        {
            foreach (var r in C.Recipes)
                if (r.Outputs.Any(o => o.Item == item) && PlaceableInModule(C.Machines[r.Machine]) && r.Inputs.All(i => Deliverable(i.Item, path))) return true;
            return false;
        }
        finally { path.Remove(item); }
    }

    private static (string Id, int Count) Ingredient(string recipe, int n)
    {
        var s = C.Recipes[C.RecipeIndex(recipe)].Inputs[n];
        return (C.Items[s.Item].Id, s.Count);
    }

    [Fact]
    public void The_new_items_are_appended_after_the_older_ones_and_have_their_chains()
    {
        int last = C.ItemIndex("core:biomass");
        for (int i = 0; i < NewItems.Length; i++) Assert.Equal(last + 1 + i, C.ItemIndex(NewItems[i]));
        Assert.Equal(("core:carbon", 3), Ingredient("core:make_carbon_fiber", 0));
        Assert.Equal(("core:nickel_ingot", 1), Ingredient("core:make_carbon_fiber", 1));
        Assert.Equal("core:assembler", C.Machines[C.Recipes[C.RecipeIndex("core:make_composite_plate")].Machine].Id);
        Assert.Equal("core:hydrogen_cracker", C.Machines[C.Recipes[C.RecipeIndex("core:make_coolant")].Machine].Id);
        Assert.Equal(("core:resonant_crystal", 1), Ingredient("core:make_focus_lens", 0));
    }

    [Fact]
    public void Every_new_recipe_is_gated_by_a_research_node_that_unlocks_it()
    {
        foreach (var id in new[] { "core:make_carbon_fiber", "core:make_composite_plate", "core:make_coolant", "core:make_focus_lens" })
        {
            int r = C.RecipeIndex(id);
            Assert.True(C.RecipeGated[r], id);
            Assert.Single(C.Research.Where(n => n.UnlockRecipes.Contains(r)));
        }
        Assert.Contains(C.MachineIndex("core:crystal_unloader"), C.Research[C.ResearchIndex("core:resonance")].UnlockMachines);
    }

    [Fact]
    public void The_resonant_crystal_comes_only_from_wrecks_and_has_a_port()
    {
        int crystal = C.ItemIndex("core:resonant_crystal");
        Assert.False(HandMinable(crystal));
        Assert.True(InWrecks(crystal));
        Assert.True(HasPort(crystal));
        Assert.DoesNotContain(C.Recipes, r => r.Outputs.Any(o => o.Item == crystal));
        // Only the wreck kind carries loot.
        Assert.All(C.World!.Kinds.Where(k => k.Behaviour != ObstacleBehaviour.Wreck), k => Assert.Empty(k.Loot));
    }

    [Fact]
    public void Wrecks_with_crystals_exist_near_home_on_the_main_seeds()
    {
        int crystal = C.ItemIndex("core:resonant_crystal");
        foreach (ulong seed in new[] { 20261121UL, 20261120UL, 777UL })
        {
            var g = Galaxy.Generate(C, seed);
            var near = new HashSet<int> { 0 };
            foreach (int n in g.Systems[0].Lanes) near.Add(n);
            foreach (int a in near.ToList()) foreach (int n in g.Systems[a].Lanes) near.Add(n);
            var wrecks = g.Obstacles.Where(o => near.Contains(o.System) && C.World!.Kinds[o.Kind].Behaviour == ObstacleBehaviour.Wreck).ToList();
            int withCrystal = wrecks.Count(o => o.LootItem == crystal);
            _out.WriteLine($"seed {seed}: {withCrystal} of {wrecks.Count} wrecks within two jumps hold crystals");
            Assert.True(wrecks.Count > 0);
            Assert.True(withCrystal >= 1, $"no crystal wreck within two jumps of home on seed {seed}");
        }
    }

    [Fact]
    public void Mk2_hardpoints_use_the_new_materials_and_nothing_early_does()
    {
        var laser = C.Structures!.ModuleCost(ModuleKind.MiningLaserMk2).Select(s => C.Items[s.Item].Id).ToList();
        var gun = C.Structures.ModuleCost(ModuleKind.GunMk2).Select(s => C.Items[s.Item].Id).ToList();
        Assert.Contains("core:focus_lens", laser);
        Assert.Contains("core:coolant", laser);
        Assert.Contains("core:composite_plate", gun);
        Assert.Contains("core:coolant", gun);
        foreach (var kind in new[] { ModuleKind.MiningLaser, ModuleKind.Gun, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.Core, ModuleKind.Workshop, ModuleKind.Defence, ModuleKind.Cargo, ModuleKind.Shipyard, ModuleKind.Dock })
            Assert.DoesNotContain(C.Structures.ModuleCost(kind), s => NewItems.Contains(C.Items[s.Item].Id));
        Assert.DoesNotContain(C.Research, n => n.Cost.Any(c => NewItems.Contains(C.Items[c.Item].Id)));
        foreach (var m in C.Machines) Assert.DoesNotContain(m.Cost, s => NewItems.Contains(C.Items[s.Item].Id));
    }

    [Fact]
    public void Every_item_is_obtainable_and_has_a_use()
    {
        var used = new HashSet<int>();
        foreach (var r in C.Recipes) foreach (var i in r.Inputs) used.Add(i.Item);
        foreach (var n in C.Research) foreach (var c in n.Cost) used.Add(c.Item);
        foreach (var m in C.Machines) foreach (var c in m.Cost) used.Add(c.Item);
        foreach (var kind in Enum.GetValues<ModuleKind>()) if (C.Structures!.IsDefined(kind)) foreach (var c in C.Structures.ModuleCost(kind)) used.Add(c.Item);
        foreach (var b in C.Belts) foreach (var c in b.CostPerCell) used.Add(c.Item);
        foreach (var c in C.JumpCost) used.Add(c.Item);
        foreach (var m in C.Machines) { if (m.Turret is { } t) used.Add(t.AmmoItem); if (m.Repairer is { } rp) used.Add(rp.Item); }
        used.Add(C.ItemIndex("core:hydrogen")); used.Add(C.ItemIndex("core:fuel_cell")); // reactor fuel
        var problems = new List<string>();
        for (int i = 0; i < C.Items.Count; i++)
        {
            string id = C.Items[i].Id;
            if (!HandMinable(i) && !InWrecks(i) && !C.Recipes.Any(r => r.Outputs.Any(o => o.Item == i))) problems.Add($"{id} is neither found nor made");
            if (!used.Contains(i)) problems.Add($"{id} is never used");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_recipe_can_run_in_a_placeable_machine_with_deliverable_inputs()
    {
        var problems = new List<string>();
        foreach (var r in C.Recipes)
        {
            var m = C.Machines[r.Machine];
            if (m.Role is MachineRole.Ship or MachineRole.Drill || m.SandboxOnly) continue; // the workbench, the legacy drills and the sandbox extractor have their own rules
            if (!PlaceableInModule(m)) problems.Add($"{r.Id}: {m.Id} cannot stand in a module");
            foreach (var i in r.Inputs)
                if (!Deliverable(i.Item)) problems.Add($"{r.Id}: input {C.Items[i.Item].Id} cannot reach a belt");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_research_graph_of_the_shipped_content_is_completable()
    {
        var done = new HashSet<int>();
        bool Unlocked(RecipeDef r) => !C.RecipeGated[r.Index] || C.Research.Any(n => done.Contains(n.Index) && n.UnlockRecipes.Contains(r.Index));
        bool Can(int item, HashSet<int> path)
        {
            if ((HandMinable(item) || InWrecks(item)) && HasPort(item)) return true;
            if (!path.Add(item)) return false;
            try
            {
                return C.Recipes.Any(r => r.Outputs.Any(o => o.Item == item) && Unlocked(r) && PlaceableInModule(C.Machines[r.Machine]) && r.Inputs.All(i => Can(i.Item, path)));
            }
            finally { path.Remove(item); }
        }
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (var n in C.Research)
                if (!done.Contains(n.Index) && n.Requires.All(done.Contains) && n.Cost.All(c => Can(c.Item, new HashSet<int>()))) { done.Add(n.Index); progress = true; }
        }
        var stuck = C.Research.Where(n => !done.Contains(n.Index)).Select(n => n.Id).ToList();
        Assert.True(stuck.Count == 0, "research that can never be completed: " + string.Join(", ", stuck));
    }

    [Fact]
    public void Every_item_machine_recipe_and_research_node_has_an_icon_and_texts_in_both_languages()
    {
        foreach (var i in C.Items) { Assert.True(IconCatalog.IsRegistered(IconCatalog.ForItem(i.Id)), i.Id); Assert.True(Loc.Has("item." + i.Id), i.Id); }
        foreach (var m in C.Machines) { Assert.True(IconCatalog.IsRegistered(IconCatalog.ForMachine(m.Id)), m.Id); Assert.True(Loc.Has("machine." + m.Id), m.Id); }
        foreach (var r in C.Recipes.Where(r => C.Machines[r.Machine].Role != MachineRole.Ship && !C.Machines[r.Machine].SandboxOnly)) Assert.True(Loc.Has("recipe." + r.Id), r.Id);
        foreach (var n in C.Research) { Assert.True(IconCatalog.IsRegistered(IconCatalog.ForResearch(n.Id)), n.Id); Assert.True(Loc.Has("research." + n.Id), n.Id); }
        foreach (var id in new[] { "core:composites", "core:cryogenics", "core:resonance" }) Assert.True(Loc.Has("research." + id + ".desc"), id);
        Assert.True(Loc.Has("build.desc.core:crystal_unloader"));
        foreach (var (key, (es, en)) in Loc.All.Where(p => p.Key.Contains("carbon_fiber") || p.Key.Contains("composite") || p.Key.Contains("coolant") || p.Key.Contains("resonan") || p.Key.Contains("focus_lens") || p.Key.Contains("crystal_unloader")))
            Assert.NotEqual(es, en);
    }

    [Fact]
    public void The_resonance_research_unlocks_the_lens_recipe_in_a_campaign_world()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261121UL, threats: false, stationRules: false, structures: true);
        Assert.True(w.Network.IsRecipeLocked(C.RecipeIndex("core:make_focus_lens")));
        w.Network.GrantResearch(C.ResearchIndex("core:resonance"));
        Assert.False(w.Network.IsRecipeLocked(C.RecipeIndex("core:make_focus_lens")));
        w.ValidateInvariants();
    }
}
