namespace Starfoundry.Sim;

/// <summary>Read-only view of a machine, for rendering and tests.</summary>
public readonly record struct MachineInfo(
    int Id, int X, int Y, int Z, int Type, int Recipe, bool Crafting, int Remaining, int InputItems, int OutputItems);

/// <summary>A one-cell machine with input and output buffers, working through one recipe at a time.</summary>
internal sealed class Machine
{
    public readonly int Id;
    public readonly int X, Y, Z;
    public readonly int Type;

    public int Recipe = -1;

    /// <summary>Items waiting to be used, per item index.</summary>
    public readonly int[] In;

    /// <summary>Finished items waiting to leave, per item index.</summary>
    public readonly int[] Out;

    public bool Crafting;
    public int Remaining;

    /// <summary>Belt segments whose end feeds this machine.</summary>
    public readonly List<int> InputsFrom = new();

    /// <summary>Belt segments whose start this machine feeds (at most two).</summary>
    public readonly int[] OutBelts = new int[2];
    public int OutCount;
    public int RoundRobin;

    public Machine(int id, int x, int y, int z, int type, int itemCount)
    {
        Id = id; X = x; Y = y; Z = z; Type = type;
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
        var c = new Machine(Id, X, Y, Z, Type, In.Length)
        {
            Recipe = Recipe, Crafting = Crafting, Remaining = Remaining, OutCount = OutCount, RoundRobin = RoundRobin,
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
