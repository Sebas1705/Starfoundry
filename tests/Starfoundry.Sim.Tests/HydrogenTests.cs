using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>Hydrogen: the cracker that makes it from ice, the ice port that feeds it, and refuelling the starter ship by hand.</summary>
public class HydrogenTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ice = C.ItemIndex("core:ice");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private static readonly int Cracker = C.MachineIndex("core:hydrogen_cracker");
    private static readonly int IcePort = C.MachineIndex("core:ice_unloader");
    private static readonly int Depot = C.MachineIndex("core:depot");
    private static readonly int CrackIce = C.RecipeIndex("core:crack_ice");
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;
    private const int Tps = Units.TicksPerSecond;

    private readonly ITestOutputHelper _out;
    public HydrogenTests(ITestOutputHelper output) => _out = output;

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int Ok(SimWorld w, Command c)
    {
        var r = Run(w, c);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    [Fact]
    public void Content_describes_the_cracker_the_recipe_and_the_ice_port()
    {
        var recipe = C.Recipes[CrackIce];
        Assert.Equal(Cracker, recipe.Machine);
        Assert.Equal(new[] { new ItemStack(Ice, 2) }, recipe.Inputs);
        Assert.Equal(new[] { new ItemStack(Hydrogen, 1) }, recipe.Outputs);
        Assert.Equal(4 * Tps, recipe.TimeTicks);
        Assert.Equal(MachineRole.Crafter, C.Machines[Cracker].Role);
        Assert.Equal(12, C.Machines[Cracker].PowerUse);
        Assert.Equal(new[] { new ItemStack(C.ItemIndex("core:iron_plate"), 20), new ItemStack(C.ItemIndex("core:iron_ingot"), 10) }, C.Machines[Cracker].Cost);
        Assert.Equal(MachineRole.Unloader, C.Machines[IcePort].Role);
        Assert.Equal(Ice, C.Machines[IcePort].Unloader!.Item);
        Assert.Equal(C.Machines[C.MachineIndex("core:ore_unloader")].Unloader!.IntervalTicks, C.Machines[IcePort].Unloader!.IntervalTicks);
        Assert.Equal(Hydrogen, C.FuelItem);
        Assert.Equal(new[] { "core:silicon", "core:circuit", "core:titanium_alloy", "core:fuel_cell" }, Enumerable.Range(Hydrogen + 1, 4).Select(i => C.Items[i].Id)); // appended after it: existing item indices are unchanged (the science items and biomass come after them)

        // Power use per machine, as designed.
        var uses = new Dictionary<string, int>
        {
            ["core:smelter"] = 10, ["core:assembler"] = 15, ["core:hydrogen_cracker"] = 12, ["core:research_lab"] = 20, ["core:depot"] = 1,
            ["core:ore_unloader"] = 2, ["core:ice_unloader"] = 2, ["core:unloader"] = 2, ["core:titanium_ore_unloader"] = 2,
            ["core:nickel_ore_unloader"] = 2, ["core:turret"] = 5, ["core:repair_bay"] = 10, ["core:wall"] = 0,
        };
        foreach (var (id, use) in uses) Assert.Equal(use, C.Machines[C.MachineIndex(id)].PowerUse);
        Assert.Equal(100, C.Structures!.PowerOutput(ModuleKind.Reactor));
        Assert.Equal(900, C.Structures.BurnIntervalTicks(ModuleKind.Reactor));
        Assert.Equal(0, C.Structures.PowerOutput(ModuleKind.Engine));
        Assert.Equal(new[] { new ItemStack(Hydrogen, 20) }, C.StarterShip!.Hold);
    }

    [Fact]
    public void The_cracker_and_the_ice_port_are_available_from_the_start_and_fit_the_workshop()
    {
        Assert.False(C.MachineGated[Cracker]);
        Assert.False(C.RecipeGated[CrackIce]);
        Assert.False(C.MachineGated[IcePort]);
        var kinds = C.Structures!;
        foreach (var kind in new[] { ModuleKind.Workshop, ModuleKind.Core })
        {
            Assert.True(kinds.AllowsRole(kind, MachineRole.Crafter));
            Assert.True(kinds.AllowsRole(kind, MachineRole.Unloader));
        }
        var campaign = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, structures: true, rules: TestContent.LegacyShip);
        Assert.False(campaign.Network.IsMachineLocked(Cracker));
        Assert.False(campaign.Network.IsMachineLocked(IcePort));
        Assert.False(campaign.Network.IsRecipeLocked(CrackIce));
    }

    [Fact]
    public void Cracker_turns_ice_into_hydrogen()
    {
        var w = SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, structures: true, rules: TestContent.LegacyShip);
        var n = w.Network;
        int s = PowerTests.Ship(w, hydrogen: 5);
        int input = n.AddSegment(0, 3, 0, 0, 0, 1, 3, BeltTier.Mk1, s);
        n.SetSource(input, new ushort[] { (ushort)Ice });
        int cracker = n.AddMachine(0, 3, 3, Cracker, CrackIce, owner: 0, structure: s);
        n.ConnectToMachine(input, cracker);
        int output = n.AddSegment(0, 3, 4, 0, 0, 1, 3, BeltTier.Mk1, s);
        n.ConnectFromMachine(cracker, output);
        n.ConnectToMachine(output, n.AddMachine(0, 3, 7, Depot, -1, owner: 0, structure: s));

        const int ticks = 1200; // 40 seconds: ten crafts of 4 s
        w.Step(ticks);
        int burns = (ticks + 899) / 900;
        long made = n.DepotDelivered;
        Assert.InRange(made, 8, 10);
        Assert.Equal(5 - burns + made, n.HoldCount(0, Hydrogen)); // everything the cracker made reached the hold
        Assert.Equal(0, n.HoldCount(0, Ice)); // ice never reaches the hold here: it goes straight into the cracker
        Assert.True(n.MachineInputsConsumed >= 2 * made); // two ice per hydrogen
        n.ValidateInvariants();
    }

    [Fact]
    public void Hand_mined_ice_can_refuel_the_ship()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: true, rules: TestContent.LegacyShip);
        var n = w.Network;
        int ship = n.ShipStructure(0);

        // hold -> ice port -> belt -> cracker -> belt -> depot -> hold, in free workshop cells beside the starter factory.
        int port = Ok(w, new PlaceMachineIn(ship, 0, 3, 9, IcePort, -1));
        int b1 = Ok(w, new PlaceSegmentIn(ship, 1, 3, 9, Dir.PosX, 2, BeltTier.Mk1));
        int cracker = Ok(w, new PlaceMachineIn(ship, 3, 3, 9, Cracker, CrackIce));
        int b2 = Ok(w, new PlaceSegmentIn(ship, 4, 3, 9, Dir.PosX, 2, BeltTier.Mk1));
        int depot = Ok(w, new PlaceMachineIn(ship, 6, 3, 9, Depot, -1));
        Ok(w, new ConnectFromMachine(port, b1));
        Ok(w, new ConnectToMachine(b1, cracker));
        Ok(w, new ConnectFromMachine(cracker, b2));
        Ok(w, new ConnectToMachine(b2, depot));
        w.Step();
        Assert.Equal(28 + 2 + 12 + 1, n.PowerStatus(ship).Demand);
        Assert.Equal(1000, n.PowerStatus(ship).SatisfactionPermille);

        // Fly to the home ice deposit and mine it by hand.
        int ice = Enumerable.Range(0, n.DepositCount).First(d => n.DepositItem(d) == Ice && n.IsDepositRevealed(d));
        var (x, y, z) = n.DepositPosition(ice);
        for (int i = 0; i < 20 * 60 * Tps && !n.InMineRange(0, ice); i++)
        {
            var p = n.GetPlayerInfo(0);
            w.Submit(0, new SetMovement(Math.Sign(x * (long)Cell - p.X), Math.Sign(y * (long)Cell - p.Y), Math.Sign(z * (long)Cell - p.Z)));
            w.Step();
        }
        Assert.True(n.InMineRange(0, ice), "the ship never reached the ice");
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(ice));
        w.Step();
        long mineStart = w.Tick;
        long deliveredBefore = n.DepotDelivered;
        long firstHydrogen = -1;
        for (int t = 1; t <= 6 * 60 * Tps; t++)
        {
            w.Step();
            if (firstHydrogen < 0 && n.DepotDelivered > deliveredBefore) firstHydrogen = w.Tick - mineStart;
        }
        _out.WriteLine($"ice density {n.DepositDensity(ice)} percent; hand mining {n.MineTicksFor(ice)} ticks per ice; first hydrogen after {firstHydrogen} ticks ({firstHydrogen / (double)Tps:F1} s)");
        Assert.True(firstHydrogen > 0, "no hydrogen was made from the mined ice");

        long made = n.DepotDelivered - deliveredBefore;
        long burns = (w.Tick + 899) / 900;
        _out.WriteLine($"hydrogen made in 6 minutes of mining: {made}; burned: {burns}; in hold: {n.HoldCount(0, Hydrogen)}");
        Assert.True(made >= 10, $"only {made} hydrogen in six minutes");
        Assert.Equal(20 - burns + made, n.HoldCount(0, Hydrogen)); // the loop returns every hydrogen to the hold
        // Mining and cracking together out-produce one reactor (which burns one per 30 s).
        Assert.True(n.HoldCount(0, Hydrogen) > 20 - 1);
        Assert.Equal(1000, n.ShipPower(0).SatisfactionPermille);
        w.ValidateInvariants();
    }

    [Fact]
    public void A_ship_with_no_hydrogen_recovers_without_any_hydrogen_cheat()
    {
        var w = SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, structures: true, rules: TestContent.LegacyShip);
        var n = w.Network;
        int s = PowerTests.Ship(w); // reactor, no hydrogen: only the emergency output
        n.GiveItems(0, Ice, 20); // the ice the player mined by hand: the only item placed in the hold
        // Hold -> ice port -> belt -> cracker -> belt -> depot -> hold.
        int port = n.AddMachine(0, 3, 0, IcePort, -1, owner: 0, structure: s);
        int input = n.AddSegment(1, 3, 0, 1, 0, 0, 2, BeltTier.Mk1, s);
        n.ConnectFromMachine(port, input);
        int cracker = n.AddMachine(3, 3, 0, Cracker, CrackIce, owner: 0, structure: s);
        n.ConnectToMachine(input, cracker);
        int output = n.AddSegment(4, 3, 0, 1, 0, 0, 2, BeltTier.Mk1, s);
        n.ConnectFromMachine(cracker, output);
        n.ConnectToMachine(output, n.AddMachine(6, 3, 0, Depot, -1, owner: 0, structure: s));
        Assert.Equal(0, n.HoldCount(0, Hydrogen));
        w.Step(2400);
        Assert.True(n.PowerStatus(s).SatisfactionPermille > 0, "a dead ship still has emergency power");
        Assert.True(n.HoldCount(0, Hydrogen) >= 3 || n.PowerStatus(s).Burning, "the cracker brought hydrogen back");
        Assert.True(n.DepotDelivered >= 3);
        w.Step(600);
        Assert.True(n.PowerStatus(s).Burning || n.HoldCount(0, Hydrogen) > 0);
        w.Network.ValidateInvariants();
    }

    // ---- Ui: tips and objective ------------------------------------------------------------------

    private static SimWorld StarterWorld() => SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: true, rules: TestContent.LegacyShip);

    [Fact]
    public void No_fuel_tip_fires_once_when_the_hydrogen_is_nearly_gone()
    {
        var w = StarterWorld();
        var tips = new Tips(C);
        Assert.Null(tips.Next(w.Network, 0)); // 20 hydrogen: fine
        w.Network.Pay(0, new[] { new ItemStack(Hydrogen, 17) });
        Assert.Equal(3, w.Network.HoldCount(0, Hydrogen));
        Assert.Equal("learn.noFuel", tips.Next(w.Network, 0)?.Key);
        Assert.Null(tips.Next(w.Network, 0)); // once only

        // Legacy worlds and ships without a reactor stay silent.
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: false);
        legacy.Network.EnsurePlayer(0);
        Assert.Null(new Tips(C).Next(legacy.Network, 0));
        var noReactor = SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, structures: true, rules: TestContent.LegacyShip);
        noReactor.Network.EnsurePlayer(0);
        int ship = noReactor.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        noReactor.Network.AddStructureModuleUnchecked(ship, 0, 0, 0, ModuleKind.Cockpit);
        noReactor.Network.AddStructureModuleUnchecked(ship, 1, 0, 0, ModuleKind.Engine);
        Assert.Null(new Tips(TestContent.CoreWithoutStarterShip).Next(noReactor.Network, 0));
    }

    [Fact]
    public void Low_power_tip_fires_when_the_hydrogen_ran_out_and_the_factory_is_short()
    {
        var w = StarterWorld();
        var tips = new Tips(C);
        w.Step(20 * 900 + 10); // the fuel is gone
        Assert.Equal(535, w.Network.ShipPower(0).SatisfactionPermille); // only the emergency output (15 against 28)
        Assert.Equal("learn.noFuel", tips.Next(w.Network, 0)?.Key);
        w.Step(Tps * 30);
        Assert.Equal("learn.lowPower", tips.Next(w.Network, 0)?.Key);
        w.Step(Tps * 60);
        Assert.Null(tips.Next(w.Network, 0));
    }

    [Fact]
    public void Make_fuel_objective_completes_when_a_cracker_has_output_or_the_hold_has_more_than_the_start()
    {
        var w = StarterWorld();
        var t = new ObjectiveTracker(C, structures: true);
        t.Update(w.Network, 0);
        Assert.False(t.IsDone(Objective.MakeFuel));
        w.Network.GiveItems(0, Hydrogen, 1); // 21 > the starting 20
        t.Update(w.Network, 0);
        Assert.True(t.IsDone(Objective.MakeFuel));

        var w2 = StarterWorld();
        var t2 = new ObjectiveTracker(C, structures: true);
        int ship = w2.Network.ShipStructure(0);
        int cracker = Ok(w2, new PlaceMachineIn(ship, 3, 3, 9, Cracker, CrackIce));
        t2.Update(w2.Network, 0);
        Assert.False(t2.IsDone(Objective.MakeFuel)); // a cracker with nothing made yet is not enough
        int belt = Ok(w2, new PlaceSegmentIn(ship, 0, 3, 9, Dir.PosX, 3, BeltTier.Mk1));
        Ok(w2, new SetSource(belt, new ushort[] { (ushort)Ice }));
        Ok(w2, new ConnectToMachine(belt, cracker));
        for (int i = 0; i < 400 && !t2.IsDone(Objective.MakeFuel); i++)
        {
            w2.Step();
            t2.Update(w2.Network, 0);
        }
        Assert.True(t2.IsDone(Objective.MakeFuel));

        // It sits right after FoundStation in the structures list and nowhere else.
        var order = t.States.Select(s => s.Objective).ToList();
        Assert.Equal(order.IndexOf(Objective.Dock) + 1, order.IndexOf(Objective.MakeFuel)); // the dock step sits between the station and the fuel
        Assert.DoesNotContain(Objective.MakeFuel, new ObjectiveTracker(C, structures: false).States.Select(s => s.Objective));
        Assert.DoesNotContain(Objective.MakeFuel, new ObjectiveTracker(C, stationRules: true).States.Select(s => s.Objective));
    }

    [Fact]
    public void New_hydrogen_strings_exist_in_both_languages()
    {
        var keys = new[]
        {
            "item.core:hydrogen", "machine.core:hydrogen_cracker", "machine.core:ice_unloader", "recipe.core:crack_ice",
            "learn.noFuel", "learn.lowPower", "obj.makeFuel", "obj.makeFuel.hint",
        };
        foreach (var key in keys)
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
    }
}
