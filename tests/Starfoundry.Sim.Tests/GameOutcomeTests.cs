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

    /// <summary>Researches the jump drive, pays the carbon and jumps to a neighbour of home with sim commands.</summary>
    private static void JumpOnce(SimWorld w)
    {
        w.Network.GrantResearch(C.ResearchIndex("core:jump_drive"));
        w.Network.GiveItems(0, C.ItemIndex("core:carbon"), w.Network.JumpCost().Single().Count);
        int next = w.Network.Galaxy!.Systems[w.Network.CurrentSystem(0)].Lanes[0];
        w.Submit(0, new Jump(next));
        w.Step();
        Assert.True(w.LastResults.Single().Ok, "the jump was rejected");
        w.Step(ExplorationRules.JumpChargeTicks);
        Assert.Equal(next, w.Network.CurrentSystem(0));
    }

    [Fact]
    public void A_jump_without_the_waves_or_the_station_is_not_a_win()
    {
        var (w, _) = ArmedShip();
        w.Step(Tps);
        JumpOnce(w);
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, new GoalSettings(3)).Kind);
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void Jumps_completed_survive_a_clone_and_change_the_checksum_only_when_non_zero()
    {
        var (w, _) = ArmedShip();
        Assert.Equal(0, w.Network.JumpsCompleted);
        ulong before = w.Checksum();
        JumpOnce(w);
        Assert.Equal(1, w.Network.JumpsCompleted);
        Assert.Equal(1, w.Network.Clone().JumpsCompleted);
        Assert.NotEqual(before, w.Checksum());
    }

    [Fact]
    public void Clearing_the_waves_needs_a_station_and_a_jump_to_win()
    {
        var (w, _) = ArmedShip();
        w.Step(Tps);
        ClearWaves(w, 2);
        var goal = new GoalSettings(2);
        Assert.Equal(2, GameOutcome.WavesSurvived(w.Network));
        Assert.False(GameOutcome.HasBase(w.Network, 0));
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, goal).Kind);

        // A station alone is not enough: the second system has not been visited.
        int station = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 30 * Units.CellUnits);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        Assert.True(GameOutcome.HasBase(w.Network, 0));
        Assert.False(GameOutcome.HasExplored(w.Network));
        Assert.Equal(0, GameOutcome.JumpsDone(w.Network));
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, goal).Kind);

        JumpOnce(w);
        Assert.Equal(1, w.Network.JumpsCompleted);
        Assert.True(GameOutcome.HasExplored(w.Network));
        Assert.Equal(1, GameOutcome.JumpsDone(w.Network));
        var r = GameOutcome.Evaluate(w.Network, 0, goal);
        Assert.Equal(OutcomeKind.Won, r.Kind);
        Assert.Equal(OutcomeReason.WavesSurvived, r.Reason);
        Assert.Equal(1, r.Stats.StationsFounded);
        Assert.Equal(0, r.Stats.StationsLost);
        Assert.Equal(2, r.Stats.WavesSurvived);
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, new GoalSettings(3)).Kind);
    }

    [Fact]
    public void The_goal_can_come_from_world_rules()
    {
        var (w, _) = ArmedShip();
        w.Step(Tps);
        ClearWaves(w, 2);
        int station = w.Network.CreateStructure(0, StructureKind.Station, 0, 0, 30 * Units.CellUnits);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);

        Assert.Equal(OutcomeKind.Won, GameOutcome.Evaluate(w.Network, 0, new WorldRules { GoalWaves = 2 }).Kind);
        Assert.Equal(OutcomeKind.Won, GameOutcome.Evaluate(w.Network, 0, new WorldRules { GoalWaves = 1 }).Kind);
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, WorldRules.Hard).Kind); // 12 waves
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, new WorldRules { GoalWaves = 3 }).Kind);
        // The world's own rules (default here: 10 waves) are used when none are passed.
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0).Kind);
        Assert.Equal(8, GoalSettings.From(WorldRules.Relaxed).WavesToWin);
        Assert.Equal(15, GoalSettings.From(WorldRules.Brutal).WavesToWin);
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
