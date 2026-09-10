# Where this project stands

Written so the next session starts from what was learned rather than
rediscovering it. Updated at the end of the session that built the navigator
and got the mod ready to publish.

## It is published

Both stores are live. This is no longer a thing being prepared.

- **Thunderstore:** <https://thunderstore.io/c/peak/p/abra9987/Hiking_GPS/> —
  team `abra9987`, chosen because a team name is permanent and an author is a
  safer thing to be stuck with than a product. Versions cannot be deleted once
  up, which is why 1.0.0 and 1.0.1 are still there with faults in them.
- **Nexus:** <https://www.nexusmods.com/peak/mods/228> — mod 228.
- **Source:** <https://github.com/abra9987/Peak-Hiking-GPS>, public.

Three releases followed the first, each because publishing showed something
reading the files had not:

- **1.0.1** — `F11` (screenshot) and `F10` (bake every icon) were bound by
  default. Both are development tools; F11 had even been written into the
  controls table as a feature. Both are `KeyCode.None` now. `CaptureHotkey` had
  been unbound for this reason already; these two were missed.
- **1.0.2** — the readme claimed mods disable Steam achievements. **They do
  not.** PEAK checks `RunSettings.blockingAchievements`, which returns nothing
  but whether you started one of the game's own custom runs, and the assemblies
  contain no reference to BepInEx, Doorstop or any mod detection at all. This
  mod never touches achievements, run settings or Steam.
- **1.0.3** — the Thunderstore icon was lines of small text, unreadable at the
  size a list actually shows it. It is the device on plain grey now.

**The version used to be declared twice** — `<Version>` in the project file,
which the packaging script reads, and a literal in `Plugin.cs`, which is what
the assembly announces. They agreed only by hand, and diverged the moment the
project file moved: a zip labelled 1.0.1 shipped a plugin calling itself 1.0.0.
`Plugin.cs` now takes `MyPluginInfo.PLUGIN_VERSION`, generated from that same
`<Version>`.

**Commits no longer carry Claude attribution.** `~/.claude/settings.json` has
`attribution: {commit: "", pr: "", sessionUrl: false}`. The whole history was
rewritten to strip the old trailers, and note why the repository was recreated
rather than renamed: GitHub's contributor graph is computed separately from the
commit history and does not clear when the history does — a stale entry had
survived months in another repository.

### What the store forms actually do

Written down because none of it is guessable and all of it cost a retry:

- **Nexus's description editor is WYSIWYG.** Its last toolbar button, `View
  source`, is BBCode. Setting that textarea's value programmatically does not
  stick — it is React-backed and ignores a value it did not see typed.
- **Nexus draws its own breadcrumb, title and stats over the lower left of the
  header banner.** A banner with its own title there gives two titles on top of
  each other. `docs/media/header.png` keeps its text right and high, and leaves
  the lower left empty on purpose.
- **Nexus takes .jpg, .png and .gif only, 8 MB each**, and videos only as an
  external link. So `demo.webp` and `demo.mp4` cannot go on the page at all;
  the GIF can at 540x304, 96 colours, every second frame — 6.5 MB.
- **PEAK has two categories on Nexus:** Miscellaneous and Mod.
- **An uploaded file's version field defaults to `1`**, and the checkbox under
  it pushes that onto the mod, quietly undoing a version set earlier.
- **The BepInEx requirement defaults to the x86 build.** PEAK is 64-bit.
- **Thunderstore's team and category dropdowns drop a selection made too
  quickly** — the list has to be open before the click lands. Click one at a
  time and check.
- **The Nexus archive ships one file**, `BepInEx/plugins/HikingGPS/HikingGPS.dll`.
  It extracts over the game folder, so a readme or licence at its root landed
  loose in the player's PEAK directory. Thunderstore keeps manifest, icon and
  readme because that platform requires them and the readme is the page.

### Media that exists now

In `docs/media/`, committed, unlike `dist/media/`:

- `header.png` 1300x372 — the Nexus banner.
- `thumbnail.png` 1920x1080 — the Nexus cover: device large, one big title.
- `icon.png` in `packaging/` 256x256 — the Thunderstore icon, device on grey.
- `device.png` — **the device alone on transparency, composited from
  `plugin/assets` at the artwork's own 999x1216**: body, glass overlay and the
  three button faces, with a map screen lifted from a capture taken at
  `SizePixels 820`. Nothing of the game's interface is in it. This is the file
  to reach for whenever the device is wanted as an object rather than a
  screenshot.
- `shot-shore.jpg`, `shot-chest.jpg` — in-game screenshots.

**The button faces are drawn at 1.28 of their recess, not the 1.14** the comment
in `MinimapController` and the prose here both claim: `Grow` adds the fraction
on each side. The look is right and shipped; the numbers in the prose are what
is wrong.

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
- **GitHub:** remote `origin` is `https://github.com/abra9987/Peak-Hiking-GPS`.
  `gh` is not installed, but Git Credential Manager holds a token for
  `abra9987` with `gist, repo, workflow`, so `git push` works and the REST API
  can be driven with `git credential fill` for anything `gh` would have done.
