using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The steps of the guided first hour, in the order a new player should meet them.</summary>
public enum Objective
{
    Fly,
    Mine,
    Refine,
    Drill,
    Smelter,
    Depot,
    Assembler,
    Lab,
    Research,
    Scan,
    Defend,
}

public sealed record ObjectiveState(Objective Objective, bool Done)
{
    /// <summary>Localisation key of the title, for example <c>obj.mine</c>.</summary>
    public string TitleKey => "obj." + Objective.ToString().ToLowerInvariant();

    /// <summary>Localisation key of the hint that says how to do it, for example <c>obj.mine.hint</c>.</summary>
    public string HintKey => TitleKey + ".hint";
}

/// <summary>
/// Watches the simulation and ticks off the guided steps. Completion is sticky: once a step is done it stays
/// done even if the player later tears the thing down. Pure logic, so it is tested without the engine.
/// </summary>
public sealed class ObjectiveTracker
{
    private readonly ContentDb _content;
    private readonly bool[] _done = new bool[Enum.GetValues<Objective>().Length];

    public ObjectiveTracker(ContentDb content) => _content = content;

    public IReadOnlyList<ObjectiveState> States =>
        Enum.GetValues<Objective>().Select(o => new ObjectiveState(o, _done[(int)o])).ToList();

    /// <summary>The first step that is not done yet, or null when everything is.</summary>
    public Objective? Current
    {
        get
        {
            foreach (var o in Enum.GetValues<Objective>())
                if (!_done[(int)o]) return o;
            return null;
        }
    }

    public bool IsDone(Objective o) => _done[(int)o];

    public int DoneCount => _done.Count(d => d);

    /// <summary>Re-evaluates every step against the world for one player. Cheap enough to call every frame.</summary>
    public void Update(BeltNetwork net, int player)
    {
        Mark(Objective.Fly, () =>
        {
            for (int d = 0; d < net.DepositCount; d++)
                if (net.IsDepositRevealed(d) && net.DepositRemaining(d) > 0 && net.InMineRange(player, d)) return true;
            return false;
        });
        Mark(Objective.Mine, () => net.MinedByHand >= 5);
        Mark(Objective.Refine, () => Has(net, player, "core:iron_ingot", 3));
        Mark(Objective.Drill, () => AnyMachine(net, m => _content.Machines[m.Type].Role == MachineRole.Drill && m.Deposit >= 0));
        Mark(Objective.Smelter, () => AnyMachine(net, m => _content.Machines[m.Type].Id == "core:smelter"));
        Mark(Objective.Depot, () => net.DepotDelivered > 0);
        Mark(Objective.Assembler, () => AnyMachine(net, m => _content.Machines[m.Type].Id == "core:assembler" && m.Recipe >= 0));
        Mark(Objective.Lab, () => AnyMachine(net, m => _content.Machines[m.Type].Role == MachineRole.Lab));
        Mark(Objective.Research, () => net.ResearchedCount > 0);
        Mark(Objective.Scan, () => net.ScanCooldown(player) > 0);
        Mark(Objective.Defend, () => AnyMachine(net, m => _content.Machines[m.Type].Role == MachineRole.Turret && m.InputItems > 0));
    }

    private void Mark(Objective o, Func<bool> test)
    {
        if (!_done[(int)o] && test()) _done[(int)o] = true;
    }

    private bool Has(BeltNetwork net, int player, string itemId, int count)
    {
        var item = _content.Items.FirstOrDefault(i => i.Id == itemId);
        return item is not null && net.HoldCount(player, item.Index) >= count;
    }

    private static bool AnyMachine(BeltNetwork net, Func<MachineInfo, bool> test)
    {
        for (int id = 0; id < net.MachineIdLimit; id++)
            if (net.IsMachineAlive(id) && test(net.GetMachineInfo(id))) return true;
        return false;
    }
}
