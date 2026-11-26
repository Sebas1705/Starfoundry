using System.Text;

namespace Starfoundry.Sim;

/// <summary>
/// The rules of one world: integer percentages and flags that scale the game. They are chosen when the world is created and stored
/// in the command-log header ([ADR 0023](docs/adr/0023-world-rules-and-difficulty.md)), so replays and saves reproduce them. The
/// defaults equal the game as it was before the rules existed, so a world with default rules hashes and plays exactly as before.
/// Immutable; compare with <see cref="Equals(WorldRules)"/>.
/// </summary>
public sealed class WorldRules : IEquatable<WorldRules>
{
    public const int MinPercent = 25, MaxPercent = 300;
    public const int MinRichness = 25, MaxRichness = 400;
    public const int MinDensityPercent = 50, MaxDensityPercent = 300;
    public const int MinGraceSeconds = 0, MaxGraceSeconds = 3600;
    public const int MinGoalWaves = 1, MaxGoalWaves = 30;
    public const int MaxStartingStacks = 64, MaxStackCount = 9999;

    /// <summary>The grace value that means "use the content's first wave grace".</summary>
    public const int ContentGrace = -1;

    private const byte FormatVersion = 1;

    /// <summary>The format that adds a second flags byte (bit 0 legacy feel, bit 1 legacy resources) and the resource density. Written only when one of them is not at its old value, so older rules keep their bytes.</summary>
    private const byte FormatVersionDensity = 2;

    private readonly ItemStack[] _startingItems = Array.Empty<ItemStack>();

    /// <summary>Damage dealt by every enemy shot, in percent of the content value.</summary>
    public int EnemyDamagePercent { get; init; } = 100;

    /// <summary>Hit points of every enemy at spawn, in percent of the content value.</summary>
    public int EnemyHpPercent { get; init; } = 100;

    /// <summary>Enemies per wave, in percent of the content's wave table (rounded half up, at least one of a type that has any).</summary>
    public int WaveSizePercent { get; init; } = 100;

    /// <summary>Time between waves: the alert meter's capacity, in percent (100 is the content's pace; higher is slower).</summary>
    public int WaveIntervalPercent { get; init; } = 100;

    /// <summary>Seconds before the first wave of a system may be warned (structure worlds), or <see cref="ContentGrace"/> for the content's value.</summary>
    public int FirstWaveGraceSeconds { get; init; } = ContentGrace;

    /// <summary>Size of every generated deposit, in percent. The starter ship's items are not affected.</summary>
    public int DepositRichnessPercent { get; init; } = 100;

    /// <summary>Output of burning reactors, in percent. The emergency trickle of an empty reactor is not affected.</summary>
    public int PowerOutputPercent { get; init; } = 100;

    /// <summary>Stacks added to the starter hold at creation, in order, as far as the hold has room.</summary>
    public IReadOnlyList<ItemStack> StartingItems
    {
        get => _startingItems;
        init => _startingItems = value is null ? Array.Empty<ItemStack>() : value.Select(s => new ItemStack(s.Item, s.Count)).ToArray();
    }

    /// <summary>False: no waves ever come (in addition to the world's own threats setting).</summary>
    public bool ThreatsEnabled { get; init; } = true;

    /// <summary>True: placing machines, belts and modules and founding stations costs nothing, and removing them refunds nothing.</summary>
    public bool FreeBuild { get; init; }

    /// <summary>
    /// True: the ship's mining laser draws no power, as before ADR 0027. Logs older than version 9 read with this on, so they replay as
    /// they were recorded; every new world leaves it off. Not a difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyLaser { get; init; }

    /// <summary>
    /// True: the galaxy is generated as before ADR 0028 (close deposits, no obstacles, no star types). Logs older than version 10 read with this on, so
    /// they replay as they were recorded; every new world leaves it off. Not a difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyGalaxy { get; init; }

    /// <summary>
    /// True: machines and belts may be built anywhere inside a module that allows them, at any layer, as before ADR 0031 (the new rule builds on the floor of a
    /// fabrication bay only). Logs older than version 13 read with this on, so they replay as they were recorded; every new world leaves it off. Not a
    /// difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyBuilding { get; init; }

