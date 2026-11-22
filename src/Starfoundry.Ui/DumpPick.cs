using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// Chooses what the "dump" key throws out of the hold: the largest stack of a spare ore (carbon, quartz, titanium ore, nickel ore, and
/// ice only while the hydrogen stock is at least <see cref="IceKeepHydrogen"/>). Iron ore, ingots, plates and hydrogen are never
/// picked, and carbon keeps the jump cost so a ship is never stranded by a dump. Pure logic, tested without the engine.
/// </summary>
public static class DumpPick
{
    /// <summary>Ice is spare only while the hydrogen stock is at least this much.</summary>
    public const int IceKeepHydrogen = 20;

    private static readonly string[] SpareOres =
        { "core:carbon", "core:quartz", "core:titanium_ore", "core:nickel_ore", "core:ice" };

    /// <summary>The stack to dump, or null when nothing is spare.</summary>
    public static ItemStack? Choose(BeltNetwork net, int player)
    {
        var content = net.Content;
        if (content is null) return null;
        int hydrogen = content.FuelItem >= 0 ? net.HoldCount(player, content.FuelItem) : 0;
        ItemStack? best = null;
        foreach (var id in SpareOres)
        {
            int item = -1;
            for (int i = 0; i < content.Items.Count; i++)
                if (content.Items[i].Id == id) { item = i; break; }
            if (item < 0) continue;
            int held = net.HoldCount(player, item);
            if (id == "core:ice" && hydrogen < IceKeepHydrogen) continue;
            foreach (var j in net.JumpCost())
                if (j.Item == item) held -= j.Count; // keep the fuel for a jump
            if (held <= 0) continue;
            if (best is null || held > best.Count) best = new ItemStack(item, held);
        }
        return best;
    }
}
