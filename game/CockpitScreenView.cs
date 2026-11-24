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
    public string SelectedName = "";
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
            Button(r, PageName(page), PageIcon(page), CockpitAction.ShowPage, i, here, true, elsewhere ? tint.Darkened(0.35f) : tint, size.X < 500f ? 12 : 14);
        }
    }

    private void DrawNoSignal(Vector2 size)
    {
        Txt(Loc.T("cp.offline"), 0, size.Y / 2f - 24f, 28, UiTheme.Bad, HorizontalAlignment.Center, size.X);
    }

    /// <summary>Scanlines, a slow bright band and a blinking pilot lamp: the screen is visibly live.</summary>
    private void DrawOverlay(Vector2 size, Color accent)
    {
        for (int y = 0; y < (int)size.Y; y += 3) DrawRect(new Rect2(0, y, size.X, 1), new Color(0f, 0f, 0f, 1f - CockpitMath.Scanline(y) + 0.02f));
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
        Txt(speed, 14, 64, wide ? 66 : 54, speedColor);
        float numW = Measure(speed, wide ? 66 : 54);
        Txt(Loc.T("cp.unit"), 20 + numW, wide ? 106 : 96, 15, UiTheme.Muted);
        Bar(new Rect2(16, wide ? 140 : 126, colW - 4, 10), f.SpeedFraction, f.Boosting ? Cyan : accent, f.TargetFraction); // the white mark is the demanded speed (the HUD gauge's target)
        Txt("/ " + CockpitMath.Speed(f.MaxSpeed, Loc.Culture), colW - 80, wide ? 106 : 96, 13, UiTheme.Dim, HorizontalAlignment.Right, 76);
        // the boost chip
        if (f.BoostAvailable)
        {
            var chip = new Rect2(wide ? colW - 98 : size.X - 112, 52, 100, 26);
            Fill(chip, f.Boosting ? Cyan.Darkened(0.4f) : new Color(0.07f, 0.09f, 0.14f), 5);
            Frame(chip, f.Boosting ? Cyan : new Color(1f, 1f, 1f, 0.2f), null, 5);
            Icon("tool:boost", chip.Position.X + 6, chip.Position.Y + 3, 20, f.Boosting ? Colors.White : UiTheme.Dim);
            Txt(Loc.T("cp.boost"), chip.Position.X + 30, chip.Position.Y + 5, 13, f.Boosting ? Colors.White : UiTheme.Dim);
        }
        float y = wide ? 160f : 142f;
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

        if (wide) DrawAttitude(new Vector2(size.X - 170f, 190f), 100f, accent);
        else DrawAttitude(new Vector2(size.X - 64f, 100f), 42f, accent, compact: true);
        if (s.Docked) Banner(size, Loc.T("cp.docked"), UiTheme.Good);
        else if (s.Jumping) Banner(size, $"{Loc.T("cp.jumping")} {s.JumpSeconds}s", UiTheme.Warn);
        else if (s.Rebuilding) Banner(size, Loc.T("cp.rebuilding"), UiTheme.Bad);
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

    private void DrawNav(Vector2 size, Color accent)
    {
        var s = Snapshot;
        Txt(Loc.T("cp.nav.system"), 16, 50, 12, UiTheme.Muted);
        Txt(Fit(s.SystemName, 18, size.X - 32f), 16, 62, 18, UiTheme.Text);
        float y = 90f;
        Txt(Loc.T("cp.nav.targets"), 16, y, 12, UiTheme.Muted);
        y += 16f;
        float rowH = 34f;
        int maxRows = size.X > 560f ? 5 : 4;
        if (s.Targets.Count == 0) Txt(Fit(Loc.T("cp.nav.none"), 15, size.X - 32f), 16, y + 8, 15, UiTheme.Dim);
        for (int i = 0; i < s.Targets.Count && i < maxRows; i++)
        {
            var t = s.Targets[i];
            var r = new Rect2(12, y + i * (rowH + 3), size.X - 24, rowH);
            Color c = t.Kind switch { NavKind.Enemy => UiTheme.Bad, NavKind.Gate => Amber, NavKind.Port => Cyan, _ => t.Selected ? UiTheme.Gold : Green };
            bool hot = t.Selectable && Hover is { } h && r.HasPoint(h);
            Fill(r, t.Selected ? new Color(c, 0.22f) : hot ? new Color(0.14f, 0.2f, 0.3f) : new Color(0.06f, 0.09f, 0.14f), 5);
            Frame(r, t.Selected ? c : hot ? Colors.White : new Color(c, 0.35f), null, 5, t.Selected || hot ? 2 : 1);
            Icon(t.Icon, r.Position.X + 8, r.Position.Y + 6, 22, c);
            string dist = CockpitMath.Distance(t.Distance, Loc.Culture);
            string turn = t.TurnDegrees > 3f ? $"{Mathf.RoundToInt(t.TurnDegrees)}°" : "";
            float right = r.End.X - 10f;
            Txt(dist, right - 70, r.Position.Y + 8, 16, c, HorizontalAlignment.Right, 70);
            Txt(turn, right - 70 - 58, r.Position.Y + 10, 13, UiTheme.Muted, HorizontalAlignment.Right, 54);
            Txt(Fit(t.Name, 15, r.Size.X - 44 - 150), r.Position.X + 38, r.Position.Y + 8, 15, t.Selected ? Colors.White : UiTheme.Text);
            if (t.Selectable) Buttons.Add(new CockpitButton(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, CockpitAction.SelectTarget, i));
        }
        float by = size.Y - 46f - 44f - 44f - 4f;
        float half = (size.X - 24f - 8f) / 2f;
        bool anySel = s.Targets.Count > 0;
        Button(new Rect2(12, by, half, 36), "<", null, CockpitAction.PrevTarget, 0, false, anySel, Green, 18);
        Button(new Rect2(12 + half + 8, by, half, 36), ">", null, CockpitAction.NextTarget, 0, false, anySel, Green, 18);
        by += 42f;
        float third = (size.X - 24f - 16f) / 3f;
        bool canAuto = s.Targets.Any(t => t.Selected && t.Kind == NavKind.Deposit) || s.Autopilot;
        Button(new Rect2(12, by, third, 38), s.Autopilot ? Loc.T("cp.nav.autoOn") : Loc.T("cp.nav.auto"), "tool:map", CockpitAction.ToggleAutopilot, 0, s.Autopilot, canAuto, Green, 13);
        Button(new Rect2(12 + third + 8, by, third, 38), s.DockAssist ? Loc.T("cp.nav.dockAssistOn") : Loc.T("cp.nav.dockAssist"), "tool:dock", CockpitAction.ToggleDockAssist, 0, s.DockAssist, s.DockNear || s.DockAssist, Cyan, 13);
        Button(new Rect2(12 + 2 * (third + 8), by, third, 38), s.ScanReady ? Loc.T("cp.nav.scan") : Loc.T("cp.nav.scanWait"), "tool:scan", CockpitAction.Scan, 0, !s.ScanReady, s.ScanReady, Amber, 13);
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
        string group = w.Hardpoints ? (w.Group == WeaponGroup.Guns ? Loc.T("cp.wpn.guns") : Loc.T("cp.wpn.laser")) : (w.Armed ? Loc.T("cp.wpn.guns") : Loc.T("cp.wpn.laser"));
        bool can = w.Hardpoints ? w.HasGun : w.ThreatActive;
        Button(r, $"{Loc.T("cp.wpn.group")}: {group}", "tool:weapons", w.Hardpoints ? CockpitAction.ToggleWeaponGroup : CockpitAction.ToggleArmed, 0, w.ActiveGroup == WeaponGroup.Guns, can, Red, 14);
    }

    // ---- weapons -------------------------------------------------------------------------------------------

    private void DrawWeapons(Vector2 size, Color accent)
    {
        var w = Snapshot.Weapons;
        float half = (size.X - 24f - 8f) / 2f;
        float y = 54f;
        DrawWeaponCard(new Rect2(12, y, half, 112), Loc.T("cp.wpn.laser"), "tool:mine", !w.Hardpoints || w.HasLaser, w.ActiveGroup == WeaponGroup.Laser,
            w.LaserFiring ? Loc.T("cp.wpn.firing") : w.Mining ? Loc.T("cp.wpn.mining") : Loc.T("cp.wpn.ready"), w.LaserFiring || w.Mining ? Green : UiTheme.Muted);
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

    private void DrawWeaponCard(Rect2 r, string name, string icon, bool fitted, bool active, string status, Color statusColor)
    {
        Color edge = active ? Red : new Color(1f, 1f, 1f, 0.2f);
        Fill(r, active ? new Color(Red, 0.16f) : new Color(0.06f, 0.08f, 0.12f), 6);
        Frame(r, fitted ? edge : new Color(0.25f, 0.27f, 0.33f), null, 6, active ? 2 : 1);
        Icon(icon, r.Position.X + 10, r.Position.Y + 10, 30, fitted ? (active ? Red : UiTheme.Text) : UiTheme.Dim);
        Txt(Fit(name, 14, r.Size.X - 20), r.Position.X + 10, r.Position.Y + 50, 14, fitted ? UiTheme.Text : UiTheme.Dim);
        Txt(Fit(fitted ? status : Loc.T("cp.wpn.missing"), 15, r.Size.X - 20), r.Position.X + 10, r.Position.Y + 76, 15, fitted ? statusColor : UiTheme.Dim);
    }
}
