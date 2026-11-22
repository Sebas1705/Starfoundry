namespace Starfoundry.Sim;

/// <summary>One item on a belt, for rendering and tests.</summary>
public readonly record struct ItemSample(int Segment, int Pos, ushort Type);

/// <summary>Read-only geometry of a segment, for rendering.</summary>
public readonly record struct SegmentInfo(
    int Id, int OriginX, int OriginY, int OriginZ, int DirX, int DirY, int DirZ,
    int LengthCells, int Length, int Speed, BeltTier Tier, int Structure = 0);

/// <summary>
/// A network of belt segments with sources and sinks. Fully deterministic: fixed tick, integer
/// state, stable processing order. Engine-independent (no Godot references).
/// Segment ids are stable and never reused; a removed segment leaves a hole.
/// Gameplay code should change the network through <see cref="SimWorld"/> commands, not directly.
/// </summary>
public sealed partial class BeltNetwork
{
    private List<BeltSegment?> _segs = new();
    private int _alive;
    private int[] _order = Array.Empty<int>();
    private bool _dirty = true;

    /// <summary>Game content (items, machines, recipes). Needed only when machines are used.</summary>
    public ContentDb? Content { get; }

    /// <summary>Sandbox: free building, nothing locked. Campaign: building costs items and research gates content.</summary>
    public GameMode Mode { get; }

    /// <summary>Whether the Remnant threat is on. Needs content with threat rules and a galaxy. Part of the world's creation settings.</summary>
    public bool ThreatsEnabled { get; }

    /// <summary>Whether building is limited to the player's station: machines and belts only inside modules, no drills,
    /// and the cargo port is available without research. Part of the world's creation settings.</summary>
    public bool StationRules { get; }

    /// <summary>Whether the structure rules are on: building happens inside ships and stations (structure commands),
    /// and placing into the legacy world (structure 0) is refused. Part of the world's creation settings.</summary>
    public bool StructureRules { get; }

    /// <summary>The world's rules (difficulty percentages and flags). Defaults equal the game before the rules existed.</summary>
    public WorldRules Rules { get; }

    /// <summary>Starting-item units that did not fit the starter hold and were left out at creation.</summary>
    public int StartingItemsClamped { get; internal set; }

    public BeltNetwork(ContentDb? content = null, GameMode mode = GameMode.Sandbox, bool threats = false, bool stationRules = false,
        bool structures = false, WorldRules? rules = null)
    {
        Rules = rules ?? WorldRules.Default;
        if (Rules.Validate() is { } problem) throw new ArgumentException("Invalid world rules: " + problem, nameof(rules));
        StructureRules = structures;
        Content = content;
        Mode = mode;
        ThreatsEnabled = threats;
        StationRules = stationRules;
        InitResearch();
    }

    public long Tick { get; private set; }
    public long Spawned { get; private set; }
    public long Consumed { get; private set; }

    /// <summary>Items destroyed because their segment was removed.</summary>
    public long Destroyed { get; private set; }

    /// <summary>Changes whenever segments are added, removed or rewired, so renderers know when to rebuild.</summary>
    public int StructureVersion { get; private set; }

    /// <summary>Number of segments that exist.</summary>
    public int SegmentCount => _alive;

    /// <summary>One past the highest id ever issued. Valid ids are below this and satisfy <see cref="IsAlive"/>.</summary>
    public int SegmentIdLimit => _segs.Count;

    public bool IsAlive(int id) => (uint)id < (uint)_segs.Count && _segs[id] is not null;

    public int InTransit
    {
        get
        {
            int n = 0;
            foreach (var s in _segs) if (s is not null) n += s.Count;
            return n;
        }
    }

    public int AddSegment(int x, int y, int z, int dx, int dy, int dz, int lengthCells, BeltTier tier, int structure = 0)
    {
        CheckLocal(structure, x, y, z);
        if (lengthCells > 0) CheckLocal(structure, x + (long)dx * (lengthCells - 1), y + (long)dy * (lengthCells - 1), z + (long)dz * (lengthCells - 1));
        int id = _segs.Count;
        _segs.Add(new BeltSegment(id, x, y, z, dx, dy, dz, lengthCells, tier, structure));
        _alive++;
        _dirty = true;
        StructureVersion++;
        return id;
    }

