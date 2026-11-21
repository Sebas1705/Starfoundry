namespace Starfoundry.Sim;

/// <summary>State of the drone bays of a structure. Rate is items per minute from the assigned deposit at the current power.</summary>
/// <param name="Bays">Drone bay modules of the structure.</param>
/// <param name="Working">True while bays are mining: assigned, powered and with room in the owner's hold.</param>
/// <param name="Deposit">The assigned deposit, or -1.</param>
/// <param name="RatePerMinute">Items per minute the bays deliver while working (0 when unassigned or without power).</param>
/// <param name="WaitingForHold">True when the owner's hold is at its mining limit, so the bays wait.</param>
/// <param name="PowerOk">True when the structure has some power (satisfaction above zero).</param>
public readonly record struct DroneStatus(int Bays, bool Working, int Deposit, int RatePerMinute, bool WaitingForHold, bool PowerOk);

public sealed partial class BeltNetwork
{
    /// <summary>Drone bays count as per mille ticks: progress grows by the satisfaction each tick and an item is mined at this many per tick of interval.</summary>
    private const int DroneProgressScale = PowerRules.FullSatisfaction;

    /// <summary>Drone bay modules of a structure (0 for structure 0 and unknown structures).</summary>
    public int DroneBays(int structure) =>
        _structures.TryGetValue(structure, out var s) ? ModuleCount(s, ModuleKind.DroneBay) : 0;

    /// <summary>The deposit the structure's drones mine, or -1.</summary>
    public int AssignedDeposit(int structure) =>
        _structures.TryGetValue(structure, out var s) ? s.DroneDeposit : -1;

    /// <summary>Whether the bays of the structure are working and how fast. Always idle without structure rules.</summary>
    public DroneStatus DroneStatus(int structure)
    {
        if (!_structures.TryGetValue(structure, out var s)) return new DroneStatus(0, false, -1, 0, false, true);
        int bays = ModuleCount(s, ModuleKind.DroneBay);
        bool powerOk = !StructureRules || s.Satisfaction > 0;
        if (!StructureRules || bays == 0 || s.DroneDeposit < 0) return new DroneStatus(bays, false, s.DroneDeposit, 0, false, powerOk);

        var d = _deposits[s.DroneDeposit];
        bool waiting = s.Owner >= 0 && _players.TryGetValue(s.Owner, out var p) && !DroneHoldHasRoom(p);
        int interval = DroneInterval(d);
        int rate = powerOk ? (int)((long)bays * 60 * Units.TicksPerSecond * s.Satisfaction / ((long)interval * DroneProgressScale)) : 0;
        return new DroneStatus(bays, powerOk && !waiting && d.Remaining > 0, s.DroneDeposit, rate, waiting, powerOk);
    }

    /// <summary>True if the deposit centre lies within the drone range of the structure's world centre (the centre of its cell 0,0,0).</summary>
    public bool DepositInDroneRange(int structure, int deposit)
    {
        var kinds = Content?.Structures;
        if (kinds is null) return false;
        var d = GetDeposit(deposit);
        var (wx, wy, wz) = WorldCentre(structure, 0, 0, 0);
        long cu = Units.CellUnits;
        long dx = wx - (d.X * cu + cu / 2), dy = wy - (d.Y * cu + cu / 2), dz = wz - (d.Z * cu + cu / 2);
        long r = (long)kinds.DroneRangeCells(ModuleKind.DroneBay) * cu;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>Sets the deposit the structure's drones mine (-1 clears it). Commands validate before calling this.</summary>
    internal void AssignDrones(int structure, int deposit)
    {
        var s = GetStructure(structure);
        if (s.DroneDeposit == deposit) return;
        s.DroneDeposit = deposit;
        s.DroneProgress.Clear();
        StructureVersion++;
    }

    private int DroneInterval(Deposit d) =>
        DepositRules.Scale(Content!.Structures!.DroneIntervalTicks(ModuleKind.DroneBay), d.Density);

    /// <summary>Same limit as hand mining: room in the hold and below the mining limit.</summary>
    private bool DroneHoldHasRoom(Player p) => p.HoldTotal + p.ShipReserved < HoldCapacityOf(p) && !MiningHoldLimitReached(p);

    /// <summary>Power the drone bays of a structure draw: the bay power use each while a deposit is assigned.</summary>
    private int DroneDemand(Structure s)
    {
        if (s.DroneDeposit < 0 || Content?.Structures is not { } kinds) return 0;
        return ModuleCount(s, ModuleKind.DroneBay) * kinds.ModulePowerUse(ModuleKind.DroneBay);
    }

    /// <summary>
    /// Drone bays mine their structure's assigned deposit for the owner (structure worlds only). Each bay's progress grows by the
    /// structure's power satisfaction (per mille) every tick; at <c>interval * 100 / density</c> ticks' worth it takes one item from the
    /// deposit into the owner's hold, or waits at full progress while the hold is at its mining limit. A deposit that is empty or out of
    /// range is unassigned.
    /// </summary>
    private void StepDrones()
    {
        if (!StructureRules || _structures.Count == 0 || Content?.Structures is null) return;
        foreach (var s in _structures.Values)
        {
            if (s.DroneDeposit < 0) continue;
            var d = _deposits[s.DroneDeposit];
            if (d.Remaining <= 0 || !DepositInDroneRange(s.Id, s.DroneDeposit))
            {
                s.DroneDeposit = -1;
                s.DroneProgress.Clear();
                StructureVersion++;
                continue;
            }
            if (s.Satisfaction <= 0 || s.Owner < 0 || !_players.TryGetValue(s.Owner, out var owner)) continue;

            long need = (long)DroneInterval(d) * DroneProgressScale;
            foreach (var (key, kind) in s.ModuleMap)
            {
                if (kind != ModuleKind.DroneBay) continue;
                s.DroneProgress.TryGetValue(key, out int progress);
                long acc = progress + s.Satisfaction;
                if (acc >= need)
                {
                    if (d.Remaining > 0 && DroneHoldHasRoom(owner))
                    {
                        d.Remaining--;
                        MinedByDrones++;
                        AddToHold(owner, d.Item, 1);
                        acc -= need;
                    }
                    else acc = need; // waits, ready to deliver as soon as there is room
                }
                if (acc > 0) s.DroneProgress[key] = (int)acc; else s.DroneProgress.Remove(key);
            }
        }
    }
}
