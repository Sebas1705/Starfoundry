namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the arsenal bar and its slots (docs/prototypes/arsenal.md), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> ArsenalTable = new()
    {
        ["arsenal.name.laser1"] = ("Láser de minería Mk1", "Mk1 mining laser"),
        ["arsenal.name.laser2"] = ("Láser de minería Mk2", "Mk2 mining laser"),
        ["arsenal.name.gun1"] = ("Cañón Mk1", "Mk1 gun"),
        ["arsenal.name.gun2"] = ("Cañón Mk2", "Mk2 gun"),
        ["arsenal.name.gun3"] = ("Cañón pesado", "Heavy cannon"),
        ["arsenal.state.ready"] = ("listo", "ready"),
        ["arsenal.state.firing"] = ("disparando", "firing"),
        ["arsenal.state.cooling"] = ("recargando", "cooling down"),
        ["arsenal.state.blocked"] = ("fuera de alcance", "out of reach"),
        ["arsenal.state.lowpower"] = ("poca energía", "low power"),
        ["arsenal.state.nopower"] = ("sin energía", "no power"),
        ["arsenal.state.dry"] = ("sin munición", "no ammo"),
        ["arsenal.noDeposit"] = ("sin yacimiento", "no deposit"),
        ["arsenal.rate"] = ("{0}/s", "{0}/s"),
        ["arsenal.left"] = ("quedan {0}", "{0} left"),
        ["arsenal.slowed"] = ("bodega llena: lento", "hold full: slow"),
        ["arsenal.ammo"] = ("munición {0}", "ammo {0}"),
        ["arsenal.keyHint"] = ("Tecla {0}", "Key {0}"),
        ["arsenal.also"] = ("actúa a la vez", "acts together"),
        ["arsenal.empty"] = ("Monta armas en un astillero", "Fit weapons at a shipyard"),
        ["arsenal.title"] = ("ARSENAL", "ARSENAL"),
    };

}
