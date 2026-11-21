using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>Other systems: the scan, map and jump flow, and the ores that only grow there.</summary>
public sealed partial class StructureBot
{
    private int CurrentSystem => Net.CurrentSystem(0);

    /// <summary>Systems the bot has scanned, and what it found: item to the systems holding a revealed deposit of it.</summary>
    private readonly HashSet<int> _scanned = new() { 0 };

    private int JumpCarbon => Net.JumpCost().Where(c => c.Item == Item("core:carbon")).Sum(c => c.Count);

    /// <summary>Shortest lane path (system ids, without the start) over systems the star map shows, or null.</summary>
    private List<int>? PathTo(int from, int to)
    {
        var g = Net.Galaxy!;
        var prev = new Dictionary<int, int> { [from] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0 && !prev.ContainsKey(to))
        {
            int s = queue.Dequeue();
            foreach (int n in g.Systems[s].Lanes)
                if (Net.IsSystemKnown(n) && prev.TryAdd(n, s)) queue.Enqueue(n);
        }
        if (!prev.ContainsKey(to)) return null;
        var path = new List<int>();
        for (int s = to; s != from; s = prev[s]) path.Add(s);
        path.Reverse();
        return path;
    }

    /// <summary>Charges one jump along a lane, topping up the carbon it costs at the current system first.</summary>
    private bool JumpTo(int next)
    {
        int cost = JumpCarbon;
        int here = CurrentSystem;
        if (here < 0) { Stuck("jump attempted in deep space"); return false; }
        if (Have("core:carbon") < cost + 10)
        {
            if (!Gather(Item("core:carbon"), cost * 3 + 10, "the next jumps", here)) { Stuck($"no carbon for a jump out of system {here}"); return false; }
        }
        Cmd(new StopMining());
        _mineTarget = -1;
        var r = Act(new Jump(next));
        if (Failed(r, $"Jump {here}->{next}")) return false;
        Wait(ExplorationRules.JumpChargeTicks / (double)Tps + 1);
        if (CurrentSystem != next) { Stuck($"the jump to system {next} did not arrive (in system {CurrentSystem})"); return false; }
        Milestone($"Jumped to system {next}");
        return true;
    }

    private bool JumpPath(List<int> path)
    {
        foreach (int next in path) if (!JumpTo(next)) return false;
        return true;
    }

    private void ScanHere()
    {
        int here = CurrentSystem;
        if (here < 0 || _scanned.Contains(here)) return;
        for (int i = 0; i < 20 && Net.ScanCooldown(0) > 0; i++) Wait(1);
        var r = Act(new Scan());
        if (Failed(r, "Scan")) return;
        _scanned.Add(here);
        Milestone("Scanned another system");
        Say($"scan of system {here} ({_c.Archetypes[Net.Galaxy!.Systems[here].Archetype].Id}) revealed {r.RevealedDeposits.Count} deposits");
    }

    /// <summary>Scanned systems that hold a revealed deposit of an item, nearest to home by lane hops first.</summary>
    private List<int> SystemsWith(int item)
    {
        var g = Net.Galaxy!;
        var found = new List<int>();
        foreach (int s in _scanned)
        {
            if (s == 0 && item != Item("core:iron_ore") && item != Item("core:carbon") && item != Item("core:ice")) continue;
            for (int d = 0; d < Net.DepositCount; d++)
                if (g.Deposits[d].System == s && Net.DepositItem(d) == item && Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 20) { found.Add(s); break; }
        }
        return found;
    }

