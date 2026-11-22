using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>The plan: named goals in order, each guarded by "can I afford it / is it unlocked".</summary>
public sealed partial class StructureBot
{
    // Module plan (module grid coordinates of the ship; the starter ship stands at (0,0,0..4)).
    private static readonly (int X, int Y, int Z) W1 = (0, 0, 1);        // starter workshop
    private static readonly (int X, int Y, int Z) D1 = (0, 1, 1);        // ship defence module, on top of the starter workshop
    private static readonly (int X, int Y, int Z) W2 = (1, 0, 0);        // laboratory workshop
    private static readonly (int X, int Y, int Z) C2 = (-1, 0, 0);       // second cargo module
    private static readonly (int X, int Y, int Z) R2 = (-1, 0, -1);      // second reactor
    private static readonly (int X, int Y, int Z) B1 = (1, 0, 1);        // ship drone bay

    private int _iceDeposit = -1;
    private bool _iceLoopBuilt;
    private bool _refuelling;
    private bool _orePaused;
    private int _lab = -1;
    private int _shipBatteries, _stationBatteries;

    private static int FloorCell(int units) => (int)Math.Floor(units / (double)Cell);

    private int Hydrogen => Net.HoldCount(0, Item("core:hydrogen"));
    private int Have(string item) => Net.HoldCount(0, Item(item));

    /// <summary>True when a wave is fighting, warned, or the alert meter will fill within <paramref name="seconds"/>.</summary>
    private bool WaveSoon(int seconds)
    {
        if (!Net.ThreatActive) return false;
        var t = Net.GetSystemThreat(0);
        if (t.WarningRemaining >= 0 || Net.EnemyCount > 0) return true;
        int rate = t.Signature - _c.Threat!.SignatureThreshold;
        if (rate <= 0) return false;
        return (t.AlertMax - t.Alert) / rate < seconds * Tps;
    }

    private void PlayCampaign()
    {
        MineIron();
        FoundStation();
        MoreCargo(2);
        HydrogenLoop();
        IngotLines(1);
        FetchCarbon(26, "carbon for the two reactors");
        ShipDefence();
        StationPower();
        ShipReactor();
        Laboratory();
        ResearchPlan();
    }

    // ---- 1: hand mining ---------------------------------------------------------------------------------

    private void MineIron()
    {
        Goal("1 fly to the home iron deposit and hand-mine it");
        int iron = FindDeposit(Item("core:iron_ore"), 0);
        if (iron < 0) { Stuck("no iron deposit revealed at home"); throw new GameEnded("no iron"); }
        _homeDeposit = iron;
        if (!GoMine(iron)) throw new GameEnded("could not reach the iron deposit");
        var p = Net.GetPlayerInfo(0);
        _homeMine = (p.X, p.Y, p.Z);
        Milestone("Hand mining the home iron deposit");
        WaitUntil(() => Net.HoldCount(0, Item("core:iron_plate")) > 0, 300);
    }

    /// <summary>Hand-mines another deposit until the hold has enough of its item, then goes back to the iron deposit (at home).</summary>
    private bool Gather(int item, int count, string why, int system = 0, Func<bool>? enough = null)
    {
        int dep = FindDeposit(item, system);
        if (dep < 0) { Stuck($"no revealed {_c.Items[item].Id} deposit in system {system} for {why}"); return false; }
        Say($"gathering {count} {_c.Items[item].Id} for {why}");
        _gatherDepth++;
        try
        {
            if (!GoMine(dep)) return false;
            bool ok = WaitUntil(() =>
            {
                MakeRoom(item);
                return enough?.Invoke() ?? (Net.HoldCount(0, item) >= count || Net.DepositRemaining(dep) <= 0);
            }, 300);
            if (!ok) Stuck($"could not hand-mine {count} {_c.Items[item].Id} in 5 minutes (hold {Net.HoldTotal(0)}/{Net.HoldCapacity(0)}, mining limit {PlayerRules.MiningHoldLimitPercent}%)");
            if (system == 0) GoHome();
            return ok;
        }
        finally { _gatherDepth--; }
    }

