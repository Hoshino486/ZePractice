#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Worker,
    [string]$EngineLabel = '',
    [string]$ResultPath = '',
    [string]$SessionRoot = '',
    [string]$TemporaryRoot = 'E:\mapper\chana\临时文件夹\ze-practice-encoding-fix-20261011\tests'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceTools = Join-Path $projectRoot 'tools'
$installedHome = 'E:\mapper\chana\ZePractice'
$installedTools = Join-Path $installedHome 'tools'
$names = @('Start-ZePractice.ps1','Update-Fys.ps1','Deploy-Tracked.ps1','Rollback-ZePractice.ps1','Invoke-ZeRcon.ps1')
$encoding = [Text.UTF8Encoding]::new($false)
$expectedReads = @{'Start-ZePractice.ps1'=1;'Update-Fys.ps1'=3;'Deploy-Tracked.ps1'=1;'Rollback-ZePractice.ps1'=1;'Invoke-ZeRcon.ps1'=1}

function Assert-Test([bool]$Condition,[string]$Reason = 'assertion failed') {
    if(-not $Condition) { throw [InvalidOperationException]::new($Reason) }
}
function Assert-InFixture([string]$Path,[string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    Assert-Test ($full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) 'fixture path escaped test session'
}
function Quote-Argument([string]$Value) {
    if($Value.Contains('"') -or $Value.Contains("`n") -or $Value.Contains("`r")) { throw 'invalid test argument' }
    return '"' + $Value + '"'
}
function Run-HiddenChild([string]$Executable,[string]$Script,[string[]]$ScriptArguments,[string]$LogPrefix) {
    Assert-InFixture ($LogPrefix + '.stdout.log') $SessionRoot
    $arguments = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Quote-Argument $Script)) + $ScriptArguments
    # Start-Process does not perform pwsh's native-command compatibility adjustment.
    # Model a clean native PS5 launch. The separate real BAT test intentionally keeps
    # a hostile PS7-style module path and verifies run.bat performs its own reset.
    $savedModulePath = $env:PSModulePath
    try {
        if([IO.Path]::GetFileName($Executable) -ieq 'powershell.exe') { $env:PSModulePath = $null }
        $process = Start-Process -FilePath $Executable -ArgumentList ($arguments -join ' ') -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput ($LogPrefix + '.stdout.log') -RedirectStandardError ($LogPrefix + '.stderr.log')
    } finally { $env:PSModulePath = $savedModulePath }
    return [pscustomobject]@{ExitCode=$process.ExitCode;Stdout=($LogPrefix+'.stdout.log');Stderr=($LogPrefix+'.stderr.log')}
}
function Run-HiddenBatch([string]$Batch,[string]$LogPrefix,[string]$PollutedModulePath) {
    Assert-InFixture $Batch $SessionRoot
    Assert-InFixture ($LogPrefix+'.stdout.log') $SessionRoot
    $savedModulePath=$env:PSModulePath
    try {
        $env:PSModulePath=$PollutedModulePath
        # NUL prevents the launcher's failure pause from waiting for a keyboard.
        $arguments='/d /s /c "'+(Quote-Argument $Batch)+' -PrepareOnly -Hidden < nul"'
        $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\cmd.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput ($LogPrefix+'.stdout.log') -RedirectStandardError ($LogPrefix+'.stderr.log')
    } finally { $env:PSModulePath=$savedModulePath }
    return [pscustomobject]@{ExitCode=$process.ExitCode;Stdout=($LogPrefix+'.stdout.log');Stderr=($LogPrefix+'.stderr.log')}
}
function Read-ChildLog([string]$Path) {
    # Child output is only test fixtures; real settings and credentials are never sent to a child log.
    return [IO.File]::ReadAllText($Path)
}

