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

## Next task: real icons instead of coloured dots

Markers are currently coloured dots. They should be recognisable pictures of
the thing — a chest that looks like a chest, a capybara that looks like a
capybara — the way the reference project (qWojtpl/PeakMap) does it.

That project ships PNGs extracted from the game, which is exactly what this one
must not do: nothing of PEAK's is copied into the mod, and a Nexus release
depends on keeping it that way.

Two sources, and only the second works for chests:

- **Items** carry their own icon: `Item.UIData.GetIcon()` returns a Texture2D.
  This is how the compass in the corner is drawn, and it works today.
- **Chests do not.** `Luggage` derives from `Spawner`, not `Item`, so there is
  no icon anywhere in the game to borrow.

So chests need an **icon baked from their own model**: place the prefab in
front of a throwaway camera against a transparent background, render once to a
small RenderTexture, keep the sprite, reuse it for every marker of that type.
Bake lazily on first sighting and cache by type name. Animals want the same
treatment.

Worth getting right while building it: a dark rim or drop shadow, or icons will
disappear against sand and snow the way the plain dots did; and the height
shading that currently tints the dot has to survive, since knowing whether a
chest is above or below is the single most useful thing the map says.

## Open threads

- Location names: the internal biome enum does not match what players call
  places (`Swamp` is the fog and the Citadel; `Roots` is the forest). A mapping
  belongs in the web taxonomy, ideally from https://peak.wiki.gg/wiki/Locations.
- Publishing as a Nexus mod: nothing prepared yet. No game assets are copied —
  icons are drawn at runtime and the compass is read from the loaded item — so
  there is nothing to strip before release.
- A full-fidelity Shore export is 102 MB; publishing the web version needs
  decimation, and that only makes sense once the geometry is complete.
