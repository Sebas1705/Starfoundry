using Starfoundry.Balance;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Pacing targets for the first stretch of the campaign, checked by a scripted player (see docs/prototypes/balance-pass.md).
/// If a content change breaks one of these, either the change or the target needs a conscious decision.
/// </summary>
public class BalanceTests
{
    private static BalanceReport Play(double delay, int observeMinutes = 0, ulong seed = 20261120UL) =>
        new BalanceBot(TestContent.CoreClassicWaves, seed) { ActionDelaySeconds = delay, ObserveAfterSeconds = observeMinutes * 60 }.Run(75 * 60);

    private static double Done(BalanceReport r, string name) => r.Milestones[name];

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void EveryPaceFinishesTheGuidedStepsWithoutGettingStuck(double delay)
    {
        var r = Play(delay);
        Assert.True(r.Finished, r.Stuck);
        Assert.InRange(r.Seconds, 8 * 60, 30 * 60);
        Assert.True(r.HoldFullSeconds <= r.Seconds * 0.08, $"hold full for {r.HoldFullSeconds:F0} s of {r.Seconds:F0} s");
    }

    [Fact]
    public void EvenAPerfectPlayerCannotRushTheFirstResearch()
    {
        var r = Play(delay: 0);
        Assert.True(r.Finished, r.Stuck);
        Assert.True(Done(r, "Researched core:logistics") >= 9 * 60, $"Logistics at {Done(r, "Researched core:logistics"):F0} s");
    }

