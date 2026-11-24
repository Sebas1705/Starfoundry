using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The weapon in hand, free fire and the loose ore (ADR 0036). <c>M</c> cycles the group in hand (the guns or the mining laser); the left click fires it. There is no
/// stow or deploy step: the group in hand is shown on the HUD and by a guide along the barrel or emitter, which points where the crosshair does. With the guns in hand the
/// click sends <see cref="SetAim"/> (the crosshair ray, converged on what it meets, turned into the ship's frame) and arms the guns; they fire with or without an enemy,
/// tracers fly as projectiles of the simulation, hit the first thing on the path and leave sparks. Recoil kicks the camera. Ore that did not fit the hold floats as pickups.
/// </summary>
public partial class Play
{
    private const double AimResendSeconds = 0.1;
    private const float AimResendDot = 0.99985f; // about one degree
    private const float DefaultConvergeCells = 60f;
    private double _aimSentAt;

    private readonly List<MeshInstance3D> _tracers = new();
    private readonly List<(MeshInstance3D Node, float Life, float Max)> _sparks = new();
    private readonly Dictionary<int, MeshInstance3D> _pickupNodes = new();
    private StandardMaterial3D? _tracerMat, _sparkMat, _flashMat, _guideMat, _pickupMat;
    private MeshInstance3D? _guideBarrel, _guideLine;
    private Label? _weaponLabel;
    private float _kick;
    private readonly HashSet<int> _overflowToasted = new();
    private bool _dryToasted;

    // ---- the group in hand ---------------------------------------------------------------------------------------

    /// <summary>M: the next weapon group in hand. No stow or deploy step: the left click fires whatever is in hand.</summary>
    private void CycleWeaponGroup() => PickWeaponGroup(_weaponGroup == WeaponGroup.Laser ? WeaponGroup.Guns : WeaponGroup.Laser);

    /// <summary>Takes a weapon group in hand (the buttons and the cockpit weapon card use it).</summary>
    private void PickWeaponGroup(WeaponGroup group)
    {
        if (!Net.HasPlayer(Player)) return;
        if (Standing) { ToastLogistics(L("toast.sitToFly")); return; }
        if (group == WeaponGroup.Guns && !Net.HasGun(Player)) { Toast(L("toast.reject.noGun"), 5.0); return; }
        if (group == WeaponGroup.Laser && !Net.HasMiningLaser(Player)) { ToastLogistics(L("toast.reject.noLaser")); return; }
        if (group == _weaponGroup) return;
        SetWeaponGroup(group);
    }

    private void SetWeaponGroup(WeaponGroup group)
    {
        _weaponGroup = group;
        if (group == WeaponGroup.Guns)
        {
            SetLaser(false);
            Toast(L("toast.weaponGroupGuns", "M"), 3.0);
        }
        else
        {
            if (Net.GetPlayerInfo(Player).Armed) Send(new SetWeapons(false));
            Toast(L("toast.weaponGroupLaser", "M"), 3.0);
        }
        AudioManager.PlayUi(Sfx.UiClick);
    }

    /// <summary>
    /// The laser is in hand whenever it is the group in hand, the player sits, no build tool is out and no panel is open: it follows from the group, so there is no
    /// step to deploy it. Mining itself is firing it (see <c>UpdateLaser</c>).
    /// </summary>
    private void SyncLaserInHand()
    {
        bool want = _weaponGroup == WeaponGroup.Laser && !Standing && _tool != ToolMode.Build && !PanelOpen() && Net.HasPlayer(Player) && Net.HasMiningLaser(Player);
        if (want == _laserOn) return;
        if (want) { _laserOn = true; _laserOutOfRangeToasted = false; }
        else SetLaser(false);
    }

