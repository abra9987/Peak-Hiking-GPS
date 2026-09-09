<#
.SYNOPSIS
    Creates the private copy of PEAK that captures run against.

.DESCRIPTION
    Captures never touch the Steam install. The copy you play stays completely
    stock -- no mod loader, nothing that could matter when playing with other
    people, and no chance of a scheduled capture starting while you are in a
    session.

    The clone gets BepInEx, the capture plugin, and a steam_appid.txt. That last
    file matters more than it looks: without it Steamworks re-launches the game
    through Steam, which starts the *Steam* copy and hands back an exited
    process -- the clone would never run at all.

    Costs about 5 GB of disk.

.PARAMETER SteamDir
    The PEAK install to copy. Probed from the Steam library folders when omitted.

.PARAMETER CloneDir
    Where the capture copy goes.

.EXAMPLE
    pwsh -File tools/setup-clone.ps1
#>

[CmdletBinding()]
param(
    [string] $SteamDir,
    [string] $CloneDir = 'A:\PeakMapCapture\PEAK',
    [string] $BepInExVersion = '5.4.23.5'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Find-SteamPeak {
    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\PEAK',
        'C:\Program Files\Steam\steamapps\common\PEAK'
    )

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

    throw 'PEAK not found in any Steam library. Pass -SteamDir.'
}

if (-not $SteamDir) { $SteamDir = Find-SteamPeak }
Write-Host "Source: $SteamDir"
Write-Host "Clone:  $CloneDir"

if ($SteamDir -eq $CloneDir) { throw 'The clone must not be the Steam install.' }

# --- Copy the game --------------------------------------------------------

New-Item -ItemType Directory -Force -Path (Split-Path $CloneDir) | Out-Null

Write-Host 'Copying game files...'
# /MIR mirrors; BepInEx is excluded so the clone is built from a stock game
# even if the source was modded at some point.
robocopy $SteamDir $CloneDir /MIR /XD BepInEx /XF winhttp.dll doorstop_config.ini .doorstop_version /NFL /NDL /NJH /NJS /NP /MT:16 | Out-Null

# robocopy uses 0-7 for success; 8 and above are real failures.
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }

# --- Steamworks ------------------------------------------------------------

Set-Content -Path (Join-Path $CloneDir 'steam_appid.txt') -Value '3527290' -NoNewline -Encoding ascii
Write-Host 'Wrote steam_appid.txt (stops the relaunch through Steam).'

# --- BepInEx ---------------------------------------------------------------

$zipName = "BepInEx_win_x64_$BepInExVersion.zip"
$zipPath = Join-Path ([System.IO.Path]::GetTempPath()) $zipName
$url = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/$zipName"

Write-Host "Downloading BepInEx $BepInExVersion..."
Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
Expand-Archive -Path $zipPath -DestinationPath $CloneDir -Force
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path (Join-Path $CloneDir 'BepInEx\plugins\PeakMapInteractive') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $CloneDir 'BepInEx\config') | Out-Null

# --- Plugin config ---------------------------------------------------------

$config = @'
[Automation]
AutoRun = true
QuitWhenDone = true
QuietCapture = true
CaptureHotkey = F9

[Capture]
HeightResolution = 1024
AlbedoResolution = 2048
BoundsPadding = 25

[Debug]
WriteDiagnostics = true

[Output]
Directory = capture-output
'@

Set-Content -Path (Join-Path $CloneDir 'BepInEx\config\dev.peakmapinteractive.capture.cfg') -Value $config -Encoding utf8

Write-Host ''
Write-Host "Clone ready at $CloneDir"
Write-Host 'Next:'
Write-Host "  dotnet build plugin/PeakMapInteractive.csproj -c Release   # builds and installs the plugin"
Write-Host "  pwsh -File tools/capture.ps1                               # captures today's map"
Write-Host ''
Write-Host 'Set PEAKMAP_CAPTURE_DIR to this path to make it the default for the other scripts.'
