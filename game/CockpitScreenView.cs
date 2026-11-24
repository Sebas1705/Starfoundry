using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Everything the cockpit displays show, gathered by the game once per refresh (see Play.Cockpit.cs). Plain data: the views only draw it.</summary>
public sealed class CockpitSnapshot
{
    /// <summary>False when there is no ship to read (lost, no player): the screens say no signal.</summary>
    public bool Online;
    public FlightDisplayData Flight;
    public SystemsDisplayData Systems;
    public WeaponsDisplayData Weapons;
    public GizmoAxis[] Axes = Array.Empty<GizmoAxis>();
    public VelocityMarker Velocity;
    public IReadOnlyList<NavTarget> Targets = Array.Empty<NavTarget>();
    public string SystemName = "";
    public bool Autopilot, DockNear, DockAssist, DockReady, Docked, Jumping, Rebuilding, HasReactor, ScanReady = true, HasGalaxy;
    public int DockDistance, JumpSeconds;
    public HudMode HudMode;
    public HudDetail HudDetail = HudDetail.Standard;
    public string SelectedName = "";
    /// <summary>Everything the navigation page can show on its map (positions in the ship's frame), and the target the pilot picked, if any.</summary>
    public IReadOnlyList<NavTarget> MapTargets = Array.Empty<NavTarget>();
    public NavKind? PickKind;
    public int PickId = -1;
    /// <summary>Angular velocity about the ship's axes in degrees per second (pitch up, yaw right, roll right) and the most it can reach.</summary>
    public System.Numerics.Vector3 TurnRates;
    public float TurnRateMax = 90f;
    public bool FlightAssist = true, HasInertia = true;
}

/// <summary>
/// One of the three displays of the console: a <see cref="Control"/> drawn into a SubViewport whose texture is the emissive surface of a quad on the
/// console. It draws the page its screen shows (flight, nav, systems or weapons) in code, with the shared <see cref="UiTheme"/> colours and the
/// icons, a tab strip along the bottom and the clickable buttons of the page, then a faint scanline pattern over everything. The buttons it drew
/// last are in <see cref="Buttons"/> (pixels of its own texture); the game hit-tests the crosshair against them.
/// </summary>
public partial class CockpitScreenView : Control
{
    public readonly CockpitScreen Screen;
    public readonly CockpitPages Pages;
    public CockpitSnapshot Snapshot { get; set; } = new();
    public readonly List<CockpitButton> Buttons = new();

    /// <summary>Where the crosshair is on this display, in texture pixels, or null when it is not on it.</summary>
    public Vector2? Hover { get; set; }
    public bool Focused { get; set; }
    public float Time { get; set; }

    private static readonly Color Bg = new(0.015f, 0.03f, 0.055f);
    private static readonly Color Cyan = new(0.45f, 0.82f, 1f);
    private static readonly Color Amber = new(1f, 0.78f, 0.35f);
    private static readonly Color Green = new(0.45f, 0.95f, 0.6f);
    private static readonly Color Red = new(1f, 0.4f, 0.35f);

