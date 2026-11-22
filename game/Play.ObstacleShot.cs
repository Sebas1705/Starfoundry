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
        // The seat controls send "no thrust" when nothing is held; the scripted flights keep their thrust by sending it every frame.
        if (_obsFrame > 4 && _obsThrust != default && _world.PendingCommands == 0) Send(new SetThrust(_obsThrust.X, _obsThrust.Y, _obsThrust.Z));
        if (_obsScene == "hit" && _obsImpact && _obsSaveAt > _obsFrame + 5) _obsSaveAt = _obsFrame + 5; // a few frames after the first impact
        if (_obsSaveAt > 0 && _obsFrame >= _obsSaveAt)
        {
            Save(_obsShotPath);
            _obsSaveAt = -1;
            GetTree().Quit();
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
