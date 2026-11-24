"""Builds the starter cockpit DETAIL (not the shell, not the collision) and exports game/assets/blender/cockpit_wide.glb.

    blender --background --python tools/blender/cockpit.py -- [--shell tools/blender/shell.json] [--out game/assets/blender/cockpit_wide.glb] [--preview dir]

Deterministic: the same shell.json gives the same file. All dimensions come from shell.json (written from C# by the test suite); the palette is the
one of game/ModuleModels.cs. Node names are the contract with the game (game/BlenderCockpit.cs):
  Seat (empty, hidden while seated) > Seat_Hull, Seat_Struct          hull plating / structural metal materials
  Console_Hull, Console_Struct, Frames_Struct, Details_Hull, Details_Struct
  Glass                                                                the game's glass material (no vertex colours)
  Glow_Amber, Glow_Green, Glow_Cyan, Glow_Red, Glow_Warm               emissive marks, one node per colour
Vertex colour (1, 0, 1) is the accent tag: the game paints it with the structure's trim colour.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sfgeo  # noqa: E402
from sfgeo import ACCENT, Part  # noqa: E402

# ---- palette (game/ModuleModels.cs) --------------------------------------------------------------------------------------------
DARK = (0.12, 0.14, 0.19, 1)
DECK = (0.19, 0.21, 0.27, 1)
STEEL = (0.46, 0.50, 0.58, 1)
PANEL = (0.72, 0.76, 0.83, 1)
ROLE = (0.30, 0.80, 1.0, 1)  # RoleColour(Cockpit)
YELLOW = (0.98, 0.86, 0.18, 1)
RED = (0.90, 0.22, 0.22, 1)
WHITE = (0.90, 0.93, 0.98, 1)
WELL = (0.05, 0.055, 0.07, 1)  # the dark bezel the displays sit in (the colour CockpitDisplays gives its bezels)
NOCOL = (1, 1, 1, 1)


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(os.path.join(here, '..', '..'))
    opts = {'shell': os.path.join(here, 'shell.json'), 'out': os.path.join(root, 'game', 'assets', 'blender', 'cockpit_wide.glb'), 'preview': None}
    i = 0
    while i < len(argv):
        if argv[i] in ('--shell', '--out', '--preview'):
            opts[argv[i][2:]] = argv[i + 1]
            i += 2
        else:
            i += 1
    return opts


V = Vector


class Build:
    def __init__(self, shell):
        self.s = shell
        self.parts = {}
        self.deck = shell['console']['deck']
        self.o = V(self.deck['deckToModuleOrigin'])
        self.dx, self.dy, self.dz = V(self.deck['deckToModuleX']), V(self.deck['deckToModuleY']), V(self.deck['deckToModuleZ'])

    def part(self, name):
        if name not in self.parts:
            self.parts[name] = Part(name)
        return self.parts[name]

    # deck frame -> module frame
    def D(self, x, y, z):
        return self.o + self.dx * x + self.dy * y + self.dz * z

    def Dv(self, x, y, z):
        return self.dx * x + self.dy * y + self.dz * z

    # ---- a hull-shaped prism along X ---------------------------------------------------------------------------------------
    def prism_x(self, part, profile_zy, x0, x1, colour):
        """A closed prism: profile points (z, y) counter-clockwise seen from +X, extruded from x0 to x1."""
        bm = part.bm
        pid = part._begin()
        a = [bm.verts.new(sfgeo.b((x0, y, z))) for z, y in profile_zy]
        c = [bm.verts.new(sfgeo.b((x1, y, z))) for z, y in profile_zy]
        bm.faces.new(a[::-1])
        bm.faces.new(c)
        n = len(a)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], c[j], c[i]))
        part._end(pid, colour, closed=True)

    # =====================================================================================================================
    def console(self):
        hull, struct, glass = self.part('Console_Hull'), self.part('Console_Struct'), self.part('Glass')
        gl = {k: self.part('Glow_' + k) for k in ('Amber', 'Green', 'Cyan', 'Red')}
        HX, ZF, ZN, TOP, FLOOR, CH = 2.55, -0.60, 0.60, 0.19, 0.09, 0.02
        up = self.Dv(0, 1, 0).normalized()
        # ---- the body under the deck: dark housing, steel side plates, a toe kick ---------------------------------------
        def under(zl):  # module (z, y) of the underside of the deck at deck-z zl
            p = self.D(0, 0, zl)
            return p.z, p.y
        zf, yf = under(-0.5888)
        zn, yn = under(0.5783)
        body = [(zf, 0.086), (2.88, 0.086), (3.10, 0.30), (zn, yn), (zf, yf)]
        self.prism_x(hull, body, 4 - 2.5 * 0.72, 4 + 2.5 * 0.72, DARK)
        side = [(zf - 0.03, 0.086), (2.9, 0.086), (3.13, 0.30), (zn + 0.02, yn + 0.02), (zf - 0.03, yf + 0.02)]
        for s in (-1, 1):
            x = 4 + s * 2.5 * 0.72
            self.prism_x(hull, side, x - 0.045 if s < 0 else x, x if s < 0 else x + 0.045, STEEL)
        # accent and role stripes across the far face, access hatches and vents on the pilot side
        hull.box((4, 0.40, zf - 0.006), (3.5, 0.045, 0.014), ACCENT)
        hull.box((4, 0.31, zf - 0.006), (3.5, 0.02, 0.014), ROLE)
        for hx in (-1.6, 0, 1.6):
            hull.box((4 + hx * 0.72 * 0.8, 0.50, 3.108), (1.0, 0.20, 0.03), DECK, bevel=0.01)
            struct.box((4 + hx * 0.72 * 0.8, 0.50, 3.128), (0.40, 0.035, 0.02), STEEL, bevel=0.006)
        for i in range(9):  # vent slats on the toe kick
            t = i / 8
            struct.box((4, 0.115 + 0.15 * t * 1.0, 2.90 + 0.17 * t), (2.9, 0.014, 0.02), STEEL, rot=(-45, 0, 0))

        # ---- the deck plate: top with a recessed pocket for each screen --------------------------------------------------
        def quad_deck(part, pts, colour, out=(0, 1, 0)):
            part.quad(*[self.D(*p) for p in pts], colour, out=self.Dv(*out))

        pockets = []
        for sc in self.s['console']['screens']:
            x, z, w, d = sc['x'], sc['z'], sc['width'], sc['depth']
            m = 0.03
            inner = (x - w / 2 - m, z - d / 2 - m, x + w / 2 + m, z + d / 2 + m)
            outer = (inner[0] - CH, inner[1] - CH, inner[2] + CH, inner[3] + CH)
            pockets.append((inner, outer))
        # top surface as a grid that leaves the pocket openings out
        xs = sorted({-HX + 0.025, HX - 0.025, *[v for _, o in pockets for v in (o[0], o[2])]})
        zs = sorted({ZF + 0.025, ZN - 0.025, *[v for _, o in pockets for v in (o[1], o[3])]})
        for i in range(len(xs) - 1):
            for j in range(len(zs) - 1):
                cx, cz = (xs[i] + xs[i + 1]) / 2, (zs[j] + zs[j + 1]) / 2
                if any(o[0] < cx < o[2] and o[1] < cz < o[3] for _, o in pockets):
                    continue
                quad_deck(hull, [(xs[i], TOP, zs[j]), (xs[i + 1], TOP, zs[j]), (xs[i + 1], TOP, zs[j + 1]), (xs[i], TOP, zs[j + 1])], DECK)
        # chamfer ring round the plate edge and skirts
        e = 0.025
        edge_in = (-HX + e, ZF + e, HX - e, ZN - e)
        edge_out = (-HX, ZF, HX, ZN)
        self.ring(hull, edge_in, edge_out, TOP, TOP - e, STEEL)
        for (xa, za, xb, zb, nrm) in ((-HX, ZF, HX, ZF, (0, 0, -1)), (-HX, ZN, HX, ZN, (0, 0, 1)), (-HX, ZF, -HX, ZN, (-1, 0, 0)), (HX, ZF, HX, ZN, (1, 0, 0))):
            quad_deck(hull, [(xa, TOP - e, za), (xb, TOP - e, zb), (xb, 0, zb), (xa, 0, za)], STEEL, nrm)
        # pockets: chamfer, wall, floor
        for inner, outer in pockets:
            self.ring(struct, outer, inner, TOP, TOP - CH, STEEL, flip_in=True)
            x0, z0, x1, z1 = inner
            wall_top = TOP - CH
            for (xa, za, xb, zb, nrm) in ((x0, z0, x1, z0, (0, 0, 1)), (x1, z0, x1, z1, (-1, 0, 0)), (x1, z1, x0, z1, (0, 0, -1)), (x0, z1, x0, z0, (1, 0, 0))):
                quad_deck(struct, [(xa, wall_top, za), (xb, wall_top, zb), (xb, FLOOR, zb), (xa, FLOOR, za)], DARK, nrm)
            quad_deck(struct, [(x0, FLOOR, z0), (x1, FLOOR, z0), (x1, FLOOR, z1), (x0, FLOOR, z1)], WELL)

        # ---- the coaming along the far edge with the gauge pods and the warning row --------------------------------------
        cz = -0.545
        hull.obox(self.D(0, TOP + 0.045, cz), self.Dv(4.9, 0, 0), self.Dv(0, 0.09, 0), self.Dv(0, 0, 0.10), DARK, bevel=0.02)
        for gx, g_col in ((-2.12, 'Green'), (-1.72, 'Amber'), (-1.32, 'Cyan'), (1.32, 'Cyan'), (1.72, 'Green'), (2.12, 'Amber')):
            c0 = self.D(gx, TOP + 0.09, cz)
            struct.cyl(c0, c0 + up * 0.05, 0.085, STEEL, segs=10)
            lens = c0 + up * 0.055
            ex, ez = self.Dv(1, 0, 0).normalized(), self.Dv(0, 0, 1).normalized()
            ring = [lens + (ex * math.cos(a) + ez * math.sin(a)) * 0.068 for a in (k * math.tau / 8 for k in range(8))]
            glass.poly(ring, NOCOL, out=up)
            gl[g_col].quad(lens + up * 0.004 - ex * 0.003 - ez * 0.05, lens + up * 0.004 + ex * 0.003 - ez * 0.05, lens + up * 0.004 + ex * 0.003 + ez * 0.02, lens + up * 0.004 - ex * 0.003 + ez * 0.02, NOCOL, out=up)
        for i, k in enumerate(('Red', 'Amber', 'Amber', 'Green', 'Cyan')):
            c0 = self.D(-0.4 + i * 0.2, TOP + 0.092, cz)
            ex, ez = self.Dv(1, 0, 0).normalized(), self.Dv(0, 0, 1).normalized()
            gl[k].quad(c0 - ex * 0.06 - ez * 0.03, c0 + ex * 0.06 - ez * 0.03, c0 + ex * 0.06 + ez * 0.03, c0 - ex * 0.06 + ez * 0.03, NOCOL, out=up)

        # ---- switch rows along the near edge --------------------------------------------------------------------------------
        ex, ez = self.Dv(1, 0, 0).normalized(), self.Dv(0, 0, 1).normalized()
        keys = ['Amber', 'Green', 'Cyan', 'Amber', 'Red', 'Green']
        for i in range(24):
            x = -2.3 + i * 0.2
            guard = i % 6 == 5
            struct.obox(self.D(x, TOP + 0.03, 0.47), self.Dv(0.045, 0, 0), self.Dv(0, 0.06, 0), self.Dv(0, 0, 0.05), RED if guard else (PANEL if i % 2 else STEEL))
            c0 = self.D(x, TOP + 0.003, 0.555)
            gl[keys[i % 6]].quad(c0 - ex * 0.04 - ez * 0.012, c0 + ex * 0.04 - ez * 0.012, c0 + ex * 0.04 + ez * 0.012, c0 - ex * 0.04 + ez * 0.012, NOCOL, out=up)
        for x in (-2.38, -1.2, 1.2, 2.38):  # rotary knobs
            c0 = self.D(x, TOP, 0.50)
            struct.cyl(c0, c0 + up * 0.07, 0.05, PANEL, segs=8, r2=0.04)
        # the strip between the screens: a trim line and the role stripe
        for gx in (-1.0225, 1.0225):
            quad_deck(hull, [(gx - 0.012, TOP + 0.002, ZF + 0.12), (gx + 0.012, TOP + 0.002, ZF + 0.12), (gx + 0.012, TOP + 0.002, ZN - 0.12), (gx - 0.012, TOP + 0.002, ZN - 0.12)], ACCENT)

    def ring(self, part, outer, inner, y_out, y_in, colour, flip_in=False):
        """Four trapezoids between two rectangles (x0, z0, x1, z1) of the deck frame, the outer at height y_out and the inner at y_in."""
        ox0, oz0, ox1, oz1 = outer
        ix0, iz0, ix1, iz1 = inner
        if flip_in:  # pocket: `outer` is the top rim, `inner` the lower edge; the faces look up and into the pocket
            pass
        o = [(ox0, y_out, oz0), (ox1, y_out, oz0), (ox1, y_out, oz1), (ox0, y_out, oz1)]
        i = [(ix0, y_in, iz0), (ix1, y_in, iz0), (ix1, y_in, iz1), (ix0, y_in, iz1)]
        cen = self.Dv(0, 1, 0)
        for k in range(4):
            a, b2 = o[k], o[(k + 1) % 4]
            c, d = i[(k + 1) % 4], i[k]
            # outward direction of the facet = away from the middle of the rectangle, plus up
            mid = (V(a) + V(b2)) / 2 - (V(o[0]) + V(o[2])) / 2
            mid.y = 0
            out_dir = self.Dv(*mid.normalized()) if not flip_in else self.Dv(*(-mid.normalized()))
            out_dir = out_dir.normalized() * 0.5 + self.Dv(0, 1, 0).normalized()
            part.quad(self.D(*a), self.D(*b2), self.D(*c), self.D(*d), colour, out=out_dir)

    # =====================================================================================================================
    def seat(self):
        root_hull, root_struct = self.part('Seat_Hull'), self.part('Seat_Struct')
        gl = {k: self.part('Glow_' + k) for k in ('Amber', 'Green', 'Cyan', 'Red')}
        ox, oz = 4.0, 4.6
        # base: a five star, a gas lift and a swivel plate
        for i in range(5):
            a = math.radians(72 * i + 18)
            dx, dz = math.sin(a), math.cos(a)
            root_struct.beam((ox + dx * 0.06, 0.13, oz + dz * 0.06), (ox + dx * 0.56, 0.11, oz + dz * 0.56), 0.10, 0.045, DARK, up=(0, 1, 0), bevel=0.008)
            root_struct.cyl((ox + dx * 0.56, 0.0869, oz + dz * 0.56), (ox + dx * 0.56, 0.17, oz + dz * 0.56), 0.04, STEEL, segs=8)
        root_struct.cyl((ox, 0.13, oz), (ox, 0.22, oz), 0.14, DARK, segs=12, r2=0.09)
        root_struct.cyl((ox, 0.22, oz), (ox, 0.62, oz), 0.055, STEEL, segs=10)
        root_struct.cyl((ox, 0.58, oz), (ox, 0.80, oz), 0.12, DARK, segs=12, r2=0.075)
        root_struct.cyl((ox, 0.74, oz), (ox, 0.78, oz), 0.26, STEEL, segs=16)
        # seat pan with bolsters and piping
        root_hull.box((ox, 0.855, oz - 0.02), (1.0, 0.15, 1.05), DARK, bevel=0.05, segs=2)
        for s in (-1, 1):
            root_hull.box((ox + s * 0.46, 0.93, oz - 0.04), (0.14, 0.10, 0.92), DARK, rot=(0, 0, -s * 8), bevel=0.03, segs=2)
        root_hull.box((ox, 0.945, oz - 0.06), (0.74, 0.02, 0.84), ACCENT, bevel=0.008)
        root_hull.box((ox, 0.93, oz - 0.52), (0.80, 0.08, 0.06), DARK, bevel=0.025, segs=2)  # front lip
        # reclined back: shell, lumbar pad, shoulder wings
        piv = V((ox, 0.88, oz + 0.50))
        tilt = 12
        R = sfgeo.rot_matrix((tilt, 0, 0))

        def back(off, size, colour, bevel=0.0, rot_extra=(0, 0, 0), segs=1):
            ctr = piv + R @ V(off)
            r = R @ sfgeo.rot_matrix(rot_extra)
            root_hull.obox(ctr, r @ V((size[0], 0, 0)), r @ V((0, size[1], 0)), r @ V((0, 0, size[2])), colour, bevel, segs)

        back((0, 0.68, 0.0), (1.06, 1.36, 0.20), DARK, bevel=0.05, segs=2)
        back((0, 0.62, -0.115), (0.74, 0.86, 0.045), ACCENT, bevel=0.012)
        back((0, 0.30, -0.115), (0.62, 0.20, 0.06), DECK, bevel=0.02)  # lumbar
        for s in (-1, 1):
            back((s * 0.50, 0.82, -0.07), (0.10, 0.86, 0.20), DARK, bevel=0.035, rot_extra=(0, s * 14, 0), segs=2)
        # headrest on two posts
        for s in (-1, 1):
            root_struct.cyl(piv + R @ V((s * 0.17, 1.30, 0.0)), piv + R @ V((s * 0.17, 1.62, 0.0)), 0.025, STEEL, segs=6)
        back((0, 1.74, -0.01), (0.62, 0.34, 0.16), DARK, bevel=0.05, segs=2)
        back((0, 1.72, -0.095), (0.46, 0.24, 0.03), ACCENT, bevel=0.008)
        # harness straps with buckles
        for s in (-1, 1):
            back((s * 0.30, 0.98, -0.125), (0.075, 0.76, 0.014), PANEL)
        back((0, 0.52, -0.135), (0.20, 0.10, 0.026), STEEL, bevel=0.01)
        # armrests: post, pad
        for s in (-1, 1):
            root_struct.box((ox + s * 0.78, 0.98, oz - 0.02), (0.07, 0.30, 0.10), STEEL, bevel=0.01)
            root_hull.box((ox + s * 0.78, 1.13, oz - 0.04), (0.17, 0.07, 0.92), DARK, bevel=0.025, segs=2)
        # flight stick on the right armrest: boot, shaft, grip, hat, trigger, buttons
        bx, bz = ox + 0.80, 4.30
        root_struct.cyl((bx, 1.165, bz), (bx, 1.20, bz), 0.075, STEEL, segs=10)
        root_struct.cyl((bx, 1.20, bz), (bx + 0.012, 1.38, bz - 0.03), 0.034, DARK, segs=8, r2=0.022)
        gtop = V((bx + 0.025, 1.52, bz - 0.06))
        root_hull.ball((bx + 0.02, 1.47, bz - 0.05), (0.052, 0.10, 0.058), DARK, segs=10, rings=6)
        root_struct.cyl((bx + 0.012, 1.38, bz - 0.03), (bx + 0.02, 1.44, bz - 0.045), 0.03, STEEL, segs=8)
        root_struct.cyl(gtop - V((0, 0.02, 0)), gtop + V((0, 0.012, 0)), 0.022, STEEL, segs=8)
        root_struct.box((bx + 0.02, 1.43, bz - 0.115), (0.02, 0.05, 0.02), PANEL)  # trigger
        gl['Red'].quad((bx - 0.03, 1.5, bz - 0.108), (bx - 0.01, 1.5, bz - 0.108), (bx - 0.01, 1.53, bz - 0.108), (bx - 0.03, 1.53, bz - 0.108), NOCOL, out=(0, 0, -1))
        gl['Amber'].quad((bx + 0.012, 1.5, bz - 0.108), (bx + 0.032, 1.5, bz - 0.108), (bx + 0.032, 1.53, bz - 0.108), (bx + 0.012, 1.53, bz - 0.108), NOCOL, out=(0, 0, -1))
        # throttle quadrant on the left armrest
        tx, tz = ox - 0.80, 4.35
        root_struct.box((tx, 1.205, tz), (0.16, 0.05, 0.52), STEEL, bevel=0.012)
        root_struct.box((tx, 1.235, tz), (0.03, 0.012, 0.40), DARK)  # the slot
        lever_a = V((tx, 1.23, tz + 0.12))
        lever_b = lever_a + V((0, 0.36 * math.cos(math.radians(35)), -0.36 * math.sin(math.radians(35))))
        root_struct.beam(lever_a, lever_b, 0.05, 0.05, DARK, bevel=0.008)
        root_hull.ball(lever_b + V((0, 0.04, -0.02)), (0.055, 0.075, 0.06), DARK, segs=8, rings=5)
        for i in range(5):
            gl['Amber'].quad((tx - 0.062, 1.2325, tz - 0.20 + i * 0.1), (tx - 0.042, 1.2325, tz - 0.20 + i * 0.1), (tx - 0.042, 1.2325, tz - 0.185 + i * 0.1), (tx - 0.062, 1.2325, tz - 0.185 + i * 0.1), NOCOL, out=(0, 1, 0))
        # rudder pedals: a cross bar, two plates on arms
        root_struct.cyl((ox - 0.62, 0.19, 3.80), (ox + 0.62, 0.19, 3.80), 0.025, STEEL, segs=8)
        for s in (-1, 1):
            root_struct.box((ox + s * 0.35, 0.215, 3.72), (0.38, 0.035, 0.27), STEEL, rot=(-24, 0, 0), bevel=0.012)
            root_struct.box((ox + s * 0.35, 0.236, 3.725), (0.34, 0.01, 0.22), DARK, rot=(-24, 0, 0))
            root_struct.beam((ox + s * 0.35, 0.19, 3.80), (ox + s * 0.35, 0.12, 4.05), 0.05, 0.03, DARK)

    # =====================================================================================================================
    def frames(self):
        fr = self.part('Frames_Struct')
        glass = self.part('Glass')
        sh = self.s['shell']
        ring = []
        for p in sh['section']:
            if not ring or (abs(ring[-1][0] - p[0]) > 1e-4 or abs(ring[-1][1] - p[1]) > 1e-4):
                ring.append(p)
        if abs(ring[0][0] - ring[-1][0]) < 1e-4 and abs(ring[0][1] - ring[-1][1]) < 1e-4:
            ring.pop()
        # windscreen pane at z = 0.06 and its frame just behind (towards the pilot)
        z0 = self.s['windscreen']['z']
        glass.poly([(x, y, z0) for x, y in ring], NOCOL, out=(0, 0, 1))
        cx = sum(p[0] for p in ring) / len(ring)
        cy = sum(p[1] for p in ring) / len(ring)
        w, dep = 0.12, 0.10
        for k in range(len(ring)):
            a, b2 = V((ring[k][0], ring[k][1], 0)), V((ring[(k + 1) % len(ring)][0], ring[(k + 1) % len(ring)][1], 0))
            e = b2 - a
            ln = e.length
            en = e / ln
            inward = V((-en.y, en.x, 0))
            if inward.dot(V((cx, cy, 0)) - a) < 0:
                inward = -inward
            ext = w * 0.6
            c = (a + b2) / 2 + inward * (w / 2) + V((0, 0, z0 + 0.01 + dep / 2 + 0.002 * k))
            fr.obox(c, en * (ln + ext), inward * w, V((0, 0, dep)), DARK, bevel=0.012)
        top = max(p[1] for p in ring)
        for x in (1.9, 6.1):  # posts
            fr.box((x, (0.08 + top) / 2, z0 + 0.10), (0.11, top - 0.08, 0.14), DARK, bevel=0.015)
            fr.box((x, (0.08 + top) / 2, z0 + 0.175), (0.05, top - 0.08, 0.02), ACCENT)
        fr.box((4.0, top - 0.17, z0 + 0.10), (5.9, 0.10, 0.12), DARK, bevel=0.015)  # header
        for x in (1.9, 6.1):  # gussets where the posts meet the header
            for s in (-1, 1):
                fr.box((x + s * 0.17, top - 0.26, z0 + 0.10), (0.28, 0.07, 0.10), DARK, rot=(0, 0, -s * 45), bevel=0.01)

        # side windows
        sw = self.s['sideWindows']
        wz0, wz1, wy0, wy1 = sw['z0'], sw['z1'], sw['y0'], sw['y1']
        F = sw['frameWidth']
        n_m = max(0, math.ceil((wz1 - wz0) / sw['mullionEvery']) - 1)
        for left in (True, False):
            xc = sw['leftWallMidX'] if left else sw['rightWallMidX']
            sgn = 1 if left else -1
            x_out = 0.03 if left else 7.97
            x_in = 0.22 if left else 7.78
            cxm, sx = (x_out + x_in) / 2, abs(x_in - x_out)
            zc, wl = (wz0 + wz1) / 2, wz1 - wz0
            fr.box((cxm, wy1 + F / 2, zc), (sx, F, wl + 2 * F), DARK, bevel=0.012)
            fr.box((cxm, wy0 - F / 2, zc), (sx, F, wl + 2 * F), DARK, bevel=0.012)
            for zz in (wz0 - F / 2, wz1 + F / 2):
                fr.box((cxm, (wy0 + wy1) / 2, zz), (sx, wy1 - wy0, F), DARK, bevel=0.012)
            for mi in range(1, n_m + 1):
                zz = wz0 + wl * mi / (n_m + 1)
                fr.box((cxm, (wy0 + wy1) / 2, zz), (sx - 0.02, wy1 - wy0, 0.08), DARK, bevel=0.01)
            # sill (steel) and a thin accent line on it
            fr.box((x_out + sgn * 0.14 + (0.0), wy0 - F - 0.03, zc), (0.28, 0.06, wl + 2 * F + 0.1), STEEL, bevel=0.012)
            fr.box((x_in - sgn * 0.0 + sgn * 0.03, wy0 - F - 0.0, zc), (0.03, 0.012, wl - 0.2), ACCENT)
            # glass
            glass.quad((xc, wy0, wz0), (xc, wy0, wz1), (xc, wy1, wz1), (xc, wy1, wz0), NOCOL, out=(sgn, 0, 0))

    # =====================================================================================================================
    def details(self):
        dh, ds = self.part('Details_Hull'), self.part('Details_Struct')
        gl = {k: self.part('Glow_' + k) for k in ('Amber', 'Green', 'Cyan', 'Red', 'Warm')}
        roof = self.s['shell']['roof']
        # overhead panel above the pilot's head, tilted towards the seat
        pc, tilt = V((4.0, roof - 0.17, 5.5)), -12
        R = sfgeo.rot_matrix((tilt, 0, 0))
        ex, ey, ez = R @ V((1, 0, 0)), R @ V((0, 1, 0)), R @ V((0, 0, 1))
        dh.obox(pc, ex * 2.5, ey * 0.22, ez * 1.25, DARK, bevel=0.03, segs=2)
        face = pc - ey * 0.111  # the underside, facing down and towards the pilot
        out = -ey
        dh.obox(face - ey * 0.003, ex * 2.38, ey * 0.012, ez * 1.13, DECK)
        rows = (-0.36, -0.12, 0.12, 0.36)
        for r_i, zz in enumerate(rows):
            for i in range(11):
                x = -1.0 + i * 0.2
                c0 = face - ey * 0.02 + ex * x + ez * zz
                if r_i % 2 == 0:
                    ds.obox(c0, ex * 0.045, ey * 0.04, ez * 0.045, PANEL if i % 3 else RED)
                else:
                    k = ('Green', 'Amber', 'Cyan')[(i + r_i) % 3]
                    gl[k].quad(c0 - ex * 0.05 - ez * 0.025, c0 + ex * 0.05 - ez * 0.025, c0 + ex * 0.05 + ez * 0.025, c0 - ex * 0.05 + ez * 0.025, NOCOL, out=out)
        for x in (-1.1, 1.1):
            c0 = face - ey * 0.01 + ex * x
            ds.cyl(c0, c0 - ey * 0.07, 0.07, STEEL, segs=10, r2=0.055)
        dh.obox(pc + ez * 0.64 - ey * 0.0, ex * 2.5, ey * 0.2, ez * 0.03, STEEL, bevel=0.008)  # the front edge plate
        # ceiling: a cable tray down the middle and two warm light strips
        ds.box((4.0, roof - 0.05, 3.2), (0.46, 0.07, 6.2), STEEL, bevel=0.012)
        for z in (0.9, 1.9, 2.9, 3.9, 4.9, 5.9, 6.9):
            ds.box((4.0, roof - 0.085, z), (0.56, 0.03, 0.06), DARK)
        for s in (-0.12, 0.12):
            ds.cyl((4.0 + s, roof - 0.065, 0.7), (4.0 + s, roof - 0.065, 7.5), 0.022, DARK, segs=6)
        for x in (2.5, 5.5):
            gl['Warm'].quad((x - 0.05, roof - 0.004, 0.9), (x + 0.05, roof - 0.004, 0.9), (x + 0.05, roof - 0.004, 7.1), (x - 0.05, roof - 0.004, 7.1), NOCOL, out=(0, -1, 0))
        # conduits along the lower chamfers with clamps (on the slope, so nothing stands in the walking space)
        liner = self.s['shell']['liner']
        for left in (True, False):
            lo, hi = (liner[0], liner[7]) if left else (liner[1], liner[2])
            nx, ny = (0.73, 0.68) if left else (-0.73, 0.68)
            for k, rr in ((0, 0.03), (1, 0.022)):
                t = 0.30 + 0.32 * k
                cxp = lo[0] + (hi[0] - lo[0]) * t + nx * (rr + 0.004)
                cyp = lo[1] + (hi[1] - lo[1]) * t + ny * (rr + 0.004)
                a, bb = V((cxp, cyp, 0.62)), V((cxp, cyp, 7.45))
                ds.cyl(a, bb, rr, STEEL if k == 0 else DARK, segs=8)
                ds.ball(bb, (rr * 1.15,) * 3, STEEL, segs=6, rings=4, smooth=False)
            for z in (1.2, 2.6, 4.0, 5.4, 6.8):
                t = 0.46
                cxp = lo[0] + (hi[0] - lo[0]) * t + nx * 0.03
                cyp = lo[1] + (hi[1] - lo[1]) * t + ny * 0.03
                ds.box((cxp, cyp, z), (0.14, 0.22, 0.05), DARK, rot=(0, 0, -43 if left else 43), bevel=0.008)
        # cable bundles from the console's far face to the windscreen posts
        for s, post_x in ((-1, 1.9), (1, 6.1)):
            pts = [V((4 + s * 0.8, 0.5, 1.99)), V((4 + s * 0.8, 0.13, 1.60)), V((4 + s * 0.8, 0.12, 1.15)), V((post_x - s * 0.02 + s * 0.12, 0.12, 0.62)), V((post_x + s * 0.12, 0.13, 0.30)), V((post_x + s * 0.12, 0.6, 0.27)), V((post_x + s * 0.12, 3.55, 0.27))]
            for off in (-0.022, 0.022):
                p2 = [p + V((off, 0, 0)) for p in pts]
                for a, bb in zip(p2, p2[1:]):
                    ds.cyl(a, bb, 0.018, DARK, segs=6)
                for p in p2[1:-1]:
                    ds.ball(p, (0.02, 0.02, 0.02), DARK, segs=6, rings=4, smooth=False)
            for y in (0.9, 1.9, 2.9):  # clamps on the post
                ds.box((post_x + s * 0.12, y, 0.27), (0.11, 0.05, 0.05), STEEL, bevel=0.008)

    # =====================================================================================================================
    def build(self):
        self.console()
        self.seat()
        self.frames()
        self.details()


def main():
    opts = parse_args()
    with open(opts['shell'], encoding='utf-8') as f:
        shell = json.load(f)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bld = Build(shell)
    bld.build()
    # objects
    root = bpy.data.objects.new('CockpitDetail', None)
    bpy.context.scene.collection.objects.link(root)
    seat = bpy.data.objects.new('Seat', None)
    bpy.context.scene.collection.objects.link(seat)
    seat.parent = root
    order = ['Console_Hull', 'Console_Struct', 'Frames_Struct', 'Details_Hull', 'Details_Struct', 'Seat_Hull', 'Seat_Struct', 'Glass', 'Glow_Amber', 'Glow_Green', 'Glow_Cyan', 'Glow_Red', 'Glow_Warm']
    total = 0
    for name in order:
        part = bld.parts.get(name)
        if part is None:
            continue
        res = part.finish(parent=seat if name.startswith('Seat_') else root)
        if res is None:
            continue
        obj, tris = res
        total += tris
        print(f"PART {name}: {tris} triangles")
    print(f"TOTAL {total} triangles")
    out = opts['out']
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=out, export_format='GLB', export_yup=True, export_apply=True,
        export_materials='NONE', export_vertex_color='ACTIVE', export_active_vertex_color_when_no_material=True,
        export_tangents=True, export_normals=True, export_texcoords=True, export_cameras=False, export_lights=False,
        export_extras=False, use_selection=False)
    print("WROTE", out, os.path.getsize(out), "bytes")
    if opts['preview']:
        import preview
        preview.render(opts['preview'])


if __name__ == '__main__':
    main()
