#requires -Version 5.1
param(
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$BuildOutputRoot = '',
    [string]$TestRoot = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $BuildOutputRoot) { $BuildOutputRoot = Join-Path $project 'artifacts\workshop-resources' }
if (-not $TestRoot) { $TestRoot = Join-Path $project 'artifacts\workshop-upload-tests' }
$preparer = Join-Path $project 'tools\Prepare-ZeWorkshopUpload.ps1'
$gameinfo = Join-Path $ClientGameRoot 'csgo\gameinfo.gi'
$gameinfoHash = (Get-FileHash -LiteralPath $gameinfo).Hash
$script:checks = 0
function Check([bool]$Value, [string]$Label) { if (-not $Value) { throw ('FAIL ' + $Label) }; $script:checks++; Write-Output ('PASS ' + $Label) }
foreach ($invalid in @('0','-1','123;quit',"123`nquit",'18446744073709551616')) {
    $rejected = $false
    try { $null = & $preparer -WorkshopId $invalid -ClientGameRoot $ClientGameRoot -BuildOutputRoot $BuildOutputRoot -OutputRoot $TestRoot } catch { $rejected = $true }
    Check $rejected 'invalid published ID rejected before staging'
}
$package = & $preparer -WorkshopId '3818329935' -ClientGameRoot $ClientGameRoot -BuildOutputRoot $BuildOutputRoot -OutputRoot $TestRoot
$descriptor = [IO.File]::ReadAllText($package.Descriptor)
Check ($descriptor.Contains('"appid" "730"') -and $descriptor.Contains('"publishedfileid" "3818329935"')) 'descriptor updates existing CS2 item'
Check (-not ($descriptor -match '"(?:visibility|title|description|previewfile)"')) 'descriptor leaves existing public metadata alone'
Check ($descriptor.Contains($package.ContentDirectory.Replace('\','/'))) 'content path is the isolated upload directory'
$files = @(Get-ChildItem -LiteralPath $package.ContentDirectory -File -Recurse)
Check ($files.Count -eq 2 -and @($files | Where-Object {$_.Name -notin @('3818329935_dir.vpk','publish_data.txt')}).Count -eq 0) 'only verified VPK and publisher metadata are uploaded'
Check ((Get-FileHash -LiteralPath $package.Vpk).Hash -ceq $package.VpkSHA256) 'staged package hash matches verified six-file build'
Check (@($package.Files | Where-Object {$_.Path.Replace('\','/') -eq 'resource/ui/radiopanel.txt'}).Count -eq 1) 'radio input table retained in package manifest'
Check (-not $package.Published -and -not $package.DownloadValidated) 'preparation does not claim publication or download success'
$again = & $preparer -WorkshopId '3818329935' -ClientGameRoot $ClientGameRoot -BuildOutputRoot $BuildOutputRoot -OutputRoot $TestRoot
Check ($again.ContentDirectory -ne $package.ContentDirectory) 'each export uses a fresh content directory'
Check ((Get-FileHash -LiteralPath $gameinfo).Hash -ceq $gameinfoHash) 'publisher preparation never edits client gameinfo'
Write-Output ('WORKSHOP_UPLOAD_TESTS ' + $script:checks + '/' + $script:checks + ' passed')
