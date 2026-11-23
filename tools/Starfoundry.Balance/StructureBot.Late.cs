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
        // One expedition: the jump itself (the win needs a second system) and the ore of the two metal smelting nodes (40 each) and quartz.
        if (!Expedition("titanium ore, nickel ore and quartz", ("core:titanium_ore", 100), ("core:nickel_ore", 100), ("core:quartz", 32)))
            Stuck("the expedition did not bring everything");
        ResearchNow("core:titanium_smelting", TiRaw);
        ResearchNow("core:nickel_smelting", NiRaw);
        Reinforce();
        // After the win the plan keeps researching what the lab bay's single raw feed (or none) can supply: the plate-only nodes and the carbon ones.
        // The metal and quartz chains (titanium, nickel, advanced plating, the science tier) need lab faces the floor layout does not have; they are
        // not played (docs/prototypes/structure-bot.md).
        FetchCarbon(80, "carbon for advanced logistics");
        ResearchNow("core:advanced_logistics", new Feed(FeedKind.Raw, "core:carbon_unloader"));
        foreach (var id in new[] { "core:turret_calibration", "core:reinforced_plating", "core:rapid_loader", "core:ship_cannon", "core:ship_armor", "core:field_repair", "core:overclock", "core:alloy_refinement", "core:nanite_repair" })
        {
            ResearchNow(id);
            Reinforce();
        }
        LaserResearch();
    }

    /// <summary>
    /// The mining laser upgrades (ADR 0027), researched last on purpose. Measured on seed 20261121: laser focusing before the drone
    /// automation moved the jump drive 3 minutes later (the lab line, not the ore, is the limit that early), and anywhere before the
    /// science tier it tips the expeditions into a hold-and-fuel loop that costs hours. They do not pay back on the way to the win, so
    /// the plan only keeps the whole basic tree researched.
    /// </summary>
    private void LaserResearch()
    {
        ResearchNow("core:laser_focusing");
        FetchCarbon(24, "carbon for the beam collimator");
        ResearchNow("core:beam_collimator", new Feed(FeedKind.Raw, "core:carbon_unloader"));
    }

    private bool _stationLabsBuilt;

    /// <summary>
    /// With drones on the iron, ore is no longer the limit: a laboratory's plate line makes at most 30 plates a minute (one assembler),
    /// so the station gets two reactors and two more labs with their own plate lines (the same two smelter rows and assembler as the ship's).
    /// </summary>
    private void StationLabs()
    {
        if (_stationLabsBuilt || _station < 0 || !Net.StructureExists(_station) || StationLabLines <= 0) return;
        // (A station lab needs a bay of its own: not part of the default plan, see StationLabLines.)
        _stationLabsBuilt = true;
        Goal("more laboratories on the station: a reactor and plate lines");
        FetchCarbon(18, "carbon for two more station reactors");
        foreach (var (x, y, z) in new[] { (-1, 0, 0), (-1, -1, 0) })
            if (Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for a station reactor")) PlaceModule(_station, x, y, z, ModuleKind.Reactor);
        for (int i = 0; i < StationLabLines; i++)
        {
            var (bx, by, bz) = Origin(3 + i, 0, 0);
            if (PlaceModule(_station, 3 + i, 0, 0, ModuleKind.Workshop) < 0) break;
            PlateLine(_station, bx, by, bz, 4, "core:research_lab", out _);
        }
    }

    /// <summary>Plate lines with a laboratory each that the bot builds on the station workshop once drones mine the iron.</summary>
    public int StationLabLines { get; init; } = 1;

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
        Goal("drones: bay modules on the station, assign the home iron deposit first");
        if (_station < 0 || _homeDeposit < 0) return;
        // Bays are iron plates and ingots, so they are built as soon as the research is done. The station's bays mine the home iron;
        // the plate supply of the laboratories was the limit of the research (the hold limit throttles the drones when ore piles up).
        int placed = 0;
        foreach (var (x, y, z) in new[] { (0, 0, -1), (0, 0, -2), (-1, 0, 0) })
        {
            if (placed >= DroneBaysOnIron) break;
            if (PlaceModule(_station, x, y, z, ModuleKind.DroneBay) < 0) break;
            placed++;
            if (placed == 1 && !Failed(Act(new AssignDrones(_station, _homeDeposit)), "AssignDrones station->iron")) _dronesOnIron = true;
        }
        // A ship bay mines the ice, so the hydrogen no longer needs hand-mined trips (the bot pauses it when ice piles up, see ManageDrones).
        if (_iceDeposit >= 0 && PlaceModule(_ship, B1.X, B1.Y, B1.Z, ModuleKind.DroneBay) > 0 && !Failed(Act(new AssignDrones(_ship, _iceDeposit)), "AssignDrones ship->ice")) _dronesOnIce = true;
    }

    private bool _dronesOnIron, _dronesPaused, _dronesOnIce, _icePaused;

    /// <summary>Ice in the hold above which the ship's drone bay is taken off the ice deposit.</summary>
    public int IceStock { get; init; } = 40;

    /// <summary>
    /// A player does not let the drones bury the hold in ore: they are taken off the iron when the stock is plenty (the lines use about
    /// two ore a second each, the hold limit would stop every other delivery) and put back when it runs down.
    /// </summary>
    private void ManageDrones()
    {
        if (_dronesOnIce && Net.StructureExists(_ship) && _iceDeposit >= 0)
        {
            int ice = Have("core:ice");
            if (!_icePaused && (ice >= IceStock || Hydrogen >= 45)) { Cmd(new AssignDrones(_ship, -1)); _icePaused = true; }
            else if (_icePaused && ice <= 12 && Hydrogen < 40 && Net.DepositRemaining(_iceDeposit) > 0) { Cmd(new AssignDrones(_ship, _iceDeposit)); _icePaused = false; }
        }
        if (!_dronesOnIron || _station < 0 || !Net.StructureExists(_station) || _homeDeposit < 0) return;
        int ore = Have("core:iron_ore");
        if (!_dronesPaused && ore >= DroneOreStock) { Cmd(new AssignDrones(_station, -1)); _dronesPaused = true; }
        else if (_dronesPaused && ore <= DroneOreStock / 3 && Net.DepositRemaining(_homeDeposit) > 0) { Cmd(new AssignDrones(_station, _homeDeposit)); _dronesPaused = false; }
    }

    /// <summary>Raw iron ore the hold may hold before the station's drones are taken off the deposit.</summary>
    public int DroneOreStock { get; init; } = 90;

    /// <summary>Drone bays on the station that mine the home iron (the ship's bays go to ice or carbon only when a stock runs low).</summary>
    public int DroneBaysOnIron { get; init; } = 2;
}
