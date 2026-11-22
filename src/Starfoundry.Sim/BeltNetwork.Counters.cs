namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    // Progress counters read by scenario goals ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)). They only ever grow, are copied by
    // Clone, validated, and mixed into the checksum only while they are non-zero (a world that never touched them hashes as before).

    private static readonly int ModuleKindSlots = Enum.GetValues<ModuleKind>().Max(k => (int)k) + 1;

    private long[] _produced = System.Array.Empty<long>();
    private long[] _minedItem = System.Array.Empty<long>();
    private readonly long[] _modulesBuilt = new long[ModuleKindSlots];
    private readonly long[] _modulesLostKind = new long[ModuleKindSlots];
    private long _producedSum, _minedItemSum, _modulesBuiltSum, _modulesLostKindSum;

    /// <summary>Stations the players founded with the found-station command (a station that was later destroyed still counts).</summary>
    public long StationsFounded { get; private set; }

    /// <summary>Reactor-ticks spent without fuel (a reactor needed hydrogen and the owner's hold had none), summed over all reactors.</summary>
    public long FuelStarvedTicks { get; private set; }

    private void InitCounters()
    {
        int items = Content?.Items.Count ?? 0;
        _produced = new long[items];
        _minedItem = new long[items];
    }

    /// <summary>Units of an item created in the world so far: machine outputs (crafting and drills), ship crafting, hand mining and drones.</summary>
    public long ProducedTotal(int item) => (uint)item < (uint)_produced.Length ? _produced[item] : 0;

    /// <summary>Units of an item taken out of deposits by hand, by drone bays and by drills.</summary>
    public long MinedTotal(int item) => (uint)item < (uint)_minedItem.Length ? _minedItem[item] : 0;

    /// <summary>Modules of a kind placed by the placement command (the modules of a template at world creation do not count).</summary>
    public long ModulesBuilt(ModuleKind kind) => (uint)(int)kind < (uint)_modulesBuilt.Length ? _modulesBuilt[(int)kind] : 0;

    /// <summary>Modules of a kind destroyed so far (including those that fell with their station).</summary>
    public long ModulesLostOf(ModuleKind kind) => (uint)(int)kind < (uint)_modulesLostKind.Length ? _modulesLostKind[(int)kind] : 0;

    /// <summary>Systems the player has been in, home included.</summary>
    public int VisitedSystemCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < SystemCount; i++) if (IsSystemVisited(i)) n++;
            return n;
        }
    }

    private void CountProduced(int item, int count)
    {
        if ((uint)item >= (uint)_produced.Length) return;
        _produced[item] += count;
        _producedSum += count;
    }

    private void CountMined(int item, int count)
    {
        if ((uint)item >= (uint)_minedItem.Length) return;
        _minedItem[item] += count;
        _minedItemSum += count;
    }

    internal void CountModuleBuilt(ModuleKind kind)
    {
        _modulesBuilt[(int)kind]++;
        _modulesBuiltSum++;
    }

    internal void CountStationFounded() => StationsFounded++;

    private void CountModuleLost(ModuleKind kind)
    {
        _modulesLostKind[(int)kind]++;
        _modulesLostKindSum++;
    }

    private void CopyCountersFrom(BeltNetwork other)
    {
        _produced = (long[])other._produced.Clone();
        _minedItem = (long[])other._minedItem.Clone();
        System.Array.Copy(other._modulesBuilt, _modulesBuilt, _modulesBuilt.Length);
        System.Array.Copy(other._modulesLostKind, _modulesLostKind, _modulesLostKind.Length);
        _producedSum = other._producedSum;
        _minedItemSum = other._minedItemSum;
        _modulesBuiltSum = other._modulesBuiltSum;
        _modulesLostKindSum = other._modulesLostKindSum;
        StationsFounded = other.StationsFounded;
        FuelStarvedTicks = other.FuelStarvedTicks;
    }

    private void MixCounters(Action<long> mix)
    {
        if (_producedSum != 0) { mix(-501); foreach (long v in _produced) mix(v); }
        if (_minedItemSum != 0) { mix(-502); foreach (long v in _minedItem) mix(v); }
        if (_modulesBuiltSum != 0) { mix(-503); foreach (long v in _modulesBuilt) mix(v); }
        if (_modulesLostKindSum != 0) { mix(-504); foreach (long v in _modulesLostKind) mix(v); }
        if (StationsFounded != 0) { mix(-505); mix(StationsFounded); }
        if (FuelStarvedTicks != 0) { mix(-506); mix(FuelStarvedTicks); }
    }

    private void ValidateCounters()
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        if (_produced.Sum() != _producedSum || _minedItem.Sum() != _minedItemSum) Fail("item counters do not add up");
        if (_modulesBuilt.Sum() != _modulesBuiltSum || _modulesLostKind.Sum() != _modulesLostKindSum) Fail("module counters do not add up");
        if (_produced.Any(v => v < 0) || _minedItem.Any(v => v < 0) || _modulesBuilt.Any(v => v < 0) || _modulesLostKind.Any(v => v < 0)) Fail("a counter is negative");
        if (_minedItemSum < MinedByHand + MinedByDrones) Fail($"mined items {_minedItemSum} fewer than hand {MinedByHand} + drones {MinedByDrones}");
        if (_producedSum < MachineOutputsCreated) Fail($"produced {_producedSum} fewer than machine outputs {MachineOutputsCreated}");
        if (_modulesLostKindSum != ModulesLost) Fail($"module losses by kind {_modulesLostKindSum} but {ModulesLost} lost");
        if (StationsFounded < 0 || FuelStarvedTicks < 0) Fail("negative stations founded");
    }
}
