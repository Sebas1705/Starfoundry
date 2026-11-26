using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Balance;

/// <summary>
/// Story levels ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)): the bot plays the stated goals of levels 01, 02, 03, 06 and 07 with the same
/// commands a player uses. Levels 04 and 05 have no plan; they are validated by reachability analysis in the content tests.
/// </summary>
public sealed partial class StructureBot
{
    /// <summary>Scenario ids this bot can play.</summary>
    public static readonly string[] PlayableScenarios = { "01_first_light", "02_cold_fuel", "03_under_siege", "06_salvage_run", "07_hull_breach" };

    /// <summary>The scenario's stars after the run (0 when it was not won or the bot played the standard campaign).</summary>
    public int Stars => _progress?.Stars ?? 0;

    /// <summary>The goals of the scenario as they stand (empty for the standard campaign).</summary>
    public IReadOnlyList<GoalState> GoalStates => _progress?.Goals ?? Array.Empty<GoalState>();

    private OutcomeResult ScenarioOutcome()
    {
        foreach (var e in _progress!.Update(Net, 0)) Say($"STORY [{e.Beat.Trigger}] {e.Beat.Speaker}: {e.Beat.TextEn}");
        foreach (var g in _progress.Goals)
        {
            if (g.Status == GoalStatus.Done && !g.Goal.IsCondition) Milestone("Goal done: " + g.Goal.Id);
            else if (g.Status == GoalStatus.Failed) Milestone("Goal failed: " + g.Goal.Id);
        }
        return new OutcomeResult(_progress.Outcome, _progress.Reason, GameOutcome.Stats(Net, 0));
    }

    private void PlayScenario()
    {
        switch (_scenario!.Id)
        {
            case "01_first_light":
                MineIron();
                FoundStation();
                FabricationBayOnly();
                break;
            case "02_cold_fuel":
                PlayColdFuel();
                break;
            case "03_under_siege":
                _factoryUp = true; // the pre-built station factory feeds the hold
                PlaySiege();
                break;
            case "06_salvage_run":
                PlaySalvageRun();
                break;
            case "07_hull_breach":
                PlayHullBreach();
                break;
            default:
                throw new NotSupportedException($"The bot has no plan for scenario '{_scenario.Id}' (levels 04 and 05 are validated by reachability analysis only).");
        }
    }

    // ---- 02 cold fuel -----------------------------------------------------------------------------------

    private void PlayColdFuel()
    {
        // The level starts with a station that has a bay and a factory but no reactor (ADR 0039: the ship carries no factory).
        _station = Net.Structures().First(s => s.Kind == StructureKind.Station && s.Owner == 0).Id;
        _factoryUp = true; // the workbench is not part of this plan
        MineIron();
        FetchCarbon(8, "the station reactor");
        Goal("a reactor module on the station");
        PlaceModule(_station, 0, -1, 0, ModuleKind.Reactor);
        if (_progress!.Find("dock_stock") is not null) DockAndStore("core:iron_ingot", 20); // the game ends with the last primary goal, so the optional stock goes first
        HydrogenLoop();
        MoreCargo(2);
    }

    // ---- 01 first light: the optional fabrication bay ----------------------------------------------------

    /// <summary>The optional goal of level 01: a fabrication bay in the station, paid from the workbench's ingots and plates (no factory in it).</summary>
    private void FabricationBayOnly()
    {
        Goal("1b a fabrication bay in the station (optional goal)");
        if (_station >= 0) PlaceModule(_station, S1.X, S1.Y, S1.Z, ModuleKind.Workshop);
    }

    // ---- docking helpers (levels 02 and 06) ---------------------------------------------------------------

    /// <summary>Flies the ship so that its west cockpit port lies a few cells from the station core's east port, then docks (what the assisted approach does for a player).</summary>
    private bool DockAtStation()
    {
        int st = Net.Structures().First(s => s.Kind == StructureKind.Station && s.Owner == 0).Id;
        var sp = Net.PortWorldPoint(st, 0, 0, 0, Dir.PosX);
        var shp = Net.PortWorldPoint(_ship, 0, 0, 0, Dir.NegX);
        var p = Net.GetPlayerInfo(0);
        if (!FlyTo(p.X + (sp.X - shp.X) + 3L * Cell, p.Y + (sp.Y - shp.Y), p.Z + (sp.Z - shp.Z), Cell / 2)) return false;
        return !Failed(Cmd(new DockAt(st, 0, 0, 0, Dir.PosX)), "DockAt");
    }

