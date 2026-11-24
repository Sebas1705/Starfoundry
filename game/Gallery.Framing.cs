using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

public partial class Gallery
{
    /// <summary>
    /// --focus=framing: how the planar module materials frame their panels (see <see cref="FaceUv"/>). Front row, left to right: a cube,
    /// a chamfered prism, a tapered block (sloped sides), a twelve-sided drum with a dome, a beam frame. Behind it a three-module ship strip
    /// and a three-module station strip. Every face should carry whole panels, with a seam on each border.
    /// </summary>
    private void BuildFramingGallery()
    {
        var hull = Materials.ModuleHull();
        var beam = Materials.ModuleStruct();
        var col = new Color(0.82f, 0.86f, 0.94f);
        void Part(string key, Material m, float x, System.Action<Models.Mb> build) =>
            Models.Add(this, key, "framing." + key, m, build, new Vector3(x, 0, 0));

        Part("cube", hull, 0, mb => mb.C(col).Block(new(0, 1f, 0), new(4f, 2f, 4f)));
        Part("chamfer", hull, 7, mb =>
        {
            var o = new Vector2[] { new(-2, -1.1f), new(-1.1f, -2), new(1.1f, -2), new(2, -1.1f), new(2, 1.1f), new(1.1f, 2), new(-1.1f, 2), new(-2, 1.1f) };
            mb.C(col).Extrude(o, 0f, 2f);
        });
        Part("taper", hull, 14, mb => mb.C(col).Block(new(0, 1f, 0), new(4f, 2f, 4f), 0.55f, 0.55f));
        Part("drum", hull, 21, mb =>
        {
            mb.C(col).Prism(new(0, 0, 0), 1.6f, 1.6f, 2f, 12);
            mb.C(col).Sphere(new(0, 2f, 0), new(1.6f, 1.0f, 1.6f), 6, 12, true);
        });
        Part("frame", beam, 28, mb =>
        {
            mb.C(new Color(0.46f, 0.50f, 0.58f));
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -2f : 2f, z = i < 2 ? -2f : 2f;
                mb.Beam(new(x, 0, z), new(x, 2f, z), 0.2f);
            }
            for (int i = 0; i < 2; i++)
            {
                float y = i * 2f;
                mb.Beam(new(-2, y, -2), new(2, y, -2), 0.2f); mb.Beam(new(-2, y, 2), new(2, y, 2), 0.2f);
                mb.Beam(new(-2, y, -2), new(-2, y, 2), 0.2f); mb.Beam(new(2, y, -2), new(2, y, 2), 0.2f);
            }
        });

        ModuleKind[] kinds = { ModuleKind.Cargo, ModuleKind.Hull, ModuleKind.Workshop };
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < 3; i++)
            {
                int ix = i;
                bool Has(int x, int y, int z) => y == 0 && z == 0 && x >= 0 && x < 3;
                var m = ModuleModels.Build(kinds[i], row == 0, 0, ModuleModels.NeighbourMask(Has, ix, 0, 0));
                m.Position = new Vector3(ix * 8f - 2f, 0, 9f + row * 10f);
                AddChild(m);
                _animated.Add(m);
            }
    }
}
