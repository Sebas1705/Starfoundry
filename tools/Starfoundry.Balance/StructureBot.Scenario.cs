using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Balance;

/// <summary>
/// Story levels ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)): the bot plays the stated goals of levels 01, 02 and 03 with the same
/// commands a player uses. Levels 04 and 05 have no plan; they are validated by reachability analysis in the content tests.
/// </summary>
public sealed partial class StructureBot
{
    /// <summary>Scenario ids this bot can play.</summary>
    public static readonly string[] PlayableScenarios = { "01_first_light", "02_cold_fuel", "03_under_siege" };

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
                break;
            case "02_cold_fuel":
                PlayColdFuel();
                break;
            case "03_under_siege":
                PlaySiege();
                break;
            default:
                throw new NotSupportedException($"The bot has no plan for scenario '{_scenario.Id}' (levels 04 and 05 are validated by reachability analysis only).");
        }
    }

    // ---- 02 cold fuel -----------------------------------------------------------------------------------

    private void PlayColdFuel()
    {
        HydrogenLoop();
        MineIron();
        FoundStation();
        MoreCargo(2);
        FetchCarbon(8, "the reactor module");
        Goal("a reactor module on the station");
        if (_station >= 0) PlaceModule(_station, 0, -1, 0, ModuleKind.Reactor);
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
}
