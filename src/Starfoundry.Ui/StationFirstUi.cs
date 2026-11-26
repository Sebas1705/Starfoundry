using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// ADR 0039 in the interface: in a station-first world a ship holds ship modules only, so the first-person floor-building tools (machines, belts, turrets,
/// walls, the Fabrication bay) are unavailable aboard a ship and the player is told to build automation in a station. Pure decisions, tested without the engine.
/// </summary>
public static class StationFirstUi
{
    /// <summary>Whether floor building is closed to the player: a station-first world, standing in (or flying) a ship that is not docked at a station.</summary>
    public static bool AutomationBlocked(bool stationFirst, bool aboardShip) => stationFirst && aboardShip;

    /// <summary>Whether a build entry needs a station while <paramref name="blocked"/>: belts and machines (turrets, walls and repair bays are machines) and the Fabrication bay.</summary>
    public static bool EntryBlocked(BuildEntry e, bool blocked)
    {
        if (!blocked) return false;
        return e.Kind switch
        {
            BuildEntryKind.Belt or BuildEntryKind.Machine => true,
            BuildEntryKind.Module => e.Module is { } k && KindBlocked(k, true),
            _ => false,
        };
    }

    /// <summary>Whether a module kind cannot be placed on a ship in a station-first world.</summary>
    public static bool KindBlocked(ModuleKind kind, bool blocked) =>
        blocked && ModuleKindIds.AllowedOn(StructureKind.Ship, kind) && !ModuleKindIds.AllowedOn(StructureKind.Ship, kind, true);

    /// <summary>Why an entry cannot be used (always <see cref="Reject.UseStation"/>), or null when it can.</summary>
    public static Reject? Problem(BuildEntry e, bool blocked) => EntryBlocked(e, blocked) ? Reject.UseStation : null;

    /// <summary>The one-line hint shown where the build tool would be (the menu header, the key panel, a toast): empty when nothing is blocked.</summary>
    public static string Hint(bool blocked) => blocked ? Loc.T("build.stationFirst") : "";

    /// <summary>Whether every entry is closed (nothing at all to build aboard).</summary>
    public static bool NothingToBuild(IEnumerable<BuildEntry> entries, bool blocked) => entries.All(e => EntryBlocked(e, blocked));
}
