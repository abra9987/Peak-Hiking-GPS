# Hiking GPS

A live minimap for PEAK, on a handheld GPS you carry.

The screen shows the mountain as the game itself draws it — real terrain, real
colours, seen from above — with markers for the things worth walking towards.
Press `M` and it is there.

**Client side.** Only you install it. Nobody else in the lobby needs it, the
host does not need it, and it changes nothing anyone else sees.

## What it shows

- **The real mountain.** Not a redrawn diagram: a camera looking down at the
  world you are standing in, so what is on the screen is what is under you.
- **Chests, campfires, statues, belltowers, capybaras and the scoutmaster** —
  drawn as pictures of themselves rather than as coloured dots. Opened chests
  disappear, so the map only ever points at loot that is still there.
- **Height, which is what PEAK is about.** A marker grows and lightens when it
  is above you and shrinks and darkens when it is below, because thirty metres
  sideways and thirty metres up are nothing alike on this mountain: one is a
  walk, the other may have no route at all.
- **Everyone else on the climb**, at any distance.
- **Your altitude** and the distance and height difference to the nearest
  unopened chest, along the bottom of the screen.

The icons are not shipped with the mod. Each one is photographed from your own
copy of the game, once, the first time you see that kind of thing — so nothing
of PEAK's artwork travels, and the pictures always match the version you have.

## Controls

| Key | |
|---|---|
| `M` | Show or hide the GPS |
| `=` / `-` | Zoom in and out, through fixed steps from 20 m to 2000 m across |
| `N` | Tilt the view: straight down, 75°, 45°. A tilted view shows how much climbing lies between you and somewhere |
| `F11` | Save a screenshot |

## Installing

1. Install **BepInEx 5** for PEAK (the `BepInExPack PEAK` package). A mod
   manager does this for you.
2. Drop `HikingGPS.dll` into `BepInEx/plugins/`, or install through the manager.
3. Launch the game. The map appears about ten seconds after a run starts, once
   your character is on their feet.

## Settings

Everything is in `BepInEx/config/com.abra9987.hikinggps.cfg`, written on first
launch. The ones worth knowing:

| Setting | |
|---|---|
| `Minimap / SizePixels` | How tall the GPS is drawn. 360 is about a third of a 1080p screen |
| `Minimap / StartZoomStep` | Which zoom step it opens on, counting the tightest as 1 |
| `Minimap / MarkerSizePixels` | How large the markers are |
| `Minimap / Icons` | Turn the photographed icons off and go back to plain markers |
| `Minimap / StartDelaySeconds` | How long after the mountain loads before the map opens |

Leave everything under `Automation` alone. It exists to develop the mod: it
takes the game over, drives it into a solo run by itself and can quit for you.
It is off by default and there is no reason to turn it on to play.

## Notes

- Mods generally disable Steam achievements. This one is no exception.
- If PEAK crashes on startup with BepInEx installed, launching with `-dx12` is
  the usual fix.
- Please do not report bugs to Aggro Crab or Landfall while running mods.
  Uninstall first, check the problem is still there, and only then report it.

## Credits

The navigator artwork is original, drawn for this mod. Everything else on the
screen is PEAK's own, rendered live by the game and never copied.
