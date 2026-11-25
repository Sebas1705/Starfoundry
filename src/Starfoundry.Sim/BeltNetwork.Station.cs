namespace Starfoundry.Sim;

/// <summary>
/// The station grid: a module is a box of <see cref="ModuleX"/> x <see cref="ModuleY"/> x <see cref="ModuleZ"/> cells
/// aligned to multiples of its size, so module (mx, my, mz) covers cells mx*ModuleX to mx*ModuleX + ModuleX - 1 and so on.
/// </summary>
public static class StationGrid
{
    public const int ModuleX = 8;
    public const int ModuleY = 4;
    public const int ModuleZ = 8;

    /// <summary>Lowest and highest valid module coordinate per axis: every cell of the module must lie inside the world limits.</summary>
    public static int MinX => MinCoord(ModuleX);
    public static int MaxX => MaxCoord(ModuleX);
    public static int MinY => MinCoord(ModuleY);
    public static int MaxY => MaxCoord(ModuleY);
    public static int MinZ => MinCoord(ModuleZ);
    public static int MaxZ => MaxCoord(ModuleZ);

    private static int MinCoord(int size) => IntMath.CeilDiv(-SimWorld.MaxCoord, size);

    private static int MaxCoord(int size) => IntMath.FloorDiv(SimWorld.MaxCoord + 1, size) - 1;

    /// <summary>The layer, inside a module, whose cells rest on the floor: machines and belts are built there only (ADR 0031).</summary>
    public const int FloorLayer = 0;

    /// <summary>True when local layer <paramref name="y"/> is the floor layer of the module that contains it.</summary>
    public static bool IsFloorLayer(int y) => ((y % ModuleY) + ModuleY) % ModuleY == FloorLayer;

    /// <summary>The local layer of the floor of module row <paramref name="my"/>.</summary>
    public static int FloorY(int my) => my * ModuleY + FloorLayer;

    public static bool IsValidModule(int mx, int my, int mz) =>
        mx >= MinX && mx <= MaxX && my >= MinY && my <= MaxY && mz >= MinZ && mz <= MaxZ;

    private static int FloorDiv(int a, int b) => IntMath.FloorDiv(a, b);

    /// <summary>The module coordinates containing a cell.</summary>
    public static (int Mx, int My, int Mz) ModuleOfCell(int x, int y, int z) =>
        (FloorDiv(x, ModuleX), FloorDiv(y, ModuleY), FloorDiv(z, ModuleZ));
}

public sealed partial class BeltNetwork
{
    /// <summary>Module coordinates per player, sorted so iteration, checksum and clones are deterministic.</summary>
    private SortedDictionary<int, SortedSet<(int Mx, int My, int Mz)>> _modules = new();

    /// <summary>The core module of each player (the first one placed). Only present while the player has modules.</summary>
    private SortedDictionary<int, (int Mx, int My, int Mz)> _cores = new();

    private static readonly (int, int, int)[] FaceSteps = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    public int ModuleCount(int player) => _modules.TryGetValue(player, out var set) ? set.Count : 0;

    public bool HasModule(int player, int mx, int my, int mz) =>
        _modules.TryGetValue(player, out var set) && set.Contains((mx, my, mz));

    /// <summary>The player's modules in coordinate order, with a flag for the core.</summary>
    public IEnumerable<(int Mx, int My, int Mz, bool Core)> Modules(int player)
    {
        if (!_modules.TryGetValue(player, out var set)) yield break;
        _cores.TryGetValue(player, out var core);
        foreach (var m in set) yield return (m.Mx, m.My, m.Mz, m == core);
    }

    /// <summary>True if the cell lies inside one of the player's modules.</summary>
    public bool CellInStation(int player, int x, int y, int z)
    {
        if (!_modules.TryGetValue(player, out var set) || set.Count == 0) return false;
        return set.Contains(StationGrid.ModuleOfCell(x, y, z));
    }

    /// <summary>True if the cell lies inside a module of any player (used when rules do not depend on who owns the station).</summary>
    public bool CellInAnyStation(int x, int y, int z)
    {
        var m = StationGrid.ModuleOfCell(x, y, z);
        foreach (var set in _modules.Values) if (set.Contains(m)) return true;
        return false;
    }

    /// <summary>The cost of the next module: the core cost for the first, then the hull cost grown by the growth percent per existing module.</summary>
    public ItemStack[] NextModuleCost(int player) => ModuleCostFor(ModuleCount(player));

    private ItemStack[] ModuleCostFor(int existing)
    {
        var rules = Content?.Modules;
        if (rules is null) return System.Array.Empty<ItemStack>();
        if (existing == 0) return rules.CoreCost;
        var result = new ItemStack[rules.HullCost.Length];
        for (int i = 0; i < result.Length; i++)
        {
            var s = rules.HullCost[i];
            long scaled = (long)s.Count * (100 + (long)rules.GrowthPercent * existing) / 100;
            result[i] = new ItemStack(s.Item, (int)Math.Max(1, Math.Min(scaled, int.MaxValue / 2)));
        }
        return result;
    }

