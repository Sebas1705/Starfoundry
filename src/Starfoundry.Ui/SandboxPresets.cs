using System.Globalization;
using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>What a sandbox or custom game starts with besides the ship.</summary>
public enum SandboxStart { Ship, ShipAndStation, ShipAndSiege }

/// <summary>A starting stack, by item id so a stored preset does not depend on the content's item order.</summary>
public sealed record SandboxItem(string Item, int Count);

/// <summary>
/// The custom rules form of the Sandbox tab: every value the player can set. <see cref="ToRules"/> turns it into <see cref="WorldRules"/>
/// (clamped to their ranges); <see cref="Clamped"/> forces every value into its range. Engine-free.
/// </summary>
public sealed record SandboxSettings
{
    public const int MaxItems = 12, MaxCount = 999;

    /// <summary>The items the form offers for the starting hold (ids; the ones missing from the content are ignored).</summary>
    public static readonly IReadOnlyList<string> OfferedItems = new[]
    {
        "core:iron_plate", "core:iron_ingot", "core:hydrogen", "core:carbon", "core:ice", "core:titanium_plate",
        "core:nickel_plate", "core:circuit", "core:silicon", "core:ammo",
    };

    public bool FreeBuild { get; init; } = true;
    public bool Threats { get; init; }
    public int EnemyDamagePercent { get; init; } = 100;
    public int EnemyHpPercent { get; init; } = 100;
    public int WaveSizePercent { get; init; } = 100;
    public int WaveIntervalPercent { get; init; } = 100;
    /// <summary>Seconds before the first wave, or -1 for the content's value.</summary>
    public int FirstWaveGraceSeconds { get; init; } = WorldRules.ContentGrace;
    public int DepositRichnessPercent { get; init; } = 100;
    public int PowerOutputPercent { get; init; } = 100;
    public int GoalWaves { get; init; } = 10;
    public SandboxStart Start { get; init; } = SandboxStart.Ship;
    /// <summary>0 means a random galaxy.</summary>
    public ulong Seed { get; init; }
    public IReadOnlyList<SandboxItem> Items { get; init; } = Array.Empty<SandboxItem>();

    public SandboxSettings Clamped() => this with
    {
        EnemyDamagePercent = Math.Clamp(EnemyDamagePercent, WorldRules.MinPercent, WorldRules.MaxPercent),
        EnemyHpPercent = Math.Clamp(EnemyHpPercent, WorldRules.MinPercent, WorldRules.MaxPercent),
        WaveSizePercent = Math.Clamp(WaveSizePercent, WorldRules.MinPercent, WorldRules.MaxPercent),
        WaveIntervalPercent = Math.Clamp(WaveIntervalPercent, WorldRules.MinPercent, WorldRules.MaxPercent),
        FirstWaveGraceSeconds = FirstWaveGraceSeconds < 0 ? WorldRules.ContentGrace : Math.Clamp(FirstWaveGraceSeconds, WorldRules.MinGraceSeconds, WorldRules.MaxGraceSeconds),
        DepositRichnessPercent = Math.Clamp(DepositRichnessPercent, WorldRules.MinRichness, WorldRules.MaxRichness),
        PowerOutputPercent = Math.Clamp(PowerOutputPercent, WorldRules.MinPercent, WorldRules.MaxPercent),
        GoalWaves = Math.Clamp(GoalWaves, WorldRules.MinGoalWaves, WorldRules.MaxGoalWaves),
        Start = Enum.IsDefined(Start) ? Start : SandboxStart.Ship,
        Seed = Seed > 999_999_999UL ? 0 : Seed,
        Items = CleanItems(Items),
    };

    private static List<SandboxItem> CleanItems(IEnumerable<SandboxItem> items)
    {
        var merged = new List<SandboxItem>();
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Item) || it.Item.Length > 64 || it.Count < 1) continue;
            int at = merged.FindIndex(m => m.Item == it.Item);
            int count = Math.Min(MaxCount, it.Count + (at >= 0 ? merged[at].Count : 0));
            if (at >= 0) merged[at] = new SandboxItem(it.Item, count);
            else if (merged.Count < MaxItems) merged.Add(new SandboxItem(it.Item, Math.Min(MaxCount, it.Count)));
        }
        return merged;
    }

    /// <summary>The world rules for this form; starting items the content does not know are dropped.</summary>
    public WorldRules ToRules(ContentDb? content = null)
    {
        var s = Clamped();
        var stacks = new List<ItemStack>();
        foreach (var it in s.Items)
        {
            if (content is null) continue;
            int index = -1;
            for (int k = 0; k < content.Items.Count; k++) if (content.Items[k].Id == it.Item) index = k;
            if (index >= 0) stacks.Add(new ItemStack(index, it.Count));
        }
        return new WorldRules
        {
            EnemyDamagePercent = s.EnemyDamagePercent, EnemyHpPercent = s.EnemyHpPercent, WaveSizePercent = s.WaveSizePercent,
            WaveIntervalPercent = s.WaveIntervalPercent, FirstWaveGraceSeconds = s.FirstWaveGraceSeconds,
            DepositRichnessPercent = s.DepositRichnessPercent, PowerOutputPercent = s.PowerOutputPercent, GoalWaves = s.GoalWaves,
            ThreatsEnabled = s.Threats, FreeBuild = s.FreeBuild, StartingItems = stacks,
        }.Clamped(content);
    }

    /// <summary>The scenario this form starts from.</summary>
    public string ScenarioId => ScenarioKinds.OpenEndedId(FreeBuild, Start);
}

