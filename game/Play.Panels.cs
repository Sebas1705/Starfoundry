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

    // ---- research ---------------------------------------------------------------------------------------------------

    private void ToggleResearchPanel()
    {
        if (_researchPanel is null) return;
        if (ResearchOpen) { CloseResearchPanel(); return; }
        CloseBuildMenu();
        _mapOpen = false;
        _researchPanel.Open();
    }

    private void CloseResearchPanel() => _researchPanel?.Close();

    private void StartResearch(int node)
    {
        Send(new SetResearch(node));
        ToastProduction(L("research.nowLab", Loc.Name("research", _content.Research[node].Id)));
    }

    // ---- build menu -------------------------------------------------------------------------------------------------

    private void ToggleBuildMenu()
    {
        if (_buildMenu is null) return;
        if (BuildOpen) { CloseBuildMenu(); return; }
        CloseResearchPanel();
        _mapOpen = false;
        _buildMenu.Open(_tool == ToolMode.Build ? CategoryOfSelection() : null);
    }

    private void CloseBuildMenu() => _buildMenu?.Close();

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
            L(e.DescriptionKey), status, st.Locked, st.Affordable, IsSelectedEntry(e));
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
