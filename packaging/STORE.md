# What to put where

The two store pages, field by field, so neither has to be invented twice.

## Thunderstore

The upload form reads most of it out of the archive itself. Upload
`dist/HikingGPS-1.1.0-thunderstore.zip`, which already carries `manifest.json`,
`icon.png`, `CHANGELOG.md`, `LICENSE` and the page text in `README.md`.

**Published:** <https://thunderstore.io/c/peak/p/abra9987/Hiking_GPS/>

| Field | Value |
|---|---|
| Team | `abra9987`. **The name is permanent** — it cannot be renamed or deleted once a package is published, which it now is |
| Package name | `Hiking_GPS` (from the manifest) |
| Version | `1.1.0` |
| Description | from the manifest, 121 characters |
| Categories | **Mods**, **Client Side**, **Quality Of Life**, **Tools** |
| NSFW | no |

The page body is `packaging/README.md`, rendered as markdown. Its images are
absolute `raw.githubusercontent.com` URLs, because a Thunderstore page has no
repository to resolve relative paths against — if the repository is ever renamed
again, those URLs move with it and must be updated here.

## Nexus

Nexus takes no manifest. Everything is typed into a five-step form, and what it
accepts is narrower than it looks. Learned by doing it:

- **The description editor is WYSIWYG, not a BBCode box.** Its last toolbar
  button, `View source`, switches to BBCode — that is where
  `packaging/nexus-description.bbcode` goes.
- **Setting that textarea's value programmatically does not stick.** It is
  React-backed and ignores a value it did not see typed; the text has to be
  entered as real keystrokes.
- **PEAK has exactly two categories on Nexus:** Miscellaneous and Mod. That is
  the whole list.
- **The header banner is 1300x372**, a different shape from the 1600x760 hero.
  `docs/media/header.png` is that shape, so the crop dialog has nothing to cut.
- **The gallery takes .jpg, .png and .gif only, 8 MB each.** No WebP. Videos are
  added as an external link (YouTube and the like), never uploaded — so neither
  `demo.webp` nor `demo.mp4` can go on the page. The GIF can: 540x304 at 96
  colours, every second frame, is 6.5 MB and fits.
- **The uploaded file's own version field defaults to `1`**, and the checkbox
  under it pushes that back onto the mod, quietly undoing a `1.0.0` set earlier.
- **The BepInEx requirement defaults to the x86 build.** PEAK is 64-bit; pick
  `BepInEx v5.4.23.4 x64 (64-bit)`.
- **The recommended permissions contradict this project's licence** — they say
  modification is Ask-me and re-upload is not allowed, while the code is MIT
  precisely so somebody else can keep the mod alive. Use "Write your own
  (custom)" and say the split: MIT code, reserved artwork.

| Field | Value |
|---|---|
| Name | Hiking GPS |
| Summary | A handheld GPS you find in a suitcase and carry. The mountain on its screen, drawn by the game itself, with markers for chests, campfires, statues, capybaras and everyone else on the climb. Client side — only you install it. |
| Description | paste `packaging/nexus-description.bbcode` |
| File | `dist/HikingGPS-1.1.0-nexus.zip`, extracts over the game folder |
| Version | 1.1.0 |
| Requirements | BepInEx 5 for PEAK, and PEAKLib (PEAKLib_Items + PEAKLib_Core) for the item — without it the corner map |
| Permissions | code MIT; the GPS artwork is reserved — see `packaging/LICENSE` |

Images to upload, from `docs/media/`: `hero.png` as the main image, then
`shot-shore.jpg` and `shot-chest.jpg`. `dist/media/demo.mp4` is the same shot as
a clip if a video is wanted; the GIF is three times the size for a worse picture
and is only worth using where a video cannot go.

### Updating a published Nexus mod, learned on 1.1.0

- The edit flow is five steps at `nexusmods.com/games/peak/mods/228/edit/general`
  and so on. General holds the mod version, the short and the full
  description; the full description's `[ ]` toolbar button is the BBCode
  source. `Save` greys out when everything is saved.
- **Files: use "Update existing file"**, pick the old main file, tick
  "Archive existing file", then set the display name and version — they
  reset to the old file's values the moment an existing file is chosen, so
  set them after, not before. "Update mod version to match this file's
  version" is fine once the versions agree.
- **PEAKLib is not on Nexus**, so it cannot be a file-to-file requirement;
  the description carries it, with the Thunderstore install.
- Gallery images upload the moment they are chosen; the old ones are deleted
  one at a time through each picture's menu, and the grid reflows after
  each, so the same slot is clicked again.
- The page's renderer stalls screenshots for ten seconds at a time; the
  accessibility tree and a line of JavaScript keep answering.

## Both

The archives are built by `pwsh tools/package.ps1` — on this machine,
`& '.\tools\package.ps1'` in Windows PowerShell 5.1. Neither archive carries a
config file: BepInEx writes one from the compiled defaults, and shipping a
filled-in one would hand every player whatever this machine happened to be set
to.
