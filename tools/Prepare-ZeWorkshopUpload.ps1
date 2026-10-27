#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$WorkshopId,
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$BuildOutputRoot = '',
    [string]$OutputRoot = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[uint64]$numericId = 0
if ($WorkshopId -notmatch '\A[1-9][0-9]{0,19}\z' -or -not [uint64]::TryParse($WorkshopId, [ref]$numericId)) { throw 'WorkshopId must be an existing positive numeric published file ID.' }
$project = Split-Path -Parent $PSScriptRoot
if (-not $BuildOutputRoot) { $BuildOutputRoot = Join-Path $project 'artifacts\workshop-resources' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $project 'artifacts\workshop-upload' }
$manifestPath = Join-Path $BuildOutputRoot 'build\asset-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.Addon -cne 'saibochana_Server' -or -not $manifest.WorkshopBundle) { throw 'Select a verified saibochana_Server workshop bundle.' }
$verification = @(& (Join-Path $PSScriptRoot 'Build-ZeWorkshopResources.ps1') -Action Verify -ClientGameRoot $ClientGameRoot -OutputRoot $BuildOutputRoot -ExtraAssetsRoot ([string]$manifest.ExtraAssetsRoot))
$sourceVpk = Join-Path $BuildOutputRoot 'build\saibochana_Server.vpk'
$destination = [IO.Path]::GetFullPath((Join-Path $OutputRoot ($WorkshopId + '-' + [Guid]::NewGuid().ToString('N'))))
if ($destination -match '["\x00-\x1f]') { throw 'Upload path contains characters unsupported by the SteamCMD descriptor.' }
$content = Join-Path $destination 'content'
$vpk = Join-Path $content ($WorkshopId + '_dir.vpk')
$descriptor = Join-Path $destination 'workshop-update.vdf'
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.Directory]::CreateDirectory($content) | Out-Null
Copy-Item -LiteralPath $sourceVpk -Destination $vpk
if ((Get-FileHash -LiteralPath $vpk -Algorithm SHA256).Hash -cne $manifest.VpkSHA256) { throw 'Staged VPK differs from the verified build; do not upload this directory.' }
$contentValue = $content.Replace('\','/')
$vdf = @'
"workshopitem"
{
    "appid" "730"
    "publishedfileid" "WORKSHOP_ID"
    "contentfolder" "CONTENT_FOLDER"
    "changenote" "Include the complete saibochana_Server resource bundle, including the radio input table."
}
'@
$vdf = $vdf.Replace('WORKSHOP_ID', $WorkshopId).Replace('CONTENT_FOLDER', $contentValue)
[IO.File]::WriteAllText($descriptor, $vdf, $utf8)
$publishData = '"publish_data"' + "`r`n{`r`n" + '    "title" "saibochana_Server"' + "`r`n" + '    "source_folder" "saibochana_Server"' + "`r`n}" + "`r`n"
[IO.File]::WriteAllText((Join-Path $content 'publish_data.txt'), $publishData, $utf8)
$readme = @'
# Complete resource upload, same Workshop ID

This directory is prepared locally; nothing has been uploaded or published.
It contains the verified, already-packed resource VPK, not a directory for the Workshop Manager to repack.
The Workshop Manager's default directory allowlist omits resource/ui/radiopanel.txt.
The update descriptor targets the EXISTING item WORKSHOP_ID. It does not change its title, description, preview, or visibility.

Run SteamCMD as the owner of this item, logging in interactively without storing a password in scripts:

    steamcmd +login STEAM_ACCOUNT +workshop_build_item "DESCRIPTOR_PATH" +quit

SteamCMD upload and any Steam approval still require verification. This package has NOT passed server download or clean-client testing.
After a successful update, verify the downloaded VPK contains all manifest files, then enable the resource ID during a maintenance window.
If the server reports Access denied or loops map reloads, stop it and use Set-ZeWorkshopResources.ps1 -Disable.
Do not put this VPK inside the csgo_addons/saibochana_Server publisher directory, add global SearchPaths, or replace the startup map with the resource ID.
Future additions require a fresh verified bundle and a newly prepared upload directory targeting the same ID.
'@
$readme = $readme.Replace('WORKSHOP_ID', $WorkshopId).Replace('DESCRIPTOR_PATH', $descriptor)
[IO.File]::WriteAllText((Join-Path $destination 'README.md'), $readme, $utf8)
$result = [ordered]@{
    WorkshopId=$WorkshopId
    Addon='saibochana_Server'
    AppId=730
    Published=$false
    DownloadValidated=$false
    ClientFilesModified=$false
    Descriptor=$descriptor
    ContentDirectory=$content
    Vpk=$vpk
    VpkSHA256=$manifest.VpkSHA256
    Files=@($manifest.Files)
    BuildVerification=$verification
}
[IO.File]::WriteAllText((Join-Path $destination 'upload-manifest.json'), ($result | ConvertTo-Json -Depth 6), $utf8)
[pscustomobject]$result
