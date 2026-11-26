using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

public enum PlanView { Orbit, Top, Section }

/// <summary>A ray in the ship's own space (module units: <see cref="StationGrid"/> sizes), for picking.</summary>
public readonly record struct PlanRay(Vector3 Origin, Vector3 Direction);

/// <summary>What a pick hit: the module that was hit and the free cell next to the face that was hit (where a new module would go), or a cell of the section plane.</summary>
public readonly record struct PlanPick((int Mx, int My, int Mz)? Module, (int Mx, int My, int Mz)? Place, Vector3 Normal);

/// <summary>
/// The camera of the blueprint editor's 3D plan as pure math (no engine): an orbit camera around a target with pan and zoom, a top-down plan view and a
/// section view (a layer of the ship, deck by deck), plus the ray and the picking the mouse needs. Space is the ship's own: x across, y up, z from the bow
/// (negative z) to the stern, in cells of <see cref="StationGrid"/>'s module size, the same space the module models are placed in. Y is up.
/// </summary>
public sealed class BlueprintCamera
{
    public const float MinDistance = 6f, MaxDistance = 400f;
    public const float MinPitch = -1.5f, MaxPitch = 1.5f;

    public Vector3 Target { get; private set; }
    public float Yaw { get; private set; } = 0.7f;
    public float Pitch { get; private set; } = 0.5f;
    public float Distance { get; private set; } = 40f;
    public float FovDegrees { get; set; } = 45f;
    public PlanView View { get; private set; } = PlanView.Orbit;

    /// <summary>The deck shown in the section view (a module layer, My).</summary>
    public int SectionLayer { get; private set; }

    private float _savedYaw, _savedPitch;

    /// <summary>Drag to orbit: yaw follows the horizontal motion, pitch the vertical one (clamped short of the poles). Ignored in the top and section views.</summary>
    public void Orbit(float dx, float dy)
    {
        if (View != PlanView.Orbit) { View = PlanView.Orbit; Yaw = _savedYaw; Pitch = _savedPitch; }
        Yaw = Wrap(Yaw + dx);
        Pitch = Math.Clamp(Pitch + dy, MinPitch, MaxPitch);
    }

    private static float Wrap(float a)
    {
        const float tau = MathF.PI * 2f;
        a %= tau;
        return a < 0f ? a + tau : a;
    }