if(-not $Worker) {
    [IO.Directory]::CreateDirectory($TemporaryRoot) | Out-Null
    $SessionRoot = Join-Path $TemporaryRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
    [IO.Directory]::CreateDirectory($SessionRoot) | Out-Null
    $ps5 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $ps7Command = Get-Command pwsh.exe -ErrorAction Stop
    $engines = @([pscustomobject]@{Label='WindowsPowerShell5';Exe=$ps5},[pscustomobject]@{Label='PowerShell7';Exe=$ps7Command.Source})
    $summaries = @()
    foreach($engine in $engines) {
        $engineRoot = Join-Path $SessionRoot $engine.Label
        [IO.Directory]::CreateDirectory($engineRoot) | Out-Null
        $resultFile = Join-Path $engineRoot 'results.json'
        $call = Run-HiddenChild $engine.Exe $PSCommandPath @('-Worker','-EngineLabel',(Quote-Argument $engine.Label),'-ResultPath',(Quote-Argument $resultFile),'-SessionRoot',(Quote-Argument $engineRoot),'-TemporaryRoot',(Quote-Argument $TemporaryRoot)) (Join-Path $SessionRoot $engine.Label)
        if(-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
            $summaries += [pscustomobject]@{Engine=$engine.Label;Version='unknown';Passed=0;Failed=1;Total=1;ResultFile=$resultFile;ExitCode=$call.ExitCode}
            continue
        }
        $result = Get-Content -LiteralPath $resultFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $summaries += [pscustomobject]@{Engine=$result.Engine;Version=$result.Version;Passed=$result.Passed;Failed=$result.Failed;Total=$result.Total;ResultFile=$resultFile;ExitCode=$call.ExitCode}
    }
    $total = ($summaries | Measure-Object -Property Total -Sum).Sum
    $passed = ($summaries | Measure-Object -Property Passed -Sum).Sum
    $failed = ($summaries | Measure-Object -Property Failed -Sum).Sum
    $summary = [pscustomobject]@{Scope='UTF8/BOM/PS5+PS7 isolated fixture regression; no real deployment, RCON, or CS2 execution';Passed=$passed;Failed=$failed;Total=$total;Engines=$summaries;SessionRoot=$SessionRoot}
    $summaryFile = Join-Path $SessionRoot 'summary.json'
    [IO.File]::WriteAllText($summaryFile,($summary | ConvertTo-Json -Depth 6),$encoding)
    $summaries | Format-Table Engine,Version,Passed,Failed,Total,ExitCode -AutoSize | Out-Host
    Write-Host ('TOTAL '+$passed+'/'+$total+' passed; failures='+$failed)
    Write-Host ('Logs and safe results: '+$summaryFile)
    if($failed -gt 0 -or @($summaries|Where-Object ExitCode -ne 0).Count -gt 0) { exit 1 }
    exit 0
}

