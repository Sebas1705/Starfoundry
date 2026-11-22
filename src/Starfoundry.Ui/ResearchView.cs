using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Where a research node stands. The order is the order the panel groups them in.</summary>
public enum ResearchState { Researching, Available, Locked, Done }

/// <summary>What a research node gives.</summary>
public enum ResearchUnlockKind { Machine, Recipe, Belt, Ability, Bonus }

/// <summary>One thing a node unlocks: the content id (or the ability or bonus name) and, for a bonus, its percentage.</summary>
public sealed record ResearchUnlock(ResearchUnlockKind Kind, string Id, int Percent = 0);

/// <summary>One item of a node's cost: what the lab needs, what it has been given, and what the hold has now.</summary>
/// <param name="Delivered">Items the lab has taken for this level (0 when the node is not being worked on).</param>
public sealed record ResearchCostLine(int Item, string ItemId, int Needed, int Delivered, int InHold)
{
    /// <summary>True when the lab already has all of this item.</summary>
    public bool Have => Delivered >= Needed;
}

/// <summary>A node a node requires, and whether it is finished.</summary>
public sealed record ResearchRequirement(int Index, string Id, bool Done);

/// <summary>Everything the panel shows about one research node.</summary>
/// <param name="Progress">Share of the cost delivered, 0 to 1 (1 for a finished node).</param>
/// <param name="Level">Levels finished; <paramref name="MaxLevels"/> is more than 1 for repeatable nodes.</param>
public sealed record ResearchEntry(int Index, string Id, ResearchState State, IReadOnlyList<ResearchCostLine> Cost, IReadOnlyList<ResearchRequirement> Requires,
    IReadOnlyList<ResearchUnlock> Unlocks, double Progress, int Level, int MaxLevels, ResearchField Field = ResearchField.Basic)
{
    public bool Repeatable => MaxLevels > 1;

    /// <summary>Can be chosen: not locked and not finished (the researching one counts, choosing it again does nothing).</summary>
    public bool CanStart => State == ResearchState.Available;

    /// <summary>The requirements still to finish, for the "locked" text.</summary>
    public IEnumerable<ResearchRequirement> Missing => Requires.Where(r => !r.Done);

    /// <summary>Key of the description in <see cref="Loc"/>.</summary>
    public string DescriptionKey => "research." + Id + ".desc";
}

/// <summary>
/// The display model of the research panel, built from the content and the world: every node with its state (researching, available,
/// locked or done), cost lines with what the lab has, requirements and what it unlocks. Pure logic, tested without the engine.
/// </summary>
public static class ResearchView
{
    /// <summary>Every node, grouped by state (researching, available, locked, done), in content order inside a group.</summary>
    public static IReadOnlyList<ResearchEntry> Build(ContentDb content, BeltNetwork net, int player)
    {
        var entries = new List<ResearchEntry>(content.Research.Count);
        foreach (var node in content.Research) entries.Add(Entry(content, net, player, node));
        return entries.OrderBy(e => (int)e.State).ThenBy(e => e.Index).ToList();
    }

    public static ResearchEntry Entry(ContentDb content, BeltNetwork net, int player, ResearchDef node)
    {
        int level = net.ResearchLevel(node.Index);
        bool done = level >= node.MaxLevels;
        var state = done ? ResearchState.Done
            : net.CurrentResearch == node.Index && net.CanResearch(node.Index) ? ResearchState.Researching
            : net.CanResearch(node.Index) ? ResearchState.Available
            : ResearchState.Locked;

        // A node being worked on (or switched away from with some progress) shows what its lab has; the others show zero.
        var cost = net.ResearchCost(node.Index);
        var lines = new List<ResearchCostLine>(cost.Length);
        long needed = 0, delivered = 0;
        foreach (var c in cost)
        {
            int got = done ? c.Count : Math.Min(c.Count, net.ResearchProgress(node.Index, c.Item));
            lines.Add(new ResearchCostLine(c.Item, content.Items[c.Item].Id, c.Count, got, net.HoldCount(player, c.Item)));
            needed += c.Count;
            delivered += got;
        }
        double progress = done ? 1.0 : needed == 0 ? 0.0 : (double)delivered / needed;

        var requires = node.Requires.Select(r => new ResearchRequirement(r, content.Research[r].Id, net.IsResearched(r))).ToList();
        return new ResearchEntry(node.Index, node.Id, state, lines, requires, Unlocks(content, node), progress, level, node.MaxLevels, node.Field);
    }

    /// <summary>Machines, recipes, belts, abilities and bonuses a node gives, in that order.</summary>
    public static IReadOnlyList<ResearchUnlock> Unlocks(ContentDb content, ResearchDef node)
    {
        var list = new List<ResearchUnlock>();
        foreach (int m in node.UnlockMachines) list.Add(new ResearchUnlock(ResearchUnlockKind.Machine, content.Machines[m].Id));
        foreach (int r in node.UnlockRecipes) list.Add(new ResearchUnlock(ResearchUnlockKind.Recipe, content.Recipes[r].Id));
        foreach (var b in node.UnlockBelts) list.Add(new ResearchUnlock(ResearchUnlockKind.Belt, "mk" + ((int)b + 1)));
        foreach (var a in node.UnlockAbilities) list.Add(new ResearchUnlock(ResearchUnlockKind.Ability, a switch
        {
            Ability.Jump => "jump",
            Ability.Shield => "shield",
            Ability.Sensor => "sensor",
            Ability.ReactorMk2 => "reactor_mk2",
            _ => "drone_bay",
        }));
        foreach (var b in node.Bonuses) list.Add(new ResearchUnlock(ResearchUnlockKind.Bonus, BonusId(b.Kind), b.Percent));
        return list;
    }

