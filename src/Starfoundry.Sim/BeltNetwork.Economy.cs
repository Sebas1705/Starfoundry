namespace Starfoundry.Sim;

public enum GameMode : byte
{
    /// <summary>Free building and nothing locked: for tests, demos and experiments.</summary>
    Sandbox = 0,

    /// <summary>Building costs items from the builder's hold, research unlocks content, debug machines are off.</summary>
    Campaign = 1,
}

/// <summary>Fixed rules of the player's ship and hands. Part of the simulation, so changing them changes states.</summary>
public static class PlayerRules
{
    public const int HoldCapacity = 60;

    /// <summary>Hand mining: one item every 15 ticks (2 per second).</summary>
    public const int MineTicksPerItem = 15;

    /// <summary>The ship's micro-refinery works this many times slower than the recipe time.</summary>
    public const int ShipCraftMultiplier = 2;

    /// <summary>Ship speed along one axis: 48 units per tick, 6 cells per second.</summary>
    public const int MoveUnitsPerTick = 48;

    /// <summary>Speed per axis when moving along two axes (about 48 / sqrt 2) and three axes (about 48 / sqrt 3).</summary>
    public const int MoveUnitsPerTickDiagonal2 = 34;
    public const int MoveUnitsPerTickDiagonal3 = 28;

    /// <summary>Hand mining works within this many cells of the deposit centre.</summary>
    public const int MineRangeCells = 6;

    /// <summary>In campaign mode, placing and removing works within this many cells of the ship.</summary>
    public const int BuildRangeCells = 24;

    /// <summary>The ship cannot leave this cube (units, same as the building limit).</summary>
    public const int WorldLimitUnits = SimWorld.MaxCoord * Units.CellUnits;
}

/// <summary>Read-only view of a player, for rendering and tests. Positions are in units (<see cref="Units.CellUnits"/> per cell).</summary>
public readonly record struct PlayerInfo(
    int Id, int HoldTotal, int MiningDeposit, int ShipRecipe, bool ShipCrafting, int ShipRemaining,
    int X = 0, int Y = 0, int Z = 0, int MoveX = 0, int MoveY = 0, int MoveZ = 0,
    int ScanCooldown = 0, int JumpTarget = -1, int JumpRemaining = 0, int Hull = 0, bool Armed = false, int RespawnRemaining = 0);

internal sealed class Player
{
    public readonly int Id;
    public readonly int[] Hold;
    public int HoldTotal;
    public int MiningDeposit = -1;
    public int MineProgress;
    public int ShipRecipe = -1;
    public bool ShipCrafting;
    public int ShipRemaining;

    /// <summary>Output items of the craft in progress, counted against the hold capacity.</summary>
    public int ShipReserved;

    /// <summary>Position in units and the movement intent per axis (-1, 0 or 1).</summary>
    public int X, Y, Z;
    public int MoveX, MoveY, MoveZ;

    /// <summary>Ticks until the scanner can fire again.</summary>
    public int ScanCooldown;

    /// <summary>System being jumped to (or -1) and ticks left of the charge. The ship is frozen while charging.</summary>
    public int JumpTarget = -1;
    public int JumpRemaining;

    /// <summary>Hit points of the ship; zero while it is being rebuilt after a loss.</summary>
    public int Hull;

    /// <summary>Whether the ship's weapon is switched on, and its cooldown.</summary>
    public bool Armed;
    public int WeaponCooldown;

    /// <summary>Ticks until the ship is rebuilt at a system anchor (0 when it is fine).</summary>
    public int RespawnRemaining;

    public Player(int id, int items)
    {
        Id = id;
        Hold = new int[items];
    }

