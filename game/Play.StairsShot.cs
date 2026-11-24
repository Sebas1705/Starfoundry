using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

public partial class Play
{
    // ---- --walk-shot=path --walk-stairs ------------------------------------------------------------------------

    private int _stairsFrame;
    private (int Mx, int My, int Mz) _stairsHull;
    private readonly System.Collections.Generic.List<(int Mx, int Mz)> _skinRow = new();

    /// <summary>
    /// <c>--walk-shot=path --walk-stairs</c>: stacks a hull and a cargo module on the starter's, stands the body under the ladder, saves the view up the
    /// shaft (<c>path.stairs.below.png</c>), climbs with the real E path, saves the deck above (<c>path.stairs.top.png</c>) and the view down the hole
    /// (<c>path.stairs.down.png</c>), climbs back, and then walks a row of hull modules built for the wall skins (<c>path.skin.&lt;n&gt;.&lt;skin&gt;.&lt;side&gt;.png</c>).
    /// Prints <c>STAIRS</c> lines with the positions. Returns true while it owns the run.
    /// </summary>
    private bool StairsShotFrame()
    {
        if (!OS.GetCmdlineUserArgs().Contains("--walk-stairs")) return false;
        if (_walkShotFrame < 10) return true;
        _stairsFrame++;
        void Save(string name) { GetViewport().GetTexture().GetImage().SavePng($"{_walkShotPath}.{name}.png"); GD.Print("SHOT saved " + name); }
        var ship = Net.GetStructure(ShipStruct);
        switch (_stairsFrame)
        {
            case 1:
                _stairsHull = ship.Modules.OrderBy(m => m.Key).First(m => m.Value == ModuleKind.Hull).Key;
                var h = _stairsHull;
                Net.AddStructureModuleUnchecked(ShipStruct, h.Mx, h.My + 1, h.Mz, ModuleKind.Hull);
                var cargo = ship.Modules.OrderBy(m => m.Key).First(m => m.Value == ModuleKind.Cargo).Key;
                Net.AddStructureModuleUnchecked(ShipStruct, cargo.Mx, cargo.My + 1, cargo.Mz, ModuleKind.Cargo);
                for (int i = 0; i < 6; i++) { Net.AddStructureModuleUnchecked(ShipStruct, 3, 0, i, ModuleKind.Hull); _skinRow.Add((3, i)); }
                _structureVersion = -1;
                break;
            case 5:
                _seated = true; ToggleSeat();
                RefreshBodyVolumes();
                _body = new Vector3(cx0(), _stairsHull.My * 4f, cz0() + 1.4f);
                _yaw = 0f; _pitch = 0.7f; // facing the front wall, looking up the shaft
                GD.Print($"STAIRS volumes={_bodyVolumes.Length} links={Stairwell.Links(_bodyVolumes).Count} prompt='{SeatPromptText()}'");
                break;
            case 30: Save("stairs.below"); break;
            case 31:
                _body = new Vector3(cx0(), _stairsHull.My * 4f, cz0());
                _pitch = 0.4f;
                GD.Print($"STAIRS before E body={_body} prompt='{SeatPromptText()}'");
                ToggleSeat();
                GD.Print($"STAIRS after E (up) body={_body}");
                _yaw = MathF.PI; _pitch = -0.1f; // turn round: the deck behind the opening and the room beyond
                break;
            case 60: Save("stairs.top"); break;
            case 61:
                _body = new Vector3(cx0(), (_stairsHull.My + 1) * 4f, cz0() + 0.3f);
                _yaw = 0f; _pitch = -1.0f; // looking down the hole
                GD.Print($"STAIRS before E body={_body} prompt='{SeatPromptText()}'");
                break;
            case 90:
                Save("stairs.down");
                ToggleSeat();
                GD.Print($"STAIRS after E (down) body={_body}");
                break;
            default:
                if (_stairsFrame >= 120 && (_stairsFrame - 120) % 24 == 0 && (_stairsFrame - 120) / 24 < _skinRow.Count * 2)
                {
                    int n = (_stairsFrame - 120) / 24 / 2;
                    bool left = ((_stairsFrame - 120) / 24) % 2 == 0;
                    var (mx, mz) = _skinRow[n];
                    int skin = HullProfile.SkinOf(ModuleKind.Hull, mx, 0, mz);
                    _body = new Vector3(mx * 8f + 4f, 0f, mz * 8f + 4f);
                    _yaw = left ? MathF.PI / 2 : -MathF.PI / 2; _pitch = -0.1f;
                    RefreshBodyVolumes();
                    GD.Print($"SKIN module=({mx},{mz}) skin={skin} {(left ? "left" : "right")}");
                    _pendingSkinShot = $"skin.{n}.{skin}.{(left ? "l" : "r")}";
                }
                else if (_pendingSkinShot is { } name && (_stairsFrame - 120) % 24 == 12) { Save(name); _pendingSkinShot = null; }
                if (_stairsFrame > 120 + 24 * _skinRow.Count * 2 + 10) GetTree().Quit();
                break;
        }
        return true;

        float cx0() => _stairsHull.Mx * 8f + (Stairwell.X0 + Stairwell.X1) / 2;
        float cz0() => _stairsHull.Mz * 8f + (Stairwell.Z0 + Stairwell.Z1) / 2;
    }

    private string? _pendingSkinShot;
}
