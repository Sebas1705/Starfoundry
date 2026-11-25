using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The ship sections (skins), the solid furniture of the module kinds, the closed pod joints, the machine solids of the walking body, and a brute-force
/// check that the front of every kind of cockpit holds against every approach: from the seat, from the doorway behind, at walking and running speed, in
/// short and very long frames, straight and at every angle.
/// </summary>
[Trait("Tier", "Slow")]
public class ShellVarietyTests
{
    private static BodyBox[] Layout(Dictionary<(int Mx, int My, int Mz), ModuleKind> mods) =>
        BodyMotion.Volumes(mods, k => HullProfile.MaskOf((x, y, z) => mods.TryGetValue((x, y, z), out var kind) ? kind : null, k.Mx, k.My, k.Mz), true);

    private static readonly float[] Heights = { HullProfile.DeckTop + 0.01f, 0.6f, 1.2f, HullProfile.BodyHeight - 0.01f };
    private static readonly float[] Offsets = { -BodyMotion.Radius, -0.15f, 0f, 0.15f, BodyMotion.Radius };

    /// <summary>Every point of the body footprint at several heights is clear of the shell, judged point by point.</summary>
    private static bool Clean(BodyBox[] volumes, Vector3 p)
    {
        foreach (float h in Heights)
            foreach (float dx in Offsets)
                foreach (float dz in Offsets)
                {
                    bool free = false;
                    foreach (var b in volumes)
                    {
                        float x = p.X + dx, y = p.Y + h, z = p.Z + dz;
                        if (y < b.MinY || y >= b.MaxY || x < b.MinX || x > b.MaxX || z < b.MinZ || z > b.MaxZ) continue;
                        if (b.Shell is not { } shell || HullProfile.Clear(shell, x - b.MinX, z - b.MinZ, y - b.MinY, y - b.MinY)) { free = true; break; }
                    }
                    if (!free) return false;
                }
        return true;
    }

