using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class FreeFlightTests
{
    private const float Quarter = MathF.PI / 2f;

    private static void Near(Vec3 expected, Vec3 actual, int digits = 4)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    [Fact]
    public void IdentityLooksAlongMinusZ()
    {
        var q = Quaternion.Identity;
        Near(new Vec3(0, 0, -1), FlightOrientation.Forward(q));
        Near(new Vec3(1, 0, 0), FlightOrientation.Right(q));
        Near(new Vec3(0, 1, 0), FlightOrientation.Up(q));
    }

    [Fact]
    public void PitchUpRaisesTheNoseAndNeverStops()
    {
        var q = FlightOrientation.Rotate(Quaternion.Identity, Quarter, 0f, 0f);
        Near(new Vec3(0, 1, 0), FlightOrientation.Forward(q)); // straight up: no 85 degree clamp
        Near(new Vec3(0, 0, 1), FlightOrientation.Up(q));      // the roof now faces backwards
        q = FlightOrientation.Rotate(q, Quarter, 0f, 0f);      // past the vertical, over the top
        Near(new Vec3(0, 0, 1), FlightOrientation.Forward(q));
        Near(new Vec3(0, -1, 0), FlightOrientation.Up(q));     // upside down
    }

    [Fact]
    public void YawRightTurnsTheNoseToTheRightOfTheShip()
    {
        var q = FlightOrientation.Rotate(Quaternion.Identity, 0f, Quarter, 0f);
        Near(new Vec3(1, 0, 0), FlightOrientation.Forward(q));
        Near(new Vec3(0, 1, 0), FlightOrientation.Up(q));
    }

    [Fact]
    public void RollRightDropsTheRightWing()
    {
        var q = FlightOrientation.Rotate(Quaternion.Identity, 0f, 0f, Quarter);
        Near(new Vec3(0, -1, 0), FlightOrientation.Right(q));
        Near(new Vec3(1, 0, 0), FlightOrientation.Up(q));
        Near(new Vec3(0, 0, -1), FlightOrientation.Forward(q)); // the nose does not move
    }

    [Fact]
    public void TurnsAreAboutTheShipsOwnAxesNotTheWorldsAxes()
    {
        // Roll a quarter turn right, then pitch up: in the world the nose goes toward the ship's own roof, which now points to +X.
        var q = FlightOrientation.Rotate(Quaternion.Identity, 0f, 0f, Quarter);
        q = FlightOrientation.Rotate(q, Quarter, 0f, 0f);
        Near(new Vec3(1, 0, 0), FlightOrientation.Forward(q));
        // The same two turns in the other order give a different attitude (a world-frame controller would not).
        var other = FlightOrientation.Rotate(Quaternion.Identity, Quarter, 0f, 0f);
        other = FlightOrientation.Rotate(other, 0f, 0f, Quarter);
        Near(new Vec3(0, 1, 0), FlightOrientation.Forward(other));
    }

    [Fact]
    public void AFullRollComesBackToTheStart()
    {
        var q = Quaternion.Identity;
        for (int i = 0; i < 360; i++) q = FlightOrientation.Rotate(q, 0f, 0f, MathF.PI / 180f);
        Assert.True(FlightOrientation.AngleFromIdentity(q) < 0.01f);
    }

    [Fact]
    public void ManySmallTurnsStayOrthonormal()
    {
        var q = Quaternion.Identity;
        var rng = new Random(7);
        for (int i = 0; i < 50_000; i++)
            q = FlightOrientation.Rotate(q, (float)(rng.NextDouble() - 0.5) * 0.05f, (float)(rng.NextDouble() - 0.5) * 0.05f, (float)(rng.NextDouble() - 0.5) * 0.05f);
        Assert.True(FlightOrientation.IsOrthonormal(q));
        Assert.Equal(1f, MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W), 4);
    }

    [Fact]
    public void ABrokenQuaternionFallsBackToTheIdentity()
    {
        Assert.Equal(Quaternion.Identity, FlightOrientation.Normalize(new Quaternion(0, 0, 0, 0)));
        Assert.Equal(Quaternion.Identity, FlightOrientation.Normalize(new Quaternion(float.NaN, 0, 0, 1)));
    }

    [Fact]
    public void AlignTakesTheShortWayBackToTheIdentity()
    {
        var upsideDown = FlightOrientation.Rotate(Quaternion.Identity, 0f, 0f, MathF.PI * 0.9f);
        var half = FlightOrientation.Align(upsideDown, 0.5f);
        Assert.True(FlightOrientation.AngleFromIdentity(half) < FlightOrientation.AngleFromIdentity(upsideDown));
        Assert.Equal(0f, FlightOrientation.AngleFromIdentity(FlightOrientation.Align(upsideDown, 1f)), 3);
        Assert.Equal(FlightOrientation.AngleFromIdentity(upsideDown), FlightOrientation.AngleFromIdentity(FlightOrientation.Align(upsideDown, 0f)), 3);
    }

    // ---- navigation: markers and the gizmo follow the attitude, whatever it is -----------------------------------------

    private const float Fov = 80f * MathF.PI / 180f;

    [Fact]
    public void ATargetToTheWorldsRightIsOnTheLeftWhenFlyingUpsideDown()
    {
        var target = new Vec3(40, 0, -40); // ahead and to +X of a ship at the origin
        var upright = Markers.Place(target, FlightOrientation.Camera(Quaternion.Identity, Fov), 1280, 720);
        Assert.True(upright.OnScreen);
        Assert.True(upright.X > 640f);

        var flipped = FlightOrientation.Rotate(Quaternion.Identity, 0f, 0f, MathF.PI); // rolled upside down
        var seen = Markers.Place(target, FlightOrientation.Camera(flipped, Fov), 1280, 720);
        Assert.True(seen.OnScreen);
        Assert.True(seen.X < 640f); // mirrored: the pilot's right is the world's -X now
    }

    [Fact]
    public void OffScreenArrowPointsTheShortWayWhateverTheRoll()
    {
        // The target is 90 degrees to the pilot's right: the arrow must point right on the screen, rolled or not.
        var q = FlightOrientation.Rotate(Quaternion.Identity, 0.4f, -0.3f, 2.2f);
        var right = FlightOrientation.Right(q);
        var cam = FlightOrientation.Camera(q, Fov);
        var p = Markers.Place(new Vec3(right.X * 50f, right.Y * 50f, right.Z * 50f), cam, 1280, 720);
        Assert.False(p.OnScreen);
        Assert.Equal(0f, p.AngleRadians, 2); // 0 = pointing right
        Assert.True(p.X > 1000f);
    }

    [Fact]
    public void ATargetBehindStillGetsAnArrow()
    {
        var back = new Vec3(0, 0, 30);
        var p = Markers.Place(back, FlightOrientation.Camera(Quaternion.Identity, Fov), 1280, 720);
        Assert.True(p.Visible);
        Assert.False(p.OnScreen);
    }

    [Fact]
    public void TurnDegreesDoesNotDependOnRoll()
    {
        var dir = new Vec3(10, 0, -10);
        float upright = NavFrame.TurnDegrees(dir, FlightOrientation.Camera(Quaternion.Identity, Fov));
        float rolled = NavFrame.TurnDegrees(dir, FlightOrientation.Camera(FlightOrientation.Rotate(Quaternion.Identity, 0f, 0f, 2.5f), Fov));
        Assert.Equal(45f, upright, 2);
        Assert.Equal(upright, rolled, 2);
        Assert.Equal(180f, NavFrame.TurnDegrees(new Vec3(0, 0, 5), FlightOrientation.Camera(Quaternion.Identity, Fov)), 2);
        Assert.Equal(0f, NavFrame.TurnDegrees(new Vec3(0, 0, 0), FlightOrientation.Camera(Quaternion.Identity, Fov)));
    }

    [Fact]
    public void GizmoShowsTheFixedGalacticAxesAgainstTheAttitude()
    {
        var axes = NavFrame.Project(FlightOrientation.Camera(Quaternion.Identity, Fov));
        Assert.Equal(new[] { "X", "Y", "Z" }, axes.Select(a => a.Label).ToArray());
        Assert.Equal(1f, axes[0].X, 4);   // galactic X is to the pilot's right
        Assert.Equal(1f, axes[1].Y, 4);   // galactic Y is overhead (only because the ship starts that way)
        Assert.True(axes[2].Depth < 0f);  // galactic Z points at the pilot

        var rolled = NavFrame.Project(FlightOrientation.Camera(FlightOrientation.Rotate(Quaternion.Identity, 0f, 0f, MathF.PI), Fov));
        Assert.Equal(-1f, rolled[0].X, 4); // upside down: the same axes, mirrored on the screen
        Assert.Equal(-1f, rolled[1].Y, 4);
        Assert.True(axes[2].Receding == false && rolled[2].Receding == false);
    }

    [Fact]
    public void DirectionToIsAUnitVector()
    {
        var d = NavFrame.DirectionTo(new Vec3(1, 1, 1), new Vec3(1, 1, 11));
        Assert.Equal(1f, d.Length, 4);
        Assert.Equal(1f, d.Z, 4);
        Assert.Equal(0f, NavFrame.DirectionTo(new Vec3(2, 2, 2), new Vec3(2, 2, 2)).Length);
    }
}
