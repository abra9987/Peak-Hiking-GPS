<#
.SYNOPSIS
    Runs one unattended capture: launches PEAK, waits for the plugin to write a
    snapshot, and copies the result where the web client expects it.

.DESCRIPTION
    Intended for Task Scheduler. The plugin does the work; this script only
    supervises it — starts the game, enforces a timeout, and fails loudly
    instead of silently publishing yesterday's map.

    PEAK renders through the GPU, so this needs a real desktop session. It
    cannot run under a service account with no display.

.PARAMETER PeakDir
    PEAK installation folder. Probed from the usual Steam library roots when
    omitted.

.PARAMETER TimeoutMinutes
    How long to wait for the snapshot before giving up.

.EXAMPLE
    pwsh -File tools/capture.ps1 -TimeoutMinutes 20
#>

[CmdletBinding()]
param(
    [string] $PeakDir,
    [int]    $TimeoutMinutes = 15,
    [string] $DestinationDir,
    # Moving the window off-screen keeps a scheduled capture out of sight, but
    # a compositor that considers the window invisible can stop the game
    # rendering altogether - and the capture reads rendered pixels.
    [bool]   $HideWindow = $true
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $DestinationDir) {
    $DestinationDir = Join-Path $repoRoot 'web\public\data'
}

# --- Locate the game -------------------------------------------------------

# The capture clone, never the Steam copy. See docs/AUTOMATION.md for why the
# two are kept apart: the copy you play stays stock, with no mod loader in it.
function Find-PeakDir {
    if ($env:PEAKMAP_CAPTURE_DIR -and (Test-Path $env:PEAKMAP_CAPTURE_DIR)) { return $env:PEAKMAP_CAPTURE_DIR }

    $candidates = @(
        'A:\PeakMapCapture\PEAK',
        (Join-Path $env:LOCALAPPDATA 'PeakMapCapture\PEAK')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'PEAK.exe')) { return $candidate }
    }

    throw @'
Capture clone not found.

Captures run against a private copy of PEAK so your Steam install stays stock.
Create one with tools/setup-clone.ps1, or pass -PeakDir / set PEAKMAP_CAPTURE_DIR.
'@
}

if (-not $PeakDir) { $PeakDir = Find-PeakDir }
Write-Host "Capture copy: $PeakDir"

if ($PeakDir -like '*steamapps*') {
    Write-Warning 'This points at the Steam install. Captures are meant to run against a clone; see tools/setup-clone.ps1.'
}

$exe = Join-Path $PeakDir 'PEAK.exe'
if (-not (Test-Path $exe)) { throw "PEAK.exe not found under '$PeakDir'." }

$bepinex = Join-Path $PeakDir 'BepInEx\plugins\PeakMapInteractive\PeakMapInteractive.dll'
if (-not (Test-Path $bepinex)) {
    throw "Capture plugin not installed. Build plugin/ first (it deploys itself), expected: $bepinex"
}

$snapshotDir = Join-Path $PeakDir 'capture-output\snapshot'
$manifest = Join-Path $snapshotDir 'snapshot.json'

# Remember what was there before, so a stale snapshot cannot be mistaken for a
# fresh one if the game crashes before writing.
$previousStamp = if (Test-Path $manifest) { (Get-Item $manifest).LastWriteTimeUtc } else { [datetime]::MinValue }

# --- Run -------------------------------------------------------------------

Write-Host "Capturing quietly; waiting up to $TimeoutMinutes minute(s)..."

# A tiny borderless window rather than the usual fullscreen takeover. The game
# still needs a real GPU surface -- reading pixels back from a render texture is
# the whole point, and -batchmode -nographics would leave nothing to read -- so
# "silent" means small, muted and out of the way rather than truly headless.
# The plugin mutes audio and keeps the game running while unfocused.
$launchArgs = @(
    '-screen-fullscreen', '0',
    '-screen-width', '640',
    '-screen-height', '360',
    '-popupwindow'
)

$process = Start-Process -FilePath $exe -WorkingDirectory $PeakDir -ArgumentList $launchArgs -PassThru

