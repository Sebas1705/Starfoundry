using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>
/// The scripted player's expansion: research carbon drilling and the jump drive (carbon reaches a second lab by belt),
/// hand-mine the jump fuel, jump to a neighbouring system that has iron, scan, fly to a deposit and build an ingot line
/// there. It answers how soon a player can leave the home ore behind, not whether the base survives while away.
/// </summary>
public sealed partial class BalanceBot
{
    private enum Phase { WaitingForResearch, GatheringFuel, Jumping, Scanning, Flying, Building, Returning, Porting, Done }

    /// <summary>Research carbon drilling and the jump drive after Logistics, then jump out and build a remote ingot line.</summary>
    public bool Expand { get; init; }

    /// <summary>After coming home, replace the drill and smelter of every ammo line with a cargo port that feeds it ingots from the hold, so the remote lines keep the turrets supplied.</summary>
    public bool UseUnloader { get; init; }

    /// <summary>Start the expansion research only when the home iron is at or below this percent of its starting amount (100 means at once).</summary>
    public int ExpandAtPercent { get; init; } = 100;

    private int HomeIronPercent() => _ironDeposit < 0 ? 100 : (int)((long)Net.DepositRemaining(_ironDeposit) * 100 / Math.Max(1, Net.DepositInitial(_ironDeposit)));

    /// <summary>Remove the carbon drill, belt and second lab once the jump drive is researched, so nothing undefended is left at home.</summary>
    public bool TakeDownCarbonLine { get; init; }

    private Phase _phase = Phase.WaitingForResearch;
    private int _carbonDeposit = -1;
    private int _targetSystem = -1;
    private int _remoteDeposit = -1;
    private int _remoteStep;
    private bool _carbonLineBuilt;
    private long _deliveredAtRemote;
    private readonly int[] _remote = new int[12];

    private bool Keeping => Upgrades > 0 || Expand; // the lab stays for research

    /// <summary>Records an expansion milestone under a key (for tests and tables) and a line of text with the home iron left.</summary>
    private void Note(string key, string what)
    {
        double now = _w.Tick / (double)Units.TicksPerSecond;
        int ore = _ironDeposit >= 0 ? Net.DepositRemaining(_ironDeposit) : -1;
        _report.Notes.Add($"{(int)now / 60}:{(int)now % 60:00} {what} (home iron left {ore})");
        _report.Milestones.TryAdd("Expansion: " + key, now);
        _report.HomeIronLeft["Expansion: " + key] = ore;
    }

    private int _portStep;

    /// <summary>One placement per call: remove the smelter of an ammo line, put a cargo port in its cell and connect it to the belt after it.</summary>
    private void PortAmmoLines()
    {
        var lines = new List<(string Name, int Z)> { ("l3", 6) };
        int[] rows = { 9, -3, 12, -6 };
        for (int i = 0; i < Turrets - 1; i++) lines.Add(("x" + i, rows[i]));
        if (_portStep >= lines.Count) { Note("Cargo ports", "ammo lines now fed from the hold"); _phase = Phase.Done; return; }

        var (name, z) = lines[_portStep];
        if (_ids.TryGetValue(name + ".smelter", out int smelter) && Net.IsMachineAlive(smelter)) Do(new RemoveMachine(smelter));
        if (!Try(new PlaceMachine(4, 0, z, Machine("core:unloader"), -1), out var r)) return;
        Do(new ConnectFromMachine(r.CreatedMachines[0], _ids[name + ".b2"]));
        _portStep++;
    }

    private int _carbonStep;
    private (int X, int Z) _carbonSpot;
    private readonly int[] _carbonIds = new int[3];

    private bool Free(int x, int y, int z) => _w.EntityAt(x, y, z).Kind == SimWorld.EntityKind.None;

