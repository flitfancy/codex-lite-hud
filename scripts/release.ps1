[CmdletBinding()]
param([switch]$DryRun)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Null
& (Join-Path $PSScriptRoot 'test.ps1') -SkipBuild
& (Join-Path $PSScriptRoot 'package.ps1') -SkipBuild
& (Join-Path $PSScriptRoot 'verify-release.ps1')
Write-Output 'Local release prepared. No commit, push or remote publication was performed.'
