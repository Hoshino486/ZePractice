param([string]$ProjectRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$radio=[IO.File]::ReadAllText((Join-Path $ProjectRoot 'client\resource\ui\radiopanel.txt'))
$count=0
foreach($match in [regex]::Matches($radio,'"cmd"\s+"([^"\r\n]+)"')) {
    foreach($command in $match.Groups[1].Value.Split(';')) {
        if($command -notmatch '^radio[123]?$|^say ![a-z][a-z0-9_]*(?: [a-z0-9_]+)*$'){throw "Unsupported Workshop command: $command"}
        $count++
    }
}
if($count -lt 36){throw 'Radio command entries missing'}
for($key=1;$key -le 8;$key++) {if(-not $radio.Contains('"cmd" "say !ze_select '+$key+';radio3"')){throw "Missing persistent key $key"}}
if(-not $radio.Contains('"cmd" "say !ze_select 9"')){throw 'Close must not toggle/reopen native radio'}
Write-Output "PASS $count Workshop radio command segments: only known radio/say commands; close is terminal"