# Worker tests never execute the installed launcher or update/rollback against the real server.
Assert-Test ([bool]$SessionRoot) 'worker session required'
Assert-InFixture $SessionRoot $TemporaryRoot
Assert-InFixture $ResultPath $SessionRoot
[IO.Directory]::CreateDirectory($SessionRoot) | Out-Null
$tests = [Collections.Generic.List[object]]::new()
$asts = @{}
$engineExecutable = if($PSVersionTable.PSEdition -eq 'Desktop') { Join-Path $PSHOME 'powershell.exe' } else { Join-Path $PSHOME 'pwsh.exe' }
function Test-Case([string]$CaseName,[scriptblock]$CaseBody) {
    try {
        & $CaseBody
        $tests.Add([pscustomobject]@{Name=$CaseName;Passed=$true})
        Write-Output ('PASS '+$CaseName)
    } catch {
        # Deliberately do not print exception text/record/stack: a JSON error can embed its entire input.
        $tests.Add([pscustomobject]@{Name=$CaseName;Passed=$false;ExceptionType=$_.Exception.GetType().FullName})
        Write-Output ('FAIL '+$CaseName+' (exception type only: '+$_.Exception.GetType().Name+')')
    }
}
Test-Case 'Native Utility module resolves Get-FileHash and ConvertFrom-Json' {
    $hashCommand=Get-Command Get-FileHash -ErrorAction Stop
    $jsonCommand=Get-Command ConvertFrom-Json -ErrorAction Stop
    Assert-Test ($null -ne $hashCommand -and $null -ne $jsonCommand)
    if($PSVersionTable.PSEdition -eq 'Desktop') { Assert-Test ($jsonCommand.Module.Version.Major -eq 3) }
}
Test-Case 'Requested PowerShell major version' {
    if($EngineLabel -eq 'WindowsPowerShell5') { Assert-Test ($PSVersionTable.PSVersion.Major -eq 5) }
    elseif($EngineLabel -eq 'PowerShell7') { Assert-Test ($PSVersionTable.PSVersion.Major -eq 7) }
    else { throw 'Unknown test engine label' }
}
foreach($root in @($sourceTools,$installedTools)) {
    $scope = if($root -eq $sourceTools){'source'}else{'installed'}
    foreach($name in $names) {
        $path = Join-Path $root $name
        Test-Case ($scope+'/'+$name+' UTF8 BOM') {
            $bytes = [IO.File]::ReadAllBytes($path)
            Assert-Test ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)
            $null = [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
        }
        Test-Case ($scope+'/'+$name+' native parser zero errors') {
            $tokens=$null;$errors=$null
            $tree=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
            Assert-Test (@($errors).Count -eq 0)
            $asts[$path]=$tree
        }
        Test-Case ($scope+'/'+$name+' JSON reads explicitly UTF8') {
            $tree=$asts[$path]
            $reads=@($tree.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Get-Content'},$true))
            Assert-Test ($reads.Count -eq $expectedReads[$name])
            foreach($read in $reads) {
                $explicit=$false
                for($index=0;$index -lt $read.CommandElements.Count;$index++) {
                    $element=$read.CommandElements[$index]
                    if($element -is [Management.Automation.Language.CommandParameterAst] -and $element.ParameterName -eq 'Encoding') {
                        $argument=if($null -ne $element.Argument){$element.Argument}else{$read.CommandElements[$index+1]}
                        $explicit=($argument.SafeGetValue() -eq 'UTF8')
                    }
                }
                Assert-Test $explicit
            }
        }
    }
    Test-Case ($scope+'/Deploy default Chinese backup path decoded correctly') {
        $tree=$asts[(Join-Path $root 'Deploy-Tracked.ps1')]
        $parameter=@($tree.ParamBlock.Parameters | Where-Object {$_.Name.VariablePath.UserPath -eq 'BackupRoot'})
        Assert-Test ($parameter.Count -eq 1)
        Assert-Test ($parameter[0].DefaultValue.SafeGetValue() -ceq 'E:\mapper\chana\临时文件夹\ze-practice-build-20261011\backup')
    }
    Test-Case ($scope+'/Rollback default Chinese manifest path decoded correctly') {
        $tree=$asts[(Join-Path $root 'Rollback-ZePractice.ps1')]
        $parameter=@($tree.ParamBlock.Parameters | Where-Object {$_.Name.VariablePath.UserPath -eq 'ManifestPath'})
        Assert-Test ($parameter.Count -eq 1)
        Assert-Test ($parameter[0].DefaultValue.SafeGetValue() -ceq 'E:\mapper\chana\临时文件夹\ze-practice-build-20261011\backup\installed-files.json')
    }
    Test-Case ($scope+'/Rollback Chinese fixed diagnostics literal decoded correctly') {
        $tree=$asts[(Join-Path $root 'Rollback-ZePractice.ps1')]
        $found=@($tree.FindAll({param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -and $node.Value -ceq '临时文件夹\ze-practice-build-20261011'},$true))
        Assert-Test ($found.Count -eq 1)
    }
}
foreach($name in $names) {
    Test-Case ($name+' source/deployment exact match') {
        Assert-Test ((Get-FileHash -LiteralPath (Join-Path $sourceTools $name) -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $installedTools $name) -Algorithm SHA256).Hash)
    }
}
Test-Case 'Installed settings valid UTF8 with correct Chinese diagnostics path; no values logged' {
    $settingsPath=Join-Path $installedHome 'server-settings.json'
    $text=[IO.File]::ReadAllText($settingsPath,[Text.UTF8Encoding]::new($false,$true))
    $settings=$text | ConvertFrom-Json -ErrorAction Stop
    Assert-Test ($settings.DiagnosticsRoot.Contains('临时文件夹'))
    Assert-Test ([IO.Path]::IsPathRooted([string]$settings.DiagnosticsRoot))
    Assert-Test (-not [string]::IsNullOrWhiteSpace([string]$settings.GameRoot))
    Assert-Test ($settings.PSObject.Properties.Name -contains 'RconPassword')
    # Clear locals before subsequent fixture work. Never serialize the object or emit the secret or its length.
    $settings=$null;$text=$null
}