    public CockpitScreenView(CockpitScreen screen, CockpitPages pages, Vector2I size)
    {
        Screen = screen;
        Pages = pages;
        CustomMinimumSize = new Vector2(size.X, size.Y);
        Size = new Vector2(size.X, size.Y);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private static Color Accent(CockpitPage page) => page switch
    {
        CockpitPage.Flight => Cyan,
        CockpitPage.Nav => Green,
        CockpitPage.Systems => Amber,
        _ => Red,
    };

    private static string PageIcon(CockpitPage page) => page switch
    {
        CockpitPage.Flight => "status:speed",
        CockpitPage.Nav => "tool:map",
        CockpitPage.Systems => "status:power",
        _ => "tool:weapons",
    };

    private static string PageName(CockpitPage page) => Loc.T("cp.page." + page.ToString().ToLowerInvariant());

    private static Color BandColor(float fraction) => CockpitMath.Band(fraction) switch
    {
        HealthBand.Good => UiTheme.Good,
        HealthBand.Worn => UiTheme.Warn,
        _ => UiTheme.Bad,
    };

    // ---- text and shape helpers --------------------------------------------------------------------------

    private Font F => ThemeDB.FallbackFont;

    /// <summary>Draws text with its top at <paramref name="y"/>.</summary>
    private void Txt(string s, float x, float y, int size, Color c, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
    {
        DrawString(F, new Vector2(x, y + F.GetAscent(size)), s, align, width, size, c);
    }

    private float Measure(string s, int size) => F.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    /// <summary>The text cut with an ellipsis to fit <paramref name="maxWidth"/>.</summary>
    private string Fit(string s, int size, float maxWidth)
    {
        if (Measure(s, size) <= maxWidth) return s;
        while (s.Length > 1 && Measure(s + "…", size) > maxWidth) s = s[..^1];
        return s + "…";
    }

    private void Icon(string id, float x, float y, int size, Color tint) =>
        DrawTextureRect(Icons.Get(id, size), new Rect2(x, y, size, size), false, tint);

    private void Fill(Rect2 r, Color c, int radius = 4)
    {
        DrawStyleBox(UiTheme.Box(c, null, radius, 0f), r);
    }

    private void Frame(Rect2 r, Color edge, Color? fill = null, int radius = 5, int width = 1)
    {
        DrawStyleBox(UiTheme.Box(fill ?? new Color(0f, 0f, 0f, 0f), edge, radius, 0f, width), r);
    }

    private void Bar(Rect2 r, float fraction, Color c, float mark = -1f)
    {
        Fill(r, UiTheme.Track, (int)(r.Size.Y / 2));
        if (fraction > 0.001f) Fill(new Rect2(r.Position.X, r.Position.Y, MathF.Max(r.Size.Y, r.Size.X * Math.Clamp(fraction, 0f, 1f)), r.Size.Y), c, (int)(r.Size.Y / 2));
        if (mark > 0f && mark < 1f) DrawLine(new Vector2(r.Position.X + r.Size.X * mark, r.Position.Y - 1), new Vector2(r.Position.X + r.Size.X * mark, r.End.Y + 1), new Color(1f, 1f, 1f, 0.6f), 1f);
    }

    /// <summary>One line of a gauge: icon, label, bar, value.</summary>
    private void GaugeLine(float x, float y, float w, string icon, string label, float fraction, Color c, string value)
    {
        Icon(icon, x, y + 1, 20, c);
        Txt(label, x + 28, y + 3, 14, UiTheme.Muted);
        float valueW = Math.Max(70f, Measure(value, 16) + 4f);
        float barX = x + 28 + 92;
        float barW = Math.Max(30f, w - (barX - x) - valueW - 8f);
        Bar(new Rect2(barX, y + 7, barW, 10), fraction, c);
        Txt(value, x + w - valueW, y + 1, 16, c, HorizontalAlignment.Right, valueW);
    }

    /// <summary>A button: drawn lit when active, dim when disabled, highlighted when the crosshair is on it. Registers its rectangle.</summary>
    private void Button(Rect2 r, string label, string? icon, CockpitAction action, int arg = 0, bool active = false, bool enabled = true, Color? tint = null, int size = 15)
    {
        Color accent = tint ?? Cyan;
        bool hot = enabled && Hover is { } h && r.HasPoint(h);
        Color fill = !enabled ? new Color(0.05f, 0.06f, 0.09f) : active ? accent.Darkened(0.55f) : hot ? new Color(0.16f, 0.24f, 0.36f) : new Color(0.08f, 0.11f, 0.17f);
        Color edge = !enabled ? new Color(0.2f, 0.22f, 0.28f) : hot ? Colors.White : active ? accent : new Color(accent, 0.55f);
        Fill(r, fill, 5);
        Frame(r, edge, null, 5, hot ? 2 : 1);
        Color text = !enabled ? UiTheme.Dim : active || hot ? Colors.White : accent;
        float tx = r.Position.X + 8f;
        if (icon is not null)
        {
            int isz = (int)Math.Clamp(r.Size.Y - 10f, 14f, 22f);
            Icon(icon, r.Position.X + 8f, r.Position.Y + (r.Size.Y - isz) / 2f, isz, text);
            tx += isz + 6f;
        }
        string shown = Fit(label, size, r.End.X - tx - 6f);
        Txt(shown, tx, r.Position.Y + (r.Size.Y - size) / 2f - 1f, size, text);
        if (enabled) Buttons.Add(new CockpitButton(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, action, arg));
    }

    // ---- the frame of a display ----------------------------------------------------------------------------

    public override void _Draw()
    {
        Buttons.Clear();
        var size = Size;
        var page = Pages.PageOn(Screen);
        Color accent = Accent(page);
        DrawRect(new Rect2(Vector2.Zero, size), Bg);
        // a faint grid behind the page
        for (float gx = 0; gx < size.X; gx += 32f) DrawLine(new Vector2(gx, 0), new Vector2(gx, size.Y), new Color(accent, 0.035f), 1f);
        for (float gy = 0; gy < size.Y; gy += 32f) DrawLine(new Vector2(0, gy), new Vector2(size.X, gy), new Color(accent, 0.035f), 1f);

        var s = Snapshot;
        DrawHeader(size, page, accent);
        if (!s.Online) DrawNoSignal(size);
        else
        {
            switch (page)
            {
                case CockpitPage.Flight: DrawFlight(size, accent); break;
                case CockpitPage.Nav: DrawNav(size, accent); break;
                case CockpitPage.Systems: DrawSystems(size, accent); break;
                default: DrawWeapons(size, accent); break;
            }
        }
        DrawTabs(size);
        if (Focused) Frame(new Rect2(2, 2, size.X - 4, size.Y - 4), new Color(Colors.White, 0.7f), null, 6, 2);
        Frame(new Rect2(0, 0, size.X, size.Y), new Color(accent, 0.5f), null, 0, 2);
        DrawOverlay(size, accent);
        if (Hover is { } p) DrawCursor(p);
    }

    private void DrawHeader(Vector2 size, CockpitPage page, Color accent)
    {
        DrawRect(new Rect2(0, 0, size.X, 40), new Color(accent, 0.16f));
        DrawLine(new Vector2(0, 40), new Vector2(size.X, 40), new Color(accent, 0.7f), 2f);
        Icon(PageIcon(page), 10, 9, 22, accent);
        Txt(PageName(page), 40, 7, 22, accent);
        string mode = Loc.T("cp.hud") + " " + Loc.T("cp.hud." + HudModes.Id(Snapshot.HudMode));
        float w = Math.Min(size.X * 0.5f, Measure(mode, 13) + 24f);
        Button(new Rect2(size.X - w - 8, 7, w, 26), mode, null, CockpitAction.CycleHudMode, 0, Snapshot.HudMode != HudMode.Full, true, Cyan, 13);
        // The detail level (minimal, standard, detailed) has its own button beside the mode one, when the title leaves room for it.
        string detail = Loc.T("cp.detail") + " " + Loc.T("hud.detail." + HudDetails.Id(Snapshot.HudDetail));
        float dw = Measure(detail, 13) + 24f;
        float dx = size.X - w - 8f - 6f - dw;
        if (dx > 40f + Measure(PageName(page), 22) + 8f)
            Button(new Rect2(dx, 7, dw, 26), detail, null, CockpitAction.CycleHudDetail, 0, Snapshot.HudDetail != HudDetail.Standard, true, Cyan, 13);
    }

    private void DrawTabs(Vector2 size)
    {
        float y = size.Y - 46f;
        DrawLine(new Vector2(0, y - 4), new Vector2(size.X, y - 4), new Color(1f, 1f, 1f, 0.12f), 1f);
        float w = (size.X - 16f - 3 * 6f) / 4f;
        for (int i = 0; i < CockpitPages.PageCount; i++)
        {
            var page = (CockpitPage)i;
            bool here = Pages.PageOn(Screen) == page;
            bool elsewhere = Pages.ScreenOf(page) is { } sc && sc != Screen;
            var r = new Rect2(8 + i * (w + 6), y, w, 38);
            Color tint = Accent(page);
            Button(r, size.X < 340f ? "" : PageName(page), PageIcon(page), CockpitAction.ShowPage, i, here, true, elsewhere ? tint.Darkened(0.35f) : tint, size.X < 500f ? 12 : 14);
        }
    }

    private void DrawNoSignal(Vector2 size)
    {
        Txt(Loc.T("cp.offline"), 0, size.Y / 2f - 24f, 28, UiTheme.Bad, HorizontalAlignment.Center, size.X);
    }

    /// <summary>Scanlines, a slow bright band and a blinking pilot lamp: the screen is visibly live.</summary>
    private void DrawOverlay(Vector2 size, Color accent)
    {
        for (int y = 0; y < (int)size.Y; y += 4) DrawRect(new Rect2(0, y, size.X, 1), new Color(0f, 0f, 0f, 1f - CockpitMath.Scanline(y))); // subtle: one dim row in four
        // Low power dims the screens (and flickers them when the reactor is nearly out): the power that reaches the consumers, as the power gauge shows it.
        if (Snapshot.Online && (Snapshot.HasReactor || Snapshot.Flight.PowerProduction > 0) && Snapshot.Flight.PowerPermille < 1000)
        {
            float lack = Math.Clamp(1f - Snapshot.Flight.PowerPermille / 1000f, 0f, 1f);
            float flicker = Snapshot.Flight.PowerPermille < 400 && ((int)(Time * 9f) % 5 == 0) ? 0.25f : 0f;
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0f, Math.Clamp(lack * 0.6f + flicker, 0f, 0.85f)));
        }
        float band = (Time * 70f) % (size.Y + 60f) - 30f;
        DrawRect(new Rect2(0, band, size.X, 26f), new Color(accent, 0.045f));
        if (((int)(Time * 1.6f)) % 2 == 0) DrawCircle(new Vector2(size.X - 16, 52), 3f, new Color(accent, 0.9f));
    }

