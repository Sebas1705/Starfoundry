using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Why an edit of the designer's staged layout was refused (the game turns these into toasts).</summary>
public enum DesignEditResult
{
    Ok,
    Occupied,
    Empty,
    NotAdjacent,
    OutOfBounds,
    KindNotAllowed,
    NoUpgrade,
    SameCell,
    TooManyModules,
}

/// <summary>
/// The engine-free state of the ship designer (ADR 0032): a staged copy of the ship's module layout on its own grid, and the edits the player
/// makes to it: add a module of a kind, remove one, move one, upgrade a hardpoint. The simulation owns the rules: <see cref="Check"/> asks it for
/// the verdict and the price of the staged layout, and <see cref="ToCommand"/> builds the <see cref="DesignerApply"/> that applies it. The editor
/// only keeps what a human needs while editing: a new module must touch an existing one, so the layout stays one piece while the player works.
/// </summary>
public sealed class ShipDesignEditor
{
    private readonly SortedDictionary<(int Mx, int My, int Mz), ModuleKind> _staged = new();
    private readonly Dictionary<(int Mx, int My, int Mz), ModuleKind> _original = new();

    /// <summary>The staged layout in module order.</summary>
    public IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> Modules => _staged;

    /// <summary>The module list of the staged layout, as the command carries it.</summary>
    public DesignModule[] ToModules() => _staged.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();

    /// <summary>The command that applies the staged layout.</summary>
    public DesignerApply ToCommand() => new(ToModules());

    /// <summary>Loads the ship's current modules as the layout and as the layout to go back to.</summary>
    public void Load(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules)
    {
        _staged.Clear();
        _original.Clear();
        foreach (var (k, v) in modules) { _staged[k] = v; _original[k] = v; }
    }

    /// <summary>Goes back to the layout of the ship.</summary>
    public void Revert()
    {
        _staged.Clear();
        foreach (var (k, v) in _original) _staged[k] = v;
    }

    /// <summary>True when the staged layout differs from the ship's.</summary>
    public bool Dirty
    {
        get
        {
            if (_staged.Count != _original.Count) return true;
            foreach (var (k, v) in _staged)
                if (!_original.TryGetValue(k, out var o) || o != v) return true;
            return false;
        }
    }

    /// <summary>Whether a module of the original layout stands at this key with this kind (it is then not new).</summary>
    public bool IsOriginal((int Mx, int My, int Mz) key, ModuleKind kind) => _original.TryGetValue(key, out var k) && k == kind;

    /// <summary>The kinds the designer offers for a ship, in menu order: every kind the content defines that a ship may have.</summary>
    public static IReadOnlyList<ModuleKind> Catalogue(ContentDb content)
    {
        var list = new List<ModuleKind>();
        if (content.Structures is not { } kinds) return list;
        foreach (var k in Order)
            if (kinds.IsDefined(k) && ModuleKindIds.AllowedOn(StructureKind.Ship, k)) list.Add(k);
        return list;
    }

    private static readonly ModuleKind[] Order =
    {
        ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.ReactorMk2, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Hull,
        ModuleKind.MiningLaser, ModuleKind.MiningLaserMk2, ModuleKind.Gun, ModuleKind.GunMk2, ModuleKind.GunHeavy,
        ModuleKind.Defence, ModuleKind.DroneBay, ModuleKind.Shield, ModuleKind.Sensor, ModuleKind.SensorShort, ModuleKind.SensorLong, ModuleKind.CommsAntenna, ModuleKind.Dock,
    };

    /// <summary>True when a module of the map stands against one of the six faces of the cell.</summary>
    public static bool Touches(IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> map, (int Mx, int My, int Mz) key)
    {
        foreach (var (dx, dy, dz) in Steps)
            if (map.ContainsKey((key.Mx + dx, key.My + dy, key.Mz + dz))) return true;
        return false;
    }

    private static readonly (int, int, int)[] Steps = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>True when a cell is free and touches a staged module, so a module of any kind could be added there.</summary>
    public bool CanAddAt((int Mx, int My, int Mz) key) =>
        !_staged.ContainsKey(key) && StructureGrid.IsValidLocalModule(key.Mx, key.My, key.Mz) && Touches(_staged, key);

    public DesignEditResult Add((int Mx, int My, int Mz) key, ModuleKind kind, ContentDb content)
    {
        if (!Catalogue(content).Contains(kind)) return DesignEditResult.KindNotAllowed;
        if (!StructureGrid.IsValidLocalModule(key.Mx, key.My, key.Mz)) return DesignEditResult.OutOfBounds;
        if (_staged.ContainsKey(key)) return DesignEditResult.Occupied;
        if (_staged.Count >= DesignerRules.MaxModules) return DesignEditResult.TooManyModules;
        if (!Touches(_staged, key)) return DesignEditResult.NotAdjacent;
        _staged[key] = kind;
        return DesignEditResult.Ok;
    }

    public DesignEditResult Remove((int Mx, int My, int Mz) key)
    {
        if (!_staged.Remove(key)) return DesignEditResult.Empty;
        return DesignEditResult.Ok;
    }