    /// <summary>Removes a segment, destroying the items on it and detaching it from its neighbours.</summary>
    public void RemoveSegment(int id)
    {
        var s = Get(id);
        Destroyed += s.Count;

        if (s.MachineOut >= 0) _machines[s.MachineOut]!.InputsFrom.Remove(id);
        if (s.FedBy >= 0) _machines[s.FedBy]!.DropOutBelt(id);

        foreach (int input in s.Inputs)
        {
            if (input != id) DropOutput(_segs[input]!, id);
        }
        for (int k = 0; k < s.OutCount; k++)
        {
            int o = s.Out[k];
            if (o == id) continue;
            var t = _segs[o]!;
            t.Inputs.Remove(id);
            FixMergeTurn(t);
        }

        _segs[id] = null;
        _alive--;
        _dirty = true;
        StructureVersion++;
    }

    /// <summary>Connects the end of <paramref name="from"/> to the start of <paramref name="to"/>. A segment may have up to two outputs (a splitter).</summary>
    public void Connect(int from, int to)
    {
        var s = Get(from);
        Get(to);
        if (s.IsSink) throw new InvalidOperationException("A sink cannot have outputs.");
        if (s.MachineOut >= 0) throw new InvalidOperationException("A segment feeding a machine cannot have other outputs.");
        if (_segs[to]!.FedBy >= 0) throw new InvalidOperationException("A segment fed by a machine cannot have input segments.");
        if (s.OutCount >= 2) throw new InvalidOperationException("A segment supports at most two outputs.");
        if (s.OutCount == 1 && s.Out[0] == to) throw new InvalidOperationException("Both outputs cannot target the same segment.");
        s.Out[s.OutCount++] = to;
        _segs[to]!.Inputs.Add(from);
        _dirty = true;
        StructureVersion++;
    }

    /// <summary>Removes the connection <paramref name="from"/> to <paramref name="to"/>. Items stay where they are.</summary>
    public void Disconnect(int from, int to)
    {
        var s = Get(from);
        var t = Get(to);
        if (!IsConnected(from, to)) throw new InvalidOperationException("These segments are not connected.");
        DropOutput(s, to);
        t.Inputs.Remove(from);
        FixMergeTurn(t);
        _dirty = true;
        StructureVersion++;
    }

    public bool IsConnected(int from, int to)
    {
        var s = Get(from);
        Get(to);
        for (int k = 0; k < s.OutCount; k++) if (s.Out[k] == to) return true;
        return false;
    }

    public int OutputCount(int segment) => Get(segment).OutCount;

    public bool IsSink(int segment) => Get(segment).IsSink;

    public bool HasSource(int segment) => Get(segment).SourcePattern is not null;

    /// <summary>Items reaching the end of this segment are consumed.</summary>
    public void SetSink(int segment)
    {
        var s = Get(segment);
        if (s.OutCount > 0 || s.MachineOut >= 0) throw new InvalidOperationException("A segment with outputs cannot be a sink.");
        s.IsSink = true;
    }

    public void ClearSink(int segment) => Get(segment).IsSink = false;

    /// <summary>Feeds the segment start whenever there is room, cycling through the item types.</summary>
    public void SetSource(int segment, ReadOnlySpan<ushort> pattern)
    {
        if (pattern.Length == 0) throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        var s = Get(segment);
        if (s.FedBy >= 0) throw new InvalidOperationException("A segment fed by a machine cannot have a source.");
        s.SourcePattern = pattern.ToArray();
        s.SourceIndex = 0;
    }

    public void ClearSource(int segment)
    {
        var s = Get(segment);
        s.SourcePattern = null;
        s.SourceIndex = 0;
    }

    /// <summary>Advances the simulation by one tick.</summary>
    public void Step()
    {
        if (_dirty) RebuildOrder();

        StepPower(); // before anything works: reactors burn, satisfaction and work credits are set for this tick
        RecheckDrillLinks(); // a structure moved from outside the tick (setup, tests) drops out-of-range links before drills work
        foreach (int id in _order) MoveSegment(_segs[id]!);

        StepMachines();
        StepUnloaders();
        StepPlayers();
        StepDrones();
        StepSensors();
        RecheckDrillLinks(); // structure moves are applied here, before the threat phase
        StepResearch();
        StepThreat();

        foreach (var s in _segs)
        {
            if (s?.SourcePattern is null || !s.CanAccept) continue;
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
            s.Length / Units.CellUnits, s.Length, s.Speed, s.Tier, s.Structure);
    }

