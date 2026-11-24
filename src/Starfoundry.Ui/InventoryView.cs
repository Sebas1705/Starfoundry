using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The filter chips of the inventory panel (ADR 0035): what an item is, as far as sorting through a full bag goes.</summary>
public enum ItemCategory { All, Ore, Parts, Fuel, Ammo }

/// <summary>What the panel shows about one slot: where it is, what is in it and how much a stack of that item holds.</summary>
public readonly record struct SlotView(ContainerRef Container, int Slot, int Item, int Count, int StackSize)
{
    public bool Empty => Count <= 0 || Item < 0;
}

/// <summary>
/// Engine-free logic of the inventory panel (ADR 0035): the category of an item, which move a drag, a drop or a shift click means, the
/// padding of a container's slot grid and the text of an item's tooltip. The Godot panel only draws and forwards what these return.
/// </summary>
public static class InventoryView
{
    /// <summary>The chip order of the filter row.</summary>
    public static readonly ItemCategory[] Categories = { ItemCategory.All, ItemCategory.Ore, ItemCategory.Parts, ItemCategory.Fuel, ItemCategory.Ammo };

    /// <summary>The category of an item by its content id: raw materials are ore, the reactor fuels fuel, ammunition ammo and everything made is a part.</summary>
    public static ItemCategory CategoryOf(string itemId)
    {
        string id = itemId.Contains(':') ? itemId[(itemId.IndexOf(':') + 1)..] : itemId;
        if (id is "hydrogen" or "fuel_cell") return ItemCategory.Fuel;
        if (id == "ammo") return ItemCategory.Ammo;
        if (id.EndsWith("_ore") || id is "ice" or "carbon" or "quartz" or "biomass" or "resonant_crystal") return ItemCategory.Ore;
        return ItemCategory.Parts;
    }

    /// <summary>True when an item of <paramref name="itemId"/> shows under the filter (everything shows under All).</summary>
    public static bool Matches(ItemCategory filter, string itemId) => filter == ItemCategory.All || CategoryOf(itemId) == filter;

    /// <summary>The Loc key of a category chip.</summary>
    public static string CategoryKey(ItemCategory c) => "inv.filter." + c.ToString().ToLowerInvariant();

    /// <summary>The icon id of a category chip.</summary>
    public static string CategoryIcon(ItemCategory c) => c switch
    {
        ItemCategory.Ore => "tool:invOre", ItemCategory.Parts => "tool:invParts", ItemCategory.Fuel => "tool:invFuel", ItemCategory.Ammo => "tool:invAmmo", _ => "tool:invAll",
    };

    /// <summary>The larger half of a stack (a right drag takes it): 1 of 1, 3 of 5, 10 of 20.</summary>
    public static int HalfOf(int count) => Math.Max(1, (count + 1) / 2);

    /// <summary>
    /// The move a shift click makes: from the person to the container beside it (the reactor's one slot, the hold, a stock, a machine's input) or, from any other
    /// container, into the person's bag. Without a container beside the bag the hold is the target. The count is zero: as many as the target takes.
    /// </summary>
    public static MoveItems QuickMove(ContainerRef from, int fromSlot, ContainerRef? other)
    {
        if (from.Kind != ContainerKind.Person) return new MoveItems(from, fromSlot, ContainerRef.Person, InventoryRules.AnySlot, 0);
        var to = other is { Kind: not ContainerKind.Person and not ContainerKind.Void and not ContainerKind.MachineOut } o ? o : ContainerRef.Hold;
        return new MoveItems(from, fromSlot, to, TargetSlot(to, InventoryRules.AnySlot), 0);
    }

    /// <summary>The slot a move into a container names: a reactor has its one slot, a person's bag the slot dropped on, every other container takes the item anywhere.</summary>
    public static int TargetSlot(ContainerRef to, int hoveredSlot) => to.Kind switch
    {
        ContainerKind.Reactor => InventoryRules.ReactorSlot,
        ContainerKind.Person => hoveredSlot,
        _ => InventoryRules.AnySlot,
    };

    /// <summary>The move of a drag that ends on a slot: the whole stack (a left drag) or its larger half (a right drag).</summary>
    public static MoveItems Drop(ContainerRef from, int fromSlot, int stackCount, ContainerRef to, int hoveredSlot, bool half) =>
        new(from, fromSlot, to, TargetSlot(to, hoveredSlot), half && stackCount > 1 ? HalfOf(stackCount) : 0);

