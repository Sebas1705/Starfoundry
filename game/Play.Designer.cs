using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Hardpoints and the shipyard in the game (ADR 0032): the ship designer panel (F2, docked at a shipyard), the weapon group (V picks the mining
/// laser or the guns; the fire button uses the group in hand), the laser beams that leave from the laser modules, and the headless shots
/// <c>--designer-shot=path</c> and <c>--laser-shot=path</c>.
/// </summary>
public partial class Play
{
    private ShipDesigner? _designer;
    private WeaponGroup _weaponGroup = WeaponGroup.Laser;
    private bool _designerAwait, _designerRejected;
    private int _designerFrame;
    private readonly List<(MeshInstance3D Core, MeshInstance3D Halo)> _extraBeams = new();

    private bool DesignerOpen => _designer is { Visible: true };

    /// <summary>True when this ship fits its weapons as modules (structure rules, not a legacy world).</summary>
    private bool HardpointsOn => _structuresOn && Net.HasPlayer(Player) && Net.HardpointsRequired(Player);

    private void BuildDesigner()
    {
        _designer = new ShipDesigner(_content, () => _world, Player, Stacks);
        _designer.ApplyRequested += cmd => { Send(cmd); _designerAwait = true; };
        _designer.CloseRequested += CloseDesigner;
        _designer.Say += text => Toast(text, 4.0);
        _uiRoot.AddChild(_designer);
    }

    private bool ShipyardDocked => _structuresOn && Net.HasPlayer(Player) && Net.TryGetShipyard(Player, out _);

    /// <summary>F2: open the ship designer when the ship is docked at one of your stations that has a shipyard; otherwise say what is missing.</summary>
    private void ToggleDesigner()
    {
        if (_designer is null || !_structuresOn) return;
        ToggleComputerTab(ComputerTab.Designer);
    }

    private void CloseDesigner()
    {
        if (_computerTab == ComputerTab.Designer) CloseComputer();
        else _designer?.Close();
    }

    /// <summary>Per frame: after an apply, waits for the simulation and reloads the layout; closes the panel if the shipyard is gone.</summary>
    private void UpdateDesigner()
    {
        if (!DesignerOpen) return;
        if (!ShipyardDocked && !_uiAudit) { CloseDesigner(); return; }
        if (_designerAwait && _world.PendingCommands == 0)
        {
            _designerAwait = false;
            if (_designerRejected) _designerRejected = false; // the refusal was already said (CollectResults) and the staged layout stays so it can be fixed
            else
            {
                _designer!.Reload();
                Toast(L("designer.applied", Net.GetStructure(Net.ShipStructure(Player)).Modules.Count), 4.0);
                AudioManager.PlayUi(Sfx.UiClick);
            }
        }
    }

    // The weapon group in hand, free fire and the guide are in Play.Weapons.cs.

    // ---- the beams ---------------------------------------------------------------------------------------------

    /// <summary>The tip of the emitter of every laser module that reaches the deposit, in world cells; empty without hardpoints.</summary>
    private List<Vector3> LaserMuzzles(int deposit)
    {
        var list = new List<Vector3>();
        if (!HardpointsOn || ShipStruct < 0) return list;
        var ship = Net.GetStructure(ShipStruct);
        long cu = Units.CellUnits;
        var tip = ModuleModels.LaserMuzzle;
        foreach (var m in Net.LasersReaching(Player, deposit))
        {
            var (x, y, z) = Net.StructureToWorld(ship, (long)((m.Mx * StationGrid.ModuleX + tip.X) * cu), (long)((m.My * StationGrid.ModuleY + tip.Y) * cu), (long)((m.Mz * StationGrid.ModuleZ + tip.Z) * cu));
            list.Add(new Vector3((float)x / cu, (float)y / cu, (float)z / cu));
        }
        return list;
    }

    private (MeshInstance3D Core, MeshInstance3D Halo) BeamPair(int i)
    {
        while (_extraBeams.Count <= i)
        {
            var core = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _laserCoreMat, Visible = false };
            var halo = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _laserHaloMat, Visible = false };
            AddChild(core);
            AddChild(halo);
            _extraBeams.Add((core, halo));
        }
        return _extraBeams[i];
    }

    // ---- --designer-shot=path and --laser-shot=path -----------------------------------------------------------------

    private string? _designerShotPath, _laserShotPath;
    private int _hpShotPhase, _hpShotWait;
    private long _hpShotTick;

    /// <summary>
    /// <c>--designer-shot=path</c>: a station with a shipyard stands next to the starter ship; the ship docks, the designer opens, a second laser and a
    /// Mk2 gun are added and the nose laser is upgraded (<c>path</c>), the design is applied (<c>path.applied.png</c>) and the ship is seen from outside
    /// (<c>path.ship.png</c>). <c>--laser-shot=path</c>: a ship with two laser modules mining a rock (<c>path</c> from outside, <c>path.cockpit.png</c> from the seat).
    /// </summary>
    private void DesignerShotFrame()
    {
        if (!_structuresOn || ShipStruct < 0) return;
        void Save(string p) { GetViewport().GetTexture().GetImage().SavePng(p); GD.Print("SHOT saved " + p); }
        int ship = ShipStruct;
        _hpShotWait++;
        bool Wait(int frames) { if (_hpShotWait < frames) return true; _hpShotWait = 0; _hpShotPhase++; return false; }
        long cu = Units.CellUnits;
        if (_designerShotPath is not null)
        {
            switch (_hpShotPhase)
            {
                case 0:
                {
                    var o = Net.GetStructure(ship);
                    int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX - (8 + 10) * cu, o.OriginY, o.OriginZ);
                    Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                    Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Shipyard);
                    Net.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Reactor);
                    foreach (var (item, n) in new[] { ("core:iron_plate", 80), ("core:iron_ingot", 40), ("core:carbon", 30), ("core:circuit", 20), ("core:titanium_alloy", 20), ("core:focus_lens", 4), ("core:coolant", 10), ("core:composite_plate", 4) })
                        Net.GiveStock(st, _content.ItemIndex(item), n);
                    if (Wait(5)) break;
                    Send(new DockAt(st, 0, 0, 0, Dir.PosX));
                    break;
                }
                case 1:
                    if (IsDockedNow) { _hpShotWait = 0; _hpShotPhase = 2; }
                    else if (_hpShotWait > 300) { GD.Print("DESIGNERSHOT did not dock"); GetTree().Quit(); }
                    break;
                case 2:
                    if (Wait(60)) break;
                    _designer!.Open();
                    _designer.PickKind(ModuleKind.MiningLaser);
                    _designer.ClickCell((1, 1, 0));
                    _designer.PickKind(ModuleKind.GunMk2);
                    _designer.ClickCell((-1, 1, 0));
                    _designer.ClickCell((0, 0, -1)); // select the nose laser
                    _designer.UpgradeSelected();
                    break;
                case 3:
                    if (Wait(20)) break;
                    Save(_designerShotPath);
                    GD.Print($"DESIGNERSHOT dirty={_designer!.Editor.Dirty} check={_designer.Editor.Check(_world, Player).Reject}");
                    _designer.PickKind(ModuleKind.Hull);
                    _designer.ClickCell((-1, 1, 0)); // an occupied cell selects it
                    _designer.RemoveSelected();
                    _designer.PickKind(ModuleKind.MiningLaser);
                    _designer.ClickCell((-1, 1, 0));
                    break;
                case 4:
                    if (Wait(15)) break;
                    Send(_designer!.Editor.ToCommand());
                    _designerAwait = true;
                    _hpShotTick = Net.Tick;
                    break;
                case 5:
                    if (_designerAwait || Net.Tick < _hpShotTick + 3) break;
                    GD.Print($"DESIGNERSHOT applied modules={Net.GetStructure(ship).Modules.Count} lasers={Net.MiningLasers(Player).Count}");
                    _hpShotWait = 0; _hpShotPhase = 6;
                    break;
                case 6:
                    if (Wait(25)) break;
                    Save(_designerShotPath + ".applied.png");
                    CloseDesigner();
                    _shotOutside = true;
                    {
                        var o = Net.GetStructure(ship);
                        var at = new Vector3(o.OriginX, o.OriginY, o.OriginZ) / cu + new Vector3(4f, 3f, 8f);
                        _shotCam = (at + new Vector3(26f, 14f, -26f), at);
                    }
                    break;
                case 7:
                    if (Wait(30)) break;
                    Save(_designerShotPath + ".ship.png");
                    GetTree().Quit();
                    break;
            }
            return;
        }
        // --laser-shot
        switch (_hpShotPhase)
        {
            case 0:
            {
                Net.AddStructureModuleUnchecked(ship, 1, 0, -1, ModuleKind.MiningLaserMk2);
                int iron = _content.ItemIndex("core:iron_ore");
                int dep = Enumerable.Range(0, Net.DepositCount).Where(d => Net.DepositItem(d) == iron && Net.IsDepositRevealed(d)).DefaultIfEmpty(-1).First();
                if (dep < 0) { GD.Print("LASERSHOT no deposit"); GetTree().Quit(); return; }
                var (x, y, z) = Net.DepositPosition(dep);
                // park so that the deposit is straight ahead of the nose, 8 cells from the lasers
                Net.SetStructureOrigin(ship, (long)(x - 8) * cu, (long)(y - 5) * cu, (long)(z + 12) * cu);
                _yaw = 0f; _pitch = 0f;
                Send(new StartMining(dep));
                _hpShotPhase = 1; _hpShotWait = 0;
                break;
            }
            case 1:
                if (Wait(90)) break;
                GD.Print($"LASERSHOT firing={Net.LaserFiring(Player)} lasers={Net.MiningLasers(Player).Count} percent={Net.LaserPercentFor(Player, Net.MiningDeposit(Player))} draw={Net.PowerStatus(ship).Demand}");
                Save(_laserShotPath! + ".cockpit.png");
                _shotOutside = true;
                {
                    var o = Net.GetStructure(ship);
                    var at = new Vector3(o.OriginX, o.OriginY, o.OriginZ) / cu + new Vector3(8f, 3f, -12f);
                    _shotCam = (at + new Vector3(16f, 7f, 10f), at);
                }
                break;
            case 2:
                if (Wait(20)) break;
                Save(_laserShotPath!);
                GetTree().Quit();
                break;
        }
    }
}
