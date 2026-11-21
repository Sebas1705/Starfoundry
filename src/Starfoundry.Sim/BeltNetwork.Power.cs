namespace Starfoundry.Sim;

/// <summary>Fixed rules of power and fuel. Part of the simulation, so changing them changes states.</summary>
public static class PowerRules
{
    /// <summary>Content id of the reactor fuel.</summary>
    public const string FuelItemId = "core:hydrogen";

    /// <summary>Satisfaction is a per-mille value: 1000 means every machine has all the power it needs.</summary>
    public const int FullSatisfaction = 1000;

    /// <summary>Turrets only fire while the satisfaction of their structure is at least this (per mille).</summary>
    public const int TurretMinSatisfaction = 500;

    /// <summary>
    /// Satisfaction per mille of a structure. Enough production (or nothing to power and no reactor) gives 1000; a structure that
    /// has a reactor that yields nothing at all (no fuel and no emergency output) gives 0 even with nothing to power; otherwise
    /// <c>production * 1000 / demand</c>.
    /// </summary>
    public static int Satisfaction(int production, int demand, bool hasReactor)
    {
        if (production >= demand) return production == 0 && hasReactor ? 0 : FullSatisfaction;
        return (int)((long)production * FullSatisfaction / demand);
    }
}

/// <summary>Power state of a structure as of the last tick. Burn remaining is the most ticks any burning reactor has left.</summary>
public readonly record struct PowerStatus(
    int Production, int Demand, int SatisfactionPermille, int BurnRemainingTicks, int HydrogenInHold, bool Burning);

public sealed partial class BeltNetwork
{
    /// <summary>Power of a structure: what its reactors produce, what its machines need, and the owner's hydrogen. Structure 0 and
    /// worlds without structure rules are always fully powered.</summary>
    public PowerStatus PowerStatus(int structure)
    {
        if (structure == 0) return new PowerStatus(0, 0, PowerRules.FullSatisfaction, 0, 0, false);
        var s = GetStructure(structure);
        int burn = 0;
        foreach (int r in s.BurnRemaining.Values) burn = Math.Max(burn, r);
        int hydrogen = s.Owner >= 0 && Content is { FuelItem: >= 0 } c ? HoldCount(s.Owner, c.FuelItem) : 0;
        if (!StructureRules) return new PowerStatus(0, 0, PowerRules.FullSatisfaction, 0, hydrogen, false);
        return new PowerStatus(s.PowerProduction, s.PowerDemand, s.Satisfaction, burn, hydrogen, burn > 0);
    }

    /// <summary>
    /// True when the structure is a station that holds at least one machine but has no reactor module, so nothing in it can ever be
    /// powered. False for ships, unknown structures, stations with a reactor, empty stations and worlds without structure rules.
    /// </summary>
    public bool StationHasNoReactor(int structure)
    {
        if (!StructureRules || structure == 0 || !_structures.TryGetValue(structure, out var s) || s.Kind != StructureKind.Station) return false;
        if (s.ModuleMap.Values.Contains(ModuleKind.Reactor)) return false;
        foreach (var m in _machines)
            if (m is not null && m.Structure == structure) return true;
        return false;
    }

    /// <summary>Power of the ship structure of a player (fully powered, nothing burning, when the player has none).</summary>
    public PowerStatus ShipPower(int player)
    {
        int ship = ShipStructure(player);
        if (ship >= 0) return PowerStatus(ship);
        int hydrogen = Content is { FuelItem: >= 0 } c ? HoldCount(player, c.FuelItem) : 0;
        return new PowerStatus(0, 0, PowerRules.FullSatisfaction, 0, hydrogen, false);
    }

    /// <summary>Satisfaction (per mille) that applies to a structure's machines and engines this tick.</summary>
    private int SatisfactionOf(int structure) =>
        StructureRules && structure != 0 && _structures.TryGetValue(structure, out var s) ? s.Satisfaction : PowerRules.FullSatisfaction;

    /// <summary>True if the machine's turret may fire: always outside structures, else at half satisfaction or better.</summary>
    private bool TurretHasPower(Machine m) => SatisfactionOf(m.Structure) >= PowerRules.TurretMinSatisfaction;

    private bool TryTakeFuel(int owner)
    {
        if (owner < 0 || Content is not { FuelItem: >= 0 } c || !_players.TryGetValue(owner, out var p) || p.Hold[c.FuelItem] <= 0) return false;
        p.Hold[c.FuelItem]--;
        p.HoldTotal--;
        HoldOut++;
        return true;
    }

    /// <summary>
    /// Runs once at the start of every tick (structure worlds only): reactors burn fuel from their owner's hold, each structure's
    /// production, demand and satisfaction are worked out, and every machine of a structure earns its work credit. Structures and
    /// machines are visited in id order, so it is deterministic.
    /// </summary>
    private void StepPower()
    {
        if (!StructureRules || _structures.Count == 0 || Content?.Structures is not { } kinds) return;

        var demand = new Dictionary<int, int>();
        foreach (var m in _machines)
        {
            if (m is null || m.Structure == 0) continue;
            demand[m.Structure] = demand.GetValueOrDefault(m.Structure) + Content.Machines[m.Type].PowerUse;
        }

        foreach (var s in _structures.Values)
        {
            int production = 0;
            bool hasReactor = false;
            foreach (var (key, kind) in s.ModuleMap)
            {
                if (kind != ModuleKind.Reactor) continue;
                hasReactor = true;
                s.BurnRemaining.TryGetValue(key, out int rem);
                if (rem == 0 && TryTakeFuel(s.Owner)) rem = kinds.BurnIntervalTicks(kind);
                if (rem > 0)
                {
                    production += kinds.PowerOutput(kind);
                    rem--;
                }
                else production += kinds.EmergencyPower(kind); // no fuel: the reactor still trickles a little power
                if (rem > 0) s.BurnRemaining[key] = rem; else s.BurnRemaining.Remove(key);
            }
            s.PowerProduction = production;
            s.PowerDemand = demand.GetValueOrDefault(s.Id) + DroneDemand(s);
            s.Satisfaction = PowerRules.Satisfaction(production, s.PowerDemand, hasReactor);
        }

        foreach (var m in _machines)
        {
            if (m is null || m.Structure == 0) continue;
            m.PowerCredit += _structures[m.Structure].Satisfaction;
            m.Powered = m.PowerCredit >= PowerRules.FullSatisfaction;
            if (m.Powered) m.PowerCredit -= PowerRules.FullSatisfaction;
        }
    }
}
