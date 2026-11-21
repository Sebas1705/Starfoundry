using Starfoundry.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Pacing of the station rules, checked by a tiny scripted player that uses only sim commands (no balance bot):
/// fly to the home iron deposit, hand-mine it with the ship refinery on, and spend the hand-refined ingots on the core
/// module, a cargo port, a smelter and a depot, then keep adding smelters and depots as the hold allows.
/// The measured numbers are written up in docs/prototypes/stations.md. If a content change breaks a bound here, either the
/// change or the target needs a conscious decision.
/// </summary>
public class StationPacingTests
{
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;

    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Unloader = C.MachineIndex("core:unloader");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Depot = C.MachineIndex("core:depot");

    private readonly ITestOutputHelper _out;
    public StationPacingTests(ITestOutputHelper output) => _out = output;

    /// <summary>What the scripted player achieved, in ticks (-1 when it never happened within the run).</summary>
    private sealed class Result
    {
        public long Arrived = -1, CoreAffordable = -1, Core = -1, Port = -1, Smelter = -1, Depot = -1, FirstWave = -1;
        public int FirstWaveWarning = -1;
        public long MaxAlertBeforeCore;
        public (int Mx, int My, int Mz) CoreModule;
        public int Distance;
    }

    private static string Min(long ticks) => ticks < 0 ? "never" : $"{ticks / (double)Tps:F0} s ({ticks / (double)Tps / 60:F1} min)";

    private static (int, int, int) Sign(long dx, long dy, long dz) => (Math.Sign(dx), Math.Sign(dy), Math.Sign(dz));

