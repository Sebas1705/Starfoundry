using System.Text.Json;

namespace Starfoundry.Sim;

public sealed record ItemDef(int Index, string Id);

public sealed record ItemStack(int Item, int Count);

/// <summary>What a machine type does.</summary>
public enum MachineRole
{
    /// <summary>Works through recipes on items delivered by belts.</summary>
    Crafter = 0,

    /// <summary>Takes ore out of a linked deposit; its recipes have no inputs.</summary>
    Drill = 1,

    /// <summary>Consumes the items the current research needs; has no recipes.</summary>
    Lab = 2,

    /// <summary>Lives on the player's ship (the micro-refinery); never placed in the world.</summary>
    Ship = 3,

    /// <summary>Delivers every item that reaches it into the hold of the player who built it, as long as there is room.</summary>
    Depot = 4,

    /// <summary>Shoots enemies in range using ammo items delivered by belts.</summary>
    Turret = 5,

    /// <summary>A tough building with no recipe or belts. Enemies in its system attack the nearest wall before anything else.</summary>
    Wall = 6,

    /// <summary>Repairs damaged machines in range over time, using an item (ingots) delivered by belts.</summary>
    Repairer = 7,

    /// <summary>Takes one item at a time from its owner's hold and puts it on an output belt, keeping a reserve in the hold.</summary>
    Unloader = 8,
}

/// <summary>How a turret fires. One ammo item is used per shot.</summary>
public sealed record TurretDef(int RangeCells, int Damage, int IntervalTicks, int AmmoItem, int AmmoCap);

/// <summary>How a repairer works: every interval it restores <c>Heal</c> hit points to the most damaged machine in range and uses one item.</summary>
public sealed record RepairerDef(int RangeCells, int Heal, int IntervalTicks, int Item, int ItemCap);

/// <summary>How an unloader works: every interval it moves one <c>Item</c> from the owner's hold to its belt, but only while the hold keeps more than <c>Reserve</c> of it.</summary>
public sealed record UnloaderDef(int Item, int IntervalTicks, int Reserve);

/// <summary>Station module costs: the first module (the core), later hull modules, and the percent each existing module adds to the next cost.</summary>
public sealed record ModuleRules(ItemStack[] CoreCost, ItemStack[] HullCost, int GrowthPercent);

/// <summary>An enemy type. Speed is in units per tick (240 units per cell).</summary>
public sealed record EnemyDef(int Index, string Id, int Hp, int Speed, int Damage, int RangeCells, int IntervalTicks);

/// <summary>
/// From wave <c>FromWave</c> on (wave 0 is the first), a wave contains <c>Base + PerWave * (waveNumber - FromWave)</c>
/// enemies of this type; earlier waves have none of them.
/// </summary>
public sealed record WaveSpec(int Enemy, int Base, int PerWave, int FromWave = 0);

/// <summary>
/// How the Remnant threat works. Each system sums the signatures of the machines in it; above the threshold the
/// alert meter fills by the excess every tick, below it the meter decays. A full meter schedules a wave after the
/// warning time.
/// </summary>
public sealed record ThreatRules(
    int AlertMax, int SignatureThreshold, int DecayPerTick, int AlertAfterWave, int WarningTicks, int NodeDistanceCells,
    int MaxEnemies, int ShipHull, int ShipWeaponRangeCells, int ShipWeaponDamage, int ShipWeaponIntervalTicks, int RespawnTicks,
    int ModuleRegenTicks = ThreatRules.DefaultModuleRegenTicks, int FirstWaveGraceTicks = 0, int StructureShipDamagePercent = 100)
{
    /// <summary>Structure worlds: every module regenerates one hit point this often while its structure is fully powered (10 s).</summary>
    public const int DefaultModuleRegenTicks = 10 * Units.TicksPerSecond;
}

/// <param name="Cost">Items paid from the builder's hold in campaign mode (refunded on removal).</param>
/// <param name="SandboxOnly">Allowed only in sandbox mode (test fixtures and debug machines).</param>
/// <param name="Hp">Hit points; a machine at zero is destroyed.</param>
/// <param name="Signature">What the machine adds to its system's signature, which attracts waves.</param>
public sealed record MachineDef(
    int Index, string Id, MachineRole Role, ItemStack[] Cost, bool SandboxOnly, int Hp = 100, int Signature = 0, TurretDef? Turret = null, RepairerDef? Repairer = null, UnloaderDef? Unloader = null,
    int PowerUse = 0);

/// <param name="TimeTicks">Crafting time in simulation ticks (converted once at load from seconds).</param>
public sealed record RecipeDef(int Index, string Id, int Machine, ItemStack[] Inputs, ItemStack[] Outputs, int TimeTicks);

/// <param name="CostPerCell">Items paid per cell of belt in campaign mode.</param>
public sealed record BeltDef(BeltTier Tier, string Id, ItemStack[] CostPerCell);

/// <summary>What a research bonus improves. Bonuses of finished research add up as percentages.</summary>
public enum BonusKind
{
    /// <summary>Turret damage per shot goes up.</summary>
    TurretDamage = 0,

    /// <summary>The interval between turret shots goes down.</summary>
    TurretRate = 1,

    /// <summary>The maximum hit points of every machine go up; machines already standing keep their share of it.</summary>
    MachineHp = 2,

    /// <summary>Repairers restore more hit points per use.</summary>
    RepairHeal = 3,

    /// <summary>The ship's hull points go up; the ship keeps its share of the new maximum.</summary>
    ShipHull = 4,

    /// <summary>The ship weapon does more damage per shot.</summary>
    ShipDamage = 5,
}

public sealed record Bonus(BonusKind Kind, int Percent);

/// <summary>Abilities of the ship that research can unlock.</summary>
public enum Ability
{
    /// <summary>Jumping along lanes between systems.</summary>
    Jump = 0,

    /// <summary>Placing drone bay modules.</summary>
    DroneBay = 1,

    /// <summary>Placing shield modules.</summary>
    Shield = 2,

    /// <summary>Placing sensor modules.</summary>
    Sensor = 3,

    /// <summary>Placing mark 2 reactor modules.</summary>
    ReactorMk2 = 4,
}

/// <param name="Requires">Research nodes that must be finished first.</param>
public sealed record ResearchDef(
    int Index, string Id, ItemStack[] Cost, int[] Requires, int[] UnlockMachines, int[] UnlockRecipes, BeltTier[] UnlockBelts,
    Ability[] UnlockAbilities, Bonus[] Bonuses, int MaxLevels = 1, int CostGrowthPercent = 0);

/// <param name="SystemCount">Systems in the galaxy, including home.</param>
/// <param name="SpacingCells">Distance between neighbouring grid positions.</param>
/// <param name="LanesPerSystem">Each system gets at least this many lanes (the graph is always connected).</param>
/// <param name="SystemRadiusCells">A ship within this many cells of a system's anchor is in that system.</param>
public sealed record GalaxyRules(int SystemCount, int SpacingCells, int LanesPerSystem, int SystemRadiusCells);

/// <summary>How many deposits of an item an archetype creates and how rich they are (inclusive ranges).</summary>
/// <param name="DensityMin">Optional richness range in percent (see <see cref="DepositRules"/>); both 0 means the legacy density of 100.</param>
public sealed record DepositSpec(int Item, int CountMin, int CountMax, int AmountMin, int AmountMax, int DensityMin = 0, int DensityMax = 0)
{
    public bool HasDensity => DensityMax > 0;
}

/// <param name="Weight">Relative chance of being picked for a system (home is never picked).</param>
/// <param name="DistanceMin">Deposits are placed this far from the system anchor, in cells (inclusive range).</param>
/// <param name="DepthMin">Optional: only systems at least this many lane jumps from home can get the archetype (0 = no limit).</param>
/// <param name="DepthMax">Optional: only systems at most this many jumps from home (0 = no limit).</param>
/// <param name="Guaranteed">If true and no system picked the archetype, the nearest eligible system (or the farthest one in a shallow galaxy) is given it.</param>
public sealed record ArchetypeDef(
    int Index, string Id, int Weight, bool IsHome, int DistanceMin, int DistanceMax, DepositSpec[] Deposits,
    int DepthMin = 0, int DepthMax = 0, bool Guaranteed = false)
{
    public bool HasDepthLimit => DepthMin > 0 || DepthMax > 0;
    public bool AllowedAt(int depth) => depth >= DepthMin && (DepthMax == 0 || depth <= DepthMax);
}

