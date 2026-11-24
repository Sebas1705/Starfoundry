using System.Globalization;
using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The engine-free half of the cockpit displays: the page state machine, the HUD modes and their persistence, the display data and the motion easing.</summary>
public class CockpitTests
{
    // ---- the page state machine -----------------------------------------------------------------------

    private static void AssertOnePagePerScreen(CockpitPages p)
    {
        var shown = new[] { CockpitScreen.Left, CockpitScreen.Center, CockpitScreen.Right }.Select(p.PageOn).ToList();
        Assert.Equal(3, shown.Distinct().Count()); // no page twice
        Assert.DoesNotContain(p.Hidden, shown);    // the hidden one is the one missing
    }

    [Fact]
    public void ScreensStartWithSystemsFlightAndNavAndWeaponsOff()
    {
        var p = new CockpitPages();
        Assert.Equal(CockpitPage.Systems, p.PageOn(CockpitScreen.Left));
        Assert.Equal(CockpitPage.Flight, p.PageOn(CockpitScreen.Center));
        Assert.Equal(CockpitPage.Nav, p.PageOn(CockpitScreen.Right));
        Assert.Equal(CockpitPage.Weapons, p.Hidden);
        Assert.Null(p.ScreenOf(CockpitPage.Weapons));
        Assert.Equal(CockpitScreen.Right, p.ScreenOf(CockpitPage.Nav));
    }

    [Fact]
    public void ShowingAPageThatIsOnAnotherScreenSwapsThem()
    {
        var p = new CockpitPages();
        p.Show(CockpitScreen.Center, CockpitPage.Nav);
        Assert.Equal(CockpitPage.Nav, p.PageOn(CockpitScreen.Center));
        Assert.Equal(CockpitPage.Flight, p.PageOn(CockpitScreen.Right)); // the right screen got the centre's page
        Assert.Equal(CockpitPage.Weapons, p.Hidden);
        AssertOnePagePerScreen(p);
    }

    [Fact]
    public void ShowingTheHiddenPageReplacesWhatTheScreenHad()
    {
        var p = new CockpitPages();
        p.Show(CockpitScreen.Left, CockpitPage.Weapons);
        Assert.Equal(CockpitPage.Weapons, p.PageOn(CockpitScreen.Left));
        Assert.Equal(CockpitPage.Systems, p.Hidden);
        AssertOnePagePerScreen(p);
    }

    [Fact]
    public void ShowingThePageAlreadyThereChangesNothing()
    {
        var p = new CockpitPages();
        p.Show(CockpitScreen.Center, CockpitPage.Flight);
        Assert.Equal(CockpitPage.Flight, p.PageOn(CockpitScreen.Center));
        AssertOnePagePerScreen(p);
    }

    [Fact]
    public void CyclingVisitsEveryPageAndKeepsTheInvariant()
    {
        var p = new CockpitPages();
        var seen = new HashSet<CockpitPage>();
        for (int i = 0; i < 8; i++)
        {
            p.Cycle(CockpitScreen.Center, 1);
            seen.Add(p.PageOn(CockpitScreen.Center));
            AssertOnePagePerScreen(p);
        }
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void CyclingBackwardWrapsAndZeroDoesNothing()
    {
        var p = new CockpitPages();
        p.Cycle(CockpitScreen.Left, 0);
        Assert.Equal(CockpitPage.Systems, p.PageOn(CockpitScreen.Left));
        p.Cycle(CockpitScreen.Left, -1);
        Assert.Equal(CockpitPage.Nav, p.PageOn(CockpitScreen.Left)); // one before Systems is Nav; the screen that had Nav now has Systems
        Assert.Equal(CockpitPage.Systems, p.PageOn(CockpitScreen.Right));
        AssertOnePagePerScreen(p);
    }

    [Fact]
    public void RandomPressesNeverBreakTheInvariant()
    {
        var p = new CockpitPages();
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            var screen = (CockpitScreen)rng.Next(3);
            if (rng.Next(2) == 0) p.Cycle(screen, rng.Next(-3, 4));
            else p.Show(screen, (CockpitPage)rng.Next(5)); // 4 is not a page: ignored
            AssertOnePagePerScreen(p);
        }
    }