    private void DrawCursor(Vector2 p)
    {
        DrawArc(p, 9f, 0f, Mathf.Tau, 20, new Color(1f, 1f, 1f, 0.85f), 2f);
        DrawCircle(p, 2.2f, new Color(1f, 1f, 1f, 0.95f));
    }

    // ---- flight --------------------------------------------------------------------------------------------

    private void DrawFlight(Vector2 size, Color accent)
    {
        var s = Snapshot;
        var f = s.Flight;
        bool wide = size.X > 560f;
        float colW = wide ? size.X * 0.5f - 10f : size.X - 24f;
        Txt(Loc.T("cp.speed"), 16, 50, 13, UiTheme.Muted);
        string speed = CockpitMath.Speed(f.Speed, Loc.Culture);
        Color speedColor = f.Boosting ? Cyan : UiTheme.Text;
        Txt(speed, 14, 64, wide ? 76 : 62, speedColor); // the key number: big enough to read from the seat at 720p, where the screen is about 150 pixels wide
        float numW = Measure(speed, wide ? 76 : 62);
        Txt(Loc.T("cp.unit"), 20 + numW, wide ? 118 : 106, 16, UiTheme.Muted);
        Bar(new Rect2(16, wide ? 158 : 142, colW - 4, 10), f.SpeedFraction, f.Boosting ? Cyan : accent, f.TargetFraction); // the white mark is the demanded speed (the HUD gauge's target)
        Txt("/ " + CockpitMath.Speed(f.MaxSpeed, Loc.Culture), colW - 80, wide ? 118 : 106, 13, UiTheme.Dim, HorizontalAlignment.Right, 76);
        // the boost chip
        if (f.BoostAvailable)
        {
            var chip = new Rect2(wide ? colW - 98 : size.X - 112, 52, 100, 26);
            Fill(chip, f.Boosting ? Cyan.Darkened(0.4f) : new Color(0.07f, 0.09f, 0.14f), 5);
            Frame(chip, f.Boosting ? Cyan : new Color(1f, 1f, 1f, 0.2f), null, 5);
            Icon("tool:boost", chip.Position.X + 6, chip.Position.Y + 3, 20, f.Boosting ? Colors.White : UiTheme.Dim);
            Txt(Loc.T("cp.boost"), chip.Position.X + 30, chip.Position.Y + 5, 13, f.Boosting ? Colors.White : UiTheme.Dim);
        }
        float y = wide ? 178f : 160f;
        float step = wide ? 28f : 29f;
        GaugeLine(16, y, colW - 4, "status:speed", Loc.T("cp.thrust"), f.Thrust, accent, $"{Mathf.RoundToInt(f.Thrust * 100f)}%");
        y += step;
        if (f.MaxHull > 0) GaugeLine(16, y, colW - 4, "status:hull", Loc.T("cp.hull"), f.HullFraction, BandColor(f.HullFraction), $"{f.Hull}/{f.MaxHull}");
        else GaugeLine(16, y, colW - 4, "status:hull", Loc.T("cp.hull"), 1f, UiTheme.Good, "OK");
        y += step;
        if (s.HasReactor || f.PowerProduction > 0) GaugeLine(16, y, colW - 4, "status:power", Loc.T("cp.power"), f.PowerFraction, PowerColor(f.PowerPermille), $"{f.PowerProduction}/{f.PowerDemand}");
        else GaugeLine(16, y, colW - 4, "status:power", Loc.T("cp.power"), 0f, UiTheme.Bad, Loc.T("cp.noPower"));
        y += step;
        string fuel = f.FuelTicksLeft > 0 ? $"{f.Hydrogen}  {PowerFormat.Clock(f.FuelTicksLeft)}" : f.Hydrogen > 0 ? $"{f.Hydrogen}" : "0";
        GaugeLine(16, y, colW - 4, "item:core:hydrogen", Loc.T("cp.fuel"), f.FuelLow ? 0.15f : 0.85f, f.FuelLow ? UiTheme.Warn : UiTheme.Good, fuel);
        y += step;
        GaugeLine(16, y, colW - 4, "status:cargo", Loc.T("cp.hold"), f.HoldFraction, f.HoldFraction > 0.95f ? UiTheme.Warn : accent, $"{f.HoldUsed}/{f.HoldCapacity}");

        if (wide) { DrawAttitude(new Vector2(size.X - 150f, 168f), 84f, accent); DrawTurnIndicator(new Rect2(size.X - 290f, 280f, 280f, 68f), accent); }
        else DrawAttitude(new Vector2(size.X - 64f, 100f), 42f, accent, compact: true);
        if (s.Docked) Banner(size, Loc.T("cp.docked"), UiTheme.Good);
        else if (s.Jumping) Banner(size, $"{Loc.T("cp.jumping")} {s.JumpSeconds}s", UiTheme.Warn);
        else if (s.Rebuilding) Banner(size, Loc.T("cp.rebuilding"), UiTheme.Bad);
    }

