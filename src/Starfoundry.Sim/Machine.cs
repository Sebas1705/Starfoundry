namespace Starfoundry.Sim;

/// <summary>Read-only view of a machine, for rendering and tests.</summary>
public readonly record struct MachineInfo(
    int Id, int X, int Y, int Z, int Type, int Recipe, bool Crafting, int Remaining, int InputItems, int OutputItems,
    int Deposit = -1, int Hp = 0, int TurretCooldown = 0, int Structure = 0, int Turn = 0);

/// <summary>A one-cell machine with input and output buffers, working through one recipe at a time.</summary>
internal sealed class Machine
{
    public readonly int Id;
    public readonly int X, Y, Z;
    public readonly int Type;

    /// <summary>Quarter turns of the footprint about the vertical axis (ADR 0050); 0 for every one-cell machine.</summary>
    public readonly int Turn;

    public int Recipe = -1;

    /// <summary>Items waiting to be used, per item index.</summary>
    public readonly int[] In;

    /// <summary>Finished items waiting to leave, per item index.</summary>
    public readonly int[] Out;

    public bool Crafting;
    public int Remaining;

    /// <summary>Deposit a drill pulls from, or -1.</summary>
    public int Deposit = -1;

    /// <summary>Player who built the machine. Depots deliver into this player's hold.</summary>
    public int Owner = -1;

    /// <summary>Hit points left, and the ticks until a turret can fire again.</summary>
    public int Hp;
    public int TurretCooldown;

    /// <summary>Power work credit in per mille (structure worlds): it gains the structure's satisfaction each tick and the machine works
    /// on a tick when it reaches 1000. <see cref="Powered"/> is the result of the current tick (recomputed at the start of every tick).</summary>
    public int PowerCredit;
    public bool Powered = true;

    /// <summary>Belt segments whose end feeds this machine.</summary>
    public readonly List<int> InputsFrom = new();

    /// <summary>Belt segments whose start this machine feeds (at most two).</summary>
    public readonly int[] OutBelts = new int[2];
    public int OutCount;
    public int RoundRobin;

    /// <summary>Structure the machine belongs to (0 = the legacy world); X, Y, Z are local cells of that structure.</summary>
    public readonly int Structure;

    public Machine(int id, int x, int y, int z, int type, int itemCount, int structure = 0, int turn = 0)
    {
        Id = id; X = x; Y = y; Z = z; Type = type; Structure = structure; Turn = turn;
        In = new int[itemCount];
        Out = new int[itemCount];
    }

    public int HeldInputs => Sum(In);
    public int HeldOutputs => Sum(Out);

    private static int Sum(int[] a)
    {
        int n = 0;
        foreach (int v in a) n += v;
        return n;
    }

    public Machine Clone()
    {
        var c = new Machine(Id, X, Y, Z, Type, In.Length, Structure, Turn)
        {
            Recipe = Recipe, Crafting = Crafting, Remaining = Remaining, OutCount = OutCount, RoundRobin = RoundRobin,
            Deposit = Deposit, Owner = Owner, Hp = Hp, TurretCooldown = TurretCooldown,
            PowerCredit = PowerCredit, Powered = Powered,
        };
        Array.Copy(In, c.In, In.Length);
        Array.Copy(Out, c.Out, Out.Length);
        Array.Copy(OutBelts, c.OutBelts, OutBelts.Length);
        c.InputsFrom.AddRange(InputsFrom);
        return c;
    }

    public void DropOutBelt(int segment)
    {
        if (OutCount == 2 && OutBelts[0] == segment) OutBelts[0] = OutBelts[1];
        OutCount--;
        OutBelts[OutCount] = 0;
        RoundRobin = 0;
    }
}
