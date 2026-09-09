# Architecture

```
  PEAK (Unity 6000.3.15f1, Mono, URP)
        │
        │  BepInEx 5 + Harmony
        ▼
  plugin/                            C#, netstandard2.1
        │  ├── Automation   drives menu → offline → solo run, no input
        │  ├── Capture      bounds → frame → heightfield + orthophoto
        │  ├── Collect      scene scan → markers in world space
        │  └── Export       snapshot.json + *.height.bin + *.albedo.jpg
        ▼
  capture-output/snapshot/           the snapshot (docs/DATA-FORMAT.md)
        │
        │  tools/capture.ps1 — supervise, validate, stage
        ▼
  web/public/data/
        │
        │  tools/publish.ps1 — build, force-push single-commit branch
        ▼
  GitHub Pages
        │
        ▼
  web/                               Three.js + Vite
           loader   fetch + decode, Unity → Three.js coordinates
           terrain  heightfield → BufferGeometry
           markers  world positions → billboards, icons drawn at runtime
           taxonomy raw game types → labels, colours, grouping
```

## The decision everything else follows from

The plugin exports **world-space coordinates and a heightfield**, not a
screenshot with dots projected onto it.

Projection is lossy and irreversible. Once `Camera.WorldToViewportPoint` has run,
altitude is gone and no amount of web-side cleverness gets it back — which is why
flat PEAK maps cannot show height, distance, or slope even in principle. Keeping
the third dimension in the export costs a few hundred kilobytes and is the entire
difference in what the client can do.

## Why raycasting rather than reading Unity Terrain

PEAK's mountains mix terrain, meshes and props. A downward ray hits whatever the
player would stand on, which is the surface people care about, and the same code
keeps working if the game changes how a biome is built. `RaycastCommand` batches
the grid across worker threads; a 1024² grid is over a million queries and takes
well under a second that way, against tens of seconds issued one at a time.

## Why orthographic capture

An orthographic top-down render maps world XZ to pixels linearly and exactly:

```
u = (x - origin.x) / size.x
v = (z - origin.z) / size.z
```

No calibration, no per-biome constants, no drift at the edges of frame. A
perspective capture has parallax that varies across the image, which forces a
hand-tuned camera per level and still leaves error — the reference project ends
up dividing by `7630` in CSS against a `7680`-pixel render to compensate.

The capture frame is squared before use. That wastes a little margin on the
narrow axis and means the heightfield and orthophoto share one resolution and one
aspect, so nothing downstream ever reasons about non-uniform scaling.

## Why scanning rather than patching each item type

Patching `Awake` on every known item class means anything unknown is silently
dropped, and each new item type needs a new patch class. `MarkerCollector` scans
a segment's MonoBehaviours once and classifies them against a name-based
registry — resolved at runtime, so a renamed or removed game type degrades a
marker instead of failing the build.

Unrecognised types are counted and reported rather than discarded, so a game
update shows up as a diagnostics entry instead of a quietly emptier map.

## Where presentation decisions live

The plugin exports what the game calls things, verbatim. Anything interpretive —
that `LuggageCaldera` and `LuggageGloom` are one chest with different loot
tables, what colour a belltower is, what to call an amulet — lives in
`web/src/taxonomy.js`.

The split matters because the interpretation can be wrong. If biome luggage turns
out to differ in some way that should be visible, that is a one-file change with
the underlying data already captured. Collapsing the types at capture time would
have destroyed the evidence.

## Coordinate systems

Unity is left-handed, Three.js is right-handed. Conversion happens in exactly one
place, `web/src/loader.js`:

```
three.x = unity.x    three.y = unity.y    three.z = -unity.z
```

Applied identically to terrain vertices and markers. A marker cannot drift off
the ground it sits on when both went through the same transform.