    /// <summary>
    /// The three-axis turn-rate indicator of the flight display: pitch, yaw and roll as bars from the centre (right or up is to the right), against the ship's
    /// turn rate, and the flight assist button. Without ship inertia the controller is off and the indicator says so.
    /// </summary>
    private void DrawTurnIndicator(Rect2 r, Color accent)
    {
        var s = Snapshot;
        Txt(Loc.T("cp.turn"), r.Position.X, r.Position.Y + 2, 12, UiTheme.Muted);
        string assist = s.FlightAssist ? Loc.T("cp.assist") : Loc.T("cp.assistOff");
        var btn = new Rect2(r.End.X - 96, r.Position.Y - 2, 96, 22);
        if (s.HasInertia) Button(btn, assist, "tool:boost", CockpitAction.ToggleFlightAssist, 0, s.FlightAssist, true, s.FlightAssist ? accent : Amber, 12);
        else Txt(Loc.T("cp.noInertia"), btn.Position.X, btn.Position.Y + 4, 12, UiTheme.Dim, HorizontalAlignment.Right, btn.Size.X);
        string[] names = { Loc.T("cp.rate.pitch"), Loc.T("cp.rate.yaw"), Loc.T("cp.rate.roll") };
        float[] rates = { s.TurnRates.X, s.TurnRates.Y, s.TurnRates.Z };
        float max = Math.Max(1f, s.TurnRateMax);
        float labelW = 34f, valueW = 40f;
        float barX = r.Position.X + labelW, barW = r.Size.X - labelW - valueW;
        for (int i = 0; i < 3; i++)
        {
            float y = r.Position.Y + 24f + i * 16f;
            Txt(names[i], r.Position.X, y - 1, 11, UiTheme.Muted);
            var track = new Rect2(barX, y + 2, barW, 8);
            Fill(track, UiTheme.Track, 4);
            float mid = barX + barW / 2f;
            float frac = Math.Clamp(rates[i] / max, -1f, 1f);
            if (MathF.Abs(frac) > 0.003f)
            {
                float w = MathF.Abs(frac) * barW / 2f;
                Fill(new Rect2(frac > 0f ? mid : mid - w, y + 2, MathF.Max(3f, w), 8), s.HasInertia ? accent : UiTheme.Dim, 3);
            }
            DrawLine(new Vector2(mid, y), new Vector2(mid, y + 12), new Color(1f, 1f, 1f, 0.5f), 1f);
            Txt(FlightAssistText.Rate(rates[i]), r.End.X - valueW, y - 1, 11, UiTheme.Text, HorizontalAlignment.Right, valueW);
        }
    }

    private static Color PowerColor(int permille) => PowerFormat.Band(permille) switch
    {
        PowerBand.Full => UiTheme.Good,
        PowerBand.Strained => UiTheme.Warn,
        _ => UiTheme.Bad,
    };

    private void Banner(Vector2 size, string text, Color c)
    {
        var r = new Rect2(16, size.Y - 90f, size.X - 32f, 30f);
        Fill(r, new Color(c, 0.2f), 5);
        Frame(r, c, null, 5);
        Txt(text, r.Position.X, r.Position.Y + 5, 16, c, HorizontalAlignment.Center, r.Size.X);
    }

    /// <summary>The galactic frame as the ship sees it (the same axes as the HUD gizmo) with the velocity dot: where the ship is heading against the nose.</summary>
    private void DrawAttitude(Vector2 c, float r, Color accent, bool compact = false)
    {
        if (!compact) Txt(Loc.T("cp.attitude") + " / " + Loc.T("cp.vector"), c.X - r - 14, c.Y - r - 34, 13, UiTheme.Muted);
        DrawCircle(c, r, new Color(0.02f, 0.05f, 0.09f, 0.9f));
        for (int i = 1; i <= 2; i++) DrawArc(c, r * i / 2f, 0f, Mathf.Tau, 48, new Color(accent, 0.22f), 1f);
        DrawArc(c, r, 0f, Mathf.Tau, 64, new Color(accent, 0.7f), compact ? 1.5f : 2f);
        DrawLine(c + new Vector2(-r, 0), c + new Vector2(r, 0), new Color(accent, 0.18f), 1f);
        DrawLine(c + new Vector2(0, -r), c + new Vector2(0, r), new Color(accent, 0.18f), 1f);
        // the nose
        DrawLine(c + new Vector2(-6, 0), c + new Vector2(6, 0), new Color(1f, 1f, 1f, 0.6f), 1f);
        DrawLine(c + new Vector2(0, -6), c + new Vector2(0, 6), new Color(1f, 1f, 1f, 0.6f), 1f);
        // the galactic axes: far ones drawn first, faint when they point away
        var axes = Snapshot.Axes;
        Color[] axisColors = { new(1f, 0.45f, 0.4f), new(0.55f, 0.95f, 0.5f), new(0.5f, 0.7f, 1f) };
        if (axes.Length >= 3)
        {
            var order = new List<int> { 0, 1, 2 };
            order.Sort((a, b) => axes[b].Depth.CompareTo(axes[a].Depth));
            foreach (int i in order)
            {
                var a = axes[i];
                var tip = c + new Vector2(a.X, -a.Y) * (r - 8f);
                var col = new Color(axisColors[i], a.Receding ? 0.5f : 1f);
                DrawLine(c, tip, col, a.Receding ? 1.5f : 3f, true);
                DrawCircle(tip, a.Receding ? 3f : 5f, col);
                if (!compact) Txt(a.Label, tip.X + 7, tip.Y - 8, 14, col);
            }
        }
        var v = Snapshot.Velocity;
        if (!v.Still)
        {
            var p = c + new Vector2(v.X, -v.Y) * (r - 10f);
            if (v.Behind) DrawArc(p, 7f, 0f, Mathf.Tau, 16, Amber, 2.5f);
            else DrawCircle(p, 6f, Amber);
            DrawArc(p, 10f, 0f, Mathf.Tau, 20, new Color(Amber, 0.6f), 1.5f);
        }
        if (!compact)
        {
            string note = v.Still ? Loc.T("cp.still") : v.Behind ? Loc.T("cp.drift") : "";
            if (note.Length > 0) Txt(note, c.X - r, c.Y + r + 8, 13, Amber, HorizontalAlignment.Center, r * 2f);
        }
    }

    // ---- navigation ----------------------------------------------------------------------------------------

    /// <summary>The map of the navigation page: where it was drawn on this display (texture pixels), so the game can tell a drag on the map from a click elsewhere.</summary>
    public Rect2 MapArea { get; private set; }

    /// <summary>The camera of the map (owned by the game, shared by the displays that show the navigation page).</summary>
    public NavMapCamera Map { get; set; } = new();

    private static Color KindColor(NavKind k, bool selected) => k switch
    {
        NavKind.Enemy => UiTheme.Bad,
        NavKind.Gate => Amber,
        NavKind.Port => Cyan,
        _ => selected ? UiTheme.Gold : Green,
    };

    private void DrawNav(Vector2 size, Color accent)
    {
        MapArea = default;
        if (size.X < 480f) DrawNavNarrow(size);
        else if (Focused) DrawNavFocused(size);
        else DrawNavCompact(size);
    }

    private NavTarget? CurrentTarget() => NavPage.Current(Snapshot.MapTargets, Snapshot.PickKind, Snapshot.PickId);

    private int IndexOf(NavTarget t)
    {
        var list = Snapshot.MapTargets;
        for (int i = 0; i < list.Count; i++) if (list[i] == t) return i;
        return -1;
    }

