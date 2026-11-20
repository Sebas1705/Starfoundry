using System.Globalization;

namespace Starfoundry.Ui;

public enum Language
{
    Es,
    En,
}

/// <summary>
/// Spanish and English text for the whole interface. Every key has both languages; placeholders are
/// <c>{0}</c>, <c>{1}</c>... and must match between them (checked by tests). Game data names (items, machines,
/// recipes, research, system types, enemies) are looked up with <see cref="Name"/>.
/// </summary>
public static class Loc
{
    public static Language Lang { get; set; } = Language.En;

    /// <summary>Culture for number formatting in the current language (decimal comma in Spanish).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Lang == Language.Es ? "es-ES" : "en-US");

    /// <summary>Spanish for locales starting with "es", English otherwise.</summary>
    public static Language FromLocale(string? locale) =>
        locale is not null && locale.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? Language.Es : Language.En;

    public static bool Has(string key) => Table.ContainsKey(key);

    public static IEnumerable<KeyValuePair<string, (string Es, string En)>> All => Table;

    /// <summary>Text for a key in the current language, formatted with the arguments. Unknown keys show as ⟦key⟧ so they stand out.</summary>
    public static string T(string key, params object[] args)
    {
        if (!Table.TryGetValue(key, out var pair)) return "⟦" + key + "⟧";
        string text = Lang == Language.Es ? pair.Es : pair.En;
        return args.Length == 0 ? text : string.Format(Culture, text, args);
    }

    /// <summary>Name of a game-data thing, for example <c>Name("item", "core:iron_ore")</c>. Falls back to a readable form of the id.</summary>
    public static string Name(string kind, string id)
    {
        string key = kind + "." + id;
        if (Table.TryGetValue(key, out var pair)) return Lang == Language.Es ? pair.Es : pair.En;
        int colon = id.IndexOf(':');
        return (colon >= 0 ? id[(colon + 1)..] : id).Replace('_', ' ');
    }

