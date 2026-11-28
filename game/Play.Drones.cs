using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Drone automation in the game: F7 assigns the drone bays of a structure to the selected deposit, the status panel shows what they
/// do, and while a structure's bays work a few small drones fly between its bays and the deposit along a faint beam. The drones are
/// cosmetic and client only (the simulation has no drone entities); the numbers come from <c>BeltNetwork.DroneStatus</c>.
/// </summary>
public partial class Play
{
    private Node3D _droneRoot = null!;
    private readonly Dictionary<int, DroneFlock> _flocks = new();
    private readonly List<int> _droneStructs = new(); // the player's structures that have a bay, refreshed when the structures change
    private int _droneVersion = -1;
    private float _droneClock;

    /// <summary>The drones of one structure: up to six craft and one beam, all reused, never freed while the structure lives.</summary>
    private sealed class DroneFlock
    {
        public readonly Node3D[] Drones = new Node3D[DroneFormat.MaxVisible];
        public readonly MeshInstance3D Beam;
        public Vector3[] Bays = Array.Empty<Vector3>(); // launch points in the structure's local cells
        public int Shown = -1;

        public DroneFlock(Node3D parent, int id)
        {
            for (int i = 0; i < Drones.Length; i++)
            {
                Drones[i] = ModuleModels.DroneCraft($"Drone{id}_{i}");
                Drones[i].Visible = false;
                parent.AddChild(Drones[i]);
            }
            Beam = new MeshInstance3D { Name = $"DroneBeam{id}", Mesh = BeamMesh, MaterialOverride = BeamMaterial, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            parent.AddChild(Beam);
        }

        public void Show(int count)
        {
            if (count == Shown) return;
            Shown = count;
            for (int i = 0; i < Drones.Length; i++) Drones[i].Visible = i < count;
            Beam.Visible = count > 0;
        }
    }

