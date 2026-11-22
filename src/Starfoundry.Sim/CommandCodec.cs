using System.Text;

namespace Starfoundry.Sim;

/// <summary>
/// Compact binary format for commands and command logs, used for saving replays and, later, for the
/// network. Little-endian, versioned, and strict when reading: sizes are bounded and unknown tags are errors.
/// </summary>
public static class CommandCodec
{
    private const byte TagPlace = 1, TagRemove = 2, TagConnect = 3, TagDisconnect = 4,
        TagSetSource = 5, TagClearSource = 6, TagSetSink = 7, TagClearSink = 8, TagBatch = 9,
        TagPlaceMachine = 10, TagRemoveMachine = 11, TagSetRecipe = 12,
        TagConnectToMachine = 13, TagConnectFromMachine = 14, TagDisconnectMachine = 15,
        TagStartMining = 16, TagStopMining = 17, TagSetShipRecipe = 18, TagSetResearch = 19,
        TagLinkDeposit = 20, TagUnlinkDeposit = 21, TagSetMovement = 22, TagScan = 23, TagJump = 24, TagSetWeapons = 25, TagRepair = 26, TagSetThrust = 27, TagPlaceModule = 28, TagRemoveModule = 29,
        TagPlaceMachineIn = 30, TagPlaceSegmentIn = 31, TagPlaceModuleIn = 32, TagRemoveModuleIn = 33, TagFoundStation = 34, TagAssignDrones = 35, TagDumpItems = 36, TagFireLaser = 37, TagSetBoost = 38, TagDockAt = 39, TagUndock = 40, TagTransferItems = 41;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SFCL");

    /// <summary>Version 2 added the content hash to the header, version 3 the game mode, version 4 the galaxy seed, version 5 the threat flag.</summary>
    /// Version 6 added the station-rules flag (bit 1); the structures flag (bit 2) uses a bit that version 6 left free,
    /// so logs written before it simply read as structures off and the version stays 6.
    private const byte LogVersion = 11;
    /// Version 7 added the world rules block (flags bit 3, written only for non-default rules). Version 8 added the scenario id (flags bit 4).
    /// Version 10 added the SetBoost command (tag 38) and the wide galaxy with obstacles: logs of versions 5 to 9 read with <see cref="WorldRules.LegacyGalaxy"/> on (ADR 0028).
    /// Version 9 added the FireLaser command (tag 37) and the mining laser's power draw: logs of versions 5 to 8 read with <see cref="WorldRules.LegacyLaser"/> on, so they replay without it (ADR 0027).
    /// Version 11 added the docking commands (tags 39 to 41, ADR 0029). They only act on state that older logs never create, so nothing is replayed differently; the reader refuses them in older logs.
    /// <summary>Version 5 headers carry no station-rules or structures flag; they still load, with both rules off.</summary>
    private const byte OldestLogVersion = 5;

    /// <summary>Deepest allowed batch nesting. The world rejects nested batches, but the format can still
    /// carry them so a log reproduces rejections exactly; the bound keeps decoding cheap and safe.</summary>
    public const int MaxNesting = 4;

    /// <summary>
    /// True if the command can be encoded and decoded: no nulls, bounded nesting, batch and pattern sizes
    /// within limits. <see cref="SimWorld.Submit"/> refuses anything else, since it could never be logged.
    /// </summary>
    public static bool IsWellFormed(Command? command) => IsWellFormed(command, 1);

    private static bool IsWellFormed(Command? command, int depth)
    {
        if (command is null || depth > MaxNesting) return false;
        switch (command)
        {
            case Batch b:
                if (b.Commands is null || b.Commands.Length > SimWorld.MaxBatch) return false;
                foreach (var inner in b.Commands)
                    if (!IsWellFormed(inner, depth + 1)) return false;
                return true;
            case SetSource s:
                return s.ItemTypes is not null && s.ItemTypes.Length <= SimWorld.MaxPattern;
            case PlaceSegment or RemoveSegment or ConnectSegments or DisconnectSegments
                or ClearSource or SetSink or ClearSink
                or PlaceMachine or RemoveMachine or SetRecipe
                or ConnectToMachine or ConnectFromMachine or DisconnectMachine
                or StartMining or StopMining or SetShipRecipe or SetResearch or LinkDeposit or UnlinkDeposit or SetMovement or SetThrust
                or Scan or Jump or SetWeapons or Repair or PlaceModule or RemoveModule
                or PlaceMachineIn or PlaceSegmentIn or PlaceModuleIn or RemoveModuleIn or FoundStation or AssignDrones or DumpItems or FireLaser or SetBoost or DockAt or Undock or TransferItems:
                return true;
            default:
                return false;
        }
    }