    /// <summary>The direction from the target towards the camera.</summary>
    public Vector3 Back => View == PlanView.Orbit
        ? new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch))
        : Vector3.UnitY;

    public Vector3 Position => Target + Back * Distance;

    /// <summary>The screen's up in space: world up, or the bow direction in the top and section views (the bow is at the top of the screen).</summary>
    public Vector3 Up => View == PlanView.Orbit ? Vector3.UnitY : -Vector3.UnitZ;

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(-Back, Up));

    /// <summary>The screen-up vector orthogonal to the view direction (used for panning).</summary>
    public Vector3 ScreenUp => Vector3.Normalize(Vector3.Cross(Right, -Back));

    /// <summary>Pans the target in the screen plane; the speed is proportional to the distance so the scene follows the pointer.</summary>
    public void Pan(float dx, float dy)
    {
        float k = Distance * MathF.Tan(FovDegrees * MathF.PI / 360f) * 2f;
        Target += (-Right * dx + ScreenUp * dy) * k;
    }

    /// <summary>Zooms by wheel steps (positive closes in): each step changes the distance by about 12 percent.</summary>
    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Pow(0.88f, steps), MinDistance, MaxDistance);

    /// <summary>The top-down plan: looks straight down, bow at the top. A second call keeps it.</summary>
    public void SetTop()
    {
        if (View == PlanView.Orbit) { _savedYaw = Yaw; _savedPitch = Pitch; }
        View = PlanView.Top;
    }

    /// <summary>The deck-by-deck section: the plan view of one layer (modules above it are cut away).</summary>
    public void SetSection(int layer)
    {
        if (View == PlanView.Orbit) { _savedYaw = Yaw; _savedPitch = Pitch; }
        View = PlanView.Section;
        SectionLayer = layer;
        Target = new Vector3(Target.X, (layer + 0.5f) * StationGrid.ModuleY, Target.Z);
    }

    public void SetOrbit()
    {
        if (View == PlanView.Orbit) return;
        View = PlanView.Orbit;
        Yaw = _savedYaw; Pitch = _savedPitch;
    }

    /// <summary>Cycles orbit, top and section (the view-toggle key), the section on the layer given.</summary>
    public void CycleView(int sectionLayer)
    {
        switch (View)
        {
            case PlanView.Orbit: SetTop(); break;
            case PlanView.Top: SetSection(sectionLayer); break;
            default: SetOrbit(); break;
        }
    }

    /// <summary>Whether a module layer is drawn: everything in the orbit and top views, the layers up to the section deck in the section view.</summary>
    public bool LayerVisible(int my) => View != PlanView.Section || my <= SectionLayer;

    /// <summary>Centres the view on a box of cells and backs off so it fits (the distance never leaves its limits).</summary>
    public void Frame((int X, int Y, int Z) min, (int X, int Y, int Z) max)
    {
        var lo = new Vector3(min.X * StationGrid.ModuleX, min.Y * StationGrid.ModuleY, min.Z * StationGrid.ModuleZ);
        var hi = new Vector3((max.X + 1) * StationGrid.ModuleX, (max.Y + 1) * StationGrid.ModuleY, (max.Z + 1) * StationGrid.ModuleZ);
        Target = (lo + hi) / 2f;
        float radius = (hi - lo).Length() / 2f;
        Distance = Math.Clamp(radius / MathF.Sin(FovDegrees * MathF.PI / 360f) * 1.05f, MinDistance, MaxDistance);
    }

    /// <summary>Centres on one cell (a click on a validation entry) without changing the distance more than needed to see it.</summary>
    public void FrameCell((int Mx, int My, int Mz) cell)
    {
        Target = CellCentre(cell);
        Distance = Math.Clamp(Distance, MinDistance, 60f);
    }

    public static Vector3 CellCentre((int Mx, int My, int Mz) c) =>
        new((c.Mx + 0.5f) * StationGrid.ModuleX, (c.My + 0.5f) * StationGrid.ModuleY, (c.Mz + 0.5f) * StationGrid.ModuleZ);

    // ---- the ray under the pointer ------------------------------------------------------------------------------

    /// <summary>The ray through a pixel (0,0 top left) of a viewport of the given size.</summary>
    public PlanRay RayAt(float px, float py, float width, float height)
    {
        float aspect = width / Math.Max(1f, height);
        float t = MathF.Tan(FovDegrees * MathF.PI / 360f);
        float nx = (px / Math.Max(1f, width) * 2f - 1f) * t * aspect;
        float ny = (1f - py / Math.Max(1f, height) * 2f) * t;
        var dir = Vector3.Normalize(-Back + Right * nx + ScreenUp * ny);
        return new PlanRay(Position, dir);
    }

    /// <summary>The 2D screen point of a point in space, or null when it is behind the camera.</summary>
    public Vector2? Project(Vector3 point, float width, float height)
    {
        var rel = point - Position;
        float z = Vector3.Dot(rel, -Back);
        if (z <= 0.01f) return null;
        float t = MathF.Tan(FovDegrees * MathF.PI / 360f);
        float aspect = width / Math.Max(1f, height);
        float nx = Vector3.Dot(rel, Right) / z / (t * aspect);
        float ny = Vector3.Dot(rel, ScreenUp) / z / t;
        return new Vector2((nx + 1f) * 0.5f * width, (1f - ny) * 0.5f * height);
    }

    /// <summary>
    /// What the ray hits among the modules (the nearest box, and the free cell next to the face hit, where a new module goes). A module on a layer the
    /// section view cuts away is not hit. When nothing is hit, the ray is taken to the plane of the section deck (or the plane through the target in the other
    /// views) so an empty cell can be clicked.
    /// </summary>
    public PlanPick Pick(PlanRay ray, IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> modules)
    {
        float best = float.MaxValue;
        (int, int, int)? hit = null;
        var normal = Vector3.Zero;
        foreach (var key in modules.Keys)
        {
            if (!LayerVisible(key.My)) continue;
            if (View == PlanView.Section && key.My != SectionLayer) continue; // the plan shows the deck itself, the decks below are not clickable
            var lo = new Vector3(key.Mx * StationGrid.ModuleX, key.My * StationGrid.ModuleY, key.Mz * StationGrid.ModuleZ);
            var hi = lo + new Vector3(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ);
            if (RayBox(ray, lo, hi, out float t, out var n) && t < best) { best = t; hit = (key.Mx, key.My, key.Mz); normal = n; }
        }
        if (hit is { } h)
        {
            var place = (h.Item1 + (int)normal.X, h.Item2 + (int)normal.Y, h.Item3 + (int)normal.Z);
            return new PlanPick(h, modules.ContainsKey(place) ? null : place, normal);
        }
        // An empty cell: the plane of the section deck (the cell at its height under the pointer), or the plane through the target.
        float planeY = View == PlanView.Section ? SectionLayer * StationGrid.ModuleY + StationGrid.ModuleY * 0.5f : Target.Y;
        if (MathF.Abs(ray.Direction.Y) > 1e-5f)
        {
            float t = (planeY - ray.Origin.Y) / ray.Direction.Y;
            if (t > 0f)
            {
                var p = ray.Origin + ray.Direction * t;
                var cell = BlueprintDraft.SnapToCell(p.X, p.Y, p.Z);
                if (View == PlanView.Section) cell = (cell.Mx, SectionLayer, cell.Mz);
                return new PlanPick(null, cell, Vector3.UnitY);
            }
        }
        return new PlanPick(null, null, Vector3.Zero);
    }

    /// <summary>Slab test of a ray against a box; <paramref name="normal"/> is the outward normal of the face the ray enters through.</summary>
    public static bool RayBox(PlanRay ray, Vector3 lo, Vector3 hi, out float t, out Vector3 normal)
    {
        t = 0f; normal = Vector3.Zero;
        float tMin = 0f, tMax = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = Get(ray.Origin, axis), d = Get(ray.Direction, axis), a = Get(lo, axis), b = Get(hi, axis);
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < a || o > b) return false;
                continue;
            }
            float t1 = (a - o) / d, t2 = (b - o) / d;
            float sign = -1f;
            if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1f; }
            if (t1 > tMin) { tMin = t1; normal = Axis(axis) * sign; }
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        t = tMin;
        return true;
    }

    private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static Vector3 Axis(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
}
