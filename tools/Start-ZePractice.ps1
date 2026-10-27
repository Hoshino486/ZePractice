param([string]$Map='', [string]$WorkshopId='', [switch]$LocalMap, [switch]$PrepareOnly, [switch]$Hidden, [switch]$NoWait)
$ErrorActionPreference='Stop'
$installHome=Split-Path -Parent $PSScriptRoot
# +exec alone does not change the configuration loaded by the engine on each map.
$serverConfig='ze-practice/server.cfg'
# The practice profile must not depend on the legacy cfg/server.cfg for RCON.
# Keep the password out of process arguments, source control and startup output.
# Execute this private config only BEFORE the first map: Workshop exec forbids rcon_password.
function Assert-ZeRconPassword([string]$Password){
 if($Password -notmatch '\A[^\s;"\\\p{Cc}]{1,100}\z'){
  throw 'Invalid RCON password in server-settings.json. Use 1-100 non-whitespace characters without quotes, semicolons, backslashes or control characters. Values are not printed.'
 }
}
function Write-ZeRconConfig([string]$Path,[string]$Password){
 Assert-ZeRconPassword $Password
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))|Out-Null
 $content='// Generated from ZePractice/server-settings.json. Do not share or edit this file.'+"`r`n"+'rcon_password "'+$Password+'"'+"`r`n"
 [IO.File]::WriteAllText($Path,$content,[Text.UTF8Encoding]::new($false))
}

function Read-SharedStartupLog([string]$Path){
 $stream=$null; $reader=$null
 try {
  $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
  $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true)
  return $reader.ReadToEnd()
 } catch [IO.IOException] {
  return ''
 } finally {
  if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}
 }
}

function Get-ZeStartupFailure([bool]$Exited,$ExitCode,[bool]$MapActive,[int]$RconAttempts,[int]$ServerProcessId,[string]$LogPath){
 if($Exited){
  $code=if($null -eq $ExitCode){'unavailable'}else{[string]$ExitCode}
  return [pscustomobject]@{Kind='ProcessExited';Message=('Dedicated server exited before Workshop startup completed. PID: '+$ServerProcessId+'; exit code: '+$code+'. Inspect engine/plugin startup errors in '+$LogPath)}
 }
 if(-not $MapActive){
  return [pscustomobject]@{Kind='BootstrapTimeout';Message=('Dedicated server is still running, but the bootstrap map did not become active within 90 seconds. PID: '+$ServerProcessId+'. Log: '+$LogPath+'. Do not start a second server.')}
 }
 return [pscustomobject]@{Kind='RconUnavailable';Message=('Bootstrap map is active, but Workshop command delivery over RCON was not confirmed after '+$RconAttempts+' attempts. PID: '+$ServerProcessId+'. Log: '+$LogPath+'. Check RCON reachability/settings; do not start a second server.')}
}

