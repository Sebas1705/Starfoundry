using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--obstacle-shot=path --obs-scene=name</c>: puts the starter ship next to an obstacle and saves one image, for checking the models
/// and the hazard HUD. Scenes: <c>rocks</c> (an asteroid belt from the seat), <c>rock</c> (a big rock and the ship from outside),
/// <c>mine</c>, <c>armed</c> (a mine whose fuse burns), <c>crystal</c>, <c>ion</c> and <c>gravity</c> (from inside the cloud), <c>cloudout</c>
/// (a cloud from outside), <c>wreck</c>, <c>debris</c>, <c>boost</c> (flying at boost with the HUD) and <c>hit</c> (just after ramming a rock at boost).
/// The ship is moved with the setup hook <see cref="BeltNetwork.SetStructureOrigin"/>; nothing of this is a saved game.
/// </summary>
public partial class Play
{
    private string? _obsShotPath;
    private string _obsScene = "rocks";
    private int _obsFrame, _obsSaveAt = -1;
    private (int X, int Y, int Z) _obsThrust;
    private bool _obsImpact;
    private int _obsStopAt = -1;
    private long _obsSaveTick = -1, _obsStopTick = -1; // the speed scenes count simulation ticks, not frames: the frames run faster than the ticks

    private void ObstacleShotSetup(string[] args)
    {
        _obsShotPath = ShotArg(args, "--obstacle-shot");
        _obsScene = ShotArg(args, "--obs-scene") ?? "rocks";
        _wantCapture = false;
        _shotPhase = 3;
    }

    private int FindObstacle(string kindId, int system = -1, Func<ObstacleInfo, bool>? where = null)
    {
        var w = _content.World!;
        int k = w.KindIndex(kindId);
        return Enumerable.Range(0, Net.ObstacleCount).Select(Net.GetObstacle)
            .Where(o => o.Kind == k && (system < 0 || o.System == system) && (where is null || where(o)))
            .OrderBy(o => (double)o.X * o.X + (double)o.Z * o.Z).Select(o => o.Id).DefaultIfEmpty(-1).First();
    }

    private void Teleport(Vector3 cells)
    {
        long cu = Units.CellUnits;
        Net.SetStructureOrigin(ShipStruct, (long)(cells.X * cu), (long)(cells.Y * cu), (long)(cells.Z * cu));
    }

    private void LookFromSeat(Vector3 target)
    {
        var d = (target - (ShipPosition(0f) + EyeLocal())).Normalized();
        _yaw = MathF.Atan2(-d.X, -d.Z);
        _pitch = MathF.Asin(d.Y);
        _shotOutside = false;
    }

    private void ObstacleShotFrame()
    {
        if (_obsShotPath is null || ShipStruct < 0) return;
        _obsFrame++;
        if (_obsFrame == 4) SetUpObsScene();
        if (_obsStopTick > 0 && Net.Tick >= _obsStopTick) { _obsStopTick = -1; _obsThrust = default; Send(new SetThrust(0, 0, 0)); } // the brake scene lets go of the throttle here
        if (_obsSaveTick > 0 && Net.Tick >= _obsSaveTick)
        {
            if (_obsScene is "depositrock" or "rockmargin") PrintHitMargin();
            Save(_obsShotPath); _obsSaveTick = -1; GetTree().Quit();
        }
        // The seat controls send "no thrust" when nothing is held; the scripted flights keep their thrust by sending it every frame.
        if (_obsFrame > 4 && _obsThrust != default && _world.PendingCommands == 0) Send(new SetThrust(_obsThrust.X, _obsThrust.Y, _obsThrust.Z));
        if (_obsScene is "hit" && _obsImpact && _obsSaveAt > _obsFrame + 5) _obsSaveAt = _obsFrame + 5; // a few frames after the first impact
        if (_obsScene is "depositrock" or "rockmargin" && _obsImpact && _obsSaveTick > Net.Tick + 10) _obsSaveTick = Net.Tick + 10; // ten ticks after the first impact
        if (_obsSaveAt > 0 && _obsFrame >= _obsSaveAt)
        {
            Save(_obsShotPath);
            _obsSaveAt = -1;
            GetTree().Quit();
        }
    }

