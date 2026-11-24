using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Brute force over the walking body and the drawn shells: every ship module kind next to every other (along X and along Z), random walks with
/// big and small steps, the stand-up spawn of the pilot seat, and the joints of the hull sections. The check does not trust the body's own
/// four-corner test: every walk samples a grid of points over the whole footprint and several heights of the body and asks, point by point,
/// whether it is clear of the shell.
/// </summary>
public class BodyShellBruteForceTests
{
    private static readonly ModuleKind[] ShipKinds = Enum.GetValues<ModuleKind>().Where(k => ModuleKindIds.AllowedOn(StructureKind.Ship, k)).ToArray();
    private static readonly ModuleKind[] StationKinds = Enum.GetValues<ModuleKind>().Where(k => ModuleKindIds.AllowedOn(StructureKind.Station, k)).ToArray();

    /// <summary>The kinds a ship is built of end to end: they share one section, so their joints line up.</summary>
    private static readonly ModuleKind[] Corridor =
    {
        ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.ReactorMk2, ModuleKind.Cockpit,
        ModuleKind.Engine, ModuleKind.Defence, ModuleKind.DroneBay, ModuleKind.Dock,
    };

    private static BodyBox[] Layout(Dictionary<(int Mx, int My, int Mz), ModuleKind> mods, bool ship) =>
        BodyMotion.Volumes(mods, k => HullProfile.MaskOf((x, y, z) => mods.TryGetValue((x, y, z), out var kind) ? kind : null, k.Mx, k.My, k.Mz), ship);

    private static Dictionary<(int, int, int), ModuleKind> Pair(ModuleKind a, ModuleKind b, bool alongZ) => new()
    {
        [(0, 0, 0)] = a,
        [alongZ ? (0, 0, 1) : (1, 0, 0)] = b,
    };

    /// <summary>True when the point (local cells of the structure) is inside the walkable part of some module: the point-wise form of the shell test.</summary>
    private static bool PointFree(BodyBox[] volumes, float x, float y, float z)
    {
        foreach (var b in volumes)
        {
            if (y < b.MinY - 0.001f || y >= b.MaxY || x < b.MinX || x > b.MaxX || z < b.MinZ || z > b.MaxZ) continue;
            if (b.Shell is not { } shell) return true;
            float lift = y - b.MinY;
            // a point of the body, at the height above the floor of its module: feet and head are the same point
            if (HullProfile.Clear(shell, x - b.MinX, z - b.MinZ, lift, lift)) return true;
        }
        return false;
    }

    private static readonly float[] Heights = { HullProfile.DeckTop + 0.01f, 0.5f, 1.0f, 1.4f, HullProfile.BodyHeight - 0.01f };
    private static readonly float[] Offsets = { -BodyMotion.Radius, -BodyMotion.Radius / 2, 0f, BodyMotion.Radius / 2, BodyMotion.Radius };

    private static string? Violation(BodyBox[] volumes, Vector3 p)
    {
        if (!BodyMotion.Contains(p, volumes)) return $"Contains is false at {p}";
        foreach (float h in Heights)
            foreach (float dx in Offsets)
                foreach (float dz in Offsets)
                    if (!PointFree(volumes, p.X + dx, p.Y + h, p.Z + dz)) return $"body point ({p.X + dx:0.00},{p.Y + h:0.00},{p.Z + dz:0.00}) of {p} is inside the shell";
        return null;
    }

    private static Vector3? StartIn(BodyBox[] volumes)
    {
        foreach (var b in volumes)
        {
            var p = BodyMotion.Nearest(new Vector3((b.MinX + b.MaxX) / 2, b.MinY, (b.MinZ + b.MaxZ) / 2), volumes);
            if (BodyMotion.Contains(p, volumes)) return p;
        }
        return null;
    }

