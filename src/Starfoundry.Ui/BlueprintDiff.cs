using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>What a ghost of the 3D plan means.</summary>
public enum GhostState { Unchanged, Added, Removed, Moved, Invalid }

/// <summary>A module that keeps its kind but changes cell: shown as a yellow ghost at <see cref="To"/> and a trail from <see cref="From"/>.</summary>
public readonly record struct GhostMove((int Mx, int My, int Mz) From, (int Mx, int My, int Mz) To, ModuleKind Kind);

/// <summary>
/// The difference between the structure's modules and the draft's, as the ghosts show it: parts that are new (green), parts that go (red), parts that only move
/// (yellow) and cells marked invalid by the validation (red hatch). The moved parts are the ones the simulation prices as free: for every kind, as many removals
/// and additions cancel as there are of the smaller side (<see cref="DesignReport.Moved"/>), paired here by distance so the view can draw a trail. Engine-free.
/// </summary>
public sealed class BlueprintDiff
{
    public required IReadOnlyList<((int Mx, int My, int Mz) Cell, ModuleKind Kind)> Added { get; init; }
    public required IReadOnlyList<((int Mx, int My, int Mz) Cell, ModuleKind Kind)> Removed { get; init; }
    public required IReadOnlyList<GhostMove> Moved { get; init; }
    public required IReadOnlyList<((int Mx, int My, int Mz) Cell, ModuleKind Kind)> Unchanged { get; init; }
    public required IReadOnlySet<(int Mx, int My, int Mz)> Invalid { get; init; }

    /// <summary>Modules the simulation counts as added: the new ones and the moved ones (<see cref="DesignReport.Added"/>).</summary>
    public int AddedTotal => Added.Count + Moved.Count;

    /// <summary>Modules the simulation counts as removed (<see cref="DesignReport.Removed"/>).</summary>
    public int RemovedTotal => Removed.Count + Moved.Count;

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Moved.Count == 0;

    /// <summary>The state a cell is drawn in (a moved part counts at its target); an invalid cell wins.</summary>
    public GhostState StateAt((int Mx, int My, int Mz) cell)
    {
        if (Invalid.Contains(cell)) return GhostState.Invalid;
        if (Added.Any(a => a.Cell == cell)) return GhostState.Added;
        if (Moved.Any(m => m.To == cell)) return GhostState.Moved;
        if (Removed.Any(r => r.Cell == cell)) return GhostState.Removed;
        return GhostState.Unchanged;
    }

    public static BlueprintDiff Compute(
        IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> original,
        IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> draft,
        IEnumerable<(int Mx, int My, int Mz)>? invalid = null)
    {
        var added = new List<((int Mx, int My, int Mz) Cell, ModuleKind Kind)>();
        var removed = new List<((int Mx, int My, int Mz) Cell, ModuleKind Kind)>();
        var same = new List<((int Mx, int My, int Mz) Cell, ModuleKind Kind)>();
        foreach (var (k, v) in original)
            if (!draft.TryGetValue(k, out var d) || d != v) removed.Add((k, v));
        foreach (var (k, v) in draft)
            if (!original.TryGetValue(k, out var o) || o != v) added.Add((k, v)); else same.Add((k, v));
        added.Sort((a, b) => a.Cell.CompareTo(b.Cell));
        removed.Sort((a, b) => a.Cell.CompareTo(b.Cell));
        same.Sort((a, b) => a.Cell.CompareTo(b.Cell));

        // Pair the removals and additions of one kind, nearest first, so a part that only moved is one yellow ghost with a trail.
        var moves = new List<GhostMove>();
        var usedAdd = new HashSet<int>();
        var usedRem = new HashSet<int>();
        foreach (var kind in added.Select(a => a.Kind).Distinct().Where(k => removed.Any(r => r.Kind == k)).OrderBy(k => (int)k))
        {
            var pairs = new List<(int Dist, int R, int A)>();
            for (int r = 0; r < removed.Count; r++)
                for (int a = 0; a < added.Count; a++)
                    if (removed[r].Kind == kind && added[a].Kind == kind)
                    {
                        var f = removed[r].Cell; var t = added[a].Cell;
                        pairs.Add((Math.Abs(f.Mx - t.Mx) + Math.Abs(f.My - t.My) + Math.Abs(f.Mz - t.Mz), r, a));
                    }
            pairs.Sort((x, y) => x.Dist != y.Dist ? x.Dist.CompareTo(y.Dist) : x.R != y.R ? x.R.CompareTo(y.R) : x.A.CompareTo(y.A));
            foreach (var (_, r, a) in pairs)
            {
                if (usedRem.Contains(r) || usedAdd.Contains(a)) continue;
                usedRem.Add(r); usedAdd.Add(a);
                moves.Add(new GhostMove(removed[r].Cell, added[a].Cell, kind));
            }
        }
        moves.Sort((a, b) => a.To.CompareTo(b.To));
        return new BlueprintDiff
        {
            Added = added.Where((_, i) => !usedAdd.Contains(i)).ToList(),
            Removed = removed.Where((_, i) => !usedRem.Contains(i)).ToList(),
            Moved = moves,
            Unchanged = same,
            Invalid = new HashSet<(int Mx, int My, int Mz)>(invalid ?? Array.Empty<(int, int, int)>()),
        };
    }
}
