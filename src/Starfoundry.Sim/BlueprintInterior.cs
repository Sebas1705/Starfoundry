namespace Starfoundry.Sim;

/// <summary>A one-cell machine of a station blueprint (ADR 0044): a content machine type with a recipe (-1 for none) at a local cell of the structure. Turrets, walls, unloaders, depots and crafters are all machines.</summary>
public readonly record struct BlueprintMachine(int X, int Y, int Z, int Type, int Recipe);

/// <summary>A straight belt segment of a station blueprint: its first cell, direction, length in cells and tier.</summary>
public readonly record struct BlueprintBelt(int X, int Y, int Z, Dir Dir, int Length, BeltTier Tier);

/// <summary>What a blueprint link joins: the end of one belt to the start of another (two of them off one belt make a splitter), the end of a belt to a machine, or a machine to the start of a belt.</summary>
public enum BlueprintLinkKind : byte { BeltToBelt = 0, BeltToMachine = 1, MachineToBelt = 2 }

/// <summary>One link of a blueprint. <see cref="From"/> and <see cref="To"/> index <see cref="BlueprintInterior.Belts"/> or <see cref="BlueprintInterior.Machines"/> according to <see cref="Kind"/>.</summary>
public readonly record struct BlueprintLink(BlueprintLinkKind Kind, int From, int To);

/// <summary>
/// The whole interior of a station as a blueprint carries it (ADR 0044): every machine, belt segment and link of its floors. Applying it makes the station's interior exactly this:
/// parts that match a current one (same cell and type; same cell, direction, length and tier) are kept with their state, the others are removed (half refund) or built (full price).
/// </summary>
public sealed record BlueprintInterior(BlueprintMachine[] Machines, BlueprintBelt[] Belts, BlueprintLink[] Links)
{
    /// <summary>Most machines plus belts one blueprint may carry.</summary>
    public const int MaxParts = 4096;

    /// <summary>Most links one blueprint may carry.</summary>
    public const int MaxLinks = 8192;

    public static BlueprintInterior Empty { get; } = new(Array.Empty<BlueprintMachine>(), Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());

    public bool IsWellFormed => Machines is not null && Belts is not null && Links is not null
        && Machines.Length + Belts.Length <= MaxParts && Links.Length <= MaxLinks;

    public bool Equals(BlueprintInterior? other) =>
        other is not null && Machines.AsSpan().SequenceEqual(other.Machines) && Belts.AsSpan().SequenceEqual(other.Belts) && Links.AsSpan().SequenceEqual(other.Links);

    public override int GetHashCode() => HashCode.Combine(Machines.Length, Belts.Length, Links.Length);
}

public sealed partial class BeltNetwork
{
    /// <summary>
    /// The interior of a structure as a blueprint: its machines and belt segments in id order and the links among them. <paramref name="machineIds"/> and <paramref name="segmentIds"/>
    /// give the id behind every index. A link is found by looking at the cells that touch a belt's ends, the way the game links what a player places.
    /// </summary>
    public BlueprintInterior ReadInterior(int structure, out int[] machineIds, out int[] segmentIds)
    {
        var machines = new List<BlueprintMachine>();
        var mids = new List<int>();
        var belts = new List<BlueprintBelt>();
        var sids = new List<int>();
        for (int id = 0; id < MachineIdLimit; id++)
        {
            if (!IsMachineAlive(id)) continue;
            var m = GetMachineInfo(id);
            if (m.Structure != structure) continue;
            machines.Add(new BlueprintMachine(m.X, m.Y, m.Z, m.Type, m.Recipe));
            mids.Add(id);
        }
        for (int id = 0; id < SegmentIdLimit; id++)
        {
            if (!IsAlive(id)) continue;
            var s = GetSegmentInfo(id);
            if (s.Structure != structure) continue;
            belts.Add(new BlueprintBelt(s.OriginX, s.OriginY, s.OriginZ, DirOf(s.DirX, s.DirY, s.DirZ), s.LengthCells, s.Tier));
            sids.Add(id);
        }
        machineIds = mids.ToArray();
        segmentIds = sids.ToArray();
        return new BlueprintInterior(machines.ToArray(), belts.ToArray(), ReadLinks(machines, mids, belts, sids));
    }

    public BlueprintInterior ReadInterior(int structure) => ReadInterior(structure, out _, out _);

    private static Dir DirOf(int dx, int dy, int dz) =>
        dx > 0 ? Dir.PosX : dx < 0 ? Dir.NegX : dy > 0 ? Dir.PosY : dy < 0 ? Dir.NegY : dz > 0 ? Dir.PosZ : Dir.NegZ;

    private static readonly (int, int, int)[] Around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    private BlueprintLink[] ReadLinks(List<BlueprintMachine> machines, List<int> mids, List<BlueprintBelt> belts, List<int> sids)
    {
        var machineAt = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < machines.Count; i++) machineAt[(machines[i].X, machines[i].Y, machines[i].Z)] = i;
        var beltStartAt = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < belts.Count; i++) beltStartAt[(belts[i].X, belts[i].Y, belts[i].Z)] = i;
        var links = new List<BlueprintLink>();
        for (int i = 0; i < belts.Count; i++)
        {
            var (lx, ly, lz) = LastCell(belts[i]);
            foreach (var (dx, dy, dz) in Around)
            {
                var c = (lx + dx, ly + dy, lz + dz);
                if (beltStartAt.TryGetValue(c, out int j) && j != i && IsConnected(sids[i], sids[j])) links.Add(new BlueprintLink(BlueprintLinkKind.BeltToBelt, i, j));
                if (machineAt.TryGetValue(c, out int m) && FeedsMachine(sids[i], mids[m])) links.Add(new BlueprintLink(BlueprintLinkKind.BeltToMachine, i, m));
            }
        }
        for (int m = 0; m < machines.Count; m++)
            foreach (var (dx, dy, dz) in Around)
                if (beltStartAt.TryGetValue((machines[m].X + dx, machines[m].Y + dy, machines[m].Z + dz), out int b) && IsFedByMachine(sids[b], mids[m]))
                    links.Add(new BlueprintLink(BlueprintLinkKind.MachineToBelt, m, b));
        return links.ToArray();
    }

    /// <summary>The last cell of a belt segment.</summary>
    public static (int X, int Y, int Z) LastCell(BlueprintBelt b)
    {
        var (dx, dy, dz) = Dirs.Vector(b.Dir);
        return (b.X + dx * (b.Length - 1), b.Y + dy * (b.Length - 1), b.Z + dz * (b.Length - 1));
    }
}
