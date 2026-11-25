using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The research panel (T) and the build menu (B): both are pointer-visible overlays above the HUD whose content comes from the
/// engine-free <see cref="ResearchView"/> and <see cref="BuildCatalog"/>. Also where a new game picks its galaxy seed.
/// </summary>
public partial class Play
{
    private ResearchPanel? _researchPanel;
    private BuildMenu? _buildMenu;
    private IReadOnlyList<BuildEntry> _catalog = Array.Empty<BuildEntry>();
    private int _panelFrame;

    private bool ResearchOpen => _researchPanel is { Visible: true };

    private bool BuildOpen => _buildMenu is { Visible: true };

    // ---- galaxy seed ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The seed of this game's galaxy. A continued game takes it from the save header; <c>--seed=N</c> fixes it; the scripted runs
    /// (any other flag: demos, screenshots, benchmarks) keep <see cref="ScriptedSeed"/> so they stay comparable; a played new game
    /// picks a random one (1 to 999 999 999, short enough to read out and type).
    /// </summary>
    private ulong ChooseSeed(string[] args)
    {
        if (Launch.Mode == Launch.Intent.Continue && Launch.ContinueSave is { } slot && Godot.FileAccess.FileExists(SavePath(slot)))
        {
            try
            {
                byte[] bytes = Godot.FileAccess.GetFileAsBytes(SavePath(slot));
                ulong seed = SaveGame.Peek(new System.IO.MemoryStream(bytes)).GalaxySeed;
                if (seed != 0) return seed;
            }
            catch (Exception e) { GD.PrintErr("SEED could not read the save header: " + e.Message); }
        }
        if (args.FirstOrDefault(a => a.StartsWith("--seed="))?["--seed=".Length..] is { } text && ulong.TryParse(text, out ulong fixedSeed) && fixedSeed != 0) return fixedSeed;
        if (args.Any(a => !a.StartsWith("--menu-shot="))) return ScriptedSeed;
        return (ulong)Random.Shared.NextInt64(1, 1_000_000_000);
    }

    // ---- scenario ----------------------------------------------------------------------------------------------------

    private ScenarioCatalog _scenarios = ScenarioCatalog.Empty;
    private ScenarioDef? _scenario;
    private ScenarioProgress? _progress;
    private string? _goalsShotPath;

    private sealed record ScenarioPlan(ScenarioDef Def, ulong Seed, WorldRules Rules);

    /// <summary>
    /// Which scenario this run plays, or null for the legacy path (every scripted flag keeps it, so the old numbers and shots do not change).
    /// In order: the scenario of a save being continued (its seed and rules come from the header); <c>--goals-shot</c> (level 3);
    /// <c>--scenario=id</c> with an optional <c>--seed=N</c>; then the choice made on the New Game screen when no other flag is given.
    /// </summary>
    private ScenarioPlan? ChooseScenario(string[] args)
    {
        if (_scenarios.All.Count == 0) return null;
        if (Launch.Mode == Launch.Intent.Continue && Launch.ContinueSave is { } slot && Godot.FileAccess.FileExists(SavePath(slot)))
        {
            try
            {
                var info = SaveGame.Peek(new System.IO.MemoryStream(Godot.FileAccess.GetFileAsBytes(SavePath(slot))));
                if (info.ScenarioId is not null && _scenarios.Find(info.ScenarioId) is { } saved && info.GalaxySeed != 0)
                    return new ScenarioPlan(saved, info.GalaxySeed, info.Rules ?? saved.Rules);
            }
            catch (Exception e) { GD.PrintErr("SCENARIO could not read the save header: " + e.Message); }
            return null;
        }
        string? id = null;
        ulong seed = 0;
        WorldRules? rules = null;
        if (_goalsShotPath is not null || (_endShotPath is not null && !args.Contains("--end-legacy"))) id = "03_under_siege";
        else if (args.FirstOrDefault(a => a.StartsWith("--scenario="))?["--scenario=".Length..] is { Length: > 0 } flag)
        {
            id = flag;
            if (args.FirstOrDefault(a => a.StartsWith("--seed="))?["--seed=".Length..] is { } s && ulong.TryParse(s, out ulong fixedSeed)) seed = fixedSeed;
            if (args.FirstOrDefault(a => a.StartsWith("--difficulty="))?["--difficulty=".Length..] is { } d) rules = WorldRules.Preset(d);
        }
        else if (Launch.ScenarioId is { } chosen && !args.Any(a => !a.StartsWith("--menu-shot=") && !a.StartsWith(ContentLocator.ContentFlag)))
        {
            id = chosen;
            seed = Launch.Seed;
            rules = Launch.Rules;
        }
        if (id is null || _scenarios.Find(id) is not { } def)
        {
            if (id is not null) GD.PrintErr($"SCENARIO unknown scenario '{id}'");
            return null;
        }
        if (def.PlayerRules && rules is null) rules = def.Rules;
        rules ??= def.Rules;
        if (seed == 0) seed = def.Seed;
        if (seed == 0) seed = (ulong)Random.Shared.NextInt64(1, 1_000_000_000);
        return new ScenarioPlan(def, seed, rules);
    }

    // ---- building the panels ----------------------------------------------------------------------------------------

