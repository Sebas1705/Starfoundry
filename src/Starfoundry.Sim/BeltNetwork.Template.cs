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

    /// <summary>
    /// ADR 0041: gives a structure built from a template its dock module when it has none, so every pre-built ship and station can dock without the content files
    /// changing (the content hash and old saves stay valid). A ship gets it on the -X flank of its cockpit (the place its old docking port was), else of its first
    /// plain module (not an engine or a hardpoint); a station on the -Z side of its core (then +Z, -X, +X), so its two X faces stay free ports. Does nothing with
    /// <see cref="WorldRules.LegacyDock"/> on or when the content has no dock module. Returns <paramref name="id"/>.
    /// </summary>
    internal int EnsureDockModule(int id)
    {
        if (Rules.LegacyDock || Content?.Structures is not { } kinds || !kinds.IsDefined(ModuleKind.Dock)) return id;
        var s = GetStructure(id);
        if (DockPieceRules) return EnsureDockPiece(s);
        if (s.ModuleMap.ContainsValue(ModuleKind.Dock) || s.ModuleMap.Count == 0) return id;
        bool station = s.Kind == StructureKind.Station;
        var offsets = station ? new[] { (0, -1), (0, 1), (-1, 0), (1, 0) } : new[] { (-1, 0), (1, 0) };
        // Passes, best host first: a ship's cockpit or a station's core, then any plain module, then any module at all.
        for (int pass = 0; pass < 3; pass++)
            foreach (var (key, kind) in s.ModuleMap)
            {
                bool host = station ? kind == ModuleKind.Core : kind == ModuleKind.Cockpit;
                bool plain = kind is not (ModuleKind.Cockpit or ModuleKind.Engine) && !ModuleKindIds.IsHardpoint(kind);
                if (pass == 0 && !host || pass == 1 && !plain) continue;
                foreach (var (dx, dz) in offsets)
                {
                    var cell = (key.Mx + dx, key.My, key.Mz + dz);
                    if (s.ModuleMap.ContainsKey(cell)) continue;
                    AddStructureModuleUnchecked(id, cell.Item1, cell.Item2, cell.Item3, ModuleKind.Dock);
                    return id;
                }
            }
        return id;
    }

    /// <summary>
    /// ADR 0047: gives a pre-built structure its docking collar when it has none: a ship on the -X flank of its cockpit (the place its old docking port was), else the +X flank, else
    /// a face of its first host module whose neighbour cell is empty; a station on the -Z face of its core (then +Z, -X, +X). Nothing is added when no face is free. No module is
    /// added: the collar is a small piece on a module face.
    /// </summary>
    private int EnsureDockPiece(Structure s)
    {
        if (s.DockPieceSet.Count > 0 || s.ModuleMap.Count == 0) return s.Id;
        bool station = s.Kind == StructureKind.Station;
        var faces = station ? new[] { Dir.NegZ, Dir.PosZ, Dir.NegX, Dir.PosX } : new[] { Dir.NegX, Dir.PosX, Dir.NegZ, Dir.PosZ };
        for (int pass = 0; pass < 2; pass++)
            foreach (var (key, kind) in s.ModuleMap)
            {
                bool host = station ? kind == ModuleKind.Core : kind == ModuleKind.Cockpit;
                if (pass == 0 && !host) continue;
                foreach (var face in faces)
                {
                    if (!DockRules.CanHostPiece(kind, face)) continue;
                    var (dx, dz) = FaceStep(face);
                    if (s.ModuleMap.ContainsKey((key.Mx + dx, key.My, key.Mz + dz))) continue;
                    AddDockPieceUnchecked(s.Id, key.Mx, key.My, key.Mz, face);
                    return s.Id;
                }
            }
        return s.Id;
    }
}
