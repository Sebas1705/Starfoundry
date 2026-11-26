using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The tools of the floor plan: pick a part, put the chosen machine down, draw a belt, erase.</summary>
public enum PlanTool { Select, Machine, Belt, Erase }

/// <summary>
/// What a draft interior changes against the station's, classified the way the simulation does (ADR 0044): a machine stays when its cell and type are the same (a recipe change
/// is not a rebuild), a belt when its cell, direction, length and tier are the same; the rest is built (full price) or removed (half back). Engine-free; a test pins that the
/// price equals <see cref="DesignReport"/>'s.
/// </summary>
public sealed class InteriorDiff
{
    public static InteriorDiff Empty { get; } = new(Array.Empty<BlueprintMachine>(), Array.Empty<BlueprintMachine>(), Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintBelt>());

    public IReadOnlyList<BlueprintMachine> AddedMachines { get; }
    public IReadOnlyList<BlueprintMachine> RemovedMachines { get; }
    public IReadOnlyList<BlueprintBelt> AddedBelts { get; }
    public IReadOnlyList<BlueprintBelt> RemovedBelts { get; }

    private InteriorDiff(IReadOnlyList<BlueprintMachine> am, IReadOnlyList<BlueprintMachine> rm, IReadOnlyList<BlueprintBelt> ab, IReadOnlyList<BlueprintBelt> rb)
    { AddedMachines = am; RemovedMachines = rm; AddedBelts = ab; RemovedBelts = rb; }

    public int Added => AddedMachines.Count + AddedBelts.Count;
    public int Removed => RemovedMachines.Count + RemovedBelts.Count;
    public bool IsEmpty => Added + Removed == 0;

    public static InteriorDiff Compute(BlueprintInterior original, BlueprintInterior draft)
    {
        var om = original.Machines.ToDictionary(m => (m.X, m.Y, m.Z, m.Type));
        var dm = draft.Machines.ToDictionary(m => (m.X, m.Y, m.Z, m.Type));
        var ob = new HashSet<BlueprintBelt>(original.Belts);
        var db = new HashSet<BlueprintBelt>(draft.Belts);
        return new InteriorDiff(
            draft.Machines.Where(m => !om.ContainsKey((m.X, m.Y, m.Z, m.Type))).ToList(),
            original.Machines.Where(m => !dm.ContainsKey((m.X, m.Y, m.Z, m.Type))).ToList(),
            draft.Belts.Where(b => !ob.Contains(b)).ToList(),
            original.Belts.Where(b => !db.Contains(b)).ToList());
    }

    /// <summary>The state a machine cell is drawn in: added (green), removed (red) or neither.</summary>
    public GhostState MachineState((int X, int Y, int Z) cell) =>
        AddedMachines.Any(m => (m.X, m.Y, m.Z) == cell) ? GhostState.Added : RemovedMachines.Any(m => (m.X, m.Y, m.Z) == cell) ? GhostState.Removed : GhostState.Unchanged;

    /// <summary>The state a belt cell is drawn in.</summary>
    public GhostState BeltState((int X, int Y, int Z) cell) =>
        AddedBelts.Any(b => InteriorDraft.BeltCells(b).Contains(cell)) ? GhostState.Added : RemovedBelts.Any(b => InteriorDraft.BeltCells(b).Contains(cell)) ? GhostState.Removed : GhostState.Unchanged;

    /// <summary>What the change costs and gives back by item: the full price of what is built, half of what is removed (belts by their whole length).</summary>
    public (SortedDictionary<int, int> Price, SortedDictionary<int, int> Refund) Amounts(ContentDb content)
    {
        var price = new SortedDictionary<int, int>();
        var refund = new SortedDictionary<int, int>();
        foreach (var m in AddedMachines)
            foreach (var c in content.Machines[m.Type].Cost) price[c.Item] = price.GetValueOrDefault(c.Item) + c.Count;
        foreach (var m in RemovedMachines)
            foreach (var c in content.Machines[m.Type].Cost) refund[c.Item] = refund.GetValueOrDefault(c.Item) + c.Count / 2;
        foreach (var b in AddedBelts)
            foreach (var c in content.Belts[(int)b.Tier]?.CostPerCell ?? Array.Empty<ItemStack>()) price[c.Item] = price.GetValueOrDefault(c.Item) + c.Count * b.Length;
        foreach (var b in RemovedBelts)
            foreach (var c in content.Belts[(int)b.Tier]?.CostPerCell ?? Array.Empty<ItemStack>()) refund[c.Item] = refund.GetValueOrDefault(c.Item) + c.Count * b.Length / 2;
        return (price, refund);
    }
}
