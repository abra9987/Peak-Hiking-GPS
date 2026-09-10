# Hiking GPS

![Hiking GPS](https://raw.githubusercontent.com/abra9987/Peak-Hiking-GPS/main/docs/media/hero.png)

A live minimap for PEAK, on a handheld GPS you carry. Press `M` and the mountain
is there — real terrain, drawn by the game itself, seen from above.

**Client side.** Only you install it. Nobody else in the lobby needs it, the host
does not need it, and it changes nothing anyone else sees.

## What it shows

- **The real mountain.** Not a redrawn diagram: a camera looking down at the
  world you are standing in, so what is on the screen is what is under you.
- **Chests, campfires, statues, belltowers, capybaras and the scoutmaster**, each
  drawn as a picture of itself rather than a coloured dot. Opened chests
  disappear, so the map only points at loot that is still there.
- **Height, which is what PEAK is about.** A marker grows and lightens above you
  and shrinks and darkens below — thirty metres sideways and thirty metres up are
  nothing alike on this mountain.
- **Everyone else on the climb**, at any distance.
- **Your altitude** and the distance to the nearest unopened chest.

![In the shore](https://raw.githubusercontent.com/abra9987/Peak-Hiking-GPS/main/docs/media/shot-shore.jpg)

## No game assets ship with this mod

The GPS itself is drawn for the mod. Every marker icon is photographed from your
own copy of the game, once, the first time you see that kind of thing — so the
pictures always match the version you have, and nothing of PEAK's artwork
travels.

## Controls

| Key | |
|---|---|
| `M` | Show or hide the GPS |
| `=` / `-` | Zoom, in fixed steps from 20 m to 2000 m across |
| `N` | Tilt: straight down, 75°, 45° |

## Install

Install it with a mod manager, or drop `HikingGPS.dll` into `BepInEx/plugins/`
by hand. Needs **BepInEx 5** (`BepInExPack PEAK`), which the manager installs
for you.

The map appears about ten seconds after a run starts, once your character is on
their feet.

## Settings

Everything is in `BepInEx/config/com.abra9987.hikinggps.cfg`, written on first
launch.

| Setting | |
|---|---|
| `SizePixels` | How large the GPS is drawn |
| `Corner` | Which corner it hangs in |
| `MarginXPixels`, `MarginYPixels` | How far in from the edges |
| `PlayerMarkerColour` | The colour of the arrow that is you, as `#RRGGBB` |
| `StartZoomStep` | Which zoom step it opens on |
| `MarkerSizePixels` | How large the markers are |
| `Icons` | Turn the photographed icons off, back to plain markers |
| `StartDelaySeconds` | How long after the mountain loads before the map opens |

Both top corners are clear of PEAK's own HUD. The bottom two are not: at the
default margin the GPS covers the stamina bar on the left and the item slots on
the right. To use them, raise `MarginYPixels` to about 95 bottom-left and 115
bottom-right.

Leave everything under `Automation` alone — it exists to develop the mod, takes
the game over and can quit for you. It is off by default.

## Notes

- Steam achievements keep working.
- If PEAK crashes on startup with BepInEx installed, launching with `-dx12` is
  the usual fix.
- Please do not report bugs to Aggro Crab or Landfall while running mods.
  Uninstall first, check the problem is still there, and only then report it.

Source and issues: [github.com/abra9987/Peak-Hiking-GPS](https://github.com/abra9987/Peak-Hiking-GPS)
