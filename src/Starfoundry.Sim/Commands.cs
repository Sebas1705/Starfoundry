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

/// <summary>Pulls (true) or releases (false) the trigger of the commanding player's mining laser without changing the mining order (ADR 0027).
/// A released laser mines nothing and draws no power; the partial item is kept. Needs a mining order. <see cref="StartMining"/> pulls the trigger.</summary>
public sealed record FireLaser(bool Firing) : Command;

/// <summary>Switches the commanding player's ship boost on or off (ADR 0028): while on, the engines run at the content's boost percent of their speed.
/// Rejected with <see cref="Reject.NotAllowedHere"/> when the content has no boost. Boost is switched off by a jump, a lost ship and the end of the world.</summary>
public sealed record SetBoost(bool On) : Command;

/// <summary>Sets what the commanding player's ship micro-refinery works on (-1 for nothing).</summary>
public sealed record SetShipRecipe(int Recipe) : Command;

/// <summary>
/// Adds <paramref name="Count"/> crafts of a workbench recipe to the ship's queue (ADR 0039; at most <see cref="PlayerRules.BenchQueueMax"/> waiting in all). The bench works
/// one craft at a time, taking the inputs from the hold when a craft starts. Refused with <see cref="Reject.NotAllowedHere"/> in a world with
/// <see cref="WorldRules.LegacyShipAutomation"/> (those use <see cref="SetShipRecipe"/>), <see cref="Reject.NoRoom"/> when the queue is full.
/// </summary>
public sealed record QueueBench(int Recipe, int Count = 1) : Command;

/// <summary>Cancels the craft in progress (its inputs return to the hold) and empties the workbench queue (ADR 0039).</summary>
public sealed record ClearBench : Command;

/// <summary>Chooses the research the labs work on (-1 for none).</summary>
public sealed record SetResearch(int Node) : Command;

/// <summary>Sets the commanding player's ship movement intent: -1, 0 or 1 per axis (world axes). The ship keeps
/// moving every tick until a new intent is set, so a held key is one command on press and one on release.</summary>
public sealed record SetMovement(int Dx, int Dy, int Dz) : Command;

/// <summary>Sets the commanding player's ship thrust: an integer direction vector with each component in
/// [-1024, 1024] and a squared length of at most 1024 squared (the game sends a unit vector scaled by 1024).
/// The ship moves along it every tick at <c>|thrust| * speed / 1024</c>; (0, 0, 0) stops it. Out of range is
/// rejected, not clamped. It replaces the axis intent of <see cref="SetMovement"/> and vice versa.</summary>
public sealed record SetThrust(int Dx, int Dy, int Dz) : Command;

/// <summary>Switches the commanding player's ship weapon on or off. While on it fires at the nearest enemy in range.</summary>
public sealed record SetWeapons(bool Armed) : Command;

/// <summary>Fires the commanding player's scanner: hidden deposits within range of the ship are revealed.</summary>
public sealed record Scan : Command;

/// <summary>Charges a jump along a lane to a connected, known system and teleports the ship to its anchor.</summary>
public sealed record Jump(int System) : Command;

/// <summary>Points a drill at a deposit.</summary>
public sealed record LinkDeposit(int Machine, int Deposit) : Command;

public sealed record UnlinkDeposit(int Machine) : Command;

/// <summary>
/// Restores a damaged machine to full hit points. In campaign mode the ship must be within building range and the hold
/// pays half the build cost scaled by the share of hit points missing (at least one of each item); in sandbox it is free.
/// </summary>
public sealed record Repair(int Machine) : Command;

/// <summary>Adds a station module at module-grid coordinates (see <see cref="StationRules"/>). The first one becomes the core;
/// later ones must share a face with an existing module. The cost is paid from the hold.</summary>
public sealed record PlaceModule(int Mx, int My, int Mz) : Command;

/// <summary>Removes a station module if nothing is built inside it and the station stays connected. Refunds half the cost.</summary>
public sealed record RemoveModule(int Mx, int My, int Mz) : Command;

/// <summary>Places a machine at a local cell of a structure (0 = the legacy world, as <see cref="PlaceMachine"/>). The cell must lie in a module whose kind allows the machine's role.</summary>
public sealed record PlaceMachineIn(int Structure, int X, int Y, int Z, int Type, int Recipe) : Command;

/// <summary>Places a belt segment at local cells of a structure. Every cell must lie in a module that allows belts.</summary>
public sealed record PlaceSegmentIn(int Structure, int X, int Y, int Z, Dir Dir, int Length, BeltTier Tier) : Command;

/// <summary>Adds a module of a kind to a structure at module-grid coordinates. The first module of a structure sets its core
/// (a station's must be <see cref="ModuleKind.Core"/>); later ones must share a face with an existing module. The cost is paid from the hold.</summary>
public sealed record PlaceModuleIn(int Structure, int Mx, int My, int Mz, ModuleKind Kind) : Command;

