namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the cockpit displays, the HUD modes and the smooth-look setting, merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> CockpitTable = new()
    {
        // pages (the tab strip of every display)
        ["cp.page.flight"] = ("VUELO", "FLIGHT"),
        ["cp.page.nav"] = ("NAV", "NAV"),
        ["cp.page.systems"] = ("SISTEMAS", "SYSTEMS"),
        ["cp.page.weapons"] = ("ARMAS", "WEAPONS"),

        // flight page
        ["cp.speed"] = ("VELOCIDAD", "SPEED"),
        ["cp.unit"] = ("celdas/s", "cells/s"),
        ["cp.thrust"] = ("EMPUJE", "THRUST"),
        ["cp.boost"] = ("IMPULSO", "BOOST"),
        ["cp.hull"] = ("CASCO", "HULL"),
        ["cp.power"] = ("ENERGÍA", "POWER"),
        ["cp.fuel"] = ("HIDRÓGENO", "HYDROGEN"),
        ["cp.hold"] = ("BODEGA", "HOLD"),
        ["cp.attitude"] = ("ACTITUD", "ATTITUDE"),
        ["cp.vector"] = ("VELOCIDAD", "VELOCITY"),
        ["cp.still"] = ("QUIETA", "STILL"),
        ["cp.drift"] = ("MARCHA ATRÁS", "REVERSE"),
        ["cp.noPower"] = ("SIN REACTOR", "NO REACTOR"),
        ["cp.docked"] = ("ATRACADA", "DOCKED"),
        ["cp.jumping"] = ("SALTANDO", "JUMPING"),
        ["cp.rebuilding"] = ("RECONSTRUYENDO", "REBUILDING"),

        // nav page
        ["cp.nav.targets"] = ("OBJETIVOS", "TARGETS"),
        ["cp.nav.none"] = ("Nada a la vista. K escanea.", "Nothing in range. K scans."),
        ["cp.nav.auto"] = ("PILOTO AUTO", "AUTOPILOT"),
        ["cp.nav.autoOn"] = ("PILOTO AUTO: EN CURSO", "AUTOPILOT: ENGAGED"),
        ["cp.nav.dockAssist"] = ("APROXIMACIÓN", "DOCK ASSIST"),
        ["cp.nav.dockAssistOn"] = ("APROXIMANDO", "APPROACHING"),
        ["cp.nav.scan"] = ("ESCANEAR", "SCAN"),
        ["cp.nav.scanWait"] = ("ESCANEO...", "SCANNING..."),
        ["cp.nav.system"] = ("SISTEMA", "SYSTEM"),
        ["cp.nav.port"] = ("Puerto", "Port"),
        ["cp.nav.enemy"] = ("Hostil", "Hostile"),
        ["cp.nav.noPick"] = ("Elige un yacimiento o un puerto", "Pick a deposit or a port"),
        ["cp.unit.cells"] = ("celdas", "cells"),
        ["cp.nav.dist"] = ("DISTANCIA", "DISTANCE"),
        ["cp.nav.noTarget"] = ("SIN OBJETIVO", "NO TARGET"),
        ["cp.nav.autoRange"] = ("AUTO", "AUTO"),
        ["cp.nav.reset"] = ("REINICIAR", "RESET"),
        ["cp.nav.mapHint"] = ("Rueda: zoom. Arrastrar: girar el mapa.", "Wheel: zoom. Drag: turn the map."),
        ["cp.nav.noRoute"] = ("Sin ruta a ese objetivo", "No route to that target"),

        // systems page
        ["cp.sys.modules"] = ("MÓDULOS", "MODULES"),
        ["cp.sys.sound"] = ("{0} de {1} sanos", "{0} of {1} sound"),
        ["cp.sys.damaged"] = ("{0} dañados (Retroceso repara)", "{0} damaged (Backspace repairs)"),
        ["cp.sys.threat"] = ("AMENAZA", "THREAT"),
        ["cp.sys.calm"] = ("SIN AMENAZAS", "ALL CLEAR"),
        ["cp.sys.wave"] = ("OLEADA EN CAMINO", "WAVE INBOUND"),
        ["cp.sys.hostiles"] = ("{0} hostiles", "{0} hostiles"),

        // weapons page
        ["cp.wpn.group"] = ("GRUPO DE ARMAS", "WEAPON GROUP"),
        ["cp.wpn.laser"] = ("LÁSER DE MINERÍA", "MINING LASER"),
        ["cp.wpn.guns"] = ("CAÑONES", "GUNS"),
        ["cp.wpn.missing"] = ("NO INSTALADO", "NOT FITTED"),
        ["cp.wpn.ready"] = ("LISTO", "READY"),
        ["cp.wpn.firing"] = ("DISPARANDO", "FIRING"),
        ["cp.wpn.mining"] = ("MINANDO", "MINING"),
        ["cp.wpn.armed"] = ("ARMADO", "ARMED"),
        ["cp.wpn.safe"] = ("EN REPOSO", "STANDBY"),
        ["cp.wpn.select"] = ("ELEGIR", "SELECT"),
        ["cp.wpn.hint"] = ("Clic mantenido en el juego: disparar", "Hold click in the game: fire"),
        ["cp.wpn.noThreat"] = ("Sin enemigos en este mundo", "No enemies in this world"),

        // chrome
        ["cp.hud"] = ("HUD", "HUD"),
        ["cp.hud.full"] = ("COMPLETO", "FULL"),
        ["cp.hud.cockpit"] = ("CABINA", "COCKPIT"),
        ["cp.hud.minimal"] = ("MÍNIMO", "MINIMAL"),
        ["cp.leave"] = ("SALIR", "EXIT"),
        ["cp.offline"] = ("SIN SEÑAL", "NO SIGNAL"),
        ["toast.hudMode"] = ("HUD: {0}", "HUD: {0}"),
        ["toast.hudModeSeat"] = ("HUD: {0} (se aplica sentado)", "HUD: {0} (applies while seated)"),
        ["toast.noConsole"] = ("Siéntate en el asiento del piloto para mirar las pantallas", "Sit at the pilot seat to look at the displays"),

        // key panel and help
        ["key.ctx.console"] = ("Pantallas", "Displays"),
        ["key.console"] = ("Mirar las pantallas de la cabina", "Look at the cockpit displays"),
        ["key.consolePress"] = ("Pulsar un botón de la pantalla", "Press a button on the display"),
        ["key.consoleNav"] = ("Cambiar de pantalla y de página", "Change screen and page"),
        ["key.consoleLeave"] = ("Dejar de mirar las pantallas", "Stop looking at the displays"),
        ["key.hudFull"] = ("HUD completo (F3 cambia)", "Full HUD (F3 switches)"),
        ["key.hudCockpit"] = ("HUD solo cabina (F3 cambia)", "Cockpit-only HUD (F3 switches)"),
        ["key.hudMinimal"] = ("HUD mínimo (F3 cambia)", "Minimal HUD (F3 switches)"),
        ["settings.smoothLook"] = ("Suavizar caminar y sentarse", "Smooth walking and sitting"),
        ["settings.tip.smoothLook"] = ("El cuerpo acelera y frena al caminar, el ojo se desliza al sentarte y levantarte y hay un leve balanceo. No cambia la simulación.", "The body speeds up and slows down when walking, the eye glides when you sit or stand and there is a faint head bob. It does not change the simulation."),
        ["settings.inertia"] = ("Inercia de la nave", "Ship inertia"),
        ["settings.inertia.off"] = ("Sin inercia", "Off"),
        ["settings.inertia.light"] = ("Ligera", "Light"),
        ["settings.inertia.full"] = ("Completa", "Full"),
        ["settings.tip.inertia"] = ("Cómo gira la nave: completa = tiene masa (el giro arranca y frena según su peso y sus motores), ligera = más ágil, sin inercia = sigue al ratón casi al instante (40 ms de suavizado). No cambia la simulación.", "How the ship turns: full = it has mass (the turn builds and stops according to its weight and engines), light = more agile, off = it follows the mouse almost at once (a 40 ms easing). It does not change the simulation."),
        ["settings.flightAssist"] = ("Asistente de vuelo (frena el giro)", "Flight assist (damps the turn)"),
        ["settings.tip.flightAssist"] = ("Con el asistente, la nave deja de girar cuando sueltas el ratón. Sin él sigue girando hasta que la frenes (F8 lo cambia en vuelo). Solo cuenta con inercia ligera o completa.", "With the assist the ship stops turning when you let go of the mouse. Without it the ship keeps rotating until you counter it (F8 switches it in flight). Only matters with light or full inertia."),
        ["key.assistOn"] = ("Asistente de vuelo activo (F8 lo quita)", "Flight assist on (F8 turns it off)"),
        ["key.assistOff"] = ("Giro libre, sin asistente (F8 lo pone)", "Free rotation, no assist (F8 turns it on)"),
        ["toast.assistOn"] = ("Asistente de vuelo activado: la nave frena el giro", "Flight assist on: the ship damps its turn"),
        ["toast.assistOff"] = ("Asistente de vuelo desactivado: giro libre", "Flight assist off: free rotation"),
        ["cp.turn"] = ("GIRO", "TURN"),
        ["cp.assist"] = ("ASISTENTE", "ASSIST"),
        ["cp.assistOff"] = ("LIBRE", "FREE"),
        ["cp.rate.pitch"] = ("CAB", "PIT"),
        ["cp.rate.yaw"] = ("GUI", "YAW"),
        ["cp.rate.roll"] = ("ALA", "ROL"),
        ["cp.noInertia"] = ("SIN INERCIA", "NO INERTIA"),
        ["help.cockpit"] = (
            "\n\nPantallas de la cabina y modos de HUD\n" +
            "  Delante del piloto hay tres pantallas con páginas Vuelo, Nav, Sistemas y Armas (una queda apagada: elegir una página que está en otra pantalla las intercambia). Se leen mejor con F4 o con un clic sobre una pantalla: la vista se acerca a ella; con Alt pulsado miras por dentro de la nave sin pilotar\n" +
            "  Clic en un botón de la pantalla (apuntando con la mirilla): cambia de página, elige un yacimiento, activa el piloto automático o la aproximación a un puerto, escanea, cambia el grupo de armas, el láser y el HUD. Con una pantalla enfocada: izquierda y derecha cambian de pantalla, arriba y abajo de página, F4 o Esc salen\n" +
            "  F3   modo de HUD: completo (todas las tarjetas y marcadores), solo cabina (sentado se ocultan tarjetas, marcadores y marco galáctico: la información está en las pantallas) y mínimo (solo mirilla, avisos y notificaciones). De pie siempre ves lo necesario para caminar y construir. Se guarda en los ajustes",
            "\n\nCockpit displays and HUD modes\n" +
            "  In front of the pilot are three screens with the pages Flight, Nav, Systems and Weapons (one page is always off: picking a page that is on another screen swaps them). They read best with F4 or a click on a screen: the view moves in on it; hold Alt to look around inside the ship without steering\n" +
            "  Click a button on a screen (aim with the crosshair): switch page, pick a deposit, engage the autopilot or the port approach, scan, change the weapon group, the laser and the HUD. With a screen focused: left and right change screen, up and down change page, F4 or Esc leave\n" +
            "  F3   HUD mode: full (every card and marker), cockpit only (seated, the cards, markers and galactic frame are hidden: the information is on the screens) and minimal (crosshair, warnings and notifications only). Standing you always see what walking and building need. It is saved in the settings"),
    };
}
