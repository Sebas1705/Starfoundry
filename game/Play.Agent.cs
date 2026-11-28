using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

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
            var world = View.FromCells(q.X, q.Y, q.Z);
            var local = inv * world; // camera space: -Z is forward
            deposits.Add(new { id = d, item = _content.Items[Net.DepositItem(d)].Id, remaining = Net.DepositRemaining(d), dist = (world - _camera.GlobalPosition).Length(), cx = local.X, cy = local.Y, cz = local.Z, inRange = Net.InMineRange(Player, d) });
        }
        object? bench = null;
        if (BenchBox() is { } box && ShipStruct >= 0 && _stationView.StructureRoot(ShipStruct) is { } root)
        {
            var centre = root.GlobalTransform * (box.Min + box.Size / 2f);
            var local = inv * centre;
            bench = new { cx = local.X, cy = local.Y, cz = local.Z, dist = (centre - _camera.GlobalPosition).Length(), aimed = AimingAtBench(), recipe = info.ShipRecipe >= 0 ? _content.Recipes[info.ShipRecipe].Id : null, crafting = info.ShipCrafting, remaining = info.ShipRemaining };
        }
        object? wreck = null, station = null;
        foreach (int oi in Net.Galaxy?.ObstaclesOf(Net.CurrentSystem(Player)) ?? Array.Empty<int>())
        {
            var ob = Net.GetObstacle(oi);
            if (_content.World is null || ob.Gone || _content.World.Kinds[ob.Kind].Behaviour != ObstacleBehaviour.Wreck) continue;
            var wp = View.Cells(ob.X, ob.Y, ob.Z); var wl = inv * wp;
            wreck = new { id = oi, cx = wl.X, cy = wl.Y, cz = wl.Z, dist = (wp - _camera.GlobalPosition).Length() };
            break;
        }
        foreach (var st in Net.Structures())
        {
            if (st.Kind != StructureKind.Station) continue;
            var sp = View.Cells(st.OriginX, st.OriginY, st.OriginZ); var sl = inv * sp;
            station = new { id = st.Id, cx = sl.X, cy = sl.Y, cz = sl.Z, dist = (sp - _camera.GlobalPosition).Length() };
            break;
        }
        return JsonSerializer.Serialize(new
        {
            player = true,
            tick = Net.Tick,
            pos = new[] { info.X, info.Y, info.Z },
            vel = new[] { info.VelX, info.VelY, info.VelZ },
            speedCps = _speedCps,
            hold,
            bag = Net.ContainerStacks(Player, new ContainerRef(ContainerKind.Person)).Sum(t => t.Item3),
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
            wreck,
            station,
            dockNear = _dockNear,
            wrecksSalvaged = Net.WrecksSalvaged(),
            bench,
            scanCooldown = info.ScanCooldown,
            stackInReach = StackInReach() is not null,
            laserOn = _laserOn,
            deposits,
            hover = new { kind = _hover.Kind.ToString(), id = _hover.Id },
            mouseLook = _mouseLook,
            computerOpen = ComputerOpen,
            computerTab = _computerTab?.ToString(),
            inventoryOpen = InventoryOpen,
            pauseOpen = PauseOpen,
            savesOpen = SavesOpen,
        });
    }

    /// <summary>
    /// Agent shortcut (not a player control): turns the ship at once so the crosshair points at a deposit; returns the angle left in degrees.
    /// Mouse aiming with synthetic events is not reproducible headless, so the harness uses this and holds the real fire button after it.
    /// </summary>
    public float AgentFace(int deposit)
    {
        var q = Net.DepositPosition(deposit);
        var local = _camera.GlobalTransform.AffineInverse() * View.FromCells(q.X, q.Y, q.Z);
        float yaw = Mathf.Atan2(local.X, -local.Z), pitch = Mathf.Atan2(local.Y, Mathf.Sqrt(local.X * local.X + local.Z * local.Z));
        _shipQuat = FlightOrientation.Rotate(_shipQuat, pitch, yaw, 0f);
        return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(-local.Normalized().Z, -1f, 1f)));
    }

    /// <summary>Agent shortcut: turns the view (the head when standing) toward the workbench through the normal look path; returns the angle left in degrees.</summary>
    public float AgentFaceBench()
    {
        if (BenchBox() is not { } box || ShipStruct < 0 || _stationView.StructureRoot(ShipStruct) is not { } root) return -1f;
        var local = _camera.GlobalTransform.AffineInverse() * (root.GlobalTransform * (box.Min + box.Size / 2f));
        float yaw = Mathf.Atan2(local.X, -local.Z), pitch = Mathf.Atan2(local.Y, Mathf.Sqrt(local.X * local.X + local.Z * local.Z));
        float sens = MouseSensitivity * SettingsStore.Current.MouseSensitivity;
        LookBy(new Vector2(yaw / sens, -pitch / sens));
        return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(-local.Normalized().Z, -1f, 1f)));
    }

    /// <summary>Agent shortcut: the code behind the slot menu's "drop" entries (the menu itself is a popup window that cannot be driven headless). Returns the count dropped.</summary>
    public int AgentDrop(string itemId, int count)
    {
        int item = _content.Items.First(i => i.Id == itemId).Index;
        foreach (var (slot, it, have) in Net.ContainerStacks(Player, ContainerRef.Hold))
        {
            if (it != item) continue;
            int n = Math.Min(count, have);
            DropToWorld(ContainerRef.Hold, slot, n);
            return n;
        }
        return 0;
    }

    /// <summary>Agent shortcut: turns the ship at once toward the first wreck ("wreck") or station ("station") of the system; returns the angle left in degrees.</summary>
    public float AgentFacePoint(string which)
    {
        Vector3? target = null;
        if (which == "wreck")
            foreach (int oi in Net.Galaxy?.ObstaclesOf(Net.CurrentSystem(Player)) ?? Array.Empty<int>())
            {
                var ob = Net.GetObstacle(oi);
                if (!ob.Gone && _content.World is { } wg && wg.Kinds[ob.Kind].Behaviour == ObstacleBehaviour.Wreck) { target = View.Cells(ob.X, ob.Y, ob.Z); break; }
            }
        else
            foreach (var st in Net.Structures())
                if (st.Kind == StructureKind.Station) { target = View.Cells(st.OriginX, st.OriginY, st.OriginZ); break; }
        if (target is null) return -1f;
        var local = _camera.GlobalTransform.AffineInverse() * target.Value;
        float yaw = Mathf.Atan2(local.X, -local.Z), pitch = Mathf.Atan2(local.Y, Mathf.Sqrt(local.X * local.X + local.Z * local.Z));
        _shipQuat = FlightOrientation.Rotate(_shipQuat, pitch, yaw, 0f);
        return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(-local.Normalized().Z, -1f, 1f)));
    }
}
