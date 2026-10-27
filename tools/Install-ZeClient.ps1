#requires -Version 5.1
<#
Legacy developer installer. Radio global mounts require -AllowLegacyGlobalMount; use Workshop distribution for players.
Never opens userdata, reads passwords, writes a key VCFG, or runs CS2. All changed files are backed up.
Example: .\Install-ZeClient.ps1 -ClientGameRoot 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game' -AssetsRoot '..\assets\ze_practice_ui'
Rollback: .\Install-ZeClient.ps1 -RestoreManifest 'BACKUP\manifest.json'
#>
[CmdletBinding()]
param(
    [string]$ClientGameRoot = '',
    [string]$AssetsRoot = '',
    [string]$BackupRoot = 'E:\mapper\chana\临时文件夹\ze-ui-access-20261011\client-backups',
    [string]$RestoreManifest = '',
    [switch]$RadioMenu,
    [switch]$AllowLegacyGlobalMount
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RadioMenu -and -not $RestoreManifest -and -not $AllowLegacyGlobalMount) {
    throw 'Legacy global radio VPK installation is disabled: it can prevent secure-server connections. Use the Workshop resource package instead. Rollback remains available with -RestoreManifest. -AllowLegacyGlobalMount is for isolated development fixtures only.'
}
try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { }
$utf8 = New-Object Text.UTF8Encoding($false, $true)
$byteText = [Text.Encoding]::GetEncoding(28591)
$assetPaths = @('panorama\layout\custom_game\zepractice\main.vxml_c','panorama\styles\custom_game\zepractice\main.vcss_c')
$allowedPaths = $assetPaths + @('cfg\ze_practice_keys.cfg','cfg\autoexec.cfg')
if ($RadioMenu) { $allowedPaths += @('ze_practice_radio\ze_practice_radio.vpk','ze_practice_radio\ze_dynamic_hud.vpk','gameinfo.gi','cfg\bind.cfg') }
$product = 'ZePracticeClientAssets'

