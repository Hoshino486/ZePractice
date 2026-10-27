#requires -Version 5.1
param([string]$TemporaryRoot='E:\mapper\chana\临时文件夹\ze-workshop-3817448617-20261011\tests')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$session=Join-Path $TemporaryRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
[IO.Directory]::CreateDirectory($session)|Out-Null
$utf8=[Text.UTF8Encoding]::new($true)
$engines=@((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'),(Get-Command pwsh -ErrorAction Stop).Source)
$results=[Collections.Generic.List[object]]::new()
$cases=@(
 @{Name='workshop';Id='3817448617';Args='';Exit=0;Command='host_workshop_map 3817448617';Workshop=$true},
 @{Name='same-map';Id='3817448617';Args='-Map ze_test';Exit=0;Command='host_workshop_map 3817448617';Workshop=$true},
 @{Name='different-map';Id='3817448617';Args='-Map de_dust2';Exit=0;Command='map de_dust2 nomapvalidation true';Workshop=$false},
 @{Name='local-override';Id='3817448617';Args='-LocalMap';Exit=0;Command='map ze_test nomapvalidation true';Workshop=$false},
 @{Name='explicit-id';Id='3817448617';Args='-WorkshopId 1234567890';Exit=0;Command='host_workshop_map 1234567890';Workshop=$true},
 @{Name='legacy-local';Id=$null;Args='';Exit=0;Command='map ze_test nomapvalidation true';Workshop=$false},
 @{Name='missing-profile';Id='3817448617';Args='';Exit=1},
 @{Name='zero-rejected';Id='0';Args='';Exit=1},
 @{Name='injection-rejected';Id='3817448617;quit';Args='';Exit=1},
 @{Name='overflow-rejected';Id='18446744073709551616';Args='';Exit=1},
 @{Name='conflicting-mode';Id='3817448617';Args='-LocalMap -WorkshopId 3817448617';Exit=1}
)
foreach($engine in $engines){
 foreach($case in $cases){
  $label=[IO.Path]::GetFileNameWithoutExtension($engine)
  $root=Join-Path $session ($label+'-'+$case.Name+'-中文路径')
  $install=Join-Path $root 'ZePractice';$tools=Join-Path $install 'tools';$game=Join-Path $root 'game';$csgo=Join-Path $game 'csgo'
  [IO.Directory]::CreateDirectory($tools)|Out-Null
  Copy-Item -LiteralPath (Join-Path $project 'tools\Start-ZePractice.ps1') -Destination $tools
  foreach($relative in @('csgo\addons\metamod\bin\win64\server.dll','csgo\addons\counterstrikesharp\bin\win64\counterstrikesharp.dll','csgo\addons\counterstrikesharp\plugins\ZePractice\ZePractice.dll','csgo\steam.inf','csgo\cfg\ze-practice\server.cfg')){
   if($case.Name -eq 'missing-profile' -and $relative -eq 'csgo\cfg\ze-practice\server.cfg'){continue}
   $f=Join-Path $game $relative;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($f))|Out-Null;[IO.File]::WriteAllText($f,'fixture',$utf8)
  }
$legacyCfg=Join-Path $csgo 'cfg\server.cfg'
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($legacyCfg))|Out-Null
  [IO.File]::WriteAllText($legacyCfg,"// DO NOT EXECUTE OR MODIFY: legacy profile fixture`r`nendround`r`n",$utf8)
  $legacyHash=(Get-FileHash -LiteralPath $legacyCfg -Algorithm SHA256).Hash
  [IO.Directory]::CreateDirectory((Join-Path $game 'csgo_addons\ze_test'))|Out-Null
  $gi="GameInfo`r`n{`r`n FileSystem`r`n {`r`n  SearchPaths`r`n  {`r`n   Game csgo_addons/ze_test`r`n   Game csgo_addons/unmanaged`r`n   Game csgo`r`n   Game core`r`n  }`r`n }`r`n}`r`n"
  [IO.File]::WriteAllText((Join-Path $csgo 'gameinfo.gi'),$gi,$utf8)
  $settings=[ordered]@{GameRoot=$game;DiagnosticsRoot=(Join-Path $root 'logs');StartupMap='ze_test';BindAddress='127.0.0.1';Port=27199;AssetMounts=@('csgo_addons/ze_test');RconPassword='fixture-only-not-a-real-password'}
  if($null -ne $case.Id){$settings.StartupWorkshopId=$case.Id}
  [IO.File]::WriteAllText((Join-Path $install 'server-settings.json'),($settings|ConvertTo-Json -Depth 5),$utf8)
  $out=Join-Path $root 'stdout.txt';$err=Join-Path $root 'stderr.txt'
  $arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $tools 'Start-ZePractice.ps1')+'" -PrepareOnly '+$case.Args
  $oldModule=$env:PSModulePath
  try {if($label -eq 'powershell'){$env:PSModulePath=$null};$proc=Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $out -RedirectStandardError $err}
  finally {$env:PSModulePath=$oldModule}
  $passed=$proc.ExitCode -eq $case.Exit;$reason=''
  if(-not $passed){$reason='Unexpected exit code '+$proc.ExitCode}
  if($passed -and $case.Exit -eq 0){
   $stdout=[IO.File]::ReadAllText($out);$after=[IO.File]::ReadAllText((Join-Path $csgo 'gameinfo.gi'))
   $passed=$stdout.Contains('Map load: '+$case.Command) -and $after.Contains('csgo/addons/metamod') -and $after.Contains('csgo_addons/unmanaged') -and (-not(Test-Path (Join-Path $install 'last-run.json')))
   if($case.Workshop){$passed=$passed -and -not $after.Contains('csgo_addons/ze_test')}else{$passed=$passed -and $after.Contains('csgo_addons/ze_test')}
   if(-not $passed){$reason='Load command, mounts, or prepare-only side effect assertion failed'}
  }
$output=[IO.File]::ReadAllText($out)+[IO.File]::ReadAllText($err)
  if($output.Contains('fixture-only-not-a-real-password')){$passed=$false;$reason='Fixture password leaked'}
  if((Get-FileHash -LiteralPath $legacyCfg -Algorithm SHA256).Hash -cne $legacyHash){$passed=$false;$reason='Legacy profile was modified'}
  if(Test-Path -LiteralPath (Join-Path $csgo 'cfg\ze-practice\rcon.generated.cfg')){$passed=$false;$reason='PrepareOnly unexpectedly wrote the private RCON config'}
  if($case.Name -eq 'missing-profile' -and -not $output.Contains('Missing dedicated practice configuration')){$passed=$false;$reason='Missing profile did not fail closed'}
  if($case.Exit -eq 0 -and -not $output.Contains('Server config: ze-practice/server.cfg')){$passed=$false;$reason='Independent profile not reported'}
  $results.Add([pscustomobject]@{Engine=$label;Test=$case.Name;Passed=$passed;Reason=$reason;Root=$root})
  Write-Host ($label+' '+$case.Name+': '+$(if($passed){'PASS'}else{'FAIL '+$reason}))
 }
}
[IO.File]::WriteAllText((Join-Path $session 'results.json'),(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5),$utf8)
$failed=@($results|Where-Object {-not $_.Passed}).Count
Write-Host ('Workshop startup: '+($results.Count-$failed)+'/'+$results.Count+' passed. Logs: '+$session)
if($failed){exit 1}