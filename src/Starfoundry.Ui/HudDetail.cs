namespace Starfoundry.Ui;

/// <summary>
/// How much the HUD says, on top of the HUD mode (<see cref="HudMode"/> says which families of the overlay exist, this says how rich each one is).
/// <see cref="Standard"/> is the default and says a little more than the HUD of old; <see cref="Minimal"/> is for people who want the view;
/// <see cref="Detailed"/> adds the numbers a pilot asked for (brownout risk, damaged modules, income per minute, objective progress, three threat arrows).
/// See <c>docs/prototypes/hud-information-study.md</c>.
/// </summary>
public enum HudDetail
{
    Minimal,
    Standard,
    Detailed,
}

/// <summary>What the player is doing, as far as the HUD cares. The context decides which groups appear.</summary>
public enum HudContext
{
    Flying,
    Mining,
    Walking,
    Building,
    Docked,
    Combat,
}

/// <summary>
/// A group of the overlay: a card, a strip or a set of markers that is read as one thing. The crosshair, the alert banner and the toasts are always on and are
/// not groups. At most <see cref="HudDetails.Ceiling"/> groups show at once.
/// </summary>
[Flags]
public enum HudGroup
{
    None = 0,
    /// <summary>The ship card: hull, power, fuel, integrity.</summary>
    Vitals = 1,
    /// <summary>The flight readout: speed, throttle, the galactic frame.</summary>
    Flight = 2,
    /// <summary>The hold strip (and the tool in hand while building).</summary>
    Hold = 4,
    /// <summary>World markers and their edge arrows.</summary>
    Markers = 8,
    /// <summary>The key panel.</summary>
    Keys = 16,
    /// <summary>The target card: the nearest enemy, or the deposit being mined.</summary>
    Target = 32,
    /// <summary>The weapon and shield rows beside the target card.</summary>
    Weapons = 64,
    /// <summary>The tool in hand with its cost.</summary>
    Tool = 128,
    /// <summary>The current guided step with its progress.</summary>
    Objective = 256,
}

/// <summary>A row inside a group. The detail level adds rows to a group; it never adds a group.</summary>
[Flags]
public enum HudRow
{
    None = 0,
    Hull = 1 << 0,
    PowerGauge = 1 << 1,
    FuelTime = 1 << 2,
    Integrity = 1 << 3,
    /// <summary>"Demand above supply" or "fuel burning down": the brownout risk.</summary>
    PowerRisk = 1 << 4,
    /// <summary>How many modules are damaged.</summary>
    ModulesDamaged = 1 << 5,
    Shield = 1 << 6,
    SpeedNumber = 1 << 7,
    VelocityGizmo = 1 << 8,
    ThrottleGauge = 1 << 9,
    /// <summary>"Full in 2:10" while the hold grows.</summary>
    HoldForecast = 1 << 10,
    IncomePerMinute = 1 << 11,
    TargetDistance = 1 << 12,
    ClosingSpeed = 1 << 13,
    TargetHull = 1 << 14,
    WeaponState = 1 << 15,
    ObjectiveProgress = 1 << 16,
    /// <summary>The three nearest threats get an edge arrow instead of the nearest one.</summary>
    ThreeThreats = 1 << 17,
}

/// <summary>The rules of <see cref="HudDetail"/>, engine-free and tested.</summary>
public static class HudDetails
{
    public const HudDetail Default = HudDetail.Standard;

    /// <summary>The most groups that show at once, at any level in any context (a proposal of the HUD study: glanceable in under a second).</summary>
    public const int Ceiling = 7;

    /// <summary>The next level (the cockpit button and the settings cycle): minimal, standard, detailed, minimal...</summary>
    public static HudDetail Next(HudDetail d) => d switch
    {
        HudDetail.Minimal => HudDetail.Standard,
        HudDetail.Standard => HudDetail.Detailed,
        _ => HudDetail.Minimal,
    };

    public static string Id(HudDetail d) => d switch
    {
        HudDetail.Minimal => "minimal",
        HudDetail.Detailed => "detailed",
        _ => "standard",
    };

    /// <summary>Reads an id (any case, a few short forms); false for anything unknown.</summary>
    public static bool TryParse(string? id, out HudDetail detail)
    {
        switch ((id ?? "").Trim().ToLowerInvariant())
        {
            case "minimal" or "min": detail = HudDetail.Minimal; return true;
            case "standard" or "std": detail = HudDetail.Standard; return true;
            case "detailed" or "det" or "detail": detail = HudDetail.Detailed; return true;
            default: detail = Default; return false;
        }
    }

    /// <summary>
    /// The context from what the player is doing. A tool in hand wins, then standing (walking, or docked while standing in a docked ship), then a threat,
    /// then the dock, then the laser firing.
    /// </summary>
    public static HudContext ContextOf(bool standing, bool building, bool docked, bool threat, bool mining)
    {
        if (building) return HudContext.Building;
        if (standing) return docked ? HudContext.Docked : HudContext.Walking;
        if (threat) return HudContext.Combat;
        if (docked) return HudContext.Docked;
        return mining ? HudContext.Mining : HudContext.Flying;
    }

