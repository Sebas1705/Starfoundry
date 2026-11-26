namespace Starfoundry.Sim;

/// <summary>One module of a ship design: the module coordinate on the ship's own grid and its kind (ADR 0032).</summary>
public readonly record struct DesignModule(int Mx, int My, int Mz, ModuleKind Kind);

/// <summary>Fixed rules of the ship designer (ADR 0032). Part of the simulation, so changing them changes states.</summary>
public static class DesignerRules
{
    /// <summary>Most modules a ship design may have.</summary>
    public const int MaxModules = 128;

    /// <summary>Most modules a station blueprint may have (ADR 0043).</summary>
    public const int MaxStationModules = 256;
}

public sealed partial class BeltNetwork
{
    /// <summary>The station a ship is docked at, when it has a shipyard module and belongs to the ship's owner; false otherwise.</summary>
    public bool TryGetShipyard(int player, out int station)
    {
        station = -1;
        if (!TryGetDock(player, out var dock) || !_structures.TryGetValue(dock.Station, out var st)) return false;
        if (st.Kind != StructureKind.Station || st.Owner != player || !st.ModuleMap.Values.Contains(ModuleKind.Shipyard)) return false;
        station = st.Id;
        return true;
    }

    /// <summary>The summed hold capacity of a module set, with the research bonus (what the ship's hold would hold after a redesign).</summary>
    internal int HoldCapacityOfKinds(IEnumerable<ModuleKind> kinds)
    {
        var sc = Content?.Structures;
        if (sc is null) return PlayerRules.HoldCapacity;
        int total = 0;
        foreach (var k in kinds) total += sc.HoldCapacity(k);
        return total * (100 + _bonus[(int)BonusKind.HoldCapacity]) / 100;
    }

    /// <summary>Items in the hold plus the items reserved by the ship's refinery: what the hold has to keep carrying after a redesign.</summary>
    internal int HoldLoadOf(int player) =>
        _players.TryGetValue(player, out var p) ? p.HoldTotal + p.ShipReserved : 0;

    internal static bool ModulesConnected(HashSet<(int, int, int)> keys) => IsConnected(keys);

    /// <summary>True when the ship's modules, as they would stand after a redesign, would intersect a module of any other structure at its docked pose.</summary>
    internal bool DesignOverlaps(Structure ship, IEnumerable<(int Mx, int My, int Mz)> keys)
    {
        var probe = ship.Clone();
        probe.ModuleMap.Clear();
        foreach (var k in keys) probe.ModuleMap[k] = ModuleKind.Hull;
        return DockPoseOverlaps(probe, ship.OriginX, ship.OriginY, ship.OriginZ);
    }

    /// <summary>Items the owner can spend at a shipyard: the hold plus the stock of the station the ship is docked at. True when both together cover the stacks.</summary>
    internal bool CanPayDesign(int player, int station, ItemStack[] net)
    {
        foreach (var s in net)
            if (HoldCount(player, s.Item) + StockCount(station, s.Item) < s.Count) return false;
        return true;
    }

    /// <summary>Pays the stacks from the hold first and from the station stock for the rest (the caller has checked <see cref="CanPayDesign"/>).</summary>
    internal void PayDesign(int player, int station, ItemStack[] net)
    {
        var st = _structures[station];
        foreach (var s in net)
        {
            int fromHold = Math.Min(s.Count, HoldCount(player, s.Item));
            if (fromHold > 0) Pay(player, new[] { new ItemStack(s.Item, fromHold) });
            int rest = s.Count - fromHold;
            if (rest > 0) st.Stock![s.Item] -= rest;
        }
    }

    /// <summary>Replaces the ship's modules by a new set the caller has validated: the removed ones go first (their state with them), then the new ones come in.</summary>
    internal void ApplyDesign(int ship, IEnumerable<(int Mx, int My, int Mz)> removed, IEnumerable<DesignModule> added)
    {
        foreach (var k in removed) RemoveStructureModuleUnchecked(ship, k.Mx, k.My, k.Mz);
        foreach (var m in added) AddStructureModuleUnchecked(ship, m.Mx, m.My, m.Mz, m.Kind);
    }
}