    // ---- free fire -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Per frame: with the guns in hand the click aims (when the aim moved) and arms the guns while it is held, and disarms them on release. In a world
    /// with the old feel the guns only fire at an enemy the simulation picks, and no aim is sent.
    /// </summary>
    private void UpdateGuns()
    {
        if (_weaponGroup != WeaponGroup.Guns || !Net.HasPlayer(Player)) return;
        var info = Net.GetPlayerInfo(Player);
        if (!Net.HasGun(Player)) { _weaponGroup = WeaponGroup.Laser; if (info.Armed) Send(new SetWeapons(false)); return; }
        if (_world.PendingCommands > 0) return;
        bool fire = _mouseLook && !Standing && !PanelOpen() && _tool != ToolMode.Build && _focus is null && Input.IsMouseButtonPressed(MouseButton.Left);
        var commands = new List<Command>(2);
        if (fire && !Net.Rules.LegacyFeel && AimIsDue(out var aim)) commands.Add(new SetAim(aim.X, aim.Y, aim.Z));
        if (fire != info.Armed) commands.Add(new SetWeapons(fire));
        if (commands.Count == 1) Send(commands[0]);
        else if (commands.Count > 1) Send(new Batch(commands.ToArray()));
    }

    private bool AimIsDue(out (int X, int Y, int Z) aim)
    {
        aim = default;
        if (!ComputeAim(out aim, out _)) return false;
        double now = Time.GetTicksMsec() / 1000.0;
        var current = Net.AimOf(Player);
        if (current is { } c)
        {
            var a = new Vector3(c.X, c.Y, c.Z).Normalized();
            var b = new Vector3(aim.X, aim.Y, aim.Z).Normalized();
            if (a.Dot(b) >= AimResendDot) return false;
            if (now - _aimSentAt < AimResendSeconds) return false;
        }
        _aimSentAt = now;
        return true;
    }

    /// <summary>
    /// The aim for the guns: the crosshair ray, converged on the first thing it meets (so the shots from the gun, which sits beside the eye, land where the crosshair is), with the
    /// optional assist pulling it to an enemy near the crosshair. <paramref name="local"/> is the direction in the ship's frame as the command wants it (the attitude the
    /// simulation has now, so the world direction it turns into is exactly <paramref name="world"/>).
    /// </summary>
    private bool ComputeAim(out (int X, int Y, int Z) local, out Vector3 world)
    {
        local = default; world = -Vector3.Forward;
        if (!Net.HasPlayer(Player)) return false;
        var origin = AimOrigin();
        var dir = AimDirection();
        var target = origin + dir * ConvergeCells(origin, dir);
        if (SettingsStore.Current.AimAssist && EnemyNearCrosshair(origin, dir) is { } enemy) target = enemy;
        var muzzle = NearestGun(target);
        var d = target - muzzle;
        d = d.LengthSquared() < 0.25f ? dir : d.Normalized();
        world = d;
        var basis = ShipStruct >= 0 ? new Basis(ToGodot(ShipAttitude.ToQuaternion(Net.AttitudeOf(ShipStruct)))) : Basis.Identity;
        local = ToThrust(basis.Inverse() * d);
        return (local.X | local.Y | local.Z) != 0;
    }

