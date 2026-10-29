# SPDX-FileCopyrightText: 2026 Vargostrap
#
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
    Release build of Vargostrap with installers (Velopack), via the repo's own `cargo xtask publish`.

.DESCRIPTION
    Needs Visual Studio 2022 Build Tools ("Desktop development with C++"), nightly Rust for the MSVC
    target, and the Velopack CLI (`dotnet tool install -g vpk`). See MAINTAINING.md.
    The version comes from the latest git tag (e.g. v0.1.0).

    Output: .build\velopack\ (Setup.exe, MSI, portable zip and the update packages to upload to a
    GitHub Release).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# NativeAOT's linker lookup calls vswhere.exe from PATH; it lives in the Visual Studio Installer folder.
$env:Path = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:USERPROFILE\.cargo\bin;$env:USERPROFILE\.dotnet\tools;$env:ProgramFiles\dotnet;$env:Path"
$env:RUSTUP_TOOLCHAIN = 'nightly-x86_64-pc-windows-msvc'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Push-Location $root
try {
    Write-Host "Version: $(git describe --tags --abbrev=0)"
    cargo xtask publish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "`nInstallers: $(Join-Path $root '.build\velopack')"
}
finally {
    Pop-Location
}
