using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ExploreTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int CarbonItem = C.ItemIndex("core:carbon");
    private static readonly int Drill = C.MachineIndex("core:drill");
    private static readonly int DrillIron = C.RecipeIndex("core:drill_iron");
    private static readonly int JumpDrive = C.ResearchIndex("core:jump_drive");

    private const ulong Seed = 20261120UL;

    private static SimWorld Galaxy(GameMode mode = GameMode.Sandbox, ulong seed = Seed) => SimWorld.CreateGalaxy(C, mode, seed);

    private static CommandResult Run(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static void Ok(SimWorld w, Command c, int player = 0)
    {
        var r = Run(w, c, player);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
    }

    private static Galaxy G(SimWorld w) => w.Network.Galaxy!;

    private static int Neighbor(SimWorld w, int system = 0) => G(w).Systems[system].Lanes[0];

    private static void JumpAndArrive(SimWorld w, int target, int player = 0)
    {
        Ok(w, new Jump(target), player);
        w.Step(ExplorationRules.JumpChargeTicks);
        Assert.Equal(target, w.Network.CurrentSystem(player));
    }

    // ---- creation --------------------------------------------------------------------------------

    [Fact]
    public void AGalaxyWorldStartsAtHomeWithOnlyItsOwnDepositsRevealed()
    {
        var w = Galaxy();
        var g = G(w);
        Assert.Equal(g.Deposits.Count, w.Network.DepositCount);

        for (int i = 0; i < g.Deposits.Count; i++)
        {
            Assert.Equal(g.Deposits[i].System == 0, w.Network.IsDepositRevealed(i));
            Assert.Equal((g.Deposits[i].X, g.Deposits[i].Y, g.Deposits[i].Z), w.Network.DepositPosition(i));
        }

        Assert.True(w.Network.IsSystemVisited(0));
        for (int s = 0; s < g.Systems.Count; s++)
        {
            Assert.Equal(s == 0, w.Network.IsSystemVisited(s));
            Assert.Equal(s == 0 || g.HasLane(0, s), w.Network.IsSystemKnown(s));
        }
        Assert.Equal(0, w.Network.CurrentSystem(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void TheSameSeedGivesTheSameWorldAndAnotherSeedADifferentOne()
    {
        Assert.Equal(Galaxy(GameMode.Campaign, 7).Checksum(), Galaxy(GameMode.Campaign, 7).Checksum());
        Assert.NotEqual(Galaxy(GameMode.Campaign, 7).Checksum(), Galaxy(GameMode.Campaign, 8).Checksum());
        Assert.NotEqual(Galaxy(GameMode.Campaign, 7).Checksum(), Galaxy(GameMode.Sandbox, 7).Checksum());
    }

    [Fact]
    public void HiddenDepositsCannotBeMinedOrLinked()
    {
        var w = Galaxy();
        int hidden = Enumerable.Range(0, w.Network.DepositCount).First(i => !w.Network.IsDepositRevealed(i));
        Assert.Equal(Reject.NotDiscovered, Run(w, new StartMining(hidden)).Reject);

        var (x, y, z) = w.Network.DepositPosition(hidden);
        Ok(w, new PlaceMachine(x, y, z + 1, Drill, DrillIron)); // sandbox: no range, so we can build right next to it
        Assert.Equal(Reject.NotDiscovered, Run(w, new LinkDeposit(0, hidden)).Reject);
        w.ValidateInvariants();
    }

    // ---- scanning -----------------------------------------------------------------------------------

    [Fact]
    public void ScanningRevealsDepositsAroundTheShipAndThenRecharges()
    {
        var w = Galaxy();
        int target = Neighbor(w);
        JumpAndArrive(w, target);

        var r = Run(w, new Scan());
        Assert.True(r.Ok);
        var expected = G(w).DepositsOf(target).ToList();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, r.RevealedDeposits.OrderBy(x => x).ToList());
        foreach (int d in expected) Assert.True(w.Network.IsDepositRevealed(d));

        // Systems that were not scanned stay hidden.
        int other = Enumerable.Range(1, G(w).Systems.Count - 1).First(s => s != target);
        foreach (int d in G(w).DepositsOf(other)) Assert.False(w.Network.IsDepositRevealed(d));

        Assert.Equal(Reject.OnCooldown, Run(w, new Scan()).Reject);
        w.Step(ExplorationRules.ScanCooldownTicks);
        var again = Run(w, new Scan());
        Assert.True(again.Ok);
        Assert.Empty(again.RevealedDeposits);
        w.ValidateInvariants();
    }

    [Fact]
    public void AScanOnlyReachesWhatIsWithinRange()
    {
        var w = Galaxy();
        // Add a hidden deposit 90 cells from home and another 70 cells away.
        int far = w.Network.AddDeposit(Ore, 100, 90, 0, 0, revealed: false);
        int near = w.Network.AddDeposit(Ore, 100, 70, 0, 0, revealed: false);

        var r = Run(w, new Scan());
        Assert.Equal(new[] { near }, r.RevealedDeposits);
        Assert.False(w.Network.IsDepositRevealed(far));
    }

    [Fact]
    public void ARevealedDepositCanBeMinedAndDrilled()
    {
        var w = Galaxy();
        int hidden = w.Network.AddDeposit(Ore, 100, 3, 0, 0, revealed: false);
        Assert.Equal(Reject.NotDiscovered, Run(w, new StartMining(hidden)).Reject);

        Ok(w, new Scan());
        Ok(w, new StartMining(hidden));
        w.Step(15 * 5);
        Assert.InRange(w.Network.HoldCount(0, Ore), 4, 6);
        w.ValidateInvariants();
    }

    // ---- jumping -------------------------------------------------------------------------------------

    [Fact]
    public void AJumpChargesFreezesTheShipThenArrivesAtTheAnchor()
    {
        var w = Galaxy();
        int deposit = w.Network.AddDeposit(Ore, 100, 2, 0, 0); // revealed, in mining range of the origin
        Ok(w, new StartMining(deposit));
        int target = Neighbor(w);
        var sys = G(w).Systems[target];

        Ok(w, new Jump(target));
        Assert.Equal(target, w.Network.JumpTarget(0));
        Assert.True(w.Network.JumpRemaining(0) > 0);

        // The ship is frozen while charging, whatever the movement intent.
        var before = w.Network.GetPlayerInfo(0);
        Ok(w, new SetMovement(1, 0, 0));
        w.Step(30);
        var during = w.Network.GetPlayerInfo(0);
        Assert.Equal((before.X, before.Y, before.Z), (during.X, during.Y, during.Z));
        Assert.Equal(Reject.Busy, Run(w, new Jump(target)).Reject);

        while (w.Network.JumpRemaining(0) > 0) w.Step();
        var after = w.Network.GetPlayerInfo(0);
        Assert.Equal((sys.X * Units.CellUnits, sys.Y * Units.CellUnits, sys.Z * Units.CellUnits), (after.X, after.Y, after.Z));
        Assert.Equal(-1, after.JumpTarget);
        Assert.Equal(target, w.Network.CurrentSystem(0));

        // Arrival visits the system, reveals its lanes and cancels mining.
        Assert.True(w.Network.IsSystemVisited(target));
        foreach (int lane in sys.Lanes) Assert.True(w.Network.IsSystemKnown(lane));
        Assert.Equal(-1, w.Network.MiningDeposit(0));

        // The intent given during the charge applies afterwards: the ship leaves the anchor.
        w.Step(10);
        Assert.True(w.Network.GetPlayerInfo(0).X > sys.X * Units.CellUnits);
        w.ValidateInvariants();
    }

    [Fact]
    public void JumpsAreOnlyPossibleAlongLanesFromInsideASystem()
    {
        var plain = new SimWorld(C);
        Assert.Equal(Reject.NoGalaxy, Run(plain, new Jump(1)).Reject);

        var w = Galaxy();
        Assert.Equal(Reject.UnknownSystem, Run(w, new Jump(99)).Reject);
        Assert.Equal(Reject.UnknownSystem, Run(w, new Jump(-1)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new Jump(0)).Reject); // already there

        int notNeighbor = Enumerable.Range(1, G(w).Systems.Count - 1).First(s => !G(w).HasLane(0, s));
        Assert.Equal(Reject.NoLane, Run(w, new Jump(notNeighbor)).Reject);

        // In deep space there is no system to jump from.
        Ok(w, new SetMovement(0, 1, 0));
        w.Step(900); // 180 cells up, beyond the 150-cell system radius
        Ok(w, new SetMovement(0, 0, 0));
        Assert.Equal(-1, w.Network.CurrentSystem(0));
        Assert.Equal(Reject.NotInASystem, Run(w, new Jump(Neighbor(w))).Reject);
    }

    [Fact]
    public void ExploringOutwardOneLaneAtATimeExpandsTheMapAndCanGoBack()
    {
        var w = Galaxy();
        int known0 = Enumerable.Range(0, G(w).Systems.Count).Count(w.Network.IsSystemKnown);

        int first = Neighbor(w);
        JumpAndArrive(w, first);
        int second = G(w).Systems[first].Lanes.First(s => s != 0);
        JumpAndArrive(w, second);

        Assert.Equal(3, Enumerable.Range(0, G(w).Systems.Count).Count(w.Network.IsSystemVisited));
        Assert.True(Enumerable.Range(0, G(w).Systems.Count).Count(w.Network.IsSystemKnown) >= known0);

        JumpAndArrive(w, first);
        JumpAndArrive(w, 0);
        Assert.Equal(0, w.Network.CurrentSystem(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void FlyingIntoASystemVisitsItToo()
    {
        var w = Galaxy();
        int target = Neighbor(w);
        var sys = G(w).Systems[target];
        Assert.False(w.Network.IsSystemVisited(target));
        w.Network.EnsurePlayer(0);

        int guard = 0;
        (int, int, int) lastIntent = (0, 0, 0);
        while (w.Network.CurrentSystem(0) != target && guard++ < 20_000)
        {
            var p = w.Network.GetPlayerInfo(0);
            static int Axis(int diff) => Math.Abs(diff) > Units.CellUnits ? Math.Sign(diff) : 0;
            var intent = (Axis(sys.X * Units.CellUnits - p.X), Axis(sys.Y * Units.CellUnits - p.Y), Axis(sys.Z * Units.CellUnits - p.Z));
            if (intent != lastIntent) { w.Submit(0, new SetMovement(intent.Item1, intent.Item2, intent.Item3)); lastIntent = intent; }
            w.Step();
        }

        Assert.Equal(target, w.Network.CurrentSystem(0));
        Assert.True(w.Network.IsSystemVisited(target));
        w.ValidateInvariants();
    }

    [Fact]
    public void InCampaignJumpingNeedsTheResearchAndPaysCarbon()
    {
        var w = Galaxy(GameMode.Campaign);
        int target = Neighbor(w);
        Assert.False(w.Network.HasAbility(Ability.Jump));
        Assert.Equal(Reject.Locked, Run(w, new Jump(target)).Reject);

        // Sandbox has it from the start.
        Assert.True(Galaxy().Network.HasAbility(Ability.Jump));

        w.Network.GrantResearch(JumpDrive);
        Assert.True(w.Network.HasAbility(Ability.Jump));
        Assert.Equal(Reject.NotEnoughItems, Run(w, new Jump(target)).Reject);

        w.Network.GiveItems(0, CarbonItem, 7);
        Ok(w, new Jump(target));
        Assert.Equal(2, w.Network.HoldCount(0, CarbonItem)); // the jump cost 5 carbon
        w.Step(ExplorationRules.JumpChargeTicks);
        Assert.Equal(target, w.Network.CurrentSystem(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void ARefusedJumpCostsNothing()
    {
        var w = Galaxy(GameMode.Campaign);
        w.Network.GrantResearch(JumpDrive);
        w.Network.GiveItems(0, CarbonItem, 20);
        int notNeighbor = Enumerable.Range(1, G(w).Systems.Count - 1).First(s => !G(w).HasLane(0, s));

        Assert.Equal(Reject.NoLane, Run(w, new Jump(notNeighbor)).Reject);
        Assert.Equal(20, w.Network.HoldCount(0, CarbonItem));
    }

    // ---- drill range ---------------------------------------------------------------------------------

    [Fact]
    public void DrillsOnlyLinkToDepositsWithinSixteenCells()
    {
        var w = new SimWorld(C);
        int exact = w.Network.AddDeposit(Ore, 100, 16, 0, 0);
        int beyond = w.Network.AddDeposit(Ore, 100, 17, 0, 0);
        Ok(w, new PlaceMachine(0, 0, 0, Drill, DrillIron));

        Assert.Equal(Reject.OutOfRange, Run(w, new LinkDeposit(0, beyond)).Reject);
        Ok(w, new LinkDeposit(0, exact));
        w.ValidateInvariants();
    }

    // ---- codec and replay ----------------------------------------------------------------------------

    [Fact]
    public void ExplorationCommandsRoundTripAndLogsCarryTheSeed()
    {
        Assert.Equal(new Jump(5), CommandCodec.FromBytes(CommandCodec.ToBytes(new Jump(5))));
        Assert.IsType<Scan>(CommandCodec.FromBytes(CommandCodec.ToBytes(new Scan())));

        var log = new[] { new LoggedCommand(0, 0, new Scan()), new LoggedCommand(3, 1, new Jump(2)) };
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Campaign, Seed);
        ms.Position = 0;
        var back = CommandCodec.ReadLog(ms, out ulong hash, out var mode, out ulong seed);
        Assert.Equal(2, back.Count);
        Assert.Equal((C.Hash, GameMode.Campaign, Seed), (hash, mode, seed));
    }

    [Fact]
    public void AnExplorationSessionReplaysIdentically()
    {
        var w = Galaxy();
        Ok(w, new Scan());
        int a = Neighbor(w);
        JumpAndArrive(w, a);
        w.Step(ExplorationRules.ScanCooldownTicks);
        Ok(w, new Scan());
        w.Submit(1, new SetMovement(0, 0, 1));
        w.Step(200);
        int b = G(w).Systems[a].Lanes.First(s => s != 0);
        w.Submit(0, new Jump(b));
        w.Step(300);

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out var mode, out ulong seed);

        var replay = SimWorld.Replay(log, w.Tick, initial: SimWorld.CreateGalaxy(C, mode, seed));
        Assert.Equal(w.Checksum(), replay.Checksum());
        Assert.True(w.Network.IsSystemVisited(b));
    }

    // ---- fuzz -----------------------------------------------------------------------------------------

    private static SimWorld StormWorld(GameMode mode)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed);
        if (mode == GameMode.Campaign)
        {
            w.Network.GrantResearch(JumpDrive);
            for (int p = 0; p < 3; p++)
            {
                w.Network.GiveItems(p, CarbonItem, 25);
                w.Network.GiveItems(p, C.ItemIndex("core:iron_ingot"), 30);
            }
        }
        return w;
    }

    private static Command RandomExploreCommand(Random rng, SimWorld w, int player)
    {
        var net = w.Network;
        var galaxy = net.Galaxy!;
        switch (rng.Next(0, 9))
        {
            case 0:
            case 1: return new Scan();
            case 2:
            case 3:
            case 4:
            {
                int cur = net.CurrentSystem(player);
                if (cur >= 0 && rng.Next(0, 5) != 0)
                {
                    var lanes = galaxy.Systems[cur].Lanes;
                    return new Jump(lanes[rng.Next(0, lanes.Length)]);
                }
                return new Jump(rng.Next(-1, galaxy.Systems.Count + 1));
            }
            case 5: return new StartMining(rng.Next(-1, net.DepositCount + 1));
            case 6: return new LinkDeposit(rng.Next(-1, net.MachineIdLimit + 1), rng.Next(-1, net.DepositCount + 1));
            case 7:
                return rng.Next(0, 8) == 0
                    ? new SetMovement(rng.Next(-1, 2), rng.Next(-1, 2), rng.Next(-1, 2))
                    : new SetMovement(0, 0, 0);
            default: return new StopMining();
        }
    }

    [Theory]
    [InlineData(GameMode.Sandbox, 31)]
    [InlineData(GameMode.Sandbox, 32)]
    [InlineData(GameMode.Sandbox, 33)]
    [InlineData(GameMode.Campaign, 34)]
    [InlineData(GameMode.Campaign, 35)]
    public void RandomExplorationStormKeepsInvariantsAndReplaysIdentically(GameMode mode, int seed)
    {
        const int ticks = 4000;
        var rng = new Random(seed);
        var w = StormWorld(mode);
        int scans = 0, revealed = 0, jumps = 0, refused = 0, noLane = 0, busy = 0, cooldown = 0, notFound = 0;

        for (int t = 0; t < ticks; t++)
        {
            int n = rng.Next(0, 3);
            for (int i = 0; i < n; i++)
            {
                int who = rng.Next(0, 3);
                int pick = rng.Next(0, 10);
                Command cmd = pick < 7 ? RandomExploreCommand(rng, w, who)
                    : pick < 8 ? CommandTests.RandomCommand(rng, w)
                    : MachineTests.RandomMachineCommand(rng, w);
                w.Submit(who, cmd);
            }
            w.Step();

            foreach (var r in w.LastResults)
            {
                if (r.Ok && r.Command is Scan) { scans++; revealed += r.RevealedDeposits.Count; }
                if (r.Ok && r.Command is Jump) jumps++;
                if (!r.Ok && r.Command is Jump) refused++;
                if (r.Reject == Reject.NoLane) noLane++;
                if (r.Reject == Reject.Busy) busy++;
                if (r.Reject == Reject.OnCooldown) cooldown++;
                if (r.Reject == Reject.NotDiscovered) notFound++;
            }
            w.ValidateInvariants();
        }

        Assert.True(scans > 5, $"only {scans} scans");
        Assert.True(revealed > 0, "no deposit was ever revealed");
        Assert.True(jumps > 3, $"only {jumps} jumps");
        Assert.True(refused > 0 && cooldown > 0 && busy > 0, "the refusal paths were not exercised");
        Assert.True(Enumerable.Range(0, w.Network.SystemCount).Count(w.Network.IsSystemVisited) > 1, "nothing was explored");
        Assert.True(noLane + notFound > 0);

        // Replay straight from the log and through the binary format, from a world created the same way.
        Assert.Equal(w.Checksum(), SimWorld.Replay(w.Log, ticks, initial: StormWorld(mode)).Checksum());

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, mode, Seed);
        ms.Position = 0;
        var restored = CommandCodec.ReadLog(ms, out ulong hash, out var readMode, out ulong readSeed);
        Assert.Equal((C.Hash, mode, Seed), (hash, readMode, readSeed));
        var replay = SimWorld.Replay(restored, ticks, initial: StormWorld(mode));
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }
}
