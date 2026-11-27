using System.Collections.Generic;
using System.Linq;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Words for the clusters of the rock field (ADR 0046): a cluster is named by its shape and its dominant ore, in both languages.</summary>
public partial class Play
{
    /// <summary>"Iron ore cluster", "Ice belt" or "Cúmulo de mineral de hierro": shape plus the dominant ore.</summary>
    private string ClusterName(ClusterParent p)
    {
        string ore = ItemName(p.Item);
        if (Loc.Lang == Language.Es) ore = ore.ToLower(Loc.Culture);
        return L(p.Shape switch { 1 => "cluster.belt", 2 => "cluster.shell", _ => "cluster.sphere" }, ore);
    }

    /// <summary>A line for the tooltip of a rock that belongs to a cluster ("Part of: ice belt (richness 3 of 5)"), else nothing.</summary>
    private string ClusterLine(int deposit) =>
        Net.DepositIsFieldRock(deposit) && Net.DepositCluster(deposit) is { } p ? "\n" + L("tip.cluster", ClusterName(p), p.Richness) : "";

    /// <summary>The cluster most of the revealed rocks belong to, named, or null when the scan found no cluster rock.</summary>
    private string? ScanClusterName(IReadOnlyList<int> revealed)
    {
        var ids = revealed.Where(d => Net.DepositIsFieldRock(d) && Net.DepositClusterId(d) >= 0).GroupBy(d => Net.DepositClusterId(d)).OrderByDescending(g => g.Count()).FirstOrDefault();
        return ids is not null && Net.DepositCluster(ids.First()) is { } p ? ClusterName(p) : null;
    }
}
