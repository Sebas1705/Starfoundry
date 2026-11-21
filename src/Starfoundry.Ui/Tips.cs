using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>A hint to show the player: a localisation key and its arguments.</summary>
public sealed record Tip(string Key, params object[] Args);

/// <summary>
/// Contextual hints that teach what the guided steps do not reach: repairing, walls, ammo, the research choice, the
/// defence upgrades and the cargo port. A hint appears when the situation it is about happens, at most once, and never
/// closer than a few seconds to the previous one. Pure logic over the simulation, tested without the engine.
/// </summary>
public sealed class Tips
{
    /// <summary>Minimum simulated seconds between two hints.</summary>
    public const double GapSeconds = 25;

    private readonly ContentDb _content;
    private readonly HashSet<string> _shown = new();
    private double _lastShown = double.NegativeInfinity;
    private double? _labIdleSince;
    private double? _logisticsAt;

    public Tips(ContentDb content) => _content = content;

    /// <summary>The hint to show now, or null. Call it every frame; it is cheap.</summary>
    /// <param name="net">The world.</param>
    /// <param name="player">The player the hints are for.</param>
    /// <param name="mapOpen">True while the star map is open (a hint about the jump cost applies then).</param>
    public Tip? Next(BeltNetwork net, int player, bool mapOpen = false)
    {
        double now = net.Tick / (double)Units.TicksPerSecond;
        Track(net, now);
        if (now - _lastShown < GapSeconds) return null;

        if (!_shown.Contains("jumpCarbon") && mapOpen && net.StructureRules && net.Galaxy is not null && JumpCostShort(net, player, out int missing))
            return Show("jumpCarbon", "learn.jumpCarbon", now, missing);
        if (!_shown.Contains("noReactor") && net.StructureRules && StationWithoutReactor(net, player)) return Show("noReactor", "learn.noReactor", now);

        // In priority order: what hurts first, then what helps.
        if (!_shown.Contains("needEngine") && net.StructureRules && ShipMissingEngineOrCockpit(net, player)) return Show("needEngine", "learn.needEngine", now);
        if (!_shown.Contains("noFuel") && net.StructureRules && ShipHasReactor(net, player) && HydrogenInHold(net, player) <= 3) return Show("noFuel", "learn.noFuel", now);
        if (!_shown.Contains("lowPower") && net.StructureRules && net.ShipStructure(player) >= 0 && HydrogenInHold(net, player) == 0
            && net.ShipPower(player).SatisfactionPermille < PowerRules.FullSatisfaction) return Show("lowPower", "learn.lowPower", now);
        if (!_shown.Contains("defence") && net.StructureRules && net.ThreatActive && !Has(net, MachineRole.Turret)
            && (net.GetSystemThreat(0).WarningRemaining >= 0 || net.EnemyCount > 0)) return Show("defence", "learn.defence", now);
        if (!_shown.Contains("ammo") && TurretWithoutAmmoUnderAttack(net)) return Show("ammo", "learn.ammo", now);
        if (!_shown.Contains("repair") && AnyDamaged(net)) return Show("repair", "learn.repair", now);
        if (!_shown.Contains("walls") && net.MachinesLost > 0 && !Has(net, MachineRole.Wall)) return Show("walls", "learn.walls", now);
        if (!_shown.Contains("research") && _labIdleSince is double since && now - since >= 20 && AnyResearchAvailable(net)) return Show("research", "learn.research", now);
        if (!_shown.Contains("upgrades") && _logisticsAt is double at && now - at >= 90 && NoDefenceUpgradeYet(net) && net.CurrentResearch < 0 && Has(net, MachineRole.Lab))
            return Show("upgrades", "learn.upgrades", now);
        if (!_shown.Contains("drones") && net.StructureRules && _content.Research.FirstOrDefault(r => r.Id == "core:drone_automation") is { } dr && net.IsResearched(dr.Index)
            && !net.Structures().Any(st => st.Owner == player && st.Modules.Values.Contains(ModuleKind.DroneBay))) return Show("drones", "learn.drones", now);
        if (!_shown.Contains("deliver") && net.ModuleCount(player) > 0 && !HasMachine(net, "core:ore_unloader")
            && net.HoldCount(player, _content.ItemIndex("core:iron_ore")) >= PlayerRules.HoldCapacity) return Show("deliver", "learn.deliver", now);
        // Structure worlds: ports are free and the starter ship already has an iron ore port, so the tip is about any other ore piling up in the hold.
        if (!_shown.Contains("portOre") && net.StructureRules && OreWithoutPort(net, player) is var ore and >= 0)
            return Show("portOre", "learn.portOre", now, Loc.Name("item", _content.Items[ore].Id), HoldCountOf(net, player, ore));
        if (!_shown.Contains("port") && !net.StructureRules && net.IsResearched(_content.ResearchIndex("core:jump_drive")) && !Has(net, MachineRole.Unloader)
            && HomeIronLow(net, player)) return Show("port", "learn.port", now);
        return null;
    }

    /// <summary>True when the player owns a ship with modules that lacks an engine or a cockpit (it cannot fly yet).</summary>
    private static bool ShipMissingEngineOrCockpit(BeltNetwork net, int player)
    {
        foreach (var st in net.Structures())
        {
            if (st.Owner != player || st.Kind != StructureKind.Ship || st.Modules.Count == 0) continue;
            if (!st.Modules.Values.Contains(ModuleKind.Engine) || !st.Modules.Values.Contains(ModuleKind.Cockpit)) return true;
        }
        return false;
    }