    [Fact]
    public void AFirstTimerFinishesTheStepsInAboutAQuarterOfAnHour()
    {
        var r = Play(delay: 15);
        Assert.InRange(r.Seconds, 9 * 60, 20 * 60);
        Assert.InRange(Done(r, "Drill"), 30, 5 * 60);          // the first drill is not instant, nor a chore
        Assert.InRange(Done(r, "Depot"), 90, 10 * 60);          // first automation inside ten minutes
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void TheFirstWaveNeverArrivesBeforeThePlayerCanDefend(double delay)
    {
        var r = Play(delay, observeMinutes: 20);
        Assert.NotNull(r.FirstWave);
        Assert.True(r.FirstWave > Done(r, "Defend") + 2 * 60, $"wave at {r.FirstWave:F0} s, turret armed at {Done(r, "Defend"):F0} s");
        Assert.True(r.FirstWaveWarning < r.FirstWave);
    }

    [Fact]
    public void ADefendedBaseSurvivesTheEarlyWaves()
    {
        var r = Play(delay: 15, observeMinutes: 25);
        Assert.True(r.Waves >= 3, $"only {r.Waves} waves seen");
        Assert.Equal(0, r.MachinesLost);
        Assert.True(r.EnemiesKilled > 0);
    }

    /// <summary>The wave during which a base with this many fed turrets (each with its own ammo line) first loses a machine.</summary>
    private static int FirstLossWave(int turrets, ulong seed, int walls = 0, RepairMode repairs = RepairMode.Never, int bays = 0, int upgrades = 0) =>
        new BalanceBot(TestContent.CoreClassicWaves, seed)
        {
            ActionDelaySeconds = 15, Turrets = turrets, Walls = walls, Repairs = repairs, Bays = bays, Upgrades = upgrades,
            StopAtFirstLoss = true, ObserveAfterSeconds = 6 * 60 * 60,
        }.Run(6 * 60 * 60).FirstLossWave ?? int.MaxValue;

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void TheWaveRampOverrunsOneTurretBetweenTheEighthAndFourteenthWave(ulong seed)
    {
        // Wave 1 is about 12 minutes in and waves come every 4 to 6 minutes: the player has to grow the defence inside the first hour or two.
        Assert.InRange(FirstLossWave(1, seed), 8, 14);
    }

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void ExtraFedTurretsBuyAtLeastTwoMoreWaves(ulong seed)
    {
        int one = FirstLossWave(1, seed), three = FirstLossWave(3, seed);
        Assert.True(three >= one + 2, $"one turret held until wave {one}, three until wave {three}");
    }

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    public void FourWallsBuyAtLeastThreeMoreWavesEvenWithoutRepairs(ulong seed)
    {
        int bare = FirstLossWave(1, seed), walled = FirstLossWave(1, seed, walls: 4);
        Assert.True(walled >= bare + 3, $"no walls held until wave {bare}, four walls until wave {walled}");
    }

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    public void RepairingDuringTheFightsBuysMuchMore(ulong seed)
    {
        int bare = FirstLossWave(1, seed);
        int never = FirstLossWave(1, seed, walls: 4, RepairMode.Never);
        int always = FirstLossWave(1, seed, walls: 4, RepairMode.Always);
        Assert.True(always >= bare + 8, $"no walls held until wave {bare}, four walls with repairs until wave {always}");
        Assert.True(always >= never + 2, $"without repairs {never}, with repairs {always}");
    }

    /// <summary>Walls and machines lost over the first stretch of waves, with the walls rebuilt whenever one falls.</summary>
    private static BalanceReport WallUpkeep(int bays, ulong seed) =>
        new BalanceBot(TestContent.CoreClassicWaves, seed)
        {
            ActionDelaySeconds = 15, Turrets = 1, Walls = 4, Bays = bays, Repairs = RepairMode.Never,
            ObserveAfterSeconds = 100 * 60,
        }.Run(130 * 60);

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    public void TwoRepairBaysCutTheWallsLostByAQuarterOrMoreWithoutAnyManualRepair(ulong seed)
    {
        var bare = WallUpkeep(0, seed);
        var bays = WallUpkeep(2, seed);
        Assert.Equal(0, bare.MachinesLost);
        Assert.Equal(0, bays.MachinesLost);
        Assert.True(bays.WallsLost * 4 <= bare.WallsLost * 3, $"walls lost: {bare.WallsLost} without bays, {bays.WallsLost} with two");
        Assert.True(bays.WallsLost > 0, "the bays slow the wear, they do not stop it");
    }

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    public void TheFourDefenceUpgradesBuyAtLeastTwoWavesToASingleTurret(ulong seed)
    {
        int bare = FirstLossWave(1, seed), upgraded = FirstLossWave(1, seed, upgrades: 4);
        Assert.True(upgraded >= bare + 2, $"no upgrades held until wave {bare}, four upgrades until wave {upgraded}");
    }

    [Fact]
    public void UpgradesPayOffMostOnAFullDefence()
    {
        // Four walls and two repair bays, with and without the four upgrades (plating makes walls and bays tougher, field repair heals more).
        int bare = FirstLossWave(1, 20261120UL, walls: 4, bays: 2), upgraded = FirstLossWave(1, 20261120UL, walls: 4, bays: 2, upgrades: 4);
        Assert.True(upgraded >= bare + 6, $"without upgrades wave {bare}, with them wave {upgraded}");
    }

    [Fact]
    public void TheUpgradesArriveOneAfterAnotherAndNeverBeforeTheirPredecessors()
    {
        var r = new BalanceBot(TestContent.CoreClassicWaves, 20261120UL) { ActionDelaySeconds = 15, Turrets = 1, Upgrades = 4, ObserveAfterSeconds = 90 * 60 }.Run(120 * 60);
        double Done(string id) => r.Milestones["Researched " + id];
        double logistics = Done("core:logistics"), calibration = Done("core:turret_calibration"), plating = Done("core:reinforced_plating"),
            loader = Done("core:rapid_loader"), repair = Done("core:field_repair");
        Assert.True(logistics < calibration && calibration < plating && plating < loader && loader < repair);
        Assert.InRange(calibration - logistics, 5 * 60, 20 * 60);   // the first upgrade is a few minutes of plates away
        Assert.InRange(repair, 40 * 60, 110 * 60);                  // all four inside the first two hours
    }

    private static BalanceReport Expansion(ulong seed) =>
        new BalanceBot(TestContent.CoreClassicWaves, seed)
        {
            ActionDelaySeconds = 15, Turrets = 1, Walls = 4, Bays = 2, ExtraPlateLine = true, Expand = true,
            ObserveAfterSeconds = 40 * 60,
        }.Run(150 * 60);

    [Theory]
    [InlineData(20261120UL)]
    [InlineData(1UL)]
    public void APlayerCanLeaveTheHomeOreBehindInAboutFortyMinutesWithMostOfItLeft(ulong seed)
    {
        var r = Expansion(seed);
        double Done(string key) => r.Milestones["Expansion: " + key];
        Assert.InRange(Done("Jump drive"), 30 * 60, 45 * 60);          // two researches' worth of plates after Logistics
        Assert.InRange(Done("Remote lines") - Done("Arrived"), 60, 5 * 60);
        Assert.True(Done("Home again") > Done("Remote lines"));
        Assert.InRange(Done("Home again"), 33 * 60, 50 * 60);         // carbon processing costs 60 plates now (was 135): about 2 minutes sooner
        Assert.True(r.HomeIronLeft["Expansion: Remote lines"] >= 0.4 * 4000, "only " + r.HomeIronLeft["Expansion: Remote lines"] + " of the home ore left");
        Assert.True(r.Milestones.ContainsKey("Expand"), "the guided step is ticked off by the first jump");
    }

    [Fact]
    public void CargoPortsKeepTheTurretsSuppliedAfterTheHomeOreIsGone()
    {
        BalanceReport Long(bool ports) =>
            new BalanceBot(TestContent.CoreClassicWaves, 20261120UL)
            {
                ActionDelaySeconds = 15, Turrets = 3, Walls = 4, Bays = 2, ExtraPlateLine = true, Upgrades = 4,
                Expand = ports, ExpandAtPercent = 40, TakeDownCarbonLine = ports, UseUnloader = ports,
                ObserveAfterSeconds = 300 * 60,
            }.Run(400 * 60);
        var without = Long(false);
        var with = Long(true);
        Assert.True(with.Milestones.ContainsKey("Expansion: Cargo ports"), "the ammo lines were switched to the hold");
        Assert.True(with.RemoteDelivered >= 200, "the remote lines delivered only " + with.RemoteDelivered + " items");
        Assert.True(with.Waves >= without.Waves + 3, $"the base saw {without.Waves} waves without the ports and {with.Waves} with them");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void PacingDoesNotDependOnTheGalaxySeed(ulong seed)
    {
        var r = Play(delay: 15, seed: seed);
        Assert.True(r.Finished, r.Stuck);
        Assert.InRange(r.Seconds, 9 * 60, 20 * 60);
    }
}
