using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Dropping items onto the ground and picking them up by hand (ADR 0038): reach, bag then hold, expiry, the codec, the log gate and the checksum.</summary>
public class DropTests
{
    private const int Cell = Units.CellUnits;
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");

    private static SimWorld NewWorld(WorldRules? rules = null) =>
        SimWorld.CreateGalaxy(C, GameMode.Sandbox, 20261121UL, threats: false, stationRules: false, structures: true, rules: rules);

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    private static (int X, int Y, int Z) Here(SimWorld w) { var p = w.Network.GetPlayerInfo(0); return (p.X, p.Y, p.Z); }

    private static DropItems DropBag(SimWorld w, int slot, int count, int dx = 0)
    {
        var (x, y, z) = Here(w);
        return new DropItems(ContainerRef.Person, slot, count, x + dx, y, z);
    }

    private static int HoldItem(SimWorld w) => Enumerable.Range(0, C.Items.Count).First(i => w.Network.HoldCount(0, i) > 0);

    [Fact]
    public void A_dropped_stack_lies_on_the_ground_and_a_pickup_takes_it_back_into_the_bag()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 30);
        Assert.Equal(Reject.None, Run(w, DropBag(w, 0, 12)));
        Assert.Equal(18, w.Network.InventoryStack(0, 0).Count);
        var k = Assert.Single(w.Network.Pickups());
        Assert.True(k.Dropped);
        Assert.Equal((Ore, 12), (k.Item, k.Count));

        var (x, y, z) = Here(w);
        Assert.Equal(Reject.None, Run(w, new PickUp(k.Id, false, x, y, z)));
        Assert.Empty(w.Network.Pickups());
        Assert.Equal(30, w.Network.InventoryStack(0, 0).Count);
        w.ValidateInvariants();
    }

    [Fact]
    public void Drops_validate_the_slot_the_count_and_the_container()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 5);
        Assert.Equal(Reject.NotEnoughItems, Run(w, DropBag(w, 0, 6)));
        Assert.Equal(Reject.NotEnoughItems, Run(w, DropBag(w, 3, 1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, DropBag(w, 99, 1)));
        var (x, y, z) = Here(w);
        Assert.Equal(Reject.InvalidArgument, Run(w, new DropItems(ContainerRef.Stock(1), 0, 1, x, y, z)));
        Assert.Equal(Reject.OutOfRange, Run(w, new DropItems(ContainerRef.Person, 0, 1, x + 100 * Cell, y, z)));
        Assert.Empty(w.Network.Pickups());
        Assert.Equal(Reject.None, Run(w, DropBag(w, 0, 0))); // zero drops the whole slot
        Assert.Null(w.Network.InventoryStack(0, 0));
    }

    [Fact]
    public void Stacks_of_one_item_merge_and_can_come_from_the_hold()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 10);
        Assert.Equal(Reject.None, Run(w, DropBag(w, 0, 4)));
        Assert.Equal(Reject.None, Run(w, DropBag(w, 0, 3, dx: 20)));
        Assert.Equal(7, Assert.Single(w.Network.Pickups()).Count);
        w.Network.GiveInventory(0, Ingot, 1);
        Assert.Equal(Reject.None, Run(w, DropBag(w, 1, 1, dx: 20)));
        Assert.Equal(2, w.Network.Pickups().Count);

        int held = w.Network.HoldTotal(0);
        var (x, y, z) = Here(w);
        Assert.Equal(Reject.None, Run(w, new DropItems(ContainerRef.Hold, HoldItem(w), 1, x, y, z)));
        Assert.Equal(held - 1, w.Network.HoldTotal(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void Reach_on_foot_is_three_cells_and_seated_is_the_tractor_range()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 10);
        Assert.Equal(Reject.None, Run(w, DropBag(w, 0, 10, dx: 5 * Cell)));
        int id = w.Network.Pickups()[0].Id;
        var (x, y, z) = Here(w);
        Assert.Equal(Reject.OutOfRange, Run(w, new PickUp(id, false, x, y, z)));
        Assert.Equal(Reject.None, Run(w, new PickUp(id, true, x, y, z))); // within six cells of the hull
        Assert.Empty(w.Network.Pickups());

        w.Network.GiveInventory(0, Ore, 10);
        Assert.Equal(Reject.None, Run(w, DropBag(w, 0, 10, dx: 30 * Cell)));
        Assert.Equal(Reject.OutOfRange, Run(w, new PickUp(w.Network.Pickups()[0].Id, true, x, y, z)));
        Assert.Equal(Reject.InvalidArgument, Run(w, new PickUp(999, true, x, y, z)));
    }

    [Fact]
    public void A_full_bag_fills_the_hold_and_at_most_sixty_four_stacks_lie_about()
    {
        var w = NewWorld();
        for (int i = 0; i < InventoryRules.Slots; i++) w.Network.GiveInventory(0, Ore, 100);
        Assert.Equal(0, w.Network.GiveInventory(0, Ingot, 1));
        var (x, y, z) = Here(w);
        int item = HoldItem(w), n = w.Network.HoldCount(0, item);
        Assert.Equal(Reject.None, Run(w, new DropItems(ContainerRef.Hold, item, 0, x, y, z)));
        var k = w.Network.Pickups()[0];
        Assert.Equal(Reject.None, Run(w, new PickUp(k.Id, false, x, y, z))); // the bag is full of ore and has no room for it: it goes to the hold
        Assert.InRange(w.Network.HoldCount(0, item), n - 2, n); // a reactor may burn a cell meanwhile
        w.ValidateInvariants();

        for (int i = 0; i < FeelRules.MaxPickups; i++) Assert.Equal(Reject.None, Run(w, new DropItems(ContainerRef.Person, 0, 1, x + (i % 8) * 300, y + (i / 8) * 300, z)));
        Assert.Equal(Reject.NoRoom, Run(w, new DropItems(ContainerRef.Person, 0, 1, x, y - 300, z)));
    }

    [Fact]
    public void A_dropped_stack_lasts_thirty_minutes_then_fades()
    {
        var w = NewWorld();
        w.Network.GiveInventory(0, Ore, 3);
        Run(w, DropBag(w, 0, 3));
        for (int i = 0; i < FeelRules.PickupLifeTicks + 100; i++) w.Step(); // an ore pickup would be gone by now
        Assert.Single(w.Network.Pickups());
        for (int i = 0; i < FeelRules.DropLifeTicks; i++) w.Step();
        Assert.Empty(w.Network.Pickups());
    }

    [Fact]
    public void Dropped_stacks_are_cloned_and_hashed_only_when_present()
    {
        var plain = NewWorld();
        var a = NewWorld();
        Assert.Equal(plain.Checksum(), a.Checksum());
        a.Network.GiveInventory(0, Ore, 9);
        Run(a, DropBag(a, 0, 9));
        Assert.NotEqual(plain.Checksum(), a.Checksum());
        Assert.Equal(a.Network.Clone().Checksum(), a.Checksum());
        a.ValidateInvariants();
    }

    [Fact]
    public void The_commands_roundtrip_and_logs_before_version_19_refuse_them()
    {
        var drop = new DropItems(ContainerRef.Person, 3, 7, -120, 5000, 33);
        var pick = new PickUp(4, true, 1, -2, 3);
        Assert.Equal(drop, CommandCodec.FromBytes(CommandCodec.ToBytes(drop)));
        Assert.Equal(pick, CommandCodec.FromBytes(CommandCodec.ToBytes(pick)));
        var log = new List<LoggedCommand> { new(0, 0, drop), new(1, 0, new Batch(new Command[] { pick })) };
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Sandbox, 1UL, threats: false, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(21, bytes[4]);
        Assert.Equal(drop, CommandCodec.ReadLog(new MemoryStream(bytes))[0].Command);
        bytes[4] = 18;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(bytes)));
    }

    [Fact]
    public void A_legacy_inventory_world_rejects_them()
    {
        var w = NewWorld(WorldRules.Default.WithLegacyInventory(true));
        var (x, y, z) = Here(w);
        Assert.Equal(Reject.NotAllowedHere, Run(w, new DropItems(ContainerRef.Hold, 0, 1, x, y, z)));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new PickUp(0, true, x, y, z)));
    }
}
