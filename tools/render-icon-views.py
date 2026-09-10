"""Renders the device from several angles on a transparent background.

For making an icon by hand. Run through Blender in the background:

    blender -b <peak_tracker.blend> -P tools/render-icon-views.py -- <out dir> [size]

Every mesh is rendered except the collision box; the film is transparent,
the light is a soft key from the upper left with a fill, and each view is
framed to the model's bounds so the device fills the picture.
"""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = Path(argv[0] if argv else "icon-views")
size = int(argv[1]) if len(argv) > 1 else 1024
out_dir.mkdir(parents=True, exist_ok=True)

scene = bpy.context.scene

# --- what is rendered ------------------------------------------------------

meshes = []
for obj in scene.objects:
    if obj.type != "MESH":
        obj.hide_render = True
        continue
    hidden = "collider" in obj.name.lower() or "collision" in obj.name.lower()
    obj.hide_render = hidden
    obj.hide_viewport = hidden
    if not hidden:
        meshes.append(obj)

# Bounds of everything visible, in world space.
lo = Vector((float("inf"),) * 3)
hi = Vector((float("-inf"),) * 3)
for obj in meshes:
    for corner in obj.bound_box:
        world = obj.matrix_world @ Vector(corner)
        lo = Vector(min(a, b) for a, b in zip(lo, world))
        hi = Vector(max(a, b) for a, b in zip(hi, world))
centre = (lo + hi) / 2
extent = hi - lo
radius = max(extent) * 0.5

print(f"bounds {tuple(round(v, 3) for v in lo)} .. {tuple(round(v, 3) for v in hi)}, radius {radius:.3f}")

# --- render settings -------------------------------------------------------

for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue

scene.render.resolution_x = size
scene.render.resolution_y = size
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.image_settings.color_depth = "8"
if hasattr(scene, "eevee"):
    for attr, value in (("taa_render_samples", 64), ("use_gtao", True), ("use_shadows", True)):
        if hasattr(scene.eevee, attr):
            setattr(scene.eevee, attr, value)
scene.view_settings.view_transform = "Standard"

# --- light -----------------------------------------------------------------

for obj in list(scene.objects):
    if obj.type == "LIGHT":
        bpy.data.objects.remove(obj, do_unlink=True)


def lamp(name, direction, energy, angle):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = angle
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    return obj


lamp("Key", (-0.5, -0.6, -1.0), 3.0, math.radians(15))
lamp("Fill", (0.7, -0.3, -0.6), 1.2, math.radians(40))
lamp("Rim", (0.2, 0.8, -0.3), 1.0, math.radians(30))

world = scene.world or bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.6, 0.6, 0.6, 1.0)
    bg.inputs[1].default_value = 0.6

# --- camera ----------------------------------------------------------------

cam_data = bpy.data.cameras.new("IconCamera")
camera = bpy.data.objects.new("IconCamera", cam_data)
scene.collection.objects.link(camera)
scene.camera = camera

# The model's screen faces +Y in Blender when exported to glTF's -Z... but
# the file's own convention is what matters here: the views below are
# named by where the camera stands relative to the model's axes, and the
# front is whichever side the screen is on. Both front and back are
# rendered, so the naming can be checked against the pictures.
VIEWS = [
    # name,            azimuth (deg, 0 = -Y side), elevation (deg), orthographic
    ("front",          0,    0,  True),
    ("front-3q-left", -35,  22,  False),
    ("front-3q-right", 35,  22,  False),
    ("front-high",     0,   45,  False),
    ("side",           90,   0,  True),
    ("back",         180,    0,  True),
    ("back-3q",      145,  22,  False),
    ("top",            0,   89,  True),
]

for name, azimuth, elevation, ortho in VIEWS:
    a = math.radians(azimuth)
    e = math.radians(elevation)
    direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    distance = radius * 4.0
    camera.location = centre + direction * distance
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()

    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = radius * 2.3
    else:
        cam_data.type = "PERSP"
        cam_data.lens = 50
        cam_data.sensor_width = 36
        # Frame the sphere around the model with a little margin.
        needed = 2.0 * math.atan(radius * 1.15 / distance)
        cam_data.lens = cam_data.sensor_width / (2.0 * math.tan(needed / 2.0))

    cam_data.clip_start = distance * 0.05
    cam_data.clip_end = distance * 4

    scene.render.filepath = str(out_dir / f"device-{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"wrote {scene.render.filepath}")
