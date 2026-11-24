namespace Starfoundry.Sim;

/// <summary>Fixed rules of the personal inventory and the container moves (ADR 0035). Part of the simulation, so changing them changes states.</summary>
public static class InventoryRules
{
    /// <summary>Slots of the person's inventory: a grid of 24 (6 by 4 on screen).</summary>
    public const int Slots = 24;

    /// <summary>Items one slot holds when the content gives the item no <c>stack_size</c> (ores, ingots and plates).</summary>
    public const int DefaultStackSize = 100;

    /// <summary>The largest <c>stack_size</c> the content may set.</summary>
    public const int MaxStackSize = 9999;

    /// <summary>The player reaches a station, a machine or a reactor that is not part of the ship they fly, and that the ship is not docked at, within this many cells of the ship.</summary>
    public const int ReachCells = 12;

    /// <summary>The slot index of a reactor's one fuel slot.</summary>
    public const int ReactorSlot = 0;

    /// <summary>"Any slot" as the target of a move: the first stack of the same item with room, then the first empty slot.</summary>
    public const int AnySlot = -1;
}

/// <summary>The places items live (ADR 0035). What a slot number means depends on the kind.</summary>
public enum ContainerKind : byte
{
    /// <summary>The commanding player's own inventory: slots 0 to <see cref="InventoryRules.Slots"/> - 1, each one stack of one item.</summary>
    Person = 0,

    /// <summary>The commanding player's ship hold. It has no slots: a slot number is an item index (and as a target, <see cref="InventoryRules.AnySlot"/> or the item's own index).</summary>
    Hold = 1,

    /// <summary>The stock of a station (<see cref="ContainerRef.Id"/>): a slot number is an item index.</summary>
    Stock = 2,

    /// <summary>The fuel slot of a reactor module (<see cref="ContainerRef.Id"/> is the structure, <see cref="ContainerRef.Mx"/> to <see cref="ContainerRef.Mz"/> the module): one slot, holding fuel cells or hydrogen.</summary>
    Reactor = 3,

    /// <summary>The input buffer of a machine (<see cref="ContainerRef.Id"/>): a slot number is an item index; it takes what its recipe, turret or repairer uses, up to the cap the belts fill it to.</summary>
    MachineIn = 4,

    /// <summary>The output buffer of a machine (<see cref="ContainerRef.Id"/>): a slot number is an item index; items can only be taken from it.</summary>
    MachineOut = 5,

    /// <summary>The bin: a target that takes everything and gives nothing back. Items moved here are gone.</summary>
    Void = 6,
}

/// <summary>A container as a command names it: a kind, an id and, for a reactor, the module coordinate.</summary>
public readonly record struct ContainerRef(ContainerKind Kind, int Id = 0, int Mx = 0, int My = 0, int Mz = 0)
{
    public static ContainerRef Person => new(ContainerKind.Person);
    public static ContainerRef Hold => new(ContainerKind.Hold);
    public static ContainerRef Void => new(ContainerKind.Void);
    public static ContainerRef Stock(int station) => new(ContainerKind.Stock, station);
    public static ContainerRef Reactor(int structure, int mx, int my, int mz) => new(ContainerKind.Reactor, structure, mx, my, mz);
    public static ContainerRef MachineIn(int machine) => new(ContainerKind.MachineIn, machine);
    public static ContainerRef MachineOut(int machine) => new(ContainerKind.MachineOut, machine);

    /// <summary>True when the kind is one of the defined ones.</summary>
    public bool IsDefined => Enum.IsDefined(Kind);

    /// <summary>True for the kinds whose slots are item indexes (the hold, a stock and the machine buffers).</summary>
    public bool SlotIsItem => Kind is ContainerKind.Hold or ContainerKind.Stock or ContainerKind.MachineIn or ContainerKind.MachineOut;
}

/// <summary>Where a reactor gets fuel when its own fuel slot is empty (ADR 0035). The default, <see cref="Hold"/>, is how reactors always behaved.</summary>
public enum FeedSource : byte
{
    /// <summary>The station stock of the structure first (a docked ship's reactors draw on its station), then the owner's hold: the behaviour before ADR 0035.</summary>
    Hold = 0,

    /// <summary>Nothing: the reactor burns only what is in its fuel slot.</summary>
    None = 1,

    /// <summary>The owner's personal inventory only.</summary>
    Inventory = 2,

    /// <summary>The hold side first (as <see cref="Hold"/>), then the owner's personal inventory.</summary>
    HoldThenInventory = 3,
}

/// <summary>
/// Moves items from one slot of one container to a slot of another (ADR 0035). <see cref="Count"/> is how many; zero or less means as many as the target takes.
/// A count above what the source slot holds is <see cref="Reject.NotEnoughItems"/>; above what the target takes, <see cref="Reject.NoRoom"/>.
/// A target slot of <see cref="InventoryRules.AnySlot"/> picks the slot: the stack of the same item with room, then the first empty one. Moving the whole stack of a
/// person slot onto another person slot that holds a different item swaps the two. Both containers must be within reach (aboard, docked or within
/// <see cref="InventoryRules.ReachCells"/>). Needs a world without <see cref="WorldRules.LegacyInventory"/>.
/// </summary>
public sealed record MoveItems(ContainerRef From, int FromSlot, ContainerRef To, int ToSlot, int Count) : Command;

/// <summary>Chooses where a reactor module of a structure of the player takes fuel from when its fuel slot is empty (ADR 0035).</summary>
public sealed record SetFeedSource(int Structure, int Mx, int My, int Mz, FeedSource Source) : Command;

/// <summary>Sorts the commanding player's inventory: stacks of one item are merged and the stacks are packed to the front ordered by item index (ADR 0035).</summary>
public sealed record SortInventory : Command;

/// <summary>What a reactor's fuel slot and burn look like (see <c>BeltNetwork.ReactorFuel</c>).</summary>
/// <param name="Item">The item in the slot, or -1 when it is empty.</param>
/// <param name="Count">How many of it.</param>
/// <param name="Capacity">How many of the item the slot takes (the item's stack size).</param>
/// <param name="BurnTicksLeft">Ticks left of the interval the reactor is burning now.</param>
/// <param name="TotalTicksLeft">Ticks the reactor keeps burning with what it holds: the current burn plus the slot.</param>
/// <param name="Feed">Where the reactor takes fuel from when the slot is empty.</param>
public readonly record struct ReactorFuelInfo(int Item, int Count, int Capacity, int BurnTicksLeft, int TotalTicksLeft, FeedSource Feed);
