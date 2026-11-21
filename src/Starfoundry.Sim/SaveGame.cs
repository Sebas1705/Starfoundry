using System.Text;

namespace Starfoundry.Sim;

/// <summary>What a save file says about the game it holds.</summary>
public sealed record SaveInfo(long Ticks, ulong Checksum, GameMode Mode, ulong GalaxySeed, bool Threats, int Player, bool StationRules = false);

/// <summary>
/// Saving and loading by replay. A save is the command log of the world plus the number of ticks played and the checksum
/// at that moment; loading creates the same starting world and replays the log, which the deterministic simulation
/// ([ADR 0002](docs/adr/0002-deterministic-simulation-core.md)) guarantees ends in the same state. The checksum is
/// checked, so a save that no longer replays (other content, a corrupt file) is refused instead of loaded wrong.
/// Only what went through commands is saved: setup done by calling the network directly is not part of a save.
/// </summary>
public static class SaveGame
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SFSV");
    private const byte Version = 1;

    /// <summary>Writes the save: magic, version, ticks, checksum, the player, then the command log with its header.</summary>
    public static void Write(Stream stream, SimWorld world, ContentDb content, GameMode mode, ulong galaxySeed, bool threats, int player, bool stationRules = false)
    {
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(world.Tick);
            w.Write(world.Checksum());
            w.Write(player);
        }
        CommandCodec.WriteLog(stream, world.Log, content.Hash, mode, galaxySeed, threats, stationRules);
    }

    /// <summary>Reads only the header (how long the game was, its mode and seed), without replaying anything.</summary>
    public static SaveInfo Peek(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var (ticks, checksum, player) = ReadHeader(r);
        CommandCodec.ReadLog(stream, out _, out var mode, out ulong seed, out bool threats, out bool stationRules);
        return new SaveInfo(ticks, checksum, mode, seed, threats, player, stationRules);
    }

    /// <summary>
    /// Rebuilds the saved world. Throws <see cref="InvalidDataException"/> if the file is not a save, was recorded with
    /// other content, or does not replay to the state it was saved in.
    /// </summary>
    public static SimWorld Read(Stream stream, ContentDb content, out SaveInfo info)
    {
        long ticks;
        ulong checksum;
        int player;
        using (var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            (ticks, checksum, player) = ReadHeader(r);

        var log = CommandCodec.ReadLog(stream, out ulong hash, out var mode, out ulong seed, out bool threats, out bool stationRules);
        if (hash != content.Hash) throw new InvalidDataException("The save was made with other game content and cannot be loaded.");
        if (ticks < 0 || ticks > 4_000_000_000L) throw new InvalidDataException("The save has an impossible length.");

        var initial = seed != 0 ? SimWorld.CreateGalaxy(content, mode, seed, threats, stationRules) : new SimWorld(content, mode);
        initial.Network.EnsurePlayer(player);
        var world = SimWorld.Replay(log, ticks, initial);
        if (world.Tick != ticks || world.Checksum() != checksum)
            throw new InvalidDataException("The save did not replay to the state it was saved in.");

        info = new SaveInfo(ticks, checksum, mode, seed, threats, player, stationRules);
        return world;
    }

    private static (long Ticks, ulong Checksum, int Player) ReadHeader(BinaryReader r)
    {
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a save file.");
        byte version = r.ReadByte();
        if (version != Version) throw new InvalidDataException($"Unsupported save version {version}.");
        return (r.ReadInt64(), r.ReadUInt64(), r.ReadInt32());
    }
}
