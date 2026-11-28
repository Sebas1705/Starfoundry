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

    /// <summary>
    /// A depot stops delivering an item once the hold has this many of it (40 percent of the capacity). Without it, one
    /// automated line fills the hold with a single item and blocks every other delivery, trapping the player.
    /// </summary>
    public const int DepotItemCap = HoldCapacity * 2 / 5;

    /// <summary>Hand mining: one item every 15 ticks (2 per second).</summary>
    public const int MineTicksPerItem = 15;

    /// <summary>
    /// Power the mining laser draws from the ship's reactors while it fires (ADR 0027), in the units of a machine's <c>power_use</c>.
    /// Drawn only by ships with a reactor module, under the structure rules and not with <see cref="WorldRules.LegacyLaser"/>.
    /// </summary>
    public const int LaserPowerUse = 5;

    /// <summary>
    /// Structure worlds only: hand mining stops while the hold is at or above this percentage of its capacity, so the rest is
    /// always free for what the factory delivers. Without it the hand-mined ore fills the hold and the depot can never deliver.
    /// </summary>
    public const int MiningHoldLimitPercent = 75;

    /// <summary>
    /// Structure worlds only: a depot stops delivering once the hold (items plus reserved slots) is at or above this percentage of
    /// its capacity, so a few slots always stay free for hand-mined and port-bound items.
    /// </summary>
    public const int DepotHoldLimitPercent = 90;

    /// <summary>The ship's micro-refinery works this many times slower than the recipe time.</summary>
    public const int ShipCraftMultiplier = 2;

    /// <summary>ADR 0039: the workbench of a world without <see cref="WorldRules.LegacyShipAutomation"/> takes this many times the recipe time (8 s for a 1 s recipe) and does one craft at a time.</summary>
    public const int BenchCraftMultiplier = 8;

    /// <summary>ADR 0039: crafts that may wait in the workbench queue.</summary>
    public const int BenchQueueMax = 5;

    /// <summary>Ship speed along one axis: 48 units per tick, 6 cells per second.</summary>
    public const int MoveUnitsPerTick = 48;

    /// <summary>Speed per axis when moving along two axes (about 48 / sqrt 2) and three axes (about 48 / sqrt 3).</summary>
    public const int MoveUnitsPerTickDiagonal2 = 34;
    public const int MoveUnitsPerTickDiagonal3 = 28;

    /// <summary>Largest thrust component, and the scale of a thrust vector: a unit vector is sent times this.</summary>
    public const int ThrustScale = 1024;

    /// <summary>True if every component is in [-ThrustScale, ThrustScale] and the squared length is at most ThrustScale squared.</summary>
    public static bool ThrustInRange(int dx, int dy, int dz)
    {
        if (dx is < -ThrustScale or > ThrustScale || dy is < -ThrustScale or > ThrustScale || dz is < -ThrustScale or > ThrustScale) return false;
        long sq = (long)dx * dx + (long)dy * dy + (long)dz * dz;
        return sq <= (long)ThrustScale * ThrustScale;
    }

    /// <summary>Hand mining works within this many cells of the deposit centre.</summary>
    public const int MineRangeCells = 6;

    /// <summary>In campaign mode, placing and removing works within this many cells of the ship.</summary>
    public const int BuildRangeCells = 24;

    /// <summary>The ship cannot leave this cube (units, same as the building limit).</summary>
    public const int WorldLimitUnits = SimWorld.MaxCoord * Units.CellUnits;
}

/// <summary>Read-only view of a deposit, for rendering and panels. Position in cells, density in percent.</summary>
public readonly record struct DepositInfo(
    int Id, int Item, int Initial, int Remaining, int X, int Y, int Z, bool Revealed, int Density);

/// <summary>Read-only view of a player, for rendering and tests. Positions are in units (<see cref="Units.CellUnits"/> per cell).</summary>
public readonly record struct PlayerInfo(
    int Id, int HoldTotal, int MiningDeposit, int ShipRecipe, bool ShipCrafting, int ShipRemaining,
    int X = 0, int Y = 0, int Z = 0, int MoveX = 0, int MoveY = 0, int MoveZ = 0,
    int ScanCooldown = 0, int JumpTarget = -1, int JumpRemaining = 0, int Hull = 0, bool Armed = false, int RespawnRemaining = 0,
    int ThrustX = 0, int ThrustY = 0, int ThrustZ = 0, int VelX = 0, int VelY = 0, int VelZ = 0, IReadOnlyList<int>? Queue = null)
{
    /// <summary>The recipes waiting at the workbench, in order (ADR 0039); always empty in a world with the legacy ship rules.</summary>
    public IReadOnlyList<int> BenchQueue => Queue ?? Array.Empty<int>();
}

internal sealed partial class Player
{
    public readonly int Id;
    public readonly int[] Hold;
    public int HoldTotal;
    public int MiningDeposit = -1;
    public int MineProgress;

    /// <summary>The trigger of the ship's mining laser is released (see <see cref="FireLaser"/>): the order stays but nothing is mined and no power is drawn. Mixed only when set.</summary>
    public bool LaserReleased;

    /// <summary>Power credit of the mining laser in per mille (below 1000): at less than full power the laser advances only on credited ticks. Mixed only when non-zero.</summary>
    public int LaserCredit;
    public int ShipRecipe = -1;
    public bool ShipCrafting;
    public int ShipRemaining;

    /// <summary>ADR 0039: crafts waiting at the workbench (recipe indices), never more than <see cref="PlayerRules.BenchQueueMax"/>. Mixed only when not empty.</summary>
    public readonly List<int> BenchQueue = new();

    /// <summary>Output items of the craft in progress, counted against the hold capacity.</summary>
    public int ShipReserved;

    /// <summary>Position in units and the movement intent per axis (-1, 0 or 1).</summary>
    public int X, Y, Z;
    public int MoveX, MoveY, MoveZ;

