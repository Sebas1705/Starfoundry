using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ThrustThrottleTests
{
    private static (int X, int Y, int Z) FromDirection(double yaw, double pitch)
    {
        double x = -Math.Sin(yaw) * Math.Cos(pitch), y = Math.Sin(pitch), z = -Math.Cos(yaw) * Math.Cos(pitch);
        return ((int)(x * 1024), (int)(y * 1024), (int)(z * 1024)); // truncated, as the game does when rounding would lengthen it
    }

    [Fact]
    public void QuantizeKeepsEveryVectorInRangeAndOnTheStep()
    {
        var rng = new Random(7);
        for (int i = 0; i < 20000; i++)
        {
            var wanted = FromDirection(rng.NextDouble() * 2 * Math.PI, (rng.NextDouble() - 0.5) * Math.PI);
            Assert.True(PlayerRules.ThrustInRange(wanted.X, wanted.Y, wanted.Z));
            var q = ThrustThrottle.Quantize(wanted);
            Assert.True(PlayerRules.ThrustInRange(q.X, q.Y, q.Z), $"{wanted} became {q}, longer than 1024");
            Assert.True(q.X % ThrustThrottle.Step == 0 && q.Y % ThrustThrottle.Step == 0 && q.Z % ThrustThrottle.Step == 0, $"{q} is off the step");
        }
    }

    [Fact]
    public void RoundingThatWouldLengthenTheVectorFallsBackToTruncation()
    {
        // 700 is 11.4 steps of 64 for the first two components (rounds to 704) but (700, 700, 0) has length 990: rounding the half cases up would
        // give (768, 768, 0), longer than 1024. Whatever rounding does, the result must stay legal.
        var q = ThrustThrottle.Quantize((736, 736, 0));
        Assert.True(PlayerRules.ThrustInRange(q.X, q.Y, q.Z));
        var edge = ThrustThrottle.Quantize((992, 0, 288));
        Assert.True(PlayerRules.ThrustInRange(edge.X, edge.Y, edge.Z));
        Assert.Equal((1024, 0, 0), ThrustThrottle.Quantize((1024, 0, 0)));
        Assert.Equal((0, 0, 0), ThrustThrottle.Quantize((20, -31, 10)));
        Assert.Equal((-64, 0, 0), ThrustThrottle.Quantize((-40, 0, 0)));
    }

    [Fact]
    public void QuantizeKeepsTheHeadingCloseToTheWantedOne()
    {
        var rng = new Random(11);
        var errors = new List<double>();
        for (int i = 0; i < 5000; i++)
        {
            var wanted = FromDirection(rng.NextDouble() * 2 * Math.PI, (rng.NextDouble() - 0.5) * Math.PI);
            var q = ThrustThrottle.Quantize(wanted);
            double dot = wanted.X * q.X + wanted.Y * q.Y + wanted.Z * q.Z;
            double lenQ = Math.Sqrt((double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z);
            double lenW = Math.Sqrt((double)wanted.X * wanted.X + (double)wanted.Y * wanted.Y + (double)wanted.Z * wanted.Z);
            if (lenQ == 0) continue;
            errors.Add(Math.Acos(Math.Min(1.0, dot / (lenW * lenQ))) * 180 / Math.PI);
        }
        errors.Sort();
        // Typically under 2 degrees; the worst case is a direction almost along an axis, where rounding up would make the vector too long
        // and the components are truncated instead.
        Assert.True(errors[errors.Count / 2] < 2.0, $"median heading error {errors[errors.Count / 2]:F2} degrees");
        Assert.True(errors[^1] < 6.0, $"worst heading error {errors[^1]:F1} degrees");
    }

    [Fact]
    public void ChangesAreRateLimitedToTenAndTheLimitIsNotExceeded()
    {
        var throttle = new ThrustThrottle();
        int sent = 0;
        double t = 0;
        // The view turns the whole time at 60 frames a second: every frame wants a different vector.
        for (int frame = 0; frame < 600; frame++, t += 1.0 / 60)
        {
            var wanted = FromDirection(frame * 0.07, 0.2);
            if (throttle.Offer(wanted, t) is not null) sent++;
        }
        Assert.InRange(sent, 90, 101); // ten seconds: ten a second at most (plus the first)
        Assert.Equal(sent, throttle.Sent);
    }

    [Fact]
    public void AStopIsNeverHeldBack()
    {
        var throttle = new ThrustThrottle();
        Assert.NotNull(throttle.Offer((1024, 0, 0), 5.0));
        // 1 ms later: a different direction is held back, a stop goes out at once.
        Assert.Null(throttle.Offer((0, 0, -1024), 5.001));
        Assert.Equal((0, 0, 0), throttle.Offer((0, 0, 0), 5.002));
        Assert.Null(throttle.Offer((0, 0, 0), 5.003)); // already stopped
    }

    [Fact]
    public void TheFinalValueIsAlwaysDelivered()
    {
        var throttle = new ThrustThrottle();
        Assert.NotNull(throttle.Offer((1024, 0, 0), 1.0));
        var final = (0, -640, -768);
        (int X, int Y, int Z)? got = null;
        // The player settles on one direction right after a send; the game offers it every frame.
        for (double t = 1.01; t < 1.25 && got is null; t += 1.0 / 60)
            got = throttle.Offer(final, t);
        Assert.NotNull(got);
        Assert.Equal(ThrustThrottle.Quantize(final), got);
        Assert.Equal(got, throttle.LastSent);
    }

    [Fact]
    public void WigglesInsideOneStepSendNothing()
    {
        var throttle = new ThrustThrottle();
        Assert.NotNull(throttle.Offer((1024, 0, 0), 0.0));
        for (int i = 0; i < 100; i++)
            Assert.Null(throttle.Offer((1024 - i % 20, i % 25 - 12, -(i % 17)), 1.0 + i * 0.05));
        Assert.Equal(1, throttle.Sent);
    }

    [Fact]
    public void AnInvalidatedThrottleSendsAgainEvenIfTheValueIsTheSame()
    {
        var throttle = new ThrustThrottle();
        Assert.NotNull(throttle.Offer((0, 0, -1024), 0.0));
        Assert.Null(throttle.Offer((0, 0, -1024), 0.5));
        throttle.Invalidate(); // a jump cleared the thrust in the simulation
        Assert.Equal((0, 0, -1024), throttle.Offer((0, 0, -1024), 0.51));
    }

    [Fact]
    public void DifferingSimulationThrustIsDetectedOnlyWhenWhatWasSentIsKnown()
    {
        var throttle = new ThrustThrottle();
        Assert.False(throttle.Differs((0, 0, 0)));
        throttle.Offer((0, 0, -1024), 0.0);
        Assert.False(throttle.Differs((0, 0, -1024)));
        Assert.True(throttle.Differs((0, 0, 0)));
        throttle.Invalidate();
        Assert.False(throttle.Differs((0, 0, 0)));
    }

    [Fact]
    public void ThrottledCommandLogIsMuchShorterThanTheOldRule()
    {
        // Ten minutes of steering at 60 frames a second, the view turning all the time: the old rule (every change, at most 30 a second)
        // against the throttle. The throttle must keep the log at a third of it or less.
        var throttle = new ThrustThrottle();
        var last = (0, 0, 0);
        double lastAt = double.NegativeInfinity;
        int old = 0, now = 0;
        for (int frame = 0; frame < 600 * 60; frame++)
        {
            double t = frame / 60.0;
            var wanted = FromDirection(0.35 * t, 0.3 * Math.Sin(0.2 * t));
            if (wanted != last && t - lastAt >= 1.0 / 30.0) { last = wanted; lastAt = t; old++; }
            if (throttle.Offer(wanted, t) is not null) now++;
        }
        Assert.True(now * 3 <= old, $"old {old}, throttled {now}");
    }
}
