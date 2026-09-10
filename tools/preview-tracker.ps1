<#
.SYNOPSIS
    Photographs the 3D device in the running game, unattended, and comes back
    with PNGs.

.DESCRIPTION
    The same bargain the icon run struck. Every way a model can arrive wrong -
    inside out, wrongly coloured, facing backwards, drawn by a shader that
    ignored half of what it was told - is invisible in the code and obvious in
    a photograph. So this makes taking the photograph cost a minute and no
    attention: it writes the capture clone's config, launches the game into a
    solo run off-screen, waits for the pictures, and copies them out.

    The game quits itself. Nothing here has to be closed by hand.

    Note that BepInEx rewrites the config file when the game exits, from what
    it held in memory - so the file is written before launching, never during,
    and the game must not already be running.

.PARAMETER PeakDir
    The capture clone. Never the copy you play.

.PARAMETER TimeoutMinutes
    How long to wait before giving up on it.

.PARAMETER Width
    Window width. The default is larger than the capture script's 640x360:
    these pictures are looked at, not measured.

.EXAMPLE
    & '.\tools\preview-tracker.ps1'
#>

[CmdletBinding()]
param(
    [string] $PeakDir = 'A:\PeakMapCapture\PEAK',
    [int]    $TimeoutMinutes = 5,
    [int]    $Width = 1280,
    [int]    $Height = 720,
    [bool]   $HideWindow = $true,
    # How large the device is built for this run, and how large the line of
    # numbers under its map is. Both are ordinary settings; they are here so a
    # size can be tried without hand-editing the clone's config between runs.
    [double] $Scale = 4.0,
    [double] $ReadoutScale = 1.0,
    # How the device is laid in a suitcase, for the luggage photographs.
    [double] $LuggageTurn = 0,
    [double] $LuggageLift = 0.06,
    [double] $LuggageAcross = 0.0,
    [double] $LuggageAlong = 0.0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

$exe = Join-Path $PeakDir 'PEAK.exe'
if (-not (Test-Path $exe)) { throw "PEAK.exe not found under '$PeakDir'." }

if (Get-Process -Name 'PEAK' -ErrorAction SilentlyContinue) {
    throw 'PEAK is already running. BepInEx would overwrite the config on exit and lose these settings.'
}

# --- Settings for this run -------------------------------------------------

# Written whole rather than patched. Only the entries that differ from the
# compiled defaults need to be here; BepInEx fills in the rest and rewrites the
# file properly on exit.
$config = Join-Path $PeakDir 'BepInEx\config\com.abra9987.hikinggps.cfg'

@"
[Automation]
AutoRun = true
QuitWhenDone = true
QuietCapture = true

[Tracker]
PreviewModel = true
Scale = $Scale
ReadoutScale = $ReadoutScale
LuggageTurn = $LuggageTurn
LuggageLift = $LuggageLift
LuggageAcross = $LuggageAcross
LuggageAlong = $LuggageAlong

[Minimap]
AutoBakeIcons = false

[Mesh]
ExportMeshes = false
"@ | Set-Content -Path $config -Encoding utf8

Write-Host "Wrote $config"

# --- Clear the last run's pictures -----------------------------------------

$shots = Join-Path $PeakDir 'capture-output\tracker'
if (Test-Path $shots) { Remove-Item (Join-Path $shots '*.png') -Force -ErrorAction SilentlyContinue }

$logPath = Join-Path $PeakDir 'BepInEx\LogOutput.log'

# --- Run -------------------------------------------------------------------

Write-Host "Launching PEAK; waiting up to $TimeoutMinutes minute(s)..."

$launchArgs = @(
    '-screen-fullscreen', '0',
    '-screen-width', $Width,
    '-screen-height', $Height,
    '-popupwindow'
)

$process = Start-Process -FilePath $exe -WorkingDirectory $PeakDir -ArgumentList $launchArgs -PassThru

Add-Type -Name Win -Namespace PeakPreview -MemberDefinition @'
[DllImport("user32.dll")]
public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
'@ -ErrorAction SilentlyContinue

foreach ($attempt in 1..40) {
    if (-not $HideWindow) { break }
    try {
        $handle = (Get-Process -Id $process.Id -ErrorAction Stop).MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
            [PeakPreview.Win]::SetWindowPos($handle, [IntPtr]::Zero, -4000, -4000, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) | Out-Null
            break
        }
    } catch { break }
    Start-Sleep -Milliseconds 500
}

# PEAK re-executes itself through Steam when launched directly: the process we
# started exits almost at once while the real game carries on. Track it by name.
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$done = $false

while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Name 'PEAK' -ErrorAction SilentlyContinue)) {
        Start-Sleep -Seconds 2
        $done = (Test-Path $shots) -and
                (@(Get-ChildItem $shots -Filter '*.png' -ErrorAction SilentlyContinue).Count -gt 0)
        break
    }

    Start-Sleep -Seconds 2
}

if (Get-Process -Name 'PEAK' -ErrorAction SilentlyContinue) {
    Write-Host 'Still running past the deadline; stopping it.'
    Stop-Process -Name 'PEAK' -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# --- Bring the results back ------------------------------------------------

$out = Join-Path $repoRoot 'capture-output\tracker'
New-Item -ItemType Directory -Force -Path $out | Out-Null

if (Test-Path $shots) {
    Copy-Item (Join-Path $shots '*.png') $out -Force -ErrorAction SilentlyContinue
}

# The log is where the shader's property list and the loaded part table land,
# and both are the point of the run as much as the pictures are.
if (Test-Path $logPath) { Copy-Item $logPath (Join-Path $out 'LogOutput.log') -Force }

$found = @(Get-ChildItem $out -Filter '*.png' -ErrorAction SilentlyContinue)
Write-Host ''
foreach ($f in $found) { Write-Host ("  {0}  {1:N0} bytes" -f $f.Name, $f.Length) }

if (-not $done -or $found.Count -eq 0) {
    Write-Host ''
    Write-Warning 'No pictures came back. The log copied next to them says why.'
    exit 1
}

Write-Host ''
Write-Host "$($found.Count) picture(s) in $out"
