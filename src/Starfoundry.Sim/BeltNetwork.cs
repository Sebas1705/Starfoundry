namespace Starfoundry.Sim;

/// <summary>One item on a belt, for rendering and tests.</summary>
public readonly record struct ItemSample(int Segment, int Pos, ushort Type);

/// <summary>Read-only geometry of a segment, for rendering.</summary>
public readonly record struct SegmentInfo(
    int Id, int OriginX, int OriginY, int OriginZ, int DirX, int DirY, int DirZ,
    int LengthCells, int Length, int Speed, BeltTier Tier);

/// <summary>
/// A network of belt segments with sources and sinks. Fully deterministic: fixed tick, integer
/// state, stable processing order. Engine-independent (no Godot references).
/// </summary>
public sealed class BeltNetwork
{
    private readonly List<BeltSegment> _segs = new();
    private int[] _order = Array.Empty<int>();
    private bool _dirty = true;

    public long Tick { get; private set; }
    public long Spawned { get; private set; }
    public long Consumed { get; private set; }

    public int SegmentCount => _segs.Count;

    public int InTransit
    {
        get
        {
            int n = 0;
            foreach (var s in _segs) n += s.Count;
            return n;
        }
    }

    public int AddSegment(int x, int y, int z, int dx, int dy, int dz, int lengthCells, BeltTier tier)
    {
        int id = _segs.Count;
        _segs.Add(new BeltSegment(id, x, y, z, dx, dy, dz, lengthCells, tier));
        _dirty = true;
        return id;
    }

    /// <summary>Connects the end of <paramref name="from"/> to the start of <paramref name="to"/>. A segment may have up to two outputs (a splitter).</summary>
    public void Connect(int from, int to)
    {
        var s = Get(from);
        Get(to);
        if (s.IsSink) throw new InvalidOperationException("A sink cannot have outputs.");
        if (s.OutCount >= 2) throw new InvalidOperationException("A segment supports at most two outputs.");
        if (s.OutCount == 1 && s.Out[0] == to) throw new InvalidOperationException("Both outputs cannot target the same segment.");
        s.Out[s.OutCount++] = to;
        _segs[to].Inputs.Add(from);
        _dirty = true;
    }

    /// <summary>Items reaching the end of this segment are consumed.</summary>
    public void SetSink(int segment)
    {
        var s = Get(segment);
        if (s.OutCount > 0) throw new InvalidOperationException("A segment with outputs cannot be a sink.");
        s.IsSink = true;
    }

    /// <summary>Feeds the segment start whenever there is room, cycling through the item types.</summary>
    public void SetSource(int segment, ReadOnlySpan<ushort> pattern)
    {
        if (pattern.Length == 0) throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        Get(segment).SourcePattern = pattern.ToArray();
    }

    /// <summary>Advances the simulation by one tick.</summary>
    public void Step()
    {
        if (_dirty) RebuildOrder();

        foreach (int id in _order) MoveSegment(_segs[id]);

        foreach (var s in _segs)
        {
            if (s.SourcePattern is null || !s.CanAccept) continue;
            // Insert exactly one spacing behind the tail so a saturated belt stays perfectly packed.
            int pos = s.Count == 0 ? 0 : s.TailPos - Units.ItemSpacing;
            s.PushTail(pos, s.SourcePattern[s.SourceIndex]);
            s.SourceIndex = (s.SourceIndex + 1) % s.SourcePattern.Length;
            Spawned++;
        }

        Tick++;
    }

    public void Step(int ticks)
    {
        for (int i = 0; i < ticks; i++) Step();
    }

    public long ConsumedBy(int segment) => Get(segment).Consumed;

    /// <summary>Items that have left the segment (handed on or consumed). Useful to check merge fairness.</summary>
    public long PassedBy(int segment) => Get(segment).Passed;

    public int CountOn(int segment) => Get(segment).Count;

    public SegmentInfo GetSegmentInfo(int id)
    {
        var s = Get(id);
        return new SegmentInfo(s.Id, s.OriginX, s.OriginY, s.OriginZ, s.DirX, s.DirY, s.DirZ,
            s.Length / Units.CellUnits, s.Length, s.Speed, s.Tier);
    }

    /// <summary>Copies every item into <paramref name="dst"/> and returns how many were written.</summary>
    public int FillSamples(Span<ItemSample> dst)
    {
        int n = 0;
        foreach (var s in _segs)
        {
            for (int k = 0; k < s.Count; k++)
            {
                if (n >= dst.Length) return n;
                int i = s.Index(k);
                dst[n++] = new ItemSample(s.Id, s.Pos[i], s.Type[i]);
            }
        }
        return n;
    }

