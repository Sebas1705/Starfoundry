using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Research descriptions must be truthful about what the research unlocks in a structure world.</summary>
public class ResearchTextTests
{
    private static readonly ContentDb C = TestContent.Core;

    [Fact]
    public void Every_unlock_named_in_a_research_description_is_really_locked_before()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261121UL, threats: false, stationRules: false, structures: true);
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        var problems = new List<string>();
        foreach (var node in C.Research)
        {
            if (!table.TryGetValue($"research.{node.Id}.desc", out var desc)) continue;
            foreach (int m in node.UnlockMachines)
            {
                string id = C.Machines[m].Id;
                if (!w.Network.IsMachineLocked(m) && Names(table, $"machine.{id}", desc))
                    problems.Add($"'{node.Id}' names machine '{id}' but it is already free in a structure world");
            }
            foreach (int r in node.UnlockRecipes)
            {
                string id = C.Recipes[r].Id;
                if (!w.Network.IsRecipeLocked(r) && Names(table, $"recipe.{id}", desc))
                    problems.Add($"'{node.Id}' names recipe '{id}' but it is already free in a structure world");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>True if either language of the description contains the (same language) name of the thing, ignoring case.</summary>
    private static bool Names(Dictionary<string, (string Es, string En)> table, string nameKey, (string Es, string En) desc)
    {
        if (!table.TryGetValue(nameKey, out var name)) return false;
        return desc.En.Contains(name.En, StringComparison.OrdinalIgnoreCase) || desc.Es.Contains(name.Es, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Carbon_processing_gives_the_machine_hit_point_bonus_it_describes()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 20261121UL, threats: false, stationRules: false, structures: true);
        int smelter = C.MachineIndex("core:smelter");
        int before = w.Network.MaxHp(smelter);
        w.Network.GrantResearch(C.ResearchIndex("core:carbon_drilling"));
        Assert.Equal(before * 120 / 100, w.Network.MaxHp(smelter));
        Assert.Equal(("Procesado de carbono", "Carbon processing"), Loc.All.Single(p => p.Key == "research.core:carbon_drilling").Value);
        Assert.Equal(60, C.Research[C.ResearchIndex("core:carbon_drilling")].Cost.Single().Count);
    }
}