/// <summary>Removes a module of a structure if nothing is built inside it and the structure stays connected. Refunds half the cost.</summary>
public sealed record RemoveModuleIn(int Structure, int Mx, int My, int Mz) : Command;

/// <summary>Founds a station: a new structure owned by the commanding player with a <see cref="ModuleKind.Core"/> module at module (0,0,0),
/// whose world origin is the corner of the world cell (Wx, Wy, Wz). The cell must be within build range of the ship and the
/// core's volume must not overlap any other structure. The core's cost is paid from the hold in campaign mode. Needs structure rules.</summary>
public sealed record FoundStation(int Wx, int Wy, int Wz) : Command;

/// <summary>Assigns the deposit that the drone bays of a structure mine (-1 clears it). The deposit must be revealed and within the bays'
/// range of the structure, and the structure needs a drone bay module. Needs structure rules.</summary>
public sealed record AssignDrones(int Structure, int Deposit) : Command;

/// <summary>Throws items out of the commanding player's hold; they are lost. A <see cref="Count"/> of zero or less, or more than
/// the hold has, discards every item of the kind. The item must exist and the hold must hold at least one.</summary>
public sealed record DumpItems(int Item, int Count) : Command;

/// <summary>Docks the commanding player's ship at a port of a station (ADR 0029): <see cref="Face"/> is the horizontal face (PosX, NegX, PosZ or NegZ)
/// of the station module at (<see cref="Mx"/>, <see cref="My"/>, <see cref="Mz"/>) whose port the ship joins. The ship's own port is the free port on the opposite face
/// that is nearest. The ship must be within <see cref="DockRules.RangeCells"/> of the port and slower than <see cref="DockRules.MaxSpeedUnitsPerTick"/>; it is then
/// locked to the port pose until <see cref="Undock"/>. Needs structure rules.</summary>
public sealed record DockAt(int Station, int Mx, int My, int Mz, Dir Face) : Command;

/// <summary>Releases the commanding player's docked ship, which keeps its position and can thrust again.</summary>
public sealed record Undock : Command;

/// <summary>Moves items between the docked ship's hold and the stock of the station it is docked at (ADR 0029). <see cref="ToStation"/> unloads the hold into the
/// stock, otherwise the stock is loaded into the hold. A <see cref="Count"/> of zero or less, or more than is available or fits, moves as many as possible.</summary>
public sealed record TransferItems(int Item, int Count, bool ToStation) : Command;

/// <summary>Sets the attitude of the commanding player's ship (ADR 0030): a unit quaternion (W, X, Y, Z) in fixed point, 16384 being 1.0 (<see cref="AttitudeRules.Scale"/>).
/// The ship's modules, ports and every range measured from them are turned about the pilot seat. Rejected with <see cref="Reject.InvalidArgument"/> when
/// the quaternion is not of unit length within <see cref="AttitudeRules.NormTolerance"/>, and with <see cref="Reject.Docked"/> for a turn while docked.</summary>
public sealed record SetAttitude(int W, int X, int Y, int Z) : Command;

/// <summary>Replaces the whole module list of the commanding player's ship (ADR 0032): the ship designer's apply. The ship must be docked at one of the
/// player's stations that has a shipyard module. The design is validated like a sequence of single placements and removals (a cockpit, an engine, a reactor and
/// a cargo module, no core, connected, nothing removed that holds a machine or belt, the docked port kept, no overlap with the station, the hold still fits) and
/// priced: modules that only move are free, a removed module refunds half its cost, the hold pays first and the station stock the rest. All or nothing.</summary>
public sealed record DesignerApply(DesignModule[] Modules) : Command;

