param([Parameter(Mandatory=$true)][int]$ServerProcessId,[string]$SettingsPath='',[string]$ReportPath='')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($SettingsPath)){$SettingsPath=Join-Path (Split-Path -Parent $PSScriptRoot) 'server-settings.json'}
$settings=Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expected=[IO.Path]::GetFullPath((Join-Path ([string]$settings.GameRoot) 'bin\win64\cs2.exe'))
$server=Get-Process -Id $ServerProcessId
if($server.Path -ne $expected){throw 'Target is not the configured dedicated server.'}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ZeDedicatedConsole
{
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] public static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] public static extern bool SetConsoleMode(IntPtr handle, uint mode);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr word, IntPtr parameter);
}
'@
[ZeDedicatedConsole]::FreeConsole() | Out-Null
$attached=$false
for($attempt=0;$attempt -lt 10;$attempt++){
    if([ZeDedicatedConsole]::AttachConsole([uint32]$ServerProcessId)){$attached=$true;break}
    Start-Sleep -Milliseconds 200
}
if(-not $attached){throw 'Dedicated console not available.'}
$handle=[IntPtr]::Zero
try{
    $handle=[ZeDedicatedConsole]::CreateFileW('CONIN$',[uint32]3221225472,3,[IntPtr]::Zero,3,0,[IntPtr]::Zero)
    [uint32]$mode=0
    if(-not [ZeDedicatedConsole]::GetConsoleMode($handle,[ref]$mode)){throw 'Dedicated console mode could not be read.'}
    $newMode=[uint32](($mode -bor 128) -band 4294967231)
    if(-not [ZeDedicatedConsole]::SetConsoleMode($handle,$newMode)){throw 'Dedicated console Quick Edit change failed.'}
    [ZeDedicatedConsole]::PostMessage($server.MainWindowHandle,0x100,[IntPtr]27,[IntPtr]::Zero) | Out-Null
    [ZeDedicatedConsole]::PostMessage($server.MainWindowHandle,0x101,[IntPtr]27,[IntPtr]::Zero) | Out-Null
    $report=[pscustomobject]@{ProcessId=$ServerProcessId;OriginalMode=$mode;NewMode=$newMode;QuickEditDisabled=$true} | ConvertTo-Json -Compress
    if($ReportPath){[IO.File]::WriteAllText($ReportPath,$report,[Text.UTF8Encoding]::new($false))}
}finally{
    if($handle -ne [IntPtr]::Zero -and $handle -ne [IntPtr](-1)){[ZeDedicatedConsole]::CloseHandle($handle) | Out-Null}
    [ZeDedicatedConsole]::FreeConsole() | Out-Null
}
Write-Output $report
