using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>Building blocks: waiting for items, flying, placing and wiring, and the fixed layouts the plan uses.</summary>
public sealed partial class StructureBot
{
    private const int ModX = StationGrid.ModuleX, ModY = StationGrid.ModuleY, ModZ = StationGrid.ModuleZ;

    /// <summary>Local origin cell of a module of a structure.</summary>
    private static (int X, int Y, int Z) Origin(int mx, int my, int mz) => (mx * ModX, my * ModY, mz * ModZ);

    // ---- generic helpers ---------------------------------------------------------------------------------

    private (long X, long Y, long Z) WorldCellOf(int structure, int x, int y, int z)
    {
        var s = Net.GetStructure(structure);
        return (s.OriginX / Cell + x, s.OriginY / Cell + y, s.OriginZ / Cell + z);
    }

    /// <summary>Flies the ship to a point (world units) and stops within <paramref name="tolUnits"/>. Time passes as it flies.</summary>
    private bool FlyTo(long tx, long ty, long tz, long tolUnits, int maxSeconds = 240)
    {
        _flying++;
        try { return FlyToCore(tx, ty, tz, tolUnits, maxSeconds); }
        finally { _flying--; }
    }

    private int _flying;

    /// <summary>Seconds of game time the bot spent flying (all legs, boosted or not), for the report.</summary>
    public static double FlightSeconds;

    /// <summary>
    /// The first obstacle that blocks the straight flight from a point to another, as a waypoint above it, or null. Solid rocks, mines and
    /// drifting debris are avoided by flying over them (the world is flat, so up is always free); clouds and wrecks are not hazards.
    /// </summary>
    private (long X, long Y, long Z)? DetourOver(long fx, long fy, long fz, long tx, long ty, long tz)
    {
        if (Net.Galaxy is null || Net.ObstacleCount == 0) return null;
        int sys = Net.CurrentSystem(0);
        if (sys < 0) return null;
        long sx = tx - fx, sy = ty - fy, sz = tz - fz;
        long len2 = Math.Max(1, sx * sx + sy * sy + sz * sz);
        int worst = -1; double worstT = 2;
        long worstClear = 0;
        foreach (int i in Net.Galaxy.ObstaclesOf(sys))
        {
            var o = Net.GetObstacle(i);
            if (o.Gone || o.Behaviour is ObstacleBehaviour.Cloud or ObstacleBehaviour.Wreck) continue;
            var def = _c.World!.Kinds[o.Kind];
            long reach = o.Radius + (def.Behaviour == ObstacleBehaviour.Mine ? Math.Max(def.TriggerCells, def.BlastCells) * (long)Cell : 0) + 6L * Cell;
            // Closest point of the segment to the obstacle, in the horizontal plane (the ship passes above, so height does not count here).
            double t = Math.Clamp(((o.X - fx) * (double)sx + (o.Z - fz) * (double)sz) / Math.Max(1, (double)sx * sx + (double)sz * sz), 0, 1);
            double cx = fx + sx * t - o.X, cz = fz + sz * t - o.Z, cy = fy + sy * t - o.Y;
            double reachH = reach + 4L * Cell;
            if (cx * cx + cz * cz > reachH * reachH) continue;
            if (cy > reach) continue; // already flying above it
            if (t < worstT) { worstT = t; worst = i; worstClear = reach; }
        }
        if (worst < 0) return null;
        var w = Net.GetObstacle(worst);
        return (w.X, w.Y + worstClear + 2L * Cell, w.Z);
    }

    private bool FlyToCore(long tx, long ty, long tz, long tolUnits, int maxSeconds)
    {
        // Legs: the straight line, or a hop over the first obstacle in the way (up to four hops per flight).
        for (int hop = 0; hop < 4; hop++)
        {
            var q = Net.GetPlayerInfo(0);
            if (DetourOver(q.X, q.Y, q.Z, tx, ty, tz) is not { } wp) break;
            if (!FlyLeg(wp.X, wp.Y, wp.Z, 5L * Cell, maxSeconds)) return false;
        }
        return FlyLeg(tx, ty, tz, tolUnits, maxSeconds);
    }

