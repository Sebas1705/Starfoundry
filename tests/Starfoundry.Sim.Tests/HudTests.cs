using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class HudTests
{
    // Looking along -Z with +X to the right and +Y up, 90 degrees of vertical field of view.
    private static readonly CameraBasis Cam = new(new Vec3(0, 0, -1), new Vec3(1, 0, 0), new Vec3(0, 1, 0), MathF.PI / 2f);
    private const float W = 1280f, H = 720f;

    [Fact]
    public void TargetAheadSitsOnTheScreenCentre()
    {
        var p = Markers.Place(new Vec3(0, 0, -30), Cam, W, H);
        Assert.True(p.Visible);
        Assert.True(p.OnScreen);
        Assert.Equal(W / 2f, p.X, 1);
        Assert.Equal(H / 2f, p.Y, 1);
        Assert.Equal(30f, p.Distance, 3);
    }

    [Fact]
    public void TargetInViewProjectsWithTheFieldOfView()
    {
        // 45 degrees to the right at 90 degrees of vertical fov: focal = 360 px, so x = 640 + 360.
        var p = Markers.Place(new Vec3(10, 0, -10), Cam, W, H);
        Assert.True(p.OnScreen);
        Assert.Equal(640f + 360f, p.X, 1);
        Assert.Equal(360f, p.Y, 1);
    }

    [Fact]
    public void TargetToTheRightPinsToTheRightEdgePointingRight()
    {
        var p = Markers.Place(new Vec3(30, 0, 0), Cam, W, H, 24, 24, 24, 24);
        Assert.True(p.Visible);
        Assert.False(p.OnScreen);
        Assert.Equal(W - 24f, p.X, 1);
        Assert.Equal(H / 2f, p.Y, 1);
        Assert.Equal(0f, p.AngleRadians, 3);
    }

    [Fact]
    public void TargetAbovePinsToTheTopPointingUp()
    {
        var p = Markers.Place(new Vec3(0, 30, 0), Cam, W, H, 24, 60, 24, 100);
        Assert.False(p.OnScreen);
        Assert.Equal(60f, p.Y, 1);
        Assert.Equal(W / 2f, p.X, 1);
        Assert.Equal(MathF.PI / 2f, p.AngleRadians, 3);
    }

    [Fact]
    public void TargetBehindStaysOnTheEdgeOnTheSideItIsOn()
    {
        var p = Markers.Place(new Vec3(-20, 0, 20), Cam, W, H);
        Assert.False(p.OnScreen);
        Assert.Equal(24f, p.X, 1); // to the left and behind: the left edge
        Assert.Equal(MathF.PI, MathF.Abs(p.AngleRadians), 3);

        var dead = Markers.Place(new Vec3(0, 0, 20), Cam, W, H);
        Assert.False(dead.OnScreen);
        Assert.Equal(H - 24f, dead.Y, 1); // dead behind: pointing down, an arbitrary but stable choice
    }

    [Fact]
    public void TooCloseIsHidden()
    {
        Assert.False(Markers.Place(new Vec3(0, 0, -3), Cam, W, H, hideBelow: 4f).Visible);
        Assert.True(Markers.Place(new Vec3(0, 0, -5), Cam, W, H, hideBelow: 4f).Visible);
    }

    [Fact]
    public void MarkersAlwaysStayInsideTheMargins()
    {
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var rel = new Vec3(rng.NextSingle() * 200 - 100, rng.NextSingle() * 200 - 100, rng.NextSingle() * 200 - 100);
            var p = Markers.Place(rel, Cam, W, H, 20, 60, 30, 110);
            if (!p.Visible) continue;
            Assert.InRange(p.X, 20f - 0.01f, W - 30f + 0.01f);
            Assert.InRange(p.Y, 60f - 0.01f, H - 110f + 0.01f);
        }
    }

    [Fact]
    public void NearestClusterIsTheCentreOfTheGroupAroundTheNearestPoint()
    {
        var points = new[] { new Vec3(0, 0, 0), new Vec3(2, 0, 0), new Vec3(100, 0, 0) };
        var c = Markers.NearestCluster(points, new Vec3(1, 0, 5), 10f);
        Assert.NotNull(c);
        Assert.Equal(1f, c!.Value.X, 3);
        Assert.Null(Markers.NearestCluster(Array.Empty<Vec3>(), default, 10f));
        // From the far side the far point is its own cluster.
        Assert.Equal(100f, Markers.NearestCluster(points, new Vec3(90, 0, 0), 10f)!.Value.X, 3);
    }

    [Fact]
    public void ToastsAreCappedAtThreeNewestFirstKept()
    {
        var q = new ToastQueue();
        for (int i = 0; i < 6; i++) q.Add("t" + i, ToastCategory.Info, 0, 10);
        var v = q.Visible(1);
        Assert.Equal(3, v.Count);
        Assert.Equal(new[] { "t3", "t4", "t5" }, v.Select(t => t.Text));
    }

    [Fact]
    public void RepeatsAreGroupedWithACount()
    {
        var q = new ToastQueue();
        q.Add("hold full", ToastCategory.Logistics, 0, 6);
        q.Add("hold full", ToastCategory.Logistics, 1, 6);
        q.Add("hold full", ToastCategory.Logistics, 2, 6);
        var v = q.Visible(3);
        Assert.Single(v);
        Assert.Equal(3, v[0].Count);
        Assert.EndsWith("x3", v[0].Display);
        Assert.Equal("hold full", new ToastView("hold full", 1, ToastCategory.Info).Display);

        // The same text in another category is a different notification.
        q.Add("hold full", ToastCategory.Threat, 3, 6);
        Assert.Equal(2, q.Visible(3).Count);
    }

    [Fact]
    public void ToastsExpireAndRepeatsExtendTheLife()
    {
        var q = new ToastQueue();
        q.Add("a", ToastCategory.Production, 0, 5);
        q.Add("a", ToastCategory.Production, 4, 5); // alive until 9 now
        Assert.Single(q.Visible(8));
        Assert.Empty(q.Visible(9.5));
        q.Add("a", ToastCategory.Production, 20, 5); // after expiry it starts again at x1
        Assert.Equal(1, q.Visible(21)[0].Count);
    }

    [Fact]
    public void HealthBandsAndToolStates()
    {
        Assert.Equal(HealthBand.Good, HudFormat.Band(100, 100));
        Assert.Equal(HealthBand.Worn, HudFormat.Band(50, 100));
        Assert.Equal(HealthBand.Critical, HudFormat.Band(30, 100));
        Assert.Equal(HealthBand.Critical, HudFormat.Band(5, 0));
        Assert.Equal(ToolState.Locked, HudFormat.Tool(true, true, true));
        Assert.Equal(ToolState.Free, HudFormat.Tool(false, false, false));
        Assert.Equal(ToolState.Affordable, HudFormat.Tool(false, true, true));
        Assert.Equal(ToolState.Short, HudFormat.Tool(false, true, false));
    }

    [Fact]
    public void Mining_limit_is_seventy_five_percent()
    {
        Assert.False(HudFormat.MiningLimitReached(44, 60));
        Assert.True(HudFormat.MiningLimitReached(45, 60));
        Assert.True(HudFormat.MiningLimitReached(60, 60));
        Assert.False(HudFormat.MiningLimitReached(0, 0));
    }
}
