using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// The on-foot tools in a world with the blueprint rules (ADR 0043 and 0044, phase 5 of the blueprint editor): the build tool no longer places machines, belts, turrets or
/// walls (the floor plan does), so those entries are closed with a hint that points at the editor. What stays on foot are the repair and undo shortcuts, which need no new
/// simulation command: demolish what the crosshair is on (right click, the refund is the simulation's rule) and repair damaged machines. Pure decisions, tested without the engine.
/// </summary>
public static class FootTools
{
    /// <summary>The build entries closed because the floor plan places them: belts and machines (turrets, walls, unloaders and repair bays are machines).</summary>
    public static bool EntryBlocked(BuildEntry e, bool interiorBlueprint) => interiorBlueprint && e.Kind is BuildEntryKind.Belt or BuildEntryKind.Machine;

    /// <summary>The same for a build palette item: a belt or a machine (anything that is neither a module nor the station founder).</summary>
    public static bool ItemBlocked(bool isBelt, bool isModule, bool isStation, bool interiorBlueprint) => interiorBlueprint && (isBelt || (!isModule && !isStation));

    /// <summary>Why an entry is closed: the ship rule first (<see cref="Reject.UseStation"/>), then the floor plan rule (<see cref="Reject.UseBlueprint"/>); null when it can be used.</summary>
    public static Reject? Problem(BuildEntry e, bool automationBlocked, bool interiorBlueprint) =>
        StationFirstUi.Problem(e, automationBlocked) ?? (EntryBlocked(e, interiorBlueprint) ? Reject.UseBlueprint : null);

    /// <summary>The one-line hint where the build tool would be: the ship hint, else the floor plan hint, else empty.</summary>
    public static string Hint(bool automationBlocked, bool interiorBlueprint) =>
        automationBlocked ? StationFirstUi.Hint(true) : interiorBlueprint ? Loc.T("build.useBlueprint") : "";
}
