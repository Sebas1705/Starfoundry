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
        new BalanceBot(TestContent.Core, seed) { ActionDelaySeconds = delay, ObserveAfterSeconds = observeMinutes * 60 }.Run(75 * 60);

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
    private static int FirstLossWave(int turrets, ulong seed, int walls = 0, RepairMode repairs = RepairMode.Never) =>
        new BalanceBot(TestContent.Core, seed)
        {
            ActionDelaySeconds = 15, Turrets = turrets, Walls = walls, Repairs = repairs,
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
