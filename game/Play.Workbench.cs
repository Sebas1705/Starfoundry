using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The ship's workbench in the world (ADR 0031): the model of the micro-refinery that <c>F</c> drives, standing on the deck of a module of the
/// player's ship, the aiming test against it and its crosshair card (name, recipe, progress, input, output, "F: change recipe"). The workbench
/// is not a machine of the simulation, so none of this is in the sim: the model hangs under the ship's structure root, in the module chosen by
/// <see cref="WorkbenchSpot.Pick"/>, and is animated from <c>PlayerInfo</c> (recipe, crafting) only while it works.
/// </summary>
public partial class Play
{
    private Node3D? _bench;
    private BenchSpot? _benchSpot;
    private int _benchStruct = -1, _benchVersion = -1;
    private PanelContainer? _benchCard;
    private Label _benchTitle = null!, _benchRecipe = null!, _benchState = null!, _benchIn = null!, _benchOut = null!, _benchHint = null!;
    private ProgressBar _benchBar = null!;

    /// <summary>The longest reach, in cells, at which the crosshair picks the workbench.</summary>
    private const float BenchAimReach = 9f;

    /// <summary>The box the crosshair picks, in the local cells of the ship structure, or null when the ship has no workbench to show.</summary>
    private (Vector3 Min, Vector3 Size)? BenchBox()
    {
        if (_bench is null || _benchSpot is not { } s) return null;
        var fp = WorkbenchSpot.Footprint(s);
        var origin = ModuleMin(s.Module.Mx, s.Module.My, s.Module.Mz);
        return (origin + new Vector3(fp.MinX, 0f, fp.MinZ), new Vector3(fp.MaxX - fp.MinX, 1.5f, fp.MaxZ - fp.MinZ));
    }

    /// <summary>Whether the crosshair points at the workbench within reach (the ray in the ship's own frame, like machines).</summary>
    private bool AimingAtBench()
    {
        int ship = ShipStruct;
        if (ship < 0 || BenchBox() is not { } box) return false;
        var (lo, ld) = AimInStruct(ship);
        return RayHitsAabb(lo, ld, box.Min, box.Size, out float t) && t <= BenchAimReach;
    }

    /// <summary>Per frame, after the structure roots moved: keeps the model in the right module, animates it and updates its card.</summary>
    private void UpdateWorkbench()
    {
        int ship = ShipStruct;
        var root = ship >= 0 && Net.HasPlayer(Player) ? _stationView.StructureRoot(ship) : null;
        if (root is null)
        {
            DropBench();
            return;
        }
        if (_bench is null || !IsInstanceValid(_bench) || _bench.GetParent() != root || _benchStruct != ship || _benchVersion != Net.StructureVersion)
        {
            DropBench();
            _benchStruct = ship;
            _benchVersion = Net.StructureVersion;
            _benchSpot = WorkbenchSpot.Pick(Net.GetStructure(ship).Modules);
            if (_benchSpot is { } spot)
            {
                _bench = Models.Workbench(spot.Wall == BenchWall.Right);
                _bench.Position = ModuleMin(spot.Module.Mx, spot.Module.My, spot.Module.Mz) + new Vector3(spot.ShiftX, 0f, 0f);
                root.AddChild(_bench);
            }
        }
        if (_bench is null) return;
        var pi = Net.GetPlayerInfo(Player);
        string? id = pi.ShipRecipe >= 0 ? _content.Recipes[pi.ShipRecipe].Id : null;
        Models.AnimateBench(_bench, _animTime, WorkbenchSpot.KindOf(id), pi.ShipCrafting);
        UpdateBenchCard(pi, id);
    }

    private void DropBench()
    {
        if (_bench is not null && IsInstanceValid(_bench)) _bench.QueueFree();
        _bench = null;
        _benchSpot = null;
        _benchStruct = -1;
        _benchVersion = -1;
        if (_benchCard is not null) _benchCard.Visible = false;
    }

