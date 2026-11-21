namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    private List<Machine?> _machines = new();
    private int _machinesAlive;

    /// <summary>Items taken out of machine input buffers when crafts start.</summary>
    public long MachineInputsConsumed { get; private set; }

    /// <summary>Items created into machine output buffers when crafts finish.</summary>
    public long MachineOutputsCreated { get; private set; }

    public int MachineCount => _machinesAlive;
    public int MachineIdLimit => _machines.Count;
    public bool IsMachineAlive(int id) => (uint)id < (uint)_machines.Count && _machines[id] is not null;

    /// <summary>Items currently inside machine buffers.</summary>
    public long MachineHeldItems
    {
        get
        {
            long n = 0;
            foreach (var m in _machines) if (m is not null) n += m.HeldInputs + m.HeldOutputs;
            return n;
        }
    }

    /// <summary>Adds a machine of a content type, optionally with a recipe (-1 for none).</summary>
    public int AddMachine(int x, int y, int z, int type, int recipe, int owner = -1, int structure = 0)
    {
        CheckLocal(structure, x, y, z);
        var content = Content ?? throw new InvalidOperationException("The network has no content.");
        if (!content.IsMachine(type)) throw new ArgumentOutOfRangeException(nameof(type));
        CheckRecipe(type, recipe);

        int id = _machines.Count;
        _machines.Add(new Machine(id, x, y, z, type, content.Items.Count, structure) { Recipe = recipe, Owner = owner, Hp = MaxHp(type) });
        _machinesAlive++;
        StructureVersion++;
        return id;
    }

    /// <summary>Removes a machine, destroying everything inside it and detaching its belts.</summary>
    public void RemoveMachine(int id)
    {
        var m = GetMachine(id);
        Destroyed += m.HeldInputs + m.HeldOutputs;
        foreach (int s in m.InputsFrom) _segs[s]!.MachineOut = -1;
        for (int k = 0; k < m.OutCount; k++) _segs[m.OutBelts[k]]!.FedBy = -1;
        _machines[id] = null;
        _machinesAlive--;
        StructureVersion++;
    }

    /// <summary>Changes the recipe. Items inside the machine and the craft in progress are discarded (counted as destroyed).</summary>
    public void SetRecipe(int id, int recipe)
    {
        var m = GetMachine(id);
        CheckRecipe(m.Type, recipe);
        if (m.Recipe == recipe) return;

        Destroyed += m.HeldInputs + m.HeldOutputs;
        Array.Clear(m.In);
        Array.Clear(m.Out);
        m.Crafting = false;
        m.Remaining = 0;
        m.Recipe = recipe;
        StructureVersion++;
    }

    /// <summary>Makes the end of <paramref name="segment"/> feed <paramref name="machine"/>.</summary>
    public void ConnectToMachine(int segment, int machine)
    {
        var s = Get(segment);
        var m = GetMachine(machine);
        if (s.OutCount > 0 || s.IsSink || s.MachineOut >= 0)
            throw new InvalidOperationException("The segment already has an output.");
        s.MachineOut = machine;
        m.InputsFrom.Add(segment);
        StructureVersion++;
    }

    /// <summary>Makes <paramref name="machine"/> feed the start of <paramref name="segment"/>.</summary>
    public void ConnectFromMachine(int machine, int segment)
    {
        var m = GetMachine(machine);
        var s = Get(segment);
        if (m.OutCount >= 2) throw new InvalidOperationException("A machine feeds at most two belts.");
        if (s.FedBy >= 0 || s.Inputs.Count > 0 || s.SourcePattern is not null)
            throw new InvalidOperationException("The segment already has an input.");
        m.OutBelts[m.OutCount++] = segment;
        s.FedBy = machine;
        StructureVersion++;
    }

    /// <summary>Removes any link between the segment and the machine, in either direction.</summary>
    public void DisconnectMachine(int segment, int machine)
    {
        var s = Get(segment);
        var m = GetMachine(machine);
        bool any = false;
        if (s.MachineOut == machine)
        {
            s.MachineOut = -1;
            m.InputsFrom.Remove(segment);
            any = true;
        }
        if (s.FedBy == machine)
        {
            s.FedBy = -1;
            m.DropOutBelt(segment);
            any = true;
        }
        if (!any) throw new InvalidOperationException("The segment and the machine are not connected.");
        StructureVersion++;
    }

    public bool FeedsMachine(int segment, int machine) => Get(segment).MachineOut == machine;

    /// <summary>True if the end of the segment feeds some machine.</summary>
    public bool FeedsAnyMachine(int segment) => Get(segment).MachineOut >= 0;

    /// <summary>True if the start of the segment is fed by some machine.</summary>
    public bool IsFedByAnyMachine(int segment) => Get(segment).FedBy >= 0;

    public int InputCount(int segment) => Get(segment).Inputs.Count;

    public bool IsFedByMachine(int segment, int machine) => Get(segment).FedBy == machine;

    public int MachineOutputCount(int machine) => GetMachine(machine).OutCount;

    public int FeedingSegmentCount(int machine) => GetMachine(machine).InputsFrom.Count;

    public int MachineInCount(int machine, int item) => GetMachine(machine).In[item];

    public int MachineOutCount(int machine, int item) => GetMachine(machine).Out[item];

    public MachineInfo GetMachineInfo(int id)
    {
        var m = GetMachine(id);
        return new MachineInfo(m.Id, m.X, m.Y, m.Z, m.Type, m.Recipe, m.Crafting, m.Remaining, m.HeldInputs, m.HeldOutputs, m.Deposit,
            m.Hp, m.TurretCooldown, m.Structure);
    }

    private Machine GetMachine(int id)
    {
        if ((uint)id >= (uint)_machines.Count) throw new ArgumentOutOfRangeException(nameof(id));
        return _machines[id] ?? throw new ArgumentOutOfRangeException(nameof(id), "The machine was removed.");
    }

    private void CheckRecipe(int type, int recipe)
    {
        if (recipe == -1) return;
        var content = Content!;
        if (!content.IsRecipe(recipe)) throw new ArgumentOutOfRangeException(nameof(recipe));
        if (content.Recipes[recipe].Machine != type)
            throw new InvalidOperationException("The recipe belongs to a different machine type.");
    }

    // ---- simulation -------------------------------------------------------------------------

    private void StepMachines()
    {
        foreach (var m in _machines)
        {
            if (m is not null) StepMachine(m);
        }
    }

    private void StepMachine(Machine m)
    {
        if (m.Recipe >= 0)
        {
            var r = Content!.Recipes[m.Recipe];

            if (m.Crafting && --m.Remaining == 0)
            {
                foreach (var o in r.Outputs)
                {
                    m.Out[o.Item] += o.Count;
                    MachineOutputsCreated += o.Count;
                }
                m.Crafting = false;
            }

            if (!m.Crafting && CanStart(m, r) && TakeFromDepositIfDrill(m, r))
            {
                foreach (var i in r.Inputs)
                {
                    m.In[i.Item] -= i.Count;
                    MachineInputsConsumed += i.Count;
                }
                m.Crafting = true;
                m.Remaining = r.TimeTicks;
            }
        }

        PushOutputs(m);
    }

    /// <summary>A craft starts when every input is present and every output has room for it (two crafts' worth).</summary>
    private static bool CanStart(Machine m, RecipeDef r)
    {
        foreach (var i in r.Inputs)
            if (m.In[i.Item] < i.Count) return false;
        foreach (var o in r.Outputs)
            if (m.Out[o.Item] + o.Count > 2 * o.Count) return false;
        return true;
    }

    /// <summary>A drill needs a linked deposit that still holds enough of the recipe's output item; the ore is taken as the craft starts.</summary>
    private bool TakeFromDepositIfDrill(Machine m, RecipeDef r)
    {
        if (Content!.Machines[m.Type].Role != MachineRole.Drill) return true;
        if (m.Deposit < 0) return false;
        var d = _deposits[m.Deposit];
        var o = r.Outputs[0];
        if (d.Item != o.Item || d.Remaining < o.Count) return false;
        d.Remaining -= o.Count;
        DrilledTotal += o.Count;
        return true;
    }

    /// <summary>
    /// Puts at most one finished item on each belt the machine feeds. The belt that goes first rotates each time an item
    /// is pushed (not each tick: a craft that ends on the same tick parity every time would otherwise always favour
    /// the same belt), and a belt that cannot take an item is skipped.
    /// </summary>
    private void PushOutputs(Machine m)
    {
        if (m.OutCount == 0) return;
        int last = -1;
        for (int k = 0; k < m.OutCount; k++)
        {
            int index = (m.RoundRobin + k) % m.OutCount;
            var belt = _segs[m.OutBelts[index]]!;
            if (!belt.CanAccept) continue;

            int item = -1;
            for (int i = 0; i < m.Out.Length; i++)
            {
                if (m.Out[i] > 0) { item = i; break; }
            }
            if (item < 0) break;

            m.Out[item]--;
            belt.PushTail(belt.Count == 0 ? 0 : belt.TailPos - Units.ItemSpacing, (ushort)item);
            last = index;
        }
        if (last >= 0) m.RoundRobin = (last + 1) % m.OutCount;
    }

    /// <summary>Hands the front item of a belt to the machine it feeds, if the machine can use it right now.</summary>
    private bool TryFeedMachine(BeltSegment s)
    {
        var m = _machines[s.MachineOut]!;
        int type = s.Type[s.Head];
        var role = Content!.Machines[m.Type].Role;
        if (role == MachineRole.Lab) return TryFeedLab(s, type);
        if (role == MachineRole.Depot) return TryFeedDepot(m, s, type);
        if (role == MachineRole.Turret) return TryFeedTurret(m, s, type);
        if (role == MachineRole.Repairer) return TryFeedRepairer(m, s, type);
        if (m.Recipe < 0) return false;
        if (type >= m.In.Length) return false;

        foreach (var i in Content!.Recipes[m.Recipe].Inputs)
        {
            if (i.Item != type) continue;
            if (m.In[type] >= 2 * i.Count) return false;
            m.In[type]++;
            s.PopFront();
            s.Passed++;
            return true;
        }
        return false;
    }

    // ---- state, checksum, validation ----------------------------------------------------------

    private void CopyMachinesFrom(BeltNetwork other)
    {
        _machines = other._machines.Select(m => m?.Clone()).ToList();
        _machinesAlive = other._machinesAlive;
        MachineInputsConsumed = other.MachineInputsConsumed;
        MachineOutputsCreated = other.MachineOutputsCreated;
    }

    private void MixMachines(Action<long> mix)
    {
        mix(_machines.Count);
        foreach (var m in _machines)
        {
            if (m is null) { mix(-1); continue; }
            mix(m.Id); mix(m.X); mix(m.Y); mix(m.Z); mix(m.Type); mix(m.Recipe); mix(m.Deposit); mix(m.Owner); mix(m.Hp); mix(m.TurretCooldown);
            if (m.Structure != 0) mix(m.Structure); // mixed only when non-zero so worlds without structures hash as before
            mix(m.Crafting ? 1 : 0); mix(m.Remaining);
            foreach (int v in m.In) mix(v);
            foreach (int v in m.Out) mix(v);
            mix(m.InputsFrom.Count);
            foreach (int s in m.InputsFrom) mix(s);
            mix(m.OutCount); mix(m.RoundRobin);
            for (int k = 0; k < m.OutCount; k++) mix(m.OutBelts[k]);
        }
    }

    private void ValidateMachines()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);

        int alive = 0;
        for (int id = 0; id < _machines.Count; id++)
        {
            var m = _machines[id];
            if (m is null) continue;
            alive++;
            if (m.Id != id) Fail($"machine {id} has id {m.Id}");
            var content = Content ?? throw new InvalidOperationException("Machines exist without content.");
            if (!content.IsMachine(m.Type)) Fail($"machine {id} has unknown type {m.Type}");
            if (m.In.Length != content.Items.Count || m.Out.Length != content.Items.Count) Fail($"machine {id} buffers have the wrong size");
            var role = content.Machines[m.Type].Role;
            if (role == MachineRole.Ship) Fail($"machine {id} is a ship machine placed in the world");
            if (role != MachineRole.Drill && m.Deposit != -1) Fail($"machine {id} is not a drill but has a deposit");
            if (m.Deposit != -1 && !IsDeposit(m.Deposit)) Fail($"machine {id} has an unknown deposit");

            if (m.Hp < 1 || m.Hp > MaxHp(m.Type)) Fail($"machine {id} hit points {m.Hp} out of range");
            var turret = content.Machines[m.Type].Turret;
            var repairer = content.Machines[m.Type].Repairer;
            var unloader = content.Machines[m.Type].Unloader;
            if (turret is null && repairer is null && unloader is null && m.TurretCooldown != 0) Fail($"machine {id} is not a turret, repairer or unloader but has a cooldown");
            if (unloader is not null && (m.TurretCooldown < 0 || m.TurretCooldown > unloader.IntervalTicks)) Fail($"machine {id} unloader cooldown out of range");
            if (turret is not null && (m.TurretCooldown < 0 || m.TurretCooldown > turret.IntervalTicks)) Fail($"machine {id} turret cooldown out of range");
            if (repairer is not null && (m.TurretCooldown < 0 || m.TurretCooldown > repairer.IntervalTicks)) Fail($"machine {id} repairer cooldown out of range");

            if (m.Recipe == -1)
            {
                if (turret is not null)
                {
                    for (int i = 0; i < m.In.Length; i++)
                    {
                        int cap = i == turret.AmmoItem ? turret.AmmoCap : 0;
                        if (m.In[i] < 0 || m.In[i] > cap) Fail($"turret {id} holds {m.In[i]} of item {i} (cap {cap})");
                    }
                    if (m.HeldOutputs > 0 || m.Crafting) Fail($"turret {id} has outputs or is crafting");
                }
                else if (repairer is not null)
                {
                    for (int i = 0; i < m.In.Length; i++)
                    {
                        int cap = i == repairer.Item ? repairer.ItemCap : 0;
                        if (m.In[i] < 0 || m.In[i] > cap) Fail($"repairer {id} holds {m.In[i]} of item {i} (cap {cap})");
                    }
                    if (m.HeldOutputs > 0 || m.Crafting) Fail($"repairer {id} has outputs or is crafting");
                }
                else if (m.HeldInputs + m.HeldOutputs > 0 || m.Crafting) Fail($"machine {id} has no recipe but holds items or is crafting");
            }
            else
            {
                if (!content.IsRecipe(m.Recipe) || content.Recipes[m.Recipe].Machine != m.Type) Fail($"machine {id} has an invalid recipe");
                var r = content.Recipes[m.Recipe];
                for (int i = 0; i < m.In.Length; i++)
                {
                    int inCap = r.Inputs.Where(s => s.Item == i).Select(s => 2 * s.Count).FirstOrDefault();
                    int outCap = r.Outputs.Where(s => s.Item == i).Select(s => 2 * s.Count).FirstOrDefault();
                    if (m.In[i] < 0 || m.In[i] > inCap) Fail($"machine {id} input buffer {i} holds {m.In[i]} (cap {inCap})");
                    if (m.Out[i] < 0 || m.Out[i] > outCap) Fail($"machine {id} output buffer {i} holds {m.Out[i]} (cap {outCap})");
                }
                if (m.Crafting && (m.Remaining < 1 || m.Remaining > r.TimeTicks)) Fail($"machine {id} remaining time {m.Remaining} out of range");
            }
            if (!m.Crafting && m.Remaining != 0) Fail($"machine {id} is idle with remaining time {m.Remaining}");

            if (m.OutCount is < 0 or > 2) Fail($"machine {id} has {m.OutCount} output belts");
            if (m.OutCount > 0 && (m.RoundRobin < 0 || m.RoundRobin >= m.OutCount)) Fail($"machine {id} round robin out of range");
            for (int k = 0; k < m.OutCount; k++)
            {
                if (!IsAlive(m.OutBelts[k]) || _segs[m.OutBelts[k]]!.FedBy != id) Fail($"machine {id} output belt link is broken");
            }
            foreach (int s in m.InputsFrom)
            {
                if (!IsAlive(s) || _segs[s]!.MachineOut != id) Fail($"machine {id} input belt link is broken");
            }
            if (m.InputsFrom.Count != m.InputsFrom.Distinct().Count()) Fail($"machine {id} has duplicate input belts");
        }
        if (alive != _machinesAlive) Fail($"machine alive counter {_machinesAlive} but {alive} machines exist");
    }
}
