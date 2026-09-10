<#
.SYNOPSIS
    Builds the mod and packs it into a zip a player can install.

.DESCRIPTION
    Produces the layout every PEAK mod loader already understands: the plugin
    under `BepInEx/plugins/`, so the zip extracts straight over the game folder
    and a mod manager finds it without being told anything.

    The zip carries the assembly and the readme, and nothing else. In
    particular it carries no configuration file: BepInEx writes one on first
    launch with the defaults compiled into the plugin, and shipping a filled-in
    one would hand every player whatever settings this machine happened to have
    at packing time.

    Nothing of PEAK's is in the package. The navigator artwork is drawn for
    this mod and built into the assembly; marker icons are photographed from
    the player's own copy of the game at runtime and never travel.

.PARAMETER Configuration
    Build configuration. Release unless you have a reason.

.PARAMETER OutputDirectory
    Where the zip is written. Defaults to `dist/` beside the repository.

.EXAMPLE
    pwsh tools/package.ps1
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'plugin\PeakMapInteractive.csproj'

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'dist' }

# The version lives in one place: the project file. Reading it back rather than
# passing it in is what stops a zip called 1.0.2 containing 1.0.1.
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw "No <Version> in $project" }

Write-Host "Hiking GPS $version" -ForegroundColor Cyan

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

& $dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$assembly = Join-Path $repo "plugin\bin\$Configuration\HikingGPS.dll"
if (-not (Test-Path $assembly)) { throw "Built nothing at $assembly" }

function New-Package {
    param([string] $Name, [string] $PluginPath, [string[]] $Extras)

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) "hikinggps-$Name-$(Get-Random)"
    $target = Join-Path $staging $PluginPath

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item $assembly $target

    foreach ($extra in $Extras) {
        $source = Join-Path $repo $extra
        if (Test-Path $source) { Copy-Item $source $staging }
        else { Write-Warning "Missing $extra" }
    }

    $zip = Join-Path $OutputDirectory "HikingGPS-$version-$Name.zip"
    if (Test-Path $zip) { Remove-Item $zip }

    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
    Remove-Item $staging -Recurse -Force

    $size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
    Write-Host "  $Name  $size MB" -ForegroundColor Green

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try { $archive.Entries | Where-Object { $_.Name } | ForEach-Object { "      $($_.FullName)" } }
    finally { $archive.Dispose() }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# Thunderstore reads the package from its root: the manifest and the icon have
# to be top-level files, and the plugin goes in a bare `plugins` folder. This
# is the one the mod managers install.
New-Package -Name 'thunderstore' -PluginPath 'plugins' -Extras @(
    'packaging\manifest.json',
    'packaging\icon.png',
    'packaging\CHANGELOG.md',
    'packaging\LICENSE',
    'packaging\README.md'
)

# Nexus and anyone installing by hand extract over the game folder instead, so
# the archive has to carry the path the file actually belongs at -- and nothing
# else. A readme or a licence at the root of this one would not go to a page or
# a folder of its own: it would land loose in the player's PEAK directory, next
# to the game's own files, with no way to tell what dropped it there.
New-Package -Name 'nexus' -PluginPath 'BepInEx\plugins\HikingGPS' -Extras @()

Write-Host "Both archives are in $OutputDirectory" -ForegroundColor Cyan
