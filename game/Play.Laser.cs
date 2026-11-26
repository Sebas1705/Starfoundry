using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The ship's mining laser, seen. The simulation mines only while it holds a mining order (see <c>UpdateLaser</c>, which sends one
/// while the fire button is down on a deposit in range); this draws the beam for exactly that state: a pulsing core and a halo from
/// the ship's nose to the rock, and a hot spot where it lands.
/// </summary>
public partial class Play
{
    private MeshInstance3D? _laserCore, _laserHalo, _laserSpot;
    private StandardMaterial3D? _laserCoreMat, _laserHaloMat;

    private void EnsureLaserNodes()
    {
        if (_laserCore is not null) return;
        _laserCoreMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 1f, 0.8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _laserHaloMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 1f, 0.55f, 0.35f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _laserCore = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _laserCoreMat, Visible = false };
        _laserHalo = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _laserHaloMat, Visible = false };
        _laserSpot = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f }, MaterialOverride = _laserCoreMat, Visible = false };
        AddChild(_laserCore);
        AddChild(_laserHalo);
        AddChild(_laserSpot);
    }

    /// <summary>Called every frame: shows the beam while the simulation mines (the order is on and the deposit is in range).</summary>
    private void UpdateLaserFx()
    {
        int dep = Net.HasPlayer(Player) && Net.LaserFiring(Player) ? Net.MiningDeposit(Player) : -1;
        bool on = dep >= 0 && Net.IsDeposit(dep) && Net.InMineRange(Player, dep) && !Standing;
        if (!on)
        {
            if (_laserCore is not null) _laserCore.Visible = _laserHalo!.Visible = _laserSpot!.Visible = false;
            foreach (var (core, halo) in _extraBeams) core.Visible = halo.Visible = false;
            return;
        }
        EnsureLaserNodes();
        var d = Net.DepositPosition(dep);
        var centre = new Vector3(d.X, d.Y, d.Z);
        var ship = ShipPosition(0f);
        // The beam leaves a little below and ahead of the camera and ends on the rock's surface, facing the ship.
        // From the cockpit the emitter sits low and to the right of the view, so the beam reads as a line running to the rock instead of a dot.
        var aim = AimDirection();
        var camBasis = _camera.GlobalTransform.Basis; // free flight: "down" and "right" are the camera's, whatever the ship's attitude
        var right = camBasis.X;

        var from = Overview ? ship + Vector3.Up * 0.3f : AimOrigin() + aim * 0.9f - camBasis.Y * 0.42f + right * 0.38f;
        float surface = 1.2f * Net.DepositRockScalePermille(dep) / 1000f;
        var toShip = (ship - centre).Normalized();
        var to = centre + toShip * surface;
        float length = (to - from).Length();
        if (length < 0.05f) return;

        float pulse = 0.75f + 0.25f * MathF.Sin((float)_animTime * 38f);
        // ADR 0032: with hardpoint modules every laser that reaches the rock draws its own beam from the tip of its emitter, in the ship's attitude.
        var muzzles = LaserMuzzles(dep);
        if (muzzles.Count > 0)
        {
            for (int i = 0; i < muzzles.Count; i++)
            {
                var (core, halo) = BeamPair(i);
                float len = (to - muzzles[i]).Length();
                if (len < 0.05f) { core.Visible = halo.Visible = false; continue; }
                foreach (var (node, width) in new[] { (core, 0.07f), (halo, 0.22f) })
                {
                    node.Visible = true;
                    node.LookAtFromPosition((muzzles[i] + to) / 2f, to);
                    node.Scale = new Vector3(width * pulse, width * pulse, len);
                }
            }
            _laserCore.Visible = _laserHalo!.Visible = false;
            for (int i = muzzles.Count; i < _extraBeams.Count; i++) _extraBeams[i].Core.Visible = _extraBeams[i].Halo.Visible = false;
            _laserSpot!.Visible = true;
            _laserSpot.Position = to;
            _laserSpot.Scale = Vector3.One * (0.8f + 0.5f * pulse);
            return;
        }
        foreach (var (node, width) in new[] { (_laserCore!, 0.07f), (_laserHalo!, 0.22f) })
        {
            node.Visible = true;
            node.LookAtFromPosition((from + to) / 2f, to);
            node.Scale = new Vector3(width * pulse, width * pulse, length);
        }
        _laserSpot!.Visible = true;
        _laserSpot.Position = to;
        _laserSpot.Scale = Vector3.One * (0.8f + 0.5f * pulse);
    }
}
