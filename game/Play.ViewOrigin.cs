using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The floating origin of the view (ADR 0053, world scale stage 1): the rebase step, the <c>--view-origin=x,y,z</c> debug start and the headless <c>--view-origin-check</c>.
/// Nothing here touches the simulation: the origin is a client-only number (<see cref="View"/>), the world stays in integer units.
/// <para>
/// What a rebase does to each part of the scene (decided node by node; "derived" means the value is rewritten from the simulation every frame, so there is nothing to shift):
/// </para>
/// <list type="bullet">
/// <item><description><b>Camera</b>: shifted here (so input read before the frame's own placement sees a consistent camera); <c>PlaceCamera</c> derives it again every frame.</description></item>
/// <item><description><b>World0</b> (the frame of structure 0, the legacy grid, whose children keep absolute cell coordinates): re-placed at minus the origin, its children untouched.</description></item>
/// <item><description><b>Structure roots</b> of the station view (the ship, stations): derived every frame from the simulation (<c>UpdateStructureRoots</c>, <c>StationView.Sync</c>); not shifted. Their machine, item and drone children are local to them.</description></item>
/// <item><description><b>Obstacle layer</b>, <b>enemy root</b>, <b>drone root</b>, <b>rock field</b> sector roots, <b>cluster glow</b> quads, <b>tracers</b>, <b>pickups</b>, <b>laser beams</b>, <b>ship</b> node and range discs: the root sits at the origin of the view and the children are rewritten in view coordinates every frame; a layer root must never be shifted (it would shift twice).</description></item>
/// <item><description><b>Deposit nodes</b> (placed once), <b>sparks</b>, <b>explosions</b> and <b>shot beams</b> (placed once, short lived): members of the group <see cref="View.WorldGroup"/>, shifted by the exact integer delta.</description></item>
/// <item><description><b>Cached view vectors</b> (<c>_waypoint</c>, <c>_lastBasePos</c>, <c>_overviewTarget</c>, <c>_demoFocus</c>, <c>_shotCam</c>, the delayed bursts): shifted by the same delta. Cell indices and structure-local vectors are not view values and stay.</description></item>
/// <item><description><b>Ambience</b> (sky, sun glare, dust, haze, streaks): camera relative; only the stored positions behind the speed of the ship shift and the streak lattice keeps its world phase (<see cref="Ambience.Rebase"/>).</description></item>
/// <item><description><b>Markers, HUD, cockpit displays</b>: projected or derived every frame; nothing to shift.</description></item>
/// </list>
/// A burst of particles (<c>LocalCoords = false</c>) keeps its particles in world space for about a second, so a rebase waits until no burst is alive (the margin is 30,000 cells).
/// </summary>
public partial class Play
{
    // ---- the rebase step ---------------------------------------------------------------------------------------------

    /// <summary>Between frames: when the ship is more than the threshold away from the view origin, moves the origin to it (snapped) and shifts what is placed once.</summary>
    private void RebaseViewIfNeeded()
    {
        if (!Net.HasPlayer(Player) || _bursts.Count > 0) return;
        var p = Net.GetPlayerInfo(Player);
        if (!View.Origin.NeedsRebase(p.X, p.Y, p.Z)) return;
        RebaseViewTo(p.X, p.Y, p.Z);
    }

    /// <summary>Moves the view origin to the snapped position of a ship (simulation units) and shifts the scene by the exact integer delta; false when the origin did not change.</summary>
    private bool RebaseViewTo(long shipX, long shipY, long shipZ)
    {
        var d = View.Rebase(shipX, shipY, shipZ);
        if (d == (0L, 0L, 0L)) return false;
        ShiftScene(d);
        return true;
    }

    private void ShiftScene((long X, long Y, long Z) d)
    {
        // The legacy frame sits at minus the origin; the camera is shifted so a read before this frame's placement agrees with the new origin.
        World0.Position = View.FromCells(0f, 0f, 0f);
        if (_camera is not null) View.Shift(_camera, d);
        foreach (var node in GetTree().GetNodesInGroup(View.WorldGroup))
            if (node is Node3D n3 && n3.IsInsideTree()) View.Shift(n3, d);

        _overviewTarget = View.Shift(_overviewTarget, d);
        _demoFocus = View.Shift(_demoFocus, d);
        _lastBasePos = View.Shift(_lastBasePos, d);
        if (_waypoint is { } wp) _waypoint = View.Shift(wp, d);
        if (_shotCam is { } sc) _shotCam = (View.Shift(sc.From, d), View.Shift(sc.At, d));
        for (int i = 0; i < _pendingBursts.Count; i++)
        {
            var e = _pendingBursts[i];
            _pendingBursts[i] = (e.Delay, View.Shift(e.At, d), e.Size, e.Ring);
        }
        Ambience.Rebase(d);
    }