    private bool FlyLeg(long tx, long ty, long tz, long tolUnits, int maxSeconds)
    {
        bool boosting = false;
        for (int i = 0; i < maxSeconds * 6; i++)
        {
            var p = Net.GetPlayerInfo(0);
            long dx = tx - p.X, dy = ty - p.Y, dz = tz - p.Z;
            long d2 = dx * dx + dy * dy + dz * dz;
            // Boost over long distances (as a player holding the key would); off well before the target so the stop is clean.
            bool wantBoost = _c.World is not null && d2 > (long)30 * Cell * 30 * Cell;
            if (wantBoost != boosting) { Cmd(new SetBoost(wantBoost)); boosting = wantBoost; }
            if (d2 <= tolUnits * tolUnits) { Cmd(new SetThrust(0, 0, 0)); if (boosting) Cmd(new SetBoost(false)); return true; }
            if (!Net.CanFly(0)) { Stuck("the ship cannot fly (no cockpit/engine or lost)"); Cmd(new SetThrust(0, 0, 0)); if (boosting) Cmd(new SetBoost(false)); return false; }
            long len = (long)Math.Sqrt(d2);
            // Slow down near the target so a step cannot overshoot the tolerance.
            long scale = Math.Min(1000, Math.Max(40, len * 1000 / (Cell * 6)));
            if (boosting) scale = 1000;
            int sx = (int)(dx * scale / len), sy = (int)(dy * scale / len), sz = (int)(dz * scale / len);
            if (i % 120 == 0) Say($"flying: ship ({p.X},{p.Y},{p.Z}) -> ({tx},{ty},{tz}) thrust ({sx},{sy},{sz}) can fly {Net.CanFly(0)}");
            _w.Submit(0, new SetThrust(sx, sy, sz));
            _w.Step(5);
            FlightSeconds += 5.0 / Tps;
            EverySecondIfDue(ref _flyTicks, 5);
        }
        Cmd(new SetThrust(0, 0, 0));
        if (boosting) Cmd(new SetBoost(false));
        var q = Net.GetPlayerInfo(0);
        Stuck($"flying to the target took too long (ship {q.X / Cell},{q.Y / Cell},{q.Z / Cell} -> {tx / Cell},{ty / Cell},{tz / Cell}, speed {Net.ShipSpeedUnitsPerTick(0)} units/tick, satisfaction {Net.ShipPower(0).SatisfactionPermille})");
        return false;
    }

    private int _flyTicks;

    private void EverySecondIfDue(ref int counter, int ticks)
    {
        counter += ticks;
        if (counter < Tps) return;
        counter -= Tps;
        EverySecond();
    }

    private (long X, long Y, long Z) DepositCentre(int dep)
    {
        var (x, y, z) = Net.DepositPosition(dep);
        return ((long)x * Cell, (long)y * Cell, (long)z * Cell);
    }

    /// <summary>Flies within mining range of a deposit and starts hand mining it. Hand mining stays on it until told otherwise.</summary>
    private bool GoMine(int deposit)
    {
        if (deposit < 0) return false;
        _mineTarget = deposit;
        if (!Net.InMineRange(0, deposit))
        {
            Cmd(new StopMining());
            var (x, y, z) = DepositCentre(deposit);
            if (!FlyTo(x, y, z, 3L * Cell)) return false;
        }
        var r = Cmd(new StartMining(deposit));
        if (!r.Ok && r.Reject != Reject.Depleted) { Stuck($"StartMining was rejected: {r.Reject}"); return false; }
        return true;
    }

