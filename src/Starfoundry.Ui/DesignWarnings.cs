using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Why an attachment of a ship design is a problem for the view.</summary>
public enum ViewWarning
{
    /// <summary>A pod stands on a cockpit: the roof glass shows plating.</summary>
    BlocksCanopy,
    /// <summary>A pod stands in front of a cockpit: the windscreen looks at its back.</summary>
    BlocksWindscreen,
    /// <summary>A pod or a module stands against the side of a cockpit: its flank windows look at a wall.</summary>
    BlocksFlankWindows,
}

/// <summary>Checks of a ship design (the modules staged in the designer) that depend on what the model draws, not on the rules. Engine-free.</summary>
public static class DesignWarnings
{
    /// <summary>
    /// The attachments that block the pilot's view: for every cockpit, a pod above it (the canopy), in front of it (the windscreen) or at its side (the flank
    /// glazing), as (cockpit, attachment, reason). Wings are drawn under the window sills (<see cref="HullProfile.WingY"/>) and never block a window, so they
    /// do not appear here. The starter ship's gun deck above the cockpit is the usual example; the model gives it a glazed skylight, so it is listed but
    /// harmless (<see cref="HullProfile.SkylightKinds"/>).
    /// </summary>
    public static List<((int, int, int) Cockpit, (int, int, int) Attachment, ViewWarning Why)> ViewBlockers(IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> modules)
    {
        var list = new List<((int, int, int), (int, int, int), ViewWarning)>();
        foreach (var (key, kind) in modules)
        {
            if (kind != ModuleKind.Cockpit) continue;
            void Check((int, int, int) at, ViewWarning why)
            {
                if (modules.TryGetValue(at, out var k) && HullProfile.IsPod(k) && !(why == ViewWarning.BlocksCanopy && HullProfile.SkylightKinds(k))) list.Add((key, at, why));
            }
            Check((key.Mx, key.My + 1, key.Mz), ViewWarning.BlocksCanopy);
            Check((key.Mx, key.My, key.Mz - 1), ViewWarning.BlocksWindscreen);
            Check((key.Mx - 1, key.My, key.Mz), ViewWarning.BlocksFlankWindows);
            Check((key.Mx + 1, key.My, key.Mz), ViewWarning.BlocksFlankWindows);
        }
        return list;
    }
}
