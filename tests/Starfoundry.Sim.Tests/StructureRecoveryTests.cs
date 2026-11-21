using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Found by the structure bot (docs/prototypes/structure-bot.md): a ship whose only engine is shot to pieces cannot fly, so it cannot
/// reach the titanium that the old engine cost needed. The ship's own modules must be rebuildable from what the home system gives.
/// </summary>
public class StructureRecoveryTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261121UL;

    private static readonly string[] HomeItems =
    {
        "core:iron_ore", "core:iron_ingot", "core:iron_plate", "core:carbon", "core:ice", "core:hydrogen", "core:ammo",
    };

    [Fact]
    public void Every_module_of_the_starter_ship_can_be_rebuilt_from_items_found_at_home()
    {
        var home = HomeItems.Select(C.ItemIndex).ToHashSet();
        var shipKinds = new[] { ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.Cargo, ModuleKind.Workshop };
        foreach (var kind in shipKinds)
            foreach (var cost in C.Structures!.ModuleCost(kind))
                Assert.True(home.Contains(cost.Item), $"a {kind} module costs {C.Items[cost.Item].Id}, which cannot be had without leaving home");
    }

    [Fact]
    public void A_ship_that_lost_its_only_engine_can_build_a_new_one_with_home_items()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: true, stationRules: false, structures: true);
        var n = w.Network;
        int ship = n.ShipStructure(0);
        Assert.True(n.CanFly(0));

        // The engine module (0, 0, 4) falls.
        n.HitModule(ship, 0, 0, 4, 100_000);
        w.Step();
        Assert.DoesNotContain(ModuleKind.Engine, n.GetStructure(ship).Modules.Values);
        Assert.False(n.CanFly(0));

        // What a player has at home: iron plates and carbon (here placed in the hold; the loop that makes them is covered elsewhere).
        n.GiveItems(0, C.ItemIndex("core:iron_plate"), 24);
        n.GiveItems(0, C.ItemIndex("core:iron_ingot"), 24);
        n.GiveItems(0, C.ItemIndex("core:carbon"), 8);
        w.Submit(0, new PlaceModuleIn(ship, 1, 0, 0, ModuleKind.Engine)); // beside the cockpit, inside build range
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        Assert.True(n.CanFly(0));
        w.ValidateInvariants();
    }
}
