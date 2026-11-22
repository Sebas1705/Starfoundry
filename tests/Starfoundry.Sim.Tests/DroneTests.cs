using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Drone bays: automated mining of an assigned deposit for the structure's owner.</summary>
public class DroneTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private const ulong Seed = 20261121UL;
    private const int Interval = 36;

    private static SimWorld World(GameMode mode = GameMode.Sandbox, bool emergency = true) =>
        SimWorld.CreateGalaxy(emergency ? C : TestContent.CoreWithoutEmergencyPower, mode, Seed, threats: false, stationRules: false, structures: true);

    /// <summary>A powered ship (reactor, enough hydrogen for several minutes) with the given number of drone bays and one deposit assigned.</summary>
    private static (SimWorld World, int Ship, int Deposit) Setup(int bays = 1, int density = 100, int amount = 100_000, int hydrogen = 6, bool emergency = true)
    {
        var w = World(emergency: emergency);
        int s = PowerTests.Ship(w, reactors: 1, hydrogen: hydrogen);
        for (int i = 0; i < bays; i++) w.Network.AddStructureModuleUnchecked(s, 10 + i, 0, 0, ModuleKind.DroneBay);
        int d = w.Network.AddDeposit(Ore, amount, 0, 0, 0, density: density);
        Assert.True(Assign(w, s, d).Ok);
        return (w, s, d);
    }

    private static CommandResult Assign(SimWorld w, int s, int d)
    {
        w.Submit(0, new AssignDrones(s, d));
        w.Step();
        return w.LastResults.Single();
    }

    [Fact]
    public void Bay_mines_the_assigned_deposit_at_the_expected_rate()
    {
        var (w, s, d) = Setup();
        int before = w.Network.HoldCount(0, Ore); // the assigning tick already ran one step of mining
        w.Step(10 * Interval);
        Assert.Equal(before + 10, w.Network.HoldCount(0, Ore));
        Assert.Equal(100_000 - w.Network.HoldCount(0, Ore), w.Network.DepositRemaining(d));
        var st = w.Network.DroneStatus(s);
        Assert.Equal(1, st.Bays);
        Assert.True(st.Working);
        Assert.True(st.PowerOk);
        Assert.False(st.WaitingForHold);
        Assert.Equal(50, st.RatePerMinute); // 60 s * 30 ticks / 36 ticks
        Assert.Equal(d, w.Network.AssignedDeposit(s));
        w.ValidateInvariants();
    }

    [Theory]
    [InlineData(25, 2)]
    [InlineData(50, 5)]
    [InlineData(100, 10)]
    [InlineData(200, 20)]
    public void Density_scales_the_rate(int density, int expected)
    {
        var (w, s, _) = Setup(density: density);
        w.Step(10 * Interval - 1);
        Assert.Equal(expected, w.Network.HoldCount(0, Ore));
        Assert.Equal(50 * density / 100, w.Network.DroneStatus(s).RatePerMinute); // 12.5 at density 25 truncates to 12
    }

    [Fact]
    public void Two_bays_double_the_rate()
    {
        var (w, s, _) = Setup(bays: 2);
        w.Step(10 * Interval - 1);
        Assert.Equal(20, w.Network.HoldCount(0, Ore));
        Assert.Equal(100, w.Network.DroneStatus(s).RatePerMinute);
    }

    [Fact]
    public void Power_zero_stops_the_bay()
    {
        var (w, s, _) = Setup(hydrogen: 0, emergency: false); // a real blackout: the shipped emergency output would keep the bay (20) at 75 percent
        w.Step(5 * Interval);
        Assert.Equal(0, w.Network.HoldCount(0, Ore));
        var st = w.Network.DroneStatus(s);
        Assert.False(st.PowerOk);
        Assert.False(st.Working);
        Assert.Equal(0, st.RatePerMinute);
        // Fuel arrives: it works again.
        w.Network.GiveItems(0, Hydrogen, 2);
        w.Step(2 * Interval);
        Assert.True(w.Network.HoldCount(0, Ore) >= 1);
    }

    [Fact]
    public void Hold_limit_makes_the_bay_wait()
    {
        var (w, s, _) = Setup();
        // The hold is 100 items; hand mining and drones stop at 75 percent (75).
        int limit = w.Network.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent / 100;
        w.Network.GiveItems(0, Ore, limit - 1 - w.Network.HoldTotal(0) + w.Network.HoldCount(0, Hydrogen) * 0);
        int start = w.Network.HoldTotal(0);
        w.Step(5 * Interval);
        Assert.Equal(limit, w.Network.HoldTotal(0)); // exactly one item fitted, then it waits
        Assert.True(start < limit);
        var st = w.Network.DroneStatus(s);
        Assert.True(st.WaitingForHold);
        Assert.False(st.Working);
        // Room appears: the waiting bay delivers at once.
        w.Network.Pay(0, new[] { new ItemStack(Ore, 5) });
        w.Step();
        Assert.Equal(limit - 5 + 1, w.Network.HoldTotal(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void Out_of_range_drops_the_assignment()
    {
        var (w, s, _) = Setup();
        long cu = Units.CellUnits;
        w.Network.SetStructureOrigin(s, 200 * cu, 0, 0);
        w.Step();
        Assert.Equal(-1, w.Network.AssignedDeposit(s));
        int mined = w.Network.HoldCount(0, Ore);
        w.Step(3 * Interval);
        Assert.Equal(mined, w.Network.HoldCount(0, Ore));
        Assert.False(w.Network.DroneStatus(s).Working);
        w.ValidateInvariants();
    }

    [Fact]
    public void Assign_requires_a_revealed_deposit_in_range()
    {
        var w = World();
        int s = PowerTests.Ship(w, hydrogen: 3);
        int hidden = w.Network.AddDeposit(Ore, 100, 0, 0, 0, revealed: false);
        int far = w.Network.AddDeposit(Ore, 100, 300, 0, 0);
        int edge = w.Network.AddDeposit(Ore, 100, 129, 0, 0); // the drone range is 130 cells (ADR 0028)
        int near = w.Network.AddDeposit(Ore, 100, 131, 0, 0);

        Assert.Equal(Reject.NoDroneBay, Assign(w, s, edge).Reject);
        w.Network.AddStructureModuleUnchecked(s, 10, 0, 0, ModuleKind.DroneBay);
        Assert.Equal(Reject.NotDiscovered, Assign(w, s, hidden).Reject);
        Assert.Equal(Reject.OutOfRange, Assign(w, s, far).Reject);
        Assert.Equal(Reject.OutOfRange, Assign(w, s, near).Reject);
        Assert.Equal(Reject.UnknownDeposit, Assign(w, s, 999).Reject);
        Assert.Equal(Reject.NoSuchStructure, Assign(w, 99, edge).Reject);
        Assert.Equal(Reject.NoSuchStructure, Assign(w, 0, edge).Reject);
        Assert.Equal(-1, w.Network.AssignedDeposit(s));
        Assert.True(Assign(w, s, edge).Ok);
        Assert.Equal(edge, w.Network.AssignedDeposit(s));
        Assert.True(Assign(w, s, -1).Ok);
        Assert.Equal(-1, w.Network.AssignedDeposit(s));
    }

    [Fact]
    public void Assign_is_refused_without_the_structure_rules_and_for_other_owners()
    {
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: false);
        int d = legacy.Network.AddDeposit(Ore, 100);
        Assert.Equal(Reject.NotAllowedHere, Assign(legacy, 1, d).Reject);

        var w = World();
        int s = PowerTests.Ship(w);
        w.Network.AddStructureModuleUnchecked(s, 10, 0, 0, ModuleKind.DroneBay);
        int d2 = w.Network.AddDeposit(Ore, 100);
        w.Submit(1, new AssignDrones(s, d2));
        w.Step();
        Assert.Equal(Reject.NotAllowedHere, w.LastResults.Single().Reject);
    }

    [Fact]
    public void Drone_bay_locked_until_researched()
    {
        var content = TestContent.Core;
        var w = SimWorld.CreateGalaxy(content, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true);
        int ship = w.Network.ShipStructure(0);
        foreach (var c in content.Structures!.ModuleCost(ModuleKind.DroneBay)) w.Network.GiveItems(0, c.Item, c.Count);
        w.Submit(0, new PlaceModuleIn(ship, 0, 0, 5, ModuleKind.DroneBay));
        w.Step();
        Assert.Equal(Reject.Locked, w.LastResults.Single().Reject);
        Assert.Equal(0, w.Network.DroneBays(ship));

        w.Network.GrantResearch(content.ResearchIndex("core:drone_automation"));
        w.Submit(0, new PlaceModuleIn(ship, 0, 0, 5, ModuleKind.DroneBay));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        Assert.Equal(1, w.Network.DroneBays(ship));
        // The cost was paid.
        foreach (var c in content.Structures.ModuleCost(ModuleKind.DroneBay)) Assert.Equal(0, w.Network.HoldCount(0, c.Item));

        // The sandbox needs no research.
        var sb = SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        sb.Submit(0, new PlaceModuleIn(sb.Network.ShipStructure(0), 0, 0, 5, ModuleKind.DroneBay));
        sb.Step();
        Assert.True(sb.LastResults.Single().Ok);
    }

    [Fact]
    public void Drone_state_roundtrips_replay_and_save()
    {
        var content = TestContent.Core;
        SimWorld Fresh() => SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        var a = Fresh();
        int ship = a.Network.ShipStructure(0);
        a.Submit(0, new PlaceModuleIn(ship, 0, 0, 5, ModuleKind.DroneBay));
        a.Submit(0, new PlaceModuleIn(ship, 0, 0, 6, ModuleKind.DroneBay));
        a.Step();
        int dep = Enumerable.Range(0, a.Network.DepositCount)
            .First(i => a.Network.IsDepositRevealed(i) && a.Network.DepositInDroneRange(ship, i));
        a.Submit(0, new AssignDrones(ship, dep));
        a.Step();
        Assert.All(a.LastResults, r => Assert.True(r.Ok, r.Reject.ToString()));
        a.Step(1234); // mid-interval, with leftover progress

        var bytes = new MemoryStream();
        SaveGame.Write(bytes, a, content, GameMode.Sandbox, Seed, threats: false, player: 0, structures: true);
        var loaded = SaveGame.Read(new MemoryStream(bytes.ToArray()), content, out _);
        Assert.Equal(a.Checksum(), loaded.Checksum());
        Assert.Equal(a.Network.DroneStatus(ship), loaded.Network.DroneStatus(ship));
        loaded.ValidateInvariants();
        a.Step(3000);
        loaded.Step(3000);
        Assert.Equal(a.Checksum(), loaded.Checksum());
        Assert.True(a.Network.MinedByDrones > 30, $"{a.Network.MinedByDrones} {a.Network.DroneStatus(ship)} {a.Network.PowerStatus(ship)} hold {a.Network.HoldTotal(0)}/{a.Network.HoldCapacity(0)}"); // the starter ship's ore port moves the ore on, so count what was mined

        Assert.Equal(a.Checksum(), a.Network.Clone().Checksum());
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, a.Log, content.Hash, GameMode.Sandbox, Seed, threats: false, structures: true);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out _, out _, out _, out _, out _);
        var b = SimWorld.Replay(log, a.Tick, Fresh());
        Assert.Equal(a.Checksum(), b.Checksum());
        b.ValidateInvariants();
    }

    [Fact]
    public void Codec_roundtrips_assign_drones()
    {
        foreach (var cmd in new Command[] { new AssignDrones(3, 7), new AssignDrones(1, -1), new PlaceModuleIn(1, 2, 3, 4, ModuleKind.DroneBay) })
        {
            Assert.True(CommandCodec.IsWellFormed(cmd));
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) CommandCodec.Write(w, cmd);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            Assert.Equal(cmd, CommandCodec.Read(r));
        }
    }

    [Fact]
    public void Legacy_worlds_unchanged()
    {
        // The same scenario as the power test: a world without structure rules hashes as before drones existed.
        var c = ContentDb.FromJson(System.Text.RegularExpressions.Regex.Replace("""
        {"schema":1,
         "items":[{"id":"t:a"},{"id":"t:b"}],
         "machines":[{"id":"t:m"},{"id":"t:drill","role":"drill"},{"id":"t:dep","role":"depot"}],
         "recipes":[{"id":"t:r","machine":"t:m","inputs":[{"item":"t:a","count":1}],"outputs":[{"item":"t:b","count":1}],"time_s":1},
                    {"id":"t:dr","machine":"t:drill","inputs":[],"outputs":[{"item":"t:a","count":1}],"time_s":1}]}
        """, @"\s+", ""));
        var net = new BeltNetwork(c);
        int dep = net.AddDeposit(0, 500, 0, 0, 0);
        int drill = net.AddMachine(0, 0, 0, 1, 1, owner: 0);
        net.LinkDeposit(drill, dep);
        int m = net.AddMachine(5, 0, 0, 0, 0, owner: 0);
        int depot = net.AddMachine(10, 0, 0, 2, -1, owner: 0);
        int s1 = net.AddSegment(1, 0, 0, 1, 0, 0, 4, BeltTier.Mk2);
        int s2 = net.AddSegment(6, 0, 0, 1, 0, 0, 4, BeltTier.Mk2);
        net.ConnectFromMachine(drill, s1); net.ConnectToMachine(s1, m);
        net.ConnectFromMachine(m, s2); net.ConnectToMachine(s2, depot);
        net.Step(600);
        Assert.Equal(6617541375898655779UL, net.Checksum());

        // Drone bays in a structure without the structure rules do nothing: the deposit is untouched.
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: false);
        int ship = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(ship, 0, 0, 0, ModuleKind.DroneBay);
        int d = w.Network.AddDeposit(Ore, 100, 0, 0, 0);
        w.Step(500);
        Assert.Equal(100, w.Network.DepositRemaining(d));
        Assert.False(w.Network.DroneStatus(ship).Working);
    }

    [Fact]
    public void Bay_does_not_deplete_below_zero()
    {
        var (w, s, d) = Setup(bays: 3, amount: 4, density: 200);
        w.Step(2000);
        Assert.Equal(0, w.Network.DepositRemaining(d));
        Assert.Equal(4, w.Network.HoldCount(0, Ore));
        Assert.Equal(-1, w.Network.AssignedDeposit(s)); // an empty deposit is unassigned
        Assert.Equal(0, w.Network.DroneStatus(s).RatePerMinute);
        w.ValidateInvariants();
    }

    [Fact]
    public void Removing_the_last_bay_clears_the_assignment_and_assigning_draws_power()
    {
        var (w, s, _) = Setup();
        Assert.Equal(20, w.Network.PowerStatus(s).Demand); // one bay at 20 while a deposit is assigned
        w.Submit(0, new RemoveModuleIn(s, 10, 0, 0));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, w.LastResults.Single().Reject.ToString());
        Assert.Equal(-1, w.Network.AssignedDeposit(s));
        Assert.Equal(0, w.Network.DroneBays(s));
        w.Step();
        Assert.Equal(0, w.Network.PowerStatus(s).Demand);
        w.ValidateInvariants();
    }
}
