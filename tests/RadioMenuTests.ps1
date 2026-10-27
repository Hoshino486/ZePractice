#requires -Version 5.1
param([string]$TestRoot='E:\mapper\chana\临时文件夹\ze-radio-freeze-fix-20261011\radio-tests',[string]$AssetsRoot='E:\mapper\chana\临时文件夹\ze-ui-access-20261011\build\ze_practice_ui')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$installer=Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\Install-ZeClient.ps1'
$root=Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))
$game=Join-Path $root 'game'
$utf8=New-Object Text.UTF8Encoding($false)
foreach($relative in @('csgo\cfg','bin\win64')){[IO.Directory]::CreateDirectory((Join-Path $game $relative))|Out-Null}
[IO.File]::WriteAllText((Join-Path $game 'bin\win64\cs2.exe'),'non-executable fixture')
$info=Join-Path $game 'csgo\gameinfo.gi'
$originalInfo="GameInfo {`tFileSystem {`tSearchPaths {`r`n`tGame csgo`r`n`tGame core`r`n} } }"
[IO.File]::WriteAllText($info,$originalInfo,$utf8)
$keys=Join-Path $game 'csgo\cfg\ze_practice_keys.cfg'
$originalKeys='bind "F5" "existing_f5"'
[IO.File]::WriteAllText($keys,$originalKeys,$utf8)
$bind=Join-Path $game 'csgo\cfg\bind.cfg'
$originalBind="bind 0 `"exec bind.cfg`"`r`nbind enter `"exec fys.cfg`"`r`n"
[IO.File]::WriteAllText($bind,$originalBind,$utf8)
$script:checks=0
function Check([bool]$Value,[string]$Label){if(-not $Value){throw "FAIL $Label"};$script:checks++;Write-Output "PASS $Label"}
$blocked=$false
try { $null=& $installer -ClientGameRoot $game -AssetsRoot $AssetsRoot -RadioMenu } catch { $blocked=$_.Exception.Message -like 'Legacy global radio*' }
Check $blocked 'global radio installation rejected by default'
Check ([IO.File]::ReadAllText($info) -ceq $originalInfo) 'default rejection leaves gameinfo unchanged'
$first=& $installer -ClientGameRoot $game -AssetsRoot $AssetsRoot -BackupRoot (Join-Path $root 'backups') -RadioMenu -AllowLegacyGlobalMount
Check ($first.Files -eq 8) 'eight explicitly-owned files'
$radio=Join-Path $game 'csgo\ze_practice_radio\ze_practice_radio.vpk'
Check (Test-Path -LiteralPath $radio) 'isolated radio resource installed'
Check (-not (Test-Path -LiteralPath (Join-Path $game 'csgo\resource\ui\radiopanel.txt'))) 'base game resource untouched'
$infoText=[IO.File]::ReadAllText($info)
Check ($infoText.IndexOf('Game'+[char]9+'csgo/ze_practice_radio') -lt $infoText.IndexOf('Game csgo')) 'override precedes packaged base resources'
$keysText=[IO.File]::ReadAllText($keys)
Check ($keysText.Contains('bind "F9" "css_ze_ui_close;radio"')) 'F9 clears stale menu and opens native radio'
Check ($keysText.Contains('bind "0" "slot10;css_ze_ui_close;exec bind.cfg"')) 'zero exits while retaining FYS binding reload'
Check ($keysText.Contains('bind "Z" "css_ze_ui_close;radio"')) 'Z closes previous dynamic panel'
Check ($keysText.Contains('bind "F10" "radio3"')) 'F10 attaches number input without replacing the active menu'
Check (Test-Path -LiteralPath (Join-Path $game 'csgo\ze_practice_radio\ze_dynamic_hud.vpk')) 'dynamic HUD and radio adapter package installed'
Check ([IO.File]::ReadAllText($bind).StartsWith($originalBind)) 'original FYS bind commands preserved'
Check ([IO.File]::ReadAllText($bind).Contains('exec ze_practice_keys')) 'practice keys reapplied after FYS bind loader'
Check ($keysText.Contains($originalKeys)) 'unrelated F5 retained'
$package=[IO.File]::ReadAllBytes($radio)
Check ([BitConverter]::ToUInt32($package,0) -eq 1437209140 -and [BitConverter]::ToUInt32($package,4) -eq 2) 'valid VPK version 2 header'
$treeSize=[BitConverter]::ToUInt32($package,8)
$dataSize=[BitConverter]::ToUInt32($package,12)
Check ($package.Length -eq 28+$treeSize+$dataSize+48) 'complete inline archive and checksums'
$md5=[Security.Cryptography.MD5]::Create()
try {
    Check ([BitConverter]::ToString($md5.ComputeHash($package,0,$package.Length-16)) -ceq [BitConverter]::ToString($package,$package.Length-16,16)) 'whole-file checksum matches'
    Check ([BitConverter]::ToString($md5.ComputeHash($package,28,$treeSize)) -ceq [BitConverter]::ToString($package,28+$treeSize+$dataSize,16)) 'directory checksum matches'
} finally { $md5.Dispose() }
$radioText=$utf8.GetString($package,28+$treeSize,$dataSize)
Check ($radioText.Contains('当前地图 / Boss / 插件状态')) 'Chinese labels preserved'
Check ($radioText.Contains('say !ze_select 8;radio3')) 'numbered menu uses client-known say transport'
Check ($radioText.Contains('__ZE_DYNAMIC_INPUT__')) 'native input layer has its own visibility marker'
Check (($radioText -split '"hotkey"').Count -eq 41) 'four groups and thirty-six choices'
$hashBefore=(Get-FileHash -LiteralPath $info).Hash
$again=& $installer -ClientGameRoot $game -AssetsRoot $AssetsRoot -BackupRoot (Join-Path $root 'backups') -RadioMenu -AllowLegacyGlobalMount
Check ($again.Changed -eq 0) 'radio reinstall is idempotent'
Check ((Get-FileHash -LiteralPath $info).Hash -eq $hashBefore) 'search path is not duplicated'
$restored=& $installer -RestoreManifest $first.Manifest
Check ($restored.Status -eq 'Restored') 'radio rollback completed'
Check ([IO.File]::ReadAllText($info) -ceq $originalInfo) 'gameinfo exact bytes restored'
Check ([IO.File]::ReadAllText($keys) -ceq $originalKeys) 'original keys restored'
Check ([IO.File]::ReadAllText($bind) -ceq $originalBind) 'FYS bind file restored byte-for-byte'
Check (-not (Test-Path -LiteralPath $radio)) 'only installed radio file removed'
[IO.File]::WriteAllText($info,'GameInfo { FileSystem { SearchPaths {} } }',$utf8)
$rejected=$false
try{ $null=& $installer -ClientGameRoot $game -AssetsRoot $AssetsRoot -BackupRoot (Join-Path $root 'backups') -RadioMenu -AllowLegacyGlobalMount }catch{$rejected=$true}
Check $rejected 'unknown search-path layout rejects before writing'
Check ([IO.File]::ReadAllText($keys) -ceq $originalKeys) 'failed install leaves bindings untouched'
Write-Output "RADIO_INSTALL_TESTS $script:checks/$script:checks passed"
