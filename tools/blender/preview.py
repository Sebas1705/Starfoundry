"""Workbench previews of the generated scene (flat vertex colours, no game materials): a quick look at the geometry from a few cameras.

Used by cockpit.py --preview <dir>. The real judgement is made in the game (see docs/prototypes/blender-pipeline.md).
"""
import math
import os

import bpy
from mathutils import Vector

import sfgeo

VIEWS = {
    'eye': ((4.0, 2.7, 6.6), (4.0, 1.0, 2.4), 80),
    'seat': ((6.6, 1.9, 6.8), (4.0, 0.9, 4.5), 55),
    'console': ((4.0, 2.0, 4.4), (4.0, 0.9, 2.5), 55),
    'side': ((7.6, 2.4, 1.6), (3.0, 1.6, 4.2), 70),
    'top': ((4.0, 7.5, 3.6), (4.0, 0.0, 3.6), 52),
}


def render(out_dir, size=(960, 540)):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.film_transparent = False
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'VERTEX'
    sh.show_cavity = False
    sh.background_type = 'WORLD'
    if sc.world is None:
        sc.world = bpy.data.worlds.new('w')
    sc.world.color = (0.02, 0.025, 0.04)
    cam_data = bpy.data.cameras.new('cam')
    cam = bpy.data.objects.new('cam', cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    for name, (pos, tgt, fov) in VIEWS.items():
        cam_data.angle = math.radians(fov)
        cam.location = sfgeo.b(pos)
        d = sfgeo.b(tgt) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out_dir, name + '.png')
        bpy.ops.render.render(write_still=True)
        print('PREVIEW', sc.render.filepath)