    [Fact]
    public void ScreenStepStopsAtTheEnds()
    {
        Assert.Equal(CockpitScreen.Center, CockpitPages.Step(CockpitScreen.Left, 1));
        Assert.Equal(CockpitScreen.Right, CockpitPages.Step(CockpitScreen.Center, 5));
        Assert.Equal(CockpitScreen.Right, CockpitPages.Step(CockpitScreen.Right, 1));
        Assert.Equal(CockpitScreen.Left, CockpitPages.Step(CockpitScreen.Left, -1));
    }

    [Fact]
    public void ButtonsHitTestInsideOnly()
    {
        var b = new CockpitButton(10, 20, 30, 40, CockpitAction.ShowPage, 2);
        Assert.True(b.Contains(10, 20));
        Assert.True(b.Contains(39.9f, 59.9f));
        Assert.False(b.Contains(40, 30));
        Assert.False(b.Contains(9.9f, 30));
    }

    // ---- HUD modes ------------------------------------------------------------------------------------

    [Fact]
    public void HudModeCyclesFullCockpitMinimal()
    {
        Assert.Equal(HudMode.Cockpit, HudModes.Next(HudMode.Full));
        Assert.Equal(HudMode.Minimal, HudModes.Next(HudMode.Cockpit));
        Assert.Equal(HudMode.Full, HudModes.Next(HudMode.Minimal));
    }

    [Fact]
    public void FullModeShowsEverythingSeatedOrNot()
    {
        foreach (bool seated in new[] { true, false })
            Assert.Equal(new HudLayers(true, true, true, true, true, true), HudModes.Layers(HudMode.Full, seated));
    }

    [Fact]
    public void CockpitModeHidesTheOverlayOnlyWhileSeated()
    {
        var seated = HudModes.Layers(HudMode.Cockpit, true);
        Assert.False(seated.Cards);
        Assert.False(seated.Markers);
        Assert.False(seated.FlightReadout);
        Assert.False(seated.Toolbar);
        Assert.True(seated.KeyPanel);
        Assert.True(seated.Toasts);
        Assert.Equal(HudModes.Layers(HudMode.Full, false), HudModes.Layers(HudMode.Cockpit, false)); // walking and building keep the whole overlay
    }

    [Fact]
    public void MinimalModeKeepsOnlyNotificationsSeated()
    {
        var seated = HudModes.Layers(HudMode.Minimal, true);
        Assert.Equal(new HudLayers(false, false, false, false, false, true), seated);
        var standing = HudModes.Layers(HudMode.Minimal, false);
        Assert.True(standing.Toolbar);
        Assert.True(standing.Toasts);
        Assert.False(standing.KeyPanel);
    }

