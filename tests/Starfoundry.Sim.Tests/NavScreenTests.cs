using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The navigation screen (N): the console layouts as content, legibility, the auto-frame, the map camera and the page's choice of target.</summary>
public class NavScreenTests
{
    private static CockpitLayouts Content() =>
        CockpitLayouts.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "cockpit_layouts.json")));

    /// <summary>The seated eye in the cockpit module's frame (the flight pivot of ADR 0030).</summary>
    private static readonly Vector3 Eye = new(4f, 2.7f, 6.6f);

    // ---- layouts as content --------------------------------------------------------------------------------

    [Fact]
    public void TheContentFileHasTheStarterLayoutAndTheBuiltInOneMatchesIt()
    {
        var file = Content().Get(CockpitLayouts.StarterId);
        var built = CockpitLayouts.BuiltInStarter;
        Assert.Equal(built.DeckOrigin, file.DeckOrigin);
        Assert.Equal(built.Squeeze, file.Squeeze);
        Assert.Equal(built.DeckTiltDegrees, file.DeckTiltDegrees);
        for (int i = 0; i < 3; i++) Assert.Equal(built.Screens[i], file.Screens[i]);
    }

    [Fact]
    public void TheConsoleGeometryIsTheOneOfTheModel()
    {
        // The console model (ModuleModels.Cockpit) builds its deck at (4, 1.02, 2.55), tilted 18 degrees, squeezed to 72 and 78 percent about X = 4.
        var l = Content().Get(CockpitLayouts.StarterId);
        Assert.Equal(new Vector3(4f, 1.02f, 2.55f), l.DeckOrigin);
        Assert.Equal(18f, l.DeckTiltDegrees);
        Assert.Equal(new Vector3(0.72f, 0.78f, 1f), l.Squeeze);
        Assert.Equal(4f, l.SqueezeAboutX);
    }

    [Fact]
    public void TheCentreScreenIsAtLeastOnePointSixTimesTheOldOne()
    {
        const float oldArea = 1.9f * 0.78f; // the centre screen of the first cockpit displays
        var l = Content().Get(CockpitLayouts.StarterId);
        Assert.True(l.AreaOf(CockpitScreen.Center) >= 1.6f * oldArea, $"area {l.AreaOf(CockpitScreen.Center):F2} against {oldArea:F2}");
    }

    [Fact]
    public void TheScreensFitTheDeckOfTheConsole()
    {
        // The deck plate is 4.9 wide and 1.25 deep, the cheek panels stand at 2.45 from the middle (0.18 thick): a screen with its bezel must stay clear of them.
        var l = Content().Get(CockpitLayouts.StarterId);
        foreach (var s in l.Screens)
        {
            Assert.True(MathF.Abs(s.X) + s.Width / 2f + l.BezelMargin <= 2.45f - 0.09f + 0.001f, $"a screen at {s.X} with width {s.Width} reaches the cheek panel");
            Assert.True(MathF.Abs(s.Z) + s.Depth / 2f + l.BezelMargin <= 1.25f / 2f + 0.001f, "a screen runs off the deck in depth");
        }
        // and they do not overlap each other
        Assert.True(l.Screens[0].X + l.Screens[0].Width / 2f < l.Screens[1].X - l.Screens[1].Width / 2f);
        Assert.True(l.Screens[1].X + l.Screens[1].Width / 2f < l.Screens[2].X - l.Screens[2].Width / 2f);
    }

    [Fact]
    public void TheCentreScreenIsTiltedTowardsTheEye()
    {
        var l = Content().Get(CockpitLayouts.StarterId);
        Assert.True(l.Of(CockpitScreen.Center).TiltDegrees >= 15f);
        // The normal of the tilted screen points more at the eye than a screen lying on the deck.
        float Facing(ScreenRect d)
        {
            var m = l.ScreenToModule(d);
            var origin = Vector3.Transform(Vector3.Zero, m);
            var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m));
            return Vector3.Dot(normal, Vector3.Normalize(Eye - origin));
        }
        var flat = l.Of(CockpitScreen.Center) with { TiltDegrees = 0f };
        Assert.True(Facing(l.Of(CockpitScreen.Center)) > Facing(flat) + 0.1f);
        Assert.True(Facing(l.Of(CockpitScreen.Center)) > 0.8f, "the centre screen should face the eye");
    }

    [Fact]
    public void ATiltedScreenKeepsItsNearEdgeOnTheDeck()
    {
        var l = Content().Get(CockpitLayouts.StarterId);
        var d = l.Of(CockpitScreen.Center);
        var flat = d with { TiltDegrees = 0f };
        var (_, _, topFlat, bottomFlat) = LayoutCorners(l, flat);
        var (_, _, top, bottom) = LayoutCorners(l, d);
        Assert.Equal(bottomFlat.X, bottom.X, 3);
        Assert.Equal(bottomFlat.Y, bottom.Y, 3);
        Assert.Equal(bottomFlat.Z, bottom.Z, 3);
        Assert.True(top.Y > topFlat.Y + 0.1f, "the far edge should rise");
    }

    private static (Vector3 Left, Vector3 Right, Vector3 Top, Vector3 Bottom) LayoutCorners(CockpitLayout l, ScreenRect d)
    {
        var m = l.ScreenToModule(d);
        Vector3 P(float x, float y) => Vector3.Transform(new Vector3(x, y, 0f), m);
        return (P(-d.Width / 2f, 0f), P(d.Width / 2f, 0f), P(0f, d.Depth / 2f), P(0f, -d.Depth / 2f));
    }

    [Fact]
    public void TheKeyValuesOfTheNavScreenAreAtLeastFourteenPixelsAt720p()
    {
        var l = Content().Get(CockpitLayouts.StarterId);
        var def = l.Of(CockpitScreen.Center);
        var (name, distance, speed) = NavPage.KeyFonts(def.PxW);
        foreach (int font in new[] { name, distance, speed })
        {
            float px = l.TextPixels(CockpitScreen.Center, font, Eye, 720f, 80f);
            Assert.True(px >= 14f, $"a {font} px font shows {px:F1} px tall at 1280x720 and 80 degrees from the seat");
        }
        // and 1080p only gets better
        Assert.True(l.TextPixels(CockpitScreen.Center, name, Eye, 1080f, 80f) > l.TextPixels(CockpitScreen.Center, name, Eye, 720f, 80f));
    }

    [Fact]
    public void TheOldNavTextWasNotReadableAtTheSameSize()
    {
        // the first version drew its list at 15 px on a 1.9 wide screen: well under the new legibility
        var old = CockpitLayouts.BuiltInStarter with
        {
            Screens = new[] { new ScreenRect(-1.72f, -0.01f, 1.25f, 0.74f, 496, 400, 0f), new ScreenRect(0f, -0.02f, 1.9f, 0.78f, 720, 400, 0f), new ScreenRect(1.72f, -0.01f, 1.25f, 0.74f, 496, 400, 0f) },
        };
        Assert.True(old.TextPixels(CockpitScreen.Center, 15f, Eye, 720f, 80f) < 6f);
    }

    [Fact]
    public void ASecondLayoutIsAnotherEntryOfTheFile()
    {
        var all = Content();
        Assert.Contains("wide_console", all.Ids);
        var wide = all.Get("wide_console");
        Assert.True(wide.AreaOf(CockpitScreen.Center) > all.Get(CockpitLayouts.StarterId).AreaOf(CockpitScreen.Center));
        Assert.Equal(all.Get(CockpitLayouts.StarterId), all.Get("no_such_style")); // an unknown style falls back to the starter console
        Assert.True(wide.TextPixels(CockpitScreen.Center, 52f, Eye, 720f, 80f) > 0f);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"layouts\": {}}")]
    [InlineData("{\"layouts\": {\"starter\": {}}}")]
    [InlineData("not json")]
    public void BadLayoutFilesAreRejected(string json) => Assert.Throws<InvalidDataException>(() => CockpitLayouts.FromJson(json));

    [Fact]
    public void AScreenWithAnUnusableSizeIsRejected()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "cockpit_layouts.json"));
        Assert.Throws<InvalidDataException>(() => CockpitLayouts.FromJson(text.Replace("\"width\": 2.30", "\"width\": 0.0")));
        Assert.Throws<InvalidDataException>(() => CockpitLayouts.FromJson(text.Replace("\"px_w\": 612", "\"px_w\": 5")));
        Assert.Throws<InvalidDataException>(() => CockpitLayouts.FromJson(text.Replace("\"tilt_degrees\": 22.0", "\"tilt_degrees\": 120.0")));
    }

    // ---- the auto-frame ------------------------------------------------------------------------------------

    private static CockpitScreen? Run(AutoFrameTracker t, float seconds, CockpitScreen? aim, float mouse = 0f, bool keys = false, bool allowed = true, float dt = 1f / 60f)
    {
        CockpitScreen? last = null;
        for (float x = 0; x < seconds - 1e-4f; x += dt) last = t.Update(dt, aim, mouse * dt, keys, allowed);
        return last;
    }

    [Fact]
    public void LookingAtAScreenForAboutFourTenthsOfASecondFramesIt()
    {
        var t = new AutoFrameTracker();
        Assert.Null(Run(t, 0.3f, CockpitScreen.Center));
        Assert.Equal(CockpitScreen.Center, Run(t, 0.2f, CockpitScreen.Center));
    }

    [Fact]
    public void AGlanceIsNotEnough()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.3f, CockpitScreen.Left);
        Run(t, 0.1f, null);
        Run(t, 0.3f, CockpitScreen.Left); // the dwell starts again
        Assert.Null(t.Framed);
    }

    [Fact]
    public void ChangingScreenRestartsTheDwell()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.3f, CockpitScreen.Left);
        Run(t, 0.3f, CockpitScreen.Center);
        Assert.Null(t.Framed);
        Run(t, 0.2f, CockpitScreen.Center);
        Assert.Equal(CockpitScreen.Center, t.Framed);
    }

    [Fact]
    public void MovingTheMouseAwayLetsGo()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.6f, CockpitScreen.Center);
        Assert.Equal(CockpitScreen.Center, t.Framed);
        Run(t, 0.2f, CockpitScreen.Center, mouse: 900f); // a fast sweep of the mouse
        Assert.Null(t.Framed);
    }

    [Fact]
    public void AStillishMouseDoesNotLetGo()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.6f, CockpitScreen.Center);
        Run(t, 3f, CockpitScreen.Center, mouse: 20f); // jitter of a hand resting on the mouse: 20 pixels a second
        Assert.Equal(CockpitScreen.Center, t.Framed);
    }

    [Fact]
    public void AMovingMouseNeverStartsAFrame()
    {
        var t = new AutoFrameTracker();
        Run(t, 3f, CockpitScreen.Center, mouse: 800f);
        Assert.Null(t.Framed);
    }

    [Fact]
    public void AFlightKeyLetsGoAndKeepsItFromStarting()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.6f, CockpitScreen.Center);
        Assert.NotNull(t.Framed);
        Run(t, 0.02f, CockpitScreen.Center, keys: true);
        Assert.Null(t.Framed);
        Run(t, 2f, CockpitScreen.Center, keys: true);
        Assert.Null(t.Framed);
        Run(t, 0.6f, CockpitScreen.Center);
        Assert.NotNull(t.Framed);
    }

    [Fact]
    public void FullFocusOrAMenuTakesTheViewBack()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.6f, CockpitScreen.Center);
        Run(t, 0.02f, CockpitScreen.Center, allowed: false); // F4, a panel or standing up
        Assert.Null(t.Framed);
        Run(t, 1f, CockpitScreen.Center, allowed: false);
        Assert.Null(t.Framed);
    }

    [Fact]
    public void TheCrosshairLeavingTheScreenEndsTheFrameAfterAShortGrace()
    {
        var t = new AutoFrameTracker();
        Run(t, 0.6f, CockpitScreen.Right);
        Run(t, 0.1f, null);
        Assert.Equal(CockpitScreen.Right, t.Framed); // the head is still turning
        Run(t, 0.3f, null);
        Assert.Null(t.Framed);
    }

    [Fact]
    public void RandomInputsNeverBreakTheTracker()
    {
        var rng = new Random(7);
        var t = new AutoFrameTracker();
        for (int i = 0; i < 20_000; i++)
        {
            CockpitScreen? aim = rng.Next(4) == 0 ? null : (CockpitScreen)rng.Next(3);
            var framed = t.Update((float)rng.NextDouble() * 0.05f, aim, (float)rng.NextDouble() * 40f, rng.Next(20) == 0, rng.Next(30) != 0);
            Assert.True(framed is null || Enum.IsDefined(framed.Value));
        }
        t.Update(float.NaN, null, float.NaN, false, true);
    }

    // ---- the map camera ------------------------------------------------------------------------------------

    [Fact]
    public void AheadIsUpTheMapAndRightIsRight()
    {
        var m = new NavMapCamera();
        m.Orbit(0f, MathF.PI / 2f - m.Tilt); // straight down: the plan view
        Assert.Equal(MathF.PI / 2f, m.Tilt, 4);
        var ahead = m.Project(new Vector3(0, 0, m.Range));
        Assert.Equal(0f, ahead.X, 4);
        Assert.Equal(1f, ahead.Y, 4);
        var right = m.Project(new Vector3(m.Range, 0, 0));
        Assert.Equal(1f, right.X, 4);
        Assert.Equal(0f, right.Y, 4);
        Assert.Equal(0f, m.Project(Vector3.Zero).X, 6);
    }

    [Fact]
    public void ARaisedPointAppearsAboveItsShadow()
    {
        var m = new NavMapCamera();
        var p = m.Project(new Vector3(0, 100, 0));
        Assert.True(p.Y > p.ShadowY);
        Assert.Equal(p.X, p.ShadowX, 5);
        Assert.Equal(0f, p.ShadowY, 5);
    }

    [Fact]
    public void RotatingTurnsTheMapWithoutLosingTheDistance()
    {
        var m = new NavMapCamera();
        m.Orbit(0f, MathF.PI / 2f - m.Tilt);
        var v = new Vector3(30, 0, 40);
        float before = MathF.Sqrt(m.Project(v).X * m.Project(v).X + m.Project(v).Y * m.Project(v).Y);
        for (int i = 0; i < 25; i++)
        {
            m.Orbit(0.3f, 0f);
            var p = m.Project(v);
            Assert.Equal(before, MathF.Sqrt(p.X * p.X + p.Y * p.Y), 4);
        }
    }

    [Fact]
    public void TheTiltAndTheRangeStayInBounds()
    {
        var m = new NavMapCamera();
        for (int i = 0; i < 100; i++) m.Orbit(10f, 3f);
        Assert.InRange(m.Tilt, NavMapCamera.MinTilt, NavMapCamera.MaxTilt);
        for (int i = 0; i < 100; i++) m.Orbit(-10f, -3f);
        Assert.InRange(m.Tilt, NavMapCamera.MinTilt, NavMapCamera.MaxTilt);
        for (int i = 0; i < 100; i++) m.Zoom(1);
        Assert.Equal(NavMapCamera.MinRange, m.Range);
        for (int i = 0; i < 100; i++) m.Zoom(-1);
        Assert.Equal(NavMapCamera.MaxRange, m.Range);
        m.Orbit(float.NaN, float.NaN);
        Assert.True(float.IsFinite(m.Yaw) && float.IsFinite(m.Tilt));
    }

    [Fact]
    public void ZoomingTurnsTheAutomaticRangeOffAndResetTurnsItOn()
    {
        var m = new NavMapCamera();
        Assert.True(m.Auto);
        m.Follow(1000f, 10f);
        Assert.True(m.Range > 1000f);
        m.Zoom(2);
        Assert.False(m.Auto);
        float r = m.Range;
        m.Follow(50f, 10f);
        Assert.Equal(r, m.Range);
        m.Reset();
        Assert.True(m.Auto);
        m.Follow(50f, 10f);
        Assert.True(m.Range < r);
    }

    [Fact]
    public void ZoomingInIsZoomingInWhateverTheViewAngle()
    {
        var m = new NavMapCamera();
        var v = new Vector3(20, 5, 60);
        float far = Math.Abs(m.Project(v).Y);
        m.Zoom(1);
        Assert.True(Math.Abs(m.Project(v).Y) > far);
    }

    // ---- what the page shows -------------------------------------------------------------------------------

    private static NavTarget Row(NavKind k, int id, float d, bool selected = false) => new(k, id, k + id.ToString(), d, 10f, "x", selected);

    [Fact]
    public void ThePickedTargetWinsThenTheSelectedDepositThenTheFirstRow()
    {
        var rows = new[] { Row(NavKind.Enemy, 1, 30f), Row(NavKind.Deposit, 4, 80f, true), Row(NavKind.Port, 0, 20f), Row(NavKind.Gate, 2, 900f) };
        Assert.Equal(NavKind.Port, NavPage.Current(rows, NavKind.Port, 0)!.Kind);
        Assert.Equal(NavKind.Gate, NavPage.Current(rows, NavKind.Gate, 2)!.Kind);
        Assert.Equal(4, NavPage.Current(rows, null, -1)!.Id);
        Assert.Equal(NavKind.Enemy, NavPage.Current(rows.Where(r => !r.Selected).ToArray(), null, -1)!.Kind);
        Assert.Equal(4, NavPage.Current(rows, NavKind.Enemy, 99)!.Id); // a pick that is gone falls back
        Assert.Null(NavPage.Current(Array.Empty<NavTarget>(), null, -1));
    }

    [Fact]
    public void EveryKindOfRowCanBePickedAndOnlyDepositsStepWithTheArrows()
    {
        foreach (var k in Enum.GetValues<NavKind>()) Assert.True(Row(k, 1, 5f).Pickable);
        Assert.True(Row(NavKind.Deposit, 1, 5f).Selectable);
        Assert.False(Row(NavKind.Port, 1, 5f).Selectable);
        Assert.False(Row(NavKind.Gate, 1, 5f).Selectable);
    }

    [Fact]
    public void TheBigDistanceHasADashWithoutATarget()
    {
        Assert.Equal("--", NavPage.BigDistance(null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("47", NavPage.BigDistance(Row(NavKind.Deposit, 1, 47.2f), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("m", NavPage.BigDistanceUnit(Row(NavKind.Deposit, 1, 47.2f), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("1.5", NavPage.BigDistance(Row(NavKind.Gate, 1, 1500f), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("km", NavPage.BigDistanceUnit(Row(NavKind.Gate, 1, 1500f), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("", NavPage.BigDistanceUnit(null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NewTextsExistInBothLanguages()
    {
        foreach (string key in new[] { "cp.nav.dist", "cp.nav.noTarget", "cp.nav.mapHint", "cp.unit.cells", "cp.nav.reset", "cp.nav.autoRange", "cp.turn", "cp.assist", "settings.inertia" })
            Assert.NotEqual(key, Loc.T(key));
    }
}