    private int HydrogenInHold(BeltNetwork net, int player) =>
        _content.FuelItem < 0 ? 0 : net.HoldCount(player, _content.FuelItem);

    /// <summary>True when the player's ship structure has a reactor module (so its power runs on hydrogen).</summary>
    private static bool ShipHasReactor(BeltNetwork net, int player)
    {
        foreach (var st in net.Structures())
            if (st.Owner == player && st.Kind == StructureKind.Ship && st.Modules.Values.Any(ModuleKindIds.IsReactor)) return true;
        return false;
    }

    private Tip Show(string id, string key, double now, params object[] args)
    {
        _shown.Add(id);
        _lastShown = now;
        return new Tip(key, args);
    }

    /// <summary>True when the hold lacks the items one jump costs; <paramref name="cost"/> is the carbon (first stack) a jump needs.</summary>
    private bool JumpCostShort(BeltNetwork net, int player, out int cost)
    {
        cost = 0;
        bool shortOf = false;
        foreach (var stack in _content.JumpCost)
        {
            if (cost == 0) cost = stack.Count;
            if (net.HoldCount(player, stack.Item) < stack.Count) shortOf = true;
        }
        return shortOf;
    }

    /// <summary>True when a station of the player has machines but no Reactor module: nothing there can be powered.</summary>
    private bool StationWithoutReactor(BeltNetwork net, int player)
    {
        foreach (var st in net.Structures())
            if (st.Owner == player && net.StationHasNoReactor(st.Id)) return true;
        return false;
    }

    /// <summary>Ore held in quantity that has no port to put it on a belt (structure worlds); returns its item index, or -1.</summary>
    private int OreWithoutPort(BeltNetwork net, int player)
    {
        foreach (var def in _content.Machines)
        {
            if (def.Role != MachineRole.Unloader || def.Unloader is not { Item: >= 0 } u) continue;
            int spare = net.HoldCount(player, u.Item);
            foreach (var j in _content.JumpCost)
                if (j.Item == u.Item) spare -= j.Count; // carbon kept for a jump is not a surplus
            if (spare < PortTipOreCount || PortStands(net, u.Item)) continue;
            return u.Item;
        }
        return -1;
    }

    /// <summary>Hold count of an ore (or ice) from which a port tip is worth showing.</summary>
    public const int PortTipOreCount = 15;

    private static int HoldCountOf(BeltNetwork net, int player, int item) => net.HoldCount(player, item);

    private bool PortStands(BeltNetwork net, int item)
    {
        for (int id = 0; id < net.MachineIdLimit; id++)
        {
            if (!net.IsMachineAlive(id)) continue;
            var def = _content.Machines[net.GetMachineInfo(id).Type];
            if (def.Role == MachineRole.Unloader && def.Unloader is { } u && u.Item == item) return true;
        }
        return false;
    }

    private void Track(BeltNetwork net, double now)
    {
        int logistics = _content.ResearchIndex("core:logistics");
        if (_logisticsAt is null && net.IsResearched(logistics)) _logisticsAt = now;

        bool labIdle = Has(net, MachineRole.Lab) && net.CurrentResearch < 0;
        if (labIdle) _labIdleSince ??= now;
        else _labIdleSince = null;
    }

    private bool HasMachine(BeltNetwork net, string machineId)
    {
        for (int id = 0; id < net.MachineIdLimit; id++)
            if (net.IsMachineAlive(id) && _content.Machines[net.GetMachineInfo(id).Type].Id == machineId) return true;
        return false;
    }

    private bool Has(BeltNetwork net, MachineRole role)
    {
        for (int id = 0; id < net.MachineIdLimit; id++)
            if (net.IsMachineAlive(id) && _content.Machines[net.GetMachineInfo(id).Type].Role == role) return true;
        return false;
    }

    private bool AnyDamaged(BeltNetwork net)
    {
        for (int id = 0; id < net.MachineIdLimit; id++)
        {
            if (!net.IsMachineAlive(id)) continue;
            var m = net.GetMachineInfo(id);
            if (m.Hp < net.MaxHp(m.Type)) return true;
        }
        return false;
    }

    private bool TurretWithoutAmmoUnderAttack(BeltNetwork net)
    {
        if (!net.ThreatActive || net.EnemyCount == 0) return false;
        bool any = false;
        for (int id = 0; id < net.MachineIdLimit; id++)
        {
            if (!net.IsMachineAlive(id)) continue;
            var m = net.GetMachineInfo(id);
            if (_content.Machines[m.Type].Role != MachineRole.Turret) continue;
            any = true;
            if (m.InputItems > 0) return false; // at least one turret can fire
        }
        return any;
    }

    private bool AnyResearchAvailable(BeltNetwork net) => Enumerable.Range(0, _content.Research.Count).Any(net.CanResearch);

    private bool NoDefenceUpgradeYet(BeltNetwork net) =>
        !_content.Research.Any(r => r.Bonuses.Length > 0 && net.IsResearched(r.Index)) &&
        _content.Research.Any(r => r.Bonuses.Length > 0 && net.CanResearch(r.Index));

    private bool HomeIronLow(BeltNetwork net, int player) =>
        OreRunway.IsLow(OreRunway.PercentLeft(net, player, _content.ItemIndex("core:iron_ore")));
}