    /// <summary>
    /// True: a ship needs no hardpoint modules: its mining laser and gun are implicit, as before ADR 0032 (one laser at 100 percent measured from the ship's
    /// reference point, one gun at the content's damage). Logs older than version 14 read with this on, so they replay as they were recorded; every new world
    /// leaves it off, so a ship with no laser module cannot mine. Not a difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyHardpoints { get; init; }

    /// <summary>
    /// True: rocks are hit by their nominal radius only, deposits are not solid and ships carry no collision padding, as before ADR 0033 (the content's
    /// collision padding, hit sizes and solid deposits are ignored). Logs older than version 15 read with this on, so they replay as they were recorded;
    /// every new world leaves it off. Not a difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyCollisions { get; init; }

    /// <summary>
    /// True: the ship's velocity follows the thrust at once, as before ADR 0033 (no acceleration limit, no inertia, no engine research bonus). Logs older
    /// than version 15 read with this on, so they replay as they were recorded; every new world leaves it off. Not a difficulty setting: it is not in the
    /// presets or the new-game screen.
    /// </summary>
    public bool LegacyFlight { get; init; }

    /// <summary>
    /// True: guns need a target to fire (no aim vector, no projectiles, no recoil) and hand mining stops at the hold limit, as before ADR 0036. Logs older than
    /// version 17 read with this on, so they replay as they were recorded; every new world leaves it off. Not a difficulty setting: it is not in the presets or
    /// the new-game screen.
    /// </summary>
    public bool LegacyFeel { get; init; }

    /// <summary>
    /// True: the galaxy gets no extra deposits and emptied deposits never regrow, as before ADR 0036 (<see cref="ResourceDensityPercent"/> is ignored). Logs older
    /// than version 17 read with this on, so they replay as they were recorded; every new world leaves it off. Not a difficulty setting: it is not in the presets
    /// or the new-game screen.
    /// </summary>
    public bool LegacyResources { get; init; }
    /// <summary>
    /// True: the player has no personal inventory, reactors have no fuel slot and no auto-feed setting, and the commands that use them are refused, as before ADR 0035. Logs older
    /// than version 18 read with this on, so they replay as they were recorded; every new world leaves it off. Bit 2 of the second flags byte. Not a difficulty setting.
    /// </summary>
    public bool LegacyInventory { get; init; }

    /// <summary>
    /// True: the world has no infinite rock field and keeps the finite cube, as before ADR 0040. Logs older than version 21 read with this on, so they replay as they were
    /// recorded; every new world leaves it off. Bit 4 of the second flags byte. Not a difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyField { get; init; }

    /// <summary>
    /// True: the antenna variants and the heavy cannon do not exist (placing them is refused) and the scanner and the navigation display ignore antennas, as before ADR 0042. Logs older
    /// than version 23 read with this on, so they replay as recorded; every new world leaves it off. Bit 6 of the second flags byte. Not a difficulty setting.
    /// </summary>
    public bool LegacyArms { get; init; }

    /// <summary>
    /// True: ships dock through the core, cockpit and dock-module ports and templates add no dock module, as before ADR 0041. Off (the default): only dock modules are
    /// ports, the starter ship and the shipyard's designs must have one and a station is founded with one. Logs older than version 22 read with this on, so they replay
    /// as recorded; every new world leaves it off. Bit 5 (32) of the second flags byte. Not a difficulty setting: it is not in the presets or the new-game screen.
    /// </summary>
    public bool LegacyDock { get; init; }

    /// <summary>
    /// How many deposits the generated galaxy holds, in percent of the generator's base count (ADR 0036): 140 adds about 40 percent more deposits, richer ones
    /// near home and small ore-bearing rocks beside belts and clusters. 100 generates the base galaxy. Ignored with <see cref="LegacyResources"/>.
    /// </summary>
    public int ResourceDensityPercent { get; init; } = DefaultDensityPercent;

    /// <summary>The density of default rules and of the Normal preset: 40 percent more deposits than the base generator makes. A world read from an old log has the flag instead (the density is ignored).</summary>
    public const int DefaultDensityPercent = 140;

    /// <summary>Waves to survive for the win (read by the outcome logic, not by the simulation).</summary>
    public int GoalWaves { get; init; } = 10;

