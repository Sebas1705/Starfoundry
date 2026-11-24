using System.Numerics;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The walking body against the drawn shell of a module: the chamfer between floor and wall (and wall and roof), the nose and tail tapers,
/// bulkheads, door frames and station walls. The shell numbers are shared with the model builder through <see cref="HullProfile"/>.
/// </summary>
public class HullProfileTests
{
    private static BodyBox[] One(ModuleKind kind, bool ship, int mask = 0) =>
        BodyMotion.Volumes(new[] { new KeyValuePair<(int, int, int), ModuleKind>((0, 0, 0), kind) }, _ => mask, ship);

    private static BodyBox[] Row(ModuleKind kind, bool ship, int count) =>
        BodyMotion.Volumes(Enumerable.Range(0, count).Select(i => new KeyValuePair<(int, int, int), ModuleKind>((0, 0, i), kind)),
            k => (k.Mz > 0 ? HullProfile.Front : 0) | (k.Mz < count - 1 ? HullProfile.Rear : 0), ship);

    private static Vector3 Walk(Vector3 from, Vector3 wish, BodyBox[] v, float seconds, float speed = BodyMotion.WalkSpeed)
    {
        var p = from;
        for (float t = 0; t < seconds; t += 1f / 60f) p = BodyMotion.Step(p, wish, speed, 1f / 60f, v);
        return p;
    }

    /// <summary>x of the liner chamfer on the left at the height of the soles, for a full ship section.</summary>
    private static float LeftChamferX(float roofY, float y)
    {
        var l = HullProfile.Liner(HullProfile.Octagon(roofY, 1f));
        var a = l[7]; var b = l[0]; // the lower left chamfer runs from the left wall down to the bottom edge
        return a.X + (b.X - a.X) * (a.Y - y) / (a.Y - b.Y);
    }

    [Fact]
    public void TheOctagonHasTheDrawnChamfer()
    {
        var o = HullProfile.Octagon(HullProfile.Roof, 1f);
        Assert.Equal(8, o.Length);
        Assert.Equal(HullProfile.MaxChamfer, o[0].X - o[7].X, 4);          // lower left chamfer: one cell in
        Assert.Equal(HullProfile.MaxChamfer, o[7].Y - o[0].Y, 4);          // and one cell up
        var low = HullProfile.Octagon(HullProfile.LowRoof, 1f);
        Assert.Equal((HullProfile.LowRoof - HullProfile.FloorY) * HullProfile.ChamferShare, low[0].X - low[7].X, 4);
        var squared = HullProfile.Octagon(HullProfile.Roof, 1f, HullProfile.Left | HullProfile.Down);
        Assert.Equal(squared[0].X, squared[7].X, 4); // a side that touches another module is squared off
    }

    [Fact]
    public void TheNoseRunsFromTheTipToTheFullSection()
    {
        Assert.Equal(HullProfile.NoseTipScale, HullProfile.NoseScale(HullProfile.NoseStart), 4);
        Assert.Equal(1f, HullProfile.NoseScale(HullProfile.NoseEnd), 4);
    }

    [Fact]
    public void WalkingIntoTheFloorChamferStopsOnTheDrawnBevel()
    {
        var v = One(ModuleKind.Hull, true);
        var p = Walk(new Vector3(4, 0, 4), new Vector3(-1, 0, 0), v, 3f);
        float wall = LeftChamferX(HullProfile.Roof, HullProfile.DeckTop);
        Assert.InRange(p.X - BodyMotion.Radius - wall, 0f, BodyMotion.MaxSubStep + 0.01f); // the corner of the body rests on the bevel, not past it
        Assert.True(wall > 0.9f, "the bevel must matter: it is a full cell wide at the floor");
        var q = Walk(new Vector3(4, 0, 4), new Vector3(1, 0, 0), v, 3f);
        Assert.Equal(8f - p.X, q.X, 1); // symmetrical on the right
    }

    [Fact]
    public void TheBodyGlidesAlongTheBevelWithoutSticking()
    {
        var v = One(ModuleKind.Hull, true);
        var against = Walk(new Vector3(4, 0, 1), new Vector3(-1, 0, 0), v, 3f);
        var p = against;
        for (int i = 0; i < 120; i++)
        {
            var next = BodyMotion.Step(p, new Vector3(-1, 0, 1), BodyMotion.WalkSpeed, 1f / 60f, v); // pushing into the bevel while moving along it
            Assert.True(next.Z >= p.Z, "never moves back");
            Assert.True(BodyMotion.Contains(next, v));
            p = next;
        }
        Assert.True(p.Z > against.Z + 3f, $"slid along the bevel: {against} -> {p}");
        Assert.Equal(against.X, p.X, 1); // and stayed against it
    }