    /// <summary>Thrust vector (scaled by <see cref="PlayerRules.ThrustScale"/>) and the signed sub-unit remainder per axis,
    /// in 1/ThrustScale of a unit, so slow or diagonal flight loses nothing. Never set together with the axis intent.</summary>
    public int ThrustX, ThrustY, ThrustZ;
    public int ThrustRemX, ThrustRemY, ThrustRemZ;

    /// <summary>The ship structure this player flies (-1 when none). With structure rules on, its origin is the source of truth for X/Y/Z.</summary>
    public int ShipStructure = -1;

    /// <summary>Structure worlds: the ship lost its last cockpit, so this player can no longer fly. Never set in legacy worlds.</summary>
    public bool ShipLost;

    /// <summary>Boost is on (ADR 0028): the engines run at the content's boost percent. Mixed only when set.</summary>
    public bool Boost;

    /// <summary>The cruise drive is engaged (ADR 0053, stage 2 slice). Mixed only when set.</summary>
    public bool Cruise;

    /// <summary>How far the ship moved during the last tick, in units per axis. Derived each tick before it is used; never saved.</summary>
    public int StepDx, StepDy, StepDz;

    /// <summary>
    /// The ship's velocity in thousandths of a unit per tick, per axis (ADR 0033). Zero in worlds without flight inertia: there the position changes by
    /// the thrust directly. The sub-milli remainder of the position advance is kept in <see cref="VelRemX"/> and the others. Mixed only when non-zero.
    /// </summary>
    public int VelX, VelY, VelZ;
    public int VelRemX, VelRemY, VelRemZ;

    public bool HasVelocity => (VelX | VelY | VelZ) != 0;

    public bool HasThrust => (ThrustX | ThrustY | ThrustZ) != 0;

    public void ClearVelocity()
    {
        VelX = VelY = VelZ = 0;
        VelRemX = VelRemY = VelRemZ = 0;
    }

    /// <summary>Stops the ship dead: intent, thrust and velocity (a jump, a respawn, a loss or a docking).</summary>
    public void ClearMotion()
    {
        MoveX = MoveY = MoveZ = 0;
        Cruise = false; // the drive goes with the motion: a docking, a jump, a lost ship, a hit that stops the ship
        ClearThrust();
        ClearVelocity();
    }

    public void ClearThrust()
    {
        ThrustX = ThrustY = ThrustZ = 0;
        ThrustRemX = ThrustRemY = ThrustRemZ = 0;
    }

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
            HoldTotal = HoldTotal, MiningDeposit = MiningDeposit, MineProgress = MineProgress, LaserReleased = LaserReleased, LaserCredit = LaserCredit, Boost = Boost, Cruise = Cruise,
            ShipRecipe = ShipRecipe, ShipCrafting = ShipCrafting, ShipRemaining = ShipRemaining, ShipReserved = ShipReserved,
            X = X, Y = Y, Z = Z, MoveX = MoveX, MoveY = MoveY, MoveZ = MoveZ,
            ThrustX = ThrustX, ThrustY = ThrustY, ThrustZ = ThrustZ, ThrustRemX = ThrustRemX, ThrustRemY = ThrustRemY, ThrustRemZ = ThrustRemZ,
            VelX = VelX, VelY = VelY, VelZ = VelZ, VelRemX = VelRemX, VelRemY = VelRemY, VelRemZ = VelRemZ,
            ScanCooldown = ScanCooldown, JumpTarget = JumpTarget, JumpRemaining = JumpRemaining,
            Hull = Hull, Armed = Armed, WeaponCooldown = WeaponCooldown, RespawnRemaining = RespawnRemaining,
            ShipStructure = ShipStructure, ShipLost = ShipLost,
        };
        Array.Copy(Hold, c.Hold, Hold.Length);
        c.BenchQueue.AddRange(BenchQueue);
        CloneInventoryInto(c);
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

    /// <summary>Richness in percent of the base rate (25 to 200); mining and drilling time scale by 100 / density.</summary>
    public int Density = DepositRules.DefaultDensity;

    /// <summary>Hidden deposits cannot be mined or linked until a scan reveals them.</summary>
    public bool Revealed = true;

    /// <summary>Ticks until an emptied deposit regrows a little (0 while it holds ore or has not been noticed empty yet, ADR 0036). Mixed only when non-zero.</summary>
    public int RegrowTicks;

    /// <summary>Radius of the rock in units for a rock of the infinite field (ADR 0040), 0 for every other deposit (the radius then follows <see cref="Initial"/>).</summary>
    public int Radius;

    /// <summary>True for a rock the infinite field materialized (ADR 0040): it never regrows, and when it is emptied it is gone for good.</summary>
    public bool Streamed;

    /// <summary>Streamed rocks: the packed sector key and the index of the rock in its sector (the identity that is stable across unloading).</summary>
    public long CellKey;
    public int CellIndex;

    /// <summary>Streamed rocks of the clustered field (ADR 0046): the id of the cluster the rock belongs to, -1 for a stray and for the uniform field. Derived from the generator, so it is not hashed.</summary>
    public long Cluster = -1;

    /// <summary>Streamed rocks: which materialization this slot holds; the client rebuilds the rock when it changes (a slot is reused after a sector is forgotten).</summary>
    public int Serial;

    /// <summary>A slot of a forgotten rock waiting to be reused: no ore, hidden, never touched by any loop.</summary>
    public bool Free;

    /// <summary>The radius of the rock in units: its own for a field rock, else the nominal radius of a deposit of this size.</summary>
    public int RadiusUnits => Radius > 0 ? Radius : DepositRules.NominalRadiusUnits(Initial);

    public Deposit(int id, int item, int amount, int x, int y, int z)
    {
        Id = id; Item = item; Initial = amount; Remaining = amount; X = x; Y = y; Z = z;
    }

    public static Deposit FreeSlot(int id) => new(id, 0, 0, 0, 0, 0) { Revealed = false, Free = true };

    public Deposit Clone() => new(Id, Item, Initial, X, Y, Z)
    {
        Remaining = Remaining, Revealed = Revealed, Density = Density, RegrowTicks = RegrowTicks,
        Radius = Radius, Streamed = Streamed, CellKey = CellKey, CellIndex = CellIndex, Serial = Serial, Free = Free, Cluster = Cluster,
    };
}

