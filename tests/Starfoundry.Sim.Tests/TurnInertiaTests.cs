using System.Numerics;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The rotation dynamics of the ship (T): the controller, the handling content, the setting and the attitude command rate.</summary>
public class TurnInertiaTests
{
    private static readonly ModuleKind[] Starter =
    {
        ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine, ModuleKind.MiningLaser, ModuleKind.Gun,
    };

    private static ShipHandling Content() =>
        ShipHandling.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "ship_handling.json")));

    private static TurnStats StarterStats(ShipInertia inertia = ShipInertia.Full) => Content().StatsFor(Starter, 0, 0, inertia);

    private const float Dt = 1f / 60f;

    /// <summary>A pilot drags the mouse right by <paramref name="radians"/>; returns how much of it the controller took (the pending turn is capped, the rest is the pilot's hand moving on).</summary>
    private static float Feed(TurnController c, float radians, TurnStats stats)
    {
        float before = c.Pending.Y;
        c.AddLook(radians, 0f, stats);
        return c.Pending.Y - before;
    }

    // ---- the content --------------------------------------------------------------------------------------

    [Fact]
    public void TheStarterGetsTheBaseNumbersOfTheContent()
    {
        var h = Content();
        var s = h.StatsFor(Starter);
        Assert.Equal(h.TurnRate, s.RateDegPerSecond, 3);
        Assert.Equal(h.TurnAccel, s.AccelDegPerSecondSquared, 3);
        Assert.Equal(h.ReferenceMass, h.MassOf(Starter), 3);
    }

    [Fact]
    public void TheStarterModulesOfTheContentAreTheReferenceShip()
    {
        // the reference ship of the handling file is the starter ship of core.json: same module kinds
        var core = TestContent.CoreFloor;
        var kinds = core.StarterShip!.Modules.Select(m => m.Kind).OrderBy(k => (int)k).ToList();
        Assert.Equal(Starter.OrderBy(k => (int)k).ToList(), kinds);
    }

    [Fact]
    public void HeavierShipsTurnSlowerAndLighterOnesFaster()
    {
        var h = Content();
        var heavy = Starter.Concat(new[] { ModuleKind.Cargo, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Defence, ModuleKind.Reactor }).ToArray();
        var light = new[] { ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.Cargo };
        var s = h.StatsFor(Starter);
        var sh = h.StatsFor(heavy);
        var sl = h.StatsFor(light);
        Assert.True(sh.RateDegPerSecond < s.RateDegPerSecond && sh.AccelDegPerSecondSquared < s.AccelDegPerSecondSquared);
        Assert.True(sl.RateDegPerSecond > s.RateDegPerSecond && sl.AccelDegPerSecondSquared > s.AccelDegPerSecondSquared);
        // Bounded: an enormous ship does not stop turning and a tiny one does not whip round.
        var huge = Enumerable.Repeat(ModuleKind.Core, 60).ToArray();
        var hs = h.StatsFor(huge);
        Assert.True(hs.RateDegPerSecond >= h.TurnRate * MathF.Sqrt(h.MinFactorPercent / 100f) - 0.01f);
        Assert.True(hs.AccelDegPerSecondSquared >= h.TurnAccel * h.MinFactorPercent / 100f - 0.01f);
        var tiny = h.StatsFor(new[] { ModuleKind.Cockpit });
        Assert.True(tiny.AccelDegPerSecondSquared <= h.TurnAccel * h.MaxFactorPercent / 100f + 0.01f);
    }

    [Fact]
    public void EngineResearchRaisesBothNumbers()
    {
        var h = Content();
        var none = h.StatsFor(Starter, 0, 0);
        var ion = h.StatsFor(Starter, 25, 0);
        var vectoring = h.StatsFor(Starter, 25, 40);
        var plasma = h.StatsFor(Starter, 60, 80);
        Assert.True(ion.RateDegPerSecond > none.RateDegPerSecond);
        Assert.Equal(none.AccelDegPerSecondSquared, ion.AccelDegPerSecondSquared, 3);
        Assert.True(vectoring.AccelDegPerSecondSquared > none.AccelDegPerSecondSquared);
        Assert.True(plasma.RateDegPerSecond > vectoring.RateDegPerSecond && plasma.AccelDegPerSecondSquared > vectoring.AccelDegPerSecondSquared);
    }

    [Fact]
    public void LightInertiaIsQuickerThanFull()
    {
        var full = StarterStats(ShipInertia.Full);
        var light = StarterStats(ShipInertia.Light);
        Assert.True(light.RateDegPerSecond > full.RateDegPerSecond);
        Assert.True(light.AccelDegPerSecondSquared > full.AccelDegPerSecondSquared);
        Assert.True(light.TimeToTurn(180f) < full.TimeToTurn(180f));
    }

    [Fact]
    public void TheHandlingFileRejectsBadNumbers()
    {
        Assert.Throws<InvalidDataException>(() => ShipHandling.FromJson("{\"turn_rate\": -3}"));
        Assert.Throws<InvalidDataException>(() => ShipHandling.FromJson("{\"min_factor_percent\": 90, \"max_factor_percent\": 50}"));
        Assert.Throws<InvalidDataException>(() => ShipHandling.FromJson("{\"mass\": {\"hull\": 0}}"));
        Assert.Throws<InvalidDataException>(() => ShipHandling.FromJson("not json"));
        Assert.Equal(ShipHandling.Default.TurnRate, ShipHandling.FromJson("{}").TurnRate); // a missing key keeps the default
    }

    [Fact]
    public void EveryModuleKindHasAMassInTheContent()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "ship_handling.json"));
        foreach (var kind in Enum.GetValues<ModuleKind>())
            Assert.Contains($"\"{ModuleKindIds.Id(kind)}\"", text);
    }

    // ---- the controller -----------------------------------------------------------------------------------

    /// <summary>Total angle in radians the ship has turned from its start, from the attitude.</summary>
    private static float AngleFrom(Quaternion start, Quaternion now) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(start, now)), 0f, 1f));

    [Fact]
    public void TheRateApproachesTheTargetWithoutOvershootingTheTurnRate()
    {
        var stats = StarterStats();
        var c = new TurnController();
        var q = Quaternion.Identity;
        float last = 0f;
        // The pilot keeps the mouse moving to the right for a second: the yaw rate may only grow, up to the turn rate.
        for (int i = 0; i < 60; i++)
        {
            c.AddLook(0.05f, 0f, stats);
            q = c.Step(q, Dt, 0f, stats, true);
            Assert.True(c.Omega.Y >= last - 1e-6f, $"the yaw rate fell from {last} to {c.Omega.Y} at step {i} while the mouse kept turning");
            Assert.True(c.Omega.Y <= stats.RateRad + 1e-5f);
            last = c.Omega.Y;
        }
        Assert.InRange(last, stats.RateRad * 0.99f, stats.RateRad + 1e-5f);
    }

    [Fact]
    public void AfterTheMouseStopsTheRateFallsMonotonicallyToRest()
    {
        var stats = StarterStats();
        var c = new TurnController();
        var q = Quaternion.Identity;
        for (int i = 0; i < 40; i++) { c.AddLook(0.05f, 0f, stats); q = c.Step(q, Dt, 0f, stats, true); }
        c.ClearPending(); // the pilot lets go and the turn already queued is dropped: only the assist is left
        float last = c.Omega.Y;
        int steps = 0;
        while (c.Omega.Y > 0f && steps++ < 600)
        {
            q = c.Step(q, Dt, 0f, stats, true);
            Assert.True(c.Omega.Y <= last + 1e-6f, "the rate rose while the assist was braking");
            last = c.Omega.Y;
        }
        Assert.Equal(0f, c.Omega.Y);
        float stopTime = steps * Dt;
        Assert.InRange(stopTime, stats.RateRad / stats.AccelRad * 0.9f, stats.RateRad / stats.AccelRad + 0.1f); // the damping takes about rate / acceleration
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.3f)]
    [InlineData(1.0f)]
    [InlineData(2.5f)]
    public void ATurnStopsOnTheTargetWithBoundedOvershoot(float radians)
    {
        var stats = StarterStats();
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
        {
            var c = new TurnController();
            var q = Quaternion.Identity;
            float fed = 0f, peak = 0f;
            for (int i = 0; i < (int)(8f / dt); i++)
            {
                // a human drags the mouse: 3 rad per second of input until the whole turn has been given, in whatever the cap allows
                if (fed < radians) fed += Feed(c, Math.Min(radians - fed, 3f * dt), stats);
                q = c.Step(q, dt, 0f, stats, true);
                peak = Math.Max(peak, AngleFrom(Quaternion.Identity, q));
            }
            float final = AngleFrom(Quaternion.Identity, q);
            Assert.Equal(0f, c.Omega.Length());
            Assert.InRange(final, radians * 0.98f - 0.002f, radians * 1.02f + 0.009f); // a coarse 30 fps step may run a third of a degree on
            Assert.True(peak - radians <= 0.02f * radians + 0.009f, $"overshoot of {(peak - radians) * 57.3f:F2} degrees on a turn of {radians * 57.3f:F0} (dt {dt})");
        }
    }

    [Fact]
    public void TheResultDoesNotDependOnTheFrameRateByMuch()
    {
        var stats = StarterStats();
        float TimeToHalfTurn(float dt)
        {
            var c = new TurnController();
            var q = Quaternion.Identity;
            float fed = 0f;
            for (int i = 0; i < (int)(10f / dt); i++)
            {
                if (fed < MathF.PI) fed += Feed(c, Math.Min(MathF.PI - fed, 8f * dt), stats);
                q = c.Step(q, dt, 0f, stats, true);
                if (AngleFrom(Quaternion.Identity, q) > MathF.PI * 0.995f && c.Omega.Length() < 0.01f) return i * dt;
            }
            return float.NaN;
        }
        float a = TimeToHalfTurn(1f / 30f), b = TimeToHalfTurn(1f / 144f);
        Assert.False(float.IsNaN(a) || float.IsNaN(b));
        Assert.InRange(MathF.Abs(a - b), 0f, 0.15f);
    }

    /// <summary>The scripted 180 degree turn of the acceptance test: the pilot drags the mouse until the nose is round, then holds still.</summary>
    private static (float Seconds, int Commands, double Span) ScriptedHalfTurn(TurnStats stats, float dt, bool countCommands)
    {
        var c = new TurnController();
        var q = Quaternion.Identity;
        float fed = 0f;
        double t = 0;
        double reachedAt = double.NaN;
        var throttle = new AttitudeThrottle();
        var known = Attitude.Identity;
        int commands = 0;
        double firstCommand = double.NaN, lastCommand = 0;
        for (int i = 0; i < (int)(8f / dt); i++, t += dt)
        {
            if (fed < MathF.PI) fed += Feed(c, Math.Min(MathF.PI - fed, 10f * dt), stats);
            q = c.Step(q, dt, 0f, stats, true);
            if (double.IsNaN(reachedAt) && AngleFrom(Quaternion.Identity, q) >= MathF.PI * 0.99f && c.Omega.Length() < 0.02f) reachedAt = t;
            if (countCommands && throttle.Offer(known, ShipAttitude.Quantize(q), t) is { } cmd)
            {
                known = Attitude.TryCreate(cmd.W, cmd.X, cmd.Y, cmd.Z, out var a) ? a : known;
                commands++;
                if (double.IsNaN(firstCommand)) firstCommand = t;
                lastCommand = t;
            }
        }
        return ((float)reachedAt, commands, lastCommand - (double.IsNaN(firstCommand) ? 0 : firstCommand));
    }

    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 144f)]
    public void AHalfTurnOfTheStarterTakesBetweenOneAndAHalfAndThreeSeconds(float dt)
    {
        var (seconds, _, _) = ScriptedHalfTurn(StarterStats(), dt, false);
        Assert.InRange(seconds, 1.5f, 3.0f);
    }

    [Fact]
    public void LightInertiaTurnsRoundFasterButStillNotInstantly()
    {
        var (full, _, _) = ScriptedHalfTurn(StarterStats(ShipInertia.Full), Dt, false);
        var (light, _, _) = ScriptedHalfTurn(StarterStats(ShipInertia.Light), Dt, false);
        Assert.True(light < full);
        Assert.True(light > 0.8f);
    }

    [Fact]
    public void TheAttitudeCommandRateStaysWithinTheExistingThrottle()
    {
        var (seconds, commands, span) = ScriptedHalfTurn(StarterStats(), Dt, true);
        Assert.True(commands > 3);
        // at most one command every MinInterval seconds while turning, plus the settle one: never above ten a second
        Assert.True(commands <= span / AttitudeThrottle.MinInterval + 2, $"{commands} commands in {span:F2} s");
        Assert.True(commands / Math.Max(0.1, span) <= 10.5);
        Assert.True(seconds > 1f);
    }

    [Fact]
    public void FiftyThousandRandomInputsKeepTheAttitudeAValidRotation()
    {
        var rng = new Random(20261124);
        var stats = StarterStats();
        var c = new TurnController();
        var q = Quaternion.Identity;
        for (int i = 0; i < 50_000; i++)
        {
            float dt = 0.001f + (float)rng.NextDouble() * 0.05f;
            if (rng.Next(3) == 0) c.AddLook((float)(rng.NextDouble() - 0.5) * 1.2f, (float)(rng.NextDouble() - 0.5) * 1.2f, stats);
            float roll = rng.Next(5) switch { 0 => -1f, 1 => 1f, _ => 0f };
            bool assist = rng.Next(10) != 0;
            if (rng.Next(500) == 0) stats = new TurnStats(20f + (float)rng.NextDouble() * 200f, 30f + (float)rng.NextDouble() * 400f);
            q = c.Step(q, dt, roll, stats, assist);
            Assert.True(FlightOrientation.IsOrthonormal(q, 1e-4f), $"the attitude lost its shape at step {i}");
            Assert.True(float.IsFinite(c.Omega.X) && float.IsFinite(c.Omega.Y) && float.IsFinite(c.Omega.Z));
        }
    }

    [Fact]
    public void BadInputsAreIgnored()
    {
        var stats = StarterStats();
        var c = new TurnController();
        c.AddLook(float.NaN, 1f, stats);
        c.AddLook(float.PositiveInfinity, 0f, stats);
        Assert.Equal(Vector3.Zero, c.Pending);
        var q = c.Step(Quaternion.Identity, float.NaN, 0f, stats, true);
        Assert.Equal(Quaternion.Identity, q);
        q = c.Step(q, 5f, float.NaN, stats, true); // a huge step is clamped
        Assert.True(FlightOrientation.IsOrthonormal(q));
    }

    [Fact]
    public void WithoutAssistTheShipKeepsSpinningUntilCountered()
    {
        var stats = StarterStats();
        var c = new TurnController();
        var q = Quaternion.Identity;
        for (int i = 0; i < 30; i++) { c.AddLook(0.03f, 0f, stats); q = c.Step(q, Dt, 0f, stats, false); }
        c.ClearPending(); // the mouse stops: nothing more is asked of the thrusters
        float spin = c.Omega.Y;
        Assert.True(spin > 0.1f);
        for (int i = 0; i < 240; i++) q = c.Step(q, Dt, 0f, stats, false); // four seconds with no input
        Assert.Equal(spin, c.Omega.Y, 4);
        Assert.True(spin <= stats.RateRad + 1e-5f);
        // counter it with the mouse the other way
        for (int i = 0; i < 400 && c.Omega.Y > 1e-4f; i++) { c.AddLook(-0.03f, 0f, stats); q = c.Step(q, Dt, 0f, stats, false); }
        Assert.True(c.Omega.Y <= 0.05f);
        // turning the assist back on brings it to rest by itself
        c.ClearPending();
        for (int i = 0; i < 300; i++) q = c.Step(q, Dt, 0f, stats, true);
        Assert.Equal(Vector3.Zero, c.Omega);
    }

    [Fact]
    public void TheRollKeysSetADesiredRateAndTheAssistBringsItBack()
    {
        var stats = StarterStats();
        var c = new TurnController();
        var q = Quaternion.Identity;
        for (int i = 0; i < 90; i++) q = c.Step(q, Dt, 1f, stats, true);
        Assert.Equal(stats.RateRad, c.Omega.Z, 4);
        float last = c.Omega.Z;
        for (int i = 0; i < 90; i++)
        {
            q = c.Step(q, Dt, 0f, stats, true);
            Assert.True(c.Omega.Z <= last + 1e-6f);
            last = c.Omega.Z;
        }
        Assert.Equal(0f, c.Omega.Z);
        Assert.True(FlightOrientation.IsOrthonormal(q));
    }

    [Fact]
    public void ResetLeavesNoSpin()
    {
        var stats = StarterStats();
        var c = new TurnController();
        var q = Quaternion.Identity;
        for (int i = 0; i < 30; i++) { c.AddLook(0.05f, 0.02f, stats); q = c.Step(q, Dt, 1f, stats, true); }
        Assert.NotEqual(Vector3.Zero, c.Omega);
        c.Reset();
        Assert.Equal(Vector3.Zero, c.Omega);
        Assert.Equal(Vector3.Zero, c.Pending);
    }

    [Fact]
    public void AFastFlickCannotQueueALongSpin()
    {
        var stats = StarterStats();
        var c = new TurnController();
        c.AddLook(40f, 40f, stats); // many turns of mouse in one go
        Assert.True(MathF.Abs(c.Pending.Y) <= stats.RateRad * TurnController.MaxPendingSeconds + 1e-5f);
        Assert.True(MathF.Abs(c.Pending.X) <= stats.RateRad * TurnController.MaxPendingSeconds + 1e-5f);
    }

    // ---- the setting --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ShipInertia.Off)]
    [InlineData(ShipInertia.Light)]
    [InlineData(ShipInertia.Full)]
    public void TheInertiaAndTheAssistSurviveTheSettingsFile(ShipInertia mode)
    {
        var saved = GameSettings.Defaults() with { ShipInertia = mode, FlightAssist = mode != ShipInertia.Light };
        var loaded = GameSettings.Parse(saved.Serialize());
        Assert.Equal(saved, loaded);
        Assert.Contains("ship_inertia=" + ShipInertias.Id(mode), saved.Serialize());
    }

    [Fact]
    public void AnOldFileThatHadSmoothLookOffKeepsTheQuickResponse()
    {
        var old = GameSettings.Parse("language=en\nsmooth_look=false\n");
        Assert.Equal(ShipInertia.Off, old.ShipInertia);
        Assert.False(old.SmoothLook);
        Assert.Equal(ShipInertia.Full, GameSettings.Parse("language=en\nsmooth_look=true\n").ShipInertia);
        Assert.Equal(ShipInertia.Full, GameSettings.Parse("language=en\n").ShipInertia);
        Assert.True(GameSettings.Parse("language=en\n").FlightAssist);
    }

    [Fact]
    public void ANewFileWinsOverTheOldFlag()
    {
        // walking smoothing off, but the ship has its full inertia: both are written, both are read back
        var s = GameSettings.Parse("smooth_look=false\nship_inertia=full\nflight_assist=false\n");
        Assert.Equal(ShipInertia.Full, s.ShipInertia);
        Assert.False(s.SmoothLook);
        Assert.False(s.FlightAssist);
    }

    [Fact]
    public void BadInertiaValuesKeepTheBaseline()
    {
        var baseline = GameSettings.Defaults() with { ShipInertia = ShipInertia.Light };
        Assert.Equal(ShipInertia.Light, GameSettings.Parse("ship_inertia=banana\n", baseline).ShipInertia);
        Assert.Equal(ShipInertia.Full, (GameSettings.Defaults() with { ShipInertia = (ShipInertia)42 }).Clamped().ShipInertia);
        Assert.True(GameSettings.Parse("flight_assist=maybe\n").FlightAssist);
    }

    [Fact]
    public void TheInertiaCyclesThroughItsThreeValues()
    {
        var m = ShipInertia.Off;
        var seen = new List<ShipInertia>();
        for (int i = 0; i < 3; i++) { m = ShipInertias.Next(m); seen.Add(m); }
        Assert.Equal(new[] { ShipInertia.Light, ShipInertia.Full, ShipInertia.Off }, seen);
    }

    [Fact]
    public void TheFlightAssistRowAppearsOnlyWithInertiaAndFollowsTheState()
    {
        var rows = KeyHints.For(new KeyState { HasTurnInertia = true, FlightAssist = true }).SelectMany(g => g.Rows).ToList();
        Assert.Contains(rows, r => r.Id == "assist" && r.LabelKey == "key.assistOn");
        var off = KeyHints.For(new KeyState { HasTurnInertia = true, FlightAssist = false }).SelectMany(g => g.Rows);
        Assert.Contains(off, r => r.Id == "assist" && r.LabelKey == "key.assistOff" && r.State == KeyRowState.Active);
        var none = KeyHints.For(new KeyState { HasTurnInertia = false }).SelectMany(g => g.Rows);
        Assert.DoesNotContain(none, r => r.Id == "assist");
        Assert.NotEqual("key.assistOn", Loc.T("key.assistOn"));
        Assert.NotEqual("settings.tip.inertia", Loc.T("settings.tip.inertia"));
        Assert.NotEqual("toast.assistOff", Loc.T("toast.assistOff"));
    }
}
