<#
.SYNOPSIS
    One scheduled run: capture the daily map, then publish it.

.DESCRIPTION
    capture.ps1 throws instead of publishing a snapshot that failed validation,
    so a bad day leaves the previous map up rather than replacing it with an
    empty one.
#>

[CmdletBinding()]
param(
    [int] $TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'

& "$PSScriptRoot\capture.ps1" -TimeoutMinutes $TimeoutMinutes
& "$PSScriptRoot\publish.ps1"
