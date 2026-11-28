namespace Starfoundry.Sim;

public enum StructureKind { Ship, Station }

public enum ModuleKind { Core, Cockpit, Engine, Reactor, Cargo, Workshop, Defence, Hull, DroneBay, Shield, Sensor, ReactorMk2, Dock, MiningLaser, MiningLaserMk2, Gun, GunMk2, Shipyard, SensorShort, SensorLong, CommsAntenna, GunHeavy }

/// <summary>Content ids of module kinds: the enum name in snake case (<c>DroneBay</c> is <c>drone_bay</c>).</summary>
public static class ModuleKindIds
{
    /// <summary>True for the module kinds that burn fuel and make power: the reactor and the mark 2 reactor.</summary>
    public static bool IsReactor(ModuleKind kind) => kind is ModuleKind.Reactor or ModuleKind.ReactorMk2;

    /// <summary>True for the hardpoint kinds (ADR 0032): the mining lasers and the guns of a ship.</summary>
    public static bool IsHardpoint(ModuleKind kind) => kind is ModuleKind.MiningLaser or ModuleKind.MiningLaserMk2 or ModuleKind.Gun or ModuleKind.GunMk2 or ModuleKind.GunHeavy;

    /// <summary>True for the mining laser module and its mark 2.</summary>
    public static bool IsMiningLaser(ModuleKind kind) => kind is ModuleKind.MiningLaser or ModuleKind.MiningLaserMk2;

    /// <summary>True for every antenna kind (ADR 0042): the sensor, the short-range and long-range sensors and the comms antenna.</summary>
    public static bool IsAntenna(ModuleKind kind) => kind is ModuleKind.Sensor or ModuleKind.SensorShort or ModuleKind.SensorLong or ModuleKind.CommsAntenna;

    /// <summary>True for the kinds ADR 0042 added (the antenna variants and the heavy cannon): a world with <see cref="WorldRules.LegacyArms"/> refuses them.</summary>
    public static bool IsArmsKind(ModuleKind kind) => kind is ModuleKind.SensorShort or ModuleKind.SensorLong or ModuleKind.CommsAntenna or ModuleKind.GunHeavy;

    /// <summary>True for the gun module, its mark 2 and the heavy cannon.</summary>
    public static bool IsGun(ModuleKind kind) => kind is ModuleKind.Gun or ModuleKind.GunMk2 or ModuleKind.GunHeavy;

    /// <summary>The next mark of a hardpoint (the mining laser becomes the mark 2, the gun becomes the mark 2), or null when there is none.</summary>
    public static ModuleKind? UpgradeOf(ModuleKind kind) => kind switch
    {
        ModuleKind.MiningLaser => ModuleKind.MiningLaserMk2,
        ModuleKind.Gun => ModuleKind.GunMk2,
        _ => null,
    };

    /// <summary>
    /// The one catalogue rule of ship and station editing (ADR 0029, ADR 0032): whether a kind may be part of a structure of this kind. A station has no
    /// cockpit, engine or hardpoint; a ship has no core and no shipyard. Everything else (hull, cargo, reactors, workshops, defence, bays, shields,
    /// sensors, docks) is shared by both.
    /// </summary>
    public static bool AllowedOn(StructureKind structure, ModuleKind kind) => structure == StructureKind.Station
        ? kind is not (ModuleKind.Cockpit or ModuleKind.Engine) && !IsHardpoint(kind)
        : kind is not (ModuleKind.Core or ModuleKind.Shipyard);

    /// <summary>
    /// <see cref="AllowedOn(StructureKind, ModuleKind)"/> for a world that may be station-first (ADR 0039): with <paramref name="stationFirst"/> a ship takes no Fabrication bay,
    /// since automation is built in a station.
    /// </summary>
    public static bool AllowedOn(StructureKind structure, ModuleKind kind, bool stationFirst) =>
        AllowedOn(structure, kind) && !(stationFirst && structure == StructureKind.Ship && kind == ModuleKind.Workshop);

