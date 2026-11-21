using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ThrustTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261120UL;

    private static void Ok(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
    }

    private static CommandResult Run(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static PlayerInfo Info(SimWorld w) => w.Network.GetPlayerInfo(0);

    [Fact]
    public void Straight_thrust_matches_axis_movement()
    {
        var axis = new SimWorld();
        var thrust = new SimWorld();
        Ok(axis, new SetMovement(1, 0, 0));
        Ok(thrust, new SetThrust(1024, 0, 0));
        axis.Step(99);
        thrust.Step(99);
        Assert.Equal(Info(axis).X, Info(thrust).X);
        Assert.Equal(100 * PlayerRules.MoveUnitsPerTick, Info(thrust).X);
        Assert.Equal((0, 0), (Info(thrust).Y, Info(thrust).Z));

        // Negative directions are symmetric with positive ones.
        var neg = new SimWorld();
        Ok(neg, new SetThrust(0, 0, -1024));
        neg.Step(99);
        Assert.Equal(-100 * PlayerRules.MoveUnitsPerTick, Info(neg).Z);
    }

    [Theory]
    [InlineData(724, 0, 724)]
    [InlineData(591, 591, 591)]
    [InlineData(-300, 700, 650)]
    [InlineData(990, 100, -200)]
    [InlineData(-724, -724, 0)]
    public void Diagonal_speed_is_constant(int dx, int dy, int dz)
    {
        // The speed is |thrust| * 48 / 1024 units a tick whatever the direction, so a diagonal is not slower
        // like SetMovement's fixed diagonal speeds. Integer vectors cannot have a length of exactly 1024
        // (1024 is a power of two), so the expectation uses the real vector length.
        var w = new SimWorld();
        Ok(w, new SetThrust(dx, dy, dz));
        w.Step(999); // 1000 ticks of movement in total
        var p = Info(w);
        double travelled = Math.Sqrt((double)p.X * p.X + (double)p.Y * p.Y + (double)p.Z * p.Z);
        double length = Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);
        double expected = 1000.0 * PlayerRules.MoveUnitsPerTick * length / 1024.0;
        Assert.InRange(Math.Abs(travelled - expected), 0, 1.5);

        // A near-unit vector travels about the straight speed: 48 units a tick.
        if (length > 1000) Assert.InRange(travelled, 1000.0 * PlayerRules.MoveUnitsPerTick * 0.97, 1000.0 * PlayerRules.MoveUnitsPerTick * 1.001);
    }

    [Fact]
    public void Remainders_do_not_drift()
    {
        var w = new SimWorld();
        Ok(w, new SetThrust(1, 0, 0));
        w.Step(1023); // 1024 ticks of movement
        Assert.Equal(PlayerRules.MoveUnitsPerTick, Info(w).X);

        var neg = new SimWorld();
        Ok(neg, new SetThrust(-1, 0, 0));
        neg.Step(1023);
        Assert.Equal(-PlayerRules.MoveUnitsPerTick, Info(neg).X);

        // A long run in a fixed direction stays within one unit of the exact line.
        var d = new SimWorld();
        Ok(d, new SetThrust(333, -777, 123));
        d.Step(9999); // 10000 ticks
        var p = Info(d);
        Assert.InRange(Math.Abs(p.X - 10000L * 333 * 48 / 1024.0), 0, 1);
        Assert.InRange(Math.Abs(p.Y + 10000L * 777 * 48 / 1024.0), 0, 1);
        Assert.InRange(Math.Abs(p.Z - 10000L * 123 * 48 / 1024.0), 0, 1);
        d.ValidateInvariants();
    }

    [Fact]
    public void Out_of_range_thrust_is_rejected()
    {
        var w = new SimWorld();
        w.Network.EnsurePlayer(0);
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetThrust(1025, 0, 0)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetThrust(0, -1025, 0)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetThrust(800, 800, 0)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetThrust(600, 600, 600)).Reject); // 1,080,000 > 1,048,576
        Assert.Equal(Reject.InvalidArgument, Run(w, new SetThrust(int.MaxValue, int.MaxValue, int.MinValue)).Reject);
        Assert.Equal((0, 0, 0), (Info(w).X, Info(w).Y, Info(w).Z));

        // The limits themselves are accepted.
        Ok(w, new SetThrust(1024, 0, 0));
        Ok(w, new SetThrust(-1024, 0, 0));
        Ok(w, new SetThrust(0, 0, 1024));
        Ok(w, new SetThrust(591, 591, 591));

        // The network method throws instead.
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Network.SetThrust(0, 1025, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Network.SetThrust(0, 800, 800, 0));
    }

    [Fact]
    public void Zero_thrust_stops()
    {
        var w = new SimWorld();
        Ok(w, new SetThrust(700, 0, 300));
        w.Step(20);
        Ok(w, new SetThrust(0, 0, 0));
        var stopped = Info(w);
        w.Step(50);
        var later = Info(w);
        Assert.Equal((stopped.X, stopped.Y, stopped.Z), (later.X, later.Y, later.Z));
        Assert.NotEqual(0, stopped.X);

        // Going again from rest does not use a stale sub-unit remainder: same distance as a fresh ship.
        var fresh = new SimWorld();
        Ok(fresh, new SetThrust(5, 0, 0));
        fresh.Step(99);
        Ok(w, new SetThrust(5, 0, 0));
        w.Step(99);
        Assert.Equal(Info(fresh).X, Info(w).X - later.X);
        w.ValidateInvariants();
    }

    [Fact]
    public void SetMovement_clears_thrust_and_back()
    {
        var w = new SimWorld();
        Ok(w, new SetThrust(0, 1024, 0));
        Assert.Equal((0, 1024, 0), (Info(w).ThrustX, Info(w).ThrustY, Info(w).ThrustZ));
        Ok(w, new SetMovement(1, 0, 0));
        var p = Info(w);
        Assert.Equal((0, 0, 0), (p.ThrustX, p.ThrustY, p.ThrustZ));
        Assert.Equal((1, 0, 0), (p.MoveX, p.MoveY, p.MoveZ));
        int y = p.Y;
        w.Step(10);
        Assert.Equal(y, Info(w).Y); // only the axis intent moves now
        Assert.True(Info(w).X > p.X);

        Ok(w, new SetThrust(0, 0, 512));
        p = Info(w);
        Assert.Equal((0, 0, 0), (p.MoveX, p.MoveY, p.MoveZ));
        Assert.Equal((0, 0, 512), (p.ThrustX, p.ThrustY, p.ThrustZ));
        int x = p.X;
        w.Step(10);
        Assert.Equal(x, Info(w).X);
        w.ValidateInvariants();

        // SetMovement itself behaves as before: a diagonal uses the fixed diagonal speed.
        var d = new SimWorld();
        Ok(d, new SetMovement(1, 0, 1));
        d.Step(9);
        Assert.Equal(10 * PlayerRules.MoveUnitsPerTickDiagonal2, Info(d).X);
    }

    private static SimWorld Flown()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: true);
        w.Network.EnsurePlayer(0);
        Ok(w, new SetThrust(333, -200, 700));
        w.Step(100);
        Ok(w, new SetThrust(-512, 640, 100));
        w.Step(77);
        return w;
    }

    [Fact]
    public void Thrust_survives_replay_and_save_roundtrip()
    {
        var plain = new SimWorld(C);
        Ok(plain, new SetThrust(333, -200, 700));
        plain.Step(100);
        Ok(plain, new SetThrust(-512, 640, 100));
        plain.Step(77);
        Assert.NotEqual(0, Info(plain).ThrustX);

        // Replay straight from the log, and after a trip through the binary log format.
        Assert.Equal(plain.Checksum(), SimWorld.Replay(plain.Log, plain.Tick, content: C).Checksum());
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, plain.Log);
        ms.Position = 0;
        var restored = CommandCodec.ReadLog(ms);
        var replay = SimWorld.Replay(restored, plain.Tick, content: C);
        Assert.Equal(plain.Checksum(), replay.Checksum());
        Assert.Equal(Info(plain), Info(replay));
        replay.ValidateInvariants();

        var w = Flown();
        Assert.NotEqual(0, Info(w).ThrustX);

        // A saved game keeps position, thrust and remainders, and keeps flying identically.
        var save = new MemoryStream();
        SaveGame.Write(save, w, C, GameMode.Campaign, Seed, threats: true, player: 0);
        var loaded = SaveGame.Read(new MemoryStream(save.ToArray()), C, out _);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal(Info(w), Info(loaded));
        w.Step(55);
        loaded.Step(55);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal(Info(w), Info(loaded));
        loaded.ValidateInvariants();
    }

    [Fact]
    public void Checksum_changes_with_thrust()
    {
        var a = new SimWorld();
        var b = new SimWorld();
        a.Network.EnsurePlayer(0);
        b.Network.EnsurePlayer(0);
        Assert.Equal(a.Checksum(), b.Checksum());
        a.Network.SetThrust(0, 100, 0, 0);
        Assert.NotEqual(a.Checksum(), b.Checksum());
        b.Network.SetThrust(0, 101, 0, 0);
        Assert.NotEqual(a.Checksum(), b.Checksum());

        // The sub-unit remainder is part of the state too: same position, same thrust, different remainder.
        var c = new SimWorld();
        var d = new SimWorld();
        Ok(c, new SetThrust(1, 0, 0));
        Ok(d, new SetThrust(1, 0, 0));
        c.Step(10);
        d.Step(11);
        Assert.Equal(Info(c).X, Info(d).X);
        Assert.NotEqual(c.Checksum(), d.Checksum());
    }

    [Fact]
    public void Jump_clears_thrust_at_start_and_keeps_later_thrust()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed);
        int target = w.Network.Galaxy!.Systems[0].Lanes[0];
        Ok(w, new SetThrust(1024, 0, 0));
        w.Step(5);
        Assert.True(Info(w).X > 0);

        Ok(w, new Jump(target));
        Assert.Equal((0, 0, 0), (Info(w).ThrustX, Info(w).ThrustY, Info(w).ThrustZ));

        // Thrust given while the jump charges does not move the frozen ship.
        var before = Info(w);
        Ok(w, new SetThrust(0, 1024, 0));
        w.Step(30);
        var during = Info(w);
        Assert.Equal((before.X, before.Y, before.Z), (during.X, during.Y, during.Z));

        while (w.Network.JumpRemaining(0) > 0) w.Step();
        var after = Info(w);
        // Like an axis intent, thrust given during the charge applies once the ship arrives.
        Assert.Equal((0, 1024, 0), (after.ThrustX, after.ThrustY, after.ThrustZ));
        var sys = w.Network.Galaxy!.Systems[target];
        Assert.Equal((sys.X * Units.CellUnits, sys.Y * Units.CellUnits, sys.Z * Units.CellUnits), (after.X, after.Y, after.Z));
        w.Step(10);
        Assert.True(Info(w).Y > after.Y);
        w.ValidateInvariants();
    }
}
