namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the weapon group in hand, free fire, recoil, hold overflow and the mining readout (ADR 0036), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> FeelTable = new()
    {
        // the weapon in hand (M cycles; the left click fires it)
        ["hud.weaponInHand"] = ("En mano: {0}", "In hand: {0}"),
        ["hud.weaponLaser"] = ("láser de minería", "mining laser"),
        ["hud.weaponGuns"] = ("cañones", "guns"),
        ["hud.weaponHint"] = ("V cambia · clic izquierdo dispara", "V switches · left click fires"),
        ["btn.groupLaser"] = ("Láser de minería", "Mining laser"),
        ["btn.groupGuns"] = ("Cañones", "Guns"),
        ["tip.groupLaser"] = ("Toma el láser de minería (V cambia de arma). Mantén el clic izquierdo sobre un yacimiento para minar.", "Take the mining laser (V switches weapon). Hold the left click on a deposit to mine."),
        ["tip.groupGuns"] = ("Toma los cañones (V cambia de arma). Mantén el clic izquierdo para disparar donde apuntas, haya enemigo o no.", "Take the guns (V switches weapon). Hold the left click to fire where you aim, enemy or not."),
        ["toast.weaponGroupLaser"] = ("En mano: láser de minería ({0} cambia)", "In hand: mining laser ({0} switches)"),
        ["toast.weaponGroupGuns"] = ("En mano: cañones ({0} cambia)", "In hand: guns ({0} switches)"),
        ["key.weaponLaser"] = ("En mano: láser de minería (V cambia)", "In hand: mining laser (V switches)"),
        ["key.weaponGuns"] = ("En mano: cañones (V cambia)", "In hand: guns (V switches)"),
        ["key.takeLaser"] = ("Toma el láser de minería", "Take the mining laser"),
        ["key.fireGuns"] = ("Mantén el clic para disparar donde apuntas", "Hold the click to fire where you aim"),

        // free fire
        ["toast.dryFire"] = ("Sin munición: la bodega no tiene con qué cargar los cañones", "Out of ammo: the hold has nothing to load the guns with"),
        ["settings.aimAssist"] = ("Ayuda de puntería", "Aim assist"),
        ["settings.tip.aimAssist"] = ("Con los cañones, apunta solo a un enemigo que esté cerca de la mira. Apagada: los disparos van donde apunta la mira.", "With the guns, aims at an enemy close to the crosshair. Off: shots go where the crosshair points."),

        // hold overflow
        ["toast.holdOverflow"] = ("Bodega llena: el sobrante flota junto a la roca. Acércate para recogerlo cuando haya sitio.", "Hold full: the surplus floats beside the rock. Fly close to collect it when there is room."),
        ["toast.pickupCollected"] = ("Recogido: {0}", "Picked up: {0}"),
        ["toast.holdFullFloat"] = ("¡Bodega llena! El láser sigue rompiendo rocas y el sobrante flota junto a ellas; gasta o descarga objetos para hacer sitio y recógelo.", "Hold full! The laser still breaks rocks and the surplus floats beside them; spend or unload items to make room, then pick it up."),
        ["toast.holdSoftLimit"] = ("Bodega casi llena: el láser va más lento", "Hold nearly full: the laser slows down"),

        // the mining readout on the weapon card
        ["cp.wpn.rate"] = ("{0}/s", "{0}/s"),
        ["cp.wpn.depositLeft"] = ("quedan {0}", "{0} left"),
        ["cp.wpn.slowed"] = ("bodega llena: lento", "hold full: slow"),
        ["cp.wpn.noDeposit"] = ("sin yacimiento", "no deposit"),
    };
}
