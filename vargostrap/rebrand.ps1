# SPDX-FileCopyrightText: 2026 Vargostrap
#
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
    Applies the mechanical (string-only) part of the Vargostrap rebrand to the Froststrap tree.

.DESCRIPTION
    Idempotent: safe to run any number of times. After merging upstream Froststrap, resolve any
    conflict in a file this script touches by taking upstream's version, then run this script again.

    It changes only:
      * Froststrap.csproj  - AssemblyName, Company, Description
      * *.axaml / *.cs     - avares://Froststrap/ resource URIs (they follow AssemblyName)
      * Strings*.resx      - "Froststrap" in user-visible <value> text (keys never change)
      * build/src packaging scripts - installer id/title, shortcut names, bundle ids

    Semantic edits (constants, update feed, mutex names, About credits) are hand edits, listed in
    vargostrap/CHANGES.md. Those reference Froststrap.Vargostrap.Brand.

.PARAMETER WhatIf
    Lists the files that would change without writing anything.
#>
[CmdletBinding()]
param([switch]$WhatIf)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'Froststrap'

# Strings whose text is about upstream projects and must keep saying "Froststrap".
$resxKeepKeys = @(
    'Menu.About.Contributors.Description',      # credits upstream contributors
    'Menu.About.Description',                    # overridden below (English only)
    'Menu.Appearance.Launcher.Description',      # points to Froststrap's Discord for launcher themes
    'Dialog.Exception.Info.2',                   # links to the upstream wiki
    'Dialog.Exception.Info.2.Alt',
    'Menu.Integrations.StudioRPC.PluginConfirmation'  # the Studio plugin is Froststrap's
)
$aboutDescription = 'A Froststrap fork, built on Froststrap, Fishstrap and Bloxstrap.'

$script:changed = @()

function Update-TextFile([string]$path, [scriptblock]$transform) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $enc = New-Object Text.UTF8Encoding($hasBom)
    $text = $enc.GetString($bytes, ($(if ($hasBom) { 3 } else { 0 })), $bytes.Length - ($(if ($hasBom) { 3 } else { 0 })))
    $new = & $transform $text
    if ($new -cne $text) {
        $script:changed += $path.Substring($root.Length + 1)
        if (-not $WhatIf) { [IO.File]::WriteAllText($path, $new, $enc) }
    }
}

# 1. Project file
Update-TextFile (Join-Path $app 'Froststrap.csproj') {
    param($t)
    $t = $t -creplace '<AssemblyName>Froststrap</AssemblyName>', '<AssemblyName>Vargostrap</AssemblyName>'
    $t = $t -creplace '<Company>Froststrap</Company>', '<Company>Vargostrap</Company>'
    $t -creplace '<Description>Froststrap - A Fishstrap fork</Description>', '<Description>Vargostrap - A Froststrap fork</Description>'
}

# 2. avares:// URIs
Get-ChildItem $app -Recurse -File -Include *.axaml, *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object { Update-TextFile $_.FullName { param($t) $t -creplace 'avares://Froststrap/', 'avares://Vargostrap/' } }

# 2b. Hardcoded user-visible text in XAML (window titles, headers, placeholders). Matches whole
#     attribute values only, so class names, namespaces and wiki URLs are untouched.
$xamlTextAttrs = 'Title|Header|Text|PlaceholderText|Description|Name'
Get-ChildItem $app -Recurse -File -Filter *.axaml |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|Vargostrap)\\' -and $_.Name -ne 'AboutPage.axaml' } |
    ForEach-Object {
        Update-TextFile $_.FullName {
            param($t)
            [regex]::Replace($t, "(?<=\s(?:$xamlTextAttrs)="")([^""{]*?)Froststrap([^""]*"")", '${1}Vargostrap${2}')
        }
    }

# 3. Localised strings (all languages): only inside <value>, only for keys not in the keep list
$dataPattern = [regex]'(?s)(?<open><data name="(?<key>[^"]+)"[^>]*>\s*<value>)(?<val>[^<]*)(?<close></value>)'
Get-ChildItem (Join-Path $app 'Resources') -Filter 'Strings*.resx' | ForEach-Object {
    $isNeutral = $_.Name -eq 'Strings.resx'
    Update-TextFile $_.FullName {
        param($t)
        $dataPattern.Replace($t, {
            param($m)
            $key = $m.Groups['key'].Value
            $val = $m.Groups['val'].Value
            if ($key -eq 'Menu.About.Description' -and $isNeutral) { $val = $aboutDescription }
            elseif ($resxKeepKeys -notcontains $key) { $val = $val -creplace 'Froststrap', 'Vargostrap' }
            $m.Groups['open'].Value + $val + $m.Groups['close'].Value
        })
    }
}

# 4. Packaging (Velopack installer id/title -> Start menu + desktop shortcut names, Linux/macOS bundles)
$packaging = @('build\src\integrate\velopack.rs', 'build\src\action\publish\linux.rs', 'build\src\action\publish\macos.rs')
foreach ($rel in $packaging) {
    Update-TextFile (Join-Path $root $rel) {
        param($t)
        # Source paths stay as-is: the project folder is still named Froststrap/.
        $t = $t -creplace 'Froststrap/Froststrap', '@@SRC@@'
        $t = $t -creplace 'Froststrap', 'Vargostrap' -creplace 'froststrap', 'vargostrap'
        $t -creplace '@@SRC@@', 'Froststrap/Froststrap'
    }
}

if ($script:changed.Count -eq 0) { Write-Host 'Rebrand already applied; nothing to change.' }
else {
    $verb = if ($WhatIf) { 'Would change' } else { 'Changed' }
    Write-Host "$verb $($script:changed.Count) file(s):"
    $script:changed | ForEach-Object { Write-Host "  $_" }
}
