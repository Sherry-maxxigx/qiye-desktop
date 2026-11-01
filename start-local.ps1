param([string]$ArchiveCache='', [switch]$SkipSystemComponents)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'scripts\Initialize-Environment.ps1') -ArchiveCache $ArchiveCache -SkipSystemComponents:$SkipSystemComponents
& (Join-Path $PSScriptRoot 'scripts\Start-Services.ps1')