    /// <summary>Copies every item into <paramref name="dst"/> and returns how many were written.</summary>
    public int FillSamples(Span<ItemSample> dst)
    {
        int n = 0;
        foreach (var s in _segs)
        {
            if (s is null) continue;
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

        Mix(Tick); Mix(Spawned); Mix(Consumed); Mix(Destroyed); Mix(_segs.Count);
        Mix(MachineInputsConsumed); Mix(MachineOutputsCreated); Mix((long)(Content?.Hash ?? 0)); Mix((int)Mode); Mix(ThreatsEnabled ? 1 : 0); if (StationRules) Mix(2); if (StructureRules) Mix(4);
        if (!Rules.IsDefault) foreach (byte rb in Rules.Serialize()) Mix(rb); // default rules hash as before they existed
        MixMachines(Mix);
        MixEconomy(Mix);
        MixStation(Mix);
        MixStructures(Mix);
        foreach (var s in _segs)
        {
            if (s is null) { Mix(-1); continue; }
            Mix(s.Id);
            Mix(s.OriginX); Mix(s.OriginY); Mix(s.OriginZ);
            Mix(s.DirX); Mix(s.DirY); Mix(s.DirZ);
            if (s.Structure != 0) Mix(s.Structure); // mixed only when non-zero so worlds without structures hash as before
            Mix(s.Length); Mix((int)s.Tier);
            Mix(s.IsSink ? 1 : 0); Mix(s.SourcePattern?.Length ?? -1);
            Mix(s.MachineOut); Mix(s.FedBy);
            Mix(s.OutCount);
            for (int k = 0; k < s.OutCount; k++) Mix(s.Out[k]);
            Mix(s.Count); Mix(s.RoundRobin); Mix(s.SourceIndex); Mix(s.MergeTurn);
            for (int k = 0; k < s.Count; k++)
            {
                int i = s.Index(k);
                Mix(s.Pos[i]); Mix(s.Type[i]);
            }
        }
        return h;
    }

    /// <summary>Deep copy of the whole state.</summary>
    public BeltNetwork Clone()
    {
        var c = new BeltNetwork(Content, Mode, ThreatsEnabled, StationRules, StructureRules, Rules);
        c.CopyFrom(this);
        c.StartingItemsClamped = StartingItemsClamped;
        return c;
    }

    /// <summary>Replaces this network's state with a deep copy of <paramref name="other"/> (used to roll back atomic commands).</summary>
    public void CopyFrom(BeltNetwork other)
    {
        _segs = other._segs.Select(s => s?.Clone()).ToList();
        _alive = other._alive;
        CopyMachinesFrom(other);
        CopyEconomyFrom(other);
        CopyStationFrom(other);
        CopyStructuresFrom(other);
        Tick = other.Tick;
        Spawned = other.Spawned;
        Consumed = other.Consumed;
        Destroyed = other.Destroyed;
        StructureVersion = other.StructureVersion;
        _order = Array.Empty<int>();
        _dirty = true;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any structural or conservation invariant is broken.
    /// Meant for tests and debug builds.
    /// </summary>
    public void ValidateInvariants()
    {
        static void Fail(string m) => throw new InvalidOperationException(m);

        ValidateStation();
        ValidateStructures();

        int alive = 0;
        for (int id = 0; id < _segs.Count; id++)
        {
            var s = _segs[id];
            if (s is null) continue;
            alive++;
            if (s.Id != id) Fail($"segment {id} has id {s.Id}");
            if (s.IsSink && s.OutCount > 0) Fail($"sink {id} has outputs");
            if (s.MachineOut >= 0)
            {
                if (s.OutCount > 0 || s.IsSink) Fail($"segment {id} feeds a machine and also has other outputs");
                if (!IsMachineAlive(s.MachineOut) || !_machines[s.MachineOut]!.InputsFrom.Contains(id)) Fail($"segment {id} has a broken machine output link");
            }
            if (s.FedBy >= 0)
            {
                if (s.Inputs.Count > 0 || s.SourcePattern is not null) Fail($"segment {id} is fed by a machine and also has other inputs");
                if (!IsMachineAlive(s.FedBy) || Array.IndexOf(_machines[s.FedBy]!.OutBelts, id, 0, _machines[s.FedBy]!.OutCount) < 0) Fail($"segment {id} has a broken machine input link");
            }
            if (s.OutCount is < 0 or > 2) Fail($"segment {id} has {s.OutCount} outputs");
            for (int k = 0; k < s.OutCount; k++)
            {
                int o = s.Out[k];
                if (!IsAlive(o)) Fail($"segment {id} outputs to dead segment {o}");
                if (!_segs[o]!.Inputs.Contains(id)) Fail($"segment {id} -> {o} lacks the back reference");
            }
            foreach (int input in s.Inputs)
            {
                if (!IsAlive(input)) Fail($"segment {id} has dead input {input}");
                if (!IsConnected(input, id)) Fail($"segment {id} lists input {input} that does not output to it");
            }
            if (s.Inputs.Count != s.Inputs.Distinct().Count()) Fail($"segment {id} has duplicate inputs");
            if (s.MergeTurn < 0 || s.MergeTurn >= Math.Max(1, s.Inputs.Count)) Fail($"segment {id} merge turn out of range");
            if (s.OutCount > 0 && (s.RoundRobin < 0 || s.RoundRobin >= s.OutCount)) Fail($"segment {id} round robin out of range");
            if (s.Count < 0 || s.Count > s.Capacity) Fail($"segment {id} item count out of range");

            int prev = int.MaxValue;
            for (int k = 0; k < s.Count; k++)
            {
                int p = s.Pos[s.Index(k)];
                if (p < 0 || p > s.Length) Fail($"segment {id} item at {p} outside 0..{s.Length}");
                if (k > 0 && prev - p < Units.ItemSpacing) Fail($"segment {id} items closer than the spacing");
                prev = p;
            }
        }

        if (alive != _alive) Fail($"alive counter {_alive} but {alive} segments exist");
        ValidateMachines();
        ValidateEconomy();
        long held = MachineHeldItems;
        if (Spawned + MachineOutputsCreated != Consumed + Destroyed + MachineInputsConsumed + InTransit + held)
            Fail($"conservation broken: spawned {Spawned} + made {MachineOutputsCreated} != consumed {Consumed} + destroyed {Destroyed} " +
                 $"+ used {MachineInputsConsumed} + in transit {InTransit} + held {held}");
    }

    private BeltSegment Get(int id)
    {
        if ((uint)id >= (uint)_segs.Count) throw new ArgumentOutOfRangeException(nameof(id));
        return _segs[id] ?? throw new ArgumentOutOfRangeException(nameof(id), "The segment was removed.");
    }

    /// <summary>Removes <paramref name="to"/> from the outputs of <paramref name="p"/>.</summary>
    private static void DropOutput(BeltSegment p, int to)
    {
        if (p.OutCount == 2 && p.Out[0] == to) p.Out[0] = p.Out[1];
        p.OutCount--;
        p.Out[p.OutCount] = 0;
        p.RoundRobin = 0;
    }

    private static void FixMergeTurn(BeltSegment t)
    {
        t.MergeTurn = t.Inputs.Count == 0 ? 0 : t.MergeTurn % t.Inputs.Count;
    }

    /// <summary>Orders segments downstream-first so space freed this tick is visible upstream. Cycles are broken by id.</summary>
    private void RebuildOrder()
    {
        int n = _segs.Count;
        var visited = new bool[n];
        var order = new List<int>(_alive);
        var stack = new Stack<(int Id, int Next)>();

        for (int r = 0; r < n; r++)
        {
            if (visited[r] || _segs[r] is null) continue;
            visited[r] = true;
            stack.Push((r, 0));
            while (stack.Count > 0)
            {
                var (id, next) = stack.Pop();
                var s = _segs[id]!;
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
        if (s.MachineOut >= 0) return TryFeedMachine(s);

        for (int t = 0; t < s.OutCount; t++)
        {
            int slot = (s.RoundRobin + t) % s.OutCount;
            var target = _segs[s.Out[slot]]!;
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
            var other = _segs[target.Inputs[(target.MergeTurn + r) % n]]!;
            if (other.LastMoved == Tick) continue; // already processed: it either went through or could not
            if (other.Count > 0 && other.Pos[other.Head] + other.Speed >= other.Length
                && other.OutCount > 0 && other.Out[other.RoundRobin % other.OutCount] == target.Id)
                return true;
        }
        return false;
    }
}
