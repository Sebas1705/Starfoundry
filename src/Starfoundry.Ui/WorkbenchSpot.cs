using System;
using System.Collections.Generic;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Which wall of its module the ship's workbench stands against.</summary>
public enum BenchWall { Left, Right }

/// <summary>What the workbench is making, for its lamp and its card.</summary>
public enum BenchKind { Off, Ingot, Plate, Ammo }

/// <summary>Where the ship's workbench stands: the module of the ship that holds it, the wall it leans on and how far it stands off that wall (ADR 0031). Client-only.</summary>
public readonly record struct BenchSpot((int Mx, int My, int Mz) Module, BenchWall Wall, float Inset = 0f)
{
    /// <summary>The move of the bench model along X from its wall position (towards the module's middle), to clear what the module already has against the wall.</summary>
    public float ShiftX => Wall == BenchWall.Left ? Inset : -Inset;
}

/// <summary>
/// Placement and numbers of the workbench model. The workbench is not a machine of the simulation (it is the ship's crafting table, driven by
/// <c>SetShipRecipe</c>), so the client decides where to draw it: in the first hull module of the ship, else in a cargo module (beside its
/// shelves), on the deck against a side wall, at the end towards the cockpit. A fabrication bay never holds it: its floor is where the player builds.
/// Pure and deterministic; the model and the aiming test live in the game.
/// </summary>
public static class WorkbenchSpot
{
    /// <summary>The bench box in the local cells of its module when it leans on the left wall: x from the wall, z along the aisle at the front end (towards the cockpit), y up from the deck.</summary>
    public const float MinX = 1.15f, MaxX = 2.30f, MinZ = 0.55f, MaxZ = 2.55f, Height = 0.98f;

    /// <summary>How far a cargo module's shelves push the bench off the wall (half a cell: the plating grid still lines up).</summary>
    public const float CargoInset = 0.5f;

    /// <summary>Module kinds that may hold the bench, best first. The others have no room for it or are floors to build on.</summary>
    private static readonly ModuleKind[] Preference = { ModuleKind.Hull, ModuleKind.Cargo };

    /// <summary>
    /// The module that holds the bench, or null when the ship has none that fits. Among modules of the best kind the lowest deck and then
    /// the one nearest the front wins (smallest Z, then X), so the choice is stable while the ship grows.
    /// </summary>
    public static BenchSpot? Pick(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules)
    {
        var all = new List<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>>(modules);
        var has = new HashSet<(int, int, int)>();
        foreach (var m in all) has.Add(m.Key);
        foreach (var kind in Preference)
        {
            (int Mx, int My, int Mz)? best = null;
            foreach (var m in all)
            {
                if (m.Value != kind) continue;
                var k = m.Key;
                // a module with a doorway on both sides has no wall to lean on
                if (has.Contains((k.Mx - 1, k.My, k.Mz)) && has.Contains((k.Mx + 1, k.My, k.Mz))) continue;
                if (best is null || Compare(k, best.Value) < 0) best = k;
            }
            if (best is not { } b) continue;
            if (kind == ModuleKind.Cargo) return new BenchSpot(b, BenchWall.Left, CargoInset); // the crates stand on the right: the shelves' side is the free one
            return new BenchSpot(b, has.Contains((b.Mx - 1, b.My, b.Mz)) ? BenchWall.Right : BenchWall.Left);
        }
        return null;
    }

    private static int Compare((int Mx, int My, int Mz) a, (int Mx, int My, int Mz) b)
    {
        int c = a.My.CompareTo(b.My);
        if (c != 0) return c;
        c = a.Mz.CompareTo(b.Mz);
        return c != 0 ? c : a.Mx.CompareTo(b.Mx);
    }

    /// <summary>The bench footprint in the local cells of its module, whichever wall it leans on (the right wall mirrors the left across the module's middle and keeps the front end), moved by the spot's inset.</summary>
    public static (float MinX, float MinZ, float MaxX, float MaxZ) Footprint(BenchSpot spot, float moduleX = StationGrid.ModuleX) => Footprint(spot.Wall, spot.ShiftX, moduleX);

    /// <summary>The footprint against <paramref name="wall"/>, moved by <paramref name="shiftX"/> along X.</summary>
    public static (float MinX, float MinZ, float MaxX, float MaxZ) Footprint(BenchWall wall, float shiftX = 0f, float moduleX = StationGrid.ModuleX) =>
        wall == BenchWall.Left
            ? (MinX + shiftX, MinZ, MaxX + shiftX, MaxZ)
            : (moduleX - MaxX + shiftX, MinZ, moduleX - MinX + shiftX, MaxZ);

    /// <summary>The kind the workbench makes, from the content id of its recipe (empty or unknown means off).</summary>
    public static BenchKind KindOf(string? recipeId) => recipeId switch
    {
        "core:ship_smelt" => BenchKind.Ingot,
        "core:ship_press" => BenchKind.Plate,
        "core:ship_ammo" => BenchKind.Ammo,
        _ => BenchKind.Off,
    };

    /// <summary>Progress of the craft in progress, 0 to 1: how much of <paramref name="totalTicks"/> has gone by with <paramref name="remainingTicks"/> left. 0 when nothing is crafting.</summary>
    public static float Progress(bool crafting, int remainingTicks, int totalTicks)
    {
        if (!crafting || totalTicks <= 0) return 0f;
        return Math.Clamp(1f - (float)remainingTicks / totalTicks, 0f, 1f);
    }
}
