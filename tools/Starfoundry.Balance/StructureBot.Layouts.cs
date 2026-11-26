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
    private bool FlyTo(long tx, long ty, long tz, long tolUnits, int maxSeconds = 240, Func<bool>? done = null)
    {
        _flying++;
        _flyDone = done;
        double before = FlightSeconds;
        try { return FlyToCore(tx, ty, tz, tolUnits, maxSeconds); }
        finally { _flying--; _flyDone = null; Say($"flight of {FlightSeconds - before:F0} s to ({tx / Cell},{ty / Cell},{tz / Cell}){(done is null ? "" : " (until the laser reaches)")}"); }
    }

    /// <summary>Ends the flight early when true (ADR 0033: a deposit is a solid rock, so the ship cannot reach its centre; it stops where its laser reaches).</summary>
    private Func<bool>? _flyDone;

    private int _flying;

    /// <summary>The distance in cells above which the bot flies boosted (a player holds the key on every longer hop).</summary>
    private const long BoostFromCells = 30;

    /// <summary>Seconds of game time the bot spent flying (all legs, boosted or not), for the report.</summary>
    public static double FlightSeconds;

    /// <summary>
    /// The first obstacle that blocks the straight flight from a point to another, as a waypoint above it, or null. Solid rocks, mines and
    /// drifting debris are avoided by flying over them (the world is flat, so up is always free); clouds and wrecks are not hazards.
    /// </summary>
    private (long X, long Y, long Z)? DetourOver(long fx, long fy, long fz, long tx, long ty, long tz)
    {
        if (Net.Galaxy is null) return null;
        int sys = Net.CurrentSystem(0);
        if (sys < 0) return null;
        long sx = tx - fx, sy = ty - fy, sz = tz - fz;
        double worstT = 2;
        (long X, long Y, long Z, long Clear)? worst = null;

        // One candidate: a sphere the ship must not enter, with the reach to clear (the radius it hits plus a margin).
        void Consider(long ox, long oy, long oz, long reach)
        {
            // Closest point of the segment to the obstacle in 3D: a leg that starts beside a rock and moves away from it, or passes it with room to spare, needs no hop over it.
            double len2 = Math.Max(1, (double)sx * sx + (double)sy * sy + (double)sz * sz);
            double t = Math.Clamp(((ox - fx) * (double)sx + (oy - fy) * (double)sy + (oz - fz) * (double)sz) / len2, 0, 1);
            if (t <= 0 && (ox - fx) * (double)sx + (oy - fy) * (double)sy + (oz - fz) * (double)sz <= 0) return; // moving away from it
            double cx = fx + sx * t - ox, cz = fz + sz * t - oz, cy = fy + sy * t - oy;
            if (cx * cx + cy * cy + cz * cz > (double)reach * reach) return;
            if (t < worstT) { worstT = t; worst = (ox, oy, oz, reach); }
        }

        foreach (int i in Net.Galaxy.ObstaclesOf(sys))
        {
            var o = Net.GetObstacle(i);
            if (o.Gone || o.Behaviour is ObstacleBehaviour.Cloud or ObstacleBehaviour.Wreck) continue;
            var def = _c.World!.Kinds[o.Kind];
            long reach = Net.ObstacleHitRadius(i) + (def.Behaviour == ObstacleBehaviour.Mine ? Math.Max(def.TriggerCells, def.BlastCells) * (long)Cell : 0) + 6L * Cell;
            Consider(o.X, o.Y, o.Z, reach);
        }
        // The rocks of deposits are solid too (ADR 0033). The one the flight ends at is not avoided: the leg stops once the laser reaches it.
        for (int d = 0; d < Net.DepositCount && !_ignoreDeposits; d++)
        {
            if (!Net.IsDepositRevealed(d) || Net.DepositRemaining(d) <= 0 || Net.DepositRockRadius(d) == 0) continue;
            var (px, py, pz) = Net.DepositPosition(d);
            long ox = px * (long)Cell, oy = py * (long)Cell, oz = pz * (long)Cell;
            long reach = Net.DepositRockRadius(d) + 6L * Cell;
            long ex = tx - ox, ey = ty - oy, ez = tz - oz;
            if (ex * ex + ey * ey + ez * ez <= (long)Cell * Cell) continue; // the flight ends at this very deposit
            Consider(ox, oy, oz, reach);
        }
        if (worst is not { } w) return null;
        return (w.X, w.Y + w.Clear + 2L * Cell, w.Z);
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
        double bestDistance = double.MaxValue;
        int lastGain = 0;
        int pins = 0;
        for (int i = 0; i < maxSeconds * 6; i++)
        {
            var p = Net.GetPlayerInfo(0);
            long dx = tx - p.X, dy = ty - p.Y, dz = tz - p.Z;
            long d2 = dx * dx + dy * dy + dz * dz;
            // No headway for ten seconds: a rock the straight line cannot get round (a deposit beside another, a rock under the target). The caller looks for another way in.
            double distance = Math.Sqrt(d2);
            if (distance < bestDistance - Cell) { bestDistance = distance; lastGain = i; }
            // No headway for ten seconds on a free flight: a wing or the hull is pressed against a rock the point of the cockpit would clear. Climb out of it (the world is flat, up is free) and go on.
            else if (i - lastGain > 60 && _flyDone is null && !boosting)
            {
                Say($"pinned at ship ({p.X},{p.Y},{p.Z}) on the way to ({tx},{ty},{tz}): climbing out");
                for (int c = 0; c < 36; c++) { _w.Submit(0, new SetThrust(0, 1000, 0)); _w.Step(5); FlightSeconds += 5.0 / Tps; EverySecondIfDue(ref _flyTicks, 5); }
                // Pinned again at the same place: the way straight down is a rock under a wing. Slide sideways (alternating sides) before the next try so the descent comes in elsewhere.
                if (++pins >= 2)
                    for (int c = 0; c < 14; c++) { _w.Submit(0, new SetThrust(pins % 2 == 0 ? 1000 : -1000, 0, 0)); _w.Step(5); FlightSeconds += 5.0 / Tps; EverySecondIfDue(ref _flyTicks, 5); }
                bestDistance = double.MaxValue; lastGain = i;
            }
            else if (i - lastGain > 60 && _flyDone is not null)
            {
                Say($"blocked at ship ({p.X},{p.Y},{p.Z}) on the way to ({tx},{ty},{tz}); solids near: {NearSolids(p.X, p.Y, p.Z)}");
                Cmd(new SetThrust(0, 0, 0));
                if (boosting) Cmd(new SetBoost(false));
                return false;
            }
            // Boost over long distances (as a player holding the key would); off well before the target so the stop is clean.
            bool wantBoost = _c.World is not null && d2 > (long)BoostFromCells * Cell * BoostFromCells * Cell;
            if (wantBoost != boosting) { Cmd(new SetBoost(wantBoost)); boosting = wantBoost; }
            if (d2 <= tolUnits * tolUnits || (_flyDone is not null && _flyDone())) { Cmd(new SetThrust(0, 0, 0)); if (boosting) Cmd(new SetBoost(false)); return true; }
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

    /// <summary>Debug text: the solid rocks and deposits within 40 cells of a point (cells, hit radius in cells).</summary>
    private string NearSolids(long x, long y, long z)
    {
        var parts = new List<string>();
        if (Net.Galaxy is not null)
        {
            int sys = Net.CurrentSystem(0);
            if (sys >= 0)
                foreach (int i in Net.Galaxy.ObstaclesOf(sys))
                {
                    var o = Net.GetObstacle(i);
                    if (o.Behaviour is not (ObstacleBehaviour.Solid or ObstacleBehaviour.Drifter)) continue;
                    if (Math.Abs(o.X - x) < 40L * Cell && Math.Abs(o.Y - y) < 40L * Cell && Math.Abs(o.Z - z) < 40L * Cell)
                        parts.Add($"rock {i} at ({o.X / Cell},{o.Y / Cell},{o.Z / Cell}) hit {Net.ObstacleHitRadius(i) / (double)Cell:F1}");
                }
        }
        for (int d = 0; d < Net.DepositCount; d++)
        {
            var (px, py, pz) = Net.DepositPosition(d);
            if (Net.DepositRockRadius(d) > 0 && Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 0 && Math.Abs(px * (long)Cell - x) < 40L * Cell && Math.Abs(py * (long)Cell - y) < 40L * Cell && Math.Abs(pz * (long)Cell - z) < 40L * Cell)
                parts.Add($"deposit {d} at ({px},{py},{pz}) hit {Net.DepositRockRadius(d) / (double)Cell:F1}");
        }
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Flies until the laser reaches a deposit (ADR 0033). A deposit is a solid rock and the ship is 48 cells long, so the bot plans a pose as a player
    /// would: a place beside the rock where the laser reaches it and no module box touches any deposit's rock, reached by a straight run in from outside
    /// that has been checked against every rock on the way. It tries the poses nearest to where the ship is first; the straight line to the centre is the fallback.
    /// </summary>
    private bool ApproachDeposit(int deposit)
    {
        if (Net.InMineRange(0, deposit)) return true;
        var plan = PlanApproach(deposit);
        if (plan is { } p)
        {
            try
            {
                _ignoreDeposits = false; // the way to the start of the run goes round the rocks as before; the run itself is checked clear of every deposit rock
                if (FlyTo(p.Wx, p.Wy, p.Wz, 3L * Cell))
                {
                    _ignoreDeposits = true;
                    if (FlyTo(p.Ox, p.Oy, p.Oz, Cell / 2, done: () => Net.InMineRange(0, deposit)) && Net.InMineRange(0, deposit)) return true;
                }
            }
            finally { _ignoreDeposits = false; }
        }
        var (x, y, z) = DepositCentre(deposit);
        if (FlyTo(x, y, z, 3L * Cell, done: () => Net.InMineRange(0, deposit))) return true;
        if (!Net.InMineRange(0, deposit)) Stuck("no way in to the deposit");
        return false;
    }

    private bool _ignoreDeposits;

    /// <summary>A pose for the mining run: where the run starts (outside the rock) and where the ship's origin stops (units).</summary>
    private readonly record struct Approach(long Wx, long Wy, long Wz, long Ox, long Oy, long Oz);

    private Approach? PlanApproach(int deposit)
    {
        var lasers = Net.MiningLasers(0);
        if (lasers.Count == 0) return null;
        var laser = lasers[0];
        var ship = Net.GetStructure(Net.ShipStructure(0));
        var cur = Net.GetPlayerInfo(0);
        long ox0 = ship.OriginX, oy0 = ship.OriginY, oz0 = ship.OriginZ;
        var (cx, cy, cz) = DepositCentre(deposit);
        var boxes = ship.Modules.Keys.Select(k => Net.ModuleWorldBounds(ship, k)).ToArray();
        // The laser's offset from the ship's origin: the pose is chosen by where the laser has to be.
        long lx = laser.X - ox0, ly = laser.Y - oy0, lz = laser.Z - oz0;
        double reach = Math.Min(laser.RangeCells, 9) * (double)Cell;

        bool Clear(long ox, long oy, long oz)
        {
            long dx = ox - ox0, dy = oy - oy0, dz = oz - oz0;
            foreach (var (min, max) in boxes)
                if (Net.BoxTouchesDepositRock(min.X + dx, min.Y + dy, min.Z + dz, max.X + dx, max.Y + dy, max.Z + dz, 160)) return false;
            return true;
        }

        // First choice: straight in from where the ship is, stopping 8 cells out along the same line, if that whole path is clear (the common case: no detour, no extra leg).
        {
            double ddx = ox0 - cx, ddy = oy0 - cy, ddz = oz0 - cz, dd = Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
            if (dd > 1)
            {
                double ux = ddx / dd, uy = ddy / dd, uz = ddz / dd;
                long Lx = (long)(cx + ux * 8.0 * Cell), Ly = (long)(cy + uy * 8.0 * Cell), Lz = (long)(cz + uz * 8.0 * Cell);
                long Ox = Lx - lx, Oy = Ly - ly, Oz = Lz - lz;
                double run = Math.Sqrt((double)(Ox - ox0) * (Ox - ox0) + (double)(Oy - oy0) * (Oy - oy0) + (double)(Oz - oz0) * (Oz - oz0));
                bool ok = true;
                for (int s = 0; s <= 40 && ok; s++)
                {
                    double k = s / 40.0;
                    ok = Clear((long)(ox0 + (Ox - ox0) * k), (long)(oy0 + (Oy - oy0) * k), (long)(oz0 + (Oz - oz0) * k));
                }
                if (ok && 8.0 * Cell <= reach) return new Approach(ox0, oy0, oz0, Ox, Oy, Oz);
            }
        }

        Approach? best = null;
        double bestScore = double.MaxValue;
        var dirs = new List<(double X, double Y, double Z)>();
        for (int ix = -1; ix <= 1; ix++) for (int iy = -1; iy <= 1; iy++) for (int iz = -1; iz <= 1; iz++)
        {
            if (ix == 0 && iy == 0 && iz == 0) continue;
            double n = Math.Sqrt(ix * ix + iy * iy + iz * iz);
            dirs.Add((ix / n, iy / n, iz / n));
        }
        foreach (var (ux, uy, uz) in dirs)
            foreach (double r in new[] { 7.0, 8.5 })
            {
                // The laser sits r cells out from the deposit's centre along (ux, uy, uz); the ship's origin follows from the laser's offset.
                long Lx = (long)(cx + ux * r * Cell), Ly = (long)(cy + uy * r * Cell), Lz = (long)(cz + uz * r * Cell);
                if (r * Cell > reach) continue;
                long Ox = Lx - lx, Oy = Ly - ly, Oz = Lz - lz;
                // The run in: from 24 cells further out along the same ray to the pose, every step clear of the rocks.
                bool ok = true;
                for (int s = 0; s <= 30 && ok; s++)
                {
                    double k = s / 30.0;
                    long sx = (long)(Ox + ux * 14 * Cell * (1 - k)), sy = (long)(Oy + uy * 14 * Cell * (1 - k)), sz = (long)(Oz + uz * 14 * Cell * (1 - k));
                    ok = Clear(sx, sy, sz);
                }
                if (!ok) continue;
                long Wx = (long)(Ox + ux * 14 * Cell), Wy = (long)(Oy + uy * 14 * Cell), Wz = (long)(Oz + uz * 14 * Cell);
                double score = Math.Sqrt((double)(Wx - cur.X) * (Wx - cur.X) + (double)(Wy - cur.Y) * (Wy - cur.Y) + (double)(Wz - cur.Z) * (Wz - cur.Z)) + Math.Sqrt((double)(Ox - Wx) * (Ox - Wx) + (double)(Oy - Wy) * (Oy - Wy) + (double)(Oz - Wz) * (Oz - Wz));
                if (score < bestScore) { bestScore = score; best = new Approach(Wx, Wy, Wz, Ox, Oy, Oz); }
            }
        return best;
    }
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
            if (!ApproachDeposit(deposit)) return false;
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
        for (int d = 0; d < g.Deposits.Count; d++)
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
        _lastBuildTrip = Now;
        Say($"out of build range of cell ({x},{y},{z}) in structure {structure}: flying closer");
        var (tx, ty, tz) = (wx * Cell, wy * Cell, wz * Cell);
        // Stop a little short, towards the ship.
        var p = Net.GetPlayerInfo(0);
        tx -= Math.Sign(tx - p.X) * 8L * Cell; tz -= Math.Sign(tz - p.Z) * 8L * Cell;
        // A deposit's rock is solid and the ship is 8 cells wide and 16 long (ADR 0033): the point must leave every module box clear of the rocks (with the hull padding the
        // sim adds), or the ship is pinned against one on the way in (seed 777: the home iron rock). The nearest clear pose that still has the cell in build range.
        var ship = Net.GetStructure(Net.ShipStructure(0));
        var boxes = ship.Modules.Keys.Select(k => Net.ModuleWorldBounds(ship, k)).ToArray();
        long margin = (_c.World?.HullPadUnits ?? 160) + 40L;
        long range = (PlayerRules.BuildRangeCells - 6) * (long)Cell;
        bool PoseClear(long ox, long oy, long oz)
        {
            long dx = ox - ship.OriginX, dy = oy - ship.OriginY, dz = oz - ship.OriginZ;
            foreach (var (min, max) in boxes)
                if (Net.BoxTouchesDepositRock(min.X + dx, min.Y + dy, min.Z + dz, max.X + dx, max.Y + dy, max.Z + dz, margin)) return false;
            return true;
        }
        long cx = wx * Cell + Cell / 2, cy = wy * Cell + Cell / 2, cz = wz * Cell + Cell / 2;
        if (!PoseClear(tx, ty, tz))
        {
            (long X, long Y, long Z)? best = null;
            double bestD = double.MaxValue;
            for (int ddy = -4; ddy <= 12; ddy += 2)
                for (int ddx = -12; ddx <= 12; ddx += 2)
                    for (int ddz = -12; ddz <= 12; ddz += 2)
                    {
                        long qx = tx + ddx * (long)Cell, qy = ty + ddy * (long)Cell, qz = tz + ddz * (long)Cell;
                        long ex = cx - qx, ey = cy - qy, ez = cz - qz;
                        if (ex * ex + ey * ey + ez * ez > range * range) continue;
                        double d = ddx * ddx + ddy * ddy * 2.0 + ddz * ddz;
                        if (d >= bestD || !PoseClear(qx, qy, qz)) continue;
                        bestD = d; best = (qx, qy, qz);
                    }
            if (best is { } b) (tx, ty, tz) = b;
        }
        FlyTo(tx, ty, tz, 3L * Cell);
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
            string at = "";
            if (res.Reject == Reject.Overlap)
            {
                var (dx, dy, dz) = Dirs.Vector(dir);
                for (int sid = 0; sid < Net.SegmentIdLimit; sid++)
                {
                    if (!Net.IsAlive(sid)) continue;
                    var si = Net.GetSegmentInfo(sid);
                    if (si.Structure != st) continue;
                    for (int k = 0; k < len; k++)
                        for (int j = 0; j < si.LengthCells; j++)
                            if (si.OriginX + si.DirX * j == x + dx * k && si.OriginY + si.DirY * j == y + dy * k && si.OriginZ + si.DirZ * j == z + dz * k) at = $" (cell held by belt {sid} from ({si.OriginX},{si.OriginY},{si.OriginZ}) x{si.LengthCells})";
                }
                for (int mid = 0; mid < Net.MachineIdLimit; mid++)
                    if (Net.IsMachineAlive(mid) && Net.GetMachineInfo(mid) is var mi && mi.Structure == st)
                        for (int k = 0; k < len; k++)
                            if (mi.X == x + dx * k && mi.Y == y + dy * k && mi.Z == z + dz * k) at = $" (cell held by {_c.Machines[mi.Type].Id} at ({mi.X},{mi.Y},{mi.Z}))";
            }
            Stuck($"PlaceSegmentIn at ({x},{y},{z}) {dir} x{len} of structure {st} was rejected: {res.Reject}{at}");
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
    // Everything stands on the floor layer (y = the module's floor) of a fabrication bay or a station core (ADR 0031): a plane of 8 by 8 cells,
    // x along the rows of belts. A defence module beside a bay (at module x + 1) gets the ammo belts across the seam.

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

    /// <summary>Ore port, smelter, depot on one row of a bay: 0.5 ingots a second into the hold.</summary>
    private bool IngotLine(int st, int ox, int oy, int oz, int y, int z)
    {
        var (s, belt) = SmelterRow(st, ox, oy, oz, y, z, 6);
        if (s < 0) return false;
        _ingotSmelters.Add(s);
        int depot = PlaceMachine(st, ox + 7, oy + y, oz + z, "core:depot");
        return depot >= 0 && !Failed(Cmd(new ConnectToMachine(belt, depot)), "ConnectToMachine");
    }

    private int _starterDepot = -1;
    private bool _factoryUp;

    /// <summary>
    /// The first factory of a bay (rows 0 to 2): ore port, smelter, depot and a plate assembler. The smelter feeds the depot along row 0 and the
    /// assembler through one belt; the assembler's plates reach the same depot along row 2.
    /// </summary>
    private bool StarterFactory(int st, int ox, int oy, int oz)
    {
        // The ingot line first (the cheapest thing that takes over from the workbench), then the plate assembler.
        int port = PlaceMachine(st, ox, oy, oz, "core:ore_unloader");
        int smelter = PlaceMachine(st, ox + 3, oy, oz, "core:smelter", "core:smelt_iron");
        int depot = PlaceMachine(st, ox + 7, oy, oz + 1, "core:depot");
        if (port < 0 || smelter < 0 || depot < 0) return false;
        _ingotSmelters.Add(smelter);
        _starterDepot = depot;
        if (Link(st, port, ox + 1, oy, oz, Dir.PosX, 2, smelter) < 0 || Link(st, smelter, ox + 4, oy, oz, Dir.PosX, 4, depot) < 0) return false;
        WaitUntil(() => Net.DepotDelivered > 0, 120);
        int asm = PlaceMachine(st, ox + 3, oy, oz + 2, "core:assembler", "core:iron_plate");
        if (asm < 0) return false;
        _starterAssembler = asm;
        return Link(st, smelter, ox + 3, oy, oz + 1, Dir.PosZ, 1, asm) >= 0
            && Link(st, asm, ox + 4, oy, oz + 2, Dir.PosX, 4, depot) >= 0;
    }

    /// <summary>
    /// Two smelter rows (z = <paramref name="zA"/> and <paramref name="zA"/> + 1) feeding one plate assembler at x7, whose output goes one cell up the
    /// column into a machine at (7, zA - 2): a laboratory or a depot. 0.5 plates a second at best.
    /// </summary>
    private int PlateLine(int st, int ox, int oy, int oz, int zA, string sink, out int sinkId, string? sinkRecipe = null)
    {
        sinkId = -1;
        int asm = PlaceMachine(st, ox + 7, oy, oz + zA, "core:assembler", "core:iron_plate");
        if (asm < 0) return -1;
        var (s1, b1) = SmelterRow(st, ox, oy, oz, 0, zA, 6);
        if (s1 < 0 || Failed(Cmd(new ConnectToMachine(b1, asm)), "ConnectToMachine")) return -1;
        var (s2, b2) = SmelterRow(st, ox, oy, oz, 0, zA + 1, 7);
        if (s2 < 0 || Failed(Cmd(new ConnectToMachine(b2, asm)), "ConnectToMachine")) return -1;
        sinkId = PlaceMachine(st, ox + 7, oy, oz + zA - 2, sink, sinkRecipe);
        if (sinkId < 0) return -1;
        if (Link(st, asm, ox + 7, oy, oz + zA - 1, Dir.NegZ, 1, sinkId) < 0) return -1;
        return asm;
    }

    /// <summary>
    /// An ammo battery on the floor of a bay with a defence module beside it (module x + 1): an ingot port, an assembler making ammo, and two belts
    /// along rows z and z + 1 that run across the seam into two turrets standing on the defence floor. Returns the turret ids.
    /// </summary>
    private List<int> AmmoBattery(int st, int ox, int oy, int oz, int z, int turrets = 2)
    {
        var made = new List<int>();
        int port = PlaceMachine(st, ox, oy, oz + z, "core:unloader");
        int asm = PlaceMachine(st, ox + 2, oy, oz + z, "core:assembler", "core:make_ammo");
        if (port < 0 || asm < 0) return made;
        if (Link(st, port, ox + 1, oy, oz + z, Dir.PosX, 1, asm) < 0) return made;
        int t1 = PlaceMachine(st, ox + 10, oy, oz + z, "core:turret");
        if (t1 < 0) return made;
        if (Link(st, asm, ox + 3, oy, oz + z, Dir.PosX, 7, t1) < 0) return made;
        made.Add(t1);
        if (turrets < 2) return made;
        int t2 = PlaceMachine(st, ox + 10, oy, oz + z + 1, "core:turret");
        if (t2 < 0) return made;
        if (Link(st, asm, ox + 2, oy, oz + z + 1, Dir.PosX, 8, t2) < 0) return made;
        made.Add(t2);
        return made;
    }

    /// <summary>An ingot port feeding a repair bay on row z: port x0, belt x1-2, bay x3.</summary>
    private int RepairBay(int st, int ox, int oy, int oz, int z)
    {
        int port = PlaceMachine(st, ox, oy, oz + z, "core:unloader");
        int bay = PlaceMachine(st, ox + 3, oy, oz + z, "core:repair_bay");
        if (port < 0 || bay < 0) return -1;
        return Link(st, port, ox + 1, oy, oz + z, Dir.PosX, 2, bay) < 0 ? -1 : bay;
    }

    /// <summary>The ice loop of a bay: ice port, belt, cracker and a belt into a depot of its own on row <paramref name="row"/>, so hydrogen reaches the hold.</summary>
    private bool IceLoop(int st, int ox, int oy, int oz, int row = 3)
    {
        int port = PlaceMachine(st, ox, oy, oz + row, "core:ice_unloader");
        int cracker = PlaceMachine(st, ox + 3, oy, oz + row, "core:hydrogen_cracker", "core:crack_ice");
        int depot = PlaceMachine(st, ox + 7, oy, oz + row, "core:depot");
        if (port < 0 || cracker < 0 || depot < 0) return false;
        if (Link(st, port, ox + 1, oy, oz + row, Dir.PosX, 2, cracker) < 0) return false;
        return Link(st, cracker, ox + 4, oy, oz + row, Dir.PosX, 3, depot) >= 0;
    }
}