    private static string BonusId(BonusKind kind) => kind switch
    {
        BonusKind.TurretDamage => "turret_damage",
        BonusKind.TurretRate => "turret_rate",
        BonusKind.MachineHp => "machine_hp",
        BonusKind.RepairHeal => "repair_heal",
        BonusKind.ShipHull => "ship_hull",
        BonusKind.ShipDamage => "ship_damage",
        BonusKind.ModuleHp => "module_hp",
        BonusKind.HoldCapacity => "hold_capacity",
        BonusKind.FuelBurn => "fuel_burn",
        BonusKind.SensorRange => "sensor_range",
        BonusKind.DroneRate => "drone_rate",
        BonusKind.ShieldAbsorb => "shield_absorb",
        BonusKind.PowerOutput => "power_output",
        BonusKind.JumpDiscount => "jump_discount",
        BonusKind.MiningRate => "mining_rate",
        _ => kind.ToString(),
    };

    /// <summary>The name of a node, with its level for a repeatable one ("Overclock (level 2/8)": the level being researched next).</summary>
    public static string Name(ResearchEntry e)
    {
        string name = Loc.Name("research", e.Id);
        if (!e.Repeatable) return name;
        return Loc.T("hud.level", name, Math.Min(e.Level + (e.State == ResearchState.Done ? 0 : 1), e.MaxLevels), e.MaxLevels);
    }

    /// <summary>How a state is called in the panel's headings.</summary>
    public static string StateName(ResearchState s) => Loc.T("research.state." + s.ToString().ToLowerInvariant());

    /// <summary>The name of one thing a node unlocks, with its kind ("Machine: smelter").</summary>
    public static string UnlockText(ResearchUnlock u) => u.Kind switch
    {
        ResearchUnlockKind.Machine => Loc.T("research.unlock.machine", Loc.Name("machine", u.Id)),
        ResearchUnlockKind.Recipe => Loc.T("research.unlock.recipe", Loc.Name("recipe", u.Id)),
        ResearchUnlockKind.Belt => Loc.T("research.unlock.belt", Loc.T("ui.belt", u.Id[2..])),
        ResearchUnlockKind.Ability => Loc.T("research.unlock.ability", Loc.T("ability." + u.Id)),
        _ when u.Id == "shield_absorb" => Loc.T("research.unlock.bonusPoints", Loc.T("bonus." + u.Id), u.Percent),
        _ when u.Id == "jump_discount" => Loc.T("research.unlock.bonusCount", Loc.T("bonus." + u.Id), u.Percent),
        _ => Loc.T("research.unlock.bonus", Loc.T("bonus." + u.Id), u.Percent),
    };

    /// <summary>The fields in the order the panel lists them: the basic tier first, then the three science fields.</summary>
    public static readonly ResearchField[] Fields = { ResearchField.Basic, ResearchField.Engineering, ResearchField.Biology, ResearchField.Physics };

    /// <summary>The name of a field, in the current language.</summary>
    public static string FieldName(ResearchField f) => Loc.T("research.field." + f.ToString().ToLowerInvariant());

    /// <summary>
    /// Every node grouped by field in <see cref="Fields"/> order (empty fields left out); inside a field the nodes keep the state
    /// order of <see cref="Build"/> (researching, available, locked, done).
    /// </summary>
    public static IReadOnlyList<(ResearchField Field, IReadOnlyList<ResearchEntry> Entries)> BuildByField(ContentDb content, BeltNetwork net, int player)
    {
        var all = Build(content, net, player);
        var groups = new List<(ResearchField, IReadOnlyList<ResearchEntry>)>();
        foreach (var f in Fields)
        {
            var inField = all.Where(e => e.Field == f).ToList();
            if (inField.Count > 0) groups.Add((f, inField));
        }
        return groups;
    }

    /// <summary>
    /// The colour a science item is drawn in (an sRGB hex string), or null for any other item: engineering amber, biology green,
    /// physics blue. The panel tints the cost line and the item icon with it.
    /// </summary>
    public static string? ScienceColour(string itemId) => itemId switch
    {
        "core:science_engineering" => "#F2A93B",
        "core:science_biology" => "#5FC27A",
        "core:science_physics" => "#58A6FF",
        _ => null,
    };

    /// <summary>Why a locked node cannot start: the requirements still unfinished, as a sentence. Empty for the other states.</summary>
    public static string LockedReason(ResearchEntry e) =>
        e.State != ResearchState.Locked ? ""
        : Loc.T("research.lockedBy", string.Join(", ", e.Missing.Select(r => Loc.Name("research", r.Id))));

    /// <summary>One cost line as text ("12/90 iron plate"), or only the total for a node the lab has not been given anything for.</summary>
    public static string CostLineText(ResearchCostLine c) =>
        $"{c.Delivered}/{c.Needed} {Loc.Name("item", c.ItemId)}";
}
