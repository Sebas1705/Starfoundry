using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

public partial class Play
{
    // ---- --walk-shot=path --walk-bevel -------------------------------------------------------------------------

    private readonly System.Collections.Generic.List<(string Name, Vector3 Body, float Yaw, float Pitch)> _bevelPlan = new();
    private int _bevelStep = -1;
    private int _bevelWait;
    private string? _bevelName;

    /// <summary>
    /// <c>--walk-shot=path --walk-bevel</c>: a scripted walk into the floor and ceiling bevels of the ship's modules. For each module kind of the
    /// ship it stands in the middle, walks into the left bevel, then along it (a diagonal push), then into the right bevel, prints the
    /// positions (<c>BEVEL</c> lines) and saves a picture of each stop (<c>path.bevel.&lt;kind&gt;.&lt;step&gt;.png</c>); the cockpit is also
    /// walked into its nose. Positions come from the real <see cref="BodyMotion.Step"/> over the real body volumes.
    /// </summary>

    private bool BevelShotFrame()
    {
        if (!OS.GetCmdlineUserArgs().Contains("--walk-bevel")) return false;
        if (_walkShotFrame < 10) return true;
        if (_bevelStep == -1)
        {
            Net.AddStructureModuleUnchecked(ShipStruct, 1, 0, 0, ModuleKind.Cockpit); // a second cockpit on the flank, with no module in front: it has a nose
            _bevelStep = -2;
            return true;
        }
        if (_bevelStep == -2)
        {
            _seated = true; ToggleSeat(); // the real stand-up path
            RefreshBodyVolumes();
            var ship = Net.GetStructure(ShipStruct);
            foreach (var kind in new[] { ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Defence, ModuleKind.DroneBay, ModuleKind.Core })
                foreach (var (key, k) in ship.Modules.OrderBy(m => m.Key))
                {
                    if (k != kind) continue;
                    PlanModule(kind, new Vector3(key.Mx * StationGrid.ModuleX, key.My * StationGrid.ModuleY, key.Mz * StationGrid.ModuleZ));
                    break;
                }
            PlanModule(ModuleKind.Cockpit, new Vector3(8f, 0f, 0f), "nose"); // the flank cockpit: its nose is the sloped part
            _bevelStep = 0; _bevelWait = 0;
            return true;
        }
        if (_bevelWait > 0)
        {
            if (--_bevelWait == 0 && _bevelName is not null)
            {
                GetViewport().GetTexture().GetImage().SavePng($"{_walkShotPath}.bevel.{_bevelName}.png");
                GD.Print($"SHOT bevel {_bevelName} body={_body:0.00}");
            }
            return true;
        }
        if (_bevelStep >= _bevelPlan.Count) { GetTree().Quit(); return true; }
        var (name, body, yaw, pitch) = _bevelPlan[_bevelStep++];
        _body = body; _yaw = yaw; _pitch = pitch; _bevelName = name; _bevelWait = 14;
        return true;
    }

    private void PlanModule(ModuleKind kind, Vector3 origin, string tag = "")
    {
        var centre = origin + new Vector3(4f, 0f, 4f);
        foreach (var b in _bodyVolumes) if (Mathf.IsEqualApprox(b.MinX, origin.X) && Mathf.IsEqualApprox(b.MinY, origin.Y) && Mathf.IsEqualApprox(b.MinZ, origin.Z)) GD.Print($"BEVEL shell {kind} {b.Shell}");
        if (kind == ModuleKind.Cockpit) centre = origin + new Vector3(4f, 0f, 6.6f);
        string K(string s) => $"{kind}{tag}.{s}";
        System.Numerics.Vector3 S(Vector3 v) => new(v.X, v.Y, v.Z);
        Vector3 Walk(Vector3 from, Vector3 wish, float seconds)
        {
            var p = S(from);
            for (float t = 0; t < seconds; t += 1f / 60f) p = BodyMotion.Step(p, S(wish), BodyMotion.WalkSpeed, 1f / 60f, _bodyVolumes);
            return new Vector3(p.X, p.Y, p.Z);
        }
        // yaw: forward = (-sin yaw, 0, -cos yaw); -X is yaw = +90 degrees, +X is -90 degrees
        var left = Walk(centre, new Vector3(-1, 0, 0), 4f);
        var along = Walk(left, new Vector3(-1, 0, 1), 1.0f);
        var right = Walk(centre, new Vector3(1, 0, 0), 4f);
        var alongR = Walk(right, new Vector3(1, 0, -1), 1.0f);
        GD.Print($"BEVEL {kind} centre={centre:0.00} left={left:0.00} (d={centre.X - left.X:0.00}) along={along:0.00} right={right:0.00} (d={right.X - centre.X:0.00}) alongR={alongR:0.00}");
        _bevelPlan.Add((K("left"), left, MathF.PI / 2f, -0.55f));
        _bevelPlan.Add((K("along"), along, MathF.PI / 2f + 0.5f, -0.6f));
        _bevelPlan.Add((K("right"), right, -MathF.PI / 2f, -0.55f));
        _bevelPlan.Add((K("ceiling"), left, MathF.PI / 2f, 0.6f));
        if (kind == ModuleKind.Cockpit)
        {
            var nose = Walk(centre, new Vector3(0, 0, -1), 6f);
            var noseSide = Walk(origin + new Vector3(6f, 0f, 6.6f), new Vector3(0.5f, 0, -1), 6f);
            GD.Print($"BEVEL Cockpit nose={nose:0.00} noseSide={noseSide:0.00}");
            _bevelPlan.Add((K("nose"), nose, 0f, 0.1f));
            _bevelPlan.Add((K("noseside"), noseSide, 0f, -0.2f));
        }
        if (kind == ModuleKind.Engine)
        {
            var tail = Walk(centre, new Vector3(0, 0, 1), 6f);
            GD.Print($"BEVEL Engine tail={tail:0.00}");
            _bevelPlan.Add((K("tail"), tail, MathF.PI, 0.1f));
        }
    }
}
