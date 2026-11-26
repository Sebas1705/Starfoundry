using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Balance;

/// <summary>The second half of the plan: new metals, plates of them, drone bays and the rest of the research.</summary>
public sealed partial class StructureBot
{
    private static readonly Feed CarbonRaw = new(FeedKind.Raw, "core:carbon_unloader", "core:carbon");
    private static readonly Feed TiRaw = new(FeedKind.Raw, "core:titanium_ore_unloader", "core:titanium_ore");
    private static readonly Feed NiRaw = new(FeedKind.Raw, "core:nickel_ore_unloader", "core:nickel_ore");
    private static readonly Feed QuartzRaw = new(FeedKind.Raw, "core:quartz_unloader", "core:quartz");
    private static readonly Feed TiIngots = new(FeedKind.Ingots, "core:titanium_ore_unloader", "core:titanium_ingot", "core:smelt_titanium");
    private static readonly Feed NiIngots = new(FeedKind.Ingots, "core:nickel_ore_unloader", "core:nickel_ingot", "core:smelt_nickel");
    private static readonly Feed SiliconIngots = new(FeedKind.Ingots, "core:quartz_unloader", "core:silicon", "core:smelt_silicon");
    private static readonly Feed TiPlates = new(FeedKind.Plates, "core:titanium_ore_unloader", "core:titanium_plate", "core:smelt_titanium", "core:titanium_plate");
    private static readonly Feed NiPlates = new(FeedKind.Plates, "core:nickel_ore_unloader", "core:nickel_plate", "core:smelt_nickel", "core:nickel_plate");

    private static readonly string[] PlateNodes =
    {
        "core:turret_calibration", "core:reinforced_plating", "core:rapid_loader", "core:ship_cannon", "core:ship_armor", "core:field_repair",
        "core:overclock", "core:alloy_refinement", "core:nanite_repair",
    };

    private bool WinReached => _seen.Contains(VictoryMilestone);

    private void PlayRemainder()
    {
        MoreCargoUpTo(4);
        // The win needs one jump. The ship takes its machines (and their noise) with it, so a long trip would hold the waves back and the win with
        // them: one hop to a neighbour and home again, then the expedition for the new metals waits for the win (or follows it).
        FirstJump();
        // Until the win the base is needed at home: the nodes that cost only plates, which also make the defence stronger.
        foreach (var id in PlateNodes)
        {
            if (WinReached) break;
            ResearchNow(id);
            Reinforce();
        }
        Goal("prepare the first expedition");
        // One expedition: the ore of the two metal smelting nodes (40 each), the same again for the ingots of advanced plating, and its quartz.
        if (!Expedition("titanium ore, nickel ore and quartz", ("core:titanium_ore", 100), ("core:nickel_ore", 100), ("core:quartz", 32)))
            Stuck("the expedition did not bring everything");
        ResearchNow("core:titanium_smelting", TiRaw);
        ResearchNow("core:nickel_smelting", NiRaw);
        Reinforce();
        // Drones mine the iron now, so ore is no longer the limit and the plate lines are: more laboratories on the station.
        StationLabs();
        FetchCarbon(80, "carbon for advanced logistics");
        ResearchNow("core:advanced_logistics", CarbonRaw);
        foreach (var id in PlateNodes)
        {
            ResearchNow(id);
            Reinforce();
        }
        // Advanced plating: titanium and nickel ingots and quartz, three feeds on the two free faces of the lab (the third waits for the first to be done).
        ResearchNow("core:advanced_plating", TiIngots, NiIngots, QuartzRaw);
        if (Science) PlayScience();
        LaserResearch();
    }

    /// <summary>The first jump of the game (a neighbouring system and back), done as soon as the drive is researched when the win still waits for it.</summary>
    private void FirstJump()
    {
        if (GameOutcome.HasExplored(Net) || WinReached || !Net.HasAbility(Ability.Jump)) return;
        Goal("first jump: a neighbouring system and back (the win needs one)");
        WaitUntil(() => !WaveSoon(60), 240);
        WaitUntil(() => Net.EnemyCount == 0, 180);
        if (Hydrogen < 40 && !_refuelling) { Refuel(50); WaitUntil(() => Hydrogen >= 35, 240); }
        if (Have("core:carbon") < JumpCarbon * 2 + 5) Gather(Item("core:carbon"), JumpCarbon * 2 + 15, "the first jump");
        int next = Net.Galaxy!.Systems[0].Lanes.FirstOrDefault(n => Net.IsSystemKnown(n), -1);
        if (next < 0) { Stuck("no neighbouring system on the star map for the first jump"); return; }
        _inExpedition = true;
        try
        {
            if (!JumpTo(next)) return;
            ScanHere();
            if (PathTo(CurrentSystem, 0) is { } home) JumpPath(home);
            _mineTarget = -1;
            GoHome();
        }
        finally { _inExpedition = false; }
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
        ResearchNow("core:beam_collimator", CarbonRaw);
    }

    private bool _stationLabsBuilt;

    /// <summary>
    /// With drones on the iron, ore is no longer the limit: a laboratory's plate line makes at most 30 plates a minute (one assembler), so the
    /// station gets two reactors and more laboratories, each in a fabrication bay of its own with the same two smelter rows and assembler as the
    /// ship's lab. The bays lie side by side along x beside the station's defence module.
    /// </summary>
    private void StationLabs()
    {
        if (_stationLabsBuilt || _station < 0 || !Net.StructureExists(_station) || StationLabLines <= 0 || !_dronesOnIron) return;
        _stationLabsBuilt = true;
        Goal("more laboratories on the station: reactors and bays with plate lines");
        FetchCarbon(18, "carbon for two more station reactors");
        foreach (var (x, y, z) in new[] { (1, -1, 0), (2, -1, 0) })
            if (Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for a station reactor")) PlaceModule(_station, x, y, z, ModuleKind.Reactor);
        for (int i = 0; i < StationLabLines; i++)
        {
            var (bx, by, bz) = Origin(3 + i, 0, 0);
            if (PlaceModule(_station, 3 + i, 0, 0, ModuleKind.Workshop) < 0) break;
            PlateLine(_station, bx, by, bz, 4, "core:research_lab", out _);
        }
    }

    /// <summary>Plate lines with a laboratory each that the bot builds on the station workshop once drones mine the iron.</summary>
    public int StationLabLines { get; init; } = 2;

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
        foreach (var (x, y, z) in new[] { (-1, 0, 0), (-1, 0, 1), (-1, 0, -1) }) // west of the core and the laboratory bays (ADR 0039: the second laboratory bay stands at (0,0,-1))
        {
            if (placed >= DroneBaysOnIron) break;
            if (PlaceModule(_station, x, y, z, ModuleKind.DroneBay) <= 0) break;
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