    public Player Clone()
    {
        var c = new Player(Id, Hold.Length)
        {
            HoldTotal = HoldTotal, MiningDeposit = MiningDeposit, MineProgress = MineProgress,
            ShipRecipe = ShipRecipe, ShipCrafting = ShipCrafting, ShipRemaining = ShipRemaining, ShipReserved = ShipReserved,
            X = X, Y = Y, Z = Z, MoveX = MoveX, MoveY = MoveY, MoveZ = MoveZ,
            ScanCooldown = ScanCooldown, JumpTarget = JumpTarget, JumpRemaining = JumpRemaining,
            Hull = Hull, Armed = Armed, WeaponCooldown = WeaponCooldown, RespawnRemaining = RespawnRemaining,
        };
        Array.Copy(Hold, c.Hold, Hold.Length);
        return c;
    }
}

internal sealed class Deposit
{
    public readonly int Id;
    public readonly int Item;
    public readonly int Initial;

    /// <summary>Centre of the deposit in cells.</summary>
    public readonly int X, Y, Z;
    public int Remaining;

    /// <summary>Hidden deposits cannot be mined or linked until a scan reveals them.</summary>
    public bool Revealed = true;

    public Deposit(int id, int item, int amount, int x, int y, int z)
    {
        Id = id; Item = item; Initial = amount; Remaining = amount; X = x; Y = y; Z = z;
    }

    public Deposit Clone() => new(Id, Item, Initial, X, Y, Z) { Remaining = Remaining, Revealed = Revealed };
}

public sealed partial class BeltNetwork
{
    private SortedDictionary<int, Player> _players = new();
    private List<Deposit> _deposits = new();
    private bool[] _researched = Array.Empty<bool>();
    private int[][] _progress = Array.Empty<int[]>();
    private int _researchCurrent = -1;

    /// <summary>Items added to player holds (mining, refining, refunds, grants).</summary>
    public long HoldIn { get; private set; }

    /// <summary>Items removed from player holds (refining inputs, build costs).</summary>
    public long HoldOut { get; private set; }

    /// <summary>Ore taken out of deposits by hand.</summary>
    public long MinedByHand { get; private set; }

    /// <summary>Ore taken out of deposits by drills.</summary>
    public long DrilledTotal { get; private set; }

    /// <summary>Items delivered to labs and used by research.</summary>
    public long ResearchItemsUsed { get; private set; }

    /// <summary>Items depots moved from belts into player holds.</summary>
    public long DepotDelivered { get; private set; }

    private void InitResearch()
    {
        int nodes = Content?.Research.Count ?? 0;
        int items = Content?.Items.Count ?? 0;
        _researched = new bool[nodes];
        _progress = new int[nodes][];
        for (int i = 0; i < nodes; i++) _progress[i] = new int[items];
    }

    // ---- deposits -----------------------------------------------------------------------------