- **BepInEx rewrites that file when the game exits**, from what it holds in memory.
  Editing it while the game is running loses the edit. Check the process first and
  read the file back after writing.
- Decompiled game source (ilspycmd) is in the session scratchpad; regenerate with
  `ilspycmd -p -o <dir> -r <Managed> <Managed>\Assembly-CSharp.dll`. It is the
  only documentation there is for how items, hands and loot actually work, and
  it has answered every question asked of it.
- **The capture clone now has PEAKLib in it**, because the item needs it:
  `plugins/PEAKLib/` holds Core, Items and SoftDependencyFix, `patchers/PEAKLib/`
  holds the MonoDetour patcher, and `core/` holds MonoDetour itself. That last
  part is easy to miss — the Thunderstore package for the patcher ships only the
  patcher, and without `MonoDetour/MonoDetour` beside it every hook fails with a
  `TypeLoadException` in the preloader and PEAKLib silently does nothing.
- `BepInEx.cfg` is back at its ordinary log levels. Turning both `LogLevels`
  lines up to `All` is how the PEAKLib problem below was found, and is worth
  reaching for whenever a mod appears to load and then does nothing: PEAKLib's
  own debug lines were the difference between a guess and an answer.
- **`tools/preview-tracker.ps1` is the loop for anything to do with the device.**
  It writes the clone's config, launches the game off-screen into a solo run,
  photographs the model from four sides and then in a character's hands, copies
  the pictures and the log back into `capture-output/tracker/`, and the game
  quits itself. About two minutes, unattended. Everything in the section on the
  physical item below was learned from it.
- **Steam has to be running and logged in to play the clone.** `steam_appid.txt`
  stops the exe bouncing the launch to the Steam copy, but Steamworks itself
  still needs the client: without it `SteamAuthTicketService.VerifyHasValidTicket`
  throws every frame and the game sits on a blank beige screen forever, with
  nothing in the BepInEx log to say why — it is in `Player.log` under
  `LocalLow/LandCrab/PEAK/`. None of the unattended runs meet this, because
  `AutoRun` takes the game offline before it ever asks Steam for anything. So
  the first person to simply *play* the clone hits it, and it looks exactly like
  a mod that hangs on startup.
- **The game must not already be running when that script starts**: BepInEx
  rewrites the config from memory on exit and would throw away the settings it
  had just been given. The script refuses rather than racing.
- Building while the game is running silently fails to copy the new DLL — the
  copy target is `ContinueOnError`. Build again after the run.

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

The mod is **Hiking GPS**, now at 1.0.3 — plugin id `com.abra9987.hikinggps`, assembly
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

- **Multiplayer has still never been tested.** It is the one claim on both
  store pages that nobody has checked: the design is client-side and read-only,
  which is why it should be fine, and the markers for other climbers cannot be
  tested alone at all. The item makes this more pressing than it was — an item
  is a networked object, and two people in a room each holding one is a case
  that has never existed.
- **A ten-second clip of the device being held and switched on, with sound**, is
  wanted for the store pages. Everything in it now exists; the missing piece is
  the recording itself. `ScreenCapture` gives frames and no audio, so this needs
  a screen recorder or ffmpeg capturing a device — the earlier demo clips were
  made from a recording and composed frame by frame in PIL, and that path still
  works.

The manifest is finished. How each field was settled is worth keeping:

- **`website_url` is `https://github.com/abra9987/Peak-Hiking-GPS`**, and the
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

## The tracker as a physical item

**It is one. The device is in the game, in a chest, and in a character's
hands.** What follows is how, and what it cost to find out.

**Unity is not needed anywhere, and was never installed.** The route everyone
documents — author a prefab in the editor, bake an AssetBundle, register that —
pins a mod to one exact editor version and breaks quietly when the version
drifts. It is also not what the library actually requires. `ItemContent(Item)`
takes a live component on a live GameObject and does not care where it came
from, so the whole thing is built in code:

```
plugin/model/peak_tracker.glb     the model, committed, source of truth
tools/build-tracker-mesh.py       -> plugin/assets/tracker.mesh  (131 KB blob)
plugin/src/Tracker/TrackerModel.cs    blob -> Mesh at runtime
plugin/src/Tracker/TrackerObject.cs   Mesh -> a GameObject with materials
plugin/src/Tracker/TrackerItem.cs     GameObject -> Item + LootData + grips
plugin/src/Tracker/TrackerRegistration.cs   the only file that mentions PEAKLib
```

The blob is committed, so a build is still one `dotnet build` with no Python in
the way. Rebuild it only when the model changes.

### The conversion, and why it is done offline

Two things go wrong between Blender and this engine, and both fail plausibly
rather than loudly. `tools/build-tracker-mesh.py` does them once, at a terminal,
and prints what it produced:

- **Handedness.** glTF is right-handed with -Z forward; Unity is left-handed
  with +Z forward. The mapping is `(x, y, -z)`, which flips handedness in one
  step and lands the screen's outward normal on Unity's +Z — so the device's own
  forward is the face you read, and **pressing a button is -Z**. Because that
  mapping is a reflection, **triangle winding is reversed too**, or every face
  points into the case. The script checks winding against the normals and says
  `INSIDE OUT` when they disagree — which is worth trusting, because the first
  version of that check had the cross product backwards and accused a perfectly
  good model.
- **Texture origin.** glTF's V runs down from the top, Unity's runs up. Get it
  wrong and the palette still maps to real colours, just the wrong ones.

### What the game's shaders actually offer

Read out of a running game rather than guessed, because none of it is written
down anywhere:

- **`W/Peak_Standard`** is PEAK's own, and `Shader.Find` finds it. Its base
  texture is **`_BaseTexture`**, not `_BaseMap` or `_MainTex` — the first attempt
  asked for those, got no error, and the device came out of the game white. It
  also has **`_BaseTexAmount`**, which starts at zero, so setting the texture
  alone is not enough. Metal and smoothness are **`_BaseMetallic` and
  `_BaseSmooth`, single figures for the whole material**: it cannot take a mask.
- **`Universal Render Pipeline/Lit` and `.../Unlit` are both in this build.**
  The lit one has `_MetallicGlossMap`, so it can take the mask.

**The case is drawn with PEAK's own shader and the mask is carried unused.**
Both versions were built side by side and photographed together on the beach.
The pipeline's shader turned the case olive and the bezel deep blue — the mask
working exactly as designed, and a smoothness of 0.78 reflecting a tropical sky.
It looked like a prop from another game. That comparison can be run again by
flipping `TrackerRun.Compare`.

**The screen is unlit**, and that is a different decision from the case. A
screen is backlit: drawn with a lit shader it goes dark at dusk, which is
exactly when somebody wants to know where they are.

### The model, V5

`Documents\DEVELOPMENT\Codex Playground\3d blender\peak_tracker\runtime\` —
`peak_tracker.blend`, `.fbx`, `.glb`, `REPORT_V5.md`, `TASK_V5.md`.

Six render meshes now, not two: the body, the three button caps and the antenna
are separate objects so they can be pressed and can sway. 1806 triangles still,
plus a 12-triangle collider. Buttons travel **0.6 mm**, and in the exported
coordinates that is +Z — which the converter's flip turns into **-Z in Unity**,
into the case.

**The palette is a grid of 32x64 blocks, not 32x32.** This cost an iteration and
is the kind of thing that hides: by a 32x32 grid, 258 triangles straddle a
boundary, and nobody notices because both halves of a block are the same colour.
For the mask they would not be. The check that matters works in UV space and
derives the granularity instead of assuming it — it is in `TASK_V5.md` §7, and
on V5 it reports zero straddles at 32x64 with 9 of 16 blocks used.

Two textures ship: `tracker_body_palette.png` and `tracker_body_mask.png`, both
256x128. The palette is sampled **point, with no mipmaps** — it is a grid of flat
swatches, and smooth filtering would paint a seam along the edge of every face
while mipmaps would average the whole grid into mud at any distance.

### Registering it as an item

`PEAKModding.PEAKLib.Core` and `.Items` come from **NuGet**, referenced with
`ExcludeAssets="runtime"` so they are compiled against and never shipped. The
dependency is **soft**: everything that worked before — map, navigator, markers
— works with PEAKLib absent, and only the item needs it. Every mention of it
lives in `TrackerRegistration.cs`, so a missing assembly cannot break anything
that does not touch that one method.

**Loot needs no patching at all.** `LootData.PopulateLootData` walks the item
database and reads a `LootData` component off whatever it finds, so declaring a
`Rarity` and a set of `SpawnPool` flags is the whole of "put it in chests". The
defaults are `Rare` and `LuggageBeach`, both settings — measured at **6.5% of
beach luggage**. A map is worth most before the climb; a navigator found in the
Citadel is a souvenir.

**`Hand_L` and `Hand_R` are made in code, not in Blender.** The game does
`item.transform.Find("Hand_L")` at the moment somebody picks an item up and
welds both hands to it with a `FixedJoint` unless `rightHandOnly` is set — so
**two hands is the default**, and a missing empty is a null dereference rather
than a warning. They carry no geometry, and the only way to judge them is a
photograph of a character holding the thing, which is this repository's loop and
not Blender's. The convention was measured off the game's own items:

```
MagicBean     Hand_L (-0.0863, 0.000, -0.1060)  euler (270.0, 195.0,   0.0)
              Hand_R (+0.0943, 0.000, -0.1060)  euler (270.0, 165.0,   0.0)