/// <summary>
/// Replaces the whole module list of one of the player's structures in a single atomic step (ADR 0043), the blueprint editor's apply: a station, or the player's ship
/// (as <see cref="DesignerApply"/> does). The verdict and the price are exactly <see cref="SimWorld.CheckBlueprint"/>, which the client previews, so the two cannot disagree.
/// A station keeps its core and at least one dock module, takes no cockpit, engine or hardpoint, stays one connected piece and loses no module that holds a machine or belt
/// or the port a ship is docked with; modules that only move are free, a removed module refunds half its cost and the hold pays first and the station stock the rest.
/// Refused with <see cref="Reject.NotAllowedHere"/> in a world with <see cref="WorldRules.LegacyBlueprint"/>.
/// </summary>
public sealed record ApplyBlueprint(int Structure, DesignModule[] Modules) : Command;

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

    /// <summary>The ship is too far away: from the deposit to mine it, or from the cell to build or remove there (campaign);
    /// or a drill is too far from the deposit it should link to.</summary>
    OutOfRange,

    /// <summary>The world has no galaxy.</summary>
    NoGalaxy,
    UnknownSystem,

    /// <summary>The ship is in deep space, not near a system anchor.</summary>
    NotInASystem,

    /// <summary>No lane connects the current system to the target.</summary>
    NoLane,

    /// <summary>A jump is already charging.</summary>
    Busy,
    OnCooldown,

    /// <summary>The deposit is still hidden: scan near it first.</summary>
    NotDiscovered,

    /// <summary>The machine has all its hit points.</summary>
    NotDamaged,

    /// <summary>The cells are not inside a station module. (A module request not touching the station uses the existing <see cref="NotAdjacent"/>.)</summary>
    OutsideStation,

    /// <summary>A module cannot be removed while a machine or belt lies inside it, or while removing it would split the station.</summary>
    ModuleOccupied,

    /// <summary>This kind of structure is not allowed here in this mode.</summary>
    NotAllowedHere,

    /// <summary>The module under the cell is of a kind that does not allow this machine or belt.</summary>
    KindNotAllowed,

    /// <summary>The command names a structure that does not exist.</summary>
    NoSuchStructure,

    /// <summary>With structure rules on, the ship structure needs a cockpit and an engine module before it can fly.</summary>
    NoCockpitOrEngine,

    /// <summary>The structure has no drone bay module to give work to.</summary>
    NoDroneBay,

    /// <summary>The module is the last cockpit, engine, reactor or cargo module of the player's ship, which the ship cannot do without.</summary>
    LastEssentialModule,

    /// <summary>The ship is docked: it cannot thrust or jump until it undocks.</summary>
    Docked,

    /// <summary>The ship is not docked at a station.</summary>
    NotDocked,

    /// <summary>The station has no free port there, or the ship has no free port on the opposite face.</summary>
    NoFreePort,

    /// <summary>The ship is moving faster than <see cref="DockRules.MaxSpeedUnitsPerTick"/>: slow down before docking.</summary>
    TooFast,

    /// <summary>The hold or the station stock has no room for the items.</summary>
    NoRoom,

    /// <summary>The ship is already docked.</summary>
    AlreadyDocked,

    /// <summary>The ship is turned more than about 15 degrees away from the axes of the port it wants to dock at.</summary>
    Misaligned,

    /// <summary>Machines and belts are built on the floor of a module only (ADR 0031): the cell is above the floor layer of its module.</summary>
    NotOnFloor,
    /// <summary>The ship has no mining laser module (ADR 0032), so it cannot mine.</summary>
    NoLaser,

    /// <summary>The ship has no gun module (ADR 0032), so it cannot shoot.</summary>
    NoGun,

    /// <summary>The ship is not docked at a station of its owner that has a shipyard module, so it cannot be redesigned.</summary>
    NoShipyard,

    /// <summary>The module or core would stand inside the rock of a deposit (ADR 0033: deposits are solid): found or extend the station beside it.</summary>
    OverlapsDeposit,

    /// <summary>The item does not belong in the slot or container it was moved to (ADR 0035): a reactor takes fuel only, a machine only what it uses, and nothing is put into an output buffer.</summary>
    WrongItem,

    /// <summary>The ship design has no dock module (ADR 0041): a ship always carries the dedicated module it docks with.</summary>
    NoDockModule,

    /// <summary>The module is the last dock module of its ship or station (ADR 0041), which cannot do without one.</summary>
    LastDockModule,

    /// <summary>Automation is built in a station (ADR 0039): a ship holds no machines, belts, turrets, walls or Fabrication bays.</summary>
    UseStation,

    /// <summary>Stations are edited in the blueprint editor (ADR 0043): the module tool and the in-world placement of station parts are refused, send ApplyBlueprint.</summary>
    UseBlueprint,
}

/// <summary>Outcome of one submitted command.</summary>
/// <param name="Tick">Tick at which the command was applied.</param>
/// <param name="Created">Ids of the segments created, in order.</param>
/// <param name="FailedIndex">For a rejected batch, the index of the command that failed; otherwise -1.</param>
/// <param name="CreatedMachineIds">Ids of the machines created, in order.</param>
/// <param name="RevealedDepositIds">Ids of the deposits a scan revealed.</param>
public sealed record CommandResult(
    long Tick, int Player, Command Command, Reject Reject, IReadOnlyList<int> Created, int FailedIndex = -1,
    IReadOnlyList<int>? CreatedMachineIds = null, IReadOnlyList<int>? RevealedDepositIds = null)
{
    public bool Ok => Reject == Reject.None;

    public IReadOnlyList<int> CreatedMachines => CreatedMachineIds ?? Array.Empty<int>();

    public IReadOnlyList<int> RevealedDeposits => RevealedDepositIds ?? Array.Empty<int>();
}

/// <summary>A command as it was applied, for replays and network sync.</summary>
public sealed record LoggedCommand(long Tick, int Player, Command Command);