    // ---- presets --------------------------------------------------------------------------------

    public static WorldRules Default { get; } = new();

    public static WorldRules Relaxed { get; } = new()
    {
        EnemyDamagePercent = 70, EnemyHpPercent = 80, WaveSizePercent = 70, WaveIntervalPercent = 130, FirstWaveGraceSeconds = 600,
        DepositRichnessPercent = 150, PowerOutputPercent = 110, GoalWaves = 8, ResourceDensityPercent = 150,
    };

    public static WorldRules Normal { get; } = new();

    public static WorldRules Hard { get; } = new()
    {
        EnemyDamagePercent = 120, EnemyHpPercent = 120, WaveSizePercent = 120, WaveIntervalPercent = 80, FirstWaveGraceSeconds = 300,
        DepositRichnessPercent = 80, PowerOutputPercent = 100, GoalWaves = 13, ResourceDensityPercent = 120,
    };

    public static WorldRules Brutal { get; } = new()
    {
        EnemyDamagePercent = 170, EnemyHpPercent = 170, WaveSizePercent = 170, WaveIntervalPercent = 65, FirstWaveGraceSeconds = 120,
        DepositRichnessPercent = 60, PowerOutputPercent = 90, GoalWaves = 15, ResourceDensityPercent = 100,
    };

    /// <summary>The named presets in order of difficulty, with their stable ids (<c>relaxed</c>, <c>normal</c>, <c>hard</c>, <c>brutal</c>).</summary>
    public static IReadOnlyList<(string Id, WorldRules Rules)> Presets { get; } =
        new[] { ("relaxed", Relaxed), ("normal", Normal), ("hard", Hard), ("brutal", Brutal) };

    /// <summary>The preset with this id (case-insensitive), or null.</summary>
    public static WorldRules? Preset(string id)
    {
        foreach (var (pid, rules) in Presets)
            if (string.Equals(pid, id, StringComparison.OrdinalIgnoreCase)) return rules;
        return null;
    }

    /// <summary>The id of the preset these rules equal, or null for custom rules.</summary>
    public string? PresetId()
    {
        foreach (var (id, rules) in Presets)
            if (Equals(rules)) return id;
        return null;
    }

    /// <summary>True when these rules equal <see cref="Default"/>: the world then behaves and hashes as before the rules existed.</summary>
    public bool IsDefault => Equals(Default);

    private WorldRules Flags(bool laser, bool galaxy, bool building, bool hardpoints, bool collisions, bool flight, bool? feel = null, bool? resources = null, bool? inventory = null, bool? field = null, bool? arms = null, bool? dock = null) => new()
    {
        LegacyInventory = inventory ?? LegacyInventory, LegacyField = field ?? LegacyField, LegacyArms = arms ?? LegacyArms, LegacyDock = dock ?? LegacyDock,
        LegacyFeel = feel ?? LegacyFeel, LegacyResources = resources ?? LegacyResources, ResourceDensityPercent = ResourceDensityPercent,
        EnemyDamagePercent = EnemyDamagePercent, EnemyHpPercent = EnemyHpPercent, WaveSizePercent = WaveSizePercent, WaveIntervalPercent = WaveIntervalPercent,
        FirstWaveGraceSeconds = FirstWaveGraceSeconds, DepositRichnessPercent = DepositRichnessPercent, PowerOutputPercent = PowerOutputPercent,
        GoalWaves = GoalWaves, ThreatsEnabled = ThreatsEnabled, FreeBuild = FreeBuild, LegacyLaser = laser, LegacyGalaxy = galaxy, LegacyBuilding = building,
        LegacyHardpoints = hardpoints, LegacyCollisions = collisions, LegacyFlight = flight, StartingItems = _startingItems,
    };

    /// <summary>A copy with <see cref="LegacyLaser"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyLaser(bool legacy) => Flags(legacy, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight);

    /// <summary>A copy with <see cref="LegacyGalaxy"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyGalaxy(bool legacy) => Flags(LegacyLaser, legacy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight);

    /// <summary>A copy with <see cref="LegacyHardpoints"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyHardpoints(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, legacy, LegacyCollisions, LegacyFlight);

