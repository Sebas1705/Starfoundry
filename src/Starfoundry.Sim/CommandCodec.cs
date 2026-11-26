using System.Text;

namespace Starfoundry.Sim;

/// <summary>
/// Compact binary format for commands and command logs, used for saving replays and, later, for the
/// network. Little-endian, versioned, and strict when reading: sizes are bounded and unknown tags are errors.
/// </summary>
public static class CommandCodec
{
    private const byte TagPlace = 1, TagRemove = 2, TagConnect = 3, TagDisconnect = 4,
        TagSetSource = 5, TagClearSource = 6, TagSetSink = 7, TagClearSink = 8, TagBatch = 9,
        TagPlaceMachine = 10, TagRemoveMachine = 11, TagSetRecipe = 12,
        TagConnectToMachine = 13, TagConnectFromMachine = 14, TagDisconnectMachine = 15,
        TagStartMining = 16, TagStopMining = 17, TagSetShipRecipe = 18, TagSetResearch = 19,
        TagLinkDeposit = 20, TagUnlinkDeposit = 21, TagSetMovement = 22, TagScan = 23, TagJump = 24, TagSetWeapons = 25, TagRepair = 26, TagSetThrust = 27, TagPlaceModule = 28, TagRemoveModule = 29,
        TagPlaceMachineIn = 30, TagPlaceSegmentIn = 31, TagPlaceModuleIn = 32, TagRemoveModuleIn = 33, TagFoundStation = 34, TagAssignDrones = 35, TagDumpItems = 36, TagFireLaser = 37, TagSetBoost = 38, TagDockAt = 39, TagUndock = 40, TagTransferItems = 41, TagSetAttitude = 42, TagDesignerApply = 46, TagSetAim = 59,
        TagMoveItems = 56, TagSetFeedSource = 57, TagSortInventory = 58, TagDropItems = 60, TagPickUp = 61, TagQueueBench = 63, TagClearBench = 64, TagApplyBlueprint = 65, TagApplyBlueprintInterior = 66;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SFCL");

    /// <summary>Version 2 added the content hash to the header, version 3 the game mode, version 4 the galaxy seed, version 5 the threat flag.</summary>
    /// Version 6 added the station-rules flag (bit 1); the structures flag (bit 2) uses a bit that version 6 left free,
    /// so logs written before it simply read as structures off and the version stays 6.
    private const byte LogVersion = 25;
    /// Version 7 added the world rules block (flags bit 3, written only for non-default rules). Version 8 added the scenario id (flags bit 4).
    /// Version 10 added the SetBoost command (tag 38) and the wide galaxy with obstacles: logs of versions 5 to 9 read with <see cref="WorldRules.LegacyGalaxy"/> on (ADR 0028).
    /// Version 9 added the FireLaser command (tag 37) and the mining laser's power draw: logs of versions 5 to 8 read with <see cref="WorldRules.LegacyLaser"/> on, so they replay without it (ADR 0027).
    /// Version 11 added the docking commands (tags 39 to 41, ADR 0029). They only act on state that older logs never create, so nothing is replayed differently; the reader refuses them in older logs.
    /// Version 14 added the DesignerApply command (tag 46) and the hardpoint modules (ADR 0032): logs of versions 5 to 13 read with <see cref="WorldRules.LegacyHardpoints"/> on, so the ship keeps its implicit laser and gun; the reader refuses the command in them.
    /// Version 15 added no command: it changes how a ship flies and collides (ADR 0033: acceleration limits and inertia, solid deposits, padded hit volumes). Logs of versions 5 to 14 read with <see cref="WorldRules.LegacyCollisions"/> and <see cref="WorldRules.LegacyFlight"/> on, so they replay as recorded.
    /// Version 17 added the SetAim command (tag 59) and ADR 0036: free fire with projectiles and recoil, hold overflow, regrowing deposits and the resource density rule. Logs of versions 5 to 16 read with <see cref="WorldRules.LegacyFeel"/> and <see cref="WorldRules.LegacyResources"/> on, so they replay as recorded; the reader refuses SetAim in them.
    /// Version 19 added dropping items and picking them up by hand (ADR 0038): the DropItems and PickUp commands (tags 60 and 61). The reader refuses them in logs of versions 5 to 18; the world rules do not change.
    /// Version 23 added the antenna variants (short and long range sensors, the comms antenna) and the heavy cannon (ADR 0042): scanner and navigation reach from fitted antennas, stacking with diminishing returns, and a slow heavy gun volley. It adds no command. Logs of versions 5 to 22 read with <see cref="WorldRules.LegacyArms"/> on, so they replay as recorded. (Version 22 belongs to the dock module.)
    /// Version 25 added the station floor plan (ADR 0044): ApplyBlueprint may carry the machines, belts and links of the station (tag 66), and in-world placement of machines and belts on a station is refused (Reject.UseBlueprint). Logs of versions 5 to 24 read with <see cref="WorldRules.LegacyInterior"/> on, so they replay as recorded; the reader refuses tag 66 in them.
    /// Version 24 added the blueprint editor for stations (ADR 0043): the ApplyBlueprint command (tag 65) and the refusal of the module tool and the in-world placement of station parts (Reject.UseBlueprint). Logs of versions 5 to 23 read with <see cref="WorldRules.LegacyBlueprint"/> on, so they replay as recorded; the reader refuses the command in them.
    /// Version 22 added dock modules as the only docking ports (ADR 0041): ships and stations carry a dedicated dock module, and core and cockpit ports are gone. It adds no command. Logs of versions 5 to 21 read with <see cref="WorldRules.LegacyDock"/> on, so they replay as recorded.
    /// Version 21 added the infinite rock field (ADR 0040): resource asteroids generated on demand around the ships, a wider world limit and rocks that vanish when emptied. It adds no command. Logs of versions 5 to 20 read with <see cref="WorldRules.LegacyField"/> on, so they replay as recorded. (Version 20 is reserved for the blueprint editor, ADR 0039.)
    /// Version 20 added the workbench queue (ADR 0039): the QueueBench and ClearBench commands (tags 63 and 64), and with it the station-first rules: a ship holds no machines, belts, turrets, walls or Fabrication bays, and the workbench crafts one item at a time in 8 s. Logs of versions 5 to 19 read with <see cref="WorldRules.LegacyShipAutomation"/> on, so they replay as recorded; the reader refuses the commands in them.
    /// Version 18 added the personal inventory (ADR 0035): the MoveItems, SetFeedSource and SortInventory commands (tags 56 to 58). Logs of versions 5 to 17 read with <see cref="WorldRules.LegacyInventory"/> on, so they replay as recorded; the reader refuses the commands in them.
    /// Version 12 added the SetAttitude command (tag 42, ADR 0030). Logs of older versions never carry it, so they replay with every ship level, exactly as recorded; the reader refuses it in them.
    /// Version 13 added no command: it changes where machines and belts may be built (the floor of a fabrication bay, ADR 0031). Logs of versions 5 to 12 read with <see cref="WorldRules.LegacyBuilding"/> on, so they replay with free building as recorded.
    /// <summary>Version 5 headers carry no station-rules or structures flag; they still load, with both rules off.</summary>
    private const byte OldestLogVersion = 5;

