#requires -Version 5.1
param([string]$LogRoot='E:\mapper\chana\临时文件夹\ze-workshop-3817448617-20261011\shared-log-tests')
$ErrorActionPreference='Stop'
[IO.Directory]::CreateDirectory($LogRoot)|Out-Null
$source=Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\Start-ZePractice.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
$function=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Read-SharedStartupLog'},$true)
if(-not $function){throw 'Shared log reader missing'}
. ([scriptblock]::Create($function.Extent.Text))
$file=Join-Path $LogRoot ('held-writer-'+$PSVersionTable.PSVersion.Major+'.log')
$writer=[IO.FileStream]::new($file,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try {
 $expected='Loading custom game "3817448617" with map "ze_forhidden_slicoo_cn"'+"`r`n"+'ss_loading -> ss_active'
 $data=[Text.Encoding]::UTF8.GetBytes($expected);$writer.Write($data,0,$data.Length);$writer.Flush()
 $read=Read-SharedStartupLog $file
 if($read -cne $expected){throw 'Reading with an active writer lost output'}
 Write-Host ('PASS PS'+$PSVersionTable.PSVersion.Major+' concurrent log writer')
} finally {$writer.Dispose()}
if((Read-SharedStartupLog (Join-Path $LogRoot 'does-not-exist.log')) -ne ''){throw 'Transient missing log should produce empty input'}
Write-Host ('PASS PS'+$PSVersionTable.PSVersion.Major+' transient missing log')