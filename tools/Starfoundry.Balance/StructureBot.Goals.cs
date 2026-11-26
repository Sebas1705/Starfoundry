using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>The plan: named goals in order, each guarded by "can I afford it / is it unlocked".</summary>
public sealed partial class StructureBot
{
    // Module plan (module grid coordinates; the starter ship stands at (0,0,0..4): cockpit, hull, cargo, reactor, engine). Bays are beside the hull,
    // defence modules beside their bay so the ammo belts cross the seam on the floor (ADR 0031).
    private static readonly (int X, int Y, int Z) F1 = (1, 0, 1);        // the ship's first fabrication bay (the factory)
    private static readonly (int X, int Y, int Z) D1 = (2, 0, 1);        // ship defence module, beside F1
    private static readonly (int X, int Y, int Z) W2 = (-1, 0, 1);       // laboratory bay
    private static readonly (int X, int Y, int Z) W3 = (-1, 0, 2);       // second laboratory bay
    private static readonly (int X, int Y, int Z) R2 = (-1, 0, 4);       // second reactor
    private static readonly (int X, int Y, int Z) B1 = (1, 0, 2);        // ship drone bay
    private static readonly (int X, int Y, int Z) S1 = (1, 0, 0);        // the station's bay, beside the core
    private static readonly (int X, int Y, int Z) SD = (2, 0, 0);        // the station's defence module, beside S1

