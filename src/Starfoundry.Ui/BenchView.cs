using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>What the ship workbench is doing, for the panel's status line.</summary>
public enum BenchState { Idle, Crafting, WaitingInputs, NoRoom }

/// <summary>One recipe row of the workbench panel: the recipe, what it takes and makes, how long a craft lasts and whether the hold can start one now.</summary>
public sealed record BenchRecipeRow(int Recipe, string Id, ItemStack[] Inputs, ItemStack[] Outputs, int Seconds, bool CanStart);

/// <summary>
/// The logic of the workbench panel (ADR 0039): the recipes it offers, the queue limits, the progress of the craft in progress, the status and which command
/// a button sends (a station-first world queues crafts; a legacy world keeps the old repeating recipe). Pure, over the content and numbers the game reads.
/// </summary>
public static class BenchView
{
    /// <summary>The workbench recipes in panel order: ingots from ore, plates from ingots, ammo from ingots.</summary>
    public static readonly string[] RecipeIds = { "core:ship_smelt", "core:ship_press", "core:ship_ammo" };

    /// <summary>The one-click quantities of the queue buttons.</summary>
    public static readonly int[] QuickCounts = { 1, PlayerRules.BenchQueueMax };

    /// <summary>Ticks of one craft: the recipe time times the manual multiplier of the world kind.</summary>
    public static int CraftTicks(RecipeDef r, bool stationFirst) => r.TimeTicks * (stationFirst ? PlayerRules.BenchCraftMultiplier : PlayerRules.ShipCraftMultiplier);

    /// <summary>Whole seconds of one craft, rounded up.</summary>
    public static int CraftSeconds(RecipeDef r, bool stationFirst) => (CraftTicks(r, stationFirst) + Units.TicksPerSecond - 1) / Units.TicksPerSecond;

    /// <summary>The recipes of the panel that exist in the content, with what the hold can start.</summary>
    public static IReadOnlyList<BenchRecipeRow> Rows(ContentDb content, bool stationFirst, Func<int, int> holdCount)
    {
        var rows = new List<BenchRecipeRow>();
        foreach (var id in RecipeIds)
        {
            var r = content.Recipes.FirstOrDefault(x => x.Id == id);
            if (r is null) continue;
            int index = r.Index;
            rows.Add(new BenchRecipeRow(index, id, r.Inputs, r.Outputs, CraftSeconds(r, stationFirst), r.Inputs.All(i => holdCount(i.Item) >= i.Count)));
        }
        return rows;
    }

    /// <summary>How many more crafts fit in the queue.</summary>
    public static int Room(int queued) => Math.Max(0, PlayerRules.BenchQueueMax - queued);

    /// <summary>How many of <paramref name="want"/> crafts the queue takes (0 when full).</summary>
    public static int Addable(int queued, int want) => Math.Clamp(want, 0, Room(queued));

    /// <summary>Progress of the craft in progress, 0 to 1.</summary>
    public static float Progress(bool crafting, int remainingTicks, int totalTicks) => WorkbenchSpot.Progress(crafting, remainingTicks, totalTicks);

    /// <summary>The status: crafting, idle (nothing waiting), or stuck on the inputs or on room in the hold for the output.</summary>
    public static BenchState State(bool crafting, int queued, bool hasInputs, bool holdHasRoom)
    {
        if (crafting) return BenchState.Crafting;
        if (queued == 0) return BenchState.Idle;
        return !hasInputs ? BenchState.WaitingInputs : !holdHasRoom ? BenchState.NoRoom : BenchState.Crafting;
    }

    /// <summary>Loc key of a status line.</summary>
    public static string StateKey(BenchState s) => s switch
    {
        BenchState.Crafting => "bench.panel.crafting",
        BenchState.WaitingInputs => "bench.card.waiting",
        BenchState.NoRoom => "bench.card.noRoom",
        _ => "bench.panel.idle",
    };

    /// <summary>The command a "queue" button sends: a station-first world queues crafts, a legacy world switches the repeating recipe on.</summary>
    public static Command QueueCommand(bool stationFirst, int recipe, int count) =>
        stationFirst ? new QueueBench(recipe, Math.Clamp(count, 1, PlayerRules.BenchQueueMax)) : new SetShipRecipe(recipe);

    /// <summary>The command the clear button sends: cancels the craft and empties the queue (a legacy world just switches the recipe off).</summary>
    public static Command ClearCommand(bool stationFirst) => stationFirst ? new ClearBench() : new SetShipRecipe(-1);

    /// <summary>Whether F opens the panel (station-first) or still cycles the recipes (legacy worlds).</summary>
    public static bool UsesPanel(bool stationFirst) => stationFirst;

    /// <summary>The "manual, slow" label with the seconds of a craft ("Manual, slow: 8 s per craft").</summary>
    public static string SlowLabel(int seconds) => Loc.T("bench.panel.slow", seconds);
}