    // ---- --view-origin=x,y,z -----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>--view-origin=x,y,z</c> (cells, also <c>--view-origin x,y,z</c>): starts the view at that origin (snapped to the multiple of 1,000 below) and, when the run has a ship
    /// structure (the campaign, a sandbox, free flight), puts the ship there, so a far-from-home world is rendered the way it is after a long flight. Called before the scene is built.
    /// </summary>
    private void ApplyViewOriginFlag(string[] args)
    {
        if (!LaunchFlags.TryViewOrigin(args, out var c)) return;
        View.Set(c.X, c.Y, c.Z);
        if (ShipStruct >= 0) TeleportShipToCells(c.X, c.Y, c.Z);
        else GD.Print("VIEWORIGIN the run has no ship structure: only the view origin moved");
        GD.Print($"VIEWORIGIN origin=({View.OriginX},{View.OriginY},{View.OriginZ}) ship=({c.X},{c.Y},{c.Z}) cells");
    }

    /// <summary>Puts the ship structure at a world position in whole cells (the pilot goes with it).</summary>
    private void TeleportShipToCells(long x, long y, long z)
    {
        long cu = Units.CellUnits;
        Net.SetStructureOrigin(ShipStruct, x * cu, y * cu, z * cu);
    }

    // ---- --view-origin-check -----------------------------------------------------------------------------------------

    private bool _viewCheckOn;
    private int _vocPhase, _vocWait, _vocPass, _vocFail;
    private Node3D? _vocProbe;
    private (long X, long Y, long Z) _vocProbeCells, _vocOriginBefore, _vocTarget;
    private Vector3 _vocProbeBefore;

    private void ViewOriginCheckSetup(string[] args)
    {
        _viewCheckOn = args.Contains(LaunchFlags.ViewOriginCheckFlag);
        if (_viewCheckOn) _wantCapture = false;
    }

    private void VocCheck(string name, bool ok, string detail = "")
    {
        if (ok) _vocPass++; else _vocFail++;
        GD.Print($"VIEWORIGINCHECK {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " - " + detail : "")}");
    }

    private void VocFinish()
    {
        GD.Print($"VIEWORIGINCHECK done pass={_vocPass} fail={_vocFail}");
        GetTree().Quit(_vocFail > 0 ? 1 : 0);
    }

    /// <summary>The farthest any node of the scene is from the view origin, in cells (nodes outside the tree and the 2D layers do not count).</summary>
    private float FarthestNode(Node from, out string name)
    {
        float worst = 0f;
        string where = "";
        void Visit(Node n)
        {
            if (n is Node3D n3 && n3.IsInsideTree())
            {
                float d = n3.GlobalPosition.Length();
                if (d > worst) { worst = d; where = $"{n3.GetPath()} ({n3.GetType().Name}, parent {n3.GetParent()?.GetType().Name}, group {(n3.IsInGroup(View.WorldGroup) ? "world" : "-")}, local {n3.Position}, visible {n3.Visible}, scale {n3.Scale}, mesh {(n3 as MeshInstance3D)?.Mesh?.GetType().Name}, tracer {(_tracers.Contains(n3) ? "yes" : "no")}, ship {(n3 == _ship ? "yes" : "no")})"; }
            }
            foreach (var c in n.GetChildren()) Visit(c);
        }
        Visit(from);
        name = where;
        return worst;
    }

    /// <summary>Puts the ship at a far spot and a probe node (a long-lived world-space node, as a deposit is) beside it; the rebase then happens (or not) on its own, in the frames that follow.</summary>
    private void VocBegin((long X, long Y, long Z) ship)
    {
        _vocTarget = ship;
        _vocOriginBefore = View.Origin.Cells;
        _vocProbeCells = (ship.X + 25, ship.Y + 3, ship.Z - 7);
        _vocProbe?.QueueFree();
        _vocProbe = new Node3D { Name = "ViewProbe", Position = View.FromCells(_vocProbeCells.X, _vocProbeCells.Y, _vocProbeCells.Z) };
        _vocProbe.AddToGroup(View.WorldGroup);
        AddChild(_vocProbe);
        _vocProbeBefore = _vocProbe.Position;
        _overviewTarget = _vocProbeBefore; // a cached view vector: it must shift by the same delta
        TeleportShipToCells(ship.X, ship.Y, ship.Z);
        _vocWait = 0;
    }