ShelfShroom   Hand_L (-0.2930, 0.070, -0.2960)  euler (272.7,   0.0, 195.0)
              Hand_R (+0.3500, 0.070, -0.2960)  euler (272.7,   0.0, 165.0)
```

Behind the item on -Z, mirrored on X, turned -90 about X, splayed by 15 either
way. Reading the whole item database rather than the handful lying on a beach
confirms it — of nearly two hundred items, the simple ones agree exactly:

```
Antidote       Hand_L (-0.160, 0.000, 0.014)  euler (270, 195, 0)
               Hand_R (+0.160, 0.000, 0.014)  euler (270, 165, 0)
Bandages       euler (270, 210, 0) / (270, 150, 0)
Beehive        euler (270, 210, 0) / (270, 150, 0)
Airplane Food  euler (270, 180, 0) / (270, 180, 0)
```

**X is 270 and Z is 0 on every one of them**; the only thing that varies is the
splay about Y, which is 180 give or take 0, 15 or 30 degrees depending on how
wide the thing is. Fruit and rope spools have hand-authored angles that look
nothing like this, because they are held rather than gripped. `TrackerRun`
prints this table on every run.

**A held item's +Z points where the character is looking.** `GetItemHoldForward`
returns `character.data.lookDirection` and the game torques the item until its
forward matches — so +Z is the face of the item that points *away* from the
person holding it. The model's +Z is the screen, which meant the first working
version handed the player a navigator held backwards: reading the back cover
while the map faced the scenery. The model therefore sits inside the item root
rotated 180 degrees about Y, so the item's forward is the back of the case.

That also flips what "in front of the device" means for anything aiming a
camera at it — the screen is on the item's -Z.

**It is a flip about the vertical, not a tilt.** Worth saying plainly, because
"rotated 180 degrees" reads like the model was leaned over and it was not. The
angle a held item sits at is the game's to decide and it decides it the same way
for everything: forward along the look direction, so any face perpendicular to
that forward is perpendicular to the view, at any pitch. The guidebook faces its
reader for exactly this reason. All the rotation settles is *which* of the two
flat faces is the one turned towards them.

Which is not the same as saying a camera behind the character can see it: the
item hangs at the chest, so from far enough back the character's own body is
between the camera and the glass, and the only reason it reads at all is that
PEAK's camera sits almost on the head.

In the automated run the device ends up at the very bottom of the player's view,
which briefly looked like a finding about whether a held map can be read at all.
**It is not one.** That run leaves the character half-spawned on purpose, and
the pose it holds things in is not the pose a played character holds them in —
in a real session the hands track the view and stay in frame, up or down.
`hand-looking-25.png` and `hand-looking-45.png` are the game's own camera tilted
down, and they settle it: the device sits centred and readable, held in both
hands with the map facing up.

**Two settings exist for tuning this by hand, in a live game.** `Tracker/Scale`
multiplies the whole device — case, collision and grip points together, so the
hands keep hold of it — and `Tracker/ReadoutScale` grows the strip of numbers
and its lettering. Both default to 1, and both are the honest lever for the same
complaint from opposite ends: one makes the device bigger in the world, the
other gives the numbers more of a device that is already the right size.
`tools/preview-tracker.ps1` takes `-Scale` and `-ReadoutScale` so a value can be
photographed without editing the clone's config between runs.

**What those pictures do raise is the size of the writing.** The screen renders
at 512 pixels across and lands on a player's display about ninety wide, so
everything on it shrinks five-fold. The map survives that; the altitude readout
does not, and comes out as a smear along the bottom of the glass. A real
handheld solves this by showing less: one big number instead of a line. That is
a decision about what the device is for, not a bug, and it is the next thing
worth deciding.

**The grips are judged from a character who has just walked**, not one standing
perfectly still. Idle brings the arms in towards the body and blends the hand
IK against whatever the animator is doing, so a photograph of somebody standing
still shows the pose a player will see least of. `HandShot.Walk` writes
`movementInput` for a couple of seconds first — written rather than pressed,
because nobody is at the keyboard, and it lands after the game's own sampling
fills that field with zero. If a future version samples later this stops
working, and the logged walking speed says so plainly.

### PEAKLib does not always install its own hooks

**This is the one thing here that is somebody else's bug and still has to be
lived with.** PEAKLib adds a registered item to the game's database from a hook
on `ItemDatabase.OnLoaded`. On PEAK v2.4.b with PEAKLib.Items 1.6.2 that hook
never installs: the module logs nothing at all, not even at Debug, while
PEAKLib.Core's hooks work fine beside it. The result is the worst of the three
possible outcomes — the item exists, has a network prefab, and is invisible to
the game, with every step reporting success.

So `TrackerRegistration.EnsureInDatabase` checks and, if needed, does that work
by hand. The id is hashed **exactly** the way PEAKLib hashes it, the same two
strings in the same order, so an item registered either way is the same item
with the same id. It also clears `LootData.AllSpawnWeightData`, because the
spawn weights are computed once and cached, and an item added afterwards would
otherwise be in the database and in no chest.

Diagnosing this needed `LogLevels = All` in `BepInEx.cfg`; a backup of the
original sits beside it as `BepInEx.cfg.bak`.

### The map on the screen

`MinimapController.OnDevice` switches the map from a drawing in the corner to a
texture a mesh can wear. The same camera draws the same mountain; what changes
is that the canvas — markers, player arrow, altitude readout — is rendered by a
camera of its own into a second texture, and the drawn case, glass and buttons
are not built at all, because they are the mesh now.

That camera and its canvas are **parked five kilometres under the sea**. A
screen-space canvas sits directly in front of whichever camera draws it, so
leaving it near the mountain would hang a full-size copy of the map in the air
beside the player.

Its culling mask is the canvas's own layer and **not zero**. A screen-space
canvas is drawn by its camera as part of that camera's ordinary rendering, so it
is culled by the same mask, and a camera told to see nothing sees least of all
the canvas it exists to photograph. Clearing the mask to keep the mountain out
produced a texture containing exactly the background colour while every log line
said the map had been handed over. Distance keeps the world out instead.

**Where the map goes is decided once, when the item's fate is known.** On the
device if there is one — a player holding a navigator does not also want a
second one in the corner, and rendering the mountain twice would be a strange
way to spend a frame — and in the corner if PEAKLib is absent or registration
failed, because a map somewhere beats no map. That decision cannot be made in
`Awake`: PEAKLib loads after this plugin, so it waits until the same moment the
item is registered.

**That path has been watched in a real session and works.** It was first
verified through the preview run, which uses AutoRun — and AutoRun deliberately
builds no map at all, creating its own in device mode instead — and then
confirmed by playing: the map appears on the device found in a chest, not in
the corner. Nothing here is left to check.

### The map has to be carried

Found by playing, within a minute of the first spawn: the device chirped awake
at the start of the run, answered the zoom keys and showed its map — with no
navigator anywhere in the inventory, and none ever picked up.

The cause was the shape of the port rather than a slip. The map has always been
a thing that exists while the run does, and turning it into a device's screen
changed where it is drawn without changing when it is alive. So the whole
device was running in the abstract: sounds, keys, markers, all belonging to an
object the player had never found. **A device that works without being carried
is not a device, it is a heads-up display wearing one as a costume.**

`MinimapController.Carrying` now gates it — always true for the drawn map in the
corner, and on a device an actual check that the local character is holding an
item whose `itemID` matches the registered one. By id rather than name: a clone
carries "(Clone)" and the mod's prefix. The waking and sleeping chirps land on
picking it up and putting it away, which is where they belonged all along.

### The size lives on the model, not on the root

`Tracker/Scale` used to scale the item's root, and `forceScale` was turned off
so the game would stop resetting it. Both are undone. The root is unit scale
now, the model and its collider carry the size as children, and the grip
points and centre of mass are multiplied by hand. `forceScale` is back on,
so the device halves inside a backpack like every other item.

The reason is the hands. The game welds each hand to the item's Rigidbody
with a `FixedJoint`, and Unity's joints do not support a scaled body: the
joint frames come out wrong by the scale and the solver pulls against the
error every step. It was not the whole story of the grip (below), but it was
a real fault, and a scaled Rigidbody is not worth having for any reason.

**Blender is not involved.** The model is the size the device really is, and
that is worth keeping: the setting exists precisely so the *apparent* size can
be tuned without the file ever becoming a lie about the object. `Scale` is
now applied live, so it can be tried in a running game.

### What playing it settled

The first session with a person actually holding the device, and the second
one, spent entirely in front of the airport mirror. Everything here came from
those hours and not from a photograph.

**Settled, do not re-open:**

- **`Tracker/Scale = 4` is the right size**, held at the settled distance.
- **`Tracker/ArrowScale = 3` is right too**, and the question is closed. At the
  drawn map's sixteen pixels the "you are here" arrow vanished into the terrain
  on a screen seen at a fraction of its render size.
- **The readout is legible at that size** without touching `ReadoutScale`.
- **The map is only alive while the device is carried**, confirmed in play.
- **The map lands on the device's screen, not in the corner**, when the item
  is registered — the real branch, with PEAKLib and without AutoRun.
- **The buttons are seen to move and the sounds are heard.** Chirps on pick-up
  and put-away, clicks on zoom and tilt, the knock at the end of the ladder.
- **The grip.** Settled, and the person holding it called it ideal. What it
  actually was is the next section.

### The grip, and what it actually was

Five passes at the hand angles were wrong, and every one of them was wrong
for the same reason: the angles were never the problem. Two things were, and
neither was visible from the grip code.

**`Item.defaultPos` was zero.** Every item carries a point, relative to the
head and in the direction of the look, where the game holds it — the
animation rig's item anchor is put there, and the arms' IK targets are the
`Hand_L`/`Hand_R` offsets from that anchor. The game's own items set it in
their prefabs; the passport is held at (0, 0, 1), a metre straight ahead. A
device built in code left it at the default, which is inside the head. So the
hold point sat in the face, the arms folded back to reach it with the elbows
by the ears, and on pick-up the game slid the device from 0.7 m in front
into that point over a fifth of a second — which is what "the palm stays
still while the forearm slowly winds round" was. `Tracker/HoldX`, `HoldY`,
`HoldZ` set it now; the settled point is (0, -0.25, 0.85).

**The root was scaled**, above. Fixed independently and worth fixing, but on
its own it changed nothing anybody could see, because the hold point was
still in the head.

The settled numbers, all defaults now and all in `[Tracker]`:

```
Scale 4      HoldX 0  HoldY -0.25  HoldZ 0.85    TiltDegrees 10
LuggageTurn 0     LuggageLift 0.06   MarkerScale 2
GripAcross 0.05  GripBehind -0.033  GripHeight -0.012
GripAngleX 270   GripAngleY 202     GripAngleZ 0      (the passport's)
```

The grip angles describe the left hand as Unity Euler angles in the item's
frame; the right hand is the mirror, which for Euler angles is the same X
with Y and Z negated — checked against every pair in the game's item
database. `TiltDegrees` leans the top of the case back towards the face.
`OffsetUp` / `OffsetAway` move the model inside the root and are zero.

**How it was found, which is the part to reuse.** Three things made the
airport a workshop instead of a five-minute walk to a chest:

- `Debug/SpawnDeviceKey` (bound to `F8` in the clone) hands over a navigator
  wherever the character stands, through the same path as the game's own
  debug command: `PhotonNetwork.Instantiate` then `Item.Interact`. With
  `Debug/SpawnItemName` set it hands over one of the game's items instead —
  `Compass`, `Passport` — so the two can be held in front of the same mirror.
- `Debug/ReloadConfigKey` (`F9`) re-reads the config file; every grip setting
  applies at once to the pattern and to every device in the world. The game
  welds the hands at pick-up, so it is `F9`, drop, `F8`, look.
- While the spawn key is bound, the log prints every two seconds what is
  held and where its hands are: mass, inertia, `defaultPos`, the
  `Hand_L`/`Hand_R` transforms, and the IK targets. Holding the passport and
  then the device and reading the two lines side by side is what found
  `defaultPos`; nothing about the grip code could have.

Also read out of the game's files with UnityPy, which parses `Transform` and
`GameObject` fine even though it fails on MonoBehaviours and meshes here:

```
Compass    Hand_L (-0.225, -0.052, -0.120) euler (315.0, 160.7,  45.0)   model +0.152 up
Passport   Hand_L (-0.183, -0.184, -0.100) euler (270.0, 125.7, 125.7)   = (270, 251.4, 0)
```

The compass's model sits 15 cm above its root; that is why it rides above
the hands, and it is a different way of holding a thing from the passport's.

**Idle hands clasped in front after putting the passport away are the
game's**, seen on a fresh launch before any key of this mod was pressed.

### It lies on its back now, and why it would not before

A dropped device stood on its bottom edge, leaning a few degrees, and stayed
there; thrown, it lay on its glass as readily as on its back. Three things
fixed it, and the first two were tried alone and did nothing visible:

- **The collider is no longer the case's box.** `TrackerObject.RestlessHull`
  builds a convex hull from the box's bounds with the back face inset on
  every side and a low off-centre ridge down the front. Every face but the
  back leans, so upright it stands on its front-bottom edge with the weight
  behind, and on its glass it rocks off the ridge onto a side edge.
- **The centre of mass is high and back**, (0, 0.03, 0.015) times Scale in
  the root's frame: standing, the weight hangs behind the edge with a long
  lever. It used to be low, which made a roly-poly.
- **`sleepThreshold` is zero.** This was the one that mattered. PhysX puts a
  body to sleep once its motion drops under a threshold, and a thin slab
  landing nearly upright is under it before gravity has leaned on it — so it
  froze mid-topple, a few degrees off vertical, and the hull and the weight
  never got their say. With sleep off it falls over at once.

**In a suitcase it is placed, not dropped.** Luggage keeps what it lays
out kinematic until somebody takes it, so no physics applies and the pose
it is given is the pose it is found in. `TrackerDevice.PlaceInLuggage`
writes that pose whole, in the suitcase's own frame, the first frame the
device is found kinematic on the ground: the middle of the spawn spots
(they sit on the case's centre line in every suitcase in the game's
files), `LuggageLift` above them, `Quaternion.Euler(90, LuggageTurn, 0)`
so the back cover faces the floor and the antenna points where the turn
says — 0 is across the case with the antenna towards the lid, away from
whoever opened it, which is the settled default. `LuggageAlong` and
`LuggageAcross` nudge from the middle and are zero.

The game's own hooks for this — `Item.offsetLuggageSpawn` with a position
and a rotation — were tried first and abandoned: the rotation is composed
onto a spawn spot that is itself turned, and the visual centring moves a
half-metre device with an antenna 40 cm off the spot, so every guess at a
number produced a different wrong pose. Writing the pose outright, in one
frame, is shorter and has no such history.

Three mistakes in that, worth not repeating. The dark panel on the back of
the model is a battery cover: a dark rectangle in an orange frame seen
from above is the *back*, not a switched-off screen, and three screenshots
were read wrong for that reason. "The nearest suitcase" by root distance
is the wrong suitcase often enough to matter — a suitcase's root sits at
one edge — so the device was carried into the closed case next door and
showed up there as a second item beside a glider; it is chosen by the
spawn spot nearest the device now. And the person testing it was walked
to the beach and back a dozen times before the unattended run learned to
do it: `LuggageShot` in `TrackerRun` now opens the nearest suitcase's lid,
lays a device in it through the suitcase's own `OffsetSpawn` and
`InitializePhysics`, and photographs it from above and from the front as
`luggage-top.png` and `luggage-front.png`. `tools/preview-tracker.ps1`
takes `-LuggageTurn`, `-LuggageLift`, `-LuggageAcross`, `-LuggageAlong`.
That loop is two minutes and nobody's legs.

**The zoom is kept between glances.** It used to snap back to
`StartZoomStep` every time the map opened; in play that threw away a
scale chosen on purpose every time the device went into a pocket.

**Only the held device is on.** Every device drew the same map and pressed
the same buttons, so three on the sand all zoomed together. Each has its
own screen material now; it shows the map only while it is the local
character's current item, and is dark otherwise. In the preview run, with
no character, every screen stays on. In multiplayer this means another
climber's navigator is seen dark — their map is theirs.

**`Tracker/Scale` is 4.** 3.5 was settled first, then 4 was tried at the
same distance and preferred for the map's legibility, and it is the default. `Tracker/MarkerScale` doubles the
marker plates on the device only, and the readout is bold.

A self-righting nudge was tried in between### The buttons move

`TrackerDevice` sits on every built device and moves the three caps 0.6 mm into
the case when their key is pressed — down on the frame the click sounds, up
gently over about a tenth of a second. A button that eases down as slowly as it
comes up feels like a sponge.

`MinimapController.Press` is the single place a button goes down, and it now
reaches three things at once: the drawing, the mesh, and the sound. So the
corner-of-the-screen map and a device in somebody's hands stay in step without
either knowing about the other.

**Seen in play.** The unattended run presses no keys, and 0.6 mm would not read
in a photograph taken at arm's length even if it did, so this was only ever
going to be confirmed by a person — and it was: the caps visibly go down on
the click and come back up.

### Sounds

Six clips in `plugin/assets/`, embedded like everything else: `power_on`,
`power_off`, three `click_*` and `zoom_limit`. `Sound/Volume` controls them,
0 for silence. Wired into the existing map already — `M` wakes and sleeps the
device, the zoom and tilt keys click, and the end of the zoom ladder knocks.

**Mono, 16-bit PCM, 44.1 kHz, and the mono part is not a preference.** Once the
device is an object somebody is holding, its sound is positional, and the engine
mixes a mono clip into stereo from where the object is. A stereo clip cannot be
placed at all. WAV rather than anything compressed because a WAV becomes an
`AudioClip` from a byte array in a few lines, where every compressed format
wants a file on disk and an asynchronous load.

The chunks are **walked**, not assumed at byte 44: an encoder may put LIST or
fact chunks before the samples, and a reader that seeks to a fixed offset plays
metadata as noise without throwing.

The brief that produced them is `docs/TASK_SOUNDS.md`, and the generator is kept
in `docs/sound-source/`. Three clicks rather than one because a zoom is five or
six presses in a row and a single sample makes that a machine gun; they differ
by about 5% in pitch, length and decay. Heard in a real climb now: the chirps
and clicks sound right, and a zoom does not sound like a machine gun.

**The game is Unity 6000.3.15f1** — read out of `PEAK_Data/globalgamemanagers`
on this machine, and the same version the PEAK modding guide names. That
question is settled; do not re-open it.

### The screen, and why its shape matters

**The screen is deliberately bare.** An earlier version of the model had the map
baked into it as `screen_map.png`, which would have shipped an item permanently
showing one frame of Shore reading "chest 19 m +4 m". It is its own mesh with its
own material slot precisely so the plugin can put a texture there.

**Its aspect is 635:600 = 1.058333**, matched to `Navigator.Screen` on purpose.
The render texture is built at that ratio — 512x484 — and the frame that fills it
is sized width-first. Sizing it the other way round fits a 542-wide frame into a
512-wide picture and leans every slope on the mountain.

The two things this section used to warn about have both been answered by
running it. The screen does **not** arrive facing away: the converter's
handedness flip lands it on Unity's +Z, and the only rotation anything needs is
the deliberate 180 degrees inside the item, so the glass faces its owner. And
the map is **not** upside down — the converter flips V once, offline, and the
picture comes out the right way up on the first try.

## Open threads

- **In-game settings.** Everything is already bound through BepInEx config, so
  **ModConfig** (PEAKModding, ~709K downloads) would render a panel for free.
  Worth confirming it discovers other mods' entries before promising it.
- **The antenna does not sway yet.** It is its own object with its origin at
  its base, so this is the same shape of work as the buttons —
  `TrackerDevice.Update` is where it would go.
- **The second carrier, agreed and not built:** the device appearing in the
  hand on `M` rather than being found in a chest. The item and the hotkey want
  the same mesh, the same screen and the same sounds, and differ only in how the
  thing gets into a hand — so this is a second small class beside
  `TrackerItem`, not a second implementation.
- **Recolouring is now nearly free** and does not need the artist. The case is
  six flat swatches in a 256x128 palette; hue-shifting the orange pair and
  leaving the greys alone is a handful of lines against a config setting.
- **Signal and battery gauges** across the top of the screen — signal as
  climbers still on their feet, battery as daylight left, both from
  `DayNightManager.instance`, which has `timeOfDay`, `dayStart` and `dayEnd`.
  These are drawn on the screen, so they are the same work whether the map is in
  a corner or in a hand.
- **Pressed-state artwork is no longer wanted.** It was on this list for the
  drawn version, where a press had to be a second picture. On a mesh the button
  moves.
- `beetle` still bakes at 19% burnt out. Everything else is under 3%.
- Location names: the internal biome enum does not match what players call
  places (`Swamp` is the fog and the Citadel; `Roots` is the forest). A mapping
  belongs in the web taxonomy, ideally from https://peak.wiki.gg/wiki/Locations.
- A full-fidelity Shore export is 102 MB; publishing the web version needs
  decimation, and that only makes sense once the geometry is complete.
