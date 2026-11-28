namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    /// <summary>ADR 0051: true when large modules (spans) exist in this world; false in worlds read from logs older than version 29.</summary>
    public bool SpanRules => !Rules.LegacyLargeModules;

    /// <summary>The large modules of a structure, in anchor order.</summary>
    public IEnumerable<DesignSpan> Spans(int structure)
    {
        if (!_structures.TryGetValue(structure, out var s)) yield break;
        foreach (var (mx, my, mz, size) in s.SpanSet) yield return new DesignSpan(mx, my, mz, size);
    }

    /// <summary>The large module that covers a module cell of a structure, when there is one.</summary>
    public bool TryGetSpanAt(int structure, (int Mx, int My, int Mz) cell, out DesignSpan span)
    {
        span = default;
        if (!_structures.TryGetValue(structure, out var s) || s.SpanSet.Count == 0) return false;
        foreach (var (mx, my, mz, size) in s.SpanSet)
            if (my == cell.My && cell.Mx >= mx && cell.Mx < mx + size && cell.Mz >= mz && cell.Mz < mz + size) { span = new DesignSpan(mx, my, mz, size); return true; }
        return false;
    }

    /// <summary>True when two module cells share one floor: the same cell, or two cells of the same large module (the seam between them has no wall, ADR 0051).</summary>
    public bool SameFloor(int structure, (int Mx, int My, int Mz) a, (int Mx, int My, int Mz) b) =>
        a == b || (TryGetSpanAt(structure, a, out var sa) && TryGetSpanAt(structure, b, out var sb) && sa == sb);

    /// <summary>Replaces all the large modules of a structure by a set the caller has validated.</summary>
    internal void SetSpans(int structure, IEnumerable<DesignSpan> spans)
    {
        var s = GetStructure(structure);
        s.SpanSet.Clear();
        foreach (var p in spans) s.SpanSet.Add((p.Mx, p.My, p.Mz, p.Size));
        StructureVersion++;
    }

    /// <summary>Makes a large module without any rule check (templates and tests).</summary>
    public void AddSpanUnchecked(int structure, int mx, int my, int mz, int size)
    {
        var s = GetStructure(structure);
        if (!s.SpanSet.Add((mx, my, mz, size))) throw new InvalidOperationException("The large module already exists.");
        StructureVersion++;
    }
}