public sealed partial class BeltNetwork
{
    private SortedDictionary<int, Player> _players = new();
    private List<Deposit> _deposits = new();
    private int[] _level = Array.Empty<int>(); // research levels: 0 not done, 1 done, more for repeatable nodes

    private readonly int[] _bonus = new int[Enum.GetValues<BonusKind>().Length];

    /// <summary>Total research bonus of a kind, as a percentage.</summary>
    public int BonusPercent(BonusKind kind) => _bonus[(int)kind];

    /// <summary>Maximum hit points of a machine type, with the research bonus.</summary>
    public int MaxHp(int machineType) => Content!.Machines[machineType].Hp * (100 + _bonus[(int)BonusKind.MachineHp]) / 100;

    /// <summary>Damage of one turret shot, with the research bonus.</summary>
    public int TurretDamage(TurretDef turret) => Math.Max(1, turret.Damage * (100 + _bonus[(int)BonusKind.TurretDamage]) / 100);

    /// <summary>Ticks between turret shots, with the research bonus (never fewer than one).</summary>
    public int TurretInterval(TurretDef turret) => Math.Max(1, turret.IntervalTicks * (100 - _bonus[(int)BonusKind.TurretRate]) / 100);

    /// <summary>The ship's maximum hull points, with the research bonus (100 without a threat section).</summary>
    public int ShipMaxHull => (Content?.Threat?.ShipHull ?? 100) * (100 + _bonus[(int)BonusKind.ShipHull]) / 100;

    /// <summary>Damage of one ship weapon shot, with the research bonus.</summary>
    public int ShipWeaponDamage => Math.Max(1, (Content?.Threat?.ShipWeaponDamage ?? 1) * StructureShipDamagePercent / 100 * (100 + _bonus[(int)BonusKind.ShipDamage]) / 100);

    /// <summary>Percent of the base ship cannon damage in structure worlds (100 elsewhere and when the content has no such key).</summary>
    private int StructureShipDamagePercent => StructureRules ? Content?.Threat?.StructureShipDamagePercent ?? 100 : 100;

    /// <summary>Hit points one repairer use restores, with the research bonus.</summary>
    public int RepairHeal(RepairerDef repairer) => Math.Max(1, repairer.Heal * (100 + _bonus[(int)BonusKind.RepairHeal]) / 100);

    /// <summary>
    /// Adds up the bonuses of finished research. When the machine hit point bonus changes, machines already standing keep
    /// their share of the new maximum, so finishing the research does not hurt or heal them by accident.
    /// </summary>
    private void RecomputeBonuses(bool scaleHp)
    {
        if (Content is null) return;
        int[] old = (int[])_bonus.Clone();
        System.Array.Clear(_bonus);
        foreach (var node in Content.Research)
            if (_level[node.Index] > 0)
                foreach (var b in node.Bonuses) _bonus[(int)b.Kind] += b.Percent * _level[node.Index];
        if (scaleHp && old[(int)BonusKind.ShipHull] != _bonus[(int)BonusKind.ShipHull])
        {
            long baseHull = Content.Threat?.ShipHull ?? 100, oldMax = baseHull * (100L + old[(int)BonusKind.ShipHull]) / 100, newMax = ShipMaxHull;
            foreach (var p in _players.Values)
                if (p.Hull > 0) p.Hull = (int)Math.Clamp(p.Hull * newMax / Math.Max(1, oldMax), 1, newMax);
        }
        if (!scaleHp || old[(int)BonusKind.MachineHp] == _bonus[(int)BonusKind.MachineHp]) return;
        foreach (var m in _machines)
        {
            if (m is null) continue;
            long oldMax = Content.Machines[m.Type].Hp * (100L + old[(int)BonusKind.MachineHp]) / 100, newMax = MaxHp(m.Type);
            m.Hp = (int)Math.Clamp(m.Hp * newMax / Math.Max(1, oldMax), 1, newMax);
        }
    }
    private int[][] _progress = Array.Empty<int[]>();
    private int _researchCurrent = -1;

    /// <summary>Items added to player holds (mining, refining, refunds, grants).</summary>
    public long HoldIn { get; private set; }

    /// <summary>Items removed from player holds (refining inputs, build costs).</summary>
    public long HoldOut { get; private set; }

    /// <summary>Ore taken out of deposits by hand.</summary>
    public long MinedByHand { get; private set; }