    private void BuildBenchCard()
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        Label Text(int size, Color? c = null)
        {
            var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeColorOverride("font_color", c ?? Colors.White);
            l.AddThemeFontSizeOverride("font_size", size);
            box.AddChild(l);
            return l;
        }
        _benchTitle = Text(16, new Color(1f, 0.9f, 0.5f));
        _benchRecipe = Text(14);
        _benchState = Text(13, new Color(0.75f, 0.8f, 0.9f));
        _benchBar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(200, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(_benchBar);
        _benchIn = Text(13);
        _benchOut = Text(13);
        _benchHint = Text(13, new Color(0.55f, 0.85f, 1f));
        _benchCard = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.06f, 0.10f, 0.78f), BorderColor = new Color(0.45f, 0.58f, 0.82f, 0.45f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(10f);
        _benchCard.AddThemeStyleboxOverride("panel", style);
        _benchCard.AddChild(box);
        _uiRoot.AddChild(_benchCard);
    }

    private string BenchStacks(ItemStack[] stacks) => string.Join(", ", stacks.Select(s => $"{s.Count} {Loc.Name("item", _content.Items[s.Item].Id)}"));

    private void UpdateBenchCard(PlayerInfo pi, string? recipeId)
    {
        if (_benchCard is null) BuildBenchCard();
        bool show = !_demo && (_mouseLook || _walkShot) && _tool == ToolMode.Pointer && !PanelOpen() && _hover.Kind is HoverKind.None or HoverKind.Ground && AimingAtBench();
        _benchCard!.Visible = show;
        if (!show) return;

        var title = Loc.Name("machine", "core:micro_refinery");
        _benchTitle.Text = char.ToUpperInvariant(title[0]) + title[1..];
        var kind = WorkbenchSpot.KindOf(recipeId);
        _benchHint.Text = L("bench.card.hint");
        if (pi.ShipRecipe < 0 || kind == BenchKind.Off)
        {
            _benchRecipe.Text = L("bench.card.off");
            _benchState.Text = "";
            _benchState.Visible = false;
            _benchBar.Visible = false;
            _benchIn.Visible = _benchOut.Visible = false;
        }
        else
        {
            var r = _content.Recipes[pi.ShipRecipe];
            int total = r.TimeTicks * PlayerRules.ShipCraftMultiplier;
            float progress = WorkbenchSpot.Progress(pi.ShipCrafting, pi.ShipRemaining, total);
            bool hasInputs = r.Inputs.All(i => Net.HoldCount(Player, i.Item) >= i.Count);
            _benchRecipe.Text = L("bench.card.recipe", L("bench." + r.Id));
            _benchState.Visible = true;
            _benchState.Text = pi.ShipCrafting ? L("bench.card.working", (int)MathF.Round(progress * 100f))
                : hasInputs ? L("bench.card.noRoom") : L("bench.card.waiting");
            _benchState.AddThemeColorOverride("font_color", pi.ShipCrafting ? new Color(0.45f, 0.9f, 0.55f) : new Color(1f, 0.78f, 0.3f));
            _benchBar.Visible = true;
            _benchBar.Value = progress;
            _benchBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Models.BenchColour(kind) });
            _benchBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.12f, 0.14f, 0.2f) });
            _benchIn.Visible = _benchOut.Visible = true;
            string held = string.Join(", ", r.Inputs.Select(i => $"{Net.HoldCount(Player, i.Item)} {Loc.Name("item", _content.Items[i.Item].Id)}"));
            _benchIn.Text = L("bench.card.in", BenchStacks(r.Inputs), held);
            _benchOut.Text = L("bench.card.out", BenchStacks(r.Outputs));
        }

        var size = GetViewport().GetVisibleRect().Size;
        _benchCard.ResetSize();
        var ts = _benchCard.GetCombinedMinimumSize();
        _benchCard.Position = new Vector2(Math.Clamp(size.X / 2f + 28f, 8f, Math.Max(8f, size.X - ts.X - 8f)), Math.Clamp(size.Y / 2f - ts.Y / 2f, 8f, Math.Max(8f, size.Y - ts.Y - 8f)));
    }

}
