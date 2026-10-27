#requires -Version 5.1
param(
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$OutputRoot = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $project 'artifacts\workshop-resources' }
$builder = Join-Path $project 'tools\Build-ZeWorkshopResources.ps1'
$extra = Join-Path $OutputRoot ('test-input-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $extra 'resource\zepractice\build-fixture.txt'
[IO.Directory]::CreateDirectory((Split-Path -Parent $fixture)) | Out-Null
[IO.File]::WriteAllText($fixture, 'fixture asset')
$script:checks = 0
function Check([bool]$Value, [string]$Label) { if (-not $Value) { throw ('FAIL ' + $Label) }; $script:checks++; Write-Output ('PASS ' + $Label) }
& $builder -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot -ExtraAssetsRoot $extra
$manifestPath = Join-Path $OutputRoot 'build\asset-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check (@($manifest.Files).Count -eq 11) 'additional compiled/static assets enter package'
$publishFixture = Join-Path $manifest.PublishAddonDirectory 'resource\zepractice\build-fixture.txt'
Check (Test-Path -LiteralPath $publishFixture) 'publisher addon includes extra assets'
& $builder -Action Verify -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot -ExtraAssetsRoot $extra
$vpk = [IO.File]::ReadAllBytes($manifest.Vpk)
$modified = [byte[]]$vpk.Clone(); $modified[$modified.Length-1] = $modified[$modified.Length-1] -bxor 1
[IO.File]::WriteAllBytes($manifest.Vpk, $modified)
$rejected = $false
try { & $builder -Action Verify -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot -ExtraAssetsRoot $extra } catch { $rejected = $true }
finally { [IO.File]::WriteAllBytes($manifest.Vpk, $vpk) }
Check $rejected 'changed VPK rejected by manifest hash'
$conflict = Join-Path $extra 'resource\ui\radiopanel.txt'
[IO.Directory]::CreateDirectory((Split-Path -Parent $conflict)) | Out-Null
[IO.File]::WriteAllText($conflict, 'conflicting asset')
$rejected = $false
try { & $builder -Action Validate -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot -ExtraAssetsRoot $extra } catch { $rejected = $true }
Check $rejected 'reserved UI path collision rejected'
Remove-Item -LiteralPath $conflict
$executable = Join-Path $extra 'resource\unexpected.dll'
[IO.File]::WriteAllText($executable, 'not an asset')
$rejected = $false
try { & $builder -Action Validate -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot -ExtraAssetsRoot $extra } catch { $rejected = $true }
Check $rejected 'executables excluded from resource pack'
Remove-Item -LiteralPath $executable
$forbidden = Join-Path $extra 'panorama\layout\hud\forbidden.vxml_c'
[IO.Directory]::CreateDirectory((Split-Path -Parent $forbidden)) | Out-Null
[IO.File]::WriteAllText($forbidden, 'not a valid workshop layout')
$rejected = $false
try { & $builder -Action Validate -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot -ExtraAssetsRoot $extra } catch { $rejected = $_.Exception.Message -like 'Workshop addons may only add Panorama layouts*' }
Check $rejected 'native HUD layouts rejected before compilation'
Remove-Item -LiteralPath $forbidden
& $builder -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot
& $builder -Action Verify -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check (@($manifest.Files).Count -eq 10) 'final package contains ten workshop-compatible assets'
Check (@($manifest.Files | Where-Object { $_.Path -match '^panorama\\layout\\(?!custom_game\\)' }).Count -eq 0) 'all packaged layouts live in custom_game'
Check (@($manifest.Files | Where-Object { $_.Path -match 'radio_filter|hudradio' }).Count -eq 0) 'native HUD override and dependent filter excluded'
$addon = $manifest.PublishAddonDirectory
$content = Join-Path (Split-Path -Parent $ClientGameRoot) 'content\csgo_addons\saibochana_Server'
Check (-not (Test-Path -LiteralPath (Join-Path $addon 'panorama\layout\hud\hudradio.vxml_c'))) 'publisher folder has no retired HUD layout'
Check (-not (Test-Path -LiteralPath (Join-Path $content 'panorama\layout\hud\hudradio.vxml'))) 'editor source cannot recompile the retired HUD override'
Check (-not (Test-Path -LiteralPath (Join-Path $content 'panorama\scripts\custom_game\zepractice\radio_filter.vjs'))) 'retired filter source removed from workshop addon'
$radio = [IO.File]::ReadAllText((Join-Path $addon 'resource\ui\radiopanel.txt'))
Check (-not $radio.Contains('__ZE_DYNAMIC_INPUT__')) 'visible native input table has a player-facing title'
Check ($radio.Contains('"cmd" "say !ze_select 9"')) 'nine closes without reopening a native menu'
Check (-not [regex]::IsMatch($radio,'"cmd"\s+"(?:[^";]+;)*css_')) 'Workshop never invokes unknown client-side css commands'
$style = [IO.File]::ReadAllText((Join-Path $content 'panorama\styles\custom_game\zepractice\radio.vcss'))
Check ($style.Contains('horizontal-align: right;')) 'dynamic options do not overlap native radio inputs'
$stale = Join-Path $addon ('panorama\layout\hud\forbidden-' + [Guid]::NewGuid().ToString('N') + '.vxml_c')
[IO.Directory]::CreateDirectory((Split-Path -Parent $stale)) | Out-Null
[IO.File]::WriteAllText($stale, 'stale fixture')
$rejected = $false
try { & $builder -Action Verify -ClientGameRoot $ClientGameRoot -OutputRoot $OutputRoot } catch { $rejected = $_.Exception.Message -like 'Workshop addons may only add Panorama layouts*' }
finally { Remove-Item -LiteralPath $stale }
Check $rejected 'verification rejects forbidden residue outside the VPK manifest'
Check (-not (Test-Path -LiteralPath $publishFixture)) 'retired asset removed from publisher folder'
Check (-not (Test-Path -LiteralPath (Join-Path $OutputRoot 'build\saibochana_Server\resource\zepractice\build-fixture.txt'))) 'retired asset removed from staging folder'
Write-Output ('WORKSHOP_BUNDLE_TESTS ' + $script:checks + '/' + $script:checks + ' passed')
