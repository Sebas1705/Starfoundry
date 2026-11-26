namespace Starfoundry.Sim;

/// <summary>
/// Drops items from the commanding player's inventory slot (<see cref="ContainerKind.Person"/>) or hold (<see cref="ContainerKind.Hold"/>, the slot is the item index)
/// onto the ground at a point in world units (ADR 0038). The client sends where the player stands (aboard) or in front of the ship (seated); the point must be
/// within 40 cells of the ship. A count of zero or less drops the whole slot. Rejected with <see cref="Reject.NotAllowedHere"/> in a world with
/// <see cref="WorldRules.LegacyInventory"/>, <see cref="Reject.NotEnoughItems"/> for a count above the slot, <see cref="Reject.NoRoom"/> when 64 stacks already lie about.
/// </summary>
public sealed record DropItems(ContainerRef From, int Slot, int Count, int X, int Y, int Z) : Command;

/// <summary>
/// Picks up a stack (ore pickups too) by its id (<see cref="BeltNetwork.Pickups"/>), into the personal inventory and then the hold; what fits is taken and the rest stays.
/// On foot (<paramref name="Seated"/> false) the stack must be within three cells of the point given (where the player stands, within 40 cells of the ship); seated, within
/// the tractor range of the ship's nearest module. <see cref="Reject.NoRoom"/> when neither takes anything.
/// </summary>
public sealed record PickUp(int Entity, bool Seated, int X, int Y, int Z) : Command;
