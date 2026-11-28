using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The tabs of the build menu.</summary>
public enum BuildCategory { Logistics, Production, Defence, Structure }

/// <summary>What a build entry places.</summary>
public enum BuildEntryKind { Belt, Machine, Module, Station, LegacyModule }

/// <summary>
/// One entry of the build menu, built from the content.
/// </summary>
/// <param name="Id">Unique id: the machine id (<c>core:smelter</c>), <c>belt:mk1</c>, <c>module:cargo</c>, <c>station</c> or <c>module</c>.</param>
/// <param name="Machine">Machine type index, or -1.</param>
/// <param name="Module">Module kind of a module entry; <see cref="ModuleKind.Core"/> for the found-a-station tool; null for belts, machines and the legacy module.</param>
/// <param name="Cost">What it costs from the content (per cell for a belt). The legacy module's real cost depends on the world: see <see cref="BuildCatalog.Status"/>.</param>
/// <param name="PowerUse">Power it draws (0 when it draws none).</param>
/// <param name="PowerOutput">Power it makes (reactors).</param>
public sealed record BuildEntry(string Id, BuildCategory Category, BuildEntryKind Kind, int Machine, BeltTier Tier, ModuleKind? Module,
    ItemStack[] Cost, int PowerUse, int PowerOutput)
{
    public bool IsBelt => Kind == BuildEntryKind.Belt;

    /// <summary>Key of the one-line description in <see cref="Loc"/>.</summary>
    public string DescriptionKey => Kind switch
    {
        BuildEntryKind.Belt => "build.desc.belt" + ((int)Tier + 1),
        BuildEntryKind.Machine => "build.desc." + Id,
        BuildEntryKind.Station => "tip.foundStation",
        BuildEntryKind.LegacyModule => "build.desc.module",
        _ => "module." + ModuleKindIds.Id(Module!.Value).Replace("_", "") + ".purpose",
    };

    /// <summary>Key of the entry's role name (what kind of thing it is).</summary>
    public string RoleKey => Kind switch
    {
        BuildEntryKind.Belt => "build.role.belt",
        BuildEntryKind.Machine => "build.role.machine",
        _ => "build.role.module",
    };
}

/// <summary>What the world says about an entry right now.</summary>
/// <param name="UnlockedBy">Index of the research node that unlocks it, or -1 when it is not locked by research.</param>
public sealed record BuildStatus(bool Locked, bool Affordable, ItemStack[] Cost, int UnlockedBy);

/// <summary>
/// The build menu's content: every placeable thing in four categories (Logistics: belts, depot and ports; Production: smelter, assembler,
/// cracker, lab; Defence: turret, wall, repair bay; Structure: the module kinds and founding a station), with the order they are shown in.
/// Pure logic over the content and the world, tested without the engine. The game picks the quick keys itself.
/// </summary>
public static class BuildCatalog
{
    /// <summary>The module kinds the module tool places in a structure world, in menu order (the core is placed by the station tool).</summary>
    public static readonly ModuleKind[] PlaceableKinds =
        { ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Defence, ModuleKind.Hull, ModuleKind.DroneBay, ModuleKind.Shield, ModuleKind.Sensor, ModuleKind.SensorShort, ModuleKind.SensorLong, ModuleKind.CommsAntenna, ModuleKind.ReactorMk2, ModuleKind.Dock, ModuleKind.Shipyard };

    public static readonly BuildCategory[] Categories = { BuildCategory.Logistics, BuildCategory.Production, BuildCategory.Defence, BuildCategory.Structure };

    /// <summary>Entries in menu order. Drills are left out of the station and structure worlds (they are refused there), like the palette.</summary>
    public static IReadOnlyList<BuildEntry> Entries(ContentDb content, bool structures, bool stationRules)
    {
        var list = new List<(int Order, BuildEntry Entry)>();
        for (int t = 0; t < 3; t++)
        {
            var tier = (BeltTier)t;
            list.Add((t, new BuildEntry("belt:mk" + (t + 1), BuildCategory.Logistics, BuildEntryKind.Belt, -1, tier, null,
                content.Belts[t]?.CostPerCell ?? Array.Empty<ItemStack>(), 0, 0)));
        }
        foreach (var m in content.Machines)
        {
            if (m.Role == MachineRole.Ship || m.SandboxOnly) continue;
            if ((structures || stationRules) && m.Role == MachineRole.Drill) continue;
            var (category, order) = Place(m);
            list.Add((order, new BuildEntry(m.Id, category, BuildEntryKind.Machine, m.Index, BeltTier.Mk1, null, m.Cost, m.PowerUse, 0)));
        }
        if (structures && content.Structures is { } kinds)
        {
            for (int i = 0; i < PlaceableKinds.Length; i++)
            {
                var k = PlaceableKinds[i];
                if (!kinds.IsDefined(k)) continue; // content without this kind does not offer it
                var def = kinds.Get(k);
                list.Add((i, new BuildEntry("module:" + ModuleKindIds.Id(k), BuildCategory.Structure, BuildEntryKind.Module, -1, BeltTier.Mk1, k,
                    def.Cost, def.PowerUse, def.PowerOutput)));
            }
            list.Add((100, new BuildEntry("station", BuildCategory.Structure, BuildEntryKind.Station, -1, BeltTier.Mk1, ModuleKind.Core,
                kinds.ModuleCost(ModuleKind.Core), 0, 0)));
        }
        else if (stationRules && content.Modules is { } rules)
            list.Add((0, new BuildEntry("module", BuildCategory.Structure, BuildEntryKind.LegacyModule, -1, BeltTier.Mk1, null, rules.CoreCost, 0, 0)));

        return list.OrderBy(e => (int)e.Entry.Category).ThenBy(e => e.Order).Select(e => e.Entry).ToList();
    }

