# SPDX-FileCopyrightText: 2026 Vargostrap
#
# SPDX-License-Identifier: MPL-2.0

<#
.SYNOPSIS
    Debug build of Vargostrap on Windows without Visual Studio.

.DESCRIPTION
    Upstream CI builds the Rust backend with nightly Rust and the MSVC toolchain. This script uses
    nightly Rust's GNU target plus WinLibs MinGW (for dlltool) instead, so Visual Studio Build Tools
    are not required for development builds.

    Prerequisites (one time):
      winget install Microsoft.DotNet.SDK.10
      winget install Rustlang.Rustup
      rustup toolchain install nightly-x86_64-pc-windows-gnu --profile minimal
      winget install BrechtSanders.WinLibs.POSIX.UCRT

    Release / NativeAOT publishing (the installer) still needs Visual Studio Build Tools with the
    "Desktop development with C++" workload, as upstream does.

    WARNING: running the built exe registers it as the roblox-player:/roblox: handler (upstream
    behaviour), replacing whatever bootstrapper you normally use.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File vargostrap\build-dev.ps1
#>
[CmdletBinding()]
param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$mingw = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Directory -Filter 'BrechtSanders.WinLibs*' -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName 'mingw64\bin' } | Where-Object { Test-Path (Join-Path $_ 'dlltool.exe') } | Select-Object -First 1
if (-not $mingw) { throw 'WinLibs MinGW not found (needed for dlltool). Run: winget install BrechtSanders.WinLibs.POSIX.UCRT' }

$env:Path = "$env:USERPROFILE\.cargo\bin;$env:ProgramFiles\dotnet;$mingw;$env:Path"
$env:RUSTUP_TOOLCHAIN = 'nightly-x86_64-pc-windows-gnu'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet build (Join-Path $root 'Froststrap\Froststrap.csproj') -c $Configuration -p:RustTargetTriple=x86_64-pc-windows-gnu -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`nOutput: $(Join-Path $root "Froststrap\bin\$Configuration\net10.0\win-x64\Vargostrap.exe")"