    /// <summary>A copy with <see cref="LegacyBuilding"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyBuilding(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, legacy, LegacyHardpoints, LegacyCollisions, LegacyFlight);

    /// <summary>A copy with <see cref="LegacyCollisions"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyCollisions(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, legacy, LegacyFlight);

    /// <summary>A copy with <see cref="LegacyFlight"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyFlight(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, legacy);

    /// <summary>A copy with <see cref="LegacyFeel"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyFeel(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight, feel: legacy);

    /// <summary>A copy with <see cref="LegacyResources"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyResources(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight, resources: legacy);
    /// <summary>A copy with <see cref="LegacyInventory"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyInventory(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight, inventory: legacy);

    /// <summary>A copy with <see cref="LegacyArms"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyArms(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight, arms: legacy);

    /// <summary>A copy with <see cref="LegacyDock"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyDock(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight, dock: legacy);

    /// <summary>A copy with <see cref="LegacyField"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyField(bool legacy) => Flags(LegacyLaser, LegacyGalaxy, LegacyBuilding, LegacyHardpoints, LegacyCollisions, LegacyFlight, field: legacy);

    /// <summary>Whether this world hashes as default rules (nothing is mixed into its checksum): default rules and rules that differ only by the legacy flags, as the worlds before ADR 0036 did.</summary>
    public bool HashesAsDefault => LegacyResources ? WithoutLegacyFlags().WithResourceDensity(DefaultDensityPercent).IsDefault : WithoutLegacyFlags().IsDefault;

    /// <summary>The bytes mixed into the checksum of a world that does not <see cref="HashesAsDefault"/>: the form the rules had before the density existed for a legacy world (so its checksums do not change), else <see cref="Serialize"/> without the legacy flags.</summary>
    public byte[] HashBytes() => LegacyResources ? WithoutLegacyFlags().Serialize(withDensity: false) : WithoutLegacyFlags().Serialize();

    /// <summary>A copy with another <see cref="ResourceDensityPercent"/>.</summary>
    public WorldRules WithResourceDensity(int percent) => new()
    {
        EnemyDamagePercent = EnemyDamagePercent, EnemyHpPercent = EnemyHpPercent, WaveSizePercent = WaveSizePercent, WaveIntervalPercent = WaveIntervalPercent,
        FirstWaveGraceSeconds = FirstWaveGraceSeconds, DepositRichnessPercent = DepositRichnessPercent, PowerOutputPercent = PowerOutputPercent,
        GoalWaves = GoalWaves, ThreatsEnabled = ThreatsEnabled, FreeBuild = FreeBuild, LegacyLaser = LegacyLaser, LegacyGalaxy = LegacyGalaxy, LegacyBuilding = LegacyBuilding,
        LegacyHardpoints = LegacyHardpoints, LegacyCollisions = LegacyCollisions, LegacyFlight = LegacyFlight, LegacyFeel = LegacyFeel, LegacyResources = LegacyResources, LegacyInventory = LegacyInventory, LegacyField = LegacyField, LegacyArms = LegacyArms, LegacyDock = LegacyDock,
        ResourceDensityPercent = percent, StartingItems = _startingItems,
    };

    /// <summary>A copy with every legacy flag cleared (the density is kept): what the world hashes as, so a log that only differs by them (versions before 9, 10, 13, 14, 15 or 17) hashes as default rules (see <see cref="HashesAsDefault"/>).</summary>
    public WorldRules WithoutLegacyFlags() => Flags(false, false, false, false, false, false, false, false, false, field: false, arms: false, dock: false);

    /// <summary>True when any legacy flag is on.</summary>
    public bool AnyLegacy => LegacyLaser || LegacyGalaxy || LegacyBuilding || LegacyHardpoints || LegacyCollisions || LegacyFlight || LegacyFeel || LegacyResources || LegacyInventory || LegacyField || LegacyArms || LegacyDock;

    // ---- validation -----------------------------------------------------------------------------