    private static readonly BoxMesh BeamMesh = new() { Size = new Vector3(0.07f, 0.07f, 1f) };
    private static readonly StandardMaterial3D BeamMaterial = new()
    {
        AlbedoColor = new Color(1f, 0.72f, 0.2f, 0.3f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true,
        Emission = new Color(1f, 0.7f, 0.2f),
        EmissionEnergyMultiplier = 0.6f,
    };

    private void BuildDroneVisuals()
    {
        _droneRoot = new Node3D { Name = "Drones" };
        AddChild(_droneRoot);
    }

    private bool DroneBayLocked(ModuleKind kind) =>
        _gameMode == GameMode.Campaign && ModuleKindIds.RequiredAbility(kind) is { } needed && !Net.HasAbility(needed);

    /// <summary>The structure F7 works on: the one aimed at or entered if it has a bay, else the ship, else the nearest of the player's bays.</summary>
    private int DroneStructure()
    {
        int target = TargetStructure();
        if (target >= 0 && Net.DroneBays(target) > 0 && Net.GetStructure(target).Owner == Player) return target;
        int ship = ShipStruct;
        if (ship >= 0 && Net.DroneBays(ship) > 0) return ship;
        var eye = ShipPosition(0f);
        int best = -1;
        float bestD = float.MaxValue;
        foreach (int id in _droneStructs)
        {
            float d = (StructOrigin(id) - eye).LengthSquared();
            if (d < bestD) { bestD = d; best = id; }
        }
        return best;
    }

    private bool DepositUsable(int d) => Net.IsDeposit(d) && Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 0;

    /// <summary>F7: assign the drones of a structure to the selected deposit; again on the same deposit (or with none selected) unassign.</summary>
    private void AssignDronesKey()
    {
        if (!_structuresOn || !Net.HasPlayer(Player)) return;
        int s = DroneStructure();
        if (s < 0)
        {
            ToastLogistics(Why(DroneBayLocked(ModuleKind.DroneBay) ? Reject.Locked : Reject.NoDroneBay));
            return;
        }
        int current = Net.AssignedDeposit(s);
        int dep = DepositUsable(_selectedDeposit) ? _selectedDeposit : -1;
        if (current >= 0 && (dep < 0 || dep == current)) { Send(new AssignDrones(s, -1)); return; }
        if (dep < 0) { ToastLogistics(L("toast.dronesNoDeposit")); return; }
        if (!Net.DepositInDroneRange(s, dep))
        {
            ToastLogistics(L("toast.dronesOutOfRange", _content.Structures?.DroneRangeCells(ModuleKind.DroneBay) ?? 60));
            return;
        }
        Send(new AssignDrones(s, dep));
    }

    private void DronesAssignedToast(AssignDrones ad)
    {
        if (ad.Deposit < 0) { ToastProduction(L("toast.dronesUnassigned")); return; }
        var st = Net.DroneStatus(ad.Structure);
        ToastProduction(L("toast.dronesAssigned", ItemName(Net.DepositItem(ad.Deposit)), st.RatePerMinute));
    }

    private bool DepositHasDrones(int deposit)
    {
        foreach (int id in _droneStructs)
            if (Net.AssignedDeposit(id) == deposit) return true;
        return false;
    }

    /// <summary>One status line per structure with bays: "Drones (Ship): 2 bays, mining Iron ore at 40/min".</summary>
    private void DroneHud(StringBuilder sb)
    {
        foreach (int id in _droneStructs)
        {
            var st = Net.DroneStatus(id);
            string item = st.Deposit >= 0 ? ItemName(Net.DepositItem(st.Deposit)) : "";
            string name = Net.GetStructure(id).Kind == StructureKind.Ship ? L("structure.ship") : L("structure.station");
            sb.Append(DroneFormat.Line(name, st, item)).Append('\n');
        }
    }

    /// <summary>Markers: other assigned deposits, and during the guided step the nearest deposit in range of an unassigned bay.</summary>
    private void DroneMarkers(Action<string, Color, Vector3, float> add)
    {
        foreach (int id in _droneStructs)
        {
            int dep = Net.AssignedDeposit(id);
            if (dep >= 0 && dep != _selectedDeposit && Net.IsDepositRevealed(dep))
            {
                var p = Net.DepositPosition(dep);
                add($"{ItemName(Net.DepositItem(dep))} - {L("mark.drones")}", new Color(1f, 0.72f, 0.2f), View.FromCells(p.X, p.Y, p.Z), 6f);
            }
        }
        if (_objectives.Current != Starfoundry.Ui.Objective.Automate) return;
        foreach (int id in _droneStructs)
        {
            if (Net.AssignedDeposit(id) >= 0) continue;
            int best = -1;
            float bestD = float.MaxValue;
            var eye = ShipPosition(0f);
            for (int d = 0; d < Net.DepositCount; d++)
            {
                if (!DepositUsable(d) || !Net.DepositInDroneRange(id, d)) continue;
                var p = Net.DepositPosition(d);
                float dist = (View.FromCells(p.X, p.Y, p.Z) - eye).LengthSquared();
                if (dist < bestD) { bestD = dist; best = d; }
            }
            if (best < 0) continue;
            var q = Net.DepositPosition(best);
            add(L("mark.droneTarget"), new Color(1f, 0.72f, 0.2f), View.FromCells(q.X, q.Y, q.Z), 6f);
            break;
        }
    }

    private void RefreshDroneStructures()
    {
        _droneVersion = Net.StructureVersion;
        _droneStructs.Clear();
        foreach (var st in Net.Structures())
        {
            if (st.Owner != Player || Net.DroneBays(st.Id) == 0) continue;
            _droneStructs.Add(st.Id);
            if (!_flocks.TryGetValue(st.Id, out var flock)) _flocks[st.Id] = flock = new DroneFlock(_droneRoot, st.Id);
            var bays = new List<Vector3>();
            foreach (var (key, kind) in st.Modules)
                if (kind == ModuleKind.DroneBay) bays.Add(ModuleMin(key.Mx, key.My, key.Mz) + new Vector3(ModuleSize.X / 2f, ModuleSize.Y - 0.3f, ModuleSize.Z / 2f));
            flock.Bays = bays.ToArray();
        }
        foreach (int id in _flocks.Keys.Where(i => !_droneStructs.Contains(i)).ToList())
        {
            _flocks[id].Show(0);
            _flocks[id].Bays = Array.Empty<Vector3>();
        }
    }

    /// <summary>Per frame: flies the drones of every working structure. Moves existing nodes only, nothing is allocated.</summary>
    private void UpdateDrones(float dt)
    {
        if (!_structuresOn) return;
        _droneClock += dt;
        if (Net.StructureVersion != _droneVersion) RefreshDroneStructures();
        for (int k = 0; k < _droneStructs.Count; k++)
        {
            int id = _droneStructs[k];
            var flock = _flocks[id];
            var status = Net.DroneStatus(id);
            int n = DroneFormat.VisibleDrones(status);
            var root = _stationView.StructureRoot(id);
            if (n == 0 || root is null || flock.Bays.Length == 0 || !DepositUsable(status.Deposit)) { flock.Show(0); continue; }
            flock.Show(n);

            var dp = Net.DepositPosition(status.Deposit);
            var target = View.FromCells(dp.X, dp.Y, dp.Z);
            var first = root.Transform * flock.Bays[0];

            // the beam from the first bay to the deposit
            var span = target - first;
            float length = span.Length();
            if (length > 0.5f)
            {
                var b = Basis.LookingAt(span / length, MathF.Abs(span.Y / length) > 0.98f ? Vector3.Forward : Vector3.Up).Scaled(new Vector3(1f, 1f, length));
                flock.Beam.Transform = new Transform3D(b, first + span * 0.5f);
            }
            else flock.Beam.Visible = false;

            float period = 4f + 2f * length / 9f; // seconds for a round trip: longer for far deposits
            for (int j = 0; j < n; j++)
            {
                var start = root.Transform * flock.Bays[j % flock.Bays.Length];
                float u = _droneClock / period + j / (float)n;
                u -= MathF.Floor(u);
                float angle = j * (Mathf.Tau / n) + _droneClock * 1.3f;
                var hover = target + new Vector3(MathF.Cos(angle) * 2.4f, 1.2f, MathF.Sin(angle) * 2.4f);
                float s; bool outbound = u < 0.42f, dwell = u >= 0.42f && u < 0.58f;
                if (outbound) s = u / 0.42f;
                else if (dwell) s = 1f;
                else s = 1f - (u - 0.58f) / 0.42f;
                s = s * s * (3f - 2f * s); // smoothstep: leaves and arrives slowly
                var pos = start.Lerp(hover, s) + Vector3.Up * (MathF.Sin(MathF.PI * s) * MathF.Min(3f, length * 0.08f));
                var fwd = dwell ? target - pos : outbound ? hover - start : start - hover;
                if (fwd.LengthSquared() < 1e-4f) fwd = Vector3.Forward;
                fwd = fwd.Normalized();
                var basis = Basis.LookingAt(fwd, MathF.Abs(fwd.Y) > 0.98f ? Vector3.Forward : Vector3.Up);
                flock.Drones[j].Transform = new Transform3D(basis, pos + Vector3.Up * (dwell ? 0.15f * MathF.Sin(_droneClock * 6f + j) : 0f));
            }
        }
    }
}