    /// <summary>The entries of one tab.</summary>
    public static IEnumerable<BuildEntry> InCategory(IReadOnlyList<BuildEntry> entries, BuildCategory category) => entries.Where(e => e.Category == category);

    private static (BuildCategory, int) Place(MachineDef m) => m.Role switch
    {
        MachineRole.Depot => (BuildCategory.Logistics, 10),
        MachineRole.Unloader => (BuildCategory.Logistics, 20 + m.Index),
        MachineRole.Crafter => (BuildCategory.Production, m.Index),
        MachineRole.Lab => (BuildCategory.Production, 100),
        MachineRole.Drill => (BuildCategory.Production, 110),
        MachineRole.Turret => (BuildCategory.Defence, 0),
        MachineRole.Wall => (BuildCategory.Defence, 1),
        MachineRole.Repairer => (BuildCategory.Defence, 2),
        _ => (BuildCategory.Production, 200 + m.Index),
    };

    /// <summary>Whether the entry is locked, whether the hold can pay for it and what it costs right now (the legacy module's cost grows with the station).</summary>
    public static BuildStatus Status(ContentDb content, BeltNetwork net, int player, BuildEntry e)
    {
        var cost = e.Kind == BuildEntryKind.LegacyModule ? net.NextModuleCost(player) : e.Cost;
        bool locked = false;
        int by = -1;
        switch (e.Kind)
        {
            case BuildEntryKind.Belt:
                locked = net.IsBeltLocked(e.Tier);
                by = locked ? FirstNode(content, n => n.UnlockBelts.Contains(e.Tier)) : -1;
                break;
            case BuildEntryKind.Machine:
                locked = net.IsMachineLocked(e.Machine);
                by = locked ? FirstNode(content, n => n.UnlockMachines.Contains(e.Machine)) : -1;
                break;
            case BuildEntryKind.Module when ModuleKindIds.RequiredAbility(e.Module!.Value) is { } needed:
                locked = net.Mode == GameMode.Campaign && !net.HasAbility(needed);
                by = locked ? FirstNode(content, n => n.UnlockAbilities.Contains(needed)) : -1;
                break;
        }
        return new BuildStatus(locked, net.Mode != GameMode.Campaign || net.CanPay(player, cost), cost, by); // sandbox placements cost nothing
    }

    private static int FirstNode(ContentDb content, Func<ResearchDef, bool> unlocks)
    {
        foreach (var n in content.Research) if (unlocks(n)) return n.Index;
        return -1;
    }

    /// <summary>The name shown for an entry.</summary>
    public static string Name(ContentDb content, BuildEntry e) => e.Kind switch
    {
        BuildEntryKind.Belt => Loc.T("ui.belt", (int)e.Tier + 1),
        BuildEntryKind.Machine => Loc.Name("machine", content.Machines[e.Machine].Id),
        BuildEntryKind.Station => Loc.T("ui.foundStation"),
        BuildEntryKind.LegacyModule => Loc.T("ui.module"),
        _ => Loc.T("module." + ModuleKindIds.Id(e.Module!.Value).Replace("_", "")),
    };

    /// <summary>The cost as text ("14x iron ingot, 6x iron plate"), with " per cell" for belts, or the word for free.</summary>
    public static string CostText(ContentDb content, BuildEntry e, ItemStack[] cost)
    {
        if (cost.Length == 0) return Loc.T("ui.free");
        string text = string.Join(", ", cost.Select(s => $"{s.Count}x {Loc.Name("item", content.Items[s.Item].Id)}"));
        return e.IsBelt ? text + Loc.T("ui.perCell") : text;
    }

    /// <summary>The power line ("uses 10", "makes 100"), or an empty string when the entry has no power at all.</summary>
    public static string PowerText(BuildEntry e) =>
        e.PowerOutput > 0 ? Loc.T("build.powerMakes", Physical.Pow(e.PowerOutput, Loc.Culture)) : e.PowerUse > 0 ? Loc.T("build.powerUses", Physical.Pow(e.PowerUse, Loc.Culture)) : "";
}
