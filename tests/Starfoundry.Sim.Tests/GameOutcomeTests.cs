using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class GameOutcomeTests
{
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int Ammo = C.ItemIndex("core:ammo");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");

    private static SimWorld World() => SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true);

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>A powered ship with a cockpit, workshop, defence module and engine, and two ammo-fed turrets.</summary>
    private static (SimWorld W, int Ship) ArmedShip()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        w.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(s, 2, 0, 0, ModuleKind.Defence);
        w.Network.AddStructureModuleUnchecked(s, 3, 0, 0, ModuleKind.Engine);
        w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor);
        w.Network.GiveItems(0, Hydrogen, 60);
        for (int i = 0; i < 2; i++)
        {
            int belt = Ok(w, new PlaceSegmentIn(s, 12, 1, i, Dir.PosX, 4, BeltTier.Mk2));
            Ok(w, new SetSource(belt, new ushort[] { (ushort)Ammo }));
            int turret = Ok(w, new PlaceMachineIn(s, 16, 1, i, Turret, -1));
            Ok(w, new ConnectToMachine(belt, turret));
        }
        return (w, s);
    }

    private static void ClearWaves(SimWorld w, int count)
    {
        for (int wave = 0; wave < count; wave++)
        {
            w.Network.ScheduleWave(0);
            for (int i = 0; i < 20_000 && (w.Network.EnemyCount > 0 || w.Network.WavesSpawned <= wave); i++) w.Step();
            Assert.Equal(0, w.Network.EnemyCount);
        }
    }

    [Fact]
    public void A_fresh_world_is_still_playing()
    {
        var (w, _) = ArmedShip();
        var r = GameOutcome.Evaluate(w.Network, 0, new GoalSettings());
        Assert.Equal(OutcomeKind.Playing, r.Kind);
        Assert.Equal(OutcomeReason.None, r.Reason);
        Assert.Equal(0, r.Stats.WavesSurvived);
        Assert.Equal(10, new GoalSettings().WavesToWin);
    }

    [Fact]
    public void Losing_the_last_cockpit_loses_the_game_and_keeps_the_stats()
    {
        var (w, s) = ArmedShip();
        w.Step(Tps * 5);
        w.Network.HitModule(s, 0, 0, 0, 9999);
        var r = GameOutcome.Evaluate(w.Network, 0, new GoalSettings(1));
        Assert.Equal(OutcomeKind.Lost, r.Kind);
        Assert.Equal(OutcomeReason.ShipLost, r.Reason);
        Assert.True(r.Stats.PlaySeconds >= 5);
        Assert.Equal(1, r.Stats.ModulesLost);
    }

    [Fact]
    public void Clearing_the_waves_without_a_station_is_not_a_win_and_with_one_it_is()
    {
        var (w, _) = ArmedShip();
        w.Step(Tps);
        ClearWaves(w, 2);
        var goal = new GoalSettings(2);
        Assert.Equal(2, GameOutcome.WavesSurvived(w.Network));
        Assert.False(GameOutcome.HasBase(w.Network, 0));
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, goal).Kind);

        int station = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 30 * Units.CellUnits);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        var r = GameOutcome.Evaluate(w.Network, 0, goal);
        Assert.Equal(OutcomeKind.Won, r.Kind);
        Assert.Equal(OutcomeReason.WavesSurvived, r.Reason);
        Assert.Equal(1, r.Stats.StationsFounded);
        Assert.Equal(0, r.Stats.StationsLost);
        Assert.Equal(2, r.Stats.WavesSurvived);
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, new GoalSettings(3)).Kind);
    }

    [Fact]
    public void The_wave_in_progress_does_not_count_as_survived()
    {
        var (w, _) = ArmedShip();
        w.Step(Tps);
        w.Network.ScheduleWave(0);
        while (w.Network.EnemyCount == 0) w.Step();
        Assert.Equal(1, w.Network.WavesSpawned);
        Assert.Equal(0, GameOutcome.WavesSurvived(w.Network));
    }

    [Fact]
    public void A_station_lost_counts_as_founded_and_lost()
    {
        var (w, _) = ArmedShip();
        int station = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 30 * Units.CellUnits);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(station, 1, 0, 0, ModuleKind.Hull);
        Assert.True(GameOutcome.HasBase(w.Network, 0));
        w.Network.HitModule(station, 0, 0, 0, 9999);
        var st = GameOutcome.Stats(w.Network, 0);
        Assert.Equal(1, st.StationsFounded);
        Assert.Equal(1, st.StationsLost);
        Assert.False(GameOutcome.HasBase(w.Network, 0));
    }

    [Fact]
    public void Legacy_worlds_are_never_lost_and_need_no_station()
    {
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Campaign, Seed, threats: true);
        w.Network.EnsurePlayer(0);
        Assert.False(w.Network.StructureRules);
        Assert.True(GameOutcome.HasBase(w.Network, 0));
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, new GoalSettings()).Kind);
        Assert.Equal(OutcomeKind.Won, GameOutcome.Evaluate(w.Network, 0, new GoalSettings(0)).Kind);
    }
}