    /// <summary>Back to hand mining the home iron.</summary>
    private void GoHome()
    {
        if (_homeDeposit >= 0) GoMine(_homeDeposit);
    }

    /// <summary>Hand mining stops at 75 percent of the hold. When the hold sits at the limit the player throws out what is not needed:
    /// raw iron ore, then hydrogen far above the reactors' need, then ingots far above any build cost (<see cref="DumpItems"/>).</summary>
    private void MakeRoom(int wanted)
    {
        int limit = Net.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent / 100;
        if (Net.HoldTotal(0) < limit) return;
        int ore = Item("core:iron_ore");
        if (wanted != ore && Net.HoldCount(0, ore) > 0)
        {
            Stuck($"the hold is at the mining limit ({Net.HoldTotal(0)}/{Net.HoldCapacity(0)}) while gathering {_c.Items[wanted].Id}: dumping {Net.HoldCount(0, ore)} iron ore");
            Cmd(new DumpItems(ore, 0));
            return;
        }
        int h = Hydrogen;
        if (wanted != Item("core:hydrogen") && h > 40)
        {
            Stuck($"the hold is at the mining limit while gathering {_c.Items[wanted].Id}: dumping {h - 30} hydrogen (hold {Net.HoldTotal(0)}/{Net.HoldCapacity(0)})");
            Cmd(new DumpItems(Item("core:hydrogen"), h - 30));
            return;
        }
        int ingots = Have("core:iron_ingot");
        if (ingots > 40)
        {
            Stuck($"the hold is at the mining limit while gathering {_c.Items[wanted].Id}: dumping {ingots - 24} iron ingots (hold {Net.HoldTotal(0)}/{Net.HoldCapacity(0)})");
            Cmd(new DumpItems(Item("core:iron_ingot"), ingots - 24));
            return;
        }
        int plates = Have("core:iron_plate");
        if (plates > 30)
        {
            Stuck($"the hold is at the mining limit while gathering {_c.Items[wanted].Id}: dumping {plates - 20} iron plates (hold {Net.HoldTotal(0)}/{Net.HoldCapacity(0)})");
            Cmd(new DumpItems(Item("core:iron_plate"), plates - 20));
        }
    }

    private int _gatherDepth;
    private int _hygieneDumps;
    private readonly List<int> _ingotSmelters = new();
    private int _starterAssembler = -1;
    private bool _smeltersOff, _platesOff;

    /// <summary>
    /// Ore is the scarce thing (hand mining gives about two a second), so the player switches off the lines that only fill the hold with
    /// goods already in stock, and the laboratories' plate lines get the ore. The same <see cref="SetRecipe"/> command a player uses.
    /// </summary>
    private void ManageStock()
    {
        int ingots = Have("core:iron_ingot"), plates = Have("core:iron_plate");
        if (!_smeltersOff && ingots >= 70 || _smeltersOff && ingots <= 36)
        {
            _smeltersOff = !_smeltersOff;
            foreach (int s in _ingotSmelters)
                if (Net.IsMachineAlive(s)) Cmd(new SetRecipe(s, _smeltersOff ? -1 : Recipe("core:smelt_iron")));
            Say(_smeltersOff ? "ingot stock is plenty: ingot lines switched off" : "ingot stock is low: ingot lines switched on");
        }
        if (_starterAssembler < 0)
            for (int id = 0; id < Net.MachineIdLimit && _starterAssembler < 0; id++)
                if (Net.IsMachineAlive(id) && Net.GetMachineInfo(id).Structure == _ship && _c.Machines[Net.GetMachineInfo(id).Type].Id == "core:assembler") _starterAssembler = id;
        if (_starterAssembler >= 0 && Net.IsMachineAlive(_starterAssembler) && (!_platesOff && plates >= 60 || _platesOff && plates <= 24))
        {
            _platesOff = !_platesOff;
            Cmd(new SetRecipe(_starterAssembler, _platesOff ? -1 : Recipe("core:iron_plate")));
            Say(_platesOff ? "plate stock is plenty: the starter assembler is switched off" : "plate stock is low: the starter assembler is switched on");
        }
    }

