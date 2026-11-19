namespace Starfoundry.Sim;

/// <summary>
/// The authoritative simulation: a belt network plus the command layer. Everything that changes the
/// world goes through <see cref="Submit"/>; commands wait in a queue and are applied at the start of
/// the next <see cref="Step"/> in submission order, so two worlds fed the same log stay identical.
/// </summary>
public sealed class SimWorld
{
    public const int MaxSegmentCells = 128;
    public const int MaxCoord = 10_000;
    public const int MaxSegments = 100_000;
    public const int MaxMachines = 50_000;
    public const int MaxPattern = 64;
    public const int MaxBatch = 4096;

    /// <summary>Occupied cells: a segment id (zero or more) or, for machines, -(machine id + 1).</summary>
    private readonly Dictionary<long, int> _cells = new();
    private readonly Queue<(int Player, Command Command)> _queue = new();
    private readonly List<LoggedCommand> _log = new();
    private readonly List<CommandResult> _results = new();

    /// <summary>The underlying network. Read it freely (rendering, queries); change it only through commands.</summary>
    public BeltNetwork Network { get; }

    public ContentDb? Content => Network.Content;

    public long Tick => Network.Tick;

    /// <summary>Results of the commands applied by the most recent <see cref="Step"/>.</summary>
    public IReadOnlyList<CommandResult> LastResults => _results;

    /// <summary>Every command applied so far, in order.</summary>
    public IReadOnlyList<LoggedCommand> Log => _log;

    public int PendingCommands => _queue.Count;

    public SimWorld(ContentDb? content = null) : this(new BeltNetwork(content)) { }

    /// <summary>Wraps an existing network (for example a scenario), indexing the cells its segments and machines occupy.</summary>
    public SimWorld(BeltNetwork network)
    {
        Network = network;
        for (int id = 0; id < network.SegmentIdLimit; id++)
        {
            if (!network.IsAlive(id)) continue;
            var info = network.GetSegmentInfo(id);
            if (!TryClaim(info.Id, info.OriginX, info.OriginY, info.OriginZ, info.DirX, info.DirY, info.DirZ, info.LengthCells))
                throw new ArgumentException($"Segment {id} overlaps another segment.", nameof(network));
        }
        for (int id = 0; id < network.MachineIdLimit; id++)
        {
            if (!network.IsMachineAlive(id)) continue;
            var m = network.GetMachineInfo(id);
            if (!_cells.TryAdd(Key(m.X, m.Y, m.Z), -(id + 1)))
                throw new ArgumentException($"Machine {id} overlaps something.", nameof(network));
        }
    }