    /// <summary>Deepest allowed batch nesting. The world rejects nested batches, but the format can still
    /// carry them so a log reproduces rejections exactly; the bound keeps decoding cheap and safe.</summary>
    public const int MaxNesting = 4;

    /// <summary>
    /// True if the command can be encoded and decoded: no nulls, bounded nesting, batch and pattern sizes
    /// within limits. <see cref="SimWorld.Submit"/> refuses anything else, since it could never be logged.
    /// </summary>
    public static bool IsWellFormed(Command? command) => IsWellFormed(command, 1);

    private static bool IsWellFormed(Command? command, int depth)
    {
        if (command is null || depth > MaxNesting) return false;
        switch (command)
        {
            case Batch b:
                if (b.Commands is null || b.Commands.Length > SimWorld.MaxBatch) return false;
                foreach (var inner in b.Commands)
                    if (!IsWellFormed(inner, depth + 1)) return false;
                return true;
            case SetSource s:
                return s.ItemTypes is not null && s.ItemTypes.Length <= SimWorld.MaxPattern;
            case PlaceSegment or RemoveSegment or ConnectSegments or DisconnectSegments
                or ClearSource or SetSink or ClearSink
                or PlaceMachine or RemoveMachine or SetRecipe
                or ConnectToMachine or ConnectFromMachine or DisconnectMachine
                or StartMining or StopMining or SetShipRecipe or SetResearch or LinkDeposit or UnlinkDeposit or SetMovement or SetThrust
                or Scan or Jump or SetWeapons or Repair or PlaceModule or RemoveModule
                or PlaceMachineIn or PlaceSegmentIn or PlaceModuleIn or RemoveModuleIn or FoundStation or AssignDrones or DumpItems or FireLaser or SetBoost or DockAt or Undock or TransferItems or SetAim:
                return true;
            case MoveItems mv:
                return Encodable(mv.From) && Encodable(mv.To);
            case SetFeedSource fs:
                return Enum.IsDefined(fs.Source);
            case SortInventory:
                return true;
            case QueueBench qb:
                return qb.Count is >= 1 and <= PlayerRules.BenchQueueMax;
            case ClearBench:
                return true;
            case DropItems di:
                return Encodable(di.From);
            case PickUp:
                return true;
            case SetAttitude sa:
                return FitsShort(sa.W) && FitsShort(sa.X) && FitsShort(sa.Y) && FitsShort(sa.Z);
            case DesignerApply da:
                return da.Modules is not null && da.Modules.Length <= DesignerRules.MaxModules;
            case ApplyBlueprint ab:
                return ab.Modules is not null && ab.Modules.Length <= DesignerRules.MaxStationModules
                    && (ab.Interior is null || (ab.Interior.IsWellFormed && ab.Interior.Machines.All(m => m.Recipe >= -1) && ab.Interior.Belts.All(b => Dirs.IsValid(b.Dir) && Enum.IsDefined(b.Tier))
                        && ab.Interior.Links.All(l => Enum.IsDefined(l.Kind))));
            default:
                return false;
        }
    }

