param([switch]$CheckOnly)
$ErrorActionPreference='Stop'
$installHome=Split-Path -Parent $PSScriptRoot
# Windows PowerShell 5.1 defaults to ANSI for BOM-less files; read JSON explicitly as UTF-8.
try {
 $settings=Get-Content -LiteralPath (Join-Path $installHome 'server-settings.json') -Raw -Encoding UTF8|ConvertFrom-Json -ErrorAction Stop
} catch {
 throw 'Failed to read server-settings.json as UTF-8 JSON. Check JSON quotes and escape Windows path backslashes (\\). Configuration values are not printed.'
}
$game=[IO.Path]::GetFullPath([string]$settings.GameRoot)
$exe=Join-Path $game 'bin\win64\cs2.exe'
if(Get-CimInstance Win32_Process -Filter "Name='cs2.exe'"| Where-Object ExecutablePath -eq $exe){throw 'Stop the ZE practice server before updating configurations.'}
$tempRoot=[IO.Path]::GetFullPath([string]$settings.DiagnosticsRoot)
$session=Join-Path $tempRoot ('fys-update-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($session)|Out-Null
$archive=Join-Path $session 'Hoshino486-cs2-master.zip'
Invoke-WebRequest -UseBasicParsing -Uri 'https://codeload.github.com/Hoshino486/cs2/zip/refs/heads/master' -OutFile $archive -TimeoutSec 180
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $session 'upstream')
$source=Join-Path $session 'upstream\cs2-master'
if(-not(Test-Path -LiteralPath (Join-Path $source '2001\sharp\configs\bosses'))){throw 'FYS layout changed; installed snapshot untouched.'}
$tool=Join-Path $PSScriptRoot 'FysConfigTool\FysConfigTool.csproj'
$staging=Join-Path $session 'staging'
$converted=Join-Path $session 'converted-stripper'
& dotnet run --project $tool -c Release -- import $source $staging 2001 $converted Hoshino486/cs2
if($LASTEXITCODE -ne 0){throw 'FYS adapter validation failed; installed snapshot untouched.'}
$manifest=Get-Content -LiteralPath (Join-Path $staging 'import-manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.rewardsIncluded -or $manifest.originalFiles.'2001/sharp/configs/bosses' -lt 1 -or $manifest.originalFiles.'2001/sharp/configs/translations' -lt 1){throw 'Missing expected FYS data; installed snapshot untouched.'}
if($CheckOnly){Write-Host ('Validated '+$manifest.originalFiles.'2001/csgo/cfg/map-configs'+' map cfgs, '+$manifest.originalFiles.'2001/sharp/configs/bosses'+' boss cfgs, '+$manifest.originalFiles.'2001/sharp/configs/translations'+' translations. Files under '+$session);exit 0}
$pluginDir=Join-Path $game 'csgo\addons\counterstrikesharp\plugins\ZePractice'
$targetName='Hoshino486-cs2-'+(Get-Date -Format 'yyyyMMdd-HHmmss')
$target=Join-Path $pluginDir $targetName
$serverHome=Split-Path -Parent $installHome
$deployment=Join-Path $PSScriptRoot 'Deploy-Tracked.ps1'
& $deployment -Source $staging -Destination $target -ServerHome $serverHome
$stripperTarget=Join-Path $game 'csgo\addons\StripperCS2'
$stripperBackup=Join-Path $session 'stripper-before'
[IO.Directory]::CreateDirectory($stripperBackup)|Out-Null
foreach($item in Get-ChildItem -LiteralPath $converted -File){$prior=Join-Path $stripperTarget $item.Name;if(Test-Path -LiteralPath $prior){Copy-Item -LiteralPath $prior -Destination (Join-Path $stripperBackup $item.Name)}}
& $deployment -Source $converted -Destination $stripperTarget -ServerHome $serverHome
$configPath=Join-Path $game 'csgo\addons\counterstrikesharp\configs\plugins\ZePractice\ZePractice.json'
$config=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json
$configBackup=Join-Path $session 'ZePractice.before.json';Copy-Item -LiteralPath $configPath -Destination $configBackup
$config.FysRoot=$targetName
$updatedDirectory=Join-Path $session 'updated-config'
[IO.Directory]::CreateDirectory($updatedDirectory)|Out-Null
$updatedPath=Join-Path $updatedDirectory 'ZePractice.json'
[IO.File]::WriteAllText($updatedPath,($config|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
& $deployment -Source $updatedPath -Destination (Split-Path -Parent $configPath) -ServerHome $serverHome
Write-Host ('Hoshino486/cs2 map/Boss/translation snapshot: '+$target)
Write-Host ('Existing configs backup: '+$session)
Write-Host 'Reload the map/server to apply Stripper entity changes; no reward files were imported.'