# Move the window off-screen once it exists, so nothing flashes up mid-session.
Add-Type -Name Win -Namespace PeakMap -MemberDefinition @'
[DllImport("user32.dll")]
public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
'@ -ErrorAction SilentlyContinue

$moved = -not $HideWindow
foreach ($attempt in 1..40) {
    if (-not $HideWindow) { break }
    try {
        $handle = (Get-Process -Id $process.Id -ErrorAction Stop).MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
            [PeakMap.Win]::SetWindowPos($handle, [IntPtr]::Zero, -4000, -4000, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) | Out-Null
            $moved = $true
            break
        }
    } catch { break }
    Start-Sleep -Milliseconds 500
}

if (-not $moved) { Write-Host 'Could not move the window off-screen; capture continues regardless.' }

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$captured = $false

while ((Get-Date) -lt $deadline) {
    if (Test-Path $manifest) {
        $stamp = (Get-Item $manifest).LastWriteTimeUtc
        if ($stamp -gt $previousStamp) {
            # The plugin writes the manifest last, but give the filesystem a
            # moment to flush before reading it.
            Start-Sleep -Seconds 3
            $captured = $true
            break
        }
    }

    # Launched directly, PEAK re-executes itself through Steam: the process we
    # started exits 0 almost immediately while the real game keeps running.
    # Track the executable by name, not by the handle we were handed.
    if ($process.HasExited -and -not (Get-Process -Name 'PEAK' -ErrorAction SilentlyContinue)) {
        Start-Sleep -Seconds 3
        if (Test-Path $manifest) {
            $stamp = (Get-Item $manifest).LastWriteTimeUtc
            if ($stamp -gt $previousStamp) { $captured = $true; break }
        }
        throw "No PEAK process is running and no snapshot was written. Check BepInEx\LogOutput.log."
    }

    Start-Sleep -Seconds 5
}

$running = Get-Process -Name 'PEAK' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Game still running; closing it.'
    $running | Stop-Process -Force
}

if (-not $captured) {
    throw "Timed out after $TimeoutMinutes minute(s) with no new snapshot."
}

# --- Validate before publishing -------------------------------------------

$snapshot = Get-Content $manifest -Raw | ConvertFrom-Json

if ($snapshot.schemaVersion -ne 1) {
    throw "Snapshot schema v$($snapshot.schemaVersion) is not the v1 this toolchain expects."
}
if ($snapshot.segments.Count -eq 0) {
    throw 'Snapshot contains no segments; refusing to publish.'
}

# Coverage is the share of a squared frame that is solid ground, and a mountain
# does not fill a square: single-digit percentages are normal for the tall,
# narrow upper segments. Only a near-empty result means the capture ran before
# the biome finished streaming in.
$totalMarkers = 0
foreach ($segment in $snapshot.segments) {
    $totalMarkers += $segment.markers.Count
    $relief = $segment.terrain.heightMax - $segment.terrain.heightMin

    if ($segment.terrain.coverage -lt 0.02) {
        throw "Segment $($segment.index) ($($segment.biome)) measured almost no ground ($([math]::Round($segment.terrain.coverage * 100, 1))%); refusing to publish."
    }
    if ($relief -lt 20) {
        throw "Segment $($segment.index) ($($segment.biome)) is flat ($([math]::Round($relief, 1)) m of relief); refusing to publish."
    }
}

if ($totalMarkers -eq 0) {
    throw 'Snapshot contains no markers at all; refusing to publish.'
}

Write-Host "Captured $($snapshot.segments.Count) segment(s), $totalMarkers markers, at $($snapshot.generatedAt)."
Write-Host "Map: $($snapshot.map.sceneName), levelIndex $($snapshot.map.levelIndex) (pool $($snapshot.map.poolIndex)/$($snapshot.map.scenePoolSize), source $($snapshot.map.indexSource))."

# --- Publish locally -------------------------------------------------------

New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
Get-ChildItem -Path $DestinationDir -File | Remove-Item -Force
Copy-Item -Path (Join-Path $snapshotDir '*') -Destination $DestinationDir -Force

$total = (Get-ChildItem $DestinationDir -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Snapshot copied to {0} ({1:N1} MB)." -f $DestinationDir, ($total / 1MB))
