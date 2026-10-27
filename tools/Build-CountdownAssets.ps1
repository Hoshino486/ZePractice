#requires -Version 7.0
param(
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$OutputRoot = '',
    [string]$BaselineAssets = ''
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (!$OutputRoot) { $OutputRoot = Join-Path $project 'artifacts\public-menu-markers-20261015' }
if (!$BaselineAssets) { $BaselineAssets = Join-Path $project 'artifacts\commander-luna-20261015\extra-assets' }
$addon = 'saibochana_countdown_build_20261015'
$content = Join-Path (Split-Path $ClientGameRoot) "content\csgo_addons\$addon"
$game = Join-Path $ClientGameRoot "csgo_addons\$addon"
$logs = Join-Path $OutputRoot ('compile-digits-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $content,$game,$logs | Out-Null
Set-Content (Join-Path $content 'addoninfo.txt') '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
{ IsPlayable = false }' -Encoding utf8NoBOM
Copy-Item (Join-Path $content 'addoninfo.txt') $game -Force
& py -3 (Join-Path $PSScriptRoot 'build_countdown_assets.py') $content
if ($LASTEXITCODE -ne 0) { throw 'Countdown authoring failed' }
$compiler = Join-Path $ClientGameRoot 'bin\win64\resourcecompiler.exe'
$arguments = '-game "' + (Join-Path $ClientGameRoot 'csgo') + '" -f -nop4 -v'
foreach ($n in 0..9) { $arguments += ' -i "' + (Join-Path $content "models\saibochana\commander\digits\$n.vmdl") + '"' }
$p = Start-Process -FilePath $compiler -ArgumentList $arguments -WorkingDirectory (Split-Path $compiler) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput "$logs\stdout.log" -RedirectStandardError "$logs\stderr.log"
$output = (Get-Content "$logs\stdout.log" -Raw) + (Get-Content "$logs\stderr.log" -Raw)
if ($p.ExitCode -ne 0 -or $output -match '(?im)ERROR:|failed to compile|error compiling') { throw "Digit compile failed: $logs" }
$extra = Join-Path $OutputRoot 'extra-assets'
New-Item -ItemType Directory -Force -Path $extra | Out-Null
Get-ChildItem -LiteralPath $BaselineAssets | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $extra -Recurse -Force }
foreach ($file in Get-ChildItem $game -Recurse -File -Filter '*_c') {
    $target = Join-Path $extra ([IO.Path]::GetRelativePath($game, $file.FullName))
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
foreach ($n in 0..9) { if (!(Test-Path (Join-Path $extra "models\saibochana\commander\digits\$n.vmdl_c"))) { throw "Missing digit $n" } }
# Keep the published version marker reproducible; do not rely on a manual staging edit.
$versionPath = Join-Path $extra 'resource\saibochana_server_version.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $versionPath) | Out-Null
[IO.File]::WriteAllText($versionPath, "saibochana_Server 1.5.0; opaque upright countdown glyphs; public menu; all public CS2 game/server branches.`r`n", [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $content 'countdown-geometry.json') -Destination $OutputRoot -Force
& py -3 (Join-Path $PSScriptRoot 'audit_commander_resources.py') $extra $ClientGameRoot (Join-Path $OutputRoot 'dependency-audit.json')
if ($LASTEXITCODE -ne 0) { throw 'Resource dependency closure failed' }
Write-Output "Compiled: $extra; logs: $logs"
