namespace Starfoundry.Sim;

public enum StructureKind { Ship, Station }

public enum ModuleKind { Core, Cockpit, Engine, Reactor, Cargo, Workshop, Defence, Hull }

/// <summary>Bounds of a structure's local grid. Structure 0 (the legacy world) keeps the world limits instead.</summary>
public static class StructureGrid
{
    /// <summary>Largest absolute local cell coordinate per axis for structures other than 0.</summary>
    public const int LocalLimit = 512;

    public static bool InLocalBounds(long v) => v >= -LocalLimit && v <= LocalLimit;
}

/// <summary>
/// A ship or station: a local cell grid with an integer world origin (in units). Machines and belt segments
/// belong to a structure and use its local cell coordinates. Structure 0 is the implicit legacy world
/// (origin 0, no registry entry).
/// </summary>
public sealed class Structure
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

    internal Structure(int id, StructureKind kind, int owner, long ox, long oy, long oz)
    {
        Id = id; Kind = kind; Owner = owner; OriginX = ox; OriginY = oy; OriginZ = oz;
    }

    internal Structure Clone()
    {
        var c = new Structure(Id, Kind, Owner, OriginX, OriginY, OriginZ);
        c.ModuleMap = new SortedDictionary<(int, int, int), ModuleKind>(ModuleMap);
        return c;
    }
}
