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

    /// <summary>Occupied local cells of every structure other than 0, with the same value encoding as <see cref="_cells"/>.
    /// Sorted by structure id; an entry exists only while it holds at least one cell.</summary>
    private readonly SortedDictionary<int, Dictionary<long, int>> _structCells = new();
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

    public SimWorld(ContentDb? content = null, GameMode mode = GameMode.Sandbox) : this(new BeltNetwork(content, mode)) { }

    public GameMode Mode => Network.Mode;

    /// <summary>
    /// Creates a world with a generated galaxy: the same content and seed always give the same systems and deposits.
    /// Deposits of the home system start revealed; the others stay hidden until scanned. Replays and peers must create
    /// the world with the same arguments (the log header stores mode, content hash and seed).
    /// </summary>
    public static SimWorld CreateGalaxy(ContentDb content, GameMode mode, ulong seed, bool threats = false, bool stationRules = false,
        bool structures = false, WorldRules? rules = null)
    {
        return Build(content, mode, seed, threats, stationRules, structures, rules, scenario: null);
    }

    /// <summary>
    /// Creates the world of a scenario ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)): a structure galaxy with the scenario's rules, its
    /// start structures built from templates (the ship at the home origin, others at their offsets in cells) and its starting items. Always
    /// a structure world with threats allowed (the rules switch them off). The seed is <paramref name="seedOverride"/> when non-zero, else the
    /// scenario's; a scenario with neither cannot be created (the game picks a random seed). <paramref name="rules"/> replaces the scenario's
    /// effective rules: the sandbox lets the player choose them, and a replay passes the rules of the log header.
    /// </summary>
    public static SimWorld CreateScenario(ContentDb content, ScenarioDef scenario, ulong seedOverride = 0, WorldRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scenario);
        ulong seed = seedOverride != 0 ? seedOverride : scenario.Seed;
        if (seed == 0) throw new ArgumentException($"Scenario '{scenario.Id}' has no fixed seed: pass a seed.", nameof(seedOverride));
        return Build(content, scenario.SandboxMode ? GameMode.Sandbox : GameMode.Campaign, seed, threats: true, stationRules: false, structures: true,
            rules ?? scenario.Rules, scenario);
    }

    /// <summary>Same as <see cref="CreateScenario(ContentDb, ScenarioDef, ulong, WorldRules?)"/>, looking the scenario up in a catalog.</summary>
    public static SimWorld CreateScenario(ContentDb content, ScenarioCatalog catalog, string scenarioId, ulong seedOverride = 0, WorldRules? rules = null) =>
        CreateScenario(content, catalog.Get(scenarioId), seedOverride, rules);

    private static SimWorld Build(ContentDb content, GameMode mode, ulong seed, bool threats, bool stationRules, bool structures, WorldRules? rules, ScenarioDef? scenario)
    {
        rules ??= WorldRules.Default;
        if (rules.Validate(content) is { } problem) throw new ArgumentException("Invalid world rules: " + problem, nameof(rules));
        var galaxy = Galaxy.Generate(content, seed);
        var net = new BeltNetwork(content, mode, threats, stationRules, structures, rules);
        foreach (var d in galaxy.Deposits)
            net.AddDeposit(d.Item, Math.Max(1, WorldRules.Scale(d.Amount, rules.DepositRichnessPercent)), d.X, d.Y, d.Z, revealed: d.System == 0, density: d.Density);
        net.SetGalaxy(galaxy);
        if (scenario is not null)
        {
            net.EnsurePlayer(0);
            long cu = Units.CellUnits;
            foreach (var s in scenario.Structures)
                net.BuildStructureFromTemplate(content.Templates[s.Template], s.Kind, 0, s.OffsetCells[0] * cu, s.OffsetCells[1] * cu, s.OffsetCells[2] * cu);
        }
        // The starter ship is part of the initial world, like the deposits: built before the first command, at no cost, at the
        // home anchor (the world origin), so replays and saves that call CreateGalaxy with the header flags get it too.
        else if (structures && content.StarterShip is { } ship)
        {
            net.EnsurePlayer(0);
            net.BuildStructureFromTemplate(ship, StructureKind.Ship, 0, 0, 0, 0);
        }
        if (rules.StartingItems.Count > 0) net.GiveStartingItems(0, rules.StartingItems);
        return new SimWorld(net);
    }

    /// <summary>
    /// Rebuilds a scenario game by replaying a command log: creates the scenario world with the seed and rules of the log header, then
    /// replays (as <see cref="Replay"/> does from a fresh world).
    /// </summary>
    public static SimWorld ReplayScenario(IReadOnlyList<LoggedCommand> log, long ticks, ContentDb content, ScenarioCatalog catalog, string scenarioId, ulong seed,
        WorldRules? rules = null) =>
        Replay(log, ticks, initial: CreateScenario(content, catalog, scenarioId, seed, rules));

    private readonly List<int> _revealed = new();

    /// <summary>Wraps an existing network (for example a scenario), indexing the cells its segments and machines occupy.</summary>
    public SimWorld(BeltNetwork network)
    {
        Network = network;
        for (int id = 0; id < network.SegmentIdLimit; id++)
        {
            if (!network.IsAlive(id)) continue;
            var info = network.GetSegmentInfo(id);
            if (!TryClaim(info.Id, info.OriginX, info.OriginY, info.OriginZ, info.DirX, info.DirY, info.DirZ, info.LengthCells, info.Structure))
                throw new ArgumentException($"Segment {id} overlaps another segment.", nameof(network));
        }
        for (int id = 0; id < network.MachineIdLimit; id++)
        {
            if (!network.IsMachineAlive(id)) continue;
            var m = network.GetMachineInfo(id);
            if (!CellsFor(m.Structure, create: true)!.TryAdd(Key(m.X, m.Y, m.Z), -(id + 1)))
                throw new ArgumentException($"Machine {id} overlaps something.", nameof(network));
        }
    }

    /// <summary>Queues a command from a player. It is applied at the start of the next tick.</summary>
    /// <exception cref="ArgumentException">The command is structurally malformed (nulls, oversized or too deeply
    /// nested), so it could not be logged. Game-rule problems are not exceptions: they are rejected with a reason.</exception>
    public void Submit(int player, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
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

        // Machines destroyed by enemies leave their cells free.
        foreach (var d in Network.TakeDestroyedMachines()) RemoveCell(d.Structure, Key(d.X, d.Y, d.Z));
        // So do belts that fell with a destroyed module or station.
        foreach (var d in Network.TakeDestroyedSegments())
            for (int k = 0; k < d.LengthCells; k++) RemoveCell(d.Structure, Key(d.OriginX + d.DirX * k, d.OriginY + d.DirY * k, d.OriginZ + d.DirZ * k));
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
    public static SimWorld Replay(IReadOnlyList<LoggedCommand> log, long ticks, SimWorld? initial = null, ContentDb? content = null,
        GameMode mode = GameMode.Sandbox, WorldRules? rules = null)
    {
        var world = initial ?? new SimWorld(new BeltNetwork(content, mode, rules: rules));
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

    public enum EntityKind { None, Segment, Machine }

    /// <summary>What occupies a cell: a belt segment, a machine, or nothing (id -1). For pickers and tools.</summary>
    public (EntityKind Kind, int Id) EntityAt(int x, int y, int z) => EntityAt(0, x, y, z);

    /// <summary>What occupies a local cell of a structure (0 = the legacy world, whose cells are world cells).</summary>
    public (EntityKind Kind, int Id) EntityAt(int structure, int x, int y, int z)
    {
        if (structure == 0 ? !(InBounds(x) && InBounds(y) && InBounds(z)) : !(StructureGrid.InLocalBounds(x) && StructureGrid.InLocalBounds(y) && StructureGrid.InLocalBounds(z)))
            return (EntityKind.None, -1);
        var cells = CellsFor(structure, create: false);
        if (cells is null || !cells.TryGetValue(Key(x, y, z), out int v)) return (EntityKind.None, -1);
        return v >= 0 ? (EntityKind.Segment, v) : (EntityKind.Machine, -(v + 1));
    }

    /// <summary>State hash for desync detection. Equal values mean equal worlds.</summary>
    public ulong Checksum() => Network.Checksum();

    /// <summary>Throws if the network or the cell index is inconsistent. For tests and debug builds.</summary>
    public void ValidateInvariants()
    {
        Network.ValidateInvariants();

        var expected = new SortedDictionary<int, Dictionary<long, int>>();
        Dictionary<long, int> Of(int structure)
        {
            if (!expected.TryGetValue(structure, out var d)) expected[structure] = d = new Dictionary<long, int>();
            return d;
        }
        for (int id = 0; id < Network.SegmentIdLimit; id++)
        {
            if (!Network.IsAlive(id)) continue;
            var s = Network.GetSegmentInfo(id);
            var cells = Of(s.Structure);
            for (int k = 0; k < s.LengthCells; k++)
            {
                long key = Key(s.OriginX + s.DirX * k, s.OriginY + s.DirY * k, s.OriginZ + s.DirZ * k);
                if (!cells.TryAdd(key, id))
                    throw new InvalidOperationException($"Segment {id} overlaps another segment or machine.");
            }
        }
        for (int id = 0; id < Network.MachineIdLimit; id++)
        {
            if (!Network.IsMachineAlive(id)) continue;
            var m = Network.GetMachineInfo(id);
            if (!Of(m.Structure).TryAdd(Key(m.X, m.Y, m.Z), -(id + 1)))
                throw new InvalidOperationException($"Machine {id} overlaps a segment or machine.");
        }

        static bool Same(Dictionary<long, int> a, Dictionary<long, int> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out int v) && v == kv.Value);

        expected.TryGetValue(0, out var world);
        if (!Same(world ?? new Dictionary<long, int>(), _cells))
            throw new InvalidOperationException("The cell index does not match the segments and machines.");
        expected.Remove(0);
        if (expected.Count != _structCells.Count
            || expected.Any(kv => !_structCells.TryGetValue(kv.Key, out var actual) || !Same(kv.Value, actual)))
            throw new InvalidOperationException("The structure cell index does not match the segments and machines.");
    }

    // ---- command application -------------------------------------------------------------

    private CommandResult Apply(int player, Command command)
    {
        var created = new List<int>();
        var createdMachines = new List<int>();
        _revealed.Clear();
        int failed = -1;
        Reject reject;
        if (command is Batch batch) reject = ApplyBatch(player, batch, created, createdMachines, out failed);
        else reject = ApplySingle(player, command, created, createdMachines);
        return new CommandResult(Network.Tick, player, command, reject, created.ToArray(), failed, createdMachines.ToArray(),
            _revealed.ToArray());
    }

    private Reject ApplyBatch(int player, Batch batch, List<int> created, List<int> createdMachines, out int failedIndex)
    {
        failedIndex = -1;
        if (batch.Commands is null || batch.Commands.Length == 0 || batch.Commands.Length > MaxBatch)
            return Reject.InvalidArgument;
        if (batch.Commands.Any(c => c is null)) return Reject.InvalidArgument;
        if (batch.Commands.Any(c => c is Batch)) return Reject.NestedBatch;

        var snapshot = Network.Clone();
        var cells = new Dictionary<long, int>(_cells);
        var structCells = _structCells.ToDictionary(kv => kv.Key, kv => new Dictionary<long, int>(kv.Value));

        for (int i = 0; i < batch.Commands.Length; i++)
        {
            var reject = ApplySingle(player, batch.Commands[i], created, createdMachines);
            if (reject == Reject.None) continue;

            Network.CopyFrom(snapshot);
            _cells.Clear();
            foreach (var kv in cells) _cells.Add(kv.Key, kv.Value);
            _structCells.Clear();
            foreach (var kv in structCells) _structCells.Add(kv.Key, kv.Value);
            created.Clear();
            createdMachines.Clear();
            _revealed.Clear();
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

    private bool Campaign => Network.Mode == GameMode.Campaign;

    /// <summary>True if the ship of <paramref name="player"/> is close enough to build or remove at the cell (campaign rule).</summary>
    public bool InBuildRange(int player, int x, int y, int z) =>
        Network.CellInRange(player, x, y, z, PlayerRules.BuildRangeCells);

    /// <summary>Build-range check for a cell of any structure: structure 0 measures the world cell from the ship; inside a structure a
    /// ship of the player is always in range and a station must have the cell near the ship (the world position of the cell).</summary>
    public bool InBuildRange(int player, int structure, int x, int y, int z) =>
        structure == 0 ? InBuildRange(player, x, y, z) : StructureCellInRange(player, Network.GetStructure(structure), x, y, z);

    private ItemStack[] BeltCost(BeltTier tier) =>
        Network.Content?.Belts[(int)tier]?.CostPerCell ?? System.Array.Empty<ItemStack>();

    private Reject ApplySingle(int player, Command command, List<int> created, List<int> machines)
    {
        switch (command)
        {
            case PlaceSegment p:
                return Place(player, p, created);

            case RemoveSegment r:
            {
                int id = Resolve(r.Segment, created);
                if (!Network.IsAlive(id)) return Reject.UnknownSegment;
                var info = Network.GetSegmentInfo(id);
                if (Campaign && !InBuildRange(player, info.Structure, info.OriginX, info.OriginY, info.OriginZ)) return Reject.OutOfRange;
                Release(info);
                Network.RemoveSegment(id);
                if (Campaign) Network.Refund(player, BeltCost(info.Tier), info.LengthCells);
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
                if (Campaign) return Reject.Locked; // a source makes items from nothing: sandbox only
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
                return PlaceMachineCore(player, pm, machines);

            case RemoveMachine rm:
            {
                int id = Resolve(rm.Machine, machines);
                if (!Network.IsMachineAlive(id)) return Reject.UnknownMachine;
                var m = Network.GetMachineInfo(id);
                if (Campaign && !InBuildRange(player, m.Structure, m.X, m.Y, m.Z)) return Reject.OutOfRange;
                RemoveCell(m.Structure, Key(m.X, m.Y, m.Z));
                Network.RemoveMachine(id);
                if (Campaign) Network.Refund(player, Network.Content!.Machines[m.Type].Cost);
                return Reject.None;
            }

            case SetRecipe sr:
            {
                int id = Resolve(sr.Machine, machines);
                if (!Network.IsMachineAlive(id)) return Reject.UnknownMachine;
                var check = CheckRecipe(Network.GetMachineInfo(id).Type, sr.Recipe);
                if (check != Reject.None) return check;
                if (sr.Recipe != -1 && Network.IsRecipeLocked(sr.Recipe)) return Reject.Locked;
                Network.SetRecipe(id, sr.Recipe);
                return Reject.None;
            }

            case StartMining sm:
            {
                if (Network.Content is null) return Reject.NoContent;
                if (!Network.IsDeposit(sm.Deposit)) return Reject.UnknownDeposit;
                if (!Network.IsDepositRevealed(sm.Deposit)) return Reject.NotDiscovered;
                if (Network.DepositRemaining(sm.Deposit) <= 0) return Reject.Depleted;
                if (!Network.InMineRange(player, sm.Deposit)) return Reject.OutOfRange;
                Network.StartMining(player, sm.Deposit);
                return Reject.None;
            }

            case SetWeapons sw:
                Network.SetWeapons(player, sw.Armed);
                return Reject.None;

            case Scan:
                if (Network.Content is null) return Reject.NoContent;
                if (Network.ScanCooldown(player) > 0) return Reject.OnCooldown;
                _revealed.AddRange(Network.Scan(player));
                return Reject.None;

            case Jump j:
            {
                var galaxy = Network.Galaxy;
                if (galaxy is null) return Reject.NoGalaxy;
                if (!Network.IsSystem(j.System)) return Reject.UnknownSystem;
                if (Campaign && !Network.HasAbility(Ability.Jump)) return Reject.Locked;
                if (Network.JumpRemaining(player) > 0) return Reject.Busy;
                int current = Network.CurrentSystem(player);
                if (current < 0) return Reject.NotInASystem;
                if (current == j.System) return Reject.InvalidArgument;
                if (!galaxy.HasLane(current, j.System)) return Reject.NoLane;
                if (!Network.IsSystemKnown(j.System)) return Reject.NotDiscovered;
                if (Campaign)
                {
                    var cost = Network.JumpCost();
                    if (!Network.CanPay(player, cost)) return Reject.NotEnoughItems;
                    Network.Pay(player, cost);
                }
                Network.StartJump(player, j.System);
                return Reject.None;
            }

            case SetMovement mv:
                if (mv.Dx is < -1 or > 1 || mv.Dy is < -1 or > 1 || mv.Dz is < -1 or > 1) return Reject.InvalidArgument;
                if ((mv.Dx | mv.Dy | mv.Dz) != 0 && !Network.CanFly(player)) return Reject.NoCockpitOrEngine; // stopping is always allowed
                Network.SetMovement(player, mv.Dx, mv.Dy, mv.Dz);
                return Reject.None;

            case SetThrust th:
                if (!PlayerRules.ThrustInRange(th.Dx, th.Dy, th.Dz)) return Reject.InvalidArgument;
                if ((th.Dx | th.Dy | th.Dz) != 0 && !Network.CanFly(player)) return Reject.NoCockpitOrEngine;
                Network.SetThrust(player, th.Dx, th.Dy, th.Dz);
                return Reject.None;

            case StopMining:
                if (Network.Content is null) return Reject.NoContent;
                Network.StopMining(player);
                return Reject.None;

            case SetShipRecipe ssr:
            {
                var content = Network.Content;
                if (content is null) return Reject.NoContent;
                if (ssr.Recipe != -1)
                {
                    if (!content.IsRecipe(ssr.Recipe)) return Reject.UnknownRecipe;
                    if (content.Machines[content.Recipes[ssr.Recipe].Machine].Role != MachineRole.Ship) return Reject.NotAShipRecipe;
                    if (Network.IsRecipeLocked(ssr.Recipe)) return Reject.Locked;
                }
                Network.SetShipRecipe(player, ssr.Recipe);
                return Reject.None;
            }

            case SetResearch sres:
            {
                if (sres.Node != -1)
                {
                    if (Network.Content is null) return Reject.NoContent;
                    if (!Network.Content.IsResearch(sres.Node)) return Reject.UnknownResearch;
                    if (!Network.CanResearch(sres.Node)) return Reject.ResearchUnavailable;
                }
                Network.SetResearch(sres.Node);
                return Reject.None;
            }

            case LinkDeposit ld:
            {
                int id = Resolve(ld.Machine, machines);
                if (!Network.IsMachineAlive(id)) return Reject.UnknownMachine;
                if (Network.Content!.Machines[Network.GetMachineInfo(id).Type].Role != MachineRole.Drill) return Reject.WrongRole;
                if (!Network.IsDeposit(ld.Deposit)) return Reject.UnknownDeposit;
                if (!Network.IsDepositRevealed(ld.Deposit)) return Reject.NotDiscovered;
                if (!Network.DrillInRange(id, ld.Deposit)) return Reject.OutOfRange; // world positions: a ship's drill counts where it is now
                Network.LinkDeposit(id, ld.Deposit);
                return Reject.None;
            }

            case UnlinkDeposit ud:
            {
                int id = Resolve(ud.Machine, machines);
                if (!Network.IsMachineAlive(id)) return Reject.UnknownMachine;
                if (Network.Content!.Machines[Network.GetMachineInfo(id).Type].Role != MachineRole.Drill) return Reject.WrongRole;
                Network.UnlinkDeposit(id);
                return Reject.None;
            }

            case ConnectToMachine ctm:
            {
                int seg = Resolve(ctm.Segment, created), mach = Resolve(ctm.Machine, machines);
                if (!Network.IsAlive(seg)) return Reject.UnknownSegment;
                if (!Network.IsMachineAlive(mach)) return Reject.UnknownMachine;
                if (Network.FeedsMachine(seg, mach)) return Reject.AlreadyConnected;
                if (Network.Content!.Machines[Network.GetMachineInfo(mach).Type].Role is MachineRole.Wall or MachineRole.Unloader) return Reject.WrongRole;
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
                if (Network.Content!.Machines[Network.GetMachineInfo(mach).Type].Role is MachineRole.Wall or MachineRole.Repairer) return Reject.WrongRole;
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

            case Repair rp:
            {
                int mach = Resolve(rp.Machine, machines);
                if (!Network.IsMachineAlive(mach)) return Reject.UnknownMachine;
                var m = Network.GetMachineInfo(mach);
                if (m.Hp >= Network.MaxHp(m.Type)) return Reject.NotDamaged;
                if (Campaign)
                {
                    if (!InBuildRange(player, m.Structure, m.X, m.Y, m.Z)) return Reject.OutOfRange;
                    var cost = Network.RepairCost(mach);
                    if (!Network.CanPayBuild(player, cost)) return Reject.NotEnoughItems;
                    Network.PayBuild(player, cost);
                }
                Network.RepairMachine(mach);
                return Reject.None;
            }

            case PlaceModule pmod:
                if (Network.StructureRules) return Reject.NotAllowedHere; // stations are structures now
                return PlaceModuleCore(player, pmod);

            case PlaceMachineIn pmi:
                return pmi.Structure == 0
                    ? PlaceMachineCore(player, new PlaceMachine(pmi.X, pmi.Y, pmi.Z, pmi.Type, pmi.Recipe), machines)
                    : PlaceMachineInCore(player, pmi, machines);

            case PlaceSegmentIn psi:
                return psi.Structure == 0
                    ? Place(player, new PlaceSegment(psi.X, psi.Y, psi.Z, psi.Dir, psi.Length, psi.Tier), created)
                    : PlaceSegmentInCore(player, psi, created);

            case PlaceModuleIn pmo:
                return PlaceModuleInCore(player, pmo);

            case RemoveModuleIn rmo:
                return RemoveModuleInCore(player, rmo);

            case FoundStation fs:
                return FoundStationCore(player, fs);

            case AssignDrones ad:
            {
                if (!Network.StructureRules) return Reject.NotAllowedHere;
                var found = BuildableStructure(player, ad.Structure, out _);
                if (found != Reject.None) return found;
                if (ad.Deposit == -1)
                {
                    Network.AssignDrones(ad.Structure, -1);
                    return Reject.None;
                }
                if (Network.DroneBays(ad.Structure) == 0) return Reject.NoDroneBay;
                if (!Network.IsDeposit(ad.Deposit)) return Reject.UnknownDeposit;
                if (!Network.IsDepositRevealed(ad.Deposit)) return Reject.NotDiscovered;
                if (!Network.DepositInDroneRange(ad.Structure, ad.Deposit)) return Reject.OutOfRange;
                Network.AssignDrones(ad.Structure, ad.Deposit);
                return Reject.None;
            }

            case DumpItems di:
            {
                var content = Network.Content;
                if (content is null) return Reject.NoContent;
                if ((uint)di.Item >= (uint)content.Items.Count) return Reject.InvalidArgument;
                int held = Network.HoldCount(player, di.Item);
                if (held <= 0) return Reject.NotEnoughItems;
                Network.DumpItems(player, di.Item, di.Count <= 0 || di.Count > held ? held : di.Count);
                return Reject.None;
            }

            case RemoveModule rmod:
                return RemoveModuleCore(player, rmod);

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

    private Reject Place(int player, PlaceSegment p, List<int> created)
    {
        if (!Dirs.IsValid(p.Direction)) return Reject.InvalidArgument;
        if (p.LengthCells < 1 || p.LengthCells > MaxSegmentCells) return Reject.InvalidArgument;
        if (!Enum.IsDefined(p.Tier)) return Reject.InvalidArgument;
        if (Network.SegmentCount >= MaxSegments) return Reject.TooManySegments;
        if (Network.StructureRules) return Reject.OutsideStation; // the legacy world takes no building with structures on
        if (Campaign && Network.IsBeltLocked(p.Tier)) return Reject.Locked;

        var (dx, dy, dz) = Dirs.Vector(p.Direction);
        long endX = (long)p.X + (long)dx * p.LengthCells;
        long endY = (long)p.Y + (long)dy * p.LengthCells;
        long endZ = (long)p.Z + (long)dz * p.LengthCells;
        if (!InBounds(p.X) || !InBounds(p.Y) || !InBounds(p.Z) || !InBounds(endX) || !InBounds(endY) || !InBounds(endZ))
            return Reject.OutOfBounds;

        if (Campaign && (!InBuildRange(player, p.X, p.Y, p.Z)
            || !InBuildRange(player, p.X + dx * (p.LengthCells - 1), p.Y + dy * (p.LengthCells - 1), p.Z + dz * (p.LengthCells - 1))))
            return Reject.OutOfRange;

        if (Network.StationRules)
        {
            for (int k = 0; k < p.LengthCells; k++)
                if (!Network.CellInStation(player, p.X + dx * k, p.Y + dy * k, p.Z + dz * k)) return Reject.OutsideStation;
        }

        for (int k = 0; k < p.LengthCells; k++)
        {
            if (_cells.ContainsKey(Key(p.X + dx * k, p.Y + dy * k, p.Z + dz * k))) return Reject.Overlap;
        }

        if (Campaign)
        {
            var cost = BeltCost(p.Tier);
            if (!Network.CanPayBuild(player, cost, p.LengthCells)) return Reject.NotEnoughItems;
            Network.PayBuild(player, cost, p.LengthCells);
        }

        int id = Network.AddSegment(p.X, p.Y, p.Z, dx, dy, dz, p.LengthCells, p.Tier);
        TryClaim(id, p.X, p.Y, p.Z, dx, dy, dz, p.LengthCells);
        created.Add(id);
        return Reject.None;
    }

    private Reject PlaceMachineCore(int player, PlaceMachine p, List<int> machines)
    {
        var content = Network.Content;
        if (content is null) return Reject.NoContent;
        if (!content.IsMachine(p.Machine)) return Reject.UnknownMachineType;
        if (content.Machines[p.Machine].Role == MachineRole.Ship) return Reject.NotPlaceable;
        var recipe = CheckRecipe(p.Machine, p.Recipe);
        if (recipe != Reject.None) return recipe;
        if (Network.MachineCount >= MaxMachines) return Reject.TooManyMachines;
        if (!InBounds(p.X) || !InBounds(p.Y) || !InBounds(p.Z)) return Reject.OutOfBounds;
        if (Network.StructureRules) return Reject.OutsideStation; // the legacy world takes no building with structures on
        if (Campaign && (Network.IsMachineLocked(p.Machine) || (p.Recipe != -1 && Network.IsRecipeLocked(p.Recipe))))
            return Reject.Locked;

        if (Campaign && !InBuildRange(player, p.X, p.Y, p.Z)) return Reject.OutOfRange;

        if (Network.StationRules)
        {
            if (content.Machines[p.Machine].Role == MachineRole.Drill) return Reject.NotAllowedHere;
            if (!Network.CellInStation(player, p.X, p.Y, p.Z)) return Reject.OutsideStation;
        }

        long key = Key(p.X, p.Y, p.Z);
        if (_cells.ContainsKey(key)) return Reject.Overlap;

        if (Campaign)
        {
            var cost = content.Machines[p.Machine].Cost;
            if (!Network.CanPayBuild(player, cost)) return Reject.NotEnoughItems;
            Network.PayBuild(player, cost);
        }

        int id = Network.AddMachine(p.X, p.Y, p.Z, p.Machine, p.Recipe, player);
        _cells.Add(key, -(id + 1));
        machines.Add(id);
        return Reject.None;
    }

    private Reject PlaceModuleCore(int player, PlaceModule p)
    {
        if (Network.Content?.Modules is null) return Reject.NoContent;
        if (!StationGrid.IsValidModule(p.Mx, p.My, p.Mz)) return Reject.OutOfBounds;
        if (Network.HasModule(player, p.Mx, p.My, p.Mz)) return Reject.Overlap;
        if (Network.ModuleCount(player) > 0 && !Network.ModuleTouchesStation(player, p.Mx, p.My, p.Mz)) return Reject.NotAdjacent;
        if (Campaign)
        {
            if (!Network.ModuleCentreInRange(player, p.Mx, p.My, p.Mz, PlayerRules.BuildRangeCells)) return Reject.OutOfRange;
            var cost = Network.NextModuleCost(player);
            if (!Network.CanPayBuild(player, cost)) return Reject.NotEnoughItems;
            Network.PayBuild(player, cost);
        }
        Network.AddModuleUnchecked(player, p.Mx, p.My, p.Mz);
        return Reject.None;
    }

    private Reject RemoveModuleCore(int player, RemoveModule r)
    {
        if (Network.Content?.Modules is null) return Reject.NoContent;
        if (!StationGrid.IsValidModule(r.Mx, r.My, r.Mz)) return Reject.OutOfBounds;
        if (!Network.HasModule(player, r.Mx, r.My, r.Mz)) return Reject.OutsideStation;
        if (Campaign && !Network.ModuleCentreInRange(player, r.Mx, r.My, r.Mz, PlayerRules.BuildRangeCells)) return Reject.OutOfRange;

        // The core anchors the station: it can only go when it is the last module.
        if (Network.IsCoreModule(player, r.Mx, r.My, r.Mz) && Network.ModuleCount(player) > 1) return Reject.ModuleOccupied;
        if (!Network.StationStaysConnectedWithout(player, r.Mx, r.My, r.Mz)) return Reject.ModuleOccupied;

        int x0 = r.Mx * StationGrid.ModuleX, y0 = r.My * StationGrid.ModuleY, z0 = r.Mz * StationGrid.ModuleZ;
        for (int x = x0; x < x0 + StationGrid.ModuleX; x++)
            for (int y = y0; y < y0 + StationGrid.ModuleY; y++)
                for (int z = z0; z < z0 + StationGrid.ModuleZ; z++)
                    if (_cells.ContainsKey(Key(x, y, z))) return Reject.ModuleOccupied;

        var refund = Network.ModuleRefund(player);
        Network.RemoveModuleUnchecked(player, r.Mx, r.My, r.Mz);
        if (Campaign) Network.Refund(player, refund);
        return Reject.None;
    }

    // ---- structure commands ---------------------------------------------------------------

    /// <summary>Resolves a structure the player may build in, or the reject that explains why not.</summary>
    private Reject BuildableStructure(int player, int structure, out Structure? s)
    {
        s = null;
        if (structure <= 0 || !Network.StructureExists(structure)) return Reject.NoSuchStructure;
        s = Network.GetStructure(structure);
        return s.Owner == player ? Reject.None : Reject.NotAllowedHere;
    }

    /// <summary>Build range for structure commands: a ship of the player is always in range; a station must have the point near the ship.
    /// Without campaign mode or structure rules the range is not checked.</summary>
    private bool StructurePointInRange(int player, Structure s, long wx, long wy, long wz)
    {
        if (!Campaign && !Network.StructureRules) return true;
        if (s.Kind == StructureKind.Ship) return true;
        return Network.WorldPointInRange(player, wx, wy, wz, PlayerRules.BuildRangeCells);
    }

    private bool StructureCellInRange(int player, Structure s, int x, int y, int z)
    {
        var (wx, wy, wz) = Network.WorldCentre(s.Id, x, y, z);
        return StructurePointInRange(player, s, wx, wy, wz);
    }

    private Reject PlaceMachineInCore(int player, PlaceMachineIn p, List<int> machines)
    {
        var content = Network.Content;
        if (content?.Structures is not { } kinds) return Reject.NoContent;
        var found = BuildableStructure(player, p.Structure, out var s);
        if (found != Reject.None) return found;
        if (!content.IsMachine(p.Type)) return Reject.UnknownMachineType;
        var def = content.Machines[p.Type];
        if (def.Role == MachineRole.Ship) return Reject.NotPlaceable;
        var recipe = CheckRecipe(p.Type, p.Recipe);
        if (recipe != Reject.None) return recipe;
        if (Network.MachineCount >= MaxMachines) return Reject.TooManyMachines;
        if (!StructureGrid.InLocalBounds(p.X) || !StructureGrid.InLocalBounds(p.Y) || !StructureGrid.InLocalBounds(p.Z)) return Reject.OutOfBounds;
        if (Campaign && (Network.IsMachineLocked(p.Type) || (p.Recipe != -1 && Network.IsRecipeLocked(p.Recipe)))) return Reject.Locked;
        if (!StructureCellInRange(player, s!, p.X, p.Y, p.Z)) return Reject.OutOfRange;

        if (!Network.TryGetModuleKindAt(p.Structure, p.X, p.Y, p.Z, out var kind)) return Reject.OutsideStation;
        if (!kinds.AllowsRole(kind, def.Role)) return Reject.KindNotAllowed;

        var cells = CellsFor(p.Structure, create: true)!;
        long key = Key(p.X, p.Y, p.Z);
        if (cells.ContainsKey(key)) return Reject.Overlap;

        if (Campaign)
        {
            if (!Network.CanPayBuild(player, def.Cost)) return Reject.NotEnoughItems;
            Network.PayBuild(player, def.Cost);
        }

        int id = Network.AddMachine(p.X, p.Y, p.Z, p.Type, p.Recipe, player, p.Structure);
        cells.Add(key, -(id + 1));
        machines.Add(id);
        return Reject.None;
    }

    private Reject PlaceSegmentInCore(int player, PlaceSegmentIn p, List<int> created)
    {
        var content = Network.Content;
        if (content?.Structures is not { } kinds) return Reject.NoContent;
        var found = BuildableStructure(player, p.Structure, out var s);
        if (found != Reject.None) return found;
        if (!Dirs.IsValid(p.Dir)) return Reject.InvalidArgument;
        if (p.Length < 1 || p.Length > MaxSegmentCells) return Reject.InvalidArgument;
        if (!Enum.IsDefined(p.Tier)) return Reject.InvalidArgument;
        if (Network.SegmentCount >= MaxSegments) return Reject.TooManySegments;
        if (Campaign && Network.IsBeltLocked(p.Tier)) return Reject.Locked;

        var (dx, dy, dz) = Dirs.Vector(p.Dir);
        long lastX = (long)p.X + (long)dx * (p.Length - 1), lastY = (long)p.Y + (long)dy * (p.Length - 1), lastZ = (long)p.Z + (long)dz * (p.Length - 1);
        if (!StructureGrid.InLocalBounds(p.X) || !StructureGrid.InLocalBounds(p.Y) || !StructureGrid.InLocalBounds(p.Z)
            || !StructureGrid.InLocalBounds(lastX) || !StructureGrid.InLocalBounds(lastY) || !StructureGrid.InLocalBounds(lastZ))
            return Reject.OutOfBounds;

        if (!StructureCellInRange(player, s!, p.X, p.Y, p.Z) || !StructureCellInRange(player, s!, (int)lastX, (int)lastY, (int)lastZ))
            return Reject.OutOfRange;

        for (int k = 0; k < p.Length; k++)
        {
            if (!Network.TryGetModuleKindAt(p.Structure, p.X + dx * k, p.Y + dy * k, p.Z + dz * k, out var kind)) return Reject.OutsideStation;
            if (!kinds.AllowsBelts(kind)) return Reject.KindNotAllowed;
        }

        var cells = CellsFor(p.Structure, create: true)!;
        for (int k = 0; k < p.Length; k++)
            if (cells.ContainsKey(Key(p.X + dx * k, p.Y + dy * k, p.Z + dz * k))) return Reject.Overlap;

        if (Campaign)
        {
            var cost = BeltCost(p.Tier);
            if (!Network.CanPayBuild(player, cost, p.Length)) return Reject.NotEnoughItems;
            Network.PayBuild(player, cost, p.Length);
        }

        int id = Network.AddSegment(p.X, p.Y, p.Z, dx, dy, dz, p.Length, p.Tier, p.Structure);
        TryClaim(id, p.X, p.Y, p.Z, dx, dy, dz, p.Length, p.Structure);
        created.Add(id);
        return Reject.None;
    }

    private Reject PlaceModuleInCore(int player, PlaceModuleIn p)
    {
        if (Network.Content?.Structures is not { } kinds) return Reject.NoContent;
        var found = BuildableStructure(player, p.Structure, out var s);
        if (found != Reject.None) return found;
        if (!Enum.IsDefined(p.Kind)) return Reject.InvalidArgument;
        if (!kinds.IsDefined(p.Kind)) return Reject.KindNotAllowed;
        if (!StructureGrid.IsValidLocalModule(p.Mx, p.My, p.Mz)) return Reject.OutOfBounds;
        if (ModuleKindIds.RequiredAbility(p.Kind) is { } needed && Campaign && !Network.HasAbility(needed)) return Reject.Locked;
        if (s!.Modules.ContainsKey((p.Mx, p.My, p.Mz))) return Reject.Overlap;
        if (s.Modules.Count > 0)
        {
            if (!Network.StructureModuleTouches(p.Structure, p.Mx, p.My, p.Mz)) return Reject.NotAdjacent;
            if (p.Kind == ModuleKind.Core) return Reject.KindNotAllowed; // only the first module can be the core
        }
        else if (s.Kind == StructureKind.Station && p.Kind != ModuleKind.Core) return Reject.KindNotAllowed;

        var centre = ModuleCentre(s, p.Mx, p.My, p.Mz);
        if (!StructurePointInRange(player, s, centre.X, centre.Y, centre.Z)) return Reject.OutOfRange;
        if (Campaign)
        {
            var cost = kinds.ModuleCost(p.Kind);
            if (!Network.CanPayBuild(player, cost)) return Reject.NotEnoughItems;
            Network.PayBuild(player, cost);
        }
        Network.AddStructureModuleUnchecked(p.Structure, p.Mx, p.My, p.Mz, p.Kind);
        Network.CountModuleBuilt(p.Kind);
        return Reject.None;
    }

    private Reject RemoveModuleInCore(int player, RemoveModuleIn r)
    {
        if (Network.Content?.Structures is not { } kinds) return Reject.NoContent;
        var found = BuildableStructure(player, r.Structure, out var s);
        if (found != Reject.None) return found;
        if (!StructureGrid.IsValidLocalModule(r.Mx, r.My, r.Mz)) return Reject.OutOfBounds;
        if (!s!.Modules.TryGetValue((r.Mx, r.My, r.Mz), out var kind)) return Reject.OutsideStation;
        var centre = ModuleCentre(s, r.Mx, r.My, r.Mz);
        if (!StructurePointInRange(player, s, centre.X, centre.Y, centre.Z)) return Reject.OutOfRange;

        // The core anchors the structure: it can only go when it is the last module.
        if (kind == ModuleKind.Core && s.Modules.Count > 1) return Reject.ModuleOccupied;
        // The ship cannot do without its last cockpit, engine, reactor or cargo module (stations are unaffected).
        if (s.Kind == StructureKind.Ship && Network.ShipStructure(player) == r.Structure
            && kind is ModuleKind.Cockpit or ModuleKind.Engine or ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Cargo
            && s.Modules.Values.Count(k => k == kind || (ModuleKindIds.IsReactor(kind) && ModuleKindIds.IsReactor(k))) <= 1) return Reject.LastEssentialModule;
        if (!Network.StructureStaysConnectedWithout(r.Structure, r.Mx, r.My, r.Mz)) return Reject.ModuleOccupied;

        var cells = CellsFor(r.Structure, create: false);
        if (cells is not null)
        {
            int x0 = r.Mx * StationGrid.ModuleX, y0 = r.My * StationGrid.ModuleY, z0 = r.Mz * StationGrid.ModuleZ;
            for (int x = x0; x < x0 + StationGrid.ModuleX; x++)
                for (int y = y0; y < y0 + StationGrid.ModuleY; y++)
                    for (int z = z0; z < z0 + StationGrid.ModuleZ; z++)
                        if (cells.ContainsKey(Key(x, y, z))) return Reject.ModuleOccupied;
        }

        Network.RemoveStructureModuleUnchecked(r.Structure, r.Mx, r.My, r.Mz);
        if (Campaign)
            Network.Refund(player, kinds.ModuleCost(kind).Select(c => new ItemStack(c.Item, c.Count / 2)).Where(c => c.Count > 0).ToArray());
        return Reject.None;
    }

    private Reject FoundStationCore(int player, FoundStation f)
    {
        if (!Network.StructureRules) return Reject.NotAllowedHere;
        if (Network.Content?.Structures is not { } kinds) return Reject.NoContent;
        long cu = Units.CellUnits;
        long limit = PlayerRules.WorldLimitUnits / cu;
        if (Math.Abs((long)f.Wx) > limit || Math.Abs((long)f.Wy) > limit || Math.Abs((long)f.Wz) > limit) return Reject.OutOfBounds;
        long ox = f.Wx * cu, oy = f.Wy * cu, oz = f.Wz * cu;

        if (!Network.WorldPointInRange(player, ox + cu / 2, oy + cu / 2, oz + cu / 2, PlayerRules.BuildRangeCells)) return Reject.OutOfRange;

        // The core's volume, [origin, origin + module size) per axis, must not touch any module volume of another structure.
        long ex = ox + StationGrid.ModuleX * cu, ey = oy + StationGrid.ModuleY * cu, ez = oz + StationGrid.ModuleZ * cu;
        foreach (var other in Network.Structures())
        {
            foreach (var (m, _) in other.Modules)
            {
                long mx0 = other.OriginX + (long)m.Mx * StationGrid.ModuleX * cu, my0 = other.OriginY + (long)m.My * StationGrid.ModuleY * cu,
                    mz0 = other.OriginZ + (long)m.Mz * StationGrid.ModuleZ * cu;
                if (ox < mx0 + StationGrid.ModuleX * cu && mx0 < ex && oy < my0 + StationGrid.ModuleY * cu && my0 < ey
                    && oz < mz0 + StationGrid.ModuleZ * cu && mz0 < ez)
                    return Reject.Overlap;
            }
        }

        var cost = kinds.ModuleCost(ModuleKind.Core);
        if (Campaign)
        {
            if (!Network.CanPayBuild(player, cost)) return Reject.NotEnoughItems;
            Network.PayBuild(player, cost);
        }
        int id = Network.CreateStructure(player, StructureKind.Station, ox, oy, oz);
        Network.AddStructureModuleUnchecked(id, 0, 0, 0, ModuleKind.Core);
        Network.CountStationFounded();
        return Reject.None;
    }

    private (long X, long Y, long Z) ModuleCentre(Structure s, int mx, int my, int mz)
    {
        long cu = Units.CellUnits;
        return (s.OriginX + ((long)mx * StationGrid.ModuleX + StationGrid.ModuleX / 2) * cu,
                s.OriginY + ((long)my * StationGrid.ModuleY + StationGrid.ModuleY / 2) * cu,
                s.OriginZ + ((long)mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2) * cu);
    }

    // ---- cell index ----------------------------------------------------------------------

    private static bool InBounds(long v) => v >= -MaxCoord && v <= MaxCoord;

    private static long Key(int x, int y, int z) =>
        ((long)(x + MaxCoord) << 30) | ((long)(y + MaxCoord) << 15) | (long)(z + MaxCoord);

    /// <summary>The cell index of a structure: the legacy dictionary for 0, otherwise the per-structure one (created on demand).</summary>
    private Dictionary<long, int>? CellsFor(int structure, bool create)
    {
        if (structure == 0) return _cells;
        if (_structCells.TryGetValue(structure, out var d)) return d;
        if (!create) return null;
        return _structCells[structure] = new Dictionary<long, int>();
    }

    private void RemoveCell(int structure, long key)
    {
        var cells = CellsFor(structure, create: false);
        if (cells is null) return;
        cells.Remove(key);
        if (structure != 0 && cells.Count == 0) _structCells.Remove(structure);
    }

    private bool TryClaim(int id, int x, int y, int z, int dx, int dy, int dz, int length, int structure = 0)
    {
        if (structure != 0 && length > 0)
        {
            long ex = x + (long)dx * (length - 1), ey = y + (long)dy * (length - 1), ez = z + (long)dz * (length - 1);
            if (!(StructureGrid.InLocalBounds(x) && StructureGrid.InLocalBounds(y) && StructureGrid.InLocalBounds(z)
                && StructureGrid.InLocalBounds(ex) && StructureGrid.InLocalBounds(ey) && StructureGrid.InLocalBounds(ez))) return false;
        }
        var cells = CellsFor(structure, create: true)!;
        for (int k = 0; k < length; k++)
        {
            if (!cells.TryAdd(Key(x + dx * k, y + dy * k, z + dz * k), id)) return false;
        }
        return true;
    }

    private void Release(SegmentInfo s)
    {
        for (int k = 0; k < s.LengthCells; k++)
            RemoveCell(s.Structure, Key(s.OriginX + s.DirX * k, s.OriginY + s.DirY * k, s.OriginZ + s.DirZ * k));
    }
}