    /// <summary>The ship's first bay: where the template put it (levels 2 and 3) or the one the bot builds.</summary>
    private (int X, int Y, int Z) ShipBay => Net.GetStructure(_ship).Modules.TryGetValue((0, 0, 1), out var k) && k == ModuleKind.Workshop ? (0, 0, 1) : F1;

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
        BuildBay();
        HydrogenLoop();
        FoundStation();
        MoreCargo(2);
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
        Cmd(new SetShipRecipe(Recipe("core:ship_smelt"))); // the workbench makes ingots from the hold's ore, slowly
        WaitUntil(() => Net.HoldCount(0, Item("core:iron_plate")) > 0, 300);
    }

    /// <summary>
    /// The first minutes of the new start (ADR 0031): the workbench makes the ingots and plates a fabrication bay costs, the bay goes beside the hull,
    /// and its starter factory (ore port, smelter, assembler, depot) takes over. Until then <see cref="BenchPolicy"/> picks the workbench recipe.
    /// </summary>
    private void BuildBay()
    {
        Goal("1b workbench: ingots and plates, then a fabrication bay and its starter factory");
        if (PlaceModule(_ship, F1.X, F1.Y, F1.Z, ModuleKind.Workshop) < 0) return;
        Milestone("Fabrication bay built");
        var (ox, oy, oz) = Origin(F1.X, F1.Y, F1.Z);
        if (!StarterFactory(_ship, ox, oy, oz)) return;
        WaitUntil(() => Net.DepotDelivered > 0, 240);
        _factoryUp = true;
        Cmd(new SetShipRecipe(-1)); // the factory makes ingots and plates now
        Milestone("Factory running: first delivery from a depot");
    }

    private bool _benchPressing;

    /// <summary>The workbench makes one thing at a time: plates while there are ingots to press and plates are short, otherwise ingots.</summary>
    private void BenchPolicy()
    {
        if (_factoryUp || !Net.HasPlayer(0)) return;
        int ingots = Have("core:iron_ingot"), plates = Have("core:iron_plate"), ore = Have("core:iron_ore");
        bool wantPlates = plates < BenchPlateTarget && (_benchPressing ? ingots >= 2 : ingots >= 16);
        _benchPressing = wantPlates;
        int want = wantPlates ? Recipe("core:ship_press") : ore > 0 && ingots < BenchIngotTarget ? Recipe("core:ship_smelt") : -1;
        if (Net.GetPlayerInfo(0).ShipRecipe != want) Cmd(new SetShipRecipe(want));
    }

    /// <summary>Plates the workbench keeps in the hold before the factory runs (a bay needs 8, a station core 12).</summary>
    public int BenchPlateTarget { get; init; } = 12;

    /// <summary>Ingots the workbench keeps in the hold before the factory runs.</summary>
    public int BenchIngotTarget { get; init; } = 60;

    /// <summary>Hand-mines another deposit until the hold has enough of its item, then goes back to the iron deposit (at home).</summary>
    private bool Gather(int item, int count, string why, int system = 0, Func<bool>? enough = null)
    {
        int dep = FindDeposit(item, system);
        if (dep < 0 && system != 0 && system == CurrentSystem && Net.Galaxy!.Wide) { Survey(item, system); dep = FindDeposit(item, system); }
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
            return;
        }
        // Last resort, only for fuel: with no hydrogen the reactors die, and a hold full of expedition cargo cannot mine the ice that
        // would bring it back. Throw half of the biggest raw stack out (a second trip is cheaper than a dead ship).
        if (wanted == Item("core:ice"))
        {
            var (id, count) = new[] { "core:quartz", "core:nickel_ore", "core:titanium_ore" }.Select(i => (Id: i, Count: Have(i))).OrderByDescending(p => p.Count).First();
            if (count < 20) return;
            Stuck($"the hold is at the mining limit while gathering ice with no room left: dumping {count / 2} {id} (hold {Net.HoldTotal(0)}/{Net.HoldCapacity(0)})");
            Cmd(new DumpItems(Item(id), count / 2));
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
        // The starter smelter also feeds the plate assembler that builds need plates from: it stays on while plates are short.
        if (!_smeltersOff && ingots >= 70 && plates >= 30 || _smeltersOff && (ingots <= 36 || plates < 20))
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
        // Ahead of the cockpit, past the nose laser module (ADR 0032), inside build range of everything. A deposit's rock is solid (ADR 0033): the station grows to about
        // 40 by 20 by 40 cells (four modules along x, the defence and the wings), so a player looks for a spot whose whole footprint is clear of the rocks (the green volume
        // would turn red), and so does the bot: the clear spot nearest to the default one, with the core inside build range and clear of the ship.
        int px = FloorCell(p.X), py = FloorCell(p.Y), pz = FloorCell(p.Z);
        var ship = Net.GetStructure(Net.ShipStructure(0));
        var shipBoxes = ship.Modules.Keys.Select(k => Net.ModuleWorldBounds(ship, k)).ToArray();
        bool Footprint(int cx, int cy, int cz) =>
            Net.BoxTouchesDepositRock(cx * (long)Cell, (cy - 8) * (long)Cell, (cz - 20) * (long)Cell, (cx + 40) * (long)Cell, (cy + 12) * (long)Cell, (cz + 20) * (long)Cell, 1L * Cell);
        bool ClearOfShip(int cx, int cy, int cz) =>
            shipBoxes.All(bx => !(cx * (long)Cell < bx.Max.X && bx.Min.X < (cx + 8) * (long)Cell && cy * (long)Cell < bx.Max.Y && bx.Min.Y < (cy + 4) * (long)Cell && cz * (long)Cell < bx.Max.Z && bx.Min.Z < (cz + 8) * (long)Cell));
        var candidates = new List<(int X, int Y, int Z)>();
        for (int dx = -24; dx <= 24; dx += 2)
            for (int dy = -8; dy <= 8; dy += 4)
                for (int dz = -34; dz <= 8; dz += 2)
                {
                    double ex = dx + 4, ey = dy + 2, ez = dz + 4; // from the ship to the centre of the core
                    if (ex * ex + ey * ey + ez * ez > 21.0 * 21.0) continue;
                    candidates.Add((px + dx, py + dy, pz + dz));
                }
        // The nearest clear spot to the ship, which hand mines beside its rock: every later build trip starts from there, so a closer station means shorter trips.
        var order = candidates.OrderBy(c => (c.X + 4 - px) * (c.X + 4 - px) + (c.Y + 2 - py) * (c.Y + 2 - py) * 4 + (c.Z + 4 - pz) * (c.Z + 4 - pz)).ToList();
        CommandResult r = null!;
        foreach (var c in order.Where(c => ClearOfShip(c.X, c.Y, c.Z) && !Footprint(c.X, c.Y, c.Z)).Concat(new List<(int X, int Y, int Z)> { (px, py, pz - 16) }))
        {
            r = Cmd(new FoundStation(c.X, c.Y, c.Z));
            if (r.Ok || r.Reject is not (Reject.OverlapsDeposit or Reject.Overlap or Reject.OutOfRange)) break;
        }
        Wait(ActionDelaySeconds);
        if (Failed(r, "FoundStation")) return;
        _station = Net.Structures().First(s => s.Kind == StructureKind.Station && s.Owner == 0).Id;
    }

    private void StationPower()
    {
        if (_station < 0) return;
        Goal("2b station: fabrication bay and reactor module (and hydrogen in the hold)");
        PlaceModule(_station, S1.X, S1.Y, S1.Z, ModuleKind.Workshop);
        if (!Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for the station reactor")) return;
        PlaceModule(_station, 0, -1, 0, ModuleKind.Reactor);
        // Something to power: an ingot line in the new workshop.
        var (ox, oy, oz) = Origin(S1.X, S1.Y, S1.Z);
        IngotLine(_station, ox, oy, oz, 0, 1);
    }

    // ---- 3: ingots and the ice loop ---------------------------------------------------------------------

    private void HydrogenLoop()
    {
        Goal("3 hydrogen: ice port + cracker wired into the depot, then ice by hand");
        var (ox, oy, oz) = Origin(ShipBay.X, ShipBay.Y, ShipBay.Z);
        if (!IceLoop(_ship, ox, oy, oz, ShipBay == F1 ? 3 : 5)) return;
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

    private static readonly (int X, int Y, int Z)[] CargoSpots = { (1, 0, 3), (1, 0, 4), (-1, 0, 3), (-1, 0, 0) };

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
        var cells = new (int X, int Y, int Z)[] { (1, 0, 0), (6, 0, 0), (1, 0, 2), (6, 0, 2) };
        for (int i = 0; i < Math.Min(count, cells.Length); i++)
            PlaceMachine(st, ox + cells[i].X, oy + cells[i].Y, oz + cells[i].Z, "core:wall");
    }

    private void ShipDefence()
    {
        Goal("5 ship defence: defence module, an ammo battery of two turrets, walls, weapons on");
        if (PlaceModule(_ship, D1.X, D1.Y, D1.Z, ModuleKind.Defence) < 0) return;
        var (ox, oy, oz) = Origin(F1.X, F1.Y, F1.Z);
        _shipBatteries += AmmoBattery(_ship, ox, oy, oz, 4).Count > 0 ? 1 : 0;
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
        if (PlaceModule(_station, SD.X, SD.Y, SD.Z, ModuleKind.Defence) < 0) return;
        var (ox, oy, oz) = Origin(S1.X, S1.Y, S1.Z);
        _stationBatteries += AmmoBattery(_station, ox, oy, oz, 4).Count > 0 ? 1 : 0;
        var (dx, dy, dz) = Origin(SD.X, SD.Y, SD.Z);
        Walls(_station, dx, dy, dz, 2);
        RepairBay(_station, ox, oy, oz, 3);
    }


    private int _nextIceTripAt;

    private int HomeIceLeft()
    {
        int left = 0;
        for (int d = 0; d < Net.Galaxy!.Deposits.Count; d++)
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
        if (_stationBatteries < 1 && _station >= 0 && Net.Structures().Any(s => s.Id == _station && s.Modules.ContainsKey(S1)) && waves >= 0 && _shipBatteries >= 1)
        {
            if (Net.HoldCount(0, Item("core:iron_plate")) >= 14) StationDefence();
        }
        if (waves >= 1 && !_shipRepair && _shipBatteries >= 1)
        {
            _shipRepair = true;
            Goal("a repair bay for the ship's modules");
            var (ox, oy, oz) = Origin(W2.X, W2.Y, W2.Z);
            RepairBay(_ship, ox, oy, oz, 7);
        }
        if (waves >= 2 && _shipBatteries == 1 && _stationBatteries >= 1)
        {
            Goal("more defence: second ship battery");
            var (ox, oy, oz) = Origin(F1.X, F1.Y, F1.Z);
            _shipBatteries += AmmoBattery(_ship, ox, oy, oz, 6).Count > 0 ? 1 : 0;
        }
        if (waves >= 2 && _stationBatteries == 1 && _shipBatteries >= 2)
        {
            Goal("more defence: second station battery");
            var (ox, oy, oz) = Origin(S1.X, S1.Y, S1.Z);
            _stationBatteries += AmmoBattery(_station, ox, oy, oz, 6).Count > 0 ? 1 : 0;
        }
    }

    // ---- 4: laboratory ----------------------------------------------------------------------------------

    private void Laboratory()
    {
        Goal("4 laboratory: fabrication bay, plate line and lab");
        if (PlaceModule(_ship, W2.X, W2.Y, W2.Z, ModuleKind.Workshop) < 0) return;
        var (ox, oy, oz) = Origin(W2.X, W2.Y, W2.Z);
        PlateLine(_ship, ox, oy, oz, 4, "core:research_lab", out _lab);
    }

    // ---- research ----------------------------------------------------------------------------------------

    /// <summary>One feed of the laboratory: the faces it occupies and the parts it is made of, so it can come down when the lab has what it wanted.</summary>
    private sealed class ActiveFeed
    {
        public Feed Feed = null!;
        public int Face;
        public readonly List<(bool Machine, int Id)> Parts = new();
    }

    private readonly List<ActiveFeed> _activeFeeds = new();

    /// <summary>
    /// Lab input faces: the lab stands at (7, 2) of its bay, the plate line comes in from (7, 3); a belt along row 1 ends at (7, 1) and one along
    /// row 2 at (6, 2), both beside the lab. Two feeds at once; a research with more feeds builds the rest as the first ones are satisfied.
    /// </summary>
    private static readonly int[] FeedRows = { 1, 2 };

    private (int X, int Y, int Z) LabOrigin => Origin(W2.X, W2.Y, W2.Z);

    private void ClearFeed(ActiveFeed f)
    {
        foreach (var (isMachine, id) in f.Parts)
        {
            if (isMachine) { if (Net.IsMachineAlive(id)) Cmd(new RemoveMachine(id)); }
            else if (Net.IsAlive(id)) Cmd(new RemoveSegment(id));
        }
        f.Parts.Clear();
        _activeFeeds.Remove(f);
    }

    private void ClearFeeds()
    {
        foreach (var f in _activeFeeds.ToArray()) ClearFeed(f);
    }

    /// <summary>Builds a feed on a face of the lab bay, tracking everything it placed. Returns the feed or null.</summary>
    private ActiveFeed? BuildFeed(Feed f, int row)
    {
        var (ox, oy, oz) = LabOrigin;
        int machines0 = Net.MachineIdLimit, segments0 = Net.SegmentIdLimit;
        int endX = row == 1 ? 7 : 6; // the last belt cell stands beside the lab
        bool built;
        switch (f.Kind)
        {
            case FeedKind.Raw:
            {
                int p = PlaceMachine(_ship, ox + 3, oy, oz + row, f.Port);
                built = p >= 0 && Link(_ship, p, ox + 4, oy, oz + row, Dir.PosX, endX - 4 + 1, _lab) >= 0;
                break;
            }
            case FeedKind.Ingots:
            {
                var (s, belt) = SmelterRow(_ship, ox, oy, oz, 0, row, endX, f.Smelt!, f.Port);
                built = s >= 0 && !Failed(Cmd(new ConnectToMachine(belt, _lab)), "ConnectToMachine");
                break;
            }
            default: // plates: port, smelter, assembler, then the belt that ends beside the lab
            {
                var (s, belt) = SmelterRow(_ship, ox, oy, oz, 0, row, 4, f.Smelt!, f.Port);
                int a = s < 0 ? -1 : PlaceMachine(_ship, ox + 5, oy, oz + row, "core:assembler", f.Plate);
                built = a >= 0 && !Failed(Cmd(new ConnectToMachine(belt, a)), "ConnectToMachine")
                    && Link(_ship, a, ox + 6, oy, oz + row, Dir.PosX, endX - 6 + 1, _lab) >= 0;
                break;
            }
        }
        var active = new ActiveFeed { Feed = f, Face = row };
        // Everything the feed made comes down with it (the inner belts and the row's port were not tracked one by one).
        for (int mid = machines0; mid < Net.MachineIdLimit; mid++)
            if (Net.IsMachineAlive(mid)) active.Parts.Add((true, mid));
        for (int sid = segments0; sid < Net.SegmentIdLimit; sid++)
            if (Net.IsAlive(sid)) active.Parts.Add((false, sid));
        _activeFeeds.Add(active);
        if (built) return active;
        ClearFeed(active);
        return null;
    }

    private enum FeedKind { Raw, Ingots, Plates }

    /// <summary>A lab feed: <paramref name="Delivers"/> is the research item it brings (so the bot knows when the lab has enough of it).</summary>
    private sealed record Feed(FeedKind Kind, string Port, string Delivers, string? Smelt = null, string? Plate = null);

    private bool ResearchNow(string id, params Feed[] feeds)
    {
        int node = _c.ResearchIndex(id);
        if (Net.IsResearched(node)) return true;
        Goal("research " + id);
        if (!WaitUntil(() => Net.CanResearch(node), 60)) { Stuck($"cannot research {id}: requirements missing"); return false; }
        if (_lab < 0) { Stuck($"cannot research {id}: no laboratory"); return false; }
        var pending = new Queue<Feed>(feeds);
        bool Satisfied(Feed f)
        {
            int item = Item(f.Delivers);
            foreach (var c in Net.ResearchCost(node))
                if (c.Item == item) return Net.ResearchProgress(node, item) >= c.Count;
            return true;
        }
        // Build what the faces take: the feeds that have the lab's attention first, the rest as faces free up.
        bool FillFaces()
        {
            while (pending.Count > 0 && _activeFeeds.Count < FeedRows.Length)
            {
                int face = FeedRows.First(r => _activeFeeds.All(a => a.Face != r));
                var f = pending.Dequeue();
                if (Satisfied(f)) continue;
                if (BuildFeed(f, face) is null) { Stuck($"could not build the lab feed ({f.Port}) for {id}"); return false; }
            }
            return true;
        }
        if (!FillFaces()) { ClearFeeds(); return false; }
        if (Failed(Act(new SetResearch(node)), "SetResearch " + id)) { ClearFeeds(); return false; }
        int lastTotal = -1, lastChange = NowSeconds;
        bool ok = WaitUntil(() =>
        {
            Between();
            // A feed whose item the lab has all of comes down; the next waiting feed takes its face.
            foreach (var a in _activeFeeds.ToArray())
                if (Satisfied(a.Feed)) ClearFeed(a);
            if (pending.Count > 0) FillFaces();
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
            if (raw is "core:quartz" or "core:nickel_ore" or "core:titanium_ore" && !_inExpedition && Net.HasAbility(Ability.Jump))
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
        if (PlaceModule(_ship, W3.X, W3.Y, W3.Z, ModuleKind.Workshop) < 0) return;
        var (ox, oy, oz) = Origin(W3.X, W3.Y, W3.Z);
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
        ResearchNow("core:drone_automation", CarbonRaw);
        BuildDrones();
        MoreCargoUpTo(4);
        FetchCarbon(80, "carbon for the jump drive");
        ResearchNow("core:jump_drive", CarbonRaw);
        Reinforce();
        PlayRemainder();
    }
}