# Only synthetic invalid JSON is passed to any script we actually execute.
foreach($name in @('Start-ZePractice.ps1','Update-Fys.ps1','Invoke-ZeRcon.ps1')) {
    Test-Case ($name+' actual hidden failure redacts malformed JSON sentinel') {
        $caseRoot=Join-Path $SessionRoot ('bad-json-'+[IO.Path]::GetFileNameWithoutExtension($name))
        $caseTools=Join-Path $caseRoot 'tools'
        Assert-InFixture $caseTools $SessionRoot
        [IO.Directory]::CreateDirectory($caseTools) | Out-Null
        $copied=Join-Path $caseTools $name
        Copy-Item -LiteralPath (Join-Path $installedTools $name) -Destination $copied
        $sentinel='FAKE_TEST_ONLY_'+[Guid]::NewGuid().ToString('N')
        $badJson='{"GameRoot":"FIXTURE_NOT_A_SERVER","RconPassword":"'+$sentinel+'","DiagnosticsRoot":"临时文件夹","broken":"\q"}'
        $mockSettings=Join-Path $caseRoot 'server-settings.json'
        [IO.File]::WriteAllText($mockSettings,$badJson,$encoding)
        $mustReject=$false
        try{$null=$badJson | ConvertFrom-Json -ErrorAction Stop}catch{$mustReject=$true}
        Assert-Test $mustReject 'malformed fixture unexpectedly parses'
        $arguments=if($name -eq 'Start-ZePractice.ps1'){@('-PrepareOnly','-Hidden')}elseif($name -eq 'Update-Fys.ps1'){@('-CheckOnly')}else{@('-Command','status')}
        $call=Run-HiddenChild $engineExecutable $copied $arguments (Join-Path $caseRoot 'child')
        $observed=(Read-ChildLog $call.Stdout)+(Read-ChildLog $call.Stderr)
        Assert-Test ($call.ExitCode -ne 0)
        Assert-Test (-not $observed.Contains($sentinel))
        Assert-Test (-not $observed.Contains('FIXTURE_NOT_A_SERVER'))
        Assert-Test (-not $observed.Contains('"RconPassword"'))
        Assert-Test ($observed -match 'server-settings\.json')
        Assert-Test (@(Get-ChildItem -LiteralPath $caseRoot -Directory | Where-Object Name -ne 'tools').Count -eq 0)
        $observed=$null;$badJson=$null;$sentinel=$null
    }
}

