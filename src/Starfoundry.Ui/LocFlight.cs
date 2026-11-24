namespace Starfoundry.Ui;

/// <summary>Spanish and English text of flight with inertia, solid deposits and the speed research (ADR 0033), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> FlightTable = new()
    {
        ["toast.reject.overlapsDeposit"] = ("Ahí hay una roca de mineral: construye junto a ella, no dentro", "There is a rock of ore there: build beside it, not inside it"),
        ["bonus.engine_speed"] = ("velocidad máxima de la nave", "ship top speed"),
        ["bonus.engine_accel"] = ("aceleración y frenada de la nave", "ship acceleration and braking"),
        ["research.core:ion_thrusters"] = ("Propulsores iónicos", "Ion thrusters"),
        ["research.core:ion_thrusters.desc"] = ("Toberas de iones más eficientes: la nave alcanza una velocidad máxima mayor.", "More efficient ion nozzles: the ship reaches a higher top speed."),
        ["research.core:thrust_vectoring"] = ("Empuje vectorial", "Thrust vectoring"),
        ["research.core:thrust_vectoring.desc"] = ("Toberas orientables: la nave acelera y frena con más fuerza.", "Steerable nozzles: the ship accelerates and brakes harder."),
        ["research.core:plasma_drive"] = ("Motor de plasma", "Plasma drive"),
        ["research.core:plasma_drive.desc"] = ("Un motor de plasma para la nave: más velocidad y más aceleración.", "A plasma drive for the ship: more speed and more acceleration."),
    };
}