    private static bool FitsShort(int v) => v >= short.MinValue && v <= short.MaxValue;

    private static bool Encodable(ContainerRef c) => c.IsDefined && (c.Kind == ContainerKind.Reactor || (c.Mx | c.My | c.Mz) == 0);

    private static void WriteContainer(BinaryWriter w, ContainerRef c)
    {
        w.Write((byte)c.Kind); w.Write(c.Id);
        if (c.Kind == ContainerKind.Reactor) { w.Write(c.Mx); w.Write(c.My); w.Write(c.Mz); }
    }

    private static ContainerRef ReadContainer(BinaryReader r)
    {
        byte kind = r.ReadByte();
        if (!Enum.IsDefined((ContainerKind)kind)) throw new InvalidDataException($"Unknown container kind {kind}.");
        int id = r.ReadInt32();
        return (ContainerKind)kind == ContainerKind.Reactor ? new ContainerRef(ContainerKind.Reactor, id, r.ReadInt32(), r.ReadInt32(), r.ReadInt32()) : new ContainerRef((ContainerKind)kind, id);
    }

    public static void Write(BinaryWriter w, Command command)
    {
        if (!IsWellFormed(command)) throw new ArgumentException("The command cannot be encoded.", nameof(command));
        WriteCore(w, command);
    }

