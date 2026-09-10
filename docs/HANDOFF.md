# Where this project stands

Written so the next session starts from what was learned rather than
rediscovering it. Updated at the end of the session that built the navigator
and got the mod ready to publish.

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
- Config: `A:\PeakMapCapture\PEAK\BepInEx\config\com.abra9987.hikinggps.cfg`.
  The old `dev.peakmapinteractive.capture.cfg` beside it is dead; the mod was renamed.
- **`pwsh` is not installed here.** The packaging script's own examples say
  `pwsh tools/package.ps1`; on this machine it is `& '.\tools\package.ps1'` in
  Windows PowerShell 5.1, which runs it fine.
- **GitHub:** remote `origin` is `https://github.com/abra9987/hiking-gps`.
  `gh` is not installed, but Git Credential Manager holds a token for
  `abra9987` with `gist, repo, workflow`, so `git push` works and the REST API
  can be driven with `git credential fill` for anything `gh` would have done.
- **BepInEx rewrites that file when the game exits**, from what it holds in memory.
  Editing it while the game is running loses the edit. Check the process first and
  read the file back after writing.
- Decompiled game source (ilspycmd) is in the session scratchpad; regenerate with
  `ilspycmd -p -o <dir> -r <Managed> <Managed>\Assembly-CSharp.dll`.

**`AutoRun` and the minimap are mutually exclusive.** Capture automation
dismisses the loading screen mid-spawn and borrows the camera; playing under it
leaves the character half-initialised and under the terrain. The unattended icon
run is the one exception, and it forces the map visible on purpose: it only
needs a loaded level to photograph, not a working character.

## The minimap

Toggle `M`, zoom `=` / `-`, tilt `N` (90 / 75 / 45 degrees). North stays up.
`F10` photographs every kind of thing on the whole mountain at once, and `F11`
saves a screenshot to `capture-output/shots/`.

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

## The navigator

The map is the screen of a drawn handheld GPS — `plugin/src/Minimap/Navigator.cs`
and `plugin/assets/`. The compass is gone; it only ever existed to excuse the
old bordered frame.

The artwork is ours, drawn for this mod, and is built into the assembly as an
embedded resource. That is the whole reason marker icons are photographed at
runtime instead: PEAK's art stays in PEAK, and everything shipped is not PEAK's.

**Where things sit is measured, not chosen.** A hole is where the alpha is
empty, so the screen is 635x600 at (182, 295) and each of the three recesses is
179x143, and the code holds those as fractions of the 999x1216 case. Redraw the
case and four constants follow. The assets were cropped to the case: they
arrived on a 1254 square with a fifth of the width as empty margin, which was a
fifth of the space the map wanted.

Three things that only showed up once it was in the game:

- **The recesses are holes straight through the drawing.** A button face that
  merely fits one leaves its corners open to the mountain behind. Putting faces
  behind the case closed that and cost what it was for — framed by a hole, a
  button reads as sunken. They are drawn on top and 14% larger than their holes,
  so the hole is covered outright and the face's own rim lands on the case. A
  press shrinks to 93% (still 1.06 of the hole), so the overhang more than
  halves without ever uncovering anything.
- **The readout ran off the screen and printed across the case.** It is one
  line now, auto-sized, on a strip 15.5% of the screen height.
- **Markers were drawn over the numbers.** They are created as things come into
  range, later than anything built at startup, and UI draws in the order things
  were added. They live on a layer of their own now, added before the strip.

Position, size and the player arrow's colour are settings. Dragging with the
mouse is deliberately not offered: during a run the cursor belongs to the game.

**All four placement settings have now been seen working.** `Minimap/Corner`,
`MarginXPixels`, `MarginYPixels` and `PlayerMarkerColour` were each driven to a
non-default value and photographed, one unattended icon run per corner. The
arrow took `#33C6FF`, the margins moved the case by the pixel count asked for,
and every corner put the device where it says.

What that turned up, which no amount of reading the code would have:

**The bottom two corners collide with PEAK's own HUD.** Measured on a 1600x900
screen at the default 320 px size: the stamina bar occupies `y 820..842,
x 61..555`, and the item slots `y 795..880, x 1237..1510`. At the default
14 px margin the case lands on both. Raising `MarginYPixels` to 95 bottom-left
and 115 bottom-right lifts it clear, confirmed by another run each. Both top
corners are clear as they stand, which is why `TopRight` is the default and
should stay it. This is now said in the `Corner` and `MarginYPixels` config
descriptions and in `packaging/README.md`, so a player meets it where they are.

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

## Releasing it

The mod is **Hiking GPS 1.0.0** — plugin id `com.abra9987.hikinggps`, assembly
`HikingGPS.dll`. The rename had never actually been run: the config file the new
id writes did not exist and the last log still said "Peak Map Interactive -
Capture", so between the rename and now nothing had loaded the renamed assembly
even once. It does load — `Loading [Hiking GPS 1.0.0]`, clean, no errors — but
that was luck rather than checking, and a rename is exactly the kind of change
that compiles perfectly and then fails to be found at runtime. Thunderstore is the target, not Nexus: PEAK's community has
around 1119 packages there against 72 on Nexus, and all three mod managers
(r2modman, Thunderstore Mod Manager, Gale) pull from it. There is no Steam
Workshop for PEAK, deliberately. Nexus is worth doing as a second channel
because it costs almost nothing extra.

