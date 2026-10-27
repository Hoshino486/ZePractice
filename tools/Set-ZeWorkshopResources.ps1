#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerGameRoot,
    [string]$WorkshopId = '',
    [string]$PackageZip = '',
    [switch]$Disable,
    [switch]$StatusOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Disable -and $WorkshopId) { throw 'Use -Disable to pause the recorded resource ID, or -WorkshopId to enable an ID, not both.' }
$utf8 = New-Object Text.UTF8Encoding($false)
$game = [IO.Path]::GetFullPath($ServerGameRoot).TrimEnd('\')
$root = Join-Path $game 'csgo'
$cfgPath = Join-Path $root 'cfg\multiaddonmanager\multiaddonmanager.cfg'
$statePath = Join-Path $root 'cfg\multiaddonmanager\ze-resources.state.json'
$expectedZip = 'D6BD19687788DD8BA20DBF7EE390ED234C4FFBA459C70D43CF2A4E876556AF68'
function Assert-Target([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Target escaped the selected dedicated server.' }
    $cursor = $full
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse-point targets are not supported.' }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}
function Read-AddonIds([string]$Text) {
    $matches = [regex]::Matches($Text, '(?im)^\s*mm_extra_addons\s+"([0-9,]*)"\s*(?://[^\r\n]*)?$')
    if ($matches.Count -eq 0) { return @() }
    return @($matches[$matches.Count-1].Groups[1].Value.Split(',') | Where-Object { $_ })
}
Assert-Target $cfgPath
Assert-Target $statePath
$config = if (Test-Path -LiteralPath $cfgPath) { [IO.File]::ReadAllText($cfgPath) } else { '' }
$currentId = ''
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $currentId = [string]$state.WorkshopId
}
$pluginPath = Join-Path $root 'addons\multiaddonmanager\bin\multiaddonmanager.dll'
$configured = [bool]$currentId -and (@(Read-AddonIds $config) -contains $currentId)
if ($StatusOnly) {
    [pscustomobject]@{Installed=(Test-Path -LiteralPath $pluginPath);WorkshopId=$currentId;Configured=$configured;ExtraAddons=@(Read-AddonIds $config);Config=$cfgPath;LiveValidation='Not established by this script'}
    return
}
if ($Disable -and -not $currentId) { throw 'No managed Workshop ID is recorded; no addon was disabled.' }
$exe = Join-Path $game 'bin\win64\cs2.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $root 'addons\metamod') -PathType Container)) { throw 'Select a dedicated server game root with Metamod already installed.' }
$running = @(Get-Process -Name cs2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe })
if ($running.Count) { throw 'Stop this dedicated server before changing addon distribution; addon changes can reload the map.' }
if ($WorkshopId) {
    [uint64]$parsed = 0
    if ($WorkshopId -notmatch '\A[1-9][0-9]{0,19}\z' -or -not [uint64]::TryParse($WorkshopId, [ref]$parsed)) { throw 'WorkshopId must be a real positive numeric published file ID.' }
}
$writes = @{}
if ($PackageZip) {
    if ((Get-FileHash -LiteralPath $PackageZip -Algorithm SHA256).Hash -cne $expectedZip) { throw 'MultiAddonManager package hash mismatch: this script is pinned to the inspected v1.6.2 Windows package.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackageZip))
    try {
        foreach ($relative in @('addons/metamod/multiaddonmanager.vdf','addons/multiaddonmanager/bin/multiaddonmanager.dll','cfg/multiaddonmanager/multiaddonmanager.cfg')) {
            $entry = $archive.GetEntry($relative)
            if ($null -eq $entry) { throw ('Package missing: ' + $relative) }
            $target = Join-Path $root $relative
            Assert-Target $target
            $inputStream = $entry.Open(); $memory = New-Object IO.MemoryStream
            try { $inputStream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $inputStream.Dispose(); $memory.Dispose() }
            if ($relative.EndsWith('.cfg')) {
                if (-not $config) { $config = $utf8.GetString($bytes) }
            } else {
                if (Test-Path -LiteralPath $target) {
                    $hash = [Security.Cryptography.SHA256]::Create()
                    try { $newHash = [BitConverter]::ToString($hash.ComputeHash($bytes)).Replace('-','') } finally { $hash.Dispose() }
                    if ((Get-FileHash -LiteralPath $target).Hash -cne $newHash) { throw 'Existing MultiAddonManager differs; review before replacing it.' }
                } else { $writes[$target] = $bytes }
            }
        }
    } finally { $archive.Dispose() }
}
if (-not (Test-Path -LiteralPath $pluginPath) -and -not $writes.ContainsKey($pluginPath.Replace('/','\'))) {
    $hasPlugin = @($writes.Keys | Where-Object { [IO.Path]::GetFullPath($_) -ieq $pluginPath }).Count -gt 0
    if (-not $hasPlugin) { throw 'Install the pinned plugin package with -PackageZip first.' }
}
if ($WorkshopId -or $Disable) {
    $directives = [regex]::Matches($config, '(?im)^\s*mm_extra_addons\b[^\r\n]*')
    foreach ($directive in $directives) {
        if ($directive.Value.Trim() -notmatch '\Amm_extra_addons\s+"[0-9,]*"\s*(?://[^\r\n]*)?\z') { throw 'Unsupported existing mm_extra_addons syntax; no files changed.' }
    }
    $ids = @(Read-AddonIds $config | Where-Object { $_ -ne $currentId })
    if ($WorkshopId) { $ids += $WorkshopId }
    $ids = @($ids | Select-Object -Unique)
    $config = [regex]::Replace($config, '(?ms)^// BEGIN ZE WORKSHOP RESOURCES\r?\n.*?^// END ZE WORKSHOP RESOURCES\r?\n?', '')
    $config = [regex]::Replace($config, '(?im)^[ \t]*mm_extra_addons\s+"[0-9,]*"[ \t]*(?://[^\r\n]*)?\r?\n?', '')
    if ($Disable) {
        $config = $config.TrimEnd() + "`r`n`r`n" + ('mm_extra_addons "' + ($ids -join ',') + '"') + "`r`n"
    } else {
        $config = $config.TrimEnd() + "`r`n`r`n" + (@(
            '// BEGIN ZE WORKSHOP RESOURCES',
            ('mm_extra_addons "' + ($ids -join ',') + '"'),
            'mm_addon_mount_download 1',
            'mm_cache_clients_with_addons 0',
            'mm_addon_connection_timeout 300',
            '// END ZE WORKSHOP RESOURCES'
        ) -join "`r`n") + "`r`n"
        $currentId = $WorkshopId
    }
}
if (-not $config) { throw 'MultiAddonManager configuration is missing.' }
$configured = [bool]$currentId -and (@(Read-AddonIds $config) -contains $currentId)
$validation = if ($configured) { 'Download and clean-client validation pending' } elseif ($currentId) { 'Disabled; download and clean-client validation pending' } else { 'Pending published ID and clean-client test' }
$writes[$cfgPath] = $utf8.GetBytes($config)
$writes[$statePath] = $utf8.GetBytes(([ordered]@{WorkshopId=$currentId;PackageVersion='v1.6.2';Configured=$configured;ClientFilesModified=$false;WorkshopPublishedByThisScript=$false;LiveValidation=$validation} | ConvertTo-Json))
$changed = @($writes.Keys | Where-Object { -not (Test-Path -LiteralPath $_) -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($_)) -cne [Convert]::ToBase64String($writes[$_]) })
$backup = Join-Path $root ('cfg\multiaddonmanager\backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$records = @()
foreach ($target in $changed) {
    Assert-Target $target
    $relative = [IO.Path]::GetFullPath($target).Substring($root.Length + 1)
    $existed = Test-Path -LiteralPath $target
    if ($existed) {
        $original = Join-Path $backup $relative
        Assert-Target $original
        [IO.Directory]::CreateDirectory((Split-Path -Parent $original)) | Out-Null
        Copy-Item -LiteralPath $target -Destination $original
    }
    $records += [ordered]@{Relative=$relative;Existed=$existed}
}
if ($changed.Count) {
    Assert-Target (Join-Path $backup 'manifest.json')
    [IO.Directory]::CreateDirectory($backup) | Out-Null
    [IO.File]::WriteAllText((Join-Path $backup 'manifest.json'), ($records | ConvertTo-Json -Depth 4), $utf8)
    foreach ($target in $changed) {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        [IO.File]::WriteAllBytes($target, $writes[$target])
    }
}
[pscustomobject]@{Changed=$changed.Count;WorkshopId=$currentId;Installed=(Test-Path -LiteralPath $pluginPath);Configured=$configured;Backup=$(if ($changed.Count) {$backup} else {''});ClientFilesModified=$false;ServerStarted=$false}
