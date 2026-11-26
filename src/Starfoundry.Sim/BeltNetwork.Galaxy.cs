namespace Starfoundry.Sim;

/// <summary>Fixed rules of scanning, jumping and drilling range. Part of the simulation.</summary>
public static class ExplorationRules
{
    /// <summary>The scanner reveals hidden deposits within this many cells of the ship.</summary>
    public const int ScanRangeCells = 80;

    /// <summary>The scanner range in the wide galaxy (ADR 0028), where deposits lie farther from their anchor.</summary>
    public const int WideScanRangeCells = 300;

    /// <summary>Ticks before the scanner can fire again (5 seconds).</summary>
    public const int ScanCooldownTicks = 150;

    /// <summary>Ticks a jump takes to charge, during which the ship cannot move (3 seconds).</summary>
    public const int JumpChargeTicks = 90;

    /// <summary>A drill can only be linked to a deposit within this many cells.</summary>
    public const int DrillRangeCells = 16;
}

public sealed partial class BeltNetwork
{
    private bool[] _known = Array.Empty<bool>();
    private bool[] _visited = Array.Empty<bool>();

    /// <summary>Jumps the ships of this world completed (arrivals by the jump drive; flying into a system is not a jump).</summary>
    public int JumpsCompleted { get; private set; }

    /// <summary>The generated galaxy, or null in a world without one.</summary>
    public Galaxy? Galaxy { get; private set; }

    /// <summary>Installs a galaxy. Home (system 0) starts visited and its lane neighbours known.</summary>
    public void SetGalaxy(Galaxy galaxy)
    {
        if (Galaxy is not null) throw new InvalidOperationException("The network already has a galaxy.");
        if (Content is null) throw new InvalidOperationException("The network has no content.");
        Galaxy = galaxy;
        _known = new bool[galaxy.Systems.Count];
        _visited = new bool[galaxy.Systems.Count];
        MarkVisited(0);
        InitField();
        InitObstacles();
        InitThreat();
        StructureVersion++;
    }

    public int SystemCount => Galaxy?.Systems.Count ?? 0;
    public bool IsSystem(int id) => (uint)id < (uint)SystemCount;
    public bool IsSystemKnown(int id) => IsSystem(id) && _known[id];
    public bool IsSystemVisited(int id) => IsSystem(id) && _visited[id];

    /// <summary>The system the player's ship is in (near its anchor), or -1 in deep space.</summary>
    public int CurrentSystem(int player)
    {
        if (Galaxy is null) return -1;
        var p = _players.TryGetValue(player, out var found) ? found : null;
        return Galaxy.SystemAt(p?.X ?? 0, p?.Y ?? 0, p?.Z ?? 0);
    }

    public bool IsDepositRevealed(int id) => GetDeposit(id).Revealed;

    public int ScanCooldown(int player) => _players.TryGetValue(player, out var p) ? p.ScanCooldown : 0;
    public int JumpTarget(int player) => _players.TryGetValue(player, out var p) ? p.JumpTarget : -1;
    public int JumpRemaining(int player) => _players.TryGetValue(player, out var p) ? p.JumpRemaining : 0;

    /// <summary>The items one jump takes from the hold in campaign mode (empty without content). Sandbox jumps are free.</summary>
    public ItemStack[] JumpCost()
    {
        var cost = Content?.JumpCost;
        int discount = _bonus[(int)BonusKind.JumpDiscount];
        if (cost is null) return Array.Empty<ItemStack>();
        if (discount == 0) return cost;
        return cost.Select(s => new ItemStack(s.Item, Math.Max(1, s.Count - discount))).ToArray(); // long-range jump research
    }

    /// <summary>True when the player's hold can pay for a jump (always true outside campaign mode, where jumps are free).</summary>
    public bool CanAffordJump(int player) => Mode != GameMode.Campaign || Content is null || CanPay(player, JumpCost());

    /// <summary>Fires the scanner: reveals every hidden deposit within range of the ship and returns their ids.</summary>
    public IReadOnlyList<int> Scan(int player)
    {
        var p = GetOrCreatePlayer(player);
        if (p.ScanCooldown > 0) throw new InvalidOperationException("The scanner is recharging.");
        p.ScanCooldown = ExplorationRules.ScanCooldownTicks;

        long range = ScanRangeCells(player) * Units.CellUnits;
        var revealed = new List<int>();
        foreach (var d in _deposits)
        {
            if (d.Revealed || d.Free) continue;
            long dx = d.X * (long)Units.CellUnits - p.X, dy = d.Y * (long)Units.CellUnits - p.Y, dz = d.Z * (long)Units.CellUnits - p.Z;
            if (dx * dx + dy * dy + dz * dz > range * range) continue;
            d.Revealed = true;
            revealed.Add(d.Id);
        }
        if (revealed.Count > 0) StructureVersion++;
        return revealed;
    }

    /// <summary>Whether the ship can jump: always in sandbox, otherwise only after the research that unlocks it.</summary>
    public bool HasAbility(Ability ability) =>
        Mode == GameMode.Sandbox || Content is not { } c || !c.AbilityGated[(int)ability]
        || c.Research.Any(n => _level[n.Index] > 0 && n.UnlockAbilities.Contains(ability));