/// <summary>Fixed limits of a deposit's density (percent of the base mining rate).</summary>
public static class DepositRules
{
    public const int DefaultDensity = 100;
    public const int MinDensity = 25;
    public const int MaxDensity = 200;

    /// <summary>Ticks a time of <paramref name="baseTicks"/> takes at a density: base * 100 / density, never below 1.</summary>
    public static int Scale(int baseTicks, int density) => Math.Max(1, (int)((long)baseTicks * 100 / density));
}

/// <summary>
/// Immutable game content loaded from data files: items, machine types, recipes, belt costs and research.
/// Simulation code refers to them by index; files refer to them by string id. Loading is strict (unknown
/// properties, duplicates and dangling references are errors) and the <see cref="Hash"/> identifies the exact
/// content, so replays and co-op peers can detect a mismatch. Anything unlocked by some research node is
/// locked until that node is finished (in campaign mode); everything else is available from the start.
/// </summary>
public sealed class ContentDb
{
    public const int MaxItems = 256;
    public const int MaxStack = 64;
    public const int MaxRecipeStacks = 4;
    public const int MaxResearch = 256;
    public const int MaxCostStack = 10_000;
    public const int MaxRecipeTicks = Units.TicksPerSecond * 600;

    private readonly Dictionary<string, int> _items = new();
    private readonly Dictionary<string, int> _machines = new();
    private readonly Dictionary<string, int> _recipes = new();
    private readonly Dictionary<string, int> _research = new();

    public IReadOnlyList<ItemDef> Items { get; }
    public IReadOnlyList<MachineDef> Machines { get; }
    public IReadOnlyList<RecipeDef> Recipes { get; }

    /// <summary>Belt definitions by tier (index 0 is Mk1). A tier without a definition is free and unlocked.</summary>
    public IReadOnlyList<BeltDef?> Belts { get; }
    public IReadOnlyList<ResearchDef> Research { get; }

    /// <summary>True if the machine type is unlocked by some research node.</summary>
    public IReadOnlyList<bool> MachineGated { get; }
    public IReadOnlyList<bool> RecipeGated { get; }
    public IReadOnlyList<bool> BeltGated { get; }

    /// <summary>True if the ability is unlocked by some research node (campaign mode needs that research).</summary>
    public IReadOnlyList<bool> AbilityGated { get; }

    /// <summary>Galaxy generation rules, or null if the content has none.</summary>
    public GalaxyRules? Galaxy { get; }
    public IReadOnlyList<ArchetypeDef> Archetypes { get; }

    /// <summary>Items a jump costs, paid from the hold in campaign mode.</summary>
    public ItemStack[] JumpCost { get; }

    /// <summary>Threat rules, or null if the content has none.</summary>
    public ThreatRules? Threat { get; }

    /// <summary>Station module costs, or null if the content has no station modules (then module commands are rejected).</summary>
    public ModuleRules? Modules { get; }

    /// <summary>Module kinds of ships and stations (cost, hit points, allowed machine roles), or null if the content has none
    /// (then the structure commands that place modules are rejected).</summary>
    public StructureContent? Structures { get; }

    /// <summary>The ship the campaign starts in (built at world creation when structure rules are on), or null if the content has none.</summary>
    public StructureTemplate? StarterShip { get; }

    /// <summary>Index of the reactor fuel item (<see cref="PowerRules.FuelItemId"/>), or -1 when the content has none.</summary>
    public int FuelItem { get; }

    /// <summary>Index of the compact reactor fuel (<see cref="PowerRules.FuelCellItemId"/>), or -1 when the content has none.</summary>
    public int FuelCellItem { get; }
    public IReadOnlyList<EnemyDef> Enemies { get; }
    public IReadOnlyList<WaveSpec> Waves { get; }
    public ulong Hash { get; }

    private ContentDb(List<ItemDef> items, List<MachineDef> machines, List<RecipeDef> recipes, BeltDef?[] belts, List<ResearchDef> research,
        GalaxyRules? galaxy, List<ArchetypeDef> archetypes, ItemStack[] jumpCost,
        ThreatRules? threat, List<EnemyDef> enemies, List<WaveSpec> waves, ModuleRules? modules, StructureContent? structures,
        StructureTemplate? starterShip)
    {
        Modules = modules;
        Structures = structures;
        StarterShip = starterShip;
        Threat = threat;
        Enemies = enemies;
        Waves = waves;
        Items = items;
        Machines = machines;
        Recipes = recipes;
        Belts = belts;
        Research = research;
        Galaxy = galaxy;
        Archetypes = archetypes;
        JumpCost = jumpCost;
        foreach (var i in items) _items.Add(i.Id, i.Index);
        FuelItem = _items.TryGetValue(PowerRules.FuelItemId, out int fuel) ? fuel : -1;
        FuelCellItem = _items.TryGetValue(PowerRules.FuelCellItemId, out int cell) ? cell : -1;
        foreach (var m in machines) _machines.Add(m.Id, m.Index);
        foreach (var r in recipes) _recipes.Add(r.Id, r.Index);
        foreach (var r in research) _research.Add(r.Id, r.Index);

        var mg = new bool[machines.Count];
        var rg = new bool[recipes.Count];
        var bg = new bool[3];
        var ag = new bool[Enum.GetValues<Ability>().Length];
        foreach (var node in research)
        {
            foreach (int m in node.UnlockMachines) mg[m] = true;
            foreach (int r in node.UnlockRecipes) rg[r] = true;
            foreach (var b in node.UnlockBelts) bg[(int)b] = true;
            foreach (var a in node.UnlockAbilities) ag[(int)a] = true;
        }
        MachineGated = mg;
        RecipeGated = rg;
        BeltGated = bg;
        AbilityGated = ag;
        Hash = ComputeHash();
    }

    public int ItemIndex(string id) => _items.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown item '{id}'.");
    public int MachineIndex(string id) => _machines.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown machine '{id}'.");
    public int RecipeIndex(string id) => _recipes.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown recipe '{id}'.");
    public int ResearchIndex(string id) => _research.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown research '{id}'.");

    public bool IsMachine(int index) => (uint)index < (uint)Machines.Count;
    public bool IsRecipe(int index) => (uint)index < (uint)Recipes.Count;
    public bool IsResearch(int index) => (uint)index < (uint)Research.Count;

    /// <summary>Parses and validates a content file. Throws <see cref="InvalidDataException"/> with a precise message.</summary>
    public static ContentDb FromJson(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException e) { throw new InvalidDataException($"Invalid JSON: {e.Message}", e); }

        using (doc)
        {
            var root = doc.RootElement;
            RequireObject(root, "content", "schema", "items", "machines", "recipes", "belts", "research", "galaxy", "archetypes", "jump_cost",
                "threat", "enemies", "waves", "modules", "structures", "structure_templates");
            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1)
                throw new InvalidDataException("Unsupported or missing content schema (expected 1).");

            var allIds = new HashSet<string>();

            var items = new List<ItemDef>();
            var itemIds = new Dictionary<string, int>();
            foreach (var e in Array(root, "items"))
            {
                RequireObject(e, "item", "id");
                string id = Id(e, "item");
                if (!itemIds.TryAdd(id, items.Count)) throw new InvalidDataException($"Duplicate item '{id}'.");
                items.Add(new ItemDef(items.Count, id));
            }
            if (items.Count == 0 || items.Count > MaxItems) throw new InvalidDataException($"Item count must be 1 to {MaxItems}.");

