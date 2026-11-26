namespace Starfoundry.Sim;

/// <summary>What a ship design would do (ADR 0032): the verdict, the items it costs and gives back, and how many modules it adds, removes and moves.</summary>
/// <param name="Cost">Items paid in total (the hold first, then the station stock), after the refund of the modules that go is set against them.</param>
/// <param name="Refund">Items given back to the hold: what the modules that go refund beyond the cost of the new ones.</param>
/// <param name="Added">Modules that are new or change kind (an upgrade removes the old mark and adds the new one).</param>
/// <param name="Moved">Modules that change place but keep their kind: they cost nothing and refund nothing. Counted inside <see cref="Added"/> and <see cref="Removed"/>.</param>
public sealed record DesignReport(Reject Reject, ItemStack[] Cost, ItemStack[] Refund, int Added, int Removed, int Moved)
{
    public bool Ok => Reject == Reject.None;
}

public sealed partial class SimWorld
{
    private static readonly ItemStack[] NoStacks = System.Array.Empty<ItemStack>();

    /// <summary>
    /// Checks a ship design for the player without changing anything: the same verdict <see cref="DesignerApply"/> would give, with the cost. The design
    /// is the ship's whole module list. It needs the ship docked at one of the player's stations that has a shipyard.
    /// </summary>
    public DesignReport CheckDesign(int player, IReadOnlyList<DesignModule> modules) => DesignCore(player, modules, apply: false);

    private Reject DesignerApplyCore(int player, DesignerApply d)
    {
        if (d.Modules is null) return Reject.InvalidArgument;
        return DesignCore(player, d.Modules, apply: true).Reject;
    }

    private static DesignReport Fail(Reject r) => new(r, NoStacks, NoStacks, 0, 0, 0);

