using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class BuildCatalogTests
{
    private static readonly ContentDb C = TestContent.Core;

    private static IEnumerable<MachineDef> Placeable(bool noDrills) =>
        C.Machines.Where(m => m.Role != MachineRole.Ship && !m.SandboxOnly && !(noDrills && m.Role == MachineRole.Drill));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void EveryPlaceableMachineIsInExactlyOneCategory(bool structures, bool stationRules)
    {
        var entries = BuildCatalog.Entries(C, structures, stationRules);
        foreach (var m in Placeable(structures || stationRules))
        {
            var hits = entries.Where(e => e.Kind == BuildEntryKind.Machine && e.Machine == m.Index).ToList();
            Assert.True(hits.Count == 1, $"{m.Id} appears {hits.Count} times");
        }
        Assert.Equal(Placeable(structures || stationRules).Count(), entries.Count(e => e.Kind == BuildEntryKind.Machine));
        foreach (var tier in new[] { BeltTier.Mk1, BeltTier.Mk2, BeltTier.Mk3 })
            Assert.Single(entries, e => e.IsBelt && e.Tier == tier);
    }

    [Fact]
    public void ShipAndSandboxMachinesAreNeverOffered()
    {
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        Assert.DoesNotContain(entries, e => e.Id == "core:micro_refinery");
        Assert.DoesNotContain(entries, e => e.Id == "core:extractor");
        Assert.DoesNotContain(entries, e => e.Id == "core:drill"); // refused in structure worlds
        Assert.Contains(BuildCatalog.Entries(C, structures: false, stationRules: false), e => e.Id == "core:drill");
    }

    [Fact]
    public void EveryModuleKindAppearsInStructureWorlds()
    {
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        foreach (var kind in Enum.GetValues<ModuleKind>().Where(k => !ModuleKindIds.IsHardpoint(k))) // hardpoints are fitted in the ship designer (ADR 0032)
            Assert.True(entries.Any(e => e.Module == kind), $"{kind} is not in the build menu");
        Assert.All(entries.Where(e => e.Module is not null), e => Assert.Equal(BuildCategory.Structure, e.Category));
        // The core is placed by the found-a-station tool.
        Assert.Equal(ModuleKind.Core, Assert.Single(entries, e => e.Kind == BuildEntryKind.Station).Module);
    }

    [Fact]
    public void StationRulesOfferTheSingleModuleTool()
    {
        var entries = BuildCatalog.Entries(C, structures: false, stationRules: true);
        Assert.Single(entries, e => e.Kind == BuildEntryKind.LegacyModule);
        Assert.DoesNotContain(entries, e => e.Kind == BuildEntryKind.Module);
        Assert.DoesNotContain(BuildCatalog.Entries(C, structures: false, stationRules: false), e => e.Category == BuildCategory.Structure);
    }

    [Fact]
    public void NoDuplicateIdsAndEveryEntryHasTextInBothLanguages()
    {
        foreach (var (structures, rules) in new[] { (true, false), (false, true), (false, false) })
        {
            var entries = BuildCatalog.Entries(C, structures, rules);
            Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
            foreach (var e in entries)
            {
                Assert.True(Loc.Has(e.DescriptionKey), "description " + e.DescriptionKey);
                Assert.False(BuildCatalog.Name(C, e).StartsWith("⟦"), "name of " + e.Id);
                Assert.True(Loc.Has(e.RoleKey));
            }
        }
        foreach (var c in BuildCatalog.Categories) Assert.True(Loc.Has("build.tab." + c.ToString().ToLowerInvariant()));
    }

    [Fact]
    public void CategoriesHoldWhatTheMenuPromises()
    {
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        IEnumerable<string> Ids(BuildCategory c) => BuildCatalog.InCategory(entries, c).Select(e => e.Id);
        Assert.Subset(new HashSet<string>(Ids(BuildCategory.Logistics)), new HashSet<string>
        {
            "belt:mk1", "belt:mk2", "belt:mk3", "core:depot", "core:unloader", "core:ore_unloader", "core:titanium_ore_unloader",
            "core:nickel_ore_unloader", "core:ice_unloader", "core:quartz_unloader", "core:carbon_unloader",
        });
        Assert.Equal(new[] { "core:smelter", "core:assembler", "core:hydrogen_cracker", "core:engineering_bench", "core:physics_lab", "core:greenhouse", "core:research_lab" }, Ids(BuildCategory.Production));
        Assert.Equal(new[] { "core:turret", "core:wall", "core:repair_bay" }, Ids(BuildCategory.Defence));
        Assert.Contains("module:cargo", Ids(BuildCategory.Structure));
        Assert.Contains("module:drone_bay", Ids(BuildCategory.Structure));
        Assert.Contains("station", Ids(BuildCategory.Structure));
        // Belts first in Logistics, in tier order; categories come in tab order.
        Assert.Equal(new[] { "belt:mk1", "belt:mk2", "belt:mk3" }, Ids(BuildCategory.Logistics).Take(3));
        Assert.Equal(entries.OrderBy(e => (int)e.Category).Select(e => e.Id), entries.Select(e => e.Id));
    }

    [Fact]
    public void CostPowerAndRoleComeFromTheContent()
    {
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        var smelter = entries.Single(e => e.Id == "core:smelter");
        Assert.Equal(C.Machines[C.MachineIndex("core:smelter")].Cost, smelter.Cost);
        Assert.Equal(10, smelter.PowerUse);
        var reactor = entries.Single(e => e.Id == "module:reactor");
        Assert.True(reactor.PowerOutput > 0);
        var bay = entries.Single(e => e.Id == "module:drone_bay");
        Assert.True(bay.PowerUse > 0);
        Assert.Equal(C.Structures!.ModuleCost(ModuleKind.Core), entries.Single(e => e.Kind == BuildEntryKind.Station).Cost);
        Assert.Equal("", BuildCatalog.PowerText(entries.Single(e => e.Id == "belt:mk1")));
    }

    private static (SimWorld W, BeltNetwork Net) Campaign()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261121UL, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        return (w, w.Network);
    }

    [Fact]
    public void LockedAndAffordableFlagsFollowTheWorld()
    {
        var (_, net) = Campaign();
        var entries = BuildCatalog.Entries(C, structures: true, stationRules: false);
        BuildStatus S(string id) => BuildCatalog.Status(C, net, 0, entries.Single(e => e.Id == id));

        var mk2 = S("belt:mk2");
        Assert.True(mk2.Locked);
        Assert.Equal(C.ResearchIndex("core:logistics"), mk2.UnlockedBy);
        Assert.False(S("belt:mk1").Locked);
        Assert.True(S("module:drone_bay").Locked);
        Assert.Equal(C.ResearchIndex("core:drone_automation"), S("module:drone_bay").UnlockedBy);
        Assert.False(S("module:cargo").Locked);
        // The ports are free of research in structure worlds, quartz and carbon ones included.
        Assert.False(S("core:quartz_unloader").Locked);
        Assert.False(S("core:carbon_unloader").Locked);

        var smelter = S("core:smelter");
        Assert.False(smelter.Locked);
        Assert.False(smelter.Affordable); // the starter hold has hydrogen only
        net.GiveItems(0, C.ItemIndex("core:iron_ingot"), 14);
        Assert.True(S("core:smelter").Affordable);
        Assert.False(S("core:assembler").Affordable); // 20 ingots

        net.GrantResearch(C.ResearchIndex("core:logistics"));
        Assert.False(S("belt:mk2").Locked);
        net.GrantResearch(C.ResearchIndex("core:drone_automation"));
        Assert.False(S("module:drone_bay").Locked);
    }

    [Fact]
    public void NothingIsLockedInTheSandbox()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 20261121UL, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        foreach (var e in BuildCatalog.Entries(C, structures: true, stationRules: false))
            Assert.False(BuildCatalog.Status(C, w.Network, 0, e).Locked, e.Id);
    }

    [Fact]
    public void FreeEntriesAreAlwaysAffordable()
    {
        var (_, net) = Campaign();
        var e = new BuildEntry("test", BuildCategory.Logistics, BuildEntryKind.Machine, -1, BeltTier.Mk1, null, Array.Empty<ItemStack>(), 0, 0);
        Assert.True(net.CanPay(0, e.Cost));
    }
}