    /// <summary>Adds a deposit centred on a cell (the origin by default, which is where players start).</summary>
    public int AddDeposit(int item, int amount, int x = 0, int y = 0, int z = 0, bool revealed = true)
    {
        var content = Content ?? throw new InvalidOperationException("The network has no content.");
        if ((uint)item >= (uint)content.Items.Count) throw new ArgumentOutOfRangeException(nameof(item));
        if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Math.Abs(x) > SimWorld.MaxCoord || Math.Abs(y) > SimWorld.MaxCoord || Math.Abs(z) > SimWorld.MaxCoord)
            throw new ArgumentOutOfRangeException(nameof(x), "The deposit is outside the world.");
        int id = _deposits.Count;
        _deposits.Add(new Deposit(id, item, amount, x, y, z) { Revealed = revealed });
        StructureVersion++;
        return id;
    }

    /// <summary>Centre of the deposit, in cells.</summary>
    public (int X, int Y, int Z) DepositPosition(int id)
    {
        var d = GetDeposit(id);
        return (d.X, d.Y, d.Z);
    }

    public int DepositCount => _deposits.Count;
    public bool IsDeposit(int id) => (uint)id < (uint)_deposits.Count;
    public int DepositItem(int id) => GetDeposit(id).Item;
    public int DepositRemaining(int id) => GetDeposit(id).Remaining;
    public int DepositInitial(int id) => GetDeposit(id).Initial;

    private Deposit GetDeposit(int id)
    {
        if ((uint)id >= (uint)_deposits.Count) throw new ArgumentOutOfRangeException(nameof(id));
        return _deposits[id];
    }

    /// <summary>Points a drill at a deposit. Drills only work while linked to a deposit holding their recipe's item.</summary>
    public void LinkDeposit(int machine, int deposit)
    {
        var m = GetMachine(machine);
        GetDeposit(deposit);
        if (Content!.Machines[m.Type].Role != MachineRole.Drill) throw new InvalidOperationException("Only drills can be linked to deposits.");
        m.Deposit = deposit;
    }

    public void UnlinkDeposit(int machine)
    {
        var m = GetMachine(machine);
        if (Content!.Machines[m.Type].Role != MachineRole.Drill) throw new InvalidOperationException("Only drills can be linked to deposits.");
        m.Deposit = -1;
    }

    // ---- players ------------------------------------------------------------------------------

    public bool HasPlayer(int id) => _players.ContainsKey(id);
    public IEnumerable<int> PlayerIds => _players.Keys;

    public PlayerInfo GetPlayerInfo(int id)
    {
        var p = GetPlayer(id);
        return new PlayerInfo(p.Id, p.HoldTotal, p.MiningDeposit, p.ShipRecipe, p.ShipCrafting, p.ShipRemaining,
            p.X, p.Y, p.Z, p.MoveX, p.MoveY, p.MoveZ, p.ScanCooldown, p.JumpTarget, p.JumpRemaining, p.Hull, p.Armed, p.RespawnRemaining);
    }

    /// <summary>Switches the ship's weapon on or off. It fires at the nearest enemy in range while on.</summary>
    public void SetWeapons(int player, bool armed) => GetOrCreatePlayer(player).Armed = armed;

    /// <summary>Makes sure the player exists, spawning at the origin with an empty hold. Needed before range checks.</summary>
    public void EnsurePlayer(int id) => GetOrCreatePlayer(id);

    /// <summary>
    /// Sets the ship's movement intent: -1, 0 or 1 per axis. The ship then moves every tick until the intent
    /// changes, at <see cref="PlayerRules.MoveUnitsPerTick"/> per axis (slower per axis when combining axes).
    /// </summary>
    public void SetMovement(int player, int dx, int dy, int dz)
    {
        if (dx is < -1 or > 1 || dy is < -1 or > 1 || dz is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(dx));
        var p = GetOrCreatePlayer(player);
        p.MoveX = dx; p.MoveY = dy; p.MoveZ = dz;
    }

    /// <summary>True if the centre of the cell is within <paramref name="rangeCells"/> of the player's ship.</summary>
    public bool CellInRange(int player, int cellX, int cellY, int cellZ, int rangeCells)
    {
        var p = _players.TryGetValue(player, out var found) ? found : null;
        long px = p?.X ?? 0, py = p?.Y ?? 0, pz = p?.Z ?? 0;
        long dx = cellX * (long)Units.CellUnits + Units.CellUnits / 2 - px;
        long dy = cellY * (long)Units.CellUnits + Units.CellUnits / 2 - py;
        long dz = cellZ * (long)Units.CellUnits + Units.CellUnits / 2 - pz;
        long r = (long)rangeCells * Units.CellUnits;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>True if the ship is close enough to hand-mine the deposit.</summary>
    public bool InMineRange(int player, int deposit)
    {
        var d = GetDeposit(deposit);
        var p = _players.TryGetValue(player, out var found) ? found : null;
        long px = p?.X ?? 0, py = p?.Y ?? 0, pz = p?.Z ?? 0;
        long dx = d.X * (long)Units.CellUnits - px, dy = d.Y * (long)Units.CellUnits - py, dz = d.Z * (long)Units.CellUnits - pz;
        long r = (long)PlayerRules.MineRangeCells * Units.CellUnits;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>Items of a kind in the player's hold (0 for an unknown player).</summary>
    public int HoldCount(int player, int item) =>
        _players.TryGetValue(player, out var p) && (uint)item < (uint)p.Hold.Length ? p.Hold[item] : 0;

    public int HoldTotal(int player) => _players.TryGetValue(player, out var p) ? p.HoldTotal : 0;

    public int MiningDeposit(int player) => _players.TryGetValue(player, out var p) ? p.MiningDeposit : -1;

    /// <summary>Adds items to a hold directly, ignoring capacity (setup and tests).</summary>
    public void GiveItems(int player, int item, int count)
    {
        var p = GetOrCreatePlayer(player);
        if ((uint)item >= (uint)p.Hold.Length) throw new ArgumentOutOfRangeException(nameof(item));
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        AddToHold(p, item, count);
    }

    /// <summary>True if the hold has every stack of <paramref name="cost"/> multiplied by <paramref name="times"/>.</summary>
    public bool CanPay(int player, ItemStack[] cost, int times = 1)
    {
        foreach (var s in cost)
            if (HoldCount(player, s.Item) < (long)s.Count * times) return false;
        return true;
    }

    public void Pay(int player, ItemStack[] cost, int times = 1)
    {
        if (!CanPay(player, cost, times)) throw new InvalidOperationException("The hold does not have the items.");
        var p = GetPlayer(player);
        foreach (var s in cost)
        {
            int n = s.Count * times;
            p.Hold[s.Item] -= n;
            p.HoldTotal -= n;
            HoldOut += n;
        }
    }

    /// <summary>Returns items to a hold, ignoring capacity.</summary>
    public void Refund(int player, ItemStack[] cost, int times = 1)
    {
        if (cost.Length == 0) return;
        var p = GetOrCreatePlayer(player);
        foreach (var s in cost) AddToHold(p, s.Item, s.Count * times);
    }

    public void StartMining(int player, int deposit)
    {
        var d = GetDeposit(deposit);
        if (d.Remaining <= 0) throw new InvalidOperationException("The deposit is empty.");
        var p = GetOrCreatePlayer(player);
        if (p.MiningDeposit != deposit) p.MineProgress = 0;
        p.MiningDeposit = deposit;
    }

    public void StopMining(int player)
    {
        var p = GetOrCreatePlayer(player);
        p.MiningDeposit = -1;
        p.MineProgress = 0;
    }

    /// <summary>Sets what the ship's micro-refinery works on (-1 for nothing). A craft in progress is undone and its inputs returned.</summary>
    public void SetShipRecipe(int player, int recipe)
    {
        var content = Content ?? throw new InvalidOperationException("The network has no content.");
        if (recipe != -1)
        {
            if (!content.IsRecipe(recipe)) throw new ArgumentOutOfRangeException(nameof(recipe));
            if (content.Machines[content.Recipes[recipe].Machine].Role != MachineRole.Ship)
                throw new InvalidOperationException("Only ship recipes can run on the micro-refinery.");
        }
        var p = GetOrCreatePlayer(player);
        if (p.ShipRecipe == recipe) return;
        AbortShipCraft(p);
        p.ShipRecipe = recipe;
    }

    private Player GetPlayer(int id) =>
        _players.TryGetValue(id, out var p) ? p : throw new ArgumentOutOfRangeException(nameof(id), "Unknown player.");

    private Player GetOrCreatePlayer(int id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (!_players.TryGetValue(id, out var p))
        {
            p = new Player(id, Content?.Items.Count ?? 0) { Hull = Content?.Threat?.ShipHull ?? 100 };
            _players.Add(id, p);
        }
        return p;
    }

    private void AddToHold(Player p, int item, int count)
    {
        p.Hold[item] += count;
        p.HoldTotal += count;
        HoldIn += count;
    }

    private void AbortShipCraft(Player p)
    {
        if (!p.ShipCrafting) return;
        foreach (var s in Content!.Recipes[p.ShipRecipe].Inputs) AddToHold(p, s.Item, s.Count);
        p.ShipCrafting = false;
        p.ShipRemaining = 0;
        p.ShipReserved = 0;
    }

    private void StepPlayers()
    {
        if (_players.Count == 0) return;
        foreach (var p in _players.Values)
        {
            if (p.ScanCooldown > 0) p.ScanCooldown--;
            if (p.JumpRemaining > 0)
            {
                if (--p.JumpRemaining == 0) CompleteJump(p);
            }
            else if (p.RespawnRemaining == 0 && (p.MoveX | p.MoveY | p.MoveZ) != 0)
            {
                MoveShip(p);
                DiscoverAt(p);
            }

            if (p.MiningDeposit >= 0)
            {
                var d = _deposits[p.MiningDeposit];
                if (d.Remaining <= 0)
                {
                    p.MiningDeposit = -1;
                    p.MineProgress = 0;
                }
                else if (p.HoldTotal + p.ShipReserved < PlayerRules.HoldCapacity && InMineRange(p.Id, p.MiningDeposit)
                    && ++p.MineProgress >= PlayerRules.MineTicksPerItem)
                {
                    p.MineProgress = 0;
                    d.Remaining--;
                    MinedByHand++;
                    AddToHold(p, d.Item, 1);
                }
            }
            if (p.ShipRecipe >= 0) StepShip(p);
        }
    }

    private static void MoveShip(Player p)
    {
        int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
        int speed = axes switch
        {
            1 => PlayerRules.MoveUnitsPerTick,
            2 => PlayerRules.MoveUnitsPerTickDiagonal2,
            _ => PlayerRules.MoveUnitsPerTickDiagonal3,
        };
        const int limit = PlayerRules.WorldLimitUnits;
        p.X = Math.Clamp(p.X + p.MoveX * speed, -limit, limit);
        p.Y = Math.Clamp(p.Y + p.MoveY * speed, -limit, limit);
        p.Z = Math.Clamp(p.Z + p.MoveZ * speed, -limit, limit);
    }

    private void StepShip(Player p)
    {
        var r = Content!.Recipes[p.ShipRecipe];

        if (p.ShipCrafting && --p.ShipRemaining == 0)
        {
            p.ShipReserved = 0;
            p.ShipCrafting = false;
            foreach (var o in r.Outputs) AddToHold(p, o.Item, o.Count);
        }

        if (p.ShipCrafting) return;

        int inTotal = 0, outTotal = 0;
        foreach (var i in r.Inputs)
        {
            if (p.Hold[i.Item] < i.Count) return;
            inTotal += i.Count;
        }
        foreach (var o in r.Outputs) outTotal += o.Count;
        if (p.HoldTotal - inTotal + outTotal > PlayerRules.HoldCapacity) return;

        foreach (var i in r.Inputs)
        {
            p.Hold[i.Item] -= i.Count;
            p.HoldTotal -= i.Count;
            HoldOut += i.Count;
        }
        p.ShipCrafting = true;
        p.ShipRemaining = r.TimeTicks * PlayerRules.ShipCraftMultiplier;
        p.ShipReserved = outTotal;
    }

    // ---- research -----------------------------------------------------------------------------

    public bool IsResearched(int node) => (uint)node < (uint)_researched.Length && _researched[node];
    public int CurrentResearch => _researchCurrent;
    public int ResearchedCount => _researched.Count(b => b);
    public int ResearchProgress(int node, int item) => _progress[node][item];

    /// <summary>True if the node exists, is not finished and all its requirements are.</summary>
    public bool CanResearch(int node)
    {
        if (Content is null || !Content.IsResearch(node) || _researched[node]) return false;
        return Content.Research[node].Requires.All(IsResearched);
    }

    /// <summary>Chooses the research labs work on (-1 for none). Progress is kept per node, so switching loses nothing.</summary>
    public void SetResearch(int node)
    {
        if (node == -1) { _researchCurrent = -1; return; }
        if (Content is null || !Content.IsResearch(node)) throw new ArgumentOutOfRangeException(nameof(node));
        if (!CanResearch(node)) throw new InvalidOperationException("The research is finished or its requirements are not.");
        _researchCurrent = node;
    }

    public bool IsMachineLocked(int type) =>
        Mode == GameMode.Campaign && Content is { } c && (c.Machines[type].SandboxOnly ||
            (c.MachineGated[type] && !c.Research.Any(n => _researched[n.Index] && n.UnlockMachines.Contains(type))));

    public bool IsRecipeLocked(int recipe) =>
        Mode == GameMode.Campaign && Content is { } c &&
            c.RecipeGated[recipe] && !c.Research.Any(n => _researched[n.Index] && n.UnlockRecipes.Contains(recipe));

    public bool IsBeltLocked(BeltTier tier) =>
        Mode == GameMode.Campaign && Content is { } c &&
            c.BeltGated[(int)tier] && !c.Research.Any(n => _researched[n.Index] && n.UnlockBelts.Contains(tier));

    /// <summary>A depot hands items to its owner's hold while there is room; when the hold is full the belt waits.</summary>
    private bool TryFeedDepot(Machine depot, BeltSegment s, int item)
    {
        if (depot.Owner < 0 || item >= Content!.Items.Count) return false;
        var p = GetOrCreatePlayer(depot.Owner);
        if (p.HoldTotal + p.ShipReserved >= PlayerRules.HoldCapacity) return false;

        AddToHold(p, item, 1);
        s.PopFront();
        s.Passed++;
        DepotDelivered++;
        MachineInputsConsumed++; // the item leaves the factory: counts like any machine input
        return true;
    }

    private bool TryFeedLab(BeltSegment s, int item)
    {
        if (_researchCurrent < 0 || item >= Content!.Items.Count) return false;
        var node = Content.Research[_researchCurrent];
        int need = 0;
        foreach (var c in node.Cost)
            if (c.Item == item) need = c.Count;
        if (need == 0 || _progress[_researchCurrent][item] >= need) return false;

        _progress[_researchCurrent][item]++;
        MachineInputsConsumed++;
        ResearchItemsUsed++;
        s.PopFront();
        s.Passed++;
        return true;
    }

    private void StepResearch()
    {
        if (_researchCurrent < 0) return;
        var node = Content!.Research[_researchCurrent];
        foreach (var c in node.Cost)
            if (_progress[_researchCurrent][c.Item] < c.Count) return;

        _researched[_researchCurrent] = true;
        _researchCurrent = -1;
        StructureVersion++;
    }

    // ---- state, checksum, validation ----------------------------------------------------------

    private void CopyEconomyFrom(BeltNetwork other)
    {
        _players = new SortedDictionary<int, Player>(other._players.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()));
        _deposits = other._deposits.Select(d => d.Clone()).ToList();
        CopyGalaxyFrom(other);
        CopyThreatFrom(other);
        _researched = (bool[])other._researched.Clone();
        _progress = other._progress.Select(a => (int[])a.Clone()).ToArray();
        _researchCurrent = other._researchCurrent;
        HoldIn = other.HoldIn;
        HoldOut = other.HoldOut;
        MinedByHand = other.MinedByHand;
        DrilledTotal = other.DrilledTotal;
        ResearchItemsUsed = other.ResearchItemsUsed;
        DepotDelivered = other.DepotDelivered;
    }

    private void MixEconomy(Action<long> mix)
    {
        mix(HoldIn); mix(HoldOut); mix(MinedByHand); mix(DrilledTotal); mix(ResearchItemsUsed); mix(DepotDelivered);
        MixGalaxy(mix);
        MixThreat(mix);
        mix(_deposits.Count);
        foreach (var d in _deposits) { mix(d.Item); mix(d.Initial); mix(d.Remaining); mix(d.X); mix(d.Y); mix(d.Z); mix(d.Revealed ? 1 : 0); }
        mix(_players.Count);
        foreach (var p in _players.Values)
        {
            mix(p.ScanCooldown); mix(p.JumpTarget); mix(p.JumpRemaining);
            mix(p.Hull); mix(p.Armed ? 1 : 0); mix(p.WeaponCooldown); mix(p.RespawnRemaining);
            mix(p.X); mix(p.Y); mix(p.Z); mix(p.MoveX); mix(p.MoveY); mix(p.MoveZ);
            mix(p.Id); mix(p.HoldTotal); mix(p.MiningDeposit); mix(p.MineProgress);
            mix(p.ShipRecipe); mix(p.ShipCrafting ? 1 : 0); mix(p.ShipRemaining); mix(p.ShipReserved);
            foreach (int h in p.Hold) mix(h);
        }
        mix(_researchCurrent);
        for (int i = 0; i < _researched.Length; i++)
        {
            mix(_researched[i] ? 1 : 0);
            foreach (int v in _progress[i]) mix(v);
        }
    }

    private void ValidateEconomy()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);

        long held = 0;
        foreach (var p in _players.Values)
        {
            int sum = 0;
            foreach (int h in p.Hold)
            {
                if (h < 0) Fail($"player {p.Id} has a negative item count");
                sum += h;
            }
            if (sum != p.HoldTotal) Fail($"player {p.Id} hold total {p.HoldTotal} but items sum to {sum}");
            held += sum;

            if (Math.Abs(p.X) > PlayerRules.WorldLimitUnits || Math.Abs(p.Y) > PlayerRules.WorldLimitUnits || Math.Abs(p.Z) > PlayerRules.WorldLimitUnits)
                Fail($"player {p.Id} is outside the world");
            if (p.MoveX is < -1 or > 1 || p.MoveY is < -1 or > 1 || p.MoveZ is < -1 or > 1) Fail($"player {p.Id} has an invalid movement intent");

            if (p.MiningDeposit != -1 && !IsDeposit(p.MiningDeposit)) Fail($"player {p.Id} mines an unknown deposit");
            if (p.MineProgress < 0 || p.MineProgress >= PlayerRules.MineTicksPerItem) Fail($"player {p.Id} mine progress out of range");
            if (p.ShipRecipe == -1)
            {
                if (p.ShipCrafting || p.ShipReserved != 0) Fail($"player {p.Id} has no ship recipe but is crafting");
            }
            else
            {
                var content = Content ?? throw new InvalidOperationException("Players have recipes without content.");
                if (!content.IsRecipe(p.ShipRecipe) || content.Machines[content.Recipes[p.ShipRecipe].Machine].Role != MachineRole.Ship)
                    Fail($"player {p.Id} has an invalid ship recipe");
                if (p.ShipCrafting && (p.ShipRemaining < 1 || p.ShipRemaining > content.Recipes[p.ShipRecipe].TimeTicks * PlayerRules.ShipCraftMultiplier))
                    Fail($"player {p.Id} ship craft time out of range");
                if (!p.ShipCrafting && (p.ShipRemaining != 0 || p.ShipReserved != 0)) Fail($"player {p.Id} is idle with a craft pending");
            }
        }
        if (HoldIn - HoldOut != held) Fail($"hold accounting broken: in {HoldIn} - out {HoldOut} != held {held}");
        ValidateGalaxy();
        ValidateThreat();

        long extracted = 0;
        foreach (var d in _deposits)
        {
            if (d.Remaining < 0 || d.Remaining > d.Initial) Fail($"deposit {d.Id} remaining {d.Remaining} out of range");
            extracted += d.Initial - d.Remaining;
        }
        if (extracted != MinedByHand + DrilledTotal) Fail($"deposit accounting broken: extracted {extracted} != hand {MinedByHand} + drills {DrilledTotal}");

        for (int i = 0; i < _researched.Length; i++)
        {
            var node = Content!.Research[i];
            if (_researched[i] && !node.Requires.All(IsResearched)) Fail($"research {node.Id} is done without its requirements");
            for (int item = 0; item < _progress[i].Length; item++)
            {
                int cap = node.Cost.Where(c => c.Item == item).Select(c => c.Count).FirstOrDefault();
                if (_progress[i][item] < 0 || _progress[i][item] > cap) Fail($"research {node.Id} progress for item {item} out of range");
            }
        }
        if (_researchCurrent != -1 && !CanResearch(_researchCurrent)) Fail("the current research cannot be researched");
    }
}
