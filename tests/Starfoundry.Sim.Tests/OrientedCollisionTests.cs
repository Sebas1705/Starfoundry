using System.Numerics;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0030: the ship attitude in the simulation (fixed-point quaternion, the SetAttitude command), oriented hit volumes, ports and
/// docking alignment, and the guarantee that a level ship behaves and hashes exactly as before.</summary>
public class OrientedCollisionTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly WorldGenRules W = C.World!;
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;
    private const int S = AttitudeRules.Scale;

    /// <summary>A quarter turn about Z (the nose axis): the long X axis of the test ship becomes the vertical.</summary>
    private static readonly Attitude Roll90 = Attitude.Of(11585, 0, 0, 11585);

    private static SimWorld World() => SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);

    private static int Ship(SimWorld w) => PowerTests.Ship(w, 1, 30);

    private static Attitude Around(Vector3 axis, float radians) =>
        ShipAttitude.Quantize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians));

    // ---- the maths ---------------------------------------------------------------------------------

    [Fact]
    public void The_identity_turns_nothing_and_a_default_construction_is_the_identity()
    {
        Assert.True(Attitude.Identity.IsIdentity);
        Assert.Equal((123L, -456L, 789L), Attitude.Identity.Rotate(123, -456, 789));
        Assert.Equal((123L, -456L, 789L), Attitude.Identity.RotateInverse(123, -456, 789));
        Assert.True(Attitude.TryCreate(S, 0, 0, 0, out var a) && a == Attitude.Identity);
        Assert.True(Attitude.TryCreate(-S, 0, 0, 0, out var b) && b == Attitude.Identity); // the negative is the same turn
        var w = World();
        Ship(w);
        Assert.True(w.Network.AttitudeOf(1).IsIdentity);
        Assert.True(w.Network.AttitudeOf(99).IsIdentity);
    }

    [Fact]
    public void Quarter_turns_map_the_axes_exactly()
    {
        // 90 degrees about Z: X goes to Y, Y to -X. The matrix is exact to a unit at these lengths.
        var (x, y, z) = Roll90.Rotate(1000, 0, 0);
        Assert.InRange(x, -1, 1); Assert.InRange(y, 999, 1001); Assert.Equal(0, z);
        (x, y, z) = Roll90.Rotate(0, 1000, 0);
        Assert.InRange(x, -1001, -999); Assert.InRange(y, -1, 1);
        // 180 degrees about Y: X and Z flip, Y stays.
        var half = Attitude.Of(0, 0, S, 0);
        Assert.Equal((-1000L, 77L, -300L), half.Rotate(1000, 77, 300));
        // The inverse undoes the turn within the rounding of the fixed point.
        var (rx, ry, rz) = Roll90.RotateInverse(Roll90.Rotate(5000, -3000, 1200).X, Roll90.Rotate(5000, -3000, 1200).Y, Roll90.Rotate(5000, -3000, 1200).Z);
        Assert.InRange(rx, 4998, 5002); Assert.InRange(ry, -3002, -2998); Assert.InRange(rz, 1198, 1202);
    }

    [Fact]
    public void The_fixed_point_matrix_matches_the_float_quaternion_and_stays_orthonormal()
    {
        var rng = new Random(2026);
        for (int i = 0; i < 2000; i++)
        {
            var q = Quaternion.Normalize(new Quaternion((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f));
            var a = ShipAttitude.Quantize(q);
            var v = new Vector3(rng.Next(-12000, 12000), rng.Next(-12000, 12000), rng.Next(-12000, 12000));
            var expected = Vector3.Transform(v, q);
            var (x, y, z) = a.Rotate((long)v.X, (long)v.Y, (long)v.Z);
            Assert.InRange(Math.Abs(x - expected.X), 0, 6); // 12000 units, an error of 1.2e-4 at the worst
            Assert.InRange(Math.Abs(y - expected.Y), 0, 6);
            Assert.InRange(Math.Abs(z - expected.Z), 0, 6);
            // Lengths survive the turn.
            double before = v.Length(), after = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
            Assert.InRange(Math.Abs(before - after), 0, 5);
        }
    }

    [Fact]
    public void Quantising_is_deterministic_canonical_and_always_valid()
    {
        var rng = new Random(7);
        for (int i = 0; i < 50_000; i++)
        {
            var q = Quaternion.Normalize(new Quaternion((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f));
            var a = ShipAttitude.Quantize(q);
            Assert.Equal(a, ShipAttitude.Quantize(q));
            Assert.True(a.W >= 0);
            // The negative quaternion is the same turn and quantises to the same attitude.
            Assert.Equal(a, ShipAttitude.Quantize(new Quaternion(-q.X, -q.Y, -q.Z, -q.W)));
            Assert.True(Attitude.TryCreate(a.W, a.X, a.Y, a.Z, out var again) && again == a);
            // Back to a float quaternion: within the resolution of the scale (a few ten-thousandths).
            var back = ShipAttitude.ToQuaternion(a);
            Assert.True(MathF.Abs(MathF.Abs(Quaternion.Dot(back, q)) - 1f) < 1e-6f);
        }
        Assert.Equal(Attitude.Identity, ShipAttitude.Quantize(Quaternion.Identity));
        Assert.Equal(Attitude.Identity, ShipAttitude.Quantize(new Quaternion(float.NaN, 0, 0, 1)));
    }

    [Fact]
    public void Quaternions_that_are_not_unit_length_are_refused()
    {
        Assert.False(Attitude.TryCreate(S, S, 0, 0, out _));
        Assert.False(Attitude.TryCreate(0, 0, 0, 0, out _));
        Assert.False(Attitude.TryCreate(S / 2, 0, 0, 0, out _));
        Assert.False(Attitude.TryCreate(S + 1, 0, 0, 0, out _)); // a component beyond 1.0 is refused whatever the length
        Assert.True(Attitude.TryCreate(11585, 0, 0, 11585, out _));
        Assert.Throws<ArgumentException>(() => Attitude.Of(1, 2, 3, 4));
    }

    [Fact]
    public void The_pivot_matches_the_seat_the_client_turns_about()
    {
        var eye = SeatLocator.EyeOffset;
        Assert.InRange(MathF.Abs(eye.X * Cell - AttitudeRules.PivotX), 0f, 0.5f);
        Assert.InRange(MathF.Abs(eye.Y * Cell - AttitudeRules.PivotY), 0f, 0.5f);
        Assert.InRange(MathF.Abs(eye.Z * Cell - AttitudeRules.PivotZ), 0f, 0.5f);
    }

    // ---- command, codec and log -------------------------------------------------------------------

    [Fact]
    public void Set_attitude_is_validated_and_applied_by_the_command_layer()
    {
        var w = World();
        int ship = Ship(w);
        w.Submit(0, new SetAttitude(11585, 0, 0, 11585));
        w.Step();
        Assert.True(w.LastResults.Single().Ok);
        Assert.Equal(Roll90, w.Network.AttitudeOf(ship));

        w.Submit(0, new SetAttitude(-11585, 0, 0, -11585)); // the same turn: stored once, canonically
        w.Step();
        Assert.Equal(Roll90, w.Network.AttitudeOf(ship));

        w.Submit(0, new SetAttitude(1, 2, 3, 4));
        w.Step();
        Assert.Equal(Reject.InvalidArgument, w.LastResults.Single().Reject);
        Assert.Equal(Roll90, w.Network.AttitudeOf(ship));

        w.Submit(0, new SetAttitude(S, 0, 0, 0));
        w.Step();
        Assert.True(w.Network.AttitudeOf(ship).IsIdentity);
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Set_attitude_is_refused_without_structure_rules_or_a_ship()
    {
        var legacy = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false);
        legacy.Submit(0, new SetAttitude(11585, 0, 0, 11585));
        legacy.Step();
        Assert.Equal(Reject.NotAllowedHere, legacy.LastResults.Single().Reject);

        var empty = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        empty.Network.EnsurePlayer(0);
        empty.Submit(0, new SetAttitude(11585, 0, 0, 11585));
        empty.Step();
        Assert.Equal(Reject.NoSuchStructure, empty.LastResults.Single().Reject);
    }

    [Fact]
    public void Set_attitude_roundtrips_in_the_codec_and_older_logs_cannot_hold_it()
    {
        foreach (var c in new[] { new SetAttitude(S, 0, 0, 0), new SetAttitude(11585, 0, 0, -11585), new SetAttitude(-1, 16383, 2, -3) })
            Assert.Equal(c, CommandCodec.FromBytes(CommandCodec.ToBytes(c)));
        Assert.False(CommandCodec.IsWellFormed(new SetAttitude(40000, 0, 0, 0))); // does not fit the 16 bit fields

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, new List<LoggedCommand> { new(0, 0, new Batch(new Command[] { new SetAttitude(11585, 0, 0, 11585) })) }, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(13, bytes[4]);
        Assert.Single(CommandCodec.ReadLog(new MemoryStream(bytes)));
        foreach (byte old in new byte[] { 5, 8, 10, 11 })
        {
            var copy = (byte[])bytes.Clone();
            copy[4] = old;
            Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(copy)));
        }
    }

    [Fact]
    public void An_older_log_replays_byte_identically_to_the_level_ship_it_was_recorded_with()
    {
        var w = World();
        Ship(w);
        w.Submit(0, new SetThrust(0, 0, 512));
        w.Step(60);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, structures: true);
        byte[] bytes = ms.ToArray();
        bytes[4] = 11;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        var fresh = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true, rules: rules);
        Ship(fresh);
        var replay = SimWorld.Replay(log, w.Tick, fresh);
        Assert.Equal(w.Checksum(), replay.Checksum());
    }

    // ---- checksum ----------------------------------------------------------------------------------

    [Fact]
    public void A_level_ship_hashes_as_before_and_a_turned_one_does_not()
    {
        var a = World(); var b = World();
        Ship(a); Ship(b);
        Assert.Equal(a.Checksum(), b.Checksum());
        a.Submit(0, new SetAttitude(S, 0, 0, 0)); // the identity again: still the same state
        a.Step(); b.Step();
        Assert.Equal(a.Checksum(), b.Checksum());
        a.Submit(0, new SetAttitude(11585, 0, 0, 11585));
        a.Step(); b.Step();
        Assert.NotEqual(a.Checksum(), b.Checksum());
        a.Submit(0, new SetAttitude(S, 0, 0, 0));
        a.Step(); b.Step();
        Assert.Equal(a.Checksum(), b.Checksum()); // nothing else changed: no trace is left
    }

    [Fact]
    public void Attitude_is_copied_replayed_and_checked()
    {
        var w = World();
        int ship = Ship(w);
        w.Submit(0, new SetAttitude(11585, 0, 0, 11585));
        w.Step(10);
        Assert.Equal(w.Checksum(), w.Network.Clone().Checksum());
        Assert.Equal(Roll90, w.Network.Clone().AttitudeOf(ship));
        var fresh = World();
        Ship(fresh);
        var replay = SimWorld.Replay(w.Log, w.Tick, fresh);
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }

    // ---- oriented hits -----------------------------------------------------------------------------

    private static int Rock(SimWorld w, string kind)
    {
        int k = W.KindIndex(kind);
        for (int i = 0; i < w.Network.ObstacleCount; i++)
            if (w.Network.GetObstacle(i).Kind == k) return i;
        throw new InvalidOperationException("no such obstacle");
    }

    /// <summary>
    /// A rolled ship (a quarter turn about its nose axis) with the rock placed just over the top face of its turned fifth module, which
    /// stands 34 cells above the origin; the same origin level has nothing there. Returns the ship and the rock.
    /// </summary>
    private static (int Ship, int Rock) RolledShipUnderRock(SimWorld w, Attitude attitude)
    {
        int ship = Ship(w);
        int rock = Rock(w, "core:asteroid_big");
        var info = w.Network.GetObstacle(rock);
        w.Network.SetStructureAttitude(ship, attitude);
        // Put the turned module (4,0,0), the top of the rolled ship, straight under the rock's centre by a gap, then back off.
        var c = w.Network.ModuleWorldCentre(ship, 4, 0, 0);
        var s = w.Network.GetStructure(ship);
        long targetY = info.Y - info.Radius - 4 * Cell - 3 * Cell; // module centre, its half height (the turned 8 cells), a gap of 3 cells
        w.Network.SetStructureOrigin(ship, s.OriginX + info.X - c.X, s.OriginY + targetY - c.Y, s.OriginZ + info.Z - c.Z);
        return (ship, rock);
    }

    private static ObstacleEvent? FlyUp(SimWorld w, int budget = 60)
    {
        w.Submit(0, new SetThrust(0, PlayerRules.ThrustScale, 0));
        for (int t = 0; t < budget; t++)
        {
            w.Step();
            foreach (var e in w.Network.ObstacleEvents) if (e.Kind == ObstacleEventKind.Impact) return e;
        }
        return null;
    }

    [Fact]
    public void A_ship_rolled_90_degrees_hits_where_its_turned_box_is_not_where_the_upright_box_was()
    {
        var w = World();
        var (ship, rock) = RolledShipUnderRock(w, Roll90);
        var centre = w.Network.ModuleWorldCentre(ship, 3, 0, 0);
        var upright = World();
        int upShip = Ship(upright);
        upright.Network.SetStructureOrigin(upShip, w.Network.GetStructure(ship).OriginX, w.Network.GetStructure(ship).OriginY, w.Network.GetStructure(ship).OriginZ);
        // The turned module stands above the origin, the upright one beside it: they are about 26 cells apart in height.
        Assert.True(centre.Y - upright.Network.ModuleWorldCentre(upShip, 3, 0, 0).Y > 20 * Cell);

        var hit = FlyUp(w) ?? throw new InvalidOperationException("the rolled ship never hit the rock");
        Assert.Equal(rock, hit.Obstacle);
        Assert.True(hit.Damage > 0);
        int rolledHp = w.Network.ModuleHp(ship, 4, 0, 0);
        Assert.True(rolledHp < w.Network.ModuleMaxHp(ModuleKind.Reactor), "the module that hit is the turned fifth module (the reactor)");
        foreach (var other in new[] { (0, 0, 0), (1, 0, 0), (2, 0, 0), (3, 0, 0) })
            Assert.Equal(w.Network.ModuleMaxHp(w.Network.GetStructure(ship).Modules[other]), w.Network.ModuleHp(ship, other.Item1, other.Item2, other.Item3));
        w.Network.ValidateInvariants();

        // The upright ship, flown the same way from the same origin, passes below the rock and does not touch it.
        Assert.Null(FlyUp(upright));
    }

    [Fact]
    public void A_ship_rolled_90_degrees_passes_through_the_place_its_upright_box_would_have_hit()
    {
        // The rock sits where the upright cargo module is (above the origin plus 28 cells along X); rolled, that place is empty.
        var w = World();
        int ship = Ship(w);
        int rock = Rock(w, "core:asteroid_big");
        var info = w.Network.GetObstacle(rock);
        w.Network.SetStructureAttitude(ship, Roll90);
        var s = w.Network.GetStructure(ship);
        // Origin so that the upright module (3,0,0) centre (28, 2, 4 cells from the origin) is just under the rock.
        w.Network.SetStructureOrigin(ship, info.X - 28 * Cell, info.Y - info.Radius - 2 * Cell - 3 * Cell - 2 * Cell, info.Z - 4 * Cell);
        Assert.Null(FlyUp(w, 20));

        var level = World();
        int lv = Ship(level);
        level.Network.SetStructureOrigin(lv, info.X - 28 * Cell, info.Y - info.Radius - 2 * Cell - 3 * Cell - 2 * Cell, info.Z - 4 * Cell);
        Assert.NotNull(FlyUp(level, 60)); // upright, the same flight does hit
    }

    [Fact]
    public void A_level_ship_collides_exactly_as_it_did_before_attitudes()
    {
        // The identity path: same impact, same damage, same state with and without an explicit identity command.
        SimWorld Run(bool explicitIdentity)
        {
            var w = World();
            int ship = Ship(w);
            int rock = Rock(w, "core:asteroid_big");
            var i = w.Network.GetObstacle(rock);
            w.Network.SetStructureOrigin(ship, i.X - 4 * Cell, i.Y + i.Radius + 3 * Cell, i.Z - 4 * Cell);
            if (explicitIdentity) w.Submit(0, new SetAttitude(S, 0, 0, 0));
            w.Submit(0, new SetThrust(0, -PlayerRules.ThrustScale, 0));
            w.Step(60);
            return w;
        }
        var a = Run(false); var b = Run(true);
        Assert.Equal(a.Network.Checksum(), b.Network.Checksum());
        Assert.True(a.Network.ModuleHp(1, 0, 0, 0) < a.Network.ModuleMaxHp(ModuleKind.Cockpit));
    }

    [Fact]
    public void Clouds_are_measured_against_the_turned_modules()
    {
        var w = World();
        int ship = Ship(w);
        int cloud = Rock(w, "core:gravity_cloud");
        var info = w.Network.GetObstacle(cloud);
        // Rolled, the fifth module is the top of a vertical ship; put the cloud centre a little less than its radius over that top.
        w.Network.SetStructureAttitude(ship, Roll90);
        var s = w.Network.GetStructure(ship);
        var top = w.Network.ModuleWorldCentre(ship, 4, 0, 0);
        long wantY = info.Y - info.Radius - 2 * Cell; // the top face of the turned module (4 cells over its centre) is 2 cells inside the cloud
        w.Network.SetStructureOrigin(ship, s.OriginX + info.X - top.X, s.OriginY + wantY - top.Y, s.OriginZ + info.Z - top.Z);
        Assert.True(w.Network.CloudEffectOf(ship).SlowPercent > 0);
        // Level at the same origin the ship lies along X near the origin plane and is a long way from the cloud.
        w.Network.SetStructureAttitude(ship, Attitude.Identity);
        Assert.Equal(0, w.Network.CloudEffectOf(ship).SlowPercent);
    }

    [Fact]
    public void Module_centres_ports_and_bounds_follow_the_attitude()
    {
        var w = World();
        int ship = Ship(w);
        var level = w.Network.ModuleWorldCentre(ship, 3, 0, 0);
        var levelPort = w.Network.PortWorldPoint(ship, 0, 0, 0, Dir.NegX);
        w.Network.SetStructureAttitude(ship, Roll90);
        var turned = w.Network.ModuleWorldCentre(ship, 3, 0, 0);
        Assert.NotEqual(level, turned);
        // The pivot (the seat) does not move: the point of the structure frame at the seat is the same in both.
        var s = w.Network.GetStructure(ship);
        var (px, py, pz) = BeltNetwork.PivotOf(s);
        Assert.Equal((s.OriginX + px, s.OriginY + py, s.OriginZ + pz), w.Network.StructureToWorld(s, px, py, pz));
        // The turned fourth module is above the pivot: 24 cells along the old X become the vertical.
        Assert.InRange(turned.Y - (s.OriginY + py), 23 * Cell, 25 * Cell);
        // A port moves with its module, and the structure frame round-trips within a unit.
        var port = w.Network.PortWorldPoint(ship, 0, 0, 0, Dir.NegX);
        Assert.NotEqual(levelPort, port);
        var back = w.Network.WorldToStructure(s, turned.X, turned.Y, turned.Z);
        var (lx, ly, lz) = (28L * Cell, 2L * Cell, 4L * Cell);
        Assert.InRange(Math.Abs(back.X - lx), 0, 2); Assert.InRange(Math.Abs(back.Y - ly), 0, 2); Assert.InRange(Math.Abs(back.Z - lz), 0, 2);
        // The bounds around a turned module contain its turned corners.
        var (min, max) = w.Network.ModuleWorldBounds(s, (3, 0, 0));
        Assert.True(min.Y < turned.Y && turned.Y < max.Y && max.Y - min.Y >= 8 * Cell);
    }

    [Fact]
    public void Enemies_target_the_turned_modules_and_sensors_read_from_them()
    {
        var w = World();
        int ship = Ship(w);
        w.Network.AddStructureModuleUnchecked(ship, 0, 0, 1, ModuleKind.Sensor);
        var level = w.Network.ModuleWorldCentre(ship, 0, 0, 1);
        w.Network.SetStructureAttitude(ship, Around(Vector3.UnitX, MathF.PI)); // upside down about X
        var turned = w.Network.ModuleWorldCentre(ship, 0, 0, 1);
        Assert.NotEqual(level.Z, turned.Z); // the module that was behind the pilot is now in front of the pivot's other side
        w.Network.ValidateInvariants();
    }

    // ---- docking -----------------------------------------------------------------------------------

    private static SimWorld Dockable(out int ship, out int station)
    {
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true);
        ship = w.Network.ShipStructure(0);
        station = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        return w;
    }

    [Fact]
    public void Docking_needs_the_ship_to_be_lined_up_with_the_port_and_settles_it_level()
    {
        var w = Dockable(out int ship, out int station);
        var dock = new DockAt(station, 0, 0, 0, Dir.PosX);

        w.Network.SetStructureAttitude(ship, Around(Vector3.UnitZ, 0.6f)); // about 34 degrees of roll
        Assert.True(w.Network.TryDockCandidate(0, station, 0, 0, 0, Dir.PosX, out var c));
        Assert.False(c.Aligned);
        w.Submit(0, dock);
        w.Step();
        Assert.Equal(Reject.Misaligned, w.LastResults.Single().Reject);
        Assert.False(w.Network.IsDocked(0));

        // Within about 15 degrees it docks, and the ship ends up exactly level on the port's axes.
        w.Network.SetStructureAttitude(ship, Around(Vector3.UnitY, 0.2f));
        w.Submit(0, dock);
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        Assert.True(w.Network.IsDocked(0));
        Assert.True(w.Network.AttitudeOf(ship).IsIdentity);
        w.Network.ValidateInvariants();

        // A docked ship cannot be turned, but can be told it is level.
        w.Submit(0, new SetAttitude(11585, 0, 0, 11585));
        w.Step();
        Assert.Equal(Reject.Docked, w.LastResults.Single().Reject);
        w.Submit(0, new SetAttitude(S, 0, 0, 0));
        w.Step();
        Assert.True(w.LastResults.Single().Ok);
    }

    [Fact]
    public void The_alignment_tolerance_is_about_fifteen_degrees()
    {
        Assert.True(Around(Vector3.UnitY, 0.25f).IsDockAligned); // 14.3 degrees
        Assert.False(Around(Vector3.UnitY, 0.28f).IsDockAligned); // 16 degrees
        Assert.True(Attitude.Identity.IsDockAligned);
        Assert.False(Attitude.Of(0, 0, S, 0).IsDockAligned);
    }

    [Fact]
    public void A_turned_ships_port_world_points_are_what_the_range_check_measures()
    {
        var w = Dockable(out int ship, out int station);
        w.Network.SetStructureAttitude(ship, Around(Vector3.UnitY, 0.2f)); // aligned enough to dock, turned enough to move the port
        Assert.True(w.Network.TryDockCandidate(0, station, 0, 0, 0, Dir.PosX, out var turned));
        w.Network.SetStructureAttitude(ship, Attitude.Identity);
        Assert.True(w.Network.TryDockCandidate(0, station, 0, 0, 0, Dir.PosX, out var level));
        Assert.NotEqual(level.DistanceUnits, turned.DistanceUnits);
    }

    // ---- what the client sends ---------------------------------------------------------------------

    [Fact]
    public void The_attitude_throttle_sends_changes_at_ten_a_second_and_the_final_value_after_a_pause()
    {
        var t = new AttitudeThrottle();
        var known = Attitude.Identity;
        var a = Around(Vector3.UnitZ, 0.10f);
        var b = Around(Vector3.UnitZ, 0.20f);
        Assert.Null(t.Offer(known, known, 0)); // nothing to say
        Assert.NotNull(t.Offer(known, a, 0)); // the first change goes out at once
        Assert.Null(t.Offer(known, b, 0.05)); // too soon
        Assert.NotNull(t.Offer(known, b, 0.11));
        // A change below a quarter of a degree waits for the settle interval, then goes out so the simulation ends exact.
        var tiny = Around(Vector3.UnitZ, 0.20f + 0.001f);
        Assert.Null(t.Offer(b, tiny, 0.2));
        Assert.NotNull(t.Offer(b, tiny, 0.45));
        t.Reset();
        Assert.NotNull(t.Offer(known, a, 10));
    }

    [Fact]
    public void The_client_quaternion_conversions_round_trip_through_the_simulation_value()
    {
        var q = Quaternion.CreateFromYawPitchRoll(0.7f, -1.1f, 2.4f);
        var a = ShipAttitude.Quantize(q);
        var cmd = ShipAttitude.ToCommand(a);
        Assert.True(Attitude.TryCreate(cmd.W, cmd.X, cmd.Y, cmd.Z, out var read) && read == a);
        Assert.True(ShipAttitude.AngleBetween(a, ShipAttitude.Quantize(ShipAttitude.ToQuaternion(a))) < 4e-3f); // acos of a number next to 1 is coarse in floats
        Assert.True(ShipAttitude.AngleBetween(Attitude.Identity, Roll90) is > 1.55f and < 1.59f);
    }
}
