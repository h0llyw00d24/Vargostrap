# Maintaining Vargostrap

## Requirements (Windows)

- .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`
- Rust via rustup: `winget install Rustlang.Rustup`, then `rustup toolchain install nightly-x86_64-pc-windows-msvc nightly-x86_64-pc-windows-gnu --profile minimal`
- Visual Studio 2022 Build Tools with **Desktop development with C++** (release builds only)
- MinGW for dev builds: `winget install BrechtSanders.WinLibs.POSIX.UCRT`
- Velopack CLI for installers: `dotnet tool install -g vpk`
- Submodules: `git submodule update --init --recursive`

## Build

| What | Command | Output |
|---|---|---|
| Dev build (fast, no installer) | `powershell -ExecutionPolicy Bypass -File vargostrap\build-dev.ps1` | `Froststrap\bin\Debug\net10.0\win-x64\Vargostrap.exe` |
| Release + installer | `powershell -ExecutionPolicy Bypass -File vargostrap\build-release.ps1` | `.build\velopack\` (`Vargostrap-win-Setup.exe`, MSI, portable zip, update packages) |

Running any build registers it as the `roblox-player:` handler, replacing whichever launcher you used before (open that launcher once to switch back).

## Release

1. Tag the version: `git tag -a v0.2.0 -m "Vargostrap 0.2.0"` (the installer version comes from the latest tag).
2. Build the release.
3. Create a GitHub Release for the tag and upload **everything** in `.build\velopack\`. Installed copies update themselves from the latest release (`Brand.UpdateRepository`), and Velopack needs all the files, not just Setup.exe.

If `vpk` says "There is a release … equal or greater to the current version", `.build\velopack\` still holds a package of the same version. For a build you haven't published, delete that folder and rebuild. Otherwise tag a new version.

## Where things are

- `Froststrap/Vargostrap/`: all Vargostrap features (namespace `Froststrap.Vargostrap`). `Brand.cs` holds the name, repo and update feed.
- `vargostrap/rebrand.ps1`: the string-only rebrand (assembly name, UI text, installer names). Safe to re-run.
- The C# namespace, the `Froststrap/` folder and SPDX copyright headers keep the Froststrap name on purpose. Renaming them would turn every upstream merge into a conflict, and the headers are license attribution.

## Merging Froststrap updates

```
git fetch upstream
git merge upstream/main
```

- Conflict in a file the rebrand script touches (`Froststrap.csproj`, `.axaml` files, `Strings*.resx`, `build/src/...`): take upstream's version, then run `vargostrap\rebrand.ps1`.
- Conflict elsewhere: re-apply the Vargostrap hook. Hand edits are small and marked `// Vargostrap`:
  - Identity, updates, startup: `App.axaml.cs`, `AppInitializer.cs`, `Program.cs` (elevated helper + `--vargostrap-reset`), `UpdaterManager.cs`, `JsonContext.cs` (one line)
  - Name collisions with Froststrap: `Utility/InterProcessLock.cs`, `LaunchHandler.cs`, `Utility/Processes.cs`, `Integrations/AccountManager/CredentialStore.cs`, `UriHandler.cs`
  - Session hooks: `Watcher.cs` (2 lines), `Bootstrapper/Bootstrapper.cs` (watcher condition, region ranking), `Integrations/RobloxServerFetcher.cs` (region ranking)
  - Discord: `Integrations/FroststrapRichPresence.cs`, `Integrations/PlayerDiscordRichPresence.cs` (1 line)
  - Roblox settings: `GBSEditor.cs` (backup hook), `RemoteData.cs` (signature check made internal)
  - Theme and icons: `Models/Persistable/Settings.cs`, `UI/ViewModels/Settings/AppearanceViewModel.cs`, `Enums/BootstrapperIcon.cs`, `Enums/RobloxIcon.cs`
  - Pages and navigation: `UI/Elements/Settings/MainWindow.axaml(.cs)`, `UI/ViewModels/Settings/MainWindowViewModel.cs`, `UI/Utility/SearchIndexBuilder.cs`, `UI/Elements/Settings/Pages/IntegrationsPage.axaml` and `BehaviourPage.axaml` (one line each), `UI/Elements/About/Pages/AboutPage.axaml`
  - Misc strings: `Bootstrapper/Bootstrapper.cs`, `Cli/FroststrapCliCommand.cs`, `UI/Elements/Bootstrapper/CustomDialog.Elements.cs`
- Logo files keep upstream's names (`Froststrap.ico`, `Froststrap.png`, `Resources/IconFroststrap.ico`, `build/icon512.png`) but hold the Vargostrap logo. If a merge brings new versions, keep ours.

## Things that can break after a Roblox update

- FastFlag allowlist changes (the app refreshes it daily; update `AllowlistService.BuiltInSnapshot` when Roblox changes it)
- `GlobalBasicSettings_13.xml` name or format (Roblox Settings page)
- Roblox log-line formats (activity tracking, network monitor, Discord presence)
- RoValra's region API (server picker)