    private static readonly Dictionary<string, (string Es, string En)> Table = new()
    {
        // ---- game data names ----
        ["item.core:iron_ore"] = ("mineral de hierro", "iron ore"),
        ["item.core:iron_ingot"] = ("lingote de hierro", "iron ingot"),
        ["item.core:iron_plate"] = ("placa de hierro", "iron plate"),
        ["item.core:carbon"] = ("carbono", "carbon"),
        ["item.core:ammo"] = ("munición", "ammo"),

        ["machine.core:extractor"] = ("extractor", "extractor"),
        ["machine.core:drill"] = ("taladro", "drill"),
        ["machine.core:smelter"] = ("horno", "smelter"),
        ["machine.core:assembler"] = ("ensamblador", "assembler"),
        ["machine.core:research_lab"] = ("laboratorio", "research lab"),
        ["machine.core:depot"] = ("depósito", "depot"),
        ["machine.core:turret"] = ("torreta", "turret"),
        ["machine.core:wall"] = ("muro", "wall"),
        ["machine.core:micro_refinery"] = ("microrrefinería", "micro-refinery"),

        ["recipe.core:mine_iron"] = ("extraer hierro", "mine iron"),
        ["recipe.core:mine_carbon"] = ("extraer carbono", "mine carbon"),
        ["recipe.core:drill_iron"] = ("taladrar hierro", "drill iron"),
        ["recipe.core:drill_carbon"] = ("taladrar carbono", "drill carbon"),
        ["recipe.core:smelt_iron"] = ("fundir hierro", "smelt iron"),
        ["recipe.core:iron_plate"] = ("placa de hierro", "iron plate"),
        ["recipe.core:make_ammo"] = ("fabricar munición", "make ammo"),
        ["recipe.core:ship_smelt"] = ("fundir a bordo", "ship smelting"),

        ["research.core:logistics"] = ("Logística", "Logistics"),
        ["research.core:carbon_drilling"] = ("Perforación de carbono", "Carbon drilling"),
        ["research.core:advanced_logistics"] = ("Logística avanzada", "Advanced logistics"),
        ["research.core:jump_drive"] = ("Motor de salto", "Jump drive"),

        ["arch.core:home"] = ("hogar", "home"),
        ["arch.core:dense_belt"] = ("cinturón denso", "dense belt"),
        ["arch.core:sparse_belt"] = ("cinturón disperso", "sparse belt"),
        ["arch.core:carbon_rich"] = ("rico en carbono", "carbon rich"),

        ["enemy.core:scout"] = ("explorador", "scout"),
        ["enemy.core:striker"] = ("destructor", "striker"),
        ["enemy.core:brute"] = ("bruto", "brute"),

        // ---- shared words ----
        ["ui.free"] = ("gratis", "free"),
        ["ui.locked"] = ("BLOQUEADO", "LOCKED"),
        ["ui.perCell"] = (" por celda", " per cell"),
        ["ui.belt"] = ("Cinta Mk{0}", "Belt Mk{0}"),

        // ---- buttons ----
        ["btn.mine"] = ("Minar  [M]", "Mine  [M]"),
        ["btn.mineStop"] = ("Dejar de minar  [M]", "Stop mining  [M]"),
        ["btn.refine"] = ("Refinar a bordo  [F]", "Refine on board  [F]"),
        ["btn.refineStop"] = ("Parar refinería  [F]", "Stop refinery  [F]"),
        ["btn.scan"] = ("Escanear  [K]", "Scan  [K]"),
        ["btn.scanWait"] = ("Escáner ({0} s)", "Scanner ({0} s)"),
        ["btn.map"] = ("Mapa estelar  [G]", "Star map  [G]"),
        ["btn.weapons"] = ("Armas  [V]", "Weapons  [V]"),
        ["btn.weaponsOff"] = ("Armas ACTIVAS  [V]", "Weapons ON  [V]"),
        ["btn.research"] = ("Elegir investigación  [T]", "Choose research  [T]"),
        ["btn.researchNext"] = ("Cambiar investigación  [T]", "Change research  [T]"),
        ["btn.repair"] = ("Reparar ({0})  [E]", "Repair ({0})  [E]"),
        ["tip.repair"] = ("Repara todas las máquinas dañadas a tu alcance. Cuesta la mitad de lo que costó construirlas, en proporción a la vida que les falta: {0}.", "Repairs every damaged machine within reach. It costs half the build cost, in proportion to the hit points missing: {0}."),
        ["tip.repairNone"] = ("No hay máquinas dañadas a tu alcance.", "No damaged machines within reach."),
        ["btn.jump"] = ("Saltar  [J]", "Jump  [J]"),
        ["btn.help"] = ("Ayuda [F1]", "Help [F1]"),
        ["btn.hideSteps"] = ("Ocultar pasos [O]", "Hide steps [O]"),
        ["btn.showSteps"] = ("Mostrar pasos [O]", "Show steps [O]"),

        // ---- button tooltips ----
        ["tip.mine"] = ("Mina el yacimiento elegido. Si estás lejos, la nave vuela hasta él y empieza sola. 2 minerales por segundo, hasta llenar la bodega.",
                        "Mines the selected deposit. If you are far away the ship flies there and starts by itself. 2 ore per second, until the hold is full."),
        ["tip.refine"] = ("La microrrefinería de la nave convierte mineral en lingotes, despacio (un lingote cada 4 s).",
                          "The ship's micro-refinery turns ore into ingots, slowly (one ingot every 4 s)."),
        ["tip.scan"] = ("Revela los yacimientos ocultos a menos de 80 celdas. Se recarga en 5 s.",
                        "Reveals hidden deposits within 80 cells. Recharges in 5 s."),
        ["tip.map"] = ("Abre el mapa de sistemas. Elige uno con las flechas y salta con J.",
                       "Opens the system map. Pick one with the arrow keys and jump with J."),
        ["tip.weapons"] = ("Enciende el arma de la nave: dispara al enemigo más cercano a menos de 25 celdas.",
                           "Switches the ship weapon on: it fires at the nearest enemy within 25 cells."),
        ["tip.research"] = ("Elige qué investigan los laboratorios. Hay que llevarles las placas por cinta.",
                            "Chooses what the labs research. Labs need the items delivered by belt."),
        ["tip.jump"] = ("Salta al sistema elegido del mapa. Cuesta objetos y tarda 3 s en cargarse.",
                        "Jumps to the system chosen on the map. It costs items and takes 3 s to charge."),

        // ---- build bar tooltips ----
        ["tip.cost"] = ("Coste: {0}", "Cost: {0}"),
        ["tip.signature"] = ("Ruido (atrae oleadas): {0}", "Noise (attracts waves): {0}"),
        ["tip.beltSpeed"] = ("Transporta {0} objetos por segundo.", "Carries {0} items per second."),
        ["tip.locked"] = ("Bloqueado: hay que investigarlo.", "Locked: research it first."),
        ["tip.needItems"] = ("Te faltan objetos en la bodega.", "You do not have the items in your hold."),
        ["tip.hp"] = ("Vida {0}/{1}", "HP {0}/{1}"),
        ["tip.role.crafter"] = ("Transforma objetos de las cintas con una receta.", "Turns items from belts into others with a recipe."),
        ["tip.role.drill"] = ("Saca mineral de un yacimiento a menos de 16 celdas.", "Pulls ore out of a deposit within 16 cells."),
        ["tip.role.lab"] = ("Consume lo que pide la investigación elegida.", "Consumes what the chosen research needs."),
        ["tip.role.depot"] = ("Mete en tu bodega todo lo que le llega por cinta.", "Puts everything that reaches it by belt into your hold."),
        ["tip.role.turret"] = ("Dispara a los enemigos; gasta munición que llega por cinta.", "Shoots enemies; uses ammo delivered by belt."),
        ["tip.role.wall"] = ("Los enemigos atacan el muro más cercano antes que cualquier otra máquina. Repáralo para que dure.", "Enemies attack the nearest wall before any other machine. Repair it to keep it standing."),
        ["tip.role.ship"] = ("Va en la nave.", "Lives on the ship."),
        ["tip.belt"] = ("Cinta Mk{0}: {1} celdas, {2} objetos encima", "Belt Mk{0}: {1} cells, {2} items on it"),
        ["tip.deposit"] = ("Yacimiento de {0}: quedan {1} de {2}.\nClic para elegirlo; M para minarlo.",
                           "{0} deposit: {1} of {2} left.\nClick to select it; M to mine it."),
        ["tip.enemy"] = ("Enemigo: {0}  (vida {1})", "Enemy: {0}  (HP {1})"),

        // ---- hints under the bar ----
        ["hint.pointer"] = ("Clic: inspeccionar una máquina, elegir un yacimiento o volar a ese punto.   Elige algo en la barra de abajo (1-9) para construir.   F1: ayuda",
                            "Click: inspect a machine, pick a deposit or fly to that spot.   Choose something in the bar below (1-9) to build.   F1: help"),
        ["hint.buildBelt"] = ("Construyendo: {0}. Clic y arrastra para tender una línea.   Clic derecho: demoler.   R: girar.   Esc: dejar de construir",
                              "Building: {0}. Click and drag to lay a line.   Right-click: demolish.   R: rotate.   Esc: stop building"),
        ["hint.buildMachine"] = ("Construyendo: {0}. Clic para colocarlo.   Clic derecho: demoler.   Tab: cambiar receta.   Esc: dejar de construir",
                                 "Building: {0}. Click to place it.   Right-click: demolish.   Tab: change recipe.   Esc: stop building"),

        // ---- status panel ----
        ["hud.hold"] = ("Bodega {0}/{1}", "Hold {0}/{1}"),
        ["hud.noDeposit"] = ("Sin yacimiento elegido: haz clic en una roca o pulsa M.", "No deposit selected: click a rock or press M."),
        ["hud.depositSelected"] = ("Yacimiento elegido: {0} (quedan {1}), a {2} celdas", "Selected deposit: {0} ({1} left), {2} cells away"),
        ["hud.mining"] = ("Minando {0} (quedan {1})", "Mining {0} ({1} left)"),
        ["hud.miningOut"] = ("Minería de {0} en pausa: la nave está lejos del yacimiento", "Mining {0} paused: the ship is too far from the deposit"),
        ["hud.miningFull"] = ("¡BODEGA LLENA! La minería y los depósitos están parados. Gasta o construye algo.",
                              "HOLD FULL! Mining and depots are paused. Spend items or build something."),
        ["hud.deepSpace"] = ("espacio profundo", "deep space"),
        ["hud.jumpReady"] = ("motor de salto listo", "jump drive ready"),
        ["hud.jumpLocked"] = ("motor de salto bloqueado: investiga Motor de salto", "jump drive locked: research Jump drive"),
        ["hud.jumping"] = ("SALTANDO a {0} en {1} s", "JUMPING to {0} in {1} s"),
        ["hud.system"] = ("Sistema: {0}   |   visitados {1}/{2}   |   {3}", "System: {0}   |   visited {1}/{2}   |   {3}"),
        ["hud.research"] = ("Investigando {0}: {1}   (el laboratorio necesita esos objetos por cinta)",
                            "Researching {0}: {1}   (the lab needs these items by belt)"),
        ["hud.researchNone"] = ("Sin investigación elegida (T).", "No research selected (T)."),
        ["hud.researchDone"] = ("Sin investigación elegida (T). Hecho: {0}", "No research selected (T). Done: {0}"),
        ["hud.speed"] = ("velocidad x{0}", "speed x{0}"),
        ["hud.paused"] = ("PAUSA", "PAUSED"),
        ["hud.alert"] = ("alerta {0}%", "alert {0}%"),
        ["hud.enemies"] = ("{0} enemigos atacando", "{0} enemies attacking"),
        ["hud.waveIncoming"] = ("OLEADA {0} en {1} s", "WAVE {0} in {1} s"),
        ["hud.rebuilding"] = ("RECONSTRUYENDO LA NAVE {0} s", "REBUILDING THE SHIP {0} s"),
        ["hud.hull"] = ("casco {0}/{1}", "hull {0}/{1}"),
        ["hud.threat"] = ("Amenaza: ruido {0} (calma hasta {1})   {2}   |   {3}", "Threat: noise {0} (calm up to {1})   {2}   |   {3}"),

        // ---- map ----
        ["map.title"] = ("MAPA ESTELAR  (* estás aquí, > elegido)", "STAR MAP  (* you are here, > selected)"),
        ["map.visited"] = ("visitado", "visited"),
        ["map.known"] = ("conocido", "known"),
        ["map.lane"] = ("ruta", "lane"),

        // ---- inspector ----
        ["insp.recipe"] = ("Receta: {0}", "Recipe: {0}"),
        ["insp.takes"] = ("Consume: {0}", "Takes: {0}"),
        ["insp.makes"] = ("Produce: {0} cada {1} s", "Makes: {0} every {1} s"),
        ["insp.repair"] = ("Reparar ({0})", "Repair ({0})"),
        ["insp.remove"] = ("Demoler (devuelve {0})", "Demolish (refunds {0})"),

        // ---- machine status ----
        ["status.working"] = ("trabajando", "working"),
        ["status.waitingInputs"] = ("esperando materiales por cinta", "waiting for inputs by belt"),
        ["status.outputBlocked"] = ("salida bloqueada: la cinta está llena o no está conectada", "output blocked: the belt is full or not connected"),
        ["status.noRecipe"] = ("sin receta: elige una (Tab o botones)", "no recipe: choose one (Tab or the buttons)"),
        ["status.drillUnlinked"] = ("sin yacimiento: construye a menos de 16 celdas de uno descubierto", "no deposit: build within 16 cells of a discovered one"),
        ["status.drillEmpty"] = ("el yacimiento se ha agotado", "the deposit is empty"),
        ["status.drillIdle"] = ("esperando para taladrar", "waiting to drill"),
        ["status.labNone"] = ("sin investigación elegida: pulsa T", "no research chosen: press T"),
        ["status.labWorking"] = ("investigando {0}", "researching {0}"),
        ["status.depotOk"] = ("entrega a tu bodega", "delivering to your hold"),
        ["status.depotCap"] = ("tienes {1} de {0}, lo máximo que entrega un depósito: gasta algunos para seguir", "you hold {1} {0}, the most a depot delivers: spend some to continue"),
        ["status.depotFull"] = ("bodega llena: esperando sitio", "hold full: waiting for room"),
        ["status.wall"] = ("atrae a los enemigos: ponlo delante de la base, al alcance de las torretas", "draws enemies: put it in front of the base, within reach of the turrets"),
        ["status.turretReady"] = ("munición {0}/{1}", "ammo {0}/{1}"),
        ["status.turretEmpty"] = ("sin munición: llévale munición por cinta", "out of ammo: bring it ammo by belt"),

        // ---- why something was refused ----
        ["why.notEnough"] = ("No tienes los objetos en la bodega", "Not enough items in the hold"),
        ["why.locked"] = ("Bloqueado: investígalo primero", "Locked: research it first"),
        ["why.overlap"] = ("Esa celda está ocupada", "That cell is taken"),
        ["why.outOfRange"] = ("Demasiado lejos de la nave", "Too far from the ship"),
        ["why.outOfBounds"] = ("Fuera de los límites del mundo", "Out of the world limits"),
        ["why.unknownDeposit"] = ("Ese yacimiento no existe", "No such deposit"),
        ["why.depleted"] = ("Ese yacimiento está agotado", "That deposit is empty"),
        ["why.researchUnavailable"] = ("Esa investigación no está disponible", "That research is not available"),
        ["why.notDamaged"] = ("No está dañada", "It is not damaged"),
        ["why.notDiscovered"] = ("Aún no lo has descubierto: escanea (K) cerca", "Not discovered yet: scan (K) near it"),
        ["why.onCooldown"] = ("El escáner se está recargando", "The scanner is recharging"),
        ["why.noLane"] = ("No hay ruta desde este sistema a ese", "No lane from this system to that one"),
        ["why.notInSystem"] = ("Estás en el espacio profundo: vuelve a un sistema", "You are in deep space: fly back to a system"),
        ["why.busy"] = ("Ya hay un salto cargándose", "A jump is already charging"),

        // ---- toasts ----
        ["toast.welcome"] = ("Bienvenido. Sigue los pasos de arriba a la derecha. F1 muestra los controles.",
                             "Welcome. Follow the steps at the top right. F1 shows the controls."),
        ["toast.holdFull"] = ("¡Bodega llena! La minería y los depósitos se paran hasta que gastes objetos.",
                              "Hold full! Mining and depots stop until you spend items."),
        ["toast.language"] = ("Idioma: español", "Language: English"),
        ["toast.beltFacing"] = ("Las cintas sueltas miran hacia {0}", "Single belts now face {0}"),
        ["toast.depositSelected"] = ("Yacimiento de {0} elegido. Pulsa M para minarlo.", "{0} deposit selected. Press M to mine it."),
        ["toast.flyingToMine"] = ("Volando al yacimiento de {0}...", "Flying to the {0} deposit..."),
        ["toast.noDeposits"] = ("No conoces ningún yacimiento aquí. Escanea con K.", "No known deposits here. Scan with K."),
        ["toast.chooseSystem"] = ("Abre el mapa (G) y elige un sistema con las flechas.", "Open the map (G) and choose a system with the arrows."),
        ["toast.noDepositForDrill"] = ("No hay un yacimiento conocido de ese mineral al alcance del taladro. Escanea (K) o construye más cerca.",
                                       "No known deposit of that ore within the drill's reach. Scan (K) or build closer."),
        ["toast.oneRecipe"] = ("Esta máquina solo tiene una receta disponible.", "This machine has only one recipe available."),
        ["toast.nothingToResearch"] = ("No queda nada por investigar.", "Nothing left to research."),
        ["toast.recipe"] = ("Receta: {0}", "Recipe: {0}"),
        ["toast.nothingToRepair"] = ("Nada que reparar al alcance.", "Nothing to repair within reach."),
        ["toast.scanNone"] = ("Escaneo: nada nuevo al alcance.", "Scan: nothing new in range."),
        ["toast.scanFound"] = ("Escaneo: {0} yacimiento(s) descubierto(s).", "Scan: {0} deposit(s) revealed."),
        ["toast.jumpCharging"] = ("Cargando el salto a {0}...", "Charging the jump to {0}..."),
        ["toast.waveWarning"] = ("Los Remnant han detectado tu ruido: llega una oleada en {0} s. Construye torretas con munición por cinta y activa tus armas (V).",
                                 "The Remnant detected your noise: a wave arrives in {0} s. Build turrets with ammo by belt and switch your weapons on (V)."),
        ["toast.waveArrived"] = ("Ha llegado la oleada {0}: {1} enemigos.", "Wave {0} has arrived: {1} enemies."),
        ["toast.machineLost"] = ("Han destruido una máquina ({0} perdidas en total).", "A machine was destroyed ({0} lost in total)."),
        ["toast.shipLost"] = ("Han destruido tu nave. Se reconstruye en el ancla...", "Your ship was destroyed. Rebuilding at the anchor..."),

        // ---- guided steps ----
        ["obj.title"] = ("PRIMEROS PASOS  {0}/{1}", "FIRST STEPS  {0}/{1}"),
        ["obj.allDone"] = ("Todos los pasos hechos. Explora, amplía y mantén la base defendida.",
                           "All steps done. Explore, expand and keep the base defended."),
        ["obj.fly"] = ("Vuela hasta el yacimiento de hierro", "Fly to the iron deposit"),
        ["obj.fly.hint"] = ("Haz clic en la roca marrón o pulsa M: la nave vuela sola.", "Click the brown rock or press M: the ship flies there by itself."),
        ["obj.mine"] = ("Mina 5 minerales", "Mine 5 ore"),
        ["obj.mine.hint"] = ("Pulsa M (o el botón Minar). Sacas 2 minerales por segundo.", "Press M (or the Mine button). You get 2 ore per second."),
        ["obj.refine"] = ("Consigue 3 lingotes de hierro", "Get 3 iron ingots"),
        ["obj.refine.hint"] = ("Pulsa F para encender la refinería de la nave: convierte mineral en lingotes, despacio.",
                               "Press F to switch the ship's refinery on: it turns ore into ingots, slowly."),
        ["obj.drill"] = ("Construye un taladro junto al yacimiento", "Build a drill next to the deposit"),
        ["obj.drill.hint"] = ("Elige Taladro en la barra (4) y haz clic en una celda a menos de 16 celdas del yacimiento. Cuesta lingotes.",
                              "Choose Drill in the bar (4) and click a cell within 16 cells of the deposit. It costs ingots."),
        ["obj.smelter"] = ("Construye un horno", "Build a smelter"),
        ["obj.smelter.hint"] = ("Elige Horno (5) y colócalo delante del taladro; une ambos con cintas (1) arrastrando.",
                                "Choose Smelter (5) and place it in front of the drill; join them with belts (1) by dragging."),
        ["obj.depot"] = ("Entrega lingotes a tu bodega", "Deliver ingots to your hold"),
        ["obj.depot.hint"] = ("Tiende cintas desde el horno hasta un Depósito (8): mete en tu bodega todo lo que le llega.",
                              "Lay belts from the smelter to a Depot (8): it puts everything that arrives into your hold."),
        ["obj.assembler"] = ("Construye un ensamblador", "Build an assembler"),
        ["obj.assembler.hint"] = ("Pon un Ensamblador (6) detrás de otro horno para fabricar placas.", "Put an Assembler (6) after another smelter to make plates."),
        ["obj.lab"] = ("Construye un laboratorio", "Build a research lab"),
        ["obj.lab.hint"] = ("Un Laboratorio (7) recibe placas por cinta y investiga. Cuesta lingotes y placas.", "A Lab (7) takes plates by belt and researches. It costs ingots and plates."),
        ["obj.research"] = ("Termina una investigación", "Finish a research"),
        ["obj.research.hint"] = ("Pulsa T para elegir una y lleva placas al laboratorio por cinta.", "Press T to pick one and bring plates to the lab by belt."),
        ["obj.scan"] = ("Escanea en busca de yacimientos nuevos", "Scan for new deposits"),
        ["obj.scan.hint"] = ("Pulsa K: revela los yacimientos ocultos a menos de 80 celdas. Vuela lejos para encontrar más.",
                             "Press K: it reveals hidden deposits within 80 cells. Fly away to find more."),
        ["obj.defend"] = ("Defiende tu base", "Defend your base"),
        ["obj.defend.hint"] = ("Las máquinas hacen ruido y atraen oleadas. Construye una Torreta (9) y llévale munición por cinta.",
                               "Machines make noise and attract waves. Build a Turret (9) and bring it ammo by belt."),

        // ---- help overlay ----
        ["help.text"] = (
            "CONTROLES   (F1 para cerrar)\n\n" +
            "Ratón\n" +
            "  Clic izquierdo   puntero: inspeccionar una máquina, elegir un yacimiento, volar a ese punto\n" +
            "                   construyendo: colocar (arrastra para tender cintas)\n" +
            "  Clic derecho     demoler lo que hay bajo el ratón (se devuelve el coste)\n" +
            "  Rueda            zoom        Arrastrar con botón central o derecho   girar la cámara        Inicio   recentrar\n\n" +
            "Nave\n" +
            "  W A S D   volar según la cámara        Espacio / C   subir / bajar\n" +
            "  M   minar (vuela primero al yacimiento)      F   refinería       K   escanear       V   armas\n\n" +
            "Construcción\n" +
            "  1-9, 0   elegir qué construir (otra vez para guardarlo)       Esc   dejar de construir\n" +
            "  Tab   cambiar receta       R   girar cintas sueltas       Z / X   cambiar de capa\n" +
            "  Con el puntero, clic en una máquina: ver su estado y cambiar su receta.\n  E   reparar todas las máquinas dañadas a tu alcance (cuesta la mitad de su coste, en proporción al daño)\n\n" +
            "Otros\n" +
            "  T   elegir investigación      G   mapa estelar (flechas y J para saltar)\n" +
            "  P   pausa       + / -   velocidad       L   idioma       O   pasos       [ ]   cambiar yacimiento",
            "CONTROLS   (F1 to close)\n\n" +
            "Mouse\n" +
            "  Left click    pointer: inspect a machine, pick a deposit, fly to that spot\n" +
            "                building: place (drag to lay belts)\n" +
            "  Right click   demolish what is under the mouse (the cost is refunded)\n" +
            "  Wheel         zoom        Middle or right drag   turn the camera        Home   recentre\n\n" +
            "Ship\n" +
            "  W A S D   fly relative to the camera        Space / C   up / down\n" +
            "  M   mine (flies to the deposit first)      F   refinery       K   scan       V   weapons\n\n" +
            "Building\n" +
            "  1-9, 0   choose what to build (again to put it away)       Esc   stop building\n" +
            "  Tab   change recipe       R   rotate single belts       Z / X   change layer\n" +
            "  With the pointer, click a machine: see its status and change its recipe.\n  E   repair every damaged machine within reach (half the build cost, in proportion to the damage)\n\n" +
            "Other\n" +
            "  T   choose research      G   star map (arrows and J to jump)\n" +
            "  P   pause       + / -   speed       L   language       O   steps       [ ]   change deposit"),
    };
}