    /// <summary>Docks, moves <paramref name="count"/> of an item (or all of it when 0) from the hold into the station stock.</summary>
    private void DockAndStore(string item, int count)
    {
        Goal("dock at the station and unload " + item);
        if (count > 0 && !WaitUntil(() => Net.HoldCount(0, Item(item)) >= count, 240)) { Stuck($"the hold never held {count} of {item}"); return; }
        if (!DockAtStation()) return;
        Milestone("Docked at the station");
        Failed(Cmd(new TransferItems(Item(item), count, true)), "TransferItems");
        Milestone("Items stored in the station stock");
        Wait(1); // let the goals see the docked ship before it leaves
        Cmd(new Undock());
    }

    // ---- 03 under siege ---------------------------------------------------------------------------------

    private int SegmentAt(int structure, int x, int y, int z)
    {
        for (int id = 0; id < Net.SegmentIdLimit; id++)
        {
            if (!Net.IsAlive(id)) continue;
            var s = Net.GetSegmentInfo(id);
            if (s.Structure == structure && s.OriginX == x && s.OriginY == y && s.OriginZ == z) return id;
        }
        return -1;
    }

    private void PlaySiege()
    {
        _station = Net.Structures().First(s => s.Kind == StructureKind.Station && s.Owner == 0).Id;
        Goal("siege: turrets on the pre-built ammo belts");
        // The template's two ammo belts run along the floor into the defence module beside the workshop; a turret at each end takes the belt.
        foreach (var (x, z, len) in new[] { (3, 11, 9), (2, 12, 10) })
        {
            int belt = SegmentAt(_station, x, 0, z);
            if (belt < 0) { Stuck($"the template ammo belt at ({x},0,{z}) is missing"); continue; }
            int turret = PlaceMachine(_station, x + len, 0, z, "core:turret");
            if (turret >= 0) Failed(Cmd(new ConnectToMachine(belt, turret)), "ConnectToMachine");
        }
        Cmd(new SetWeapons(true));
        var (dx, dy, dz) = Origin(1, 0, 1);
        Goal("siege: walls to draw the fire");
        Walls(_station, dx, dy, dz, 2);
        Goal("siege: a second ammo battery with two turrets");
        _stationBatteries += AmmoBattery(_station, 0, 0, 8, 5).Count > 0 ? 1 : 0;
        Goal("siege: a repair bay");
        RepairBay(_station, 0, 0, 8, 2);
        Goal("siege: hold the line");
    }
    // ---- 06 salvage run and 07 hull breach ----------------------------------------------------------------

    private void PlaySalvageRun()
    {
        Goal("salvage run: fly to the wreck, strip it, dock and unload the loot");
        int wreck = -1;
        foreach (int i in Net.Galaxy!.ObstaclesOf(Net.CurrentSystem(0)))
            if (_c.World!.Kinds[Net.GetObstacle(i).Kind].Behaviour == ObstacleBehaviour.Wreck) { wreck = i; break; }
        if (wreck < 0) { Stuck("no wreck in the home system"); return; }
        var o = Net.GetObstacle(wreck);
        // Stop beside the wreck, well inside the salvage reach (its radius and 12 cells), and wait for the strip to finish.
        if (!FlyTo(o.X, o.Y, o.Z, o.Radius + 5L * Cell)) return;
        if (!WaitUntil(() => Net.GetObstacle(wreck).Gone, 10)) { Stuck("the wreck was not salvaged"); return; }
        Milestone("Wreck salvaged");
        DockAndStore("core:resonant_crystal", 0);
    }

    private void PlayHullBreach()
    {
        Goal("hull breach: guns armed, then hold out");
        Cmd(new SetWeapons(true));
    }
}