    /// <summary>
    /// Marks a research node and everything it requires finished, without paying for them. For setup and tests;
    /// gameplay goes through labs.
    /// </summary>
    public void GrantResearch(int node, int levels = 1)
    {
        if (Content is null || !Content.IsResearch(node)) throw new ArgumentOutOfRangeException(nameof(node));
        levels = Math.Clamp(levels, 1, Content.Research[node].MaxLevels);
        if (_level[node] >= levels) return;
        foreach (int required in Content.Research[node].Requires) GrantResearch(required);
        _level[node] = levels;
        if (_researchCurrent == node) _researchCurrent = -1;
        RecomputeBonuses(scaleHp: true);
        StructureVersion++;
    }

    /// <summary>Starts charging a jump to a connected system. The ship is frozen until it arrives.</summary>
    public void StartJump(int player, int target)
    {
        var galaxy = Galaxy ?? throw new InvalidOperationException("The network has no galaxy.");
        if (!IsSystem(target)) throw new ArgumentOutOfRangeException(nameof(target));
        var p = GetOrCreatePlayer(player);
        if (p.JumpRemaining > 0) throw new InvalidOperationException("A jump is already charging.");
        int current = galaxy.SystemAt(p.X, p.Y, p.Z);
        if (current < 0) throw new InvalidOperationException("The ship is not in a system.");
        if (current == target || !galaxy.HasLane(current, target)) throw new InvalidOperationException("There is no lane to that system.");
        if (!_known[target]) throw new InvalidOperationException("The system is not known yet.");
        p.Boost = false; // no boosting through a jump charge
        p.JumpTarget = target;
        p.JumpRemaining = ExplorationRules.JumpChargeTicks;
        p.ClearMotion(); // the ship is frozen while it charges: velocity zeroed (ADR 0033)
    }

    private void CompleteJump(Player p)
    {
        var s = Galaxy!.Systems[p.JumpTarget];
        SetShipPosition(p, s.X * Units.CellUnits, s.Y * Units.CellUnits, s.Z * Units.CellUnits);
        ClearMining(p);
        MarkVisited(p.JumpTarget);
        JumpsCompleted++;
        p.JumpTarget = -1;
        StructureVersion++;
    }

    /// <summary>Flying into a system counts as visiting it, just like jumping there.</summary>
    private void DiscoverAt(Player p)
    {
        if (Galaxy is null) return;
        int s = Galaxy.SystemAt(p.X, p.Y, p.Z);
        if (s < 0 || _visited[s]) return;
        MarkVisited(s);
        StructureVersion++;
    }

    private void MarkVisited(int system)
    {
        _visited[system] = true;
        _known[system] = true;
        foreach (int lane in Galaxy!.Systems[system].Lanes) _known[lane] = true;
    }

    private void CopyGalaxyFrom(BeltNetwork other)
    {
        Galaxy = other.Galaxy;
        _known = (bool[])other._known.Clone();
        _visited = (bool[])other._visited.Clone();
        JumpsCompleted = other.JumpsCompleted;
        CopyObstaclesFrom(other);
        CopyFieldFrom(other);
    }

    private void MixGalaxy(Action<long> mix)
    {
        mix((long)(Galaxy?.Seed ?? 0));
        foreach (bool b in _known) mix(b ? 1 : 0);
        foreach (bool b in _visited) mix(b ? 1 : 0);
        if (JumpsCompleted != 0) mix(JumpsCompleted); // worlds that never jumped hash as before
        MixObstacles(mix);
        MixField(mix);
    }

    private void ValidateGalaxy()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);

        if (Galaxy is null)
        {
            if (_known.Length != 0 || _visited.Length != 0) Fail("system flags exist without a galaxy");
        }
        else
        {
            int n = Galaxy.Systems.Count;
            if (_known.Length != n || _visited.Length != n) Fail("system flags have the wrong size");
            if (!_visited[0]) Fail("home is not visited");
            for (int i = 0; i < n; i++)
            {
                if (_visited[i] && !_known[i]) Fail($"system {i} is visited but not known");
                if (_known[i] && !_visited[i] && !Galaxy.Systems[i].Lanes.Any(l => _visited[l])) Fail($"system {i} is known without a visited neighbour");
            }
        }

        ValidateObstacles();
        ValidateField();
        foreach (var p in _players.Values)
        {
            if (p.ScanCooldown < 0 || p.ScanCooldown > ExplorationRules.ScanCooldownTicks) Fail($"player {p.Id} scan cooldown out of range");
            if ((p.JumpTarget >= 0) != (p.JumpRemaining > 0)) Fail($"player {p.Id} jump state is inconsistent");
            if (p.JumpTarget >= 0 && (!IsSystem(p.JumpTarget) || p.JumpRemaining > ExplorationRules.JumpChargeTicks)) Fail($"player {p.Id} jump out of range");
            if (p.MiningDeposit >= 0 && !_deposits[p.MiningDeposit].Revealed) Fail($"player {p.Id} mines a hidden deposit");
        }
        foreach (var m in _machines)
        {
            if (m is not null && m.Deposit >= 0 && !_deposits[m.Deposit].Revealed) Fail($"machine {m.Id} drills a hidden deposit");
        }
    }
}