    [Fact]
    public void ThePilotCannotStandInTheBevel()
    {
        var v = One(ModuleKind.Hull, true);
        Assert.False(BodyMotion.Contains(new Vector3(0.8f, 0, 4), v));
        Assert.False(BodyMotion.Contains(new Vector3(1.2f, 0, 4), v)); // the corner at 0.9 is inside the chamfer (the bevel is at about 1.1)
        Assert.True(BodyMotion.Contains(new Vector3(1.6f, 0, 4), v));
        Assert.True(BodyMotion.Contains(new Vector3(4, 0, 4), v));
    }

    [Fact]
    public void ABevelNextToAnotherModuleIsSquaredOff()
    {
        var open = One(ModuleKind.Hull, true, HullProfile.Left);
        Assert.True(BodyMotion.Contains(new Vector3(0.4f, 0, 4), open)); // no wall and no bevel on a side that touches a module
        var closed = One(ModuleKind.Hull, true);
        Assert.False(BodyMotion.Contains(new Vector3(0.4f, 0, 4), closed));
    }

    [Fact]
    public void TheCeilingBevelClipsTheHeadUnderALowRoof()
    {
        // A ship reactor has the full roof like the rest of the hull (so the joints line up) and its vessel is solid; a core module is lower (3.0): at the head
        // height (1.8) the roof chamfer of a ship section would start well above, so only the floor chamfer matters.
        var v = One(ModuleKind.Reactor, true);
        Assert.Equal(HullProfile.Roof, HullProfile.RoofOf(ModuleKind.Reactor, true));
        Assert.False(BodyMotion.Contains(new Vector3(4, 0, 4), v), "the containment vessel stands in the middle");
        Assert.True(BodyMotion.Contains(new Vector3(1.9f, 0, 4), v));
        var low = One(ModuleKind.Hull, true);
        var p = Walk(new Vector3(4, 0, 4), new Vector3(-1, 0, 0), low, 3f);
        Assert.InRange(p.X - BodyMotion.Radius - LeftChamferX(HullProfile.Roof, HullProfile.DeckTop), 0f, BodyMotion.MaxSubStep + 0.01f);
        // The low modules (shield, laser, gun) have a roof under the head: the body cannot stand in them.
        foreach (var kind in new[] { ModuleKind.Shield, ModuleKind.MiningLaser, ModuleKind.MiningLaserMk2, ModuleKind.Gun, ModuleKind.GunMk2 })
            foreach (bool ship in new[] { true, false })
                Assert.False(BodyMotion.Contains(new Vector3(4, 0, 4), One(kind, ship)), $"{kind} ship={ship}");
    }

    [Fact]
    public void TheNoseFunnelStopsTheBodyWhereTheHeadMeetsTheRoof()
    {
        var v = One(ModuleKind.Cockpit, true);
        // the console and the seat are solid: straight ahead from behind the seat the body is stopped by the seat
        var blocked = Walk(new Vector3(4, 0, 6.6f), new Vector3(0, 0, -1), v, 6f);
        Assert.InRange(blocked.Z, HullProfile.SeatZ1, 6.6f);
        // up the aisle beside the console the nose funnel stops the body where the head meets the roof, and pushing aside into the slanted wall slides along it
        var p = Walk(new Vector3(1.7f, 0, 6.6f), new Vector3(0, 0, -1), v, 6f);
        Assert.InRange(p.Z, 0.5f, 3.6f);
        Assert.True(BodyMotion.Contains(p, v));
        var q = Walk(new Vector3(1.7f, 0, 6.6f), new Vector3(-0.5f, 0, -1), v, 6f);
        Assert.True(BodyMotion.Contains(q, v));
        Assert.True(q.Z < 4f, $"went forward along the slanted wall: {q}");
        var withNeighbour = One(ModuleKind.Cockpit, true, HullProfile.Front);
        Assert.True(BodyMotion.Contains(new Vector3(4, 0, 0.5f), withNeighbour)); // a cockpit with a module in front has no nose
    }

    [Fact]
    public void TheEndsOfAShipAreClosedByBulkheadsAndTheTail()
    {
        var v = One(ModuleKind.Cargo, true);
        var back = Walk(new Vector3(4, 0, 4), new Vector3(0, 0, 1), v, 4f);
        Assert.InRange(back.Z, 8f - HullProfile.NoseStart - HullProfile.LinerInset - BodyMotion.Radius - 0.25f, 8f - HullProfile.NoseStart - HullProfile.LinerInset - BodyMotion.Radius);
        var tail = Walk(new Vector3(4, 0, 4), new Vector3(0, 0, 1), One(ModuleKind.Engine, true), 4f);
        Assert.InRange(tail.Z, 5.0f, HullProfile.TailEnd - HullProfile.LinerInset - BodyMotion.Radius);
        var hangar = Walk(new Vector3(4, 0, 4), new Vector3(0, 0, 1), One(ModuleKind.DroneBay, true), 4f);
        Assert.True(hangar.Z > 7.5f, "the drone bay has an open rear");
    }

