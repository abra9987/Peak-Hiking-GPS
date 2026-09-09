<div align="center">

# Peak Map Interactive

**The daily PEAK map as actual terrain — not a screenshot of terrain.**

Real geometry, real altitudes, every marker in world space.

</div>

---

## What this is

An unofficial, open-source map for the daily mountain in [PEAK](https://store.steampowered.com/app/3527290/PEAK/).
A BepInEx plugin measures the generated map from inside the game and exports it;
a web client renders it as a navigable 3D surface.

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
plugin/    BepInEx plugin: measures the map, writes a snapshot   (C#)
web/       3D viewer                                             (Three.js + Vite)
tools/     Capture supervision, publishing, format fixture       (PowerShell + Node)
docs/      Data format, architecture, automation
```

## Try it without the game

The viewer runs against a synthetic snapshot, which doubles as the contract test
for [`docs/DATA-FORMAT.md`](docs/DATA-FORMAT.md):

```bash
node tools/make-fixture.mjs
cd web && npm install && npm run dev
```

## Capturing a real map

Requires PEAK, [BepInEx 5](https://github.com/BepInEx/BepInEx) (Mono build), and
the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer.

```bash
# Build and deploy the plugin (it finds PEAK and installs itself)
dotnet build plugin/PeakMapInteractive.csproj -c Release

# Launch, capture, validate, stage for the web client
pwsh -File tools/capture.ps1
```

The plugin drives the game itself: offline mode, solo run, capture, quit. With
`AutoRun` disabled in `BepInEx/config/dev.peakmapinteractive.capture.cfg` it
stays inert and captures only on the hotkey (F9 by default), so it is safe to
leave installed while playing.

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

Working and verified: data format, viewer, terrain and marker rendering,
publishing, format fixture.

Not yet verified against a live game: the plugin has not been compiled or run
here — no .NET SDK was available on the machine it was written on. It is built
against the API surface of a plugin known to work with this game version, but
treat the first run as commissioning, not as a regression test. Enable
`WriteDiagnostics` for that run: it lists every component type the marker
registry did not recognise, which is the fastest way to find what PEAK 2.4.b
calls things.

Known gaps: mushrooms and other run-time food spawn per session rather than per
day and are not capturable this way; The Klin needs checking.

## Legal

Unofficial fan project. Not affiliated with, endorsed or sponsored by Aggro Crab
or Landfall Games. All game assets, trademarks and content remain theirs.

Source code is MIT (see [LICENSE](LICENSE)). The licence covers this code only —
not captured map data, and not anything belonging to the game. No game assets are
redistributed here; marker icons are generated at runtime.
