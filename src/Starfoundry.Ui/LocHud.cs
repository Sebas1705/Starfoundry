using System.Runtime.CompilerServices;

namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the HUD detail levels, the extra HUD rows and the interface sound settings. Merged into <see cref="Loc"/>'s table when the assembly loads.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> HudDetailTable = new()
    {
        // the levels
        ["hud.detail.minimal"] = ("MÍNIMO", "MINIMAL"),
        ["hud.detail.standard"] = ("ESTÁNDAR", "STANDARD"),
        ["hud.detail.detailed"] = ("DETALLADO", "DETAILED"),
        ["cp.detail"] = ("DETALLE", "DETAIL"),
        ["toast.hudDetail"] = ("Detalle del HUD: {0}", "HUD detail: {0}"),
        ["key.hudDetail"] = ("Detalle del HUD: {0} (Mayús+F3 cambia)", "HUD detail: {0} (Shift+F3 switches)"),
        ["settings.hudDetail"] = ("Detalle del HUD: {0}", "HUD detail: {0}"),
        ["settings.tip.hudDetail"] = ("Cuánta información da el HUD: mínimo (solo lo vital), estándar o detallado (riesgo de apagón, módulos dañados, ingresos por minuto, progreso del objetivo). Mayús+F3 también lo cambia.", "How much the HUD says: minimal (the vitals only), standard or detailed (brownout risk, damaged modules, income per minute, objective progress). Shift+F3 also changes it."),

        // interface sounds
        ["settings.uiClick"] = ("Sonido al pulsar botones", "Click sound on buttons"),
        ["settings.tip.uiClick"] = ("Un clic suave en el momento de pulsar un botón del menú o del HUD.", "A soft click the moment a menu or HUD button is pressed."),
        ["settings.uiHover"] = ("Tic al pasar sobre botones", "Tick when hovering buttons"),
        ["settings.tip.uiHover"] = ("Un tic muy suave al mover el puntero sobre un botón.", "A very quiet tick when the pointer moves onto a button."),

        // extra rows of the ship card
        ["hud.row.powerRisk.demand"] = ("Riesgo de apagón: el consumo supera la producción", "Brownout risk: demand is above supply"),
        ["hud.row.powerRisk.fuel"] = ("Riesgo de apagón: combustible para {0}", "Brownout risk: fuel for {0}"),
        ["hud.row.powerOk"] = ("Energía estable", "Power stable"),
        ["hud.row.modulesOk"] = ("Módulos intactos", "Modules sound"),
        ["hud.row.modulesDamaged"] = ("{0} de {1} módulos dañados", "{0} of {1} modules damaged"),
        ["hud.row.shield"] = ("Escudo: absorbe {0} %", "Shield: absorbs {0} %"),

        // the target card
        ["hud.target.title"] = ("OBJETIVO", "TARGET"),
        ["hud.target.enemy"] = ("{0}  {1} celdas", "{0}  {1} cells"),
        ["hud.target.hull"] = ("casco {0} %", "hull {0} %"),
        ["hud.target.closing"] = ("se acerca {0} celdas/s", "closing {0} cells/s"),
        ["hud.target.opening"] = ("se aleja {0} celdas/s", "opening {0} cells/s"),
        ["hud.target.steady"] = ("a distancia constante", "steady range"),
        ["hud.target.deposit"] = ("{0}: quedan {1}, {2} celdas", "{0}: {1} left, {2} cells"),
        ["hud.target.mining"] = ("minando {0}: quedan {1}", "mining {0}: {1} left"),
        ["hud.weapons.laser"] = ("Láser", "Laser"),
        ["hud.weapons.guns"] = ("Cañones", "Guns"),
        ["hud.weapons.armed"] = ("{0}: armado", "{0}: armed"),
        ["hud.weapons.safe"] = ("{0}: en reposo", "{0}: safe"),

        // the hold
        ["hud.hold.forecast"] = ("llena en {0}", "full in {0}"),
        ["hud.hold.income"] = ("+{0}/min", "+{0}/min"),
        ["hud.hold.incomeTip"] = ("Ingresos de la bodega: {0} por minuto (últimos 30 s). Llena en {1}.", "Hold income: {0} per minute (last 30 s). Full in {1}."),

        // the objective line
        ["hud.obj.step"] = ("Paso {0}/{1}: {2}", "Step {0}/{1}: {2}"),
    };

#pragma warning disable CA2255 // a library initialiser on purpose: it keeps this table out of the shared Loc constructor
    [ModuleInitializer]
    internal static void RegisterHudDetail()
    {
        foreach (var (key, value) in HudDetailTable) Table[key] = value;
    }
}