    /// <summary>The ability research must unlock before this kind can be placed in a campaign world, or null when it is free.</summary>
    /// <summary>
    /// The side, in module cells, of the biggest span (large module, ADR 0051) a structure of this kind may hold of a module kind: 1 means the kind stays single. Stations take big bays,
    /// holds and hulls; ships only a hold and a hull (a ship holds no automation, ADR 0039); cores, cockpits, engines, hardpoints, antennas, shields and docks stay single.
    /// </summary>
    public static int MaxSpan(StructureKind structure, ModuleKind kind) => structure == StructureKind.Station
        ? kind switch { ModuleKind.Workshop or ModuleKind.Cargo or ModuleKind.Hull => 3, ModuleKind.Reactor or ModuleKind.Defence or ModuleKind.DroneBay => 2, _ => 1 }
        : kind switch { ModuleKind.Cargo or ModuleKind.Hull => 2, _ => 1 };

    /// <summary>Percent of its price a module costs when it is built as part of a span (ADR 0051).</summary>
    public const int SpanPricePercent = 85;

    public static Ability? RequiredAbility(ModuleKind kind) => kind switch
    {
        ModuleKind.DroneBay => Ability.DroneBay,
        ModuleKind.Shield => Ability.Shield,
        ModuleKind.Sensor or ModuleKind.SensorShort or ModuleKind.SensorLong or ModuleKind.CommsAntenna => Ability.Sensor,
        ModuleKind.ReactorMk2 => Ability.ReactorMk2,
        _ => null,
    };

    public static string Id(ModuleKind kind)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in kind.ToString())
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>Bounds of a structure's local grid. Structure 0 (the legacy world) keeps the world limits instead.</summary>
public static class StructureGrid
{
    /// <summary>Largest absolute local cell coordinate per axis for structures other than 0.</summary>
    public const int LocalLimit = 512;

    public static bool InLocalBounds(long v) => v >= -LocalLimit && v <= LocalLimit;

    /// <summary>True if every cell of the module (module size is <see cref="StationGrid"/>) lies inside the local grid limit.</summary>
    public static bool IsValidLocalModule(long mx, long my, long mz) =>
        InLocalBounds(mx * StationGrid.ModuleX) && InLocalBounds(mx * StationGrid.ModuleX + StationGrid.ModuleX - 1)
        && InLocalBounds(my * StationGrid.ModuleY) && InLocalBounds(my * StationGrid.ModuleY + StationGrid.ModuleY - 1)
        && InLocalBounds(mz * StationGrid.ModuleZ) && InLocalBounds(mz * StationGrid.ModuleZ + StationGrid.ModuleZ - 1);
}

/// <summary>Fixed rules of flying a ship structure. Part of the simulation, so changing them changes states.</summary>
public static class StructureFlight
{
    /// <summary>Where the pilot sits relative to the ship structure origin, in units (the player position is origin + this offset).</summary>
    public const int CockpitOffsetX = 0, CockpitOffsetY = 0, CockpitOffsetZ = 0;

    /// <summary>Engine speed percentages are percent of <see cref="PlayerRules.MoveUnitsPerTick"/>: this is the denominator (100).</summary>
    public const int PercentScale = 100;

    /// <summary>Denominator of the sub-unit flight remainders: the percent scale times the power satisfaction scale (1000).</summary>
    public const int RemainderScale = PercentScale * 1000;

    /// <summary>The summed engine speed is clamped to this many percent of the base speed (4x).</summary>
    public const int MaxSpeedPercent = 400;
}

/// <summary>
/// A ship or station: a local cell grid with an integer world origin (in units). Machines and belt segments
/// belong to a structure and use its local cell coordinates. Structure 0 is the implicit legacy world
/// (origin 0, no registry entry).
/// </summary>
public sealed partial class Structure
{
    public int Id { get; }
    public StructureKind Kind { get; }
    public int Owner { get; }
    public long OriginX { get; internal set; }
    public long OriginY { get; internal set; }
    public long OriginZ { get; internal set; }

    /// <summary>Modules by local module coordinate (module size is <see cref="StationGrid"/>), sorted for determinism.</summary>
    internal SortedDictionary<(int Mx, int My, int Mz), ModuleKind> ModuleMap { get; private set; } = new();

    public IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> Modules => ModuleMap;

    /// <summary>Ticks left of the current burn per reactor module (only burning reactors have an entry).</summary>
    internal SortedDictionary<(int Mx, int My, int Mz), int> BurnRemaining { get; private set; } = new();

    /// <summary>Power produced, power demanded and the satisfaction (per mille) of the last tick. See <c>BeltNetwork.PowerStatus</c>.</summary>
    /// <summary>The deposit this structure's drone bays mine, or -1. See <c>BeltNetwork.AssignedDeposit</c>.</summary>
    internal int DroneDeposit = -1;

