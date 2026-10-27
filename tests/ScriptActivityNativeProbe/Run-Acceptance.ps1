param([string]$ProjectRoot=(Split-Path (Split-Path $PSScriptRoot)))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ProjectRoot)
$out=Join-Path $root 'artifacts\script-diagnostics-20261016\native-acceptance'
$probe='E:\mapper\chana\ZePracticeTest-20261016\game\csgo\addons\counterstrikesharp\plugins\ScriptActivityNativeProbe'
New-Item -ItemType Directory -Force $out | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Send-TestRcon([string]$command) {
    & (Join-Path $root 'tools\Invoke-ZeRcon.ps1') -SettingsPath (Join-Path $root 'artifacts\personal-bots-hud-20261016\isolated-settings.json') -Command $command |
      Add-Content (Join-Path $out 'commands.log')
}
function Snapshot([string]$label) {
    Send-TestRcon "css_script_probe $label"
    $path=Join-Path $probe ($label+'.json')
    Copy-Item -LiteralPath $path -Destination $out -Force
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).reading
}
function Check([bool]$ok,[string]$name) {
    $results.Add([pscustomobject]@{name=$name;pass=$ok})
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'results.json')
    if(!$ok){throw $name};Write-Output "PASS $name"
}
Send-TestRcon 'css_plugins list'
$empty=Snapshot 'empty'
Check ($empty.Script.Available -and $empty.Script.Entities -eq 0 -and $empty.Script.Window.TotalCallbacks -eq 0) 'cold-start-empty-map-valid-zero'
Send-TestRcon 'css_script_fixture'
Start-Sleep -Milliseconds 650
$active=Snapshot 'active'
Check ($active.Script.Available -and $active.Script.Entities -eq 1 -and $active.Script.Window.ThinkCallbacks -gt 0) 'actual-setthink-callbacks-observed'
Check ($active.Script.Window.ActiveContexts -eq 1 -and $active.Script.Window.OtherResults -eq 0) 'real-context-and-return-convention'
Start-Sleep 3
$idle=Snapshot 'idle'
Check ($idle.Script.Window.TotalCallbacks -eq 21) 'exact-20-think-plus-one-start-input'
Check ($idle.Script.Window.Callbacks -eq 0 -and $idle.Script.Window.TotalErrors -eq 0) 'event-only-idle-is-not-unavailable'
Send-TestRcon 'css_script_fixture_input Missing'
Start-Sleep 1
$missing=Snapshot 'unregistered-input'
Check ($missing.Script.Window.TotalCallbacks -eq 21) 'unregistered-callback-attempt-not-counted'
Send-TestRcon 'css_script_fixture_input Event'
Start-Sleep 1
$event=Snapshot 'event'
Check ($event.Script.Window.TotalCallbacks -eq 22 -and $event.Script.Window.TotalErrors -eq 0) 'registered-event-counted-once'
Send-TestRcon 'css_script_fixture_input Throw'
Start-Sleep 1
$errorSample=Snapshot 'caught-error'
Check ($errorSample.Script.Window.TotalCallbacks -eq 23 -and $errorSample.Script.Window.TotalErrors -eq 1) 'actual-v8-exception-return-two-counted'
Send-TestRcon 'css_script_fixture_input Event'
Start-Sleep 1
$continued=Snapshot 'after-error'
Check ($continued.Script.Window.TotalCallbacks -eq 24 -and $continued.Script.Window.TotalErrors -eq 1) 'scripts-continue-after-caught-error'
Send-TestRcon 'css_script_fault_fixture'
Start-Sleep 1
$failed=Snapshot 'sampler-unavailable'
Check (!$failed.Script.Available -and $null -eq $failed.Script.Window -and $failed.Script.Error -eq 'isolated-test-unavailable') 'sampler-failure-never-fabricated-zero'
Check ($failed.Native.Tick -gt $continued.Native.Tick) 'sampler-failure-does-not-stop-server'
Write-Output ('PASS '+$results.Count+' native script checks; reload plugin next to restore sampler')