    /// <summary>
    /// ADR 0033: after the ship has hit a rock, how close the drawn rock comes to the ship's module boxes: the smallest distance from any vertex of the
    /// rock model (in the world, as drawn) to any module box, in cells. Positive: the rock does not enter the boxes; the hull is drawn within its boxes
    /// plus a little, which the padding covers. Printed as <c>MARGIN ...</c>.
    /// </summary>
    private void PrintHitMargin()
    {
        var views = new System.Collections.Generic.List<Node3D>();
        if (_obsScene == "rockmargin") { if (_obstacleLayer?.GetNodeOrNull<Node3D>("Obstacle" + FindObstacle("core:asteroid_big", 0)) is { } v) views.Add(v); }
        else { int d = DefaultDeposit(); if (d >= 0 && d < _depositNodes.Count) views.Add(_depositNodes[d]); }
        var s = Net.GetStructure(ShipStruct);
        float best = float.MaxValue;
        foreach (var view in views)
            foreach (var mi in EnumerateMeshes(view))
                foreach (var vert in mi.Mesh.GetFaces())
                {
                    var w = mi.GlobalTransform * vert;
                    foreach (var key in s.Modules.Keys)
                    {
                        var (min, max) = Net.ModuleWorldBounds(s, key);
                        var lo = new Vector3(min.X, min.Y, min.Z) / CellUnits; var hi = new Vector3(max.X, max.Y, max.Z) / CellUnits;
                        float dx = MathF.Max(MathF.Max(lo.X - w.X, 0f), w.X - hi.X), dy = MathF.Max(MathF.Max(lo.Y - w.Y, 0f), w.Y - hi.Y), dz = MathF.Max(MathF.Max(lo.Z - w.Z, 0f), w.Z - hi.Z);
                        best = MathF.Min(best, MathF.Sqrt(dx * dx + dy * dy + dz * dz));
                    }
                }
        GD.Print($"MARGIN {_obsScene}: nearest drawn rock vertex to a module box {best:F3} cells (hull pad {_content.World!.HullPadUnits / (float)CellUnits:F2} cells)");
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D> EnumerateMeshes(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is MeshInstance3D mi && mi.Mesh is not null && c.Name != "Blast") yield return mi;
            foreach (var m in EnumerateMeshes(c)) yield return m;
        }
    }

    private void SetUpObsScene()
    {
        _speed = 1;
        Vector3 P(ObstacleInfo o) => new Vector3(o.X, o.Y, o.Z) / CellUnits;
        switch (_obsScene)
        {
            case "rocks":
            {
                // From the seat at home, looking at the densest direction of rocks.
                var rocks = Enumerable.Range(0, Net.ObstacleCount).Select(Net.GetObstacle).Where(o => o.System == 0 && o.Behaviour == ObstacleBehaviour.Solid).ToList();
                var best = rocks.OrderByDescending(a => rocks.Count(b => (P(a) - P(b)).Length() < 60f)).First();
                var c = P(best);
                Teleport(c + new Vector3(-8f, 20f, 70f));
                LookFromSeat(c + new Vector3(0f, 0f, 0f));
                _obsSaveAt = _obsFrame + 8;
                break;
            }
            case "rock":
            {
                var o = Net.GetObstacle(FindObstacle("core:asteroid_big", 0));
                var c = P(o);
                Teleport(c + new Vector3(-16f, o.Radius / (float)CellUnits + 3f, -4f));
                _shotCam = (c + new Vector3(-40f, 18f, 30f), c);
                _shotOutside = true;
                _obsSaveAt = _obsFrame + 8;
                break;
            }
            case "mine":
            case "armed":
            {
                var o = Net.GetObstacle(FindObstacle("core:floating_mine"));
                var c = P(o);
                bool armed = _obsScene == "armed";
                Teleport(c + new Vector3(-4f, -2f - (armed ? 10f : 0f), -(armed ? 0f : 34f) - 4f));
                _shotCam = (c + new Vector3(-20f, 8f, -26f), c);
                if (!armed) LookFromSeat(c);
                else { _shotOutside = true; }
                _obsSaveAt = _obsFrame + (armed ? 14 : 8);
                break;
            }
            case "crystal":
            {
                var o = Net.GetObstacle(FindObstacle("core:unstable_crystal"));
                var c = P(o);
                Teleport(c + new Vector3(-4f, -2f, -40f));
                LookFromSeat(c);
                _obsSaveAt = _obsFrame + 8;
                break;
            }
            case "ion":
            case "gravity":
            {
                var o = Net.GetObstacle(FindObstacle(_obsScene == "ion" ? "core:ion_cloud" : "core:gravity_cloud"));
                var c = P(o);
                Teleport(c + new Vector3(-4f, 0f, 0f));
                _yaw = 0.6f; _pitch = 0.05f; _shotOutside = false;
                _obsSaveAt = _obsFrame + 10;
                break;
            }
            case "cloudout":
            {
                var o = Net.GetObstacle(FindObstacle("core:ion_cloud"));
                var c = P(o);
                float r = o.Radius / (float)CellUnits;
                Teleport(c + new Vector3(r * 0.9f, 0f, 0f));
                _shotCam = (c + new Vector3(-r * 1.8f, r * 0.35f, r * 1.8f), c);
                _shotOutside = true;
                _obsSaveAt = _obsFrame + 8;
                break;
            }
            case "wreck":
            {
                var o = Net.GetObstacle(FindObstacle("core:derelict_wreck", 0));
                var c = P(o);
                Teleport(c + new Vector3(-30f, -2f, -4f));
                _shotCam = (c + new Vector3(-14f, 6f, -14f), c);
                _shotOutside = true;
                _obsSaveAt = _obsFrame + 8;
                break;
            }
            case "debris":
            {
                var o = Net.GetObstacle(FindObstacle("core:drifting_debris"));
                var c = P(o);
                Teleport(c + new Vector3(-30f, -2f, -34f));
                _shotCam = (c + new Vector3(-8f, 3f, -10f), c);
                _shotOutside = true;
                _obsSaveAt = _obsFrame + 8;
                break;
            }
            // Speed feel (ADR 0033): the cockpit view and the HUD a moment after the throttle goes full (the ramp), at cruise, at boost and while braking.
            case "ramp": case "cruise": case "boostcruise": case "brake":
            {
                _yaw = 0f; _pitch = 0f; _shotOutside = false;
                if (_obsScene == "boostcruise") Send(new SetBoost(true));
                _obsThrust = (0, 0, -1024);
                _obsSaveTick = Net.Tick + _obsScene switch { "ramp" => 16, "cruise" => 70, "boostcruise" => 80, _ => 62 };
                if (_obsScene == "brake") _obsStopTick = Net.Tick + 50; // full speed after the ramp, then the throttle is released: the shot is 12 ticks into the braking
                break;
            }
            // Solid deposits and the hit volume (ADR 0033): the ship dives onto the rock of a deposit, seen from the side a few frames after the first impact.
            case "depositrock":
            {
                var (dx, dy, dz) = Net.DepositPosition(DefaultDeposit());
                var c = new Vector3(dx, dy, dz);
                Teleport(c + new Vector3(-4f, 14f, -4f));
                _shotCam = (c + new Vector3(-16f, 5f, 12f), c + new Vector3(0f, 2f, 0f));
                _shotOutside = true;
                _obsThrust = (0, -1024, 0);
                _obsSaveTick = Net.Tick + 600; // shortened to a few ticks after the first impact (see ObstacleShotFrame)
                break;
            }
            case "rockmargin":
            {
                var o = Net.GetObstacle(FindObstacle("core:asteroid_big", 0));
                var c = P(o);
                float r = o.Radius / (float)CellUnits;
                Teleport(c + new Vector3(-4f, r + 14f, -4f));
                _shotCam = (c + new Vector3(-6f, r * 1.55f + 1f, 16f), c + new Vector3(0f, r * 1.4f, 0f));
                _shotOutside = true;
                _obsThrust = (0, -1024, 0);
                _obsSaveTick = Net.Tick + 600; // shortened to a few ticks after the first impact (see ObstacleShotFrame)
                break;
            }
            case "boost":
            {
                _yaw = 0f; _pitch = 0f; _shotOutside = false;
                Send(new SetBoost(true));
                _obsThrust = (0, 0, -1024);
                _obsSaveAt = _obsFrame + 40;
                break;
            }
            case "hit":
            {
                var o = Net.GetObstacle(FindObstacle("core:asteroid_small", 0));
                var c = P(o);
                float r = o.Radius / (float)CellUnits;
                Teleport(c + new Vector3(-4f, r + 6f, -4f));
                _yaw = 0f; _pitch = -0.2f; _shotOutside = false;
                Send(new SetBoost(true));
                _obsThrust = (0, -1024, 0);
                _speed = 4;
                _obsSaveAt = _obsFrame + 400; // ends a few frames after the first impact (see ObstacleShotFrame)
                break;
            }
        }
    }
}