    private void BuildPanels()
    {
        _catalog = BuildCatalog.Entries(_content, _structuresOn, _stationRules);

        _researchPanel = new ResearchPanel(() => ResearchView.Build(_content, Net, Player));
        _researchPanel.StartRequested += StartResearch;
        _researchPanel.CloseRequested += CloseResearchPanel;
        _uiRoot.AddChild(_researchPanel);

        _buildMenu = new BuildMenu(() => _catalog, EntryView);
        _buildMenu.Chosen += ChooseFromMenu;
        _buildMenu.CloseRequested += CloseBuildMenu;
        _uiRoot.AddChild(_buildMenu);
        BuildInventoryPanel();
        BuildComputer();
    }

    /// <summary>Per frame: keeps the open panel in step with the world (a few times a second is plenty).</summary>
    private void UpdatePanels()
    {
        if (!ResearchOpen && !BuildOpen) return;
        if (++_panelFrame % 6 != 0) return;
        var size = GetViewport().GetVisibleRect().Size;
        if (ResearchOpen) _researchPanel!.Refresh(size);
        if (BuildOpen) _buildMenu!.Refresh(size);
    }

    /// <summary>The right-hand column of the screen the research panel and the build menu dock to: about 40 percent of the width, from the top down to the key panel.</summary>
    private Rect2 DockRect(Vector2 size)
    {
        float width = _computerTab == ComputerTab.Inventory ? Math.Clamp(size.X - 350f, 440f, 960f) : Math.Clamp(size.X * 0.4f, 440f, 860f); // the inventory has two panels side by side
        float bottom = _keyPanel is { Visible: true } ? _keyRect.Position.Y - 8f : size.Y - 8f;
        float top = ComputerOpen ? 10f + ShipComputerBar.Height + 6f : 10f; // the ship computer's tab strip sits above the panel
        return new Rect2(size.X - width - 10f, top, width, Math.Max(200f, bottom - top));
    }

    // ---- research ---------------------------------------------------------------------------------------------------

    private void ToggleResearchPanel() => ToggleComputerTab(ComputerTab.Research);

    private void CloseResearchPanel()
    {
        if (_computerTab == ComputerTab.Research) CloseComputer();
        else _researchPanel?.Close();
    }

    private void StartResearch(int node)
    {
        Send(new SetResearch(node));
        ToastProduction(L("research.nowLab", Loc.Name("research", _content.Research[node].Id)));
    }

    // ---- build menu -------------------------------------------------------------------------------------------------

    private void ToggleBuildMenu() => ToggleComputerTab(ComputerTab.Build);

    private void CloseBuildMenu()
    {
        if (_computerTab == ComputerTab.Build) CloseComputer();
        else _buildMenu?.Close();
    }

    /// <summary>The tab of the current tool, so the menu opens where the player last built.</summary>
    private BuildCategory? CategoryOfSelection()
    {
        var e = _catalog.FirstOrDefault(IsSelectedEntry);
        return e?.Category;
    }

    /// <summary>The palette index a catalog entry selects, or -1 (the entry is not in this world's palette).</summary>
    private int PaletteIndexOf(BuildEntry e) => e.Kind switch
    {
        BuildEntryKind.Belt => _palette.FindIndex(b => b.IsBelt && b.Tier == e.Tier),
        BuildEntryKind.Machine => _palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation && b.Machine == e.Machine),
        BuildEntryKind.Module or BuildEntryKind.LegacyModule => _palette.FindIndex(b => b.IsModule),
        _ => _palette.FindIndex(b => b.IsStation),
    };

    private bool IsSelectedEntry(BuildEntry e)
    {
        if (_tool != ToolMode.Build) return false;
        int index = PaletteIndexOf(e);
        if (index < 0 || index != _selected) return false;
        return e.Kind != BuildEntryKind.Module || e.Module == _moduleKind;
    }

    private BuildEntryView EntryView(BuildEntry e)
    {
        var st = BuildCatalog.Status(_content, Net, Player, e);
        int index = PaletteIndexOf(e);
        string key = index >= 0 && (index < DirectKeys || _palette[index].IsStation || (_palette[index].IsModule && e.Kind != BuildEntryKind.Module)) ? KeyLabel(index) : "";
        string role = e.Kind == BuildEntryKind.Machine ? L("tip.role." + _content.Machines[e.Machine].Role.ToString().ToLowerInvariant()) : L(e.RoleKey);
        string status = st.Locked
            ? (st.UnlockedBy >= 0 ? L("build.locked", Loc.Name("research", _content.Research[st.UnlockedBy].Id)) : L("build.lockedNoNode"))
            : !st.Affordable && st.Cost.Length > 0 ? Missing(st.Cost) : "";
        return new BuildEntryView(BuildCatalog.Name(_content, e), key, role, BuildCatalog.CostText(_content, e, st.Cost), BuildCatalog.PowerText(e),
            L(e.DescriptionKey), status, st.Locked, st.Affordable, IsSelectedEntry(e), EntryIcon(e),
            st.Cost.Select(c => (Icons.ItemId(_content.Items[c.Item].Id), c.Count, Net.HoldCount(Player, c.Item))).ToList());
    }

    private void ChooseFromMenu(BuildEntry e)
    {
        var st = BuildCatalog.Status(_content, Net, Player, e);
        if (st.Locked)
        {
            ToastLogistics(st.UnlockedBy >= 0 ? L("build.locked", Loc.Name("research", _content.Research[st.UnlockedBy].Id)) : Why(Reject.Locked), 5.0);
            return;
        }
        int index = PaletteIndexOf(e);
        if (index < 0) return;
        if (e.Kind == BuildEntryKind.Module && e.Module is { } kind) _moduleKind = kind;
        SelectBuild(index); // closes the menu and leaves the player in flight or aim with the tool in hand
    }
}
