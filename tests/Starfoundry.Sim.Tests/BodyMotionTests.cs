using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class BodyMotionTests
{
    private static BodyBox[] Ship(params (int, int, int)[] modules) => BodyMotion.Volumes(modules, 8f, 4f, 8f);

    private static BodyBox[] Row() => Ship((0, 0, 0), (0, 0, 1), (0, 0, 2));

    [Fact]
    public void VolumesAreTheModuleBoxes()
    {
        var v = Ship((0, 0, 0), (1, 1, 2));
        Assert.Equal(new BodyBox(0, 0, 0, 8, 4, 8), v[0]);
        Assert.Equal(new BodyBox(8, 4, 16, 16, 8, 24), v[1]);
    }

    [Fact]
    public void WalkingAtSpeedCoversTheDistance()
    {
        var v = Row();
        var p = BodyMotion.Step(new Vector3(4, 0, 8), new Vector3(0, 0, 1), BodyMotion.WalkSpeed, 0.5f, v);
        Assert.Equal(10f, p.Z, 3);
        Assert.Equal(4f, p.X, 3);
        Assert.Equal(0f, p.Y);
        var r = BodyMotion.Step(new Vector3(4, 0, 8), new Vector3(0, 0, 1), BodyMotion.RunSpeed, 1f, v);
        Assert.Equal(15f, r.Z, 3);
    }

    [Fact]
    public void NoWishOrNoTimeDoesNotMove()
    {
        var v = Row();
        var start = new Vector3(4, 0, 4);
        Assert.Equal(start, BodyMotion.Step(start, Vector3.Zero, 4f, 1f, v));
        Assert.Equal(start, BodyMotion.Step(start, new Vector3(1, 0, 0), 4f, 0f, v));
    }

    [Fact]
    public void DiagonalIsNotFasterThanStraight()
    {
        var v = Ship((0, 0, 0), (1, 0, 0), (0, 0, 1), (1, 0, 1));
        var p = BodyMotion.Step(new Vector3(4, 0, 4), new Vector3(1, 0, 1), 4f, 1f, v);
        Assert.Equal(4f, new Vector2(p.X - 4, p.Z - 4).Length(), 2);
        // An over-long wish vector is normalised too.
        var q = BodyMotion.Step(new Vector3(4, 0, 4), new Vector3(5, 0, 0), 2f, 1f, v);
        Assert.Equal(6f, q.X, 3);
    }

    [Fact]
    public void StopsAtTheOuterWallWithTheBodyRadius()
    {
        var v = Row();
        var p = BodyMotion.Step(new Vector3(4, 0, 4), new Vector3(-1, 0, 0), 4f, 5f, v);
        Assert.InRange(p.X, BodyMotion.Radius, BodyMotion.Radius + BodyMotion.MaxSubStep);
        Assert.True(BodyMotion.Contains(p, v));
    }

    [Fact]
    public void SlidesAlongAWall()
    {
        var v = Row();
        var start = new Vector3(BodyMotion.Radius, 0, 4);
        var p = BodyMotion.Step(start, new Vector3(-1, 0, 1), 4f, 1f, v);
        Assert.Equal(BodyMotion.Radius, p.X, 3); // held by the wall
        Assert.True(p.Z > 4f + 2f); // keeps going along it
        Assert.True(BodyMotion.Contains(p, v));
    }

    [Fact]
    public void PassesBetweenAdjacentModules()
    {
        var v = Row();
        var p = new Vector3(4, 0, 2);
        for (int i = 0; i < 20; i++) p = BodyMotion.Step(p, new Vector3(0, 0, 1), 4f, 0.25f, v);
        Assert.Equal(22f, p.Z, 2); // from the first module through the second and into the third
        for (int i = 0; i < 40; i++) p = BodyMotion.Step(p, new Vector3(0, 0, 1), 4f, 0.25f, v);
        Assert.InRange(p.Z, 24f - BodyMotion.Radius - BodyMotion.MaxSubStep, 24f - BodyMotion.Radius);
    }

    [Fact]
    public void NeverEntersEmptySpaceAroundAnLShape()
    {
        // Modules at (0,0,0), (1,0,0) and (0,0,1): the corner at x > 8, z > 8 is empty.
        var v = Ship((0, 0, 0), (1, 0, 0), (0, 0, 1));
        var p = new Vector3(7, 0, 7);
        var rng = new Random(7);
        for (int i = 0; i < 4000; i++)
        {
            var wish = new Vector3((float)(rng.NextDouble() * 2 - 1), 0, (float)(rng.NextDouble() * 2 - 1));
            p = BodyMotion.Step(p, wish, rng.Next(2) == 0 ? BodyMotion.WalkSpeed : BodyMotion.RunSpeed, 1f / 60f, v);
            Assert.True(BodyMotion.Contains(p, v), $"step {i}: {p}");
        }
        // Walking straight into the empty corner stops at the notch.
        var q = BodyMotion.Step(new Vector3(7, 0, 7), new Vector3(1, 0, 1), 4f, 5f, v);
        Assert.True(BodyMotion.Contains(q, v));
        Assert.False(q.X > 8f + 0.01f && q.Z > 8f + 0.01f);
    }

    [Fact]
    public void ALongFrameCannotJumpAcrossAGap()
    {
        var v = Ship((0, 0, 0), (0, 0, 2)); // nothing at z 8 to 16
        var p = BodyMotion.Step(new Vector3(4, 0, 7), new Vector3(0, 0, 1), 7f, 10f, v);
        Assert.InRange(p.Z, 8f - BodyMotion.Radius - BodyMotion.MaxSubStep, 8f - BodyMotion.Radius);
    }

    [Fact]
    public void StackedModulesAreAnotherLevel()
    {
        var v = Ship((0, 0, 0), (0, 1, 1)); // the next module is a storey up: not walkable from the floor
        var p = BodyMotion.Step(new Vector3(4, 0, 4), new Vector3(0, 0, 1), 4f, 5f, v);
        Assert.InRange(p.Z, 8f - BodyMotion.Radius - BodyMotion.MaxSubStep, 8f - BodyMotion.Radius);
        Assert.Equal(0f, p.Y);
    }

    [Fact]
    public void IsDeterministic()
    {
        var v = Row();
        Vector3 Run()
        {
            var p = new Vector3(4, 0, 4);
            var rng = new Random(11);
            for (int i = 0; i < 500; i++)
                p = BodyMotion.Step(p, new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f), 4f, 0.016f, v);
            return p;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ContainsRejectsEmptySpaceAndNearestPutsTheBodyBack()
    {
        var v = Row();
        Assert.False(BodyMotion.Contains(new Vector3(30, 0, 4), v));
        Assert.False(BodyMotion.Contains(new Vector3(4, 0, 30), v));
        var back = BodyMotion.Nearest(new Vector3(30, 0, 12), v);
        Assert.True(BodyMotion.Contains(back, v));
        Assert.Equal(8f - BodyMotion.Radius, back.X, 3);
        Assert.Equal(new Vector3(4, 0, 4), BodyMotion.Nearest(new Vector3(4, 0, 4), v));
        Assert.Equal(new Vector3(1, 2, 3), BodyMotion.Nearest(new Vector3(1, 2, 3), Array.Empty<BodyBox>()));
    }

    [Fact]
    public void EmptyShipHasNoRoom()
    {
        var start = new Vector3(4, 0, 4);
        Assert.Equal(start, BodyMotion.Step(start, new Vector3(1, 0, 0), 4f, 1f, Array.Empty<BodyBox>()));
    }

    [Fact]
    public void SeatReachIsHorizontal()
    {
        var seat = new Vector3(4, 0, 4.6f);
        Assert.True(BodyMotion.InSeatReach(new Vector3(4, 0, 6.6f), seat));
        Assert.True(BodyMotion.InSeatReach(seat, seat));
        Assert.False(BodyMotion.InSeatReach(new Vector3(4, 0, 12f), seat));
        Assert.False(BodyMotion.InSeatReach(new Vector3(4, 4, 4.6f), seat)); // another level
    }
}