    /// <summary>True when a drag from one container to another could ever work, so the panel can refuse early with a hint (nothing goes into an output buffer or out of the bin).</summary>
    public static bool CanDrop(ContainerRef from, ContainerRef to) =>
        from.Kind != ContainerKind.Void && to.Kind != ContainerKind.MachineOut && !(from == to && from.Kind != ContainerKind.Person);

    /// <summary>How many slots a container grid shows: its stacks padded with empty ones to whole rows, and at least <paramref name="minRows"/> rows.</summary>
    public static int PaddedSlots(int stacks, int columns, int minRows)
    {
        int rows = Math.Max(minRows, (stacks + columns) / columns); // always at least one empty slot to drop into
        return rows * columns;
    }

    // ---- text ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The tooltip of an item: its name, how much one slot holds, what kind of item it is and what it is for: fuel (how long one burns), ammunition, and what
    /// it is made into. <paramref name="count"/> is the stack under the pointer (0 for none), <paramref name="burnIntervalTicks"/> how long one hydrogen burns.
    /// </summary>
    public static string Tooltip(ContentDb content, int item, int count, int stackSize, int burnIntervalTicks)
    {
        string id = content.Items[item].Id;
        var sb = new StringBuilder();
        sb.Append(Capitalise(Loc.Name("item", id)));
        if (count > 0) sb.Append("  x").Append(count.ToString(Loc.Culture));
        sb.Append('\n').Append(Loc.T("inv.tip.stack", stackSize.ToString(Loc.Culture), Loc.T(CategoryKey(CategoryOf(id)))));
        if (item == content.FuelItem) sb.Append('\n').Append(Loc.T("inv.tip.fuel", PowerFormat.Clock(burnIntervalTicks)));
        else if (item == content.FuelCellItem) sb.Append('\n').Append(Loc.T("inv.tip.fuel", PowerFormat.Clock((long)burnIntervalTicks * PowerRules.FuelCellBurnIntervals)));
        var uses = UsedFor(content, item, 4);
        if (uses.Count > 0) sb.Append('\n').Append(Loc.T("inv.tip.usedFor", string.Join(", ", uses)));
        var machines = UsedBy(content, item);
        if (machines.Count > 0) sb.Append('\n').Append(Loc.T("inv.tip.usedBy", string.Join(", ", machines)));
        return sb.ToString();
    }

    /// <summary>Names of what an item is made into: the outputs of the recipes that consume it, without repeats, at most <paramref name="max"/> (then an ellipsis).</summary>
    public static List<string> UsedFor(ContentDb content, int item, int max)
    {
        var names = new List<string>();
        foreach (var r in content.Recipes)
        {
            if (!r.Inputs.Any(s => s.Item == item)) continue;
            foreach (var o in r.Outputs)
            {
                string name = Loc.Name("item", content.Items[o.Item].Id);
                if (!names.Contains(name)) names.Add(name);
            }
        }
        if (names.Count > max) { names = names.Take(max).ToList(); names.Add("..."); }
        return names;
    }

    /// <summary>Names of the machines that use the item as ammunition or repair material.</summary>
    public static List<string> UsedBy(ContentDb content, int item)
    {
        var names = new List<string>();
        foreach (var m in content.Machines)
            if ((m.Turret is { } t && t.AmmoItem == item) || (m.Repairer is { } r && r.Item == item)) names.Add(Loc.Name("machine", m.Id));
        return names;
    }

    /// <summary>The line under the fuel slot: how long the reactor keeps burning, as <c>m:ss</c>, or the empty-slot hint.</summary>
    public static string BurnLine(ReactorFuelInfo info) =>
        info.TotalTicksLeft <= 0 ? Loc.T("inv.reactor.cold") : Loc.T("inv.reactor.burn", PowerFormat.Clock(info.TotalTicksLeft));

    /// <summary>The Loc key of a feed source.</summary>
    public static string FeedKey(FeedSource source) => "inv.feed." + source.ToString().ToLowerInvariant();

    /// <summary>The order the feed selector lists its choices in.</summary>
    public static readonly FeedSource[] FeedChoices = { FeedSource.None, FeedSource.Hold, FeedSource.Inventory, FeedSource.HoldThenInventory };

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