function Full-Path([string]$Path) { return [IO.Path]::GetFullPath($Path).TrimEnd('\') }
function Assert-Path([string]$Path, [string]$Root) {
    $full = Full-Path $Path; $base = Full-Path $Root
    if ($full -ne $base -and -not $full.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A file path left the explicitly selected directory.' }
    # Reject junctions/symlinks in the whole existing ancestor chain, including the root itself.
    $cursor = $full
    while (-not [string]::IsNullOrEmpty($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A target or backup ancestor is a reparse point.' }
        }
        $next = Split-Path -Parent $cursor
        if ($next -eq $cursor) { break }; $cursor = $next
    }
    return $full
}
function Hash-Bytes([byte[]]$Bytes) {
    if ($null -eq $Bytes) { $Bytes = [byte[]]@() }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}
function Get-Hash([string]$Path) { return Hash-Bytes ([IO.File]::ReadAllBytes($Path)) }
function Write-Atomic([string]$Path, [byte[]]$Bytes, [string]$Root) {
    if ($null -eq $Bytes) { $Bytes = [byte[]]@() }
    $full = Assert-Path $Path $Root
    [IO.Directory]::CreateDirectory((Split-Path -Parent $full)) | Out-Null
    $temp = $full + '.ze-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $null = Assert-Path $temp $Root
    try {
        [IO.File]::WriteAllBytes($temp, $Bytes)
        if (Test-Path -LiteralPath $full -PathType Leaf) { [IO.File]::Replace($temp, $full, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temp, $full) }
    } finally { if (Test-Path -LiteralPath $temp -PathType Leaf) { Remove-Item -LiteralPath $temp -Force } }
}
function Save-Manifest($Value, [string]$Path, [string]$Root) {
    Write-Atomic $Path ($utf8.GetBytes(($Value | ConvertTo-Json -Depth 8))) $Root
}
function Verify-GameRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Specify -ClientGameRoot as the client game directory.' }
    $full = Full-Path $Path; $null = Assert-Path $full $full
    if (-not (Test-Path -LiteralPath (Join-Path $full 'csgo') -PathType Container) -or -not (Test-Path -LiteralPath (Join-Path $full 'bin\win64\cs2.exe') -PathType Leaf)) {
        throw 'ClientGameRoot must contain csgo and bin\win64\cs2.exe.'
    }
    return $full
}
function Read-CfgBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return ,([byte[]]@()) }
    $value = [IO.File]::ReadAllBytes($Path)
    if ($value.Length -gt 4194304) { throw 'A client cfg is larger than the 4 MiB edit limit.' }
    # Preserve UTF-8/ANSI bytes, including BOM and non-ASCII comments, rather than guessing an ANSI codepage.
    if ($value.Length -ge 2 -and (($value[0] -eq 255 -and $value[1] -eq 254) -or ($value[0] -eq 254 -and $value[1] -eq 255))) { throw 'Convert the UTF-16 client cfg to UTF-8 before installation.' }
    if ($value -contains [byte]0) { throw 'A client cfg contains NUL bytes; no files were changed.' }
    return ,$value
}
function Update-Keys([byte[]]$Original) {
    $text = if ($null -eq $Original) { "" } else { $byteText.GetString($Original) }; $newline = if ($text.Contains("`r`n")) { "`r`n" } elseif ($text.Contains("`n")) { "`n" } else { [Environment]::NewLine }
    $begin = '// BEGIN ZE_PRACTICE_KEYS_V1'; $end = '// END ZE_PRACTICE_KEYS_V1'
    $pattern = '(?ms)^' + [regex]::Escape($begin) + '\r?\n.*?^' + [regex]::Escape($end) + '(?:\r?\n|$)'
    $text = [regex]::Replace($text, $pattern, '')
    if ($text.Contains($begin) -or $text.Contains($end)) { throw 'The existing ZePractice key block has incomplete markers; no files were changed.' }
    if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { $text += $newline }
    $menuCommand = if ($RadioMenu) { 'css_ze_ui_close;radio' } else { 'css_ze_ui' }
    $block = @($begin,'// Managed keys below; F5/F7/F8 remain untouched. Radio mode adds 0 to close and reload bind.cfg.','bind "ALT" "css_ze_noclip"',('bind "F9" "' + $menuCommand + '"'),'bind "F11" "css_ze_god"','bind "INS" "css_ze_save"','bind "HOME" "css_ze_tp"','bind "END" "css_ze_clearcheckpoint"',$end)
    if ($RadioMenu) { $block = $block[0..($block.Count-2)] + @('bind "Z" "css_ze_ui_close;radio"','bind "F10" "radio3"','bind "0" "slot10;css_ze_ui_close;exec bind.cfg"',$end) }
    return ,$byteText.GetBytes($text + ($block -join $newline) + $newline)
}
function Update-Autoexec([byte[]]$Original) {
    $text = if ($null -eq $Original) { "" } else { $byteText.GetString($Original) }; $newline = if ($text.Contains("`r`n")) { "`r`n" } elseif ($text.Contains("`n")) { "`n" } else { [Environment]::NewLine }
    $begin = '// BEGIN ZE_PRACTICE_AUTOEXEC_V1'; $end = '// END ZE_PRACTICE_AUTOEXEC_V1'
    $pattern = '(?ms)^' + [regex]::Escape($begin) + '\r?\n.*?^' + [regex]::Escape($end) + '(?:\r?\n|$)'
    $text = [regex]::Replace($text, $pattern, '')
    if ($text.Contains($begin) -or $text.Contains($end)) { throw 'The existing ZePractice autoexec block has incomplete markers; no files were changed.' }
    # Move an existing standalone exec to the end instead of appending a duplicate. Unrelated lines stay byte-for-byte.
    $text = [regex]::Replace($text, '(?im)^[ \t]*exec[ \t]+(?:"ze_practice_keys(?:\.cfg)?"|ze_practice_keys(?:\.cfg)?)[ \t]*(?://[^\r\n]*)?(?:\r?\n|$)', '')
    if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { $text += $newline }
    return ,$byteText.GetBytes($text + (@($begin,'exec ze_practice_keys',$end) -join $newline) + $newline)
}
function Update-RadioSearchPath([byte[]]$Original) {
    $text = $byteText.GetString($Original)
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $text = [regex]::Replace($text, '(?m)^[ \t]*Game[ \t]+csgo[/\\]ze_practice_radio(?:[/\\](?:ze_practice_radio|ze_dynamic_hud)\.vpk)?[ \t]*(?:\r?\n|$)', '')
    $pattern = '(?m)^([ \t]*)Game[ \t]+csgo[ \t]*(?://[^\r\n]*)?\r?$'
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) { throw 'Expected one Game csgo search path; gameinfo was not modified.' }
    $line = $matches[0]
    $insert = $line.Groups[1].Value + "Game" + [char]9 + "csgo/ze_practice_radio/ze_dynamic_hud.vpk" + $newline
    $insert += $line.Groups[1].Value + "Game" + [char]9 + "csgo/ze_practice_radio/ze_practice_radio.vpk" + $newline
    return ,$byteText.GetBytes($text.Insert($line.Index, $insert))
}
function New-RadioPackage([byte[]]$Content) {
    if ($Content.Length -eq 0 -or $Content.Length -gt 1048576) { throw 'Radio resource must be between 1 byte and 1 MiB.' }
    $null = $utf8.GetString($Content)
    [uint32]$crc = [uint32]::MaxValue
    foreach ($value in $Content) {
        $crc = $crc -bxor $value
        for ($bit = 0; $bit -lt 8; $bit++) {
            if (($crc -band 1) -ne 0) { $crc = ($crc -shr 1) -bxor [uint32]3988292384 }
            else { $crc = $crc -shr 1 }
        }
    }
    $crc = $crc -bxor [uint32]::MaxValue
    $treeStream = New-Object IO.MemoryStream
    $treeWriter = New-Object IO.BinaryWriter($treeStream)
    $packageStream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($packageStream)
    $md5 = [Security.Cryptography.MD5]::Create()
    try {
        $treeWriter.Write([Text.Encoding]::ASCII.GetBytes("txt`0resource/ui`0radiopanel`0"))
        $treeWriter.Write($crc)
        $treeWriter.Write([uint16]0)
        $treeWriter.Write([uint16]32767)
        $treeWriter.Write([uint32]0)
        $treeWriter.Write([uint32]$Content.Length)
        $treeWriter.Write([uint16]65535)
        $treeWriter.Write([byte[]]@(0,0,0))
        $treeWriter.Flush()
        $tree = $treeStream.ToArray()
        foreach ($field in @([uint32]1437209140,2,$tree.Length,$Content.Length,0,48,0)) { $writer.Write([uint32]$field) }
        $writer.Write($tree)
        $writer.Write($Content)
        $writer.Write($md5.ComputeHash($tree))
        $writer.Write($md5.ComputeHash([byte[]]@()))
        $writer.Flush()
        $writer.Write($md5.ComputeHash($packageStream.ToArray()))
        $writer.Flush()
        return ,$packageStream.ToArray()
    } finally { $md5.Dispose(); $treeWriter.Dispose(); $writer.Dispose(); $treeStream.Dispose(); $packageStream.Dispose() }
}
function Verify-Resource([byte[]]$Value) {
    if ($Value.Length -lt 32 -or [BitConverter]::ToUInt32($Value,0) -ne $Value.Length -or [BitConverter]::ToUInt16($Value,4) -ne 12) {
        throw 'A UI asset is not a complete compiled Source 2 resource.'
    }
}