    private static void WriteCore(BinaryWriter w, Command command)
    {
        switch (command)
        {
            case PlaceSegment p:
                w.Write(TagPlace);
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
                w.Write((byte)p.Direction);
                w.Write(p.LengthCells);
                w.Write((byte)p.Tier);
                break;
            case RemoveSegment r:
                w.Write(TagRemove); w.Write(r.Segment);
                break;
            case ConnectSegments c:
                w.Write(TagConnect); w.Write(c.From); w.Write(c.To);
                break;
            case DisconnectSegments d:
                w.Write(TagDisconnect); w.Write(d.From); w.Write(d.To);
                break;
            case SetSource s:
                w.Write(TagSetSource);
                w.Write(s.Segment);
                w.Write(s.ItemTypes.Length);
                foreach (ushort t in s.ItemTypes) w.Write(t);
                break;
            case ClearSource cs:
                w.Write(TagClearSource); w.Write(cs.Segment);
                break;
            case SetSink ss:
                w.Write(TagSetSink); w.Write(ss.Segment);
                break;
            case ClearSink cl:
                w.Write(TagClearSink); w.Write(cl.Segment);
                break;
            case PlaceMachine pm:
                w.Write(TagPlaceMachine);
                w.Write(pm.X); w.Write(pm.Y); w.Write(pm.Z); w.Write(pm.Machine); w.Write(pm.Recipe);
                break;
            case RemoveMachine rm:
                w.Write(TagRemoveMachine); w.Write(rm.Machine);
                break;
            case SetRecipe sr:
                w.Write(TagSetRecipe); w.Write(sr.Machine); w.Write(sr.Recipe);
                break;
            case ConnectToMachine ctm:
                w.Write(TagConnectToMachine); w.Write(ctm.Segment); w.Write(ctm.Machine);
                break;
            case ConnectFromMachine cfm:
                w.Write(TagConnectFromMachine); w.Write(cfm.Machine); w.Write(cfm.Segment);
                break;
            case DisconnectMachine dm:
                w.Write(TagDisconnectMachine); w.Write(dm.Segment); w.Write(dm.Machine);
                break;
            case StartMining sm:
                w.Write(TagStartMining); w.Write(sm.Deposit);
                break;
            case StopMining:
                w.Write(TagStopMining);
                break;
            case SetBoost sb:
                w.Write(TagSetBoost); w.Write(sb.On);
                break;
            case FireLaser fl:
                w.Write(TagFireLaser); w.Write(fl.Firing);
                break;
            case SetShipRecipe ssr:
                w.Write(TagSetShipRecipe); w.Write(ssr.Recipe);
                break;
            case SetResearch sres:
                w.Write(TagSetResearch); w.Write(sres.Node);
                break;
            case LinkDeposit ld:
                w.Write(TagLinkDeposit); w.Write(ld.Machine); w.Write(ld.Deposit);
                break;
            case UnlinkDeposit ud:
                w.Write(TagUnlinkDeposit); w.Write(ud.Machine);
                break;
            case Repair rp:
                w.Write(TagRepair); w.Write(rp.Machine);
                break;
            case SetMovement mv:
                w.Write(TagSetMovement); w.Write(mv.Dx); w.Write(mv.Dy); w.Write(mv.Dz);
                break;
            case SetThrust th:
                w.Write(TagSetThrust); w.Write(th.Dx); w.Write(th.Dy); w.Write(th.Dz);
                break;
            case PlaceModule pmod:
                w.Write(TagPlaceModule); w.Write(pmod.Mx); w.Write(pmod.My); w.Write(pmod.Mz);
                break;
            case RemoveModule rmod:
                w.Write(TagRemoveModule); w.Write(rmod.Mx); w.Write(rmod.My); w.Write(rmod.Mz);
                break;
            case PlaceMachineIn pmi:
                w.Write(TagPlaceMachineIn); w.Write(pmi.Structure);
                w.Write(pmi.X); w.Write(pmi.Y); w.Write(pmi.Z); w.Write(pmi.Type); w.Write(pmi.Recipe);
                break;
            case PlaceSegmentIn psi:
                w.Write(TagPlaceSegmentIn); w.Write(psi.Structure);
                w.Write(psi.X); w.Write(psi.Y); w.Write(psi.Z);
                w.Write((byte)psi.Dir); w.Write(psi.Length); w.Write((byte)psi.Tier);
                break;
            case PlaceModuleIn pmo:
                w.Write(TagPlaceModuleIn); w.Write(pmo.Structure);
                w.Write(pmo.Mx); w.Write(pmo.My); w.Write(pmo.Mz); w.Write((byte)pmo.Kind);
                break;
            case RemoveModuleIn rmo:
                w.Write(TagRemoveModuleIn); w.Write(rmo.Structure); w.Write(rmo.Mx); w.Write(rmo.My); w.Write(rmo.Mz);
                break;
            case FoundStation fs:
                w.Write(TagFoundStation); w.Write(fs.Wx); w.Write(fs.Wy); w.Write(fs.Wz);
                break;
            case AssignDrones ad:
                w.Write(TagAssignDrones); w.Write(ad.Structure); w.Write(ad.Deposit);
                break;
            case DumpItems di:
                w.Write(TagDumpItems); w.Write(di.Item); w.Write(di.Count);
                break;
            case DockAt da:
                w.Write(TagDockAt); w.Write(da.Station); w.Write(da.Mx); w.Write(da.My); w.Write(da.Mz); w.Write((byte)da.Face);
                break;
            case Undock:
                w.Write(TagUndock);
                break;
            case TransferItems ti:
                w.Write(TagTransferItems); w.Write(ti.Item); w.Write(ti.Count); w.Write(ti.ToStation);
                break;
            case DropItems di:
                w.Write(TagDropItems); WriteContainer(w, di.From); w.Write(di.Slot); w.Write(di.Count); w.Write(di.X); w.Write(di.Y); w.Write(di.Z);
                break;
            case PickUp pu:
                w.Write(TagPickUp); w.Write(pu.Entity); w.Write(pu.Seated); w.Write(pu.X); w.Write(pu.Y); w.Write(pu.Z);
                break;
            case MoveItems mv:
                w.Write(TagMoveItems); WriteContainer(w, mv.From); w.Write(mv.FromSlot); WriteContainer(w, mv.To); w.Write(mv.ToSlot); w.Write(mv.Count);
                break;
            case SetFeedSource fs:
                w.Write(TagSetFeedSource); w.Write(fs.Structure); w.Write(fs.Mx); w.Write(fs.My); w.Write(fs.Mz); w.Write((byte)fs.Source);
                break;
            case QueueBench qb:
                w.Write(TagQueueBench); w.Write(qb.Recipe); w.Write(qb.Count);
                break;
            case ClearBench:
                w.Write(TagClearBench);
                break;
            case SortInventory:
                w.Write(TagSortInventory);
                break;
            case DesignerApply da:
                w.Write(TagDesignerApply); w.Write(da.Modules.Length);
                foreach (var m in da.Modules) { w.Write(m.Mx); w.Write(m.My); w.Write(m.Mz); w.Write((byte)m.Kind); }
                break;
            case ApplyBlueprint ab:
                w.Write(ab.Interior is null ? TagApplyBlueprint : TagApplyBlueprintInterior); w.Write(ab.Structure); w.Write(ab.Modules.Length);
                foreach (var m in ab.Modules) { w.Write(m.Mx); w.Write(m.My); w.Write(m.Mz); w.Write((byte)m.Kind); }
                if (ab.Interior is { } part)
                {
                    w.Write(part.Machines.Length);
                    foreach (var m in part.Machines) { w.Write(m.X); w.Write(m.Y); w.Write(m.Z); w.Write(m.Type); w.Write(m.Recipe); }
                    w.Write(part.Belts.Length);
                    foreach (var b in part.Belts) { w.Write(b.X); w.Write(b.Y); w.Write(b.Z); w.Write((byte)b.Dir); w.Write(b.Length); w.Write((byte)b.Tier); }
                    w.Write(part.Links.Length);
                    foreach (var l in part.Links) { w.Write((byte)l.Kind); w.Write(l.From); w.Write(l.To); }
                }
                break;
            case SetAttitude sa:
                w.Write(TagSetAttitude); w.Write((short)sa.W); w.Write((short)sa.X); w.Write((short)sa.Y); w.Write((short)sa.Z);
                break;
            case SetAim sm:
                w.Write(TagSetAim); w.Write(sm.Dx); w.Write(sm.Dy); w.Write(sm.Dz);
                break;
            case Scan:
                w.Write(TagScan);
                break;
            case Jump j:
                w.Write(TagJump); w.Write(j.System);
                break;
            case SetWeapons sw:
                w.Write(TagSetWeapons); w.Write(sw.Armed);
                break;
            case Batch b:
                w.Write(TagBatch);
                w.Write(b.Commands.Length);
                foreach (var inner in b.Commands) WriteCore(w, inner);
                break;
            default:
                throw new ArgumentException($"Unknown command type {command.GetType().Name}.");
        }
    }

