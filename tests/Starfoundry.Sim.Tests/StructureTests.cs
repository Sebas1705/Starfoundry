using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class StructureTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Smelter = C.MachineIndex("core:smelter");

    [Fact]
    public void Same_local_cell_in_two_structures_is_independent()
    {
        var net = new BeltNetwork(C);
        int a = net.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        int b = net.CreateStructure(0, StructureKind.Station, 50_000, 0, 0);
        int ma = net.AddMachine(3, 1, 3, Smelter, -1, structure: a);
        int mb = net.AddMachine(3, 1, 3, Smelter, -1, structure: b);
        int sa = net.AddSegment(0, 0, 0, 1, 0, 0, 4, BeltTier.Mk1, structure: a);
        int sb = net.AddSegment(0, 0, 0, 1, 0, 0, 4, BeltTier.Mk1, structure: b);
        var w = new SimWorld(net);
        w.ValidateInvariants();

        Assert.Equal((SimWorld.EntityKind.Machine, ma), w.EntityAt(a, 3, 1, 3));
        Assert.Equal((SimWorld.EntityKind.Machine, mb), w.EntityAt(b, 3, 1, 3));
        Assert.Equal((SimWorld.EntityKind.Segment, sa), w.EntityAt(a, 0, 0, 0));
        Assert.Equal((SimWorld.EntityKind.Segment, sb), w.EntityAt(b, 0, 0, 0));
        Assert.Equal((SimWorld.EntityKind.None, -1), w.EntityAt(0, 3, 1, 3));
        Assert.Equal(a, net.GetMachineInfo(ma).Structure);
        Assert.Equal(b, net.GetSegmentInfo(sb).Structure);
    }

    [Fact]
    public void Structure_zero_behaves_as_before()
    {
        var net = new BeltNetwork(C);
        int m = net.AddMachine(2, 0, 2, Smelter, -1);
        int s = net.AddSegment(5, 0, 5, 1, 0, 0, 3, BeltTier.Mk1);
        var w = new SimWorld(net);
        Assert.Equal((SimWorld.EntityKind.Machine, m), w.EntityAt(2, 0, 2));
        Assert.Equal((SimWorld.EntityKind.Machine, m), w.EntityAt(0, 2, 0, 2));
        Assert.Equal((SimWorld.EntityKind.Segment, s), w.EntityAt(7, 0, 5));
        Assert.Equal(0, net.GetMachineInfo(m).Structure);
        Assert.Equal(0, net.GetSegmentInfo(s).Structure);
        Assert.Empty(net.Structures());
        // Structure 0 needs no registry entry and sits at the origin.
        Assert.Equal((2L * 240 + 120, 120L, 2L * 240 + 120), net.WorldCentre(0, 2, 0, 2));
        w.ValidateInvariants();
        // World coordinates beyond the local limit stay valid for structure 0.
        net.AddMachine(StructureGrid.LocalLimit + 100, 0, 0, Smelter, -1);
        new SimWorld(net).ValidateInvariants();
    }

    [Fact]
    public void WorldCentre_is_origin_plus_local()
    {
        var net = new BeltNetwork(C);
        int s = net.CreateStructure(1, StructureKind.Ship, 10_000, -2_400, 777);
        Assert.Equal((10_000L + 3 * 240 + 120, -2_400L + 1 * 240 + 120, 777L + -2 * 240 + 120), net.WorldCentre(s, 3, 1, -2));
        Assert.Equal(1, net.GetStructure(s).Owner);
        Assert.Equal(StructureKind.Ship, net.GetStructure(s).Kind);
    }

    [Fact]
    public void Moving_origin_changes_world_centre_not_local_cell()
    {
        var net = new BeltNetwork(C);
        int s = net.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        int m = net.AddMachine(1, 1, 1, Smelter, -1, structure: s);
        var w = new SimWorld(net);
        var before = net.WorldCentre(s, 1, 1, 1);
        net.SetStructureOrigin(s, 2_400, 480, -960);
        var after = net.WorldCentre(s, 1, 1, 1);
        Assert.Equal((before.X + 2_400, before.Y + 480, before.Z - 960), after);
        Assert.Equal((SimWorld.EntityKind.Machine, m), w.EntityAt(s, 1, 1, 1));
        var info = net.GetMachineInfo(m);
        Assert.Equal((1, 1, 1), (info.X, info.Y, info.Z));
        w.ValidateInvariants();
    }

    private static ulong WorldHash(bool withStructure)
    {
        var net = new BeltNetwork(C);
        net.AddMachine(2, 0, 2, Smelter, -1);
        net.AddSegment(5, 0, 5, 1, 0, 0, 3, BeltTier.Mk1);
        if (withStructure) net.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        return net.Checksum();
    }

    [Fact]
    public void Checksum_unchanged_without_structures()
    {
        // Structure 0 entities hash exactly as before: the structure field is only mixed when non-zero,
        // so an explicit structure: 0 is the same world as omitting it.
        Assert.Equal(WorldHash(false), WorldHash(false));
        var a = new BeltNetwork(C);
        a.AddMachine(2, 0, 2, Smelter, -1);
        a.AddSegment(5, 0, 5, 1, 0, 0, 3, BeltTier.Mk1);
        var b = new BeltNetwork(C);
        b.AddMachine(2, 0, 2, Smelter, -1, structure: 0);
        b.AddSegment(5, 0, 5, 1, 0, 0, 3, BeltTier.Mk1, structure: 0);
        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.Equal(a.Clone().Checksum(), a.Checksum());
    }

    [Fact]
    public void Checksum_changes_with_a_structure()
    {
        Assert.NotEqual(WorldHash(false), WorldHash(true));

        var net = new BeltNetwork(C);
        int s = net.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        ulong h0 = net.Checksum();
        net.SetStructureOrigin(s, 240, 0, 0);
        Assert.NotEqual(h0, net.Checksum());

        // The same cell in structure 0 and in the structure hashes differently.
        var p = new BeltNetwork(C);
        p.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        p.AddMachine(1, 1, 1, Smelter, -1);
        var q = new BeltNetwork(C);
        q.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        q.AddMachine(1, 1, 1, Smelter, -1, structure: 1);
        Assert.NotEqual(p.Checksum(), q.Checksum());
    }

    [Fact]
    public void Clone_and_rollback_keep_structure_occupancy()
    {
        var net = new BeltNetwork(C);
        int s = net.CreateStructure(2, StructureKind.Station, 4_800, 0, 0);
        net.AddMachine(4, 2, 4, Smelter, -1, structure: s);
        net.AddSegment(0, 0, 0, 0, 0, 1, 3, BeltTier.Mk1, structure: s);
        var clone = net.Clone();
        Assert.Equal(net.Checksum(), clone.Checksum());
        Assert.Equal(net.WorldCentre(s, 1, 2, 3), clone.WorldCentre(s, 1, 2, 3));
        Assert.Equal(2, clone.GetStructure(s).Owner);
        Assert.Equal(s, clone.GetMachineInfo(0).Structure);

        // A failing batch rolls back and the structure cells stay as they were.
        var w = new SimWorld(clone);
        w.Submit(0, new Batch(new Command[]
        {
            new PlaceSegment(0, 0, 40, Dir.PosX, 2, BeltTier.Mk1),
            new PlaceSegment(0, 0, 40, Dir.PosX, 2, BeltTier.Mk1), // overlaps the first: the batch rolls back
        }));
        w.Step();
        Assert.NotEqual(Reject.None, w.LastResults.Single().Reject);
        w.ValidateInvariants();
        Assert.Equal((SimWorld.EntityKind.Machine, 0), w.EntityAt(s, 4, 2, 4));
        Assert.Equal((SimWorld.EntityKind.Segment, 0), w.EntityAt(s, 0, 0, 2));
        Assert.Equal((SimWorld.EntityKind.None, -1), w.EntityAt(0, 0, 0, 40));
    }

    [Fact]
    public void Invariants_detect_a_machine_in_a_missing_structure()
    {
        var net = new BeltNetwork(C);
        net.AddMachine(0, 0, 0, Smelter, -1, structure: 7);
        Assert.Throws<InvalidOperationException>(() => net.ValidateInvariants());

        var net2 = new BeltNetwork(C);
        net2.AddSegment(0, 0, 0, 1, 0, 0, 2, BeltTier.Mk1, structure: 3);
        Assert.Throws<InvalidOperationException>(() => net2.ValidateInvariants());

        var ok = new BeltNetwork(C);
        int s = ok.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        ok.AddMachine(0, 0, 0, Smelter, -1, structure: s);
        ok.ValidateInvariants();
    }

    [Fact]
    public void Local_coordinates_beyond_the_limit_rejected()
    {
        var net = new BeltNetwork(C);
        int s = net.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        int lim = StructureGrid.LocalLimit;
        net.AddMachine(lim, -lim, lim, Smelter, -1, structure: s); // the edge itself is fine
        Assert.Throws<ArgumentOutOfRangeException>(() => net.AddMachine(lim + 1, 0, 0, Smelter, -1, structure: s));
        Assert.Throws<ArgumentOutOfRangeException>(() => net.AddMachine(0, -lim - 1, 0, Smelter, -1, structure: s));
        Assert.Throws<ArgumentOutOfRangeException>(() => net.AddSegment(lim - 1, 0, 0, 1, 0, 0, 3, BeltTier.Mk1, structure: s));
        Assert.Throws<ArgumentOutOfRangeException>(() => net.AddSegment(0, 0, -lim, 0, 0, -1, 2, BeltTier.Mk1, structure: s));
        net.AddSegment(lim - 2, 0, 0, 1, 0, 0, 3, BeltTier.Mk1, structure: s); // ends exactly on the limit
        new SimWorld(net).ValidateInvariants();
        Assert.Equal(2, net.MachineCount + net.SegmentCount); // rejected adds left nothing behind
    }
}
