# Where this project stands

Written at the end of the first working session, so the next one starts from
what was learned rather than rediscovering it.

## What this is now

It began as a web map of PEAK's daily mountain and ended as an **in-game
companion mod**. That change came from the goal, not from giving up on the web
version: the map exists so a player can look at it mid-climb and decide where
to go next, and a live view rendered by the game itself does that better than
anything exported.

Both halves are in the repository. The mod is the active one.

```
plugin/   BepInEx plugin: the live minimap, and the snapshot exporter   (C#)
web/      3D viewer for exported snapshots                  (Three.js + Vite)
tools/    Clone setup, capture supervision, publishing       (PowerShell)
```

## Setup on this machine

- Game: `A:\SteamLibrary\steamapps\common\PEAK` — **stock, no mod loader**, for playing.
- Capture clone: `A:\PeakMapCapture\PEAK` — BepInEx 5.4.23.5 + the plugin. Has
  `steam_appid.txt`, without which Steamworks bounces the launch to the Steam copy.
- .NET SDK 8 is user-local: `C:\Users\gumer\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- Build and deploy in one step: `dotnet build plugin/PeakMapInteractive.csproj -c Release`.
- Config: `A:\PeakMapCapture\PEAK\BepInEx\config\dev.peakmapinteractive.capture.cfg`.
- Decompiled game source (ilspycmd) is in the session scratchpad; regenerate with
  `ilspycmd -p -o <dir> -r <Managed> <Managed>\Assembly-CSharp.dll`.

**`AutoRun` and the minimap are mutually exclusive.** Capture automation
dismisses the loading screen mid-spawn and borrows the camera; playing under it
leaves the character half-initialised and under the terrain.

## The minimap, and what is unverified

Toggle `M`, zoom `=` / `-`, tilt `N` (90 / 75 / 45 degrees). North stays up.

Working as of the last run: the view itself, altitude with a smoothed trend
arrow, distance and height difference to the nearest unopened chest, markers
shaded and sized by how far above or below the player they sit, other climbers
marked at any distance, a bordered frame, and the game's own font.

Not yet confirmed by anyone playing:

- **Compass aim.** The icon is a single image with the needle painted in under a
  pirate hat, so the whole thing rotates. `CompassNeedleOffset` (default 45)
  says where that painted needle already points; if the compass aims wide by a
  constant angle, that is the number to change. `-135` if the red end is the
  pointer rather than the white.
- **Tilted views in tight terrain.** The camera steps back 1200 m along its own
  view direction, which may end up inside rock in the Roots gullies or the
  Citadel. If it does, back off by terrain height instead of a fixed distance.
- **Marker height shading** uses a plus or minus 60 m scale, picked by eye.
- **Everything about the baked icons**, below.

Also new and unseen: capybaras, the scoutmaster and mobs are marked now. They
are found by component, the way chests are, rather than by name.

## Things worth not rediscovering

**The daily map is one integer from the server.** `NextLevelService` hands out
a `levelIndex` that advances every 24 h from 2025-06-14 17:00 UTC, and the game
loads `MapBaker.ScenePaths[levelIndex % 21]`. The maps are pre-baked scenes on
disk — **21 of them, the whole rotation**. Snapshots record which one they are.

**Water is a 5000x1000x5000 box at y = -1** named `Misc/Water/Collision` on the
`Water` layer. Every ray that misses the mountain hits it; excluding the layer
is what turned a fake "100% coverage" into real terrain.

**Layers:** 0 Default, 4 Water, 10 Character, 20 Terrain, 21 Map, 22 InvisWall,
29 Vines, 31 Post. `InvisWall` is the segment barrier that otherwise renders as
broad flat sheets of ground.

**Nothing renders while the loading screen is up.** Every capture route came
back black — render texture, URP render request, `Camera.Render`, a purpose-
built camera and the game's own — until `LoadingScreenHandler.KillCurrentLoadingScreen()`
plus a wait. `Camera.Render()` genuinely does nothing under URP, silently.

**Colliders are rarely on the named object.** Both the ground-colour lookup and
the marker classifier failed until they walked up the parent chain. Suspect this
first whenever something "finds nothing".

**Vertex colours on terrain are splat weights, not colour.** Using them directly
paints the mountain lilac. The real colours come from the material:
`_BaseColor`, `_Color1`, `_Color21`, `_Color3`, `_TopColor`, `_Tint`, blended the
way `W/Peak_Rock` blends them. `_TopColor` is the walkable surface — sand on the
shore, snow higher up — which is why it doubles as the ground colour.

**Chests:** the `Luggage` component, with `IsOpen`. Matching by name catches
every loose pickup as well.

## The exporter, and its ceiling

Still present and working, but it cannot reach 1:1 and this is why:

Roughly a quarter of Shore — 1766k triangles across 3081 meshes — cannot be
read at runtime. Unity only honours the GPU readback flag before a mesh is
uploaded, and the terrain shells (`Beach`, `Cliff`, `ground`) arrive already
uploaded. Priming them every frame during loading does not help; they are on the
GPU before the plugin ever sees them.

The data is all on disk: `level11` alone holds 1925 meshes, 89518 mesh filters
and 1152 textures. UnityPy loads those scene files but returns no vertices for
Unity 6000.3, so an offline path needs either AssetRipper or a decoder for the
mesh streams — the vertex layout logic in `MeshReader.cs` already works and
would port.

Whether that is worth doing now is an open question. The companion mod reaches
the goal without it.

## Marker icons: built, not yet seen

Markers are no longer coloured dots. Each one is now a round plate with a
photograph of the thing standing on it — `plugin/src/Minimap/IconBaker.cs`.

Nothing of PEAK's is copied. The photograph is taken on the player's own
machine, from the model already loaded in the scene, because there was no icon
to borrow: `Luggage` derives from `Spawner`, not `Item`, so `UIData.GetIcon()`
— which is how the compass is drawn — has nothing to offer a chest.

How it works, and why each part is the way it is:

- A **render-only copy** of the object is built out of bare meshes and
  materials. Instantiating the object itself would wake a `Luggage` up, add it
  to `ALL_LUGGAGE` and bring a `PhotonView` along, so photographing a chest
  would quietly edit the run.
- The copy is stood up at **y = -9000**, below the water box, on an unused
  layer, and photographed by an orthographic camera that draws only that layer.
- The view is along whichever horizontal axis the object is **thinnest**, so
  the widest silhouette faces the lens. A capybara seen end-on is a brown blob.
- The rig brings **its own point lights**, because the icon is baked once and
  kept: a chest photographed at 3 a.m. under the game's own sun would stay a
  black shape for the rest of the run. Their intensity is computed from the
  distance, since a point light falls off with its square and the distance is
  proportional to the size of the subject.
- `Camera.Render()` still does nothing under URP, so the bake waits for a real
  frame and reads the texture back afterwards.
- The result is trimmed to what was drawn, so every icon carries the same
  visual weight, and mipmapped, because 128 pixels drawn at twenty sparkles.

The plate underneath survived on purpose: it still carries the category colour
and the height shading, which is the one thing a top-down map cannot say and
the thing this map is for. The icon only darkens when it is below the player —
an `Image` tint multiplies, so lightening a photograph merely washes it out.

**None of it has been seen yet.** Set `Debug/DumpIcons = true` and every baked
icon is written to `capture-output/icons/*.png`, which is the only way to judge
one without squinting at it twenty pixels across. Things to check first:

- Whether the shaders write alpha for opaque surfaces. If not there is a
  fallback that keys against the background, and the log says when it fires.
- Whether the two point lights are the right brightness against ambient.
- Whether chests, capybaras and the scoutmaster are recognisable at
  `Minimap/MarkerSizePixels` (26 by default).

Icons that come out unreadable are the case for drawing that one in Blender
instead and embedding the PNG: our own artwork carries no licence problem, and
the marker code does not care where a sprite came from.

## Open threads

- Location names: the internal biome enum does not match what players call
  places (`Swamp` is the fog and the Citadel; `Roots` is the forest). A mapping
  belongs in the web taxonomy, ideally from https://peak.wiki.gg/wiki/Locations.
- Publishing as a Nexus mod: nothing prepared yet. No game assets are copied —
  icons are drawn at runtime and the compass is read from the loaded item — so
  there is nothing to strip before release.
- A full-fidelity Shore export is 102 MB; publishing the web version needs
  decimation, and that only makes sense once the geometry is complete.