    private DesignReport DesignCore(int player, IReadOnlyList<DesignModule> modules, bool apply)
    {
        if (!Network.StructureRules) return Fail(Reject.NotAllowedHere);
        if (Network.Content?.Structures is not { } kinds) return Fail(Reject.NoContent);
        int shipId = Network.ShipStructure(player);
        if (shipId < 0 || Network.ShipLost(player)) return Fail(Reject.NoSuchStructure);
        if (!Network.TryGetDock(player, out var dock)) return Fail(Reject.NotDocked);
        if (!Network.TryGetShipyard(player, out int station)) return Fail(Reject.NoShipyard);
        var ship = Network.GetStructure(shipId);

        // The set itself: bounded, defined kinds the catalogue allows on a ship, valid distinct coordinates.
        if (modules.Count < 1 || modules.Count > DesignerRules.MaxModules) return Fail(Reject.InvalidArgument);
        var next = new SortedDictionary<(int Mx, int My, int Mz), ModuleKind>();
        foreach (var m in modules)
        {
            if (!Enum.IsDefined(m.Kind)) return Fail(Reject.InvalidArgument);
            if (!StructureGrid.IsValidLocalModule(m.Mx, m.My, m.Mz)) return Fail(Reject.OutOfBounds);
            if (Network.StationFirst && m.Kind == ModuleKind.Workshop) return Fail(Reject.UseStation); // ADR 0039
            if (!kinds.IsDefined(m.Kind) || !ModuleKindIds.AllowedOn(StructureKind.Ship, m.Kind)) return Fail(Reject.KindNotAllowed);
            if (!next.TryAdd((m.Mx, m.My, m.Mz), m.Kind)) return Fail(Reject.InvalidArgument);
        }

        var old = ship.Modules;
        var removed = new List<(int Mx, int My, int Mz)>();
        var added = new List<DesignModule>();
        foreach (var (key, kind) in old)
            if (!next.TryGetValue(key, out var nk) || nk != kind) removed.Add(key);
        foreach (var (key, kind) in next)
            if (!old.TryGetValue(key, out var ok) || ok != kind) added.Add(new DesignModule(key.Mx, key.My, key.Mz, kind));

        // The ship cannot do without a cockpit, an engine, a reactor and a cargo module (ADR 0022); it has no core.
        if (!next.Values.Contains(ModuleKind.Cockpit) || !next.Values.Contains(ModuleKind.Engine)) return Fail(Reject.NoCockpitOrEngine);
        if (!next.Values.Any(ModuleKindIds.IsReactor) || !next.Values.Contains(ModuleKind.Cargo)) return Fail(Reject.LastEssentialModule);
        if (!BeltNetwork.ModulesConnected(new HashSet<(int, int, int)>(next.Keys))) return Fail(Reject.NotAdjacent); // the walkable path stays whole

        // New kinds need their research in a campaign world, as placing them one by one does.
        if (Campaign)
            foreach (var m in added)
                if (ModuleKindIds.RequiredAbility(m.Kind) is { } needed && !Network.HasAbility(needed)) return Fail(Reject.Locked);

        // The docked port stays: same module, same kind, and its face is still free.
        var pk = (dock.ShipMx, dock.ShipMy, dock.ShipMz);
        if (!next.TryGetValue(pk, out var portKind) || !old.TryGetValue(pk, out var oldKind) || portKind != oldKind) return Fail(Reject.ModuleOccupied);
        var face = Dirs.Vector(dock.ShipFace);
        if (next.ContainsKey((pk.Item1 + face.X, pk.Item2, pk.Item3 + face.Z))) return Fail(Reject.ModuleOccupied);

        // What goes (or changes kind) must be empty: no machine or belt inside.
        var cells = CellsFor(shipId, create: false);
        if (cells is not null)
            foreach (var key in removed)
            {
                int x0 = key.Mx * StationGrid.ModuleX, y0 = key.My * StationGrid.ModuleY, z0 = key.Mz * StationGrid.ModuleZ;
                for (int x = x0; x < x0 + StationGrid.ModuleX; x++)
                    for (int y = y0; y < y0 + StationGrid.ModuleY; y++)
                        for (int z = z0; z < z0 + StationGrid.ModuleZ; z++)
                            if (cells.ContainsKey(Key(x, y, z))) return Fail(Reject.ModuleOccupied);
            }

        // Room: the new hold must still take what the old one carries, and the new modules must not sit inside the station.
        if (Network.HoldCapacityOfKinds(next.Values) < Network.HoldLoadOf(player)) return Fail(Reject.NoRoom);
        if (Network.DesignOverlaps(ship, next.Keys)) return Fail(Reject.Overlap);

        // The price: a module that only moves (a removal and an addition of one kind cancel) costs nothing; the rest pays full price and refunds half.
        var addedByKind = added.GroupBy(m => m.Kind).ToDictionary(g => g.Key, g => g.Count());
        var removedByKind = removed.GroupBy(k => old[k]).ToDictionary(g => g.Key, g => g.Count());
        int moved = 0;
        foreach (var kind in addedByKind.Keys.ToArray())
        {
            removedByKind.TryGetValue(kind, out int r);
            int pair = Math.Min(addedByKind[kind], r);
            moved += pair;
            addedByKind[kind] -= pair;
            if (r > 0) removedByKind[kind] = r - pair;
        }
        var net = new SortedDictionary<int, int>();
        foreach (var (kind, n) in addedByKind)
            foreach (var s in kinds.ModuleCost(kind)) net[s.Item] = net.GetValueOrDefault(s.Item) + s.Count * n;
        foreach (var (kind, n) in removedByKind)
            foreach (var s in kinds.ModuleCost(kind)) net[s.Item] = net.GetValueOrDefault(s.Item) - s.Count / 2 * n;
        bool pays = Campaign && !Network.Rules.FreeBuild;
        var cost = pays ? net.Where(kv => kv.Value > 0).Select(kv => new ItemStack(kv.Key, kv.Value)).ToArray() : NoStacks;
        var refund = pays ? net.Where(kv => kv.Value < 0).Select(kv => new ItemStack(kv.Key, -kv.Value)).ToArray() : NoStacks;
        if (pays && !Network.CanPayDesign(player, station, cost)) return new DesignReport(Reject.NotEnoughItems, cost, refund, added.Count, removed.Count, moved);
        var report = new DesignReport(Reject.None, cost, refund, added.Count, removed.Count, moved);
        if (!apply) return report;

        if (cost.Length > 0) Network.PayDesign(player, station, cost);
        Network.ApplyDesign(shipId, removed, added);
        if (refund.Length > 0) Network.Refund(player, refund);
        foreach (var (kind, n) in addedByKind)
            for (int i = 0; i < n; i++) Network.CountModuleBuilt(kind); // a module that only moved was not built again
        return report;
    }
}