            var machines = new List<MachineDef>();
            var machineIds = new Dictionary<string, int>();
            foreach (var e in Array(root, "machines"))
            {
                RequireObject(e, "machine", "id", "role", "cost", "sandbox_only", "hp", "signature", "turret", "repairer", "unloader", "power_use");
                string id = Id(e, "machine");
                if (!machineIds.TryAdd(id, machines.Count)) throw new InvalidDataException($"Duplicate machine '{id}'.");
                if (!allIds.Add(id)) throw new InvalidDataException($"Id '{id}' is used twice across machines, recipes, belts and research.");

                var role = MachineRole.Crafter;
                if (e.TryGetProperty("role", out var r))
                {
                    role = r.ValueKind == JsonValueKind.String ? r.GetString() switch
                    {
                        "crafter" => MachineRole.Crafter,
                        "drill" => MachineRole.Drill,
                        "lab" => MachineRole.Lab,
                        "ship" => MachineRole.Ship,
                        "depot" => MachineRole.Depot,
                        "turret" => MachineRole.Turret,
                        "wall" => MachineRole.Wall,
                        "repairer" => MachineRole.Repairer,
                        "unloader" => MachineRole.Unloader,
                        _ => throw new InvalidDataException($"Machine '{id}' has an unknown role."),
                    } : throw new InvalidDataException($"Machine '{id}' role must be a string.");
                }
                var cost = e.TryGetProperty("cost", out _) ? Stacks(e, "cost", id, itemIds, allowEmpty: true, max: MaxCostStack) : System.Array.Empty<ItemStack>();
                bool sandboxOnly = false;
                if (e.TryGetProperty("sandbox_only", out var so))
                {
                    if (so.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException($"Machine '{id}' sandbox_only must be true or false.");
                    sandboxOnly = so.GetBoolean();
                }
                if (role == MachineRole.Ship && cost.Length > 0)
                    throw new InvalidDataException($"Ship machine '{id}' cannot have a cost (it is never built).");

                int hp = e.TryGetProperty("hp", out _) ? Int(e, "hp", id, 1, 100_000) : 100;
                int signature = e.TryGetProperty("signature", out _) ? Int(e, "signature", id, 0, 1000) : 0;
                TurretDef? turretDef = null;
                if (e.TryGetProperty("turret", out var tu))
                {
                    if (role != MachineRole.Turret) throw new InvalidDataException($"Machine '{id}' has turret data but is not a turret.");
                    RequireObject(tu, "turret", "range_cells", "damage", "interval_ticks", "ammo_item", "ammo_cap");
                    string ammo = Str(tu, "ammo_item", id);
                    if (!itemIds.TryGetValue(ammo, out int ammoIndex)) throw new InvalidDataException($"Turret '{id}' uses unknown ammo item '{ammo}'.");
                    turretDef = new TurretDef(Int(tu, "range_cells", id, 1, 100), Int(tu, "damage", id, 1, 10_000),
                        Int(tu, "interval_ticks", id, 1, 600), ammoIndex, Int(tu, "ammo_cap", id, 1, 1000));
                }
                else if (role == MachineRole.Turret) throw new InvalidDataException($"Turret '{id}' needs turret data.");
                RepairerDef? repairerDef = null;
                if (e.TryGetProperty("repairer", out var rp))
                {
                    if (role != MachineRole.Repairer) throw new InvalidDataException($"Machine '{id}' has repairer data but is not a repairer.");
                    RequireObject(rp, "repairer", "range_cells", "heal", "interval_ticks", "item", "item_cap");
                    string fuel = Str(rp, "item", id);
                    if (!itemIds.TryGetValue(fuel, out int fuelIndex)) throw new InvalidDataException($"Repairer '{id}' uses unknown item '{fuel}'.");
                    repairerDef = new RepairerDef(Int(rp, "range_cells", id, 1, 100), Int(rp, "heal", id, 1, 10_000),
                        Int(rp, "interval_ticks", id, 1, 600), fuelIndex, Int(rp, "item_cap", id, 1, 1000));
                }
                else if (role == MachineRole.Repairer) throw new InvalidDataException($"Repairer '{id}' needs repairer data.");
                UnloaderDef? unloaderDef = null;
                if (e.TryGetProperty("unloader", out var un))
                {
                    if (role != MachineRole.Unloader) throw new InvalidDataException($"Machine '{id}' has unloader data but is not an unloader.");
                    RequireObject(un, "unloader", "item", "interval_ticks", "reserve");
                    string carried = Str(un, "item", id);
                    if (!itemIds.TryGetValue(carried, out int carriedIndex)) throw new InvalidDataException($"Unloader '{id}' uses unknown item '{carried}'.");
                    unloaderDef = new UnloaderDef(carriedIndex, Int(un, "interval_ticks", id, 1, 600), Int(un, "reserve", id, 0, 60));
                }
                else if (role == MachineRole.Unloader) throw new InvalidDataException($"Unloader '{id}' needs unloader data.");
                int powerUse = e.TryGetProperty("power_use", out _) ? Int(e, "power_use", id, 0, 1000) : 0;
                machines.Add(new MachineDef(machines.Count, id, role, cost, sandboxOnly, hp, signature, turretDef, repairerDef, unloaderDef, powerUse));
            }

            var recipes = new List<RecipeDef>();
            var recipeIds = new Dictionary<string, int>();
            foreach (var e in Array(root, "recipes"))
            {
                RequireObject(e, "recipe", "id", "machine", "inputs", "outputs", "time_s");
                string id = Id(e, "recipe");
                if (!recipeIds.TryAdd(id, recipes.Count)) throw new InvalidDataException($"Duplicate recipe '{id}'.");
                if (!allIds.Add(id)) throw new InvalidDataException($"Id '{id}' is used twice across machines, recipes, belts and research.");

                string machineId = Str(e, "machine", id);
                if (!machineIds.TryGetValue(machineId, out int machine))
                    throw new InvalidDataException($"Recipe '{id}' uses unknown machine '{machineId}'.");

                var inputs = Stacks(e, "inputs", id, itemIds, allowEmpty: true, max: MaxStack);
                var outputs = Stacks(e, "outputs", id, itemIds, allowEmpty: false, max: MaxStack);

                var role = machines[machine].Role;
                if (role is MachineRole.Lab or MachineRole.Depot or MachineRole.Turret or MachineRole.Wall or MachineRole.Repairer or MachineRole.Unloader)
                    throw new InvalidDataException($"Recipe '{id}' targets a lab, depot, turret, wall, repairer or unloader, which have no recipes.");
                if (role == MachineRole.Drill && (inputs.Length != 0 || outputs.Length != 1))
                    throw new InvalidDataException($"Drill recipe '{id}' must have no inputs and exactly one output.");

                if (!e.TryGetProperty("time_s", out var t) || t.ValueKind != JsonValueKind.Number || !t.TryGetDecimal(out decimal seconds))
                    throw new InvalidDataException($"Recipe '{id}' needs a numeric time_s.");
                int ticks = (int)Math.Round(seconds * Units.TicksPerSecond, MidpointRounding.AwayFromZero);
                if (ticks < 1 || ticks > MaxRecipeTicks)
                    throw new InvalidDataException($"Recipe '{id}' time_s must be between one tick and {MaxRecipeTicks / Units.TicksPerSecond} seconds.");

                recipes.Add(new RecipeDef(recipes.Count, id, machine, inputs, outputs, ticks));
            }

            var belts = new BeltDef?[3];
            var beltIds = new Dictionary<string, BeltTier>();
            if (root.TryGetProperty("belts", out _))
            {
                foreach (var e in Array(root, "belts"))
                {
                    RequireObject(e, "belt", "id", "tier", "cost_per_cell");
                    string id = Id(e, "belt");
                    if (!allIds.Add(id)) throw new InvalidDataException($"Id '{id}' is used twice across machines, recipes, belts and research.");
                    string tierName = Str(e, "tier", id);
                    var tier = tierName switch
                    {
                        "mk1" => BeltTier.Mk1,
                        "mk2" => BeltTier.Mk2,
                        "mk3" => BeltTier.Mk3,
                        _ => throw new InvalidDataException($"Belt '{id}' has an unknown tier '{tierName}'."),
                    };
                    if (belts[(int)tier] is not null) throw new InvalidDataException($"Belt tier {tierName} is defined twice.");
                    var cost = Stacks(e, "cost_per_cell", id, itemIds, allowEmpty: true, max: MaxStack);
                    belts[(int)tier] = new BeltDef(tier, id, cost);
                    beltIds.Add(id, tier);
                }
            }

            var research = new List<ResearchDef>();
            var researchIds = new Dictionary<string, int>();
            if (root.TryGetProperty("research", out _))
            {
                var pending = new List<(JsonElement Element, string Id)>();
                foreach (var e in Array(root, "research"))
                {
                    RequireObject(e, "research node", "id", "cost", "requires", "unlocks", "abilities", "bonuses", "repeatable");
                    string id = Id(e, "research node");
                    if (!researchIds.TryAdd(id, researchIds.Count)) throw new InvalidDataException($"Duplicate research '{id}'.");
                    if (!allIds.Add(id)) throw new InvalidDataException($"Id '{id}' is used twice across machines, recipes, belts and research.");
                    pending.Add((e, id));
                }
                if (pending.Count > MaxResearch) throw new InvalidDataException($"At most {MaxResearch} research nodes are allowed.");

                foreach (var (e, id) in pending)
                {
                    var cost = Stacks(e, "cost", id, itemIds, allowEmpty: false, max: MaxCostStack);

                    var requires = new List<int>();
                    foreach (var q in Array(e, "requires"))
                    {
                        string rid = q.ValueKind == JsonValueKind.String ? q.GetString()! : throw new InvalidDataException($"Research '{id}' requires must list ids.");
                        if (!researchIds.TryGetValue(rid, out int ri)) throw new InvalidDataException($"Research '{id}' requires unknown research '{rid}'.");
                        if (ri == researchIds[id]) throw new InvalidDataException($"Research '{id}' requires itself.");
                        if (requires.Contains(ri)) throw new InvalidDataException($"Research '{id}' lists '{rid}' twice.");
                        requires.Add(ri);
                    }

                    var um = new List<int>();
                    var ur = new List<int>();
                    var ub = new List<BeltTier>();
                    foreach (var u in Array(e, "unlocks"))
                    {
                        string uid = u.ValueKind == JsonValueKind.String ? u.GetString()! : throw new InvalidDataException($"Research '{id}' unlocks must list ids.");
                        if (machineIds.TryGetValue(uid, out int mi)) { if (!um.Contains(mi)) um.Add(mi); }
                        else if (recipeIds.TryGetValue(uid, out int rec)) { if (!ur.Contains(rec)) ur.Add(rec); }
                        else if (beltIds.TryGetValue(uid, out var tier)) { if (!ub.Contains(tier)) ub.Add(tier); }
                        else throw new InvalidDataException($"Research '{id}' unlocks unknown id '{uid}'.");
                    }
                    var ua = new List<Ability>();
                    if (e.TryGetProperty("abilities", out _))
                    {
                        foreach (var a in Array(e, "abilities"))
                        {
                            var ability = (a.ValueKind == JsonValueKind.String ? a.GetString() : null) switch
                            {
                                "jump" => Ability.Jump,
                                "drone_bay" => Ability.DroneBay,
                                "shield" => Ability.Shield,
                                "sensor" => Ability.Sensor,
                                "reactor_mk2" => Ability.ReactorMk2,
                                _ => throw new InvalidDataException($"Research '{id}' lists an unknown ability."),
                            };
                            if (ua.Contains(ability)) throw new InvalidDataException($"Research '{id}' lists an ability twice.");
                            ua.Add(ability);
                        }
                    }
                    var bonuses = new List<Bonus>();
                    if (e.TryGetProperty("bonuses", out _))
                    {
                        foreach (var b in Array(e, "bonuses"))
                        {
                            RequireObject(b, "bonus", "kind", "percent");
                            var kind = Str(b, "kind", id) switch
                            {
                                "turret_damage" => BonusKind.TurretDamage,
                                "turret_rate" => BonusKind.TurretRate,
                                "machine_hp" => BonusKind.MachineHp,
                                "repair_heal" => BonusKind.RepairHeal,
                                "ship_hull" => BonusKind.ShipHull,
                                "ship_damage" => BonusKind.ShipDamage,
                                _ => throw new InvalidDataException($"Research '{id}' has an unknown bonus kind."),
                            };
                            if (bonuses.Any(x => x.Kind == kind)) throw new InvalidDataException($"Research '{id}' lists a bonus kind twice.");
                            bonuses.Add(new Bonus(kind, Int(b, "percent", id, 1, kind == BonusKind.TurretRate ? 75 : 1000)));
                        }
                    }
                    int maxLevels = 1, growth = 0;
                    if (e.TryGetProperty("repeatable", out var rep))
                    {
                        RequireObject(rep, "repeatable", "max_levels", "cost_growth_percent");
                        maxLevels = Int(rep, "max_levels", id, 2, 50);
                        growth = Int(rep, "cost_growth_percent", id, 1, 200);
                        if (bonuses.Count == 0) throw new InvalidDataException($"Research '{id}' is repeatable but has no bonuses to repeat.");
                    }
                    if (um.Count + ur.Count + ub.Count + ua.Count + bonuses.Count == 0) throw new InvalidDataException($"Research '{id}' unlocks nothing.");
                    research.Add(new ResearchDef(research.Count, id, cost, requires.ToArray(), um.ToArray(), ur.ToArray(), ub.ToArray(), ua.ToArray(), bonuses.ToArray(), maxLevels, growth));
                }

                // Requirements must form a DAG, otherwise some node could never be researched.
                for (int i = 0; i < research.Count; i++)
                {
                    var seen = new HashSet<int>();
                    var stack = new Stack<int>(research[i].Requires);
                    while (stack.Count > 0)
                    {
                        int n = stack.Pop();
                        if (n == i) throw new InvalidDataException($"Research '{research[i].Id}' depends on itself through a cycle.");
                        if (!seen.Add(n)) continue;
                        foreach (int p in research[n].Requires) stack.Push(p);
                    }
                }
            }

            GalaxyRules? galaxy = null;
            if (root.TryGetProperty("galaxy", out var g))
            {
                RequireObject(g, "galaxy", "system_count", "spacing_cells", "lanes_per_system", "system_radius_cells");
                galaxy = new GalaxyRules(
                    Int(g, "system_count", "galaxy", 2, MaxSystems),
                    Int(g, "spacing_cells", "galaxy", 100, 2000),
                    Int(g, "lanes_per_system", "galaxy", 1, 4),
                    Int(g, "system_radius_cells", "galaxy", 20, 400));
            }

            var archetypes = new List<ArchetypeDef>();
            if (root.TryGetProperty("archetypes", out _))
            {
                var archetypeIds = new HashSet<string>();
                foreach (var e in Array(root, "archetypes"))
                {
                    RequireObject(e, "archetype", "id", "weight", "home", "distance_cells", "deposits", "depth", "guaranteed");
                    string id = Id(e, "archetype");
                    if (!archetypeIds.Add(id)) throw new InvalidDataException($"Duplicate archetype '{id}'.");
                    if (!allIds.Add(id)) throw new InvalidDataException($"Id '{id}' is used twice across machines, recipes, belts, research and archetypes.");

                    bool home = false;
                    if (e.TryGetProperty("home", out var h))
                    {
                        if (h.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException($"Archetype '{id}' home must be true or false.");
                        home = h.GetBoolean();
                    }
                    int weight = Int(e, "weight", id, home ? 0 : 1, 1000);
                    var (dmin, dmax) = Range(e, "distance_cells", id, 1, 400);

                    int depthMin = 0, depthMax = 0;
                    if (e.TryGetProperty("depth", out _)) (depthMin, depthMax) = Range(e, "depth", id, 0, 32);
                    bool guaranteed = false;
                    if (e.TryGetProperty("guaranteed", out var gv))
                    {
                        if (gv.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException($"Archetype '{id}' guaranteed must be true or false.");
                        guaranteed = gv.GetBoolean();
                    }
                    if (home && (depthMax > 0 || guaranteed)) throw new InvalidDataException($"The home archetype '{id}' cannot have a depth or be guaranteed.");

                    var specs = new List<DepositSpec>();
                    foreach (var s in Array(e, "deposits"))
                    {
                        RequireObject(s, "deposit spec", "item", "count", "amount", "density");
                        string item = Str(s, "item", $"deposits of '{id}'");
                        if (!itemIds.TryGetValue(item, out int itemIndex)) throw new InvalidDataException($"Archetype '{id}' uses unknown item '{item}'.");
                        var (cmin, cmax) = Range(s, "count", id, 0, 16);
                        var (amin, amax) = Range(s, "amount", id, 1, 1_000_000);
                        int denMin = 0, denMax = 0;
                        if (s.TryGetProperty("density", out _)) (denMin, denMax) = Range(s, "density", id, DepositRules.MinDensity, DepositRules.MaxDensity);
                        specs.Add(new DepositSpec(itemIndex, cmin, cmax, amin, amax, denMin, denMax));
                    }
                    if (specs.Count == 0 || specs.Count > 8) throw new InvalidDataException($"Archetype '{id}' needs 1 to 8 deposit specs.");
                    archetypes.Add(new ArchetypeDef(archetypes.Count, id, weight, home, dmin, dmax, specs.ToArray(), depthMin, depthMax, guaranteed));
                }
            }

            ItemStack[] jumpCost = System.Array.Empty<ItemStack>();
            if (root.TryGetProperty("jump_cost", out _)) jumpCost = Stacks(root, "jump_cost", "jump_cost", itemIds, allowEmpty: true, max: MaxStack);

            if (galaxy is not null)
            {
                if (archetypes.Count(a => a.IsHome) != 1) throw new InvalidDataException("A galaxy needs exactly one home archetype.");
                if (!archetypes.Any(a => !a.IsHome && a.Weight > 0)) throw new InvalidDataException("A galaxy needs at least one non-home archetype with a weight.");
                if (!archetypes.Any(a => !a.IsHome && a.Weight > 0 && !a.HasDepthLimit)) throw new InvalidDataException("A galaxy needs a non-home archetype with a weight and no depth limit.");
                foreach (var a in archetypes)
                    if (a.DistanceMax > galaxy.SystemRadiusCells) throw new InvalidDataException($"Archetype '{a.Id}' places deposits outside the system radius.");
            }
            else if (archetypes.Count > 0) throw new InvalidDataException("Archetypes need a galaxy section.");

            ThreatRules? threat = null;
            if (root.TryGetProperty("threat", out var th))
            {
                RequireObject(th, "threat", "alert_max", "signature_threshold", "decay_per_tick", "alert_after_wave", "warning_seconds",
                    "node_distance_cells", "max_enemies", "ship_hull", "ship_weapon", "respawn_seconds", "module_regen_seconds", "first_wave_grace_seconds", "structure_ship_damage_percent");
                if (!th.TryGetProperty("ship_weapon", out var sw)) throw new InvalidDataException("'ship_weapon' in threat must be an object.");
                RequireObject(sw, "ship_weapon", "range_cells", "damage", "interval_ticks");
                int alertMax = Int(th, "alert_max", "threat", 100, 10_000_000);
                threat = new ThreatRules(
                    alertMax,
                    Int(th, "signature_threshold", "threat", 0, 100_000),
                    Int(th, "decay_per_tick", "threat", 0, 100_000),
                    Int(th, "alert_after_wave", "threat", 0, alertMax - 1),
                    Int(th, "warning_seconds", "threat", 1, 600) * Units.TicksPerSecond,
                    Int(th, "node_distance_cells", "threat", 10, 140),
                    Int(th, "max_enemies", "threat", 1, 200),
                    Int(th, "ship_hull", "threat", 1, 100_000),
                    Int(sw, "range_cells", "ship_weapon", 1, 100),
                    Int(sw, "damage", "ship_weapon", 1, 10_000),
                    Int(sw, "interval_ticks", "ship_weapon", 1, 600),
                    Int(th, "respawn_seconds", "threat", 1, 600) * Units.TicksPerSecond,
                    th.TryGetProperty("module_regen_seconds", out _) ? Int(th, "module_regen_seconds", "threat", 1, 3600) * Units.TicksPerSecond : ThreatRules.DefaultModuleRegenTicks,
                    th.TryGetProperty("first_wave_grace_seconds", out _) ? Int(th, "first_wave_grace_seconds", "threat", 0, 36_000) * Units.TicksPerSecond : 0,
                    th.TryGetProperty("structure_ship_damage_percent", out _) ? Int(th, "structure_ship_damage_percent", "threat", 100, 1000) : 100);
            }

            ModuleRules? modules = null;
            if (root.TryGetProperty("modules", out var mod))
            {
                RequireObject(mod, "modules", "core_cost", "hull_cost", "growth_percent");
                modules = new ModuleRules(
                    Stacks(mod, "core_cost", "modules", itemIds, allowEmpty: false, max: MaxStack),
                    Stacks(mod, "hull_cost", "modules", itemIds, allowEmpty: false, max: MaxStack),
                    Int(mod, "growth_percent", "modules", 0, 1000));
            }

            StructureContent? structures = null;
            if (root.TryGetProperty("structures", out var st)) structures = LoadStructures(st, itemIds);

            StructureTemplate? starterShip = null;
            if (root.TryGetProperty("structure_templates", out var tpl))
            {
                RequireObject(tpl, "structure_templates", "starter_ship");
                if (tpl.TryGetProperty("starter_ship", out var ss))
                {
                    if (structures is null) throw new InvalidDataException("'structure_templates' needs a 'structures' section.");
                    starterShip = LoadTemplate(ss, "structure_templates.starter_ship", structures, machines, recipes, beltIds, itemIds);
                }
            }

            var enemies = new List<EnemyDef>();
            var enemyIds = new Dictionary<string, int>();
            if (root.TryGetProperty("enemies", out _))
            {
                foreach (var e in Array(root, "enemies"))
                {
                    RequireObject(e, "enemy", "id", "hp", "speed", "damage", "range_cells", "interval_ticks");
                    string id = Id(e, "enemy");
                    if (!enemyIds.TryAdd(id, enemies.Count)) throw new InvalidDataException($"Duplicate enemy '{id}'.");
                    if (!allIds.Add(id)) throw new InvalidDataException($"Id '{id}' is used twice across machines, recipes, belts, research, archetypes and enemies.");
                    enemies.Add(new EnemyDef(enemies.Count, id, Int(e, "hp", id, 1, 100_000), Int(e, "speed", id, 1, 200),
                        Int(e, "damage", id, 1, 10_000), Int(e, "range_cells", id, 1, 50), Int(e, "interval_ticks", id, 1, 600)));
                }
            }

            var waves = new List<WaveSpec>();
            if (root.TryGetProperty("waves", out _))
            {
                foreach (var e in Array(root, "waves"))
                {
                    RequireObject(e, "wave", "enemy", "base", "per_wave", "from_wave");
                    string enemyId = Str(e, "enemy", "a wave");
                    if (!enemyIds.TryGetValue(enemyId, out int enemyIndex)) throw new InvalidDataException($"A wave uses unknown enemy '{enemyId}'.");
                    int b = Int(e, "base", "a wave", 0, 100), pw = Int(e, "per_wave", "a wave", 0, 100);
                    int from = e.TryGetProperty("from_wave", out _) ? Int(e, "from_wave", "a wave", 0, 1000) : 0;
                    if (b + pw == 0) throw new InvalidDataException($"A wave of '{enemyId}' would never contain any.");
                    waves.Add(new WaveSpec(enemyIndex, b, pw, from));
                }
            }

            if (threat is not null)
            {
                if (enemies.Count == 0 || waves.Count == 0) throw new InvalidDataException("A threat section needs enemies and waves.");
                if (!machines.Any(m => m.Role == MachineRole.Turret)) throw new InvalidDataException("A threat section needs at least one turret machine to defend with.");
            }
            else if (enemies.Count > 0 || waves.Count > 0) throw new InvalidDataException("Enemies and waves need a threat section.");

            return new ContentDb(items, machines, recipes, belts, research, galaxy, archetypes, jumpCost, threat, enemies, waves, modules, structures, starterShip);
        }
    }

    public const int MaxSystems = 64;

    private static StructureContent LoadStructures(JsonElement st, Dictionary<string, int> itemIds)
    {
        var names = Enum.GetValues<ModuleKind>().Select(ModuleKindIds.Id).ToArray();
        RequireObject(st, "structures", names);
        var kinds = new ModuleKindDef[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            if (!st.TryGetProperty(names[i], out var k))
            {
                // Kinds added after the first release are optional so older content still loads; an absent kind cannot be placed.
                if (i >= (int)ModuleKind.Shield) { kinds[i] = new ModuleKindDef((ModuleKind)i, System.Array.Empty<ItemStack>(), 0, System.Array.Empty<MachineRole>(), false, 0, 0); continue; }
                throw new InvalidDataException($"'structures' needs an entry for '{names[i]}'.");
            }
            string owner = "structures." + names[i];
            RequireObject(k, owner, "cost", "hp", "allows", "hold_capacity", "thrust_speed_percent", "power_output", "burn_interval_ticks",
                "drone_range_cells", "drone_interval_ticks", "power_use", "defence_bonus_percent", "emergency_power", "regen_hp",
                "shield_percent", "shield_stack_percent", "shield_max_percent", "sensor_range_cells");
            var kindValue = (ModuleKind)i;
            bool isReactor = ModuleKindIds.IsReactor(kindValue);
            bool isBay = kindValue == ModuleKind.DroneBay;
            bool isShield = kindValue == ModuleKind.Shield;
            bool isSensor = kindValue == ModuleKind.Sensor;
            if (!isBay && (k.TryGetProperty("drone_range_cells", out _) || k.TryGetProperty("drone_interval_ticks", out _)))
                throw new InvalidDataException($"'drone_range_cells' and 'drone_interval_ticks' only belong to the drone bay, not {owner}.");
            if (!isBay && !isShield && !isSensor && k.TryGetProperty("power_use", out _))
                throw new InvalidDataException($"'power_use' only belongs to the drone bay, shield and sensor, not {owner}.");
            if (!isShield && (k.TryGetProperty("shield_percent", out _) || k.TryGetProperty("shield_stack_percent", out _) || k.TryGetProperty("shield_max_percent", out _)))
                throw new InvalidDataException($"'shield_percent', 'shield_stack_percent' and 'shield_max_percent' only belong to the shield, not {owner}.");
            if (!isSensor && k.TryGetProperty("sensor_range_cells", out _))
                throw new InvalidDataException($"'sensor_range_cells' only belongs to the sensor, not {owner}.");
            int droneRange = isBay ? (k.TryGetProperty("drone_range_cells", out _) ? Int(k, "drone_range_cells", owner, 1, 10_000) : 60) : 0;
            int droneInterval = isBay ? (k.TryGetProperty("drone_interval_ticks", out _) ? Int(k, "drone_interval_ticks", owner, 1, 1_000_000) : 90) : 0;
            int bayPower = isBay || isShield || isSensor ? (k.TryGetProperty("power_use", out _) ? Int(k, "power_use", owner, 0, 100_000) : isBay ? 20 : 0) : 0;
            int shieldPercent = isShield ? (k.TryGetProperty("shield_percent", out _) ? Int(k, "shield_percent", owner, 0, 100) : 30) : 0;
            int shieldStack = isShield ? (k.TryGetProperty("shield_stack_percent", out _) ? Int(k, "shield_stack_percent", owner, 0, 100) : 10) : 0;
            int shieldMax = isShield ? (k.TryGetProperty("shield_max_percent", out _) ? Int(k, "shield_max_percent", owner, 0, 100) : 60) : 0;
            int sensorRange = isSensor ? (k.TryGetProperty("sensor_range_cells", out _) ? Int(k, "sensor_range_cells", owner, 1, 10_000) : 120) : 0;
            if (!isReactor && (k.TryGetProperty("power_output", out _) || k.TryGetProperty("burn_interval_ticks", out _) || k.TryGetProperty("emergency_power", out _)))
                throw new InvalidDataException($"'power_output', 'burn_interval_ticks' and 'emergency_power' only belong to the reactors, not {owner}.");
            int powerOutput = isReactor ? (k.TryGetProperty("power_output", out _) ? Int(k, "power_output", owner, 0, 100_000) : 100) : 0;
            int burnInterval = isReactor ? (k.TryGetProperty("burn_interval_ticks", out _) ? Int(k, "burn_interval_ticks", owner, 1, 1_000_000) : 900) : 0;
            int emergency = isReactor ? (k.TryGetProperty("emergency_power", out _) ? Int(k, "emergency_power", owner, 0, 100_000) : 15) : 0;
            bool isDefence =(ModuleKind)i == ModuleKind.Defence;
            if (!isDefence && k.TryGetProperty("defence_bonus_percent", out _))
                throw new InvalidDataException($"'defence_bonus_percent' only belongs to the defence module, not {owner}.");
            int defenceBonus = isDefence ? (k.TryGetProperty("defence_bonus_percent", out _) ? Int(k, "defence_bonus_percent", owner, 0, 100) : 10) : 0;
            var roles = new List<MachineRole>();
            bool belts = false;
            if (!k.TryGetProperty("allows", out var al) || al.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"'allows' in {owner} must be an array.");
            foreach (var a in al.EnumerateArray())
            {
                string? s = a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (s == "belt")
                {
                    if (belts) throw new InvalidDataException($"'allows' in {owner} lists 'belt' twice.");
                    belts = true;
                    continue;
                }
                MachineRole role = s switch
                {
                    "crafter" => MachineRole.Crafter,
                    "drill" => MachineRole.Drill,
                    "lab" => MachineRole.Lab,
                    "depot" => MachineRole.Depot,
                    "turret" => MachineRole.Turret,
                    "wall" => MachineRole.Wall,
                    "repairer" => MachineRole.Repairer,
                    "unloader" => MachineRole.Unloader,
                    _ => throw new InvalidDataException($"'allows' in {owner} has an unknown entry '{s}'."),
                };
                if (roles.Contains(role)) throw new InvalidDataException($"'allows' in {owner} lists '{s}' twice.");
                roles.Add(role);
            }
            kinds[i] = new ModuleKindDef((ModuleKind)i,
                Stacks(k, "cost", owner, itemIds, allowEmpty: false, max: MaxCostStack),
                Int(k, "hp", owner, 1, 100_000), roles.ToArray(), belts,
                Int(k, "hold_capacity", owner, 0, 100_000), Int(k, "thrust_speed_percent", owner, 0, 10_000), powerOutput, burnInterval, droneRange, droneInterval, bayPower, defenceBonus, emergency,
                k.TryGetProperty("regen_hp", out _) ? Int(k, "regen_hp", owner, 1, 1000) : 1,
                shieldPercent, shieldStack, shieldMax, sensorRange);
        }
        return new StructureContent(kinds);
    }

    private static int[] Cell(JsonElement e, string name, string owner)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3)
            throw new InvalidDataException($"'{name}' in {owner} must be [x, y, z].");
        var r = new int[3];
        int i = 0;
        foreach (var n in v.EnumerateArray())
        {
            if (n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out r[i]))
                throw new InvalidDataException($"'{name}' in {owner} must hold integers.");
            i++;
        }
        return r;
    }

    /// <summary>Loads and validates a pre-built structure: modules face-connected, machines and belts inside modules that allow them,
    /// no two things in one cell, belts wired to machines they touch.</summary>
    private static StructureTemplate LoadTemplate(JsonElement t, string owner, StructureContent kinds, List<MachineDef> machineDefs,
        List<RecipeDef> recipeDefs, Dictionary<string, BeltTier> beltIds, Dictionary<string, int> itemIds)
    {
        RequireObject(t, owner, "modules", "machines", "belts", "hold");
        var kindNames = Enum.GetValues<ModuleKind>().ToDictionary(ModuleKindIds.Id);
        var modules = new List<TemplateModule>();
        var moduleKinds = new Dictionary<(int, int, int), ModuleKind>();
        foreach (var e in Array(t, "modules"))
        {
            RequireObject(e, "template module", "at", "kind");
            var at = Cell(e, "at", owner);
            string kn = Str(e, "kind", owner);
            if (!kindNames.TryGetValue(kn, out var kind)) throw new InvalidDataException($"{owner} uses unknown module kind '{kn}'.");
            if (!StructureGrid.IsValidLocalModule(at[0], at[1], at[2])) throw new InvalidDataException($"{owner} has a module beyond the local grid.");
            if (!moduleKinds.TryAdd((at[0], at[1], at[2]), kind)) throw new InvalidDataException($"{owner} lists a module twice.");
            modules.Add(new TemplateModule(at[0], at[1], at[2], kind));
        }
        if (modules.Count == 0) throw new InvalidDataException($"{owner} needs at least one module.");
        var seen = new HashSet<(int, int, int)> { (modules[0].Mx, modules[0].My, modules[0].Mz) };
        var queue = new Queue<(int, int, int)>(seen);
        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1) })
                if (moduleKinds.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
        }
        if (seen.Count != modules.Count) throw new InvalidDataException($"The modules of {owner} are not one face-connected piece.");

        bool InModule(int x, int y, int z, out ModuleKind kind) =>
            moduleKinds.TryGetValue(StationGrid.ModuleOfCell(x, y, z), out kind);

        var cells = new HashSet<(int, int, int)>();
        var machines = new List<TemplateMachine>();
        var names = new Dictionary<string, int>();
        foreach (var e in Array(t, "machines"))
        {
            RequireObject(e, "template machine", "id", "machine", "at", "recipe");
            string name = Str(e, "id", owner);
            if (name.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')))
                throw new InvalidDataException($"{owner} has an invalid machine name '{name}'.");
            if (!names.TryAdd(name, machines.Count)) throw new InvalidDataException($"{owner} uses the machine name '{name}' twice.");
            string mid = Str(e, "machine", owner);
            int type = machineDefs.FindIndex(m => m.Id == mid);
            if (type < 0) throw new InvalidDataException($"{owner} uses unknown machine '{mid}'.");
            var role = machineDefs[type].Role;
            if (role == MachineRole.Ship) throw new InvalidDataException($"{owner} cannot place the ship machine '{mid}'.");
            int recipe = -1;
            if (e.TryGetProperty("recipe", out var rv) && rv.ValueKind != JsonValueKind.Null)
            {
                string rid = Str(e, "recipe", owner);
                recipe = recipeDefs.FindIndex(r => r.Id == rid);
                if (recipe < 0) throw new InvalidDataException($"{owner} uses unknown recipe '{rid}'.");
                if (recipeDefs[recipe].Machine != type) throw new InvalidDataException($"{owner} gives '{name}' the recipe '{rid}' of another machine.");
            }
            var at = Cell(e, "at", owner);
            if (!InModule(at[0], at[1], at[2], out var kind) || !kinds.AllowsRole(kind, role))
                throw new InvalidDataException($"Machine '{name}' of {owner} is not inside a module that allows it.");
            if (!cells.Add((at[0], at[1], at[2]))) throw new InvalidDataException($"{owner} puts two things in the cell of '{name}'.");
            machines.Add(new TemplateMachine(name, at[0], at[1], at[2], type, recipe));
        }

        var belts = new List<TemplateBelt>();
        var outputs = new int[machines.Count];
        foreach (var e in Array(t, "belts"))
        {
            RequireObject(e, "template belt", "at", "dir", "length", "tier", "from", "to");
            var at = Cell(e, "at", owner);
            Dir dir = Str(e, "dir", owner) switch
            {
                "+x" => Dir.PosX, "-x" => Dir.NegX, "+y" => Dir.PosY, "-y" => Dir.NegY, "+z" => Dir.PosZ, "-z" => Dir.NegZ,
                _ => throw new InvalidDataException($"{owner} has a belt with an unknown dir (use +x, -x, +y, -y, +z or -z)."),
            };
            int length = Int(e, "length", owner, 1, SimWorld.MaxSegmentCells);
            string tn = Str(e, "tier", owner);
            var tier = tn switch
            {
                "mk1" => BeltTier.Mk1, "mk2" => BeltTier.Mk2, "mk3" => BeltTier.Mk3,
                _ => throw new InvalidDataException($"{owner} has a belt with an unknown tier '{tn}'."),
            };
            var (dx, dy, dz) = Dirs.Vector(dir);
            for (int k = 0; k < length; k++)
            {
                int x = at[0] + dx * k, y = at[1] + dy * k, z = at[2] + dz * k;
                if (!StructureGrid.InLocalBounds(x) || !StructureGrid.InLocalBounds(y) || !StructureGrid.InLocalBounds(z)
                    || !InModule(x, y, z, out var kind) || !kinds.AllowsBelts(kind))
                    throw new InvalidDataException($"A belt of {owner} is not inside modules that allow belts.");
                if (!cells.Add((x, y, z))) throw new InvalidDataException($"{owner} puts two things in one belt cell.");
            }
            int lx = at[0] + dx * (length - 1), ly = at[1] + dy * (length - 1), lz = at[2] + dz * (length - 1);
            int Link(string key, int cx, int cy, int cz)
            {
                if (!e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return -1;
                string n = Str(e, key, owner);
                if (!names.TryGetValue(n, out int mi)) throw new InvalidDataException($"{owner} links a belt to unknown machine '{n}'.");
                var m = machines[mi];
                if (Math.Abs(m.X - cx) + Math.Abs(m.Y - cy) + Math.Abs(m.Z - cz) != 1)
                    throw new InvalidDataException($"A belt of {owner} is linked to '{n}' but does not touch it.");
                return mi;
            }
            int from = Link("from", at[0], at[1], at[2]), to = Link("to", lx, ly, lz);
            if (from >= 0)
            {
                if (machineDefs[machines[from].Type].Role is MachineRole.Wall or MachineRole.Repairer)
                    throw new InvalidDataException($"{owner}: '{machines[from].Name}' cannot feed a belt.");
                if (++outputs[from] > 2) throw new InvalidDataException($"{owner}: '{machines[from].Name}' feeds more than two belts.");
            }
            if (to >= 0 && machineDefs[machines[to].Type].Role is MachineRole.Wall or MachineRole.Unloader)
                throw new InvalidDataException($"{owner}: '{machines[to].Name}' cannot take items from a belt.");
            belts.Add(new TemplateBelt(at[0], at[1], at[2], dir, length, tier, from, to));
        }
        var hold = t.TryGetProperty("hold", out _) ? Stacks(t, "hold", owner, itemIds, allowEmpty: true, max: MaxCostStack) : System.Array.Empty<ItemStack>();
        long capacity = moduleKinds.Values.Sum(k => (long)kinds.HoldCapacity(k)), held = hold.Sum(s => (long)s.Count);
        if (held > capacity) throw new InvalidDataException($"The starting hold of {owner} ({held} items) is larger than its cargo capacity ({capacity}).");
        return new StructureTemplate(modules.ToArray(), machines.ToArray(), belts.ToArray(), hold);
    }

    private static int Int(JsonElement e, string name, string owner, int min, int max)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n) || n < min || n > max)
            throw new InvalidDataException($"'{name}' in {owner} must be an integer from {min} to {max}.");
        return n;
    }

    private static (int Min, int Max) Range(JsonElement e, string name, string owner, int min, int max)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2)
            throw new InvalidDataException($"'{name}' in {owner} must be [min, max].");
        var items = v.EnumerateArray().ToArray();
        if (items.Any(i => i.ValueKind != JsonValueKind.Number || !i.TryGetInt32(out _)))
            throw new InvalidDataException($"'{name}' in {owner} must hold integers.");
        int a = items[0].GetInt32(), b = items[1].GetInt32();
        if (a < min || b > max || a > b) throw new InvalidDataException($"'{name}' in {owner} must satisfy {min} <= min <= max <= {max}.");
        return (a, b);
    }

    public static ContentDb FromFile(string path) => FromJson(File.ReadAllText(path));

    private static JsonElement.ArrayEnumerator Array(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"'{name}' must be an array.");
        return a.EnumerateArray();
    }

    private static void RequireObject(JsonElement e, string what, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Each {what} must be an object.");
        foreach (var p in e.EnumerateObject())
            if (System.Array.IndexOf(allowed, p.Name) < 0) throw new InvalidDataException($"Unknown property '{p.Name}' in {what}.");
    }

    private static string Id(JsonElement e, string what)
    {
        string id = Str(e, "id", what);
        int colon = id.IndexOf(':');
        if (colon <= 0 || colon == id.Length - 1 || id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or ':')) || id.Count(c => c == ':') != 1)
            throw new InvalidDataException($"Invalid {what} id '{id}' (expected pack:name in lower case).");
        return id;
    }

    private static string Str(JsonElement e, string name, string context)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(v.GetString()))
            throw new InvalidDataException($"'{name}' must be a non-empty string in {context}.");
        return v.GetString()!;
    }

    private static ItemStack[] Stacks(JsonElement owner, string name, string ownerId, Dictionary<string, int> itemIds, bool allowEmpty, int max)
    {
        var list = new List<ItemStack>();
        foreach (var s in Array(owner, name))
        {
            RequireObject(s, "stack", "item", "count");
            string item = Str(s, "item", $"{name} of '{ownerId}'");
            if (!itemIds.TryGetValue(item, out int index))
                throw new InvalidDataException($"'{ownerId}' uses unknown item '{item}'.");
            if (!s.TryGetProperty("count", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out int count) || count < 1 || count > max)
                throw new InvalidDataException($"'{ownerId}' count must be 1 to {max}.");
            if (list.Any(x => x.Item == index))
                throw new InvalidDataException($"'{ownerId}' lists '{item}' twice in {name}.");
            list.Add(new ItemStack(index, count));
        }
        if (list.Count > MaxRecipeStacks) throw new InvalidDataException($"'{ownerId}' has more than {MaxRecipeStacks} entries in {name}.");
        if (list.Count == 0 && !allowEmpty) throw new InvalidDataException($"'{ownerId}' needs at least one entry in {name} (an output or a cost).");
        return list.ToArray();
    }

    private ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; }
        }
        void MixStr(string s)
        {
            Mix(s.Length);
            foreach (char c in s) Mix(c);
        }
        void MixStacks(ItemStack[] stacks)
        {
            Mix(stacks.Length);
            foreach (var s in stacks) { Mix(s.Item); Mix(s.Count); }
        }

        Mix(Items.Count);
        foreach (var i in Items) MixStr(i.Id);
        Mix(Machines.Count);
        foreach (var m in Machines)
        {
            MixStr(m.Id); Mix((int)m.Role); MixStacks(m.Cost); Mix(m.SandboxOnly ? 1 : 0); Mix(m.Hp); Mix(m.Signature);
            if (m.Turret is { } t) { Mix(t.RangeCells); Mix(t.Damage); Mix(t.IntervalTicks); Mix(t.AmmoItem); Mix(t.AmmoCap); }
            if (m.Repairer is { } rp) { Mix(rp.RangeCells); Mix(rp.Heal); Mix(rp.IntervalTicks); Mix(rp.Item); Mix(rp.ItemCap); }
            if (m.Unloader is { } un) { Mix(un.Item); Mix(un.IntervalTicks); Mix(un.Reserve); }
            else Mix(-1);
            if (m.PowerUse != 0) Mix(m.PowerUse); // mixed only when set so content without power hashes as before
        }
        Mix(Recipes.Count);
        foreach (var r in Recipes)
        {
            MixStr(r.Id); Mix(r.Machine); Mix(r.TimeTicks);
            MixStacks(r.Inputs);
            MixStacks(r.Outputs);
        }
        foreach (var b in Belts)
        {
            if (b is null) { Mix(-1); continue; }
            MixStr(b.Id); Mix((int)b.Tier); MixStacks(b.CostPerCell);
        }
        Mix(Research.Count);
        foreach (var r in Research)
        {
            MixStr(r.Id); MixStacks(r.Cost);
            Mix(r.Requires.Length); foreach (int q in r.Requires) Mix(q);
            Mix(r.UnlockMachines.Length); foreach (int q in r.UnlockMachines) Mix(q);
            Mix(r.UnlockRecipes.Length); foreach (int q in r.UnlockRecipes) Mix(q);
            Mix(r.UnlockBelts.Length); foreach (var q in r.UnlockBelts) Mix((int)q);
            Mix(r.UnlockAbilities.Length); foreach (var q in r.UnlockAbilities) Mix((int)q);
            Mix(r.Bonuses.Length); foreach (var q in r.Bonuses) { Mix((int)q.Kind); Mix(q.Percent); }
            Mix(r.MaxLevels); Mix(r.CostGrowthPercent);
        }
        if (Galaxy is { } g)
        {
            Mix(g.SystemCount); Mix(g.SpacingCells); Mix(g.LanesPerSystem); Mix(g.SystemRadiusCells);
        }
        else Mix(-1);
        Mix(Archetypes.Count);
        foreach (var a in Archetypes)
        {
            MixStr(a.Id); Mix(a.Weight); Mix(a.IsHome ? 1 : 0); Mix(a.DistanceMin); Mix(a.DistanceMax);
            Mix(a.Deposits.Length);
            foreach (var d in a.Deposits)
            {
                Mix(d.Item); Mix(d.CountMin); Mix(d.CountMax); Mix(d.AmountMin); Mix(d.AmountMax);
                if (d.HasDensity) { Mix(d.DensityMin); Mix(d.DensityMax); } // absent for legacy content, which hashes as before
            }
            if (a.HasDepthLimit || a.Guaranteed) { Mix(a.DepthMin); Mix(a.DepthMax); Mix(a.Guaranteed ? 1 : 0); }
        }
        MixStacks(JumpCost);
        if (Modules is { } mo) { MixStacks(mo.CoreCost); MixStacks(mo.HullCost); Mix(mo.GrowthPercent); }
        else Mix(-1);
        if (Structures is { } sc)
        {
            foreach (var k in sc.Kinds)
            {
                if (!sc.IsDefined(k.Kind)) continue; // content that predates a module kind hashes as it did before
                Mix((int)k.Kind); MixStacks(k.Cost); Mix(k.Hp);
                Mix(k.AllowedRoles.Length); foreach (var r in k.AllowedRoles) Mix((int)r);
                Mix(k.AllowsBelts ? 1 : 0); Mix(k.HoldCapacity); Mix(k.ThrustSpeedPercent);
                if (k.PowerOutput != 0 || k.BurnIntervalTicks != 0) { Mix(k.PowerOutput); Mix(k.BurnIntervalTicks); }
                if (ModuleKindIds.IsReactor(k.Kind) && k.EmergencyPower != 15) Mix(k.EmergencyPower); // 15 is the default
                if (k.DroneIntervalTicks != 0) { Mix(k.DroneRangeCells); Mix(k.DroneIntervalTicks); Mix(k.PowerUse); }
                if (k.Kind is ModuleKind.Shield or ModuleKind.Sensor) { Mix(k.PowerUse); Mix(k.ShieldPercent); Mix(k.ShieldStackPercent); Mix(k.ShieldMaxPercent); Mix(k.SensorRangeCells); }
                if (k.Kind == ModuleKind.Defence && k.DefenceBonusPercent != 10) Mix(k.DefenceBonusPercent); // 10 is the default
                if (k.RegenHp != 1) Mix(k.RegenHp); // 1 is the default
            }
        }
        else Mix(-1);
        if (StarterShip is { } ship)
        {
            Mix(ship.Modules.Length);
            foreach (var m in ship.Modules) { Mix(m.Mx); Mix(m.My); Mix(m.Mz); Mix((int)m.Kind); }
            Mix(ship.Machines.Length);
            foreach (var m in ship.Machines) { MixStr(m.Name); Mix(m.X); Mix(m.Y); Mix(m.Z); Mix(m.Type); Mix(m.Recipe); }
            Mix(ship.Belts.Length);
            foreach (var b in ship.Belts) { Mix(b.X); Mix(b.Y); Mix(b.Z); Mix((int)b.Dir); Mix(b.Length); Mix((int)b.Tier); Mix(b.From); Mix(b.To); }
            if (ship.Hold.Length > 0) MixStacks(ship.Hold);
        }
        else Mix(-1);
        if (Threat is { } th)
        {
            Mix(th.AlertMax); Mix(th.SignatureThreshold); Mix(th.DecayPerTick); Mix(th.AlertAfterWave); Mix(th.WarningTicks);
            Mix(th.NodeDistanceCells); Mix(th.MaxEnemies); Mix(th.ShipHull); Mix(th.ShipWeaponRangeCells); Mix(th.ShipWeaponDamage);
            Mix(th.ShipWeaponIntervalTicks); Mix(th.RespawnTicks);
            if (th.ModuleRegenTicks != ThreatRules.DefaultModuleRegenTicks || th.FirstWaveGraceTicks != 0) { Mix(th.ModuleRegenTicks); Mix(th.FirstWaveGraceTicks); }
            if (th.StructureShipDamagePercent != 100) Mix(th.StructureShipDamagePercent);
        }
        else Mix(-1);
        Mix(Enemies.Count);
        foreach (var e in Enemies) { MixStr(e.Id); Mix(e.Hp); Mix(e.Speed); Mix(e.Damage); Mix(e.RangeCells); Mix(e.IntervalTicks); }
        Mix(Waves.Count);
        foreach (var w in Waves) { Mix(w.Enemy); Mix(w.Base); Mix(w.PerWave); Mix(w.FromWave); }
        return h;
    }
}
