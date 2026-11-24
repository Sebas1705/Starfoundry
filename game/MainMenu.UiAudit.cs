using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--ui-audit-menu [--ui-lang=es|en] [--audit-sizes=...] [--audit-shots=dir]</c>: the text-fit audit of the main menu (Main, Settings, Controls) and of
/// the New game screen (Campaign, Story, Sandbox) for every window size; the in-game panels are covered by <c>--ui-audit</c>. Prints <c>UIAUDIT</c> lines and
/// <c>UIAUDIT RESULT errors=N</c>, and quits with exit code 1 when there were errors.
/// </summary>
public partial class MainMenu
{
    private bool _menuAudit;
    private string[] _auditSizes = { "800x600", "1024x600", "1280x720", "1920x1080" };
    private string? _auditShots;
    private int _auditFrame, _auditStep, _auditSize, _auditErrors, _auditNotes;
    private readonly List<(string Name, Action Setup)> _auditSteps = new();

    private void MenuAuditSetup(string[] args)
    {
        _menuAudit = true;
        if (args.FirstOrDefault(a => a.StartsWith("--audit-sizes="))?["--audit-sizes=".Length..] is { Length: > 0 } list) _auditSizes = list.Split(',');
        _auditShots = args.FirstOrDefault(a => a.StartsWith("--audit-shots="))?["--audit-shots=".Length..];
        var s = _auditSteps;
        void Back() { if (_newGame is not null) CloseNewGame(); }
        s.Add(("settle", () => { }));
        s.Add(("menu", () => { Back(); _menu.Open(MenuScreen.Page.Main); }));
        s.Add(("menu.settings", () => { Back(); _menu.Open(MenuScreen.Page.Settings); }));
        s.Add(("menu.controls", () => { Back(); _menu.Open(MenuScreen.Page.Controls); }));
        s.Add(("newgame.campaign", () => { Back(); OpenNewGame(NewGameScreen.Tab.Campaign); }));
        s.Add(("newgame.story", () => _newGame?.ShowTab(NewGameScreen.Tab.Story)));
        s.Add(("newgame.sandbox", () => _newGame?.ShowTab(NewGameScreen.Tab.Sandbox)));
        s.Add(("done", Back));
    }

    private void MenuAuditFrame()
    {
        _auditFrame++;
        const int Period = 20;
        if (_auditSize >= _auditSizes.Length)
        {
            GD.Print($"UIAUDIT RESULT errors={_auditErrors} notes={_auditNotes} lang={Loc.Lang} screen=menu");
            _menuAudit = false;
            GetTree().Quit(_auditErrors == 0 ? 0 : 1);
            return;
        }
        int phase = _auditFrame % Period;
        if (_auditStep == 0 && _auditFrame == 1)
        {
            var wh = _auditSizes[_auditSize].Split('x');
            var win = GetWindow();
            win.Mode = Window.ModeEnum.Windowed;
            win.Size = new Vector2I(int.Parse(wh[0]), int.Parse(wh[1]));
        }
        if (_auditStep >= _auditSteps.Count) { _auditStep = 0; _auditSize++; _auditFrame = 0; return; }
        var step = _auditSteps[_auditStep];
        if (phase == 2) step.Setup();
        if (phase == 16)
        {
            _auditStep++;
            if (step.Name == "settle") return;
            string ctx = $"{_auditSizes[_auditSize]}/{step.Name}";
            if (_auditShots is not null) { System.IO.Directory.CreateDirectory(_auditShots); GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_auditShots, ctx.Replace("/", "_") + ".png")); }
            var findings = UiAudit.Run(GetTree().Root, GetViewport().GetVisibleRect());
            int errors = UiAudit.Report(ctx, findings);
            _auditErrors += errors;
            _auditNotes += findings.Count - errors;
        }
    }
}
