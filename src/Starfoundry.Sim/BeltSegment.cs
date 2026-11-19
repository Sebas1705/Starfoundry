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

    public bool IsSink;
    public long Consumed;

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
