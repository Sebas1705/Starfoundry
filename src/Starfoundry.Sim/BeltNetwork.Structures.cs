namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    /// <summary>Structures other than the implicit world (structure 0), sorted by id so iteration, checksum and clones are deterministic.</summary>
    private SortedDictionary<int, Structure> _structures = new();

    private int _nextStructureId = 1;

    /// <summary>Creates a ship or station at a world origin (units) and returns its id (1, 2, ...; ids are never reused).</summary>
    public int CreateStructure(int owner, StructureKind kind, long ox, long oy, long oz)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        int id = _nextStructureId++;
        _structures.Add(id, new Structure(id, kind, owner, ox, oy, oz));
        StructureVersion++;
        return id;
    }

    /// <summary>The structures in id order (structure 0 is not listed).</summary>
    public IEnumerable<Structure> Structures() => _structures.Values;

    /// <summary>True for 0 (the legacy world) and for every created structure.</summary>
    public bool StructureExists(int id) => id == 0 || _structures.ContainsKey(id);

    public Structure GetStructure(int id) =>
        _structures.TryGetValue(id, out var s) ? s : throw new ArgumentOutOfRangeException(nameof(id), "No such structure.");

    /// <summary>Moves a structure's world origin (units). Local cells and occupancy do not change.</summary>
    public void SetStructureOrigin(int id, long ox, long oy, long oz)
    {
        var s = GetStructure(id);
        s.OriginX = ox; s.OriginY = oy; s.OriginZ = oz;
        StructureVersion++;
    }

    /// <summary>Adds a module to a structure without any rule check (commands validate before calling this).</summary>
    public void AddStructureModuleUnchecked(int id, int mx, int my, int mz, ModuleKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!GetStructure(id).ModuleMap.TryAdd((mx, my, mz), kind)) throw new InvalidOperationException("The module already exists.");
        StructureVersion++;
    }

    /// <summary>World position in units of the centre of a local cell: origin + local * cell size + half a cell, per axis.
    /// Structure 0 is the legacy world with its origin at 0.</summary>
    public (long X, long Y, long Z) WorldCentre(int structure, int x, int y, int z)
    {
        long ox = 0, oy = 0, oz = 0;
        if (structure != 0)
        {
            var s = GetStructure(structure);
            ox = s.OriginX; oy = s.OriginY; oz = s.OriginZ;
        }
        long cu = Units.CellUnits;
        return (ox + x * cu + cu / 2, oy + y * cu + cu / 2, oz + z * cu + cu / 2);
    }

    /// <summary>Throws if a structure other than 0 has a coordinate outside the local grid limit.</summary>
    private static void CheckLocal(int structure, long x, long y, long z)
    {
        if (structure < 0) throw new ArgumentOutOfRangeException(nameof(structure));
        if (structure != 0 && !(StructureGrid.InLocalBounds(x) && StructureGrid.InLocalBounds(y) && StructureGrid.InLocalBounds(z)))
            throw new ArgumentOutOfRangeException(nameof(x), "Local coordinates are beyond StructureGrid.LocalLimit.");
    }

    private void CopyStructuresFrom(BeltNetwork other)
    {
        _structures = new SortedDictionary<int, Structure>(other._structures.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()));
        _nextStructureId = other._nextStructureId;
    }

    private void MixStructures(Action<long> mix)
    {
        if (_structures.Count == 0) return; // a world without structures hashes exactly as before they existed
        mix(_structures.Count); mix(_nextStructureId);
        foreach (var s in _structures.Values)
        {
            mix(s.Id); mix((int)s.Kind); mix(s.Owner); mix(s.OriginX); mix(s.OriginY); mix(s.OriginZ);
            mix(s.ModuleMap.Count);
            foreach (var (m, kind) in s.ModuleMap) { mix(m.Mx); mix(m.My); mix(m.Mz); mix((int)kind); }
        }
    }

    private void ValidateStructures()
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        foreach (var (id, s) in _structures)
        {
            if (s.Id != id) Fail($"structure {id} has id {s.Id}");
            if (id >= _nextStructureId) Fail($"structure {id} is beyond the id counter");
            if (!Enum.IsDefined(s.Kind)) Fail($"structure {id} has an unknown kind");
            foreach (var (m, kind) in s.ModuleMap)
                if (!Enum.IsDefined(kind)) Fail($"structure {id} module {m} has an unknown kind");
        }
        foreach (var m in _machines)
        {
            if (m is null || m.Structure == 0) continue;
            if (!_structures.ContainsKey(m.Structure)) Fail($"machine {m.Id} is in missing structure {m.Structure}");
            if (!(StructureGrid.InLocalBounds(m.X) && StructureGrid.InLocalBounds(m.Y) && StructureGrid.InLocalBounds(m.Z)))
                Fail($"machine {m.Id} lies beyond the local grid limit");
        }
        foreach (var s in _segs)
        {
            if (s is null || s.Structure == 0) continue;
            if (!_structures.ContainsKey(s.Structure)) Fail($"segment {s.Id} is in missing structure {s.Structure}");
            int n = s.Length / Units.CellUnits - 1;
            if (!(StructureGrid.InLocalBounds(s.OriginX) && StructureGrid.InLocalBounds(s.OriginY) && StructureGrid.InLocalBounds(s.OriginZ)
                && StructureGrid.InLocalBounds(s.OriginX + (long)s.DirX * n) && StructureGrid.InLocalBounds(s.OriginY + (long)s.DirY * n)
                && StructureGrid.InLocalBounds(s.OriginZ + (long)s.DirZ * n)))
                Fail($"segment {s.Id} lies beyond the local grid limit");
        }
    }
}