`pwsh tools/package.ps1` builds both archives into `dist/`:

- **thunderstore** — manifest and icon at the root, plugin in a bare `plugins/`
  folder. This is the one the mod managers install.
- **nexus** — `BepInEx/plugins/HikingGPS/`, extracted over the game folder.

Neither carries a config file: BepInEx writes one from the compiled defaults,
and shipping a filled-in one would hand every player whatever this machine
happened to be set to.

**The defaults are the release.** A fresh install used to have `AutoRun` on,
which would have taken a player's session offline, started a solo run by itself
and quit the game. It is off now, along with the exporter, and the capture
hotkey is unbound. Harmony patches are installed only when the automation is
switched on, so a player's game is not patched at all — which is what makes it
safe in company.

Licence is split, in `packaging/LICENSE`: MIT on the code so somebody can keep
the mod alive when the game updates and the author is busy, artwork reserved
because it is the mod's face.

### Still needed from a person

- **A Thunderstore team.** Its name is permanent — it cannot be renamed or
  deleted once a package is published. This is the last thing standing between
  the archives in `dist/` and a published mod.

The manifest is finished. How each field was settled is worth keeping:

- **`website_url` is `https://github.com/abra9987/hiking-gps`**, and the
  repository behind it is public and pushed. Publishing it forced two things
  that had been quietly wrong. The root `LICENSE` was plain MIT while
  `plugin/assets` sits in the repository, so publishing as it stood would have
  licensed the artwork MIT and contradicted `packaging/LICENSE`; the same
  ARTWORK carve-out is now in both. And the top-level `README` was entirely
  about the web exporter and never mentioned the mod, so a player arriving from
  a Thunderstore link would have landed on a page about something else. It
  leads with the mod now, with the exporter as the half it grew out of.

- **The BepInEx dependency string is `BepInEx-BepInExPack_PEAK-5.4.75301`**,
  taken off the package page, confirmed against
  `api/experimental/package/BepInEx/BepInExPack_PEAK/` and against the full v1
  package dump. It is in `manifest.json`. Note the pack's version is not
  BepInEx's own: the clone runs BepInEx 5.4.23.5 under a pack numbered 5.4.75301,
  and that is expected rather than a mismatch to go fixing.
- **"Hiking GPS" is free.** The whole PEAK package list (8 MB, 1266 packages)
  was pulled and searched: nothing contains `hiking`, nothing contains `gps`,
  and no owner contains `abra`. Package names are scoped per team anyway, so
  this is about not colliding in search rather than about being blocked.
- **Multiplayer has never been tested.** The design is client-side and
  read-only, which is why it should be fine, and "should" is not "is". The
  markers for other climbers cannot be tested alone at all.

### Media

`dist/media/` (gitignored, rebuild rather than hunt for it):

- `demo.mp4` (1120x630, 30 fps, 5.4 MB), `demo.webp` (4.8 MB) and `demo.gif`
  (10.8 MB) — the same shot: gliding, then a push in on the device where the
  map can be read, then back out.
- `hero.png` — the page's opening image. **The device in it is a crop from a
  screenshot** and carries the game's own HUD with it; it wants rebuilding from
  `plugin/assets` at full resolution, which needs a large clean capture first.
- Screenshots taken in play with `F11`, which saves to `capture-output/shots/`.

Worth knowing for next time: GIF cannot compress between frames and is capped
at 256 colours, so smoothness costs it about triple. The same eleven seconds
are 10.8 MB as a 20 fps GIF and 5.4 MB as a 30 fps MP4 at twice the size. The
short smooth clips other mods post are MP4 or WebM.

The push-in was composed frame by frame in PIL: crop and centre eased between a
wide framing and the device, at 30 fps, with subpixel crop boxes — at 10 fps and
integer crops the move judders. **Measure the device in the recording rather
than computing it from config**; the first attempt assumed 296x360 and the clip
was actually made at 173x189, so the push barely moved.

## Open threads

- **In-game settings.** Everything is already bound through BepInEx config, so
  **ModConfig** (PEAKModding, ~709K downloads) would render a panel for free.
  Worth confirming it discovers other mods' entries before promising it.
- **Second version, agreed and deferred:** recolouring the case (either the
  author draws variants or the orange is hue-shifted in code, leaving the greys
  alone); the signal and battery gauges across the top of the screen — signal
  as climbers still on their feet, battery as daylight left, both from
  `DayNightManager.instance` which has `timeOfDay`, `dayStart`, `dayEnd`;
  pressed-state artwork for the buttons, two frames each.
- `beetle` still bakes at 19% burnt out. Everything else is under 3%.
- Location names: the internal biome enum does not match what players call
  places (`Swamp` is the fog and the Citadel; `Roots` is the forest). A mapping
  belongs in the web taxonomy, ideally from https://peak.wiki.gg/wiki/Locations.
- A full-fidelity Shore export is 102 MB; publishing the web version needs
  decimation, and that only makes sense once the geometry is complete.