    /// <summary>The big numbers: the name of the target, its distance and the ship's speed. Fonts are in texture pixels (see <see cref="NavPage.KeyFonts"/>).</summary>
    private void DrawBigNumbers(float x, float y, float w, NavTarget? t, int nameFont, int numFont, bool sideBySide)
    {
        var s = Snapshot;
        Color c = t is null ? UiTheme.Dim : KindColor(t.Kind, true);
        float h = nameFont * 1.2f;
        string name = t?.Name ?? Loc.T("cp.nav.noTarget");
        float iconW = 0f;
        if (t is not null) { int isz = Math.Min(nameFont, 44); Icon(t.Icon, x, y + (h - isz) / 2f, isz, c); iconW = isz + 8f; }
        Txt(Fit(name, nameFont, w - iconW), x + iconW, y, nameFont, t is null ? UiTheme.Dim : Colors.White);
        float yy = y + h + 2f;
        string dist = NavPage.BigDistance(t, Loc.Culture);
        string speed = CockpitMath.Speed(s.Flight.Speed, Loc.Culture);
        float colW = sideBySide ? w / 2f : w;
        Txt(Loc.T("cp.nav.dist"), x, yy, 12, UiTheme.Muted);
        Txt(dist, x, yy + 12f, numFont, c);
        float dW = Measure(dist, numFont);
        Txt(Loc.T("cp.unit.cells"), x + Math.Min(dW + 6f, colW - 40f), yy + 12f + numFont * 0.62f, 13, UiTheme.Muted);
        float sx = sideBySide ? x + colW : x;
        float sy = sideBySide ? yy : yy + 12f + numFont * 1.2f + 4f;
        Txt(Loc.T("cp.speed"), sx, sy, 12, UiTheme.Muted);
        Txt(speed, sx, sy + 12f, numFont, s.Flight.Boosting ? Cyan : UiTheme.Text);
        if (s.Flight.Boosting) Txt(Loc.T("cp.boost"), sx + Measure(Loc.T("cp.speed"), 12) + 8f, sy, 12, Cyan);
    }

    private void DrawNavButtons(Rect2 area, bool vertical, bool narrow = false)
    {
        var s = Snapshot;
        var cur = CurrentTarget();
        bool canAuto = s.Autopilot || (cur is { Kind: NavKind.Deposit or NavKind.Port });
        int n = 3;
        float gap = 6f;
        for (int i = 0; i < n; i++)
        {
            Rect2 r = vertical
                ? new Rect2(area.Position.X, area.Position.Y + i * (area.Size.Y - gap * (n - 1)) / n + i * gap, area.Size.X, (area.Size.Y - gap * (n - 1)) / n)
                : new Rect2(area.Position.X + i * ((area.Size.X - gap * (n - 1)) / n + gap), area.Position.Y, (area.Size.X - gap * (n - 1)) / n, area.Size.Y);
            switch (i)
            {
                case 0: Button(r, s.Autopilot ? Loc.T("cp.nav.autoOn") : Loc.T("cp.nav.auto"), narrow ? null : "tool:map", CockpitAction.ToggleAutopilot, 0, s.Autopilot, canAuto, Green, narrow ? 11 : 13); break;
                case 1: Button(r, s.DockAssist ? Loc.T("cp.nav.dockAssistOn") : Loc.T("cp.nav.dockAssist"), narrow ? null : "tool:dock", CockpitAction.ToggleDockAssist, 0, s.DockAssist, s.DockNear || s.DockAssist, Cyan, narrow ? 11 : 13); break;
                default: Button(r, s.ScanReady ? Loc.T("cp.nav.scan") : Loc.T("cp.nav.scanWait"), narrow ? null : "tool:scan", CockpitAction.Scan, 0, !s.ScanReady, s.ScanReady, Amber, narrow ? 11 : 13); break;
            }
        }
    }

    /// <summary>The target list: icon, name, turn and distance per row; any row can be picked (deposits, threats, ports, the gate).</summary>
    private void DrawTargetRows(float x, float y, float w, int maxRows, float rowH, int font)
    {
        var s = Snapshot;
        var current = CurrentTarget();
        if (s.Targets.Count == 0) Txt(Fit(Loc.T("cp.nav.none"), font, w), x + 4, y + 6, font, UiTheme.Dim);
        var shown = s.Targets.Take(maxRows).ToList();
        if (current is not null && !shown.Contains(current) && s.Targets.Contains(current) && shown.Count > 0) shown[^1] = current; // the target the page is about is always in view
        for (int i = 0; i < shown.Count; i++)
        {
            var t = shown[i];
            var r = new Rect2(x, y + i * (rowH + 3f), w, rowH);
            Color c = KindColor(t.Kind, t.Selected);
            bool picked = current == t;
            bool hot = Hover is { } h && r.HasPoint(h);
            Fill(r, picked ? new Color(c, 0.22f) : hot ? new Color(0.14f, 0.2f, 0.3f) : new Color(0.06f, 0.09f, 0.14f), 5);
            Frame(r, picked ? c : hot ? Colors.White : new Color(c, 0.35f), null, 5, picked || hot ? 2 : 1);
            int isz = (int)Math.Min(22f, rowH - 6f);
            Icon(t.Icon, r.Position.X + 6, r.Position.Y + (rowH - isz) / 2f, isz, c);
            string dist = CockpitMath.Distance(t.Distance, Loc.Culture);
            float dw = Math.Max(48f, Measure(dist, font + 1) + 6f);
            Txt(dist, r.End.X - 6 - dw, r.Position.Y + (rowH - font) / 2f - 1f, font + 1, c, HorizontalAlignment.Right, dw);
            string turn = t.TurnDegrees > 3f ? $"{Mathf.RoundToInt(t.TurnDegrees)}°" : "";
            float turnW = w > 260f ? 40f : 0f;
            if (turnW > 0f) Txt(turn, r.End.X - 6 - dw - turnW, r.Position.Y + (rowH - font) / 2f, font - 2, UiTheme.Muted, HorizontalAlignment.Right, turnW - 4f);
            Txt(Fit(t.Name, font, r.Size.X - isz - 20 - dw - turnW), r.Position.X + isz + 12, r.Position.Y + (rowH - font) / 2f - 1f, font, picked ? Colors.White : UiTheme.Text);
            int idx = IndexOf(t);
            if (idx >= 0) Buttons.Add(new CockpitButton(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, CockpitAction.PickTarget, idx));
        }
    }

    private void DrawPrevNext(Rect2 r, bool any)
    {
        float half = (r.Size.X - 4f) / 2f;
        Button(new Rect2(r.Position.X, r.Position.Y, half, r.Size.Y), "<", null, CockpitAction.PrevTarget, 0, false, any, Green, 18);
        Button(new Rect2(r.Position.X + half + 4f, r.Position.Y, half, r.Size.Y), ">", null, CockpitAction.NextTarget, 0, false, any, Green, 18);
    }

