#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Installer = '',
    [string]$AssetsRoot = 'E:\mapper\chana\临时文件夹\ze-ui-access-20261011\build\ze_practice_ui',
    [string]$TestRoot = 'E:\mapper\chana\临时文件夹\ze-ui-access-20261011\client-installer-tests'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { }
if ([string]::IsNullOrWhiteSpace($Installer)) { $Installer = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'tools\Install-ZeClient.ps1' }
$root = Join-Path $TestRoot ('ps' + $PSVersionTable.PSVersion.Major + '-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$backups = Join-Path $root '备份'; $source = Join-Path $root '资源'; $nl = [Environment]::NewLine
$utf8 = New-Object Text.UTF8Encoding($false); $bomUtf8 = New-Object Text.UTF8Encoding($true)
$paths = @('panorama\layout\custom_game\zepractice\main.vxml_c','panorama\styles\custom_game\zepractice\main.vcss_c')
foreach ($path in $paths) { $to = Join-Path $source $path; [IO.Directory]::CreateDirectory((Split-Path -Parent $to)) | Out-Null; Copy-Item -LiteralPath (Join-Path $AssetsRoot $path) -Destination $to }
[IO.File]::WriteAllText((Join-Path $source 'must-not-copy.txt'), 'never install this file')
$script:checks = 0
function Check([bool]$Condition, [string]$Name) { if (-not $Condition) { throw ('FAIL ' + $Name) }; $script:checks++; Write-Output ('PASS ' + $Name) }
function Expect-Failure([scriptblock]$Action, [string]$Name) { $failed = $false; try { $null = & $Action } catch { $failed = $true }; Check $failed $Name }
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Text([string]$Path) { return [IO.File]::ReadAllText($Path, $utf8) }
function Fixture([string]$Name) {
    $game = Join-Path (Join-Path $root $Name) 'game'
    foreach ($relative in @('csgo\cfg','bin\win64','userdata\100\730\local\cfg')) { [IO.Directory]::CreateDirectory((Join-Path $game $relative)) | Out-Null }
    [IO.File]::WriteAllText((Join-Path $game 'bin\win64\cs2.exe'), 'NON-EXECUTABLE TEST FIXTURE ONLY')
    [IO.File]::WriteAllText((Join-Path $game 'userdata\100\730\local\cfg\cs2_user_keys_0_slot0.vcfg'), 'USERDATA MUST STAY UNCHANGED')
    return $game
}
function Install([string]$Game, [string]$From = $source) { return & $Installer -ClientGameRoot $Game -AssetsRoot $From -BackupRoot $backups }
function Restore([string]$Manifest) { return & $Installer -RestoreManifest $Manifest -WarningAction SilentlyContinue }
function Relative([string]$Game, [string]$Path) { return Join-Path (Join-Path $Game 'csgo') $Path }
$tokens = $null; $errors = $null; [Management.Automation.Language.Parser]::ParseFile($Installer,[ref]$tokens,[ref]$errors) | Out-Null
Check ($errors.Count -eq 0) 'installer AST has zero syntax errors'
$bytes = [IO.File]::ReadAllBytes($Installer); Check ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) 'installer has PS5 UTF8 BOM'

