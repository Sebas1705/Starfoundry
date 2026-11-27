using System.Security.Cryptography;
using System.Text;

namespace Starfoundry.Ui;

/// <summary>Why a save file cannot be used. The file is never touched because of it.</summary>
public enum SaveProblem { NotASave, Truncated, Corrupt, NewerFormat }

/// <summary>A save file that is not usable: <see cref="Problem"/> says why; the message is for logs, the interface shows a localised text for the problem.</summary>
public sealed class SaveFileException : IOException
{
    public SaveProblem Problem { get; }

    public SaveFileException(SaveProblem problem, string message) : base(message) => Problem = problem;
}

/// <summary>The parts of a save file: what it says about itself, a small screenshot (JPEG, may be empty), the goals sidecar text of a scenario game, and the save itself (<c>SaveGame</c> bytes).</summary>
public sealed record SaveParts(SaveMeta Meta, byte[] Thumbnail, string Goals, byte[] Payload);

/// <summary>
/// The container of one save slot (ADR 0049): <c>SFSL</c>, a format byte, then length-prefixed blocks (metadata text, thumbnail, goals text, the
/// <c>SaveGame</c> bytes) and a SHA-256 of everything before it. The checksum makes a truncated or edited file recognisable without replaying it.
/// The payload is exactly what <c>SaveGame.Write</c> produces, so replaying a slot is the same code as before. Files written by older versions
/// (a bare <c>SFSV</c> save) are recognised by <see cref="IsLegacy"/> and wrapped once by the migration.
/// </summary>
public static class SaveFile
{
    public const string Extension = ".sfslot";
    public const byte Format = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SFSL");
    private static readonly byte[] LegacyMagic = Encoding.ASCII.GetBytes("SFSV");
    private const int HashLength = 32;
    public const int MaxMeta = 16 * 1024, MaxThumbnail = 512 * 1024, MaxGoals = 1024 * 1024, MaxPayload = 256 * 1024 * 1024;

    public static byte[] Pack(SaveParts parts)
    {
        byte[] meta = Encoding.UTF8.GetBytes(SaveMetaText.Serialize(parts.Meta));
        byte[] goals = Encoding.UTF8.GetBytes(parts.Goals ?? "");
        if (meta.Length > MaxMeta || parts.Thumbnail.Length > MaxThumbnail || goals.Length > MaxGoals || parts.Payload.Length > MaxPayload)
            throw new ArgumentException("A part of the save is too large.");
        using var ms = new MemoryStream(parts.Payload.Length + parts.Thumbnail.Length + meta.Length + goals.Length + 64);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Format);
            foreach (var block in new[] { meta, parts.Thumbnail, goals, parts.Payload }) { w.Write(block.Length); w.Write(block); }
        }
        byte[] hash = SHA256.HashData(ms.GetBuffer().AsSpan(0, (int)ms.Length));
        ms.Write(hash);
        return ms.ToArray();
    }

    /// <summary>Reads and verifies a file. Throws <see cref="SaveFileException"/> for a file that is not a slot, is cut short, fails its checksum or is from a newer format.</summary>
    public static SaveParts Unpack(byte[] bytes)
    {
        if (bytes.Length < Magic.Length + 1 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new SaveFileException(SaveProblem.NotASave, "Not a save slot file.");
        byte format = bytes[Magic.Length];
        if (format > Format) throw new SaveFileException(SaveProblem.NewerFormat, $"The save slot format {format} is newer than this game understands.");
        if (bytes.Length < Magic.Length + 1 + 4 * 4 + HashLength) throw new SaveFileException(SaveProblem.Truncated, "The save slot file is cut short.");
        int bodyLength = bytes.Length - HashLength;
        if (!SHA256.HashData(bytes.AsSpan(0, bodyLength)).AsSpan().SequenceEqual(bytes.AsSpan(bodyLength)))
            throw new SaveFileException(SaveProblem.Corrupt, "The checksum of the save slot does not match: the file is damaged.");
        int pos = Magic.Length + 1;
        byte[] Block(int max)
        {
            if (pos + 4 > bodyLength) throw new SaveFileException(SaveProblem.Truncated, "The save slot file is cut short.");
            int n = BitConverter.ToInt32(bytes, pos);
            pos += 4;
            if (n < 0 || n > max || pos + n > bodyLength) throw new SaveFileException(SaveProblem.Corrupt, "A block of the save slot has an impossible length.");
            var block = bytes.AsSpan(pos, n).ToArray();
            pos += n;
            return block;
        }
        byte[] meta = Block(MaxMeta), thumb = Block(MaxThumbnail), goals = Block(MaxGoals), payload = Block(MaxPayload);
        return new SaveParts(SaveMetaText.Parse(Encoding.UTF8.GetString(meta)), thumb, Encoding.UTF8.GetString(goals), payload);
    }

    /// <summary>True for a bare <c>SaveGame</c> file, the only format of versions before the slots.</summary>
    public static bool IsLegacy(byte[] bytes) => bytes.Length >= LegacyMagic.Length && bytes.AsSpan(0, LegacyMagic.Length).SequenceEqual(LegacyMagic);

    // The fixed layout of a SaveGame payload: "SFSV", version, ticks (8), checksum (8), player (4), then the command log: "SFCL", version, content hash (8)...
    private const int LogHeaderAt = 4 + 1 + 8 + 8 + 4;

    /// <summary>The command log version inside a <c>SaveGame</c> payload, or 0 when it is too short to say.</summary>
    public static int LogVersionOf(byte[] payload) => payload.Length > LogHeaderAt + 4 ? payload[LogHeaderAt + 4] : 0;

    /// <summary>The content hash the log of a <c>SaveGame</c> payload was recorded with, or 0 when it is too short to say.</summary>
    public static ulong ContentHashOf(byte[] payload) => payload.Length >= LogHeaderAt + 5 + 8 ? BitConverter.ToUInt64(payload, LogHeaderAt + 5) : 0;
}
