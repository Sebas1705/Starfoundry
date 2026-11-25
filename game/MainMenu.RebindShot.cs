using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

public partial class MainMenu
{
    private string? _rebindShotPath;

    /// <summary>
    /// <c>--rebind-shot=path</c> (add <c>--resolution 1280x720</c> or <c>1920x1080</c>): saves the key rebinding page in Spanish and English, the capture
    /// dialog, the swap dialog, the reserved-key notice and the scrolled list as <c>path.es.png</c>, <c>.en.png</c>, <c>.capture.png</c>, <c>.conflict.png</c>,
    /// <c>.reserved.png</c> and <c>.scrolled.png</c>, and quits. Nothing is saved to the settings file.
    /// </summary>
    private void RebindShotFrame()
    {
        string path = _rebindShotPath!;
        _frame++;
        var page = () => _menu.KeysPage;
        if (_frame == 10) { Loc.Lang = Language.Es; _menu.Open(MenuScreen.Page.Controls); }
        if (_frame == 50) { Save(path + ".es.png"); Loc.Lang = Language.En; _menu.Rebuild(); }
        if (_frame == 90) { Save(path + ".en.png"); page()?.BeginCapture(new KeySlot("scan", 0)); }
        if (_frame == 110) { Save(path + ".capture.png"); page()?.HandleCaptured("F"); }
        if (_frame == 130) { Save(path + ".conflict.png"); page()?.Dismiss(); page()?.BeginCapture(new KeySlot("scan", 0)); page()?.HandleCaptured("Tab"); }
        if (_frame == 150) { Save(path + ".reserved.png"); page()?.Dismiss(); page()?.ScrollTo(100000); }
        if (_frame == 180) { Save(path + ".scrolled.png"); GetTree().Quit(); }
    }
}
