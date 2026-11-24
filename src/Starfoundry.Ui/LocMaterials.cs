namespace Starfoundry.Ui;

/// <summary>Spanish and English text of the optional material chains (composites, coolant, salvaged crystals and lenses), merged into <see cref="Loc"/>'s table.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, (string Es, string En)> MaterialsTable = new()
    {
        ["item.core:carbon_fiber"] = ("fibra de carbono", "carbon fibre"),
        ["item.core:composite_plate"] = ("placa compuesta", "composite plate"),
        ["item.core:coolant"] = ("refrigerante", "coolant"),
        ["item.core:resonant_crystal"] = ("cristal resonante", "resonant crystal"),
        ["item.core:focus_lens"] = ("lente de enfoque", "focus lens"),
        ["machine.core:crystal_unloader"] = ("puerto de cristal", "crystal port"),
        ["build.desc.core:crystal_unloader"] = ("Saca cristales resonantes de tu bodega a una cinta (para la lente de enfoque).", "Takes resonant crystals out of your hold onto a belt (for the focus lens)."),
        ["recipe.core:make_carbon_fiber"] = ("tejer fibra de carbono", "weave carbon fibre"),
        ["recipe.core:make_composite_plate"] = ("fabricar placa compuesta", "make composite plate"),
        ["recipe.core:make_coolant"] = ("destilar refrigerante", "distil coolant"),
        ["recipe.core:make_focus_lens"] = ("tallar lente de enfoque", "grind focus lens"),
        ["research.core:composites"] = ("Compuestos", "Composites"),
        ["research.core:composites.desc"] = ("Fibra de carbono (carbono y lingote de níquel) y placa compuesta (fibra y placa de titanio). Un casco de nave un 20% más duro.", "Carbon fibre (carbon and a nickel ingot) and composite plate (fibre and a titanium plate). A ship hull 20% tougher."),
        ["research.core:cryogenics"] = ("Criogenia", "Cryogenics"),
        ["research.core:cryogenics.desc"] = ("Refrigerante a partir de hielo y silicio en el craqueador: los cañones y láseres Mk2 lo necesitan para no sobrecalentarse.", "Coolant from ice and silicon in the cracker: Mk2 guns and lasers need it so they do not overheat."),
        ["research.core:resonance"] = ("Resonancia", "Resonance"),
        ["research.core:resonance.desc"] = ("Talla lentes de enfoque con los cristales resonantes de los pecios y abre su puerto de bodega. El láser minero extrae un 20% más rápido.", "Grinds focus lenses from the resonant crystals of wrecks and opens their hold port. The mining laser extracts 20% faster."),
    };
}
