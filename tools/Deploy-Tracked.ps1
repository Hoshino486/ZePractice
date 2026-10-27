param(
 [Parameter(Mandatory=$true)][string]$Source,
 [Parameter(Mandatory=$true)][string]$Destination,
 [string]$ServerHome='E:\mapper\chana',
 [string]$BackupRoot='E:\mapper\chana\临时文件夹\ze-practice-build-20261011\backup',
 [switch]$SkipPdb
)
$ErrorActionPreference='Stop'
$base=[IO.Path]::GetFullPath($ServerHome).TrimEnd('\')+'\'
$dest=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
$sourceFull=[IO.Path]::GetFullPath($Source)
if(-not $dest.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Destination must be inside ServerHome.'}
$serverExe=Join-Path $ServerHome 'steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe'
if(Get-CimInstance Win32_Process -Filter "Name='cs2.exe'" | Where-Object ExecutablePath -eq $serverExe){throw 'Stop this dedicated server before deploying files.'}
[IO.Directory]::CreateDirectory($BackupRoot)|Out-Null
$manifestPath=Join-Path $BackupRoot 'installed-files.json'
$manifest=@(); if(Test-Path -LiteralPath $manifestPath){$parsedManifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json; $manifest=@($parsedManifest)}
$list=[Collections.Generic.List[object]]::new(); foreach($x in $manifest){$list.Add($x)}
$index=@{}; for($i=0;$i -lt $list.Count;$i++){$index[$list[$i].Target]=$i}
try {
 $files=if(Test-Path -LiteralPath $sourceFull -PathType Leaf){@(Get-Item -LiteralPath $sourceFull)}else{@(Get-ChildItem -LiteralPath $sourceFull -Recurse -File)}
 foreach($file in $files){
  if($SkipPdb -and $file.Extension -eq '.pdb'){continue}
  $relative=if(Test-Path -LiteralPath $sourceFull -PathType Leaf){$file.Name}else{$file.FullName.Substring($sourceFull.TrimEnd('\').Length).TrimStart('\')}
  $target=[IO.Path]::GetFullPath((Join-Path $dest $relative))
  if(-not $target.StartsWith($dest+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Target escaped destination.'}
  $previous=$null; $oldHash=$null; $existed=Test-Path -LiteralPath $target -PathType Leaf
  if($index.ContainsKey($target)){$entry=$list[$index[$target]]}
  else {
   if($existed){
    $oldHash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    $previous=Join-Path $BackupRoot ('originals\'+$target.Substring($base.Length))
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($previous))|Out-Null
    Copy-Item -LiteralPath $target -Destination $previous
   }
   $entry=[pscustomobject]@{Target=$target;Existed=$existed;Backup=$previous;OldSHA256=$oldHash;NewSHA256=$null}
   $index[$target]=$list.Count; $list.Add($entry)
  }
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $target -Force
  $entry.NewSHA256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
 }
} finally {
 [IO.File]::WriteAllText($manifestPath,(ConvertTo-Json -InputObject @($list.ToArray()) -Depth 5),[Text.UTF8Encoding]::new($false))
}
Write-Host ('Deployed '+$files.Count+' files to '+$dest+'. Tracked total: '+$list.Count)