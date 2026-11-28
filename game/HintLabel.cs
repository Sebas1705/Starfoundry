using Godot;

namespace Starfoundry.Game;

/// <summary>
/// A label for the hint line over the view (a fixed-width, centred, wrapping label): while it has text it draws the same soft pill as a toast behind
/// the text, as wide as the longest line and not as wide as the label, and nothing when it is empty. The script draw runs under the label's own text.
/// </summary>
public partial class HintLabel : Label
{
    private static readonly StyleBoxFlat Pill = UiTheme.Box(new Color(0.02f, 0.04f, 0.08f, 0.9f), new Color(0.45f, 0.58f, 0.82f, 0.35f), 9, 0f);
    private string _drawn = "";
    private Vector2 _drawnSize;

    public override void _Process(double delta)
    {
        if (Text == _drawn && Size == _drawnSize) return;
        _drawn = Text;
        _drawnSize = Size;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Text.Length == 0) return;
        var font = GetThemeFont("font") ?? ThemeDB.FallbackFont;
        int size = GetThemeFontSize("font_size");
        var measured = font.GetMultilineStringSize(Text, HorizontalAlignment.Left, Size.X, size);
        float w = System.Math.Min(Size.X, measured.X);
        DrawStyleBox(Pill, new Rect2((Size.X - w) / 2f - 10f, -3f, w + 20f, Size.Y + 6f));
    }
}
