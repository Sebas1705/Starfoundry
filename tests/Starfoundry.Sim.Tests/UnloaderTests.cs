using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class UnloaderTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Unloader = C.MachineIndex("core:unloader");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Plate = C.ItemIndex("core:iron_plate");
    private static readonly UnloaderDef Def = C.Machines[Unloader].Unloader!;

    private static SimWorld World() => new(C); // sandbox: no costs, locks or threats

    private static int Ok(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>An unloader owned by the given player with a four-cell belt to the east; returns (unloader, belt).</summary>
    private static (int U, int Belt) WithBelt(SimWorld w, bool sink, int player = 0)
    {
        int u = Ok(w, new PlaceMachine(5, 0, 5, Unloader, -1), player);
        int belt = Ok(w, new PlaceSegment(6, 0, 5, Dir.PosX, 4, BeltTier.Mk1), player);
        Ok(w, new ConnectFromMachine(u, belt), player);
        if (sink) Ok(w, new SetSink(belt), player);
        return (u, belt);
    }

    [Fact]
    public void TheShippedUnloaderCarriesIngotsAtTwoASecondAndKeepsAReserve()
    {
        Assert.Equal(MachineRole.Unloader, C.Machines[Unloader].Role);
        Assert.Equal(Ingot, Def.Item);
        Assert.Equal(30, Def.IntervalTicks);
        Assert.True(Def.Reserve > 0, "a reserve keeps the player able to build");
        Assert.True(Def.Reserve < PlayerRules.DepotItemCap, "the reserve is below what depots deliver, so a depot-fed hold can feed it");
    }

    [Fact]
    public void ItIsLockedUntilTheJumpDriveIsResearched()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261120UL);
        w.Network.EnsurePlayer(0);
        Assert.True(w.Network.IsMachineLocked(Unloader));
        w.Network.GrantResearch(C.ResearchIndex("core:jump_drive"));
        Assert.False(w.Network.IsMachineLocked(Unloader));
    }

    [Fact]
    public void ItMovesIngotsFromTheHoldToTheBeltDownToTheReserve()
    {
        var w = World();
        w.Network.GiveItems(0, Ingot, 40);
        var (_, belt) = WithBelt(w, sink: true);
        w.Step(30 * 120);

        Assert.Equal(Def.Reserve, w.Network.HoldCount(0, Ingot));
        Assert.Equal(40 - Def.Reserve, w.Network.PassedBy(belt));
        w.ValidateInvariants(); // hold accounting and item conservation both hold
    }

    [Fact]
    public void ItNeverTakesWhatTheBeltCannotCarry()
    {
        var w = World();
        w.Network.GiveItems(0, Ingot, 60);
        var (_, belt) = WithBelt(w, sink: false); // the belt fills up and stops accepting
        w.Step(30 * 120);

        int onBelt = w.Network.CountOn(belt);
        Assert.True(onBelt > 0);
        Assert.Equal(60 - onBelt, w.Network.HoldCount(0, Ingot));
        Assert.Equal(0, w.Network.PassedBy(belt));
        w.ValidateInvariants();
    }

    [Fact]
    public void ItTakesFromItsOwnersHoldAndOnlyThat()
    {
        var w = World();
        w.Network.EnsurePlayer(1);
        w.Network.GiveItems(0, Ingot, 40);
        w.Network.GiveItems(1, Ingot, 15);
        var (_, belt) = WithBelt(w, sink: true, player: 1);
        w.Step(30 * 120);

        Assert.Equal(40, w.Network.HoldCount(0, Ingot));       // the other player's hold is untouched
        Assert.Equal(Def.Reserve, w.Network.HoldCount(1, Ingot));
        Assert.Equal(15 - Def.Reserve, w.Network.PassedBy(belt));
    }

    [Fact]
    public void ItCarriesOnlyItsItemAndNeedsABelt()
    {
        var w = World();
        w.Network.GiveItems(0, Plate, 40); // plates are not what it carries
        var (u, belt) = WithBelt(w, sink: true);
        w.Step(30 * 60);
        Assert.Equal(40, w.Network.HoldCount(0, Plate));
        Assert.Equal(0, w.Network.PassedBy(belt));

        // Without any belt it does nothing at all and the hold is untouched.
        var lone = World();
        lone.Network.GiveItems(0, Ingot, 40);
        Ok(lone, new PlaceMachine(5, 0, 5, Unloader, -1));
        lone.Step(30 * 60);
        Assert.Equal(40, lone.Network.HoldCount(0, Ingot));
    }

    [Fact]
    public void ABeltCannotFeedAnUnloader()
    {
        var w = World();
        int u = Ok(w, new PlaceMachine(5, 0, 5, Unloader, -1));
        int belt = Ok(w, new PlaceSegment(2, 0, 5, Dir.PosX, 3, BeltTier.Mk1));
        w.Submit(0, new ConnectToMachine(belt, u));
        w.Step();
        Assert.Equal(Reject.WrongRole, w.LastResults.Single().Reject);
    }

    [Fact]
    public void AnUnloaderFeedsAFactoryFromTheHoldAndRunsDeterministically()
    {
        // Hold ingots go to an assembler that makes plates: the remote-supply case, a hold-fed line with no deposit at all.
        ulong Run(out long plates)
        {
            var w = World();
            w.Network.GiveItems(0, Ingot, 60);
            int u = Ok(w, new PlaceMachine(0, 0, 0, Unloader, -1));
            int b1 = Ok(w, new PlaceSegment(1, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
            int asm = Ok(w, new PlaceMachine(4, 0, 0, C.MachineIndex("core:assembler"), C.RecipeIndex("core:iron_plate")));
            int b2 = Ok(w, new PlaceSegment(5, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
            Ok(w, new ConnectFromMachine(u, b1));
            Ok(w, new ConnectToMachine(b1, asm));
            Ok(w, new ConnectFromMachine(asm, b2));
            Ok(w, new SetSink(b2));
            w.Step(30 * 300);
            w.ValidateInvariants();
            plates = w.Network.PassedBy(b2);
            return w.Checksum();
        }
        ulong a = Run(out long plates), b = Run(out _);
        Assert.Equal(a, b);
        Assert.InRange(plates, (60 - Def.Reserve) / 2 - 1, (60 - Def.Reserve) / 2); // two ingots per plate, nothing below the reserve is touched
    }

    [Fact]
    public void UnloaderContentIsValidated()
    {
        const string head = """{"schema":1,"items":[{"id":"t:a"}],"machines":[""";
        const string ok = """{"id":"t:u","role":"unloader","unloader":{"item":"t:a","interval_ticks":10,"reserve":5}}""";
        var c = ContentDb.FromJson(head + ok + """],"recipes":[]}""");
        Assert.Equal(new UnloaderDef(0, 10, 5), c.Machines[0].Unloader);
        Assert.NotEqual(c.Hash, ContentDb.FromJson(head + ok.Replace("\"reserve\":5", "\"reserve\":6") + """],"recipes":[]}""").Hash);

        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + """{"id":"t:u","role":"unloader"}""" + """],"recipes":[]}"""));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"item\":\"t:a\"", "\"item\":\"t:zz\"") + """],"recipes":[]}"""));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"role\":\"unloader\"", "\"role\":\"wall\"") + """],"recipes":[]}"""));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"interval_ticks\":10", "\"interval_ticks\":0") + """],"recipes":[]}"""));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"reserve\":5", "\"reserve\":61") + """],"recipes":[]}"""));
    }
}
