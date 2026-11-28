namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the multi-cell machines, their recipes and the research that unlocks them (ADR 0050), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> FootprintsTable = new()
    {
        ["machine.core:large_smelter"] = ("fundición grande", "large smelter"),
        ["machine.core:long_assembler"] = ("ensambladora larga", "long assembler"),
        ["machine.core:l_fabricator"] = ("fabricadora en L", "L fabricator"),
        ["machine.core:reactor_core"] = ("núcleo de reactor", "reactor core"),
        ["build.desc.core:large_smelter"] = ("Fundición de 2 x 2 m: funde cuatro minerales de golpe, un 60 % más rápido que una fundición pequeña por metro de cinta.", "A 2 x 2 furnace: smelts four ores at once, 60 percent faster than a small smelter."),
        ["build.desc.core:long_assembler"] = ("Ensambladora de 1 x 3 m: trabaja por lotes de tres placas y deja una cinta recorrerla de lado a lado.", "A 1 x 3 assembler: works in batches of three plates and lets a belt run along it."),
        ["build.desc.core:l_fabricator"] = ("Fabricadora en forma de L de 4 casillas: aleaciones, fibra y placas compuestas por lotes; sus dos brazos admiten una cinta cada uno.", "A four-tile L-shaped fabricator: alloys, fibre and composite plate in batches; each arm takes a belt."),
        ["build.desc.core:reactor_core"] = ("Núcleo de 5 casillas en cruz: fabrica células de combustible por lotes con menos hidrógeno por célula que el craqueador.", "A five-tile plus-shaped core: makes fuel cells in batches with less hydrogen per fuel cell than the cracker."),
        ["recipe.core:smelt_iron_bulk"] = ("fundir hierro por lotes", "smelt iron in bulk"),
        ["recipe.core:smelt_titanium_bulk"] = ("fundir titanio por lotes", "smelt titanium in bulk"),
        ["recipe.core:smelt_nickel_bulk"] = ("fundir níquel por lotes", "smelt nickel in bulk"),
        ["recipe.core:smelt_silicon_bulk"] = ("fundir silicio por lotes", "smelt silicon in bulk"),
        ["recipe.core:iron_plate_batch"] = ("placas de hierro por lotes", "iron plates in batches"),
        ["recipe.core:titanium_plate_batch"] = ("placas de titanio por lotes", "titanium plates in batches"),
        ["recipe.core:nickel_plate_batch"] = ("placas de níquel por lotes", "nickel plates in batches"),
        ["recipe.core:make_circuit_batch"] = ("circuitos por lotes", "circuits in batches"),
        ["recipe.core:make_alloy_batch"] = ("aleación de titanio por lotes", "titanium alloy in batches"),
        ["recipe.core:make_carbon_fiber_batch"] = ("fibra de carbono por lotes", "carbon fibre in batches"),
        ["recipe.core:make_composite_plate_batch"] = ("placa compuesta por lotes", "composite plate in batches"),
        ["recipe.core:make_fuel_cell_bulk"] = ("células de combustible por lotes", "fuel cells in bulk"),
        ["research.core:heavy_industry"] = ("Industria pesada", "Heavy industry"),
        ["research.core:heavy_industry.desc"] = ("Máquinas de varias casillas: fundición grande 2 x 2, ensambladora larga 1 x 3, fabricadora en L y núcleo de reactor en cruz, con recetas por lotes.", "Multi-tile machines: the 2 x 2 large smelter, the 1 x 3 long assembler, the L fabricator and the plus-shaped reactor core, with batch recipes."),
        ["bp.val.footprint"] = ("Una máquina no cabe: debe quedar entera sobre el suelo de un solo módulo", "A machine does not fit: it has to stand whole on the floor of a single module"),
        ["research.core:large_structures"] = ("Estructuras grandes", "Large structures"),
        ["research.core:large_structures.desc"] = ("Módulos grandes: bahías, bodegas y cascos de 2 x 2 o 3 x 3 módulos con un solo suelo, algo más baratos por módulo.", "Large modules: bays, holds and hulls of 2 x 2 or 3 x 3 modules with one joined floor, a little cheaper per module."),
        ["toast.reject.badSpan"] = ("Un módulo grande es un cuadrado completo de 2 x 2 o 3 x 3 módulos del mismo tipo, y ese tipo debe poder ser grande", "A large module is a full square of 2 x 2 or 3 x 3 modules of one kind, and that kind has to be allowed to be large"),
        ["bp.tool.large"] = ("Módulo grande", "Large module"),
        ["bp.tool.large.hint"] = ("elige el tipo y el tamaño en la paleta; un clic pone el cuadrado entero desde esa casilla; borrar quita el módulo grande entero", "pick the kind and size in the palette; a click places the whole square from that tile; delete takes the whole large module away"),
        ["bp.large.size"] = ("{0} grande {1} x {1}", "Large {0} {1} x {1}"),
        ["bp.large.locked"] = ("Falta investigar Estructuras grandes", "Large structures are not researched yet"),
        ["machine.size"] = ("{0} x {1} m", "{0} x {1} m"),
        ["bp.plan.turn"] = ("Girar la máquina", "Turn the machine"),
    };

}
