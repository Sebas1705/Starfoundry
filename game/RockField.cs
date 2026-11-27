using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The rocks of the streamed field (ADR 0040, 0046) drawn as MultiMesh batches, one batch per loaded sector, instead of two nodes per rock. A sector holds its rocks
/// in a few MultiMeshes: near the camera (<see cref="NearRange"/>) one per ore, size class, richness bucket and depletion level, each of a shared two-surface mesh
/// (rock with vein paint plus ore chunks); farther, one per size class of a shared 80-face mesh tinted per instance. Instances are placed relative to the sector,
/// so a sector is one node with a small local range. Rocks stay individually mineable and targetable: the simulation owns them, picking is a ray against their
/// spheres (<c>Play</c>), and a hidden, emptied or forgotten rock is simply left out of its sector's batch the next time it is rebuilt.
/// </summary>
public sealed partial class RockField : Node3D
{
    /// <summary>Distance in cells from the camera to a sector box within which the sector draws its near tier.</summary>
    public const float NearRange = 110f;
    private const float Hysteresis = 25f;

    private sealed class Slot { public bool Known; public int Serial; public bool Shown; public int Level; public long Sector; }

    private sealed class Batch
    {
        public long Key;
        public Node3D Root = null!;
        public Vector3 Min;
        public readonly HashSet<int> Ids = new();
        public bool Dirty = true, Near, Applied;
        public readonly List<MultiMeshInstance3D> NearNodes = new(), FarNodes = new();
        public int Instances;
    }

    private readonly List<Slot> _slots = new();
    private readonly Dictionary<long, Batch> _batches = new();
    private readonly List<long> _empty = new();
    private readonly List<(float D2, Batch B)> _order = new();
    private const int MaxRebuildsPerFrame = 10;

    /// <summary>Rocks currently drawn (visible instances), batches and MultiMesh nodes, for the measurement.</summary>
    public int DrawnRocks { get; private set; }
    public int BatchCount => _batches.Count;
    public int MultiMeshNodes { get; private set; }
    public int NearBatches { get; private set; }

    public RockField() { Name = "RockField"; }

    /// <summary>Brings the batches in step with the simulation and picks the tier of each sector for the camera at <paramref name="camera"/> (cells).</summary>
    public void Sync(BeltNetwork net, ContentDb content, Vector3 camera)
    {
        int count = net.DepositCount;
        while (_slots.Count < count) _slots.Add(new Slot());
        for (int d = 0; d < count; d++)
        {
            var s = _slots[d];
            if (!net.DepositIsFieldRock(d))
            {
                if (s.Known) Detach(d, s);
                continue;
            }
            int serial = net.DepositSerial(d), remaining = net.DepositRemaining(d), initial = Math.Max(1, net.DepositInitial(d));
            bool shown = net.IsDepositRevealed(d) && remaining > 0;
            int level = DepositView.LevelOf(remaining, initial);
            if (s.Known && s.Serial == serial)
            {
                if (s.Shown == shown && (s.Level == level || !shown)) continue;
                s.Shown = shown; s.Level = level;
                if (_batches.TryGetValue(s.Sector, out var same)) same.Dirty = true;
                continue;
            }
            if (s.Known) Detach(d, s);
            long key = net.DepositSectorKey(d);
            s.Known = true; s.Serial = serial; s.Shown = shown; s.Level = level; s.Sector = key;
            var b = BatchOf(key, net);
            b.Ids.Add(d);
            b.Dirty = true;
        }
        _empty.Clear();
        int sector = net.FieldSectorCells;
        DrawnRocks = 0; MultiMeshNodes = 0; NearBatches = 0;
        _order.Clear();
        foreach (var (key, b) in _batches)
        {
            if (b.Ids.Count == 0) { _empty.Add(key); continue; }
            // distance from the camera to the sector box: the tier, and the order in which dirty sectors are rebuilt
            float dx = MathF.Max(MathF.Max(b.Min.X - camera.X, 0f), camera.X - (b.Min.X + sector));
            float dy = MathF.Max(MathF.Max(b.Min.Y - camera.Y, 0f), camera.Y - (b.Min.Y + sector));
            float dz = MathF.Max(MathF.Max(b.Min.Z - camera.Z, 0f), camera.Z - (b.Min.Z + sector));
            _order.Add((dx * dx + dy * dy + dz * dz, b));
        }
        int budget = MaxRebuildsPerFrame; // the first use of an ore, size and bucket builds its mesh: a scan that reveals a whole cluster spreads over a few frames, the nearest sectors first
        if (_order.Count > budget) _order.Sort((a, c) => a.D2.CompareTo(c.D2));
        foreach (var (d2, b) in _order)
        {
            if (b.Dirty && budget > 0) { Rebuild(b, net, content); budget--; }
            float dist = MathF.Sqrt(d2);
            bool near = dist < (b.Near ? NearRange + Hysteresis : NearRange); // with a margin so a rock at the edge does not flicker between tiers
            if (near != b.Near || !b.Applied)
            {
                b.Near = near; b.Applied = true;
                foreach (var n in b.NearNodes) n.Visible = near;
                foreach (var n in b.FarNodes) n.Visible = !near;
            }
            if (near) NearBatches++;
            MultiMeshNodes += near ? b.NearNodes.Count : b.FarNodes.Count;
            DrawnRocks += b.Instances;
        }
        foreach (long key in _empty) Free(key);
    }

