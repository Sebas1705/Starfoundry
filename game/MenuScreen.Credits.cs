using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>The Credits page of the menu (ADR 0037): the project licence, every bundled third-party pack from <c>assets/credits.tsv</c> (generated from ASSETS.md) and the libraries.</summary>
public partial class MenuScreen
{
    private Page _backTo = Page.Main;

    /// <summary>Opens the Credits page; Back (and Esc) return to the page it was opened from.</summary>
    public void OpenCredits()
    {
        _backTo = Current == Page.Credits ? _backTo : Current;
        Open(Page.Credits);
    }

    private Page BackTarget => Current == Page.Credits ? _backTo : Page.Main;

    private void BuildCredits()
    {
        _panel.CustomMinimumSize = new Vector2(620, 0);
        Header();
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = FocusModeEnum.All, Name = "CreditsScroll" };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);

        AddCreditsLine(box, Loc.T("credits.project", Starfoundry.Ui.Credits.ProjectLicence, Starfoundry.Ui.Credits.ProjectCopyright), 14, Colors.White);
        AddCreditsLine(box, Loc.T("credits.assets"), 13, Muted);
        string data = FileAccess.FileExists("res://assets/credits.tsv") ? FileAccess.GetFileAsString("res://assets/credits.tsv") : "";
        var packs = Starfoundry.Ui.Credits.ParseDataFile(data);
        if (packs.Count == 0) AddCreditsLine(box, Loc.T("credits.none"), 13, Colors.White);
        foreach (var p in packs)
            AddCreditsLine(box, Loc.T("credits.pack", p.Pack, p.Author, p.Licence, p.Files) + "\n" + p.Source, 13, Colors.White);
        AddCreditsLine(box, Loc.T("credits.libs"), 13, Muted);
        foreach (var l in Starfoundry.Ui.Credits.Libraries)
            AddCreditsLine(box, $"{l.Name}: {l.Licence}\n{l.Source}", 13, Colors.White);
        _body.AddChild(scroll);
        _body.AddChild(BackButton());
        _focusName ??= "BackButton";
    }

    private static void AddCreditsLine(VBoxContainer box, string text, int size, Color colour)
    {
        var l = UiKit.Text(size, colour);
        l.Text = text;
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(l);
    }
}
