using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>One item of the cost panel: the full price of the parts that are new, the refund of the parts that go, the net, and whether it can be paid.</summary>
/// <param name="Price">Items the new parts cost in full.</param>
/// <param name="Refund">Items the removed parts give back (half of their cost, per module, as <c>DesignerApply</c> does).</param>
/// <param name="Net">Price minus refund: above zero is paid (the hold first, then the station stock), below zero is refunded to the hold.</param>
/// <param name="Available">What the hold and the station stock hold together.</param>
/// <param name="Missing">What is short of paying the net (zero when it can be paid or nothing is paid).</param>
public readonly record struct CostLine(int Item, string ItemId, int Price, int Refund, int Net, int Available, int Missing);

/// <summary>
/// The cost of a draft by item (docs/superpowers/specs/2026-11-26-blueprint-editor-design.md, section 3): computed from the diff and the content with the same
/// rule the simulation applies (a part that only moves is free, a removed part refunds half, the rest pays in full), so the panel can show price, refund,
/// net and what is missing at once; a test pins that the net equals <see cref="DesignReport.Cost"/> and <see cref="DesignReport.Refund"/>. Engine-free.
/// </summary>
public sealed class BlueprintCost
{
    public required IReadOnlyList<CostLine> Lines { get; init; }

    /// <summary>False in a world where building is free (sandbox, free build): every line is then zero.</summary>
    public bool Pays { get; init; }

    public bool CanPay => Lines.All(l => l.Missing == 0);

    public IReadOnlyList<CostLine> Missing => Lines.Where(l => l.Missing > 0).ToList();

    /// <summary>Items paid, as the command pays them (net above zero), in item order.</summary>
    public ItemStack[] ToPay => Lines.Where(l => l.Net > 0).Select(l => new ItemStack(l.Item, l.Net)).ToArray();

    /// <summary>Items refunded (net below zero), in item order.</summary>
    public ItemStack[] ToRefund => Lines.Where(l => l.Net < 0).Select(l => new ItemStack(l.Item, -l.Net)).ToArray();

    /// <summary>The summed net of every item, positive when the draft costs and negative when it refunds (for a one-line total).</summary>
    public int NetTotal => Lines.Sum(l => l.Net);

    /// <param name="pays">Whether building costs items in this world (<c>Mode == Campaign</c> and not free build).</param>
    /// <param name="available">The items that can be spent per item index: the hold plus the station stock.</param>
    public static BlueprintCost Compute(BlueprintDiff diff, ContentDb content, bool pays, Func<int, int> available, InteriorDiff? interior = null)
    {
        var price = new SortedDictionary<int, int>();
        var refund = new SortedDictionary<int, int>();
        if (pays && content.Structures is { } kinds)
        {
            foreach (var (_, kind) in diff.Added)
                foreach (var s in kinds.ModuleCost(kind)) price[s.Item] = price.GetValueOrDefault(s.Item) + s.Count;
            foreach (var (_, kind) in diff.Removed)
                foreach (var s in kinds.ModuleCost(kind)) refund[s.Item] = refund.GetValueOrDefault(s.Item) + s.Count / 2;
            if (interior is not null)
            {
                var (ip, ir) = interior.Amounts(content);
                foreach (var (item, n) in ip) price[item] = price.GetValueOrDefault(item) + n;
                foreach (var (item, n) in ir) refund[item] = refund.GetValueOrDefault(item) + n;
            }
        }
        var lines = new List<CostLine>();
        foreach (int item in price.Keys.Union(refund.Keys).OrderBy(i => i))
        {
            int p = price.GetValueOrDefault(item), r = refund.GetValueOrDefault(item), net = p - r, have = available(item);
            lines.Add(new CostLine(item, content.Items[item].Id, p, r, net, have, net > have ? net - have : 0));
        }
        return new BlueprintCost { Lines = lines, Pays = pays };
    }
}
