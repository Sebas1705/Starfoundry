using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Saved designs on disk: one text file per design in <c>user://blueprints</c>, written through a temporary file so a crash never leaves half a design. The simulation, the command log and the save game never see these files.</summary>
public sealed class UserBlueprintStore : IBlueprintStore
{
    public const string Dir = "user://blueprints";

    public IReadOnlyList<string> Files()
    {
        using var d = DirAccess.Open(Dir);
        return d is null ? Array.Empty<string>() : d.GetFiles().Where(f => f.EndsWith(".bp", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    public string? Read(string file)
    {
        if (!SafeName(file) || !FileAccess.FileExists($"{Dir}/{file}")) return null;
        using var f = FileAccess.Open($"{Dir}/{file}", FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    public bool Write(string file, string text)
    {
        if (!SafeName(file)) return false;
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(Dir);
            string tmp = $"{Dir}/{file}.tmp";
            using (var f = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
            {
                if (f is null) return false;
                f.StoreString(text);
            }
            using var d = DirAccess.Open(Dir);
            if (d is null) return false;
            if (d.FileExists(file)) d.Remove(file);
            return d.Rename(file + ".tmp", file) == Error.Ok;
        }
        catch (Exception) { return false; }
    }

    public bool Delete(string file)
    {
        if (!SafeName(file)) return false;
        using var d = DirAccess.Open(Dir);
        return d is not null && d.FileExists(file) && d.Remove(file) == Error.Ok;
    }

    private static bool SafeName(string file) => file.Length > 0 && file.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && !file.Contains("..");
}

/// <summary>The saved designs of the blueprint editor (phase 5): a modal list with slots, variants and presets, over the engine-free <see cref="BlueprintLibrary"/>.</summary>
public partial class BlueprintEditor
{
    private BlueprintLibrary? _library;
    private ColorRect _designsShade = null!;
    private PanelContainer _designsPanel = null!;
    private VBoxContainer _designRows = null!;
    private LineEdit _designName = null!, _designVariant = null!;
    private FitLabel _designTitle = null!, _designHint = null!;
    private string? _designSelected, _designActive, _armedDelete, _armedOverwrite;

    /// <summary>Replaces the store (tests and the headless check use a memory one so they never touch the user's files).</summary>
    public IBlueprintStore Store { get; set; } = new UserBlueprintStore();

    private BlueprintLibrary Library => _library ??= new BlueprintLibrary(Store, _content);

    private string StructureName => _structure >= 0 ? "station" : "ship";

    public bool DesignsOpen => _designsShade is { Visible: true };

    private void BuildDesigns()
    {
        _designsShade = new ColorRect { Name = "DesignsShade", Color = new Color(0f, 0f, 0f, 0.55f), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _designsShade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _designsPanel = Panel("DesignsPanel", UiTheme.Gold);
        _designsShade.AddChild(_designsPanel);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 8);
        _designsPanel.AddChild(col);

        _designTitle = Label("DesignsTitle", 1, UiTheme.Heading, UiTheme.Small);
        col.AddChild(_designTitle);
        _designHint = Label("DesignsHint", 2, UiTheme.Small, UiTheme.Caption, Muted);
        col.AddChild(_designHint);

        _designRows = new VBoxContainer { Name = "DesignList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _designRows.AddThemeConstantOverride("separation", 3);
        var scroll = new ScrollWithHint(_designRows) { Name = "DesignScroll" };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(scroll);

        _designName = new LineEdit { Name = "DesignName", PlaceholderText = Loc.T("bp.design.name"), MaxLength = BlueprintDesignFormat.MaxName, ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 32) };
        _designVariant = new LineEdit { Name = "DesignVariant", PlaceholderText = Loc.T("bp.design.variant"), MaxLength = BlueprintDesignFormat.MaxName, ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 32) };
        var fields = new HBoxContainer();
        fields.AddThemeConstantOverride("separation", 8);
        fields.AddChild(_designName); fields.AddChild(_designVariant);
        col.AddChild(fields);

        var actions = new HFlowContainer { Name = "DesignActions" };
        actions.AddThemeConstantOverride("h_separation", 6);
        actions.AddThemeConstantOverride("v_separation", 6);
        Button Act(string name, string key, Action a)
        {
            var b = UiKit.Button(Loc.T(key), a, 13, 90);
            b.Name = name; b.ClipText = true; b.TooltipText = Loc.T(key);
            actions.AddChild(b);
            return b;
        }
        Act("DesignSave", "bp.design.save", SaveDesign);
        Act("DesignLoad", "bp.design.load", LoadDesign);
        Act("DesignRename", "bp.design.rename", RenameDesign);
        Act("DesignDuplicate", "bp.design.duplicate", DuplicateDesign);
        Act("DesignPreset", "bp.design.pin", TogglePreset);
        Act("DesignDelete", "bp.design.delete", DeleteDesign);
        Act("DesignClose", "bp.design.close", CloseDesigns);
        col.AddChild(actions);
        AddChild(_designsShade);
    }

    private void LayoutDesigns(Vector2 size)
    {
        if (_designsPanel is null) return;
        var s = new Vector2(Math.Min(620f, size.X - 32f), Math.Min(500f, size.Y - 32f));
        _designsPanel.Size = s;
        _designsPanel.Position = (size - s) / 2f;
    }

    public void OpenDesigns()
    {
        _armedDelete = _armedOverwrite = null;
        _designTitle.SetFit(Loc.T(_structure >= 0 ? "bp.design.title.station" : "bp.design.title"));
        _designHint.SetFit(Loc.T("bp.design.empty"));
        ImportLegacySlot();
        RebuildDesignList();
        LayoutDesigns(_lastSize.X > 1f ? _lastSize : GetViewportRect().Size);
        _designsShade.Visible = true;
        _designName.GrabFocus();
    }

    public void CloseDesigns()
    {
        _designsShade.Visible = false;
        _designName.ReleaseFocus(); _designVariant.ReleaseFocus();
    }

    /// <summary>The first slot file of the phase 2 stub (<c>ship_slot1.txt</c>) becomes a design once, so nobody loses it.</summary>
    private void ImportLegacySlot()
    {
        string path = $"{UserBlueprintStore.Dir}/{StructureName}_slot1.txt";
        if (Store is not UserBlueprintStore || !FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var d = f is null ? null : BlueprintDesignFormat.Parse(f.GetAsText(), _content);
        if (d is null) return;
        Library.Save(d with { Structure = StructureName, Name = Loc.T("bp.design.imported") });
        using var dir = DirAccess.Open(UserBlueprintStore.Dir);
        dir?.Remove($"{StructureName}_slot1.txt");
    }

    private void RebuildDesignList()
    {
        ClearChildren(_designRows);
        var list = Library.List(StructureName);
        _designHint.Visible = list.Count == 0;
        int i = 0;
        foreach (var info in list)
        {
            var row = info;
            string text = Loc.T("bp.design.row", info.Preset ? Loc.T("bp.design.preset") : "", info.Variant.Length > 0 ? info.Name + " / " + info.Variant : info.Name, info.Modules, info.Parts > 0 ? Loc.T("bp.design.row.parts", info.Parts) : "");
            var b = new Button
            {
                Name = "Design_" + i++, Text = text, ToggleMode = true, Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.All,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30), TooltipText = text,
            };
            UiTheme.StyleButton(b);
            b.AddThemeFontSizeOverride("font_size", 13);
            b.SetPressedNoSignal(row.File == _designSelected);
            b.Pressed += () => SelectDesign(row);
            _designRows.AddChild(b);
        }
    }

    private void SelectDesign(DesignInfo info)
    {
        _designSelected = info.File;
        _designName.Text = info.Name;
        _designVariant.Text = info.Variant;
        _armedDelete = _armedOverwrite = null;
        RebuildDesignList();
    }

    private DesignInfo? SelectedInfo() => _designSelected is null ? null : Library.List(StructureName).FirstOrDefault(i => i.File == _designSelected);

    /// <summary>Saves the draft under the name and variant in the fields; an existing design is replaced only on a second press.</summary>
    public void SaveDesign()
    {
        string name = BlueprintDesignFormat.Clean(_designName.Text);
        if (name.Length == 0) { Say?.Invoke(Loc.T("bp.design.needName")); return; }
        string variant = BlueprintDesignFormat.Clean(_designVariant.Text);
        string file = BlueprintLibrary.FileFor(StructureName, name, variant);
        bool exists = Library.Load(file) is not null;
        if (exists && _armedOverwrite != file) { _armedOverwrite = file; Say?.Invoke(Loc.T("bp.design.overwrite", name)); return; }
        var saved = Library.Save(_s.ToDesign(name, variant, Library.Load(file)?.Preset ?? false));
        _armedOverwrite = null;
        if (saved is null) { Say?.Invoke(Loc.T("bp.design.failed")); return; }
        _designSelected = _designActive = saved;
        Say?.Invoke(Loc.T("bp.design.saved", variant.Length > 0 ? name + " / " + variant : name));
        RebuildDesignList();
    }

    /// <summary>Ctrl+S and the Save button: replaces the design that was last saved or loaded; with none yet it opens the list.</summary>
    public void QuickSave()
    {
        if (_designActive is { } file && Library.Load(file) is { Structure: var st } d && st == StructureName)
        {
            if (Library.Save(_s.ToDesign(d.Name, d.Variant, d.Preset)) is not null) { Say?.Invoke(Loc.T("bp.design.saved", d.Variant.Length > 0 ? d.Name + " / " + d.Variant : d.Name)); return; }
            Say?.Invoke(Loc.T("bp.design.failed"));
            return;
        }
        OpenDesigns();
    }

    public void LoadDesign()
    {
        var info = SelectedInfo();
        if (info is null) { Say?.Invoke(Loc.T("bp.design.pickFirst")); return; }
        var d = Library.Load(info.File);
        if (d is null) { Say?.Invoke(Loc.T("bp.design.unreadable")); return; }
        if (_s.FloorModule is not null) CloseFloor();
        var r = _s.LoadDesign(d);
        if (r is null) { Say?.Invoke(Loc.T("bp.design.wrongKind")); return; }
        _designActive = info.File;
        string label = d.Variant.Length > 0 ? d.Name + " / " + d.Variant : d.Name;
        Say?.Invoke(r.Value.Skipped > 0 ? Loc.T("bp.design.loadedSkipped", label, r.Value.Done, r.Value.Skipped) : Loc.T("bp.design.loaded", label, r.Value.Done));
        _discardArmed = false;
        CloseDesigns();
    }

    public void RenameDesign()
    {
        var info = SelectedInfo();
        if (info is null) { Say?.Invoke(Loc.T("bp.design.pickFirst")); return; }
        string name = BlueprintDesignFormat.Clean(_designName.Text);
        if (name.Length == 0) { Say?.Invoke(Loc.T("bp.design.needName")); return; }
        var file = Library.Rename(info.File, name);
        if (file is null) { Say?.Invoke(Loc.T("bp.design.failed")); return; }
        if (_designActive == info.File) _designActive = file;
        _designSelected = file;
        Say?.Invoke(Loc.T("bp.design.renamed", name));
        RebuildDesignList();
    }

    public void DuplicateDesign()
    {
        var info = SelectedInfo();
        if (info is null) { Say?.Invoke(Loc.T("bp.design.pickFirst")); return; }
        string variant = BlueprintDesignFormat.Clean(_designVariant.Text);
        if (variant.Length == 0 || variant == info.Variant) { Say?.Invoke(Loc.T("bp.design.needVariant")); return; }
        var file = Library.Duplicate(info.File, variant);
        if (file is null) { Say?.Invoke(Loc.T("bp.design.failed")); return; }
        _designSelected = file;
        Say?.Invoke(Loc.T("bp.design.duplicated", variant));
        RebuildDesignList();
    }

    public void TogglePreset()
    {
        var info = SelectedInfo();
        if (info is null) { Say?.Invoke(Loc.T("bp.design.pickFirst")); return; }
        if (!Library.SetPreset(info.File, !info.Preset)) { Say?.Invoke(Loc.T("bp.design.failed")); return; }
        Say?.Invoke(Loc.T(info.Preset ? "bp.design.unpinned" : "bp.design.pinned", info.Name));
        RebuildDesignList();
    }

    /// <summary>Deleting needs a second press on the same design.</summary>
    public void DeleteDesign()
    {
        var info = SelectedInfo();
        if (info is null) { Say?.Invoke(Loc.T("bp.design.pickFirst")); return; }
        if (_armedDelete != info.File) { _armedDelete = info.File; Say?.Invoke(Loc.T("bp.design.deleteArm", info.Name)); return; }
        _armedDelete = null;
        if (!Library.Delete(info.File)) { Say?.Invoke(Loc.T("bp.design.failed")); return; }
        if (_designActive == info.File) _designActive = null;
        _designSelected = null;
        Say?.Invoke(Loc.T("bp.design.deleted", info.Name));
        RebuildDesignList();
    }
}