    public static void Write(BinaryWriter w, Command command)
    {
        if (!IsWellFormed(command)) throw new ArgumentException("The command cannot be encoded.", nameof(command));
        WriteCore(w, command);
    }

    private static void WriteCore(BinaryWriter w, Command command)
    {
        switch (command)
        {
            case PlaceSegment p:
                w.Write(TagPlace);
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
                w.Write((byte)p.Direction);
                w.Write(p.LengthCells);
                w.Write((byte)p.Tier);
                break;
            case RemoveSegment r:
                w.Write(TagRemove); w.Write(r.Segment);
                break;
            case ConnectSegments c:
                w.Write(TagConnect); w.Write(c.From); w.Write(c.To);
                break;
            case DisconnectSegments d:
                w.Write(TagDisconnect); w.Write(d.From); w.Write(d.To);
                break;
            case SetSource s:
                w.Write(TagSetSource);
                w.Write(s.Segment);
                w.Write(s.ItemTypes.Length);
                foreach (ushort t in s.ItemTypes) w.Write(t);
                break;
            case ClearSource cs:
                w.Write(TagClearSource); w.Write(cs.Segment);
                break;
            case SetSink ss:
                w.Write(TagSetSink); w.Write(ss.Segment);
                break;
            case ClearSink cl:
                w.Write(TagClearSink); w.Write(cl.Segment);
                break;
            case PlaceMachine pm:
                w.Write(TagPlaceMachine);
                w.Write(pm.X); w.Write(pm.Y); w.Write(pm.Z); w.Write(pm.Machine); w.Write(pm.Recipe);
                break;
            case RemoveMachine rm:
                w.Write(TagRemoveMachine); w.Write(rm.Machine);
                break;
            case SetRecipe sr:
                w.Write(TagSetRecipe); w.Write(sr.Machine); w.Write(sr.Recipe);
                break;
            case ConnectToMachine ctm:
                w.Write(TagConnectToMachine); w.Write(ctm.Segment); w.Write(ctm.Machine);
                break;
            case ConnectFromMachine cfm:
                w.Write(TagConnectFromMachine); w.Write(cfm.Machine); w.Write(cfm.Segment);
                break;
            case DisconnectMachine dm:
                w.Write(TagDisconnectMachine); w.Write(dm.Segment); w.Write(dm.Machine);
                break;
            case StartMining sm:
                w.Write(TagStartMining); w.Write(sm.Deposit);
                break;
            case StopMining:
                w.Write(TagStopMining);
                break;
            case SetBoost sb:
                w.Write(TagSetBoost); w.Write(sb.On);
                break;
            case FireLaser fl:
                w.Write(TagFireLaser); w.Write(fl.Firing);
                break;
            case SetShipRecipe ssr:
                w.Write(TagSetShipRecipe); w.Write(ssr.Recipe);
                break;
            case SetResearch sres:
                w.Write(TagSetResearch); w.Write(sres.Node);
                break;
            case LinkDeposit ld:
                w.Write(TagLinkDeposit); w.Write(ld.Machine); w.Write(ld.Deposit);
                break;
            case UnlinkDeposit ud:
                w.Write(TagUnlinkDeposit); w.Write(ud.Machine);
                break;
            case Repair rp:
                w.Write(TagRepair); w.Write(rp.Machine);
                break;
            case SetMovement mv:
                w.Write(TagSetMovement); w.Write(mv.Dx); w.Write(mv.Dy); w.Write(mv.Dz);
                break;
            case SetThrust th:
                w.Write(TagSetThrust); w.Write(th.Dx); w.Write(th.Dy); w.Write(th.Dz);
                break;
            case PlaceModule pmod:
                w.Write(TagPlaceModule); w.Write(pmod.Mx); w.Write(pmod.My); w.Write(pmod.Mz);
                break;
            case RemoveModule rmod:
                w.Write(TagRemoveModule); w.Write(rmod.Mx); w.Write(rmod.My); w.Write(rmod.Mz);
                break;
            case PlaceMachineIn pmi:
                w.Write(TagPlaceMachineIn); w.Write(pmi.Structure);
                w.Write(pmi.X); w.Write(pmi.Y); w.Write(pmi.Z); w.Write(pmi.Type); w.Write(pmi.Recipe);
                break;
            case PlaceSegmentIn psi:
                w.Write(TagPlaceSegmentIn); w.Write(psi.Structure);
                w.Write(psi.X); w.Write(psi.Y); w.Write(psi.Z);
                w.Write((byte)psi.Dir); w.Write(psi.Length); w.Write((byte)psi.Tier);
                break;
            case PlaceModuleIn pmo:
                w.Write(TagPlaceModuleIn); w.Write(pmo.Structure);
                w.Write(pmo.Mx); w.Write(pmo.My); w.Write(pmo.Mz); w.Write((byte)pmo.Kind);
                break;
            case RemoveModuleIn rmo:
                w.Write(TagRemoveModuleIn); w.Write(rmo.Structure); w.Write(rmo.Mx); w.Write(rmo.My); w.Write(rmo.Mz);
                break;
            case FoundStation fs:
                w.Write(TagFoundStation); w.Write(fs.Wx); w.Write(fs.Wy); w.Write(fs.Wz);
                break;
            case AssignDrones ad:
                w.Write(TagAssignDrones); w.Write(ad.Structure); w.Write(ad.Deposit);
                break;
            case DumpItems di:
                w.Write(TagDumpItems); w.Write(di.Item); w.Write(di.Count);
                break;
            case DockAt da:
                w.Write(TagDockAt); w.Write(da.Station); w.Write(da.Mx); w.Write(da.My); w.Write(da.Mz); w.Write((byte)da.Face);
                break;
            case Undock:
                w.Write(TagUndock);
                break;
            case TransferItems ti:
                w.Write(TagTransferItems); w.Write(ti.Item); w.Write(ti.Count); w.Write(ti.ToStation);
                break;
            case Scan:
                w.Write(TagScan);
                break;
            case Jump j:
                w.Write(TagJump); w.Write(j.System);
                break;
            case SetWeapons sw:
                w.Write(TagSetWeapons); w.Write(sw.Armed);
                break;
            case Batch b:
                w.Write(TagBatch);
                w.Write(b.Commands.Length);
                foreach (var inner in b.Commands) WriteCore(w, inner);
                break;
            default:
                throw new ArgumentException($"Unknown command type {command.GetType().Name}.");
        }
    }