    private static void RandomWalks(BodyBox[] volumes, string label, int seed, int steps)
    {
        var start = StartIn(volumes);
        if (start is null) return; // nothing walkable in this layout (a pair of pods)
        var rng = new Random(seed);
        var p = start.Value;
        Assert.Null(Violation(volumes, p));
        float[] dts = { 1f / 60f, 1f / 20f, 0.25f, 0.5f, 1f };
        for (int i = 0; i < steps; i++)
        {
            // a long push in one direction every now and then, so the body reaches every wall and every joint
            var dir = i % 25 < 12 && i > 0 ? new Vector3(MathF.Sign(rng.NextSingle() - 0.5f), 0, MathF.Sign(rng.NextSingle() - 0.5f)) : new Vector3(rng.NextSingle() - 0.5f, 0, rng.NextSingle() - 0.5f);
            float dt = dts[rng.Next(dts.Length)];
            var next = BodyMotion.Step(p, dir, rng.Next(2) == 0 ? BodyMotion.WalkSpeed : BodyMotion.RunSpeed, dt, volumes);
            Assert.True(Violation(volumes, next) is null, $"{label} step {i} dt={dt}: {Violation(volumes, next)} (from {p})");
            p = next;
        }
    }

    [Fact]
    public void EveryShipKindNextToEveryOtherNeverLetsTheBodyIntoTheShell()
    {
        int seed = 1;
        foreach (var a in ShipKinds)
            foreach (var b in ShipKinds)
                foreach (bool alongZ in new[] { false, true })
                    RandomWalks(Layout(Pair(a, b, alongZ), true), $"{a}+{b} alongZ={alongZ}", seed++, 260);
    }

    [Fact]
    public void EveryStationKindNextToEveryOtherNeverLetsTheBodyIntoTheShell()
    {
        int seed = 500;
        foreach (var a in StationKinds)
            foreach (var b in StationKinds)
                foreach (bool alongZ in new[] { false, true })
                    RandomWalks(Layout(Pair(a, b, alongZ), false), $"station {a}+{b} alongZ={alongZ}", seed++, 160);
    }


    [Fact]
    public void ADockedShipAndStationNeverLetTheBodyIntoEitherShell()
    {
        int seed = 900;
        foreach (var ship in Corridor)
            foreach (var station in new[] { ModuleKind.Dock, ModuleKind.Core, ModuleKind.Hull, ModuleKind.Workshop, ModuleKind.Defence })
                foreach (bool alongZ in new[] { false, true })
                {
                    // the ship's flank (or nose) is docked to the station module next to it: both sides carry the neighbour bit and the airlock bit
                    int shipMask = alongZ ? HullProfile.Rear | HullProfile.DockedRear : HullProfile.Right | HullProfile.DockedRight;
                    int stationMask = alongZ ? HullProfile.Front | HullProfile.DockedFront : HullProfile.Left | HullProfile.DockedLeft;
                    var s = BodyMotion.Volumes(new[] { new KeyValuePair<(int, int, int), ModuleKind>((0, 0, 0), ship) }, _ => shipMask, true);
                    var t = BodyMotion.Volumes(new[] { new KeyValuePair<(int, int, int), ModuleKind>(alongZ ? (0, 0, 1) : (1, 0, 0), station) }, _ => stationMask, false);
                    RandomWalks(s.Concat(t).ToArray(), $"docked {ship}+{station} alongZ={alongZ}", seed++, 300);
                }
    }
    [Fact]
    public void ARowOfEveryCorridorKindIsWalkedEndToEnd()
    {
        // all the body kinds of a ship in one row, then the starter ship with its pods: the body walks the whole spine and never leaves it
        var row = new Dictionary<(int, int, int), ModuleKind>();
        for (int i = 0; i < Corridor.Length; i++) row[(0, 0, i)] = Corridor[i];
        RandomWalks(Layout(row, true), "row", 77, 1500);
        var starter = new Dictionary<(int, int, int), ModuleKind>
        {
            [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Cargo, [(0, 0, 3)] = ModuleKind.Reactor, [(0, 0, 4)] = ModuleKind.Engine,
            [(0, 0, -1)] = ModuleKind.MiningLaser, [(0, 1, 0)] = ModuleKind.Gun,
        };
        RandomWalks(Layout(starter, true), "starter", 78, 1500);
        var wide = new Dictionary<(int, int, int), ModuleKind>(starter) { [(-1, 0, 0)] = ModuleKind.Cargo, [(1, 0, 0)] = ModuleKind.Workshop, [(-1, 0, 1)] = ModuleKind.Cargo, [(1, 0, 3)] = ModuleKind.Defence };
        RandomWalks(Layout(wide, true), "wide", 79, 1500);
    }

    [Fact]
    public void AGridOfBodyPositionsOverEveryShipKindKeepsAllItsPointsInside()
    {
        // not a walk: every position on a 0.25 grid of a module of each kind, in a row of three (joints on both ends), is judged by the point test
        foreach (var kind in ShipKinds)
        {
            var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Hull, [(0, 0, 1)] = kind, [(0, 0, 2)] = ModuleKind.Hull, [(1, 0, 1)] = ModuleKind.Cargo };
            var v = Layout(mods, true);
            for (float x = 0.3f; x < 16f; x += 0.25f)
                for (float z = 0.3f; z < 24f; z += 0.25f)
                {
                    var p = new Vector3(x, 0, z);
                    if (!BodyMotion.Contains(p, v)) continue;
                    var bad = Violation(v, p);
                    Assert.True(bad is null, $"{kind}: {bad}");
                }
        }
    }

