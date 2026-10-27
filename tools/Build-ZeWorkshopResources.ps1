#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Validate','Build','Verify')][string]$Action = 'Build',
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$OutputRoot = '',
    [string]$ExtraAssetsRoot = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $project 'artifacts\workshop-resources' }
& (Join-Path $PSScriptRoot 'Build-ZePanel.ps1') -Action $Action -ClientGameRoot $ClientGameRoot -TemporaryRoot $OutputRoot -ProjectRoot $project -WorkshopBundle -ExtraAssetsRoot $ExtraAssetsRoot
