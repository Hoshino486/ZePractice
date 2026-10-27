#requires -Version 5.1
$ErrorActionPreference='Stop'
$source=Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\Start-ZePractice.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Launcher parser errors'}
$definition=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ZeStartupFailure'},$true)
if(-not $definition){throw 'Startup failure classifier missing'}
. ([scriptblock]::Create($definition.Extent.Text))
$passed=0
function Check($value,[string]$name){if(-not $value){throw $name};$script:passed++;Write-Host ('PASS '+$name)}
$log='E:\server\logs\startup.log'
$failure=Get-ZeStartupFailure $true -1073741819 $false 0 3380 $log
Check ($failure.Kind -eq 'ProcessExited' -and $failure.Message.Contains('-1073741819') -and $failure.Message.Contains($log)) 'native crash reports actual code/log'
Check (-not $failure.Message.Contains('RCON') -and -not $failure.Message.Contains('Do not start a second server')) 'dead process is not reported as RCON timeout or running'
$failure=Get-ZeStartupFailure $true 0 $true 3 3380 $log
Check ($failure.Kind -eq 'ProcessExited' -and $failure.Message.Contains('exit code: 0')) 'early clean exit takes priority over RCON errors'
$failure=Get-ZeStartupFailure $true $null $false 0 3380 $log
Check ($failure.Message.Contains('unavailable')) 'unknown exit code is not invented'
$failure=Get-ZeStartupFailure $false $null $false 0 3380 $log
Check ($failure.Kind -eq 'BootstrapTimeout' -and $failure.Message.Contains('still running') -and -not $failure.Message.Contains('RCON')) 'live inactive map has its own diagnosis'
$failure=Get-ZeStartupFailure $false $null $true 7 3380 $log
Check ($failure.Kind -eq 'RconUnavailable' -and $failure.Message.Contains('7 attempts')) 'active map but unavailable RCON is distinct'
# Exercise a real short-lived child as well, retaining its handle like the launcher.
$engine=(Get-Process -Id $PID).Path
$child=Start-Process -FilePath $engine -ArgumentList '-NoProfile -NonInteractive -Command "exit 23"' -WindowStyle Hidden -PassThru
$null=$child.Handle;$child.WaitForExit();$child.Refresh()
$failure=Get-ZeStartupFailure $child.HasExited $child.ExitCode $false 0 $child.Id $log
Check ($failure.Kind -eq 'ProcessExited' -and $failure.Message.Contains('exit code: 23')) 'real child early exit preserves its code'
Write-Host ('STARTUP_FAILURE_TESTS '+$passed+'/'+$passed+' passed')
