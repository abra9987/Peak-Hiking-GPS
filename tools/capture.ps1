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
    [string] $DestinationDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $DestinationDir) {
    $DestinationDir = Join-Path $repoRoot 'web\public\data'
}

# --- Locate the game -------------------------------------------------------

function Find-PeakDir {
    if ($env:PEAK_DIR -and (Test-Path $env:PEAK_DIR)) { return $env:PEAK_DIR }

    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\PEAK',
        'C:\Program Files\Steam\steamapps\common\PEAK'
    )

    # Any additional Steam libraries the client knows about.
    $vdf = 'C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($match in ([regex]'"path"\s+"([^"]+)"').Matches((Get-Content $vdf -Raw))) {
            $path = $match.Groups[1].Value -replace '\\\\', '\'
            $candidates += (Join-Path $path 'steamapps\common\PEAK')
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'PEAK.exe')) { return $candidate }
    }

    throw 'PEAK installation not found. Pass -PeakDir or set PEAK_DIR.'
}

if (-not $PeakDir) { $PeakDir = Find-PeakDir }
Write-Host "PEAK:        $PeakDir"

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

Write-Host "Launching PEAK; waiting up to $TimeoutMinutes minute(s)..."
$process = Start-Process -FilePath $exe -WorkingDirectory $PeakDir -PassThru

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

    if ($process.HasExited -and -not $captured) {
        Start-Sleep -Seconds 3
        if (Test-Path $manifest) {
            $stamp = (Get-Item $manifest).LastWriteTimeUtc
            if ($stamp -gt $previousStamp) { $captured = $true; break }
        }
        throw "PEAK exited (code $($process.ExitCode)) without writing a snapshot. Check BepInEx\LogOutput.log."
    }

    Start-Sleep -Seconds 5
}

if (-not $process.HasExited) {
    Write-Host 'Game still running; closing it.'
    $process | Stop-Process -Force
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

foreach ($segment in $snapshot.segments) {
    # A segment that measured almost nothing means the capture ran before the
    # biome finished streaming in. Publishing it would look like a bad map day.
    if ($segment.terrain.coverage -lt 0.25) {
        throw "Segment $($segment.index) ($($segment.biome)) has only $([math]::Round($segment.terrain.coverage * 100, 1))% terrain coverage; refusing to publish."
    }
}

Write-Host "Captured $($snapshot.segments.Count) segment(s) at $($snapshot.generatedAt)."

# --- Publish locally -------------------------------------------------------

New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
Get-ChildItem -Path $DestinationDir -File | Remove-Item -Force
Copy-Item -Path (Join-Path $snapshotDir '*') -Destination $DestinationDir -Force

$total = (Get-ChildItem $DestinationDir -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Snapshot copied to {0} ({1:N1} MB)." -f $DestinationDir, ($total / 1MB))
