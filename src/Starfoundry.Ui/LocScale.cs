namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the world scale and the cruise drive (ADR 0053), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> ScaleTable = new()
    {
        ["research.core:cruise_drive"] = ("Motor de crucero", "Cruise drive"),
        ["research.core:cruise_drive.desc"] = ("Un motor para el vacío entre sistemas y cúmulos de rocas: acelera hasta 3 km/s en unos 12 s y frena solo al acercarte a una zona. Dentro de un sistema o junto a un cúmulo la nave vuelve a la velocidad de impulso.",
            "A drive for the void between systems and rock clusters: it spins up to 3 km/s in about 12 s and brakes by itself as you near a zone. Inside a system or beside a cluster the ship drops back to boost speed."),
        ["act.cruise.name"] = ("crucero", "cruise"),
        ["act.cruise.hint"] = ("Pulsa para enganchar o soltar el motor de crucero: solo funciona en el vacío, lejos de sistemas y cúmulos.", "Press to engage or release the cruise drive: it only works in the void, away from systems and clusters."),
        ["key.cruise"] = ("Crucero (motor de largo alcance)", "Cruise (long-range drive)"),
        ["cp.cruise"] = ("CRUCERO", "CRUISE"),
        ["hud.cruise"] = ("CRUCERO", "CRUISE"),
        ["toast.cruise.on"] = ("Crucero enganchado", "Cruise engaged"),
        ["toast.cruise.off"] = ("Crucero suelto", "Cruise released"),
        ["toast.cruise.zone"] = ("El crucero no funciona dentro de un sistema ni junto a un cúmulo de rocas: aléjate un poco", "Cruise does not work inside a system or beside a rock cluster: move a little way off"),
        ["toast.cruise.locked"] = ("Falta investigar el motor de crucero", "The cruise drive is not researched yet"),
        ["toast.cruise.none"] = ("Este mundo no tiene motor de crucero", "This world has no cruise drive"),
        ["toast.cruise.dropped"] = ("Crucero desactivado: zona cerca", "Cruise off: a zone is near"),
    };
}
