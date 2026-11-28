namespace Starfoundry.Ui;

/// <summary>Spanish and English text of flight with inertia, solid deposits and the speed research (ADR 0033), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> FlightTable = new()
    {
        ["toast.reject.overlapsDeposit"] = ("Ahí hay una roca de mineral: construye junto a ella, no dentro", "There is a rock of ore there: build beside it, not inside it"),
        ["hud.flightSpeedOf"] = ("{0} / {1} m/s", "{0} / {1} m/s"),
        ["hud.flightGaugeTip"] = ("Velocidad frente a la máxima de los motores ahora. La marca blanca es la velocidad que pide el empuje ({0} m/s); la nave la alcanza con una aceleración de {1}.", "Speed against what the engines can do now. The white tick is the speed the thrust asks for ({0} m/s); the ship gets there at an acceleration of {1}."),
        ["obstacle.depositRock"] = ("roca de {0}", "{0} rock"),
        ["designer.flight"] = ("Motores: {0}  -  velocidad máxima {1} m/s, {2} s en alcanzarla", "Engines: {0}  -  top speed {1} m/s, {2} s to reach it"),
        ["designer.flightPlain"] = ("Motores: {0}  -  velocidad máxima {1} m/s", "Engines: {0}  -  top speed {1} m/s"),
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
