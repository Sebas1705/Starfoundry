using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Maps the rejects of the station rules (and the generic ones they share) to localisation keys.</summary>
public static class RejectTexts
{
    /// <summary>The <c>toast.reject.*</c> key for a reject, or null when the reject has no dedicated text.</summary>
    public static string? Key(Reject r) => r switch
    {
        Reject.OutsideStation => "toast.reject.outsideStation",
        Reject.NotAdjacent => "toast.reject.notAdjacent",
        Reject.ModuleOccupied => "toast.reject.moduleOccupied",
        Reject.NotAllowedHere => "toast.reject.notAllowedHere",
        Reject.Overlap => "toast.reject.overlap",
        Reject.OutOfBounds => "toast.reject.outOfBounds",
        Reject.NotEnoughItems => "toast.reject.notEnoughItems",
        Reject.KindNotAllowed => "toast.reject.kindNotAllowed",
        Reject.NoSuchStructure => "toast.reject.noSuchStructure",
        // Reject.NoCockpitOrEngine -> "toast.reject.noCockpitOrEngine" is wired by the integrator once the enum value exists.
        _ => null,
    };
}
