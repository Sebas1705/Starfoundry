using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The contextual key panel at the bottom right: gathers what the player is doing into a <see cref="KeyState"/> every frame, asks the
/// engine-free <see cref="KeyHints"/> for the rows and lays the panel out. Everything else in the HUD keeps clear of <see cref="_keyRect"/>.
/// </summary>
public partial class Play
{
    private KeyPanel _keyPanel = null!;
    private Rect2 _keyRect;
    private int _keyTipFrame;
    private int _damagedCount;

    private void BuildKeyPanel()
    {
        _keyPanel = new KeyPanel();
        _keyPanel.KeyPressed += PressKey;
        _uiRoot.AddChild(_keyPanel);
    }

    /// <summary>A click on a row of the key panel presses the key it stands for.</summary>
    private void PressKey(string name)
    {
        if (!Enum.TryParse<Key>(name, out var key)) return;
        OnKey(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
    }

    private static HintTool HintToolOf(BuildItem item) => item.IsStation ? HintTool.Station : item.IsModule ? HintTool.Module : item.IsBelt ? HintTool.Belt : HintTool.Machine;

    /// <summary>The state the key panel is drawn from, read from the game each frame.</summary>
    private KeyState KeyStateNow()
    {
        bool hasPlayer = Net.HasPlayer(Player);
        var player = hasPlayer ? Net.GetPlayerInfo(Player) : default;
        bool building = _tool == ToolMode.Build;
        BuildItem? item = building ? _palette[_selected] : null;
        bool galaxy = Net.Galaxy is not null;

        var aim = HintAim.None;
        string aimName = "";
        bool aimRecipes = false, aimDamaged = false;
        int aimHp = 0, aimMax = 0;
        if (!building && AimActive)
        {
            if (_hover.Kind == HoverKind.Machine && Net.IsMachineAlive(_hover.Id))
            {
                var m = Net.GetMachineInfo(_hover.Id);
                aim = HintAim.Machine;
                aimName = MachineName(m.Type);
                aimRecipes = RecipesFor(m.Type).Count > 1;
                aimDamaged = m.Hp < Net.MaxHp(m.Type);
            }
            else if (_hover.Kind == HoverKind.Segment) aim = HintAim.Segment;
            else if (_hover.Kind == HoverKind.Deposit) aim = HintAim.Deposit;
            else if (Net.ThreatActive && AimedModule() is { } module)
            {
                aimMax = Net.ModuleMaxHp(module.Kind);
                aimHp = Net.ModuleHp(module.S, module.Key.Mx, module.Key.My, module.Key.Mz);
                if (aimMax > 0 && aimHp < aimMax) { aim = HintAim.Module; aimName = KindName(module.Kind); }
            }
        }

        bool threatNow = false;
        if (Net.ThreatActive && hasPlayer)
        {
            var th = Net.GetSystemThreat(Math.Max(0, Net.CurrentSystem(Player)));
            threatNow = th.WarningRemaining >= 0 || th.Enemies > 0;
        }

        var jumpCost = Net.JumpCost();
        var dock = DockKeyInfo();
        return new KeyState
        {
            BoostAvailable = _content.World is not null && Net.CanFly(Player),
            Boosting = Net.IsBoosting(Player),
            Dock = dock.Hint,
            DockReady = dock.Ready,
            DockAssisting = _dockAssist,
            DockDistanceCells = dock.DistanceCells,
            DockSpeedPercent = dock.SpeedPercent,
            StockUsed = dock.Used,
            StockCapacity = dock.Capacity,
            PauseOpen = PauseOpen,
            HelpOpen = _helpOpen,
            BuildOpen = BuildOpen,
            ResearchOpen = ResearchOpen,
            MapOpen = _mapOpen,
            InfoOpen = _detailsOpen,
            CanWalk = CanWalk,
            Standing = Standing,
            NearSeat = Standing && BodyMotion.InSeatReach(Sys(_body), Sys(SeatPosition)),
            StructuresOn = _structuresOn,
            ModuleToolAvailable = _palette.Any(b => b.IsModule),
            Tool = building ? HintToolOf(item!) : HintTool.None,
            ToolName = building ? ItemName(item!) : "",
            ToolHasRecipes = building && !item!.IsBelt && !item.IsModule && !item.IsStation
                && _content.Recipes.Count(r => r.Machine == item.Machine && !Net.IsRecipeLocked(r.Index)) > 1,
            BeltAnchored = _beltAnchor is not null,
            Aim = aim,
            AimName = aimName,
            AimHasRecipes = aimRecipes,
            AimDamaged = aimDamaged,
            AimHp = aimHp,
            AimMaxHp = aimMax,
            HasGalaxy = galaxy,
            ThreatActive = Net.ThreatActive,
            ThreatNow = threatNow,
            Armed = hasPlayer && player.Armed,
            Mining = _laserOn,
            LaserFiring = Net.LaserFiring(Player),
            Refining = hasPlayer && player.ShipRecipe == _content.RecipeIndex("core:ship_smelt"),
            ScanReady = Net.ScanCooldown(Player) == 0,
            AnythingDamaged = _damagedCount > 0,
            DroneBayBuilt = _structuresOn && aim == HintAim.Deposit && DroneStructure() >= 0,
            DroneBayLocked = _structuresOn && DroneBayLocked(ModuleKind.DroneBay),
            JumpUnlocked = Net.HasAbility(Ability.Jump),
            JumpAffordable = Net.CanAffordJump(Player),
            SystemChosen = _selectedSystem >= 0,
            JumpCost = jumpCost.Length == 0 || _gameMode != GameMode.Campaign ? "" : Stacks(jumpCost),
        };
    }

    /// <summary>The tooltip of the quick-keys row: every direct key with what it builds and what that costs.</summary>
    private string? KeyTip(KeyRow row)
    {
        if (row.Id != "quick") return null;
        var sb = new StringBuilder(L("key.tip.quick"));
        for (int i = 0; i < _palette.Count && i < DirectKeys; i++)
        {
            var item = _palette[i];
            string cost = ItemLocked(item) ? L("ui.locked") : ItemCost(item).Length == 0 ? L("ui.free") : Stacks(ItemCost(item)) + (item.IsBelt ? L("ui.perCell") : "");
            sb.Append('\n').Append(KeyLabel(i)).Append("  ").Append(ItemName(item)).Append(": ").Append(cost);
        }
        return sb.ToString();
    }

    /// <summary>Per frame: refreshes the rows of the panel and puts it in the bottom right corner.</summary>
    private void UpdateKeyPanel(Vector2 size)
    {
        _keyPanel.Visible = !_demo && !EndOpen && Net.HasPlayer(Player);
        if (!_keyPanel.Visible) { _keyRect = new Rect2(size, Vector2.Zero); return; }
        if (++_keyTipFrame % 120 == 0) _keyPanel.Invalidate(); // costs and locks in the tooltip change slowly
        int sizeClass = size.Y < 800f ? 0 : size.Y < 1000f ? 1 : 2;
        _keyPanel.Set(KeyHints.For(KeyStateNow()), sizeClass, KeyTip);
        _keyPanel.ResetSize();
        var panel = _keyPanel.GetCombinedMinimumSize();
        _keyPanel.Size = panel;
        _keyPanel.Position = new Vector2(size.X - panel.X - 10f, size.Y - panel.Y - 8f);
        _keyRect = new Rect2(_keyPanel.Position, panel);
    }

    /// <summary>Moves a box out of the key panel's way: above it, or left of it when there is no room above.</summary>
    private Vector2 AvoidKeyPanel(Vector2 pos, Vector2 size, float gap = 8f)
    {
        if (!_keyPanel.Visible || !new Rect2(pos, size).Intersects(_keyRect)) return pos;
        var up = new Vector2(pos.X, _keyRect.Position.Y - size.Y - gap);
        return up.Y >= 8f ? up : new Vector2(_keyRect.Position.X - size.X - gap, pos.Y);
    }

    // ---- --keys-shot=path ----------------------------------------------------------------------------

    private string? _keysShotPath;
    private int _keysFrame;

    private void KeysShotSetup(string[] args)
    {
        _wantCapture = false;
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (!_structuresOn || ShipStruct < 0) return;
        Net.GiveItems(Player, _content.ItemIndex("core:iron_ingot"), 14);
        Net.GiveItems(Player, _content.ItemIndex("core:carbon"), 4);
        _world.Step(30);
    }

    /// <summary>Points the view at a machine of the ship (the first alive one of the given kind, else any), so the crosshair is on it.</summary>
    private bool LookAtMachine(string id)
    {
        int type = _content.MachineIndex(id);
        for (int pass = 0; pass < 2; pass++)
            foreach (int machine in _machineIds)
            {
                if (!Net.IsMachineAlive(machine)) continue;
                var m = Net.GetMachineInfo(machine);
                if (pass == 0 && m.Type != type) continue;
                var target = StructOrigin(m.Structure) + new Vector3(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f);
                var d = target - _camera.GlobalPosition;
                if (d.Length() < 2f) continue;
                _yaw = MathF.Atan2(-d.X, -d.Z);
                _pitch = MathF.Asin(d.Y / d.Length());
                return true;
            }
        return false;
    }

    /// <summary>
    /// <c>--keys-shot=path</c>: the structures campaign at 1280x720 (or <c>--keys-size=1920x1080</c>) with the key panel in six contexts:
    /// <c>path.flying.png</c> (seated, nothing aimed), <c>path.build.png</c> (a machine tool in hand), <c>path.aim.png</c> (the crosshair on a
    /// machine), <c>path.walking.png</c> (standing), <c>path.map.png</c> (the star map) and <c>path.menu.png</c> (the build menu docked at the
    /// right), then <c>path.research.png</c> (the research panel docked), <c>path.help.png</c> (the F1 overlay) and <c>path.info.png</c> (seated with the info panels open, H). <c>--ui-lang=en</c> renders them
    /// in English. Seed fixed, nothing is saved.
    /// </summary>
    private void KeysShotFrame()
    {
        if (_keysShotPath is not { } path) return;
        _keysFrame++;
        _wantCapture = false;
        var win = GetWindow();
        var args = OS.GetCmdlineUserArgs();
        var sizeArg = args.FirstOrDefault(a => a.StartsWith("--keys-size="))?["--keys-size=".Length..].Split('x');
        void Shot(string name) { GetViewport().GetTexture().GetImage().SavePng(path + "." + name + ".png"); GD.Print("KEYSSHOT saved " + path + "." + name + ".png " + GetViewport().GetVisibleRect().Size); }
        switch (_keysFrame)
        {
            case 3:
                if (sizeArg is { Length: 2 } && int.TryParse(sizeArg[0], out int w) && int.TryParse(sizeArg[1], out int h)) SetWindowSize(win, w, h);
                else SetWindowSize(win, 1280, 720);
                break;
            case 15: _yaw = 0f; _pitch = 0.25f; break; // away from the workshop: nothing to aim at
            case 45: Shot("flying"); break;
            case 46:
                LookAtMachine("core:smelter");
                SelectBuild(_palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation && b.Machine == _content.MachineIndex("core:smelter")));
                break;
            case 75: Shot("build"); break;
            case 76: _tool = ToolMode.Pointer; if (!LookAtMachine("core:smelter")) GD.Print("KEYSSHOT no machine to aim at"); break;
            case 105: GD.Print($"KEYSSHOT aim hover={_hover.Kind} context={KeyHints.ContextOf(KeyStateNow())}"); Shot("aim"); break;
            case 106: _seated = true; ToggleSeat(); _body = new Vector3(4f, 0f, 20f); _yaw = MathF.PI * 0.8f; _pitch = -0.1f; break; // in the cargo module: nothing to aim at
            case 136: Shot("walking"); break;
            case 137: _body = StandSpot; ToggleSeat(); _yaw = MathF.PI; _pitch = -0.35f; _mapOpen = true; break;
            case 167: Shot("map"); break;
            case 168: _mapOpen = false; ToggleBuildMenu(); break;
            case 200: Shot("menu"); break;
            case 201: ToggleBuildMenu(); ToggleResearchPanel(); break; // the build menu closes, the research panel opens in its place
            case 235: Shot("research"); break;
            case 236: ToggleResearchPanel(); _helpOpen = true; break;
            case 262: Shot("help"); break;
            case 263: _helpOpen = false; _tool = ToolMode.Pointer; _yaw = 0f; _pitch = 0.25f; _detailsOpen = true; break; // the info panels (H) while seated
            case 295: Shot("info"); break;
            case 296: _detailsOpen = false; break;
            case 297: GetTree().Quit(); break;
        }
    }
}
