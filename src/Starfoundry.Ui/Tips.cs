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
    public Tip? Next(BeltNetwork net, int player)
    {
        double now = net.Tick / (double)Units.TicksPerSecond;
        Track(net, now);
        if (now - _lastShown < GapSeconds) return null;

        // In priority order: what hurts first, then what helps.
        if (!_shown.Contains("needEngine") && net.StructureRules && ShipMissingEngineOrCockpit(net, player)) return Show("needEngine", "learn.needEngine", now);
        if (!_shown.Contains("ammo") && TurretWithoutAmmoUnderAttack(net)) return Show("ammo", "learn.ammo", now);
        if (!_shown.Contains("repair") && AnyDamaged(net)) return Show("repair", "learn.repair", now);
        if (!_shown.Contains("walls") && net.MachinesLost > 0 && !Has(net, MachineRole.Wall)) return Show("walls", "learn.walls", now);
        if (!_shown.Contains("research") && _labIdleSince is double since && now - since >= 20 && AnyResearchAvailable(net)) return Show("research", "learn.research", now);
        if (!_shown.Contains("upgrades") && _logisticsAt is double at && now - at >= 90 && NoDefenceUpgradeYet(net) && net.CurrentResearch < 0 && Has(net, MachineRole.Lab))
            return Show("upgrades", "learn.upgrades", now);
        if (!_shown.Contains("deliver") && net.ModuleCount(player) > 0 && !HasMachine(net, "core:ore_unloader")
            && net.HoldCount(player, _content.ItemIndex("core:iron_ore")) >= PlayerRules.HoldCapacity) return Show("deliver", "learn.deliver", now);
        if (!_shown.Contains("port") && net.IsResearched(_content.ResearchIndex("core:jump_drive")) && !Has(net, MachineRole.Unloader)
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

    private Tip Show(string id, string key, double now)
    {
        _shown.Add(id);
        _lastShown = now;
        return new Tip(key);
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
