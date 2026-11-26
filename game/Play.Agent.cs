using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>A read-only state snapshot for agent-driven runs (the harness reads it through run_script: <c>play.call("AgentState")</c>).</summary>
public partial class Play
{
    /// <summary>JSON: tick, ship position and velocity, speed, hold, seat, docking, arsenal, laser, scenario outcome and goal progress.</summary>
    public string AgentState()
    {
        if (!Net.HasPlayer(Player)) return "{\"player\":false}";
        var info = Net.GetPlayerInfo(Player);
        var hold = new Dictionary<string, int>();
        foreach (var item in _content.Items)
        {
            int count = Net.HoldCount(Player, item.Index);
            if (count > 0) hold[item.Id] = count;
        }
        var goals = new List<object>();
        if (_progress is not null)
            foreach (var g in _progress.Goals) goals.Add(new { id = g.Goal.Id, status = g.Status.ToString(), g.Current, g.Target });
        var deposits = new List<object>();
        var inv = _camera.GlobalTransform.AffineInverse();
        foreach (int d in RevealedDeposits())
        {
            if (Net.DepositRemaining(d) <= 0) continue;
            var q = Net.DepositPosition(d);
            var world = new Vector3(q.X, q.Y, q.Z);
            var local = inv * world; // camera space: -Z is forward
            deposits.Add(new { id = d, item = _content.Items[Net.DepositItem(d)].Id, remaining = Net.DepositRemaining(d), dist = (world - _camera.GlobalPosition).Length(), cx = local.X, cy = local.Y, cz = local.Z, inRange = Net.InMineRange(Player, d) });
        }
        return JsonSerializer.Serialize(new
        {
            player = true,
            tick = Net.Tick,
            pos = new[] { info.X, info.Y, info.Z },
            vel = new[] { info.VelX, info.VelY, info.VelZ },
            speedCps = _speedCps,
            hold,
            holdTotal = info.HoldTotal,
            seated = _seated,
            docked = IsDockedNow,
            paused = _paused,
            arsenalSelected = _arsenal.Selected,
            armed = info.Armed,
            miningDeposit = info.MiningDeposit,
            laserFiring = Net.LaserFiring(Player),
            scenario = _scenario?.Id,
            outcome = _progress?.Outcome.ToString(),
            outcomeShown = _outcomeShown,
            goals,
            deposits,
            hover = new { kind = _hover.Kind.ToString(), id = _hover.Id },
            mouseLook = _mouseLook,
            computerOpen = ComputerOpen,
            inventoryOpen = InventoryOpen,
            pauseOpen = PauseOpen,
        });
    }
}
