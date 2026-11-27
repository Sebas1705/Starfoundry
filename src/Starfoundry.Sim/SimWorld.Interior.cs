namespace Starfoundry.Sim;

public sealed partial class SimWorld
{
    /// <summary>What applying a station interior does (ADR 0044), worked out and validated before anything changes.</summary>
    private sealed class InteriorPlan
    {
        public BlueprintInterior Cur = BlueprintInterior.Empty;
        public int[] CurMachineIds = Array.Empty<int>(), CurBeltIds = Array.Empty<int>();
        public readonly List<int> RemoveMachines = new(), RemoveBelts = new(); // ids
        public readonly List<int> NewMachines = new(), NewBelts = new(); // indices into the desired interior
        public int[] MachineId = Array.Empty<int>(), BeltId = Array.Empty<int>(); // desired index -> id of the kept part, or -1 for a new one
        public readonly List<(int Id, int Recipe)> Recipes = new();
        public readonly SortedDictionary<int, int> Net = new();
    }

    private static bool Touching((int X, int Y, int Z) a, (int X, int Y, int Z) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) == 1;

    private Reject PlanInterior(int player, int structure, Structure s, SortedDictionary<(int Mx, int My, int Mz), ModuleKind> next, BlueprintInterior it, bool dockedHere, out InteriorPlan? planOut)
    {
        planOut = null;
        var content = Network.Content!;
        var kinds = content.Structures!;
        if (!it.IsWellFormed) return Reject.InvalidArgument;
        var plan = new InteriorPlan { Cur = Network.ReadInterior(structure, out var mids, out var sids) };
        plan.CurMachineIds = mids; plan.CurBeltIds = sids;
        var cur = plan.Cur;
        var curMachine = new Dictionary<(int, int, int, int, int), int>();
        for (int i = 0; i < cur.Machines.Length; i++) curMachine[(cur.Machines[i].X, cur.Machines[i].Y, cur.Machines[i].Z, cur.Machines[i].Type, cur.Machines[i].Turn)] = i;
        var curBelt = new Dictionary<BlueprintBelt, int>();
        for (int i = 0; i < cur.Belts.Length; i++) curBelt[cur.Belts[i]] = i;

        var occupied = new HashSet<(int, int, int)>();
        plan.MachineId = new int[it.Machines.Length];
        plan.BeltId = new int[it.Belts.Length];
        var keptMachine = new bool[cur.Machines.Length];
        var keptBelt = new bool[cur.Belts.Length];

        for (int i = 0; i < it.Machines.Length; i++)
        {
            var m = it.Machines[i];
            if (!content.IsMachine(m.Type)) return Reject.UnknownMachineType;
            var def = content.Machines[m.Type];
            if (def.Role == MachineRole.Ship) return Reject.NotPlaceable;
            if ((uint)m.Turn > 3) return Reject.InvalidArgument;
            if (Network.Rules.LegacyFootprints && (m.Turn != 0 || def.IsMultiCell)) return Reject.NotAllowedHere; // ADR 0050: a world from before footprints has one-cell machines only
            if (m.Turn != 0 && !def.IsMultiCell) return Reject.InvalidArgument; // a one-cell machine has no turn to give
            bool kept = curMachine.TryGetValue((m.X, m.Y, m.Z, m.Type, m.Turn), out int ci);
            if (def.Role == MachineRole.Drill && !kept) return Reject.KindNotAllowed; // a drill needs a deposit: it is linked in the world, not drawn
            var recipe = CheckRecipe(m.Type, m.Recipe);
            if (recipe != Reject.None) return recipe;
            if (!StructureGrid.InLocalBounds(m.X) || !StructureGrid.InLocalBounds(m.Y) || !StructureGrid.InLocalBounds(m.Z)) return Reject.OutOfBounds;
            bool recipeChanged = kept && cur.Machines[ci].Recipe != m.Recipe;
            if (Campaign && ((!kept && Network.IsMachineLocked(m.Type)) || ((!kept || recipeChanged) && m.Recipe != -1 && Network.IsRecipeLocked(m.Recipe)))) return Reject.Locked;
            if (!next.TryGetValue(StationGrid.ModuleOfCell(m.X, m.Y, m.Z), out var mk)) return Reject.OutsideStation;
            var problem = kinds.CellProblem(mk, m.Y, isBelt: false, def.Role, Network.FreeBuilding);
            if (problem != Reject.None) return problem;
            var anchorModule = StationGrid.ModuleOfCell(m.X, m.Y, m.Z);
            foreach (var (dx, dz) in def.Cells(m.Turn))
            {
                int cx = m.X + dx, cz = m.Z + dz;
                if (!StructureGrid.InLocalBounds(cx) || !StructureGrid.InLocalBounds(cz)) return Reject.OutOfBounds;
                if (StationGrid.ModuleOfCell(cx, m.Y, cz) != anchorModule) return Reject.FootprintBlocked; // a footprint stays inside one module's floor
                if (!occupied.Add((cx, m.Y, cz))) return Reject.Overlap;
            }
            if (kept)
            {
                keptMachine[ci] = true;
                plan.MachineId[i] = plan.CurMachineIds[ci];
                if (recipeChanged) plan.Recipes.Add((plan.CurMachineIds[ci], m.Recipe));
            }
            else
            {
                plan.MachineId[i] = -1;
                plan.NewMachines.Add(i);
                foreach (var c in def.Cost) plan.Net[c.Item] = plan.Net.GetValueOrDefault(c.Item) + c.Count;
            }
        }

        for (int i = 0; i < it.Belts.Length; i++)
        {
            var b = it.Belts[i];
            if (!Dirs.IsValid(b.Dir) || b.Length < 1 || b.Length > MaxSegmentCells || !Enum.IsDefined(b.Tier)) return Reject.InvalidArgument;
            bool kept = curBelt.TryGetValue(b, out int ci);
            if (Campaign && !kept && Network.IsBeltLocked(b.Tier)) return Reject.Locked;
            var (dx, dy, dz) = Dirs.Vector(b.Dir);
            long lx = b.X + (long)dx * (b.Length - 1), ly = b.Y + (long)dy * (b.Length - 1), lz = b.Z + (long)dz * (b.Length - 1);
            if (!StructureGrid.InLocalBounds(b.X) || !StructureGrid.InLocalBounds(b.Y) || !StructureGrid.InLocalBounds(b.Z)
                || !StructureGrid.InLocalBounds(lx) || !StructureGrid.InLocalBounds(ly) || !StructureGrid.InLocalBounds(lz)) return Reject.OutOfBounds;
            for (int k = 0; k < b.Length; k++)
            {
                int cx = b.X + dx * k, cy = b.Y + dy * k, cz = b.Z + dz * k;
                if (!next.TryGetValue(StationGrid.ModuleOfCell(cx, cy, cz), out var mk)) return Reject.OutsideStation;
                var problem = kinds.CellProblem(mk, cy, isBelt: true, MachineRole.Crafter, Network.FreeBuilding);
                if (problem != Reject.None) return problem;
                if (!occupied.Add((cx, cy, cz))) return Reject.Overlap;
            }
            if (kept) { keptBelt[ci] = true; plan.BeltId[i] = plan.CurBeltIds[ci]; }
            else
            {
                plan.BeltId[i] = -1;
                plan.NewBelts.Add(i);
                foreach (var c in BeltCost(b.Tier)) plan.Net[c.Item] = plan.Net.GetValueOrDefault(c.Item) + c.Count * b.Length;
            }
        }

        for (int i = 0; i < cur.Machines.Length; i++)
            if (!keptMachine[i])
            {
                plan.RemoveMachines.Add(plan.CurMachineIds[i]);
                foreach (var c in content.Machines[cur.Machines[i].Type].Cost) plan.Net[c.Item] = plan.Net.GetValueOrDefault(c.Item) - c.Count / 2;
            }
        for (int i = 0; i < cur.Belts.Length; i++)
            if (!keptBelt[i])
            {
                plan.RemoveBelts.Add(plan.CurBeltIds[i]);
                foreach (var c in BeltCost(cur.Belts[i].Tier)) plan.Net[c.Item] = plan.Net.GetValueOrDefault(c.Item) - c.Count * cur.Belts[i].Length / 2;
            }
        if (Network.MachineCount + plan.NewMachines.Count - plan.RemoveMachines.Count > MaxMachines) return Reject.TooManyMachines;
        if (Network.SegmentCount + plan.NewBelts.Count - plan.RemoveBelts.Count > MaxSegments) return Reject.TooManySegments;

        // Reach: with the ship docked here everything is at hand, else every part that is built or removed must be in build range.
        if (!dockedHere)
        {
            foreach (int i in plan.NewMachines)
                if (!StructureCellInRange(player, s, it.Machines[i].X, it.Machines[i].Y, it.Machines[i].Z)) return Reject.OutOfRange;
            foreach (int i in plan.NewBelts)
                if (!StructureCellInRange(player, s, it.Belts[i].X, it.Belts[i].Y, it.Belts[i].Z)) return Reject.OutOfRange;
            for (int i = 0; i < cur.Machines.Length; i++)
                if (!keptMachine[i] && !StructureCellInRange(player, s, cur.Machines[i].X, cur.Machines[i].Y, cur.Machines[i].Z)) return Reject.OutOfRange;
            for (int i = 0; i < cur.Belts.Length; i++)
                if (!keptBelt[i] && !StructureCellInRange(player, s, cur.Belts[i].X, cur.Belts[i].Y, cur.Belts[i].Z)) return Reject.OutOfRange;
        }

        var links = CheckLinks(it, plan, content);
        if (links != Reject.None) return links;
        planOut = plan;
        return Reject.None;
    }

