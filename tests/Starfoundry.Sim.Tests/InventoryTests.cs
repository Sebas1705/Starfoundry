using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The personal inventory and the container moves (ADR 0035, docs/prototypes/inventory.md): every container pair, stacks and capacity, reach, the
/// reactor fuel slot and its feed modes, the codec, the legacy gate and the checksum.
/// </summary>
public class InventoryTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261121UL;

    private static readonly ContentDb C = TestContent.Core;
    private static readonly ContentDb Shipped = TestContent.CoreFloor;
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private static readonly int FuelCell = C.ItemIndex("core:fuel_cell");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Carbon = C.ItemIndex("core:carbon");

    private static SimWorld NewWorld(ContentDb? content = null, WorldRules? rules = null, GameMode mode = GameMode.Sandbox) =>
        SimWorld.CreateGalaxy(content ?? C, mode, Seed, threats: false, stationRules: false, structures: true, rules: rules);

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    private static void Ok(SimWorld w, Command c) => Assert.Equal(Reject.None, Run(w, c));

    private static MoveItems Move(ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int count) => new(from, fromSlot, to, toSlot, count);

    /// <summary>Empties the hold, so only what a test gives is there.</summary>
    private static void EmptyHold(SimWorld w)
    {
        for (int i = 0; i < w.Content!.Items.Count; i++)
            if (w.Network.HoldCount(0, i) > 0) Ok(w, new DumpItems(i, 0));
    }

    private static (int Mx, int My, int Mz) ReactorOf(SimWorld w) =>
        w.Network.GetStructure(w.Network.ShipStructure(0)).Modules.First(kv => ModuleKindIds.IsReactor(kv.Value)).Key;

    private static ContainerRef ReactorSlot(SimWorld w)
    {
        var k = ReactorOf(w);
        return ContainerRef.Reactor(w.Network.ShipStructure(0), k.Mx, k.My, k.Mz);
    }

    /// <summary>The starter ship and a core-only station whose +X port is 10 cells from the ship's -X port (see DockingTests).</summary>
    private static SimWorld Dockable(out int ship, out int station, int gapCells = 10, ContentDb? content = null)
    {
        var w = NewWorld(content);
        ship = w.Network.ShipStructure(0);
        station = w.Network.CreateStructure(0, StructureKind.Station, (-8 - gapCells) * (long)Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        return w;
    }

    private static void Dock(SimWorld w, int station) => Ok(w, new DockAt(station, 0, 0, 0, Dir.PosX));

    // ---- the person and the hold ---------------------------------------------------------------------------

    [Fact]
    public void Items_move_between_the_hold_and_the_person_in_both_directions()
    {
        var w = NewWorld();
        w.Step(); // the reactor takes its first fuel from the hold
        int held = w.Network.HoldCount(0, Hydrogen);
        Assert.True(held >= 5);
        Ok(w, Move(ContainerRef.Hold, Hydrogen, ContainerRef.Person, InventoryRules.AnySlot, 5));
        Assert.Equal(held - 5, w.Network.HoldCount(0, Hydrogen));
        Assert.Equal(5, w.Network.InventoryCount(0, Hydrogen));
        Assert.Equal(new ItemStack(Hydrogen, 5), w.Network.InventoryStack(0, 0));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Hold, InventoryRules.AnySlot, 2));
        Assert.Equal(held - 3, w.Network.HoldCount(0, Hydrogen));
        Assert.Equal(3, w.Network.InventoryCount(0, Hydrogen));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Hold, Hydrogen, 0)); // zero means the rest, and the item's own index is a valid hold slot
        Assert.Equal(held, w.Network.HoldCount(0, Hydrogen));
        Assert.Equal(0, w.Network.InventoryTotal(0));
        Assert.Null(w.Network.InventoryStack(0, 0));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_hold_accounting_follows_the_moves()
    {
        var w = NewWorld();
        w.Step(); // the reactor takes its first fuel
        long before = w.Network.HoldIn - w.Network.HoldOut;
        Ok(w, Move(ContainerRef.Hold, Hydrogen, ContainerRef.Person, InventoryRules.AnySlot, 4));
        Assert.Equal(before - 4, w.Network.HoldIn - w.Network.HoldOut);
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Hold, InventoryRules.AnySlot, 0));
        Assert.Equal(before, w.Network.HoldIn - w.Network.HoldOut);
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Stacks_are_capped_by_the_stack_size_and_fill_slots_in_order()
    {
        var w = NewWorld();
        w.Network.GiveItems(0, Ore, 40);
        EmptyHold(w);
        w.Network.GiveItems(0, Ore, 30);
        Assert.Equal(100, C.StackSizeOf(Ore)); // the default
        Assert.Equal(30, w.Network.GiveInventory(0, Ore, 30));
        Assert.Equal(new ItemStack(Ore, 30), w.Network.InventoryStack(0, 0));
        Assert.Equal(100, w.Network.GiveInventory(0, Ore, 100)); // 70 top up the first stack, 30 start the second
        Assert.Equal(new ItemStack(Ore, 100), w.Network.InventoryStack(0, 0));
        Assert.Equal(new ItemStack(Ore, 30), w.Network.InventoryStack(0, 1));
        Assert.Equal(2, w.Network.InventoryUsedSlots(0));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_content_sets_stack_sizes_and_fuel_cells_stack_to_twenty()
    {
        Assert.Equal(20, Shipped.StackSizeOf(Shipped.ItemIndex("core:fuel_cell")));
        Assert.Equal(100, Shipped.StackSizeOf(Shipped.ItemIndex("core:iron_ore")));
        Assert.Equal(100, Shipped.StackSizeOf(-1)); // anything unknown falls back to the default
        var w = NewWorld(Shipped);
        int cell = Shipped.ItemIndex("core:fuel_cell");
        Assert.Equal(45, w.Network.GiveInventory(0, cell, 45));
        Assert.Equal(new[] { 20, 20, 5 }, new[] { 0, 1, 2 }.Select(i => w.Network.InventoryStack(0, i)!.Count).ToArray());
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json"));
        Assert.NotEqual(Shipped.Hash, ContentDb.FromJson(System.Text.RegularExpressions.Regex.Replace(json, @",\s*""stack_size"": 20", "")).Hash); // the stack size is part of the content hash
    }

    [Fact]
    public void A_full_inventory_refuses_more_and_a_strict_count_must_fit()
    {
        var w = NewWorld();
        for (int s = 0; s < InventoryRules.Slots - 1; s++) w.Network.GiveInventory(0, Carbon, 100);
        Assert.Equal(InventoryRules.Slots - 1, w.Network.InventoryUsedSlots(0));
        w.Network.GiveItems(0, Carbon, 10);
        int held = w.Network.HoldCount(0, Carbon);
        // One empty slot left: room for 100 more of an ore, or of a new item, nothing beyond.
        Ok(w, Move(ContainerRef.Hold, Carbon, ContainerRef.Person, InventoryRules.AnySlot, held));
        Assert.Equal(0, w.Network.HoldCount(0, Carbon));
        w.Network.GiveInventory(0, Carbon, 100 - held); // the last slot full
        Assert.Equal(InventoryRules.Slots, w.Network.InventoryUsedSlots(0));
        w.Network.GiveItems(0, Ingot, 5);
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Hold, Ingot, ContainerRef.Person, InventoryRules.AnySlot, 5)));
        Assert.Equal(5, w.Network.HoldCount(0, Ingot)); // nothing moved
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Counts_above_what_is_there_or_what_fits_are_rejected_and_zero_takes_what_fits()
    {
        var w = NewWorld();
        EmptyHold(w);
        w.Network.GiveItems(0, Carbon, 30);
        Assert.Equal(Reject.NotEnoughItems, Run(w, Move(ContainerRef.Hold, Carbon, ContainerRef.Person, InventoryRules.AnySlot, 31)));
        Assert.Equal(Reject.NotEnoughItems, Run(w, Move(ContainerRef.Hold, Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        Assert.Equal(30, w.Network.HoldCount(0, Carbon));
        // Fill the inventory so that only 20 more ore fit, then ask for 30: strict is NoRoom, zero moves the 20.
        for (int s = 0; s < InventoryRules.Slots - 1; s++) w.Network.GiveInventory(0, Carbon, 100);
        w.Network.GiveInventory(0, Carbon, 80);
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Hold, Carbon, ContainerRef.Person, InventoryRules.AnySlot, 30)));
        Ok(w, Move(ContainerRef.Hold, Carbon, ContainerRef.Person, InventoryRules.AnySlot, 0));
        Assert.Equal(10, w.Network.HoldCount(0, Carbon));
        Assert.Equal(InventoryRules.Slots * 100, w.Network.InventoryTotal(0));
    }

    [Fact]
    public void A_split_moves_part_of_a_stack_to_a_chosen_slot_and_stacks_merge()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 60);
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Person, 5, 30)); // right drag: half
        Assert.Equal(new ItemStack(Ore, 30), w.Network.InventoryStack(0, 0));
        Assert.Equal(new ItemStack(Ore, 30), w.Network.InventoryStack(0, 5));
        Ok(w, Move(ContainerRef.Person, 5, ContainerRef.Person, 0, 0)); // the rest merges back
        Assert.Equal(new ItemStack(Ore, 60), w.Network.InventoryStack(0, 0));
        Assert.Null(w.Network.InventoryStack(0, 5));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Person, 0, 10))); // onto itself
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Person, InventoryRules.AnySlot, 10))); // within the grid a slot is needed
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_whole_stack_dropped_on_another_item_swaps_but_a_part_does_not()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 40);
        w.Network.GiveInventory(0, Ingot, 7);
        Assert.Equal(Reject.WrongItem, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Person, 1, 10)));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Person, 1, 0));
        Assert.Equal(new ItemStack(Ingot, 7), w.Network.InventoryStack(0, 0));
        Assert.Equal(new ItemStack(Ore, 40), w.Network.InventoryStack(0, 1));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Sorting_merges_stacks_and_packs_them_in_item_order()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ingot, 70);
        w.Network.GiveInventory(0, Ore, 10);
        w.Network.GiveInventory(0, Ingot, 70); // 100 + 40 over two stacks
        w.Network.GiveInventory(0, Ore, 5);
        Ok(w, Move(ContainerRef.Person, 2, ContainerRef.Person, 9, 0)); // fragment the second ingot stack
        Ok(w, new SortInventory());
        var stacks = w.Network.ContainerStacks(0, ContainerRef.Person);
        Assert.Equal(new[] { (0, Ore, 15), (1, Ingot, 100), (2, Ingot, 40) }, stacks.ToArray());
        Assert.Equal(15 + 140, w.Network.InventoryTotal(0));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_bin_takes_everything_and_gives_nothing_back()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 40);
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Void, InventoryRules.AnySlot, 15));
        Assert.Equal(25, w.Network.InventoryCount(0, Ore));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Void, InventoryRules.AnySlot, 0));
        Assert.Equal(0, w.Network.InventoryTotal(0));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Void, 0, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 99, ContainerRef.Void, InventoryRules.AnySlot, 1)));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Bad_addresses_are_invalid_arguments()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 10);
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, InventoryRules.Slots, ContainerRef.Hold, InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, -1, ContainerRef.Hold, InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Person, InventoryRules.Slots, 1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Hold, C.Items.Count, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Hold, Ingot, 1))); // a hold slot is the item's own index
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Hold, Ore, ContainerRef.Hold, InventoryRules.AnySlot, 1)));
        Assert.Equal(10, w.Network.InventoryCount(0, Ore));
    }

    // ---- stock, docking and reach ---------------------------------------------------------------------------

    [Fact]
    public void The_station_stock_and_the_person_exchange_items_while_docked()
    {
        var w = Dockable(out _, out int station);
        Dock(w, station);
        w.Network.GiveStock(station, Ingot, 12);
        Ok(w, Move(ContainerRef.Stock(station), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 5));
        Assert.Equal(7, w.Network.StockCount(station, Ingot));
        Assert.Equal(5, w.Network.InventoryCount(0, Ingot));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Stock(station), InventoryRules.AnySlot, 3));
        Assert.Equal(10, w.Network.StockCount(station, Ingot));
        Ok(w, Move(ContainerRef.Stock(station), Ingot, ContainerRef.Hold, InventoryRules.AnySlot, 4));
        Ok(w, Move(ContainerRef.Hold, Ingot, ContainerRef.Stock(station), InventoryRules.AnySlot, 4));
        Assert.Equal(Reject.NotEnoughItems, Run(w, Move(ContainerRef.Stock(station), Ore, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_stock_refuses_what_its_capacity_does_not_hold()
    {
        var w = Dockable(out _, out int station);
        Dock(w, station);
        int capacity = w.Network.StockCapacity(station);
        w.Network.GiveStock(station, Ingot, capacity - 2);
        w.Network.GiveInventory(0, Ore, 5);
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Stock(station), InventoryRules.AnySlot, 3)));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Stock(station), InventoryRules.AnySlot, 2));
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Stock(station), InventoryRules.AnySlot, 0)));
        Assert.Equal(3, w.Network.InventoryCount(0, Ore));
    }

    [Fact]
    public void A_station_out_of_reach_is_refused_and_one_within_reach_or_docked_is_not()
    {
        var w = Dockable(out _, out int station, gapCells: 40);
        w.Network.GiveStock(station, Ingot, 5);
        Assert.Equal(Reject.OutOfRange, Run(w, Move(ContainerRef.Stock(station), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        var near = Dockable(out _, out int nearStation, gapCells: 6); // the ship is within reach of the core but not docked
        near.Network.GiveStock(nearStation, Ingot, 5);
        Ok(near, Move(ContainerRef.Stock(nearStation), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1));
        Assert.Equal(1, near.Network.InventoryCount(0, Ingot));
        var docked = Dockable(out _, out int dockedStation);
        Dock(docked, dockedStation);
        docked.Network.GiveStock(dockedStation, Ingot, 5);
        Ok(docked, Move(ContainerRef.Stock(dockedStation), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1));
    }

    [Fact]
    public void Stocks_and_reactors_of_other_players_and_missing_structures_are_refused()
    {
        var w = Dockable(out int ship, out int station);
        Dock(w, station);
        Assert.Equal(Reject.NoSuchStructure, Run(w, Move(ContainerRef.Stock(999), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.NoSuchStructure, Run(w, Move(ContainerRef.Stock(ship), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1))); // a ship has no stock
        var k = ReactorOf(w);
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Reactor(ship, k.Mx + 9, k.My, k.Mz), 0, 1))); // no module there
        Assert.Equal(Reject.InvalidArgument, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Reactor(station, 0, 0, 0), 0, 1))); // the core is no reactor
    }

    [Fact]
    public void A_transfer_is_a_move_between_the_hold_and_the_stock_and_behaves_as_before()
    {
        foreach (bool toStation in new[] { true, false })
        {
            var viaTransfer = Dockable(out _, out int a);
            var viaMove = Dockable(out _, out int b);
            Dock(viaTransfer, a); Dock(viaMove, b);
            if (!toStation) { viaTransfer.Network.GiveStock(a, Ingot, 30); viaMove.Network.GiveStock(b, Ingot, 30); }
            else { viaTransfer.Network.GiveItems(0, Ingot, 30); viaMove.Network.GiveItems(0, Ingot, 30); }
            Ok(viaTransfer, new TransferItems(Ingot, 12, toStation));
            Ok(viaMove, toStation ? Move(ContainerRef.Hold, Ingot, ContainerRef.Stock(b), InventoryRules.AnySlot, 12) : Move(ContainerRef.Stock(b), Ingot, ContainerRef.Hold, InventoryRules.AnySlot, 12));
            Assert.Equal(viaTransfer.Checksum(), viaMove.Checksum());
            Assert.Equal(viaTransfer.Network.HoldCount(0, Ingot), viaMove.Network.HoldCount(0, Ingot));
            Assert.Equal(viaTransfer.Network.StockCount(a, Ingot), viaMove.Network.StockCount(b, Ingot));
        }
    }

    // ---- the reactor ----------------------------------------------------------------------------------------

    [Fact]
    public void A_reactor_slot_takes_fuel_only_one_kind_at_a_time_and_up_to_a_stack()
    {
        var w = NewWorld(Shipped);
        int cell = Shipped.ItemIndex("core:fuel_cell"), hydrogen = Shipped.ItemIndex("core:hydrogen"), ore = Shipped.ItemIndex("core:iron_ore");
        var slot = ReactorSlot(w);
        w.Network.GiveItems(0, cell, 25);
        w.Network.GiveItems(0, ore, 3);
        Assert.Equal(Reject.WrongItem, Run(w, Move(ContainerRef.Hold, ore, slot, 0, 1)));
        Ok(w, Move(ContainerRef.Hold, cell, slot, 0, 10));
        Assert.Equal(Reject.WrongItem, Run(w, Move(ContainerRef.Hold, hydrogen, slot, 0, 1))); // the slot holds cells now
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Hold, cell, slot, 0, 11))); // a stack is 20
        Ok(w, Move(ContainerRef.Hold, cell, slot, InventoryRules.AnySlot, 10));
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Hold, cell, slot, 0, 1)));
        Assert.True(w.Network.TryGetReactorFuel(slot.Id, slot.Mx, slot.My, slot.Mz, out var info));
        Assert.Equal((cell, 20, 20), (info.Item, info.Count, info.Capacity));
        Ok(w, Move(slot, 0, ContainerRef.Person, InventoryRules.AnySlot, 5)); // and it gives fuel back
        Assert.Equal(5, w.Network.InventoryCount(0, cell));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_reactor_burns_from_its_slot_first_and_leaves_the_hold_alone()
    {
        var w = NewWorld();
        var slot = ReactorSlot(w);
        int start = w.Network.HoldCount(0, Hydrogen);
        Ok(w, Move(ContainerRef.Hold, Hydrogen, slot, 0, 3));
        Assert.Equal(start - 3, w.Network.HoldCount(0, Hydrogen));
        int interval = w.Network.BurnInterval(ModuleKind.Reactor);
        w.Step(interval * 2 + 5);
        Assert.True(w.Network.TryGetReactorFuel(slot.Id, slot.Mx, slot.My, slot.Mz, out var info));
        Assert.Equal(start - 3, w.Network.HoldCount(0, Hydrogen)); // the hold was not touched while the slot had fuel
        Assert.Equal(0, info.Count); // three were used up
        w.Step(interval * 2);
        Assert.True(w.Network.HoldCount(0, Hydrogen) < start - 3); // and only then does the hold pay (the default feed)
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_burn_time_counts_the_slot_and_fuel_cells_last_six_times_longer()
    {
        var w = NewWorld(Shipped);
        int cell = Shipped.ItemIndex("core:fuel_cell"), hydrogen = Shipped.ItemIndex("core:hydrogen");
        var slot = ReactorSlot(w);
        int interval = w.Network.BurnInterval(ModuleKind.Reactor);
        w.Network.GiveItems(0, cell, 4);
        Ok(w, Move(ContainerRef.Hold, cell, slot, 0, 4));
        Assert.True(w.Network.TryGetReactorFuel(slot.Id, slot.Mx, slot.My, slot.Mz, out var info));
        Assert.True(info.TotalTicksLeft >= 4 * 6 * interval - 1); // the tick of the move already took one cell
        Assert.True(info.TotalTicksLeft <= 4 * 6 * interval);
        Assert.Equal(FeedSource.Hold, info.Feed);
        // Hydrogen counts one interval each.
        var h = NewWorld(Shipped);
        var hs = ReactorSlot(h);
        Ok(h, Move(ContainerRef.Hold, hydrogen, hs, 0, 4));
        Assert.True(h.Network.TryGetReactorFuel(hs.Id, hs.Mx, hs.My, hs.Mz, out var hi));
        Assert.Equal(3 * interval, hi.TotalTicksLeft - hi.BurnTicksLeft); // one hydrogen is already burning
    }

    private static SimWorld FedWorld(FeedSource source, int hold, int inventory)
    {
        var w = NewWorld();
        EmptyHold(w);
        if (hold > 0) w.Network.GiveItems(0, Hydrogen, hold);
        if (inventory > 0) w.Network.GiveInventory(0, Hydrogen, inventory);
        var k = ReactorOf(w);
        if (source != FeedSource.Hold) Ok(w, new SetFeedSource(w.Network.ShipStructure(0), k.Mx, k.My, k.Mz, source));
        return w;
    }

    [Fact]
    public void Auto_feed_none_burns_only_the_slot()
    {
        var w = FedWorld(FeedSource.None, hold: 20, inventory: 20);
        w.Step(w.Network.BurnInterval(ModuleKind.Reactor) * 6);
        Assert.Equal(20, w.Network.HoldCount(0, Hydrogen));
        Assert.Equal(20, w.Network.InventoryCount(0, Hydrogen));
        Assert.False(w.Network.PowerStatus(w.Network.ShipStructure(0)).Burning); // nothing was fed
    }

    [Fact]
    public void Auto_feed_from_the_hold_is_the_default_and_leaves_the_inventory_alone()
    {
        var w = FedWorld(FeedSource.Hold, hold: 20, inventory: 20);
        w.Step(w.Network.BurnInterval(ModuleKind.Reactor) * 6);
        Assert.True(w.Network.HoldCount(0, Hydrogen) < 20);
        Assert.Equal(20, w.Network.InventoryCount(0, Hydrogen));
        Assert.Equal(FeedSource.Hold, w.Network.FeedSourceOf(w.Network.ShipStructure(0), ReactorOf(w).Mx, ReactorOf(w).My, ReactorOf(w).Mz));
    }

    [Fact]
    public void Auto_feed_from_the_inventory_burns_it_and_leaves_the_hold_alone()
    {
        var w = FedWorld(FeedSource.Inventory, hold: 20, inventory: 20);
        w.Step(w.Network.BurnInterval(ModuleKind.Reactor) * 6);
        Assert.Equal(20, w.Network.HoldCount(0, Hydrogen));
        Assert.True(w.Network.InventoryCount(0, Hydrogen) < 20);
        Assert.Equal(0, w.Network.InventoryCount(0, FuelCell));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Auto_feed_hold_then_inventory_takes_the_hold_first_and_the_inventory_when_it_is_dry()
    {
        var w = FedWorld(FeedSource.HoldThenInventory, hold: 2, inventory: 20);
        int interval = w.Network.BurnInterval(ModuleKind.Reactor);
        w.Step(interval * 2 - 2);
        Assert.Equal(20, w.Network.InventoryCount(0, Hydrogen)); // the hold still had fuel
        w.Step(interval * 4);
        Assert.Equal(0, w.Network.HoldCount(0, Hydrogen));
        Assert.True(w.Network.InventoryCount(0, Hydrogen) < 20); // then the inventory paid
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Feed_sources_are_set_by_the_owner_within_reach_on_reactors_only()
    {
        var w = Dockable(out int ship, out int station, gapCells: 40);
        var k = ReactorOf(w);
        Ok(w, new SetFeedSource(ship, k.Mx, k.My, k.Mz, FeedSource.Inventory));
        Assert.Equal(FeedSource.Inventory, w.Network.FeedSourceOf(ship, k.Mx, k.My, k.Mz));
        Ok(w, new SetFeedSource(ship, k.Mx, k.My, k.Mz, FeedSource.Hold));
        Assert.Equal(FeedSource.Hold, w.Network.FeedSourceOf(ship, k.Mx, k.My, k.Mz));
        w.Network.ValidateInvariants(); // the default is stored as nothing
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetFeedSource(ship, 0, 0, 0, FeedSource.None))); // the cockpit is no reactor
        Assert.Equal(Reject.NoSuchStructure, Run(w, new SetFeedSource(77, 0, 0, 0, FeedSource.None)));
        w.Network.AddStructureModuleUnchecked(station, 1, 0, 0, ModuleKind.Reactor);
        Assert.Equal(Reject.OutOfRange, Run(w, new SetFeedSource(station, 1, 0, 0, FeedSource.None))); // 40 cells away and not docked
    }

    [Fact]
    public void A_removed_reactor_loses_its_fuel_slot_and_feed_setting()
    {
        var w = Dockable(out int ship, out int station);
        int reactorStation = station;
        w.Network.AddStructureModuleUnchecked(station, 0, 1, 0, ModuleKind.Reactor);
        Dock(w, station);
        Ok(w, new SetFeedSource(station, 0, 1, 0, FeedSource.None));
        w.Network.GiveInventory(0, Hydrogen, 5);
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.Reactor(station, 0, 1, 0), 0, 5));
        w.Network.RemoveStructureModuleUnchecked(station, 0, 1, 0);
        w.Network.ValidateInvariants();
        w.Network.AddStructureModuleUnchecked(station, 0, 1, 0, ModuleKind.Reactor);
        Assert.True(w.Network.TryGetReactorFuel(station, 0, 1, 0, out var info));
        Assert.Equal((-1, 0, FeedSource.Hold), (info.Item, info.Count, info.Feed));
        _ = ship; _ = reactorStation;
    }

    [Fact]
    public void A_docked_ships_reactor_still_draws_the_station_stock_under_the_default_feed()
    {
        var w = Dockable(out _, out int station);
        Dock(w, station);
        w.Network.GiveStock(station, Hydrogen, 5);
        int hold = w.Network.HoldCount(0, Hydrogen);
        w.Step(w.Network.BurnInterval(ModuleKind.Reactor) + 3);
        Assert.Equal(hold, w.Network.HoldCount(0, Hydrogen));
        Assert.True(w.Network.StockCount(station, Hydrogen) < 5);
    }

    // ---- machine buffers --------------------------------------------------------------------------------------

    private static SimWorld WithSmelter(out int machine, out int station)
    {
        var w = Dockable(out _, out station, gapCells: 4);
        w.Network.AddStructureModuleUnchecked(station, 0, 1, 0, ModuleKind.Reactor); // a powered station: its stock fuels the reactor
        w.Network.GiveStock(station, Hydrogen, 40);
        int smelter = C.MachineIndex("core:smelter");
        Ok(w, new PlaceMachineIn(station, 1, 0, 1, smelter, C.RecipeIndex("core:smelt_iron")));
        machine = w.Network.MachineCount - 1;
        return w;
    }

    [Fact]
    public void A_machine_input_takes_what_its_recipe_uses_and_the_output_gives_items_back()
    {
        var w = WithSmelter(out int machine, out _);
        int cap = w.Network.MachineInputCapacity(machine, Ore);
        Assert.True(cap > 0);
        Assert.Equal(0, w.Network.MachineInputCapacity(machine, Carbon));
        w.Network.GiveInventory(0, Ore, 50);
        w.Network.GiveInventory(0, Carbon, 1);
        Assert.Equal(Reject.WrongItem, Run(w, Move(ContainerRef.Person, 1, ContainerRef.MachineIn(machine), InventoryRules.AnySlot, 1))); // carbon is no input of the recipe
        Assert.Equal(Reject.NoRoom, Run(w, Move(ContainerRef.Person, 0, ContainerRef.MachineIn(machine), InventoryRules.AnySlot, cap + 1)));
        Ok(w, Move(ContainerRef.Person, 0, ContainerRef.MachineIn(machine), InventoryRules.AnySlot, cap));
        Assert.True(w.Network.GetMachineInfo(machine).InputItems > 0 && w.Network.GetMachineInfo(machine).Crafting); // the smelter took one of them to work on
        Assert.Equal(Reject.WrongItem, Run(w, Move(ContainerRef.Person, 0, ContainerRef.MachineOut(machine), InventoryRules.AnySlot, 1))); // nothing goes into an output
        w.Network.ValidateInvariants(); // the hand counters keep the belt conservation law whole
        w.Step(Units.TicksPerSecond * 20);
        int made = w.Network.GetMachineInfo(machine).OutputItems;
        Assert.True(made > 0);
        Ok(w, Move(ContainerRef.MachineOut(machine), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 0));
        Assert.Equal(made, w.Network.InventoryCount(0, Ingot));
        Assert.Equal(0, w.Network.GetMachineInfo(machine).OutputItems);
        Assert.Equal(Reject.NotEnoughItems, Run(w, Move(ContainerRef.MachineOut(machine), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        w.Network.ValidateInvariants();
        Assert.Equal(w.Network.Clone().Checksum(), w.Checksum());
    }

    [Fact]
    public void Machines_out_of_reach_or_missing_are_refused()
    {
        var w = WithSmelter(out int machine, out int station);
        Assert.Equal(Reject.UnknownMachine, Run(w, Move(ContainerRef.MachineOut(machine + 40), Ingot, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        w.Network.SetStructureOrigin(station, (-8 - 60) * (long)Cell, 0, 0); // the station is now far from the ship
        w.Network.GiveInventory(0, Ore, 5);
        Assert.Equal(Reject.OutOfRange, Run(w, Move(ContainerRef.Person, 0, ContainerRef.MachineIn(machine), InventoryRules.AnySlot, 1)));
    }

    // ---- hash, copies and replays -----------------------------------------------------------------------------------

    [Fact]
    public void An_inventory_that_was_never_used_or_is_empty_again_does_not_change_the_checksum()
    {
        var plain = NewWorld();
        var used = NewWorld();
        Assert.Equal(plain.Checksum(), used.Checksum());
        used.Network.GiveInventory(0, Ore, 10);
        Assert.NotEqual(plain.Checksum(), used.Checksum()); // carrying something is state
        Ok(used, Move(ContainerRef.Person, 0, ContainerRef.Void, InventoryRules.AnySlot, 0));
        plain.Step();
        Assert.Equal(plain.Checksum(), used.Checksum()); // the bin leaves no trace: the same state again
        var rejected = NewWorld();
        Assert.Equal(Reject.NotEnoughItems, Run(rejected, Move(ContainerRef.Person, 3, ContainerRef.Hold, InventoryRules.AnySlot, 1)));
        Assert.Equal(plain.Checksum(), rejected.Checksum());
    }

    [Fact]
    public void The_inventory_and_the_reactor_fuel_are_part_of_the_hash_the_clone_and_the_validation()
    {
        var w = NewWorld();
        var slot = ReactorSlot(w);
        w.Network.GiveInventory(0, Hydrogen, 30);
        Ok(w, Move(ContainerRef.Person, 0, slot, 0, 10));
        Ok(w, new SetFeedSource(slot.Id, slot.Mx, slot.My, slot.Mz, FeedSource.HoldThenInventory));
        var clone = w.Network.Clone();
        Assert.Equal(w.Network.Checksum(), clone.Checksum());
        Assert.Equal(20, clone.InventoryCount(0, Hydrogen));
        Assert.True(clone.TryGetReactorFuel(slot.Id, slot.Mx, slot.My, slot.Mz, out var info));
        Assert.True(w.Network.TryGetReactorFuel(slot.Id, slot.Mx, slot.My, slot.Mz, out var live));
        Assert.Equal(live, info);
        Assert.Equal((Hydrogen, FeedSource.HoldThenInventory), (info.Item, info.Feed));
        clone.ValidateInvariants();
        var other = NewWorld();
        other.Network.GiveInventory(0, Hydrogen, 30);
        Ok(other, Move(ContainerRef.Person, 0, ContainerRef.Reactor(slot.Id, slot.Mx, slot.My, slot.Mz), 0, 11));
        Ok(other, new SetFeedSource(slot.Id, slot.Mx, slot.My, slot.Mz, FeedSource.HoldThenInventory));
        Assert.NotEqual(w.Checksum(), other.Checksum());
    }

    private static List<LoggedCommand> Script(out SimWorld recorded)
    {
        var w = NewWorld();
        var slot = ReactorSlot(w);
        w.Network.GiveInventory(0, Ore, 10);
        w.Submit(0, new MoveItems(ContainerRef.Hold, Hydrogen, ContainerRef.Person, InventoryRules.AnySlot, 6));
        w.Step(3);
        w.Submit(0, new MoveItems(ContainerRef.Person, 1, slot, 0, 4));
        w.Submit(0, new SetFeedSource(slot.Id, slot.Mx, slot.My, slot.Mz, FeedSource.Inventory));
        w.Step(Units.TicksPerSecond * 4);
        w.Submit(0, new SortInventory());
        w.Submit(0, new MoveItems(ContainerRef.Person, 0, ContainerRef.Void, InventoryRules.AnySlot, 3));
        w.Step(Units.TicksPerSecond * 2);
        recorded = w;
        return w.Log.ToList();
    }

    [Fact]
    public void A_log_with_inventory_commands_replays_to_the_same_state_and_roundtrips_through_the_codec()
    {
        Script(out var recorded);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        var log = CommandCodec.ReadLog(new MemoryStream(ms.ToArray()), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.False(rules.LegacyInventory);
        Assert.Equal(recorded.Log.Count, log.Count);
        Assert.Equal(recorded.Log.Select(l => l.Command), log.Select(l => l.Command));
        // The recording world gave the inventory ore before the first tick by hand, which a log cannot carry: replay on an equally prepared world.
        var prepared = NewWorld(); prepared.Network.GiveInventory(0, Ore, 10);
        var replay = SimWorld.Replay(log, recorded.Tick, initial: prepared);
        Assert.Equal(recorded.Checksum(), replay.Checksum());
        replay.Network.ValidateInvariants();
    }

    [Fact]
    public void The_commands_roundtrip_individually_and_inside_a_batch()
    {
        Command[] commands =
        {
            new MoveItems(ContainerRef.Person, 3, ContainerRef.Reactor(4, -1, 2, -3), 0, 7),
            new MoveItems(ContainerRef.Stock(9), 5, ContainerRef.Hold, InventoryRules.AnySlot, 0),
            new MoveItems(ContainerRef.MachineOut(12), 2, ContainerRef.MachineIn(13), 2, -1),
            new MoveItems(ContainerRef.Person, 23, ContainerRef.Void, InventoryRules.AnySlot, 5),
            new SetFeedSource(2, 1, 0, -4, FeedSource.HoldThenInventory),
            new SortInventory(),
        };
        foreach (var c in commands)
        {
            byte[] bytes = CommandCodec.ToBytes(c);
            Assert.Equal(c, CommandCodec.FromBytes(bytes));
            Assert.Equal(bytes, CommandCodec.ToBytes(CommandCodec.FromBytes(bytes)));
        }
        var batch = new Batch(commands.Take(2).ToArray());
        var back = Assert.IsType<Batch>(CommandCodec.FromBytes(CommandCodec.ToBytes(batch)));
        Assert.Equal(batch.Commands, back.Commands);
        Assert.Equal(56, CommandCodec.ToBytes(commands[0])[0]);
        Assert.Equal(57, CommandCodec.ToBytes(commands[4])[0]);
        Assert.Equal(58, CommandCodec.ToBytes(commands[5])[0]);
    }

    [Fact]
    public void Malformed_inventory_commands_cannot_be_encoded_or_decoded()
    {
        Assert.False(CommandCodec.IsWellFormed(new MoveItems(new ContainerRef((ContainerKind)77), 0, ContainerRef.Person, 0, 1)));
        Assert.False(CommandCodec.IsWellFormed(new MoveItems(new ContainerRef(ContainerKind.Hold, 0, 5, 0, 0), 0, ContainerRef.Person, 0, 1))); // only a reactor carries a module coordinate
        Assert.False(CommandCodec.IsWellFormed(new SetFeedSource(0, 0, 0, 0, (FeedSource)9)));
        Assert.Throws<ArgumentException>(() => CommandCodec.ToBytes(new SetFeedSource(0, 0, 0, 0, (FeedSource)9)));
        byte[] bad = CommandCodec.ToBytes(new SetFeedSource(0, 0, 0, 0, FeedSource.None));
        bad[^1] = 9;
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(bad));
        byte[] badKind = CommandCodec.ToBytes(new MoveItems(ContainerRef.Person, 0, ContainerRef.Hold, 0, 1));
        badKind[1] = 200;
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(badKind));
    }

    // ---- legacy -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Logs_older_than_version_18_read_with_the_legacy_flag_and_cannot_hold_the_commands()
    {
        Script(out var recorded);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, recorded.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(19, bytes[4]);
        var empty = new MemoryStream();
        CommandCodec.WriteLog(empty, new List<LoggedCommand>(), C.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        for (byte version = 6; version <= 17; version++)
        {
            var old = empty.ToArray();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyInventory, $"version {version}");
            Assert.True(rules.WithoutLegacyFlags().IsDefault);
        }
        var v15 = (byte[])bytes.Clone();
        v15[4] = 17;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(v15)));
        var current = CommandCodec.ReadLog(new MemoryStream(empty.ToArray()), out _, out _, out _, out _, out _, out _, out var now);
        Assert.False(now.LegacyInventory);
        Assert.Empty(current);
    }

    [Fact]
    public void A_legacy_world_refuses_the_commands_and_hashes_and_replays_as_before()
    {
        var legacy = NewWorld(rules: new WorldRules().WithLegacyInventory(true));
        var current = NewWorld();
        Assert.Equal(current.Checksum(), legacy.Checksum()); // the flag alone is invisible to the hash
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, Move(ContainerRef.Hold, Hydrogen, ContainerRef.Person, InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new SortInventory()));
        var k = ReactorOf(legacy);
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new SetFeedSource(legacy.Network.ShipStructure(0), k.Mx, k.My, k.Mz, FeedSource.None)));
        // A world whose reactors burn from the hold behaves the same with and without the new code paths (default feed): ships keep their fuel use.
        var a = NewWorld(rules: new WorldRules().WithLegacyInventory(true)); var b = NewWorld();
        a.Step(Units.TicksPerSecond * 120); b.Step(Units.TicksPerSecond * 120);
        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.Equal(a.Network.HoldCount(0, Hydrogen), b.Network.HoldCount(0, Hydrogen));
    }

    [Fact]
    public void The_legacy_flag_roundtrips_in_the_rules_block_and_other_rules_keep_their_bytes()
    {
        var legacy = new WorldRules().WithLegacyInventory(true);
        Assert.True(legacy.LegacyInventory && legacy.AnyLegacy);
        Assert.Equal(2, legacy.Serialize()[0]);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.True(legacy.WithoutLegacyFlags().IsDefault);
        Assert.NotEqual(WorldRules.Default, legacy);
        byte[] block = legacy.Serialize();
        Assert.Equal(4, block[1 + 8 * 4 + 1] & 4); // bit 2 of the second flags byte
        Assert.True(legacy.WithLegacyFlight(true).LegacyInventory); // the helpers keep each other's flags
        Assert.True(legacy.Clamped().LegacyInventory);
        Assert.False(legacy.WithLegacyInventory(false).LegacyInventory);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyInventory));
        byte[] bytes = legacy.Serialize();
        bytes[1 + 8 * 4 + 1] = 8; // a flag nobody defined
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => WorldRules.Deserialize(legacy.Serialize().AsSpan(0, legacy.Serialize().Length - 1)));
    }

    [Fact]
    public void Inventory_commands_work_without_the_structure_rules_for_the_person_and_the_hold()
    {
        var w = new SimWorld(C, GameMode.Sandbox);
        w.Network.EnsurePlayer(0);
        w.Network.GiveItems(0, Ore, 12);
        Ok(w, Move(ContainerRef.Hold, Ore, ContainerRef.Person, InventoryRules.AnySlot, 12));
        Assert.Equal(12, w.Network.InventoryCount(0, Ore));
        Assert.Equal(Reject.NotAllowedHere, Run(w, Move(ContainerRef.Person, 0, ContainerRef.Stock(1), InventoryRules.AnySlot, 1)));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new SetFeedSource(1, 0, 0, 0, FeedSource.None)));
        w.Network.ValidateInvariants();
    }
}
