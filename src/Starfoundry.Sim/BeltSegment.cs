namespace Starfoundry.Sim;

/// <summary>
/// A straight belt run. Items are stored front-to-back in a ring buffer; the front item has the
/// largest position. Positions are sub-units from the segment start.
/// </summary>
internal sealed class BeltSegment
{
    public readonly int Id;
    public readonly int Length;
    public readonly int Speed;
    public readonly BeltTier Tier;
    public readonly int Capacity;

    public readonly int OriginX, OriginY, OriginZ;
    public readonly int DirX, DirY, DirZ;

    public readonly int[] Pos;
    public readonly ushort[] Type;
    public int Head;
    public int Count;

    public readonly int[] Out = new int[2];
    public int OutCount;
    public int RoundRobin;

    /// <summary>Segments whose end feeds this one. More than one means this segment is a merge.</summary>
    public readonly List<int> Inputs = new();

    /// <summary>Index into <see cref="Inputs"/> of the input with top priority; rotates after each transfer.</summary>
    public int MergeTurn;

    /// <summary>Tick in which this segment was last processed; lets merges tell who still has to move.</summary>
    public long LastMoved = -1;

    /// <summary>Machine this segment's end feeds, or -1. Exclusive with outputs to other segments and with being a sink.</summary>
    public int MachineOut = -1;

    /// <summary>Machine whose output feeds this segment's start, or -1. Exclusive with input segments and a source.</summary>
    public int FedBy = -1;

    public bool IsSink;
    public long Consumed;

    /// <summary>Items that left this segment, by transfer or by being consumed.</summary>
    public long Passed;

    public ushort[]? SourcePattern;
    public int SourceIndex;

    public BeltSegment(int id, int x, int y, int z, int dx, int dy, int dz, int lengthCells, BeltTier tier)
    {
        if (lengthCells <= 0) throw new ArgumentOutOfRangeException(nameof(lengthCells));
        Id = id;
        OriginX = x; OriginY = y; OriginZ = z;
        DirX = dx; DirY = dy; DirZ = dz;
        Length = lengthCells * Units.CellUnits;
        Tier = tier;
        Speed = BeltTiers.SpeedPerTick(tier);
        Capacity = Length / Units.ItemSpacing + 2;
        Pos = new int[Capacity];
        Type = new ushort[Capacity];
    }

    public BeltSegment Clone()
    {
        var c = new BeltSegment(Id, OriginX, OriginY, OriginZ, DirX, DirY, DirZ, Length / Units.CellUnits, Tier);
        Array.Copy(Pos, c.Pos, Capacity);
        Array.Copy(Type, c.Type, Capacity);
        c.Head = Head;
        c.Count = Count;
        Array.Copy(Out, c.Out, Out.Length);
        c.OutCount = OutCount;
        c.RoundRobin = RoundRobin;
        c.Inputs.AddRange(Inputs);
        c.MergeTurn = MergeTurn;
        c.LastMoved = LastMoved;
        c.MachineOut = MachineOut;
        c.FedBy = FedBy;
        c.IsSink = IsSink;
        c.Consumed = Consumed;
        c.Passed = Passed;
        c.SourcePattern = (ushort[]?)SourcePattern?.Clone();
        c.SourceIndex = SourceIndex;
        return c;
    }

    public int Index(int k) => (Head + k) % Capacity;

    public int TailPos => Pos[Index(Count - 1)];

    public bool CanAccept => Count < Capacity && (Count == 0 || TailPos >= Units.ItemSpacing);

    public void PushTail(int pos, ushort type)
    {
        int i = Index(Count);
        Pos[i] = pos;
        Type[i] = type;
        Count++;
    }

    public void PopFront()
    {
        Head = (Head + 1) % Capacity;
        Count--;
    }
}