# Execute actual copied run.bat through cmd, under an inherited PS7 module path.
# Both this and direct -File use a fake game tree with no cs2.exe anywhere.
$ps7Exe=(Get-Command pwsh.exe -ErrorAction Stop).Source
$pollutedModulePath=(Join-Path ([IO.Path]::GetDirectoryName($ps7Exe)) 'Modules')+';'+$env:PSModulePath
Test-Case 'Installed run.bat resets PSModulePath and uses native absolute Windows PowerShell' {
    $batText=[IO.File]::ReadAllText((Join-Path $installedHome 'run.bat'))
    Assert-Test ($batText -match '(?im)^setlocal\b')
    Assert-Test ($batText -match '(?im)^set "PSModulePath="\s*$')
    Assert-Test ($batText -match '(?im)^"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell\.exe" ')
    Assert-Test ($batText.IndexOf('set "PSModulePath="') -lt $batText.IndexOf('"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"'))
}
Test-Case 'cmd to actual run.bat redacts malformed settings under PS7 module inheritance' {
    $caseRoot=Join-Path $SessionRoot 'bad-json-Start-ZePractice'
    $batch=Join-Path $caseRoot 'run.bat'
    Copy-Item -LiteralPath (Join-Path $installedHome 'run.bat') -Destination $batch
    $call=Run-HiddenBatch $batch (Join-Path $caseRoot 'batch') $pollutedModulePath
    $observed=(Read-ChildLog $call.Stdout)+(Read-ChildLog $call.Stderr)
    Assert-Test ($call.ExitCode -ne 0)
    Assert-Test ($observed -match 'Failed to read server-settings\.json as UTF-8 JSON')
    Assert-Test ($observed -notmatch 'FAKE_TEST_ONLY_|FIXTURE_NOT_A_SERVER|"RconPassword"')
}
foreach($route in @('direct','cmd-run-bat')) {
    Test-Case ('Valid UTF8 Chinese settings actual PrepareOnly via '+$route+' never starts CS2') {
        $fixture=Join-Path $SessionRoot ('prepare-'+$route+'-中文目录')
        $tools=Join-Path $fixture 'tools'
        $game=Join-Path $fixture '模拟游戏'
        $diagnostics=Join-Path $fixture '临时文件夹'
        $requiredFiles=@('csgo\addons\metamod\bin\win64\server.dll','csgo\addons\counterstrikesharp\bin\win64\counterstrikesharp.dll','csgo\addons\counterstrikesharp\plugins\ZePractice\ZePractice.dll','csgo\steam.inf','csgo\cfg\ze-practice\server.cfg')
        foreach($pathToMake in @($tools,(Join-Path $game 'bin\win64'))) {
            Assert-InFixture $pathToMake $SessionRoot
            [IO.Directory]::CreateDirectory($pathToMake)|Out-Null
        }
        foreach($relative in $requiredFiles) {
            $file=Join-Path $game $relative
            Assert-InFixture $file $SessionRoot
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))|Out-Null
            # These are deliberately text markers, not binaries. Nothing is executable.
            [IO.File]::WriteAllText($file,'TEST FIXTURE ONLY',$encoding)
        }
        $gi=Join-Path $game 'csgo\gameinfo.gi'
        [IO.File]::WriteAllText($gi,"GameInfo`r`n{`r`n FileSystem`r`n {`r`n  SearchPaths`r`n  {`r`n   Game csgo`r`n  }`r`n }`r`n}`r`n",$encoding)
        $fakeSecret='FAKE_VALID_TEST_ONLY_'+[Guid]::NewGuid().ToString('N')
        $fakeSettings=[ordered]@{GameRoot=$game;DiagnosticsRoot=$diagnostics;StartupMap='de_dust2';BindAddress='127.0.0.1';Port=27099;AssetMounts=@();RconPassword=$fakeSecret}
        $mockSettings=Join-Path $fixture 'server-settings.json'
        [IO.File]::WriteAllText($mockSettings,($fakeSettings|ConvertTo-Json -Depth 4),$encoding)
        $copied=Join-Path $tools 'Start-ZePractice.ps1'
        Copy-Item -LiteralPath (Join-Path $installedTools 'Start-ZePractice.ps1') -Destination $copied
        if($route -eq 'direct') {
            $call=Run-HiddenChild $engineExecutable $copied @('-PrepareOnly','-Hidden') (Join-Path $fixture 'child')
        } else {
            $batch=Join-Path $fixture 'run.bat'
            Copy-Item -LiteralPath (Join-Path $installedHome 'run.bat') -Destination $batch
            $call=Run-HiddenBatch $batch (Join-Path $fixture 'batch') $pollutedModulePath
        }
        $observed=(Read-ChildLog $call.Stdout)+(Read-ChildLog $call.Stderr)
        Assert-Test ($call.ExitCode -eq 0)
        Assert-Test ($observed -match 'Preflight OK\.')
        Assert-Test (-not $observed.Contains($fakeSecret))
        Assert-Test ([IO.Directory]::Exists((Join-Path $diagnostics 'runtime')))
        Assert-Test ([IO.Directory]::Exists((Join-Path $diagnostics 'version-state')))
        Assert-Test ([IO.File]::ReadAllText($gi).Contains('Game' + "`t" + 'csgo/addons/metamod'))
        Assert-Test (-not [IO.File]::Exists((Join-Path $game 'bin\win64\cs2.exe')))
        Assert-Test (-not [IO.File]::Exists((Join-Path $fixture 'last-run.json')))
        Assert-Test (@(Get-ChildItem -LiteralPath $diagnostics -File -Filter 'run.json' -Recurse).Count -eq 0)
        $fakeSettings=$null;$fakeSecret=$null;$observed=$null
    }
}
# Isolated copy/deployment roundtrip. The executable-path guard points at a fake ServerHome under this test session.
$deployRoot=Join-Path $SessionRoot '中文部署测试'
$mockServer=Join-Path $deployRoot '独立ServerHome'
$source=Join-Path $deployRoot '源文件'
$destination=Join-Path $mockServer 'ZePractice\中文目录'
$backup=Join-Path $mockServer '备份目录'
foreach($path in @($source,$destination,$backup)) {Assert-InFixture $path $SessionRoot;[IO.Directory]::CreateDirectory($path)|Out-Null}
$sourceFile=Join-Path $source '中文配置.json'
$existing=Join-Path $destination '中文配置.json'
[IO.File]::WriteAllText($existing,'{"message":"部署前原文件"}',$encoding)
$originalHash=(Get-FileHash -LiteralPath $existing -Algorithm SHA256).Hash
[IO.File]::WriteAllText($sourceFile,'{"message":"首次中文部署"}',$encoding)
[IO.File]::WriteAllText((Join-Path $source '新增中文.txt'),'新增文件',$encoding)
$deployScript=Join-Path $installedTools 'Deploy-Tracked.ps1'
$deployArguments=@('-Source',(Quote-Argument $source),'-Destination',(Quote-Argument $destination),'-ServerHome',(Quote-Argument $mockServer),'-BackupRoot',(Quote-Argument $backup))
Test-Case 'Deploy isolated Chinese paths and UTF8 no-BOM manifest first pass' {
    $call=Run-HiddenChild $engineExecutable $deployScript $deployArguments (Join-Path $deployRoot 'first')
    Assert-Test ($call.ExitCode -eq 0)
    Assert-Test ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath $existing -Algorithm SHA256).Hash)
    $manifestPath=Join-Path $backup 'installed-files.json'
    $parsed=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json; $entries=@($parsed)
    Assert-Test ($entries.Count -eq 2)
    $entry=@($entries | Where-Object {$_.Target -ceq $existing})
    Assert-Test ($entry.Count -eq 1 -and $entry[0].Existed)
    Assert-InFixture $entry[0].Backup $SessionRoot
    Assert-Test ((Get-FileHash -LiteralPath $entry[0].Backup -Algorithm SHA256).Hash -ceq $originalHash)
    Assert-Test ($entry[0].OldSHA256 -ceq $originalHash)
}
Test-Case 'Deploy second pass parses Chinese manifest without duplicates and preserves original backup' {
    [IO.File]::WriteAllText($sourceFile,'{"message":"第二次更新测试"}',$encoding)
    $call=Run-HiddenChild $engineExecutable $deployScript $deployArguments (Join-Path $deployRoot 'second')
    Assert-Test ($call.ExitCode -eq 0)
    $parsed=Get-Content -LiteralPath (Join-Path $backup 'installed-files.json') -Raw -Encoding UTF8 | ConvertFrom-Json; $entries=@($parsed)
    Assert-Test ($entries.Count -eq 2)
    $entry=@($entries | Where-Object {$_.Target -ceq $existing})
    Assert-Test ($entry.Count -eq 1)
    Assert-Test ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ceq $entry[0].NewSHA256)
    Assert-Test ((Get-FileHash -LiteralPath $existing -Algorithm SHA256).Hash -ceq $entry[0].NewSHA256)
    Assert-Test ((Get-FileHash -LiteralPath $entry[0].Backup -Algorithm SHA256).Hash -ceq $originalHash)
}
$failed=@($tests.ToArray() | Where-Object {$_.Passed -eq $false}).Count
$result=[pscustomobject]@{Engine=$EngineLabel;Version=$PSVersionTable.PSVersion.ToString();DefaultCodePage=[Text.Encoding]::Default.CodePage;Passed=($tests.Count-$failed);Failed=$failed;Total=$tests.Count;Tests=@($tests.ToArray());LogDirectory=$SessionRoot;RealServerTouched=$false}
[IO.File]::WriteAllText($ResultPath,($result | ConvertTo-Json -Depth 6),$encoding)
Write-Output ('SUMMARY '+($tests.Count-$failed)+'/'+$tests.Count+' passed')
if($failed -gt 0){exit 1}
exit 0