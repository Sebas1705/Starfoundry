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

/// <summary>Places a one-cell machine of a content type (<see cref="ContentDb.Machines"/> index) with a recipe index, or -1 for none.
/// Inside a batch, negative machine ids refer to machines placed earlier in the same batch (-1 first, -2 second, ...).</summary>
public sealed record PlaceMachine(int X, int Y, int Z, int Machine, int Recipe) : Command;

/// <summary>Removes a machine; everything inside it is destroyed and its belts are detached.</summary>
public sealed record RemoveMachine(int Machine) : Command;

/// <summary>Changes a machine's recipe (-1 clears it). Items inside and the craft in progress are discarded.</summary>
public sealed record SetRecipe(int Machine, int Recipe) : Command;

/// <summary>The end of a belt feeds a machine. The machine cell must touch the belt's last cell by a face.</summary>
public sealed record ConnectToMachine(int Segment, int Machine) : Command;

/// <summary>A machine feeds the start of a belt. The belt's first cell must touch the machine cell by a face.</summary>
public sealed record ConnectFromMachine(int Machine, int Segment) : Command;

/// <summary>Removes the link between a belt and a machine, in whichever direction it exists.</summary>
public sealed record DisconnectMachine(int Segment, int Machine) : Command;

/// <summary>The commanding player starts hand-mining a deposit. Ore flows into their hold until it is full or the deposit is empty.</summary>
public sealed record StartMining(int Deposit) : Command;

public sealed record StopMining : Command;

/// <summary>Sets what the commanding player's ship micro-refinery works on (-1 for nothing).</summary>
public sealed record SetShipRecipe(int Recipe) : Command;

/// <summary>Chooses the research the labs work on (-1 for none).</summary>
public sealed record SetResearch(int Node) : Command;

/// <summary>Sets the commanding player's ship movement intent: -1, 0 or 1 per axis (world axes). The ship keeps
/// moving every tick until a new intent is set, so a held key is one command on press and one on release.</summary>
public sealed record SetMovement(int Dx, int Dy, int Dz) : Command;

/// <summary>Points a drill at a deposit.</summary>
public sealed record LinkDeposit(int Machine, int Deposit) : Command;

public sealed record UnlinkDeposit(int Machine) : Command;

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
    NoContent,
    UnknownMachineType,
    UnknownMachine,
    UnknownRecipe,
    RecipeMismatch,
    TooManyMachines,
    MachineOutputsFull,

    /// <summary>The belt already has a link that excludes this one (for example it feeds a machine and so cannot also feed a belt).</summary>
    SegmentConflict,

    /// <summary>The builder's hold does not contain the cost (campaign mode).</summary>
    NotEnoughItems,

    /// <summary>Not unlocked yet, or not allowed in this game mode.</summary>
    Locked,
    UnknownDeposit,
    UnknownResearch,

    /// <summary>The research is already finished or its requirements are not.</summary>
    ResearchUnavailable,

    /// <summary>The machine type lives on the ship and cannot be placed in the world.</summary>
    NotPlaceable,

    /// <summary>The machine has the wrong role for this command (for example linking a deposit to a smelter).</summary>
    WrongRole,
    Depleted,

    /// <summary>The recipe does not belong to the ship's micro-refinery.</summary>
    NotAShipRecipe,

    /// <summary>The ship is too far away: from the deposit to mine it, or from the cell to build or remove there (campaign).</summary>
    OutOfRange,
}

/// <summary>Outcome of one submitted command.</summary>
/// <param name="Tick">Tick at which the command was applied.</param>
/// <param name="Created">Ids of the segments created, in order.</param>
/// <param name="FailedIndex">For a rejected batch, the index of the command that failed; otherwise -1.</param>
/// <param name="CreatedMachineIds">Ids of the machines created, in order.</param>
public sealed record CommandResult(
    long Tick, int Player, Command Command, Reject Reject, IReadOnlyList<int> Created, int FailedIndex = -1,
    IReadOnlyList<int>? CreatedMachineIds = null)
{
    public bool Ok => Reject == Reject.None;

    public IReadOnlyList<int> CreatedMachines => CreatedMachineIds ?? Array.Empty<int>();
}

/// <summary>A command as it was applied, for replays and network sync.</summary>
public sealed record LoggedCommand(long Tick, int Player, Command Command);