    /// <summary>The links of a blueprint must be what the link commands accept: touching ends, roles, one input and at most two outputs per belt, two outputs per machine.</summary>
    private Reject CheckLinks(BlueprintInterior it, InteriorPlan plan, ContentDb content)
    {
        int nb = it.Belts.Length, nm = it.Machines.Length;
        var beltIn = new int[nb];
        var beltOutBelts = new int[nb];
        var beltOutMachines = new int[nb];
        var machineOut = new int[nm];
        var seen = new HashSet<(byte, int, int)>();
        (int, int, int) Start(int i) => (it.Belts[i].X, it.Belts[i].Y, it.Belts[i].Z);
        (int, int, int) End(int i) => BeltNetwork.LastCell(it.Belts[i]);
        bool TouchesMachine((int X, int Y, int Z) c, int i)
        {
            var m = it.Machines[i];
            foreach (var (dx, dz) in content.Machines[m.Type].Cells(m.Turn))
                if (Touching(c, (m.X + dx, m.Y, m.Z + dz))) return true;
            return false;
        }
        foreach (var l in it.Links)
        {
            if (!Enum.IsDefined(l.Kind) || !seen.Add(((byte)l.Kind, l.From, l.To))) return Reject.InvalidArgument;
            switch (l.Kind)
            {
                case BlueprintLinkKind.BeltToBelt:
                    if ((uint)l.From >= (uint)nb || (uint)l.To >= (uint)nb) return Reject.InvalidArgument;
                    if (l.From == l.To) return Reject.SelfConnection;
                    if (!Touching(End(l.From), Start(l.To))) return Reject.NotAdjacent;
                    beltOutBelts[l.From]++; beltIn[l.To]++;
                    break;
                case BlueprintLinkKind.BeltToMachine:
                    if ((uint)l.From >= (uint)nb || (uint)l.To >= (uint)nm) return Reject.InvalidArgument;
                    if (content.Machines[it.Machines[l.To].Type].Role is MachineRole.Wall or MachineRole.Unloader) return Reject.WrongRole;
                    if (!TouchesMachine(End(l.From), l.To)) return Reject.NotAdjacent;
                    beltOutMachines[l.From]++;
                    break;
                default:
                    if ((uint)l.From >= (uint)nm || (uint)l.To >= (uint)nb) return Reject.InvalidArgument;
                    if (content.Machines[it.Machines[l.From].Type].Role is MachineRole.Wall or MachineRole.Repairer) return Reject.WrongRole;
                    if (!TouchesMachine(Start(l.To), l.From)) return Reject.NotAdjacent;
                    machineOut[l.From]++; beltIn[l.To]++;
                    break;
            }
        }
        for (int i = 0; i < nb; i++)
        {
            if (beltIn[i] > 1) return Reject.SegmentConflict;
            if (beltOutBelts[i] > 2) return Reject.OutputsFull;
            if (beltOutMachines[i] > 1 || (beltOutMachines[i] > 0 && beltOutBelts[i] > 0)) return Reject.SegmentConflict;
            int id = plan.BeltId[i];
            if (id >= 0)
            {
                if (Network.IsSink(id) && beltOutBelts[i] + beltOutMachines[i] > 0) return Reject.FromIsSink;
                if (Network.HasSource(id) && beltIn[i] > 0) return Reject.SegmentConflict;
            }
        }
        for (int m = 0; m < nm; m++)
            if (machineOut[m] > 2) return Reject.MachineOutputsFull;
        return Reject.None;
    }