    /// <summary>FNV-1a hash of the whole state. Equal checksums mean equal states.</summary>
    public ulong Checksum()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int b = 0; b < 8; b++)
            {
                h ^= (byte)(v >> (b * 8));
                h *= 1099511628211UL;
            }
        }

        Mix(Tick); Mix(Spawned); Mix(Consumed);
        foreach (var s in _segs)
        {
            Mix(s.Count); Mix(s.RoundRobin); Mix(s.SourceIndex); Mix(s.MergeTurn);
            for (int k = 0; k < s.Count; k++)
            {
                int i = s.Index(k);
                Mix(s.Pos[i]); Mix(s.Type[i]);
            }
        }
        return h;
    }

    private BeltSegment Get(int id)
    {
        if ((uint)id >= (uint)_segs.Count) throw new ArgumentOutOfRangeException(nameof(id));
        return _segs[id];
    }

    /// <summary>Orders segments downstream-first so space freed this tick is visible upstream. Cycles are broken by id.</summary>
    private void RebuildOrder()
    {
        int n = _segs.Count;
        var visited = new bool[n];
        var order = new List<int>(n);
        var stack = new Stack<(int Id, int Next)>();

        for (int r = 0; r < n; r++)
        {
            if (visited[r]) continue;
            visited[r] = true;
            stack.Push((r, 0));
            while (stack.Count > 0)
            {
                var (id, next) = stack.Pop();
                var s = _segs[id];
                if (next < s.OutCount)
                {
                    stack.Push((id, next + 1));
                    int o = s.Out[next];
                    if (!visited[o])
                    {
                        visited[o] = true;
                        stack.Push((o, 0));
                    }
                }
                else
                {
                    order.Add(id);
                }
            }
        }

        _order = order.ToArray();
        _dirty = false;
    }

    private void MoveSegment(BeltSegment s)
    {
        s.LastMoved = Tick;
        if (s.Count == 0) return;

        int front = s.Head;
        bool frontKept;
        int fp = s.Pos[front] + s.Speed;
        if (fp >= s.Length)
        {
            int overflow = fp - s.Length;
            if (s.IsSink)
            {
                s.PopFront();
                s.Consumed++;
                s.Passed++;
                Consumed++;
                frontKept = false;
            }
            else if (TryTransfer(s, overflow))
            {
                frontKept = false;
            }
            else
            {
                s.Pos[front] = s.Length;
                frontKept = true;
            }
        }
        else
        {
            s.Pos[front] = fp;
            frontKept = true;
        }

        int limit = s.Length;
        int k = 0;
        if (frontKept)
        {
            limit = s.Pos[s.Head] - Units.ItemSpacing;
            k = 1;
        }

        for (; k < s.Count; k++)
        {
            int i = s.Index(k);
            int np = s.Pos[i] + s.Speed;
            if (np > limit) np = limit;
            s.Pos[i] = np;
            limit = np - Units.ItemSpacing;
        }
    }

    /// <summary>Moves the front item of <paramref name="s"/> into one of its outputs, round-robin. Returns false when every output is full.</summary>
    private bool TryTransfer(BeltSegment s, int overflow)
    {
        for (int t = 0; t < s.OutCount; t++)
        {
            int slot = (s.RoundRobin + t) % s.OutCount;
            var target = _segs[s.Out[slot]];
            if (!target.CanAccept) continue;
            if (target.Inputs.Count > 1 && HigherPriorityInputWaiting(s, target)) continue;

            int pos = overflow;
            if (target.Count > 0)
            {
                int packed = target.TailPos - Units.ItemSpacing;
                // At a merge the arrival phase of each input differs, which would leave a gap behind
                // the tail every time. Let a waiting item close it, by at most one tick of travel, so
                // a saturated merge still fills the output belt.
                pos = target.Inputs.Count > 1 ? Math.Min(overflow + target.Speed, packed) : Math.Min(overflow, packed);
            }
            pos = Math.Clamp(pos, 0, target.Length);

            target.PushTail(pos, s.Type[s.Head]);
            s.PopFront();
            s.Passed++;
            s.RoundRobin = (slot + 1) % s.OutCount;
            if (target.Inputs.Count > 1)
                target.MergeTurn = (target.Inputs.IndexOf(s.Id) + 1) % target.Inputs.Count;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Fair merge: inputs take turns in round-robin order. An input yields only to a higher-priority
    /// input that still has to move this tick and has an item at its end aimed at the same target.
    /// An input with nothing to send never blocks the others, so the split is max-min fair.
    /// </summary>
    private bool HigherPriorityInputWaiting(BeltSegment s, BeltSegment target)
    {
        int n = target.Inputs.Count;
        int mine = (target.Inputs.IndexOf(s.Id) - target.MergeTurn + n) % n;
        for (int r = 0; r < mine; r++)
        {
            var other = _segs[target.Inputs[(target.MergeTurn + r) % n]];
            if (other.LastMoved == Tick) continue; // already processed: it either went through or could not
            if (other.Count > 0 && other.Pos[other.Head] + other.Speed >= other.Length
                && other.OutCount > 0 && other.Out[other.RoundRobin % other.OutCount] == target.Id)
                return true;
        }
        return false;
    }
}