    [Theory]
    [InlineData("full", HudMode.Full)]
    [InlineData("COCKPIT", HudMode.Cockpit)]
    [InlineData(" minimal ", HudMode.Minimal)]
    public void HudModeIdsParse(string id, HudMode expected)
    {
        Assert.True(HudModes.TryParse(id, out var mode));
        Assert.Equal(expected, mode);
        Assert.True(HudModes.TryParse(HudModes.Id(expected), out var again));
        Assert.Equal(expected, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hidden")]
    [InlineData(null)]
    public void UnknownHudModeIdsAreRefused(string? id)
    {
        Assert.False(HudModes.TryParse(id, out var mode));
        Assert.Equal(HudMode.Full, mode);
    }

    [Theory]
    [InlineData(HudMode.Full)]
    [InlineData(HudMode.Cockpit)]
    [InlineData(HudMode.Minimal)]
    public void HudModeSurvivesTheSettingsFile(HudMode mode)
    {
        var saved = GameSettings.Defaults() with { HudMode = mode, SmoothLook = false };
        var loaded = GameSettings.Parse(saved.Serialize());
        Assert.Equal(mode, loaded.HudMode);
        Assert.False(loaded.SmoothLook);
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void OldSettingsFilesGetTheDefaults()
    {
        var loaded = GameSettings.Parse("language=en\nfov=90\n");
        Assert.Equal(HudMode.Full, loaded.HudMode);
        Assert.True(loaded.SmoothLook);
        Assert.Equal(90f, loaded.Fov);
    }

    [Fact]
    public void BadHudModeValuesKeepTheBaseline()
    {
        var baseline = GameSettings.Defaults() with { HudMode = HudMode.Cockpit };
        Assert.Equal(HudMode.Cockpit, GameSettings.Parse("hud_mode=banana\nsmooth_look=maybe\n", baseline).HudMode);
        Assert.True(GameSettings.Parse("smooth_look=maybe\n", baseline).SmoothLook);
        Assert.Equal(HudMode.Full, (GameSettings.Defaults() with { HudMode = (HudMode)99 }).Clamped().HudMode);
    }

    // ---- display data ---------------------------------------------------------------------------------

    [Fact]
    public void VelocityAheadSitsInTheCentre()
    {
        var m = CockpitMath.Velocity(0f, 0f, 5f);
        Assert.False(m.Still);
        Assert.False(m.Behind);
        Assert.Equal(0f, m.X, 3);
        Assert.Equal(0f, m.Y, 3);
    }

    [Fact]
    public void VelocityToTheSideReachesTheRimAtNinetyDegrees()
    {
        var m = CockpitMath.Velocity(4f, 0f, 0f);
        Assert.Equal(1f, m.X, 3);
        Assert.Equal(0f, m.Y, 3);
        Assert.False(m.Behind);
        var half = CockpitMath.Velocity(1f, 0f, 1f); // 45 degrees
        Assert.Equal(0.5f, half.X, 3);
    }

    [Fact]
    public void VelocityUpIsPositiveYAndBackwardIsFlagged()
    {
        var up = CockpitMath.Velocity(0f, 3f, 3f);
        Assert.True(up.Y > 0f);
        Assert.Equal(0f, up.X, 3);
        var back = CockpitMath.Velocity(1f, 0f, -5f);
        Assert.True(back.Behind);
        Assert.Equal(1f, MathF.Sqrt(back.X * back.X + back.Y * back.Y), 3); // on the rim
        var straightBack = CockpitMath.Velocity(0f, 0f, -5f);
        Assert.True(straightBack.Behind);
        Assert.False(straightBack.Still);
    }

    [Fact]
    public void SlowOrBrokenVelocityIsStill()
    {
        Assert.True(CockpitMath.Velocity(0f, 0f, 0.01f).Still);
        Assert.True(CockpitMath.Velocity(float.NaN, 0f, 1f).Still);
        Assert.True(CockpitMath.Velocity(float.PositiveInfinity, 0f, 0f).Still);
    }

    [Fact]
    public void MarkerStaysInsideTheDiscForAnyVelocity()
    {
        var rng = new Random(3);
        for (int i = 0; i < 1000; i++)
        {
            var m = CockpitMath.Velocity((float)rng.NextDouble() * 20 - 10, (float)rng.NextDouble() * 20 - 10, (float)rng.NextDouble() * 20 - 10);
            Assert.True(m.X * m.X + m.Y * m.Y <= 1.0001f);
        }
    }

    [Fact]
    public void FractionsClampAndSurviveZeroMaxima()
    {
        Assert.Equal(0.5f, CockpitMath.Fraction(5f, 10f));
        Assert.Equal(1f, CockpitMath.Fraction(50f, 10f));
        Assert.Equal(0f, CockpitMath.Fraction(-3f, 10f));
        Assert.Equal(0f, CockpitMath.Fraction(5f, 0f));
        Assert.Equal(0f, CockpitMath.Fraction(float.NaN, 10f));
    }

    [Fact]
    public void FlightDataWorksItsFractionsOut()
    {
        var d = new FlightDisplayData(6f, 12f, 0.5f, false, true, 9f, 40, 100, 30, 40, 750, 5, 60 * 60, 12, 48);
        Assert.Equal(0.5f, d.SpeedFraction);
        Assert.Equal(0.75f, d.TargetFraction, 3);
        Assert.Equal(0.4f, d.HullFraction, 3);
        Assert.Equal(0.75f, d.PowerFraction, 3);
        Assert.Equal(0.25f, d.HoldFraction, 3);
        Assert.False(d.FuelLow);
        Assert.True(d with { FuelTicksLeft = 0, Hydrogen = 0 } is { FuelLow: true });
        Assert.True(d with { FuelTicksLeft = 5 } is { FuelLow: true });
    }

    [Fact]
    public void SystemsHealthyFractionHandlesNoModules()
    {
        Assert.Equal(0f, new SystemsDisplayData(0, 0, 0, 0, 0, 0, 0, false).HealthyFraction);
        Assert.Equal(0.75f, new SystemsDisplayData(8, 2, 10, 10, 0, 0, 0, false).HealthyFraction, 3);
    }

    [Fact]
    public void ActiveWeaponGroupFollowsTheFittings()
    {
        static WeaponsDisplayData W(bool hardpoints, bool laser, bool gun, WeaponGroup group, bool armed) => new(hardpoints, laser, gun, group, armed, false, false, true, 0);
        Assert.Equal(WeaponGroup.Laser, W(false, true, true, WeaponGroup.Laser, armed: true).ActiveGroup);   // ADR 0036: the group in hand decides, in every world, not the armed flag
        Assert.Equal(WeaponGroup.Guns, W(false, true, true, WeaponGroup.Guns, armed: false).ActiveGroup);
        Assert.Equal(WeaponGroup.Guns, W(true, true, true, WeaponGroup.Guns, armed: true).ActiveGroup);
        Assert.Equal(WeaponGroup.Laser, W(true, true, true, WeaponGroup.Laser, armed: false).ActiveGroup);
        Assert.Equal(WeaponGroup.Laser, W(true, true, false, WeaponGroup.Guns, armed: false).ActiveGroup); // no gun fitted: the laser stays
        Assert.Null(W(true, false, false, WeaponGroup.Laser, armed: false).ActiveGroup);                    // nothing fitted
    }

    [Fact]
    public void BandsFollowTheHudBars()
    {
        Assert.Equal(HealthBand.Good, CockpitMath.Band(1f));
        Assert.Equal(HealthBand.Critical, CockpitMath.Band(0.05f));
        Assert.Equal(HealthBand.Critical, CockpitMath.Band(-1f));
    }

    [Fact]
    public void SpeedAndDistanceAreFormattedForReading()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        var es = CultureInfo.GetCultureInfo("es-ES");
        Assert.Equal("0.0", CockpitMath.Speed(-0f, en));
        Assert.Equal("0.0", CockpitMath.Speed(-2f, en));
        Assert.Equal("3,5", CockpitMath.Speed(3.5f, es));
        Assert.Equal("120", CockpitMath.Speed(120.4f, en));
        Assert.Equal("0.0", CockpitMath.Speed(float.NaN, en));
        Assert.Equal("42", CockpitMath.Distance(41.6f, en));
        Assert.Equal("1.2k", CockpitMath.Distance(1234f, en));
        Assert.Equal("0", CockpitMath.Distance(-5f, en));
    }

    [Fact]
    public void ScanlinesDarkenEveryFourthRow()
    {
        Assert.Equal(0.92f, CockpitMath.Scanline(0));
        Assert.Equal(1f, CockpitMath.Scanline(1));
        Assert.Equal(1f, CockpitMath.Scanline(3));
        Assert.Equal(0.92f, CockpitMath.Scanline(4));
    }

    // ---- the navigation list --------------------------------------------------------------------------

    private static NavTarget Dep(int id, float d, bool sel = false) => new(NavKind.Deposit, id, "ore" + id, d, 0f, "i", sel);

    [Fact]
    public void NavListPutsThreatsFirstThenTheSelectionThenDistance()
    {
        var rows = NavList.Build(new[]
        {
            Dep(1, 30f), Dep(2, 10f), Dep(3, 90f, sel: true),
            new NavTarget(NavKind.Enemy, -1, "raider", 50f, 0f, "e"),
            new NavTarget(NavKind.Port, -1, "port", 20f, 0f, "p"),
            new NavTarget(NavKind.Gate, 4, "gate", 900f, 0f, "g"),
        }, 10);
        Assert.Equal(new[] { NavKind.Enemy, NavKind.Deposit, NavKind.Gate, NavKind.Port, NavKind.Deposit, NavKind.Deposit }, rows.Select(r => r.Kind));
        Assert.Equal(3, rows[1].Id);
        Assert.Equal(new[] { 2, 1 }, rows.Skip(4).Select(r => r.Id));
    }

    [Fact]
    public void NavListKeepsTheSelectedDepositEvenWhenFar()
    {
        var all = Enumerable.Range(1, 12).Select(i => Dep(i, i * 10f)).Append(Dep(99, 5000f, sel: true));
        var rows = NavList.Build(all, 5);
        Assert.Equal(5, rows.Count);
        Assert.Contains(rows, r => r.Id == 99);
        Assert.Empty(NavList.Build(all, 0));
    }

    [Fact]
    public void NavSelectionStepsAmongDepositsAndWraps()
    {
        var rows = NavList.Build(new[] { Dep(5, 10f), Dep(7, 20f), Dep(9, 30f), new NavTarget(NavKind.Enemy, -1, "x", 1f, 0f, "e") }, 10);
        Assert.Equal(7, NavList.Step(rows, 5, 1));
        Assert.Equal(5, NavList.Step(rows, 9, 1));  // wraps
        Assert.Equal(9, NavList.Step(rows, 5, -1)); // wraps back
        Assert.Equal(5, NavList.Step(rows, -1, 1)); // nothing selected: the first
        Assert.Equal(9, NavList.Step(rows, -1, -1));
        Assert.Equal(-1, NavList.Step(new[] { rows.First(r => r.Kind == NavKind.Enemy) }, -1, 1));
    }

    // ---- motion easing --------------------------------------------------------------------------------

    [Fact]
    public void DampMovesTowardTheTargetWithoutOvershooting()
    {
        float v = 0f;
        for (int i = 0; i < 600; i++)
        {
            float next = MotionSmoothing.Damp(v, 10f, 8f, 1f / 60f);
            Assert.True(next >= v && next <= 10f);
            v = next;
        }
        Assert.Equal(10f, v, 2);
        Assert.Equal(3f, MotionSmoothing.Damp(3f, 9f, 0f, 1f)); // no rate, no motion
        Assert.Equal(3f, MotionSmoothing.Damp(3f, 9f, 5f, 0f));
    }

    [Fact]
    public void DampIsFrameRateIndependent()
    {
        float a = 0f, b = 0f;
        for (int i = 0; i < 30; i++) a = MotionSmoothing.Damp(a, 1f, 6f, 1f / 60f);
        for (int i = 0; i < 120; i++) b = MotionSmoothing.Damp(b, 1f, 6f, 1f / 240f);
        Assert.Equal(a, b, 3);
    }

    [Fact]
    public void DampAngleTakesTheShortWayRound()
    {
        float a = MotionSmoothing.DampAngle(3.0f, -3.0f, 100f, 1f); // 3.0 -> -3.0 is 0.28 rad through pi, not 6 the long way
        Assert.True(MathF.Abs(MathF.IEEERemainder(a - -3.0f, MathF.Tau)) < 0.01f);
    }

    [Fact]
    public void ApproachLimitsAccelerationAndReachesTheTargetExactly()
    {
        var v = Vector3.Zero;
        var target = new Vector3(6f, 0f, 0f);
        v = MotionSmoothing.Approach(v, target, 20f, 30f, 0.1f);
        Assert.Equal(2f, v.X, 4); // 20 * 0.1
        for (int i = 0; i < 10; i++) v = MotionSmoothing.Approach(v, target, 20f, 30f, 0.1f);
        Assert.Equal(target, v);
        v = MotionSmoothing.Approach(v, Vector3.Zero, 20f, 30f, 0.1f);
        Assert.Equal(3f, v.X, 4); // slowing uses the stronger deceleration
        Assert.Equal(target, MotionSmoothing.Approach(target, target, 1f, 1f, 1f));
        Assert.Equal(v, MotionSmoothing.Approach(v, Vector3.Zero, 20f, 30f, 0f));
    }

    [Fact]
    public void EaseIsFlatAtTheEnds()
    {
        Assert.Equal(0f, MotionSmoothing.Ease(-1f));
        Assert.Equal(1f, MotionSmoothing.Ease(2f));
        Assert.Equal(0.5f, MotionSmoothing.Ease(0.5f));
        Assert.True(MotionSmoothing.Ease(0.1f) < 0.1f);
    }

    [Fact]
    public void EyeBlendSlidesToTheNewPlaceAndSettles()
    {
        var blend = new EyeBlend();
        var seat = new Vector3(4f, 2.7f, 6.6f);
        var standing = new Vector3(4f, 1.7f, 6.6f);
        Assert.Equal(seat, blend.Update(seat, false, 1f / 60f)); // first frame: no blend
        var first = blend.Update(standing, true, 1f / 60f);       // stood up: the eye does not jump
        Assert.True(first.Y > standing.Y + 0.5f);
        Assert.True(first.Y < seat.Y);
        var eye = first;
        for (int i = 0; i < 300; i++)
        {
            var next = blend.Update(standing, false, 1f / 60f);
            Assert.True(next.Y <= eye.Y + 1e-5f); // only ever moves toward the new eye
            eye = next;
        }
        Assert.Equal(standing, eye);
        Assert.Equal(Vector3.Zero, blend.Offset);
    }
}
