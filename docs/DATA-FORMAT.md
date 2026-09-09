# Snapshot format v1

A *snapshot* is one complete capture of a daily PEAK map: terrain geometry,
an orthophoto and every marker, for all segments.

```
snapshot/
  snapshot.json            manifest, all metadata, all markers
  segment_0.height.bin     uint16 LE heightfield
  segment_0.albedo.jpg     orthographic top-down colour
  segment_1.height.bin
  ...
```

## Design rules

1. **World coordinates only.** Markers store `[x, y, z]` in Unity world space.
   Nothing is pre-projected to screen space. The reference project
   (`qWojtpl/PeakMap`) throws away the third dimension inside the plugin via
   `Camera.WorldToViewportPoint`, which is why its web app can never show
   height, angle or distance. We keep the full vector.

2. **Terrain and markers share one coordinate frame.** No registration step,
   no fudge factors. A marker at world `y = 812` sits on terrain that is
   `812` high at that `xz`, by construction.

3. **Orthographic, never perspective.** An orthographic top-down render maps
   world XZ to pixels linearly and exactly:
   `u = (x - origin.x) / size.x`, `v = (z - origin.z) / size.z`.
   Perspective capture needs per-level camera constants tuned by eye — the
   reference project ends up dividing by `7630` in CSS against a `7680` px
   render, a permanent horizontal skew.

4. **No binary in git history.** See `docs/AUTOMATION.md`.

## snapshot.json

```jsonc
{
  "schemaVersion": 1,
  "generatedAt": "2026-09-09T17:04:11Z",  // UTC, ISO 8601
  "gameVersion": "2.4.b",
  "pluginVersion": "0.1.0",
  "capture": {
    "heightResolution": 1024,     // samples per axis
    "albedoResolution": 4096,     // px per axis
    "raycastLayerMask": -1
  },
  "segments": [ /* Segment[] */ ]
}
```

### Segment

```jsonc
{
  "index": 0,
  "biome": "Shore",
  "displayName": "Shore",

  "bounds": {                     // world-space AABB of everything captured
    "min": [-512.0, -30.5, -480.0],
    "max": [ 512.0, 611.2,  520.0]
  },

  "terrain": {
    "file": "segment_0.height.bin",
    "width": 1024,                // samples along X
    "depth": 1024,                // samples along Z
    "origin": [-512.0, -480.0],   // world XZ of sample (0,0)
    "size": [1024.0, 1000.0],     // world extent covered
    "heightMin": -30.5,           // maps to stored value 1
    "heightMax": 611.2,           // maps to stored value 65535
    "coverage": 0.973             // fraction of samples that hit geometry
  },

  "albedo": {
    "file": "segment_0.albedo.jpg",
    "resolution": 4096,
    "origin": [-512.0, -480.0],   // identical framing to terrain
    "size": [1024.0, 1000.0]
  },

  "markers": [ /* Marker[] */ ]
}
```

### Heightfield encoding — `segment_N.height.bin`

Raw, header-less, `width * depth` samples, **row-major, uint16 little-endian**.
Row `z` runs along increasing world Z; within a row, column `x` runs along
increasing world X.

| stored value | meaning |
|---|---|
| `0` | **no data** — the downward ray hit nothing |
| `1 … 65535` | height, linearly mapped |

Decode:

```js
height = heightMin + ((v - 1) / 65534) * (heightMax - heightMin)
```

Reserving `0` for no-data costs one quantisation step and removes the need for
a separate coverage mask. At a typical 640 m vertical range the resolution is
**~1 cm** — far below anything observable in game.

`uint16` rather than `float32` halves transfer size (2 MB vs 4 MB per segment
at 1024²) for precision nobody can perceive. `.bin` rather than 16-bit PNG
because browsers silently downsample 16-bit PNG to 8 bits when decoded through
`<img>`/canvas — that would quantise 640 m of mountain into 2.5 m steps.

### Marker

```jsonc
{
  "id": "0:luggage:3f2a91",     // stable within a snapshot
  "kind": "luggage",            // group, drives UI layer toggles
  "type": "LuggageBig",         // exact game identifier
  "name": "BIG LUGGAGE",        // localised display name, may be null
  "pos": [312.4, 208.7, -95.1]  // Unity world space
}
```

`kind` is one of: `luggage`, `belltower`, `animal`, `amulet`, `tomb`,
`statue`, `campfire`, `misc`.

`type` is passed through verbatim from the game so that new content shows up
as an unknown type rather than vanishing. The web app renders unknown types
with a neutral icon instead of dropping them — the reference project silently
skips anything without a matching PNG.

## Coordinate conversion for the web client

Unity is left-handed (Y up, Z forward). Three.js is right-handed (Y up,
Z toward viewer). Conversion happens in exactly one place, `web/src/loader.js`:

```
three.x =  unity.x
three.y =  unity.y
three.z = -unity.z
```

Applied identically to terrain vertices and markers, so they stay consistent.