    private int FindDeposit(int item, int system, bool revealedOnly = true, int nearTo = -1)
    {
        var g = Net.Galaxy!;
        int best = -1;
        long bestD = long.MaxValue;
        for (int d = 0; d < Net.DepositCount; d++)
        {
            if (Net.DepositItem(d) != item || Net.DepositRemaining(d) <= 0) continue;
            if (revealedOnly && !Net.IsDepositRevealed(d)) continue;
            if (g.Deposits[d].System != system) continue;
            var (x, y, z) = Net.DepositPosition(d);
            var p = Net.GetPlayerInfo(0);
            long dx = x * (long)Cell - p.X, dy = y * (long)Cell - p.Y, dz = z * (long)Cell - p.Z;
            long dist = dx * dx + dy * dy + dz * dz;
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    /// <summary>Waits until the hold can pay the cost. Mining and the factory run meanwhile. False after the timeout (recorded as stuck).</summary>
    private bool Need(ItemStack[] cost, string what, int times = 1, int maxSeconds = 2400)
    {
        foreach (var s in cost)
            if (s.Count * times > PlayerRules.DepotItemCap && Net.HoldCapacity(0) < s.Count * times)
                Stuck($"{what} costs {s.Count * times} {_c.Items[s.Item].Id}, more than the hold can ever carry");
        if (WaitUntil(() => Net.CanPay(0, cost, times), maxSeconds)) return true;
        Stuck($"could not afford {what} within {maxSeconds / 60} minutes (hold: {string.Join(", ", cost.Select(c => $"{_c.Items[c.Item].Id.Split(':')[1]} {Net.HoldCount(0, c.Item)}/{c.Count * times}"))})");
        return false;
    }

    private ItemStack[] CostOf(string machine) => _c.Machines[Machine(machine)].Cost;

    // ---- placing ---------------------------------------------------------------------------------------

    /// <summary>Moves the ship close enough to build at a local cell if it is not already.</summary>
    private void EnsureRange(int structure, int x, int y, int z)
    {
        var (wx, wy, wz) = WorldCellOf(structure, x, y, z);
        if (Net.CellInRange(0, (int)wx, (int)wy, (int)wz, PlayerRules.BuildRangeCells - 2)) return;
        Say($"out of build range of cell ({x},{y},{z}) in structure {structure}: flying closer");
        var (tx, ty, tz) = (wx * Cell, wy * Cell, wz * Cell);
        // Stop a little short, towards the ship.
        var p = Net.GetPlayerInfo(0);
        FlyTo(tx - Math.Sign(tx - p.X) * 8L * Cell, ty, tz - Math.Sign(tz - p.Z) * 8L * Cell, 4L * Cell);
    }

    /// <summary>Returns to where the ship hand mines (its first mining spot).</summary>
    private void ReturnHome()
    {
        if (_homeMine is not { } h) return;
        if (!Net.InMineRange(0, _homeDeposit)) FlyTo(h.X, h.Y, h.Z, Cell / 2);
        _mineTarget = _homeDeposit;
    }

    private (long X, long Y, long Z)? _homeMine;
    private int _homeDeposit = -1;

    private int PlaceMachine(int st, int x, int y, int z, string machine, string? recipe = null)
    {
        int type = Machine(machine);
        int r = recipe is null ? -1 : Recipe(recipe);
        if (!Need(_c.Machines[type].Cost, machine)) return -1;
        EnsureRange(st, x, y, z);
        var res = Act(new PlaceMachineIn(st, x, y, z, type, r));
        if (!res.Ok)
        {
            if (res.Reject == Reject.NotEnoughItems) return PlaceMachine(st, x, y, z, machine, recipe);
            string at = "";
            if (res.Reject == Reject.Overlap)
                for (int id = 0; id < Net.MachineIdLimit; id++)
                    if (Net.IsMachineAlive(id) && Net.GetMachineInfo(id) is var mi && mi.Structure == st && mi.X == x && mi.Y == y && mi.Z == z) at = $" (cell held by {_c.Machines[mi.Type].Id}, machine {id})";
            Stuck($"PlaceMachineIn {machine} at ({x},{y},{z}) of structure {st} was rejected: {res.Reject}{at}");
            return -1;
        }
        return res.CreatedMachines[0];
    }

    private int PlaceBelt(int st, int x, int y, int z, Dir dir, int len)
    {
        var cost = _c.Belts.First(b => b is not null && b.Tier == BeltTier.Mk1)!.CostPerCell;
        if (!Need(cost.Select(c => new ItemStack(c.Item, c.Count * len)).ToArray(), "a belt")) return -1;
        EnsureRange(st, x, y, z);
        var res = Act(new PlaceSegmentIn(st, x, y, z, dir, len, BeltTier.Mk1));
        if (!res.Ok)
        {
            if (res.Reject == Reject.NotEnoughItems) return PlaceBelt(st, x, y, z, dir, len);
            Stuck($"PlaceSegmentIn at ({x},{y},{z}) {dir} x{len} of structure {st} was rejected: {res.Reject}");
            return -1;
        }
        return res.Created[0];
    }

    /// <summary>A belt from a producer machine into a consumer machine. Returns the belt id or -1.</summary>
    private int Link(int st, int from, int x, int y, int z, Dir dir, int len, int to)
    {
        if (from < 0 || to < 0) return -1;
        int b = PlaceBelt(st, x, y, z, dir, len);
        if (b < 0) return -1;
        if (Failed(Cmd(new ConnectFromMachine(from, b)), "ConnectFromMachine")) return -1;
        if (Failed(Cmd(new ConnectToMachine(b, to)), "ConnectToMachine")) return -1;
        return b;
    }

    private int PlaceModule(int st, int mx, int my, int mz, ModuleKind kind)
    {
        var kinds = _c.Structures!;
        if (!Need(kinds.ModuleCost(kind), $"a {kind} module")) return -1;
        var s = Net.GetStructure(st);
        if (s.Modules.ContainsKey((mx, my, mz))) return 0;
        var (ox, oy, oz) = Origin(mx, my, mz);
        EnsureRange(st, ox + 4, oy + 2, oz + 4);
        var res = Act(new PlaceModuleIn(st, mx, my, mz, kind));
        if (!res.Ok)
        {
            if (res.Reject == Reject.NotEnoughItems) return PlaceModule(st, mx, my, mz, kind);
            Stuck($"PlaceModuleIn {kind} at ({mx},{my},{mz}) of structure {st} was rejected: {res.Reject}");
            return -1;
        }
        return 1;
    }

    // ---- layouts ---------------------------------------------------------------------------------------

    /// <summary>One smelter row, ore port to a smelter to the belt that leaves it: port x0, belt x1-2, smelter x3, belt x4 to <paramref name="endX"/>.
    /// The caller connects the last belt to its consumer. Returns (smelter, last belt) or (-1, -1).</summary>
    private (int Smelter, int Belt) SmelterRow(int st, int ox, int oy, int oz, int y, int z, int endX, string recipe = "core:smelt_iron", string port = "core:ore_unloader")
    {
        int p = PlaceMachine(st, ox, oy + y, oz + z, port);
        int s = PlaceMachine(st, ox + 3, oy + y, oz + z, "core:smelter", recipe);
        if (p < 0 || s < 0) return (-1, -1);
        if (Link(st, p, ox + 1, oy + y, oz + z, Dir.PosX, 2, s) < 0) return (-1, -1);
        int outBelt = PlaceBelt(st, ox + 4, oy + y, oz + z, Dir.PosX, endX - 4 + 1);
        if (outBelt < 0 || Failed(Cmd(new ConnectFromMachine(s, outBelt)), "ConnectFromMachine")) return (-1, -1);
        return (s, outBelt);
    }

    /// <summary>Ore port, smelter, depot on one row of a module: 0.5 ingots a second into the hold.</summary>
    private bool IngotLine(int st, int ox, int oy, int oz, int y, int z)
    {
        var (s, belt) = SmelterRow(st, ox, oy, oz, y, z, 6);
        if (s < 0) return false;
        _ingotSmelters.Add(s);
        int depot = PlaceMachine(st, ox + 7, oy + y, oz + z, "core:depot");
        return depot >= 0 && !Failed(Cmd(new ConnectToMachine(belt, depot)), "ConnectToMachine");
    }

    /// <summary>
    /// Two smelter rows (z and z+1, at y = oy+1) feeding one plate assembler at x7, whose output climbs one cell into a machine at (7, 3, z):
    /// a laboratory or a depot. 0.5 plates a second at best.
    /// </summary>
    private int PlateLine(int st, int ox, int oy, int oz, int z, string sink, out int sinkId)
    {
        sinkId = -1;
        int asm = PlaceMachine(st, ox + 7, oy + 1, oz + z, "core:assembler", "core:iron_plate");
        if (asm < 0) return -1;
        var (s1, b1) = SmelterRow(st, ox, oy, oz, 1, z, 6);
        if (s1 < 0 || Failed(Cmd(new ConnectToMachine(b1, asm)), "ConnectToMachine")) return -1;
        var (s2, b2) = SmelterRow(st, ox, oy, oz, 1, z + 1, 7);
        if (s2 < 0 || Failed(Cmd(new ConnectToMachine(b2, asm)), "ConnectToMachine")) return -1;
        sinkId = PlaceMachine(st, ox + 7, oy + 3, oz + z, sink);
        if (sinkId < 0) return -1;
        if (Link(st, asm, ox + 7, oy + 2, oz + z, Dir.PosY, 1, sinkId) < 0) return -1;
        return asm;
    }

    /// <summary>
    /// An ammo battery on the top layer (y=3) of a workshop or core module under a defence module (at my+1): an ingot port, an assembler
    /// making ammo, and two belts into two turrets standing in the defence module above. Returns the turret ids.
    /// </summary>
    private List<int> AmmoBattery(int st, int ox, int oy, int oz, int z, int turrets = 2)
    {
        var made = new List<int>();
        int port = PlaceMachine(st, ox, oy + 3, oz + z, "core:unloader");
        int asm = PlaceMachine(st, ox + 2, oy + 3, oz + z, "core:assembler", "core:make_ammo");
        if (port < 0 || asm < 0) return made;
        if (Link(st, port, ox + 1, oy + 3, oz + z, Dir.PosX, 1, asm) < 0) return made;
        int t1 = PlaceMachine(st, ox + 5, oy + 4, oz + z, "core:turret");
        if (t1 < 0) return made;
        if (Link(st, asm, ox + 3, oy + 3, oz + z, Dir.PosX, 3, t1) < 0) return made;
        made.Add(t1);
        if (turrets < 2) return made;
        int t2 = PlaceMachine(st, ox + 5, oy + 4, oz + z + 1, "core:turret");
        if (t2 < 0) return made;
        if (Link(st, asm, ox + 2, oy + 3, oz + z + 1, Dir.PosX, 4, t2) < 0) return made;
        made.Add(t2);
        return made;
    }

    /// <summary>An ingot port feeding a repair bay on the layer y: port x0, belt x1-2, bay x3.</summary>
    private int RepairBay(int st, int ox, int oy, int oz, int y, int z)
    {
        int port = PlaceMachine(st, ox, oy + y, oz + z, "core:unloader");
        int bay = PlaceMachine(st, ox + 3, oy + y, oz + z, "core:repair_bay");
        if (port < 0 || bay < 0) return -1;
        return Link(st, port, ox + 1, oy + y, oz + z, Dir.PosX, 2, bay) < 0 ? -1 : bay;
    }

    /// <summary>The ice loop of the ship's workshop: ice port, belt, cracker, belt into the starter depot, so hydrogen reaches the hold.</summary>
    private bool IceLoop(int st, int ox, int oy, int oz)
    {
        // The starter depot stands at local (7,1,2) of the starter workshop; a belt along x at y=2 ends right above it.
        int depot = -1;
        for (int id = 0; id < Net.MachineIdLimit; id++)
            if (Net.IsMachineAlive(id) && Net.GetMachineInfo(id).Structure == st && _c.Machines[Net.GetMachineInfo(id).Type].Role == MachineRole.Depot) depot = id;
        if (depot < 0) { Stuck("no depot to deliver hydrogen into"); return false; }
        int port = PlaceMachine(st, ox, oy + 2, oz + 2, "core:ice_unloader");
        int cracker = PlaceMachine(st, ox + 3, oy + 2, oz + 2, "core:hydrogen_cracker", "core:crack_ice");
        if (port < 0 || cracker < 0) return false;
        if (Link(st, port, ox + 1, oy + 2, oz + 2, Dir.PosX, 2, cracker) < 0) return false;
        return Link(st, cracker, ox + 4, oy + 2, oz + 2, Dir.PosX, 4, depot) >= 0;
    }
}