    public static Command Read(BinaryReader r) => Read(r, 1);

    private static Command Read(BinaryReader r, int depth)
    {
        byte tag = r.ReadByte();
        switch (tag)
        {
            case TagPlace:
            {
                int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                byte dir = r.ReadByte();
                int len = r.ReadInt32();
                byte tier = r.ReadByte();
                // Out-of-range values are legal to decode; the world rejects them with a reason.
                return new PlaceSegment(x, y, z, (Dir)dir, len, (BeltTier)tier);
            }
            case TagRemove: return new RemoveSegment(r.ReadInt32());
            case TagConnect: return new ConnectSegments(r.ReadInt32(), r.ReadInt32());
            case TagDisconnect: return new DisconnectSegments(r.ReadInt32(), r.ReadInt32());
            case TagSetSource:
            {
                int segment = r.ReadInt32();
                int n = r.ReadInt32();
                if (n < 0 || n > SimWorld.MaxPattern) throw new InvalidDataException($"Source pattern length {n} out of range.");
                var types = new ushort[n];
                for (int i = 0; i < n; i++) types[i] = r.ReadUInt16();
                return new SetSource(segment, types);
            }
            case TagClearSource: return new ClearSource(r.ReadInt32());
            case TagSetSink: return new SetSink(r.ReadInt32());
            case TagClearSink: return new ClearSink(r.ReadInt32());
            case TagPlaceMachine: return new PlaceMachine(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagRemoveMachine: return new RemoveMachine(r.ReadInt32());
            case TagSetRecipe: return new SetRecipe(r.ReadInt32(), r.ReadInt32());
            case TagConnectToMachine: return new ConnectToMachine(r.ReadInt32(), r.ReadInt32());
            case TagConnectFromMachine: return new ConnectFromMachine(r.ReadInt32(), r.ReadInt32());
            case TagDisconnectMachine: return new DisconnectMachine(r.ReadInt32(), r.ReadInt32());
            case TagStartMining: return new StartMining(r.ReadInt32());
            case TagStopMining: return new StopMining();
            case TagSetShipRecipe: return new SetShipRecipe(r.ReadInt32());
            case TagSetResearch: return new SetResearch(r.ReadInt32());
            case TagLinkDeposit: return new LinkDeposit(r.ReadInt32(), r.ReadInt32());
            case TagUnlinkDeposit: return new UnlinkDeposit(r.ReadInt32());
            case TagRepair: return new Repair(r.ReadInt32());
            case TagSetMovement: return new SetMovement(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagSetThrust: return new SetThrust(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPlaceModule: return new PlaceModule(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagRemoveModule: return new RemoveModule(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPlaceMachineIn:
                return new PlaceMachineIn(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPlaceSegmentIn:
            {
                int structure = r.ReadInt32(), x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                byte dir = r.ReadByte();
                int len = r.ReadInt32();
                byte tier = r.ReadByte();
                return new PlaceSegmentIn(structure, x, y, z, (Dir)dir, len, (BeltTier)tier);
            }
            case TagPlaceModuleIn: return new PlaceModuleIn(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (ModuleKind)r.ReadByte());
            case TagRemoveModuleIn: return new RemoveModuleIn(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagFoundStation: return new FoundStation(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagAssignDrones: return new AssignDrones(r.ReadInt32(), r.ReadInt32());
            case TagDumpItems: return new DumpItems(r.ReadInt32(), r.ReadInt32());
            case TagFireLaser: return new FireLaser(r.ReadBoolean());
            case TagSetBoost: return new SetBoost(r.ReadBoolean());
            case TagDockAt:
            {
                int station = r.ReadInt32(), mx = r.ReadInt32(), my = r.ReadInt32(), mz = r.ReadInt32();
                byte face = r.ReadByte();
                return new DockAt(station, mx, my, mz, (Dir)face);
            }
            case TagUndock: return new Undock();
            case TagTransferItems: return new TransferItems(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean());
            case TagScan: return new Scan();
            case TagJump: return new Jump(r.ReadInt32());
            case TagSetWeapons: return new SetWeapons(r.ReadBoolean());
            case TagBatch:
            {
                if (depth >= MaxNesting) throw new InvalidDataException("Batches are nested too deeply.");
                int n = r.ReadInt32();
                if (n < 0 || n > SimWorld.MaxBatch) throw new InvalidDataException($"Batch size {n} out of range.");
                var list = new Command[n];
                for (int i = 0; i < n; i++) list[i] = Read(r, depth + 1);
                return new Batch(list);
            }
            default:
                throw new InvalidDataException($"Unknown command tag {tag}.");
        }
    }

    public static byte[] ToBytes(Command command)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) Write(w, command);
        return ms.ToArray();
    }

    public static Command FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        var command = Read(r);
        if (ms.Position != ms.Length) throw new InvalidDataException("Trailing bytes after the command.");
        return command;
    }

    /// <summary>Writes a log: magic, version, content hash, count, then (tick, player, command) entries.
    /// Pass <see cref="ContentDb.Hash"/> of the content the log was recorded with (0 if there is none).</summary>
    public static void WriteLog(Stream stream, IReadOnlyList<LoggedCommand> log, ulong contentHash = 0, GameMode mode = GameMode.Sandbox,
        ulong galaxySeed = 0, bool threats = false, bool stationRules = false, bool structures = false, WorldRules? rules = null, string? scenarioId = null)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(LogVersion);
        w.Write(contentHash);
        w.Write((byte)mode);
        w.Write(galaxySeed);
        bool hasRules = rules is not null && !rules.IsDefault;
        bool hasScenario = !string.IsNullOrEmpty(scenarioId);
        w.Write((byte)((threats ? 1 : 0) | (stationRules ? 2 : 0) | (structures ? 4 : 0) | (hasRules ? 8 : 0) | (hasScenario ? 16 : 0)));
        if (hasRules)
        {
            byte[] block = rules!.Serialize();
            w.Write(block.Length);
            w.Write(block);
        }
        if (hasScenario)
        {
            byte[] id = Encoding.UTF8.GetBytes(scenarioId!);
            if (id.Length > MaxScenarioIdBytes) throw new ArgumentException("The scenario id is too long.", nameof(scenarioId));
            w.Write(id.Length);
            w.Write(id);
        }
        w.Write(log.Count);
        foreach (var e in log)
        {
            w.Write(e.Tick);
            w.Write(e.Player);
            Write(w, e.Command);
        }
    }

    public static List<LoggedCommand> ReadLog(Stream stream) => ReadLog(stream, out _);

    /// <summary>Reads a log and the hash of the content it was recorded with. Compare it with
    /// <see cref="ContentDb.Hash"/> before replaying: a different hash means the replay would diverge.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash) => ReadLog(stream, out contentHash, out _);

    /// <summary>Also returns the game mode the log was recorded in.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode) =>
        ReadLog(stream, out contentHash, out mode, out _);

    /// <summary>Also returns the galaxy seed the world was created with (0 for a world without a galaxy).</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out _);

    /// <summary>Also returns whether the world had threats switched on.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out _);

    /// <summary>Also returns whether the world used station rules. Version 5 logs have no such flag and read as false.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out stationRules, out _);

    /// <summary>Also returns whether the world used structure rules. Logs written before the flag existed (and version 5 logs) read as false.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules, out bool structures) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out stationRules, out structures, out _);

    /// <summary>Also returns the world rules (flags bit 3, version 7). Logs without the block, including versions 5 and 6, read as default rules.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules, out bool structures, out WorldRules rules) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out stationRules, out structures, out rules, out _);

    /// <summary>Longest scenario id the header carries, in UTF-8 bytes.</summary>
    public const int MaxScenarioIdBytes = 64;

    /// <summary>
    /// Also returns the scenario id (flags bit 4, version 8), or null. A world made from a scenario must be rebuilt with it
    /// (<see cref="SimWorld.ReplayScenario"/>); logs of versions 5 to 7 read as no scenario.
    /// </summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules, out bool structures, out WorldRules rules, out string? scenarioId)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a command log.");
        byte version = r.ReadByte();
        if (version < OldestLogVersion || version > LogVersion) throw new InvalidDataException($"Unsupported log version {version}.");
        contentHash = r.ReadUInt64();
        byte rawMode = r.ReadByte();
        if (rawMode > (byte)GameMode.Campaign) throw new InvalidDataException($"Unknown game mode {rawMode}.");
        mode = (GameMode)rawMode;
        galaxySeed = r.ReadUInt64();
        byte flags = r.ReadByte();
        int knownFlags = version >= 8 ? 31 : version >= 7 ? 15 : version >= 6 ? 7 : 1;
        if (flags > knownFlags) throw new InvalidDataException($"Unknown header flags {flags}.");
        threats = (flags & 1) != 0;
        stationRules = (flags & 2) != 0;
        structures = (flags & 4) != 0;
        rules = WorldRules.Default;
        if ((flags & 8) != 0)
        {
            int length = r.ReadInt32();
            if (length < 1 || length > 4096) throw new InvalidDataException("Bad world rules block length.");
            var block = r.ReadBytes(length);
            if (block.Length != length) throw new InvalidDataException("The world rules block is truncated.");
            rules = WorldRules.Deserialize(block);
        }
        if (version < 9) rules = rules.WithLegacyLaser(true); // older logs were recorded before the laser drew power
        if (version < 10) rules = rules.WithLegacyGalaxy(true); // and before the wide galaxy (ADR 0028)
        scenarioId = null;
        if ((flags & 16) != 0)
        {
            int length = r.ReadInt32();
            if (length < 1 || length > MaxScenarioIdBytes) throw new InvalidDataException("Bad scenario id length.");
            var bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new InvalidDataException("The scenario id is truncated.");
            scenarioId = Encoding.UTF8.GetString(bytes);
        }
        int count = r.ReadInt32();
        if (count < 0) throw new InvalidDataException("Negative entry count.");

        var log = new List<LoggedCommand>();
        long lastTick = long.MinValue;
        for (int i = 0; i < count; i++)
        {
            long tick = r.ReadInt64();
            if (tick < lastTick) throw new InvalidDataException("Log entries are not in tick order.");
            lastTick = tick;
            int player = r.ReadInt32();
            var command = Read(r);
            if (version < 9 && HasFireLaser(command)) throw new InvalidDataException("A log older than version 9 cannot hold a FireLaser command.");
            if (version < 10 && HasBoost(command)) throw new InvalidDataException("A log older than version 10 cannot hold a SetBoost command.");
            if (version < 11 && HasDocking(command)) throw new InvalidDataException("A log older than version 11 cannot hold a docking command.");
            log.Add(new LoggedCommand(tick, player, command));
        }
        return log;
    }

    private static bool HasBoost(Command c) => c is SetBoost || (c is Batch b && b.Commands.Any(HasBoost));
    private static bool HasDocking(Command c) => c is DockAt or Undock or TransferItems || (c is Batch b && b.Commands.Any(HasDocking));

    private static bool HasFireLaser(Command c) => c is FireLaser || (c is Batch b && b.Commands.Any(HasFireLaser));
}