# Existing resources + UTF-8 cfg are fully reversible. No actual game process is started.
$game = Fixture '中文客户端'; $keys = Relative $game 'cfg\ze_practice_keys.cfg'; $auto = Relative $game 'cfg\autoexec.cfg'; $vxml = Relative $game $paths[0]; $vcss = Relative $game $paths[1]
[IO.Directory]::CreateDirectory((Split-Path -Parent $vxml)) | Out-Null; [IO.File]::WriteAllBytes($vxml,[byte[]]@(1,2,3))
$oldKeys = '// 原有快捷键：保持不变' + $nl + 'bind "F5" "ms_tp"' + $nl + 'bind "F7" "ms_c"' + $nl + 'bind "F8" "ms_dc"' + $nl
$oldAuto = '// 中文注释保持原字节' + $nl + 'bind "F1" "say hello"' + $nl + 'exec "ze_practice_keys.cfg"' + $nl + 'bind "SPACE" "+jump"' + $nl
[IO.File]::WriteAllText($keys,$oldKeys,$bomUtf8); [IO.File]::WriteAllText($auto,$oldAuto,$bomUtf8)
$oldKeyHash=Hash $keys; $oldAutoHash=Hash $auto; $oldVxmlHash=Hash $vxml; $userdata=Join-Path $game 'userdata\100\730\local\cfg\cs2_user_keys_0_slot0.vcfg';$userdataHash=Hash $userdata
$r = Install $game
Check ($r.Status -eq 'Installed' -and $r.Changed -eq 4 -and $r.Files -eq 4) 'only four owned files installed'
Check (-not $r.UserdataTouched) 'install explicitly reports userdata untouched'
Check ((Hash $vxml) -eq (Hash (Join-Path $source $paths[0]))) 'compiled XML copied exactly'
Check ((Hash $vcss) -eq (Hash (Join-Path $source $paths[1]))) 'compiled CSS copied exactly'
Check (-not (Test-Path -LiteralPath (Relative $game 'must-not-copy.txt'))) 'unrelated assets ignored'
Check ((Hash $userdata) -eq $userdataHash) 'key VCFG remains byte-for-byte untouched'
$keysText=Text $keys; $autoText=Text $auto
Check ($keysText.Contains('// 原有快捷键：保持不变')) 'UTF8 key comments preserved'
Check ($autoText.Contains('// 中文注释保持原字节')) 'UTF8 autoexec comments preserved'
foreach ($line in @('bind "F5" "ms_tp"','bind "F7" "ms_c"','bind "F8" "ms_dc"')) { Check ($keysText.Contains($line)) ('legacy binding preserved ' + $line) }
foreach ($line in @('bind "ALT" "css_ze_noclip"','bind "F9" "css_ze_ui"','bind "F11" "css_ze_god"','bind "INS" "css_ze_save"','bind "HOME" "css_ze_tp"','bind "END" "css_ze_clearcheckpoint"')) { Check ($keysText.Contains($line)) ('requested binding installed ' + $line) }
Check ([regex]::Matches($autoText,'(?im)^exec ze_practice_keys\r?$').Count -eq 1) 'one active standalone exec'
Check ($autoText.Contains('bind "SPACE" "+jump"') -and $autoText.Contains('bind "F1" "say hello"')) 'unrelated autoexec binds preserved'
$m = Get-Content -LiteralPath $r.Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($m.Files.Count -eq 4 -and $m.Status -eq 'Installed') 'manifest owns exactly four entries'
Check (@($m.Files | Where-Object { $_.InstalledSHA256 -notmatch '\A[0-9A-F]{64}\z' }).Count -eq 0) 'manifest contains four valid SHA256 values'
foreach ($entry in @($m.Files | Where-Object { $_.Existed })) { Check ((Hash (Join-Path (Split-Path -Parent $r.Manifest) $entry.BackupRelative)) -eq $entry.OriginalSHA256) ('original backed up: ' + $entry.RelativePath) }
$r2=Install $game; Check ($r2.Changed -eq 0) 'repeat installation is byte-idempotent'
$rr2=Restore $r2.Manifest; Check ($rr2.Restored -eq 0) 'no-change install cannot claim prior files'
$rr=Restore $r.Manifest; Check ($rr.Status -eq 'Restored' -and $rr.Restored -eq 4) 'full owned rollback'
Check ((Hash $keys) -eq $oldKeyHash -and (Hash $auto) -eq $oldAutoHash -and (Hash $vxml) -eq $oldVxmlHash) 'all originals restored byte-for-byte'
Check (-not (Test-Path -LiteralPath $vcss)) 'new-only CSS removed on rollback'
Check ((Hash $userdata) -eq $userdataHash) 'rollback leaves VCFG untouched'
$rr=Restore $r.Manifest; Check ($rr.Restored -eq 0) 'rollback is idempotent'

# Changed user file is not rolled back, while other owned unchanged outputs may be reverted.
$game=Fixture '用户后续改动'; $r=Install $game; $auto=Relative $game 'cfg\autoexec.cfg'; [IO.File]::AppendAllText($auto,('// user later edit' + $nl),$utf8); $edited=Hash $auto
$rr=Restore $r.Manifest
Check ($rr.Status -eq 'PartiallyRestored' -and $rr.Conflicts.Count -eq 1 -and $rr.Restored -eq 3) 'modified target reported and skipped'
Check ((Hash $auto) -eq $edited) 'rollback never overwrites later user edits'
Check (-not (Test-Path -LiteralPath (Relative $game $paths[0]))) 'unmodified owned new resource removed'

# Reject a tampered/traversal manifest before writes.
$game=Fixture '篡改清单'; $r=Install $game; $before=Hash (Relative $game $paths[0]); $m=Get-Content -LiteralPath $r.Manifest -Raw -Encoding UTF8 | ConvertFrom-Json; $m.Files[0].RelativePath='..\bin\win64\cs2.exe'; [IO.File]::WriteAllText($r.Manifest,($m|ConvertTo-Json -Depth 8),$utf8)
Expect-Failure { Restore $r.Manifest } 'reject path traversal in restore manifest'
Check ((Hash (Relative $game $paths[0])) -eq $before) 'invalid manifest causes no resource writes'