    /// <summary>Carries out a validated interior plan: parts that go are removed, new belts and machines are built, recipes change, and the links become exactly the blueprint's.</summary>
    private void ApplyInterior(int player, int structure, BlueprintInterior it, InteriorPlan plan)
    {
        foreach (int id in plan.RemoveMachines)
        {
            var m = Network.GetMachineInfo(id);
            ReleaseMachineCells(structure, m.X, m.Y, m.Z, m.Type, m.Turn);
            Network.RemoveMachine(id);
        }
        foreach (int id in plan.RemoveBelts)
        {
            Release(Network.GetSegmentInfo(id));
            Network.RemoveSegment(id);
        }
        foreach (var (id, recipe) in plan.Recipes) Network.SetRecipe(id, recipe);
        foreach (int i in plan.NewBelts)
        {
            var b = it.Belts[i];
            var (dx, dy, dz) = Dirs.Vector(b.Dir);
            int id = Network.AddSegment(b.X, b.Y, b.Z, dx, dy, dz, b.Length, b.Tier, structure);
            if (!TryClaim(id, b.X, b.Y, b.Z, dx, dy, dz, b.Length, structure)) throw new InvalidOperationException("A blueprint belt overlaps a part.");
            plan.BeltId[i] = id;
        }
        foreach (int i in plan.NewMachines)
        {
            var m = it.Machines[i];
            int id = Network.AddMachine(m.X, m.Y, m.Z, m.Type, m.Recipe, player, structure, m.Turn);
            if (!ClaimMachineCells(structure, id, m.X, m.Y, m.Z, m.Type, m.Turn)) throw new InvalidOperationException("A blueprint machine overlaps a part.");
            plan.MachineId[i] = id;
        }

        // Links: what stands between parts that stay and is not in the blueprint goes; what the blueprint has and is not there yet is made.
        var desired = new HashSet<(BlueprintLinkKind, int, int)>();
        foreach (var l in it.Links)
            desired.Add((l.Kind, l.Kind == BlueprintLinkKind.MachineToBelt ? plan.MachineId[l.From] : plan.BeltId[l.From], l.Kind == BlueprintLinkKind.BeltToMachine ? plan.MachineId[l.To] : plan.BeltId[l.To]));
        var removed = new HashSet<int>(plan.RemoveMachines);
        var removedBelts = new HashSet<int>(plan.RemoveBelts);
        var existing = new HashSet<(BlueprintLinkKind, int, int)>();
        foreach (var l in plan.Cur.Links)
        {
            int from = l.Kind == BlueprintLinkKind.MachineToBelt ? plan.CurMachineIds[l.From] : plan.CurBeltIds[l.From];
            int to = l.Kind == BlueprintLinkKind.BeltToMachine ? plan.CurMachineIds[l.To] : plan.CurBeltIds[l.To];
            bool gone = l.Kind switch
            {
                BlueprintLinkKind.BeltToBelt => removedBelts.Contains(from) || removedBelts.Contains(to),
                BlueprintLinkKind.BeltToMachine => removedBelts.Contains(from) || removed.Contains(to),
                _ => removed.Contains(from) || removedBelts.Contains(to),
            };
            if (gone) continue;
            existing.Add((l.Kind, from, to));
            if (desired.Contains((l.Kind, from, to))) continue;
            switch (l.Kind)
            {
                case BlueprintLinkKind.BeltToBelt: Network.Disconnect(from, to); break;
                case BlueprintLinkKind.BeltToMachine: Network.DisconnectMachine(from, to); break;
                default: Network.DisconnectMachine(to, from); break;
            }
        }
        foreach (var l in it.Links)
        {
            int from = l.Kind == BlueprintLinkKind.MachineToBelt ? plan.MachineId[l.From] : plan.BeltId[l.From];
            int to = l.Kind == BlueprintLinkKind.BeltToMachine ? plan.MachineId[l.To] : plan.BeltId[l.To];
            if (existing.Contains((l.Kind, from, to))) continue;
            switch (l.Kind)
            {
                case BlueprintLinkKind.BeltToBelt: Network.Connect(from, to); break;
                case BlueprintLinkKind.BeltToMachine: Network.ConnectToMachine(from, to); break;
                default: Network.ConnectFromMachine(from, to); break;
            }
        }
    }
}
