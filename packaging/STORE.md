# What to put where

The two store pages, field by field, so neither has to be invented twice.

## Thunderstore

The upload form reads most of it out of the archive itself. Upload
`dist/HikingGPS-1.0.0-thunderstore.zip`, which already carries `manifest.json`,
`icon.png`, `CHANGELOG.md`, `LICENSE` and the page text in `README.md`.

| Field | Value |
|---|---|
| Team | must be created first; **the name is permanent** and cannot be renamed or deleted once a package is published |
| Package name | `Hiking_GPS` (from the manifest) |
| Version | `1.0.0` |
| Description | from the manifest, 121 characters |
| Categories | **Mods**, **Client Side**, **Quality Of Life**, **Tools** |
| NSFW | no |

The page body is `packaging/README.md`, rendered as markdown. Its images are
absolute `raw.githubusercontent.com` URLs, because a Thunderstore page has no
repository to resolve relative paths against — if the repository is ever renamed
again, those URLs move with it and must be updated here.

## Nexus

Nexus takes no manifest. Everything is typed into the form, and the description
is BBCode rather than markdown — `packaging/nexus-description.bbcode` is that
text, ready to paste.

| Field | Value |
|---|---|
| Name | Hiking GPS |
| Summary | A live minimap for PEAK on a handheld GPS. Real terrain drawn by the game itself, with markers for chests, campfires, statues, capybaras and everyone else on the climb. Client side — only you install it. |
| Description | paste `packaging/nexus-description.bbcode` |
| File | `dist/HikingGPS-1.0.0-nexus.zip`, extracts over the game folder |
| Version | 1.0.0 |
| Requirements | BepInEx 5 for PEAK |
| Permissions | code MIT; the GPS artwork is reserved — see `packaging/LICENSE` |

Images to upload, from `docs/media/`: `hero.png` as the main image, then
`shot-shore.jpg` and `shot-chest.jpg`. `dist/media/demo.mp4` is the same shot as
a clip if a video is wanted; the GIF is three times the size for a worse picture
and is only worth using where a video cannot go.

## Both

The archives are built by `pwsh tools/package.ps1` — on this machine,
`& '.\tools\package.ps1'` in Windows PowerShell 5.1. Neither archive carries a
config file: BepInEx writes one from the compiled defaults, and shipping a
filled-in one would hand every player whatever this machine happened to be set
to.
