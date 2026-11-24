using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The discard command: items leave the hold for good, so a full hold of spare ore can never lock the factory.</summary>
public class DumpTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Carbon = C.ItemIndex("core:carbon");
    private static readonly int Ice = C.ItemIndex("core:ice");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private const ulong Seed = 20261121UL;

    private static SimWorld World(GameMode mode = GameMode.Sandbox) =>
        SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: false, structures: true, rules: new WorldRules().WithLegacyFeel(true)); // the hard stop at the hold limit is the rule before ADR 0036

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    [Fact]
    public void Dumping_lowers_the_hold()
    {
        var w = World();
        PowerTests.Ship(w);
        w.Network.GiveItems(0, Carbon, 30);
        Assert.True(Run(w, new DumpItems(Carbon, 12)).Ok);
        Assert.Equal(18, w.Network.HoldCount(0, Carbon));
        Assert.Equal(18, w.Network.HoldTotal(0));
        w.ValidateInvariants();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(99)]
    public void Count_out_of_range_dumps_everything_held(int count)
    {
        var w = World();
        PowerTests.Ship(w);
        w.Network.GiveItems(0, Carbon, 30);
        w.Network.GiveItems(0, Ore, 7);
        Assert.True(Run(w, new DumpItems(Carbon, count)).Ok);
        Assert.Equal(0, w.Network.HoldCount(0, Carbon));
        Assert.Equal(7, w.Network.HoldCount(0, Ore)); // other items stay
        w.ValidateInvariants();
    }

    [Fact]
    public void Invalid_item_or_empty_hold_is_rejected()
    {
        var w = World();
        PowerTests.Ship(w);
        Assert.Equal(Reject.InvalidArgument, Run(w, new DumpItems(-1, 1)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new DumpItems(C.Items.Count, 1)).Reject);
        Assert.Equal(Reject.NotEnoughItems, Run(w, new DumpItems(Carbon, 1)).Reject);
    }

    [Theory]
    [InlineData(GameMode.Sandbox)]
    [InlineData(GameMode.Campaign)]
    public void Works_in_every_mode(GameMode mode)
    {
        var w = World(mode);
        PowerTests.Ship(w);
        w.Network.GiveItems(0, Carbon, 5);
        Assert.True(Run(w, new DumpItems(Carbon, 5)).Ok);
        Assert.Equal(0, w.Network.HoldCount(0, Carbon));
    }

    [Fact]
    public void Codec_roundtrip()
    {
        foreach (var c in new[] { new DumpItems(3, 9), new DumpItems(0, 0), new DumpItems(13, -1) })
        {
            byte[] bytes = CommandCodec.ToBytes(c);
            var back = Assert.IsType<DumpItems>(CommandCodec.FromBytes(bytes));
            Assert.Equal(c, back);
            Assert.Equal(bytes, CommandCodec.ToBytes(back));
        }
        var batch = new Batch(new Command[] { new DumpItems(1, 2), new StopMining() });
        Assert.Equal(CommandCodec.ToBytes(batch), CommandCodec.ToBytes(CommandCodec.FromBytes(CommandCodec.ToBytes(batch))));
    }

    [Fact]
    public void A_dump_survives_save_and_load()
    {
        // The starter ship carries 20 hydrogen; the test dumps 5 of it (a test-only choice) and replays the log.
        var core = TestContent.Core;
        var w = SimWorld.CreateGalaxy(core, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        w.Step(10);
        int h = core.ItemIndex("core:hydrogen");
        int before = w.Network.HoldCount(0, h);
        Run(w, new DumpItems(h, 5));
        w.Step(60);
        var ms = new MemoryStream();
        SaveGame.Write(ms, w, core, GameMode.Sandbox, Seed, threats: false, player: 0, stationRules: false, structures: true);
        var loaded = SaveGame.Read(new MemoryStream(ms.ToArray()), core, out _);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal(w.Network.HoldCount(0, h), loaded.Network.HoldCount(0, h));
        Assert.True(before - 5 >= loaded.Network.HoldCount(0, h));
        Assert.Contains(loaded.Log, l => l.Command is DumpItems);
    }

    [Fact]
    public void Mining_resumes_after_dumping()
    {
        var w = World();
        PowerTests.Ship(w); // no hydrogen: the reactor would burn one and free room
        int d = w.Network.AddDeposit(Ore, 100_000, 0, 0, 0, density: 200);
        int limit = w.Network.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent / 100;
        w.Network.GiveItems(0, Carbon, limit - w.Network.HoldTotal(0)); // a hold full of spare ore
        Assert.True(Run(w, new StartMining(d)).Ok);
        int before = w.Network.HoldCount(0, Ore);
        w.Step(300);
        Assert.Equal(before, w.Network.HoldCount(0, Ore)); // blocked at the limit

        Assert.True(Run(w, new DumpItems(Carbon, 0)).Ok);
        w.Step(300);
        Assert.True(w.Network.HoldCount(0, Ore) > before);
        w.ValidateInvariants();
    }

    [Fact]
    public void Key_pick_never_chooses_iron_or_fuel_and_keeps_the_jump_carbon()
    {
        var w = World();
        PowerTests.Ship(w);
        var n = w.Network;
        Assert.Null(DumpPick.Choose(n, 0));
        n.GiveItems(0, Ore, 20);
        n.GiveItems(0, Hydrogen, 5);
        n.GiveItems(0, Ice, 15);
        Assert.Null(DumpPick.Choose(n, 0)); // iron and hydrogen stay; ice is needed while fuel is low
        n.GiveItems(0, Carbon, 12);
        var pick = DumpPick.Choose(n, 0)!;
        Assert.Equal(Carbon, pick.Item);
        Assert.Equal(12 - C.JumpCost.Single(j => j.Item == Carbon).Count, pick.Count);
        n.GiveItems(0, Hydrogen, 20);
        Assert.Equal(Ice, DumpPick.Choose(n, 0)!.Item); // 15 ice beats 7 spare carbon once hydrogen is plentiful
    }
}
