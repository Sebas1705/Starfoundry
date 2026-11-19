using System.Text;

namespace Starfoundry.Sim;

/// <summary>
/// Compact binary format for commands and command logs, used for saving replays and, later, for the
/// network. Little-endian, versioned, and strict when reading: sizes are bounded and unknown tags are errors.
/// </summary>
public static class CommandCodec
{
    private const byte TagPlace = 1, TagRemove = 2, TagConnect = 3, TagDisconnect = 4,
        TagSetSource = 5, TagClearSource = 6, TagSetSink = 7, TagClearSink = 8, TagBatch = 9;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SFCL");
    private const byte LogVersion = 1;

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
                or ClearSource or SetSink or ClearSink:
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

    /// <summary>Writes a log: magic, version, count, then (tick, player, command) entries.</summary>
    public static void WriteLog(Stream stream, IReadOnlyList<LoggedCommand> log)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(LogVersion);
        w.Write(log.Count);
        foreach (var e in log)
        {
            w.Write(e.Tick);
            w.Write(e.Player);
            Write(w, e.Command);
        }
    }

    public static List<LoggedCommand> ReadLog(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a command log.");
        byte version = r.ReadByte();
        if (version != LogVersion) throw new InvalidDataException($"Unsupported log version {version}.");
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
            log.Add(new LoggedCommand(tick, player, Read(r)));
        }
        return log;
    }
}
