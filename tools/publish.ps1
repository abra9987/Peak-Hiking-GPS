<#
.SYNOPSIS
    Builds the site with the current snapshot and publishes it to GitHub Pages
    as a branch that never accumulates history.

.DESCRIPTION
    The published branch is rebuilt from scratch every time and force-pushed as
    a single commit, so the repository never carries yesterday's map data.

    This matters more than it sounds. The project this one improves on commits
    ~38 MB of JPEG into main every day; after 87 commits its repository is
    2.05 GB, and every clone pays for every map that has ever existed. Binary
    snapshots have no useful diff and nobody wants them back, so keeping them
    in history buys nothing at all.

    Source history lives on `main` and is untouched by this script.

.PARAMETER Branch
    Branch to publish to. Must be a branch you are happy to have rewritten.

.PARAMETER Remote
    Git remote to push to.

.PARAMETER DryRun
    Build and stage everything, but stop before pushing.

.EXAMPLE
    pwsh -File tools/publish.ps1 -DryRun
#>

[CmdletBinding()]
param(
    [string] $Branch = 'gh-pages',
    [string] $Remote = 'origin',
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$webDir = Join-Path $repoRoot 'web'
$dataDir = Join-Path $webDir 'public\data'
$distDir = Join-Path $webDir 'dist'

if (-not (Test-Path (Join-Path $dataDir 'snapshot.json'))) {
    throw "No snapshot in $dataDir. Run tools/capture.ps1 first."
}

$snapshot = Get-Content (Join-Path $dataDir 'snapshot.json') -Raw | ConvertFrom-Json
Write-Host "Publishing snapshot from $($snapshot.generatedAt) ($($snapshot.segments.Count) segments)."

# --- Build -----------------------------------------------------------------

Push-Location $webDir
try {
    if (-not (Test-Path 'node_modules')) {
        Write-Host 'Installing dependencies...'
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }

    Write-Host 'Building...'
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
finally {
    Pop-Location
}

if (-not (Test-Path (Join-Path $distDir 'index.html'))) {
    throw "Build produced no index.html in $distDir."
}

# GitHub Pages would otherwise run the output through Jekyll, which drops
# files and folders beginning with an underscore.
New-Item -ItemType File -Force -Path (Join-Path $distDir '.nojekyll') | Out-Null

$size = (Get-ChildItem $distDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Site is {0:N1} MB." -f ($size / 1MB))

if ($DryRun) {
    Write-Host "Dry run: built but not pushed. Output is in $distDir."
    return
}

# --- Publish ---------------------------------------------------------------

Push-Location $repoRoot
try {
    $url = git remote get-url $Remote 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $url) {
        throw "Remote '$Remote' is not configured. Add it with: git remote add $Remote <url>"
    }
}
finally {
    Pop-Location
}

# A throwaway repository, so the published branch is exactly one commit deep
# no matter how many times this has run before.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("peakmap-publish-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item -Path $distDir -Destination $staging -Recurse

Push-Location $staging
try {
    git init --quiet --initial-branch=$Branch
    git add --all

    $message = "Snapshot $($snapshot.generatedAt)"
    git -c user.name='peak-map-publisher' -c user.email='publisher@localhost' commit --quiet -m $message
    if ($LASTEXITCODE -ne 0) { throw 'Nothing to commit.' }

    Write-Host "Force-pushing to $Remote/$Branch ..."
    git push --force --quiet $url "${Branch}:${Branch}"
    if ($LASTEXITCODE -ne 0) { throw 'Push failed.' }

    Write-Host "Published: $message"
}
finally {
    Pop-Location
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
