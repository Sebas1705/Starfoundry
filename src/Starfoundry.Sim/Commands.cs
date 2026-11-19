namespace Starfoundry.Sim;

/// <summary>Axis-aligned belt direction.</summary>
public enum Dir : byte
{
    PosX = 0,
    NegX = 1,
    PosY = 2,
    NegY = 3,
    PosZ = 4,
    NegZ = 5,
}

public static class Dirs
{
    public static (int X, int Y, int Z) Vector(Dir d) => d switch
    {
        Dir.PosX => (1, 0, 0),
        Dir.NegX => (-1, 0, 0),
        Dir.PosY => (0, 1, 0),
        Dir.NegY => (0, -1, 0),
        Dir.PosZ => (0, 0, 1),
        Dir.NegZ => (0, 0, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public static bool IsValid(Dir d) => (byte)d <= (byte)Dir.NegZ;
}

/// <summary>
/// A request to change the world. Commands are the only way gameplay code changes the simulation:
/// they are validated, applied at a tick boundary in submission order, logged, and replayable.
/// Segment ids below zero inside a <see cref="Batch"/> refer to earlier placements of that batch:
/// -1 is the first <see cref="PlaceSegment"/>, -2 the second, and so on.
/// </summary>
public abstract record Command;

/// <summary>Places a straight belt segment starting at the given cell and running in a direction.</summary>
public sealed record PlaceSegment(int X, int Y, int Z, Dir Direction, int LengthCells, BeltTier Tier) : Command;

public sealed record RemoveSegment(int Segment) : Command;

/// <summary>Connects the end of one segment to the start of another. The first cell of <see cref="To"/>
/// must touch the last cell of <see cref="From"/> by a face (straight ahead or to a side).</summary>
public sealed record ConnectSegments(int From, int To) : Command;

public sealed record DisconnectSegments(int From, int To) : Command;

public sealed record SetSource(int Segment, ushort[] ItemTypes) : Command;

public sealed record ClearSource(int Segment) : Command;

public sealed record SetSink(int Segment) : Command;

public sealed record ClearSink(int Segment) : Command;

/// <summary>Several commands applied atomically: either all succeed or the world is left untouched.</summary>
public sealed record Batch(Command[] Commands) : Command;

public enum Reject
{
    None = 0,
    InvalidArgument,
    OutOfBounds,
    Overlap,
    TooManySegments,
    UnknownSegment,
    SelfConnection,
    NotAdjacent,
    OutputsFull,
    AlreadyConnected,
    NotConnected,
    FromIsSink,
    SinkHasOutputs,
    NestedBatch,
}

/// <summary>Outcome of one submitted command.</summary>
/// <param name="Tick">Tick at which the command was applied.</param>
/// <param name="Created">Ids of the segments created, in order.</param>
/// <param name="FailedIndex">For a rejected batch, the index of the command that failed; otherwise -1.</param>
public sealed record CommandResult(
    long Tick, int Player, Command Command, Reject Reject, IReadOnlyList<int> Created, int FailedIndex = -1)
{
    public bool Ok => Reject == Reject.None;
}

/// <summary>A command as it was applied, for replays and network sync.</summary>
public sealed record LoggedCommand(long Tick, int Player, Command Command);