if (-not [string]::IsNullOrWhiteSpace($RestoreManifest)) {
    $manifestPath = Full-Path $RestoreManifest; $sessionRoot = Split-Path -Parent $manifestPath
    $null = Assert-Path $manifestPath $sessionRoot
    try {
        if ((Get-Item -LiteralPath $manifestPath).Length -gt 65536) { throw 'oversized' }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($manifest.Product -cne $product -or $manifest.Version -ne 1 -or @($manifest.Files).Count -notin @(4,5,6,7,8)) { throw 'wrong schema' }
        [Guid]$id = $manifest.InstallationId
    } catch { throw 'Restore manifest is not a valid ZePractice client installation manifest.' }
    $allowedPaths = $assetPaths + @('cfg\ze_practice_keys.cfg','cfg\autoexec.cfg')
    if (@($manifest.Files).Count -eq 5) { $allowedPaths += 'resource\ui\radiopanel.txt' }
    if (@($manifest.Files).Count -eq 6) {
        $radioPath = if (@($manifest.Files | Where-Object { $_.RelativePath -ceq 'ze_practice_radio\ze_practice_radio.vpk' }).Count -eq 1) { 'ze_practice_radio\ze_practice_radio.vpk' } else { 'ze_practice_radio\resource\ui\radiopanel.txt' }
        $allowedPaths += @($radioPath,'gameinfo.gi')
    }
    if (@($manifest.Files).Count -eq 7) { $allowedPaths += @('ze_practice_radio\ze_practice_radio.vpk','gameinfo.gi','cfg\bind.cfg') }
    if (@($manifest.Files).Count -eq 8) { $allowedPaths += @('ze_practice_radio\ze_practice_radio.vpk','ze_practice_radio\ze_dynamic_hud.vpk','gameinfo.gi','cfg\bind.cfg') }
    $game = Verify-GameRoot ([string]$manifest.ClientGameRoot)
    if (-not [string]::IsNullOrWhiteSpace($ClientGameRoot) -and (Full-Path $ClientGameRoot) -ne $game) { throw 'ClientGameRoot does not match this installation manifest.' }
    $targetRoot = Join-Path $game 'csgo'; $seen = @{}; $restorePlan = @(); $conflicts = @(); $unchanged = 0
    foreach ($entry in @($manifest.Files)) {
        $relative = [string]$entry.RelativePath
        if ($allowedPaths -cnotcontains $relative -or $seen.ContainsKey($relative) -or $entry.Changed -isnot [bool] -or $entry.Existed -isnot [bool] -or [string]$entry.InstalledSHA256 -notmatch '\A[0-9A-F]{64}\z') { throw 'Restore manifest contains an invalid or unowned file entry.' }
        $seen[$relative] = $true; $target = Assert-Path (Join-Path $targetRoot $relative) $targetRoot
        if (-not $entry.Changed) { $unchanged++; continue }
        $backup = $null
        if ($entry.Existed) {
            if ([string]$entry.BackupRelative -cne ('originals\' + $relative) -or [string]$entry.OriginalSHA256 -notmatch '\A[0-9A-F]{64}\z') { throw 'Restore backup metadata is invalid.' }
            $backup = Assert-Path (Join-Path $sessionRoot $entry.BackupRelative) $sessionRoot
            if (-not (Test-Path -LiteralPath $backup -PathType Leaf) -or (Get-Hash $backup) -cne $entry.OriginalSHA256) { throw 'An original backup is missing or changed; no files were restored.' }
            if ($relative -eq 'gameinfo.gi' -and -not $AllowLegacyGlobalMount -and [IO.File]::ReadAllText($backup) -match '(?im)^\s*Game\s+csgo[/\\]ze_practice_radio') {
                throw 'This rollback would restore a retired global radio mount. The repaired gameinfo is preserved; use an earlier clean backup instead.'
            }
        }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            if (-not $entry.Existed -and -not (Test-Path -LiteralPath $target)) { $unchanged++; continue }
            $conflicts += $relative; continue
        }
        $current = Get-Hash $target
        if ($entry.Existed -and $current -ceq $entry.OriginalSHA256) { $unchanged++; continue }
        if ($current -cne $entry.InstalledSHA256) { $conflicts += $relative; continue }
        $restorePlan += [pscustomobject]@{Target=$target; Backup=$backup; Entry=$entry}
    }
    foreach ($item in $restorePlan) {
        # Recheck immediately before every write/delete; never clobber an edit made after the manifest was read.
        $null = Assert-Path $item.Target $targetRoot
        if (-not (Test-Path -LiteralPath $item.Target -PathType Leaf) -or (Get-Hash $item.Target) -cne $item.Entry.InstalledSHA256) { throw 'A target changed during rollback; remaining files were left alone.' }
        if ($null -ne $item.Backup) { Write-Atomic $item.Target ([IO.File]::ReadAllBytes($item.Backup)) $targetRoot }
        else { Remove-Item -LiteralPath $item.Target -Force }
    }
    $status = if ($conflicts.Count -eq 0) { 'Restored' } else { 'PartiallyRestored' }
    if ($conflicts.Count -gt 0) { Write-Warning ('Skipped user-modified or missing files: ' + ($conflicts -join ', ')) }
    [pscustomobject]@{Status=$status; Restored=$restorePlan.Count; Unchanged=$unchanged; Conflicts=@($conflicts); Manifest=$manifestPath; ClientGameRoot=$game; UserdataTouched=$false}
    return
}

$game = Verify-GameRoot $ClientGameRoot
if ([string]::IsNullOrWhiteSpace($AssetsRoot)) { $AssetsRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\ze_practice_ui' }
$assets = Full-Path $AssetsRoot; $targetRoot = Join-Path $game 'csgo'; $plan = @()
foreach ($relative in $allowedPaths) {
    $target = Assert-Path (Join-Path $targetRoot $relative) $targetRoot
    if ((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'An installation file path already exists as a directory.' }
    $old = if (Test-Path -LiteralPath $target -PathType Leaf) { [IO.File]::ReadAllBytes($target) } else { [byte[]]@() }
    if ($assetPaths -contains $relative) {
        $source = Assert-Path (Join-Path $assets $relative) $assets
        $new = [IO.File]::ReadAllBytes($source); Verify-Resource $new
    } elseif ($relative -eq 'ze_practice_radio\ze_practice_radio.vpk') {
        $source = Join-Path (Split-Path -Parent $PSScriptRoot) 'client\resource\ui\radiopanel.txt'
        $new = New-RadioPackage ([IO.File]::ReadAllBytes($source))
    } elseif ($relative -eq 'ze_practice_radio\ze_dynamic_hud.vpk') {
        foreach ($required in @('panorama\layout\custom_game\zepractice\radio.vxml_c','panorama\styles\custom_game\zepractice\radio.vcss_c','panorama\layout\hud\hudradio.vxml_c','panorama\scripts\custom_game\zepractice\radio_filter.vjs_c')) {
            Verify-Resource ([IO.File]::ReadAllBytes((Assert-Path (Join-Path $assets $required) $assets)))
        }
        $packageRoot = Split-Path -Parent $assets
        $source = Assert-Path (Join-Path $packageRoot 'ze_practice_ui.vpk') $packageRoot
        $new = [IO.File]::ReadAllBytes($source)
        if ($new.Length -lt 76 -or $new.Length -gt 33554432 -or [BitConverter]::ToUInt32($new,0) -ne 1437209140 -or [BitConverter]::ToUInt32($new,4) -ne 2) { throw 'Invalid dynamic UI package.' }
        $md5 = [Security.Cryptography.MD5]::Create()
        try { if ([BitConverter]::ToString($md5.ComputeHash($new,0,$new.Length-16)) -cne [BitConverter]::ToString($new,$new.Length-16,16)) { throw 'Dynamic UI package checksum mismatch.' } } finally { $md5.Dispose() }
    } elseif ($relative -eq 'gameinfo.gi') { $new = Update-RadioSearchPath (Read-CfgBytes $target)
    } elseif ($relative -eq 'cfg\ze_practice_keys.cfg') { $new = Update-Keys (Read-CfgBytes $target) }
    else { $new = Update-Autoexec (Read-CfgBytes $target) }
    $exists = Test-Path -LiteralPath $target -PathType Leaf
    $oldHash = if ($exists) { Hash-Bytes $old } else { $null }; $newHash = Hash-Bytes $new
    $plan += [pscustomobject]@{Path=$relative; Target=$target; Old=$old; New=$new; Existed=$exists; OldHash=$oldHash; NewHash=$newHash; Changed=(-not $exists -or $oldHash -cne $newHash)}
}
$backupBase = Full-Path $BackupRoot; $null = Assert-Path $backupBase $backupBase
# Backups must not be inside the game tree (Steam verification must not remove originals).
if ($backupBase -eq $game -or $backupBase.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'BackupRoot must be outside ClientGameRoot.' }
$id = [Guid]::NewGuid().ToString('N'); $sessionRoot = Assert-Path (Join-Path $backupBase ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $id)) $backupBase
[IO.Directory]::CreateDirectory($sessionRoot) | Out-Null
$manifestPath = Join-Path $sessionRoot 'manifest.json'; $entries = @()
foreach ($item in $plan) {
    $backupRelative = $null
    if ($item.Existed) {
        $backupRelative = 'originals\' + $item.Path; $backup = Assert-Path (Join-Path $sessionRoot $backupRelative) $sessionRoot
        Write-Atomic $backup $item.Old $sessionRoot
        if ((Get-Hash $backup) -cne $item.OldHash) { throw 'Original backup verification failed; client files were not changed.' }
    }
    $entries += [ordered]@{RelativePath=$item.Path; Existed=$item.Existed; Changed=$item.Changed; BackupRelative=$backupRelative; OriginalSHA256=$item.OldHash; InstalledSHA256=$item.NewHash}
}
$manifest = [ordered]@{Product=$product; Version=1; InstallationId=$id; CreatedAt=[DateTimeOffset]::Now.ToString('o'); ClientGameRoot=$game; AssetsRoot=$assets; Status='Prepared'; Files=$entries; UserdataTouched=$false}
Save-Manifest $manifest $manifestPath $sessionRoot
foreach ($item in $plan) {
    $null = Assert-Path $item.Target $targetRoot
    if ($item.Existed) {
        if (-not (Test-Path -LiteralPath $item.Target -PathType Leaf) -or (Get-Hash $item.Target) -cne $item.OldHash) { throw ('A target changed after backup. Stop and inspect the prepared manifest: ' + $manifestPath) }
    } elseif (Test-Path -LiteralPath $item.Target) { throw ('A target appeared during installation. Inspect the prepared manifest: ' + $manifestPath) }
    if ($item.Changed) { Write-Atomic $item.Target $item.New $targetRoot }
    if ((Get-Hash $item.Target) -cne $item.NewHash) { throw ('Installed hash mismatch. Restore using: ' + $manifestPath) }
}
$manifest.Status = 'Installed'; Save-Manifest $manifest $manifestPath $sessionRoot
$activation = if ($RadioMenu) { 'Restart the client to reload the native radio resource. F9 opens the radio menu.' } else { 'Next client launch executes autoexec; in an already running client, execute exec ze_practice_keys once in the client console.' }
[pscustomobject]@{Status='Installed'; Changed=@($plan | Where-Object { $_.Changed }).Count; Files=$plan.Count; Manifest=$manifestPath; ClientGameRoot=$game; AssetsRoot=$assets; UserdataTouched=$false; Activation=$activation}
