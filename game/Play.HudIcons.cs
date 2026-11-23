using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Icon ids for what the HUD names: the tool in hand and the kinds of things aimed at. Small and stable on purpose.</summary>
public partial class Play
{
    /// <summary>The icon of a marker that gave none: the callers that name a station, a port, ice or drones by their label (the structure helpers pass only a label).</summary>
    private string MarkerIcon(string label) =>
        label == L("mark.station") || label == L("mark.base") ? "status:station"
        : label == L("mark.dockPort") ? "status:port"
        : label == L("mark.ice") ? "item:core:ice"
        : label == L("mark.droneTarget") || label == L("mark.drones") ? "status:drone"
        : "";

    /// <summary>The icon id of a build menu entry.</summary>
    private string EntryIcon(BuildEntry e) => e.Kind switch
    {
        BuildEntryKind.Belt => IconCatalog.ForBelt(e.Tier),
        BuildEntryKind.Machine => IconCatalog.ForMachine(_content.Machines[e.Machine].Id),
        BuildEntryKind.Station => "tool:found",
        BuildEntryKind.LegacyModule => "module:core",
        _ => IconCatalog.ForModule(e.Module ?? ModuleKind.Core),
    };

    /// <summary>The icon id of a build palette entry.</summary>
    private string ToolIcon(BuildItem item) =>
        item.IsStation ? "tool:found"
        : item.IsModule ? (_structuresOn ? IconCatalog.ForModule(_moduleKind) : "module:core")
        : item.IsBelt ? IconCatalog.ForBelt(item.Tier)
        : IconCatalog.ForMachine(_content.Machines[item.Machine].Id);
}