    private static readonly (string Name, Dictionary<(int Mx, int My, int Mz), ModuleKind> Mods)[] CockpitLayouts =
    {
        ("starter", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Cargo, [(0, 0, -1)] = ModuleKind.MiningLaser, [(0, 1, 0)] = ModuleKind.Gun }),
        ("nose", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull }),
        ("nose and gun", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 1, 0)] = ModuleKind.Gun }),
        ("flank", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(1, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(1, 0, 1)] = ModuleKind.Hull }),
        ("sensor in front", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, -1)] = ModuleKind.Sensor }),
        ("shield in front, hull on top", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, -1)] = ModuleKind.Shield, [(0, 1, 0)] = ModuleKind.Hull }),
    };

    /// <summary>The front end of a cockpit: the body keeps its radius behind the cap of the windscreen (or of the nose) and never gets past it.</summary>
    private const float FrontLimit = HullProfile.NoseStart + HullProfile.LinerInset - 0.05f;

    [Fact]
    public void TheFrontOfEveryCockpitHoldsAgainstEveryApproach()
    {
        int walks = 0;
        foreach (var (name, mods) in CockpitLayouts)
        {
            var v = Layout(mods);
            var starts = new List<Vector3>();
            for (float x = 1.0f; x < 7.5f; x += 1.3f)
                for (float z = 0.3f; z < 15f; z += 2.3f)
                {
                    var p = new Vector3(x, 0, z);
                    if (BodyMotion.Contains(p, v)) starts.Add(p);
                }
            starts.Add(SeatLocator.Locate(mods).Stand);
            Assert.NotEmpty(starts);
            foreach (var start in starts)
                for (int a = 0; a < 12; a++)
                {
                    float ang = a * MathF.Tau / 12 + 0.05f;
                    var wish = new Vector3(MathF.Sin(ang), 0, -MathF.Cos(ang)); // every heading, straight ahead at a = 0
                    foreach (var (dt, speed) in new[] { (1f / 60f, BodyMotion.RunSpeed), (0.25f, BodyMotion.RunSpeed), (1f, BodyMotion.RunSpeed), (1f, BodyMotion.WalkSpeed) })
                    {
                        var p = start;
                        for (int i = 0; i < 10; i++)
                        {
                            p = BodyMotion.Step(p, wish, speed, dt, v);
                            Assert.True(Clean(v, p), $"{name}: from {start} heading {ang:0.00} dt={dt} step {i}: {p}");
                            Assert.True(p.Z >= FrontLimit, $"{name}: the body got in front of the cockpit: {p}");
                        }
                        walks++;
                    }
                }
        }
        Assert.True(walks > 1500);
    }

    [Fact]
    public void FromTheDoorwayBehindARunAtTheFrontStopsInsideTheCockpit()
    {
        foreach (var (name, mods) in CockpitLayouts)
        {
            var v = Layout(mods);
            foreach (float x in new[] { 2.0f, 3.0f, 4.0f, 5.0f, 6.0f })
                foreach (float dt in new[] { 1f / 60f, 0.5f, 1f, 2f })
                {
                    var p = new Vector3(x, 0, 9f); // in the hull behind the cockpit, at its doorway
                    if (!BodyMotion.Contains(p, v)) continue;
                    for (int i = 0; i < 12; i++) p = BodyMotion.Step(p, new Vector3(0, 0, -1), BodyMotion.RunSpeed, dt, v);
                    Assert.True(Clean(v, p) && p.Z >= FrontLimit, $"{name}: ran through the front: {p}");
                }
        }
    }

    [Fact]
    public void TheWidePilotSectionHasABiggerFloorAndTallerWallsThanThePlainOne()
    {
        var plain = HullProfile.Octagon(HullProfile.Roof, 1f);
        var wide = HullProfile.Octagon(HullProfile.Roof, 1f, HullProfile.SkinWide << HullProfile.SkinShift);
        Assert.True(wide[1].X - wide[0].X > plain[1].X - plain[0].X + 1f, "the floor of the pilot section is wider");
        Assert.True(wide[3].Y - wide[2].Y > plain[3].Y - plain[2].Y + 0.5f, "its side wall is taller, so the glazing is");
        Assert.Equal(HullProfile.SkinWide, HullProfile.SkinOf(ModuleKind.Cockpit, 3, 1, -2));
    }

    [Fact]
    public void EverySkinIsAConvexSectionInsideTheModuleAndTheJointRingSeesTheNeighbourSkin()
    {
        for (int skin = 0; skin < 4; skin++)
            foreach (int mask in new[] { 0, HullProfile.Left, HullProfile.Right | HullProfile.Up, HullProfile.Down })
            {
                var o = HullProfile.Octagon(HullProfile.Roof, 1f, mask | (skin << HullProfile.SkinShift));
                Assert.Equal(8, o.Length);
                float area = 0;
                for (int i = 0; i < 8; i++)
                {
                    var a = o[i]; var b = o[(i + 1) % 8]; var c = o[(i + 2) % 8];
                    Assert.True((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) >= -1e-4f, $"skin {skin} mask {mask} is convex at {i}");
                    area += a.X * b.Y - b.X * a.Y;
                    Assert.InRange(a.X, -0.001f, HullProfile.W + 0.001f);
                }
                Assert.True(area > 0, "counter-clockwise");
            }
        // a module whose rear neighbour is an arched hull reports that skin for the joint plate
        int found = -1;
        for (int mz = 1; mz < 60 && found < 0; mz++)
            if (HullProfile.SkinOf(ModuleKind.Hull, 0, 0, mz) == HullProfile.SkinArched) found = mz;
        Assert.True(found > 0, "arched hulls exist");
        var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, found - 1)] = ModuleKind.Cargo, [(0, 0, found)] = ModuleKind.Hull };
        int m = HullProfile.MaskOf((x, y, z) => mods.TryGetValue((x, y, z), out var k) ? k : null, 0, 0, found - 1);
        Assert.Equal(HullProfile.SkinArched, (HullProfile.NeighbourMask(m, front: false)!.Value >> HullProfile.SkinShift) & 3);
    }

    [Fact]
    public void SkinsAreDeterministicAndEveryKindOfSectionOccursButNotOnPodsOrTheEngine()
    {
        var seen = new HashSet<int>();
        for (int x = -3; x < 3; x++)
            for (int z = -3; z < 12; z++)
            {
                int a = HullProfile.SkinOf(ModuleKind.Cargo, x, 0, z);
                Assert.Equal(a, HullProfile.SkinOf(ModuleKind.Cargo, x, 0, z));
                seen.Add(a);
                Assert.Equal(HullProfile.SkinPlain, HullProfile.SkinOf(ModuleKind.Engine, x, 0, z));
                Assert.Equal(HullProfile.SkinPlain, HullProfile.SkinOf(ModuleKind.Gun, x, 0, z));
                Assert.Equal(HullProfile.SkinPlain, HullProfile.SkinOf(ModuleKind.Dock, x, 0, z));
            }
        Assert.Contains(HullProfile.SkinPlain, seen);
        Assert.Contains(HullProfile.SkinArched, seen);
        Assert.Contains(HullProfile.SkinBoxy, seen);
    }

    [Fact]
    public void APodIsClosedAtBothEndsSoNoDaylightComesThroughTheJoint()
    {
        foreach (var kind in new[] { ModuleKind.MiningLaser, ModuleKind.Gun, ModuleKind.Shield, ModuleKind.Sensor })
        {
            var shell = HullProfile.ShellOf(kind, true, HullProfile.Rear | HullProfile.Front);
            Assert.True(shell.Pod);
            Assert.Null(HullProfile.SectionAt(shell, HullProfile.D - 0.05f)); // the rear bulkhead stands even with a neighbour behind
            Assert.Null(HullProfile.SectionAt(shell, 0.05f));
        }
    }

    [Fact]
    public void TheFurnitureOfTheModuleKindsIsSolid()
    {
        BodyBox[] One(ModuleKind kind, int mask = 0) => BodyMotion.Volumes(new[] { new KeyValuePair<(int, int, int), ModuleKind>((0, 0, 0), kind) }, _ => mask, true);
        Vector3 Run(BodyBox[] v, Vector3 from, Vector3 dir) { var p = from; for (int i = 0; i < 300; i++) p = BodyMotion.Step(p, dir, BodyMotion.WalkSpeed, 1f / 60f, v); return p; }
        // the cargo rack on the left wall, the crates on the right
        Assert.True(Run(One(ModuleKind.Cargo), new(4, 0, 2), new(-1, 0, 0)).X > 1.7f + BodyMotion.Radius - 0.05f);
        Assert.True(Run(One(ModuleKind.Cargo), new(4, 0, 2), new(1, 0, 0)).X < 6.05f - BodyMotion.Radius + 0.05f);
        // the engine's machinery fills the room, the reactor vessel and its pillars stand in the middle, a hangar is closed by its door wall
        Assert.True(Run(One(ModuleKind.Engine, HullProfile.Front), new(4, 0, 0.45f), new(0, 0, 1)).Z < 0.7f);
        Assert.False(BodyMotion.Contains(new Vector3(4, 0, 4), One(ModuleKind.Reactor)));
        Assert.False(BodyMotion.Contains(new Vector3(1.2f, 0, 1.2f), One(ModuleKind.Reactor)));
        Assert.True(Run(One(ModuleKind.DroneBay), new(2.2f, 0, 4), new(0, 0, 1)).Z < 7.1f);
        // the gun deck: a turret mount stands in the way
        Assert.False(BodyMotion.Contains(new Vector3(4, 0, 2.5f), One(ModuleKind.Defence, HullProfile.Front | HullProfile.Rear)));
    }

    [Fact]
    public void MachinesAreSolidAndBeltsAreWalkedOver()
    {
        var v = BodyMotion.Volumes(new[] { new KeyValuePair<(int, int, int), ModuleKind>((0, 0, 0), ModuleKind.Hull) }, _ => 0, true);
        var machine = new[] { new BodySolid(4f, 0f, 4f, 5f, 1f, 5f) };
        var belt = new[] { new BodySolid(4f, 0f, 4f, 5f, 0.15f, 5f) };
        var start = new Vector3(2.5f, 0, 4.5f);
        Vector3 Walk(BodySolid[] solids) { var p = start; for (int i = 0; i < 300; i++) p = BodyMotion.Step(p, new Vector3(1, 0, 0), BodyMotion.WalkSpeed, 1f / 60f, v, solids); return p; }
        Assert.True(Walk(machine).X < 4f - BodyMotion.Radius + 0.05f, "a machine stops the body");
        Assert.True(Walk(belt).X > 5.3f, "the body walks over a belt");
        Assert.False(BodyMotion.Contains(new Vector3(4.5f, 0, 4.5f), v, machine));
        // standing in a spot that becomes solid moves the body to the nearest free place
        var moved = BodyMotion.Nearest(new Vector3(4.5f, 0, 4.5f), v, machine);
        Assert.True(BodyMotion.Contains(moved, v, machine));
    }


    // ---- walkable connectivity ----------------------------------------------------------------------------------------

    /// <summary>Flood fill over the body's collision from <paramref name="start"/> on the floor (y = 0), on a grid of <paramref name="res"/> cells; returns the reached positions.</summary>
    private static HashSet<(int, int)> Flood(BodyBox[] v, IReadOnlyList<BodySolid>? solids, Vector3 start, float res = 0.25f)
    {
        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        var s = ((int)MathF.Round(start.X / res), (int)MathF.Round(start.Z / res));
        if (!BodyMotion.Contains(new Vector3(s.Item1 * res, 0, s.Item2 * res), v, solids)) return seen;
        seen.Add(s); queue.Enqueue(s);
        while (queue.Count > 0)
        {
            var (cx, cz) = queue.Dequeue();
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (cx + dx, cz + dz);
                if (seen.Contains(n) || !BodyMotion.Contains(new Vector3(n.Item1 * res, 0, n.Item2 * res), v, solids)) continue;
                seen.Add(n); queue.Enqueue(n);
            }
        }
        return seen;
    }

    private static bool ReachesModule(HashSet<(int, int)> reached, (int Mx, int Mz) m, float res = 0.25f)
    {
        foreach (var (cx, cz) in reached)
        {
            float x = cx * res, z = cz * res;
            if (x >= m.Mx * 8f && x < (m.Mx + 1) * 8f && z >= m.Mz * 8f && z < (m.Mz + 1) * 8f) return true;
        }
        return false;
    }

    private static void AssertWalkable(string name, Dictionary<(int Mx, int My, int Mz), ModuleKind> mods, IReadOnlyList<BodySolid>? solids = null, BodyBox[]? extra = null)
    {
        var v = Layout(mods);
        if (extra is not null) v = v.Concat(extra).ToArray();
        var reached = Flood(v, solids, SeatLocator.Locate(mods).Stand);
        Assert.NotEmpty(reached);
        foreach (var (key, kind) in mods)
        {
            if (key.My != 0 || HullProfile.IsPod(kind)) continue;
            if (kind == ModuleKind.Engine)
            {
                // a deliberate dead end: its entrance (the doorway at its front) must be reached from the module in front of it
                var inFront = (key.Mx, key.Mz - 1);
                Assert.True(mods.ContainsKey((key.Mx, 0, key.Mz - 1)) && ReachesModule(reached, inFront), $"{name}: the way to the engine at {key} is blocked");
                continue;
            }
            Assert.True(ReachesModule(reached, (key.Mx, key.Mz)), $"{name}: {kind} at {key} cannot be reached from the pilot seat");
        }
    }

    [Fact]
    public void EveryWalkableModuleIsReachableFromTheSeatInTheStarterTheNoseLayoutAndTheRow()
    {
        AssertWalkable("starter", CockpitLayouts[0].Mods);
        AssertWalkable("starter with a workshop", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Workshop, [(0, 0, 3)] = ModuleKind.Cargo, [(0, 0, 4)] = ModuleKind.Reactor, [(0, 0, 5)] = ModuleKind.Engine, [(0, 0, -1)] = ModuleKind.MiningLaser, [(0, 1, 0)] = ModuleKind.Gun });
        AssertWalkable("nose", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Cargo, [(0, 0, 3)] = ModuleKind.Workshop, [(0, 0, 4)] = ModuleKind.Reactor, [(0, 0, 5)] = ModuleKind.Engine, [(0, 1, 0)] = ModuleKind.Gun, [(1, 0, 0)] = ModuleKind.Cockpit, [(1, 0, 1)] = ModuleKind.Hull });
        var kinds = new[] { ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.ReactorMk2, ModuleKind.Defence, ModuleKind.Dock, ModuleKind.Engine }; // the drone bay is closed by its hangar door wall: it ends a line (the corridor test below puts it last)
        var row = new Dictionary<(int, int, int), ModuleKind>();
        for (int i = 0; i < kinds.Length; i++) row[(0, 0, i)] = kinds[i];
        AssertWalkable("row", row);
        // every order of the corridor kinds in a row of five behind a cockpit keeps the way open (the drone bay closes its end, so it goes last)
        var corridor = new[] { ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.ReactorMk2, ModuleKind.Defence };
        foreach (var a in corridor) foreach (var b in corridor) foreach (var c in corridor)
            AssertWalkable($"{a} {b} {c}", new() { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = a, [(0, 0, 2)] = b, [(0, 0, 3)] = c, [(0, 0, 4)] = ModuleKind.DroneBay });
    }

    [Fact]
    public void TheWorkbenchAndTheMachinesOfTheFactoryLeaveTheShipWalkable()
    {
        var mods = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Workshop, [(0, 0, 3)] = ModuleKind.Cargo };
        var bench = WorkbenchSpot.Pick(mods)!.Value;
        var fp = WorkbenchSpot.Footprint(bench);
        float oz = bench.Module.Mz * 8f;
        var solids = new List<BodySolid> { new(fp.MinX, 0, oz + fp.MinZ, fp.MaxX, 1.5f, oz + fp.MaxZ) };
        // the machines of the walk-shot factory (a smelter, a depot) on the floor of the workshop, one cell each
        foreach (var (x, z) in new[] { (9, 18), (14, 18), (11, 21) }) solids.Add(new BodySolid(x, 0, z, x + 1, 1, z + 1));
        AssertWalkable("factory", mods, solids);
    }

    [Fact]
    public void ADockedShipAndStationAreOneWalkableWholeThroughTheAirlock()
    {
        var ship = new Dictionary<(int Mx, int My, int Mz), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Cargo };
        int ShipMask((int Mx, int My, int Mz) k) => HullProfile.MaskOf((x, y, z) => ship.TryGetValue((x, y, z), out var kind) ? kind : null, k.Mx, k.My, k.Mz)
            | (k == (0, 0, 1) ? HullProfile.Right | HullProfile.DockedRight : 0);
        var station = new Dictionary<(int Mx, int My, int Mz), ModuleKind> { [(1, 0, 1)] = ModuleKind.Dock, [(1, 0, 2)] = ModuleKind.Hull, [(2, 0, 1)] = ModuleKind.Core };
        int StMask((int Mx, int My, int Mz) k) => HullProfile.MaskOf((x, y, z) => station.TryGetValue((x, y, z), out var kind) ? kind : null, k.Mx, k.My, k.Mz)
            | (k == (1, 0, 1) ? HullProfile.Left | HullProfile.DockedLeft : 0);
        var v = BodyMotion.Volumes(ship, ShipMask, true).Concat(BodyMotion.Volumes(station, StMask, false)).ToArray();
        var reached = Flood(v, null, SeatLocator.Locate(ship).Stand);
        foreach (var key in ship.Keys) Assert.True(ReachesModule(reached, (key.Mx, key.Mz)), $"ship module {key}");
        foreach (var key in station.Keys) Assert.True(ReachesModule(reached, (key.Mx, key.Mz)), $"station module {key} is behind the airlock");
    }
    [Fact]
    public void TheMeshAuditFindsWhatABodyOverlapsAndWhatAViewerCanSee()
    {
        // a wall (facing -x) with a body-sized box beside it and a box far away
        var wall = new List<AuditTri>
        {
            new(new(0, 0, 0), new(0, 3, 0), new(0, 3, 4), -Vector3.UnitX, false, "wall"),
            new(new(0, 0, 0), new(0, 3, 4), new(0, 0, 4), -Vector3.UnitX, false, "wall"),
        };
        var scene = new MeshAudit.Scene(wall);
        Assert.Equal(2, scene.TrianglesIn(new(-0.3f, 0.3f, 1f), new(0.3f, 1.8f, 1.6f)).Count);
        Assert.Empty(scene.TrianglesIn(new(2f, 0.3f, 1f), new(2.6f, 1.8f, 1.6f)));
        // seen from the side it faces, not from behind it
        Assert.True(scene.CanBeSeen(new(0, 1.5f, 2f), -Vector3.UnitX));
        Assert.False(scene.CanBeSeen(new(0, 1.5f, 2f), Vector3.UnitX));
    }
}
