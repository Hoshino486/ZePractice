#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]]$Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Valve's shipped cs_script_demo/maps/scripts/point_script.d.ts, CustomHudLayout:
# Panel: id,class,hittest; Label: +text; Image: +src; Button: id,class only.
# The generic Panorama compiler accepts more than the in-game CustomHud validator.
# In particular hittestchildren compiles, but rejects the entire HUD at runtime.
foreach ($file in $Path) {
    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($file), $settings)
    try {
        $xml = New-Object Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($reader)
    } finally { $reader.Dispose() }
    if ($xml.DocumentElement.Name -cne 'root') { throw "CustomHud '$file': expected root element." }
    $ids = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($node in $xml.SelectNodes('//*')) {
        if ($node.NamespaceURI -or $node.Prefix) { throw "CustomHud '$file': XML namespaces are not supported." }
        $allowed = @()
        switch -CaseSensitive ($node.Name) {
            'root'   { $allowed = @() }
            'styles' { $allowed = @(); if ($node.ParentNode.Name -cne 'root') { throw "CustomHud '$file': styles must be below root." } }
            'include' {
                $allowed = @('src')
                if ($node.ParentNode.Name -cne 'styles') { throw "CustomHud '$file': only stylesheet includes are supported." }
                if ($node.GetAttribute('src') -cnotmatch '^s2r://panorama/styles/custom_game/[A-Za-z0-9_./-]+\.vcss_c$' -or
                    $node.GetAttribute('src').Split('/') -contains '..') { throw "CustomHud '$file': invalid stylesheet path." }
            }
            'Panel'  { $allowed = @('id','class','hittest') }
            'Label'  { $allowed = @('id','class','hittest','text') }
            'Image'  { $allowed = @('id','class','hittest','src') }
            'Button' { $allowed = @('id','class') }
            default  { throw "CustomHud '$file': unsupported element <$($node.Name)>." }
        }
        foreach ($attribute in $node.Attributes) {
            if ($allowed -cnotcontains $attribute.Name) {
                throw "CustomHud '$file': unsupported attribute '$($attribute.Name)' on <$($node.Name)> id='$($node.GetAttribute('id'))'."
            }
        }
        if ($node.HasAttribute('id') -and (-not $node.GetAttribute('id') -or -not $ids.Add($node.GetAttribute('id')))) {
            throw "CustomHud '$file': empty or duplicate panel id '$($node.GetAttribute('id'))'."
        }
        if ($node.HasAttribute('hittest') -and @('true','false') -cnotcontains $node.GetAttribute('hittest')) {
            throw "CustomHud '$file': hittest must be true or false."
        }
    }
    Write-Output "PASS CustomHud client contract: $file"
}