    private static Quaternion ToGodot(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>Where the gun nearest to a world point is now, in cells (the ship's position without gun modules).</summary>
    private Vector3 NearestGun(Vector3 toward)
    {
        var best = ShipPosition(0f);
        float bestD = float.MaxValue;
        foreach (var g in Net.Guns(Player))
        {
            var at = new Vector3(g.X, g.Y, g.Z) / CellUnits;
            float d = (at - toward).LengthSquared();
            if (d < bestD) { bestD = d; best = at; }
        }
        return best;
    }

    /// <summary>The distance along a ray to the first deposit, enemy or obstacle it meets, in cells; the default distance when it meets nothing.</summary>
    private float ConvergeCells(Vector3 origin, Vector3 dir)
    {
        float best = DefaultConvergeCells;
        void Test(Vector3 centre, float radius)
        {
            if (RayHitsSphere(origin, dir, centre, radius, out float t) && t > AimMinCells && t < best) best = t;
        }
        for (int i = 0; i < Net.DepositCount; i++)
        {
            if (Net.DepositRemaining(i) <= 0 || !Net.IsDepositRevealed(i)) continue;
            var p = Net.DepositPosition(i);
            float radius = Math.Max(Net.DepositRockRadius(i), DepositRules.NominalRadiusUnits(Net.DepositInitial(i))) / CellUnits;
            Test(new Vector3(p.X, p.Y, p.Z), radius);
        }
        foreach (var e in Net.Enemies()) Test(new Vector3(e.X, e.Y, e.Z) / CellUnits, FeelRules.EnemyHitRadiusUnits / CellUnits);
        int sys = Net.CurrentSystem(Player);
        if (Net.Galaxy is { } galaxy && sys >= 0 && galaxy.Obstacles.Count > 0)
            foreach (int i in galaxy.ObstaclesOf(sys))
            {
                var o = Net.GetObstacle(i);
                if (o.Gone || o.Behaviour == ObstacleBehaviour.Cloud) continue;
                Test(new Vector3(o.X, o.Y, o.Z) / CellUnits, Net.ObstacleHitRadius(i, padded: false) / CellUnits);
            }
        return best;
    }

    /// <summary>The nearest enemy within a narrow cone around the crosshair and in range of the guns, as a point in cells; null when there is none.</summary>
    private Vector3? EnemyNearCrosshair(Vector3 origin, Vector3 dir)
    {
        float range = (Net.Content?.Threat?.ShipWeaponRangeCells ?? 25) + 2f;
        Vector3? best = null;
        float bestDot = MathF.Cos(Mathf.DegToRad(5f));
        foreach (var e in Net.Enemies())
        {
            var at = new Vector3(e.X, e.Y, e.Z) / CellUnits;
            var to = at - origin;
            float len = to.Length();
            if (len < 1f || len > range) continue;
            float dot = dir.Dot(to / len);
            if (dot > bestDot) { bestDot = dot; best = at; }
        }
        return best;
    }

    // ---- what the simulation says happened (after every tick) -----------------------------------------------------

    /// <summary>Called after every simulation tick: impacts become sparks, pickups become toasts, a dry hold clicks, a volley kicks the camera.</summary>
    private void WatchFeel()
    {
        foreach (var hit in Net.Impacts)
        {
            var at = new Vector3(hit.X, hit.Y, hit.Z) / CellUnits;
            Spark(at, hit.Kind == ImpactKind.Enemy ? new Color(1f, 0.55f, 0.25f) : new Color(1f, 0.9f, 0.6f), hit.Destroyed ? 1.1f : 0.55f);
            _audio?.Play(hit.Kind is ImpactKind.Enemy ? Sfx.ModuleHit : Sfx.RockThud, at);
            if (hit.Destroyed && hit.Kind == ImpactKind.Debris) Explode(at, 1.6f, 0f);
        }
        foreach (var e in Net.PickupEvents)
        {
            if (e.Kind == PickupEventKind.Created && _overflowToasted.Add(e.Source)) ToastLogistics(L("toast.holdOverflow"), 7.0);
            else if (e.Kind == PickupEventKind.Collected) _audio?.Play(Sfx.MinePop);
        }
        if (Net.DryFired(Player))
        {
            AudioManager.PlayUi(Sfx.UiError);
            if (!_dryToasted) { _dryToasted = true; ToastLogistics(L("toast.dryFire"), 4.0); }
        }
        else if (!Net.GetPlayerInfo(Player).Armed) _dryToasted = false;

        // A volley of ours: kick the camera by the recoil of the guns (a small push for the main guns, which have none).
        foreach (var s in Net.Shots)
        {
            if (s.Kind != ShotKind.Ship) continue;
            float recoil = Net.GunRecoil(Player) / 1000f;
            _kick = Math.Max(_kick, 0.35f + Math.Min(3.5f, recoil * 1.2f));
            _shake = MathF.Max(_shake, 0.04f + Math.Min(0.4f, recoil * 0.1f));
            break;
        }
    }

    private void Spark(Vector3 at, Color colour, float size)
    {
        if (_sparks.Count >= 30) return;
        _sparkMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = colour };
        var mat = (StandardMaterial3D)_sparkMat.Duplicate();
        mat.AlbedoColor = colour;
        var node = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 }, MaterialOverride = mat, Position = at, Scale = Vector3.One * size * 0.4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        _sparks.Add((node, 0.28f, 0.28f));
    }

    // ---- every frame -----------------------------------------------------------------------------------------------

    /// <summary>Per frame: tracers, sparks, the aiming guide, the pickups, the camera kick and the label of the group in hand.</summary>
    private void UpdateWeaponFx(float dt)
    {
        UpdateTracers();
        UpdateSparks(dt);
        UpdatePickups();
        UpdateGuide();
        UpdateWeaponLabel();
        if (_kick > 0.001f)
        {
            // The kick tips the camera up and a little sideways, then settles; applied after the camera was placed, so it never builds up.
            float k = _kick;
            var t = _camera.Transform;
            t.Basis = t.Basis * new Basis(Vector3.Right, Mathf.DegToRad(k)) * new Basis(Vector3.Up, Mathf.DegToRad(k * 0.25f * MathF.Sin((float)_animTime * 91f)));
            _camera.Transform = t;
            _kick = Math.Max(0f, _kick - (0.6f + _kick * 9f) * dt);
        }
    }