    /// <summary>
    /// Plays <paramref name="minutes"/> of campaign with the station rules on. The ship stays at the deposit: the core goes in the
    /// module that contains the ship's cell, which is within build range of the ship by construction.
    /// </summary>
    private Result Play(int minutes, bool stationRules = true, bool threats = true)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats, stationRules);
        w.Network.EnsurePlayer(0);
        var net = w.Network;
        var r = new Result();

        int deposit = -1;
        for (int d = 0; d < net.DepositCount; d++)
            if (net.IsDepositRevealed(d) && net.DepositItem(d) == Ore) { deposit = d; break; }
        Assert.True(deposit >= 0, "no revealed iron deposit at home");
        var (ox, oy, oz) = net.DepositPosition(deposit);
        var start = net.GetPlayerInfo(0);
        r.Distance = (int)Math.Sqrt(Math.Pow(ox - start.X / (double)Units.CellUnits, 2) + Math.Pow(oy - start.Y / (double)Units.CellUnits, 2) + Math.Pow(oz - start.Z / (double)Units.CellUnits, 2));

        bool Do(Command c) { w.Submit(0, c); w.Step(); return w.LastResults.Last().Ok; }
        Reject DoR(Command c) { w.Submit(0, c); w.Step(); return w.LastResults.Last().Reject; }

        var homeSystem = net.CurrentSystem(0);
        bool mining = false;
        var module = (0, 0, 0);
        int nextX = 0;
        long end = (long)minutes * 60 * Tps;
        var placed = new List<int>();

        // Cells inside the core module that are free for the next building, one row at a time.
        (int, int, int) NextCell()
        {
            int x = module.Item1 * StationGrid.ModuleX + (nextX % 8);
            int y = module.Item2 * StationGrid.ModuleY + 1;
            int z = module.Item3 * StationGrid.ModuleZ + (nextX / 8) % StationGrid.ModuleZ;
            nextX++;
            return (x, y, z);
        }

        while (w.Tick < end)
        {
            var p = net.GetPlayerInfo(0);

            if (!mining)
            {
                if (net.InMineRange(0, deposit))
                {
                    Do(new SetMovement(0, 0, 0));
                    Do(new StartMining(deposit));
                    Do(new SetShipRecipe(C.RecipeIndex("core:ship_smelt")));
                    mining = true;
                    r.Arrived = w.Tick;
                    var cell = (X: p.X / Units.CellUnits, Y: p.Y / Units.CellUnits, Z: p.Z / Units.CellUnits);
                    module = StationGrid.ModuleOfCell(cell.X, cell.Y, cell.Z);
                    r.CoreModule = module;
                }
                else
                {
                    var (sx, sy, sz) = Sign(ox * (long)Units.CellUnits - p.X, oy * (long)Units.CellUnits - p.Y, oz * (long)Units.CellUnits - p.Z);
                    Do(new SetMovement(sx, sy, sz));
                }
                continue;
            }

            if (r.Core < 0)
            {
                if (r.CoreAffordable < 0 && net.CanPay(0, net.NextModuleCost(0))) r.CoreAffordable = w.Tick;
                if (r.CoreAffordable >= 0 && Do(new PlaceModule(module.Item1, module.Item2, module.Item3))) r.Core = w.Tick;
                else
                {
                    if (threats)
                    {
                        var t0 = net.GetSystemThreat(homeSystem);
                        r.MaxAlertBeforeCore = Math.Max(r.MaxAlertBeforeCore, t0.Alert + (t0.WarningRemaining >= 0 ? 1 : 0));
                    }
                    if (r.CoreAffordable < 0) w.Step();
                }
            }
            else
            {
                // Build order: cargo port, smelter, depot, then smelters and depots in turn.
                int kind = r.Port < 0 ? Unloader : r.Smelter < 0 ? Smelter : r.Depot < 0 ? Depot : (placed.Count % 2 == 0 ? Smelter : Depot);
                if (net.CanPay(0, C.Machines[kind].Cost))
                {
                    var (x, y, z) = NextCell();
                    var res = DoR(new PlaceMachine(x, y, z, kind, -1));
                    Assert.Equal(Reject.None, res);
                    placed.Add(kind);
                    if (kind == Unloader) r.Port = w.Tick;
                    else if (kind == Smelter && r.Smelter < 0) r.Smelter = w.Tick;
                    else if (kind == Depot && r.Depot < 0) r.Depot = w.Tick;
                }
                else w.Step();
            }

            if (r.FirstWave < 0 && threats)
            {
                var t = net.GetSystemThreat(homeSystem);
                if (t.Wave > 0) r.FirstWave = w.Tick;
                if (r.FirstWaveWarning < 0 && t.WarningRemaining >= 0) r.FirstWaveWarning = (int)w.Tick;
            }
        }
        _out.WriteLine($"rules={stationRules} deposit {deposit} at ({ox},{oy},{oz}), {r.Distance} cells from the ship start; core module {r.CoreModule}");
        _out.WriteLine($"arrived {Min(r.Arrived)}; core affordable {Min(r.CoreAffordable)}; core {Min(r.Core)}; port {Min(r.Port)}; smelter {Min(r.Smelter)}; depot {Min(r.Depot)}; first wave {Min(r.FirstWave)}; warning {Min(r.FirstWaveWarning)}");
        w.ValidateInvariants();
        return r;
    }

    [Fact]
    public void Core_affordable_within_four_minutes_of_hand_mining()
    {
        var r = Play(minutes: 6, threats: false);
        Assert.True(r.CoreAffordable >= 0, "the core was never affordable");
        Assert.True(r.CoreAffordable <= 4 * 60 * Tps, $"core affordable at {Min(r.CoreAffordable)}");
        Assert.True(r.Core >= 0, "the core was never placed");
    }

    [Fact]
    public void Cargo_port_and_smelter_by_eight_minutes()
    {
        var r = Play(minutes: 10, threats: false);
        Assert.True(r.Port >= 0, "the cargo port was never built");
        Assert.True(r.Smelter >= 0, "the smelter was never built");
        Assert.True(r.Port <= 8 * 60 * Tps, $"port at {Min(r.Port)}");
        Assert.True(r.Smelter <= 8 * 60 * Tps, $"smelter at {Min(r.Smelter)}");
    }

    [Fact]
    public void First_wave_waits_for_the_core_when_rules_on()
    {
        var r = Play(minutes: 40);
        Assert.True(r.Core >= 0);
        Assert.Equal(0, r.MaxAlertBeforeCore); // nothing can raise the alert before the core exists
        if (r.FirstWave >= 0) Assert.True(r.FirstWave >= r.Core + 3 * 60 * Tps, $"first wave at {Min(r.FirstWave)}, core at {Min(r.Core)}");
    }

    [Fact]
    public void Hand_mined_ore_reaches_a_smelter_through_the_ore_port()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: true);
        w.Network.EnsurePlayer(0);
        var net = w.Network;
        int deposit = -1;
        for (int d = 0; d < net.DepositCount; d++)
            if (net.IsDepositRevealed(d) && net.DepositItem(d) == Ore) { deposit = d; break; }
        Assert.True(deposit >= 0);
        var (ox, oy, oz) = net.DepositPosition(deposit);

        int Ok(Command c)
        {
            w.Submit(0, c); w.Step();
            var res = w.LastResults.Last();
            Assert.True(res.Ok, $"{c} was rejected: {res.Reject}");
            return res.CreatedMachines.Count > 0 ? res.CreatedMachines[0] : res.Created.Count > 0 ? res.Created[0] : -1;
        }

        // Fly to the deposit and hand-mine raw ore into the hold (no ship refinery recipe: the ore stays ore).
        for (int i = 0; i < 60 * Tps && !net.InMineRange(0, deposit); i++)
        {
            var p = net.GetPlayerInfo(0);
            var (sx, sy, sz) = Sign(ox * (long)Units.CellUnits - p.X, oy * (long)Units.CellUnits - p.Y, oz * (long)Units.CellUnits - p.Z);
            w.Submit(0, new SetMovement(sx, sy, sz)); w.Step();
        }
        Assert.True(net.InMineRange(0, deposit), "never reached the deposit");
        w.Submit(0, new SetMovement(0, 0, 0)); w.Step();
        Ok(new StartMining(deposit));
        w.Step(20 * Tps);
        int mined = net.HoldCount(0, Ore);
        Assert.True(mined > 0, "hand mining put no ore in the hold");

        // The ore port is what moves it out: the module goes where the ship is, the line inside it.
        var pl = net.GetPlayerInfo(0);
        var module = StationGrid.ModuleOfCell(pl.X / Units.CellUnits, pl.Y / Units.CellUnits, pl.Z / Units.CellUnits);
        net.GiveItems(0, Ingot, 200); // stands for the hand-refined ingots the pacing script above pays with
        Ok(new PlaceModule(module.Item1, module.Item2, module.Item3));
        int bx = module.Item1 * StationGrid.ModuleX, by = module.Item2 * StationGrid.ModuleY + 1, bz = module.Item3 * StationGrid.ModuleZ + 1;
        int port = Ok(new PlaceMachine(bx, by, bz, C.MachineIndex("core:ore_unloader"), -1));
        int b1 = Ok(new PlaceSegment(bx + 1, by, bz, Dir.PosX, 3, BeltTier.Mk1));
        int smelter = Ok(new PlaceMachine(bx + 4, by, bz, Smelter, C.RecipeIndex("core:smelt_iron")));
        int b2 = Ok(new PlaceSegment(bx + 5, by, bz, Dir.PosX, 3, BeltTier.Mk1));
        Ok(new ConnectFromMachine(port, b1));
        Ok(new ConnectToMachine(b1, smelter));
        Ok(new ConnectFromMachine(smelter, b2));
        Ok(new SetSink(b2));
        Ok(new StopMining());
        int before = net.HoldCount(0, Ore);
        w.Step(30 * Tps);
        w.ValidateInvariants();
        Assert.True(net.PassedBy(b2) > 0, "no ingot reached the end of the line");
        Assert.True(net.HoldCount(0, Ore) < before, "the hold kept its ore");
    }

    [Fact]
    public void Wave_clock_is_unchanged_when_rules_off()
    {
        // Free building: the first machine starts raising the alert at once, with no core in the way.
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: true, stationRules: false);
        w.Network.EnsurePlayer(0);
        for (int i = 0; i < 6; i++)
        {
            w.Submit(0, new PlaceMachine(i * 2, 0, 0, Smelter, -1));
            w.Step();
            Assert.True(w.LastResults.Single().Ok);
        }
        w.Step(Tps);
        Assert.True(w.Network.GetSystemThreat(w.Network.CurrentSystem(0)).Alert > 0);
    }
}
