"""Small geometry kit for the Starfoundry Blender generators (runs inside Blender, headless).

Everything is authored in the GAME frame (the module frame of Godot: X right, Y up, Z back, one unit is one cell) and converted to Blender's Z-up frame
only when a vertex is created (G2B below), so the glTF exporter, which converts back to Y up, writes the numbers the game uses. Primitives are added to a
`Part` (one Blender object = one Godot mesh = one material slot in the game). Each primitive carries a flat vertex colour; the colour ACCENT is a tag the
game replaces with the trim colour of the structure's variant.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

# Blender (x, -z, y) from game (x, y, z): a proper rotation, so winding is preserved.
G2B = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

ACCENT = (1.0, 0.0, 1.0, 1.0)  # replaced at runtime by the structure's trim colour


def b(v):
    """Game vector -> Blender vector."""
    return Vector((v[0], -v[2], v[1]))


def g(v):
    """Blender vector -> game vector."""
    return Vector((v[0], v[2], -v[1]))


def col(r, gg, bb, a=1.0):
    return (r, gg, bb, a)


def _basis(d):
    """Two unit vectors perpendicular to d (game frame)."""
    d = Vector(d).normalized()
    ref = Vector((0, 1, 0)) if abs(d.y) < 0.95 else Vector((1, 0, 0))
    ex = d.cross(ref).normalized()
    ey = d.cross(ex).normalized()
    return ex, ey


def rot_matrix(deg):
    """Game-frame rotation from Euler degrees (applied X, then Y, then Z)."""
    rx, ry, rz = (math.radians(a) for a in deg)
    return Matrix.Rotation(rz, 3, 'Z') @ Matrix.Rotation(ry, 3, 'Y') @ Matrix.Rotation(rx, 3, 'X')


class Part:
    """One mesh: a pile of primitives that share a material in the game."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.pid = self.bm.faces.layers.int.new("pid")
        self.closed = self.bm.faces.layers.int.new("closed")
        self.smooth = self.bm.faces.layers.int.new("smooth")
        self.colours = {}
        self._next = 1

    # ---- bookkeeping ----------------------------------------------------------------------------------------------------
    def _begin(self):
        pid = self._next
        self._next += 1
        return pid

    def _end(self, pid, colour, closed=True, smooth=False):
        self.colours[pid] = colour
        for f in self.bm.faces:
            if f[self.pid] == 0:
                f[self.pid] = pid
                f[self.closed] = 1 if closed else 0
                f[self.smooth] = 1 if smooth else 0

    def _edges(self, verts):
        """The edges of freshly made verts in a fixed order (sets of mesh elements iterate in memory order, which would make the export differ run to run)."""
        self.bm.edges.index_update()
        return sorted({e for v in verts for e in v.link_edges}, key=lambda e: e.index)

    # ---- primitives -----------------------------------------------------------------------------------------------------
    def obox(self, centre, ex, ey, ez, colour, bevel=0.0, segs=1):
        """A box from three full-length axis vectors (game frame); any parallelepiped works."""
        pid = self._begin()
        m = Matrix((
            (ex[0], ey[0], ez[0], centre[0]),
            (ex[1], ey[1], ez[1], centre[1]),
            (ex[2], ey[2], ez[2], centre[2]),
            (0, 0, 0, 1)))
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=G2B @ m)
        if bevel > 0:
            edges = self._edges(ret['verts'])
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=segs, offset_type='OFFSET', profile=0.5, affect='EDGES', clamp_overlap=True)
        self._end(pid, colour)

    def box(self, centre, size, colour, rot=(0, 0, 0), bevel=0.0, segs=1):
        r = rot_matrix(rot)
        self.obox(centre, r @ Vector((size[0], 0, 0)), r @ Vector((0, size[1], 0)), r @ Vector((0, 0, size[2])), colour, bevel, segs)

    def beam(self, a, bpt, w, t, colour, up=(0, 1, 0), bevel=0.0):
        """A bar from a to bpt with a w x t section; w lies along `up` projected perpendicular to the bar."""
        a, bpt = Vector(a), Vector(bpt)
        d = bpt - a
        ln = d.length
        dn = d / ln
        u = Vector(up) - dn * Vector(up).dot(dn)
        if u.length < 1e-4:
            u = _basis(dn)[0]
        u.normalize()
        s = dn.cross(u).normalized()
        self.obox((a + bpt) / 2, d, u * w, s * t, colour, bevel)

    def cyl(self, a, bpt, r, colour, segs=8, r2=None, bevel=0.0, smooth=False, cap=True):
        """A cylinder or a cone from a (radius r) to bpt (radius r2)."""
        a, bpt = Vector(a), Vector(bpt)
        d = bpt - a
        ex, ey = _basis(d)
        pid = self._begin()
        m = Matrix((
            (ex[0], ey[0], d[0], (a[0] + bpt[0]) / 2),
            (ex[1], ey[1], d[1], (a[1] + bpt[1]) / 2),
            (ex[2], ey[2], d[2], (a[2] + bpt[2]) / 2),
            (0, 0, 0, 1)))
        ret = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=1.0, matrix=G2B @ m)
        if bevel > 0:
            edges = self._edges(ret['verts'])
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=1, offset_type='OFFSET', profile=0.5, affect='EDGES', clamp_overlap=True)
        self._end(pid, colour, closed=cap, smooth=smooth)

    def ball(self, centre, radii, colour, segs=8, rings=5, smooth=True):
        pid = self._begin()
        m = Matrix((
            (radii[0], 0, 0, centre[0]),
            (0, radii[1], 0, centre[1]),
            (0, 0, radii[2], centre[2]),
            (0, 0, 0, 1)))
        bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=1.0, matrix=G2B @ m)
        self._end(pid, colour, smooth=smooth)

    def poly(self, pts, colour, out=None, closed=False):
        """A single flat polygon (game-frame points); `out` is the direction its front face points to."""
        pid = self._begin()
        vs = [self.bm.verts.new(b(p)) for p in pts]
        f = self.bm.faces.new(vs)
        if out is not None:
            n = g(f.normal)
            if n.dot(Vector(out)) < 0:
                f.normal_flip()
        self._end(pid, colour, closed=closed)

    def quad(self, p0, p1, p2, p3, colour, out=None):
        self.poly([p0, p1, p2, p3], colour, out)

    # ---- finishing ------------------------------------------------------------------------------------------------------
    def _recalc(self):
        faces = [f for f in self.bm.faces if f[self.closed] == 1]
        if faces:
            bmesh.ops.recalc_face_normals(self.bm, faces=faces)

    def finish(self, parent=None, uv_tile=1.0):
        """Normals, UVs, vertex colours and the Blender object. Returns (object, triangle count) or None when empty."""
        bm = self.bm
        if not bm.faces:
            bm.free()
            return None
        self._recalc()
        bm.normal_update()
        uv_frames(bm, uv_tile)
        cl = bm.loops.layers.float_color.new("Color")
        for f in bm.faces:
            c = self.colours[f[self.pid]]
            f.smooth = bool(f[self.smooth])
            for lp in f.loops:
                lp[cl] = c
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        mesh.color_attributes.active_color = mesh.color_attributes["Color"]
        mesh.color_attributes.render_color_index = mesh.color_attributes.active_color_index
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if parent is not None:
            obj.parent = parent
        mesh.calc_loop_triangles()
        return obj, len(mesh.loop_triangles)


