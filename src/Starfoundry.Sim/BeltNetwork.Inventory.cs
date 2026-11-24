namespace Starfoundry.Sim;

/// <summary>The personal inventory of a player: a lazily allocated array of stacks. Nothing is allocated, hashed or validated while it was never used (ADR 0035).</summary>
internal sealed partial class Player
{
    /// <summary>Item and count per slot (null until the first item arrives). An empty slot has a count of zero and an item of -1.</summary>
    internal int[]? InvItem, InvCount;

    internal bool HasInventory
    {
        get
        {
            if (InvCount is null) return false;
            foreach (int c in InvCount) if (c > 0) return true;
            return false;
        }
    }

    internal void EnsureInventory()
    {
        if (InvItem is not null) return;
        InvItem = new int[InventoryRules.Slots];
        InvCount = new int[InventoryRules.Slots];
        Array.Fill(InvItem, -1);
    }

    internal void CloneInventoryInto(Player copy)
    {
        if (InvItem is null || InvCount is null) return;
        copy.InvItem = (int[])InvItem.Clone();
        copy.InvCount = (int[])InvCount.Clone();
    }
}

/// <summary>The fuel in the slot of a reactor module.</summary>
internal readonly record struct FuelSlot(int Item, int Count);

public sealed partial class Structure
{
    /// <summary>Fuel in the fuel slot of each reactor module (only reactors with fuel have an entry). See ADR 0035.</summary>
    internal SortedDictionary<(int Mx, int My, int Mz), FuelSlot> ReactorFuel { get; private set; } = new();

    /// <summary>The feed source of each reactor module that is not the default <see cref="FeedSource.Hold"/>.</summary>
    internal SortedDictionary<(int Mx, int My, int Mz), FeedSource> FeedSources { get; private set; } = new();

    internal void CloneReactorFuelInto(Structure copy)
    {
        copy.ReactorFuel = new SortedDictionary<(int, int, int), FuelSlot>(ReactorFuel);
        copy.FeedSources = new SortedDictionary<(int, int, int), FeedSource>(FeedSources);
    }
}

public sealed partial class BeltNetwork
{
    /// <summary>Items a person put into the buffers of machines by hand (ADR 0035). Counted into the conservation law of the belts.</summary>
    public long HandIntoMachines { get; private set; }

    /// <summary>Items a person took out of the buffers of machines by hand.</summary>
    public long HandOutOfMachines { get; private set; }

    // ---- reading -------------------------------------------------------------------------------------

    /// <summary>The most of an item one inventory slot holds.</summary>
    public int StackSizeOf(int item) => Content?.StackSizeOf(item) ?? InventoryRules.DefaultStackSize;

    /// <summary>The stack in a slot of a player's personal inventory, or null when the slot is empty or does not exist.</summary>
    public ItemStack? InventoryStack(int player, int slot)
    {
        if (!_players.TryGetValue(player, out var p) || p.InvCount is null || (uint)slot >= (uint)p.InvCount.Length || p.InvCount[slot] <= 0) return null;
        return new ItemStack(p.InvItem![slot], p.InvCount[slot]);
    }

    /// <summary>Items of a kind in a player's personal inventory.</summary>
    public int InventoryCount(int player, int item)
    {
        if (!_players.TryGetValue(player, out var p) || p.InvCount is null) return 0;
        int n = 0;
        for (int i = 0; i < p.InvCount.Length; i++)
            if (p.InvCount[i] > 0 && p.InvItem![i] == item) n += p.InvCount[i];
        return n;
    }

