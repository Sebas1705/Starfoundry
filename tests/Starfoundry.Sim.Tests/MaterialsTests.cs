using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The new materials: ice, titanium, nickel and quartz in the galaxy, their recipes, research and uses.</summary>
public class MaterialsTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static ulong[] Seeds => Enumerable.Range(0, 120).Select(i => (ulong)i * 7919UL + 20261120UL).ToArray();

    private static int[] Depths(Galaxy g)
    {
        var depth = Enumerable.Repeat(-1, g.Systems.Count).ToArray();
        depth[0] = 0;
        var q = new Queue<int>();
        q.Enqueue(0);
        while (q.Count > 0)
        {
            int u = q.Dequeue();
            foreach (int v in g.Systems[u].Lanes) if (depth[v] < 0) { depth[v] = depth[u] + 1; q.Enqueue(v); }
        }
        return depth;
    }

    private static IEnumerable<GeneratedDeposit> Of(Galaxy g, string item) =>
        g.Deposits.Where(d => d.Item == C.ItemIndex(item));

    // ---- content ----------------------------------------------------------------------------------

    [Fact]
    public void NewItemsAreAppendedAfterTheExistingOnes()
    {
        Assert.Equal(new[] { "core:iron_ore", "core:iron_ingot", "core:iron_plate", "core:carbon", "core:ammo" },
            C.Items.Take(5).Select(i => i.Id).ToArray());
        foreach (var id in new[] { "core:ice", "core:titanium_ore", "core:titanium_ingot", "core:nickel_ore", "core:nickel_ingot", "core:quartz", "core:titanium_plate", "core:nickel_plate" })
            Assert.True(C.ItemIndex(id) >= 5, id);
        Assert.True(C.Items.Count <= ContentDb.MaxItems);
    }

    [Theory]
    [InlineData("core:smelt_titanium", "core:smelter", "core:titanium_ore", "core:titanium_ingot")]
    [InlineData("core:smelt_nickel", "core:smelter", "core:nickel_ore", "core:nickel_ingot")]
    [InlineData("core:titanium_plate", "core:assembler", "core:titanium_ingot", "core:titanium_plate")]
    [InlineData("core:nickel_plate", "core:assembler", "core:nickel_ingot", "core:nickel_plate")]
    public void NewRecipesExistAndAreResearchLocked(string recipe, string machine, string input, string output)
    {
        int r = C.RecipeIndex(recipe);
        var def = C.Recipes[r];
        Assert.Equal(C.MachineIndex(machine), def.Machine);
        Assert.Equal(C.ItemIndex(input), Assert.Single(def.Inputs).Item);
        Assert.Equal(C.ItemIndex(output), Assert.Single(def.Outputs).Item);
        Assert.True(C.RecipeGated[r]);

        var campaign = new SimWorld(C, GameMode.Campaign);
        Assert.True(campaign.Network.IsRecipeLocked(r));
        var sandbox = new SimWorld(C, GameMode.Sandbox);
        Assert.False(sandbox.Network.IsRecipeLocked(r));
    }

    [Theory]
    [InlineData("core:mine_ice")]
    [InlineData("core:mine_titanium")]
    [InlineData("core:mine_nickel")]
    [InlineData("core:mine_quartz")]
    public void ExtractorRecipesExistForEveryNewOre(string recipe)
    {
        var def = C.Recipes[C.RecipeIndex(recipe)];
        Assert.Equal(C.MachineIndex("core:extractor"), def.Machine);
        Assert.Empty(def.Inputs);
    }

    [Fact]
    public void TitaniumIsSmeltedSlowerThanIron()
    {
        Assert.True(C.Recipes[C.RecipeIndex("core:smelt_titanium")].TimeTicks > C.Recipes[C.RecipeIndex("core:smelt_iron")].TimeTicks);
    }

    [Fact]
    public void ResearchGatesTheNewRecipesAndPorts()
    {
        var ti = C.ResearchIndex("core:titanium_smelting");
        var ni = C.ResearchIndex("core:nickel_smelting");
        var plating = C.ResearchIndex("core:advanced_plating");
        Assert.Contains(C.RecipeIndex("core:smelt_titanium"), C.Research[ti].UnlockRecipes);
        Assert.Contains(C.RecipeIndex("core:smelt_nickel"), C.Research[ni].UnlockRecipes);
        Assert.Contains(C.RecipeIndex("core:titanium_plate"), C.Research[plating].UnlockRecipes);
        Assert.Contains(C.RecipeIndex("core:nickel_plate"), C.Research[plating].UnlockRecipes);
        Assert.Contains(C.MachineIndex("core:titanium_ore_unloader"), C.Research[ti].UnlockMachines);
        Assert.Contains(C.MachineIndex("core:nickel_ore_unloader"), C.Research[ni].UnlockMachines);
        Assert.Contains(ti, C.Research[plating].Requires);
        Assert.Contains(ni, C.Research[plating].Requires);

        // Quartz and the new ingots are spent in the lab.
        var cost = C.Research[plating].Cost.Select(s => s.Item).ToList();
        Assert.Contains(C.ItemIndex("core:quartz"), cost);
        Assert.Contains(C.ItemIndex("core:titanium_ingot"), cost);
        Assert.Contains(C.ItemIndex("core:nickel_ingot"), cost);
        Assert.Contains(C.ItemIndex("core:titanium_ore"), C.Research[ti].Cost.Select(s => s.Item));
        Assert.Contains(C.ItemIndex("core:nickel_ore"), C.Research[ni].Cost.Select(s => s.Item));
    }

    [Fact]
    public void FinishingTheResearchUnlocksTheRecipes()
    {
        var w = new SimWorld(C, GameMode.Campaign);
        int r = C.RecipeIndex("core:smelt_titanium");
        Assert.True(w.Network.IsRecipeLocked(r));
        w.Network.GrantResearch(C.ResearchIndex("core:titanium_smelting"));
        Assert.False(w.Network.IsRecipeLocked(r));
        Assert.True(w.Network.IsRecipeLocked(C.RecipeIndex("core:smelt_nickel")));
        Assert.True(w.Network.IsRecipeLocked(C.RecipeIndex("core:titanium_plate")));
        w.Network.GrantResearch(C.ResearchIndex("core:advanced_plating"));
        Assert.False(w.Network.IsRecipeLocked(C.RecipeIndex("core:titanium_plate")));
        Assert.False(w.Network.IsRecipeLocked(C.RecipeIndex("core:nickel_plate")));
    }

    [Fact]
    public void ALockedRecipeCannotBeSetOnAMachineBeforeItsResearch()
    {
        var w = new SimWorld(C, GameMode.Campaign);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_ingot"), 100);
        w.Submit(0, new PlaceMachine(0, 0, 0, C.MachineIndex("core:smelter"), C.RecipeIndex("core:smelt_iron")));
        w.Step();
        int smelter = w.LastResults.Single().CreatedMachines.Single();
        w.Submit(0, new SetRecipe(smelter, C.RecipeIndex("core:smelt_titanium")));
        w.Step();
        Assert.Equal(Reject.Locked, w.LastResults.Single().Reject);
        w.Network.GrantResearch(C.ResearchIndex("core:titanium_smelting"));
        w.Submit(0, new SetRecipe(smelter, C.RecipeIndex("core:smelt_titanium")));
        w.Step();
        Assert.True(w.LastResults.Single().Ok);
    }

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    [Theory]
    [InlineData("core:titanium_ore_unloader", "core:smelt_titanium", "core:titanium_ore")]
    [InlineData("core:nickel_ore_unloader", "core:smelt_nickel", "core:nickel_ore")]
    public void APortAndSmelterTurnTheNewOreIntoIngots(string port, string recipe, string ore)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261121UL, threats: false, stationRules: true);
        w.Network.EnsurePlayer(0);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_ingot"), 5000);
        w.Network.GrantResearch(C.ResearchIndex(recipe == "core:smelt_titanium" ? "core:titanium_smelting" : "core:nickel_smelting"));
        Ok(w, new PlaceModule(0, 0, 0));
        int p = Ok(w, new PlaceMachine(0, 1, 1, C.MachineIndex(port), -1));
        int b1 = Ok(w, new PlaceSegment(1, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        int smelter = Ok(w, new PlaceMachine(4, 1, 1, C.MachineIndex("core:smelter"), C.RecipeIndex(recipe)));
        int b2 = Ok(w, new PlaceSegment(5, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        Ok(w, new ConnectFromMachine(p, b1));
        Ok(w, new ConnectToMachine(b1, smelter));
        Ok(w, new ConnectFromMachine(smelter, b2));
        Ok(w, new SetSink(b2));
        w.Network.GiveItems(0, C.ItemIndex(ore), 6);
        w.Step(30 * 120);
        w.ValidateInvariants();
        Assert.Equal(0, w.Network.HoldCount(0, C.ItemIndex(ore)));
        Assert.Equal(6, w.Network.PassedBy(b2));
    }

    [Fact]
    public void ModuleCostsUseTheNewPlatesButTheCoreAndStarterShipDoNot()
    {
        var s = C.Structures!;
        int Cost(ModuleKind k, string item) => s.Kinds[(int)k].Cost.Where(c => c.Item == C.ItemIndex(item)).Sum(c => c.Count);
        Assert.True(Cost(ModuleKind.Engine, "core:titanium_plate") > 0);
        Assert.True(Cost(ModuleKind.Defence, "core:titanium_plate") > 0);
        Assert.True(Cost(ModuleKind.Reactor, "core:nickel_plate") > 0);
        foreach (var item in new[] { "core:titanium_plate", "core:nickel_plate", "core:titanium_ingot", "core:nickel_ingot", "core:quartz", "core:ice" })
            Assert.Equal(0, Cost(ModuleKind.Core, item));
    }

    [Fact]
    public void TheStarterShipStillBuildsAtNoCost()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 1, structures: true, stationRules: true);
        Assert.Single(w.Network.Structures());
        // The only thing the hold starts with is the starter ship's hydrogen.
        Assert.Equal(20, w.Network.HoldTotal(0));
        Assert.Equal(20, w.Network.HoldCount(0, C.ItemIndex("core:hydrogen")));
        w.ValidateInvariants();
    }

    [Fact]
    public void TheNewOrePortsAreUnloadersForTheirItemAndBypassTheResearchWithStationRules()
    {
        foreach (var (machine, item) in new[] { ("core:titanium_ore_unloader", "core:titanium_ore"), ("core:nickel_ore_unloader", "core:nickel_ore") })
        {
            int m = C.MachineIndex(machine);
            Assert.Equal(MachineRole.Unloader, C.Machines[m].Role);
            Assert.Equal(C.ItemIndex(item), C.Machines[m].Unloader!.Item);
            var off = new SimWorld(C, GameMode.Campaign);
            Assert.True(off.Network.IsMachineLocked(m));
            var rules = new BeltNetwork(C, GameMode.Campaign, threats: false, stationRules: true);
            Assert.False(rules.IsMachineLocked(m));
        }
    }

    // ---- galaxy layout ----------------------------------------------------------------------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(99UL)]
    [InlineData(20261120UL)]
    public void HomeHasIronCarbonAndALittleThinIce(ulong seed)
    {
        var g = Galaxy.Generate(C, seed);
        var ds = g.DepositsOf(0).Select(i => g.Deposits[i]).ToList();
        Assert.Equal(3, ds.Count);
        Assert.Equal((C.ItemIndex("core:iron_ore"), 4000), (ds[0].Item, ds[0].Amount));
        Assert.Equal((C.ItemIndex("core:carbon"), 1500), (ds[1].Item, ds[1].Amount));
        Assert.Equal(C.ItemIndex("core:ice"), ds[2].Item);
        Assert.InRange(ds[2].Amount, 600, 800);
        Assert.InRange(ds[2].Density, 40, 70);
        Assert.Equal(100, ds[0].Density);
        Assert.Equal(100, ds[1].Density);
    }

    [Fact]
    public void HomeIceIsRevealedAtStartAndCanBeHandMined()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261120UL);
        int ice = Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == C.ItemIndex("core:ice"));
        Assert.True(w.Network.IsDepositRevealed(ice));
        Assert.Equal(0, w.Network.Galaxy!.Deposits[ice].System);
    }

    [Fact]
    public void GalaxyGenerationIsDeterministicAndKeepsTheSystemCount()
    {
        foreach (var seed in Seeds.Take(10))
        {
            var a = Galaxy.Generate(C, seed);
            var b = Galaxy.Generate(C, seed);
            Assert.Equal(C.Galaxy!.SystemCount, a.Systems.Count);
            Assert.Equal(a.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)), b.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)));
            Assert.Equal(a.Deposits, b.Deposits);
        }
    }

    [Fact]
    public void NickelAndIceAreOneOrTwoJumpsAwayAndTitaniumAndQuartzFarther()
    {
        foreach (var seed in Seeds)
        {
            var g = Galaxy.Generate(C, seed);
            var depth = Depths(g);
            int nickel = C.ItemIndex("core:nickel_ore"), ice = C.ItemIndex("core:ice");
            int titanium = C.ItemIndex("core:titanium_ore"), quartz = C.ItemIndex("core:quartz");

            var near = g.Deposits.Where(d => d.System != 0 && depth[d.System] <= 2).ToList();
            Assert.True(near.Any(d => d.Item == nickel), $"seed {seed}: no nickel within two jumps");
            Assert.True(near.Any(d => d.Item == ice), $"seed {seed}: no ice within two jumps");

            Assert.True(Of(g, "core:titanium_ore").Any(), $"seed {seed}: no titanium");
            Assert.True(Of(g, "core:quartz").Any(), $"seed {seed}: no quartz");
            // Nickel and the system ice only ever sit near; titanium and quartz only far.
            Assert.All(g.Deposits.Where(d => d.Item == nickel), d => Assert.InRange(depth[d.System], 1, 2));
            // Three jumps or more; a shallow galaxy (fewer than two systems that far) falls back to the farthest free ones, at
            // least two jumps away.
            int far = depth.Count(d => d >= 3) >= 2 ? 3 : 2;
            Assert.All(g.Deposits.Where(d => d.Item == titanium), d => Assert.True(depth[d.System] >= far, $"seed {seed}: titanium at depth {depth[d.System]}"));
            Assert.All(g.Deposits.Where(d => d.Item == quartz), d => Assert.True(depth[d.System] >= far, $"seed {seed}: quartz at depth {depth[d.System]}"));
            Assert.All(g.Deposits.Where(d => d.Item == ice && d.System != 0), d => Assert.InRange(depth[d.System], 1, 2));
        }
    }

    [Fact]
    public void NewVeinsHaveTheirDensityRanges()
    {
        foreach (var seed in Seeds.Take(40))
        {
            var g = Galaxy.Generate(C, seed);
            Assert.All(Of(g, "core:nickel_ore"), d => Assert.InRange(d.Density, 60, 140));
            Assert.All(Of(g, "core:titanium_ore"), d => Assert.InRange(d.Density, 50, 150));
            Assert.All(Of(g, "core:quartz"), d => Assert.InRange(d.Density, 40, 120));
            Assert.All(Of(g, "core:ice").Where(d => d.System != 0), d => Assert.InRange(d.Density, 80, 160));
            Assert.All(Of(g, "core:iron_ore"), d => Assert.Equal(100, d.Density));
        }
    }

    // ---- hand mining the new materials -------------------------------------------------------------

    [Theory]
    [InlineData("core:ice")]
    [InlineData("core:titanium_ore")]
    [InlineData("core:nickel_ore")]
    [InlineData("core:quartz")]
    public void EveryNewOreCanBeMinedByHandAndRespectsTheHold(string item)
    {
        int it = C.ItemIndex(item);
        var w = new SimWorld(C, GameMode.Campaign);
        w.Network.AddDeposit(it, 1000, density: 150);
        w.Submit(0, new StartMining(0));
        w.Step(300);
        Assert.True(w.Network.HoldCount(0, it) > 0);
        Assert.Equal(w.Network.HoldCount(0, it), w.Network.MinedByHand);
        w.Step(PlayerRules.HoldCapacity * 15);
        Assert.Equal(PlayerRules.HoldCapacity, w.Network.HoldTotal(0));
        w.ValidateInvariants();
    }
}
