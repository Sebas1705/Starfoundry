namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the guided steps added with the new systems (docking, salvage, the shipyard), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> MissionsTable = new()
    {
        ["obj.dock"] = ("Atraca en tu estación", "Dock at your station"),
        ["obj.dock.hint"] = ("Acércate despacio a un puerto libre de la estación (lo marca un anillo de luces) y pulsa U: la nave se acopla. Con , descargas la bodega en el almacén de la estación y con . la vuelves a cargar.",
                             "Fly slowly to a free port of the station (a ring of lights marks it) and press U: the ship docks. Press , to unload the hold into the station stock and . to load it again."),
        ["obj.salvage"] = ("Recupera un pecio", "Salvage a wreck"),
        ["obj.salvage.hint"] = ("Los pecios abandonados guardan botín. Vuela despacio a menos de 12 celdas de uno y espera un segundo: lo desguazas y el botín va a la bodega. Los cristales resonantes solo salen de ahí.",
                                "Derelict wrecks hold loot. Fly slowly to within 12 cells of one and wait a second: you strip it and the loot goes into the hold. Resonant crystals come from nowhere else."),
        ["obj.upgrade"] = ("Añade o mejora un arma", "Fit or upgrade a weapon"),
        ["obj.upgrade.hint"] = ("Construye un astillero en la estación (N), atraca y pulsa F2: el diseñador añade un segundo láser o cañón, o mejora uno a Mk2 (pide lente de enfoque, refrigerante y placa compuesta).",
                                "Build a shipyard at the station (N), dock and press F2: the designer adds a second laser or gun, or upgrades one to Mk2 (it wants a focus lens, coolant and a composite plate)."),
    };
}
