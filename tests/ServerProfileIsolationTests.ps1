#requires -Version 5.1
param([string]$EngineLabel = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# This regression never executes the launcher, CS2, RCON, or a real server CFG.
# Only the two password helpers and two argument assignments are evaluated, with fixtures.
$project = Split-Path -Parent $PSScriptRoot
$deployment = 'E:\mapper\chana\ZePractice'
$game = 'E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game'
$temporaryRoot = 'E:\mapper\chana\临时文件夹\ze-round-freeze-fix-20261011\profile-tests'
$untouchedManifest = 'E:\mapper\chana\临时文件夹\ze-round-freeze-fix-20261011\backup-profile-isolation\untouched-files.json'
if (-not $EngineLabel) { $EngineLabel = 'PS' + $PSVersionTable.PSVersion.Major }
if ($EngineLabel -notmatch '^[A-Za-z0-9_.-]{1,40}$') { throw 'EngineLabel must be a short filename-safe label.' }
$session = Join-Path $temporaryRoot ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $PID + '-' + $EngineLabel)
$fixturePrefix = [IO.Path]::GetFullPath($temporaryRoot).TrimEnd('\') + '\'
if (-not [IO.Path]::GetFullPath($session).StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture path escaped its designated temporary directory.' }
[IO.Directory]::CreateDirectory($session) | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false, $true)
$results = New-Object 'Collections.Generic.List[object]'

function Assert-Test([bool]$Condition) {
    if (-not $Condition) { throw 'Regression assertion failed; fixture values are intentionally omitted.' }
}
function Test-Case([string]$Name, [scriptblock]$Body) {
    $passed = $false
    $errorCode = ''
    try { & $Body; $passed = $true }
    catch {
        # Never print Exception.Message/InvocationInfo: a faulty helper may echo its input.
        $errorCode = $_.Exception.GetType().Name
    }
    $results.Add([pscustomobject]@{Name=$Name; Passed=$passed; ErrorCode=$errorCode})
    Write-Host ($Name + '=' + $passed)
}
function Read-TestAst([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Assert-Test (@($errors).Count -eq 0)
    return $ast
}
function Require-OneNode($Ast, [scriptblock]$Predicate) {
    $nodes = @($Ast.FindAll($Predicate, $true))
    Assert-Test ($nodes.Count -eq 1)
    return $nodes[0]
}
function Assert-FixturePath([string]$Path) {
    $prefix = [IO.Path]::GetFullPath($session).TrimEnd('\') + '\'
    Assert-Test ([IO.Path]::GetFullPath($Path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase))
}

$targets = @(
    @{Name='source'; Path=(Join-Path $project 'tools\Start-ZePractice.ps1')},
    @{Name='deployed'; Path=(Join-Path $deployment 'tools\Start-ZePractice.ps1')}
)
foreach ($target in $targets) {
    $ast = $null
    Test-Case ($target.Name + '.parse-launcher') { $script:ast = Read-TestAst $target.Path }
    if ($null -eq $ast) { continue }
    $serverAssignment = $null; $argumentAssignment = $null; $assertFunction = $null; $writeFunction = $null
    Test-Case ($target.Name + '.extract-required-ast') {
        $script:serverAssignment = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left -is [Management.Automation.Language.VariableExpressionAst] -and $n.Left.VariablePath.UserPath -eq 'serverConfig' }
        $script:argumentAssignment = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left -is [Management.Automation.Language.VariableExpressionAst] -and $n.Left.VariablePath.UserPath -eq 'arguments' }
        $script:assertFunction = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-ZeRconPassword' }
        $script:writeFunction = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Write-ZeRconConfig' }
    }
    if ($null -eq $serverAssignment -or $null -eq $argumentAssignment -or $null -eq $assertFunction -or $null -eq $writeFunction) { continue }

    Test-Case ($target.Name + '.generation-after-prepare-before-process') {
        $prepare = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -match '^if\s*\(\s*\$PrepareOnly\s*\)' }
        $prepareExits = @($prepare.FindAll({param($n) $n -is [Management.Automation.Language.ExitStatementAst]}, $true))
        Assert-Test ($prepareExits.Count -gt 0)
        $writeCall = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Write-ZeRconConfig' }
        $startCall = Require-OneNode $ast { param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Start-Process' }
        Assert-Test ($prepare.Extent.EndOffset -lt $writeCall.Extent.StartOffset)
        Assert-Test ($writeCall.Extent.EndOffset -lt $startCall.Extent.StartOffset)
        $guard = Require-OneNode $writeFunction { param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Assert-ZeRconPassword' }
        Assert-Test ($null -ne $guard)
    }

    foreach ($mode in @('workshop-bootstrap', 'local')) {
        Test-Case ($target.Name + '.arguments.' + $mode) {
            $fixturePassword = 'fixture-secret-start-中文-ArgCheck'
            $settings = [pscustomobject]@{BindAddress='127.0.0.1'; Port=27199; RconPassword=$fixturePassword}
            $port = 27199
            $nativeLog = Join-Path $session 'fixture-console.log'
            $addonArg = ''; $initialMapCommand = 'map de_dust2'
            if ($mode -eq 'local') { $addonArg = '-addon ze_fixture'; $initialMapCommand = 'map ze_fixture nomapvalidation true' }
            # Evaluate only the real assignments; never dot-source the launcher itself.
            . ([scriptblock]::Create($serverAssignment.Extent.Text))
            . ([scriptblock]::Create($argumentAssignment.Extent.Text))
            Assert-Test ($serverConfig -ceq 'ze-practice/server.cfg')
            $selector = [regex]::Matches($arguments, '(?i)(?:^|\s)\+servercfgfile\s+"?ze-practice/server\.cfg"?(?=\s|$)')
            $execProfile = [regex]::Matches($arguments, '(?i)(?:^|\s)\+exec\s+"?ze-practice/server\.cfg"?(?=\s|$)')
            $privateExec = [regex]::Matches($arguments, '(?i)(?:^|\s)\+exec\s+"?ze-practice/rcon\.generated\.cfg"?(?=\s|$)')
            $initialMap = [regex]::Match($arguments, '(?i)(?:^|\s)\+map\s+')
            Assert-Test ($selector.Count -eq 1 -and $execProfile.Count -eq 1 -and $initialMap.Success)
            Assert-Test ($selector[0].Index -lt $initialMap.Index -and $execProfile[0].Index -lt $initialMap.Index)
            Assert-Test ($privateExec.Count -eq 1 -and $privateExec[0].Index -lt $initialMap.Index)
            Assert-Test ($arguments.Contains('+' + $initialMapCommand))
            Assert-Test ($arguments -notmatch '(?i)(?:^|\s)\+exec\s+"?(?:[.]?/)?server(?:\.cfg)?"?(?=\s|$)')
            Assert-Test ($arguments -notmatch '(?i)rcon_password|RconPassword')
            Assert-Test (-not $arguments.Contains($fixturePassword))
        }
    }

    # Definitions only; every invocation below receives an explicitly checked fixture path.
    . ([scriptblock]::Create($assertFunction.Extent.Text))
    . ([scriptblock]::Create($writeFunction.Extent.Text))
    $validPasswords = @(
        @{Name='unicode'; Value='fixture-中文口令-é-安全'},
        @{Name='ascii'; Value='fixture_A9-._@!'},
        @{Name='length-100'; Value=('A' * 100)}
    )
    foreach ($item in $validPasswords) {
        Test-Case ($target.Name + '.password-roundtrip.' + $item.Name) {
            $fixture = Join-Path $session ($target.Name + '-' + $item.Name + '\rcon.generated.cfg')
            Assert-FixturePath $fixture
            $output = @(Write-ZeRconConfig -Path $fixture -Password $item.Value *>&1)
            Assert-Test ($output.Count -eq 0)
            $bytes = [IO.File]::ReadAllBytes($fixture)
            Assert-Test (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191))
            $text = [IO.File]::ReadAllText($fixture, $utf8)
            $passwordLine = [regex]::Matches($text, '(?m)^[ \t]*rcon_password[ \t]+"([^"\r\n]*)"[ \t]*\r?$')
            Assert-Test ($passwordLine.Count -eq 1)
            Assert-Test ($passwordLine[0].Groups[1].Value -ceq $item.Value)
        }
    }

    $marker = 'fixture-secret-start'
    $invalidPasswords = @(
        @{Name='empty'; Value=''},
        @{Name='whitespace-only'; Value=' '},
        @{Name='embedded-space'; Value=($marker + ' A')},
        @{Name='tab'; Value=($marker + "`tA")},
        @{Name='newline'; Value=($marker + "`r`nA")},
        @{Name='trailing-lf'; Value=($marker + "`n")},
        @{Name='unicode-space'; Value=($marker + [char]0x3000 + 'A')},
        @{Name='double-quote'; Value=($marker + '"A')},
        @{Name='semicolon'; Value=($marker + ';A')},
        @{Name='backslash'; Value=($marker + '\A')},
        @{Name='nul'; Value=($marker + [char]0 + 'A')},
        @{Name='del'; Value=($marker + [char]0x7f + 'A')},
        @{Name='c1-control-0081'; Value=($marker + [char]0x81 + 'A')},
        @{Name='c1-control-009f'; Value=($marker + [char]0x9f + 'A')},
        @{Name='length-101'; Value=($marker + ('A' * (101 - $marker.Length)))}
    )
    foreach ($item in $invalidPasswords) {
        Test-Case ($target.Name + '.password-rejected.' + $item.Name) {
            $fixture = Join-Path $session ($target.Name + '-rejection-existing.cfg')
            Assert-FixturePath $fixture
            $original = 'unchanged non-secret fixture marker'
            [IO.File]::WriteAllText($fixture, $original, $utf8)
            $rejected = $false; $errorText = ''; $output = @()
            try { $output = @(Write-ZeRconConfig -Path $fixture -Password $item.Value *>&1) }
            catch { $rejected = $true; $errorText = ($_ | Out-String) }
            Assert-Test $rejected
            Assert-Test ([IO.File]::ReadAllText($fixture, $utf8) -ceq $original)
            Assert-Test (-not $errorText.Contains($marker))
            Assert-Test (-not (($output | Out-String).Contains($marker)))
        }
    }
}

$sourceProfile = Join-Path $project 'config\ze-practice\server.cfg'
$deployedProfile = Join-Path $game 'csgo\cfg\ze-practice\server.cfg'
Test-Case 'profile.template-matches-deployment' {
    Assert-Test ((Get-FileHash -LiteralPath $sourceProfile -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath $deployedProfile -Algorithm SHA256).Hash)
}
foreach ($profile in @(@{Name='source'; Path=$sourceProfile}, @{Name='deployed'; Path=$deployedProfile})) {
    Test-Case ('profile.' + $profile.Name + '.isolated-no-workshop-rcon-reexec') {
        $text = [IO.File]::ReadAllText($profile.Path, $utf8)
        # The cold-start +servercfgfile argument is authoritative; do not reapply it from a Workshop CFG.
        # Password assignment is cold-start-only: executing it again inside a Workshop
        # map is blocked by the engine's DISALLOWED WORKSHOP CONVAR restriction.
        Assert-Test ($text -notmatch '(?im)^[ \t]*exec[ \t]+"?ze-practice/rcon\.generated\.cfg"?(?:[ \t;]|\r?$)')
        Assert-Test ($text -notmatch '(?im)^[ \t]*(?:endround|quit|rcon_password)(?:[ \t;]|\r?$)')
        Assert-Test ($text -notmatch '(?im)^[ \t]*exec[ \t]+"?server(?:\.cfg)?"?(?:[ \t;]|\r?$)')
    }
}

$manifestRecords = @()
Test-Case 'untouched.manifest-has-three-records' {
    # Windows PowerShell 5.1 can preserve the JSON array as one pipeline object.
    # Parse first, then enumerate the returned array explicitly in both engines.
    $parsed = [IO.File]::ReadAllText($untouchedManifest, $utf8) | ConvertFrom-Json
    $script:manifestRecords = @($parsed)
    Assert-Test ($manifestRecords.Count -eq 3)
}
$untouched = @(
    @{Name='legacy-server-cfg'; Path=(Join-Path $game 'csgo\cfg\server.cfg')},
    @{Name='steamcmd-update-bat'; Path='E:\mapper\chana\run.bat'},
    @{Name='legacy-server-start-bat'; Path=(Join-Path $game 'bin\win64\run.bat')}
)
foreach ($item in $untouched) {
    Test-Case ('untouched.' + $item.Name) {
        $records = @($manifestRecords | Where-Object { [IO.Path]::GetFullPath([string]$_.Path) -ieq [IO.Path]::GetFullPath($item.Path) })
        Assert-Test ($records.Count -eq 1)
        Assert-Test ([string]$records[0].SHA256 -match '^[0-9a-fA-F]{64}$')
        $hash = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash
        Assert-Test ($hash -ieq [string]$records[0].SHA256)
        # Only the boolean result is emitted; neither hashes nor file contents are logged.
    }
}

$failed = @($results | Where-Object { -not $_.Passed }).Count
$summary = [ordered]@{
    Engine=$EngineLabel
    PowerShellVersion=$PSVersionTable.PSVersion.ToString()
    Scope='AST/fixture-only profile isolation regression; no launcher, CS2, RCON, or real CFG execution'
    Passed=($results.Count - $failed)
    Failed=$failed
    Total=$results.Count
    Results=@($results.ToArray())
}
$resultFile = Join-Path $session 'results.json'
Assert-FixturePath $resultFile
[IO.File]::WriteAllText($resultFile, ($summary | ConvertTo-Json -Depth 5), $utf8)
Write-Host ('Server profile isolation: ' + ($results.Count - $failed) + '/' + $results.Count + ' passed.')
Write-Host ('Result: ' + $resultFile)
if ($failed -gt 0) { exit 1 }