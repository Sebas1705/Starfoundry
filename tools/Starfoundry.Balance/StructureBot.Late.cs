using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>The second half of the plan: new metals, plates of them, drone bays and the rest of the research.</summary>
public sealed partial class StructureBot
{
    private static readonly Feed TiRaw = new(FeedKind.Raw, "core:titanium_ore_unloader");
    private static readonly Feed NiRaw = new(FeedKind.Raw, "core:nickel_ore_unloader");
    private static readonly Feed QuartzRaw = new(FeedKind.Raw, "core:quartz_unloader");
    private static readonly Feed TiIngots = new(FeedKind.Ingots, "core:titanium_ore_unloader", "core:smelt_titanium");
    private static readonly Feed NiIngots = new(FeedKind.Ingots, "core:nickel_ore_unloader", "core:smelt_nickel");
    private static readonly Feed TiPlates = new(FeedKind.Plates, "core:titanium_ore_unloader", "core:smelt_titanium", "core:titanium_plate");

    private void PlayRemainder()
    {
        MoreCargoUpTo(4);
        Goal("prepare the first expedition");
        // 1: titanium and nickel ore for their smelting research, and quartz for the plating that follows.
        if (!Expedition("titanium ore, nickel ore and quartz", ("core:titanium_ore", 40), ("core:nickel_ore", 40), ("core:quartz", 30)))
            Stuck("the first expedition did not bring everything");
        ResearchNow("core:titanium_smelting", TiRaw);
        ResearchNow("core:nickel_smelting", NiRaw);
        Reinforce();
        // 2: ingots of both metals and more quartz for advanced plating.
        Expedition("ore for titanium and nickel ingots, more quartz", ("core:titanium_ore", 40), ("core:nickel_ore", 40), ("core:quartz", 50));
        ResearchNow("core:advanced_plating", TiIngots, NiIngots, QuartzRaw);
        Reinforce();
        // 3: titanium plates (made from ore) for the drone research.
        Expedition("titanium ore for plates, quartz", ("core:titanium_ore", 40), ("core:quartz", 20));
        ResearchNow("core:drone_automation", TiPlates, QuartzRaw);
        Reinforce();
        // 4: drone bays need titanium and nickel plates in the hold.
        BuildDrones();
        // The rest: defence research until the game is decided.
        FetchCarbon(80, "carbon for advanced logistics");
        ResearchNow("core:advanced_logistics", new Feed(FeedKind.Raw, "core:carbon_unloader"));
        foreach (var id in new[] { "core:turret_calibration", "core:reinforced_plating", "core:rapid_loader", "core:ship_cannon", "core:ship_armor", "core:field_repair", "core:overclock", "core:alloy_refinement", "core:nanite_repair" })
        {
            ResearchNow(id);
            Reinforce();
        }
    }

    private void MoreCargoUpTo(int total)
    {
        int have = Net.GetStructure(_ship).Modules.Values.Count(k => k == ModuleKind.Cargo) - 1;
        if (have >= total) return;
        Goal($"cargo modules up to {total} extra");
        for (int i = have; i < total && i < CargoSpots.Length; i++) PlaceModule(_ship, CargoSpots[i].X, CargoSpots[i].Y, CargoSpots[i].Z, ModuleKind.Cargo);
    }

    /// <summary>A line port, smelter, plate assembler, depot: plates of a new metal into the hold.</summary>
    private bool PlateDepotLine(int st, int ox, int oy, int oz, int y, int z, string port, string smelt, string plate)
    {
        int p = PlaceMachine(st, ox, oy + y, oz + z, port);
        int s = PlaceMachine(st, ox + 3, oy + y, oz + z, "core:smelter", smelt);
        int a = PlaceMachine(st, ox + 5, oy + y, oz + z, "core:assembler", plate);
        int d = PlaceMachine(st, ox + 7, oy + y, oz + z, "core:depot");
        if (p < 0 || s < 0 || a < 0 || d < 0) return false;
        return Link(st, p, ox + 1, oy + y, oz + z, Dir.PosX, 2, s) >= 0
            && Link(st, s, ox + 4, oy + y, oz + z, Dir.PosX, 1, a) >= 0
            && Link(st, a, ox + 6, oy + y, oz + z, Dir.PosX, 1, d) >= 0;
    }

    private void BuildDrones()
    {
        Goal("6 drones: titanium and nickel plates, drone bay modules, assign deposits");
        var bayCost = _c.Structures!.ModuleCost(ModuleKind.DroneBay);
        int ti = Item("core:titanium_plate"), ni = Item("core:nickel_plate");
        int needTi = bayCost.Where(c => c.Item == ti).Sum(c => c.Count) * 2, needNi = bayCost.Where(c => c.Item == ni).Sum(c => c.Count) * 2;
        if (Have("core:titanium_plate") < needTi || Have("core:nickel_plate") < needNi)
        {
            Expedition("ore for the drone bays' titanium and nickel plates", ("core:titanium_ore", needTi * 2), ("core:nickel_ore", needNi * 2));
            var (ox, oy, oz) = Origin(1, 0, 0); // station workshop
            if (_station >= 0)
            {
                PlateDepotLine(_station, ox, oy, oz, 2, 3, "core:titanium_ore_unloader", "core:smelt_titanium", "core:titanium_plate");
                PlateDepotLine(_station, ox, oy, oz, 2, 5, "core:nickel_ore_unloader", "core:smelt_nickel", "core:nickel_plate");
            }
        }
        if (!WaitUntil(() => Have("core:titanium_plate") >= 8 && Have("core:nickel_plate") >= 8, 20 * 60)) Stuck("titanium and nickel plates never reached the hold for the drone bays");
        // One bay on the station mines the iron; one on the ship mines the ice.
        if (_station >= 0 && PlaceModule(_station, 0, 0, -1, ModuleKind.DroneBay) > 0 && _homeDeposit >= 0)
            Failed(Act(new AssignDrones(_station, _homeDeposit)), "AssignDrones station->iron");
        if (WaitUntil(() => Have("core:titanium_plate") >= 8 && Have("core:nickel_plate") >= 8, 10 * 60) && PlaceModule(_ship, B1.X, B1.Y, B1.Z, ModuleKind.DroneBay) > 0 && _iceDeposit >= 0)
            Failed(Act(new AssignDrones(_ship, _iceDeposit)), "AssignDrones ship->ice");
    }
}