# Windows PowerShell 5.1 defaults to ANSI for BOM-less files; read JSON explicitly as UTF-8.
try {
 $settings=Get-Content -LiteralPath (Join-Path $installHome 'server-settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
} catch {
 throw 'Failed to read server-settings.json as UTF-8 JSON. Check JSON quotes and escape Windows path backslashes (\\). Configuration values are not printed.'
}
$game=[IO.Path]::GetFullPath([string]$settings.GameRoot)
$bin=Join-Path $game 'bin\win64'
$exe=Join-Path $bin 'cs2.exe'
# An explicit different -Map keeps the local-map path; -LocalMap opts out for development.
if(-not $Map){$Map=[string]$settings.StartupMap}
if($LocalMap -and $WorkshopId){throw 'Use either -WorkshopId or -LocalMap, not both.'}
if(-not $LocalMap -and -not $PSBoundParameters.ContainsKey('WorkshopId') -and $Map -eq [string]$settings.StartupMap){
 $WorkshopId=[string]$settings.StartupWorkshopId
}
$workshopMode=-not [string]::IsNullOrWhiteSpace($WorkshopId) -and -not $LocalMap
if($workshopMode){
 [UInt64]$parsedWorkshopId=0
 if($WorkshopId -notmatch '^[1-9][0-9]{0,19}$' -or -not [UInt64]::TryParse($WorkshopId,[ref]$parsedWorkshopId)){throw 'Invalid Workshop ID. Use the numeric id from the Steam Workshop URL.'}
}
$mapCommand=if($workshopMode){'host_workshop_map '+$WorkshopId}else{'map '+$Map+' nomapvalidation true'}
$initialMapCommand=if($workshopMode){'map de_dust2'}else{$mapCommand}
if($Map -notmatch '^[a-zA-Z0-9_][a-zA-Z0-9_-]{0,127}$'){throw 'Invalid map name.'}
$address=$null
if(-not [Net.IPAddress]::TryParse([string]$settings.BindAddress,[ref]$address)){throw 'BindAddress must be an IP address.'}
$addons=@($settings.AssetMounts | Where-Object {$_ -like 'csgo_addons/*'} | ForEach-Object {$_ -replace '^csgo_addons/',''})
$addonArg=if(-not $workshopMode -and $addons.Count -gt 0){'-addon '+$addons[0]}else{''}
$port=[int]$settings.Port
if($port -lt 1024 -or $port -gt 65535){throw 'Invalid server port.'}
if(Get-CimInstance Win32_Process -Filter "Name='cs2.exe'"|Where-Object ExecutablePath -eq $exe){throw 'This dedicated server is already running.'}
$profileCfg=Join-Path $game ('csgo\cfg\'+($serverConfig -replace '/','\'))
if(-not(Test-Path -LiteralPath $profileCfg -PathType Leaf)){throw 'Missing dedicated practice configuration: csgo/cfg/ze-practice/server.cfg. No fallback to legacy server.cfg is allowed.'}
Assert-ZeRconPassword ([string]$settings.RconPassword)
$utf8=[Text.UTF8Encoding]::new($false)
$tempRoot=[IO.Path]::GetFullPath([string]$settings.DiagnosticsRoot)
$session=Join-Path $tempRoot ('runtime\'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($session)|Out-Null
$env:TEMP=$session; $env:TMP=$session
$gi=Join-Path $game 'csgo\gameinfo.gi'; $si=Join-Path $game 'csgo\steam.inf'
$versionHash=(Get-FileHash -LiteralPath $si -Algorithm SHA256).Hash
$state=Join-Path $tempRoot 'version-state'; [IO.Directory]::CreateDirectory($state)|Out-Null
$cache=Join-Path $state ('gameinfo-'+$versionHash+'.gi')
function Test-GameInfo([string]$text){
 if($text -notmatch '\bSearchPaths\b' -or $text -notmatch '\bGameInfo\b'){return $false}
 foreach($m in [regex]::Matches($text,'(?m)^\s*"?LayeredOnMod"?\s+"?([\w.-]+)"?')){
  if(-not(Test-Path -LiteralPath (Join-Path $game ($m.Groups[1].Value+'\gameinfo.gi')))){return $false}
 }
 return $true
}
$text=[IO.File]::ReadAllText($gi)
if(-not(Test-GameInfo $text)){
 if((Test-Path -LiteralPath $cache) -and (Test-GameInfo ([IO.File]::ReadAllText($cache)))){
  Copy-Item -LiteralPath $gi -Destination (Join-Path $session 'gameinfo.invalid.gi')
  $text=[IO.File]::ReadAllText($cache)
 }else{throw 'Invalid gameinfo.gi for this engine version. Run the original SteamCMD updater first; do not restore an old gameinfo.gi.'}
}
$before=[IO.File]::ReadAllText($gi)
# Do not let an old unpacked development addon override downloaded Workshop resources.
# Only detach exact addon search paths owned by this launcher's AssetMounts list.
if($workshopMode){
 foreach($mount in @($settings.AssetMounts)){
  if($mount -notmatch '^csgo_addons/[a-zA-Z0-9_-]+$'){throw 'Invalid asset mount in settings.'}
  $pattern='(?m)^[\t ]*Game[\t ]+"?'+[regex]::Escape($mount)+'"?[\t ]*\r?\n'
  $text=[regex]::Replace($text,$pattern,'')
 }
}
$mounts=@('csgo/addons/metamod')
if(-not $workshopMode){$mounts+=@($settings.AssetMounts)}
foreach($mount in $mounts){
 if($mount -notmatch '^(csgo/addons/metamod|csgo_addons/[a-zA-Z0-9_-]+)$'){throw ('Invalid asset mount: '+$mount)}
 if(-not(Test-Path -LiteralPath (Join-Path $game $mount))){throw ('Missing mount directory: '+$mount)}
 $pattern='(?m)^[\t ]*Game[\t ]+"?'+[regex]::Escape($mount)+'"?[\t ]*\r?$'
 if($text -notmatch $pattern){
  $matches=[regex]::Matches($text,'(?m)^([\t ]*)Game[\t ]+"?csgo"?[\t ]*\r?$')
  if($matches.Count -ne 1){throw 'Expected exactly one Game csgo search path. No blind replacement performed.'}
  $newline=if($text.Contains("`r`n")){"`r`n"}else{"`n"}
  $text=$text.Insert($matches[0].Index,($matches[0].Groups[1].Value+"Game`t"+$mount+$newline))
 }
}
if($text -ne $before){
 Copy-Item -LiteralPath $gi -Destination (Join-Path $session 'gameinfo.before.gi')
 [IO.File]::WriteAllText($gi,$text,$utf8)
 Write-Host 'Updated managed search paths only; the rest of gameinfo.gi was preserved.'
}
foreach($required in @('csgo\addons\metamod\bin\win64\server.dll','csgo\addons\counterstrikesharp\bin\win64\counterstrikesharp.dll','csgo\addons\counterstrikesharp\plugins\ZePractice\ZePractice.dll')){
 if(-not(Test-Path -LiteralPath (Join-Path $game $required))){throw ('Missing plugin dependency: '+$required)}
}
# Dedicated-server map validation rejects local custom maps unless explicitly bypassed;
# the VPK must also be visible from game/csgo/maps, not just csgo_addons/<name>/maps.
$mapsDir=Join-Path $game 'csgo\maps'
if(-not $workshopMode){
foreach($addon in $addons){
 foreach($suffix in @('.vpk','_skybox.vpk')){
  $fileName=$Map+$suffix
  $sourceVpk=Join-Path $game ('csgo_addons\'+$addon+'\maps\'+$fileName)
  if(-not(Test-Path -LiteralPath $sourceVpk -PathType Leaf)){continue}
  $targetVpk=Join-Path $mapsDir $fileName
  if((Test-Path -LiteralPath $targetVpk -PathType Leaf) -and (Get-FileHash -LiteralPath $sourceVpk).Hash -eq (Get-FileHash -LiteralPath $targetVpk).Hash){continue}
  & (Join-Path $PSScriptRoot 'Deploy-Tracked.ps1') -Source $sourceVpk -Destination $mapsDir -ServerHome (Split-Path -Parent $installHome)
  if($LASTEXITCODE -ne 0){throw ('Failed to stage custom map '+$fileName)}
 }
}
}
Write-Host ('ZE Practice - '+$Map+' - '+$settings.BindAddress+':'+$port)
Write-Host ('Map load: '+$mapCommand)
Write-Host ('Server config: '+$serverConfig)
Write-Host ('Logs: '+$session)
if($workshopMode){Write-Host 'Workshop bootstrap: de_dust2, then switch after Steam/RCON initialization.'}
if($PrepareOnly){Write-Host 'Preflight OK.';exit 0}
$rconHelper=Join-Path $PSScriptRoot 'Invoke-ZeRcon.ps1'
if($workshopMode -and -not (Test-Path -LiteralPath $rconHelper)){throw 'Missing Workshop startup RCON helper.'}
$log=Join-Path $session 'console.log'
$nativeLog=Join-Path $bin 'ze-practice-console.log'
if(Test-Path -LiteralPath $nativeLog){Move-Item -LiteralPath $nativeLog -Destination (Join-Path $session 'previous-console.log')}
[IO.File]::WriteAllText($log,'',$utf8)
New-Item -ItemType HardLink -Path $nativeLog -Value $log|Out-Null
Write-ZeRconConfig -Path (Join-Path $game 'csgo\cfg\ze-practice\rcon.generated.cfg') -Password ([string]$settings.RconPassword)
$arguments='-dedicated -insecure -console -usercon '+$addonArg+' -consolelog_append -con_logfile "'+$nativeLog+'" -ip '+$settings.BindAddress+' -port '+$port+' +servercfgfile '+$serverConfig+' +game_alias casual +exec ze-practice/rcon.generated.cfg +exec '+$serverConfig+' +'+$initialMapCommand
$processArgs=@{FilePath=$exe;WorkingDirectory=$bin;ArgumentList=$arguments;PassThru=$true}
if($Hidden){$processArgs.WindowStyle='Hidden'}
# Interactive startup deliberately keeps native console handles. Redirection makes some CS2 console windows blank.
$process=Start-Process @processArgs
# Retain the process handle so Windows PowerShell can still read the real exit code after an early crash.
$null=$process.Handle
$consoleHelper=Join-Path $PSScriptRoot 'Disable-ZeConsoleQuickEdit.ps1'
if(Test-Path -LiteralPath $consoleHelper){
 $helperArgs='-NoProfile -ExecutionPolicy Bypass -File "'+$consoleHelper+'" -ServerProcessId '+$process.Id+' -ReportPath "'+(Join-Path $session 'console-input-mode.json')+'"'
 $helper=Start-Process -FilePath (Get-Command powershell.exe).Source -ArgumentList $helperArgs -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $session 'console-helper.log') -RedirectStandardError (Join-Path $session 'console-input-mode-error.log')
 if($helper.ExitCode -ne 0){Write-Warning 'Dedicated console Quick Edit setup did not complete; avoid selecting text in the server window.'}
}
$record=[ordered]@{StartedAt=(Get-Date).ToString('o');ProcessId=$process.Id;Map=$Map;WorkshopId=$(if($workshopMode){$WorkshopId}else{$null});MapLoadCommand=$mapCommand;ServerConfig=$serverConfig;BootstrapMap=$(if($workshopMode){'de_dust2'}else{$null});Port=$port;BindAddress=$settings.BindAddress;ConsoleLog=$log;SteamInfoSHA256=$versionHash;GameinfoSHA256=(Get-FileHash -LiteralPath $gi).Hash}
$runFile=Join-Path $session 'run.json'
[IO.File]::WriteAllText($runFile,($record|ConvertTo-Json),$utf8)
[IO.File]::WriteAllText((Join-Path $installHome 'last-run.json'),($record|ConvertTo-Json),$utf8)
if($workshopMode){
 # On this installation, host_workshop_map alone stays in the console/idle loop.
 # Start the built-in map first so SteamGameServer/UGC, game frames and RCON exist.
 $deadline=[DateTime]::UtcNow.AddSeconds(90); $requested=$false; $bootstrapActive=$false; $rconAttempts=0
 while([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited){
  if((Read-SharedStartupLog $log) -match 'ss_loading -> ss_active'){
   $bootstrapActive=$true
   try {
    $rconAttempts++
    $null=& $rconHelper -Command $mapCommand -SettingsPath (Join-Path $installHome 'server-settings.json')
    $requested=$true; break
   } catch {
    # Early RCON availability can lag the active-map log. Keep credentials/errors out of output.
   }
  }
  Start-Sleep -Seconds 1; $process.Refresh()
 }
 if(-not $requested){
  $process.Refresh()
  $exitCode=$null
  if($process.HasExited){$process.WaitForExit();$exitCode=$process.ExitCode;$record.ExitedAt=(Get-Date).ToString('o');$record.ExitCode=$exitCode}
  $failure=Get-ZeStartupFailure -Exited $process.HasExited -ExitCode $exitCode -MapActive $bootstrapActive -RconAttempts $rconAttempts -ServerProcessId $process.Id -LogPath $log
  $record.StartupFailureKind=$failure.Kind; $record.StartupError=$failure.Message
  [IO.File]::WriteAllText($runFile,($record|ConvertTo-Json),$utf8)
  [IO.File]::WriteAllText((Join-Path $installHome 'last-run.json'),($record|ConvertTo-Json),$utf8)
  throw $failure.Message
 }
 $record.WorkshopRequestedAt=(Get-Date).ToString('o')
 [IO.File]::WriteAllText($runFile,($record|ConvertTo-Json),$utf8)
 [IO.File]::WriteAllText((Join-Path $installHome 'last-run.json'),($record|ConvertTo-Json),$utf8)
 Write-Host ('Workshop '+$WorkshopId+' requested. Steam will download/update it before the final map loads.')
}
if($NoWait){Write-Host ('Started PID '+$process.Id);exit 0}
$readyPattern=if($workshopMode){'(?s)Loading custom game "'+[regex]::Escape($WorkshopId)+'".*?ss_loading -> ss_active'}else{'ss_loading -> ss_active|SV:\s+\d+ player server started'}
$verified=$false
while(-not $process.WaitForExit(1000)){
 if(-not $verified -and [regex]::IsMatch((Read-SharedStartupLog $log),$readyPattern)){
  [IO.File]::WriteAllText($cache,$text,$utf8); $verified=$true
  Write-Host 'Map active. Saved a recovery snapshot for this engine version.'
 }
}
$process.Refresh();$code=$process.ExitCode
$record.ExitedAt=(Get-Date).ToString('o');$record.ExitCode=$code
[IO.File]::WriteAllText($runFile,($record|ConvertTo-Json),$utf8)
[IO.File]::WriteAllText((Join-Path $installHome 'last-run.json'),($record|ConvertTo-Json),$utf8)
Write-Host ('Server exited: '+$code)
exit $code