    /// <summary>Moves a module to a free cell that touches the layout without it (a module that only moves costs nothing).</summary>
    public DesignEditResult Move((int Mx, int My, int Mz) from, (int Mx, int My, int Mz) to)
    {
        if (!_staged.TryGetValue(from, out var kind)) return DesignEditResult.Empty;
        if (from == to) return DesignEditResult.SameCell;
        if (!StructureGrid.IsValidLocalModule(to.Mx, to.My, to.Mz)) return DesignEditResult.OutOfBounds;
        if (_staged.ContainsKey(to)) return DesignEditResult.Occupied;
        _staged.Remove(from);
        if (!Touches(_staged, to) && _staged.Count > 0) { _staged[from] = kind; return DesignEditResult.NotAdjacent; }
        _staged[to] = kind;
        return DesignEditResult.Ok;
    }

    /// <summary>The next mark of the module at the key, or null when it has none.</summary>
    public ModuleKind? UpgradeFor((int Mx, int My, int Mz) key, ContentDb content) =>
        _staged.TryGetValue(key, out var kind) && ModuleKindIds.UpgradeOf(kind) is { } up && content.Structures is { } k && k.IsDefined(up) ? up : null;

    public DesignEditResult Upgrade((int Mx, int My, int Mz) key, ContentDb content)
    {
        if (!_staged.ContainsKey(key)) return DesignEditResult.Empty;
        if (UpgradeFor(key, content) is not { } up) return DesignEditResult.NoUpgrade;
        _staged[key] = up;
        return DesignEditResult.Ok;
    }

    /// <summary>The simulation's verdict and price for the staged layout, for a player docked at a shipyard.</summary>
    public DesignReport Check(SimWorld world, int player) => world.CheckDesign(player, ToModules());

    /// <summary>The bounds of the staged layout (inclusive), widened by <paramref name="margin"/> modules on every side so the grid shows the free cells next to it.</summary>
    public ((int X, int Y, int Z) Min, (int X, int Y, int Z) Max) Bounds(int margin = 1)
    {
        if (_staged.Count == 0) return ((-margin, -margin, -margin), (margin, margin, margin));
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var k in _staged.Keys)
        {
            x0 = Math.Min(x0, k.Mx); x1 = Math.Max(x1, k.Mx);
            y0 = Math.Min(y0, k.My); y1 = Math.Max(y1, k.My);
            z0 = Math.Min(z0, k.Mz); z1 = Math.Max(z1, k.Mz);
        }
        return ((x0 - margin, y0 - margin, z0 - margin), (x1 + margin, y1 + margin, z1 + margin));
    }

    /// <summary>
    /// What the layout's engines give (ADR 0033): how many there are, the top speed in cells per second and the seconds from rest to the top speed. The engine
    /// percent is capped like the simulation's, then raised by the research bonuses; the time is the content's acceleration time shortened by the acceleration bonus.
    /// Null without engines or when the world has no flight inertia (the time is then 0).
    /// </summary>
    public (int Engines, double TopCellsPerSecond, double SecondsToTop)? Flight(ContentDb content, int speedBonusPercent, int accelBonusPercent) =>
        FlightOf(_staged.Values, content, speedBonusPercent, accelBonusPercent);

    /// <summary>The same as <see cref="Flight"/> for any list of module kinds (the blueprint editor's stats use it).</summary>
    public static (int Engines, double TopCellsPerSecond, double SecondsToTop)? FlightOf(IEnumerable<ModuleKind> modules, ContentDb content, int speedBonusPercent, int accelBonusPercent)
    {
        if (content.Structures is not { } kinds) return null;
        int engines = 0;
        long percent = 0;
        foreach (var k in modules)
            if (k == ModuleKind.Engine) { engines++; percent += kinds.EngineSpeedPercent(k); }
        if (engines == 0) return null;
        bool inertia = content.World is { HasInertia: true };
        percent = Math.Min(percent, StructureFlight.MaxSpeedPercent);
        if (inertia) percent = percent * (100 + speedBonusPercent) / 100;
        double top = PlayerRules.MoveUnitsPerTick * (double)Units.TicksPerSecond / Units.CellUnits * percent / StructureFlight.PercentScale;
        double seconds = inertia ? content.World!.AccelTicks * 100.0 / (100 + accelBonusPercent) / Units.TicksPerSecond : 0.0;
        return (engines, top, seconds);
    }

    /// <summary>What the layout's hardpoints add up to: lasers, their summed strength in percent, guns and their summed strength.</summary>
    public (int Lasers, int LaserPercent, int Guns, int GunPercent) Hardpoints(ContentDb content)
    {
        int l = 0, lp = 0, g = 0, gp = 0;
        if (content.Structures is not { } kinds) return (0, 0, 0, 0);
        foreach (var k in _staged.Values)
        {
            if (ModuleKindIds.IsMiningLaser(k)) { l++; lp += kinds.HardpointPercent(k); }
            else if (ModuleKindIds.IsGun(k)) { g++; gp += kinds.HardpointPercent(k); }
        }
        return (l, lp, g, gp);
    }
}
