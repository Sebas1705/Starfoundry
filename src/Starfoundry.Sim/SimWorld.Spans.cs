namespace Starfoundry.Sim;

public sealed partial class SimWorld
{
    /// <summary>The large modules a design or blueprint would leave (ADR 0051).</summary>
    private sealed record SpanPlan(List<DesignSpan> Next, List<DesignSpan> Added)
    {
        public static SpanPlan Empty => new(new List<DesignSpan>(), new List<DesignSpan>());

        /// <summary>True when two module cells of the planned structure share a floor.</summary>
        public bool SameFloor((int Mx, int My, int Mz) a, (int Mx, int My, int Mz) b)
        {
            if (a == b) return true;
            foreach (var sp in Next)
                if (Covers(sp, a) && Covers(sp, b)) return true;
            return false;
        }

        private static bool Covers(DesignSpan sp, (int Mx, int My, int Mz) c) => c.My == sp.My && c.Mx >= sp.Mx && c.Mx < sp.Mx + sp.Size && c.Mz >= sp.Mz && c.Mz < sp.Mz + sp.Size;
    }

    /// <summary>
    /// Works out the large modules after a design or blueprint and checks them (ADR 0051): each is a full square of 2 or 3 module cells of one kind that its structure may build that large, in
    /// the new module set, none overlapping, and in a campaign world the research is done. A null <paramref name="requested"/> keeps the large modules whose cells are all still there with
    /// the same kind (the others fall apart into ordinary modules). In a world from before large modules a design carries none.
    /// </summary>
    private Reject PlanSpans(Structure s, IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> next, DesignSpan[]? requested, out SpanPlan plan)
    {
        plan = SpanPlan.Empty;
        if (!Network.SpanRules) return requested is { Length: > 0 } ? Reject.NotAllowedHere : Reject.None;
        bool Valid(DesignSpan sp)
        {
            if (sp.Size < 2 || sp.Size > 3 || !next.TryGetValue((sp.Mx, sp.My, sp.Mz), out var kind)) return false;
            if (ModuleKindIds.MaxSpan(s.Kind, kind) < sp.Size) return false;
            for (int dx = 0; dx < sp.Size; dx++)
                for (int dz = 0; dz < sp.Size; dz++)
                    if (!next.TryGetValue((sp.Mx + dx, sp.My, sp.Mz + dz), out var k) || k != kind) return false;
            return true;
        }
        var covered = new HashSet<(int, int, int)>();
        bool Take(DesignSpan sp)
        {
            for (int dx = 0; dx < sp.Size; dx++)
                for (int dz = 0; dz < sp.Size; dz++)
                    if (!covered.Add((sp.Mx + dx, sp.My, sp.Mz + dz))) return false;
            return true;
        }
        var list = new List<DesignSpan>();
        if (requested is null)
        {
            foreach (var (mx, my, mz, size) in s.SpanSet)
            {
                var sp = new DesignSpan(mx, my, mz, size);
                if (Valid(sp) && Take(sp)) list.Add(sp);
            }
        }
        else
        {
            if (requested.Length > DesignerRules.MaxSpans) return Reject.InvalidArgument;
            foreach (var sp in requested)
            {
                if (!Valid(sp) || !Take(sp)) return Reject.BadSpan;
                list.Add(sp);
            }
        }
        list.Sort((a, b) => (a.My, a.Mx, a.Mz, a.Size).CompareTo((b.My, b.Mx, b.Mz, b.Size)));
        var existing = new HashSet<(int, int, int, int)>(s.SpanSet);
        var added = list.Where(sp => !existing.Contains((sp.Mx, sp.My, sp.Mz, sp.Size))).ToList();
        if (added.Count > 0 && Campaign && !Network.HasAbility(Ability.LargeModules)) return Reject.Locked;
        plan = new SpanPlan(list, added);
        return Reject.None;
    }

    /// <summary>
    /// Takes the span discount off the price of the modules a design adds to new large modules: <see cref="ModuleKindIds.SpanPricePercent"/> of each module's price, rounded up per item, so a
    /// span costs a little less than its cells one by one. Cells that already stood cost nothing now and give no discount, and taking a span down refunds the usual half.
    /// </summary>
    private void PriceSpans(SpanPlan plan, IReadOnlyList<DesignModule> added, StructureContent kinds, SortedDictionary<int, int> net)
    {
        foreach (var sp in plan.Added)
            foreach (var m in added)
            {
                if (m.My != sp.My || m.Mx < sp.Mx || m.Mx >= sp.Mx + sp.Size || m.Mz < sp.Mz || m.Mz >= sp.Mz + sp.Size) continue;
                foreach (var c in kinds.ModuleCost(m.Kind))
                    net[c.Item] = net.GetValueOrDefault(c.Item) - (c.Count - (c.Count * ModuleKindIds.SpanPricePercent + 99) / 100);
            }
    }
}