    private void ViewOriginCheckFrame()
    {
        if (!_viewCheckOn) return;
        _vocWait++;
        switch (_vocPhase)
        {
            case 0:
                if (_vocWait < 5) return;
                if (ShipStruct < 0) { VocCheck("setup: the run has a ship structure", false); VocFinish(); return; }
                VocCheck("start: the origin is (0, 0, 0)", View.Origin.Cells == (0L, 0L, 0L));
                VocBegin((30000, 500, -25000));
                _vocPhase = 1;
                return;
            case 1:
                VocRound("far east (a rebase is due)", expectRebase: true);
                return;
            case 2:
                VocBegin((-35000, -400, 15000));
                _vocPhase = 3;
                return;
            case 3:
                VocRound("far west, a negative delta", expectRebase: true);
                return;
            case 4:
                VocBegin((View.Origin.Cells.X + 5000, View.Origin.Cells.Y, View.Origin.Cells.Z)); // well inside the threshold
                _vocPhase = 5;
                return;
            case 5:
                VocRound("5,000 cells from the origin (no rebase)", expectRebase: false);
                return;
            default:
                VocFinish();
                return;
        }
    }

    private void VocRound(string label, bool expectRebase)
    {
        bool moved = View.Origin.Cells != _vocOriginBefore;
        if (expectRebase && !moved && _vocWait < 40) return; // the rebase comes with the next frames
        if (!expectRebase && _vocWait < 12) return;
        var o = View.Origin.Cells;
        var delta = (X: o.X - _vocOriginBefore.X, Y: o.Y - _vocOriginBefore.Y, Z: o.Z - _vocOriginBefore.Z);
        VocCheck($"{label}: the origin {(expectRebase ? "moved" : "stayed")}", moved == expectRebase, $"origin ({o.X},{o.Y},{o.Z})");
        if (expectRebase)
        {
            var snapped = (ViewOrigin.Snap(_vocTarget.X), ViewOrigin.Snap(_vocTarget.Y), ViewOrigin.Snap(_vocTarget.Z));
            VocCheck($"{label}: the origin is the snapped ship position", o == snapped, $"({o.X},{o.Y},{o.Z}) wanted ({snapped.Item1},{snapped.Item2},{snapped.Item3})");
            VocCheck($"{label}: the origin is a multiple of {ViewOrigin.SnapCells}", o.X % ViewOrigin.SnapCells == 0 && o.Y % ViewOrigin.SnapCells == 0 && o.Z % ViewOrigin.SnapCells == 0);
        }
        var after = _vocProbe!.Position;
        var shift = new Vector3(delta.X, delta.Y, delta.Z);
        VocCheck($"{label}: a long-lived node moved by exactly the delta", (_vocProbeBefore - after) == shift, $"before {_vocProbeBefore} after {after} delta {shift}");
        var fresh = View.FromCells(_vocProbeCells.X, _vocProbeCells.Y, _vocProbeCells.Z);
        VocCheck($"{label}: that node is where the simulation puts it", after == fresh, $"{after} vs {fresh}");
        var back = View.ToUnits(after);
        long cu = Units.CellUnits;
        VocCheck($"{label}: view to units round trip is exact", back == (_vocProbeCells.X * cu, _vocProbeCells.Y * cu, _vocProbeCells.Z * cu), $"{back}");
        var cachedAfter = _overviewTarget;
        VocCheck($"{label}: a cached view vector moved by exactly the delta", (_vocProbeBefore - cachedAfter) == shift, $"{cachedAfter}");
        VocCheck($"{label}: the legacy frame sits at minus the origin", World0.Position == new Vector3(-o.X, -o.Y, -o.Z), $"{World0.Position}");
        float far = FarthestNode(this, out string name);
        VocCheck($"{label}: no node is farther than 50,000 cells from the view origin", far <= 50000f, $"{far:F0} at {name}");
        float cam = _camera.GlobalPosition.Length();
        VocCheck($"{label}: the camera stays near the view origin", cam <= ViewOrigin.RebaseCells + 1000f, $"{cam:F0}");
        _vocPhase++;
        _vocWait = 0;
    }
}