    /// <summary>The first problem with these rules, or null when they are valid. Pass the content to also check the starting items.</summary>
    public string? Validate(ContentDb? content = null)
    {
        static bool In(int v, int lo, int hi) => v >= lo && v <= hi;
        if (!In(EnemyDamagePercent, MinPercent, MaxPercent)) return $"enemy damage percent must be {MinPercent}..{MaxPercent}";
        if (!In(EnemyHpPercent, MinPercent, MaxPercent)) return $"enemy hp percent must be {MinPercent}..{MaxPercent}";
        if (!In(WaveSizePercent, MinPercent, MaxPercent)) return $"wave size percent must be {MinPercent}..{MaxPercent}";
        if (!In(WaveIntervalPercent, MinPercent, MaxPercent)) return $"wave interval percent must be {MinPercent}..{MaxPercent}";
        if (FirstWaveGraceSeconds != ContentGrace && !In(FirstWaveGraceSeconds, MinGraceSeconds, MaxGraceSeconds))
            return $"first wave grace must be {ContentGrace} (content default) or {MinGraceSeconds}..{MaxGraceSeconds} seconds";
        if (!In(DepositRichnessPercent, MinRichness, MaxRichness)) return $"deposit richness percent must be {MinRichness}..{MaxRichness}";
        if (!In(PowerOutputPercent, MinPercent, MaxPercent)) return $"power output percent must be {MinPercent}..{MaxPercent}";
        if (!In(GoalWaves, MinGoalWaves, MaxGoalWaves)) return $"goal waves must be {MinGoalWaves}..{MaxGoalWaves}";
        if (!In(ResourceDensityPercent, MinDensityPercent, MaxDensityPercent)) return $"resource density percent must be {MinDensityPercent}..{MaxDensityPercent}";
        if (_startingItems.Length > MaxStartingStacks) return $"at most {MaxStartingStacks} starting stacks";
        foreach (var s in _startingItems)
        {
            if (s.Item < 0 || (content is not null && s.Item >= content.Items.Count)) return $"unknown starting item {s.Item}";
            if (!In(s.Count, 1, MaxStackCount)) return $"starting stack counts must be 1..{MaxStackCount}";
        }
        return null;
    }

    /// <summary>A copy with every value forced into its valid range (invalid stacks are dropped). For settings coming from a UI.</summary>
    public WorldRules Clamped(ContentDb? content = null) => new()
    {
        EnemyDamagePercent = Math.Clamp(EnemyDamagePercent, MinPercent, MaxPercent),
        EnemyHpPercent = Math.Clamp(EnemyHpPercent, MinPercent, MaxPercent),
        WaveSizePercent = Math.Clamp(WaveSizePercent, MinPercent, MaxPercent),
        WaveIntervalPercent = Math.Clamp(WaveIntervalPercent, MinPercent, MaxPercent),
        FirstWaveGraceSeconds = FirstWaveGraceSeconds < 0 ? ContentGrace : Math.Clamp(FirstWaveGraceSeconds, MinGraceSeconds, MaxGraceSeconds),
        DepositRichnessPercent = Math.Clamp(DepositRichnessPercent, MinRichness, MaxRichness),
        PowerOutputPercent = Math.Clamp(PowerOutputPercent, MinPercent, MaxPercent),
        GoalWaves = Math.Clamp(GoalWaves, MinGoalWaves, MaxGoalWaves),
        ThreatsEnabled = ThreatsEnabled,
        FreeBuild = FreeBuild,
        LegacyLaser = LegacyLaser,
        LegacyGalaxy = LegacyGalaxy,
        LegacyBuilding = LegacyBuilding, LegacyHardpoints = LegacyHardpoints, LegacyCollisions = LegacyCollisions, LegacyFlight = LegacyFlight,
        LegacyFeel = LegacyFeel, LegacyResources = LegacyResources, LegacyInventory = LegacyInventory, LegacyField = LegacyField, LegacyArms = LegacyArms, LegacyDock = LegacyDock, ResourceDensityPercent = Math.Clamp(ResourceDensityPercent, MinDensityPercent, MaxDensityPercent),
        StartingItems = _startingItems
            .Where(s => s.Item >= 0 && (content is null || s.Item < content.Items.Count) && s.Count >= 1)
            .Select(s => new ItemStack(s.Item, Math.Min(s.Count, MaxStackCount)))
            .Take(MaxStartingStacks).ToArray(),
    };

    // ---- integer scaling (used by the simulation) -------------------------------------------------