    /// <summary>Items drone bays have taken from deposits.</summary>
    public long MinedByDrones { get; private set; }

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
        _level = new int[nodes];
        _progress = new int[nodes][];
        for (int i = 0; i < nodes; i++) _progress[i] = new int[items];
    }

    // ---- deposits -----------------------------------------------------------------------------

    /// <summary>Adds a deposit centred on a cell (the origin by default, which is where players start).</summary>
    public int AddDeposit(int item, int amount, int x = 0, int y = 0, int z = 0, bool revealed = true, int density = DepositRules.DefaultDensity)
    {
        var content = Content ?? throw new InvalidOperationException("The network has no content.");
        if ((uint)item >= (uint)content.Items.Count) throw new ArgumentOutOfRangeException(nameof(item));
        if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount));
        if (density < DepositRules.MinDensity || density > DepositRules.MaxDensity) throw new ArgumentOutOfRangeException(nameof(density));
        int maxCoord = Math.Max(SimWorld.MaxCoord, content.World?.Field?.WorldLimitCells ?? 0); // a wide world places its systems far beyond the finite cube (ADR 0053)
        if (Math.Abs(x) > maxCoord || Math.Abs(y) > maxCoord || Math.Abs(z) > maxCoord)
            throw new ArgumentOutOfRangeException(nameof(x), "The deposit is outside the world.");
        int id = _deposits.Count;
        _deposits.Add(new Deposit(id, item, amount, x, y, z) { Revealed = revealed, Density = density });
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

    /// <summary>Richness of the deposit in percent (25 to 200, 100 is the base rate).</summary>
    public int DepositDensity(int id) => GetDeposit(id).Density;

    /// <summary>Ticks of hand mining per item at the deposit: <c>MineTicksPerItem * 100 / density</c> shortened by the laser research
    /// (<see cref="BonusKind.MiningRate"/>), at least 1. With no bonus it is exactly the pre-laser-research value.</summary>
    public int MineTicksFor(int deposit) => MineTicks(GetDeposit(deposit).Density);

    /// <summary>Ticks per item for the strength of the lasers that reach the deposit: <paramref name="laserPercent"/> is 100 for one mark 1 laser (and the implicit laser),
    /// so the result is then exactly <c>MineTicksPerItem * 10000 / (density * (100 + bonus))</c>; two such lasers halve it (ADR 0032).</summary>
    private int MineTicks(int density, int laserPercent = 100) =>
        (int)Math.Max(1L, (long)PlayerRules.MineTicksPerItem * 1_000_000 / ((long)density * (100 + _bonus[(int)BonusKind.MiningRate]) * Math.Max(1, laserPercent)));

    /// <summary>True while the player's mining laser is firing: a mining order is on and the trigger is not released.</summary>
    public bool LaserFiring(int player) => _players.TryGetValue(player, out var p) && p.MiningDeposit >= 0 && !p.LaserReleased;

    /// <summary>
    /// Releases (false) or pulls (true) the trigger of the player's mining laser without changing the mining order. A released
    /// laser mines nothing and draws no power; the partial item it has mined is kept. Needs a mining order.
    /// </summary>
    public void FireLaser(int player, bool firing)
    {
        var p = GetOrCreatePlayer(player);
        if (p.MiningDeposit < 0) throw new InvalidOperationException("The player has no mining order.");
        p.LaserReleased = !firing;
    }

    /// <summary>Clears the mining order and everything the laser carries with it.</summary>
    private static void ClearMining(Player p)
    {
        p.MiningDeposit = -1;
        p.MineProgress = 0;
        p.LaserReleased = false;
        p.LaserCredit = 0;
    }

    /// <summary>
    /// True if the laser would mine this tick: it fires at a deposit that still holds ore, in range, with room in the hold.
    /// This is what draws power and what advances the progress.
    /// </summary>
    private bool LaserWouldMine(Player p) =>
        p.MiningDeposit >= 0 && !p.LaserReleased && _deposits[p.MiningDeposit].Remaining > 0
        && HandMiningHasRoom(p) && InMineRange(p.Id, p.MiningDeposit);

    /// <summary>Whether the laser draws power in this world: structure rules, not the legacy laser.</summary>
    private bool LaserDrawsPower => StructureRules && !Rules.LegacyLaser;

    /// <summary>
    /// Whether the laser makes progress this tick given the power of the ship it is mounted on: always when the reactors cover what
    /// the lasers draw (and with no power model), else on the ticks its credit reaches 1000, the way machines work. The laser is
    /// served before the machines (see <c>StepPower</c>), so only a ship whose production is below the laser's draw slows it.
    /// Call only when <see cref="LaserWouldMine"/>.
    /// </summary>
    private bool LaserAdvances(Player p)
    {
        if (!LaserDrawsPower || !_structures.TryGetValue(p.ShipStructure, out var ship) || ship.LaserDraw == 0) return true;
        int satisfaction = ship.PowerProduction >= ship.LaserDraw
            ? PowerRules.FullSatisfaction
            : (int)((long)ship.PowerProduction * PowerRules.FullSatisfaction / ship.LaserDraw);
        if (satisfaction >= PowerRules.FullSatisfaction) return true;
        p.LaserCredit += satisfaction;
        if (p.LaserCredit < PowerRules.FullSatisfaction) return false;
        p.LaserCredit -= PowerRules.FullSatisfaction;
        return true;
    }

    /// <summary>Everything a renderer or panel needs to know about a deposit.</summary>
    public DepositInfo GetDepositInfo(int id)
    {
        var d = GetDeposit(id);
        return new DepositInfo(d.Id, d.Item, d.Initial, d.Remaining, d.X, d.Y, d.Z, d.Revealed, d.Density);
    }

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
            p.X, p.Y, p.Z, p.MoveX, p.MoveY, p.MoveZ, p.ScanCooldown, p.JumpTarget, p.JumpRemaining, p.Hull, p.Armed, p.RespawnRemaining, p.ThrustX, p.ThrustY, p.ThrustZ, p.VelX, p.VelY, p.VelZ,
            p.BenchQueue.Count == 0 ? null : p.BenchQueue.ToArray());
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
        p.ClearThrust();
    }

    /// <summary>
    /// Sets the ship's thrust: an integer direction vector, each component in [-1024, 1024] with a squared length of at
    /// most 1024 squared. The ship moves along it every tick at <c>thrust * MoveUnitsPerTick / 1024</c> per axis, with
    /// sub-unit remainders carried. Replaces the axis intent; (0, 0, 0) stops the ship.
    /// </summary>
    public void SetThrust(int player, int dx, int dy, int dz)
    {
        if (!PlayerRules.ThrustInRange(dx, dy, dz)) throw new ArgumentOutOfRangeException(nameof(dx));
        var p = GetOrCreatePlayer(player);
        p.MoveX = p.MoveY = p.MoveZ = 0;
        p.ClearThrust();
        p.ThrustX = dx; p.ThrustY = dy; p.ThrustZ = dz;
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
        if (p is not null && HardpointsRequired(p)) return LasersInReach(p, deposit).Count > 0; // ADR 0032: a laser module must reach it
        long px = p?.X ?? 0, py = p?.Y ?? 0, pz = p?.Z ?? 0;
        long dx = d.X * (long)Units.CellUnits - px, dy = d.Y * (long)Units.CellUnits - py, dz = d.Z * (long)Units.CellUnits - pz;
        long r = (long)PlayerRules.MineRangeCells * Units.CellUnits + MineReachExtraUnits(d);
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

    /// <summary>Adds the world rules' starting stacks to a hold, in order, as far as the hold has room; the rest is counted in
    /// <see cref="StartingItemsClamped"/>. Called once while the world is created.</summary>
    internal void GiveStartingItems(int player, IReadOnlyList<ItemStack> stacks)
    {
        var p = GetOrCreatePlayer(player);
        int capacity = HoldCapacityOf(p);
        foreach (var s in stacks)
        {
            int fit = Math.Clamp(capacity - p.HoldTotal, 0, s.Count);
            if (fit > 0) AddToHold(p, s.Item, fit);
            StartingItemsClamped += s.Count - fit;
        }
    }

    /// <summary>Like <see cref="CanPay"/> for building costs: always true when the world has free building.</summary>
    public bool CanPayBuild(int player, ItemStack[] cost, int times = 1) => Rules.FreeBuild || CanPay(player, cost, times);

    /// <summary>Like <see cref="Pay"/> for building costs: takes nothing when the world has free building.</summary>
    public void PayBuild(int player, ItemStack[] cost, int times = 1)
    {
        if (!Rules.FreeBuild) Pay(player, cost, times);
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

    /// <summary>Removes items from a hold for good (the caller has checked the count).</summary>
    internal void DumpItems(int player, int item, int count)
    {
        var p = GetPlayer(player);
        p.Hold[item] -= count;
        p.HoldTotal -= count;
        HoldOut += count;
    }

    /// <summary>Returns items to a hold, ignoring capacity.</summary>
    public void Refund(int player, ItemStack[] cost, int times = 1)
    {
        if (cost.Length == 0 || Rules.FreeBuild) return; // free building takes nothing, so removing gives nothing back
        var p = GetOrCreatePlayer(player);
        foreach (var s in cost) AddToHold(p, s.Item, s.Count * times);
    }

    public void StartMining(int player, int deposit)
    {
        var d = GetDeposit(deposit);
        if (d.Remaining <= 0) throw new InvalidOperationException("The deposit is empty.");
        var p = GetOrCreatePlayer(player);
        if (p.MiningDeposit != deposit) { p.MineProgress = 0; p.LaserCredit = 0; }
        p.MiningDeposit = deposit;
        p.LaserReleased = false; // starting to mine pulls the trigger, as it always meant
    }

    public void StopMining(int player) => ClearMining(GetOrCreatePlayer(player));

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

    /// <summary>ADR 0039: adds crafts to the workbench queue. The caller has checked the recipe and the rules.</summary>
    public void QueueBenchCore(int player, int recipe, int count)
    {
        var p = GetOrCreatePlayer(player);
        for (int i = 0; i < count; i++) p.BenchQueue.Add(recipe);
    }

    /// <summary>The crafts waiting at the workbench of a player, in order.</summary>
    public int BenchQueueCount(int player) => _players.TryGetValue(player, out var p) ? p.BenchQueue.Count : 0;

    /// <summary>ADR 0039: cancels the craft in progress (inputs back to the hold) and empties the queue.</summary>
    public void ClearBenchCore(int player)
    {
        var p = GetOrCreatePlayer(player);
        AbortShipCraft(p);
        p.BenchQueue.Clear();
    }

    private Player GetPlayer(int id) =>
        _players.TryGetValue(id, out var p) ? p : throw new ArgumentOutOfRangeException(nameof(id), "Unknown player.");

    private Player GetOrCreatePlayer(int id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (!_players.TryGetValue(id, out var p))
        {
            p = new Player(id, Content?.Items.Count ?? 0) { Hull = ShipMaxHull };
            _players.Add(id, p);
        }
        return p;
    }

    /// <summary>
    /// Unloaders (machine id order): every interval, move one item from the owner's hold to an output belt while the hold
    /// keeps more than the reserve. The item enters the belts like a source's, and leaves the hold like a payment.
    /// </summary>
    private void StepUnloaders()
    {
        if (Content is null) return;
        foreach (var m in _machines)
        {
            if (m is null) continue;
            var def = Content.Machines[m.Type].Unloader;
            if (def is null || !m.Powered) continue; // an unpowered tick does not even count down the interval
            if (m.TurretCooldown > 0) m.TurretCooldown--;
            if (m.TurretCooldown > 0 || m.OutCount == 0 || m.Owner < 0 || !_players.TryGetValue(m.Owner, out var p)) continue;
            if (p.Hold[def.Item] <= def.Reserve) continue;

            for (int k = 0; k < m.OutCount; k++)
            {
                int index = (m.RoundRobin + k) % m.OutCount;
                var belt = _segs[m.OutBelts[index]]!;
                if (!belt.CanAccept) continue;
                p.Hold[def.Item]--;
                p.HoldTotal--;
                HoldOut++;
                Spawned++;
                belt.PushTail(belt.Count == 0 ? 0 : belt.TailPos - Units.ItemSpacing, (ushort)def.Item);
                m.RoundRobin = (index + 1) % m.OutCount;
                m.TurretCooldown = def.IntervalTicks;
                break;
            }
        }
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
            p.StepDx = p.StepDy = p.StepDz = 0;
            if (p.ScanCooldown > 0) p.ScanCooldown--;
            if (p.JumpRemaining > 0)
            {
                if (--p.JumpRemaining == 0) CompleteJump(p);
            }
            else if (p.RespawnRemaining == 0 && ((p.MoveX | p.MoveY | p.MoveZ) != 0 || p.HasThrust || p.HasVelocity))
            {
                int x0 = p.X, y0 = p.Y, z0 = p.Z;
                MoveShip(p);
                p.StepDx = p.X - x0; p.StepDy = p.Y - y0; p.StepDz = p.Z - z0;
                DiscoverAt(p);
            }

            if (p.MiningDeposit >= 0)
            {
                var d = _deposits[p.MiningDeposit];
                if (d.Remaining <= 0)
                {
                    ClearMining(p);
                }
                else if (LaserWouldMine(p) && LaserAdvances(p) && ++p.MineProgress >= MineTicks(d.Density, LaserPercentFor(p, p.MiningDeposit)) * HandMiningPenalty(p))
                {
                    p.MineProgress = 0;
                    d.Remaining--;
                    MinedByHand++;
                    CountProduced(d.Item, 1);
                    CountMined(d.Item, 1);
                    HandMinedItem(p, d);
                }
            }
            if (!StationFirst ? p.ShipRecipe >= 0 : p.ShipCrafting || p.BenchQueue.Count > 0) StepShip(p);
        }
    }

    private void MoveShip(Player p)
    {
        int flight = FlightStructure(p);
        if (flight >= 0)
        {
            MoveShipStructure(p, flight);
            return;
        }

        int limit = WorldLimit;
        if (p.HasThrust)
        {
            // Truncation toward zero on the accumulated value with a signed remainder: both signs behave alike.
            static int Advance(int thrust, ref int rem)
            {
                int acc = thrust * PlayerRules.MoveUnitsPerTick + rem;
                rem = acc % PlayerRules.ThrustScale;
                return acc / PlayerRules.ThrustScale;
            }
            p.X = Math.Clamp(p.X + Advance(p.ThrustX, ref p.ThrustRemX), -limit, limit);
            p.Y = Math.Clamp(p.Y + Advance(p.ThrustY, ref p.ThrustRemY), -limit, limit);
            p.Z = Math.Clamp(p.Z + Advance(p.ThrustZ, ref p.ThrustRemZ), -limit, limit);
            return;
        }

        int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
        int speed = axes switch
        {
            1 => PlayerRules.MoveUnitsPerTick,
            2 => PlayerRules.MoveUnitsPerTickDiagonal2,
            _ => PlayerRules.MoveUnitsPerTickDiagonal3,
        };
        p.X = Math.Clamp(p.X + p.MoveX * speed, -limit, limit);
        p.Y = Math.Clamp(p.Y + p.MoveY * speed, -limit, limit);
        p.Z = Math.Clamp(p.Z + p.MoveZ * speed, -limit, limit);
    }

    private void StepShip(Player p)
    {
        bool bench = StationFirst; // ADR 0039: one craft at a time from a queue, 8 s each
        if (bench && !p.ShipCrafting && p.BenchQueue.Count > 0) p.ShipRecipe = p.BenchQueue[0];
        var r = Content!.Recipes[p.ShipRecipe];

        if (p.ShipCrafting && --p.ShipRemaining == 0)
        {
            p.ShipReserved = 0;
            p.ShipCrafting = false;
            foreach (var o in r.Outputs) { AddToHold(p, o.Item, o.Count); CountProduced(o.Item, o.Count); }
        }

        if (p.ShipCrafting) return;
        if (bench)
        {
            if (p.BenchQueue.Count == 0) return;
            p.ShipRecipe = p.BenchQueue[0]; // the next craft: only looked at here, it leaves the queue when its inputs are taken
            r = Content.Recipes[p.ShipRecipe];
        }

        int inTotal = 0, outTotal = 0;
        foreach (var i in r.Inputs)
        {
            if (p.Hold[i.Item] < i.Count) return;
            inTotal += i.Count;
        }
        foreach (var o in r.Outputs) outTotal += o.Count;
        if (p.HoldTotal - inTotal + outTotal > HoldCapacityOf(p)) return;

        foreach (var i in r.Inputs)
        {
            p.Hold[i.Item] -= i.Count;
            p.HoldTotal -= i.Count;
            HoldOut += i.Count;
        }
        if (bench) p.BenchQueue.RemoveAt(0);
        p.ShipCrafting = true;
        p.ShipRemaining = r.TimeTicks * (bench ? PlayerRules.BenchCraftMultiplier : PlayerRules.ShipCraftMultiplier);
        p.ShipReserved = outTotal;
    }

    // ---- research -----------------------------------------------------------------------------

    public bool IsResearched(int node) => (uint)node < (uint)_level.Length && _level[node] > 0;

    /// <summary>How many times a node has been researched (0 or 1, more for repeatable nodes).</summary>
    public int ResearchLevel(int node) => _level[node];

    /// <summary>
    /// Items the next level of a node costs: the base cost for the first level, then each level costs the previous one
    /// times (100 + growth) percent, rounded up. For a finished node it is the cost of its last level.
    /// </summary>
    public ItemStack[] ResearchCost(int node)
    {
        var def = Content!.Research[node];
        int steps = Math.Min(_level[node], def.MaxLevels - 1);
        var cost = def.Cost;
        for (int k = 0; k < steps; k++)
            cost = cost.Select(c => new ItemStack(c.Item, (int)Math.Min(int.MaxValue / 4, ((long)c.Count * (100 + def.CostGrowthPercent) + 99) / 100))).ToArray();
        return cost;
    }
    public int CurrentResearch => _researchCurrent;
    public int ResearchedCount => _level.Count(l => l > 0);
    public int ResearchProgress(int node, int item) => _progress[node][item];

    /// <summary>True if the node exists, is not finished and all its requirements are.</summary>
    public bool CanResearch(int node)
    {
        if (Content is null || !Content.IsResearch(node) || _level[node] >= Content.Research[node].MaxLevels) return false;
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
            (c.MachineGated[type] && !((StationRules || StructureRules) && c.Machines[type].Role == MachineRole.Unloader) && !c.Research.Any(n => _level[n.Index] > 0 && n.UnlockMachines.Contains(type))));

    public bool IsRecipeLocked(int recipe) =>
        Mode == GameMode.Campaign && Content is { } c &&
            c.RecipeGated[recipe] && !c.Research.Any(n => _level[n.Index] > 0 && n.UnlockRecipes.Contains(recipe));

    public bool IsBeltLocked(BeltTier tier) =>
        Mode == GameMode.Campaign && Content is { } c &&
            c.BeltGated[(int)tier] && !c.Research.Any(n => _level[n.Index] > 0 && n.UnlockBelts.Contains(tier));

    /// <summary>A depot hands items to its owner's hold while there is room; when the hold is full the belt waits.</summary>
    private bool TryFeedDepot(Machine depot, BeltSegment s, int item)
    {
        if (depot.Owner < 0 || item >= Content!.Items.Count) return false;
        var p = GetOrCreatePlayer(depot.Owner);
        int capacity = HoldCapacityOf(p);
        if (p.HoldTotal + p.ShipReserved >= capacity) return false;
        if (StructureRules && (long)(p.HoldTotal + p.ShipReserved) * 100 >= (long)capacity * PlayerRules.DepotHoldLimitPercent) return false; // a few slots stay free
        if (p.Hold[item] >= capacity * 2 / 5) return false; // one item type never takes the whole hold

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
        int need = 0;
        foreach (var c in ResearchCost(_researchCurrent))
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
        var cost = ResearchCost(_researchCurrent);
        foreach (var c in cost)
            if (_progress[_researchCurrent][c.Item] < c.Count) return;

        _level[_researchCurrent]++;
        // A repeatable node that has more levels to go starts its next level from nothing.
        if (_level[_researchCurrent] < Content!.Research[_researchCurrent].MaxLevels) System.Array.Clear(_progress[_researchCurrent]);
        _researchCurrent = -1;
        RecomputeBonuses(scaleHp: true);
        StructureVersion++;
    }

    // ---- state, checksum, validation ----------------------------------------------------------

    private void CopyEconomyFrom(BeltNetwork other)
    {
        _players = new SortedDictionary<int, Player>(other._players.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()));
        _deposits = other._deposits.Select(d => d.Clone()).ToList();
        CopyGalaxyFrom(other);
        CopyThreatFrom(other);
        CopyFeelFrom(other);
        _level = (int[])other._level.Clone();
        RecomputeBonuses(scaleHp: false);
        _progress = other._progress.Select(a => (int[])a.Clone()).ToArray();
        _researchCurrent = other._researchCurrent;
        HoldIn = other.HoldIn;
        HoldOut = other.HoldOut;
        MinedByHand = other.MinedByHand;
        MinedByDrones = other.MinedByDrones;
        DrilledTotal = other.DrilledTotal;
        ResearchItemsUsed = other.ResearchItemsUsed;
        DepotDelivered = other.DepotDelivered;
    }

    private void MixEconomy(Action<long> mix)
    {
        mix(HoldIn); mix(HoldOut); mix(MinedByHand); mix(DrilledTotal); mix(ResearchItemsUsed); mix(DepotDelivered);
        if (MinedByDrones != 0) mix(MinedByDrones); // worlds without drone mining hash as before
        MixGalaxy(mix);
        MixThreat(mix);
        MixFeel(mix);
        mix(_deposits.Count);
        foreach (var d in _deposits)
        {
            mix(d.Item); mix(d.Initial); mix(d.Remaining); mix(d.X); mix(d.Y); mix(d.Z); mix(d.Revealed ? 1 : 0);
            if (d.Density != DepositRules.DefaultDensity) mix(d.Density); // all-100 worlds hash as before densities existed
        }
        mix(_players.Count);
        foreach (var p in _players.Values)
        {
            mix(p.ScanCooldown); mix(p.JumpTarget); mix(p.JumpRemaining);
            mix(p.Hull); mix(p.Armed ? 1 : 0); mix(p.WeaponCooldown); mix(p.RespawnRemaining);
            mix(p.X); mix(p.Y); mix(p.Z); mix(p.MoveX); mix(p.MoveY); mix(p.MoveZ);
            mix(p.ThrustX); mix(p.ThrustY); mix(p.ThrustZ); mix(p.ThrustRemX); mix(p.ThrustRemY); mix(p.ThrustRemZ);
            mix(p.Id); mix(p.HoldTotal); mix(p.MiningDeposit); mix(p.MineProgress);
            if (p.ShipStructure >= 0) mix(p.ShipStructure);
            if (p.ShipLost) mix(-77);
            if (p.LaserReleased) mix(-91); // mixed only when set and when non-zero: worlds that never touch the laser trigger hash as before
            if (p.LaserCredit != 0) mix(p.LaserCredit);
            if (p.Boost) mix(-93);
            if (p.Cruise) mix(-94); // mixed only when set: worlds without the cruise drive hash as before
            if (p.HasVelocity || (p.VelRemX | p.VelRemY | p.VelRemZ) != 0) { mix(-3303); mix(p.VelX); mix(p.VelY); mix(p.VelZ); mix(p.VelRemX); mix(p.VelRemY); mix(p.VelRemZ); } // worlds without inertia hash as before
            mix(p.ShipRecipe); mix(p.ShipCrafting ? 1 : 0); mix(p.ShipRemaining); mix(p.ShipReserved);
            if (p.BenchQueue.Count > 0) { mix(-4404); foreach (int q in p.BenchQueue) mix(q); } // mixed only when not empty: worlds without the queue hash as before
            foreach (int h in p.Hold) mix(h);
            MixInventory(p, mix);
        }
        mix(_researchCurrent);
        for (int i = 0; i < _level.Length; i++)
        {
            mix(_level[i]);
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
            ValidateInventory(p);
            held += sum;

            if (Math.Abs(p.X) > WorldLimit || Math.Abs(p.Y) > WorldLimit || Math.Abs(p.Z) > WorldLimit)
                Fail($"player {p.Id} is outside the world");
            if (p.MoveX is < -1 or > 1 || p.MoveY is < -1 or > 1 || p.MoveZ is < -1 or > 1) Fail($"player {p.Id} has an invalid movement intent");
            if (!PlayerRules.ThrustInRange(p.ThrustX, p.ThrustY, p.ThrustZ)) Fail($"player {p.Id} has an invalid thrust");
            int remLimit = FlightStructure(p) >= 0 ? PlayerRules.ThrustScale * StructureFlight.RemainderScale : PlayerRules.ThrustScale;
            if (Math.Abs(p.ThrustRemX) >= remLimit || Math.Abs(p.ThrustRemY) >= remLimit || Math.Abs(p.ThrustRemZ) >= remLimit)
                Fail($"player {p.Id} has a thrust remainder out of range");
            if (p.HasThrust && (p.MoveX | p.MoveY | p.MoveZ) != 0) Fail($"player {p.Id} has both an axis intent and a thrust");
            if (Math.Abs(p.VelRemX) >= 1000 || Math.Abs(p.VelRemY) >= 1000 || Math.Abs(p.VelRemZ) >= 1000) Fail($"player {p.Id} has a velocity remainder out of range");
            if ((p.HasVelocity || (p.VelRemX | p.VelRemY | p.VelRemZ) != 0) && !FlightInertia) Fail($"player {p.Id} has a velocity in a world without inertia");
            // A structure flight keeps a sub-unit remainder for the axis intent too (engine percent scales the speed).
            bool axisRemainder = FlightStructure(p) >= 0 && (p.MoveX | p.MoveY | p.MoveZ) != 0;
            if (!p.HasThrust && !axisRemainder && (p.ThrustRemX | p.ThrustRemY | p.ThrustRemZ) != 0) Fail($"player {p.Id} has a thrust remainder without thrust");

            if (p.MiningDeposit != -1 && !IsDeposit(p.MiningDeposit)) Fail($"player {p.Id} mines an unknown deposit");
            int mineLimit = DepositRules.Scale(PlayerRules.MineTicksPerItem, p.MiningDeposit >= 0 ? _deposits[p.MiningDeposit].Density : DepositRules.MinDensity);
            if (p.MineProgress < 0 || p.MineProgress >= mineLimit * MineLimitFactor) Fail($"player {p.Id} mine progress out of range");
            if (p.LaserCredit is < 0 or >= PowerRules.FullSatisfaction) Fail($"player {p.Id} laser credit out of range");
            if (p.MiningDeposit < 0 && (p.LaserReleased || p.LaserCredit != 0)) Fail($"player {p.Id} has laser state without a mining order");
            if (p.BenchQueue.Count > PlayerRules.BenchQueueMax || (!StationFirst && p.BenchQueue.Count > 0)) Fail($"player {p.Id} has a bad workbench queue");
            foreach (int q in p.BenchQueue)
                if (Content is null || !Content.IsRecipe(q) || Content.Machines[Content.Recipes[q].Machine].Role != MachineRole.Ship) Fail($"player {p.Id} queues an invalid workbench recipe");
            if (p.ShipRecipe == -1)
            {
                if (p.ShipCrafting || p.ShipReserved != 0) Fail($"player {p.Id} has no ship recipe but is crafting");
            }
            else
            {
                var content = Content ?? throw new InvalidOperationException("Players have recipes without content.");
                if (!content.IsRecipe(p.ShipRecipe) || content.Machines[content.Recipes[p.ShipRecipe].Machine].Role != MachineRole.Ship)
                    Fail($"player {p.Id} has an invalid ship recipe");
                if (p.ShipCrafting && (p.ShipRemaining < 1 || p.ShipRemaining > content.Recipes[p.ShipRecipe].TimeTicks * (StationFirst ? PlayerRules.BenchCraftMultiplier : PlayerRules.ShipCraftMultiplier)))
                    Fail($"player {p.Id} ship craft time out of range");
                if (!p.ShipCrafting && (p.ShipRemaining != 0 || p.ShipReserved != 0)) Fail($"player {p.Id} is idle with a craft pending");
            }
        }
        if (HoldIn - HoldOut != held) Fail($"hold accounting broken: in {HoldIn} - out {HoldOut} != held {held}");
        ValidateGalaxy();
        ValidateThreat();
        ValidateFeel();

        long extracted = 0;
        foreach (var d in _deposits)
        {
            if (d.Remaining < 0 || d.Remaining > d.Initial) Fail($"deposit {d.Id} remaining {d.Remaining} out of range");
            extracted += d.Initial - d.Remaining;
        }
        extracted += FieldRememberedExtracted(); // ore taken from rocks of the infinite field that were forgotten since (ADR 0040)
        if (extracted + RegrownTotal != MinedByHand + DrilledTotal + MinedByDrones)
            Fail($"deposit accounting broken: extracted {extracted} + regrown {RegrownTotal} != hand {MinedByHand} + drills {DrilledTotal} + drones {MinedByDrones}");

        for (int i = 0; i < _level.Length; i++)
        {
            var node = Content!.Research[i];
            if (_level[i] < 0 || _level[i] > node.MaxLevels) Fail($"research {node.Id} level {_level[i]} out of range");
            var cost = ResearchCost(i);
            if (_level[i] > 0 && !node.Requires.All(IsResearched)) Fail($"research {node.Id} is done without its requirements");
            for (int item = 0; item < _progress[i].Length; item++)
            {
                int cap = cost.Where(c => c.Item == item).Select(c => c.Count).FirstOrDefault();
                if (_progress[i][item] < 0 || _progress[i][item] > cap) Fail($"research {node.Id} progress for item {item} out of range");
            }
        }
        if (_researchCurrent != -1 && !CanResearch(_researchCurrent)) Fail("the current research cannot be researched");
    }
}
