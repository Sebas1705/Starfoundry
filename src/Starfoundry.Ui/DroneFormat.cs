using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>What the drone bays of a structure are doing, for the HUD line and the flying drones.</summary>
public enum DroneState
{
    /// <summary>No bay in the structure.</summary>
    NoBays,

    /// <summary>Bays but no deposit assigned.</summary>
    Idle,

    /// <summary>Mining the assigned deposit.</summary>
    Working,

    /// <summary>Assigned, but the structure has no power.</summary>
    NoPower,

    /// <summary>Assigned, but the owner's hold is at the mining limit.</summary>
    HoldFull,
}

/// <summary>Engine-free helpers of the drone readout: the state, the HUD line and how many drones fly.</summary>
public static class DroneFormat
{
    /// <summary>The most drones drawn flying for one structure.</summary>
    public const int MaxVisible = 6;

    public static DroneState State(DroneStatus s)
    {
        if (s.Bays <= 0) return DroneState.NoBays;
        if (s.Deposit < 0) return DroneState.Idle;
        if (!s.PowerOk) return DroneState.NoPower;
        if (s.WaitingForHold) return DroneState.HoldFull;
        return s.Working ? DroneState.Working : DroneState.Idle;
    }

    /// <summary>Drones drawn in flight: two per bay up to <see cref="MaxVisible"/> while working, none otherwise.</summary>
    public static int VisibleDrones(DroneStatus s) => State(s) == DroneState.Working ? Math.Min(s.Bays * 2, MaxVisible) : 0;

    /// <summary>"Drones (Ship): 2 bay(s), mining Iron ore at 40/min": the HUD line of one structure; empty without bays.</summary>
    public static string Line(string structure, DroneStatus s, string item)
    {
        var state = State(s);
        if (state == DroneState.NoBays) return "";
        string detail = state switch
        {
            DroneState.Working => Loc.T("status.droneWorkingShort", item, s.RatePerMinute),
            DroneState.NoPower => Loc.T("status.droneNoPower"),
            DroneState.HoldFull => Loc.T("status.droneHoldFull"),
            _ => Loc.T("status.droneIdle"),
        };
        return Loc.T("hud.drones", structure, s.Bays, detail);
    }
}