    /// <summary>
    /// The depots hand finished goods to the hold up to 40 percent of its capacity per item, and hand mining stops at 75 percent, so
    /// two or three stocked items freeze mining and starve every ore port. A player notices that the hold is full and throws out the
    /// surplus; so does the bot (it is the same <see cref="DumpItems"/> command).
    /// </summary>
    private void HoldHygiene()
    {
        if (_mineTarget < 0) return;
        int limit = Net.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent / 100;
        if (Net.HoldTotal(0) < limit - 1 || Net.DepositRemaining(_mineTarget) <= 0 || !Net.InMineRange(0, _mineTarget)) return;
        foreach (var (item, keep) in new[] { ("core:iron_ingot", 36), ("core:iron_plate", 30), ("core:hydrogen", 30), ("core:ammo", 0) })
        {
            int have = Have(item);
            if (have <= keep + 4 || item == "core:ammo") continue;
            if (_hygieneDumps++ == 0) Stuck($"hand mining froze: the hold is at the mining limit ({Net.HoldTotal(0)}/{Net.HoldCapacity(0)}) with {have} {item} the depots keep delivering; throwing the surplus out (repeats are not listed)");
            Cmd(new DumpItems(Item(item), have - keep));
            return;
        }
    }

    private void FetchCarbon(int count, string why)
    {
        Goal("fetch carbon: " + why);
        if (Have("core:carbon") >= count) return;
        Gather(Item("core:carbon"), count, why);
    }

    // ---- 2: the station ---------------------------------------------------------------------------------

    private void FoundStation()
    {
        Goal("2 found a station next to the ship");
        var core = _c.Structures!.ModuleCost(ModuleKind.Core);
        if (!Need(core, "the station core")) return;
        var p = Net.GetPlayerInfo(0);
        int wx = FloorCell(p.X), wy = FloorCell(p.Y), wz = FloorCell(p.Z) - 8; // beside the cockpit, on its -z side, inside build range of everything
        var r = Act(new FoundStation(wx, wy, wz));
        if (Failed(r, "FoundStation")) return;
        _station = Net.Structures().First(s => s.Kind == StructureKind.Station && s.Owner == 0).Id;
    }

