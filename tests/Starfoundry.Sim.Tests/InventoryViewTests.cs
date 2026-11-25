using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The engine-free half of the inventory panel (ADR 0035): categories, the moves a drag or a shift click means, the grid padding and the texts.</summary>
public class InventoryViewTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;

    [Fact]
    public void Every_item_has_a_category_and_the_fuels_and_ammo_are_found()
    {
        foreach (var i in C.Items) Assert.NotEqual(ItemCategory.All, InventoryView.CategoryOf(i.Id));
        Assert.Equal(ItemCategory.Fuel, InventoryView.CategoryOf("core:hydrogen"));
        Assert.Equal(ItemCategory.Fuel, InventoryView.CategoryOf("core:fuel_cell"));
        Assert.Equal(ItemCategory.Ammo, InventoryView.CategoryOf("core:ammo"));
        Assert.Equal(ItemCategory.Ore, InventoryView.CategoryOf("core:titanium_ore"));
        Assert.Equal(ItemCategory.Ore, InventoryView.CategoryOf("core:ice"));
        Assert.Equal(ItemCategory.Parts, InventoryView.CategoryOf("core:iron_plate"));
        Assert.Equal(ItemCategory.Parts, InventoryView.CategoryOf("mod:unknown_thing")); // an item nobody classified is a part
        Assert.True(InventoryView.Matches(ItemCategory.All, "core:ammo"));
        Assert.True(InventoryView.Matches(ItemCategory.Fuel, "core:fuel_cell"));
        Assert.False(InventoryView.Matches(ItemCategory.Ore, "core:fuel_cell"));
        Assert.Equal(5, InventoryView.Categories.Length);
        foreach (var c in InventoryView.Categories)
        {
            Assert.True(Loc.Has(InventoryView.CategoryKey(c)));
            Assert.True(IconCatalog.IsRegistered(InventoryView.CategoryIcon(c)));
        }
    }

    [Fact]
    public void A_right_drag_takes_the_larger_half()
    {
        Assert.Equal(1, InventoryView.HalfOf(1));
        Assert.Equal(1, InventoryView.HalfOf(2));
        Assert.Equal(3, InventoryView.HalfOf(5));
        Assert.Equal(10, InventoryView.HalfOf(20));
        Assert.Equal(50, InventoryView.HalfOf(99));
        var half = InventoryView.Drop(ContainerRef.Person, 3, 41, ContainerRef.Person, 8, half: true);
        Assert.Equal(new MoveItems(ContainerRef.Person, 3, ContainerRef.Person, 8, 21), half);
        var whole = InventoryView.Drop(ContainerRef.Person, 3, 41, ContainerRef.Person, 8, half: false);
        Assert.Equal(0, whole.Count); // zero: the whole stack, as much as the target takes
        Assert.Equal(0, InventoryView.Drop(ContainerRef.Person, 3, 1, ContainerRef.Hold, 0, half: true).Count); // a single item has no half
    }

    [Fact]
    public void Drops_name_the_slot_each_container_understands()
    {
        var reactor = ContainerRef.Reactor(2, 1, 0, 0);
        Assert.Equal(InventoryRules.ReactorSlot, InventoryView.Drop(ContainerRef.Person, 0, 5, reactor, 17, false).ToSlot);
        Assert.Equal(InventoryRules.AnySlot, InventoryView.Drop(ContainerRef.Person, 0, 5, ContainerRef.Hold, 17, false).ToSlot);
        Assert.Equal(InventoryRules.AnySlot, InventoryView.Drop(ContainerRef.Person, 0, 5, ContainerRef.Stock(4), 17, false).ToSlot);
        Assert.Equal(17, InventoryView.Drop(ContainerRef.Hold, 3, 5, ContainerRef.Person, 17, false).ToSlot);
        Assert.Equal(InventoryRules.AnySlot, InventoryView.Drop(ContainerRef.Hold, 3, 5, ContainerRef.Person, InventoryRules.AnySlot, false).ToSlot);
    }

    [Fact]
    public void A_shift_click_goes_to_the_other_panel_or_back_to_the_bag()
    {
        var reactor = ContainerRef.Reactor(2, 1, 0, 0);
        Assert.Equal(new MoveItems(ContainerRef.Person, 4, reactor, 0, 0), InventoryView.QuickMove(ContainerRef.Person, 4, reactor));
        Assert.Equal(new MoveItems(ContainerRef.Person, 4, ContainerRef.Stock(3), -1, 0), InventoryView.QuickMove(ContainerRef.Person, 4, ContainerRef.Stock(3)));
        Assert.Equal(new MoveItems(ContainerRef.Person, 4, ContainerRef.Hold, -1, 0), InventoryView.QuickMove(ContainerRef.Person, 4, null)); // nothing beside the bag: the hold
        Assert.Equal(new MoveItems(ContainerRef.Person, 4, ContainerRef.Hold, -1, 0), InventoryView.QuickMove(ContainerRef.Person, 4, ContainerRef.MachineOut(9))); // an output takes nothing
        Assert.Equal(new MoveItems(ContainerRef.Person, 4, ContainerRef.MachineIn(9), -1, 0), InventoryView.QuickMove(ContainerRef.Person, 4, ContainerRef.MachineIn(9)));
        Assert.Equal(new MoveItems(ContainerRef.Hold, 7, ContainerRef.Person, -1, 0), InventoryView.QuickMove(ContainerRef.Hold, 7, reactor));
        Assert.Equal(new MoveItems(reactor, 0, ContainerRef.Person, -1, 0), InventoryView.QuickMove(reactor, 0, reactor));
        Assert.Equal(new MoveItems(ContainerRef.MachineOut(9), 2, ContainerRef.Person, -1, 0), InventoryView.QuickMove(ContainerRef.MachineOut(9), 2, null));
    }

    [Fact]
    public void Quick_moves_and_drops_do_what_they_say_in_a_world()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, 20261121UL, threats: false, stationRules: false, structures: true);
        int hydrogen = C.ItemIndex("core:hydrogen");
        var ship = w.Network.ShipStructure(0);
        var key = w.Network.GetStructure(ship).Modules.First(kv => ModuleKindIds.IsReactor(kv.Value)).Key;
        var reactor = ContainerRef.Reactor(ship, key.Mx, key.My, key.Mz);
        w.Network.GiveInventory(0, hydrogen, 12);
        w.Submit(0, InventoryView.QuickMove(ContainerRef.Person, 0, reactor));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        Assert.Equal(0, w.Network.InventoryTotal(0));
        w.Submit(0, InventoryView.QuickMove(reactor, 0, reactor));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        Assert.True(w.Network.InventoryCount(0, hydrogen) >= 11); // the reactor may have burned one meanwhile
        // Dropping on the bin works from the bag; dragging out of the bin does not exist.
        w.Submit(0, InventoryView.Drop(ContainerRef.Person, 0, 11, ContainerRef.Void, -1, half: true));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        Assert.True(InventoryView.CanDrop(ContainerRef.Person, ContainerRef.Void));
        Assert.False(InventoryView.CanDrop(ContainerRef.Void, ContainerRef.Person));
        Assert.False(InventoryView.CanDrop(ContainerRef.Person, ContainerRef.MachineOut(1)));
        Assert.False(InventoryView.CanDrop(ContainerRef.Hold, ContainerRef.Hold));
        Assert.True(InventoryView.CanDrop(ContainerRef.Person, ContainerRef.Person));
    }

    [Fact]
    public void Container_grids_pad_to_whole_rows_with_a_free_slot()
    {
        Assert.Equal(24, InventoryView.PaddedSlots(0, 6, 4)); // the bag: a fixed grid of four rows
        Assert.Equal(24, InventoryView.PaddedSlots(5, 6, 4));
        Assert.Equal(24, InventoryView.PaddedSlots(23, 6, 4));
        Assert.Equal(30, InventoryView.PaddedSlots(24, 6, 4)); // full: one more row to drop into
        Assert.Equal(12, InventoryView.PaddedSlots(6, 6, 1));
        Assert.Equal(6, InventoryView.PaddedSlots(0, 6, 1));
        Assert.All(new[] { 0, 1, 5, 6, 7, 11, 12, 13, 40 }, n => Assert.True(InventoryView.PaddedSlots(n, 6, 2) > n && InventoryView.PaddedSlots(n, 6, 2) % 6 == 0));
    }

    [Fact]
    public void Tooltips_name_the_item_its_stack_its_fuel_time_and_what_it_makes()
    {
        foreach (var lang in new[] { Language.Es, Language.En })
        {
            var before = Loc.Lang;
            Loc.Lang = lang;
            try
            {
                int ore = C.ItemIndex("core:iron_ore"), cell = C.ItemIndex("core:fuel_cell"), hydrogen = C.ItemIndex("core:hydrogen"), ammo = C.ItemIndex("core:ammo");
                string t = InventoryView.Tooltip(C, ore, 40, 100, 900);
                Assert.DoesNotContain("⟦", t);
                Assert.Contains("x40", t);
                Assert.Contains("100", t);
                Assert.Contains(Loc.Name("item", "core:iron_ingot"), t); // iron ore is smelted into ingots
                string h = InventoryView.Tooltip(C, hydrogen, 0, 100, 900);
                Assert.Contains("0:30", h); // 900 ticks at 30 per second
                Assert.DoesNotContain("x0", h);
                Assert.Contains("3:00", InventoryView.Tooltip(C, cell, 3, 20, 900)); // a fuel cell burns six intervals
                Assert.Contains(Loc.Name("machine", "core:turret"), InventoryView.Tooltip(C, ammo, 1, 100, 900));
                foreach (var item in C.Items) Assert.DoesNotContain("⟦", InventoryView.Tooltip(C, item.Index, 1, C.StackSizeOf(item.Index), 900));
            }
            finally { Loc.Lang = before; }
        }
    }

    [Fact]
    public void The_reactor_lines_and_feed_choices_have_text_in_both_languages()
    {
        foreach (var source in InventoryView.FeedChoices)
        {
            Assert.True(Loc.Has(InventoryView.FeedKey(source)), source.ToString());
            Assert.True(Loc.Has(InventoryView.FeedKey(source) + ".tip"), source.ToString());
        }
        Assert.Equal(4, InventoryView.FeedChoices.Distinct().Count());
        Assert.Equal(Enum.GetValues<FeedSource>().Length, InventoryView.FeedChoices.Length);
        var cold = new ReactorFuelInfo(-1, 0, 0, 0, 0, FeedSource.Hold);
        var warm = new ReactorFuelInfo(0, 4, 100, 100, 3700, FeedSource.Hold);
        Assert.Equal(Loc.T("inv.reactor.cold"), InventoryView.BurnLine(cold));
        Assert.Contains("2:04", InventoryView.BurnLine(warm));
        string[] keys =
        {
            "inv.title", "inv.you", "inv.slots", "inv.empty", "inv.sort", "inv.sort.tip", "inv.bin", "inv.bin.tip", "inv.bin.confirm", "inv.bin.yes", "inv.bin.no", "inv.hint", "inv.hint.keys",
            "inv.picked", "inv.close", "inv.legacy", "inv.nothing", "inv.out", "inv.tip.stack", "inv.tip.fuel", "inv.tip.usedFor", "inv.tip.usedBy", "inv.tab.hold", "inv.tab.stock",
            "inv.tab.reactor", "inv.tab.reactorN", "inv.tab.machine", "inv.hold.title", "inv.stock.title", "inv.stock.none", "inv.machine.title", "inv.machine.in", "inv.machine.out",
            "inv.machine.pick", "inv.reactor.title", "inv.reactor.slot", "inv.reactor.burn", "inv.reactor.cold", "inv.reactor.first", "inv.feed.title", "inv.toast.fed", "inv.toast.sorted",
            "inv.toast.binned", "inv.toast.moved", "inv.toast.noOther", "toast.reject.wrongItem", "key.inventory", "key.ctx.inventory", "key.invDrag", "key.invQuick", "key.invKeys",
            "key.invClose", "help.inventory", "icon.tool:inventory", "icon.tool:sort", "icon.tool:bin",
        };
        foreach (var k in keys) Assert.True(Loc.Has(k), k);
        foreach (var (key, value) in Loc.All.Where(p => p.Key.StartsWith("inv.") || p.Key.StartsWith("key.inv")))
        {
            Assert.False(string.IsNullOrWhiteSpace(value.Es), key);
            Assert.False(string.IsNullOrWhiteSpace(value.En), key);
        }
    }

    [Fact]
    public void The_key_panel_has_an_inventory_context_and_rows()
    {
        var openState = new KeyState { InventoryOpen = true };
        var open = KeyHints.For(openState);
        Assert.Equal(HintContext.Inventory, KeyHints.ContextOf(openState));
        Assert.Contains(open.SelectMany(g => g.Rows), r => r.Id == "close" && r.ActionKey == "Tab" && r.State == KeyRowState.Active);
        Assert.Contains(open.SelectMany(g => g.Rows), r => r.Id == "invQuick");
        foreach (var state in new[] { new KeyState(), new KeyState { Standing = true } })
            Assert.Contains(KeyHints.For(state).SelectMany(g => g.Rows), r => r.Id == "computer" && r.ActionKey == "Tab" && r.Clickable); // the inventory opens from the computer (or its own key I)
        foreach (var row in open.SelectMany(g => g.Rows)) Assert.True(Loc.Has(row.LabelKey), row.LabelKey);
        Assert.Equal(HintContext.BuildMenu, KeyHints.ContextOf(new KeyState { InventoryOpen = true, BuildOpen = true })); // an older panel keeps priority if both are somehow open
    }
}