    /// <summary>Progress per drone bay module, in per mille ticks (only bays with progress have an entry).</summary>
    internal SortedDictionary<(int Mx, int My, int Mz), int> DroneProgress { get; private set; } = new();

    /// <summary>Current hit points of the damaged modules only: an undamaged module has no entry and counts as at full
    /// <c>structures.&lt;kind&gt;.hp</c>. Entries are in 1 .. max - 1. See <c>BeltNetwork.ModuleHp</c>.</summary>
    internal SortedDictionary<(int Mx, int My, int Mz), int> ModuleDamage { get; private set; } = new();

    internal int PowerProduction, PowerDemand, Satisfaction = PowerRules.FullSatisfaction;

    /// <summary>Power the mining lasers of this ship asked for in the last power step (part of <see cref="PowerDemand"/>). The laser is served before the machines, see ADR 0027.</summary>
    internal int LaserDraw;

    /// <summary>Ticks left in which this structure takes no further collision damage (ADR 0028). Mixed only when non-zero.</summary>
    internal int ImpactCooldown;

    /// <summary>Ships only: the attitude set by <see cref="SetAttitude"/> (ADR 0030). The identity unless set; mixed into the checksum only when it is not.</summary>
    internal Attitude Attitude = Attitude.Identity;

    /// <summary>Ships only: the station this ship is docked at, or -1 (ADR 0029). While docked the ship is locked to the port pose and cannot thrust.</summary>
    internal int DockedTo = -1;

    /// <summary>Ships only, meaningful while docked: the ship's own port (module and face) and the station's port it is joined to.</summary>
    internal (int Mx, int My, int Mz) DockShipModule, DockStationModule;
    internal Dir DockShipFace, DockStationFace;

    /// <summary>ADR 0047: the docking collars of this structure, small pieces attached to a horizontal face of a module (host module, face as a <see cref="Dir"/> value), sorted for determinism. Empty in worlds with dock modules; mixed into the checksum only when not empty.</summary>
    internal SortedSet<(int Mx, int My, int Mz, int Face)> DockPieceSet { get; private set; } = new();

    /// <summary>ADR 0051: the large modules of this structure, each a square of Size x Size module cells whose lowest corner is (Mx, My, Mz); the cells stay ordinary entries of the module map. Empty unless a span was built; hashed only when not empty.</summary>
    internal SortedSet<(int Mx, int My, int Mz, int Size)> SpanSet { get; private set; } = new();

    /// <summary>Stations only: the items kept in the station's stock by item index, or null while it never held anything. See ADR 0029.</summary>
    internal int[]? Stock;

    internal Structure(int id, StructureKind kind, int owner, long ox, long oy, long oz)
    {
        Id = id; Kind = kind; Owner = owner; OriginX = ox; OriginY = oy; OriginZ = oz;
    }

    /// <summary>True while this ship is docked at a station.</summary>
    public bool IsDocked => DockedTo >= 0;

    /// <summary>The station a ship is docked at, or -1.</summary>
    public int DockedStation => DockedTo;

    internal Structure Clone()
    {
        var c = new Structure(Id, Kind, Owner, OriginX, OriginY, OriginZ);
        c.ModuleMap = new SortedDictionary<(int, int, int), ModuleKind>(ModuleMap);
        c.BurnRemaining = new SortedDictionary<(int, int, int), int>(BurnRemaining);
        c.ModuleDamage = new SortedDictionary<(int, int, int), int>(ModuleDamage);
        c.DroneDeposit = DroneDeposit;
        c.DroneProgress = new SortedDictionary<(int, int, int), int>(DroneProgress);
        c.PowerProduction = PowerProduction; c.PowerDemand = PowerDemand; c.Satisfaction = Satisfaction; c.LaserDraw = LaserDraw; c.ImpactCooldown = ImpactCooldown;
        c.Attitude = Attitude; c.DockedTo = DockedTo; c.DockShipModule = DockShipModule; c.DockStationModule = DockStationModule;
        c.DockShipFace = DockShipFace; c.DockStationFace = DockStationFace;
        foreach (var dp in DockPieceSet) c.DockPieceSet.Add(dp);
        foreach (var sp in SpanSet) c.SpanSet.Add(sp);
        c.Stock = Stock is null ? null : (int[])Stock.Clone();
        CloneReactorFuelInto(c);
        return c;
    }
}
