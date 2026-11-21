using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Where the pilot sits, in local cells of the ship structure: the seat, the eye while seated and the spot a standing pilot appears at.</summary>
public readonly record struct SeatSpots(bool FromCockpit, (int Mx, int My, int Mz) Module, Vector3 Seat, Vector3 Eye, Vector3 Stand);

/// <summary>
/// Finds the pilot seat of a ship from its real Cockpit modules. The first cockpit by sorted module coordinate is used, so the choice is
/// deterministic; when it is destroyed or removed the next one takes over, and a ship with no cockpit falls back to the structure origin.
/// The seat, eye and stand spot keep the same offsets inside the 8x4x8 module box as the cockpit model has. Engine-free.
/// </summary>
public static class SeatLocator
{
    /// <summary>The seat model inside the cockpit module, from the module origin.</summary>
    public static readonly Vector3 SeatOffset = new(4f, 0f, 4.6f);
    /// <summary>The seated eye, just behind the seat so the workshop stays in view when looking aft.</summary>
    public static readonly Vector3 EyeOffset = new(4f, 2.7f, 6.6f);
    /// <summary>Where a pilot who stands up appears.</summary>
    public static readonly Vector3 StandOffset = new(4f, 0f, 6.6f);

    /// <summary>The first Cockpit module by sorted coordinate, or null when there is none.</summary>
    public static (int Mx, int My, int Mz)? FirstCockpit(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules)
    {
        (int, int, int)? best = null;
        foreach (var (coord, kind) in modules)
        {
            if (kind != ModuleKind.Cockpit) continue;
            if (best is null || coord.CompareTo(best.Value) < 0) best = coord;
        }
        return best is { } b ? (b.Item1, b.Item2, b.Item3) : null;
    }

    /// <summary>The seat spots of a ship with these modules.</summary>
    public static SeatSpots Locate(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules)
    {
        var cockpit = FirstCockpit(modules);
        var m = cockpit ?? (0, 0, 0);
        var origin = new Vector3(m.Item1 * StationGrid.ModuleX, m.Item2 * StationGrid.ModuleY, m.Item3 * StationGrid.ModuleZ);
        return new SeatSpots(cockpit is not null, m, origin + SeatOffset, origin + EyeOffset, origin + StandOffset);
    }
}