    /// <summary>The default look (and the one the seat sees): the three numbers that matter in big type, a small map, the list and the buttons.</summary>
    private void DrawNavCompact(Vector2 size)
    {
        var (nameF, numF, _) = NavPage.KeyFonts((int)size.X);
        var cur = CurrentTarget();
        DrawBigNumbers(12f, 44f, size.X - 24f - 92f, cur, nameF, numF, sideBySide: true);
        DrawPrevNext(new Rect2(size.X - 92f, 52f, 84f, 36f), Snapshot.Targets.Count > 0);
        float mapX = size.X * 0.5f + 4f;
        float top = 44f + nameF * 1.2f + 2f + 12f + numF * 1.2f + 14f;
        float bottom = size.Y - 46f - 44f - 4f;
        DrawMap(new Rect2(mapX, top, size.X - mapX - 8f, bottom - top - 4f), false);
        DrawTargetRows(8f, top, mapX - 16f, 3, 31f, 15);
        DrawNavButtons(new Rect2(8f, bottom, size.X - 16f, 40f), false);
    }

    /// <summary>The focused look (F4 or a click): the map takes the left 62 percent of the display, the numbers, the list and the buttons the right.</summary>
    private void DrawNavFocused(Vector2 size)
    {
        float mapW = MathF.Round(size.X * 0.62f);
        DrawMap(new Rect2(8f, 46f, mapW - 8f, size.Y - 46f - 56f), true);
        float x = mapW + 8f, w = size.X - x - 8f;
        var cur = CurrentTarget();
        DrawBigNumbers(x, 46f, w, cur, 26, 34, sideBySide: true);
        float arrowsY = 46f + 26 * 1.2f + 2f + 12f + 34 * 1.2f + 8f;
        DrawPrevNext(new Rect2(x, arrowsY, w, 28f), Snapshot.Targets.Count > 0);
        DrawTargetRows(x, arrowsY + 34f, w, 3, 27f, 13);
        float bh = 3 * 26f + 8f;
        DrawNavButtons(new Rect2(x, size.Y - 46f - 4f - bh, w, bh), true);
    }

    private void DrawNavNarrow(Vector2 size)
    {
        var cur = CurrentTarget();
        DrawBigNumbers(10f, 44f, size.X - 20f, cur, 22, 28, sideBySide: true);
        float y = 44f + 22 * 1.2f + 2f + 12f + 28 * 1.2f + 8f;
        DrawTargetRows(8f, y, size.X - 16f, 4, 30f, 13);
        DrawPrevNext(new Rect2(8f, size.Y - 46f - 4f - 36f - 4f - 30f - 2f, size.X - 16f, 30f), Snapshot.Targets.Count > 0);
        DrawNavButtons(new Rect2(8f, size.Y - 46f - 4f - 36f, size.X - 16f, 36f), false, true);
    }

    // ---- the 3D map ----------------------------------------------------------------------------------------

    private static Vector2 MapPoint(Vector2 centre, NavMapCamera.Point p, float radius) => new(centre.X + p.X * radius, centre.Y - p.Y * radius);

