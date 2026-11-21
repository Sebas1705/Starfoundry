using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Ctrl+Delete: throw the largest stack of spare ore out of the hold (see <see cref="DumpPick"/>).</summary>
public partial class Play
{
    private void DumpSpareOre()
    {
        if (!Net.HasPlayer(Player)) return;
        var stack = DumpPick.Choose(Net, Player);
        if (stack is null) { ToastLogistics(L("toast.dumpNothing")); return; }
        Send(new DumpItems(stack.Item, stack.Count));
    }
}
