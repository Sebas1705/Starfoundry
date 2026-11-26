namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the personal inventory, the container panel and the reactor fuel slot (ADR 0035), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> InventoryTable = new()
    {
        // panel
        ["inv.title"] = ("Inventario", "Inventory"),
        ["inv.you"] = ("Tú", "You"),
        ["inv.slots"] = ("{0}/{1} huecos", "{0}/{1} slots"),
        ["inv.empty"] = ("Vacío", "Empty"),
        ["inv.sort"] = ("Ordenar", "Sort"),
        ["inv.sort.tip"] = ("Junta las pilas del mismo objeto y las ordena", "Merges stacks of the same item and orders them"),
        ["inv.bin"] = ("Papelera", "Bin"),
        ["inv.bin.tip"] = ("Suelta aquí lo que quieras tirar (te pedirá confirmación)", "Drop here what you want to throw away (it asks first)"),
        ["inv.bin.confirm"] = ("¿Tirar {0} x {1}? No se puede deshacer.", "Throw away {0} x {1}? This cannot be undone."),
        ["inv.bin.yes"] = ("Tirar", "Throw away"),
        ["inv.bin.no"] = ("Cancelar", "Cancel"),
        ["inv.hint"] = ("Arrastra: pila entera  -  clic derecho y arrastra: mitad  -  Mayús + clic: mover al otro panel  -  clic derecho: menú", "Drag: whole stack  -  right-drag: half  -  Shift + click: move to the other panel  -  right click: menu"),
        ["inv.hint.keys"] = ("Teclado: flechas mueven, Intro coge y suelta, Mayús + Intro mueve al otro panel, Supr tira", "Keyboard: arrows move, Enter picks up and drops, Shift + Enter moves to the other panel, Del throws away"),
        ["inv.picked"] = ("En la mano: {0} x {1}. Elige el hueco de destino y pulsa Intro.", "In hand: {0} x {1}. Pick the target slot and press Enter."),
        ["inv.menu.one"] = ("Tirar uno al suelo", "Drop one"),
        ["inv.menu.half"] = ("Tirar la mitad al suelo", "Drop half"),
        ["inv.menu.all"] = ("Tirar todo al suelo", "Drop all"),
        ["inv.menu.toHold"] = ("Mover a la bodega", "Move to hold"),
        ["inv.menu.toBag"] = ("Mover a la mochila", "Move to bag"),
        ["inv.menu.toOther"] = ("Mover al otro panel", "Move to the other panel"),
        ["inv.close"] = ("Cerrar el inventario", "Close the inventory"),
        ["inv.legacy"] = ("Esta partida es anterior al inventario personal: no está disponible aquí. Empieza una partida nueva para usarlo.", "This game predates the personal inventory: it is not available here. Start a new game to use it."),
        ["inv.nothing"] = ("Nada que mostrar", "Nothing to show"),
        ["inv.out"] = ("Fuera de alcance: acércate o atraca", "Out of reach: move closer or dock"),
        ["inv.filter.all"] = ("Todo", "All"),
        ["inv.filter.ore"] = ("Minerales", "Ores"),
        ["inv.filter.parts"] = ("Piezas", "Parts"),
        ["inv.filter.fuel"] = ("Combustible", "Fuel"),
        ["inv.filter.ammo"] = ("Munición", "Ammo"),
        ["inv.tip.stack"] = ("Una pila: hasta {0}  -  {1}", "One stack: up to {0}  -  {1}"),
        ["inv.tip.fuel"] = ("Combustible de reactor: dura {0}", "Reactor fuel: burns for {0}"),
        ["inv.tip.usedFor"] = ("Sirve para fabricar: {0}", "Used to make: {0}"),
        ["inv.tip.usedBy"] = ("Lo usan: {0}", "Used by: {0}"),

        // the container beside the bag
        ["inv.tab.hold"] = ("Bodega", "Hold"),
        ["inv.tab.stock"] = ("Almacén", "Stock"),
        ["inv.tab.reactor"] = ("Reactor", "Reactor"),
        ["inv.tab.reactorN"] = ("Reactor {0}", "Reactor {0}"),
        ["inv.tab.machine"] = ("Máquina", "Machine"),
        ["inv.hold.title"] = ("Bodega de la nave", "Ship hold"),
        ["inv.stock.title"] = ("Almacén de la estación", "Station stock"),
        ["inv.stock.none"] = ("Atraca en una estación tuya o acércate a ella para ver su almacén", "Dock at one of your stations or move close to see its stock"),
        ["inv.machine.title"] = ("{0}", "{0}"),
        ["inv.machine.in"] = ("Entrada", "Input"),
        ["inv.machine.out"] = ("Salida (solo se puede sacar)", "Output (take only)"),
        ["inv.machine.pick"] = ("Apunta a una máquina o a un reactor y pulsa {0} para ver su contenido", "Aim at a machine or a reactor and press {0} to see what is inside"),

        // the reactor
        ["inv.reactor.title"] = ("Ranura de combustible", "Fuel slot"),
        ["inv.reactor.slot"] = ("Arrastra aquí hidrógeno o celdas de combustible", "Drag hydrogen or fuel cells here"),
        ["inv.reactor.burn"] = ("Combustible para {0}", "Fuel for {0}"),
        ["inv.reactor.cold"] = ("Sin combustible: solo da su energía de emergencia", "No fuel: it only gives its emergency power"),
        ["inv.reactor.first"] = ("El reactor quema primero lo que hay en su ranura.", "The reactor burns what is in its slot first."),
        ["inv.feed.title"] = ("Alimentación automática (cuando la ranura se vacía)", "Auto-feed (when the slot runs empty)"),
        ["inv.feed.none"] = ("Ninguna", "None"),
        ["inv.feed.hold"] = ("De la bodega", "From the hold"),
        ["inv.feed.inventory"] = ("De mi inventario", "From my inventory"),
        ["inv.feed.holdtheninventory"] = ("Bodega y luego inventario", "Hold, then inventory"),
        ["inv.feed.none.tip"] = ("El reactor solo quema lo que tú pongas en su ranura.", "The reactor burns only what you put in its slot."),
        ["inv.feed.hold.tip"] = ("Como siempre: toma combustible del almacén de la estación (si estás atracado) y de la bodega.", "As it always was: it takes fuel from the station stock (when docked) and from the hold."),
        ["inv.feed.inventory.tip"] = ("Toma combustible de tu inventario personal.", "It takes fuel from your personal inventory."),
        ["inv.feed.holdtheninventory.tip"] = ("Toma primero de la bodega y, si no queda, de tu inventario.", "It takes from the hold first and from your inventory when that runs dry."),

        // toasts
        ["inv.toast.fed"] = ("Reactor: alimentación {0}", "Reactor: auto-feed {0}"),
        ["inv.toast.sorted"] = ("Inventario ordenado", "Inventory sorted"),
        ["inv.toast.binned"] = ("Tirado: {0} x {1}", "Thrown away: {0} x {1}"),
        ["inv.toast.moved"] = ("{0} x {1}: {2}", "{0} x {1}: {2}"),
        ["inv.toast.noOther"] = ("Elige antes dónde mover los objetos (pestañas de la derecha)", "Pick where to move things first (the tabs on the right)"),
        ["toast.reject.wrongItem"] = ("Eso no cabe ahí: la ranura no admite ese objeto", "That does not go there: the slot does not take that item"),

        // key panel and help
        ["key.inventory"] = ("Inventario", "Inventory"),
        ["key.ctx.inventory"] = ("Inventario", "Inventory"),
        ["key.invDrag"] = ("Arrastra una pila (clic derecho: mitad)", "Drag a stack (right button: half)"),
        ["key.invQuick"] = ("Mayús + clic: al otro panel", "Shift + click: to the other panel"),
        ["key.invKeys"] = ("Flechas, Intro: coger y soltar", "Arrows, Enter: pick up and drop"),
        ["key.invClose"] = ("Cerrar inventario", "Close inventory"),
        ["help.inventory"] = (
            "\n\nInventario personal\n" +
            "  El inventario (24 huecos) se abre junto a la bodega, el almacén de la estación, el reactor o la máquina que apuntas. Arrastra una pila de un hueco a otro (clic derecho: la mitad),\n" +
            "       Mayús + clic la mueve al otro panel, el botón Ordenar junta y ordena, los filtros atenúan lo que no es de su tipo y la papelera tira lo que sueltes (pide confirmación).\n" +
            "  El reactor tiene una ranura de combustible: quema primero lo que hay en ella. Su selector de alimentación automática elige de dónde toma más cuando se vacía (ninguna, la bodega, tu inventario, o la bodega y luego el inventario).\n",
            "\n\nPersonal inventory\n" +
            "  The inventory (24 slots) opens beside the hold, the station stock, the reactor or the machine you are aiming at. Drag a stack from slot to slot (right button: half),\n" +
            "       Shift + click moves it to the other panel, Sort merges and orders, the filters dim what is not of their kind and the bin throws away what you drop on it (it asks first).\n" +
            "  A reactor has a fuel slot: it burns what is in it first. Its auto-feed selector chooses where it takes more from when the slot runs empty (none, the hold, your inventory, or the hold and then the inventory).\n"),

        // icon names
        ["icon.tool:inventory"] = ("Inventario", "Inventory"),
        ["icon.tool:sort"] = ("Ordenar", "Sort"),
        ["icon.tool:bin"] = ("Papelera", "Bin"),
        ["icon.tool:invAll"] = ("Todo", "All"),
        ["icon.tool:invOre"] = ("Minerales", "Ores"),
        ["icon.tool:invParts"] = ("Piezas", "Parts"),
        ["icon.tool:invFuel"] = ("Combustible", "Fuel"),
        ["icon.tool:invAmmo"] = ("Munición", "Ammo"),
    };
}
