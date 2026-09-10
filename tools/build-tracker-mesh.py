#!/usr/bin/env python3
"""
Turns the tracker's glTF binary into the blob the plugin carries inside itself.

The device is a real 3D model now, and a mod cannot ask a player to install
Unity to see it. The documented route for a custom item - author a prefab in
the editor, bake an AssetBundle - needs the exact editor the game was built
with, and bundles carrying shaders break when that version drifts. So the mesh
travels the same way the navigator artwork already does: built into the
assembly as a resource, and rebuilt into a `Mesh` at runtime.

What this script exists to do is get the awkward part wrong here, offline,
where it can be printed and looked at, rather than in a game that renders a
black silhouette and says nothing about why.

Two conversions happen, and both are the kind that produce a plausible-looking
failure:

  Handedness. glTF is right-handed with -Z forward; Unity is left-handed with
  +Z forward. The mapping used is (x, y, -z), which flips handedness in one
  step and lands the screen's outward normal on Unity's +Z - so the device's
  own forward is the face you read, and pressing a button is -Z. Because that
  mapping is a reflection, triangle winding is reversed too, or every face
  would end up pointing into the case.

  Texture origin. glTF's V runs down from the top; Unity's runs up from the
  bottom. So V is flipped. Get this wrong and the palette still maps to
  something - just the wrong swatch - which is exactly the sort of mistake that
  survives all the way to a screenshot.

Usage:
    python tools/build-tracker-mesh.py [source.glb] [-o out.mesh]

Defaults to plugin/model/peak_tracker.glb -> plugin/assets/tracker.mesh.
"""

import argparse
import json
import math
import struct
import sys
from pathlib import Path

MAGIC = b"PKTRACK\x01"

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942

COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
COMPONENT_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Gltf:
    """Just enough glTF to read a small static model out of a .glb."""

    def __init__(self, path: Path):
        data = path.read_bytes()
        magic, _version, length = struct.unpack_from("<III", data, 0)
        if magic != GLB_MAGIC:
            raise SystemExit(f"{path} is not a .glb (bad magic)")

        chunks = {}
        offset = 12
        while offset < length:
            chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
            offset += 8
            chunks[chunk_type] = data[offset : offset + chunk_length]
            offset += chunk_length

        self.json = json.loads(chunks[CHUNK_JSON].decode("utf-8"))
        self.bin = chunks.get(CHUNK_BIN, b"")

    def accessor(self, index):
        acc = self.json["accessors"][index]
        if "sparse" in acc:
            raise SystemExit("sparse accessors are not supported; re-export without them")

        view = self.json["bufferViews"][acc["bufferView"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        count = COMPONENT_COUNT[acc["type"]]
        fmt = COMPONENT[acc["componentType"]]
        stride = view.get("byteStride") or count * struct.calcsize(fmt)

        return [
            struct.unpack_from("<" + fmt * count, self.bin, start + i * stride)
            for i in range(acc["count"])
        ]

    def view_bytes(self, index):
        view = self.json["bufferViews"][index]
        start = view.get("byteOffset", 0)
        return self.bin[start : start + view["byteLength"]]


def node_translation(node):
    if "matrix" in node:
        m = node["matrix"]           # column-major
        return (m[12], m[13], m[14])
    return tuple(node.get("translation", (0.0, 0.0, 0.0)))


def collect(gltf):
    """Every node that carries a mesh, in scene order, with its own offset."""
    nodes = gltf.json["nodes"]
    scene = gltf.json["scenes"][gltf.json.get("scene", 0)]

    found = []

    def walk(index, parent_offset):
        node = nodes[index]
        tx, ty, tz = node_translation(node)
        offset = (parent_offset[0] + tx, parent_offset[1] + ty, parent_offset[2] + tz)

        if "rotation" in node or "scale" in node:
            rot = node.get("rotation")
            scale = node.get("scale")
            if rot and rot != [0, 0, 0, 1]:
                raise SystemExit(
                    f"node '{node.get('name')}' carries a rotation; "
                    "apply transforms before exporting"
                )
            if scale and scale != [1, 1, 1]:
                raise SystemExit(
                    f"node '{node.get('name')}' carries a scale; "
                    "apply transforms before exporting"
                )

        if "mesh" in node:
            found.append((node.get("name") or f"node{index}", node["mesh"], offset))

        for child in node.get("children", []):
            walk(child, offset)

    for root in scene["nodes"]:
        walk(root, (0.0, 0.0, 0.0))

    return found


def convert(gltf, mesh_index):
    """One glTF mesh, in Unity's coordinates and winding."""
    mesh = gltf.json["meshes"][mesh_index]
    if len(mesh["primitives"]) != 1:
        raise SystemExit(
            f"mesh '{mesh.get('name')}' has {len(mesh['primitives'])} primitives; "
            "one material slot per mesh is expected"
        )

    prim = mesh["primitives"][0]
    if prim.get("mode", 4) != 4:
        raise SystemExit(f"mesh '{mesh.get('name')}' is not triangles")

    attrs = prim["attributes"]
    positions = gltf.accessor(attrs["POSITION"])
    normals = (
        gltf.accessor(attrs["NORMAL"]) if "NORMAL" in attrs else [(0.0, 0.0, 1.0)] * len(positions)
    )
    uvs = (
        gltf.accessor(attrs["TEXCOORD_0"])
        if "TEXCOORD_0" in attrs
        else [(0.0, 0.0)] * len(positions)
    )
    indices = [i[0] for i in gltf.accessor(prim["indices"])]

    # (x, y, -z): right-handed to left-handed in one step, and the screen's
    # outward normal lands on Unity's +Z.
    positions = [(p[0], p[1], -p[2]) for p in positions]
    normals = [(n[0], n[1], -n[2]) for n in normals]

    # glTF's V runs down from the top of the image, Unity's runs up from the bottom.
    uvs = [(u[0], 1.0 - u[1]) for u in uvs]

    # That mapping is a reflection, so every triangle now faces inward.
    flipped = []
    for t in range(0, len(indices), 3):
        a, b, c = indices[t], indices[t + 1], indices[t + 2]
        flipped += [a, c, b]

    material = None
    if prim.get("material") is not None:
        material = gltf.json["materials"][prim["material"]].get("name")

    return positions, normals, uvs, flipped, material


def write_string(out, text):
    raw = text.encode("utf-8")
    out += struct.pack("<I", len(raw))
    out += raw


def build(gltf, nodes, extra):
    out = bytearray()
    out += MAGIC
    out += struct.pack("<I", len(nodes))

    report = []

    for name, mesh_index, offset in nodes:
        positions, normals, uvs, indices, material = convert(gltf, mesh_index)

        if len(positions) > 65535:
            raise SystemExit(
                f"'{name}' has {len(positions)} vertices; the blob stores 16-bit indices"
            )

        # The node's own offset, through the same handedness flip.
        ox, oy, oz = offset[0], offset[1], -offset[2]

        write_string(out, name)
        write_string(out, material or "")
        out += struct.pack("<fff", ox, oy, oz)
        out += struct.pack("<II", len(positions), len(indices))

        for p in positions:
            out += struct.pack("<fff", *p)
        for n in normals:
            out += struct.pack("<fff", *n)
        for uv in uvs:
            out += struct.pack("<ff", *uv)
        for i in indices:
            out += struct.pack("<H", i)

        report.append((name, material, positions, normals, indices, (ox, oy, oz)))

    textures = []

    for image in gltf.json.get("images", []):
        if "bufferView" not in image:
            raise SystemExit(
                f"image '{image.get('name')}' is not embedded; export the .glb with textures packed"
            )
        textures.append((image.get("name") or "texture", gltf.view_bytes(image["bufferView"])))

    # Textures the model carries but glTF cannot express. The material mask is
    # metallic in R and smoothness in A, which is what Unity's lit shaders read
    # and what glTF has no slot for - it packs occlusion, roughness and metal
    # into RGB instead, so the .glb holds a converted copy and the real one
    # travels beside it.
    for path in extra:
        if not path.exists():
            raise SystemExit(f"no such texture: {path}")
        textures.append((path.stem, path.read_bytes()))

    out += struct.pack("<I", len(textures))
    for name, blob in textures:
        write_string(out, name)
        out += struct.pack("<I", len(blob))
        out += blob

    return bytes(out), report, textures


def describe(report, textures):
    """Print what came out, so a bad conversion is caught here and not in-game."""
    print(f"{'node':<20} {'material':<24} {'verts':>6} {'tris':>6}  bounds (Unity, metres)")
    print("-" * 100)

    total_tris = 0
    for name, material, positions, normals, indices, offset in report:
        tris = len(indices) // 3
        total_tris += tris
        xs = [p[0] + offset[0] for p in positions]
        ys = [p[1] + offset[1] for p in positions]
        zs = [p[2] + offset[2] for p in positions]
        print(
            f"{name:<20} {material or '-':<24} {len(positions):>6} {tris:>6}  "
            f"x {min(xs):+.5f}..{max(xs):+.5f}  "
            f"y {min(ys):+.5f}..{max(ys):+.5f}  "
            f"z {min(zs):+.5f}..{max(zs):+.5f}"
        )

    print("-" * 100)
    print(f"{'':<20} {'':<24} {'':>6} {total_tris:>6}")

    # The one conversion that fails silently: if winding and normals disagree,
    # the model renders inside out and nothing says so.
    print()
    for name, _material, positions, normals, indices, _offset in report:
        agree = disagree = degenerate = 0
        for t in range(0, len(indices), 3):
            a, b, c = indices[t], indices[t + 1], indices[t + 2]
            pa, pb, pc = positions[a], positions[b], positions[c]
            e1 = [pb[i] - pa[i] for i in range(3)]
            e2 = [pc[i] - pa[i] for i in range(3)]
            # Unity's Vector3.Cross - and so Mesh.RecalculateNormals - uses the
            # ordinary cross product formula, left-handed coordinates or not.
            # It is the winding that carries the handedness, not the formula.
            geo = [
                e1[1] * e2[2] - e1[2] * e2[1],
                e1[2] * e2[0] - e1[0] * e2[2],
                e1[0] * e2[1] - e1[1] * e2[0],
            ]
            n = normals[a]
            if math.sqrt(sum(g * g for g in geo)) < 1e-12:
                degenerate += 1
                continue
            if sum(geo[i] * n[i] for i in range(3)) > 0:
                agree += 1
            else:
                disagree += 1

        verdict = "ok" if disagree <= agree * 0.02 else "INSIDE OUT"
        note = f"  degenerate {degenerate}" if degenerate else ""
        print(
            f"  winding vs normals  {name:<20} agree {agree:>5}  "
            f"disagree {disagree:>5}   {verdict}{note}"
        )

    if textures:
        print()
        for name, blob in textures:
            print(f"  texture  {name}  ({len(blob):,} bytes)")


def main():
    root = Path(__file__).resolve().parent.parent

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "source", nargs="?", default=root / "plugin" / "model" / "peak_tracker.glb", type=Path
    )
    parser.add_argument(
        "-o", "--out", default=root / "plugin" / "assets" / "tracker.mesh", type=Path
    )
    parser.add_argument(
        "-t",
        "--texture",
        action="append",
        default=None,
        type=Path,
        help="an extra PNG to pack, named after its file stem; repeatable",
    )
    args = parser.parse_args()

    if not args.source.exists():
        raise SystemExit(f"no such file: {args.source}")

    gltf = Gltf(args.source)
    nodes = collect(gltf)
    if not nodes:
        raise SystemExit("the scene holds no meshes")

    extra = args.texture
    if extra is None:
        # The mask sits beside the model and is picked up without being asked for.
        beside = args.source.parent / "tracker_body_mask.png"
        extra = [beside] if beside.exists() else []

    blob, report, textures = build(gltf, nodes, extra)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes(blob)

    print(f"{args.source}")
    print(f"  -> {args.out}  ({len(blob):,} bytes)")
    print()
    describe(report, textures)


if __name__ == "__main__":
    main()