    [Fact]
    public void ModulesJoinedInARowAreWalkedThroughTheDoorway()
    {
        var v = Row(ModuleKind.Hull, true, 3);
        var p = Walk(new Vector3(4, 0, 1), new Vector3(0, 0, 1), v, 8f);
        Assert.True(p.Z > 20f, $"walks the whole row: {p}"); // the bevels do not catch the body where modules meet
        // the door frame at the joint is wider than the body only in the middle: beside it the body is held
        var sideways = Walk(new Vector3(1.6f, 0, 6f), new Vector3(0, 0, 1), v, 3f);
        Assert.True(sideways.Z < 8f, $"stopped by the frame: {sideways}");
    }

    [Fact]
    public void AStationBoxHasThickWallsAndADoorway()
    {
        var closed = One(ModuleKind.Core, false);
        var p = Walk(new Vector3(4, 0, 4), new Vector3(-1, 0, 0), closed, 4f);
        Assert.InRange(p.X - BodyMotion.Radius, HullProfile.StationFace + HullProfile.StationWall, HullProfile.StationFace + HullProfile.StationWall + BodyMotion.MaxSubStep);
        var row = Row(ModuleKind.Core, false, 2);
        Assert.True(Walk(new Vector3(4, 0, 4), new Vector3(0, 0, 1), row, 4f).Z > 9f);
        Assert.True(Walk(new Vector3(1.5f, 0, 4), new Vector3(0, 0, 1), row, 4f).Z < 8f); // beside the doorway the wall holds
    }

    [Fact]
    public void ADockedAirlockIsNarrowerThanTheBodyAllowsBesideIt()
    {
        var ship = BodyMotion.Volumes(new[] { new KeyValuePair<(int, int, int), ModuleKind>((0, 0, 0), ModuleKind.Hull) },
            _ => HullProfile.Right | HullProfile.DockedRight, true);
        Assert.True(Walk(new Vector3(4, 0, 4), new Vector3(1, 0, 0), ship, 4f).X > 7.5f); // through the airlock, no wall in the way
        Assert.True(Walk(new Vector3(4, 0, 1.8f), new Vector3(1, 0, 0), ship, 4f).X < 7.5f); // beside the airlock frame: held by the hull
    }

    [Fact]
    public void EveryKindHasAShellAndTheStandingSpotFits()
    {
        foreach (ModuleKind kind in Enum.GetValues<ModuleKind>())
            foreach (bool ship in new[] { true, false })
            {
                var shell = HullProfile.ShellOf(kind, ship, 0);
                Assert.Equal(HullProfile.RoofOf(kind, ship), shell.RoofY);
                var v = One(kind, ship);
                bool tall = shell.RoofY - HullProfile.LinerInset > HullProfile.BodyHeight + 0.3f;
                if (!tall) continue; // the low modules are not walkable (checked above)
                var spot = BodyMotion.Nearest(new Vector3(4, 0, 4), v);
                Assert.True(BodyMotion.Contains(spot, v), $"{kind} ship={ship} spot {spot}");
                // every neighbour combination keeps a spot to stand on, and a random walk never leaves the shell
                var rng = new Random(7);
                for (int mask = 0; mask < 64; mask += 3)
                {
                    var vm = One(kind, ship, mask);
                    var p = BodyMotion.Nearest(new Vector3(4, 0, 4), vm);
                    Assert.True(BodyMotion.Contains(p, vm), $"{kind} ship={ship} mask={mask}");
                    for (int i = 0; i < 600; i++)
                    {
                        p = BodyMotion.Step(p, new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f), BodyMotion.RunSpeed, 1f / 30f, vm);
                        Assert.True(BodyMotion.Contains(p, vm), $"{kind} ship={ship} mask={mask} step {i}: {p}");
                    }
                }
            }
    }

    [Fact]
    public void ACornerOfTheBodyNeverEntersAnyBevelAcrossKindsAndSections()
    {
        // Brute force over the floor of every ship kind: a point is walkable only when all four corners of the body are inside the liner section.
        foreach (ModuleKind kind in new[] { ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.Core, ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Defence, ModuleKind.Dock, ModuleKind.Shipyard })
        {
            var v = One(kind, true);
            var shell = HullProfile.ShellOf(kind, true, 0);
            for (float x = 0.5f; x < 7.6f; x += 0.1f)
                for (float z = 0.5f; z < 7.6f; z += 0.1f)
                {
                    if (!BodyMotion.Contains(new Vector3(x, 0, z), v)) continue;
                    foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    {
                        float cx = x + dx * BodyMotion.Radius, cz = z + dz * BodyMotion.Radius;
                        var liner = HullProfile.SectionAt(shell, cz);
                        Assert.NotNull(liner);
                        Assert.True(HullProfile.InsideSection(liner!, 0, cx, HullProfile.DeckTop), $"{kind} feet corner {cx},{cz}");
                        Assert.True(HullProfile.InsideSection(liner!, 0, cx, HullProfile.BodyHeight), $"{kind} head corner {cx},{cz}");
                    }
                }
        }
    }
}