    /// <summary>Queues a command from a player. It is applied at the start of the next tick.</summary>
    /// <exception cref="ArgumentException">The command is structurally malformed (nulls, oversized or too deeply
    /// nested), so it could not be logged. Game-rule problems are not exceptions: they are rejected with a reason.</exception>
    public void Submit(int player, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!CommandCodec.IsWellFormed(command)) throw new ArgumentException("The command is malformed.", nameof(command));
        _queue.Enqueue((player, command));
    }

    /// <summary>Applies queued commands in order, then advances the simulation by one tick.</summary>
    public void Step()
    {
        _results.Clear();
        while (_queue.Count > 0)
        {
            var (player, command) = _queue.Dequeue();
            _log.Add(new LoggedCommand(Network.Tick, player, command));
            _results.Add(Apply(player, command));
        }
        Network.Step();
    }

    public void Step(int ticks)
    {
        for (int i = 0; i < ticks; i++) Step();
    }

    /// <summary>
    /// Rebuilds a world by replaying a command log from an empty world (or from <paramref name="initial"/>)
    /// for <paramref name="ticks"/> ticks. A world fed the same log ends in the same state. Pass the same
    /// content the log was recorded with; <see cref="CommandCodec.ReadLog"/> returns its hash for checking.
    /// </summary>
    public static SimWorld Replay(IReadOnlyList<LoggedCommand> log, long ticks, SimWorld? initial = null, ContentDb? content = null)
    {
        var world = initial ?? new SimWorld(content);
        int next = 0;
        for (long t = 0; t < ticks; t++)
        {
            while (next < log.Count && log[next].Tick <= world.Tick)
            {
                world.Submit(log[next].Player, log[next].Command);
                next++;
            }
            world.Step();
        }
        return world;
    }

    /// <summary>State hash for desync detection. Equal values mean equal worlds.</summary>
    public ulong Checksum() => Network.Checksum();

    /// <summary>Throws if the network or the cell index is inconsistent. For tests and debug builds.</summary>
    public void ValidateInvariants()
    {
        Network.ValidateInvariants();

        var expected = new Dictionary<long, int>();
        for (int id = 0; id < Network.SegmentIdLimit; id++)
        {
            if (!Network.IsAlive(id)) continue;
            var s = Network.GetSegmentInfo(id);
            for (int k = 0; k < s.LengthCells; k++)
            {
                long key = Key(s.OriginX + s.DirX * k, s.OriginY + s.DirY * k, s.OriginZ + s.DirZ * k);
                if (!expected.TryAdd(key, id))
                    throw new InvalidOperationException($"Segment {id} overlaps another segment or machine.");
            }
        }
        for (int id = 0; id < Network.MachineIdLimit; id++)
        {
            if (!Network.IsMachineAlive(id)) continue;
            var m = Network.GetMachineInfo(id);
            if (!expected.TryAdd(Key(m.X, m.Y, m.Z), -(id + 1)))
                throw new InvalidOperationException($"Machine {id} overlaps a segment or machine.");
        }
        if (expected.Count != _cells.Count || expected.Any(kv => !_cells.TryGetValue(kv.Key, out int v) || v != kv.Value))
            throw new InvalidOperationException("The cell index does not match the segments and machines.");
    }

    // ---- command application -------------------------------------------------------------

    private CommandResult Apply(int player, Command command)
    {
        var created = new List<int>();
        var createdMachines = new List<int>();
        int failed = -1;
        Reject reject;
        if (command is Batch batch) reject = ApplyBatch(batch, created, createdMachines, out failed);
        else reject = ApplySingle(command, created, createdMachines);
        return new CommandResult(Network.Tick, player, command, reject, created.ToArray(), failed, createdMachines.ToArray());
    }

    private Reject ApplyBatch(Batch batch, List<int> created, List<int> createdMachines, out int failedIndex)
    {
        failedIndex = -1;
        if (batch.Commands is null || batch.Commands.Length == 0 || batch.Commands.Length > MaxBatch)
            return Reject.InvalidArgument;
        if (batch.Commands.Any(c => c is null)) return Reject.InvalidArgument;
        if (batch.Commands.Any(c => c is Batch)) return Reject.NestedBatch;

        var snapshot = Network.Clone();
        var cells = new Dictionary<long, int>(_cells);

        for (int i = 0; i < batch.Commands.Length; i++)
        {
            var reject = ApplySingle(batch.Commands[i], created, createdMachines);
            if (reject == Reject.None) continue;

            Network.CopyFrom(snapshot);
            _cells.Clear();
            foreach (var kv in cells) _cells.Add(kv.Key, kv.Value);
            created.Clear();
            createdMachines.Clear();
            failedIndex = i;
            return reject;
        }
        return Reject.None;
    }

    /// <summary>Resolves an id; negative ids refer to things created earlier in the same batch (-1 is the first).</summary>
    private static int Resolve(int id, List<int> created)
    {
        if (id >= 0) return id;
        int index = -id - 1;
        return index < created.Count ? created[index] : -1;
    }

    private Reject ApplySingle(Command command, List<int> created, List<int> machines)
    {
        switch (command)
        {
            case PlaceSegment p:
                return Place(p, created);

            case RemoveSegment r:
            {
                int id = Resolve(r.Segment, created);
                if (!Network.IsAlive(id)) return Reject.UnknownSegment;
                var info = Network.GetSegmentInfo(id);
                Release(info);
                Network.RemoveSegment(id);
                return Reject.None;
            }

            case ConnectSegments c:
            {
                int from = Resolve(c.From, created), to = Resolve(c.To, created);
                if (!Network.IsAlive(from) || !Network.IsAlive(to)) return Reject.UnknownSegment;
                if (from == to) return Reject.SelfConnection;
                if (Network.IsSink(from)) return Reject.FromIsSink;
                if (Network.IsConnected(from, to)) return Reject.AlreadyConnected;
                if (Network.FeedsAnyMachine(from) || Network.IsFedByAnyMachine(to)) return Reject.SegmentConflict;
                if (Network.OutputCount(from) >= 2) return Reject.OutputsFull;
                var f = Network.GetSegmentInfo(from);
                var t = Network.GetSegmentInfo(to);
                // The start cell of the target must touch the last cell of the source by a face:
                // straight ahead, or to either side (which lets a belt split into two outputs).
                int lastX = f.OriginX + f.DirX * (f.LengthCells - 1);
                int lastY = f.OriginY + f.DirY * (f.LengthCells - 1);
                int lastZ = f.OriginZ + f.DirZ * (f.LengthCells - 1);
                int manhattan = Math.Abs(t.OriginX - lastX) + Math.Abs(t.OriginY - lastY) + Math.Abs(t.OriginZ - lastZ);
                if (manhattan != 1) return Reject.NotAdjacent;
                Network.Connect(from, to);
                return Reject.None;
            }

            case DisconnectSegments d:
            {
                int from = Resolve(d.From, created), to = Resolve(d.To, created);
                if (!Network.IsAlive(from) || !Network.IsAlive(to)) return Reject.UnknownSegment;
                if (!Network.IsConnected(from, to)) return Reject.NotConnected;
                Network.Disconnect(from, to);
                return Reject.None;
            }

            case SetSource s:
            {
                int id = Resolve(s.Segment, created);
                if (!Network.IsAlive(id)) return Reject.UnknownSegment;
                if (s.ItemTypes is null || s.ItemTypes.Length == 0 || s.ItemTypes.Length > MaxPattern)
                    return Reject.InvalidArgument;
                if (Network.IsFedByAnyMachine(id)) return Reject.SegmentConflict;
                Network.SetSource(id, s.ItemTypes);
                return Reject.None;
            }

            case ClearSource cs:
            {
                int id = Resolve(cs.Segment, created);
                if (!Network.IsAlive(id)) return Reject.UnknownSegment;
                Network.ClearSource(id);
                return Reject.None;
            }

            case SetSink ss:
            {
                int id = Resolve(ss.Segment, created);
                if (!Network.IsAlive(id)) return Reject.UnknownSegment;
                if (Network.OutputCount(id) > 0 || Network.FeedsAnyMachine(id)) return Reject.SinkHasOutputs;
                Network.SetSink(id);
                return Reject.None;
            }

            case ClearSink cl:
            {
                int id = Resolve(cl.Segment, created);
                if (!Network.IsAlive(id)) return Reject.UnknownSegment;
                Network.ClearSink(id);
                return Reject.None;
            }

            case PlaceMachine pm:
                return PlaceMachineCore(pm, machines);

            case RemoveMachine rm:
            {
                int id = Resolve(rm.Machine, machines);
                if (!Network.IsMachineAlive(id)) return Reject.UnknownMachine;
                var m = Network.GetMachineInfo(id);
                _cells.Remove(Key(m.X, m.Y, m.Z));
                Network.RemoveMachine(id);
                return Reject.None;
            }

            case SetRecipe sr:
            {
                int id = Resolve(sr.Machine, machines);
                if (!Network.IsMachineAlive(id)) return Reject.UnknownMachine;
                var check = CheckRecipe(Network.GetMachineInfo(id).Type, sr.Recipe);
                if (check != Reject.None) return check;
                Network.SetRecipe(id, sr.Recipe);
                return Reject.None;
            }

            case ConnectToMachine ctm:
            {
                int seg = Resolve(ctm.Segment, created), mach = Resolve(ctm.Machine, machines);
                if (!Network.IsAlive(seg)) return Reject.UnknownSegment;
                if (!Network.IsMachineAlive(mach)) return Reject.UnknownMachine;
                if (Network.FeedsMachine(seg, mach)) return Reject.AlreadyConnected;
                var f = Network.GetSegmentInfo(seg);
                var m = Network.GetMachineInfo(mach);
                int lastX = f.OriginX + f.DirX * (f.LengthCells - 1);
                int lastY = f.OriginY + f.DirY * (f.LengthCells - 1);
                int lastZ = f.OriginZ + f.DirZ * (f.LengthCells - 1);
                if (Math.Abs(m.X - lastX) + Math.Abs(m.Y - lastY) + Math.Abs(m.Z - lastZ) != 1) return Reject.NotAdjacent;
                if (Network.OutputCount(seg) > 0 || Network.IsSink(seg) || Network.FeedsAnyMachine(seg)) return Reject.SegmentConflict;
                Network.ConnectToMachine(seg, mach);
                return Reject.None;
            }

            case ConnectFromMachine cfm:
            {
                int mach = Resolve(cfm.Machine, machines), seg = Resolve(cfm.Segment, created);
                if (!Network.IsMachineAlive(mach)) return Reject.UnknownMachine;
                if (!Network.IsAlive(seg)) return Reject.UnknownSegment;
                if (Network.IsFedByMachine(seg, mach)) return Reject.AlreadyConnected;
                var s = Network.GetSegmentInfo(seg);
                var m = Network.GetMachineInfo(mach);
                if (Math.Abs(m.X - s.OriginX) + Math.Abs(m.Y - s.OriginY) + Math.Abs(m.Z - s.OriginZ) != 1) return Reject.NotAdjacent;
                if (Network.MachineOutputCount(mach) >= 2) return Reject.MachineOutputsFull;
                if (Network.IsFedByAnyMachine(seg) || Network.InputCount(seg) > 0 || Network.HasSource(seg)) return Reject.SegmentConflict;
                Network.ConnectFromMachine(mach, seg);
                return Reject.None;
            }

            case DisconnectMachine dm:
            {
                int seg = Resolve(dm.Segment, created), mach = Resolve(dm.Machine, machines);
                if (!Network.IsAlive(seg)) return Reject.UnknownSegment;
                if (!Network.IsMachineAlive(mach)) return Reject.UnknownMachine;
                if (!Network.FeedsMachine(seg, mach) && !Network.IsFedByMachine(seg, mach)) return Reject.NotConnected;
                Network.DisconnectMachine(seg, mach);
                return Reject.None;
            }

            default:
                return Reject.InvalidArgument;
        }
    }

    private Reject CheckRecipe(int machineType, int recipe)
    {
        if (recipe == -1) return Reject.None;
        var content = Network.Content;
        if (content is null) return Reject.NoContent;
        if (!content.IsRecipe(recipe)) return Reject.UnknownRecipe;
        return content.Recipes[recipe].Machine == machineType ? Reject.None : Reject.RecipeMismatch;
    }

    private Reject Place(PlaceSegment p, List<int> created)
    {
        if (!Dirs.IsValid(p.Direction)) return Reject.InvalidArgument;
        if (p.LengthCells < 1 || p.LengthCells > MaxSegmentCells) return Reject.InvalidArgument;
        if (!Enum.IsDefined(p.Tier)) return Reject.InvalidArgument;
        if (Network.SegmentCount >= MaxSegments) return Reject.TooManySegments;

        var (dx, dy, dz) = Dirs.Vector(p.Direction);
        long endX = (long)p.X + (long)dx * p.LengthCells;
        long endY = (long)p.Y + (long)dy * p.LengthCells;
        long endZ = (long)p.Z + (long)dz * p.LengthCells;
        if (!InBounds(p.X) || !InBounds(p.Y) || !InBounds(p.Z) || !InBounds(endX) || !InBounds(endY) || !InBounds(endZ))
            return Reject.OutOfBounds;

        for (int k = 0; k < p.LengthCells; k++)
        {
            if (_cells.ContainsKey(Key(p.X + dx * k, p.Y + dy * k, p.Z + dz * k))) return Reject.Overlap;
        }

        int id = Network.AddSegment(p.X, p.Y, p.Z, dx, dy, dz, p.LengthCells, p.Tier);
        TryClaim(id, p.X, p.Y, p.Z, dx, dy, dz, p.LengthCells);
        created.Add(id);
        return Reject.None;
    }

    private Reject PlaceMachineCore(PlaceMachine p, List<int> machines)
    {
        var content = Network.Content;
        if (content is null) return Reject.NoContent;
        if (!content.IsMachine(p.Machine)) return Reject.UnknownMachineType;
        var recipe = CheckRecipe(p.Machine, p.Recipe);
        if (recipe != Reject.None) return recipe;
        if (Network.MachineCount >= MaxMachines) return Reject.TooManyMachines;
        if (!InBounds(p.X) || !InBounds(p.Y) || !InBounds(p.Z)) return Reject.OutOfBounds;

        long key = Key(p.X, p.Y, p.Z);
        if (_cells.ContainsKey(key)) return Reject.Overlap;

        int id = Network.AddMachine(p.X, p.Y, p.Z, p.Machine, p.Recipe);
        _cells.Add(key, -(id + 1));
        machines.Add(id);
        return Reject.None;
    }

    // ---- cell index ----------------------------------------------------------------------

    private static bool InBounds(long v) => v >= -MaxCoord && v <= MaxCoord;

    private static long Key(int x, int y, int z) =>
        ((long)(x + MaxCoord) << 30) | ((long)(y + MaxCoord) << 15) | (long)(z + MaxCoord);

    private bool TryClaim(int id, int x, int y, int z, int dx, int dy, int dz, int length)
    {
        for (int k = 0; k < length; k++)
        {
            if (!_cells.TryAdd(Key(x + dx * k, y + dy * k, z + dz * k), id)) return false;
        }
        return true;
    }

    private void Release(SegmentInfo s)
    {
        for (int k = 0; k < s.LengthCells; k++)
            _cells.Remove(Key(s.OriginX + s.DirX * k, s.OriginY + s.DirY * k, s.OriginZ + s.DirZ * k));
    }
}