    private void UpdateTracers()
    {
        EnsureFeelMaterials();
        var list = Net.Projectiles();
        for (int i = 0; i < list.Count; i++)
        {
            while (_tracers.Count <= i)
            {
                var n = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 1f) }, MaterialOverride = _tracerMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(n);
                _tracers.Add(n);
            }
            var q = list[i];
            var pos = new Vector3(q.X, q.Y, q.Z) / CellUnits;
            var vel = new Vector3(q.Vx, q.Vy, q.Vz) / CellUnits;
            if (vel.LengthSquared() < 1e-8f) { _tracers[i].Visible = false; continue; }
            // The streak ends at the projectile and trails the length of its last two ticks.
            var tail = pos - vel * 2f;
            _tracers[i].Visible = true;
            _tracers[i].LookAtFromPosition((pos + tail) / 2f, pos);
            _tracers[i].Scale = new Vector3(1f, 1f, (pos - tail).Length());
        }
        for (int i = list.Count; i < _tracers.Count; i++) _tracers[i].Visible = false;
    }

    private void UpdateSparks(float dt)
    {
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var (node, life, max) = _sparks[i];
            life -= dt;
            if (life <= 0f) { node.QueueFree(); _sparks.RemoveAt(i); continue; }
            float f = life / max;
            node.Scale *= 1f + 5f * dt;
            if (node.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = new Color(m.AlbedoColor, f);
            _sparks[i] = (node, life, max);
        }
    }

    private void UpdatePickups()
    {
        if (Net.PickupCount == 0 && _pickupNodes.Count == 0) return;
        EnsureFeelMaterials();
        var seen = new HashSet<int>();
        foreach (var k in Net.Pickups())
        {
            seen.Add(k.Id);
            if (!_pickupNodes.TryGetValue(k.Id, out var node))
            {
                node = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 5, Rings = 3 }, MaterialOverride = _pickupMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(node);
                _pickupNodes[k.Id] = node;
            }
            // Bigger for a bigger heap, smaller as it fades in the last ten seconds, turning slowly.
            float size = 0.35f + Math.Min(0.6f, MathF.Sqrt(k.Count) * 0.05f);
            float fade = Math.Clamp(k.Life / (10f * Units.TicksPerSecond), 0f, 1f);
            node.Position = new Vector3(k.X, k.Y, k.Z) / CellUnits;
            node.Scale = Vector3.One * size * fade;
            node.Rotation = new Vector3((float)_animTime * 0.9f + k.Id, (float)_animTime * 1.3f, 0f);
        }
        foreach (int id in _pickupNodes.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _pickupNodes[id].QueueFree();
            _pickupNodes.Remove(id);
        }
    }

    /// <summary>
    /// The barrel or emitter in hand visibly points where the crosshair does: a short bright barrel from the nearest gun (or the first laser) along the aim, with a faint
    /// guide line, brighter and with a muzzle flash while the guns fire.
    /// </summary>
    private void UpdateGuide()
    {
        bool show = !Standing && !Overview && !PanelOpen() && _tool != ToolMode.Build && Net.HasPlayer(Player) && HardpointsOn && _focus is null;
        if (!show) { if (_guideBarrel is not null) _guideBarrel.Visible = _guideLine!.Visible = false; return; }
        EnsureFeelMaterials();
        if (_guideBarrel is null)
        {
            _guideBarrel = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _guideMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _guideLine = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _guideMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_guideBarrel);
            AddChild(_guideLine);
        }
        var origin = AimOrigin();
        var dir = AimDirection();
        var target = origin + dir * ConvergeCells(origin, dir);
        Vector3 from;
        bool guns = _weaponGroup == WeaponGroup.Guns;
        if (guns)
        {
            from = NearestGun(target) + Vector3.Up * 0.0f;
            if (!Net.Guns(Player).Any()) { _guideBarrel.Visible = _guideLine!.Visible = false; return; }
        }
        else
        {
            var lasers = Net.MiningLasers(Player);
            if (lasers.Count == 0) { _guideBarrel.Visible = _guideLine!.Visible = false; return; }
            var m = lasers.OrderBy(l => (new Vector3(l.X, l.Y, l.Z) / CellUnits - target).LengthSquared()).First();
            from = new Vector3(m.X, m.Y, m.Z) / CellUnits;
        }
        var d = (target - from);
        if (d.LengthSquared() < 1f) { _guideBarrel.Visible = _guideLine!.Visible = false; return; }
        d = d.Normalized();
        bool firing = guns ? Net.GetPlayerInfo(Player).Armed : Net.LaserFiring(Player);
        float pulse = 0.8f + 0.2f * MathF.Sin((float)_animTime * 30f);
        float width = firing ? 0.16f * pulse : 0.09f;
        float barrel = 1.6f;
        _guideBarrel.Visible = true;
        _guideBarrel.LookAtFromPosition(from + d * barrel * 0.6f, from + d * barrel * 2f);
        _guideBarrel.Scale = new Vector3(width, width, barrel);
        _guideLine!.Visible = !firing;
        float line = Math.Min(30f, (target - from).Length()) - barrel * 1.6f;
        if (line > 0.5f)
        {
            _guideLine.LookAtFromPosition(from + d * (barrel * 1.6f + line / 2f), from + d * 60f);
            _guideLine.Scale = new Vector3(0.025f, 0.025f, line);
        }
        else _guideLine.Visible = false;
        _guideMat!.AlbedoColor = guns ? new Color(1f, 0.45f, 0.4f, firing ? 0.95f : 0.5f) : new Color(0.4f, 1f, 0.6f, firing ? 0.95f : 0.5f);
    }

    private void UpdateWeaponLabel()
    {
        bool show = !Standing && !PanelOpen() && _tool != ToolMode.Build && Net.HasPlayer(Player) && !Overview && !_demo;
        if (_weaponLabel is null)
        {
            if (!show) return;
            _weaponLabel = MakeText(15, new Color(0.9f, 0.95f, 1f));
            _weaponLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            _weaponLabel.AddThemeConstantOverride("outline_size", 5);
            _weaponLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _weaponLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _uiRoot.AddChild(_weaponLabel);
        }
        _weaponLabel.Visible = show;
        if (!show) return;
        bool guns = _weaponGroup == WeaponGroup.Guns;
        _weaponLabel.Text = L("hud.weaponInHand", L(guns ? "hud.weaponGuns" : "hud.weaponLaser")) + "   " + L("hud.weaponHint");
        _weaponLabel.AddThemeColorOverride("font_color", guns ? new Color(1f, 0.6f, 0.55f) : new Color(0.6f, 1f, 0.75f));
        var size = GetViewport().GetVisibleRect().Size;
        _weaponLabel.Size = new Vector2(size.X, 24f);
        _weaponLabel.Position = new Vector2(0f, size.Y - 74f);
    }

    /// <summary>
    /// The mining readout of the weapon card: items per second in tenths (the laser as it is now, slowed past the hold limit) at the deposit being mined or aimed at, and how
    /// much is left in it (-1 when there is none). 0 when no deposit is chosen.
    /// </summary>
    private int MiningReadout(out int depositLeft, out bool slowed)
    {
        depositLeft = -1;
        slowed = Net.HandMiningSlowed(Player);
        int dep = Net.MiningDeposit(Player);
        if (dep < 0 && _hover.Kind == HoverKind.Deposit) dep = _hover.Id;
        if (dep < 0 || !Net.IsDeposit(dep)) return 0;
        depositLeft = Net.DepositRemaining(dep);
        int ticks = Net.MineTicksNow(Player, dep);
        return ticks <= 0 ? 0 : Units.TicksPerSecond * 10 / ticks;
    }

    private void EnsureFeelMaterials()
    {
        _tracerMat ??= new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.2f), EmissionEnergyMultiplier = 3f };
        _pickupMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.75f, 0.35f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.25f), EmissionEnergyMultiplier = 2f };
        _guideMat ??= new StandardMaterial3D { AlbedoColor = new Color(1f, 0.45f, 0.4f, 0.5f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
    }
}
