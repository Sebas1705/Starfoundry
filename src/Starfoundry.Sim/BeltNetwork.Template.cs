namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    /// <summary>
    /// Builds a structure from a template at a world origin, without any cost or rule check (the template was validated when the
    /// content loaded). Owner is the player the structure and its machines belong to. Returns the structure id.
    /// For world setup: call it before the first command, before wrapping the network in a <see cref="SimWorld"/>.
    /// </summary>
    internal int BuildStructureFromTemplate(StructureTemplate template, StructureKind kind, int owner, long ox, long oy, long oz)
    {
        int id = CreateStructure(owner, kind, ox, oy, oz);
        foreach (var m in template.Modules) AddStructureModuleUnchecked(id, m.Mx, m.My, m.Mz, m.Kind);
        var machines = new int[template.Machines.Length];
        for (int i = 0; i < machines.Length; i++)
        {
            var m = template.Machines[i];
            machines[i] = AddMachine(m.X, m.Y, m.Z, m.Type, m.Recipe, owner, id);
        }
        foreach (var b in template.Belts)
        {
            var (dx, dy, dz) = Dirs.Vector(b.Dir);
            int seg = AddSegment(b.X, b.Y, b.Z, dx, dy, dz, b.Length, b.Tier, id);
            if (b.From >= 0) ConnectFromMachine(machines[b.From], seg);
            if (b.To >= 0) ConnectToMachine(seg, machines[b.To]);
        }
        if (owner >= 0)
            foreach (var h in template.Hold) GiveItems(owner, h.Item, h.Count);
        return id;
    }
}
