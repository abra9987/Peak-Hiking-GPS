<div align="center">

# Hiking GPS

**A live minimap for PEAK, on a handheld GPS you carry.**

Real terrain, drawn by the game itself. Markers for the things worth walking
towards. And height, which is the axis a flat map throws away.

</div>

---

## The mod

An unofficial, open-source BepInEx mod for [PEAK](https://store.steampowered.com/app/3527290/PEAK/).
Press `M` and the mountain is there: a camera looking down at the world you are
standing in, with markers for unopened chests, campfires, statues, belltowers,
capybaras, the scoutmaster and everyone else on the climb. A marker grows and
lightens above you and shrinks and darkens below, because thirty metres sideways
and thirty metres up are nothing alike on this mountain.

**Client side.** Only you install it. Nobody else in the lobby needs it, the host
does not need it, and it changes nothing anyone else sees. Harmony patches are
installed only if you switch the development automation on, so an ordinary
install does not patch the game at all.

**No game assets ship with it.** The GPS itself is drawn for this mod. Marker
icons are photographed from your own copy of the game at runtime, the first time
you see each kind of thing — so the pictures always match the version you have,
and none of PEAK's artwork travels.

Controls, settings and installation are in
[`packaging/README.md`](packaging/README.md), the readme the mod itself ships
with. `pwsh tools/package.ps1` builds the Thunderstore and Nexus archives into
`dist/`.

## The other half: the exporter and the web viewer

This began as a web map, and that half still works and still lives here: a
plugin that measures the daily mountain and exports it, and a browser client
that renders the export as navigable 3D terrain. The mod is the active one —
when the question is "where do I go next", a live view rendered by the game
beats anything exported — but none of the measuring below was wasted, and the
export is the only route to a map you can study without the game running.

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

## Layout

```
plugin/    BepInEx plugin: the live GPS, and the snapshot exporter  (C#)
web/       3D viewer for exported snapshots                        (Three.js + Vite)
tools/     Clone setup, capture supervision, packaging, publishing (PowerShell + Node)
packaging/ What ships to Thunderstore and Nexus: manifest, readme, licence
docs/      Data format, architecture, automation, handoff notes
```

## Try it without the game

The viewer runs against a synthetic snapshot, which doubles as the contract test
for [`docs/DATA-FORMAT.md`](docs/DATA-FORMAT.md):

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
[`docs/AUTOMATION.md`](docs/AUTOMATION.md).

## Publishing

```bash
pwsh -File tools/publish.ps1
```

Builds the site and force-pushes it as a single-commit branch. Map data never
enters git history: a daily binary snapshot has no meaningful diff and nobody
ever wants an old one back, so committing it only inflates the clone. See
[`docs/AUTOMATION.md`](docs/AUTOMATION.md).

## Status

Working end to end against the live game. Verified on PEAK 2.4.b: the plugin
builds, drives the game unattended, and captures the real daily map -- terrain
geometry, altitudes and markers -- which the viewer renders in 3D.

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

## Legal

Unofficial fan project. Not affiliated with, endorsed or sponsored by Aggro Crab
or Landfall Games. All game assets, trademarks and content remain theirs.

Source code is MIT (see [LICENSE](LICENSE)). The licence covers this code only —
not captured map data, and not anything belonging to the game. No game assets are
redistributed here; marker icons are generated at runtime.

The GPS artwork in `plugin/assets` is the one exception on our side: it was drawn
for this mod and is reserved, so that the mod keeps its own face. Fork the code
freely and draw your own device. If you want to use this one anyway, ask — the
answer is likely yes.
