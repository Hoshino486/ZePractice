#requires -Version 5.1
param(
    [Parameter(Mandatory=$true)][string]$PackageZip,
    [string]$TestRoot = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $TestRoot) { $TestRoot = Join-Path $project 'artifacts\resource-config-tests' }
$installer = Join-Path $project 'tools\Set-ZeWorkshopResources.ps1'
$root = Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))
$game = Join-Path $root 'game'
$utf8 = New-Object Text.UTF8Encoding($false)
$script:checks = 0
function Check([bool]$Value, [string]$Label) { if (-not $Value) { throw ('FAIL ' + $Label) }; $script:checks++; Write-Output ('PASS ' + $Label) }
foreach ($relative in @('bin\win64','csgo\addons\metamod','csgo\cfg\multiaddonmanager')) { [IO.Directory]::CreateDirectory((Join-Path $game $relative)) | Out-Null }
[IO.File]::WriteAllText((Join-Path $game 'bin\win64\cs2.exe'), 'fixture not executable')
$gi = Join-Path $game 'csgo\gameinfo.gi'
[IO.File]::WriteAllText($gi, 'fixture gameinfo preserved')
$giHash = (Get-FileHash -LiteralPath $gi).Hash
$cfg = Join-Path $game 'csgo\cfg\multiaddonmanager\multiaddonmanager.cfg'
$custom = '// preserved fixture' + "`r`n" + 'mm_extra_addons "111,222"' + "`r`n" + 'mm_client_extra_addons "333"' + "`r`n" + 'mm_addon_debug 1' + "`r`n"
[IO.File]::WriteAllText($cfg, $custom, $utf8)
$first = & $installer -ServerGameRoot $game -PackageZip $PackageZip
Check ($first.Installed -and -not $first.Configured) 'plugin stages without fabricating Workshop ID'
Check ([IO.File]::ReadAllText($cfg) -ceq $custom) 'existing config preserved before publishing'
$rejected = $false
try { $null = & $installer -ServerGameRoot $game -Disable } catch { $rejected = $true }
Check $rejected 'disabling without a recorded resource ID is rejected'
Check ([IO.File]::ReadAllText($cfg) -ceq $custom) 'unknown resource does not remove other addons'
$again = & $installer -ServerGameRoot $game -PackageZip $PackageZip
Check ($again.Changed -eq 0) 'staging is idempotent'
$before = (Get-FileHash -LiteralPath $cfg).Hash
foreach ($bad in @('0','-1','1;quit','123 456','18446744073709551616',"123`nquit")) {
    $rejected = $false
    try { $null = & $installer -ServerGameRoot $game -WorkshopId $bad } catch { $rejected = $true }
    Check $rejected ('invalid ID rejected: ' + ($bad -replace '\s','_'))
}
Check ((Get-FileHash -LiteralPath $cfg).Hash -eq $before) 'invalid IDs do not write config'
$configured = & $installer -ServerGameRoot $game -WorkshopId '444'
$text = [IO.File]::ReadAllText($cfg)
Check ($text.Contains('mm_extra_addons "111,222,444"')) 'resource ID merges with existing addons'
Check ($text.Contains('mm_client_extra_addons "333"')) 'client-only addon list preserved'
Check ($text.Contains('mm_addon_debug 1')) 'unrelated convar preserved'
Check ($text.Contains('mm_addon_mount_download 1')) 'refresh download enabled for updates'
Check ($text.Contains('mm_cache_clients_with_addons 0')) 'stale client cache disabled'
Check ([regex]::Matches($text, '(?m)^mm_extra_addons ').Count -eq 1) 'only one addon-list directive'
$again = & $installer -ServerGameRoot $game -WorkshopId '444'
Check ($again.Changed -eq 0) 'setting same published ID is idempotent'
$null = & $installer -ServerGameRoot $game -WorkshopId '555'
$text = [IO.File]::ReadAllText($cfg)
Check ($text.Contains('mm_extra_addons "111,222,555"') -and -not $text.Contains('444')) 'replace only managed ID'
Check ((Get-FileHash -LiteralPath $gi).Hash -eq $giHash) 'gameinfo untouched'
$status = & $installer -ServerGameRoot $game -StatusOnly
Check ($status.Installed -and $status.WorkshopId -eq '555') 'status reports configured ID without starting server'
Check $status.Configured 'status confirms resource is present in active addon list'
$disabled = & $installer -ServerGameRoot $game -Disable
$text = [IO.File]::ReadAllText($cfg)
Check (-not $disabled.Configured -and $disabled.WorkshopId -eq '555') 'disable preserves published ID but deactivates mounting'
Check ($text.Contains('mm_extra_addons "111,222"') -and -not $text.Contains('555')) 'disable removes only managed resource ID'
Check ($text.Contains('mm_client_extra_addons "333"') -and $text.Contains('mm_addon_debug 1')) 'disable preserves unrelated addon settings'
Check (-not $text.Contains('BEGIN ZE WORKSHOP RESOURCES') -and -not $text.Contains('mm_addon_mount_download 1')) 'disable removes managed download overrides'
$status = & $installer -ServerGameRoot $game -StatusOnly
Check (-not $status.Configured -and $status.WorkshopId -eq '555') 'status distinguishes recorded ID from active configuration'
$state = Get-Content -LiteralPath (Join-Path $game 'csgo\cfg\multiaddonmanager\ze-resources.state.json') -Raw | ConvertFrom-Json
Check (-not $state.Configured -and $state.LiveValidation.StartsWith('Disabled;')) 'disabled state does not claim download validation'
$again = & $installer -ServerGameRoot $game -Disable
Check ($again.Changed -eq 0) 'disabling repeatedly is idempotent'
$again = & $installer -ServerGameRoot $game -PackageZip $PackageZip
Check ($again.Changed -eq 0 -and -not $again.Configured) 'restaging plugin does not reactivate disabled resource'
$before = (Get-FileHash -LiteralPath $cfg).Hash
$rejected = $false
try { $null = & $installer -ServerGameRoot $game -WorkshopId '666' -Disable } catch { $rejected = $true }
Check $rejected 'conflicting enable and disable flags are rejected'
Check ((Get-FileHash -LiteralPath $cfg).Hash -eq $before) 'conflicting flags leave configuration unchanged'
$resumed = & $installer -ServerGameRoot $game -WorkshopId '555'
Check ($resumed.Configured -and [IO.File]::ReadAllText($cfg).Contains('mm_extra_addons "111,222,555"')) 'same published ID resumes without duplicating other addons'
[IO.File]::WriteAllText($cfg, 'mm_extra_addons "555"' + "`r`n", $utf8)
$disabled = & $installer -ServerGameRoot $game -Disable
Check (-not $disabled.Configured -and [IO.File]::ReadAllText($cfg).Contains('mm_extra_addons ""')) 'disabling sole resource leaves a valid empty addon list'
$again = & $installer -ServerGameRoot $game -Disable
Check ($again.Changed -eq 0) 'empty disabled list stays idempotent'
$tampered = Join-Path $root 'tampered.zip'
[IO.File]::WriteAllText($tampered,'not the pinned package')
$rejected = $false
try { $null = & $installer -ServerGameRoot $game -PackageZip $tampered } catch { $rejected = $true }
Check $rejected 'tampered plugin archive rejected'
[IO.File]::WriteAllText($cfg, 'mm_extra_addons 111,222', $utf8)
$rejected = $false
try { $null = & $installer -ServerGameRoot $game -WorkshopId '666' } catch { $rejected = $true }
Check $rejected 'ambiguous existing config rejected before overwrite'
Check ([IO.File]::ReadAllText($cfg) -ceq 'mm_extra_addons 111,222') 'rejected config left intact'
$rejected = $false
try { $null = & $installer -ServerGameRoot $game -Disable } catch { $rejected = $true }
Check $rejected 'disable rejects ambiguous existing addon syntax'
Check ([IO.File]::ReadAllText($cfg) -ceq 'mm_extra_addons 111,222') 'failed disable leaves ambiguous config intact'
Check ((Get-FileHash -LiteralPath $gi).Hash -eq $giHash) 'disable and resume never touch gameinfo'
Write-Output ('WORKSHOP_RESOURCE_TESTS ' + $script:checks + '/' + $script:checks + ' passed')