    /// <summary>
    /// Builds the carbon line (drill, three belts, a second lab) around the carbon deposit, one placement per call,
    /// each paid from the hold: the ingots of the whole line are more than the hold keeps from depots, so it cannot be one batch.
    /// </summary>
    private bool TryCarbonLine()
    {
        if (_carbonDeposit < 0)
            for (int d = 0; d < Net.DepositCount; d++)
                if (Net.IsDepositRevealed(d) && Net.DepositItem(d) == Item("core:carbon")) { _carbonDeposit = d; break; }
        if (_carbonDeposit < 0) throw new InvalidOperationException("No carbon deposit at home.");
        int drill = Machine("core:drill"), lab = Machine("core:research_lab");

        if (_carbonStep == 0)
        {
            // The first corner around the deposit where all five cells are free and in the ship's building range.
            var (cx, _, cz) = Net.DepositPosition(_carbonDeposit);
            for (int k = 3; k <= 12 && _carbonStep == 0; k += 1)
                foreach (var (sx, sz) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                {
                    int x = cx + sx * k, z = cz + sz * k;
                    bool ok = Enumerable.Range(0, 5).All(i => Free(x + i, 0, z) && _w.InBuildRange(0, x + i, 0, z));
                    if (!ok) continue;
                    _carbonSpot = (x, z);
                    _carbonStep = 1;
                    break;
                }
            if (_carbonStep == 0) throw new InvalidOperationException("No free spot for the carbon line around the deposit.");
        }

        var (px, pz) = _carbonSpot;
        switch (_carbonStep)
        {
            case 1:
                if (!Try(new PlaceMachine(px, 0, pz, drill, Recipe("core:drill_carbon")), out var r1)) return false;
                _carbonIds[0] = r1.CreatedMachines[0];
                Do(new LinkDeposit(_carbonIds[0], _carbonDeposit));
                _carbonStep = 2;
                return false;
            case 2:
                if (!Try(new PlaceSegment(px + 1, 0, pz, Dir.PosX, 3, BeltTier.Mk1), out var r2)) return false;
                _carbonIds[1] = r2.Created[0];
                Do(new ConnectFromMachine(_carbonIds[0], _carbonIds[1]));
                _carbonStep = 3;
                return false;
            default:
                if (!Try(new PlaceMachine(px + 4, 0, pz, lab, -1), out var r3)) return false;
                _carbonIds[2] = r3.CreatedMachines[0];
                Do(new ConnectToMachine(_carbonIds[1], _carbonIds[2]));
                return true;
        }
    }

    /// <summary>The expansion state machine; called once a second after the base script.</summary>
    private void Expansion()
    {
        if (!Expand || _phase == Phase.Done) return;
        int jumpNode = _c.ResearchIndex("core:jump_drive");
        switch (_phase)
        {
            case Phase.WaitingForResearch:
                if (!_carbonLineBuilt && Net.IsResearched(_c.ResearchIndex("core:carbon_drilling"))) _carbonLineBuilt = TryCarbonLine();
                if (_carbonLineBuilt && _carbonNoted == false) { _carbonNoted = true; Note("Carbon line", "carbon line built"); }
                if (Net.IsResearched(jumpNode))
                {
                    Note("Jump drive", "jump drive researched");
                    // The carbon line has done its job and sits outside the turrets' reach: take it down and get the ingots back.
                    if (TakeDownCarbonLine) { Do(new RemoveMachine(_carbonIds[0])); Do(new RemoveSegment(_carbonIds[1])); Do(new RemoveMachine(_carbonIds[2])); }
                    _phase = Phase.GatheringFuel;
                }
                break;

            case Phase.GatheringFuel:
            {
                _expanding = true;
                int need = 2 * _c.JumpCost.Where(s => s.Item == Item("core:carbon")).Sum(s => s.Count); // there and back
                if (Net.HoldCount(0, Item("core:carbon")) >= need) { Do(new StopMining()); _phase = Phase.Jumping; break; }
                if (Net.GetPlayerInfo(0).MiningDeposit != _carbonDeposit)
                {
                    FlyTo(Net.DepositPosition(_carbonDeposit), () => Net.InMineRange(0, _carbonDeposit));
                    Do(new StartMining(_carbonDeposit));
                }
                break;
            }

            case Phase.Jumping:
            {
                if (_targetSystem < 0) _targetSystem = PickTargetSystem();
                if (Net.JumpRemaining(0) == 0 && Net.CurrentSystem(0) == 0)
                {
                    var r = Do(new Jump(_targetSystem));
                    if (!r.Ok && r.Reject != Reject.Busy) throw new InvalidOperationException($"Jump rejected with {r.Reject}");
                    Note("Jump out", $"jump to system {_targetSystem} started");
                }
                else if (Net.JumpRemaining(0) == 0 && Net.CurrentSystem(0) == _targetSystem) { Note("Arrived", "arrived"); _phase = Phase.Scanning; }
                break;
            }

            case Phase.Scanning:
            {
                if (Net.ScanCooldown(0) > 0) break;
                Do(new Scan());
                _remoteDeposit = NearestRevealedIron();
                if (_remoteDeposit < 0) throw new InvalidOperationException("The scan found no iron in the target system.");
                Note("Scan", $"scan found iron deposit {_remoteDeposit} with {Net.DepositRemaining(_remoteDeposit)}");
                _phase = Phase.Flying;
                break;
            }

            case Phase.Flying:
                FlyTo(Net.DepositPosition(_remoteDeposit), () => Net.InMineRange(0, _remoteDeposit));
                Do(new StartMining(_remoteDeposit));
                Do(new SetShipRecipe(Recipe("core:ship_smelt")));
                _mining = true;
                _phase = Phase.Building;
                break;

            case Phase.Building:
                BuildRemoteLine();
                break;

            case Phase.Porting:
                PortAmmoLines();
                break;

            case Phase.Returning:
            {
                if (Net.JumpRemaining(0) == 0 && Net.CurrentSystem(0) == _targetSystem)
                {
                    var r = Do(new Jump(0));
                    if (!r.Ok && r.Reject != Reject.Busy) throw new InvalidOperationException($"Jump home rejected with {r.Reject}");
                    Note("Jump home", "jump home started");
                }
                else if (Net.JumpRemaining(0) == 0 && Net.CurrentSystem(0) == 0)
                {
                    FlyTo((6, 0, 3), () => Math.Abs(Net.GetPlayerInfo(0).X - 6 * Units.CellUnits) <= Units.CellUnits / 2
                        && Math.Abs(Net.GetPlayerInfo(0).Z - 3 * Units.CellUnits) <= Units.CellUnits / 2);
                    _expanding = false;
                    Note("Home again", "back at the base");
                    _phase = UseUnloader ? Phase.Porting : Phase.Done;
                }
                break;
            }
        }
    }

    private bool _carbonNoted;
    private bool _expanding;

    private int PickTargetSystem()
    {
        var g = Net.Galaxy!;
        int iron = Item("core:iron_ore");
        // The nearest lane neighbour of home whose archetype makes iron deposits.
        return g.Systems[0].Lanes
            .Where(n => _c.Archetypes[g.Systems[n].Archetype].Deposits.Any(s => s.Item == iron))
            .OrderBy(n => Dist2(g.Systems[n].X - g.Systems[0].X, g.Systems[n].Y - g.Systems[0].Y, g.Systems[n].Z - g.Systems[0].Z))
            .DefaultIfEmpty(-1).First() is int t and >= 0 ? t : throw new InvalidOperationException("No neighbouring system has iron.");
    }

    private static long Dist2(long x, long y, long z) => x * x + y * y + z * z;

    private int NearestRevealedIron()
    {
        var p = Net.GetPlayerInfo(0);
        int best = -1;
        long bestD = long.MaxValue;
        for (int d = 0; d < Net.DepositCount; d++)
        {
            if (!Net.IsDepositRevealed(d) || Net.DepositItem(d) != Item("core:iron_ore") || Net.DepositRemaining(d) <= 0) continue;
            var pos = Net.DepositPosition(d);
            long dd = Dist2(pos.X * (long)Units.CellUnits - p.X, pos.Y * (long)Units.CellUnits - p.Y, pos.Z * (long)Units.CellUnits - p.Z);
            if (dd < bestD) { bestD = dd; best = d; }
        }
        return best;
    }

    /// <summary>Flies tick by tick (the ship covers 6 cells a second, so once-a-second corrections overshoot) until the test holds.</summary>
    private void FlyTo((int X, int Y, int Z) target, Func<bool> arrived)
    {
        for (int tick = 0; tick < 60 * Units.TicksPerSecond && !arrived(); tick++)
        {
            Steer(target);
            _w.Step();
        }
        Do(new SetMovement(0, 0, 0));
        _intent = (0, 0, 0);
    }

    /// <summary>
    /// Two lines beside the remote deposit, paid from the hold one placement per call: ingots (drill, belt, smelter, belt,
    /// depot) and plates (the same with an assembler before the depot).
    /// </summary>
    private void BuildRemoteLine()
    {
        var (dx, dy, dz) = Net.DepositPosition(_remoteDeposit);
        int x = dx + 2;
        // _remoteStep 0 to 4 build the ingot line at depth dz, 5 to 9 the plate line at dz + 4.
        int z = _remoteStep < 5 ? dz : dz + 4;
        int i = _remoteStep < 5 ? _remoteStep : _remoteStep - 5;
        if (_remoteStep < 5 ? RemoteStep(i, false, x, dy, z) : RemoteStep(i, true, x, dy, z)) _remoteStep++;
        if (_remoteStep == 10)
        {
            _deliveredAtRemote = Net.DepotDelivered;
            Note("Remote lines", "remote ingot and plate lines built");
            _phase = Phase.Returning;
        }
    }


    /// <summary>One placement of a remote line; returns true when it was paid and placed.</summary>
    private bool RemoteStep(int i, bool plates, int x, int y, int z)
    {
        int slot = plates ? 6 : 0;
        switch (i)
        {
            case 0:
                if (!Try(new PlaceMachine(x, y, z, Machine("core:drill"), Recipe("core:drill_iron")), out var r0)) return false;
                _remote[slot] = r0.CreatedMachines[0];
                Do(new LinkDeposit(_remote[slot], _remoteDeposit));
                return true;
            case 1:
                if (!Try(new PlaceSegment(x + 1, y, z, Dir.PosX, 3, BeltTier.Mk1), out var r1)) return false;
                _remote[slot + 1] = r1.Created[0];
                Do(new ConnectFromMachine(_remote[slot], _remote[slot + 1]));
                return true;
            case 2:
                if (!Try(new PlaceMachine(x + 4, y, z, Machine("core:smelter"), Recipe("core:smelt_iron")), out var r2)) return false;
                _remote[slot + 2] = r2.CreatedMachines[0];
                Do(new ConnectToMachine(_remote[slot + 1], _remote[slot + 2]));
                return true;
            case 3:
                if (!Try(new PlaceSegment(x + 5, y, z, Dir.PosX, 3, BeltTier.Mk1), out var r3)) return false;
                _remote[slot + 3] = r3.Created[0];
                Do(new ConnectFromMachine(_remote[slot + 2], _remote[slot + 3]));
                return true;
            case 4 when !plates:
            {
                if (!Try(new PlaceMachine(x + 8, y, z, Machine("core:depot"), -1), out var r4)) return false;
                Do(new ConnectToMachine(_remote[slot + 3], r4.CreatedMachines[0]));
                return true;
            }
            default:
                // Plate line: the assembler sits where the ingot line has its depot, then a belt and a depot.
                return PlateTail(x, y, z, slot);
        }
    }

    private int _plateTail;

    private bool PlateTail(int x, int y, int z, int slot)
    {
        switch (_plateTail)
        {
            case 0:
                if (!Try(new PlaceMachine(x + 8, y, z, Machine("core:assembler"), Recipe("core:iron_plate")), out var a)) return false;
                _remote[slot + 4] = a.CreatedMachines[0];
                Do(new ConnectToMachine(_remote[slot + 3], _remote[slot + 4]));
                _plateTail = 1;
                return false;
            case 1:
                if (!Try(new PlaceSegment(x + 9, y, z, Dir.PosX, 3, BeltTier.Mk1), out var b)) return false;
                _remote[slot + 5] = b.Created[0];
                Do(new ConnectFromMachine(_remote[slot + 4], _remote[slot + 5]));
                _plateTail = 2;
                return false;
            default:
                if (!Try(new PlaceMachine(x + 12, y, z, Machine("core:depot"), -1), out var d)) return false;
                Do(new ConnectToMachine(_remote[slot + 5], d.CreatedMachines[0]));
                return true;
        }
    }

    /// <summary>Items the remote depots have delivered since the lines were built (the home depots mostly sit at their cap).</summary>
    public long RemoteDelivered => _phase >= Phase.Returning ? Net.DepotDelivered - _deliveredAtRemote : 0;
}
