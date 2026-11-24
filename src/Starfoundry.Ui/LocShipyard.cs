namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the hardpoint modules, the shipyard and the ship designer (ADR 0032), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> ShipyardTable = new()
    {
        // module kinds (the names the build menu and the designer use are "module." + the lower-case enum name)
        ["module.mininglaser"] = ("Láser de minería", "Mining laser"),
        ["module.mininglaser.purpose"] = ("Montaje de la nave: el haz sale de este módulo. Cada láser suma rendimiento y gasta 5 de energía mientras mina.", "A ship hardpoint: the beam leaves from this module. Each laser adds yield and draws 5 power while it mines."),
        ["module.mininglasermk2"] = ("Láser de minería Mk2", "Mk2 mining laser"),
        ["module.mininglasermk2.purpose"] = ("Láser potente: 170 % de rendimiento y 14 celdas de alcance; gasta 8 de energía.", "A strong laser: 170 percent yield and 14 cells of reach; draws 8 power."),
        ["module.gun"] = ("Cañón", "Gun"),
        ["module.gun.purpose"] = ("Montaje de la nave: dispara al enemigo más cercano mientras mantienes el fuego (grupo de armas V).", "A ship hardpoint: shoots the nearest enemy while you hold fire (weapon group V)."),
        ["module.gunmk2"] = ("Cañón Mk2", "Mk2 gun"),
        ["module.gunmk2.purpose"] = ("Cañón potente: 170 % del daño de un cañón Mk1.", "A strong gun: 170 percent of the damage of a mark 1 gun."),
        ["module.shipyard"] = ("Astillero", "Shipyard"),
        ["module.shipyard.purpose"] = ("Solo en estaciones: con una nave atracada abre el diseñador (F2) para añadir, mover o mejorar sus módulos y armas.", "Stations only: with a ship docked it opens the designer (F2) to add, move or upgrade its modules and weapons."),

        // rejects
        ["toast.reject.noLaser"] = ("La nave no tiene láser de minería: instala uno en un astillero", "The ship has no mining laser: fit one at a shipyard"),
        ["toast.reject.noGun"] = ("La nave no tiene cañón: instala uno en un astillero", "The ship has no gun: fit one at a shipyard"),
        ["toast.reject.noShipyard"] = ("Hace falta la nave atracada en una estación tuya con un astillero", "The ship must be docked at one of your stations that has a shipyard"),

        // key panel
        ["key.weaponLaser"] = ("Arma: láser de minería (V cambia)", "Weapon: mining laser (V switches)"),
        ["key.weaponGuns"] = ("Arma: cañones (V cambia)", "Weapon: guns (V switches)"),
        ["key.fireGuns"] = ("Mantén el clic para disparar los cañones", "Hold the click to fire the guns"),
        ["key.designer"] = ("Diseñador de naves", "Ship designer"),
        ["key.why.noLaser"] = ("La nave no tiene láser de minería: instala uno en el astillero.", "The ship has no mining laser: fit one at the shipyard."),
        ["key.why.noGun"] = ("La nave no tiene cañón: instala uno en el astillero.", "The ship has no gun: fit one at the shipyard."),
        ["toast.weaponGroupLaser"] = ("Arma en mano: láser de minería ({0})", "Weapon in hand: mining laser ({0})"),
        ["toast.weaponGroupGuns"] = ("Arma en mano: cañones ({0})", "Weapon in hand: guns ({0})"),
        ["toast.noShipyardHere"] = ("Para el diseñador atraca en una estación tuya con un astillero", "For the designer dock at one of your stations that has a shipyard"),

        // the ship designer
        ["designer.title"] = ("Diseñador de naves", "Ship designer"),
        ["designer.subtitle"] = ("Astillero de la estación: edita los módulos de tu nave sobre su rejilla.", "Station shipyard: edit your ship's modules on its grid."),
        ["designer.layer"] = ("Capa {0} (altura de módulo)", "Layer {0} (module height)"),
        ["designer.layerUp"] = ("Capa +", "Layer +"),
        ["designer.layerDown"] = ("Capa -", "Layer -"),
        ["designer.front"] = ("proa", "bow"),
        ["designer.back"] = ("popa", "stern"),
        ["designer.palette"] = ("Módulos", "Modules"),
        ["designer.preview"] = ("Vista previa", "Preview"),
        ["designer.hint"] = ("Elige un módulo y haz clic en una celda vacía junto a la nave para añadirlo. Haz clic en un módulo para quitarlo, moverlo o mejorarlo.", "Pick a module and click a free cell next to the ship to add it. Click a module to remove, move or upgrade it."),
        ["designer.selected"] = ("Módulo seleccionado: {0}", "Selected module: {0}"),
        ["designer.nothingSelected"] = ("Ningún módulo seleccionado", "No module selected"),
        ["designer.remove"] = ("Quitar", "Remove"),
        ["designer.move"] = ("Mover", "Move"),
        ["designer.moving"] = ("Mover: elige la celda de destino", "Move: pick the destination cell"),
        ["designer.upgrade"] = ("Mejorar a {0}", "Upgrade to {0}"),
        ["designer.noUpgrade"] = ("Sin mejora", "No upgrade"),
        ["designer.apply"] = ("Aplicar diseño", "Apply design"),
        ["designer.revert"] = ("Deshacer cambios", "Revert changes"),
        ["designer.close"] = ("Cerrar (Esc)", "Close (Esc)"),
        ["designer.valid"] = ("Diseño válido", "Valid design"),
        ["designer.unchanged"] = ("Sin cambios", "No changes"),
        ["designer.invalid"] = ("No se puede aplicar: {0}", "Cannot apply: {0}"),
        ["designer.changes"] = ("{0} nuevos, {1} quitados, {2} movidos", "{0} new, {1} removed, {2} moved"),
        ["designer.cost"] = ("Coste: {0}", "Cost: {0}"),
        ["designer.refund"] = ("Reembolso: {0}", "Refund: {0}"),
        ["designer.free"] = ("Sin coste", "No cost"),
        ["designer.funds"] = ("Disponible (bodega y almacén): {0}", "Available (hold and stock): {0}"),
        ["designer.weapons"] = ("Armas: {0} láseres (+{1} % de minado), {2} cañones ({3} % de daño)", "Weapons: {0} lasers ({1} percent mining), {2} guns ({3} percent damage)"),
        ["designer.hardpointHint"] = ("Los láseres y cañones solo se instalan aquí. Cada láser mina desde su propio módulo.", "Lasers and guns are fitted only here. Each laser mines from its own module."),
        ["designer.applied"] = ("Diseño aplicado: {0}", "Design applied: {0}"),
        ["designer.edit.occupied"] = ("Esa celda ya tiene un módulo", "That cell already has a module"),
        ["designer.edit.notAdjacent"] = ("Un módulo nuevo debe tocar a otro por una cara", "A new module must touch another by a face"),
        ["designer.edit.outOfBounds"] = ("Esa celda queda fuera de la rejilla", "That cell is outside the grid"),
        ["designer.edit.kindNotAllowed"] = ("Una nave no admite ese módulo", "A ship does not take that module"),
        ["designer.edit.noUpgrade"] = ("Ese módulo no tiene mejora", "That module has no upgrade"),
        ["designer.edit.tooMany"] = ("La nave ya tiene demasiados módulos", "The ship already has too many modules"),
        ["designer.edit.empty"] = ("No hay ningún módulo ahí", "There is no module there"),
        ["designer.edit.sameCell"] = ("Elige otra celda", "Pick another cell"),
        ["learn.shipyard"] = ("Construye un astillero en la estación, atraca la nave y pulsa F2: el diseñador añade o mejora láseres y cañones.", "Build a shipyard at the station, dock the ship and press F2: the designer adds or upgrades lasers and guns."),
    };

    static Loc()
    {
        foreach (var (key, value) in ShipyardTable) Table[key] = value;
        foreach (var (key, value) in FlightTable) Table[key] = value;
        foreach (var (key, value) in MaterialsTable) Table[key] = value;
        foreach (var (key, value) in MissionsTable) Table[key] = value;
        AddIconStrings();
        foreach (var (key, value) in CockpitTable) Table[key] = value;
        // the camel-case forms of the names (the game looks modules up both ways, as for droneBay)
        foreach (var (lower, camel) in new[] { ("mininglaser", "miningLaser"), ("mininglasermk2", "miningLaserMk2"), ("gunmk2", "gunMk2") })
        {
            Table["module." + camel] = Table["module." + lower];
            Table["module." + camel + ".purpose"] = Table["module." + lower + ".purpose"];
        }
    }
}