    private Batch BatchOf(long key, BeltNetwork net)
    {
        if (_batches.TryGetValue(key, out var b)) return b;
        var (cx, cy, cz) = SectorField.Unkey(key);
        int side = net.FieldSectorCells;
        b = new Batch { Key = key, Min = new Vector3(cx * (float)side, cy * (float)side, cz * (float)side) };
        b.Root = new Node3D { Name = $"Sector{cx}_{cy}_{cz}", Position = b.Min };
        AddChild(b.Root);
        _batches[key] = b;
        return b;
    }

    private void Detach(int d, Slot s)
    {
        if (_batches.TryGetValue(s.Sector, out var b)) { b.Ids.Remove(d); b.Dirty = true; }
        s.Known = false; s.Shown = false;
    }

    private void Free(long key)
    {
        if (!_batches.Remove(key, out var b)) return;
        b.Root.QueueFree();
    }

    private static int Hash(int x, int y, int z) => unchecked(x * 73856093 ^ y * 19349663 ^ z * 83492791);

    /// <summary>Rebuilds the MultiMeshes of one sector from the rocks that are revealed and not emptied.</summary>
    private void Rebuild(Batch b, BeltNetwork net, ContentDb content)
    {
        foreach (var n in b.NearNodes) n.QueueFree();
        foreach (var n in b.FarNodes) n.QueueFree();
        b.NearNodes.Clear(); b.FarNodes.Clear();
        b.Dirty = false; b.Applied = false; b.Instances = 0;
        var near = new Dictionary<(int Item, int Cls, int Bucket, int Level), List<(Transform3D T, int D)>>();
        var far = new Dictionary<(int Cls, int Item, int Bucket), List<Transform3D>>();
        int radiusPercent = Math.Max(1, net.RockRadiusPercent);
        foreach (int d in b.Ids)
        {
            var s = _slots[d];
            if (!s.Shown) continue;
            var q = net.DepositPosition(d);
            int scalePermille = net.DepositRockScalePermille(d);
            int cls = DepositView.SizeClass(scalePermille * 100 / radiusPercent);
            int bucket = DepositView.Bucket(net.DepositDensity(d));
            int h = Hash(q.X, q.Y, q.Z);
            var rng = new Models.Rng(h);
            var basis = Basis.FromEuler(new Vector3(rng.F() * Mathf.Tau, rng.F() * Mathf.Tau, rng.F() * Mathf.Tau))
                .Scaled(Vector3.One * (scalePermille / 1000f) * new Vector3(0.9f + 0.2f * rng.F(), 0.9f + 0.2f * rng.F(), 0.9f + 0.2f * rng.F()));
            var t = new Transform3D(basis, new Vector3(q.X, q.Y, q.Z) - b.Min);
            int item = net.DepositItem(d);
            (near.TryGetValue((item, cls, bucket, s.Level), out var nl) ? nl : near[(item, cls, bucket, s.Level)] = new()).Add((t, d));
            (far.TryGetValue((cls, item, bucket), out var fl) ? fl : far[(cls, item, bucket)] = new()).Add(t);
            b.Instances++;
        }
        var (kx, ky, kz) = SectorField.Unkey(b.Key);
        int variant = (int)((uint)Hash(kx, ky, kz) % DepositView.Variants);
        foreach (var (key, list) in near)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = DepositView.SharedNearMesh(content.Items[key.Item].Id, key.Cls, key.Bucket, key.Level, variant),
                InstanceCount = list.Count,
            };
            for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i].T);
            var node = new MultiMeshInstance3D { Name = $"Near{key.Item}_{key.Cls}_{key.Bucket}_{key.Level}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            b.Root.AddChild(node);
            b.NearNodes.Add(node);
        }
        // far tier: one MultiMesh per size class; the ore tint is the instance colour, so every ore of a class shares one draw call
        var byClass = new Dictionary<int, List<(Transform3D T, Color C)>>();
        foreach (var ((cls, item, bucket), list) in far)
        {
            var color = DepositView.FarTint(content.Items[item].Id, bucket);
            foreach (var t in list) (byClass.TryGetValue(cls, out var l) ? l : byClass[cls] = new()).Add((t, color));
        }
        foreach (var (cls, list) in byClass)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = DepositView.SharedFarMesh(cls, variant), InstanceCount = list.Count,
            };
            for (int i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, list[i].T); mm.SetInstanceColor(i, list[i].C); }
            var node = new MultiMeshInstance3D { Name = $"Far{cls}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            b.Root.AddChild(node);
            b.FarNodes.Add(node);
        }
    }
}
