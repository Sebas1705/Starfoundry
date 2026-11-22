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
    public const int MinGraceSeconds = 0, MaxGraceSeconds = 3600;
    public const int MinGoalWaves = 1, MaxGoalWaves = 30;
    public const int MaxStartingStacks = 64, MaxStackCount = 9999;

    /// <summary>The grace value that means "use the content's first wave grace".</summary>
    public const int ContentGrace = -1;

    private const byte FormatVersion = 1;

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

    /// <summary>Waves to survive for the win (read by the outcome logic, not by the simulation).</summary>
    public int GoalWaves { get; init; } = 10;

    // ---- presets --------------------------------------------------------------------------------

    public static WorldRules Default { get; } = new();

    public static WorldRules Relaxed { get; } = new()
    {
        EnemyDamagePercent = 70, EnemyHpPercent = 80, WaveSizePercent = 70, WaveIntervalPercent = 130, FirstWaveGraceSeconds = 600,
        DepositRichnessPercent = 150, PowerOutputPercent = 110, GoalWaves = 8,
    };

    public static WorldRules Normal { get; } = new();

    public static WorldRules Hard { get; } = new()
    {
        EnemyDamagePercent = 130, EnemyHpPercent = 130, WaveSizePercent = 130, WaveIntervalPercent = 80, FirstWaveGraceSeconds = 300,
        DepositRichnessPercent = 80, PowerOutputPercent = 100, GoalWaves = 12,
    };

    public static WorldRules Brutal { get; } = new()
    {
        EnemyDamagePercent = 170, EnemyHpPercent = 170, WaveSizePercent = 170, WaveIntervalPercent = 65, FirstWaveGraceSeconds = 120,
        DepositRichnessPercent = 60, PowerOutputPercent = 90, GoalWaves = 15,
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

    /// <summary>A copy with <see cref="LegacyLaser"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyLaser(bool legacy) => new()
    {
        LegacyGalaxy = LegacyGalaxy,
        EnemyDamagePercent = EnemyDamagePercent, EnemyHpPercent = EnemyHpPercent, WaveSizePercent = WaveSizePercent, WaveIntervalPercent = WaveIntervalPercent,
        FirstWaveGraceSeconds = FirstWaveGraceSeconds, DepositRichnessPercent = DepositRichnessPercent, PowerOutputPercent = PowerOutputPercent,
        GoalWaves = GoalWaves, ThreatsEnabled = ThreatsEnabled, FreeBuild = FreeBuild, LegacyLaser = legacy, StartingItems = _startingItems,
    };

    /// <summary>A copy with <see cref="LegacyGalaxy"/> set to <paramref name="legacy"/>.</summary>
    public WorldRules WithLegacyGalaxy(bool legacy) => new()
    {
        EnemyDamagePercent = EnemyDamagePercent, EnemyHpPercent = EnemyHpPercent, WaveSizePercent = WaveSizePercent, WaveIntervalPercent = WaveIntervalPercent,
        FirstWaveGraceSeconds = FirstWaveGraceSeconds, DepositRichnessPercent = DepositRichnessPercent, PowerOutputPercent = PowerOutputPercent,
        GoalWaves = GoalWaves, ThreatsEnabled = ThreatsEnabled, FreeBuild = FreeBuild, LegacyLaser = LegacyLaser, LegacyGalaxy = legacy, StartingItems = _startingItems,
    };

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
    /// flags byte (bit 0 threats, bit 1 free build, bit 2 legacy laser, bit 3 legacy galaxy), the stack count and the stacks (item, count). Little endian.
    /// </summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FormatVersion);
            w.Write(EnemyDamagePercent); w.Write(EnemyHpPercent); w.Write(WaveSizePercent); w.Write(WaveIntervalPercent);
            w.Write(FirstWaveGraceSeconds); w.Write(DepositRichnessPercent); w.Write(PowerOutputPercent); w.Write(GoalWaves);
            w.Write((byte)((ThreatsEnabled ? 1 : 0) | (FreeBuild ? 2 : 0) | (LegacyLaser ? 4 : 0) | (LegacyGalaxy ? 8 : 0)));
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
            if (version != FormatVersion) throw new InvalidDataException($"Unsupported world rules version {version}.");
            int damage = r.ReadInt32(), hp = r.ReadInt32(), size = r.ReadInt32(), interval = r.ReadInt32(), grace = r.ReadInt32(),
                richness = r.ReadInt32(), power = r.ReadInt32(), goal = r.ReadInt32();
            byte flags = r.ReadByte();
            if (flags > 15) throw new InvalidDataException($"Unknown world rules flags {flags}.");
            int count = r.ReadInt32();
            if (count < 0 || count > MaxStartingStacks) throw new InvalidDataException("Bad starting stack count.");
            var stacks = new ItemStack[count];
            for (int i = 0; i < count; i++) stacks[i] = new ItemStack(r.ReadInt32(), r.ReadInt32());
            if (ms.Position != ms.Length) throw new InvalidDataException("Trailing bytes after the world rules.");
            var rules = new WorldRules
            {
                EnemyDamagePercent = damage, EnemyHpPercent = hp, WaveSizePercent = size, WaveIntervalPercent = interval,
                FirstWaveGraceSeconds = grace, DepositRichnessPercent = richness, PowerOutputPercent = power, GoalWaves = goal,
                ThreatsEnabled = (flags & 1) != 0, FreeBuild = (flags & 2) != 0, LegacyLaser = (flags & 4) != 0, LegacyGalaxy = (flags & 8) != 0, StartingItems = stacks,
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
        && PowerOutputPercent == other.PowerOutputPercent && ThreatsEnabled == other.ThreatsEnabled && FreeBuild == other.FreeBuild && LegacyLaser == other.LegacyLaser && LegacyGalaxy == other.LegacyGalaxy
        && GoalWaves == other.GoalWaves && _startingItems.AsSpan().SequenceEqual(other._startingItems);

    public override bool Equals(object? obj) => Equals(obj as WorldRules);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(EnemyDamagePercent); h.Add(EnemyHpPercent); h.Add(WaveSizePercent); h.Add(WaveIntervalPercent); h.Add(FirstWaveGraceSeconds);
        h.Add(DepositRichnessPercent); h.Add(PowerOutputPercent); h.Add(ThreatsEnabled); h.Add(FreeBuild); h.Add(LegacyLaser); h.Add(LegacyGalaxy); h.Add(GoalWaves);
        foreach (var s in _startingItems) { h.Add(s.Item); h.Add(s.Count); }
        return h.ToHashCode();
    }

    public static bool operator ==(WorldRules? a, WorldRules? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(WorldRules? a, WorldRules? b) => !(a == b);

    public override string ToString() =>
        PresetId() is { } id ? $"WorldRules({id})" : $"WorldRules(dmg {EnemyDamagePercent}%, hp {EnemyHpPercent}%, size {WaveSizePercent}%, interval {WaveIntervalPercent}%, grace {FirstWaveGraceSeconds}, richness {DepositRichnessPercent}%, power {PowerOutputPercent}%, goal {GoalWaves}, threats {ThreatsEnabled}, free {FreeBuild}, items {_startingItems.Length})";
}