    /// <summary>The groups a level shows in a context (before the HUD mode takes any family away).</summary>
    public static HudGroup Groups(HudDetail detail, HudContext ctx)
    {
        const HudGroup V = HudGroup.Vitals, F = HudGroup.Flight, H = HudGroup.Hold, M = HudGroup.Markers, K = HudGroup.Keys, T = HudGroup.Target,
            W = HudGroup.Weapons, L = HudGroup.Tool, O = HudGroup.Objective;
        return (ctx, detail) switch
        {
            (HudContext.Flying, HudDetail.Minimal) => V | F | K,
            (HudContext.Flying, HudDetail.Standard) => V | F | H | M | K | T,
            (HudContext.Flying, _) => V | F | H | M | K | T | O,

            (HudContext.Mining, HudDetail.Minimal) => V | H | K,
            (HudContext.Mining, HudDetail.Standard) => V | H | M | K | T,
            (HudContext.Mining, _) => V | F | H | M | K | T | O,

            (HudContext.Walking, HudDetail.Minimal) => K,
            (HudContext.Walking, HudDetail.Standard) => M | K,
            (HudContext.Walking, _) => V | H | M | K | O,

            (HudContext.Building, HudDetail.Minimal) => L | K,
            (HudContext.Building, HudDetail.Standard) => L | H | K,
            (HudContext.Building, _) => V | L | H | K | O,

            (HudContext.Docked, HudDetail.Minimal) => K,
            (HudContext.Docked, HudDetail.Standard) => V | H | K,
            (HudContext.Docked, _) => V | H | K | O,

            (HudContext.Combat, HudDetail.Minimal) => V | M | K,
            (HudContext.Combat, HudDetail.Standard) => V | F | M | K | T | W,
            (_, _) => V | F | M | K | T | W | O,
        };
    }

    /// <summary>How many groups a set holds.</summary>
    public static int Count(HudGroup groups)
    {
        int n = 0;
        for (int g = (int)groups; g != 0; g &= g - 1) n++;
        return n;
    }

    /// <summary>The rows a level shows in a context. Only rows of the groups that show are returned.</summary>
    public static HudRow Rows(HudDetail detail, HudContext ctx)
    {
        var groups = Groups(detail, ctx);
        var rows = HudRow.None;
        if (groups.HasFlag(HudGroup.Vitals))
        {
            rows |= HudRow.Hull | HudRow.FuelTime;
            if (detail >= HudDetail.Standard) rows |= HudRow.PowerGauge | HudRow.Integrity;
            if (detail >= HudDetail.Detailed) rows |= HudRow.PowerRisk | HudRow.ModulesDamaged;
            if (ctx == HudContext.Combat && detail >= HudDetail.Standard) rows |= HudRow.Shield;
        }
        if (groups.HasFlag(HudGroup.Flight))
        {
            rows |= HudRow.SpeedNumber;
            if (detail >= HudDetail.Standard) rows |= HudRow.VelocityGizmo | HudRow.ThrottleGauge;
        }
        if (groups.HasFlag(HudGroup.Hold))
        {
            if (detail >= HudDetail.Standard) rows |= HudRow.HoldForecast;
            if (detail >= HudDetail.Detailed) rows |= HudRow.IncomePerMinute;
        }
        if (groups.HasFlag(HudGroup.Target))
        {
            rows |= HudRow.TargetDistance;
            if (detail >= HudDetail.Standard) rows |= HudRow.ClosingSpeed | HudRow.TargetHull;
        }
        if (groups.HasFlag(HudGroup.Weapons)) rows |= HudRow.WeaponState;
        if (groups.HasFlag(HudGroup.Objective)) rows |= HudRow.ObjectiveProgress;
        if (groups.HasFlag(HudGroup.Markers) && detail >= HudDetail.Detailed) rows |= HudRow.ThreeThreats;
        return rows;
    }

    /// <summary>How many threats get an edge arrow: three at the detailed level, one otherwise.</summary>
    public static int ThreatArrows(HudDetail detail) => detail >= HudDetail.Detailed ? 3 : 1;

    // ---- key panel ------------------------------------------------------------------------------------------

    /// <summary>The key rows the minimal level drops: the actions a pilot learns in the first minutes. Alerts and rows that are lit stay.</summary>
    private static readonly HashSet<string> LearnedRows = new()
    {
        "build", "research", "map", "scan", "mine", "refine", "weapons", "found", "module", "boost", "roll", "look", "console", "info", "run",
    };

    /// <summary>
    /// What the key panel shows at a level: all of it at standard and detailed; at minimal the rows of the first-minutes actions are dropped (a
    /// row that is lit or an alert stays), and a group left empty goes with them.
    /// </summary>
    public static IReadOnlyList<KeyGroup> TrimKeys(IReadOnlyList<KeyGroup> groups, HudDetail detail)
    {
        if (detail != HudDetail.Minimal) return groups;
        var result = new List<KeyGroup>();
        foreach (var g in groups)
        {
            var rows = g.Rows.Where(r => !LearnedRows.Contains(r.Id) || r.State is KeyRowState.Active or KeyRowState.Alert).ToList();
            if (rows.Count > 0) result.Add(g with { Rows = rows });
        }
        return result;
    }
}