    /// <summary>All the items in a player's personal inventory.</summary>
    public int InventoryTotal(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.InvCount is null) return 0;
        int n = 0;
        foreach (int c in p.InvCount) n += c;
        return n;
    }

    /// <summary>Slots of a player's personal inventory that hold something.</summary>
    public int InventoryUsedSlots(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.InvCount is null) return 0;
        int n = 0;
        foreach (int c in p.InvCount) if (c > 0) n++;
        return n;
    }

    /// <summary>The fuel slot, the burn left and the feed source of a reactor module; false when the structure has no reactor there.</summary>
    public bool TryGetReactorFuel(int structure, int mx, int my, int mz, out ReactorFuelInfo info)
    {
        info = default;
        if (!_structures.TryGetValue(structure, out var s) || !s.ModuleMap.TryGetValue((mx, my, mz), out var kind) || !ModuleKindIds.IsReactor(kind)) return false;
        s.ReactorFuel.TryGetValue((mx, my, mz), out var slot);
        s.BurnRemaining.TryGetValue((mx, my, mz), out int burn);
        int count = slot.Count, item = count > 0 ? slot.Item : -1;
        int total = burn + (int)Math.Min(int.MaxValue / 2, (long)count * FuelIntervals(item) * BurnInterval(kind));
        info = new ReactorFuelInfo(item, count, item >= 0 ? StackSizeOf(item) : 0, burn, total, s.FeedSources.TryGetValue((mx, my, mz), out var f) ? f : FeedSource.Hold);
        return true;
    }

    /// <summary>The feed source of a reactor module (the default <see cref="FeedSource.Hold"/> for one that has none set or is no reactor).</summary>
    public FeedSource FeedSourceOf(int structure, int mx, int my, int mz) =>
        _structures.TryGetValue(structure, out var s) && s.FeedSources.TryGetValue((mx, my, mz), out var f) ? f : FeedSource.Hold;

    /// <summary>Burn intervals one item of the fuel slot lasts: a fuel cell 6, hydrogen 1, anything else 0.</summary>
    private int FuelIntervals(int item) =>
        Content is null || item < 0 ? 0 : item == Content.FuelCellItem ? PowerRules.FuelCellBurnIntervals : item == Content.FuelItem ? 1 : 0;

    /// <summary>The most of an item a machine's input buffer takes: what its recipe uses (twice a craft's need), a turret's ammo cap or a repairer's item cap; 0 for anything else.</summary>
    public int MachineInputCapacity(int machine, int item)
    {
        if (Content is null || !IsMachineAlive(machine)) return 0;
        var m = _machines[machine]!;
        var def = Content.Machines[m.Type];
        if (m.Recipe != -1)
        {
            foreach (var s in Content.Recipes[m.Recipe].Inputs) if (s.Item == item) return 2 * s.Count;
            return 0;
        }
        if (def.Turret is { } t) return item == t.AmmoItem ? t.AmmoCap : 0;
        if (def.Repairer is { } r) return item == r.Item ? r.ItemCap : 0;
        return 0;
    }

    /// <summary>The stacks of a container as (slot, item, count) in slot order, for panels and tests. A person's slots are slot numbers; the other kinds list one entry per item present with the item index as the slot.</summary>
    public IReadOnlyList<(int Slot, int Item, int Count)> ContainerStacks(int player, ContainerRef c)
    {
        var list = new List<(int, int, int)>();
        if (!_players.TryGetValue(player, out var p) || Content is null) return list;
        switch (c.Kind)
        {
            case ContainerKind.Person:
                if (p.InvCount is not null)
                    for (int i = 0; i < p.InvCount.Length; i++) if (p.InvCount[i] > 0) list.Add((i, p.InvItem![i], p.InvCount[i]));
                break;
            case ContainerKind.Hold:
                for (int i = 0; i < p.Hold.Length; i++) if (p.Hold[i] > 0) list.Add((i, i, p.Hold[i]));
                break;
            case ContainerKind.Stock:
                if (_structures.TryGetValue(c.Id, out var st) && st.Stock is { } stock)
                    for (int i = 0; i < stock.Length; i++) if (stock[i] > 0) list.Add((i, i, stock[i]));
                break;
            case ContainerKind.Reactor:
                if (TryGetReactorFuel(c.Id, c.Mx, c.My, c.Mz, out var fuel) && fuel.Count > 0) list.Add((InventoryRules.ReactorSlot, fuel.Item, fuel.Count));
                break;
            case ContainerKind.MachineIn or ContainerKind.MachineOut:
                if (IsMachineAlive(c.Id))
                {
                    var buffer = c.Kind == ContainerKind.MachineIn ? _machines[c.Id]!.In : _machines[c.Id]!.Out;
                    for (int i = 0; i < buffer.Length; i++) if (buffer[i] > 0) list.Add((i, i, buffer[i]));
                }
                break;
        }
        return list;
    }

    // ---- reach -----------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the player can use a container now (ADR 0035): their own inventory and hold always (while the ship exists); a reactor, machine or station stock of
    /// theirs when it is in the ship they fly, in the station their ship is docked at, or within <see cref="InventoryRules.ReachCells"/> of the ship.
    /// Returns <see cref="Reject.None"/> or the reason.
    /// </summary>
    public Reject CheckReach(int player, ContainerRef c)
    {
        if (!_players.TryGetValue(player, out var p) || Content is null) return Reject.InvalidArgument;
        switch (c.Kind)
        {
            case ContainerKind.Person or ContainerKind.Void:
                return Reject.None;
            case ContainerKind.Hold:
                return p.ShipLost || p.RespawnRemaining > 0 ? Reject.OutOfRange : Reject.None;
            case ContainerKind.Stock:
            {
                if (!StructureRules) return Reject.NotAllowedHere;
                if (!_structures.TryGetValue(c.Id, out var st) || st.Kind != StructureKind.Station) return Reject.NoSuchStructure;
                if (st.Owner != player) return Reject.NotAllowedHere;
                return StructureReachable(p, st, null) ? Reject.None : Reject.OutOfRange;
            }
            case ContainerKind.Reactor:
            {
                if (!StructureRules) return Reject.NotAllowedHere;
                if (!_structures.TryGetValue(c.Id, out var st)) return Reject.NoSuchStructure;
                if (st.Owner != player) return Reject.NotAllowedHere;
                if (!st.ModuleMap.TryGetValue((c.Mx, c.My, c.Mz), out var kind) || !ModuleKindIds.IsReactor(kind)) return Reject.InvalidArgument;
                return StructureReachable(p, st, (c.Mx, c.My, c.Mz)) ? Reject.None : Reject.OutOfRange;
            }
            case ContainerKind.MachineIn or ContainerKind.MachineOut:
            {
                if (!IsMachineAlive(c.Id)) return Reject.UnknownMachine;
                var m = _machines[c.Id]!;
                if (m.Structure == 0) return CellInRange(player, m.X, m.Y, m.Z, InventoryRules.ReachCells) ? Reject.None : Reject.OutOfRange;
                if (!_structures.TryGetValue(m.Structure, out var st)) return Reject.NoSuchStructure;
                if (st.Owner != player) return Reject.NotAllowedHere;
                var (wx, wy, wz) = WorldCentre(m.Structure, m.X, m.Y, m.Z);
                return StructureReachable(p, st, null, (wx, wy, wz)) ? Reject.None : Reject.OutOfRange;
            }
            default:
                return Reject.InvalidArgument;
        }
    }

    /// <summary>The ship the player flies, the station it is docked at, or anything of the structure within reach of the ship.</summary>
    private bool StructureReachable(Player p, Structure s, (int Mx, int My, int Mz)? module, (long X, long Y, long Z)? point = null)
    {
        if (s.Id == p.ShipStructure) return true;
        if (p.ShipStructure >= 0 && _structures.TryGetValue(p.ShipStructure, out var ship) && ship.DockedTo == s.Id) return true;
        if (point is { } pt) return WorldPointInRange(p.Id, pt.X, pt.Y, pt.Z, InventoryRules.ReachCells);
        if (module is { } m) return ModuleInReach(p, s, m);
        foreach (var key in s.ModuleMap.Keys)
            if (ModuleInReach(p, s, key)) return true;
        return false;
    }

    private bool ModuleInReach(Player p, Structure s, (int Mx, int My, int Mz) key)
    {
        var (wx, wy, wz) = WorldCentre(s.Id, key.Mx * StationGrid.ModuleX + StationGrid.ModuleX / 2, key.My * StationGrid.ModuleY + StationGrid.ModuleY / 2, key.Mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2);
        return WorldPointInRange(p.Id, wx, wy, wz, InventoryRules.ReachCells);
    }

    // ---- the move ---------------------------------------------------------------------------------------

    /// <summary>
    /// Moves items between two containers. With <paramref name="clamp"/> a count above what the source holds or the target takes moves as many as possible (how
    /// <see cref="TransferItems"/> always behaved); without it such a count is rejected. Reach is the caller's business (the command checks it, a transfer
    /// between a docked ship and its station is in reach by definition). Returns the reject and how many items moved.
    /// </summary>
    internal (Reject Reject, int Moved) MoveCore(int player, ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int count, bool clamp)
    {
        if (!_players.TryGetValue(player, out var p) || Content is null) return (Reject.InvalidArgument, 0);
        if (!SlotAddressOk(from, fromSlot, target: false) || !SlotAddressOk(to, toSlot, target: true) || from.Kind == ContainerKind.Void) return (Reject.InvalidArgument, 0);
        bool samePerson = from.Kind == ContainerKind.Person && to.Kind == ContainerKind.Person;
        if (!samePerson && from == to) return (Reject.InvalidArgument, 0);
        if (samePerson && fromSlot == toSlot) return (Reject.InvalidArgument, 0);
        if (samePerson && toSlot == InventoryRules.AnySlot) return (Reject.InvalidArgument, 0);

        if (!Peek(p, from, fromSlot, out int item, out int have) || have <= 0) return (Reject.NotEnoughItems, 0);
        if (to.SlotIsItem && toSlot != InventoryRules.AnySlot && toSlot != item) return (Reject.InvalidArgument, 0);
        int want = count <= 0 ? have : count;
        if (!clamp && want > have) return (Reject.NotEnoughItems, 0);

        // Dropping a whole person stack on another person stack of a different item swaps the two.
        if (samePerson && Peek(p, to, toSlot, out int otherItem, out int otherCount) && otherCount > 0 && otherItem != item)
        {
            if (want < have) return (Reject.WrongItem, 0);
            p.InvItem![fromSlot] = otherItem; p.InvCount![fromSlot] = otherCount;
            p.InvItem[toSlot] = item; p.InvCount[toSlot] = have;
            return (Reject.None, have);
        }

        int room = RoomFor(p, to, toSlot, item);
        if (room < 0) return (Reject.WrongItem, 0);
        if (room == 0) return (Reject.NoRoom, 0);
        if (!clamp && count > 0 && want > room) return (Reject.NoRoom, 0);
        int n = Math.Min(Math.Min(want, have), room);
        if (n <= 0) return (Reject.NoRoom, 0);

        Take(p, from, fromSlot, item, n);
        Put(p, to, toSlot, item, n);
        return (Reject.None, n);
    }

    private bool SlotAddressOk(ContainerRef c, int slot, bool target)
    {
        if (!c.IsDefined) return false;
        switch (c.Kind)
        {
            case ContainerKind.Person: return (target && slot == InventoryRules.AnySlot) || (uint)slot < (uint)InventoryRules.Slots;
            case ContainerKind.Reactor: return slot == InventoryRules.ReactorSlot || (target && slot == InventoryRules.AnySlot);
            case ContainerKind.Void: return target;
            default: return (target && slot == InventoryRules.AnySlot) || (uint)slot < (uint)Content!.Items.Count;
        }
    }

    private bool Peek(Player p, ContainerRef c, int slot, out int item, out int count)
    {
        item = -1; count = 0;
        switch (c.Kind)
        {
            case ContainerKind.Person:
                if (p.InvCount is null || p.InvCount[slot] <= 0) return false;
                item = p.InvItem![slot]; count = p.InvCount[slot];
                return true;
            case ContainerKind.Hold:
                item = slot; count = p.Hold[slot];
                return true;
            case ContainerKind.Stock:
                item = slot; count = StockCount(c.Id, slot);
                return true;
            case ContainerKind.Reactor:
                if (!_structures.TryGetValue(c.Id, out var st) || !st.ReactorFuel.TryGetValue((c.Mx, c.My, c.Mz), out var fuel) || fuel.Count <= 0) return false;
                item = fuel.Item; count = fuel.Count;
                return true;
            case ContainerKind.MachineIn or ContainerKind.MachineOut:
                if (!IsMachineAlive(c.Id)) return false;
                item = slot; count = (c.Kind == ContainerKind.MachineIn ? _machines[c.Id]!.In : _machines[c.Id]!.Out)[slot];
                return true;
            default:
                return false;
        }
    }

    /// <summary>How many of an item the target takes at a slot; -1 when the item does not belong there.</summary>
    private int RoomFor(Player p, ContainerRef c, int slot, int item)
    {
        switch (c.Kind)
        {
            case ContainerKind.Person:
            {
                int size = StackSizeOf(item);
                if (slot != InventoryRules.AnySlot)
                {
                    if (p.InvCount is null || p.InvCount[slot] <= 0) return size;
                    return p.InvItem![slot] == item ? Math.Max(0, size - p.InvCount[slot]) : -1;
                }
                int room = 0;
                for (int i = 0; i < InventoryRules.Slots; i++)
                {
                    if (p.InvCount is null || p.InvCount[i] <= 0) room += size;
                    else if (p.InvItem![i] == item) room += Math.Max(0, size - p.InvCount[i]);
                }
                return room;
            }
            case ContainerKind.Hold:
                return Math.Max(0, HoldCapacityOf(p) - p.HoldTotal - p.ShipReserved);
            case ContainerKind.Stock:
                return Math.Max(0, StockCapacity(c.Id) - StockTotal(c.Id));
            case ContainerKind.Reactor:
            {
                if (FuelIntervals(item) == 0) return -1;
                var st = _structures[c.Id];
                if (st.ReactorFuel.TryGetValue((c.Mx, c.My, c.Mz), out var fuel) && fuel.Count > 0)
                    return fuel.Item == item ? Math.Max(0, StackSizeOf(item) - fuel.Count) : -1;
                return StackSizeOf(item);
            }
            case ContainerKind.MachineIn:
            {
                int cap = MachineInputCapacity(c.Id, item);
                return cap == 0 ? -1 : Math.Max(0, cap - _machines[c.Id]!.In[item]);
            }
            case ContainerKind.Void:
                return int.MaxValue;
            default:
                return -1;
        }
    }

    private void Take(Player p, ContainerRef c, int slot, int item, int n)
    {
        switch (c.Kind)
        {
            case ContainerKind.Person:
                p.InvCount![slot] -= n;
                if (p.InvCount[slot] == 0) p.InvItem![slot] = -1;
                break;
            case ContainerKind.Hold:
                p.Hold[item] -= n; p.HoldTotal -= n; HoldOut += n;
                break;
            case ContainerKind.Stock:
                _structures[c.Id].Stock![item] -= n;
                break;
            case ContainerKind.Reactor:
            {
                var st = _structures[c.Id];
                var fuel = st.ReactorFuel[(c.Mx, c.My, c.Mz)];
                if (fuel.Count - n <= 0) st.ReactorFuel.Remove((c.Mx, c.My, c.Mz)); else st.ReactorFuel[(c.Mx, c.My, c.Mz)] = new FuelSlot(fuel.Item, fuel.Count - n);
                break;
            }
            case ContainerKind.MachineIn:
                _machines[c.Id]!.In[item] -= n; HandOutOfMachines += n;
                break;
            case ContainerKind.MachineOut:
                _machines[c.Id]!.Out[item] -= n; HandOutOfMachines += n;
                break;
        }
    }

    private void Put(Player p, ContainerRef c, int slot, int item, int n)
    {
        switch (c.Kind)
        {
            case ContainerKind.Person:
                p.EnsureInventory();
                if (slot != InventoryRules.AnySlot) { p.InvItem![slot] = item; p.InvCount![slot] += n; break; }
                int size = StackSizeOf(item);
                for (int i = 0; i < InventoryRules.Slots && n > 0; i++) // the stacks of the item that have room first, in slot order
                {
                    if (p.InvCount![i] <= 0 || p.InvItem![i] != item) continue;
                    int add = Math.Min(n, size - p.InvCount[i]);
                    if (add > 0) { p.InvCount[i] += add; n -= add; }
                }
                for (int i = 0; i < InventoryRules.Slots && n > 0; i++)
                {
                    if (p.InvCount![i] > 0) continue;
                    int add = Math.Min(n, size);
                    p.InvItem![i] = item; p.InvCount[i] = add; n -= add;
                }
                break;
            case ContainerKind.Hold:
                AddToHold(p, item, n);
                break;
            case ContainerKind.Stock:
            {
                var st = _structures[c.Id];
                st.Stock ??= new int[Content!.Items.Count];
                st.Stock[item] += n;
                break;
            }
            case ContainerKind.Reactor:
            {
                var st = _structures[c.Id];
                int have = st.ReactorFuel.TryGetValue((c.Mx, c.My, c.Mz), out var fuel) ? fuel.Count : 0;
                st.ReactorFuel[(c.Mx, c.My, c.Mz)] = new FuelSlot(item, have + n);
                break;
            }
            case ContainerKind.MachineIn:
                _machines[c.Id]!.In[item] += n; HandIntoMachines += n;
                break;
        }
    }

    /// <summary>Adds items to a person's inventory without any rule except the slots and stacks (setup and tests). Returns how many fit.</summary>
    public int GiveInventory(int player, int item, int count)
    {
        var p = GetOrCreatePlayer(player);
        if (Content is null || (uint)item >= (uint)Content.Items.Count) throw new ArgumentOutOfRangeException(nameof(item));
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        int room = RoomFor(p, ContainerRef.Person, InventoryRules.AnySlot, item);
        int n = Math.Min(count, Math.Max(0, room));
        if (n > 0) Put(p, ContainerRef.Person, InventoryRules.AnySlot, item, n);
        return n;
    }

    /// <summary>Merges stacks of one item and packs the stacks to the front of the inventory ordered by item index.</summary>
    internal void SortInventory(int player)
    {
        var p = GetOrCreatePlayer(player);
        if (p.InvCount is null) return;
        var totals = new SortedDictionary<int, int>();
        for (int i = 0; i < p.InvCount.Length; i++)
            if (p.InvCount[i] > 0) totals[p.InvItem![i]] = totals.GetValueOrDefault(p.InvItem[i]) + p.InvCount[i];
        Array.Fill(p.InvItem!, -1);
        Array.Clear(p.InvCount);
        int slot = 0;
        foreach (var (item, total) in totals)
        {
            int size = StackSizeOf(item), left = total;
            while (left > 0 && slot < InventoryRules.Slots)
            {
                int n = Math.Min(left, size);
                p.InvItem![slot] = item; p.InvCount[slot] = n; left -= n; slot++;
            }
        }
    }

    // ---- reactors -----------------------------------------------------------------------------------------

    /// <summary>Sets where a reactor module takes fuel from when its slot is empty (the default is stored as no entry).</summary>
    internal void SetFeedSourceCore(int structure, int mx, int my, int mz, FeedSource source)
    {
        var s = GetStructure(structure);
        if (source == FeedSource.Hold) s.FeedSources.Remove((mx, my, mz)); else s.FeedSources[(mx, my, mz)] = source;
    }

    /// <summary>Takes one fuel item from a reactor's own fuel slot. Returns the burn intervals it gives, or 0 when the slot is empty.</summary>
    private int TakeSlotFuel(Structure s, (int Mx, int My, int Mz) key)
    {
        if (s.ReactorFuel.Count == 0 || !s.ReactorFuel.TryGetValue(key, out var fuel) || fuel.Count <= 0) return 0;
        int intervals = FuelIntervals(fuel.Item);
        if (fuel.Count == 1) s.ReactorFuel.Remove(key); else s.ReactorFuel[key] = new FuelSlot(fuel.Item, fuel.Count - 1);
        return intervals;
    }

    /// <summary>Takes one fuel item from the owner's personal inventory: a fuel cell first, else hydrogen. Returns the burn intervals it gives, or 0.</summary>
    private int TakeFuelFromInventory(int owner)
    {
        if (owner < 0 || Content is null || !_players.TryGetValue(owner, out var p) || p.InvCount is null) return 0;
        foreach (int item in new[] { Content.FuelCellItem, Content.FuelItem })
        {
            if (item < 0) continue;
            for (int i = 0; i < p.InvCount.Length; i++)
            {
                if (p.InvCount[i] <= 0 || p.InvItem![i] != item) continue;
                p.InvCount[i]--;
                if (p.InvCount[i] == 0) p.InvItem[i] = -1;
                return FuelIntervals(item);
            }
        }
        return 0;
    }

    /// <summary>The fuel a reactor takes when its slot is empty, by its feed source (ADR 0035). The default draws the station stock and the hold as reactors always did.</summary>
    private int TakeFedFuel(Structure s, (int Mx, int My, int Mz) key)
    {
        var mode = s.FeedSources.Count == 0 ? FeedSource.Hold : s.FeedSources.TryGetValue(key, out var f) ? f : FeedSource.Hold;
        int intervals = 0;
        if (mode is FeedSource.Hold or FeedSource.HoldThenInventory)
        {
            intervals = TakeFuelFromStock(s); // the station's stock first: its reactors and a docked ship's burn it before the hold
            if (intervals == 0) intervals = TakeFuel(s.Owner);
        }
        if (intervals == 0 && mode is FeedSource.Inventory or FeedSource.HoldThenInventory) intervals = TakeFuelFromInventory(s.Owner);
        return intervals;
    }

    // ---- hash, copy, validation ------------------------------------------------------------------------------------

    private static void MixInventory(Player p, Action<long> mix)
    {
        if (!p.HasInventory) return; // a player who never carried anything hashes as before the inventory existed
        mix(-4101);
        for (int i = 0; i < p.InvCount!.Length; i++)
            if (p.InvCount[i] > 0) { mix(i); mix(p.InvItem![i]); mix(p.InvCount[i]); }
    }

    private static void MixReactorFuel(Structure s, Action<long> mix)
    {
        if (s.ReactorFuel.Count > 0)
        {
            mix(-4102);
            foreach (var (k, f) in s.ReactorFuel) { mix(k.Mx); mix(k.My); mix(k.Mz); mix(f.Item); mix(f.Count); }
        }
        if (s.FeedSources.Count > 0)
        {
            mix(-4103);
            foreach (var (k, f) in s.FeedSources) { mix(k.Mx); mix(k.My); mix(k.Mz); mix((int)f); }
        }
    }

    private void MixHandCounters(Action<long> mix)
    {
        if (HandIntoMachines != 0) { mix(-4104); mix(HandIntoMachines); }
        if (HandOutOfMachines != 0) { mix(-4105); mix(HandOutOfMachines); }
    }

    private void CopyInventoryFrom(BeltNetwork other)
    {
        HandIntoMachines = other.HandIntoMachines;
        HandOutOfMachines = other.HandOutOfMachines;
    }

    private void ValidateInventory(Player p)
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        if (p.InvCount is null) return;
        if (Rules.LegacyInventory && p.HasInventory) Fail($"player {p.Id} has an inventory in a legacy world");
        if (p.InvItem is null || p.InvCount.Length != InventoryRules.Slots || p.InvItem.Length != InventoryRules.Slots) Fail($"player {p.Id} has a malformed inventory");
        for (int i = 0; i < InventoryRules.Slots; i++)
        {
            int count = p.InvCount[i];
            if (count < 0) Fail($"player {p.Id} slot {i} has a negative count");
            if (count == 0) { if (p.InvItem![i] != -1) Fail($"player {p.Id} slot {i} is empty but names an item"); continue; }
            if (Content is null || (uint)p.InvItem![i] >= (uint)Content.Items.Count) Fail($"player {p.Id} slot {i} holds an unknown item");
            else if (count > StackSizeOf(p.InvItem![i])) Fail($"player {p.Id} slot {i} holds {count}, more than a stack");
        }
    }

    private void ValidateReactorFuel(Structure s)
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        foreach (var (key, fuel) in s.ReactorFuel)
        {
            if (!s.ModuleMap.TryGetValue(key, out var kind) || !ModuleKindIds.IsReactor(kind)) Fail($"structure {s.Id} has fuel in a module {key} that is no reactor");
            if (FuelIntervals(fuel.Item) == 0) Fail($"structure {s.Id} reactor {key} holds an item that is no fuel");
            else if (fuel.Count < 1 || fuel.Count > StackSizeOf(fuel.Item)) Fail($"structure {s.Id} reactor {key} holds {fuel.Count} fuel, out of range");
            if (!StructureRules) Fail($"structure {s.Id} has reactor fuel without the structure rules");
        }
        foreach (var (key, source) in s.FeedSources)
        {
            if (!s.ModuleMap.TryGetValue(key, out var kind) || !ModuleKindIds.IsReactor(kind)) Fail($"structure {s.Id} has a feed source on a module {key} that is no reactor");
            if (!Enum.IsDefined(source) || source == FeedSource.Hold) Fail($"structure {s.Id} reactor {key} has an invalid feed source");
        }
    }
}
