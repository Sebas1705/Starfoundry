namespace Starfoundry.Ui;

/// <summary>Names of the icons that are not game data (tools, actions and status symbols), merged into the <see cref="Loc"/> table.</summary>
public static partial class Loc
{
    private static readonly (string Key, string Es, string En)[] IconStrings =
    {
        ("tool:build", "Construir", "Build"), ("tool:demolish", "Demoler", "Demolish"), ("tool:mine", "Láser minero", "Mining laser"),
        ("tool:weapons", "Armas", "Weapons"), ("tool:scan", "Escanear", "Scan"), ("tool:map", "Mapa estelar", "Star map"),
        ("tool:research", "Investigación", "Research"), ("tool:undock", "Desacoplar", "Undock"), ("tool:dock", "Atracar", "Dock"),
        ("tool:jump", "Salto", "Jump"), ("tool:repair", "Reparar", "Repair"), ("tool:refine", "Refinar", "Refine"), ("tool:seat", "Asiento", "Seat"),
        ("tool:walk", "Caminar", "Walk"), ("tool:look", "Mirar", "Look"), ("tool:help", "Ayuda", "Help"), ("tool:language", "Idioma", "Language"),
        ("tool:boost", "Impulso", "Boost"), ("tool:pause", "Pausa", "Pause"), ("tool:settings", "Ajustes", "Settings"), ("tool:save", "Guardar", "Save"),
        ("tool:load", "Cargar partida", "Load game"), ("tool:rotate", "Girar", "Rotate"), ("tool:layer", "Capa", "Layer"), ("tool:unload", "Descargar", "Unload"),
        ("tool:store", "Cargar en la nave", "Load aboard"), ("tool:cancel", "Cancelar", "Cancel"), ("tool:close", "Cerrar", "Close"),
        ("tool:found", "Fundar estación", "Found station"), ("tool:pick", "Elegir", "Choose"), ("tool:inspect", "Inspeccionar", "Inspect"),
        ("tool:recipe", "Receta", "Recipe"), ("tool:kind", "Tipo", "Kind"), ("tool:tab", "Pestaña", "Tab"), ("tool:start", "Empezar", "Start"),
        ("tool:info", "Información", "Info"), ("tool:stock", "Almacén", "Stock"), ("tool:module", "Módulo", "Module"), ("tool:drones", "Drones", "Drones"),
        ("tool:place", "Colocar", "Place"), ("tool:quick", "Acceso rápido", "Quick keys"), ("tool:run", "Correr", "Run"), ("tool:hp", "Puntos de vida", "Hit points"),
        ("tool:menu", "Menú", "Menu"), ("tool:resume", "Continuar", "Resume"), ("tool:hold", "Bodega", "Hold"), ("tool:belt", "Cinta", "Belt"),
        ("tool:machine", "Máquina", "Machine"), ("tool:target", "Objetivo", "Target"), ("tool:add", "Añadir", "Add"), ("tool:designer", "Diseñador de naves", "Ship designer"), ("tool:fireGuns", "Disparar cañones", "Fire guns"),
        ("tool:assist", "Asistente de vuelo", "Flight assist"), ("tool:computer", "Ordenador de la nave", "Ship computer"), ("tool:fullscreen", "Pantalla completa", "Fullscreen"), ("tool:systems", "Sistemas", "Systems"),
        ("status:power", "Energía", "Power"), ("status:fuel", "Combustible", "Fuel"), ("status:hull", "Casco", "Hull"), ("status:shield", "Escudo", "Shield"),
        ("status:speed", "Velocidad", "Speed"), ("status:cargo", "Bodega", "Hold"), ("status:warning", "Aviso", "Warning"), ("status:alert", "Alerta", "Alert"),
        ("status:objective", "Objetivo", "Objective"), ("status:enemy", "Enemigo", "Enemy"), ("status:station", "Estación", "Station"),
        ("status:port", "Puerto de atraque", "Docking port"), ("status:gate", "Puerta", "Gate"), ("status:deposit", "Yacimiento", "Deposit"),
        ("status:check", "Hecho", "Done"), ("status:cross", "No", "No"), ("status:locked", "Bloqueado", "Locked"), ("status:time", "Tiempo", "Time"),
        ("status:wave", "Oleada", "Wave"), ("status:threat", "Amenaza", "Threat"), ("status:system", "Sistema", "System"),
        ("status:health", "Integridad", "Integrity"), ("status:signature", "Firma", "Signature"), ("status:research", "Investigación", "Research"),
        ("status:shieldOk", "Escudo activo", "Shield up"), ("status:drone", "Dron", "Drone"),
    };

    /// <summary>Adds the icon names to the table (called from the one static constructor, in LocShipyard.cs).</summary>
    private static void AddIconStrings()
    {
        foreach (var (key, es, en) in IconStrings) Table["icon." + key] = (es, en);
    }
}
