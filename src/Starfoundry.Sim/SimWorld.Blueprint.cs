namespace Starfoundry.Sim;

public sealed partial class SimWorld
{
    /// <summary>
    /// Checks a blueprint of one of the player's structures without changing anything (ADR 0043): the verdict and the price <see cref="ApplyBlueprint"/> would give, from the same
    /// code, so what the editor previews is what applying does. The blueprint is the structure's whole module list.
    /// </summary>
    public DesignReport CheckBlueprint(int player, int structure, IReadOnlyList<DesignModule> modules) => BlueprintCore(player, structure, modules, apply: false);

    private Reject ApplyBlueprintCore(int player, ApplyBlueprint a)
    {
        if (a.Modules is null) return Reject.InvalidArgument;
        return BlueprintCore(player, a.Structure, a.Modules, apply: true).Reject;
    }

    private DesignReport BlueprintCore(int player, int structure, IReadOnlyList<DesignModule> modules, bool apply)
    {
        if (!Network.BlueprintRules) return Fail(Reject.NotAllowedHere);
        if (Network.Content?.Structures is not { } kinds) return Fail(Reject.NoContent);
        var found = BuildableStructure(player, structure, out var s);
        if (found != Reject.None) return Fail(found);
        if (s!.Kind == StructureKind.Ship)
            return Network.ShipStructure(player) == structure ? DesignCore(player, modules, apply) : Fail(Reject.NotAllowedHere);

        // The set itself: bounded, defined kinds a station may hold, valid distinct coordinates.
        if (modules.Count < 1 || modules.Count > DesignerRules.MaxStationModules) return Fail(Reject.InvalidArgument);
        var next = new SortedDictionary<(int Mx, int My, int Mz), ModuleKind>();
        foreach (var m in modules)
        {
            if (!Enum.IsDefined(m.Kind)) return Fail(Reject.InvalidArgument);
            if (!StructureGrid.IsValidLocalModule(m.Mx, m.My, m.Mz)) return Fail(Reject.OutOfBounds);
            if (!kinds.IsDefined(m.Kind) || !ModuleKindIds.AllowedOn(StructureKind.Station, m.Kind)) return Fail(Reject.KindNotAllowed);
            if (Network.Rules.LegacyArms && ModuleKindIds.IsArmsKind(m.Kind)) return Fail(Reject.KindNotAllowed);
            if (!next.TryAdd((m.Mx, m.My, m.Mz), m.Kind)) return Fail(Reject.InvalidArgument);
        }

        var old = s.Modules;
        var removed = new List<(int Mx, int My, int Mz)>();
        var added = new List<DesignModule>();
        foreach (var (key, kind) in old)
            if (!next.TryGetValue(key, out var nk) || nk != kind) removed.Add(key);
        foreach (var (key, kind) in next)
            if (!old.TryGetValue(key, out var ok) || ok != kind) added.Add(new DesignModule(key.Mx, key.My, key.Mz, kind));

        // The core anchors the station: the one module of its kind, never added, removed or moved.
        foreach (var (key, kind) in old)
            if (kind == ModuleKind.Core && (!next.TryGetValue(key, out var ck) || ck != ModuleKind.Core)) return Fail(Reject.ModuleOccupied);
        if (next.Values.Count(k => k == ModuleKind.Core) != old.Values.Count(k => k == ModuleKind.Core)) return Fail(Reject.KindNotAllowed);
        if (!Network.Rules.LegacyDock && kinds.IsDefined(ModuleKind.Dock) && !next.Values.Contains(ModuleKind.Dock)) return Fail(Reject.NoDockModule); // ADR 0041
        if (!BeltNetwork.ModulesConnected(new HashSet<(int, int, int)>(next.Keys))) return Fail(Reject.NotAdjacent);

        // New kinds need their research in a campaign world, as placing them one by one does.
        if (Campaign)
            foreach (var m in added)
                if (ModuleKindIds.RequiredAbility(m.Kind) is { } needed && !Network.HasAbility(needed)) return Fail(Reject.Locked);

        // What goes (or changes kind) must not be a port a ship is docked with, and must be empty: no machine or belt inside.
        var cells = CellsFor(structure, create: false);
        foreach (var key in removed)
        {
            if (Network.IsDockPortInUse(structure, key.Mx, key.My, key.Mz)) return Fail(Reject.ModuleOccupied);
            if (cells is null) continue;
            int x0 = key.Mx * StationGrid.ModuleX, y0 = key.My * StationGrid.ModuleY, z0 = key.Mz * StationGrid.ModuleZ;
            for (int x = x0; x < x0 + StationGrid.ModuleX; x++)
                for (int y = y0; y < y0 + StationGrid.ModuleY; y++)
                    for (int z = z0; z < z0 + StationGrid.ModuleZ; z++)
                        if (cells.ContainsKey(Key(x, y, z))) return Fail(Reject.ModuleOccupied);
        }

        // Room: a new module may not stand where a docked ship is, nor inside the rock of a deposit.
        foreach (var m in added)
        {
            if (Network.ModuleOverlapsDocked(structure, m.Mx, m.My, m.Mz)) return Fail(Reject.Overlap);
            if (ModuleTouchesDepositRock(s, m.Mx, m.My, m.Mz)) return Fail(Reject.OverlapsDeposit);
        }

        // Reach: with the ship docked at this station the whole of it is at hand, else every module that changes must be in build range.
        bool dockedHere = Network.TryGetDock(player, out var dock) && dock.Station == structure;
        if (!dockedHere)
            foreach (var (mx, my, mz) in removed.Concat(added.Select(m => (m.Mx, m.My, m.Mz))))
            {
                var centre = ModuleCentre(s, mx, my, mz);
                if (!StructurePointInRange(player, s, centre.X, centre.Y, centre.Z)) return Fail(Reject.OutOfRange);
            }

        var (addedByKind, cost, refund, moved, affordable) = PriceDesign(player, structure, kinds, old, removed, added);
        if (!affordable) return new DesignReport(Reject.NotEnoughItems, cost, refund, added.Count, removed.Count, moved);
        var report = new DesignReport(Reject.None, cost, refund, added.Count, removed.Count, moved);
        if (!apply) return report;

        if (cost.Length > 0) Network.PayDesign(player, structure, cost);
        Network.ApplyDesign(structure, removed, added);
        if (refund.Length > 0) Network.Refund(player, refund);
        foreach (var (kind, n) in addedByKind)
            for (int i = 0; i < n; i++) Network.CountModuleBuilt(kind);
        return report;
    }
}
