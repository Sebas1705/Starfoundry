namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    private bool ActorNearShip(Player p, int x, int y, int z)
    {
        var (hx, hy, hz) = NearestHullPoint(p, x, y, z);
        long dx = hx - x, dy = hy - y, dz = hz - z, r = FeelRules.DropActorReachUnits;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>Drops items from the person's inventory or the hold onto the ground (ADR 0038).</summary>
    internal Reject DropItemsCore(int player, ContainerRef from, int slot, int count, int x, int y, int z)
    {
        if (Rules.LegacyInventory || Rules.LegacyFeel) return Reject.NotAllowedHere;
        if (Content is null) return Reject.NoContent;
        if (!_players.TryGetValue(player, out var p) || p.RespawnRemaining > 0 || p.ShipLost) return Reject.InvalidArgument;
        if (from.Kind is not (ContainerKind.Person or ContainerKind.Hold)) return Reject.InvalidArgument;
        long lim = PlayerRules.WorldLimitUnits * 2L;
        if (Math.Abs((long)x) > lim || Math.Abs((long)y) > lim || Math.Abs((long)z) > lim) return Reject.InvalidArgument;
        if (!SlotAddressOk(from, slot, target: false)) return Reject.InvalidArgument;
        if (!ActorNearShip(p, x, y, z)) return Reject.OutOfRange;
        if (!Peek(p, from, slot, out int item, out int have) || have <= 0) return Reject.NotEnoughItems;
        int n = count <= 0 ? have : count;
        if (n > have) return Reject.NotEnoughItems;

        OrePickup? target = null;
        long m = FeelRules.DropMergeUnits;
        foreach (var k in _pickups)
        {
            if (!k.Dropped || k.Item != item) continue;
            long dx = k.X - x, dy = k.Y - y, dz = k.Z - z;
            if (dx * dx + dy * dy + dz * dz <= m * m && (long)k.Count + n <= int.MaxValue / 2) { target = k; break; }
        }
        if (target is null && _pickups.Count >= FeelRules.MaxPickups) return Reject.NoRoom;

        Take(p, from, slot, item, n);
        if (target is not null) { target.Count += n; target.Life = FeelRules.DropLifeTicks; }
        else _pickups.Add(new OrePickup { Id = _nextPickupId++, Item = item, Count = n, Source = -1, Dropped = true, Life = FeelRules.DropLifeTicks, X = x, Y = y, Z = z });
        return Reject.None;
    }

    /// <summary>Picks a stack up by hand (ADR 0038): the person's inventory first, then the hold.</summary>
    internal Reject PickUpCore(int player, int entity, bool seated, int x, int y, int z)
    {
        if (Rules.LegacyInventory || Rules.LegacyFeel) return Reject.NotAllowedHere;
        if (Content is null) return Reject.NoContent;
        if (!_players.TryGetValue(player, out var p) || p.RespawnRemaining > 0 || p.ShipLost) return Reject.InvalidArgument;
        OrePickup? k = null;
        foreach (var q in _pickups) if (q.Id == entity) { k = q; break; }
        if (k is null) return Reject.InvalidArgument;

        if (seated)
        {
            var (hx, hy, hz) = NearestHullPoint(p, k.X, k.Y, k.Z);
            long dx = hx - k.X, dy = hy - k.Y, dz = hz - k.Z, r = FeelRules.TractorRangeUnits;
            if (dx * dx + dy * dy + dz * dz > r * r) return Reject.OutOfRange;
        }
        else
        {
            if (!ActorNearShip(p, x, y, z)) return Reject.OutOfRange;
            long dx = k.X - x, dy = k.Y - y, dz = k.Z - z, r = FeelRules.HandReachUnits;
            if (dx * dx + dy * dy + dz * dz > r * r) return Reject.OutOfRange;
        }

        int left = k.Count, taken = 0;
        int bag = Math.Max(0, RoomFor(p, ContainerRef.Person, InventoryRules.AnySlot, k.Item));
        int toBag = Math.Min(left, bag);
        if (toBag > 0) { Put(p, ContainerRef.Person, InventoryRules.AnySlot, k.Item, toBag); left -= toBag; taken += toBag; }
        if (left > 0)
        {
            int toHold = Math.Min(left, Math.Max(0, HoldCapacityOf(p) - p.HoldTotal - p.ShipReserved));
            if (toHold > 0) { AddToHold(p, k.Item, toHold); left -= toHold; taken += toHold; }
        }
        if (taken == 0) return Reject.NoRoom;
        k.Count = left;
        if (left == 0) _pickups.Remove(k);
        _pickupEvents.Add(new PickupEvent(PickupEventKind.Collected, k.Source, k.Item, taken));
        return Reject.None;
    }
}
