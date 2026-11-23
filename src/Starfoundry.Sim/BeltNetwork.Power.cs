namespace Starfoundry.Sim;

/// <summary>Fixed rules of power and fuel. Part of the simulation, so changing them changes states.</summary>
public static class PowerRules
{
    /// <summary>Content id of the reactor fuel.</summary>
    public const string FuelItemId = "core:hydrogen";

    /// <summary>Content id of the compact reactor fuel: one cell burns as long as <see cref="FuelCellBurnIntervals"/> hydrogen.</summary>
    public const string FuelCellItemId = "core:fuel_cell";

    /// <summary>Burn intervals one fuel cell lasts in a reactor (one hydrogen lasts one).</summary>
    public const int FuelCellBurnIntervals = 6;

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
    int Production, int Demand, int SatisfactionPermille, int BurnRemainingTicks, int HydrogenInHold, bool Burning, int FuelCellsInHold = 0);

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
        int cells = s.Owner >= 0 && Content is { FuelCellItem: >= 0 } cc ? HoldCount(s.Owner, cc.FuelCellItem) : 0;
        if (!StructureRules) return new PowerStatus(0, 0, PowerRules.FullSatisfaction, 0, hydrogen, false, cells);
        return new PowerStatus(s.PowerProduction, s.PowerDemand, s.Satisfaction, burn, hydrogen, burn > 0, cells);
    }

    /// <summary>
    /// True when the structure is a station that holds at least one machine but has no reactor module, so nothing in it can ever be
    /// powered. False for ships, unknown structures, stations with a reactor, empty stations and worlds without structure rules.
    /// </summary>
    public bool StationHasNoReactor(int structure)
    {
        if (!StructureRules || structure == 0 || !_structures.TryGetValue(structure, out var s) || s.Kind != StructureKind.Station) return false;
        if (s.ModuleMap.Values.Any(ModuleKindIds.IsReactor)) return false;
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

    /// <summary>
    /// Takes one fuel item from the owner's hold: a fuel cell when the hold has one, else a hydrogen. Returns how many burn
    /// intervals it keeps a reactor burning (<see cref="PowerRules.FuelCellBurnIntervals"/> or 1), or 0 when there is no fuel.
    /// </summary>
    private int TakeFuel(int owner)
    {
        if (owner < 0 || Content is null || !_players.TryGetValue(owner, out var p)) return 0;
        int item, intervals;
        if (Content.FuelCellItem >= 0 && p.Hold[Content.FuelCellItem] > 0) { item = Content.FuelCellItem; intervals = PowerRules.FuelCellBurnIntervals; }
        else if (Content.FuelItem >= 0 && p.Hold[Content.FuelItem] > 0) { item = Content.FuelItem; intervals = 1; }
        else return 0;
        p.Hold[item]--;
        p.HoldTotal--;
        HoldOut++;
        return intervals;
    }

    /// <summary>Power the always-on modules of a structure draw: shields and sensors.</summary>
    /// <summary>Ticks one unit of fuel keeps a reactor of this kind burning, with the research bonus (algae fuel).</summary>
    public int BurnInterval(ModuleKind kind) => (Content?.Structures?.BurnIntervalTicks(kind) ?? 0) * (100 + _bonus[(int)BonusKind.FuelBurn]) / 100;

    /// <summary>Power the mining laser of the ship's pilot draws while it fires (ADR 0027): <see cref="PlayerRules.LaserPowerUse"/> per laser that would mine this tick.</summary>
    private int LaserDemand(Structure s)
    {
        if (!LaserDrawsPower || s.Kind != StructureKind.Ship) return 0;
        int total = 0;
        foreach (var p in _players.Values)
            if (p.ShipStructure == s.Id && LaserWouldMine(p)) total += LaserPowerFor(p);
        return total;
    }

    private int ModuleDemand(Structure s)
    {
        if (Content?.Structures is not { } kinds) return 0;
        int total = 0;
        foreach (var kind in s.ModuleMap.Values)
            if (kind is ModuleKind.Shield or ModuleKind.Sensor) total += kinds.ModulePowerUse(kind);
        return total;
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
                if (!ModuleKindIds.IsReactor(kind)) continue;
                hasReactor = true;
                s.BurnRemaining.TryGetValue(key, out int rem);
                if (rem == 0)
                {
                    int intervals = TakeFuelFromStock(s); // the station's stock first: its reactors and a docked ship's burn it before the hold
                    if (intervals == 0) intervals = TakeFuel(s.Owner);
                    if (intervals > 0) rem = BurnInterval(kind) * intervals;
                }
                if (rem > 0)
                {
                    production += WorldRules.Scale(kinds.PowerOutput(kind) * (100 + _bonus[(int)BonusKind.PowerOutput]) / 100, Rules.PowerOutputPercent);
                    rem--;
                }
                else
                {
                    production += kinds.EmergencyPower(kind); // no fuel: the reactor still trickles a little power
                    FuelStarvedTicks++;
                }
                if (rem > 0) s.BurnRemaining[key] = rem; else s.BurnRemaining.Remove(key);
            }
            if (WorldGen is not null && production > 0 && CloudEffectOf(s.Id).DrainPercent is > 0 and var drain) production = production * (100 - drain) / 100; // ion clouds (ADR 0028)
            s.PowerProduction = production;
            int others = demand.GetValueOrDefault(s.Id) + DroneDemand(s) + ModuleDemand(s);
            s.LaserDraw = hasReactor ? LaserDemand(s) : 0;
            s.PowerDemand = others + s.LaserDraw;
            // The mining laser is served first: a brownout slows the machines but never the laser, so a starved base can always mine the ice
            // that brings the fuel back (the recovery loop of ADR 0022 needs hand mining). It starves only when the reactors give less than it asks.
            int forMachines = others == 0 ? production : production - Math.Min(production, s.LaserDraw);
            s.Satisfaction = PowerRules.Satisfaction(forMachines, others, hasReactor);
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
