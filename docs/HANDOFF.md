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
leaves the character half-initialised and under the terrain. The unattended icon
run is the one exception, and it forces the map visible on purpose: it only
needs a loaded level to photograph, not a working character.

## The minimap

Toggle `M`, zoom `=` / `-`, tilt `N` (90 / 75 / 45 degrees). North stays up.
`F10` photographs every kind of thing on the whole mountain at once.

The map opens only once the run has actually started, and waits for the later
of two things: the game's own idea of having begun — not passed out on the
beach, not warping, standing on something, which is what
`Character.TestSpawnChallengeItems` waits for — and ten seconds from the
mountain finishing loading. `isGrounded` alone goes true the instant a body
touches sand, long before the character has got up.

Zoom is a fixed ladder: 20, 29, 41, 58, 83, 119, 170, 243, 347, 496, 708, 1012,
1446, 2000 metres across. It opens on the third rung and returns there each
time it opens. It used to multiply whatever the span happened to be, so there
was no such thing as a step. The marker scan does not shrink with the view — it
reaches 250 m at any zoom, because the compass and the readout name the nearest
chest and want to see past the edge of the picture.

Still not confirmed by anyone playing:

- **Compass aim.** The icon is a single image with the needle painted in under a
  pirate hat, so the whole thing rotates. `CompassNeedleOffset` (default 45)
  says where that painted needle already points. `-135` if the red end is the
  pointer rather than the white. It also sits half outside the panel's left
  edge, which the screenshots show and nobody has needed fixed, because the
  compass is being replaced by a drawn navigator.
- **Tilted views in tight terrain.** The camera steps back 1200 m along its own
  view direction, which may end up inside rock in the Roots gullies or the
  Citadel. If it does, back off by terrain height instead of a fixed distance.
- **Marker height shading** uses a plus or minus 60 m scale, picked by eye.
- **Which rung to open on.** Three is 41 metres, and at that scale the crashed
  plane fills the window and the chests are visible in the world anyway, so the
  markers nearly duplicate what is already drawn. They start earning their place
  around the fifth or sixth rung.

## Marker icons

Markers are a round plate with a photograph of the thing standing on it —
`plugin/src/Minimap/IconBaker.cs`. Chests, capybaras, the scoutmaster, mobs,
statues, bells, belltowers and campfires all come out recognisable at
twenty-six pixels.

Nothing of PEAK's is copied. The photograph is taken on the player's own
machine from the model already in the scene, because there was no icon to
borrow: `Luggage` derives from `Spawner`, not `Item`, so `UIData.GetIcon()` —
which is how the compass is drawn — has nothing to offer a chest.

The plate underneath survived on purpose: it carries the category colour and
the height shading, which is the one thing a top-down map cannot say and the
thing this map is for. The icon only darkens when it is below the player, since
an `Image` tint multiplies and lightening a photograph merely washes it out.

### What it took, and what each thing cost to find

Every one of these produced a plausible-looking failure that hid the next.

- **A render-only copy, not an instance.** A copied `Luggage` wakes up, joins
  `ALL_LUGGAGE` and brings a `PhotonView`, so photographing a chest would have
  edited the run. The copy is bare meshes, materials and transforms.
- **Alpha does not survive.** Told to clear to transparent, URP hands back a
  fully opaque texture, so trimming to "what was drawn" trimmed the whole frame
  and every icon was a black square. The object is photographed twice instead,
  against black and against white: unchanged pixels are the object, black-to-
  white ones are empty, and the gap between is the alpha the pipeline withheld.
- **`WaitForEndOfFrame` inside the end-of-frame phase resumes in that same
  phase.** Back-to-back bakes read the texture twice with no render between and
  got two identical frames. Wait a whole frame instead.
- **Exposure has to be a real control.** Turning the rig's own lamps down does
  not reach: a statue stayed 72% burnt out with them at a twentieth, because
  the scene's sun and ambient were doing the work. Dimming the copy's own
  albedo scales every source at once.
- **Scale every colour the shader declares, not just `_BaseColor`.** PEAK's rock
  shader blends `_Color1`, `_Color21`, `_Color3`, `_TopColor` and `_Tint`; the
  scout statue has nine colours in one material, and dimming one of them moved
  245 to 239 and no further. Shaders are asked what they have rather than
  guessed at by name.
- **Matte the copy.** A highlight does not come from the base colour, so
  dimming albedo left the gloss where it was. At map size a specular streak
  reads as a hole anyway.
- **Stand close.** The camera is orthographic and frames the same picture from
  any distance, so standing back four radii bought nothing.
- **Keep the best attempt.** Every shot after the first is an experiment, and
  one of them — falling back to an unlit shader — rescued the scout statue and
  ruined a marble one that was already fine.
- **Level afterwards.** The 2nd and 98th percentile of the object are stretched
  across the range, which brings a statue's folds back and a suitcase's straps
  up with one rule.
- **Names are generous.** "Statues" was 83 metres of Citadel masonry and "Floor
  Statues" 120 metres of floor. Over thirty metres across is scenery you stand
  in, not somewhere you walk to. The sweep also stops at the first match rather
  than descending, or a belltower and its own bell become two icons.

### Working on them without playing

Dialling icons in was costing five minutes of climbing per attempt, to reach
one statue. It costs nothing now.

Set `Automation/AutoRun`, `Automation/QuitWhenDone`, `Minimap/AutoBakeIcons` and
`Debug/DumpIcons`, then launch. The game walks itself into a solo run, wakes all
six biomes — everything above the beach exists but every renderer under it is
inactive until a player gets close — photographs every kind of thing, writes
each icon and a screenshot of the map to `capture-output/icons/`, and quits.
About a minute, unattended. Put `AutoRun` back to false to play.

The screenshot matters as much as the icons. Judging a PNG on its own says
nothing about twenty-six pixels on a plate over sand, and the first screenshot
immediately showed the altitude readout had been printing over the sky since it
was written.

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

## Open threads

- **A drawn navigator to hold the map.** The bordered frame reads as an overlay
  rather than as something from the game, and the compass in the corner was
  only ever there to excuse that. The plan is artwork: a device body with a
  transparent hole for the screen, an optional glass layer of scratches drawn
  over the map, and two-frame press artwork for the zoom buttons. The map is
  drawn as a rectangle underneath and the body covers everything outside the
  hole, so the hole can be any shape. Buttons cannot be clicked — the cursor
  belongs to the game during a run — so their animation is feedback that a
  keypress landed, which is why two frames is enough and a long one would lag
  behind a held key. The compass goes when the navigator arrives.

- Location names: the internal biome enum does not match what players call
  places (`Swamp` is the fog and the Citadel; `Roots` is the forest). A mapping
  belongs in the web taxonomy, ideally from https://peak.wiki.gg/wiki/Locations.
- Publishing as a Nexus mod: nothing prepared yet. No game assets are copied —
  icons are drawn at runtime and the compass is read from the loaded item — so
  there is nothing to strip before release.
- A full-fidelity Shore export is 102 MB; publishing the web version needs
  decimation, and that only makes sense once the geometry is complete.
