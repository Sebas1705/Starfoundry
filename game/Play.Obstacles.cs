using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// What the player sees and hears of the obstacles of the wide galaxy (ADR 0028): the models of the system the ship is in, boost on
/// Shift, a contextual hazard line (only when a rock, mine or cloud is close), a camera shake and the toasts and sounds of impacts,
/// blasts, armed mines and salvage. <see cref="WatchObstacles"/> runs after every simulation tick, because the events of a tick are
/// gone after the next one.
/// </summary>
public partial class Play
{
    /// <summary>Hazards closer than this many cells (from the nearest module of the ship) show a warning.</summary>
    private const float HazardWarnCells = 30f;

    private ObstacleLayer? _obstacleLayer;
    private Label? _hazardLabel, _boostLabel;
    private float _shake;
    private bool _boostWanted;
    private string _hazardText = "";

    private void EnsureObstacleUi()
    {
        if (_obstacleLayer is not null) return;
        _obstacleLayer = new ObstacleLayer();
        AddChild(_obstacleLayer);
        _hazardLabel = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _hazardLabel.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.25f));
        _hazardLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _hazardLabel.AddThemeConstantOverride("outline_size", 6);
        _hazardLabel.AddThemeFontSizeOverride("font_size", 22);
        _hazardLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.KeepSize);
        _hazardLabel.AnchorLeft = 0.2f; _hazardLabel.AnchorRight = 0.8f; _hazardLabel.OffsetLeft = 0; _hazardLabel.OffsetRight = 0; _hazardLabel.OffsetTop = 150;
        _uiRoot.AddChild(_hazardLabel);
        _boostLabel = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _boostLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 1f));
        _boostLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _boostLabel.AddThemeConstantOverride("outline_size", 6);
        _boostLabel.AddThemeFontSizeOverride("font_size", 20);
        _boostLabel.AnchorLeft = 0.4f; _boostLabel.AnchorRight = 0.6f; _boostLabel.OffsetTop = 110;
        _uiRoot.AddChild(_boostLabel);
    }

    /// <summary>Per frame: the models, boost input, the hazard line and the camera shake.</summary>
    private void UpdateObstacles(float dt)
    {
        if (_demo || !Net.HasPlayer(Player)) return;
        EnsureObstacleUi();
        int system = Net.CurrentSystem(Player);
        _obstacleLayer!.Sync(Net, _content, system, (float)_animTime, dt);

        UpdateBoost();

        // Hazard line: nothing unless something is close.
        string text = "";
        if (_structuresOn && ShipStruct >= 0 && Net.ObstacleCount > 0 && system >= 0 && !_paused) text = HazardText(system);
        if (text != _hazardText) { _hazardText = text; _hazardLabel!.Text = text; }
        _hazardLabel!.Visible = text.Length > 0;
        _boostLabel!.Visible = Net.IsBoosting(Player);
        if (_boostLabel.Visible) _boostLabel.Text = L("hud.boost");
        _shake = MathF.Max(0f, _shake - dt * 2.5f);
    }

    /// <summary>Shift while flying is the boost; releasing it, standing up or opening a menu ends it. The key is read each frame and only changes are sent.</summary>
    private void UpdateBoost()
    {
        if (_content.World is null || !Net.CanFly(Player) || Net.JumpRemaining(Player) > 0 || _obsShotPath is not null) return;
        bool want = !Standing && !_paused && HeldKeys.Down("boost", 0) && !_mapOpen && _uiFocusFree();
        if (want == _boostWanted && want == Net.IsBoosting(Player)) return;
        if (want == _boostWanted && _world.PendingCommands > 0) return;
        _boostWanted = want;
        if (want != Net.IsBoosting(Player)) { Send(new SetBoost(want)); if (want) _audio?.Play(Sfx.BoostWhoosh); }
    }

    private bool _uiFocusFree() => GetViewport().GuiGetFocusOwner() is not LineEdit;

    /// <summary>The warning line for the most pressing hazard near the ship, or an empty string.</summary>
    private string HazardText(int system)
    {
        int ship = ShipStruct;
        var shipOrigin = Net.GetStructure(ship);
        var world = _content.World!;
        // Nearest distance (cells) from the ship's module boxes, approximated by distance to the nearest module centre minus its half size.
        var centres = shipOrigin.Modules.Keys.Select(k => Net.ModuleWorldCentre(ship, k.Mx, k.My, k.Mz)).ToList();
        if (centres.Count == 0) return "";
        string best = "";
        float bestScore = float.MaxValue;

        var cloud = Net.CloudEffectOf(ship);
        if (cloud.Cloud >= 0)
        {
            var kind = world.Kinds[Net.Galaxy!.Obstacles[cloud.Cloud].Kind];
            if (cloud.DrainPercent > 0) return L("hazard.ion", 100 - cloud.DrainPercent);
            if (cloud.SlowPercent > 0) return L("hazard.gravity", 100 - cloud.SlowPercent);
            _ = kind;
        }

        foreach (int i in Net.Galaxy!.ObstaclesOf(system))
        {
            var o = Net.GetObstacle(i);
            if (o.Gone || o.Behaviour is ObstacleBehaviour.Cloud or ObstacleBehaviour.Wreck) continue;
            var def = world.Kinds[o.Kind];
            float d = float.MaxValue;
            foreach (var c in centres)
            {
                float dx = (c.X - o.X) / (float)Units.CellUnits, dy = (c.Y - o.Y) / (float)Units.CellUnits, dz = (c.Z - o.Z) / (float)Units.CellUnits;
                d = MathF.Min(d, MathF.Sqrt(dx * dx + dy * dy + dz * dz) - 4f); // a module is about 8 cells across
            }
            d -= o.Radius / (float)Units.CellUnits;
            if (o.Behaviour == ObstacleBehaviour.Mine)
            {
                if (o.FuseTicks > 0) return L("hazard.armed", Cap(Loc.Name("obstacle", def.Id)), (o.FuseTicks / (float)Units.TicksPerSecond).ToString("F1", Loc.Culture));
                d -= def.TriggerCells; // inside the trigger range counts as touching
            }
            if (d < HazardWarnCells && d < bestScore) { bestScore = d; best = L("hazard.near", Cap(Loc.Name("obstacle", def.Id)), MathF.Max(0f, MathF.Round(d)).ToString("F0", Loc.Culture)); }
        }
        // The rocks of veins and of the field are solid too (ADR 0033, 0040) and are what a ship meets first near home: warn about them like any rock.
        // Only revealed rocks are solid in the simulation, and only those within reach of the ship are looked at.
        var at = Net.GetPlayerInfo(Player);
        float near = HazardWarnCells + 40f;
        for (int i = 0; i < Net.DepositCount; i++)
        {
            long rockR = Net.DepositRockRadius(i);
            if (rockR <= 0) continue;
            var dep = Net.GetDepositInfo(i);
            if (!dep.Revealed || dep.Remaining <= 0) continue;
            if (MathF.Abs(dep.X - at.X / (float)Units.CellUnits) > near || MathF.Abs(dep.Y - at.Y / (float)Units.CellUnits) > near || MathF.Abs(dep.Z - at.Z / (float)Units.CellUnits) > near) continue;
            float d = float.MaxValue;
            foreach (var c in centres)
            {
                float dx = c.X / (float)Units.CellUnits - dep.X, dy = c.Y / (float)Units.CellUnits - dep.Y, dz = c.Z / (float)Units.CellUnits - dep.Z;
                d = MathF.Min(d, MathF.Sqrt(dx * dx + dy * dy + dz * dz) - 4f);
            }
            d -= rockR / (float)Units.CellUnits;
            if (d < HazardWarnCells && d < bestScore) { bestScore = d; best = L("hazard.near", L("obstacle.depositRock", ItemName(dep.Item)), MathF.Max(0f, MathF.Round(d)).ToString("F0", Loc.Culture)); }
        }
        // Rocks only matter when you are heading for them fast enough to hurt: below the safe speed a bump is harmless.
        if (best.Length > 0 && bestScore > 14f)
        {
            var p = Net.GetPlayerInfo(Player);
            float speed = MathF.Sqrt((float)p.ThrustX * p.ThrustX + (float)p.ThrustY * p.ThrustY + (float)p.ThrustZ * p.ThrustZ) / PlayerRules.ThrustScale * Net.ShipSpeedUnitsPerTick(Player) * (Net.IsBoosting(Player) ? world.BoostPercent / 100f : 1f);
            if (speed <= world.SafeSpeedUnits) return "";
        }
        return best;
    }

    /// <summary>Called after every tick: the obstacle events of that tick become toasts, sounds, a flash and a shake.</summary>
    private void WatchObstacles()
    {
        if (Net.ObstacleCount == 0 || _demo) return;
        int myShip = _structuresOn ? Net.ShipStructure(Player) : -1;
        var world = _content.World;
        foreach (var e in Net.ObstacleEvents)
        {
            // The rock of a deposit (ADR 0033) is hit like an obstacle; it has no obstacle record.
            ObstacleInfo info = default;
            string name;
            Vector3 at;
            if (e.Deposit >= 0)
            {
                var dp = Net.DepositPosition(e.Deposit);
                at = new Vector3(dp.X, dp.Y, dp.Z);
                name = L("obstacle.depositRock", ItemName(Net.DepositItem(e.Deposit)));
            }
            else
            {
                info = Net.GetObstacle(e.Obstacle);
                name = world is null ? "" : Loc.Name("obstacle", world.Kinds[info.Kind].Id);
                at = new Vector3(info.X, info.Y, info.Z) / CellUnits;
            }
            switch (e.Kind)
            {
                case ObstacleEventKind.Impact:
                    if (e.Structure != myShip) break;
                    _obsImpact = true;
                    _vigAlpha = MathF.Max(_vigAlpha, Math.Clamp(0.2f + e.Damage / 200f, 0f, 0.8f));
                    _shake = MathF.Max(_shake, Math.Clamp(0.25f + e.Damage / 100f, 0f, 1.2f));
                    _audio?.Play(Sfx.RockThud, at);
                    ToastThreat(L("toast.collision", e.Damage, ImpactModuleName(e.Structure), (e.Speed * (float)Units.TicksPerSecond / Units.CellUnits).ToString("F1", Loc.Culture)), 4.0);
                    break;
                case ObstacleEventKind.Bump:
                    if (e.Structure != myShip) break;
                    _shake = MathF.Max(_shake, 0.12f);
                    _audio?.Play(Sfx.ModuleHit, at);
                    break;
                case ObstacleEventKind.Armed:
                    _audio?.Play(Sfx.MineBeep, at);
                    break;
                case ObstacleEventKind.Explosion:
                    _audio?.Play(Sfx.Explosion, at);
                    Explode(at, MathF.Max(6f, (world?.Kinds[info.Kind].BlastCells ?? 10) * 0.5f), 0f);
                    _shake = MathF.Max(_shake, Math.Clamp(1.2f - (ShipPosition(0f) - at).Length() / 60f, 0f, 1f));
                    ToastThreat(L("toast.explosion", name), 4.0);
                    break;
                case ObstacleEventKind.Salvaged:
                    _audio?.Play(Sfx.SalvageChime);
                    ToastProduction(L("toast.salvaged", ItemName(e.Item), e.Count), 5.0);
                    break;
            }
        }
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>The kind of the module that took the last hit of the ship (the module hit events of the same tick name it).</summary>
    private string ImpactModuleName(int structure)
    {
        var hit = Net.ModuleHits.LastOrDefault(h => h.Structure == structure);
        return hit.Structure == structure && structure >= 0 ? KindName(hit.Kind) : "";
    }

    /// <summary>A small random offset for the camera while the ship shakes after an impact (zero when calm).</summary>
    private Vector3 ShakeOffset()
    {
        if (_shake <= 0.001f) return RumbleOffset();
        float t = (float)_animTime * 53f;
        return new Vector3(MathF.Sin(t * 1.3f), MathF.Sin(t * 1.7f + 2f), MathF.Sin(t * 1.1f + 4f)) * (_shake * 0.35f) + RumbleOffset();
    }
}