    /// <summary>What removing a module gives back: half of what the last module added would cost, per stack, rounded down.</summary>
    public ItemStack[] ModuleRefund(int player)
    {
        var cost = ModuleCostFor(Math.Max(0, ModuleCount(player) - 1));
        return cost.Select(s => new ItemStack(s.Item, s.Count / 2)).Where(s => s.Count > 0).ToArray();
    }

    /// <summary>True if the module shares a face with a module of the player (diagonal neighbours do not count).</summary>
    public bool ModuleTouchesStation(int player, int mx, int my, int mz)
    {
        if (!_modules.TryGetValue(player, out var set)) return false;
        foreach (var (dx, dy, dz) in FaceSteps)
            if (set.Contains((mx + dx, my + dy, mz + dz))) return true;
        return false;
    }

    /// <summary>True if the module's centre is within <paramref name="rangeCells"/> of the player's ship.</summary>
    public bool ModuleCentreInRange(int player, int mx, int my, int mz, int rangeCells)
    {
        var p = _players.TryGetValue(player, out var found) ? found : null;
        long px = p?.X ?? 0, py = p?.Y ?? 0, pz = p?.Z ?? 0;
        long cu = Units.CellUnits;
        long dx = ((long)mx * StationGrid.ModuleX + StationGrid.ModuleX / 2) * cu - px;
        long dy = ((long)my * StationGrid.ModuleY + StationGrid.ModuleY / 2) * cu - py;
        long dz = ((long)mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2) * cu - pz;
        long r = (long)rangeCells * cu;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>True if the player's modules, minus the given one, are still one face-connected piece (or none are left).</summary>
    public bool StationStaysConnectedWithout(int player, int mx, int my, int mz)
    {
        if (!_modules.TryGetValue(player, out var set)) return true;
        var remaining = new HashSet<(int, int, int)>(set);
        remaining.Remove((mx, my, mz));
        return IsConnected(remaining);
    }

    private static bool IsConnected(HashSet<(int, int, int)> cells)
    {
        if (cells.Count <= 1) return true;
        var seen = new HashSet<(int, int, int)>();
        var stack = new Stack<(int, int, int)>();
        var start = cells.First();
        seen.Add(start);
        stack.Push(start);
        while (stack.Count > 0)
        {
            var (x, y, z) = stack.Pop();
            foreach (var (dx, dy, dz) in FaceSteps)
            {
                var n = (x + dx, y + dy, z + dz);
                if (cells.Contains(n) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen.Count == cells.Count;
    }

    /// <summary>True if the module is the player's core.</summary>
    public bool IsCoreModule(int player, int mx, int my, int mz) =>
        _cores.TryGetValue(player, out var c) && c == (mx, my, mz);

    /// <summary>Adds a module without any rule check. The first module of a player becomes the core. Commands validate before calling this;
    /// setup code and tests may call it directly (for example to build a state the rules would never reach).</summary>
    public void AddModuleUnchecked(int player, int mx, int my, int mz)
    {
        if (!_modules.TryGetValue(player, out var set)) _modules[player] = set = new SortedSet<(int, int, int)>();
        if (!set.Add((mx, my, mz))) throw new InvalidOperationException("The module already exists.");
        if (!_cores.ContainsKey(player)) _cores[player] = (mx, my, mz);
        StructureVersion++;
    }

    /// <summary>Removes a module without any rule check.</summary>
    public void RemoveModuleUnchecked(int player, int mx, int my, int mz)
    {
        if (!_modules.TryGetValue(player, out var set) || !set.Remove((mx, my, mz))) throw new InvalidOperationException("No such module.");
        if (set.Count == 0)
        {
            _modules.Remove(player);
            _cores.Remove(player);
        }
        StructureVersion++;
    }

    private void CopyStationFrom(BeltNetwork other)
    {
        _modules = new SortedDictionary<int, SortedSet<(int Mx, int My, int Mz)>>(
            other._modules.ToDictionary(kv => kv.Key, kv => new SortedSet<(int Mx, int My, int Mz)>(kv.Value)));
        _cores = new SortedDictionary<int, (int Mx, int My, int Mz)>(other._cores);
    }

    private void MixStation(Action<long> mix)
    {
        if (_modules.Count == 0) return; // a world without a station hashes exactly as before modules existed
        mix(_modules.Count);
        foreach (var (player, set) in _modules)
        {
            mix(player);
            mix(set.Count);
            foreach (var m in set) { mix(m.Mx); mix(m.My); mix(m.Mz); }
            _cores.TryGetValue(player, out var core);
            mix(core.Mx); mix(core.My); mix(core.Mz);
        }
    }

    private void ValidateStation()
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        if (_cores.Keys.Any(k => !_modules.ContainsKey(k))) Fail("a core exists for a player without modules");
        foreach (var (player, set) in _modules)
        {
            if (set.Count == 0) Fail($"player {player} has an empty module set");
            foreach (var m in set)
                if (!StationGrid.IsValidModule(m.Mx, m.My, m.Mz)) Fail($"module {m} of player {player} lies outside the world limits");
            if (!_cores.TryGetValue(player, out var core) || !set.Contains(core)) Fail($"player {player} has no core among their modules");
            if (!IsConnected(new HashSet<(int, int, int)>(set.Select(m => (m.Mx, m.My, m.Mz)))))
                Fail($"the station of player {player} is not one connected piece (a module floats)");
        }
    }
}