# Reject changed original backup transactionally, before restoring any target.
$game=Fixture '损坏备份';$keys=Relative $game 'cfg\ze_practice_keys.cfg';[IO.File]::WriteAllText($keys,'bind "F5" "ms_tp"',$utf8);$r=Install $game;$before=Hash (Relative $game $paths[0]);$backup=Join-Path (Split-Path -Parent $r.Manifest) 'originals\cfg\ze_practice_keys.cfg';[IO.File]::AppendAllText($backup,'changed',$utf8)
Expect-Failure { Restore $r.Manifest } 'reject changed original backup'
Check ((Hash (Relative $game $paths[0])) -eq $before) 'bad backup rejected before any rollback'

# Full validation precedes installation: missing assets, bad game roots and bad cfg never create a UI resource.
$game=Fixture '缺少资源';Expect-Failure { Install $game (Join-Path $root 'missing-assets') } 'missing source assets rejected'
Check (-not (Test-Path -LiteralPath (Relative $game $paths[0]))) 'missing assets cause no partial deployment'
Expect-Failure { Install (Join-Path $root 'not-a-game') } 'game root must have csgo and cs2.exe'
Expect-Failure { & $Installer -ClientGameRoot $game -AssetsRoot $source -BackupRoot (Join-Path $game 'backups') } 'backup directory inside game rejected'
Check (-not (Test-Path -LiteralPath (Relative $game $paths[0]))) 'bad backup location causes no partial deployment'
$game=Fixture 'UTF16配置';[IO.File]::WriteAllText((Relative $game 'cfg\autoexec.cfg'),'UTF16 cfg',[Text.Encoding]::Unicode)
Expect-Failure { Install $game } 'UTF16 cfg rejected without recoding'
Check (-not (Test-Path -LiteralPath (Relative $game $paths[0]))) 'cfg validation precedes resource writes'
$game=Fixture '不完整标记';[IO.File]::WriteAllText((Relative $game 'cfg\ze_practice_keys.cfg'),'// BEGIN ZE_PRACTICE_KEYS_V1',$utf8)
Expect-Failure { Install $game } 'incomplete managed marker rejected'
$game=Fixture '空配置';$keys=Relative $game 'cfg\ze_practice_keys.cfg';[IO.File]::WriteAllBytes($keys,[byte[]]@());$r=Install $game;$rr=Restore $r.Manifest
Check ((Get-Item -LiteralPath $keys).Length -eq 0) 'existing empty cfg restored correctly'

# ANSI comment bytes are preserved without implicit PS5 locale decoding.
$game=Fixture 'ANSI配置';$auto=Relative $game 'cfg\autoexec.cfg';$ansi=[byte[]]@(47,47,32,214,208,206,196,13,10);[IO.File]::WriteAllBytes($auto,$ansi);$r=Install $game;$actual=[IO.File]::ReadAllBytes($auto)
Check ([BitConverter]::ToString($actual[0..($ansi.Length-1)]) -eq [BitConverter]::ToString($ansi)) 'legacy ANSI comments preserved byte-for-byte'
$rr=Restore $r.Manifest;Check ([BitConverter]::ToString([IO.File]::ReadAllBytes($auto)) -eq [BitConverter]::ToString($ansi)) 'legacy ANSI rollback exact'

# A junction target is rejected, without following it into another directory.
$game=Fixture '联接目录';$outside=Join-Path $root 'junction-outside';[IO.Directory]::CreateDirectory($outside)|Out-Null
New-Item -ItemType Junction -Path (Relative $game 'panorama') -Target $outside | Out-Null
Expect-Failure { Install $game } 'target junction rejected'
Check (@(Get-ChildItem -LiteralPath $outside -Force).Count -eq 0) 'junction destination left untouched'
$summary=[ordered]@{Checks=$script:checks;Runtime=$PSVersionTable.PSVersion.ToString();FixtureRoot=$root;RealGameTouched=$false;AnyGameStarted=$false}
[IO.File]::WriteAllText((Join-Path $root 'test-results.json'),($summary|ConvertTo-Json),$utf8)
Write-Output ('TOTAL '+$script:checks+' passed on PowerShell '+$PSVersionTable.PSVersion+'; fixtures: '+$root)
