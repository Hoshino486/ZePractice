#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ManifestPath='E:\mapper\chana\临时文件夹\ze-practice-build-20261011\backup\installed-files.json',
    [ValidateSet('Plugin','AllTracked')][string]$Scope='Plugin',
    [switch]$Apply,
    [switch]$KeepModified
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Deliberately fixed to this installation. Never accept an arbitrary deletion root from the manifest.
$serverHome='E:\mapper\chana'
$gameRoot=Join-Path $serverHome 'steamapps\common\Counter-Strike Global Offensive\game'
$projectRoot=Join-Path $serverHome 'ZePractice'
$cssRoot=Join-Path $gameRoot 'csgo\addons\counterstrikesharp'
$diagnosticsRoot=Join-Path $serverHome '临时文件夹\ze-practice-build-20261011'
function Get-Full([string]$Path) {
    if([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)){throw 'An absolute nonempty path is required.'}
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    if($full.StartsWith('\\') -or $full.Substring(2).Contains(':')){throw 'UNC, device paths, and alternate streams are not accepted.'}
    return $full
}
function Is-Under([string]$Path,[string]$Root) {
    $full=Get-Full $Path; $base=Get-Full $Root
    return $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoLink([string]$Path) {
    $cursor=Get-Full $Path
    while($cursor) {
        if(Test-Path -LiteralPath $cursor) {
            $item=Get-Item -LiteralPath $cursor -Force
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Reparse point/junction is not accepted: '+$cursor)}
        }
        $next=[IO.Path]::GetDirectoryName($cursor)
        if($next -eq $cursor){break}; $cursor=$next
    }
}
$allowedRoots=@(
    $projectRoot,
    $cssRoot,
    (Join-Path $gameRoot 'csgo\addons\StripperCS2'),
    (Join-Path $gameRoot 'csgo\cfg\ze-practice'),
    (Join-Path $gameRoot 'csgo_addons\ze_forhidden_slicoo_cn')
)
$allowedExact=@(
    (Join-Path $gameRoot 'csgo\addons\metamod\counterstrikesharp.vdf'),
    (Join-Path $gameRoot 'csgo\addons\metamod\StripperCS2.vdf'),
    (Join-Path $gameRoot 'csgo\addons\cs2fixes\configs\admins.jsonc'),
    (Join-Path $gameRoot 'csgo\addons\cs2fixes\configs\maplist.jsonc'),
    (Join-Path $gameRoot 'csgo\maps\ze_forhidden_slicoo_cn.vpk'),
    (Join-Path $gameRoot 'csgo\maps\ze_forhidden_slicoo_cn_skybox.vpk')
)
$pluginRoots=@(
    $projectRoot,
    (Join-Path $cssRoot 'plugins\ZePractice'),
    (Join-Path $cssRoot 'configs\plugins\ZePractice'),
    (Join-Path $gameRoot 'csgo\cfg\ze-practice')
)
function Is-Allowed([string]$Target) {
    if(-not(Is-Under $Target $serverHome)){return $false}
    foreach($root in $allowedRoots){if(Is-Under $Target $root){return $true}}
    foreach($path in $allowedExact){if($Target.Equals($path,[StringComparison]::OrdinalIgnoreCase)){return $true}}
    return $false
}
function Is-InScope([string]$Target) {
    if($Scope -eq 'AllTracked'){return $true}
    foreach($root in $pluginRoots){if(Is-Under $Target $root){return $true}}
    return $false
}
$manifest=Get-Full $ManifestPath
if(-not(Is-Under $manifest $diagnosticsRoot) -or [IO.Path]::GetFileName($manifest) -ne 'installed-files.json'){throw 'Manifest must be installed-files.json under this deployment diagnostics directory.'}
Assert-NoLink $manifest
if(-not(Test-Path -LiteralPath $manifest -PathType Leaf)){throw ('Manifest not found: '+$manifest)}
$manifestHash=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
$backupRoot=[IO.Path]::GetDirectoryName($manifest)
$originalsRoot=Join-Path $backupRoot 'originals'
$parsedManifest=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$entries=@($parsedManifest)
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$plans=[Collections.Generic.List[object]]::new()
$outsideScope=0
foreach($entry in $entries) {
    foreach($field in @('Target','Existed','Backup','OldSHA256','NewSHA256')) {
        if(-not($entry.PSObject.Properties.Name -contains $field)){throw ('Manifest record missing '+$field)}
    }
    if($entry.Existed -isnot [bool]){throw 'Manifest Existed must be Boolean.'}
    $target=Get-Full ([string]$entry.Target)
    if(-not(Is-Allowed $target)){throw ('Target is outside this project allow-list: '+$target)}
    if(-not $seen.Add($target)){throw ('Duplicate target in manifest: '+$target)}
    Assert-NoLink $target
    if(-not(Is-InScope $target)){$outsideScope++;continue}
    if([string]$entry.NewSHA256 -notmatch '^[0-9A-Fa-f]{64}$'){throw ('Missing valid deployed hash: '+$target)}
    $backup=$null
    if($entry.Existed) {
        if([string]$entry.OldSHA256 -notmatch '^[0-9A-Fa-f]{64}$'){throw ('Missing original hash: '+$target)}
        $backup=Get-Full ([string]$entry.Backup)
        $expected=Get-Full (Join-Path $originalsRoot $target.Substring(($serverHome+'\').Length))
        if(-not(Is-Under $backup $originalsRoot) -or -not $backup.Equals($expected,[StringComparison]::OrdinalIgnoreCase)){throw ('Backup path mismatch: '+$target)}
        Assert-NoLink $backup
        if(-not(Test-Path -LiteralPath $backup -PathType Leaf)){throw ('Original backup missing: '+$backup)}
        if((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $entry.OldSHA256){throw ('Original backup hash mismatch: '+$backup)}
    }
    if(Test-Path -LiteralPath $target -PathType Container){throw ('File target is now a directory: '+$target)}
    $hash=$null
    if(Test-Path -LiteralPath $target -PathType Leaf){$hash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash}
    $status=''; $action='None'
    if($entry.Existed -and $hash -eq $entry.OldSHA256){$status='AlreadyOriginal'}
    elseif(-not $entry.Existed -and $null -eq $hash){$status='AlreadyAbsent'}
    elseif($null -eq $hash){$status='MissingOriginalTarget';$action='Restore'}
    elseif($hash -ne $entry.NewSHA256){$status='ModifiedAfterDeployment'}
    elseif($entry.Existed){$status='Ready';$action='Restore'}
    else{$status='Ready';$action='Remove'}
    $plans.Add([pscustomobject]@{Target=$target;Action=$action;Status=$status;ObservedSHA256=$hash;ExpectedNewSHA256=$entry.NewSHA256;OldSHA256=$entry.OldSHA256;Backup=$backup;Existed=$entry.Existed})
}
$blocked=@($plans|Where-Object Status -eq 'ModifiedAfterDeployment')
$actions=@($plans|Where-Object Action -ne 'None')
Write-Host ('Mode: '+$(if($Apply){'APPLY'}else{'PREVIEW ONLY'})+' | Scope: '+$Scope)
Write-Host ('Manifest records: '+$entries.Count+' | In scope: '+$plans.Count+' | Other scope left untouched: '+$outsideScope)
$plans | Group-Object Status,Action | Select-Object Count,Name | Format-Table -AutoSize | Out-Host
$actions | Select-Object -First 12 Action,Target | Format-Table -AutoSize | Out-Host
if($blocked.Count -gt 0) {
    Write-Warning ($blocked.Count.ToString()+' files differ from their recorded deployment hash; rollback will not overwrite these edits.')
    $blocked | Select-Object -First 30 Target,ObservedSHA256 | Format-List | Out-Host
}
Write-Host 'Only manifest-listed files are eligible. Directories, untracked files, runtime data, and gameinfo.gi are never deleted.'
Write-Host 'Plugin scope keeps CounterStrikeSharp, StripperCS2, map assets, native-loader VDFs, and shared CS2Fixes settings.'
if(-not $Apply){Write-Host 'No target files changed. Use -Apply only after reviewing this preview and stopping this dedicated server.';return}
if($blocked.Count -gt 0 -and -not $KeepModified){throw 'Apply aborted before any change: preserve/reconcile modified files first, or use -KeepModified to leave those files untouched.'}
if($blocked.Count -gt 0){Write-Warning ($blocked.Count.ToString()+' edited files will remain untouched; current server settings may still reference the plugin. Review them separately.')}
$serverExe=Join-Path $gameRoot 'bin\win64\cs2.exe'
if(Get-CimInstance Win32_Process -Filter "Name='cs2.exe'" | Where-Object {$_.ExecutablePath -eq $serverExe}){throw 'Apply refused: this dedicated server is running. Stop it normally yourself, then retry.'}
if($Scope -eq 'AllTracked') {
    $plugins=Join-Path $cssRoot 'plugins'
    if(Test-Path -LiteralPath $plugins) {
        $foreign=@(Get-ChildItem -LiteralPath $plugins -Directory | Where-Object { $_.Name -ne 'ZePractice' -and @(Get-ChildItem -LiteralPath $_.FullName -Filter '*.dll' -File -ErrorAction SilentlyContinue).Count -gt 0 })
        if($foreign.Count -gt 0){throw ('Other CounterStrikeSharp plugins are installed; retain shared dependencies with -Scope Plugin. Found: '+($foreign.Name -join ', '))}
    }
}
if((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $manifestHash){throw 'Manifest changed during preview; retry.'}
# Revalidate EVERY target before the first mutation; do not remove a newly modified file.
foreach($item in $actions) {
    Assert-NoLink $item.Target
    $now=$null; if(Test-Path -LiteralPath $item.Target -PathType Leaf){$now=(Get-FileHash -LiteralPath $item.Target -Algorithm SHA256).Hash}
    if($now -ne $item.ObservedSHA256){throw ('Target changed during preview: '+$item.Target)}
    if($item.Action -eq 'Restore') {
        Assert-NoLink $item.Backup
        if((Get-FileHash -LiteralPath $item.Backup -Algorithm SHA256).Hash -ne $item.OldSHA256){throw ('Backup changed during preview: '+$item.Backup)}
    }
}
$session=Join-Path $diagnosticsRoot ('rollback\'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($session)|Out-Null
$journal=[Collections.Generic.List[object]]::new()
$journalPath=Join-Path $session 'rollback-journal.json'
foreach($item in $actions) {
    Assert-NoLink $item.Target
    $now=$null; if(Test-Path -LiteralPath $item.Target -PathType Leaf){$now=(Get-FileHash -LiteralPath $item.Target -Algorithm SHA256).Hash}
    if($now -ne $item.ObservedSHA256){throw ('Target changed immediately before action. Prior actions are recorded in '+$journalPath+': '+$item.Target)}
    $saved=$null
    if($null -ne $now) {
        $saved=Join-Path $session ('before\'+$item.Target.Substring(($serverHome+'\').Length))
        if(-not(Is-Under $saved $session)){throw 'Rollback journal backup escaped session.'}
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null
        Copy-Item -LiteralPath $item.Target -Destination $saved
        if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $now){throw 'Rollback safety copy hash mismatch.'}
    }
    $record=[pscustomobject]@{Target=$item.Target;Action=$item.Action;BeforeSHA256=$now;BeforeCopy=$saved;Completed=$false}
    $journal.Add($record)
    [IO.File]::WriteAllText($journalPath,(ConvertTo-Json -InputObject @($journal.ToArray()) -Depth 5),[Text.UTF8Encoding]::new($false))
    if($item.Action -eq 'Restore') {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($item.Target))|Out-Null
        Copy-Item -LiteralPath $item.Backup -Destination $item.Target -Force
        if((Get-FileHash -LiteralPath $item.Target -Algorithm SHA256).Hash -ne $item.OldSHA256){throw ('Restored hash mismatch: '+$item.Target)}
    } elseif($item.Action -eq 'Remove') {
        # Exactly one verified file; deliberately no -Recurse and no directory deletion.
        Remove-Item -LiteralPath $item.Target -Force
        if(Test-Path -LiteralPath $item.Target){throw ('Removal verification failed: '+$item.Target)}
    }
    $record.Completed=$true
    [IO.File]::WriteAllText($journalPath,(ConvertTo-Json -InputObject @($journal.ToArray()) -Depth 5),[Text.UTF8Encoding]::new($false))
}
Write-Host ('Completed '+$journal.Count+' file actions. Verified originals / before-rollback copies and journal: '+$session)