    public static Command Read(BinaryReader r) => Read(r, 1);

    private static Command Read(BinaryReader r, int depth)
    {
        byte tag = r.ReadByte();
        switch (tag)
        {
            case TagPlace:
            {
                int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                byte dir = r.ReadByte();
                int len = r.ReadInt32();
                byte tier = r.ReadByte();
                // Out-of-range values are legal to decode; the world rejects them with a reason.
                return new PlaceSegment(x, y, z, (Dir)dir, len, (BeltTier)tier);
            }
            case TagRemove: return new RemoveSegment(r.ReadInt32());
            case TagConnect: return new ConnectSegments(r.ReadInt32(), r.ReadInt32());
            case TagDisconnect: return new DisconnectSegments(r.ReadInt32(), r.ReadInt32());
            case TagSetSource:
            {
                int segment = r.ReadInt32();
                int n = r.ReadInt32();
                if (n < 0 || n > SimWorld.MaxPattern) throw new InvalidDataException($"Source pattern length {n} out of range.");
                var types = new ushort[n];
                for (int i = 0; i < n; i++) types[i] = r.ReadUInt16();
                return new SetSource(segment, types);
            }
            case TagClearSource: return new ClearSource(r.ReadInt32());
            case TagSetSink: return new SetSink(r.ReadInt32());
            case TagClearSink: return new ClearSink(r.ReadInt32());
            case TagPlaceMachine: return new PlaceMachine(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagRemoveMachine: return new RemoveMachine(r.ReadInt32());
            case TagSetRecipe: return new SetRecipe(r.ReadInt32(), r.ReadInt32());
            case TagConnectToMachine: return new ConnectToMachine(r.ReadInt32(), r.ReadInt32());
            case TagConnectFromMachine: return new ConnectFromMachine(r.ReadInt32(), r.ReadInt32());
            case TagDisconnectMachine: return new DisconnectMachine(r.ReadInt32(), r.ReadInt32());
            case TagStartMining: return new StartMining(r.ReadInt32());
            case TagStopMining: return new StopMining();
            case TagSetShipRecipe: return new SetShipRecipe(r.ReadInt32());
            case TagSetResearch: return new SetResearch(r.ReadInt32());
            case TagLinkDeposit: return new LinkDeposit(r.ReadInt32(), r.ReadInt32());
            case TagUnlinkDeposit: return new UnlinkDeposit(r.ReadInt32());
            case TagRepair: return new Repair(r.ReadInt32());
            case TagSetMovement: return new SetMovement(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagSetThrust: return new SetThrust(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPlaceModule: return new PlaceModule(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagRemoveModule: return new RemoveModule(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPlaceMachineIn:
                return new PlaceMachineIn(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPlaceSegmentIn:
            {
                int structure = r.ReadInt32(), x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                byte dir = r.ReadByte();
                int len = r.ReadInt32();
                byte tier = r.ReadByte();
                return new PlaceSegmentIn(structure, x, y, z, (Dir)dir, len, (BeltTier)tier);
            }
            case TagPlaceModuleIn: return new PlaceModuleIn(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (ModuleKind)r.ReadByte());
            case TagRemoveModuleIn: return new RemoveModuleIn(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagFoundStation: return new FoundStation(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagAssignDrones: return new AssignDrones(r.ReadInt32(), r.ReadInt32());
            case TagDumpItems: return new DumpItems(r.ReadInt32(), r.ReadInt32());
            case TagFireLaser: return new FireLaser(r.ReadBoolean());
            case TagSetBoost: return new SetBoost(r.ReadBoolean());
            case TagDockAt:
            {
                int station = r.ReadInt32(), mx = r.ReadInt32(), my = r.ReadInt32(), mz = r.ReadInt32();
                byte face = r.ReadByte();
                return new DockAt(station, mx, my, mz, (Dir)face);
            }
            case TagSetAttitude: return new SetAttitude(r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadInt16());
            case TagUndock: return new Undock();
            case TagTransferItems: return new TransferItems(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean());
            case TagDesignerApply:
            {
                int n = r.ReadInt32();
                if (n < 0 || n > DesignerRules.MaxModules) throw new InvalidDataException($"Design size {n} out of range.");
                var modules = new DesignModule[n];
                for (int i = 0; i < n; i++) modules[i] = new DesignModule(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (ModuleKind)r.ReadByte());
                return new DesignerApply(modules);
            }
            case TagApplyBlueprint or TagApplyBlueprintInterior:
            {
                int structure = r.ReadInt32(), n = r.ReadInt32();
                if (n < 0 || n > DesignerRules.MaxStationModules) throw new InvalidDataException($"Blueprint size {n} out of range.");
                var modules = new DesignModule[n];
                for (int i = 0; i < n; i++) modules[i] = new DesignModule(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (ModuleKind)r.ReadByte());
                if (tag == TagApplyBlueprint) return new ApplyBlueprint(structure, modules);
                int nm = r.ReadInt32();
                if (nm < 0 || nm > BlueprintInterior.MaxParts) throw new InvalidDataException($"Blueprint machine count {nm} out of range.");
                var machines = new BlueprintMachine[nm];
                for (int i = 0; i < nm; i++) machines[i] = new BlueprintMachine(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                int nb = r.ReadInt32();
                if (nb < 0 || nb + nm > BlueprintInterior.MaxParts) throw new InvalidDataException($"Blueprint belt count {nb} out of range.");
                var belts = new BlueprintBelt[nb];
                for (int i = 0; i < nb; i++)
                {
                    int bx = r.ReadInt32(), by = r.ReadInt32(), bz = r.ReadInt32();
                    byte dir = r.ReadByte();
                    int len = r.ReadInt32();
                    byte tier = r.ReadByte();
                    if (!Dirs.IsValid((Dir)dir) || !Enum.IsDefined((BeltTier)tier)) throw new InvalidDataException("Bad belt in a blueprint.");
                    belts[i] = new BlueprintBelt(bx, by, bz, (Dir)dir, len, (BeltTier)tier);
                }
                int nl = r.ReadInt32();
                if (nl < 0 || nl > BlueprintInterior.MaxLinks) throw new InvalidDataException($"Blueprint link count {nl} out of range.");
                var links = new BlueprintLink[nl];
                for (int i = 0; i < nl; i++)
                {
                    byte kind = r.ReadByte();
                    if (!Enum.IsDefined((BlueprintLinkKind)kind)) throw new InvalidDataException("Bad link in a blueprint.");
                    links[i] = new BlueprintLink((BlueprintLinkKind)kind, r.ReadInt32(), r.ReadInt32());
                }
                return new ApplyBlueprint(structure, modules, new BlueprintInterior(machines, belts, links));
            }
            case TagSetAim: return new SetAim(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagDropItems: return new DropItems(ReadContainer(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagPickUp: return new PickUp(r.ReadInt32(), r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case TagMoveItems: return new MoveItems(ReadContainer(r), r.ReadInt32(), ReadContainer(r), r.ReadInt32(), r.ReadInt32());
            case TagSetFeedSource:
            {
                int structure = r.ReadInt32(), mx = r.ReadInt32(), my = r.ReadInt32(), mz = r.ReadInt32();
                byte source = r.ReadByte();
                if (!Enum.IsDefined((FeedSource)source)) throw new InvalidDataException($"Unknown feed source {source}.");
                return new SetFeedSource(structure, mx, my, mz, (FeedSource)source);
            }
            case TagSortInventory: return new SortInventory();
            case TagQueueBench: return new QueueBench(r.ReadInt32(), r.ReadInt32());
            case TagClearBench: return new ClearBench();
            case TagScan: return new Scan();
            case TagJump: return new Jump(r.ReadInt32());
            case TagSetWeapons: return new SetWeapons(r.ReadBoolean());
            case TagBatch:
            {
                if (depth >= MaxNesting) throw new InvalidDataException("Batches are nested too deeply.");
                int n = r.ReadInt32();
                if (n < 0 || n > SimWorld.MaxBatch) throw new InvalidDataException($"Batch size {n} out of range.");
                var list = new Command[n];
                for (int i = 0; i < n; i++) list[i] = Read(r, depth + 1);
                return new Batch(list);
            }
            default:
                throw new InvalidDataException($"Unknown command tag {tag}.");
        }
    }

    public static byte[] ToBytes(Command command)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) Write(w, command);
        return ms.ToArray();
    }

    public static Command FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        var command = Read(r);
        if (ms.Position != ms.Length) throw new InvalidDataException("Trailing bytes after the command.");
        return command;
    }

    /// <summary>Writes a log: magic, version, content hash, count, then (tick, player, command) entries.
    /// Pass <see cref="ContentDb.Hash"/> of the content the log was recorded with (0 if there is none).</summary>
    public static void WriteLog(Stream stream, IReadOnlyList<LoggedCommand> log, ulong contentHash = 0, GameMode mode = GameMode.Sandbox,
        ulong galaxySeed = 0, bool threats = false, bool stationRules = false, bool structures = false, WorldRules? rules = null, string? scenarioId = null)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(LogVersion);
        w.Write(contentHash);
        w.Write((byte)mode);
        w.Write(galaxySeed);
        bool hasRules = rules is not null && !rules.IsDefault;
        bool hasScenario = !string.IsNullOrEmpty(scenarioId);
        w.Write((byte)((threats ? 1 : 0) | (stationRules ? 2 : 0) | (structures ? 4 : 0) | (hasRules ? 8 : 0) | (hasScenario ? 16 : 0)));
        if (hasRules)
        {
            byte[] block = rules!.Serialize();
            w.Write(block.Length);
            w.Write(block);
        }
        if (hasScenario)
        {
            byte[] id = Encoding.UTF8.GetBytes(scenarioId!);
            if (id.Length > MaxScenarioIdBytes) throw new ArgumentException("The scenario id is too long.", nameof(scenarioId));
            w.Write(id.Length);
            w.Write(id);
        }
        w.Write(log.Count);
        foreach (var e in log)
        {
            w.Write(e.Tick);
            w.Write(e.Player);
            Write(w, e.Command);
        }
    }

    public static List<LoggedCommand> ReadLog(Stream stream) => ReadLog(stream, out _);

    /// <summary>Reads a log and the hash of the content it was recorded with. Compare it with
    /// <see cref="ContentDb.Hash"/> before replaying: a different hash means the replay would diverge.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash) => ReadLog(stream, out contentHash, out _);

    /// <summary>Also returns the game mode the log was recorded in.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode) =>
        ReadLog(stream, out contentHash, out mode, out _);

    /// <summary>Also returns the galaxy seed the world was created with (0 for a world without a galaxy).</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out _);

    /// <summary>Also returns whether the world had threats switched on.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out _);

    /// <summary>Also returns whether the world used station rules. Version 5 logs have no such flag and read as false.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out stationRules, out _);

    /// <summary>Also returns whether the world used structure rules. Logs written before the flag existed (and version 5 logs) read as false.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules, out bool structures) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out stationRules, out structures, out _);

    /// <summary>Also returns the world rules (flags bit 3, version 7). Logs without the block, including versions 5 and 6, read as default rules.</summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules, out bool structures, out WorldRules rules) =>
        ReadLog(stream, out contentHash, out mode, out galaxySeed, out threats, out stationRules, out structures, out rules, out _);

    /// <summary>Longest scenario id the header carries, in UTF-8 bytes.</summary>
    public const int MaxScenarioIdBytes = 64;

    /// <summary>
    /// Also returns the scenario id (flags bit 4, version 8), or null. A world made from a scenario must be rebuilt with it
    /// (<see cref="SimWorld.ReplayScenario"/>); logs of versions 5 to 7 read as no scenario.
    /// </summary>
    public static List<LoggedCommand> ReadLog(Stream stream, out ulong contentHash, out GameMode mode, out ulong galaxySeed, out bool threats,
        out bool stationRules, out bool structures, out WorldRules rules, out string? scenarioId)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a command log.");
        byte version = r.ReadByte();
        if (version < OldestLogVersion || version > LogVersion) throw new InvalidDataException($"Unsupported log version {version}.");
        contentHash = r.ReadUInt64();
        byte rawMode = r.ReadByte();
        if (rawMode > (byte)GameMode.Campaign) throw new InvalidDataException($"Unknown game mode {rawMode}.");
        mode = (GameMode)rawMode;
        galaxySeed = r.ReadUInt64();
        byte flags = r.ReadByte();
        int knownFlags = version >= 8 ? 31 : version >= 7 ? 15 : version >= 6 ? 7 : 1;
        if (flags > knownFlags) throw new InvalidDataException($"Unknown header flags {flags}.");
        threats = (flags & 1) != 0;
        stationRules = (flags & 2) != 0;
        structures = (flags & 4) != 0;
        rules = WorldRules.Default;
        if ((flags & 8) != 0)
        {
            int length = r.ReadInt32();
            if (length < 1 || length > 4096) throw new InvalidDataException("Bad world rules block length.");
            var block = r.ReadBytes(length);
            if (block.Length != length) throw new InvalidDataException("The world rules block is truncated.");
            rules = WorldRules.Deserialize(block);
        }
        if (version < 9) rules = rules.WithLegacyLaser(true); // older logs were recorded before the laser drew power
        if (version < 10) rules = rules.WithLegacyGalaxy(true); // and before the wide galaxy (ADR 0028)
        if (version < 13) rules = rules.WithLegacyBuilding(true); // and before building on the floor of a bay only (ADR 0031)
        if (version < 14) rules = rules.WithLegacyHardpoints(true); // and before hardpoint modules (ADR 0032): the ship keeps its implicit laser and gun
        if (version < 15) rules = rules.WithLegacyCollisions(true).WithLegacyFlight(true); // and before solid deposits, padded hit volumes and inertia (ADR 0033)
        if (version < 17) rules = rules.WithLegacyFeel(true).WithLegacyResources(true); // and before free fire, recoil, hold overflow and the resource density (ADR 0036)
        if (version < 18) rules = rules.WithLegacyInventory(true); // and before the personal inventory (ADR 0035)
        if (version < 20) rules = rules.WithLegacyShipAutomation(true); // and before the station-first rules and the slow workbench (ADR 0039)
        if (version < 21) rules = rules.WithLegacyField(true); // and before the infinite rock field (ADR 0040)
        if (version < 22) rules = rules.WithLegacyDock(true); // and before dock modules were the only docking ports (ADR 0041)
        if (version < 23) rules = rules.WithLegacyArms(true); // and before the antenna variants and the heavy cannon (ADR 0042)
        if (version < 24) rules = rules.WithLegacyBlueprint(true); // and before stations were edited in the blueprint editor (ADR 0043)
        if (version < 25) rules = rules.WithLegacyInterior(true); // and before the station floor plan (ADR 0044)
        scenarioId = null;
        if ((flags & 16) != 0)
        {
            int length = r.ReadInt32();
            if (length < 1 || length > MaxScenarioIdBytes) throw new InvalidDataException("Bad scenario id length.");
            var bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new InvalidDataException("The scenario id is truncated.");
            scenarioId = Encoding.UTF8.GetString(bytes);
        }
        int count = r.ReadInt32();
        if (count < 0) throw new InvalidDataException("Negative entry count.");

        var log = new List<LoggedCommand>();
        long lastTick = long.MinValue;
        for (int i = 0; i < count; i++)
        {
            long tick = r.ReadInt64();
            if (tick < lastTick) throw new InvalidDataException("Log entries are not in tick order.");
            lastTick = tick;
            int player = r.ReadInt32();
            var command = Read(r);
            if (version < 9 && HasFireLaser(command)) throw new InvalidDataException("A log older than version 9 cannot hold a FireLaser command.");
            if (version < 10 && HasBoost(command)) throw new InvalidDataException("A log older than version 10 cannot hold a SetBoost command.");
            if (version < 11 && HasDocking(command)) throw new InvalidDataException("A log older than version 11 cannot hold a docking command.");
            if (version < 12 && HasAttitude(command)) throw new InvalidDataException("A log older than version 12 cannot hold a SetAttitude command.");
            if (version < 14 && HasDesigner(command)) throw new InvalidDataException("A log older than version 14 cannot hold a DesignerApply command.");
            if (version < 17 && HasAim(command)) throw new InvalidDataException("A log older than version 17 cannot hold a SetAim command.");
            if (version < 18 && HasInventoryCommand(command)) throw new InvalidDataException("A log older than version 18 cannot hold an inventory command.");
            if (version < 25 && HasInteriorBlueprint(command)) throw new InvalidDataException("A log older than version 25 cannot hold a station interior blueprint.");
            if (version < 24 && HasBlueprint(command)) throw new InvalidDataException("A log older than version 24 cannot hold an ApplyBlueprint command.");
            if (version < 20 && HasBenchCommand(command)) throw new InvalidDataException("A log older than version 20 cannot hold a workbench queue command.");
            if (version < 19 && HasDropCommand(command)) throw new InvalidDataException("A log older than version 19 cannot hold a drop or pick-up command.");
            log.Add(new LoggedCommand(tick, player, command));
        }
        return log;
    }

    private static bool HasInteriorBlueprint(Command c) => c is ApplyBlueprint { Interior: not null } || (c is Batch b && b.Commands.Any(HasInteriorBlueprint));
    private static bool HasBlueprint(Command c) => c is ApplyBlueprint || (c is Batch b && b.Commands.Any(HasBlueprint));
    private static bool HasBenchCommand(Command c) => c is QueueBench or ClearBench || (c is Batch b && b.Commands.Any(HasBenchCommand));
    private static bool HasDropCommand(Command c) => c is DropItems or PickUp || (c is Batch b && b.Commands.Any(HasDropCommand));
    private static bool HasAim(Command c) => c is SetAim || (c is Batch b && b.Commands.Any(HasAim));
    private static bool HasInventoryCommand(Command c) => c is MoveItems or SetFeedSource or SortInventory || (c is Batch b && b.Commands.Any(HasInventoryCommand));
    private static bool HasDesigner(Command c) => c is DesignerApply || (c is Batch b && b.Commands.Any(HasDesigner));
    private static bool HasAttitude(Command c) => c is SetAttitude || (c is Batch b && b.Commands.Any(HasAttitude));
    private static bool HasBoost(Command c) => c is SetBoost || (c is Batch b && b.Commands.Any(HasBoost));
    private static bool HasDocking(Command c) => c is DockAt or Undock or TransferItems || (c is Batch b && b.Commands.Any(HasDocking));

    private static bool HasFireLaser(Command c) => c is FireLaser || (c is Batch b && b.Commands.Any(HasFireLaser));
}
