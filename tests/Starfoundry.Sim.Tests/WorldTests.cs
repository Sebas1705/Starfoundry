using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class WorldTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");

    private static SimWorld Campaign() => new(C, GameMode.Campaign);

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

    private static PlayerInfo Info(SimWorld w, int player = 0) => w.Network.GetPlayerInfo(player);

    private static PlaceSegment Belt(int x, int y, int z, int len = 1) => new(x, y, z, Dir.PosX, len, BeltTier.Mk1);

    // ---- movement ------------------------------------------------------------------------------

    [Fact]
    public void TheShipMovesAtSixCellsPerSecondUntilTheIntentChanges()
    {
        var w = Campaign();
        Ok(w, new SetMovement(1, 0, 0));
        w.Step(29); // 30 ticks of movement in total
        Assert.Equal(30 * PlayerRules.MoveUnitsPerTick, Info(w).X);
        Assert.Equal(6 * Units.CellUnits, Info(w).X);

        Ok(w, new SetMovement(0, 0, 0));
        int x = Info(w).X;
        w.Step(100);
        Assert.Equal(x, Info(w).X); // the stop applies at the start of its tick, before the ship moves
        w.ValidateInvariants();
    }

    [Fact]
    public void CombiningAxesSlowsEachAxisSoTheSpeedStaysAboutTheSame()
    {
        var w = Campaign();
        Ok(w, new SetMovement(1, 0, -1));
        w.Step(99);
        Assert.Equal(100 * PlayerRules.MoveUnitsPerTickDiagonal2, Info(w).X);
        Assert.Equal(-100 * PlayerRules.MoveUnitsPerTickDiagonal2, Info(w).Z);
        Assert.Equal(0, Info(w).Y);

        var w3 = Campaign();
        Ok(w3, new SetMovement(1, 1, 1));
        w3.Step(99);
        Assert.Equal(100 * PlayerRules.MoveUnitsPerTickDiagonal3, Info(w3).Y);

        // Speed along the path stays within a few percent of the straight-line speed.
        double diag2 = Math.Sqrt(2.0) * PlayerRules.MoveUnitsPerTickDiagonal2;
        double diag3 = Math.Sqrt(3.0) * PlayerRules.MoveUnitsPerTickDiagonal3;
        Assert.InRange(diag2 / PlayerRules.MoveUnitsPerTick, 0.98, 1.02);
        Assert.InRange(diag3 / PlayerRules.MoveUnitsPerTick, 0.98, 1.02);
    }

    [Fact]
    public void InvalidIntentsAreRejectedAndPlayersMoveIndependently()
    {
        var w = Campaign();
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetMovement(2, 0, 0)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetMovement(0, -5, 0)).Reject);

        w.Submit(0, new SetMovement(1, 0, 0));
        w.Submit(1, new SetMovement(0, 0, 1));
        w.Step(10);
        Assert.Equal(10 * PlayerRules.MoveUnitsPerTick, Info(w, 0).X);
        Assert.Equal(0, Info(w, 0).Z);
        Assert.Equal(10 * PlayerRules.MoveUnitsPerTick, Info(w, 1).Z);
        Assert.Equal(0, Info(w, 1).X);
        w.ValidateInvariants();
    }

    [Fact]
    public void TheShipCannotLeaveTheWorld()
    {
        var w = new SimWorld();
        Ok(w, new SetMovement(-1, 1, 0));
        w.Step(PlayerRules.WorldLimitUnits / PlayerRules.MoveUnitsPerTickDiagonal2 + 100);
        Assert.Equal(-PlayerRules.WorldLimitUnits, Info(w).X);
        Assert.Equal(PlayerRules.WorldLimitUnits, Info(w).Y);
        w.ValidateInvariants();
    }

    // ---- mining range ----------------------------------------------------------------------------

    [Fact]
    public void MiningNeedsTheShipWithinRangeOfTheDeposit()
    {
        var w = Campaign();
        w.Network.AddDeposit(Ore, 500, 6, 0, 0);   // exactly at the range limit from the origin
        w.Network.AddDeposit(Ore, 500, 7, 0, 0);   // just outside
        Ok(w, new StartMining(0));
        Assert.Equal(Reject.OutOfRange, Run(w, new StartMining(1)).Reject);
        Assert.Equal(0, w.Network.MiningDeposit(0));
    }

    [Fact]
    public void LeavingRangePausesMiningAndReturningResumesIt()
    {
        var w = Campaign();
        w.Network.AddDeposit(Ore, 500, 20, 0, 0);
        Assert.Equal(Reject.OutOfRange, Run(w, new StartMining(0)).Reject);

        // Fly towards it until in range (14 cells is 5.6... cells away from a deposit at 20).
        Ok(w, new SetMovement(1, 0, 0));
        while (!w.Network.InMineRange(0, 0)) w.Step();
        Ok(w, new SetMovement(0, 0, 0));
        Ok(w, new StartMining(0));
        w.Step(15 * 5);
        int mined = w.Network.HoldCount(0, Ore);
        Assert.InRange(mined, 4, 6);

        // Fly away: the deposit stays selected but nothing is mined.
        Ok(w, new SetMovement(-1, 0, 0));
        w.Step(200);
        int atLeaving = w.Network.HoldCount(0, Ore);
        w.Step(300);
        Assert.Equal(atLeaving, w.Network.HoldCount(0, Ore));
        Assert.Equal(0, w.Network.MiningDeposit(0));

        // Come back and it carries on.
        Ok(w, new SetMovement(1, 0, 0));
        while (!w.Network.InMineRange(0, 0)) w.Step();
        Ok(w, new SetMovement(0, 0, 0));
        w.Step(15 * 5);
        Assert.True(w.Network.HoldCount(0, Ore) > atLeaving);
        w.ValidateInvariants();
    }

    [Fact]
    public void DepositsKeepTheirPositionsAndStayInsideTheWorld()
    {
        var w = Campaign();
        int d = w.Network.AddDeposit(Ore, 10, -7, 2, 3);
        Assert.Equal((-7, 2, 3), w.Network.DepositPosition(d));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Network.AddDeposit(Ore, 10, SimWorld.MaxCoord + 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Network.DepositPosition(5));
    }

    // ---- build range (campaign) ------------------------------------------------------------------

    [Fact]
    public void BuildingNeedsTheShipWithinRangeInCampaignOnly()
    {
        var w = Campaign();
        w.Network.GiveItems(0, Ingot, 50);

        Assert.True(Run(w, Belt(23, 0, 0)).Ok);
        Assert.Equal(Reject.OutOfRange, Run(w, Belt(24, 0, 0)).Reject);
        Assert.Equal(Reject.OutOfRange, Run(w, Belt(0, 30, 0)).Reject);
        Assert.True(Run(w, new PlaceMachine(0, 0, 5, Smelter, SmeltIron)).Ok);
        Assert.Equal(Reject.OutOfRange, Run(w, new PlaceMachine(0, 0, 40, Smelter, SmeltIron)).Reject);

        // Both ends of a long belt must be in range.
        Assert.Equal(Reject.OutOfRange, Run(w, new PlaceSegment(0, 2, 0, Dir.PosX, 40, BeltTier.Mk1)).Reject);
        Assert.True(Run(w, new PlaceSegment(0, 2, 0, Dir.PosX, 20, BeltTier.Mk1)).Ok);

        // Sandbox has no limit.
        var s = new SimWorld(C);
        Assert.True(Run(s, Belt(5000, 0, 0)).Ok);
        Assert.True(Run(s, new PlaceMachine(-3000, 0, 0, Smelter, SmeltIron)).Ok);
    }

    [Fact]
    public void RemovingNeedsRangeToo()
    {
        var w = Campaign();
        w.Network.GiveItems(0, Ingot, 20);
        Ok(w, Belt(20, 0, 0));
        Ok(w, new PlaceMachine(0, 0, 3, Smelter, SmeltIron));

        Ok(w, new SetMovement(-1, 0, 0));
        w.Step(100); // 20 cells away from the origin, so about 40 from the belt
        Ok(w, new SetMovement(0, 0, 0));
        Assert.Equal(Reject.OutOfRange, Run(w, new RemoveSegment(0)).Reject);
        Assert.True(w.Network.IsAlive(0));

        Ok(w, new SetMovement(1, 0, 0));
        w.Step(100);
        Ok(w, new SetMovement(0, 0, 0));
        Assert.True(Run(w, new RemoveSegment(0)).Ok);

        // Far from the machine as well.
        Ok(w, new SetMovement(0, 0, 1));
        w.Step(300);
        Ok(w, new SetMovement(0, 0, 0));
        Assert.Equal(Reject.OutOfRange, Run(w, new RemoveMachine(0)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void ABatchWithAnOutOfRangeStepRollsBackAndRefunds()
    {
        var w = Campaign();
        w.Network.GiveItems(0, Ingot, 30);

        var r = Run(w, new Batch(new Command[]
        {
            new PlaceMachine(0, 0, 0, Smelter, SmeltIron), // in range, 8 ingots
            Belt(2, 0, 0, 3),                              // in range, 3 ingots
            Belt(80, 0, 0),                                // far away
        }));

        Assert.Equal(Reject.OutOfRange, r.Reject);
        Assert.Equal(2, r.FailedIndex);
        Assert.Equal(30, w.Network.HoldCount(0, Ingot));
        Assert.Equal(0, w.Network.MachineCount);
        Assert.Equal(0, w.Network.SegmentCount);
        w.ValidateInvariants();
    }

    // ---- codec, replay ---------------------------------------------------------------------------

    [Fact]
    public void MovementCommandsSurviveARoundTripAndReplayIdentically()
    {
        var cmd = new SetMovement(1, -1, 0);
        Assert.Equal(cmd, CommandCodec.FromBytes(CommandCodec.ToBytes(cmd)));

        var w = Campaign();
        w.Network.AddDeposit(Ore, 300, 10, 0, 0);
        w.Network.GiveItems(0, Ingot, 40);
        w.Submit(0, new SetMovement(1, 0, 0));
        w.Step(50);
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(0));
        w.Submit(1, new SetMovement(0, 1, 1));
        w.Step(400);

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out _);

        SimWorld Start()
        {
            var s = Campaign();
            s.Network.AddDeposit(Ore, 300, 10, 0, 0);
            s.Network.GiveItems(0, Ingot, 40);
            return s;
        }
        Assert.Equal(w.Checksum(), SimWorld.Replay(log, w.Tick, initial: Start()).Checksum());
        Assert.True(w.Network.HoldCount(0, Ore) > 0);
    }

    [Fact]
    public void PositionsAndDepositsAreInTheChecksum()
    {
        var a = Campaign();
        var b = Campaign();
        Ok(a, new SetMovement(1, 0, 0));
        Ok(b, new SetMovement(0, 0, 1));
        Assert.NotEqual(a.Checksum(), b.Checksum());

        var c = Campaign();
        var d = Campaign();
        c.Network.AddDeposit(Ore, 10, 1, 0, 0);
        d.Network.AddDeposit(Ore, 10, 2, 0, 0);
        Assert.NotEqual(c.Checksum(), d.Checksum());
    }
}
