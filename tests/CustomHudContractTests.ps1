#requires -Version 5.1
[CmdletBinding()]
param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $project 'artifacts\custom-hud-contract-tests' }
[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$validator = Join-Path $project 'tools\Validate-ZeCustomHud.ps1'
$passed = 0
function Test-Document([string]$Name, [string]$Xml, [bool]$Expected, [string]$Message = '') {
    $file = Join-Path $OutputRoot ($Name + '.xml')
    [IO.File]::WriteAllText($file, $Xml, (New-Object Text.UTF8Encoding($false)))
    $accepted = $true; $failure = ''
    try { & $validator -Path $file | Out-Null } catch { $accepted = $false; $failure = $_.Exception.Message }
    if ($accepted -ne $Expected -or ($Message -and $failure -notlike "*$Message*")) {
        throw "FAIL $Name accepted=$accepted error=$failure"
    }
    $script:passed++
    Write-Output "PASS $Name"
}
Test-Document 'minimal-panel' '<root><Panel id="hud" class="screen" hittest="false"><Label text="test" hittest="false" /></Panel></root>' $true
Test-Document 'supported-controls' '<root><Panel><Button id="b" class="button"><Label text="test" /></Button><Image id="i" class="icon" src="test" hittest="false" /></Panel></root>' $true
Test-Document 'styles' '<root><styles><include src="s2r://panorama/styles/custom_game/test.vcss_c" /></styles><Panel /></root>' $true
Test-Document 'hittestchildren-root-regression' '<root><Panel hittest="false" hittestchildren="false" /></root>' $false "unsupported attribute 'hittestchildren'"
Test-Document 'hittestchildren-nested-regression' '<root><Panel><Panel id="diagnostics_window" hittestchildren="false" /></Panel></root>' $false "unsupported attribute 'hittestchildren'"
Test-Document 'button-hittest-regression' '<root><Panel><Button id="b" hittest="true" /></Panel></root>' $false "unsupported attribute 'hittest'"
Test-Document 'inline-style' '<root><Panel style="width: 100%;" /></root>' $false "unsupported attribute 'style'"
Test-Document 'inline-event' '<root><Panel onload="bad()" /></root>' $false "unsupported attribute 'onload'"
Test-Document 'scripts' '<root><scripts><include src="bad.js" /></scripts><Panel /></root>' $false 'unsupported element'
Test-Document 'unknown-panel' '<root><Panel><TextEntry /></Panel></root>' $false 'unsupported element'
Test-Document 'namespaces' '<root xmlns:x="bad"><x:Panel /></root>' $false
Test-Document 'wrong-case' '<root><panel /></root>' $false 'unsupported element'
Test-Document 'duplicate-id' '<root><Panel id="same"><Label id="same" /></Panel></root>' $false 'duplicate panel id'
Test-Document 'empty-id' '<root><Panel id="" /></root>' $false 'duplicate panel id'
Test-Document 'invalid-boolean' '<root><Panel hittest="0" /></root>' $false 'hittest must be'
Test-Document 'style-traversal' '<root><styles><include src="s2r://panorama/styles/custom_game/../test.vcss_c" /></styles><Panel /></root>' $false 'invalid stylesheet path'
Test-Document 'doctype' '<!DOCTYPE root [<!ENTITY test "bad">]><root><Panel /></root>' $false
foreach ($name in @('main','radio','movement','diagnostics')) {
    & $validator -Path (Join-Path $project "panorama\layout\custom_game\zepractice\$name.xml")
    $passed++
}
# Verify both real failing documents remain rejected even though resourcecompiler accepted them.
$baseline = Join-Path $project 'artifacts\hud-client-validation-20261016\baseline\panorama\layout\custom_game\zepractice'
if (Test-Path -LiteralPath $baseline) {
    foreach ($name in @('movement','diagnostics')) {
        Test-Document "original-$name-rejected" ([IO.File]::ReadAllText((Join-Path $baseline "$name.xml"))) $false "unsupported attribute 'hittestchildren'"
    }
}
Write-Output "PASS $passed CustomHud client-contract checks"