    /// <summary>The nearest system (by lane hops from here) the star map shows that has not been scanned.</summary>
    private int NextUnscanned()
    {
        var g = Net.Galaxy!;
        int here = CurrentSystem;
        var dist = new Dictionary<int, int> { [here] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(here);
        while (queue.Count > 0)
        {
            int s = queue.Dequeue();
            if (!_scanned.Contains(s) && Net.IsSystemKnown(s)) return s;
            foreach (int n in g.Systems[s].Lanes)
                if (Net.IsSystemKnown(n) && dist.TryAdd(n, dist[s] + 1)) queue.Enqueue(n);
        }
        return -1;
    }

    /// <summary>
    /// Brings ore home: explores the star map (jump, scan) until every wanted item has a known deposit, mines each by hand, then jumps home.
    /// <paramref name="wanted"/> maps an item id to how many of it the hold should have afterwards.
    /// </summary>
    private bool _inExpedition;

    private bool Expedition(string why, params (string Item, int Count)[] wanted)
    {
        _inExpedition = true;
        try { return ExpeditionCore(why, wanted); }
        finally { _inExpedition = false; }
    }

    private bool ExpeditionCore(string why, (string Item, int Count)[] wanted)
    {
        Goal("expedition: " + why);
        if (!Net.HasAbility(Ability.Jump)) { Stuck("expedition without the jump drive"); return false; }
        // Leave only just after a wave, with fuel for the base, and carbon for the way.
        WaitUntil(() => !WaveSoon(60), 240); // a quiet moment if one comes; otherwise leave between two fights
        WaitUntil(() => Net.EnemyCount == 0, 180);
        // Every reactor burns a hydrogen every 30 s wherever the ship is, so the hold carries the fuel for the whole trip.
        if (Hydrogen < 50 && !_refuelling) { Refuel(60); WaitUntil(() => Hydrogen >= 45, 240); }
        if (Have("core:carbon") < JumpCarbon * 6) Gather(Item("core:carbon"), JumpCarbon * 8, "the jumps of the trip");
        if (!Need(new[] { new ItemStack(Item("core:carbon"), JumpCarbon * 4) }, "carbon for the trip", 1, 240)) return false;

        // Pack for the trip: the hold carries a quarter less than its size and the ore comes first, so spare ingots and plates stay behind
        // (the depots refill them at home).
        foreach (var (spare, keep) in new[] { ("core:iron_ingot", 20), ("core:iron_plate", 10) })
            if (Have(spare) > keep + 5) Cmd(new DumpItems(Item(spare), Have(spare) - keep));

        bool Satisfied((string Item, int Count) w) => Have(w.Item) >= w.Count;
        int guard = 0;
        while (!wanted.All(Satisfied) && guard++ < 24)
        {
            var open = wanted.Where(w => !Satisfied(w)).ToArray();
            // Mine here what is wanted and present.
            foreach (var w in open)
            {
                if (CurrentSystem == 0) continue;
                if (SystemsWith(Item(w.Item)).Contains(CurrentSystem))
                    Gather(Item(w.Item), w.Count, $"the expedition ({w.Item})", CurrentSystem, () => Have(w.Item) >= w.Count || Net.HoldTotal(0) >= Net.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent / 100);
            }
            open = wanted.Where(w => !Satisfied(w)).ToArray();
            if (open.Length == 0) break;
            if (Net.HoldTotal(0) >= Net.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent / 100) { Stuck($"the hold is at the mining limit before the expedition got everything ({string.Join(", ", open.Select(o => o.Item + " " + Have(o.Item) + "/" + o.Count))})"); break; }
            // Go where a missing item is known to be, otherwise look at a new system.
            int target = -1;
            foreach (var w in open)
            {
                var systems = SystemsWith(Item(w.Item)).Where(s => s != CurrentSystem).ToList();
                if (systems.Count > 0) { target = systems.OrderBy(s => PathTo(CurrentSystem, s)?.Count ?? 99).First(); break; }
            }
            if (target < 0) target = NextUnscanned();
            if (target < 0) { Stuck($"no unexplored system left to look for {string.Join(", ", open.Select(o => o.Item))}"); break; }
            var path = PathTo(CurrentSystem, target);
            if (path is null) { Stuck($"no known lane path to system {target}"); break; }
            if (!JumpPath(path)) break;
            ScanHere();
        }

        // Home again.
        if (CurrentSystem != 0)
        {
            var home = PathTo(CurrentSystem, 0);
            if (home is null || !JumpPath(home)) { Stuck("could not get home from the expedition"); return false; }
        }
        // Arrival: the ship appears at the anchor; the base is a few cells away.
        _mineTarget = -1;
        GoHome();
        return wanted.All(Satisfied);
    }
}
