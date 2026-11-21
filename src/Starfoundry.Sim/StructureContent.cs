namespace Starfoundry.Sim;

/// <summary>
/// What one kind of structure module costs, how tough it is and what may be built inside it.
/// </summary>
/// <param name="Cost">Items paid from the builder's hold in campaign mode; half is refunded on removal.</param>
/// <param name="AllowedRoles">Machine roles that may stand inside a module of this kind.</param>
/// <param name="AllowsBelts">Whether belt cells may lie inside a module of this kind.</param>
/// <param name="HoldCapacity">Items a module of this kind adds to the ship's hold (cargo only; 0 otherwise).</param>
/// <param name="ThrustSpeedPercent">Percent of the base ship speed a module of this kind adds (engine only; 0 otherwise).</param>
public sealed record ModuleKindDef(
    ModuleKind Kind, ItemStack[] Cost, int Hp, MachineRole[] AllowedRoles, bool AllowsBelts, int HoldCapacity, int ThrustSpeedPercent);

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
}
