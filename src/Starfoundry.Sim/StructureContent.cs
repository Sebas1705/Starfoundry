namespace Starfoundry.Sim;

/// <summary>
/// What one kind of structure module costs, how tough it is and what may be built inside it.
/// </summary>
/// <param name="Cost">Items paid from the builder's hold in campaign mode; half is refunded on removal.</param>
/// <param name="AllowedRoles">Machine roles that may stand inside a module of this kind.</param>
/// <param name="AllowsBelts">Whether belt cells may lie inside a module of this kind.</param>
/// <param name="HoldCapacity">Items a module of this kind adds to the ship's hold (cargo only; 0 otherwise).</param>
/// <param name="ThrustSpeedPercent">Percent of the base ship speed a module of this kind adds (engine only; 0 otherwise).</param>
/// <param name="PowerOutput">Power a burning module of this kind produces (reactor only; 0 otherwise).</param>
/// <param name="BurnIntervalTicks">Ticks one hydrogen keeps a reactor burning (reactor only; 0 otherwise).</param>
/// <param name="DroneRangeCells">How far from the structure its drones reach a deposit, in cells (drone bay only; 0 otherwise).</param>
/// <param name="DroneIntervalTicks">Ticks one bay needs per item at density 100 (drone bay only; 0 otherwise).</param>
/// <param name="PowerUse">Power one bay draws while its structure has an assigned deposit (drone bay only; 0 otherwise).</param>
public sealed record ModuleKindDef(
    ModuleKind Kind, ItemStack[] Cost, int Hp, MachineRole[] AllowedRoles, bool AllowsBelts, int HoldCapacity, int ThrustSpeedPercent,
    int PowerOutput = 0, int BurnIntervalTicks = 0, int DroneRangeCells = 0, int DroneIntervalTicks = 0, int PowerUse = 0);

/// <summary>The module kinds of ships and stations, from the <c>structures</c> section of the content.</summary>
public sealed class StructureContent
{
    private readonly ModuleKindDef[] _kinds;

    internal StructureContent(ModuleKindDef[] kinds)
    {
        _kinds = kinds;
    }

    /// <summary>One definition per <see cref="ModuleKind"/>, indexed by the enum value.</summary>
    public IReadOnlyList<ModuleKindDef> Kinds => _kinds;

    public ModuleKindDef Get(ModuleKind kind) => _kinds[(int)kind];

    /// <summary>The machine roles allowed inside a module of this kind.</summary>
    public IReadOnlyList<MachineRole> AllowedRoles(ModuleKind kind) => _kinds[(int)kind].AllowedRoles;

    public bool AllowsRole(ModuleKind kind, MachineRole role) => System.Array.IndexOf(_kinds[(int)kind].AllowedRoles, role) >= 0;

    public bool AllowsBelts(ModuleKind kind) => _kinds[(int)kind].AllowsBelts;

    public ItemStack[] ModuleCost(ModuleKind kind) => _kinds[(int)kind].Cost;

    public int ModuleHp(ModuleKind kind) => _kinds[(int)kind].Hp;

    /// <summary>Items one module of this kind adds to the hold (0 unless it is a cargo module).</summary>
    public int HoldCapacity(ModuleKind kind) => _kinds[(int)kind].HoldCapacity;

    /// <summary>Percent of the base speed one module of this kind adds (0 unless it is an engine).</summary>
    public int EngineSpeedPercent(ModuleKind kind) => _kinds[(int)kind].ThrustSpeedPercent;

    /// <summary>Power one burning module of this kind produces (0 unless it is a reactor).</summary>
    public int PowerOutput(ModuleKind kind) => _kinds[(int)kind].PowerOutput;

    /// <summary>Ticks one hydrogen keeps a reactor burning (0 unless it is a reactor).</summary>
    public int BurnIntervalTicks(ModuleKind kind) => _kinds[(int)kind].BurnIntervalTicks;

    /// <summary>Reach of a drone bay in cells (0 unless it is a drone bay).</summary>
    public int DroneRangeCells(ModuleKind kind) => _kinds[(int)kind].DroneRangeCells;

    /// <summary>Ticks one drone bay needs per item at density 100 (0 unless it is a drone bay).</summary>
    public int DroneIntervalTicks(ModuleKind kind) => _kinds[(int)kind].DroneIntervalTicks;

    /// <summary>Power one drone bay draws while mining (0 unless it is a drone bay).</summary>
    public int ModulePowerUse(ModuleKind kind) => _kinds[(int)kind].PowerUse;
}
