namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the station-first interface (ADR 0039): the ship workbench panel, the machine panel and the "build in a station" hint, merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> StationTable = new()
    {
        // the hint where the build tool would be
        ["build.stationFirst"] = ("La automatización se construye en una estación", "Automation is built in a station"),

        // the workbench panel
        ["bench.card.hintPanel"] = ("F: abrir el banco de trabajo", "F: open the workbench"),
        ["bench.panel.title"] = ("Banco de trabajo de la nave", "Ship workbench"),
        ["bench.panel.slow"] = ("Manual y lento: {0} s por fabricación", "Manual, slow: {0} s per craft"),
        ["bench.panel.seconds"] = ("{0} s", "{0} s"),
        ["bench.panel.recipes"] = ("Recetas", "Recipes"),
        ["bench.panel.takes"] = ("Toma", "Takes"),
        ["bench.panel.makes"] = ("Hace", "Makes"),
        ["bench.panel.queue1"] = ("+1", "+1"),
        ["bench.panel.queue5"] = ("x{0}", "x{0}"),
        ["bench.panel.queueTip"] = ("Añade fabricaciones a la cola (como máximo {0} esperando).", "Adds crafts to the queue (at most {0} waiting)."),
        ["bench.panel.noInputs"] = ("Faltan materiales en la bodega para empezar", "Not enough materials in the hold to start"),
        ["bench.panel.queueTitle"] = ("Cola ({0}/{1})", "Queue ({0}/{1})"),
        ["bench.panel.queueEmpty"] = ("Nada en espera", "Nothing waiting"),
        ["bench.panel.clear"] = ("Cancelar todo", "Cancel all"),
        ["bench.panel.clearTip"] = ("Cancela la fabricación en curso (los materiales vuelven a la bodega) y vacía la cola.", "Cancels the craft in progress (its inputs return to the hold) and empties the queue."),
        ["bench.panel.queueFull"] = ("La cola está llena", "The queue is full"),
        ["bench.panel.inProgress"] = ("En curso: {0}", "In progress: {0}"),
        ["bench.panel.crafting"] = ("Fabricando", "Crafting"),
        ["bench.panel.idle"] = ("Inactivo: añade algo a la cola", "Idle: add something to the queue"),
        ["bench.panel.hold"] = ("Bodega: {0}", "Hold: {0}"),
        ["bench.panel.close"] = ("Cerrar", "Close"),
        ["bench.panel.hint"] = ("F o Esc para cerrar", "F or Esc to close"),
        ["bench.panel.inSlot"] = ("Entrada", "Input"),
        ["bench.panel.outSlot"] = ("Salida", "Output"),
        ["bench.panel.next"] = ("Siguiente: {0}", "Next: {0}"),

        // the machine panel
        ["machine.panel.status"] = ("Estado: {0}", "Status: {0}"),
        ["machine.panel.running"] = ("Funcionando", "Running"),
        ["machine.panel.waitingInput"] = ("Esperando materiales", "Waiting for input"),
        ["machine.panel.outputFull"] = ("Salida llena", "Output full"),
        ["machine.panel.noPower"] = ("Sin energía", "No power"),
        ["machine.panel.off"] = ("Apagada", "Off"),
        ["machine.panel.recipes"] = ("Receta", "Recipe"),
        ["machine.panel.pick"] = ("Usar", "Use"),
        ["machine.panel.current"] = ("En uso", "In use"),
        ["machine.panel.takes"] = ("Toma", "Takes"),
        ["machine.panel.makes"] = ("Hace", "Makes"),
        ["machine.panel.perMinute"] = ("{0} por minuto", "{0} per minute"),
        ["machine.panel.perMinuteNow"] = ("Ahora: {0} por min (a plena potencia: {1})", "Now: {0} per min (at full power: {1})"),
        ["machine.panel.enable"] = ("Encender", "Switch on"),
        ["machine.panel.disable"] = ("Apagar", "Switch off"),
        ["machine.panel.input"] = ("Entrada", "Input"),
        ["machine.panel.output"] = ("Salida", "Output"),
        ["machine.panel.noRecipes"] = ("Esta máquina no tiene recetas", "This machine has no recipes"),

        // the key panel
        ["key.interactBench"] = ("Abrir el banco de trabajo (manual, lento)", "Open the workbench (manual, slow)"),
        ["key.ctx.bench"] = ("Banco de trabajo", "Workbench"),
        ["key.ctx.machine"] = ("Máquina", "Machine"),
        ["key.benchQueue"] = ("Añadir a la cola", "Add to the queue"),
        ["key.machineRecipe"] = ("Elegir la receta", "Pick the recipe"),
        ["key.closePanel"] = ("Cerrar el panel", "Close the panel"),
        ["key.stationFirst"] = ("Construir: solo en una estación", "Build: stations only"),
        ["key.why.stationFirst"] = ("La automatización se construye en una estación, no en la nave.", "Automation is built in a station, not on the ship."),
    };
}
