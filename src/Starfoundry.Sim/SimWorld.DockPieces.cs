namespace Starfoundry.Sim;

public sealed partial class SimWorld
{
    /// <summary>The docking collars a design or blueprint would leave, with what they add and remove (ADR 0047).</summary>
    private sealed record PiecePlan(List<DockPieceSpec> Next, List<DockPieceSpec> Added, List<DockPieceSpec> Removed);

    /// <summary>
    /// Works out the docking collars after a design or blueprint of a structure and checks them (ADR 0047): each sits on a horizontal face of a module of the new set that can
    /// host it, none twice, at most <see cref="DockRules.MaxPieces"/>, at least one when <paramref name="requireOne"/> (the structure docks through them), and none that a ship is
    /// docked at is taken away. A null <paramref name="requested"/> keeps the collars that still have a host. In a world with dock modules a design carries no collars.
    /// </summary>
    private Reject PlanDockPieces(Structure s, IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> next, DockPieceSpec[]? requested, bool requireOne, out PiecePlan plan)
    {
        plan = new PiecePlan(new List<DockPieceSpec>(), new List<DockPieceSpec>(), new List<DockPieceSpec>());
        if (!Network.DockPieceRules) return requested is { Length: > 0 } ? Reject.NotAllowedHere : Reject.None;
        foreach (var kind in next.Values)
            if (kind == ModuleKind.Dock) return Reject.KindNotAllowed; // the dock module is retired: the dock is a collar
        var wanted = new SortedSet<(int Mx, int My, int Mz, int Face)>();
        if (requested is null)
        {
            foreach (var p in s.DockPieceSet)
                if (next.TryGetValue((p.Mx, p.My, p.Mz), out var hk) && DockRules.CanHostPiece(hk, (Dir)p.Face)) wanted.Add(p);
        }
        else
        {
            if (requested.Length > DockRules.MaxPieces) return Reject.InvalidArgument;
            foreach (var p in requested)
            {
                if (!Enum.IsDefined(p.Face) || !next.TryGetValue((p.Mx, p.My, p.Mz), out var hk) || !DockRules.CanHostPiece(hk, p.Face)) return Reject.BadDockPiece;
                if (!wanted.Add((p.Mx, p.My, p.Mz, (int)p.Face))) return Reject.InvalidArgument;
            }
        }
        if (requireOne && wanted.Count == 0) return Reject.NoDockModule;
        foreach (var p in wanted) plan.Next.Add(new DockPieceSpec(p.Mx, p.My, p.Mz, (Dir)p.Face));
        foreach (var p in s.DockPieceSet)
        {
            if (wanted.Contains(p)) continue;
            var spec = new DockPieceSpec(p.Mx, p.My, p.Mz, (Dir)p.Face);
            if (Network.IsDockPieceInUse(s.Id, p.Mx, p.My, p.Mz, spec.Face)) return Reject.ModuleOccupied; // undock first
            plan.Removed.Add(spec);
        }
        foreach (var p in wanted)
            if (!s.DockPieceSet.Contains(p)) plan.Added.Add(new DockPieceSpec(p.Mx, p.My, p.Mz, (Dir)p.Face));
        return Reject.None;
    }

    /// <summary>The extra item map of a design: what the interior costs (may be null) plus the docking collars.</summary>
    private SortedDictionary<int, int> ExtraWithPieces(PiecePlan plan, SortedDictionary<int, int>? interiorNet)
    {
        var net = interiorNet is null ? new SortedDictionary<int, int>() : new SortedDictionary<int, int>(interiorNet);
        PriceDockPieces(plan, net);
        return net;
    }

    /// <summary>
    /// Adds the price of the docking collars to a net item map (positive pays, negative refunds): a collar that only moves (one removed, one added) is free, a new one pays
    /// <see cref="BeltNetwork.DockPieceCost"/> and a removed one refunds half of it, as modules do.
    /// </summary>
    private void PriceDockPieces(PiecePlan plan, SortedDictionary<int, int> net)
    {
        int moved = Math.Min(plan.Added.Count, plan.Removed.Count);
        int pay = plan.Added.Count - moved, back = plan.Removed.Count - moved;
        if (pay == 0 && back == 0) return;
        foreach (var c in Network.DockPieceCost())
            net[c.Item] = net.GetValueOrDefault(c.Item) + c.Count * pay - c.Count / 2 * back;
    }
}