    // ---- standing up ----------------------------------------------------------------------------------------------------

    [Fact]
    public void StandingUpFromTheSeatPutsTheBodyInsideTheShellWhateverIsAroundTheCockpit()
    {
        var around = new ModuleKind?[] { null }.Concat(ShipKinds.Select(k => (ModuleKind?)k)).ToArray();
        int count = 0;
        foreach (var front in around)
            foreach (var rear in around)
                foreach (var side in new ModuleKind?[] { null, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Gun })
                    foreach (var up in new ModuleKind?[] { null, ModuleKind.Gun, ModuleKind.Hull })
                    {
                        var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit };
                        if (front is { } f) mods[(0, 0, -1)] = f;
                        if (rear is { } r) mods[(0, 0, 1)] = r;
                        if (side is { } s) { mods[(-1, 0, 0)] = s; mods[(1, 0, 0)] = s; }
                        if (up is { } u) mods[(0, 1, 0)] = u;
                        var v = Layout(mods, true);
                        var seat = SeatLocator.Locate(mods);
                        // the spawn of the real stand-up path (GodotPlay.StandUp): the stand spot as it is, with no nearest-spot correction
                        Assert.True(Violation(v, seat.Stand) is null, $"front={front} rear={rear} side={side} up={up}: {Violation(v, seat.Stand)}");
                        Assert.True(BodyMotion.InSeatReach(seat.Stand, seat.Seat), "the stand spot is within reach of the seat");
                        count++;
                    }
        Assert.True(count > 1000);
    }

    [Fact]
    public void TheSeatAndTheConsoleAreSolidAndTheAisleBesideThemIsOpen()
    {
        var v = Layout(new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull }, true);
        var seat = SeatLocator.Locate(new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit });
        // straight ahead from the stand spot: the seat stops the body, it does not walk through it
        var p = seat.Stand;
        for (int i = 0; i < 240; i++) p = BodyMotion.Step(p, new Vector3(0, 0, -1), BodyMotion.WalkSpeed, 1f / 60f, v);
        Assert.True(p.Z >= HullProfile.SeatZ1 + BodyMotion.Radius - 0.01f, $"stopped by the seat: {p}");
        // beside the seat there is room to walk forward to the console and no further
        var q = new Vector3(1.7f, 0, seat.Stand.Z);
        for (int i = 0; i < 360; i++) q = BodyMotion.Step(q, new Vector3(0, 0, -1), BodyMotion.WalkSpeed, 1f / 60f, v);
        Assert.InRange(q.Z, HullProfile.ConsoleZ1 - 0.1f, 3.6f);
        Assert.Null(Violation(v, q));
        // and the pilot can still reach the hull behind the cockpit
        var back = seat.Stand;
        for (int i = 0; i < 600; i++) back = BodyMotion.Step(back, new Vector3(0, 0, 1), BodyMotion.WalkSpeed, 1f / 60f, v);
        Assert.True(back.Z > 12f, $"walked into the hull behind: {back}");
    }

    // ---- joints ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryBodyKindOfAShipHasTheSameSectionSoTheJointsLineUp()
    {
        foreach (var k in Corridor)
            Assert.Equal(HullProfile.Roof, HullProfile.RoofOf(k, true));
        foreach (var a in Corridor)
            foreach (var b in Corridor)
            {
                // the end of a and the start of b, as they are drawn when they touch: the same polygon, vertex for vertex
                var sa = HullProfile.Octagon(HullProfile.RoofOf(a, true), 1f, HullProfile.Rear);
                var sb = HullProfile.Octagon(HullProfile.RoofOf(b, true), 1f, HullProfile.Front);
                Assert.Equal(sa, sb);
                var la = HullProfile.SectionAt(HullProfile.ShellOf(a, true, HullProfile.Rear), HullProfile.D - 0.01f);
                var lb = HullProfile.SectionAt(HullProfile.ShellOf(b, true, HullProfile.Front), 0.01f);
                if (a == ModuleKind.Engine || la is null || lb is null) continue; // a tail closes the end; a nose is checked below
                Assert.Equal(la, lb);
            }
    }

    [Fact]
    public void TheNoseAndTheTailTaperInsideTheirOwnModule()
    {
        // a nose starts small at the front of its own module and is the full section by NoseEnd, well before the rear joint
        var shell = HullProfile.ShellOf(ModuleKind.Cockpit, true, HullProfile.Rear);
        Assert.True(shell.Nose);
        var full = HullProfile.SectionAt(shell, HullProfile.D - 0.01f)!;
        Assert.Equal(full, HullProfile.SectionAt(shell, HullProfile.NoseEnd + 0.01f));
        Assert.Equal(full, HullProfile.SectionAt(HullProfile.ShellOf(ModuleKind.Engine, true, HullProfile.Front), 0.01f));
        Assert.True(HullProfile.TailStart > HullProfile.D / 2 && HullProfile.TailEnd <= HullProfile.D);
    }

    [Fact]
    public void APodInFrontOfACockpitClosesItsNoseWithoutADoorway()
    {
        var shell = HullProfile.ShellOf(ModuleKind.Cockpit, true, HullProfile.Front | HullProfile.PodFront);
        Assert.False(shell.Nose);
        Assert.Null(HullProfile.SectionAt(shell, 0.05f));    // the windscreen: solid for the body
        Assert.NotNull(HullProfile.SectionAt(shell, 0.5f));
        Assert.False(HullProfile.Clear(shell, 4f, 0.1f, 0.1f, 1.7f));
        var door = HullProfile.ShellOf(ModuleKind.Cockpit, true, HullProfile.Front); // a hull in front: the doorway is open
        Assert.True(HullProfile.Clear(door, 4f, 0.1f, 0.1f, 1.7f));
    }

    [Fact]
    public void MaskOfFlagsPodsOnTheFrontAndRearOnly()
    {
        var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, -1)] = ModuleKind.MiningLaser, [(0, 1, 0)] = ModuleKind.Gun, [(0, 0, 1)] = ModuleKind.Hull };
        int m = HullProfile.MaskOf((x, y, z) => mods.TryGetValue((x, y, z), out var k) ? k : null, 0, 0, 0);
        Assert.Equal(HullProfile.Front | HullProfile.PodFront | HullProfile.Rear | HullProfile.Up, m);
        Assert.True(HullProfile.IsPod(ModuleKind.Shield) && HullProfile.IsPod(ModuleKind.Sensor) && HullProfile.IsPod(ModuleKind.GunMk2));
        Assert.False(HullProfile.IsPod(ModuleKind.Hull));
    }
}
