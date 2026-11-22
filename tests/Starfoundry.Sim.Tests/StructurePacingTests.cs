using Starfoundry.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Pacing of the structure rules, checked by a tiny scripted player that uses only sim commands: it hand-mines the home iron
/// deposit from the starter ship and founds a station next to the ship as soon as the hold affords it. The measured numbers are
/// written up in docs/prototypes/structures.md. If a content change breaks a bound here, either the change or the target needs a
/// conscious decision.
/// </summary>
public class StructurePacingTests
{
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;

    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Plate = C.ItemIndex("core:iron_plate");

    private readonly ITestOutputHelper _out;
    public StructurePacingTests(ITestOutputHelper output) => _out = output;

    private static string Min(long ticks) => ticks < 0 ? "never" : $"{ticks} ticks, {ticks / (double)Tps:F0} s ({ticks / (double)Tps / 60:F1} min)";

    private static SimWorld World(bool threats = false) =>
        SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats, stationRules: false, structures: true);

    private static int HomeIron(SimWorld w) =>
        Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == Ore && w.Network.IsDepositRevealed(d));

    /// <summary>Flies the ship to the home deposit with sim commands only, then starts hand mining.</summary>
    private static void FlyAndMine(SimWorld w)
    {
        var net = w.Network;
        int deposit = HomeIron(w);
        var (ox, oy, oz) = net.DepositPosition(deposit);
        for (int i = 0; i < 20 * 60 * Tps && !net.InMineRange(0, deposit); i++)
        {
            var p = net.GetPlayerInfo(0);
            w.Submit(0, new SetMovement(Math.Sign(ox * (long)Cell - p.X), Math.Sign(oy * (long)Cell - p.Y), Math.Sign(oz * (long)Cell - p.Z)));
            w.Step();
        }
        Assert.True(net.InMineRange(0, deposit), "the ship never reached the deposit");
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(deposit));
        w.Step();
        Assert.True(w.LastResults.Last().Ok);
    }

    private sealed class Run
    {
        public long Mining, FirstOre = -1, FirstIngot = -1, FirstPlate = -1, CoreAffordable = -1, Founded = -1, FirstWave = -1;
        public int MaxHoldTotal, MaxSignature;
        public long LongestIngotStall;
        public int Ingots, Plates, Ore;
    }

    /// <summary>Mines from the first tick of mining; founds a station as soon as the hold affords it (when <paramref name="found"/>).</summary>
    private Run Play(int minutes, bool found = true, bool threats = false)
    {
        var w = World(threats);
        var net = w.Network;
        FlyAndMine(w);
        var r = new Run { Mining = w.Tick };
        long end = r.Mining + (long)minutes * 60 * Tps;
        var cost = C.Structures!.ModuleCost(ModuleKind.Core);
        int homeSystem = net.CurrentSystem(0);
        int lastIngots = 0;
        long lastGain = w.Tick;
        while (w.Tick < end)
        {
            w.Step();
            long t = w.Tick - r.Mining;
            if (r.FirstOre < 0 && net.HoldCount(0, Ore) > 0) r.FirstOre = t;
            if (r.FirstIngot < 0 && net.HoldCount(0, Ingot) > 0) r.FirstIngot = t;
            if (r.FirstPlate < 0 && net.HoldCount(0, Plate) > 0) r.FirstPlate = t;
            r.MaxHoldTotal = Math.Max(r.MaxHoldTotal, net.GetPlayerInfo(0).HoldTotal);
            r.MaxSignature = Math.Max(r.MaxSignature, net.SystemSignature(homeSystem));
            int ingots = net.HoldCount(0, Ingot);
            if (ingots > lastIngots) { r.LongestIngotStall = Math.Max(r.LongestIngotStall, w.Tick - lastGain); lastGain = w.Tick; }
            lastIngots = ingots;
            if (r.CoreAffordable < 0 && net.CanPay(0, cost)) r.CoreAffordable = t;
            if (r.FirstWave < 0 && threats && net.GetSystemThreat(homeSystem).Wave > 0) r.FirstWave = t;
            if (found && r.Founded < 0 && r.CoreAffordable >= 0)
            {
                var s = net.GetStructure(net.ShipStructure(0));
                w.Submit(0, new FoundStation((int)(s.OriginX / Cell) + 12, (int)(s.OriginY / Cell), (int)(s.OriginZ / Cell)));
                w.Step();
                if (w.LastResults.Last().Ok) r.Founded = w.Tick - r.Mining;
            }
        }
        r.Ingots = net.HoldCount(0, Ingot); r.Plates = net.HoldCount(0, Plate); r.Ore = net.HoldCount(0, Ore);
        _out.WriteLine($"first ore {Min(r.FirstOre)}; first ingot {Min(r.FirstIngot)}; first plate {Min(r.FirstPlate)}; core affordable {Min(r.CoreAffordable)}; station founded {Min(r.Founded)}; first wave {Min(r.FirstWave)}");
        _out.WriteLine($"max hold {r.MaxHoldTotal}/{net.HoldCapacity(0)}; max signature {r.MaxSignature}; longest ingot stall {r.LongestIngotStall} ticks; ore {r.Ore} ingots {r.Ingots} plates {r.Plates}");
        w.ValidateInvariants();
        return r;
    }

    [Fact]
    public void Mining_stops_at_the_hold_limit_and_leaves_room_for_output()
    {
        var w = World();
        var net = w.Network;
        FlyAndMine(w);
        int capacity = net.HoldCapacity(0);
        Assert.Equal(100, capacity);
        int limit = capacity * PlayerRules.MiningHoldLimitPercent / 100;
        Assert.Equal(75, limit);
        long mined = net.MinedByHand;
        int maxTotal = 0, blocked = 0; long minedAtLimit = 0;
        for (int i = 0; i < 10 * 60 * Tps; i++)
        {
            int totalBefore = net.GetPlayerInfo(0).HoldTotal;
            w.Step();
            // The ore port can take an ore out and let mining add one in the same tick, so mining at the limit is throttled, not zero.
            if (totalBefore >= limit) { blocked++; minedAtLimit += net.MinedByHand - mined; }
            mined = net.MinedByHand;
            maxTotal = Math.Max(maxTotal, net.GetPlayerInfo(0).HoldTotal);
        }
        _out.WriteLine($"max hold {maxTotal}/{capacity}; ticks with mining blocked {blocked}");
        Assert.True(blocked > 0, "the limit was never reached, so it was not exercised");
        Assert.True(minedAtLimit * PlayerRules.MineTicksPerItem * 2 <= blocked, $"mining ran at the limit: {minedAtLimit} ore in {blocked} ticks");
        // Only the factory can fill the last quarter: mining itself never crosses the limit.
        Assert.True(maxTotal > limit, "the factory never delivered into the reserved quarter");
        w.ValidateInvariants();

        // Legacy worlds keep the full hold for mining.
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: false);
        Assert.Equal(PlayerRules.HoldCapacity, legacy.Network.HoldCapacity(0));
    }

    [Fact]
    public void Depots_leave_a_tenth_of_the_hold_free()
    {
        var w = World();
        var net = w.Network;
        int ti = C.ItemIndex("core:titanium_plate"), ni = C.ItemIndex("core:nickel_plate");
        int cap = net.HoldCapacity(0);
        int limit = cap * PlayerRules.DepotHoldLimitPercent / 100;
        Assert.Equal(90, limit);
        // Goods nobody consumes (the 20 starting hydrogen make 80), plus ore for the starter lines: the depot is the only thing that can add to the hold.
        net.GiveItems(0, ti, 30);
        net.GiveItems(0, ni, 30);
        net.GiveItems(0, Ore, 9);
        int max = 0;
        long delivered = net.DepotDelivered;
        for (int i = 0; i < 5 * 60 * Tps; i++) { w.Step(); max = Math.Max(max, net.HoldTotal(0)); }
        _out.WriteLine($"max hold {max}/{cap}; delivered {net.DepotDelivered - delivered}");
        Assert.True(net.DepotDelivered > delivered, "the depot never delivered, so the limit was not exercised");
        Assert.True(max <= limit, $"the depot filled the hold to {max}, past {limit}");
        w.ValidateInvariants();
    }

    [Fact]
    public void Starter_ship_makes_its_first_plate_within_two_minutes_of_mining()
    {
        var r = Play(3, found: false);
        Assert.True(r.FirstIngot >= 0 && r.FirstPlate >= 0, "no ingot or plate reached the hold");
        Assert.True(r.FirstPlate <= 2 * 60 * Tps, $"first plate after {Min(r.FirstPlate)}");
    }

    [Fact]
    public void Can_found_a_station_within_six_minutes_of_play()
    {
        var r = Play(8);
        Assert.True(r.CoreAffordable >= 0, "the station was never affordable");
        Assert.True(r.Founded >= 0, "the station was never founded");
        Assert.True(r.Founded <= 6 * 60 * Tps, $"station founded after {Min(r.Founded)}");
    }

    [Fact]
    public void Hold_never_deadlocks()
    {
        var w = World();
        var net = w.Network;
        FlyAndMine(w);
        int cap = PlayerRules.DepotItemCap;
        int last = 0;
        long lastGain = w.Tick, longestStall = 0;
        bool capReached = false;
        for (int i = 0; i < 10 * 60 * Tps; i++)
        {
            w.Step();
            int ingots = net.HoldCount(0, Ingot);
            Assert.True(ingots >= last, $"ingots dropped from {last} to {ingots} at tick {w.Tick} with nothing being built");
            if (ingots > last) lastGain = w.Tick;
            else if (!capReached && ingots < cap) longestStall = Math.Max(longestStall, w.Tick - lastGain);
            if (ingots >= cap) capReached = true;
            last = ingots;
        }
        _out.WriteLine($"ingots {last} (depot cap {cap}); longest stall before the cap {longestStall} ticks; plates {net.HoldCount(0, Plate)}");
        Assert.True(capReached, "the depot never filled the hold with ingots");
        Assert.True(longestStall <= 60 * Tps, $"ingots stalled for {longestStall} ticks");
        Assert.True(net.HoldCount(0, Plate) > 0);
        w.ValidateInvariants();
    }

    [Fact]
    public void First_wave_does_not_come_before_the_first_station_or_five_minutes()
    {
        // Measured: the starter factory's machines add up to exactly the signature threshold (8), which does not raise the alert,
        // so no wave comes from the starter ship alone. A wave needs more machines, i.e. a station with a factory.
        var r = Play(12, threats: true);
        Assert.True(r.Founded >= 0);
        Assert.True(r.MaxSignature <= C.Threat!.SignatureThreshold, $"signature {r.MaxSignature} above the threshold");
        Assert.True(r.FirstWave < 0 || (r.FirstWave >= r.Founded && r.FirstWave >= 5 * 60 * Tps), $"first wave at {Min(r.FirstWave)}, station at {Min(r.Founded)}");
    }
}
