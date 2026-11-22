using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Nothing floats inside a structure: a machine or a belt on an upper layer of a module stands on a steel post that reaches down to the
/// deck. All posts of a structure are one MultiMesh child of its machine node, rebuilt with the rest of the geometry.
/// </summary>
public partial class Play
{
    private static readonly Color PostColour = new(0.28f, 0.31f, 0.38f);
    private static readonly Color PostFoot = new(0.19f, 0.21f, 0.27f);

    /// <summary>Top of the deck plate of a module, in cells above the module's floor.</summary>
    private const float DeckTop = 0.09f;

    private static int FloorOf(int y) => (int)Math.Floor(y / (double)StationGrid.ModuleY) * StationGrid.ModuleY;

    /// <summary>Adds the support posts under every machine and belt that is not on the floor layer of its module (structures only, not open space).</summary>
    private void BuildSupports()
    {
        var posts = new Dictionary<int, List<(Vector3 Pos, float Height)>>();
        void Post(int structure, float x, float z, int y, float bottomOffset)
        {
            int floor = FloorOf(y);
            float top = y + bottomOffset, h = top - floor - DeckTop;
            if (h < 0.05f) return;
            if (!posts.TryGetValue(structure, out var l)) posts[structure] = l = new List<(Vector3, float)>();
            l.Add((new Vector3(x, floor + DeckTop, z), h));
        }

        foreach (int id in _machineIds)
        {
            var m = Net.GetMachineInfo(id);
            if (m.Structure == 0 || FloorOf(m.Y) == m.Y) continue;
            Post(m.Structure, m.X + 0.5f, m.Z + 0.5f, m.Y, 0.14f);
        }
        for (int id = 0; id < _segs.Length; id++)
        {
            if (!Net.IsAlive(id)) continue;
            var s = Net.GetSegmentInfo(id);
            if (s.Structure == 0 || s.DirY != 0 || FloorOf(s.OriginY) == s.OriginY) continue;
            int len = s.LengthCells;
            // a post under the first cell, the last cell and every third cell between
            for (int i = 0; i < len; i++)
                if (i == 0 || i == len - 1 || i % 3 == 0)
                    Post(s.Structure, s.OriginX + s.DirX * i + 0.5f, s.OriginZ + s.DirZ * i + 0.5f, s.OriginY, 0.0f);
        }

        foreach (var (structure, list) in posts)
        {
            if (!_geos.TryGetValue(structure, out var geo)) continue;
            // two boxes per post: a slim shaft and a wider foot plate on the deck
            var buf = new float[list.Count * 2 * FloatsPerItem];
            for (int i = 0; i < list.Count; i++)
            {
                var (p, h) = list[i];
                Write(buf, (i * 2) * FloatsPerItem, new Vector3(0.34f, h, 0.34f), p + new Vector3(0f, h / 2f, 0f), PostColour);
                Write(buf, (i * 2 + 1) * FloatsPerItem, new Vector3(0.7f, 0.06f, 0.7f), p + new Vector3(0f, 0.03f, 0f), PostFoot);
            }
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new BoxMesh { Size = Vector3.One, Material = VertexMaterial() }, InstanceCount = list.Count * 2,
            };
            mm.Buffer = buf;
            geo.Machines.AddChild(new MultiMeshInstance3D { Name = "Supports", Multimesh = mm });
        }
    }
}
