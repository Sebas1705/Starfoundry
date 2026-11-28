using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>What a station machine is doing, for the machine panel.</summary>
public enum MachineRun { NoRecipe, NoPower, Running, WaitingInput, OutputFull }

/// <summary>
/// The logic of the machine panel (ADR 0039): the status of a crafting machine from what the simulation shows, its throughput per minute
/// and the recipe an enable button restores. Pure, tested without the engine.
/// </summary>
public static class MachineView
{
    /// <summary>The status of a crafting machine. No recipe first (it is switched off), then power (a machine without power does not work), working, a full output and finally waiting for inputs.</summary>
    public static MachineRun Classify(bool hasRecipe, bool needsPower, int satisfactionPermille, bool crafting, int outputItems)
    {
        if (!hasRecipe) return MachineRun.NoRecipe;
        if (needsPower && satisfactionPermille <= 0) return MachineRun.NoPower;
        if (crafting) return MachineRun.Running;
        return outputItems > 0 ? MachineRun.OutputFull : MachineRun.WaitingInput;
    }

    /// <summary>Loc key of a status line.</summary>
    public static string StatusKey(MachineRun r) => r switch
    {
        MachineRun.NoPower => "machine.panel.noPower",
        MachineRun.Running => "machine.panel.running",
        MachineRun.WaitingInput => "machine.panel.waitingInput",
        MachineRun.OutputFull => "machine.panel.outputFull",
        _ => "machine.panel.off",
    };

    /// <summary>Items per minute of a recipe's output at full power, working without a pause: count * 60 s / time. 0 for a recipe with no time.</summary>
    public static double NominalPerMinute(int outputCount, int timeTicks) =>
        timeTicks <= 0 ? 0 : outputCount * 60.0 * Units.TicksPerSecond / timeTicks;

    /// <summary>Items per minute right now: the nominal rate scaled by the power satisfaction while the machine runs, 0 in any other state.</summary>
    public static double CurrentPerMinute(MachineRun run, int outputCount, int timeTicks, int satisfactionPermille) =>
        run == MachineRun.Running ? NominalPerMinute(outputCount, timeTicks) * Math.Clamp(satisfactionPermille, 0, PowerRules.FullSatisfaction) / PowerRules.FullSatisfaction : 0;

    /// <summary>The mass a recipe turns out per second at full power, in kilograms, from its outputs (content id and count) and its time (ADR 0052).</summary>
    public static double OutputKgPerSecond(IEnumerable<(string ItemId, int Count)> outputs, int timeTicks) =>
        timeTicks <= 0 ? 0 : outputs.Sum(o => Physical.Kg(o.ItemId, o.Count)) * Units.TicksPerSecond / timeTicks;

    /// <summary>The "N per minute" number, one decimal below ten and none above.</summary>
    public static string PerMinuteText(double perMinute) =>
        perMinute >= 10 ? Math.Round(perMinute).ToString("0", Loc.Culture) : perMinute.ToString("0.#", Loc.Culture);

    /// <summary>Progress of the craft in progress, 0 to 1.</summary>
    public static float Progress(bool crafting, int remainingTicks, int totalTicks) => WorkbenchSpot.Progress(crafting, remainingTicks, totalTicks);

    /// <summary>The recipe the enable button restores: the one remembered when the machine was switched off if it is still offered, otherwise the first offered.</summary>
    public static int RestoreRecipe(int remembered, IReadOnlyList<int> offered) =>
        offered.Count == 0 ? -1 : remembered >= 0 && offered.Contains(remembered) ? remembered : offered[0];

    /// <summary>The recipe to remember when switching off: the current one, or the one already remembered when the machine has none.</summary>
    public static int Remember(int current, int remembered) => current >= 0 ? current : remembered;
}
