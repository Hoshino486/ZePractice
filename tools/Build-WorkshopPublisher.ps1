[CmdletBinding()]
param([string]$Sdk = 'E:\mapper\chana\ZePractice\native\CS2Fixes-2000932\source\CS2Fixes\sdk', [string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
if (!$OutputRoot) { $OutputRoot = Join-Path (Split-Path $PSScriptRoot) 'artifacts\commander-luna-20261015\publisher' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force $OutputRoot | Out-Null
$src = Join-Path $PSScriptRoot 'WorkshopPublisher.cpp'
$cmd = 'call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul && cl /nologo /EHsc /MT /O2 /std:c++17 /I"'+$Sdk+'\public" "'+$src+'" /Fo"'+$OutputRoot+'\WorkshopPublisher.obj" /Fe"'+$OutputRoot+'\WorkshopPublisher.exe" /link "'+$Sdk+'\lib\public\win64\steam_api64.lib"'
$saved = $env:PATH
try { $env:PATH = 'C:\Windows\System32;C:\Windows'; & cmd /d /c $cmd; $code = $LASTEXITCODE }
finally { $env:PATH = $saved }
if ($code -ne 0) { throw "Publisher compile failed: $code" }
Copy-Item 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game\bin\win64\steam_api64.dll' $OutputRoot -Force
Set-Content (Join-Path $OutputRoot 'steam_appid.txt') '730' -Encoding ascii
Write-Output ('Publisher: ' + (Join-Path $OutputRoot 'WorkshopPublisher.exe'))
