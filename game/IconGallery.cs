using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>Gallery.tscn --focus=icons</c>: the whole icon set on one sheet, grouped, each icon at 32 and 16 pixels with its name in the current
/// language, plus an unregistered id to show the generic fallback. Flags: <c>--shot=path.png</c> (save after a few frames and quit),
/// <c>--lang=es|en</c>, <c>--page=N</c> (0 for all groups, 1 to 3 for a third of them each, for a small window). Add
/// <c>--resolution 1280x1700</c> to see everything at once.
/// </summary>
public partial class IconGallery : Node
{
    private string? _shot;
    private int _frame;

    private static readonly (string Title, Func<string, bool> Pick)[] Groups =
    {
        ("Items", id => id.StartsWith("item:")),
        ("Modules", id => id.StartsWith("module:")),
        ("Machines and belts", id => id.StartsWith("machine:") || id.StartsWith("belt:")),
        ("Science fields", id => id.StartsWith("field:")),
        ("Research nodes", id => id.StartsWith("research:")),
        ("Enemies, obstacles, stars, systems", id => id.StartsWith("enemy:") || id.StartsWith("obstacle:") || id.StartsWith("star:") || id.StartsWith("arch:")),
        ("Tools and actions", id => id.StartsWith("tool:")),
        ("Status", id => id.StartsWith("status:")),
    };

    public static void Run(Node host, string[] args)
    {
        string? Arg(string key) => args.FirstOrDefault(a => a.StartsWith(key + "="))?[(key.Length + 1)..];
        if (Arg("--lang") == "es") Loc.Lang = Language.Es;
        else if (Arg("--lang") == "en") Loc.Lang = Language.En;
        int page = int.TryParse(Arg("--page"), out int p) ? p : 0;
        var gallery = new IconGallery { _shot = Arg("--shot") };
        host.AddChild(gallery);
        gallery.Build(page);
    }

    private void Build(int page)
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var bg = new ColorRect { Color = new Color(0.05f, 0.07f, 0.11f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);
        var scroll = new MarginContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.AddThemeConstantOverride("margin_left", 16);
        scroll.AddThemeConstantOverride("margin_top", 12);
        scroll.AddThemeConstantOverride("margin_right", 16);
        layer.AddChild(scroll);
        scroll.Theme = UiTheme.Create();

        var all = IconCatalog.All.ToList();
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(column);
        var title = new Label { Text = $"Icons ({all.Count})   stroke {IconGlyphs.Stroke} on a 24 grid   scale x{Icons.PixelScale}" };
        title.AddThemeFontSizeOverride("font_size", UiTheme.Heading);
        column.AddChild(title);

        var groups = Groups.Select(g => (g.Title, Ids: all.Where(g.Pick).ToList())).Where(g => g.Ids.Count > 0).ToList();
        groups.Add(("Fallback (unregistered ids)", new List<string> { "item:mod:unobtainium", "machine:mod:gizmo", "module:mod:thing", "tool:unknown" }));
        if (page > 0) groups = groups.Where((_, i) => i % 3 == page - 1).ToList();
        foreach (var (name, ids) in groups)
        {
            var head = new Label { Text = name.ToUpperInvariant() };
            head.AddThemeFontSizeOverride("font_size", UiTheme.Small);
            head.AddThemeColorOverride("font_color", UiTheme.Muted);
            column.AddChild(head);
            column.AddChild(UiTheme.Rule());
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 6);
            column.AddChild(flow);
            foreach (var id in ids) flow.AddChild(Cell(id));
        }
    }

    private static Control Cell(string id)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(112, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.09f, 0.12f, 0.19f, 0.95f), new Color(0.3f, 0.38f, 0.55f, 0.6f), 6, 5f));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 3);
        panel.AddChild(box);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new IconRect(id, 32));
        row.AddChild(new IconRect(id, 16));
        row.AddChild(new IconRect(id, 24));
        box.AddChild(row);
        var name = new Label { Text = IconCatalog.Name(id), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100, 0) };
        name.AddThemeFontSizeOverride("font_size", UiTheme.Caption);
        box.AddChild(name);
        return panel;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_shot is null || _frame != 12) return;
        var err = GetViewport().GetTexture().GetImage().SavePng(_shot);
        GD.Print($"screenshot {_shot}: {err}");
        GetTree().Quit();
    }
}
