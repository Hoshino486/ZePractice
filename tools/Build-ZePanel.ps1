#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Validate','Build','Verify')][string]$Action = 'Build',
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$TemporaryRoot = 'E:\mapper\chana\临时文件夹\ze-ui-access-20261011',
    [string]$ProjectRoot = '',
    [switch]$WorkshopBundle,
    [string]$ExtraAssetsRoot = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { }
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$ClientGameRoot = [IO.Path]::GetFullPath($ClientGameRoot)
$TemporaryRoot = [IO.Path]::GetFullPath($TemporaryRoot)
$addonName = if ($WorkshopBundle) { 'saibochana_Server' } else { 'ze_practice_ui' }
$sourceXml = Join-Path $ProjectRoot 'panorama\layout\custom_game\zepractice\main.xml'
$sourceCss = Join-Path $ProjectRoot 'panorama\styles\custom_game\zepractice\main.css'
$sourceRadioXml = Join-Path $ProjectRoot 'panorama\layout\custom_game\zepractice\radio.xml'
$sourceRadioCss = Join-Path $ProjectRoot 'panorama\styles\custom_game\zepractice\radio.css'
$sourceMovementXml = Join-Path $ProjectRoot 'panorama\layout\custom_game\zepractice\movement.xml'
$sourceMovementCss = Join-Path $ProjectRoot 'panorama\styles\custom_game\zepractice\movement.css'
$sourceDiagnosticsXml = Join-Path $ProjectRoot 'panorama\layout\custom_game\zepractice\diagnostics.xml'
$sourceDiagnosticsCss = Join-Path $ProjectRoot 'panorama\styles\custom_game\zepractice\diagnostics.css'
$contentParent = Join-Path (Split-Path -Parent $ClientGameRoot) 'content\csgo_addons'
$contentAddon = Join-Path $contentParent $addonName
$gameParent = Join-Path $ClientGameRoot 'csgo_addons'
$gameAddon = Join-Path $gameParent $addonName
$buildRoot = Join-Path $TemporaryRoot 'build'
$stage = Join-Path $buildRoot $addonName
$manifestPath = Join-Path $buildRoot 'asset-manifest.json'
$vpkPath = Join-Path $buildRoot ($addonName + '.vpk')
$files = @('panorama\layout\custom_game\zepractice\main.vxml_c','panorama\styles\custom_game\zepractice\main.vcss_c','panorama\layout\custom_game\zepractice\radio.vxml_c','panorama\styles\custom_game\zepractice\radio.vcss_c','addoninfo.txt')
$extraFiles = @()
$files += @('panorama\layout\custom_game\zepractice\movement.vxml_c','panorama\styles\custom_game\zepractice\movement.vcss_c')
$files += @('panorama\layout\custom_game\zepractice\diagnostics.vxml_c','panorama\styles\custom_game\zepractice\diagnostics.vcss_c')
if ($WorkshopBundle) { $files += 'resource\ui\radiopanel.txt' }
else { $files += @('panorama\layout\hud\hudradio.vxml_c','panorama\scripts\custom_game\zepractice\radio_filter.vjs_c') }
if ($ExtraAssetsRoot) {
    if (-not $WorkshopBundle) { throw 'ExtraAssetsRoot requires WorkshopBundle.' }
    $ExtraAssetsRoot = [IO.Path]::GetFullPath($ExtraAssetsRoot).TrimEnd('\')
}
$utf8 = New-Object Text.UTF8Encoding($false, $true)

function Assert-Under([string]$Path, [string]$Root) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if ($absolute -ne $rootFull -and -not $absolute.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output path left its dedicated workspace.' }
    $cursor = $absolute
    while ($cursor.Length -ge $rootFull.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse-point output is not supported.' }
        }
        if ($cursor -eq $rootFull) { break }
        $cursor = Split-Path -Parent $cursor
    }
}
function Save-Text([string]$Path, [string]$Text, [string]$Root) {
    Assert-Under $Path $Root
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Assert-WorkshopAsset([string]$Relative) {
    $normalized = $Relative.Replace('\', '/').ToLowerInvariant()
    if ($normalized.StartsWith('panorama/layout/') -and -not $normalized.StartsWith('panorama/layout/custom_game/')) {
        throw ('Workshop addons may only add Panorama layouts under panorama/layout/custom_game/: ' + $Relative)
    }
}
function Assert-WorkshopTree([string]$Root) {
    if (-not $WorkshopBundle -or -not (Test-Path -LiteralPath $Root)) { return }
    $layoutRoot = Join-Path $Root 'panorama\layout'
    if (-not (Test-Path -LiteralPath $layoutRoot)) { return }
    Assert-Under $layoutRoot $Root
    foreach ($asset in @(Get-ChildItem -LiteralPath $layoutRoot -Recurse -File -Force)) {
        Assert-Under $asset.FullName $Root
        Assert-WorkshopAsset $asset.FullName.Substring($Root.TrimEnd('\').Length + 1)
    }
}
function Validate-Source {
    # Compilation alone does not enforce the client's restricted CustomHud schema.
    & (Join-Path $PSScriptRoot 'Validate-ZeCustomHud.ps1') -Path @($sourceXml, $sourceRadioXml, $sourceMovementXml, $sourceDiagnosticsXml)
    $xmlText = [IO.File]::ReadAllText($sourceXml, $utf8)
    $cssText = [IO.File]::ReadAllText($sourceCss, $utf8)
    $readerSettings = New-Object Xml.XmlReaderSettings
    $readerSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create((New-Object IO.StringReader($xmlText)), $readerSettings)
    try { $document = New-Object Xml.XmlDocument; $document.XmlResolver = $null; $document.Load($reader) } finally { $reader.Dispose() }
    if ($document.SelectNodes('//scripts|//script').Count -ne 0) { throw 'Custom HUD cannot depend on Panorama scripts.' }
    $ids = @{}
    foreach ($node in $document.SelectNodes('//*[@id]')) {
        $id = $node.GetAttribute('id')
        if ($ids.ContainsKey($id)) { throw ('Duplicate panel: ' + $id) }
        $ids[$id] = $node
    }
    $required = @('close','previous','next','state_god','state_noclip','tab_main','tab_practice','tab_weapons','tab_classes','tab_items','tab_maps','tab_admin') + @(0..9 | ForEach-Object { 'option_' + $_ })
    foreach ($id in $required) { if (-not $ids.ContainsKey($id) -or $ids[$id].Name -ne 'Button') { throw ('Missing native click button: ' + $id) } }
    foreach ($n in 0..9) { foreach ($prefix in @('card_text_','card_num_','card_hint_')) { if (-not $ids.ContainsKey($prefix + $n)) { throw 'Missing card dialog variable label.' } } }
    if (-not $ids['window'].GetAttribute('class').Split(' ').Contains('hidden')) { throw 'Panel must be hidden until its owner is selected.' }
    if ($cssText -notmatch '(?s)\.hidden\s*\{[^}]*visibility\s*:\s*collapse') { throw 'Missing hidden class.' }
    Write-Output ('PASS source structure: ' + $ids.Count + ' unique panels; ' + $required.Count + ' native click buttons; 10 cards; zero scripts.')
    $movementText = [IO.File]::ReadAllText($sourceMovementXml, $utf8)
    $movementReader = [Xml.XmlReader]::Create((New-Object IO.StringReader($movementText)), $readerSettings)
    try { $movement = New-Object Xml.XmlDocument; $movement.XmlResolver = $null; $movement.Load($movementReader) } finally { $movementReader.Dispose() }
    if ($movement.SelectNodes('//scripts|//script|//Button').Count -ne 0) { throw 'Movement HUD must not capture input or load scripts.' }
    foreach ($id in @('movement_window','movement_speed','movement_jump')) {
        if ($movement.SelectNodes("//*[@id='$id']").Count -ne 1) { throw ('Invalid movement HUD panel: ' + $id) }
    }
    if (-not $movement.SelectSingleNode("//*[@id='movement_window']").GetAttribute('class').Split(' ').Contains('hidden')) { throw 'Movement HUD must start hidden.' }
    if ([IO.File]::ReadAllText($sourceMovementCss, $utf8) -notmatch 'vertical-align:\s*bottom') { throw 'Movement HUD must remain at bottom.' }
    Write-Output 'PASS personal movement HUD: bottom-center, initially hidden, zero buttons/scripts.'
    $diagReader = [Xml.XmlReader]::Create((New-Object IO.StringReader([IO.File]::ReadAllText($sourceDiagnosticsXml,$utf8))),$readerSettings)
    try {$diag = New-Object Xml.XmlDocument; $diag.XmlResolver=$null; $diag.Load($diagReader)} finally {$diagReader.Dispose()}
    if($diag.SelectNodes('//scripts|//script|//Button').Count -ne 0){throw 'Diagnostics HUD must be read-only with no scripts/buttons.'}
    foreach($id in @('diagnostics_window','diag_entities','diag_networked','diag_script','diag_mean','diag_peak','diag_detail')) {
        if($diag.SelectNodes("//*[@id='$id']").Count -ne 1){throw ('Missing diagnostics panel: '+$id)}
    }
    if(-not $diag.SelectSingleNode("//*[@id='diagnostics_window']").GetAttribute('class').Split(' ').Contains('hidden')){throw 'Diagnostics HUD must start hidden.'}
    Write-Output 'PASS diagnostics HUD: independent custom_game layout, persistent-state labels, no scripts/buttons.'
}
if ($ExtraAssetsRoot) {
    if (-not (Test-Path -LiteralPath $ExtraAssetsRoot -PathType Container)) { throw 'ExtraAssetsRoot does not exist.' }
    Assert-Under $ExtraAssetsRoot $ExtraAssetsRoot
    foreach ($item in @(Get-ChildItem -LiteralPath $ExtraAssetsRoot -Recurse -Force)) {
        Assert-Under $item.FullName $ExtraAssetsRoot
        if ($item.PSIsContainer) { continue }
        $relative = $item.FullName.Substring($ExtraAssetsRoot.Length + 1)
        Assert-WorkshopAsset $relative
        if ($relative -notmatch '\A(models|materials|agents|characters|particles|sounds|soundevents|panorama|resource)\\' -or $relative -match '(?i)\.(vpk|dll|exe|ps1|cfg|bat|cmd)$' -or $files -contains $relative) { throw ('Invalid or conflicting extra asset: ' + $relative) }
        if ($relative -notmatch '(?i)(_c|\.png|\.jpg|\.jpeg|\.svg|\.ttf|\.txt|\.json)$') { throw ('Extra assets must be compiled resources or supported static assets: ' + $relative) }
        $extraFiles += $relative
    }
    $extraFiles = @($extraFiles | Sort-Object -Unique)
    $files += $extraFiles
}
if ($WorkshopBundle) { foreach ($relative in $files) { Assert-WorkshopAsset $relative } }
if (-not ('ZePanelBuildHash' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
public static class ZePanelBuildHash {
    public static uint Crc32(byte[] value) {
        uint crc = 0xffffffff;
        foreach (byte b in value) {
            crc ^= b;
            for (int n=0; n<8; n++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0U : 0xedb88320U);
        }
        return ~crc;
    }
}
'@
}
function Hash-Bytes([byte[]]$Bytes, [string]$Algorithm = 'SHA256') {
    $hash = [Security.Cryptography.HashAlgorithm]::Create($Algorithm)
    try { return ,$hash.ComputeHash($Bytes) } finally { $hash.Dispose() }
}
function Hex([byte[]]$Bytes) { return [BitConverter]::ToString($Bytes).Replace('-', '') }
function Write-ZeroString([IO.BinaryWriter]$Writer, [string]$Value) { $Writer.Write($utf8.GetBytes($Value)); $Writer.Write([byte]0) }
function Build-Vpk {
    $entries = @(); [uint32]$offset = 0
    foreach ($relative in $files) {
        $data = [IO.File]::ReadAllBytes((Join-Path $stage $relative))
        $entries += [pscustomobject]@{Path=$relative; Ext=[IO.Path]::GetExtension($relative).Substring(1); Dir=(Split-Path -Parent $relative).Replace('\','/'); Name=[IO.Path]::GetFileNameWithoutExtension($relative); Data=$data; Offset=$offset}
        $offset += [uint32]$data.Length
    }
    $treeStream = New-Object IO.MemoryStream
    $treeWriter = New-Object IO.BinaryWriter($treeStream)
    foreach ($ext in @($entries.Ext | Sort-Object -Unique)) {
        Write-ZeroString $treeWriter $ext
        foreach ($dir in @($entries | Where-Object { $_.Ext -eq $ext } | ForEach-Object { $_.Dir } | Sort-Object -Unique)) {
            if ([string]::IsNullOrEmpty($dir)) { Write-ZeroString $treeWriter ' ' } else { Write-ZeroString $treeWriter $dir }
            foreach ($entry in @($entries | Where-Object { $_.Ext -eq $ext -and $_.Dir -eq $dir } | Sort-Object Name)) {
                Write-ZeroString $treeWriter $entry.Name
                $treeWriter.Write([uint32][ZePanelBuildHash]::Crc32($entry.Data)); $treeWriter.Write([uint16]0); $treeWriter.Write([uint16]32767)
                $treeWriter.Write([uint32]$entry.Offset); $treeWriter.Write([uint32]$entry.Data.Length); $treeWriter.Write([uint16]65535)
            }
            $treeWriter.Write([byte]0)
        }
        $treeWriter.Write([byte]0)
    }
    $treeWriter.Write([byte]0); $treeWriter.Flush(); $tree = $treeStream.ToArray(); $treeWriter.Dispose(); $treeStream.Dispose()
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    $writer.Write([uint32]0x55aa1234); $writer.Write([uint32]2); $writer.Write([uint32]$tree.Length); $writer.Write([uint32]$offset)
    $writer.Write([uint32]0); $writer.Write([uint32]48); $writer.Write([uint32]0); $writer.Write([byte[]]$tree)
    foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Data) }
    $writer.Write([byte[]](Hash-Bytes $tree 'MD5')); $writer.Write([byte[]](Hash-Bytes ([byte[]]@()) 'MD5')); $writer.Flush()
    $writer.Write([byte[]](Hash-Bytes $stream.ToArray() 'MD5')); $writer.Flush()
    Assert-Under $vpkPath $buildRoot
    [IO.File]::WriteAllBytes($vpkPath, $stream.ToArray()); $writer.Dispose(); $stream.Dispose()
}
function Verify-Vpk {
    $bytes = [IO.File]::ReadAllBytes($vpkPath)
    $stream = New-Object IO.MemoryStream(,$bytes)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt32() -ne 0x55aa1234 -or $reader.ReadUInt32() -ne 2) { throw 'VPK header invalid.' }
        $treeLength = $reader.ReadUInt32(); $dataLength = $reader.ReadUInt32()
        if ($reader.ReadUInt32() -ne 0 -or $reader.ReadUInt32() -ne 48 -or $reader.ReadUInt32() -ne 0) { throw 'Unexpected VPK sections.' }
        if ($bytes.Length -ne 28 + $treeLength + $dataLength + 48) { throw 'VPK length invalid.' }
        function Read-ZeroString {
            $chars = New-Object 'Collections.Generic.List[byte]'
            while (($value = $reader.ReadByte()) -ne 0) { $chars.Add($value); if ($chars.Count -gt 4096) { throw 'VPK string invalid.' } }
            return $utf8.GetString($chars.ToArray())
        }
        $found = @{}
        while (($ext = Read-ZeroString).Length -gt 0) {
            while (($dir = Read-ZeroString).Length -gt 0) {
                while (($name = Read-ZeroString).Length -gt 0) {
                    $crc = $reader.ReadUInt32(); $preload = $reader.ReadUInt16(); $archive = $reader.ReadUInt16(); $offset = $reader.ReadUInt32(); $length = $reader.ReadUInt32(); $terminator = $reader.ReadUInt16()
                    if ($preload -ne 0 -or $archive -ne 32767 -or $terminator -ne 65535 -or ([long]$offset + $length) -gt $dataLength) { throw 'VPK directory entry invalid.' }
                    $relative = if ($dir -eq ' ') { $name + '.' + $ext } else { $dir.Replace('/','\') + '\' + $name + '.' + $ext }
                    if ($files -notcontains $relative -or $found.ContainsKey($relative)) { throw 'VPK contains unexpected or duplicate file.' }
                    $payload = New-Object byte[] $length
                    [Buffer]::BlockCopy($bytes, [int](28 + $treeLength + $offset), $payload, 0, [int]$length)
                    if ([ZePanelBuildHash]::Crc32($payload) -ne $crc -or (Hex (Hash-Bytes $payload)) -ne (Get-FileHash -LiteralPath (Join-Path $stage $relative) -Algorithm SHA256).Hash) { throw 'VPK payload verification failed.' }
                    $found[$relative] = $true
                }
            }
        }
        if ($stream.Position -ne 28 + $treeLength -or $found.Count -ne $files.Count) { throw 'VPK tree boundary/count invalid.' }
        $prefix = New-Object byte[] ($bytes.Length - 16); [Buffer]::BlockCopy($bytes, 0, $prefix, 0, $prefix.Length)
        $last = New-Object byte[] 16; [Buffer]::BlockCopy($bytes, $bytes.Length - 16, $last, 0, 16)
        if ((Hex (Hash-Bytes $prefix 'MD5')) -ne (Hex $last)) { throw 'VPK whole-file MD5 invalid.' }
        Write-Output ('PASS VPK: ' + $found.Count + ' files; CRC32, SHA256 and whole-file MD5 verified; radio layout adapter only, no maps.')
    } finally { $reader.Dispose(); $stream.Dispose() }
}

Validate-Source
if ($Action -eq 'Validate') { return }
if ($Action -eq 'Verify') {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.Addon -cne $addonName -or (Get-FileHash -LiteralPath $vpkPath -Algorithm SHA256).Hash -cne $manifest.VpkSHA256) { throw 'VPK manifest does not match this bundle.' }
    if (@($manifest.Files).Count -ne $files.Count) { throw 'Manifest file count differs; supply the same ExtraAssetsRoot used by the build.' }
    Assert-WorkshopTree $stage
    Assert-WorkshopTree $gameAddon
    Assert-WorkshopTree $contentAddon
    foreach ($entry in $manifest.Files) {
        $path = Join-Path $stage $entry.Path; Assert-Under $path $stage
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) { throw 'Staged asset changed after build.' }
    }
    Verify-Vpk
    Write-Output ('PASS manifest: ' + $manifestPath)
    return
}
$compiler = Join-Path $ClientGameRoot 'bin\win64\resourcecompiler.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw ('Resource compiler missing: ' + $compiler) }
Assert-Under $contentAddon $contentParent; Assert-Under $gameAddon $gameParent; Assert-Under $stage $TemporaryRoot
$session = Join-Path $buildRoot ('compiler-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Assert-Under $session $TemporaryRoot
[IO.Directory]::CreateDirectory($session) | Out-Null
$inputXml = Join-Path $contentAddon 'panorama\layout\custom_game\zepractice\main.vxml'
$inputCss = Join-Path $contentAddon 'panorama\styles\custom_game\zepractice\main.vcss'
Save-Text $inputXml ([IO.File]::ReadAllText($sourceXml, $utf8)) $contentAddon
Save-Text $inputCss ([IO.File]::ReadAllText($sourceCss, $utf8)) $contentAddon
$inputRadioXml = Join-Path $contentAddon 'panorama\layout\custom_game\zepractice\radio.vxml'
$inputRadioCss = Join-Path $contentAddon 'panorama\styles\custom_game\zepractice\radio.vcss'
$radioXml = [IO.File]::ReadAllText($sourceRadioXml, $utf8)
$radioCss = [IO.File]::ReadAllText($sourceRadioCss, $utf8)
if ($WorkshopBundle) {
    $radioXml = $radioXml.Replace('text="9. 返回主菜单"', 'text="9. 关闭全部（或聊天 !9）"').Replace('text="0. 关闭"', 'text="聊天 !0 / !ze_ui：完整关闭"')
    $radioCss = $radioCss.Replace('margin-left: 10px;', 'horizontal-align: right; margin-right: 20px;')
}
Save-Text $inputRadioXml $radioXml $contentAddon
Save-Text $inputRadioCss $radioCss $contentAddon
$inputMovementXml = Join-Path $contentAddon 'panorama\layout\custom_game\zepractice\movement.vxml'
$inputMovementCss = Join-Path $contentAddon 'panorama\styles\custom_game\zepractice\movement.vcss'
Save-Text $inputMovementXml ([IO.File]::ReadAllText($sourceMovementXml, $utf8)) $contentAddon
Save-Text $inputMovementCss ([IO.File]::ReadAllText($sourceMovementCss, $utf8)) $contentAddon
$inputDiagnosticsXml = Join-Path $contentAddon 'panorama\layout\custom_game\zepractice\diagnostics.vxml'
$inputDiagnosticsCss = Join-Path $contentAddon 'panorama\styles\custom_game\zepractice\diagnostics.vcss'
Save-Text $inputDiagnosticsXml ([IO.File]::ReadAllText($sourceDiagnosticsXml,$utf8)) $contentAddon
Save-Text $inputDiagnosticsCss ([IO.File]::ReadAllText($sourceDiagnosticsCss,$utf8)) $contentAddon
$inputNativeRadio = Join-Path $contentAddon 'panorama\layout\hud\hudradio.vxml'
$inputRadioFilter = Join-Path $contentAddon 'panorama\scripts\custom_game\zepractice\radio_filter.vjs'
if (-not $WorkshopBundle) {
    Save-Text $inputNativeRadio ([IO.File]::ReadAllText((Join-Path $ProjectRoot 'panorama\layout\hud\hudradio.xml'), $utf8)) $contentAddon
    Save-Text $inputRadioFilter ([IO.File]::ReadAllText((Join-Path $ProjectRoot 'panorama\scripts\custom_game\zepractice\radio_filter.js'), $utf8)) $contentAddon
} else {
    foreach ($retired in @(
        @{Path=$inputNativeRadio; Source=(Join-Path $ProjectRoot 'panorama\layout\hud\hudradio.xml')},
        @{Path=$inputRadioFilter; Source=(Join-Path $ProjectRoot 'panorama\scripts\custom_game\zepractice\radio_filter.js')}
    )) {
        Assert-Under $retired.Path $contentAddon
        if (-not (Test-Path -LiteralPath $retired.Path -PathType Leaf)) { continue }
        if ([IO.File]::ReadAllText($retired.Path, $utf8) -cne [IO.File]::ReadAllText($retired.Source, $utf8)) { throw ('Retired addon source was edited outside the builder: ' + $retired.Path) }
        $archived = Join-Path $session ('retired-source\' + $retired.Path.Substring($contentAddon.Length + 1))
        Assert-Under $archived $session
        [IO.Directory]::CreateDirectory((Split-Path -Parent $archived)) | Out-Null
        Move-Item -LiteralPath $retired.Path -Destination $archived
    }
}
Save-Text (Join-Path $contentAddon 'panorama\preprocessor_config.txt') '"PanzipCfg" { "BlockDefs" { } }' $contentAddon
$addonInfo = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->' + [Environment]::NewLine + '{ IsPlayable = false Panorama = { AllowCustomGameUI = true AddonLayoutPath = "panorama/layout/custom_game/" } }'
Save-Text (Join-Path $contentAddon 'addoninfo.txt') $addonInfo $contentAddon
Save-Text (Join-Path $gameAddon 'addoninfo.txt') $addonInfo $gameAddon
if ($WorkshopBundle) {
    $radioTable = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'client\resource\ui\radiopanel.txt'), $utf8).Replace('__ZE_DYNAMIC_INPUT__', '菜单快捷键（实际选项见右侧）')
    Save-Text (Join-Path $gameAddon 'resource\ui\radiopanel.txt') $radioTable $gameAddon
}
$stdout = Join-Path $session 'stdout.log'; $stderr = Join-Path $session 'stderr.log'
$argsText = '-game "' + (Join-Path $ClientGameRoot 'csgo') + '" -i "' + $inputCss + '" -i "' + $inputXml + '" -f -nop4 -v'
$argsText += ' -i "' + $inputRadioCss + '" -i "' + $inputRadioXml + '"'
$argsText += ' -i "' + $inputMovementCss + '" -i "' + $inputMovementXml + '"'
$argsText += ' -i "' + $inputDiagnosticsCss + '" -i "' + $inputDiagnosticsXml + '"'
if (-not $WorkshopBundle) { $argsText += ' -i "' + $inputRadioFilter + '" -i "' + $inputNativeRadio + '"' }
$previousTemp = $env:TEMP; $previousTmp = $env:TMP
try {
    $env:TEMP = $session; $env:TMP = $session
    $process = Start-Process -FilePath $compiler -ArgumentList $argsText -WorkingDirectory (Split-Path -Parent $compiler) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
} finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
$compilerOutput = (Get-Content -LiteralPath $stdout -Raw -Encoding UTF8) + (Get-Content -LiteralPath $stderr -Raw -Encoding UTF8)
if ($process.ExitCode -ne 0 -or $compilerOutput -match '(?im)^.*(ERROR:|failed to compile|error compiling|parse error)') { throw ('Resource compilation failed. Read compiler logs at: ' + $session) }
if ($WorkshopBundle -and (Test-Path -LiteralPath $manifestPath)) {
    $previous = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($previous.Addon -cne $addonName) { throw 'OutputRoot belongs to another addon; choose a separate output directory.' }
    $obsolete = @()
    foreach ($entry in @($previous.Files | Where-Object { $files -notcontains $_.Path })) {
        foreach ($folder in @($stage, $gameAddon)) {
            $oldPath = Join-Path $folder $entry.Path
            Assert-Under $oldPath $folder
            if (Test-Path -LiteralPath $oldPath -PathType Leaf) {
                if ((Get-FileHash -LiteralPath $oldPath).Hash -cne $entry.SHA256) { throw ('Retired asset was edited outside the builder: ' + $oldPath) }
                $obsolete += $oldPath
            }
        }
    }
    foreach ($oldPath in $obsolete) { Remove-Item -LiteralPath $oldPath }
}
foreach ($relative in $files) {
    $isExtra = $extraFiles -contains $relative
    $from = if ($isExtra) { Join-Path $ExtraAssetsRoot $relative } else { Join-Path $gameAddon $relative }
    if (-not (Test-Path -LiteralPath $from -PathType Leaf) -or (Get-Item -LiteralPath $from).Length -lt 1) { throw ('Compiler output missing/empty: ' + $from) }
    if (-not $isExtra -and $relative.EndsWith('_c') -and (Get-Item -LiteralPath $from).LastWriteTimeUtc -lt (Get-Item -LiteralPath $inputXml).LastWriteTimeUtc.AddSeconds(-2)) { throw 'Compiler output is stale.' }
    $to = Join-Path $stage $relative; Assert-Under $to $stage
    [IO.Directory]::CreateDirectory((Split-Path -Parent $to)) | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force
    if ($isExtra) {
        $publishTarget = Join-Path $gameAddon $relative; Assert-Under $publishTarget $gameAddon
        [IO.Directory]::CreateDirectory((Split-Path -Parent $publishTarget)) | Out-Null
        Copy-Item -LiteralPath $from -Destination $publishTarget -Force
    }
}
Assert-WorkshopTree $stage
Assert-WorkshopTree $gameAddon
Assert-WorkshopTree $contentAddon
Build-Vpk
Verify-Vpk
$manifest = [ordered]@{
    Addon = $addonName; BuiltAt = [DateTimeOffset]::Now.ToString('o'); Layout = 'panorama/layout/custom_game/zepractice/main.vxml'
    ClientDeploymentPerformed = $false; ServerDeploymentPerformed = $false; WorkshopPublished = $false
    WorkshopBundle = [bool]$WorkshopBundle; ExtraAssetsRoot = $ExtraAssetsRoot
    PublishAddonDirectory = $gameAddon
    Compatibility = $(if ($WorkshopBundle) { 'Only custom_game layouts; no native HUD override or radio-filter script. Native radio input remains visible; press 9 to close the dynamic panel. Workshop download, input and secure-server reconnect still require live validation.' } else { 'Development UI bundle.' })
    Compiler = $compiler; CompilerLogDirectory = $session
    Files = @($files | ForEach-Object { $path = Join-Path $stage $_; [ordered]@{Path=$_; Bytes=(Get-Item -LiteralPath $path).Length; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} })
    Vpk = $vpkPath; VpkSHA256 = (Get-FileHash -LiteralPath $vpkPath -Algorithm SHA256).Hash
}
Save-Text $manifestPath ($manifest | ConvertTo-Json -Depth 8) $buildRoot
Write-Output ('PASS compiled asset bundle: ' + $stage)
Write-Output ('Manifest: ' + $manifestPath)
Write-Output 'No csgo root deployment, gameinfo edits, original map edits, Workshop publication, or server state changes were performed.'
