"""Renders the device from several angles on a transparent background.

For making an icon by hand. Run through Blender in the background:

    blender -b <peak_tracker.blend> -P tools/render-icon-views.py -- <out dir> [size] [screen.png] [set]

The third argument is a picture to light the screen with; the fourth is
which set of views to render: "survey" (the default, eight plain angles)
or "candidates" (five framings for choosing an icon from).

Every mesh is rendered except the collision box; the film is transparent,
the light is a soft key from the upper left with a fill, and each view is
framed to the model's bounds so the device fills the picture.
"""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = Path(argv[0] if argv else "icon-views")
size = int(argv[1]) if len(argv) > 1 else 1024
screen_image = Path(argv[2]) if len(argv) > 2 and argv[2] not in ("", "-") else None
view_set = argv[3] if len(argv) > 3 else "survey"
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

# --- the screen, switched on ---------------------------------------------

# The screen is one flat swatch of the palette, so its UVs are a dot. For a
# picture on it the mesh gets a UV layer projected from its own extents —
# it is a flat rectangle in its local XY — and an emissive material, which
# is what a backlit display is.
if screen_image is not None and screen_image.exists():
    screen = next((o for o in meshes if "screen" in o.name.lower()), None)
    if screen is None:
        print("no mesh with 'screen' in its name; the screen stays dark")
    else:
        mesh = screen.data
        xs = [v.co.x for v in mesh.vertices]
        ys = [v.co.y for v in mesh.vertices]
        zs = [v.co.z for v in mesh.vertices]
        spans = [max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)]
        # The two long axes of the rectangle carry the picture.
        flat = spans.index(min(spans))
        axes = [i for i in range(3) if i != flat]
        lows = [min(xs), min(ys), min(zs)]
        uv = mesh.uv_layers.new(name="Screen")
        mesh.uv_layers.active = uv
        for poly in mesh.polygons:
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                u = (co[axes[0]] - lows[axes[0]]) / spans[axes[0]]
                v = (co[axes[1]] - lows[axes[1]]) / spans[axes[1]]
                uv.data[li].uv = (u, v)

        mat = bpy.data.materials.new("ScreenLit")
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        nodes.clear()
        out = nodes.new("ShaderNodeOutputMaterial")
        emit = nodes.new("ShaderNodeEmission")
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(str(screen_image.resolve()))
        uvn = nodes.new("ShaderNodeUVMap")
        uvn.uv_map = "Screen"
        links.new(uvn.outputs[0], tex.inputs[0])
        links.new(tex.outputs[0], emit.inputs[0])
        emit.inputs[1].default_value = 1.0
        links.new(emit.outputs[0], out.inputs[0])
        screen.data.materials.clear()
        screen.data.materials.append(mat)
        print(f"screen lit with {screen_image.name}, picture axes {axes}")

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
SURVEY = [
    # name,            azimuth (deg, 0 = -Y side), elevation (deg), orthographic, roll (deg)
    ("front",          0,    0,  True,  0),
    ("front-3q-left", -35,  22,  False, 0),
    ("front-3q-right", 35,  22,  False, 0),
    ("front-high",     0,   45,  False, 0),
    ("side",           90,   0,  True,  0),
    ("back",         180,    0,  True,  0),
    ("back-3q",      145,  22,  False, 0),
    ("top",            0,   89,  True,  0),
]

# Five framings an icon could be cut from, each a different idea:
# a flat product shot, two classic three-quarters, a steep look down at
# it as if held, and a tilted one with some movement in it.
CANDIDATES = [
    ("1-flat",        0,   8,  True,   0),
    ("2-3q-left",   -30,  20,  False,  0),
    ("3-3q-right",   30,  28,  False,  0),
    ("4-held",       -8,  48,  False,  0),
    ("5-tilted",    -25,  30,  False, 14),
]

VIEWS = CANDIDATES if view_set == "candidates" else SURVEY

for name, azimuth, elevation, ortho, roll in VIEWS:
    a = math.radians(azimuth)
    e = math.radians(elevation)
    direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    distance = radius * 4.0
    camera.location = centre + direction * distance
    aim = (-direction).to_track_quat("-Z", "Y")
    if roll:
        aim = aim @ Quaternion((0.0, 0.0, 1.0), math.radians(roll))
    camera.rotation_euler = aim.to_euler()

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

    scene.render.filepath = str(out_dir / f"{'icon' if view_set == 'candidates' else 'device'}-{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"wrote {scene.render.filepath}")