# ---- UVs: a port of game/FaceUv.cs (whole panels per coplanar face, in module units) --------------------------------------------

def _q(x):
    return int(round(x * 1000.0))


def _fit(length, mn, tile):
    if length >= 0.5 * tile:
        panels = max(1, int(round(length / tile)))
        scale = panels / length
        return scale, -mn * scale
    scale = 1.0 / tile
    return scale, 0.5 - (mn + length / 2.0) * scale


def uv_frames(bm, tile=1.0):
    """Face-framed UVs in the game frame: coplanar neighbouring faces form one face; its longer side is u; a whole number of panels fits each side."""
    uvl = bm.loops.layers.uv.verify()
    faces = [f for f in bm.faces if f.calc_area() > 1e-9]
    for f in bm.faces:  # degenerate faces keep zero UVs
        for lp in f.loops:
            lp[uvl].uv = (0.0, 0.0)

    def vkey(v):
        p = g(v.co)
        return (_q(p.x), _q(p.y), _q(p.z))

    bm.faces.index_update()
    parent = {f.index: f.index for f in faces}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    nrm = {f.index: g(f.normal) for f in faces}
    edges = {}
    for f in faces:
        ks = [vkey(l.vert) for l in f.loops]
        for i in range(len(ks)):
            e = (ks[i], ks[(i + 1) % len(ks)])
            e = e if e[0] < e[1] else (e[1], e[0])
            for o in edges.get(e, ()):
                if nrm[o].dot(nrm[f.index]) > 0.9999:
                    parent[find(o)] = find(f.index)
            edges.setdefault(e, []).append(f.index)
    islands = {}
    for f in faces:
        islands.setdefault(find(f.index), []).append(f)

    for fl in islands.values():
        n = nrm[fl[0].index]
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        if ax >= 0.9995:
            tu, tv = Vector((0, 0, 1)), Vector((0, 1, 0))
        elif ay >= 0.9995:
            tu, tv = Vector((1, 0, 0)), Vector((0, 0, 1))
        elif az >= 0.9995:
            tu, tv = Vector((1, 0, 0)), Vector((0, 1, 0))
        else:
            cnt = {}
            for f in fl:
                ks = [vkey(l.vert) for l in f.loops]
                for i in range(len(ks)):
                    e = (ks[i], ks[(i + 1) % len(ks)])
                    e = e if e[0] < e[1] else (e[1], e[0])
                    cnt[e] = cnt.get(e, 0) + 1
            best = 0.0
            tu = Vector((0, 1, 0)).cross(n)
            tu = tu.normalized() if tu.length > 1e-6 else Vector((1, 0, 0))
            for f in fl:
                vs = [g(l.vert.co) for l in f.loops]
                ks = [vkey(l.vert) for l in f.loops]
                for i in range(len(vs)):
                    e = (ks[i], ks[(i + 1) % len(ks)])
                    e = e if e[0] < e[1] else (e[1], e[0])
                    if cnt[e] != 1:
                        continue
                    d = vs[(i + 1) % len(vs)] - vs[i]
                    d -= n * d.dot(n)
                    ln = d.length
                    if ln > best + 1e-4:
                        best, tu = ln, d / ln
            tv = n.cross(tu).normalized()
        pts = [g(l.vert.co) for f in fl for l in f.loops]

        def measure(tu_, tv_):
            us = [p.dot(tu_) for p in pts]
            vs_ = [p.dot(tv_) for p in pts]
            return min(us), max(us), min(vs_), max(vs_)

        u0, u1, v0, v1 = measure(tu, tv)
        if v1 - v0 > (u1 - u0) * 1.001:
            tu, tv = tv, tu
            u0, u1, v0, v1 = measure(tu, tv)
        su, ou = _fit(u1 - u0, u0, tile)
        sv, ov = _fit(v1 - v0, v0, tile)
        for f in fl:
            for lp in f.loops:
                p = g(lp.vert.co)
                u = p.dot(tu) * su + ou
                v = p.dot(tv) * sv + ov
                lp[uvl].uv = (u, 1.0 - v)  # glTF flips v: the game then sees the v that FaceUv.cs would have written
