using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Where a research node sits in the tree: its column (the depth of its longest requirement chain), its row inside its field's band and the band.</summary>
public readonly record struct TreeSlot(int Index, int Column, int Row, int Band, ResearchField Field);

/// <summary>
/// Lays the research nodes out as a tree: one horizontal band per science field (the basic tier first, then the fields that have nodes), the column
/// of a node is the length of its longest chain of requirements, and nodes of the same band and column stack in content order. Pure logic over the
/// entries, tested without the engine; the panel draws the slots and the lines between a node and what it requires.
/// </summary>
public static class ResearchTreeLayout
{
    public static IReadOnlyList<TreeSlot> Layout(IReadOnlyList<ResearchEntry> entries)
    {
        var byIndex = entries.ToDictionary(e => e.Index);
        var depth = new Dictionary<int, int>();
        int Depth(int index, int guard = 0)
        {
            if (depth.TryGetValue(index, out int d)) return d;
            if (!byIndex.TryGetValue(index, out var e) || guard > 64) return 0;
            int result = 0;
            foreach (var r in e.Requires) result = Math.Max(result, Depth(r.Index, guard + 1) + 1);
            return depth[index] = result;
        }

        var slots = new List<TreeSlot>();
        int band = 0;
        foreach (var field in ResearchView.Fields)
        {
            var nodes = entries.Where(e => e.Field == field).OrderBy(e => e.Index).ToList();
            if (nodes.Count == 0) continue;
            var rowOfColumn = new Dictionary<int, int>();
            foreach (var n in nodes)
            {
                int column = Depth(n.Index);
                int row = rowOfColumn.GetValueOrDefault(column);
                rowOfColumn[column] = row + 1;
                slots.Add(new TreeSlot(n.Index, column, row, band, field));
            }
            band++;
        }
        return slots;
    }

    /// <summary>Rows each band needs (the tallest column of the band), in band order.</summary>
    public static IReadOnlyList<int> BandRows(IReadOnlyList<TreeSlot> slots) =>
        slots.GroupBy(s => s.Band).OrderBy(g => g.Key).Select(g => g.Max(s => s.Row) + 1).ToList();

    public static int Columns(IReadOnlyList<TreeSlot> slots) => slots.Count == 0 ? 0 : slots.Max(s => s.Column) + 1;
}