/// <summary>A named, saved sandbox form.</summary>
public sealed record SandboxPreset(string Name, SandboxSettings Settings);

/// <summary>
/// The named sandbox presets of <c>user://sandbox_presets.cfg</c>: validation of names, saving over a name, removing, and the text form
/// (<see cref="CfgText"/>; one <c>[preset N]</c> section each, the name inside). A bad entry is skipped, never fatal. Engine-free.
/// </summary>
public sealed class SandboxPresets
{
    public const int MaxPresets = 24, MaxNameLength = 32;

    private readonly List<SandboxPreset> _presets = new();

    public IReadOnlyList<SandboxPreset> All => _presets;

    /// <summary>The trimmed name, or null when it is empty, too long or has control characters.</summary>
    public static string? CleanName(string? name)
    {
        string n = (name ?? "").Trim();
        return n.Length == 0 || n.Length > MaxNameLength || n.Any(char.IsControl) ? null : n;
    }

    public SandboxPreset? Find(string name) =>
        _presets.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Stores the settings under the name, replacing a preset of the same name (case-insensitive). False when the name is bad or the list is full.</summary>
    public bool Save(string name, SandboxSettings settings)
    {
        if (CleanName(name) is not { } clean) return false;
        var preset = new SandboxPreset(clean, settings.Clamped());
        int at = _presets.FindIndex(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) { _presets[at] = preset; return true; }
        if (_presets.Count >= MaxPresets) return false;
        _presets.Add(preset);
        return true;
    }

    public bool Remove(string name)
    {
        int at = _presets.FindIndex(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (at < 0) return false;
        _presets.RemoveAt(at);
        return true;
    }

    // ---- text form ---------------------------------------------------------------------------

    public string Serialize()
    {
        var sb = new StringBuilder("; Starfoundry sandbox presets\n");
        int n = 0;
        foreach (var p in _presets)
        {
            var s = p.Settings;
            sb.Append("[preset ").Append(++n).Append("]\n");
            sb.Append("name=").Append(CfgText.Quote(p.Name)).Append('\n');
            void Int(string key, long v) => sb.Append(key).Append('=').Append(v.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("free_build=").Append(s.FreeBuild ? "true" : "false").Append('\n');
            sb.Append("threats=").Append(s.Threats ? "true" : "false").Append('\n');
            Int("enemy_damage", s.EnemyDamagePercent); Int("enemy_hp", s.EnemyHpPercent); Int("wave_size", s.WaveSizePercent);
            Int("wave_interval", s.WaveIntervalPercent); Int("first_wave_grace", s.FirstWaveGraceSeconds);
            Int("richness", s.DepositRichnessPercent); Int("power", s.PowerOutputPercent); Int("goal_waves", s.GoalWaves);
            sb.Append("start=").Append(s.Start switch { SandboxStart.ShipAndStation => "station", SandboxStart.ShipAndSiege => "siege", _ => "ship" }).Append('\n');
            Int("seed", (long)s.Seed);
            sb.Append("items=").Append(CfgText.Quote(string.Join(',', s.Items.Select(i => i.Item + ":" + i.Count.ToString(CultureInfo.InvariantCulture))))).Append("\n\n");
        }
        return sb.ToString();
    }

    public static SandboxPresets Parse(string? text)
    {
        var result = new SandboxPresets();
        foreach (var section in CfgText.Parse(text))
        {
            if (!section.Name.StartsWith("preset", StringComparison.Ordinal)) continue;
            if (CleanName(section.Get("name")) is not { } name) continue;
            int I(string key, int fallback) => int.TryParse(section.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
            bool B(string key, bool fallback) => bool.TryParse(section.Get(key), out bool v) ? v : fallback;
            var items = new List<SandboxItem>();
            foreach (string part in (section.Get("items") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int colon = part.LastIndexOf(':');
                if (colon > 0 && int.TryParse(part[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                    items.Add(new SandboxItem(part[..colon], count));
            }
            var d = new SandboxSettings();
            var settings = new SandboxSettings
            {
                FreeBuild = B("free_build", d.FreeBuild), Threats = B("threats", d.Threats),
                EnemyDamagePercent = I("enemy_damage", 100), EnemyHpPercent = I("enemy_hp", 100), WaveSizePercent = I("wave_size", 100),
                WaveIntervalPercent = I("wave_interval", 100), FirstWaveGraceSeconds = I("first_wave_grace", WorldRules.ContentGrace),
                DepositRichnessPercent = I("richness", 100), PowerOutputPercent = I("power", 100), GoalWaves = I("goal_waves", 10),
                Start = section.Get("start") switch { "station" => SandboxStart.ShipAndStation, "siege" => SandboxStart.ShipAndSiege, _ => SandboxStart.Ship },
                Seed = ulong.TryParse(section.Get("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed) ? seed : 0,
                Items = items,
            }.Clamped();
            result.Save(name, settings);
        }
        return result;
    }
}
