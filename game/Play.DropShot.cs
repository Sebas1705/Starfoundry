using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--drop-shot=path</c> (ADR 0038): three stacks are dropped ahead of the ship, <c>path</c> is the cockpit view with the crosshair on one (its tooltip),
/// <c>path.outside.png</c> the crates from outside; then the seated pick-up takes one back and the log line says where it went.
/// </summary>
public partial class Play
{
    private void DropShotFrame(Action<string> save)
    {
        long cu = Units.CellUnits;
        switch (_feelPhase)
        {
            case 0:
            {
                Net.GiveInventory(Player, _content.ItemIndex("core:iron_ore"), 140);
                Net.GiveInventory(Player, _content.ItemIndex("core:iron_ingot"), 30);
                Net.GiveInventory(Player, _content.ItemIndex("core:carbon"), 12);
                _yaw = 0f; _pitch = 0f; _speed = 2;
                DropToWorldAt(ContainerRef.Person, 0, 100, -1.5f); // the sim merges stacks of one item within a cell, so each drop is a different item
                DropToWorldAt(ContainerRef.Person, 2, 25, 0f);
                DropToWorldAt(ContainerRef.Person, 3, 12, 1.5f);
                _feelPhase = 1; _feelWait = 0;
                break;
            }
            case 1:
                if (_feelWait < 12) break;
                GD.Print($"DROPSHOT stacks={string.Join(",", Net.Pickups().Select(k => $"{k.Item}x{k.Count}@{k.X},{k.Y},{k.Z}"))} hover={_hover.Kind} text={_hover.Text.Replace("\n", " / ")}");
                save(_dropShotPath!);
                _shotOutside = true;
                {
                    var k = Net.Pickups().First();
                    var at = new Vector3(k.X, k.Y, k.Z) / cu;
                    _shotCam = (at + new Vector3(5f, -1f, -7f), at);
                }
                _feelPhase = 2; _feelWait = 0;
                break;
            case 2:
                if (_feelWait < 10) break;
                save(_dropShotPath + ".outside.png");
                {
                    var k = Net.Pickups().First();
                    var eye = AimOrigin() * cu;
                    Send(new PickUp(k.Id, true, (int)eye.X, (int)eye.Y, (int)eye.Z));
                }
                _feelPhase = 3; _feelWait = 0;
                break;
            case 3:
                if (_feelWait < 6) break;
                GD.Print($"DROPSHOT after pickup stacks={Net.Pickups().Count} bag0={Net.InventoryStack(Player, 0)?.Count} last={_world.LastResults.LastOrDefault()?.Reject}");
                GetTree().Quit();
                break;
        }
    }
}
