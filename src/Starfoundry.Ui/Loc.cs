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
        ["machine.core:repair_bay"] = ("taller de reparación", "repair bay"),
        ["machine.core:unloader"] = ("puerto de carga", "cargo port"),
        ["machine.core:micro_refinery"] = ("microrrefinería", "micro-refinery"),
        ["machine.core:ore_unloader"] = ("puerto de mineral", "ore port"),

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
        ["research.core:turret_calibration"] = ("Calibración de torretas", "Turret calibration"),
        ["research.core:reinforced_plating"] = ("Blindaje reforzado", "Reinforced plating"),
        ["research.core:rapid_loader"] = ("Cargador rápido", "Rapid loader"),
        ["research.core:field_repair"] = ("Reparación de campo", "Field repair"),
        ["research.core:ship_armor"] = ("Blindaje de la nave", "Ship armour"),
        ["research.core:ship_cannon"] = ("Cañón de la nave", "Ship cannon"),
        ["research.core:overclock"] = ("Sobrecarga", "Overclock"),
        ["research.core:alloy_refinement"] = ("Refinado de aleaciones", "Alloy refinement"),
        ["research.core:nanite_repair"] = ("Reparación con nanobots", "Nanite repair"),

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
        ["tip.role.repairer"] = ("Repara poco a poco las máquinas dañadas cercanas, gastando lingotes que le llegan por cinta.", "Slowly repairs damaged machines nearby, using ingots delivered by belt."),
        ["tip.role.unloader"] = ("Saca de tu bodega un objeto y lo pone en una cinta: el puerto de carga lleva lingotes (sin tocar los últimos 10) y el de mineral lleva mineral (sin reserva). Así lo que mines en otro sistema puede alimentar una fábrica lejos de él.", "Takes one kind of item out of your hold and puts it on a belt: the cargo port carries ingots (leaving the last 10) and the ore port carries ore (no reserve). What you mine in another system can feed a factory far from it."),
        ["tip.role.ship"] = ("Va en la nave.", "Lives on the ship."),
        ["tip.belt"] = ("Cinta Mk{0}: {1} celdas, {2} objetos encima", "Belt Mk{0}: {1} cells, {2} items on it"),
        ["tip.deposit"] = ("Yacimiento de {0}: quedan {1} de {2}.\nClic izquierdo para elegirlo; M para minarlo.",
                           "{0} deposit: {1} of {2} left.\nLeft click to select it; M to mine it."),
        ["tip.enemy"] = ("Enemigo: {0}  (vida {1})", "Enemy: {0}  (HP {1})"),

        // ---- hints under the bar ----
        ["hint.pointer"] = ("Ratón: mirar   WASD: volar   Espacio/C: subir/bajar   Clic: inspeccionar lo que apuntas   1-9: construir   Tab: soltar el ratón   F1: ayuda",
                            "Mouse: look   WASD: fly   Space/C: up/down   Click: inspect what you aim at   1-9: build   Tab: free the mouse   F1: help"),
        ["hint.pointerWalk"] = ("Ratón: mirar   WASD: caminar   Mayús: correr   E: sentarte en el asiento (para volar)   Clic: inspeccionar lo que apuntas   1-9: construir   Tab: soltar el ratón   F1: ayuda",
                                "Mouse: look   WASD: walk   Shift: run   E: sit at the pilot seat (to fly)   Click: inspect what you aim at   1-9: build   Tab: free the mouse   F1: help"),
        ["hint.sit"] = ("E: sentarse", "E: sit"),
        ["hint.stand"] = ("E: levantarse", "E: stand"),
        ["toast.sitToFly"] = ("De pie no puedes pilotar ni usar el piloto automático: siéntate en el asiento del piloto (E).", "You cannot fly or use the autopilot standing up: sit at the pilot seat (E)."),
        ["toast.seatTooFar"] = ("Estás lejos del asiento del piloto: acércate para sentarte (E).", "You are too far from the pilot seat: walk closer to sit (E)."),
        ["toast.stoodUp"] = ("Te levantas: WASD camina por la nave (Mayús corre). La nave se queda quieta hasta que te sientes (E).", "You stand up: WASD walks around the ship (Shift runs). The ship holds still until you sit (E)."),
        ["hint.buildBelt"] = ("Construyendo: {0}. Apunta al suelo y haz clic donde empieza la línea; apunta al final y haz clic otra vez (o arrastra).   Clic derecho: cancelar o demoler.   R: girar.   Esc: dejar de construir",
                              "Building: {0}. Aim at the ground and click where the line starts; aim at the end and click again (or drag).   Right-click: cancel or demolish.   R: rotate.   Esc: stop building"),
        ["hint.buildMachine"] = ("Construyendo: {0}. Apunta al suelo y haz clic para colocarlo.   Clic derecho: demoler.   Y: cambiar receta.   Esc: dejar de construir",
                                 "Building: {0}. Aim at the ground and click to place it.   Right-click: demolish.   Y: change recipe.   Esc: stop building"),

        // ---- status panel ----
        ["hud.hold"] = ("Bodega {0}/{1}", "Hold {0}/{1}"),
        ["hud.noDeposit"] = ("Sin yacimiento elegido: apunta a una roca y haz clic, o pulsa M.", "No deposit selected: aim at a rock and click, or press M."),
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
        ["hud.oreLow"] = ("Queda poco {0} en este sistema ({1}%): escanea (K) y salta (G, J) a otro sistema.", "Little {0} left in this system ({1}%): scan (K) and jump (G, J) to another system."),
        ["learn.ammo"] = ("Tus torretas no tienen munición. Hace falta una línea que la fabrique (ensamblador con la receta de munición) y una cinta hasta cada torreta.", "Your turrets have no ammo. They need a line that makes it (an assembler on the ammo recipe) and a belt to each turret."),
        ["learn.repair"] = ("Hay máquinas dañadas. Pulsa Retroceso para repararlas (cuesta la mitad de lo que costaron) o construye un taller de reparación (Q) con una cinta de lingotes.", "Some machines are damaged. Press Backspace to repair them (half their build cost) or build a repair bay (Q) fed ingots by belt."),
        ["learn.walls"] = ("Los enemigos atacan primero el muro más cercano. Pon muros (0) delante de la base, al alcance de las torretas, y repáralos con Retroceso durante la oleada.", "Enemies attack the nearest wall first. Put walls (0) in front of the base, within reach of the turrets, and repair them with Backspace during the wave."),
        ["learn.research"] = ("El laboratorio está parado. Pulsa T para elegir qué investigar y asegúrate de que le llegan placas por cinta.", "The lab is idle. Press T to choose what to research and make sure plates reach it by belt."),
        ["learn.upgrades"] = ("Hay mejoras de defensa por investigar (T): daño y cadencia de torretas, vida de las máquinas y reparación.", "There are defence upgrades to research (T): turret damage and fire rate, machine hit points and repair."),
        ["learn.port"] = ("Con el motor de salto puedes construir un puerto de carga (B): saca lingotes de tu bodega a una cinta, así lo que mines en otro sistema alimenta tu fábrica.", "With the jump drive you can build a cargo port (B): it takes ingots out of your hold onto a belt, so what you mine in another system feeds your factory."),
        ["hud.defense"] = ("Mejoras de defensa: daño de torretas +{0}%, cadencia +{1}%, vida de máquinas +{2}%, reparación +{3}%, casco de la nave +{4}%, arma de la nave +{5}%", "Defence upgrades: turret damage +{0}%, fire rate +{1}%, machine hit points +{2}%, repair +{3}%, ship hull +{4}%, ship weapon +{5}%"),
        ["hud.level"] = ("{0} (nivel {1}/{2})", "{0} (level {1}/{2})"),
        ["hud.researchDone"] = ("Sin investigación elegida (T). Hecho: {0}", "No research selected (T). Done: {0}"),
        ["hud.speed"] = ("velocidad x{0}", "speed x{0}"),
        ["hud.paused"] = ("PAUSA", "PAUSED"),
        ["hud.ess.hull"] = ("Casco {0}/{1}", "Hull {0}/{1}"),
        ["hud.ess.step"] = ("Paso: {0}", "Step: {0}"),
        ["hud.ess.more"] = ("H: más detalles", "H: more details"),
        ["btn.details"] = ("Detalles (H)", "Details (H)"),
        ["btn.detailsHide"] = ("Menos (H)", "Less (H)"),
        ["hud.flightSpeed"] = ("{0} celdas/s", "{0} cells/s"),
        ["hud.tool.pointer"] = ("Sin herramienta: clic inspecciona", "No tool: click inspects"),
        ["hud.tool"] = ("{0}  -  {1}", "{0}  -  {1}"),
        ["mark.dist"] = ("{0} celdas", "{0} cells"),
        ["mark.base"] = ("Base", "Base"),
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
        ["status.noRecipe"] = ("sin receta: elige una (Tab y luego los botones)", "no recipe: choose one (Tab, then the buttons)"),
        ["status.drillUnlinked"] = ("sin yacimiento: construye a menos de 16 celdas de uno descubierto", "no deposit: build within 16 cells of a discovered one"),
        ["status.drillEmpty"] = ("el yacimiento se ha agotado", "the deposit is empty"),
        ["status.drillIdle"] = ("esperando para taladrar", "waiting to drill"),
        ["status.labNone"] = ("sin investigación elegida: pulsa T", "no research chosen: press T"),
        ["status.labWorking"] = ("investigando {0}", "researching {0}"),
        ["status.depotOk"] = ("entrega a tu bodega", "delivering to your hold"),
        ["status.depotCap"] = ("tienes {1} de {0}, lo máximo que entrega un depósito: gasta algunos para seguir", "you hold {1} {0}, the most a depot delivers: spend some to continue"),
        ["status.depotFull"] = ("bodega llena: esperando sitio", "hold full: waiting for room"),
        ["status.repairerReady"] = ("listo: repara las máquinas a menos de {0} celdas (lingotes {1}/{2})", "ready: repairs machines within {0} cells (ingots {1}/{2})"),
        ["status.repairerEmpty"] = ("sin lingotes: llévale lingotes por cinta", "out of ingots: bring it ingots by belt"),
        ["status.unloaderReady"] = ("saca de tu bodega (quedan {0}, reserva {1})", "unloading from your hold ({0} left, reserve {1})"),
        ["status.unloaderEmpty"] = ("tu bodega no tiene más de {0} de lo que saca este puerto: no saca nada", "your hold has no more than {0} of what this port carries: nothing to unload"),
        ["status.unloaderNoBelt"] = ("sin cinta de salida: conecta una", "no output belt: connect one"),
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
        ["toast.holdFull"] = ("¡Bodega llena! La minería y los depósitos se paran. Gasta objetos construyendo (módulos, estaciones) para que la fábrica pueda entregar; la minería deja libre el 25% de la bodega para ella.",
                              "Hold full! Mining and depots stop. Spend items by building (modules, stations) so the factory can deliver; mining keeps 25% of the hold free for it."),
        ["toast.language"] = ("Idioma: español", "Language: English"),
        ["toast.beltFacing"] = ("Las cintas sueltas miran hacia {0}", "Single belts now face {0}"),
        ["toast.depositSelected"] = ("Yacimiento de {0} elegido. Pulsa M para minarlo.", "{0} deposit selected. Press M to mine it."),
        ["toast.miningStopped"] = ("Minería parada. Pulsa M para volver a minar.", "Mining stopped. Press M to mine again."),
        ["toast.flyingToMine"] = ("Volando al yacimiento de {0}...", "Flying to the {0} deposit..."),
        ["toast.noDeposits"] = ("No conoces ningún yacimiento aquí. Escanea con K.", "No known deposits here. Scan with K."),
        ["toast.chooseSystem"] = ("Abre el mapa (G) y elige un sistema con las flechas.", "Open the map (G) and choose a system with the arrows."),
        ["toast.noDepositForDrill"] = ("No hay un yacimiento conocido de ese mineral al alcance del taladro. Escanea (K) o construye más cerca.",
                                       "No known deposit of that ore within the drill's reach. Scan (K) or build closer."),
        ["toast.oneRecipe"] = ("Esta máquina solo tiene una receta disponible.", "This machine has only one recipe available."),
        ["toast.nothingToResearch"] = ("No queda nada por investigar.", "Nothing left to research."),
        ["toast.recipe"] = ("Receta: {0}", "Recipe: {0}"),
        ["toast.nothingToRepair"] = ("Nada que reparar al alcance.", "Nothing to repair within reach."),
        ["toast.oreLow"] = ("Se acaba el yacimiento: queda un {1}% de {0} en este sistema.", "The deposit is running out: {1}% of the {0} is left in this system."),
        ["toast.saved"] = ("Partida guardada (tiempo de juego {0}). F9 la carga.", "Game saved (game time {0}). F9 loads it."),
        ["toast.loaded"] = ("Partida cargada (tiempo de juego {0}).", "Game loaded (game time {0})."),
        ["toast.noSave"] = ("No hay ninguna partida guardada. F6 guarda.", "There is no saved game. F6 saves."),
        ["toast.saveFailed"] = ("No se pudo guardar: {0}", "Could not save: {0}"),
        ["toast.loadFailed"] = ("No se pudo cargar: {0}", "Could not load: {0}"),
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
        ["obj.fly.hint"] = ("Mira la roca marrón y vuela hacia ella con W, o pulsa M: la nave vuela sola.", "Look at the brown rock and fly to it with W, or press M: the ship flies there by itself."),
        ["obj.mine"] = ("Mina 5 minerales", "Mine 5 ore"),
        ["obj.mine.hint"] = ("Pulsa M una vez (o el botón Minar): vuela y mina sola a 2 minerales por segundo. Otra M la para.", "Press M once (or the Mine button): it flies there and mines by itself at 2 ore per second. Another M stops it."),
        ["obj.refine"] = ("Consigue 3 lingotes de hierro", "Get 3 iron ingots"),
        ["obj.refine.hint"] = ("Pulsa F para encender la refinería de la nave: convierte mineral en lingotes, despacio.",
                               "Press F to switch the ship's refinery on: it turns ore into ingots, slowly."),
        ["obj.drill"] = ("Construye un taladro junto al yacimiento", "Build a drill next to the deposit"),
        ["obj.drill.hint"] = ("Elige Taladro en la barra (4) y apunta al suelo a menos de 16 celdas del yacimiento (mira hacia abajo); clic para colocarlo. Cuesta lingotes.",
                              "Choose Drill in the bar (4), aim at the ground within 16 cells of the deposit (look down) and click to place it. It costs ingots."),
        ["obj.smelter"] = ("Construye un horno", "Build a smelter"),
        ["obj.smelter.hint"] = ("Elige Horno (5) y colócalo delante del taladro; une ambos con cintas (1): clic al inicio y clic al final.",
                                "Choose Smelter (5) and place it in front of the drill; join them with belts (1): click the start, then click the end."),
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
        ["obj.expand"] = ("Salta a otro sistema", "Jump to another system"),
        ["obj.expand.hint"] = ("El yacimiento de casa se agota en un par de horas. Investiga la perforación de carbono y el motor de salto (T), escanea (K) y salta (G, J) a un sistema con hierro.",
                               "The home deposit runs out in a couple of hours. Research carbon drilling and the jump drive (T), scan (K) and jump (G, J) to a system with iron."),

        // ---- guided steps under the station rules ----
        ["obj.core"] = ("Construye el núcleo de la estación", "Build the station core"),
        ["obj.core.hint"] = ("Elige Módulo en la barra y colócalo cerca de la nave: el primero es el núcleo y solo se construye dentro de la estación. Cuesta lingotes.",
                             "Choose Module in the bar and place it near the ship: the first one is the core, and you can only build inside the station. It costs ingots."),
        ["obj.cargoPort"] = ("Construye un puerto de carga", "Build a cargo port"),
        ["obj.cargoPort.hint"] = ("El mineral no llega solo: vuela a la estación y descarga la bodega por un puerto a una cinta. El puerto de mineral lleva mineral a los hornos; el de carga, lingotes. Colócalo dentro de un módulo.",
                                  "Ore does not arrive by itself: fly to the station and unload your hold through a port onto a belt. The ore port carries ore to smelters; the cargo port carries ingots. Place it inside a module."),
        ["obj.mine.hint.station"] = ("Pulsa M una vez: la nave mina sola a 2 minerales por segundo hasta llenar la bodega. Luego vuelve a la estación para descargarla.",
                                     "Press M once: the ship mines by itself at 2 ore per second until the hold is full. Then fly back to the station to unload it."),
        ["obj.refine.hint.station"] = ("Pulsa F para encender la refinería de la nave: convierte mineral en lingotes, despacio. Los necesitas para construir el núcleo.",
                                       "Press F to switch the ship's refinery on: it turns ore into ingots, slowly. You need them to build the core."),
        ["obj.smelter.hint.station"] = ("Elige Horno (5) y colócalo dentro de un módulo, junto a un puerto de mineral (el de carga solo lleva lingotes); une ambos con cintas (1). Si no cabe, amplía la estación con otro módulo.",
                                        "Choose Smelter (5) and place it inside a module, next to an ore port (the cargo port carries only ingots); join them with belts (1). If it does not fit, grow the station with another module."),
        ["obj.depot.hint.station"] = ("Tiende cintas desde el horno hasta un Depósito (8) dentro de la estación: mete en tu bodega todo lo que le llega.",
                                      "Lay belts from the smelter to a Depot (8) inside the station: it puts everything that arrives into your hold."),
        ["learn.deliver"] = ("Tienes la bodega llena de mineral. Vuela a la estación y descárgala con un puerto de mineral: saca el mineral a una cinta que alimente tus hornos (el puerto de carga solo lleva lingotes).",
                             "Your hold is full of ore. Fly to the station and unload it with an ore port: it puts the ore onto a belt that feeds your smelters (the cargo port carries only ingots)."),

        // ---- build rejects under the station rules ----
        ["toast.reject.outsideStation"] = ("Solo puedes construir dentro de los módulos de la estación", "You can only build inside the station modules"),
        ["toast.reject.notAdjacent"] = ("El módulo debe tocar una cara de la estación", "A module must touch a face of the station"),
        ["toast.reject.moduleOccupied"] = ("Ese módulo tiene máquinas o cintas, o su retirada partiría la estación", "That module holds machines or belts, or removing it would split the station"),
        ["toast.reject.notAllowedHere"] = ("Eso no se puede construir aquí", "That cannot be built here"),
        ["toast.reject.overlap"] = ("Esa celda está ocupada", "That cell is taken"),
        ["toast.reject.outOfBounds"] = ("Fuera de los límites del mundo", "Out of the world limits"),
        ["module.core"] = ("Núcleo", "Core"),
        ["module.core.purpose"] = ("El corazón de una estación.", "The heart of a station."),
        ["module.cockpit"] = ("Cabina", "Cockpit"),
        ["module.cockpit.purpose"] = ("Puesto de vuelo del piloto.", "The pilot's flying position."),
        ["module.engine"] = ("Motor", "Engine"),
        ["module.engine.purpose"] = ("Da el empuje para volar.", "Provides the thrust to fly."),
        ["module.reactor"] = ("Reactor", "Reactor"),
        ["module.reactor.purpose"] = ("Energía (más adelante).", "Power (later)."),
        ["module.cargo"] = ("Bodega", "Cargo"),
        ["module.cargo.purpose"] = ("Contenedor de objetos.", "An item container."),
        ["module.workshop"] = ("Taller", "Workshop"),
        ["module.workshop.purpose"] = ("Espacio para construir máquinas.", "Build space for machines."),
        ["module.defence"] = ("Defensa", "Defence"),
        ["module.defence.purpose"] = ("Torretas y muros.", "Turrets and walls."),
        ["module.hull"] = ("Casco", "Hull"),
        ["module.hull.purpose"] = ("Estructura y cintas.", "Structure and belts."),
        ["structure.ship"] = ("Nave", "Ship"),
        ["structure.station"] = ("Estación", "Station"),
        ["hud.structure"] = ("{0}: {1}", "{0}: {1}"),
        ["hud.modules"] = ("Módulos: {0}", "Modules: {0}"),
        ["toast.reject.kindNotAllowed"] = ("Ese tipo de módulo no admite eso", "That module kind does not allow this"),
        ["toast.reject.noSuchStructure"] = ("Esa estructura no existe", "That structure does not exist"),
        ["toast.reject.noCockpitOrEngine"] = ("Para volar hacen falta una cabina y un motor", "Flying needs a cockpit and an engine"),
        ["learn.needEngine"] = ("Tu nave no puede volar todavía: hace falta al menos un módulo de cabina y uno de motor.", "Your ship cannot fly yet: it needs at least one cockpit module and one engine module."),
        ["obj.watchFactory"] = ("Mira salir la primera placa", "Watch the first plate come out"),
        ["obj.watchFactory.hint"] = ("La nave inicial mina a mano y guarda el mineral en el contenedor; el puerto de mineral lo lleva al horno y el ensamblador hace placas. Espera a que llegue la primera a tu bodega.",
                                     "The starter ship mines by hand into the container; the ore port feeds the smelter and the assembler makes plates. Wait for the first one to reach your hold."),
        ["obj.foundStation"] = ("Funda una estación", "Found a station"),
        ["obj.foundStation.hint"] = ("Pulsa I para elegir la herramienta de estación, apunta al espacio junto a tu nave (el volumen verde = se puede) y haz clic: empieza con un módulo núcleo. Cuesta objetos de la bodega.",
                                     "Press I to pick the station tool, aim at the space beside your ship (the green volume = it can be founded) and click: it starts with a core module. It costs items from the hold."),
        ["obj.addModule"] = ("Añade módulos", "Add modules"),
        ["obj.addModule.hint"] = ("Amplía tu nave o tu estación con más módulos (taller, defensa, casco) pegados a una cara: cada tipo permite construir cosas distintas.",
                                  "Grow your ship or station with more modules (workshop, defence, hull) touching a face: each kind allows different things to be built."),
        ["toast.reject.notEnoughItems"] = ("No tienes los objetos en la bodega", "Not enough items in the hold"),

        // ---- station modules ----
        ["ui.module"] = ("Módulo", "Module"),
        ["ui.core"] = ("Núcleo", "Core"),
        ["ui.foundStation"] = ("Fundar estación", "Found station"),
        ["tip.foundStation"] = ("Funda una estación nueva: un módulo núcleo en el espacio junto a tu nave.", "Founds a new station: a core module in the space beside your ship."),
        ["hint.foundStation"] = ("{0}: apunta al espacio junto a tu nave (verde = se puede fundar) y haz clic. Debe estar a tu alcance y sin tocar otras estructuras.   Esc: dejar de construir",
                                 "{0}: aim at the space beside your ship (green = it can be founded) and click. It must be within reach and not touch other structures.   Esc: stop building"),
        ["hint.buildModuleKind"] = ("Módulo: {0} (N cambia el tipo). Apunta junto a un módulo de tu nave o estación (verde = se puede) y haz clic.   Clic derecho en un módulo vacío: retirarlo (devuelve la mitad).   Esc: dejar de construir",
                                    "Module: {0} (N changes the kind). Aim next to a module of your ship or station (green = it can be built) and click.   Right-click an empty module: remove it (refunds half).   Esc: stop building"),
        ["toast.stationFounded"] = ("¡Estación fundada! Añádele módulos con N.", "Station founded! Add modules to it with N."),
        ["hud.flightReady"] = ("lista para volar", "ready to fly"),
        ["hud.flightNot"] = ("no puede volar: falta cabina o motor", "cannot fly: needs a cockpit and an engine"),
        ["hud.modulesDetail"] = ("Módulos: {0}", "Modules: {0}"),
        ["hint.buildModule"] = ("Construyendo: {0}. Apunta a donde quieras el módulo (verde = se puede) y haz clic.   Clic derecho en un módulo vacío: retirarlo (devuelve la mitad).   Esc: dejar de construir",
                                "Building: {0}. Aim where you want the module (green = it can be built) and click.   Right-click an empty module: remove it (refunds half).   Esc: stop building"),
        ["tip.module"] = ("{0} de estación: una caja de 8 x 8 x 4 celdas donde se puede construir.", "Station {0}: an 8 x 8 x 4 cell box where you can build."),
        ["why.moduleMissing"] = ("Te falta: {0}", "Missing: {0}"),
        ["why.moduleExists"] = ("Ya hay un módulo ahí", "There is already a module there"),
        ["hud.modules"] = ("Módulos {0}", "Modules {0}"),
        ["mark.station"] = ("Estación", "Station"),

        // ---- main menu, pause menu and settings ----
        ["menu.title"] = ("Starfoundry", "Starfoundry"),
        ["menu.subtitle"] = ("Construye una fábrica en el espacio", "Build a factory in space"),
        ["menu.continue"] = ("Continuar", "Continue"),
        ["menu.continueInfo"] = ("{0} - tiempo de juego {1}", "{0} - play time {1}"),
        ["menu.noSave"] = ("No hay partida guardada", "No saved game"),
        ["menu.save.quicksave"] = ("guardado rápido", "quick save"),
        ["menu.save.autosave"] = ("autoguardado", "autosave"),
        ["menu.new"] = ("Nueva partida", "New game"),
        ["menu.settings"] = ("Ajustes", "Settings"),
        ["menu.controls"] = ("Controles", "Controls"),
        ["menu.quit"] = ("Salir", "Quit"),
        ["menu.back"] = ("Volver", "Back"),
        ["menu.hint"] = ("Flechas y Intro para moverte, Esc para volver", "Arrows and Enter to move, Esc to go back"),
        ["pause.title"] = ("Pausa", "Paused"),
        ["pause.resume"] = ("Reanudar", "Resume"),
        ["pause.save"] = ("Guardar (F6)", "Save (F6)"),
        ["pause.load"] = ("Cargar (F9)", "Load (F9)"),
        ["pause.mainMenu"] = ("Menú principal", "Main menu"),
        ["pause.saved"] = ("Guardado ({0}).", "Saved ({0})."),
        ["pause.loaded"] = ("Partida cargada ({0}).", "Game loaded ({0})."),
        ["pause.saveFailed"] = ("No se pudo guardar.", "Could not save."),
        ["pause.noSave"] = ("No hay guardado rápido que cargar.", "There is no quick save to load."),
        ["pause.loadFailed"] = ("No se pudo cargar.", "Could not load."),
        ["settings.title"] = ("Ajustes", "Settings"),
        ["settings.language"] = ("Idioma", "Language"),
        ["settings.languageValue"] = ("Español", "English"),
        ["settings.sensitivity"] = ("Sensibilidad del ratón: x{0}", "Mouse sensitivity: x{0}"),
        ["settings.fov"] = ("Campo de visión: {0}°", "Field of view: {0}°"),
        ["settings.volume"] = ("Volumen general: {0}%", "Master volume: {0}%"),
        ["settings.invertY"] = ("Invertir el ratón en vertical", "Invert mouse vertically"),
        ["settings.fullscreen"] = ("Pantalla completa", "Fullscreen"),
        ["settings.reset"] = ("Valores por defecto", "Reset to defaults"),
        ["settings.tip.language"] = ("Cambia el idioma de todo el juego (también L en la partida).", "Changes the language of the whole game (also L in game)."),
        ["settings.tip.sensitivity"] = ("Multiplica la velocidad con la que gira la vista: de 0,5 a 2.", "Scales how fast the view turns: 0.5 to 2."),
        ["settings.tip.fov"] = ("Campo de visión de la cabina: de 70 a 100 grados.", "Cockpit field of view: 70 to 100 degrees."),
        ["settings.tip.volume"] = ("Se guarda para cuando haya sonido; todavía no hay audio.", "Saved for when there is sound; there is no audio yet."),
        ["settings.tip.invertY"] = ("Mover el ratón hacia arriba mira hacia abajo.", "Moving the mouse up looks down."),
        ["settings.tip.fullscreen"] = ("Pantalla completa (F11 en la partida).", "Fullscreen (F11 in game)."),
        ["settings.file"] = ("Se guardan solos en settings.cfg", "Saved automatically to settings.cfg"),

        // ---- help overlay ----
        ["help.text"] = (
            "CONTROLES   (F1 para cerrar)\n\n" +
            "Ratón\n" +
            "  Mover el ratón   mirar (la vista es la cabina de la nave); la mirilla del centro es tu puntería\n" +
            "  Clic izquierdo   inspeccionar lo que apuntas o elegir un yacimiento\n" +
            "                   construyendo: apunta al suelo y coloca (cintas: clic al inicio y clic al final, o arrastra)\n" +
            "  Clic derecho     demoler lo que apuntas (se devuelve el coste)\n" +
            "  Tab              soltar el ratón para usar los botones (clic en el juego para volver a mirar)\n\n" +
            "Nave\n" +
            "  W S   avanzar / retroceder hacia donde miras     A D   desplazarse a los lados     Espacio / C   subir / bajar\n" +
            "  E   sentarte / levantarte del asiento del piloto (de pie: WASD camina, Mayús corre y no se vuela)\n  M   minar (vuela primero al yacimiento)      F   refinería       K   escanear       V   armas\n  Marcadores: yacimiento elegido, enemigo más cercano y tu base; en el borde de la pantalla señalan hacia dónde mirar\n\n" +
            "Construcción\n" +
            "  1-9, 0, Q, B, U   elegir qué construir (N: módulo de estación)       Esc   dejar de construir\n" +
            "  Y   cambiar receta       R   girar cintas sueltas       Z / X   cambiar de capa\n" +
            "  N   módulo de estación (el primero es el núcleo; solo se construye dentro de los módulos)\n" +
            "  Estructuras: N elige la herramienta de módulos y, pulsada otra vez, cambia su tipo (cabina, motor, reactor, bodega, taller, defensa, casco); I elige fundar una estación junto a la nave; cada tipo de módulo admite máquinas distintas\n" +
            "  Clic en una máquina sin herramienta: ver su estado y cambiar su receta (Tab para usar sus botones).\n  Retroceso   reparar todas las máquinas dañadas a tu alcance (cuesta la mitad de su coste, en proporción al daño)\n\n" +
            "Otros\n" +
            "  T   elegir investigación      G   mapa estelar (flechas y J para saltar)\n" +
            "  P   pausa       + / -   velocidad       L   idioma       O   pasos       [ ]   cambiar yacimiento\n  H   más / menos detalles en el panel de estado       F6   guardar       F9   cargar (se guarda sola cada 5 minutos)       F11   pantalla completa",
            "CONTROLS   (F1 to close)\n\n" +
            "Mouse\n" +
            "  Move the mouse   look (the view is the ship's cockpit); the centre crosshair is your aim\n" +
            "  Left click       inspect what you aim at or pick a deposit\n" +
            "                   building: aim at the ground and place (belts: click the start and click the end, or drag)\n" +
            "  Right click      demolish what you aim at (the cost is refunded)\n" +
            "  Tab              free the mouse to use the buttons (click in the game to look again)\n\n" +
            "Ship\n" +
            "  W S   forward / back where you look     A D   strafe     Space / C   up / down\n" +
            "  E   sit at / stand up from the pilot seat (standing: WASD walks, Shift runs, no flying)\n  M   mine (flies to the deposit first)      F   refinery       K   scan       V   weapons\n  Markers: the selected deposit, the nearest enemy and your base; at the screen edge they point where to look\n\n" +
            "Building\n" +
            "  1-9, 0, Q, B, U   choose what to build (N: station module)       Esc   stop building\n" +
            "  Y   change recipe       R   rotate single belts       Z / X   change layer\n" +
            "  N   station module (the first one is the core; you can only build inside modules)\n" +
            "  Structures: N picks the module tool and, pressed again, changes its kind (cockpit, engine, reactor, cargo, workshop, defence, hull); I picks founding a station beside the ship; each module kind allows different machines\n" +
            "  With no tool, click a machine: see its status and change its recipe (Tab to use its buttons).\n  Backspace   repair every damaged machine within reach (half the build cost, in proportion to the damage)\n\n" +
            "Other\n" +
            "  T   choose research      G   star map (arrows and J to jump)\n" +
            "  P   pause       + / -   speed       L   language       O   steps       [ ]   change deposit\n  H   more / fewer details in the status panel       F6   save       F9   load (it also saves itself every 5 minutes)       F11   fullscreen"),
    };
}