    private void StationPower()
    {
        if (_station < 0) return;
        Goal("2b station: workshop module and reactor module (and hydrogen in the hold)");
        PlaceModule(_station, 1, 0, 0, ModuleKind.Workshop);
        if (!Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for the station reactor")) return;
        PlaceModule(_station, 0, -1, 0, ModuleKind.Reactor);
        // Something to power: an ingot line in the new workshop.
        var (ox, oy, oz) = Origin(1, 0, 0);
        IngotLine(_station, ox, oy, oz, 1, 1);
    }

    // ---- 3: ingots and the ice loop ---------------------------------------------------------------------

    private void IngotLines(int count)
    {
        Goal($"more ingots: {count} ingot line(s) in the starter workshop");
        var (ox, oy, oz) = Origin(W1.X, W1.Y, W1.Z);
        for (int i = 0; i < count; i++) IngotLine(_ship, ox, oy, oz, 1, 5 + i);
    }

    private void HydrogenLoop()
    {
        Goal("3 hydrogen: ice port + cracker wired into the depot, then ice by hand");
        var (ox, oy, oz) = Origin(W1.X, W1.Y, W1.Z);
        if (!IceLoop(_ship, ox, oy, oz)) return;
        _iceLoopBuilt = true;
        _iceDeposit = FindDeposit(Item("core:ice"), 0);
        Refuel(); // hand-mines ice for the cracker and goes back to the iron
        WaitUntil(() => _seen.Contains("Hydrogen loop: first hydrogen made from ice"), 240);
    }

    /// <summary>Keeps the reactors fed: when hydrogen runs low, hand-mines ice for the cracker and goes back to the iron.</summary>
    private void Refuel(int target = 32)
    {
        if (!_iceLoopBuilt || Net.CurrentSystem(0) != 0 || NowSeconds < _noRefuelUntil) return;
        _refuelling = true;
        try
        {
            int ice = Net.HoldCount(0, Item("core:ice"));
            if (Hydrogen + ice / 2 >= target - 8) return; // the cracker is still working through the ice already in the hold
            string before = _goal;
            if (FindDeposit(Item("core:ice"), 0) < 0)
            {
                // The home ice is mined out: the only ice left is in other systems.
                if (!_iceGoneNoted) { _iceGoneNoted = true; Stuck("the home ice deposit is exhausted; the reactors need ice from another system"); }
                if (Net.HasAbility(Ability.Jump) && Net.CurrentSystem(0) == 0) Expedition("ice for the reactors", ("core:ice", 90));
                _noRefuelUntil = NowSeconds + 120;
                _goal = before;
                return;
            }
            Goal("refuel: hand-mine ice (hydrogen low)");
            Gather(Item("core:ice"), 60, "hydrogen", 0, () => Hydrogen + Net.HoldCount(0, Item("core:ice")) / 2 >= target);
            _goal = before;
        }
        finally { _refuelling = false; }
    }

    private bool _iceGoneNoted;
    private int _noRefuelUntil;

    // ---- hold, defence, power ---------------------------------------------------------------------------

    private static readonly (int X, int Y, int Z)[] CargoSpots = { C2, (-1, 0, 1), (-1, 1, 0), (-1, 1, 1) };

    /// <summary>Extra cargo modules (60 hold each): the depots' stock, hydrogen and hand-mined ore fill the starter hold's 45 usable slots.</summary>
    private void MoreCargo(int count)
    {
        Goal($"{count} extra cargo module(s) (more hold)");
        for (int i = 0; i < count; i++) PlaceModule(_ship, CargoSpots[i].X, CargoSpots[i].Y, CargoSpots[i].Z, ModuleKind.Cargo);
    }

    private void ShipReactor()
    {
        Goal("a second reactor module on the ship");
        if (!Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for the ship reactor")) return;
        PlaceModule(_ship, R2.X, R2.Y, R2.Z, ModuleKind.Reactor);
    }

    /// <summary>Walls in a defence module draw the enemies' fire (they attack the nearest wall first) to where the turrets reach.</summary>
    private void Walls(int st, int ox, int oy, int oz, int count)
    {
        var cells = new (int X, int Y, int Z)[] { (1, 2, 1), (1, 2, 6), (6, 2, 1), (6, 2, 6) };
        for (int i = 0; i < Math.Min(count, cells.Length); i++)
            PlaceMachine(st, ox + cells[i].X, oy + cells[i].Y, oz + cells[i].Z, "core:wall");
    }

    private void ShipDefence()
    {
        Goal("5 ship defence: defence module, an ammo battery of two turrets, walls, weapons on");
        if (PlaceModule(_ship, D1.X, D1.Y, D1.Z, ModuleKind.Defence) < 0) return;
        var (ox, oy, oz) = Origin(W1.X, W1.Y, W1.Z);
        _shipBatteries += AmmoBattery(_ship, ox, oy, oz, 5).Count > 0 ? 1 : 0;
        Cmd(new SetWeapons(true));
        var (dx, dy, dz) = Origin(D1.X, D1.Y, D1.Z);
        Walls(_ship, dx, dy, dz, 2);
    }

    private bool _stationDefenceStarted;

    private void StationDefence()
    {
        if (_station < 0 || _stationDefenceStarted) return;
        _stationDefenceStarted = true;
        Goal("5b station defence: defence module, battery, walls and a repair bay");
        if (PlaceModule(_station, 0, 1, 0, ModuleKind.Defence) < 0) return;
        var (ox, oy, oz) = Origin(0, 0, 0);
        _stationBatteries += AmmoBattery(_station, ox, oy, oz, 5).Count > 0 ? 1 : 0;
        var (dx, dy, dz) = Origin(0, 1, 0);
        Walls(_station, dx, dy, dz, 2);
        RepairBay(_station, ox, oy, oz, 2, 2);
    }


    private int _nextIceTripAt;

    private int HomeIceLeft()
    {
        int left = 0;
        for (int d = 0; d < Net.DepositCount; d++)
            if (Net.DepositItem(d) == Item("core:ice") && Net.Galaxy!.Deposits[d].System == 0) left += Net.DepositRemaining(d);
        return left;
    }

    private bool _reinforcing;
    private bool _shipRepair;

    /// <summary>Called from the waits: adds turrets, walls and repair bays as the waves grow. Does nothing while a fight is on or when the plan is met.</summary>
    private void Between()
    {
        if (_reinforcing || Net.EnemyCount > 0 || _station < 0 || !Net.StructureExists(_station) || CurrentSystem != 0) return;
        _reinforcing = true;
        string before = _goal;
        try
        {
            Reinforce();
            // The home ice runs out (each reactor burns two hydrogen, four ice, a minute): fetch ice from an ice field while there is time.
            if (Net.HasAbility(Ability.Jump) && _iceLoopBuilt && !_inExpedition && NowSeconds >= _nextIceTripAt && HomeIceLeft() < 160 && Have("core:ice") < 40)
            {
                _nextIceTripAt = NowSeconds + 15 * 60;
                Expedition("ice for the reactors (the home ice is nearly gone)", ("core:ice", 140));
            }
        }
        finally { _reinforcing = false; _goal = before; }
    }

    private void Reinforce()
    {
        long waves = Net.WavesSpawned;
        // No third reactor: each burns 2 hydrogen a minute (4 ice) and the home ice is finite, so a ship at 83 percent power is the cheaper choice.
        if (_stationBatteries < 1 && _station >= 0 && Net.Structures().Any(s => s.Id == _station && s.Modules.ContainsKey((0, 0, 0))) && waves >= 0 && _shipBatteries >= 1)
        {
            if (Net.HoldCount(0, Item("core:iron_plate")) >= 14) StationDefence();
        }
        if (waves >= 1 && !_shipRepair && _shipBatteries >= 1)
        {
            _shipRepair = true;
            Goal("a repair bay for the ship's modules");
            var (ox, oy, oz) = Origin(W1.X, W1.Y, W1.Z);
            RepairBay(_ship, ox, oy, oz, 2, 5);
        }
        if (waves >= 2 && _shipBatteries == 1 && _stationBatteries >= 1)
        {
            Goal("more defence: second ship battery");
            var (ox, oy, oz) = Origin(W1.X, W1.Y, W1.Z);
            _shipBatteries += AmmoBattery(_ship, ox, oy, oz, 2).Count > 0 ? 1 : 0;
        }
        if (waves >= 2 && _stationBatteries == 1 && _shipBatteries >= 2)
        {
            Goal("more defence: second station battery");
            var (ox, oy, oz) = Origin(0, 0, 0);
            _stationBatteries += AmmoBattery(_station, ox, oy, oz, 1).Count > 0 ? 1 : 0;
        }
    }

    // ---- 4: laboratory ----------------------------------------------------------------------------------

    private void Laboratory()
    {
        Goal("4 laboratory: workshop module, plate line and lab");
        if (PlaceModule(_ship, W2.X, W2.Y, W2.Z, ModuleKind.Workshop) < 0) return;
        var (ox, oy, oz) = Origin(W2.X, W2.Y, W2.Z);
        PlateLine(_ship, ox, oy, oz, 1, "core:research_lab", out _lab);
    }

    // ---- research ----------------------------------------------------------------------------------------

    private readonly List<(bool Machine, int Id)> _feedParts = new();

    /// <summary>Lab input faces: rows of the lab workshop whose belts end beside the lab at (7,3,1).</summary>
    private static readonly int[] FeedRows = { 1, 2, 0 };

    private (int X, int Y, int Z) LabOrigin => Origin(W2.X, W2.Y, W2.Z);

    private void ClearFeeds()
    {
        foreach (var (isMachine, id) in _feedParts)
        {
            if (isMachine) { if (Net.IsMachineAlive(id)) Cmd(new RemoveMachine(id)); }
            else if (Net.IsAlive(id)) Cmd(new RemoveSegment(id));
        }
        _feedParts.Clear();
    }

    /// <summary>A raw item port feeding the lab: port at x3 of a row of the lab workshop (y=3), a belt along the row into the lab.</summary>
    private bool FeedRaw(int row, string port)
    {
        var (ox, oy, oz) = LabOrigin;
        int end = row == 1 ? 6 : 7;
        int p = PlaceMachine(_ship, ox + 3, oy + 3, oz + row, port);
        if (p < 0) return false;
        _feedParts.Add((true, p));
        int b = Link(_ship, p, ox + 4, oy + 3, oz + row, Dir.PosX, end - 4 + 1, _lab);
        if (b >= 0) _feedParts.Add((false, b));
        return b >= 0;
    }

    /// <summary>A port, a smelter and a belt into the lab, for ingots of a new metal.</summary>
    private bool FeedIngots(int row, string port, string recipe)
    {
        var (ox, oy, oz) = LabOrigin;
        int end = row == 1 ? 6 : 7;
        var (s, belt) = SmelterRow(_ship, ox, oy, oz, 3, row, end, recipe, port);
        if (s < 0) return false;
        _feedParts.Add((true, s));
        _feedParts.Add((false, belt));
        // The port of the row is part of the feed too (taking the feed down leaves its cell free for the next one).
        for (int id = 0; id < Net.MachineIdLimit; id++)
            if (Net.IsMachineAlive(id) && Net.GetMachineInfo(id) is var mi && mi.Structure == _ship && mi.X == ox && mi.Y == oy + 3 && mi.Z == oz + row) _feedParts.Add((true, id));
        return !Failed(Cmd(new ConnectToMachine(belt, _lab)), "ConnectToMachine");
    }

    /// <summary>Port, smelter, plate assembler and a belt into the lab (row 2 or 0): plates of a new metal.</summary>
    private bool FeedPlates(int row, string port, string smelt, string plate)
    {
        var (ox, oy, oz) = LabOrigin;
        int p = PlaceMachine(_ship, ox, oy + 3, oz + row, port);
        int s = PlaceMachine(_ship, ox + 3, oy + 3, oz + row, "core:smelter", smelt);
        int a = PlaceMachine(_ship, ox + 5, oy + 3, oz + row, "core:assembler", plate);
        if (p < 0 || s < 0 || a < 0) return false;
        _feedParts.Add((true, p)); _feedParts.Add((true, s)); _feedParts.Add((true, a));
        int b1 = Link(_ship, p, ox + 1, oy + 3, oz + row, Dir.PosX, 2, s);
        int b2 = Link(_ship, s, ox + 4, oy + 3, oz + row, Dir.PosX, 1, a);
        int b3 = Link(_ship, a, ox + 6, oy + 3, oz + row, Dir.PosX, 2, _lab);
        foreach (int b in new[] { b1, b2, b3 }) if (b >= 0) _feedParts.Add((false, b));
        return b1 >= 0 && b2 >= 0 && b3 >= 0;
    }

    private enum FeedKind { Raw, Ingots, Plates }

    private sealed record Feed(FeedKind Kind, string Port, string? Smelt = null, string? Plate = null);

    private bool ResearchNow(string id, params Feed[] feeds)
    {
        int node = _c.ResearchIndex(id);
        if (Net.IsResearched(node)) return true;
        Goal("research " + id);
        if (!WaitUntil(() => Net.CanResearch(node), 60)) { Stuck($"cannot research {id}: requirements missing"); return false; }
        if (_lab < 0) { Stuck($"cannot research {id}: no laboratory"); return false; }
        int row = 0;
        foreach (var f in feeds)
        {
            int r = FeedRows[row++];
            int machines0 = Net.MachineIdLimit, segments0 = Net.SegmentIdLimit;
            bool built = f.Kind switch
            {
                FeedKind.Raw => FeedRaw(r, f.Port),
                FeedKind.Ingots => FeedIngots(r, f.Port, f.Smelt!),
                _ => FeedPlates(r == 1 ? 2 : r, f.Port, f.Smelt!, f.Plate!),
            };
            // Everything the feed made comes down with it (the inner belts and the row's port were not tracked one by one).
            for (int mid = machines0; mid < Net.MachineIdLimit; mid++)
                if (Net.IsMachineAlive(mid) && !_feedParts.Contains((true, mid))) _feedParts.Add((true, mid));
            for (int sid = segments0; sid < Net.SegmentIdLimit; sid++)
                if (Net.IsAlive(sid) && !_feedParts.Contains((false, sid))) _feedParts.Add((false, sid));
            if (!built) { Stuck($"could not build the lab feed ({f.Port}) for {id}"); ClearFeeds(); return false; }
        }
        if (Failed(Act(new SetResearch(node)), "SetResearch " + id)) { ClearFeeds(); return false; }
        int lastTotal = -1, lastChange = NowSeconds;
        bool ok = WaitUntil(() =>
        {
            Between();
            int total = Net.ResearchCost(node).Sum(c => Net.ResearchProgress(node, c.Item));
            if (total != lastTotal) { lastTotal = total; lastChange = NowSeconds; }
            else if (NowSeconds - lastChange > 150 && !Net.IsResearched(node))
            {
                lastChange = NowSeconds;
                TopUp(node);
            }
            return Net.IsResearched(node);
        }, 90 * 60);
        if (!ok) Stuck($"research {id} did not finish within 90 minutes (progress: {string.Join(", ", Net.ResearchCost(node).Select(c => $"{_c.Items[c.Item].Id.Split(':')[1]} {Net.ResearchProgress(node, c.Item)}/{c.Count}"))})");
        ClearFeeds();
        return ok;
    }

    /// <summary>The research stopped moving: a raw item the lab still needs is missing from the hold (something else spent it). Mine the rest.</summary>
    private void TopUp(int node)
    {
        foreach (var c in Net.ResearchCost(node))
        {
            string id = _c.Items[c.Item].Id;
            if (id is "core:iron_plate" or "core:iron_ingot" || id.StartsWith("core:science_")) continue;
            int need = c.Count - Net.ResearchProgress(node, c.Item);
            // The ore behind a processed item the lab is waiting for (silicon from quartz, plates from ore): two ore each.
            var (raw, perItem) = id switch
            {
                "core:silicon" => ("core:quartz", 2),
                "core:titanium_plate" => ("core:titanium_ore", 2),
                "core:nickel_plate" => ("core:nickel_ore", 2),
                _ => (id, 1),
            };
            if (need <= 0 || Have(raw) >= need * perItem) continue;
            Stuck($"research {_c.Research[node].Id} stalled {need} {id} short while the hold has {Have(raw)} {raw}: something else spent it; mining more");
            if (Science && raw is "core:quartz" or "core:nickel_ore" or "core:titanium_ore" && !_inExpedition && Net.HasAbility(Ability.Jump))
            {
                // Not at home: one more trip (the smelters and belts hold some of what was brought).
                Expedition($"{raw} the stalled research still needs", (raw, Have(raw) + need * perItem + 30));
                continue;
            }
            if (SystemsWith(c.Item).Contains(0) || c.Item == Item("core:carbon")) Gather(c.Item, need + 6, "a stalled research");
        }
    }

    private bool _lab2Built;

    /// <summary>A second laboratory with its own plate line, once there is ore to feed it.</summary>
    private void SecondLab()
    {
        if (_lab2Built) return;
        _lab2Built = true;
        Goal("a second laboratory with its own plate line");
        var (ox, oy, oz) = LabOrigin;
        PlateLine(_ship, ox, oy, oz, 4, "core:research_lab", out _);
    }

    private void ResearchPlan()
    {
        ResearchNow("core:logistics");
        SecondLab();
        ResearchNow("core:carbon_drilling");
        StationDefence();
        // Automation costs plates and carbon only and its bays iron: do it first, it raises the ore the plates are made of.
        FetchCarbon(26, "carbon for drone automation");
        ResearchNow("core:drone_automation", new Feed(FeedKind.Raw, "core:carbon_unloader"));
        BuildDrones();
        MoreCargoUpTo(4);
        FetchCarbon(80, "carbon for the jump drive");
        ResearchNow("core:jump_drive", new Feed(FeedKind.Raw, "core:carbon_unloader"));
        Reinforce();
        PlayRemainder();
    }
}