    /// <summary>
    /// The map of space round the ship: rings of range on the ship's plane, the ship at the middle, and every deposit, threat, port and gate as its icon at its
    /// place, with a stem down to the plane and a dashed route to the chosen one. The mouse turns it (drag) and zooms it (wheel) while it is in focus.
    /// </summary>
    private void DrawMap(Rect2 r, bool detailed)
    {
        MapArea = r;
        Fill(r, new Color(0.01f, 0.025f, 0.045f), 6);
        Frame(r, new Color(Cyan, 0.4f), null, 6);
        var map = Map;
        var s = Snapshot;
        var cur = CurrentTarget();
        float st = MathF.Max(0.3f, MathF.Sin(map.Tilt));
        float radius = MathF.Min(r.Size.X / 2f - 10f, (r.Size.Y / 2f - 10f) / st);
        var centre = new Vector2(r.Position.X + r.Size.X / 2f, r.Position.Y + r.Size.Y / 2f);
        // rings on the ship's plane (an ellipse seen from above at the tilt) and the two axes
        for (int ring = 1; ring <= 3; ring++)
        {
            float rho = ring / 3f;
            var pts = new Vector2[49];
            for (int i = 0; i <= 48; i++)
            {
                float a = i / 48f * MathF.Tau;
                var p = map.Project(new System.Numerics.Vector3(MathF.Sin(a) * rho * map.Range, 0f, MathF.Cos(a) * rho * map.Range));
                pts[i] = MapPoint(centre, p, radius);
            }
            DrawPolyline(pts, new Color(Cyan, ring == 3 ? 0.4f : 0.18f), 1f, true);
            if (detailed || ring == 3)
            {
                var lp = MapPoint(centre, map.Project(new System.Numerics.Vector3(0f, 0f, rho * map.Range)), radius);
                Txt(CockpitMath.Distance(rho * map.Range, Loc.Culture), lp.X + 4f, lp.Y - 6f, detailed ? 12 : 11, new Color(Cyan, 0.6f));
            }
        }
        var fwd = MapPoint(centre, map.Project(new System.Numerics.Vector3(0f, 0f, map.Range)), radius);
        var back = MapPoint(centre, map.Project(new System.Numerics.Vector3(0f, 0f, -map.Range)), radius);
        var right = MapPoint(centre, map.Project(new System.Numerics.Vector3(map.Range, 0f, 0f)), radius);
        var left = MapPoint(centre, map.Project(new System.Numerics.Vector3(-map.Range, 0f, 0f)), radius);
        DrawLine(back, fwd, new Color(Cyan, 0.15f), 1f);
        DrawLine(left, right, new Color(Cyan, 0.15f), 1f);
        // the ship
        float k = map.Range * 0.06f;
        var nose = MapPoint(centre, map.Project(new System.Numerics.Vector3(0f, 0f, k)), radius);
        var wl = MapPoint(centre, map.Project(new System.Numerics.Vector3(-k * 0.6f, 0f, -k * 0.6f)), radius);
        var wr = MapPoint(centre, map.Project(new System.Numerics.Vector3(k * 0.6f, 0f, -k * 0.6f)), radius);
        DrawColoredPolygon(new[] { nose, wr, centre, wl }, new Color(Cyan, 0.9f));
        // targets, far ones first
        var items = new List<(NavTarget T, int Index, NavMapCamera.Point P, float Over)>();
        for (int i = 0; i < s.MapTargets.Count; i++)
        {
            var t = s.MapTargets[i];
            var local = t.Local;
            if (t.Kind == NavKind.Gate) local = System.Numerics.Vector3.Normalize(local.LengthSquared() > 1e-6f ? local : System.Numerics.Vector3.UnitZ) * map.Range * 0.9f;
            var p = map.Project(local);
            float over = MathF.Sqrt(p.X * p.X + (p.Y / st) * (p.Y / st));
            if (over > 0.97f) { float f = 0.97f / over; p = new NavMapCamera.Point(p.X * f, p.Y * f, p.Depth, p.ShadowX * f, p.ShadowY * f); }
            items.Add((t, i, p, over));
        }
        items.Sort((a, b) => b.P.Depth.CompareTo(a.P.Depth));
        Vector2? routeEnd = null;
        string? routeLabel = null;
        foreach (var (t, index, p, over) in items)
        {
            bool picked = cur == t;
            Color c = KindColor(t.Kind, t.Selected);
            var pos = MapPoint(centre, p, radius);
            float fade = Math.Clamp(1f - p.Depth * 0.35f, 0.55f, 1f);
            bool outside = over > 0.97f;
            if (!outside)
            {
                var shadow = MapPoint(centre, new NavMapCamera.Point(p.ShadowX, p.ShadowY, 0f, 0f, 0f), radius);
                if (shadow.DistanceTo(pos) > 3f)
                {
                    DrawLine(shadow, pos, new Color(c, 0.3f), 1f);
                    DrawCircle(shadow, 2f, new Color(c, 0.35f));
                }
            }
            int isz = detailed ? 22 : 18;
            if (picked) { DrawArc(pos, isz * 0.85f, 0f, Mathf.Tau, 28, UiTheme.Gold, 2f); routeEnd = pos; routeLabel = t.Name + "  " + CockpitMath.Distance(t.Distance, Loc.Culture); }
            Icon(t.Icon, pos.X - isz / 2f, pos.Y - isz / 2f, isz, new Color(c, outside ? 0.55f : fade));
            var hit = new Rect2(pos.X - 12f, pos.Y - 12f, 24f, 24f);
            Buttons.Add(new CockpitButton(hit.Position.X, hit.Position.Y, hit.Size.X, hit.Size.Y, CockpitAction.PickTarget, index));
            if (Hover is { } h && hit.HasPoint(h) && !picked)
            {
                string label = t.Name + "  " + CockpitMath.Distance(t.Distance, Loc.Culture);
                float lw = Measure(label, 12) + 8f;
                var lr = new Rect2(Math.Clamp(pos.X + 10f, r.Position.X + 2f, r.End.X - lw - 2f), pos.Y - 24f, lw, 18f);
                Fill(lr, new Color(0f, 0f, 0f, 0.8f), 4);
                Txt(label, lr.Position.X + 4f, lr.Position.Y + 2f, 12, Colors.White);
            }
        }
        if (routeEnd is { } end)
        {
            // the route: a dashed line from the ship to the chosen target, with its name and distance
            float len = centre.DistanceTo(end);
            int dashes = Math.Max(1, (int)(len / 10f));
            for (int i = 0; i < dashes; i++)
                DrawLine(centre.Lerp(end, (i + 0.1f) / dashes), centre.Lerp(end, (i + 0.6f) / dashes), new Color(UiTheme.Gold, 0.85f), 2f);
            if (routeLabel is not null)
            {
                int lf = detailed ? 14 : 12;
                float lw = Measure(routeLabel, lf) + 10f;
                var lr = new Rect2(r.Position.X + 8f, r.End.Y - 26f, Math.Min(lw, r.Size.X - 16f), 20f);
                Fill(lr, new Color(0f, 0f, 0f, 0.65f), 4);
                Txt(Fit(routeLabel, lf, lr.Size.X - 8f), lr.Position.X + 5f, lr.Position.Y + 2f, lf, UiTheme.Gold);
            }
        }
        else if (s.MapTargets.Count == 0) Txt(Fit(Loc.T("cp.nav.none"), 13, r.Size.X - 16f), r.Position.X + 8f, r.End.Y - 24f, 13, UiTheme.Dim);
        if (detailed)
        {
            // zoom buttons and the hint
            float bx = r.Position.X + 8f, by = r.Position.Y + 8f;
            Button(new Rect2(bx, by, 28, 26), "+", null, CockpitAction.MapZoom, 1, false, true, Cyan, 16);
            Button(new Rect2(bx + 32, by, 28, 26), "-", null, CockpitAction.MapZoom, -1, false, true, Cyan, 16);
            Button(new Rect2(bx + 64, by, 52, 26), map.Auto ? Loc.T("cp.nav.autoRange") : Loc.T("cp.nav.reset"), null, CockpitAction.MapZoom, 0, map.Auto, true, Cyan, 12);
            Txt(Fit(Loc.T("cp.nav.mapHint"), 11, r.Size.X - 16f), r.Position.X + 8f, r.End.Y - 46f, 11, UiTheme.Dim);
        }
    }

    // ---- systems -------------------------------------------------------------------------------------------