    /// <summary>value * percent / 100, rounded half up.</summary>
    public static int Scale(int value, int percent) => (int)(((long)value * percent + 50) / 100);

    /// <summary>Like <see cref="Scale"/> but at least 1 when the value is at least 1.</summary>
    public static int ScaleMin1(int value, int percent) => value < 1 ? Math.Max(0, value) : Math.Max(1, Scale(value, percent));

    // ---- serialisation --------------------------------------------------------------------------

    /// <summary>
    /// The stable binary form: a format byte, the eight integers (damage, hp, wave size, interval, grace, richness, power, goal), a
    /// flags byte (bit 0 threats, bit 1 free build, bit 2 legacy laser, bit 3 legacy galaxy, bit 4 legacy building, bit 5 legacy hardpoints, bit 6 legacy collisions, bit 7 legacy flight), the stack count and the stacks (item, count). Little endian.
    /// </summary>
    public byte[] Serialize() => Serialize(withDensity: true);

    private byte[] Serialize(bool withDensity)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bool extended = withDensity && (LegacyFeel || LegacyResources || LegacyInventory || LegacyField || LegacyArms || LegacyDock || ResourceDensityPercent != 100);
            w.Write(extended ? FormatVersionDensity : FormatVersion);
            w.Write(EnemyDamagePercent); w.Write(EnemyHpPercent); w.Write(WaveSizePercent); w.Write(WaveIntervalPercent);
            w.Write(FirstWaveGraceSeconds); w.Write(DepositRichnessPercent); w.Write(PowerOutputPercent); w.Write(GoalWaves);
            w.Write((byte)((ThreatsEnabled ? 1 : 0) | (FreeBuild ? 2 : 0) | (LegacyLaser ? 4 : 0) | (LegacyGalaxy ? 8 : 0) | (LegacyBuilding ? 16 : 0) | (LegacyHardpoints ? 32 : 0) | (LegacyCollisions ? 64 : 0) | (LegacyFlight ? 128 : 0)));
            if (extended) { w.Write((byte)((LegacyFeel ? 1 : 0) | (LegacyResources ? 2 : 0) | (LegacyInventory ? 4 : 0) | (LegacyField ? 16 : 0) | (LegacyArms ? 64 : 0) | (LegacyDock ? 32 : 0))); w.Write(ResourceDensityPercent); }
            w.Write(_startingItems.Length);
            foreach (var s in _startingItems) { w.Write(s.Item); w.Write(s.Count); }
        }
        return ms.ToArray();
    }

    /// <summary>Reads the form written by <see cref="Serialize"/>.</summary>
    /// <exception cref="InvalidDataException">The data is truncated, has trailing bytes, an unknown version or out-of-range values.</exception>
    public static WorldRules Deserialize(ReadOnlySpan<byte> data)
    {
        try
        {
            using var ms = new MemoryStream(data.ToArray());
            using var r = new BinaryReader(ms, Encoding.UTF8);
            byte version = r.ReadByte();
            if (version != FormatVersion && version != FormatVersionDensity) throw new InvalidDataException($"Unsupported world rules version {version}.");
            int damage = r.ReadInt32(), hp = r.ReadInt32(), size = r.ReadInt32(), interval = r.ReadInt32(), grace = r.ReadInt32(),
                richness = r.ReadInt32(), power = r.ReadInt32(), goal = r.ReadInt32();
            byte flags = r.ReadByte();
            byte flags2 = 0;
            int density = 100;
            if (version == FormatVersionDensity)
            {
                flags2 = r.ReadByte();
                density = r.ReadInt32();
                if ((flags2 & ~(7 | 16 | 32 | 64)) != 0) throw new InvalidDataException("Unknown world rules flags.");
            }
            int count = r.ReadInt32();
            if (count < 0 || count > MaxStartingStacks) throw new InvalidDataException("Bad starting stack count.");
            var stacks = new ItemStack[count];
            for (int i = 0; i < count; i++) stacks[i] = new ItemStack(r.ReadInt32(), r.ReadInt32());
            if (ms.Position != ms.Length) throw new InvalidDataException("Trailing bytes after the world rules.");
            var rules = new WorldRules
            {
                EnemyDamagePercent = damage, EnemyHpPercent = hp, WaveSizePercent = size, WaveIntervalPercent = interval,
                FirstWaveGraceSeconds = grace, DepositRichnessPercent = richness, PowerOutputPercent = power, GoalWaves = goal,
                ThreatsEnabled = (flags & 1) != 0, FreeBuild = (flags & 2) != 0, LegacyLaser = (flags & 4) != 0, LegacyGalaxy = (flags & 8) != 0, LegacyBuilding = (flags & 16) != 0, LegacyHardpoints = (flags & 32) != 0, LegacyCollisions = (flags & 64) != 0, LegacyFlight = (flags & 128) != 0,
                LegacyFeel = (flags2 & 1) != 0, LegacyResources = (flags2 & 2) != 0, LegacyInventory = (flags2 & 4) != 0, LegacyField = (flags2 & 16) != 0, LegacyArms = (flags2 & 64) != 0, LegacyDock = (flags2 & 32) != 0, ResourceDensityPercent = density, StartingItems = stacks,
            };
            if (rules.Validate() is { } problem) throw new InvalidDataException("Invalid world rules: " + problem + ".");
            return rules;
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("The world rules are truncated.", e);
        }
    }

    // ---- equality ---------------------------------------------------------------------------------

    public bool Equals(WorldRules? other) =>
        other is not null && EnemyDamagePercent == other.EnemyDamagePercent && EnemyHpPercent == other.EnemyHpPercent
        && WaveSizePercent == other.WaveSizePercent && WaveIntervalPercent == other.WaveIntervalPercent
        && FirstWaveGraceSeconds == other.FirstWaveGraceSeconds && DepositRichnessPercent == other.DepositRichnessPercent
        && PowerOutputPercent == other.PowerOutputPercent && ThreatsEnabled == other.ThreatsEnabled && FreeBuild == other.FreeBuild && LegacyLaser == other.LegacyLaser && LegacyGalaxy == other.LegacyGalaxy && LegacyBuilding == other.LegacyBuilding && LegacyHardpoints == other.LegacyHardpoints && LegacyCollisions == other.LegacyCollisions && LegacyFlight == other.LegacyFlight && LegacyFeel == other.LegacyFeel && LegacyInventory == other.LegacyInventory && LegacyField == other.LegacyField && LegacyArms == other.LegacyArms && LegacyDock == other.LegacyDock && LegacyResources == other.LegacyResources && ResourceDensityPercent == other.ResourceDensityPercent
        && GoalWaves == other.GoalWaves && _startingItems.AsSpan().SequenceEqual(other._startingItems);

    public override bool Equals(object? obj) => Equals(obj as WorldRules);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(EnemyDamagePercent); h.Add(EnemyHpPercent); h.Add(WaveSizePercent); h.Add(WaveIntervalPercent); h.Add(FirstWaveGraceSeconds);
        h.Add(DepositRichnessPercent); h.Add(PowerOutputPercent); h.Add(ThreatsEnabled); h.Add(FreeBuild); h.Add(LegacyLaser); h.Add(LegacyGalaxy); h.Add(LegacyBuilding); h.Add(LegacyHardpoints); h.Add(LegacyCollisions); h.Add(LegacyFlight); h.Add(LegacyFeel); h.Add(LegacyResources); h.Add(LegacyInventory); h.Add(LegacyField); h.Add(LegacyArms); h.Add(LegacyDock); h.Add(ResourceDensityPercent); h.Add(GoalWaves);
        foreach (var s in _startingItems) { h.Add(s.Item); h.Add(s.Count); }
        return h.ToHashCode();
    }

    public static bool operator ==(WorldRules? a, WorldRules? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(WorldRules? a, WorldRules? b) => !(a == b);

    public override string ToString() =>
        PresetId() is { } id ? $"WorldRules({id})" : $"WorldRules(dmg {EnemyDamagePercent}%, hp {EnemyHpPercent}%, size {WaveSizePercent}%, interval {WaveIntervalPercent}%, grace {FirstWaveGraceSeconds}, richness {DepositRichnessPercent}%, power {PowerOutputPercent}%, goal {GoalWaves}, density {ResourceDensityPercent}%, threats {ThreatsEnabled}, free {FreeBuild}, items {_startingItems.Length})";
}
