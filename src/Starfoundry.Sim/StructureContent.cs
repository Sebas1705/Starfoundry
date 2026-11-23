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
/// <param name="DefenceBonusPercent">Percent each module of this kind adds to the turret damage of its structure (defence only; 0 otherwise).</param>
/// <param name="EmergencyPower">Power a reactor of this kind yields while it has no hydrogen and is not burning (reactor only; 0 otherwise).</param>
/// <param name="RegenHp">Hit points a damaged module of this kind regains each passive regeneration step (1 by default).</param>
/// <param name="PowerUse">Power one module draws: a bay while its structure has an assigned deposit, a shield or sensor always (0 otherwise).</param>
/// <param name="ShieldPercent">Percent of the damage to every other module one powered shield absorbs (shield only; 0 otherwise).</param>
/// <param name="ShieldStackPercent">Percent each further shield adds (shield only; 0 otherwise).</param>
/// <param name="ShieldMaxPercent">Cap of the summed absorption (shield only; 0 otherwise).</param>
/// <param name="SensorRangeCells">How far from the sensor deposits are revealed automatically, in cells (sensor only; 0 otherwise).</param>
/// <param name="HardpointPercent">Strength of one hardpoint in percent of the base mining rate or gun damage: 100 is the mark 1 (mining laser and gun kinds only; 0 otherwise). ADR 0032.</param>
/// <param name="HardpointRangeCells">How far from its module centre one mining laser reaches a deposit, in cells (mining laser kinds only; 0 otherwise).</param>
/// <param name="BuildableFloor">Whether machines and belts may be built on the floor of a module of this kind (ADR 0031). Content that does not say defaults to: the kind allows something.</param>
public sealed record ModuleKindDef(
    ModuleKind Kind, ItemStack[] Cost, int Hp, MachineRole[] AllowedRoles, bool AllowsBelts, int HoldCapacity, int ThrustSpeedPercent,
    int PowerOutput = 0, int BurnIntervalTicks = 0, int DroneRangeCells = 0, int DroneIntervalTicks = 0, int PowerUse = 0, int DefenceBonusPercent = 0, int EmergencyPower = 0, int RegenHp = 1,
    int ShieldPercent = 0, int ShieldStackPercent = 0, int ShieldMaxPercent = 0, int SensorRangeCells = 0, bool BuildableFloor = false, int HardpointPercent = 0, int HardpointRangeCells = 0);

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

    /// <summary>False for a module kind the content does not define (kinds added after the first release are optional): it cannot be placed.</summary>
    public bool IsDefined(ModuleKind kind) => _kinds[(int)kind].Hp > 0;

    /// <summary>The machine roles allowed inside a module of this kind.</summary>
    public IReadOnlyList<MachineRole> AllowedRoles(ModuleKind kind) => _kinds[(int)kind].AllowedRoles;

    public bool AllowsRole(ModuleKind kind, MachineRole role) => System.Array.IndexOf(_kinds[(int)kind].AllowedRoles, role) >= 0;

    public bool AllowsBelts(ModuleKind kind) => _kinds[(int)kind].AllowsBelts;

    /// <summary>True for the kinds whose floor machines and belts may be built on (the fabrication bay, a station core, the defence module for turrets and walls).</summary>
    public bool IsBuildable(ModuleKind kind) => _kinds[(int)kind].BuildableFloor;

    /// <summary>
    /// Whether a machine of <paramref name="role"/> (or a belt cell when <paramref name="isBelt"/>) may stand at local layer <paramref name="y"/> of a module of
    /// <paramref name="kind"/> (ADR 0031): <see cref="Reject.KindNotAllowed"/> when the kind does not allow it (or is not buildable at all) and
    /// <see cref="Reject.NotOnFloor"/> when it is not at the floor level of the module. With <paramref name="legacy"/> (<see cref="WorldRules.LegacyBuilding"/>) only
    /// the kind's allowed list counts, as before the rule.
    /// </summary>
    public Reject CellProblem(ModuleKind kind, int y, bool isBelt, MachineRole role, bool legacy)
    {
        if (!(isBelt ? AllowsBelts(kind) : AllowsRole(kind, role))) return Reject.KindNotAllowed;
        if (legacy) return Reject.None;
        if (!IsBuildable(kind)) return Reject.KindNotAllowed;
        return StationGrid.IsFloorLayer(y) ? Reject.None : Reject.NotOnFloor;
    }

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

    /// <summary>Power one unfuelled reactor yields so a dead ship still crawls (0 unless it is a reactor).</summary>
    public int EmergencyPower(ModuleKind kind) => _kinds[(int)kind].EmergencyPower;

    /// <summary>Reach of a drone bay in cells (0 unless it is a drone bay).</summary>
    public int DroneRangeCells(ModuleKind kind) => _kinds[(int)kind].DroneRangeCells;

    /// <summary>Ticks one drone bay needs per item at density 100 (0 unless it is a drone bay).</summary>
    public int DroneIntervalTicks(ModuleKind kind) => _kinds[(int)kind].DroneIntervalTicks;

    /// <summary>Percent one module of this kind adds to its structure's turret damage (0 unless it is a defence module).</summary>
    public int DefenceBonusPercent(ModuleKind kind) => _kinds[(int)kind].DefenceBonusPercent;

    /// <summary>Hit points a damaged module of this kind regains at each passive regeneration step.</summary>
    public int RegenHp(ModuleKind kind) => _kinds[(int)kind].RegenHp;

    /// <summary>Power one module draws: a drone bay while mining, a shield or a sensor always (0 for every other kind).</summary>
    public int ModulePowerUse(ModuleKind kind) => _kinds[(int)kind].PowerUse;

    /// <summary>Strength in percent of one hardpoint module (mining laser and gun kinds; 0 for every other kind).</summary>
    public int HardpointPercent(ModuleKind kind) => _kinds[(int)kind].HardpointPercent;

    /// <summary>Reach in cells of one mining laser module (0 for every other kind).</summary>
    public int HardpointRangeCells(ModuleKind kind) => _kinds[(int)kind].HardpointRangeCells;

    public ModuleKindDef ShieldDef => _kinds[(int)ModuleKind.Shield];

    /// <summary>Range of a sensor in cells (0 unless it is a sensor).</summary>
    public int SensorRangeCells(ModuleKind kind) => _kinds[(int)kind].SensorRangeCells;
}
