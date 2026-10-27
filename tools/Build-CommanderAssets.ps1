#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ClientGameRoot = 'D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive 730\game',
    [string]$AssetRoot = 'E:\mapper\chana\ZePractice\assets',
    [string]$OutputRoot = ''
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (!$OutputRoot) { $OutputRoot = Join-Path $project 'artifacts\commander-luna-20261015' }
$addon = 'saibochana_commander_build_20261015'
$content = Join-Path (Split-Path $ClientGameRoot) "content\csgo_addons\$addon"
$game = Join-Path $ClientGameRoot "csgo_addons\$addon"
$sources = Join-Path $AssetRoot '战双帕弥什-终焉.卢娜-弥月寒调\sources'
$logRoot = Join-Path $OutputRoot ('compile-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $content,$game,$logRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $sources 'agents') -Destination $content -Recurse -Force
& py -3 (Join-Path $PSScriptRoot 'complete_luna_arm_dependency.py') $content
if ($LASTEXITCODE -ne 0) { throw 'Arm dependency generation failed.' }
$kv3 = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->'
Set-Content (Join-Path $content 'addoninfo.txt') "$kv3`n{ IsPlayable = false }" -Encoding utf8NoBOM
Copy-Item (Join-Path $content 'addoninfo.txt') $game -Force
$meshDir = Join-Path $content 'models\saibochana\commander'
$matDir = Join-Path $content 'materials\saibochana\commander'
New-Item -ItemType Directory -Force -Path $meshDir,$matDir | Out-Null
Copy-Item -LiteralPath (Join-Path $AssetRoot '12.jpg') -Destination (Join-Path $matDir '12.jpg') -Force
# Six faces use the user's original image, so the icon is readable from every approach.
$vertices = @(@(-16,-16,-16),@(16,-16,-16),@(16,16,-16),@(-16,16,-16),@(-16,-16,16),@(16,-16,16),@(16,16,16),@(-16,16,16))
$faces = @(@(1,4,3,2),@(5,6,7,8),@(1,2,6,5),@(2,3,7,6),@(3,4,8,7),@(4,1,5,8))
$obj = @('mtllib marker.mtl','o commander_icon')
$obj += $vertices | ForEach-Object { 'v ' + ($_ -join ' ') }
$obj += @('vt 0 0','vt 1 0','vt 1 1','vt 0 1','usemtl marker')
foreach ($f in $faces) { $obj += "f $($f[0])/1 $($f[1])/2 $($f[2])/3"; $obj += "f $($f[0])/1 $($f[2])/3 $($f[3])/4" }
Set-Content (Join-Path $meshDir 'marker.obj') ($obj -join "`n") -Encoding utf8NoBOM
Set-Content (Join-Path $meshDir 'marker.mtl') "newmtl marker`nKd 1 1 1`nmap_Kd ../../../materials/saibochana/commander/12.jpg" -Encoding utf8NoBOM
Set-Content (Join-Path $matDir 'marker.vmat') @'
Layer0
{
    shader "csgo_unlitgeneric.vfx"
    F_RENDER_BACKFACES 1
    TextureColor "materials/saibochana/commander/12.jpg"
    g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
}
'@ -Encoding utf8NoBOM
$modelHeader = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc41:version{1e0c7f71-2d9b-4bb6-ae30-6b4f38b82b9e} -->'
# Keep the current modeldoc version GUID from the user-authored model.
$modelHeader = (Get-Content (Join-Path $content 'agents\fys\luna\luna.vmdl') -TotalCount 1)
$markerVmdl = @'
{
    rootNode = {
        _class = "RootNode"
        children = [
            { _class = "RenderMeshList" children = [
                { _class = "RenderMeshFile" name = "marker" filename = "models/saibochana/commander/marker.obj" import_translation = [0.0, 0.0, 0.0] import_rotation = [0.0, 0.0, 0.0] import_scale = 1.0 }
            ] },
            { _class = "MaterialGroupList" children = [
                { _class = "DefaultMaterialGroup" remaps = [ { from = "marker" to = "materials/saibochana/commander/marker.vmat" } ] use_global_default = true global_default_material = "materials/saibochana/commander/marker.vmat" }
            ] }
        ]
        model_archetype = "static_prop_model"
    }
}
'@
Set-Content (Join-Path $meshDir 'marker.vmdl') ($modelHeader + "`n" + $markerVmdl) -Encoding utf8NoBOM
$compiler = Join-Path $ClientGameRoot 'bin\win64\resourcecompiler.exe'
$argsText = '-game "' + (Join-Path $ClientGameRoot 'csgo') + '" -i "' + (Join-Path $content 'agents\fys\luna\luna.vmdl') + '" -i "' + (Join-Path $meshDir 'marker.vmdl') + '" -f -nop4 -v'
$argsText += ' -i "' + (Join-Path $content 'characters\fys\luna\arms\luna_arms.vmdl') + '"'
$p = Start-Process -FilePath $compiler -ArgumentList $argsText -WorkingDirectory (Split-Path $compiler) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $logRoot 'stdout.log') -RedirectStandardError (Join-Path $logRoot 'stderr.log')
Write-Output "Compiler exit=$($p.ExitCode); logs=$logRoot; compiled=$game"
$output = (Get-Content (Join-Path $logRoot 'stdout.log') -Raw) + (Get-Content (Join-Path $logRoot 'stderr.log') -Raw)
if ($p.ExitCode -ne 0 -or $output -match '(?im)ERROR:|failed to compile|error compiling') { throw 'Asset compile failed; inspect complete logs.' }
$extra = Join-Path $OutputRoot 'extra-assets'
foreach ($file in Get-ChildItem $game -Recurse -File -Filter '*_c') {
    $target = Join-Path $extra ([IO.Path]::GetRelativePath($game,$file.FullName))
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
foreach ($required in @('agents\fys\luna\luna.vmdl_c','models\saibochana\commander\marker.vmdl_c')) {
    if (!(Test-Path (Join-Path $extra $required))) { throw "Missing compiled output $required" }
}
Write-Output "Compiled assets: $extra (dependency audit required before publication)"
