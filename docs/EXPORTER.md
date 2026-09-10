# The exporter and the web viewer

The other half of this repository, and where it started.

Before the mod existed, the plan was a web map: a plugin that measures the daily
mountain from inside the game and exports it, and a browser client that renders
the export as navigable 3D terrain. That half still works and still lives here.

The mod is the active one — when the question is "where do I go next", a live
view rendered by the game beats anything exported. But none of the measuring
below was wasted, and the export is the only route to a map you can study
without the game running.

Existing PEAK maps flatten the mountain to a photograph and paint dots on top.
That is a reasonable thing to build, and it throws away the one axis the game is
actually about. PEAK is a climbing game. A chest 200 m above you and a chest
200 m along the ridge look identical on a flat image, and they are not remotely
the same problem.

## What it does differently

| | Flat screenshot maps | This project |
|---|---|---|
| Terrain | One 7680×4320 JPEG per biome | Sampled heightfield, rendered as geometry |
| Marker positions | Projected to 2D during capture, depth discarded | World-space `[x, y, z]`, kept |
| Altitude | Not representable | Shown per marker, in metres |
| Camera | Two fixed angles, baked at capture time | Free orbit, any angle |
| Projection | Perspective, hand-tuned per biome | Orthographic, exact by construction |
| Payload | ~38 MB of JPEG | ~2 MB per biome, streamed per biome |
| Unknown item types | Silently dropped | Rendered, labelled with the raw game type |
| Repository growth | Binary snapshots committed daily | Snapshots never enter git history |
| Icons | Extracted game artwork | Drawn at runtime, no game assets shipped |

The projection row is the one that makes the rest possible. Capturing
orthographically means world XZ maps to texture UV by a linear transform that is
exact everywhere, so terrain and markers share one coordinate frame and cannot
drift apart. Everything else follows from not throwing that away.

## Try it without the game

The viewer runs against a synthetic snapshot, which doubles as the contract test
for [`DATA-FORMAT.md`](DATA-FORMAT.md):

```bash
node tools/make-fixture.mjs
cd web && npm install && npm run dev
```

## Capturing a real map

Requires PEAK and the [.NET SDK](https://dotnet.microsoft.com/download) 8 or
newer. BepInEx is installed for you, into a copy of the game.

```bash
# One-off: clone the game (~5 GB), add BepInEx, write a config
pwsh -File tools/setup-clone.ps1

# Build the plugin; it installs itself into the clone
dotnet build plugin/PeakMapInteractive.csproj -c Release

# Capture, validate, stage for the web client
pwsh -File tools/capture.ps1
```

**Your Steam install is never touched.** Captures run against a private clone,
so the copy you play stays completely stock: no mod loader, nothing that could
matter when playing with other people, and no chance of a scheduled capture
starting mid-session. The clone carries a `steam_appid.txt` so it runs directly
instead of bouncing the launch back to the Steam copy.

The capture is meant to be unobtrusive: the clone starts muted, in a small
window moved off-screen, plays itself through to a loaded run, measures every
segment and quits. It cannot be truly headless — reading pixels back needs a
real GPU surface, and `-batchmode -nographics` would leave nothing to read.

Automating the daily reset — the map turns over at **17:00 UTC** — is covered in
[`AUTOMATION.md`](AUTOMATION.md).

## Publishing the viewer

```bash
pwsh -File tools/publish.ps1
```

Builds the site and force-pushes it as a single-commit branch. Map data never
enters git history: a daily binary snapshot has no meaningful diff and nobody
ever wants an old one back, so committing it only inflates the clone. See
[`AUTOMATION.md`](AUTOMATION.md).

## Status

Working end to end against the live game. Verified on PEAK 2.4.b: the plugin
builds, drives the game unattended, and captures the real daily map — terrain
geometry, altitudes and markers — which the viewer renders in 3D.

**How the daily map actually works.** The server hands out a `levelIndex` that
advances once every 24 hours from 2025-06-14 17:00 UTC, and the game loads
`ScenePaths[levelIndex % 21]`. The maps are pre-baked scenes shipped inside the
game: the server contributes one integer, nothing more. There are **21 maps in
the whole rotation**, and every snapshot records which one it is
(`map.levelIndex`, `map.sceneName`, `map.poolIndex`), so a capture can be
verified rather than trusted.

That has a consequence worth acting on: the rotation is finite, so it can be
captured once instead of scraped daily. See "Capture the whole rotation" below.

### Known gaps

- **Orthophoto not captured.** The top-down colour pass renders nothing under
  the game's URP setup. Diagnosed as far as: the camera is enabled, base type,
  has a `UniversalRenderer`, and `SubmitRenderRequest` is accepted without
  error, yet the target stays untouched at any resolution, window state or
  render path. Snapshots omit `albedo` rather than shipping a black image, and
  the viewer shades terrain from the heightfield instead.
- **Barrier surfaces sampled as terrain.** Large flat colliders that are not
  ground still appear in some segments. The viewer drops implausibly steep
  cells, which removes tree and prop curtains, but a broad gently-sloped
  barrier passes that test.
- **Mushrooms and run-time food** spawn per session rather than per day and are
  not capturable this way.

### Capture the whole rotation

Because the pool is 21 fixed scenes, the end state is an atlas rather than a
daily scraper: capture every map once, publish all of them, and let the client
compute `levelIndex % 21` in the browser. No scheduled game launches, and the
map for any future date becomes a lookup rather than a wait.
