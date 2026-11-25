using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--ui-shot=path</c>: the structures campaign with a few research states made up for the picture (Logistics and Carbon drilling done,
/// Advanced logistics being researched with plates delivered by a belt, the rest available or locked) and then four images:
/// <c>path.research.png</c> (the research panel), <c>path.research.locked.png</c> (a locked node selected), <c>path.build.png</c> (the build
/// menu docked at the right, on the Production tab, plus <c>path.build.logistics.png</c>, <c>path.build.structure.png</c> and <c>path.build.defence.png</c>) and
/// <c>path.bar.png</c> (the bottom strip and the key panel with a tool in hand); the panels dock at the right with the key panel under them. <c>--ui-lang=es</c> renders them in Spanish. Seed fixed, nothing is saved.
/// </summary>
public partial class Play
{
    private string? _uiShotPath;
    private int _uiFrame;

    private void UiShotSetup(string[] args)
    {
        _wantCapture = false;
        _uiKeys = args.Contains("--ui-keys");
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (!_structuresOn || ShipStruct < 0) return;

        foreach (var id in new[] { "core:logistics", "core:carbon_drilling" }) Net.GrantResearch(_content.ResearchIndex(id));
        Net.GiveItems(Player, _content.ItemIndex("core:iron_ingot"), 24);
        Net.GiveItems(Player, _content.ItemIndex("core:iron_plate"), 12);
        Net.GiveItems(Player, _content.ItemIndex("core:carbon"), 4);

        // A lab in the workshop fed plates by a belt from a source: the research shows real progress. Setup for the picture only.
        int ship = ShipStruct;
        int lab = Net.AddMachine(5, 1, 13, _content.MachineIndex("core:research_lab"), -1, Player, ship);
        int belt = Net.AddSegment(1, 1, 13, 1, 0, 0, 4, BeltTier.Mk1, ship);
        Net.SetSource(belt, new ushort[] { (ushort)_content.ItemIndex("core:iron_plate") });
        Net.ConnectToMachine(belt, lab);
        Net.SetResearch(_content.ResearchIndex("core:advanced_logistics"));
        _world.Step(1500);
        _structureVersion = -1; // the machines and belts added directly are drawn from the next frame
    }

    private void UiShotFrame()
    {
        if (_uiShotPath is not { } path) return;
        _uiFrame++;
        if (_uiKeys) { _wantCapture = false; UiKeysFrame(); return; }
        _wantCapture = false;
        switch (_uiFrame)
        {
            case 30: ToggleResearchPanel(); break;
            case 50: Save(path + ".research.png"); break;
            case 51: _researchPanel?.Select(_content.ResearchIndex("core:rapid_loader")); break;
            case 60: Save(path + ".research.locked.png"); break;
            case 61: CloseResearchPanel(); _buildMenu?.Open(BuildCategory.Production); break;
            case 80: Save(path + ".build.png"); break;
            case 81: _buildMenu?.SetTab(BuildCategory.Logistics); break;
            case 95: Save(path + ".build.logistics.png"); break;
            case 96: _buildMenu?.SetTab(BuildCategory.Structure); break;
            case 110: Save(path + ".build.structure.png"); break;
            case 111: _buildMenu?.SetTab(BuildCategory.Defence); break;
            case 125: Save(path + ".build.defence.png"); break;
            case 126:
                CloseBuildMenu();
                SelectBuild(_palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation && b.Machine == _content.MachineIndex("core:smelter")));
                break;
            case 150: Save(path + ".bar.png"); break;
            case 151: _tool = ToolMode.Pointer; ToggleResearchPanel(); break;
            case 160: _researchPanel?.Select(_content.ResearchIndex("core:repair_drones")); break;
            case 175: Save(path + ".research.field.png"); break; // a science-field node: its colour, ring and requirements
            case 176: GetTree().Quit(); break;
        }
    }

    // ---- --ui-keys: the keyboard path of the panels, through real input events ------------------------------------------------

    private bool _uiKeys;

    private static void SendKey(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    }

    /// <summary>
    /// <c>--ui-shot=path --ui-keys</c> presses real keys: T opens the research panel, Down moves the selection and Enter starts that
    /// research, Esc closes it; B opens the build menu, PgDn changes tab, Down and Enter choose a tool, and the tool is then in hand.
    /// Prints what happened as UIKEYS lines (so a failure is visible without looking at pictures).
    /// </summary>
    private void UiKeysFrame()
    {
        switch (_uiFrame)
        {
            case 20: SendKey(Key.Tab); break; // the ship computer opens on its last tab (research the first time)
            case 26: GD.Print($"UIKEYS research_open={ResearchOpen} selected={_researchPanel?.Selected} current={Net.CurrentResearch}"); SendKey(Key.Down); break;
            case 32: GD.Print($"UIKEYS after_down selected={_researchPanel?.Selected}"); SendKey(Key.Enter); break;
            case 38: GD.Print($"UIKEYS after_enter current={Net.CurrentResearch} ({(Net.CurrentResearch >= 0 ? _content.Research[Net.CurrentResearch].Id : "none")})"); SendKey(Key.Escape); break;
            case 44: GD.Print($"UIKEYS after_esc research_open={ResearchOpen} pause_open={PauseOpen}"); SendKey(Key.B); break;
            case 50: GD.Print($"UIKEYS build_open={BuildOpen} tab={_buildMenu?.Tab}"); SendKey(Key.Pagedown); break;
            case 56: GD.Print($"UIKEYS after_pgdn tab={_buildMenu?.Tab}"); SendKey(Key.Down); break;
            case 62: SendKey(Key.Enter); break;
            case 68: GD.Print($"UIKEYS after_enter build_open={BuildOpen} tool={_tool} selected={(_selected < _palette.Count ? ItemName(_palette[_selected]) : "?")}"); SendKey(Key.B); break;
            case 74: GD.Print($"UIKEYS reopened={BuildOpen}"); SendKey(Key.Escape); break;
            case 80: GD.Print($"UIKEYS after_esc build_open={BuildOpen} tool={_tool}"); GetTree().Quit(); break;
        }
    }
}
