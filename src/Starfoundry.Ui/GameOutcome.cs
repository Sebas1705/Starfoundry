using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The goal of a game: survive this many waves with a station standing and, in structure worlds, visit another system. The player's default lives in <see cref="GameSettings"/>.</summary>
public sealed record GoalSettings(int WavesToWin = GoalSettings.DefaultWaves)
{
    public const int MinWaves = 3, MaxWaves = 30, DefaultWaves = 10;

    /// <summary>Jumps to complete in structure worlds: the player must reach a second system, which takes the jump drive research.</summary>
    public const int JumpsToWin = 1;

    /// <summary>Legacy: the goal from an old settings file (the wave count is clamped). Games use <see cref="From(WorldRules)"/> instead.</summary>
    public static GoalSettings From(GameSettings settings) => new(Math.Clamp(settings.GoalWaves, MinWaves, MaxWaves));

    /// <summary>The goal a world's rules ask for (<see cref="WorldRules.GoalWaves"/>).</summary>
    public static GoalSettings From(WorldRules rules) => new(rules.GoalWaves);
}

/// <summary>Where a game stands: still going, won, or lost.</summary>
public enum OutcomeKind { Playing, Won, Lost }

/// <summary>Why the game ended (<see cref="None"/> while it is going on). The last two come from scenarios: every primary goal done without waves among them, and a primary goal failed (a kept module lost, a time limit passed).</summary>
public enum OutcomeReason { None, WavesSurvived, ShipLost, GoalsDone, GoalFailed }

/// <summary>The numbers of the end screen.</summary>
/// <param name="PlaySeconds">Simulated play time.</param>
/// <param name="ModulesBuilt">Modules the player built: those standing now plus those lost, minus the starter ship's.</param>
/// <param name="StationsFounded">Stations standing now plus those lost.</param>
/// <param name="WavesSurvived">Waves that arrived and were cleared (the wave being fought does not count yet).</param>
/// <param name="MinedByHand">Items taken from deposits by hand.</param>
/// <param name="MinedByDrones">Items taken from deposits by drones.</param>
public sealed record OutcomeStats(double PlaySeconds, int ModulesBuilt, int StationsFounded, int StationsLost, int ModulesLost, int WavesSurvived,
    long MinedByHand, long MinedByDrones, long MachinesLost);

public sealed record OutcomeResult(OutcomeKind Kind, OutcomeReason Reason, OutcomeStats Stats);

/// <summary>
/// Decides whether a played game is won or lost and gathers the end-screen numbers. Lost: the ship's last cockpit fell (structure
/// worlds only: the older worlds rebuild the ship, so they never lose). Won: the goal's number of waves was cleared with the ship
/// alive and, in structure worlds, a station of the player standing and a jump completed (which needs the jump drive research). Pure logic over the simulation, tested without the engine.
/// </summary>
public static class GameOutcome
{
    /// <summary>Evaluates against the goal in the world's own rules (<see cref="BeltNetwork.Rules"/>).</summary>
    public static OutcomeResult Evaluate(BeltNetwork net, int player) => Evaluate(net, player, net.Rules);

    /// <summary>Evaluates against the goal waves of <paramref name="rules"/>.</summary>
    public static OutcomeResult Evaluate(BeltNetwork net, int player, WorldRules rules) => Evaluate(net, player, GoalSettings.From(rules));

    public static OutcomeResult Evaluate(BeltNetwork net, int player, GoalSettings goal)
    {
        var stats = Stats(net, player);
        if (net.StructureRules && net.ShipLost(player))
            return new OutcomeResult(OutcomeKind.Lost, OutcomeReason.ShipLost, stats);
        if (net.HasPlayer(player) && ShipAlive(net, player) && stats.WavesSurvived >= goal.WavesToWin && HasBase(net, player) && HasExplored(net))
            return new OutcomeResult(OutcomeKind.Won, OutcomeReason.WavesSurvived, stats);
        return new OutcomeResult(OutcomeKind.Playing, OutcomeReason.None, stats);
    }

    /// <summary>Waves cleared: all that arrived while nothing is left alive, otherwise one less (the one in progress).</summary>
    public static int WavesSurvived(BeltNetwork net) =>
        (int)Math.Clamp(net.EnemyCount == 0 ? net.WavesSpawned : net.WavesSpawned - 1, 0, int.MaxValue);

    /// <summary>True when the player owns a station (structure worlds); older worlds need no base.</summary>
    public static bool HasBase(BeltNetwork net, int player) =>
        !net.StructureRules || net.Structures().Any(s => s.Owner == player && s.Kind == StructureKind.Station);

    /// <summary>True when the world has been left at least <see cref="GoalSettings.JumpsToWin"/> time(s) by the jump drive (structure worlds); older worlds need no jump.</summary>
    public static bool HasExplored(BeltNetwork net) => !net.StructureRules || net.JumpsCompleted >= GoalSettings.JumpsToWin;

    /// <summary>Jumps completed, capped at the goal (for the goal line).</summary>
    public static int JumpsDone(BeltNetwork net) => Math.Min(net.JumpsCompleted, GoalSettings.JumpsToWin);

    private static bool ShipAlive(BeltNetwork net, int player) =>
        net.StructureRules ? !net.ShipLost(player) : net.GetPlayerInfo(player).RespawnRemaining == 0;

    public static OutcomeStats Stats(BeltNetwork net, int player)
    {
        int modules = 0, stations = 0;
        foreach (var s in net.Structures())
        {
            if (s.Owner != player) continue;
            modules += s.Modules.Count;
            if (s.Kind == StructureKind.Station) stations++;
        }
        int starter = net.StructureRules ? ObjectiveTracker.StarterModuleCount : 0;
        int built = (int)Math.Max(0, modules + net.ModulesLost - starter);
        return new OutcomeStats(net.Tick / (double)Units.TicksPerSecond, built, stations + (int)net.StationsLost, (int)net.StationsLost,
            (int)net.ModulesLost, WavesSurvived(net), net.MinedByHand, net.MinedByDrones, net.MachinesLost);
    }
}