    private void DrawSystems(Vector2 size, Color accent)
    {
        var s = Snapshot;
        var f = s.Flight;
        float w = size.X - 28f;
        float y = 52f;
        if (f.MaxHull > 0) GaugeLine(14, y, w, "status:hull", Loc.T("cp.hull"), f.HullFraction, BandColor(f.HullFraction), $"{f.Hull}/{f.MaxHull}");
        else GaugeLine(14, y, w, "status:hull", Loc.T("cp.hull"), 1f, UiTheme.Good, "OK");
        y += 30;
        if (s.HasReactor || f.PowerProduction > 0) GaugeLine(14, y, w, "status:power", Loc.T("cp.power"), f.PowerFraction, PowerColor(f.PowerPermille), $"{f.PowerProduction}/{f.PowerDemand}");
        else GaugeLine(14, y, w, "status:power", Loc.T("cp.power"), 0f, UiTheme.Bad, Loc.T("cp.noPower"));
        y += 30;
        string fuel = f.FuelTicksLeft > 0 ? $"{f.Hydrogen}  {PowerFormat.Clock(f.FuelTicksLeft)}" : f.Hydrogen > 0 ? $"{f.Hydrogen}" : "0";
        GaugeLine(14, y, w, "item:core:hydrogen", Loc.T("cp.fuel"), f.FuelLow ? 0.15f : 0.85f, f.FuelLow ? UiTheme.Warn : UiTheme.Good, fuel);
        y += 30;
        GaugeLine(14, y, w, "status:cargo", Loc.T("cp.hold"), f.HoldFraction, f.HoldFraction > 0.95f ? UiTheme.Warn : accent, $"{f.HoldUsed}/{f.HoldCapacity}");
        y += 38;

        // the modules as a row of cells: sound green, damaged red
        var sy = s.Systems;
        Txt(Loc.T("cp.sys.modules"), 14, y, 12, UiTheme.Muted);
        Txt(Loc.T("cp.sys.sound", sy.Modules - sy.DamagedModules, sy.Modules), 14, y, 12, sy.DamagedModules > 0 ? UiTheme.Warn : UiTheme.Good, HorizontalAlignment.Right, w);
        y += 18;
        int n = Math.Min(sy.Modules, 28);
        float cell = Math.Min(24f, (w - (n - 1) * 3f) / Math.Max(1, n));
        for (int i = 0; i < n; i++)
        {
            bool bad = i >= n - Math.Min(sy.DamagedModules, n);
            Fill(new Rect2(14 + i * (cell + 3f), y, cell, 18), bad ? new Color(UiTheme.Bad, 0.85f) : new Color(UiTheme.Good, 0.55f), 3);
        }
        y += 28;
        if (sy.DamagedModules > 0) Txt(Fit(Loc.T("cp.sys.damaged", sy.DamagedModules), 13, w), 14, y, 13, UiTheme.Warn);
        y += 22;

        // the threat banner
        var wave = new Rect2(12, y, size.X - 24, 34);
        Color tc = sy.ThreatNow ? UiTheme.Bad : UiTheme.Good;
        Fill(wave, new Color(tc, 0.16f), 5);
        Frame(wave, tc, null, 5);
        string threat = sy.ThreatNow ? (Snapshot.Weapons.Enemies > 0 ? $"{Loc.T("cp.sys.threat")}: {Loc.T("cp.sys.hostiles", Snapshot.Weapons.Enemies)}" : Loc.T("cp.sys.wave")) : Loc.T("cp.sys.calm");
        Icon(sy.ThreatNow ? "status:alert" : "status:shieldOk", wave.Position.X + 8, wave.Position.Y + 6, 22, tc);
        Txt(Fit(threat, 15, wave.Size.X - 44), wave.Position.X + 38, wave.Position.Y + 8, 15, tc);

        float by = size.Y - 46f - 44f - 4f;
        DrawGroupButton(new Rect2(12, by, size.X - 24, 38));
    }

    private void DrawGroupButton(Rect2 r)
    {
        var w = Snapshot.Weapons;
        string group = w.ActiveGroup == WeaponGroup.Guns ? Loc.T("cp.wpn.guns") : Loc.T("cp.wpn.laser"); // ADR 0036: the group in hand, whatever the world
        Button(r, $"{Loc.T("cp.wpn.group")}: {group}", "tool:weapons", CockpitAction.ToggleWeaponGroup, 0, w.ActiveGroup == WeaponGroup.Guns, w.HasGun && w.HasLaser, Red, 14);
    }

    // ---- weapons -------------------------------------------------------------------------------------------

    private void DrawWeapons(Vector2 size, Color accent)
    {
        var w = Snapshot.Weapons;
        float half = (size.X - 24f - 8f) / 2f;
        float y = 54f;
        DrawWeaponCard(new Rect2(12, y, half, 112), Loc.T("cp.wpn.laser"), "tool:mine", !w.Hardpoints || w.HasLaser, w.ActiveGroup == WeaponGroup.Laser,
            w.LaserFiring ? Loc.T("cp.wpn.firing") : w.Mining ? Loc.T("cp.wpn.mining") : Loc.T("cp.wpn.ready"), w.LaserFiring || w.Mining ? Green : UiTheme.Muted,
            MiningDetail(w));
        DrawWeaponCard(new Rect2(12 + half + 8, y, half, 112), Loc.T("cp.wpn.guns"), "tool:fireGuns", !w.Hardpoints || w.HasGun, w.ActiveGroup == WeaponGroup.Guns,
            w.Armed ? Loc.T("cp.wpn.armed") : Loc.T("cp.wpn.safe"), w.Armed ? Red : UiTheme.Muted);
        y += 126f;
        DrawGroupButton(new Rect2(12, y, size.X - 24, 38));
        y += 46f;
        bool laserOk = !w.Hardpoints || w.HasLaser;
        Button(new Rect2(12, y, size.X - 24, 38), (w.Mining ? Loc.T("cp.wpn.mining") : Loc.T("key.mine")), "tool:mine", CockpitAction.ToggleMining, 0, w.Mining, laserOk, Green, 14);
        y += 50f;
        Txt(Fit(w.ThreatActive ? (w.Enemies > 0 ? Loc.T("cp.sys.hostiles", w.Enemies) : Loc.T("cp.sys.calm")) : Loc.T("cp.wpn.noThreat"), 14, size.X - 28f), 14, y, 14, w.Enemies > 0 ? UiTheme.Bad : UiTheme.Muted);
        Txt(Fit(Loc.T("cp.wpn.hint"), 12, size.X - 28f), 14, y + 22, 12, UiTheme.Dim);
    }

    /// <summary>The third line of the laser card: items per second and what is left in the deposit; the slowdown past the hold limit says so instead.</summary>
    private static string MiningDetail(WeaponsDisplayData w)
    {
        if (w.DepositLeft < 0) return Loc.T("cp.wpn.noDeposit");
        string rate = Loc.T("cp.wpn.rate", (w.MineTenthsPerSecond / 10f).ToString("0.0", Loc.Culture));
        return w.Slowed ? rate + " · " + Loc.T("cp.wpn.slowed") : rate + " · " + Loc.T("cp.wpn.depositLeft", w.DepositLeft);
    }

    private void DrawWeaponCard(Rect2 r, string name, string icon, bool fitted, bool active, string status, Color statusColor, string? detail = null)
    {
        Color edge = active ? Red : new Color(1f, 1f, 1f, 0.2f);
        Fill(r, active ? new Color(Red, 0.16f) : new Color(0.06f, 0.08f, 0.12f), 6);
        Frame(r, fitted ? edge : new Color(0.25f, 0.27f, 0.33f), null, 6, active ? 2 : 1);
        Icon(icon, r.Position.X + 10, r.Position.Y + 10, 30, fitted ? (active ? Red : UiTheme.Text) : UiTheme.Dim);
        Txt(Fit(name, 14, r.Size.X - 20), r.Position.X + 10, r.Position.Y + 50, 14, fitted ? UiTheme.Text : UiTheme.Dim);
        Txt(Fit(fitted ? status : Loc.T("cp.wpn.missing"), 15, r.Size.X - 20), r.Position.X + 10, r.Position.Y + 76, 15, fitted ? statusColor : UiTheme.Dim);
        if (detail is not null && fitted) Txt(Fit(detail, 12, r.Size.X - 20), r.Position.X + 10, r.Position.Y + 94, 12, active ? UiTheme.Text : UiTheme.Muted);
    }
}
