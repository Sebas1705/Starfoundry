namespace Starfoundry.Ui;

/// <summary>The tabs of the ship computer (Tab), in the order of their number keys.</summary>
public enum ComputerTab
{
    Map,
    Research,
    Inventory,
    Build,
    Info,
    Designer,
    Settings,
    Systems,
}

/// <summary>What the computer can show right now: the map needs a galaxy and the designer a docked shipyard.</summary>
public readonly record struct ComputerAvailability(bool HasGalaxy, bool ShipyardDocked)
{
    public bool Has(ComputerTab tab) => tab switch
    {
        ComputerTab.Map => HasGalaxy,
        ComputerTab.Designer => ShipyardDocked,
        _ => true,
    };
}

/// <summary>The tab strip of the ship computer: names, icons, number keys and stepping with Q and E. Engine-free.</summary>
public static class ShipComputerTabs
{
    public static IReadOnlyList<ComputerTab> All { get; } = Enum.GetValues<ComputerTab>();

    public static string NameKey(ComputerTab tab) => "computer.tab." + tab.ToString().ToLowerInvariant();

    /// <summary>The <c>tool:</c> icon of a tab.</summary>
    public static string Icon(ComputerTab tab) => tab switch
    {
        ComputerTab.Map => "tool:map",
        ComputerTab.Research => "tool:research",
        ComputerTab.Inventory => "tool:inventory",
        ComputerTab.Build => "tool:build",
        ComputerTab.Info => "tool:info",
        ComputerTab.Designer => "tool:designer",
        ComputerTab.Settings => "tool:settings",
        _ => "tool:systems",
    };

    /// <summary>The Loc key of the hint shown on a tab that cannot be opened now, or null when it can.</summary>
    public static string? WhyOff(ComputerTab tab, ComputerAvailability a) => a.Has(tab) ? null : tab == ComputerTab.Map ? "computer.tab.mapOff" : "computer.tab.designerOff";

    /// <summary>The tab with this number (1 is the first), or null when the number is out of range or the tab is off.</summary>
    public static ComputerTab? FromNumber(int number, ComputerAvailability a) =>
        number >= 1 && number <= All.Count && a.Has(All[number - 1]) ? All[number - 1] : null;

    /// <summary>The next tab in a direction, skipping the ones that are off and wrapping round.</summary>
    public static ComputerTab Step(ComputerTab from, int step, ComputerAvailability a)
    {
        int n = All.Count, i = (int)from;
        for (int k = 0; k < n; k++)
        {
            i = ((i + step) % n + n) % n;
            if (a.Has(All[i])) return All[i];
        }
        return from;
    }
}
