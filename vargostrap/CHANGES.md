# Vargostrap: changes to upstream Froststrap files

Vargostrap is a fork of [Froststrap](https://github.com/Froststrap/Froststrap) (MPL-2.0).
Upstream base: `Froststrap/Froststrap@82bf6e9c` (main, 2026-10-29).

Rules this fork follows to stay easy to merge:

- New code goes in `Froststrap/Vargostrap/` (C#, namespace `Froststrap.Vargostrap`) and `vargostrap/` (docs, scripts).
- The C# namespace and the `Froststrap/` project folder are **not** renamed. That would touch every file and make each upstream merge a full conflict.
- Name changes that are plain string swaps are made by `vargostrap/rebrand.ps1`, which is idempotent.
- Every other edit to an upstream file is listed below. Each one is a single line or a small block, marked with a `Vargostrap:` comment where it isn't obvious.

## Merging upstream

```powershell
git fetch upstream
git merge upstream/main
# Conflict in a file listed under "Scripted"? Take upstream's version of that file, then:
powershell -ExecutionPolicy Bypass -File vargostrap\rebrand.ps1
# Conflict in a file listed under "Hand edits"? Re-apply the edit described in the table.
```

## Scripted (re-run `vargostrap/rebrand.ps1`)

| What | Files |
|---|---|
| `AssemblyName`, `Company`, `Description` | `Froststrap/Froststrap.csproj` |
| `avares://Froststrap/` → `avares://Vargostrap/` (resource URIs follow the assembly name) | about 24 `.axaml`/`.cs` files |
| Hardcoded window titles, headers and placeholders (`Title=`, `Text=`, …) | about 12 `.axaml` files (AboutPage is excluded and edited by hand) |
| "Froststrap" → "Vargostrap" in UI strings, every language. Keys never change. Strings about upstream (contributors, upstream wiki and Discord, the Studio plugin) keep "Froststrap". | `Froststrap/Resources/Strings*.resx` |
| Installer id/title (Velopack `--packId`/`--packTitle`, which name the Start menu and desktop shortcuts), Linux `.desktop`/package names, macOS bundle ids | `build/src/integrate/velopack.rs`, `build/src/action/publish/linux.rs`, `build/src/action/publish/macos.rs` |

## Hand edits

| File | Change | Why |
|---|---|---|
| `Froststrap/App.axaml.cs` | `ProjectName`/`ProjectOwner`/`ProjectRepository`/download and support links now point at `Vargostrap.Brand`. AUMID registry key and display name use `Brand.AppUserModelId` / `ProjectName`. | `ProjectName` drives the data folder (`%LocalAppData%\Vargostrap`), the uninstall key, the `HKCU\Software\Vargostrap` key and the exe lookup. Vargostrap and Froststrap can now be installed side by side. |
| `Froststrap/AppInitializer.cs` | Native resolver assembly list: `"Froststrap"` → `Brand.AssemblyName` | It must match the renamed assembly, or native libraries fail to load. |
| `Froststrap/UpdaterManager.cs` | Update feed → `Brand.UpdateRepository` (empty, so self-update is off). `IsInstalled` returns false when no feed is set. An inert source is used instead of `GithubSource("")`. | **Safety:** the upstream feed would "update" Vargostrap into Froststrap. |
| `Froststrap/Utility/InterProcessLock.cs` | Lock folder and file prefix use `App.ProjectName` | Stops Froststrap and Vargostrap from blocking each other's launches. |
| `Froststrap/LaunchHandler.cs`, `Froststrap/Utility/Processes.cs` | Settings-activation request file and background-updater kill event use `App.ProjectName` | Same as above. |
| `Froststrap/Integrations/AccountManager/CredentialStore.cs` | Credential-manager prefix uses `App.ProjectName` | Saved accounts are kept separate from an installed Froststrap. |
| `Froststrap/UriHandler.cs` | Linux `.desktop` name → `Brand.LinuxDesktopFile` | Matches the renamed Linux package. |
| `Froststrap/Integrations/FroststrapRichPresence.cs` | Large-image hover text → `App.ProjectName`, GitHub button → `Brand.RepositoryUrl` | Branding. The Discord application ID and image key are still upstream's (see Stage 4). |
| `Froststrap/UI/Elements/Bootstrapper/CustomDialog.Elements.cs` | Fallback custom-theme title → `App.ProjectName` | Branding. |
| `Froststrap/Bootstrapper/Bootstrapper.cs` | Flatpak-missing message says "Vargostrap" | Branding (hardcoded string). |
| `Froststrap/Integrations/RobloxServerFetcher.cs` | User-Agent → `Roblox/{App.ProjectName}` | Identifies the app honestly to the region API. |
| `Froststrap/Cli/FroststrapCliCommand.cs` | CLI name, description and examples say `vargostrap` | Branding. |
| `Froststrap/RemoteData.cs` | `VerifyEcdsa` and `ConfigPublicKey`: `private` → `internal` | Stage 2. The allowlist service re-checks the signature of Froststrap's `Data.json` instead of copying the crypto code. |
| `Froststrap/JsonContext.cs` | One `[JsonSerializable(typeof(Vargostrap.Models.VargostrapSettings))]` line | Stage 2. AOT-safe JSON for `VargostrapSettings.json`. A second `partial` declaration doesn't work: the source generator then emits duplicate files. |
| `Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs` | `"fastflags"` navigation creates `FpsOptimizationViewModel`, with an FPS Optimization title. Breadcrumb label, nav icon (`Gauge`), and a last-page case for the new view model. | Stage 2. FPS Optimization replaces FastFlag Settings under the same `fastflags` tag. |
| `Froststrap/UI/Elements/Settings/MainWindow.axaml(.cs)` | Nav item text and icon. `_pageInfo` title and icon. View-model→page mapping for `FpsOptimizationViewModel`, placed **before** `FastFlagsViewModel` because it is a subclass. Adds the `vargo` xmlns. | Stage 2. |
| `Froststrap/UI/Utility/SearchIndexBuilder.cs` | Search entry for `fastflags` uses the FPS Optimization title | Stage 2. |
| `Froststrap/Models/Persistable/Settings.cs` | Default `Theme` → `VargoTheme.DefaultTheme` (Custom). Default `CustomGradientStops` → `VargoTheme.CreateDefaultGradient()` (brown). | Stage 3 request: brown gradient by default. The gradient is only drawn for the Custom theme. |
| `Froststrap/UI/ViewModels/Settings/AppearanceViewModel.cs` | "Reset gradient" uses `VargoTheme.CreateDefaultGradient()` | Reset goes back to the brown gradient. |
| `Froststrap/Enums/BootstrapperIcon.cs`, `Froststrap/Enums/RobloxIcon.cs` | Display name of the default icon: `"Froststrap"` → `App.ProjectName` | The Appearance page lists the default icon as "Vargostrap". |
| `Froststrap/GBSEditor.cs` | `SettingsBackup.EnsureBackup()` before writing in `Save()` and `ImportSettings()` | Stage 3. One-time backup of the original Roblox settings file. |
| `Froststrap/Watcher.cs` | `VargoSession.Begin(...)` after the watchers start. `await vargoSession.EndAsync()` after Roblox exits. | Stage 3+. The single per-session hook for all Vargostrap features (the lock check now; AHK, priority and the network monitor later). |
| `Froststrap/Bootstrapper/Bootstrapper.cs` | `PrepareWatcherIfNeededAsync` also starts the watcher when `VargoSession.NeedsWatcher` | Stage 3+. Upstream only starts the watcher for activity tracking or auto-close integrations. |
| `Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs` (Stage 3) | `"globalsettings"` navigation creates `RobloxSettingsViewModel`. Title, breadcrumb, icon (`SlidersHorizontal`), last-page case. | Roblox Settings replaces Global Settings under the same tag. |
| `Froststrap/UI/Elements/Settings/MainWindow.axaml(.cs)` (Stage 3) | Nav text and icon. `_pageInfo`. View mapping (before `GlobalSettingsViewModel`). The off-screen search indexer builds the Vargostrap view models for `fastflags` and `globalsettings`. | |
| `Froststrap/UI/Utility/SearchIndexBuilder.cs` (Stage 3) | Search entry for `globalsettings` uses the Roblox Settings title | |
| `Froststrap/Integrations/FroststrapRichPresence.cs` | Client ID → `VargoRpc.ApplicationId`. The hardcoded presence is replaced by `VargoRpc.Build(...)`, with a fingerprint used for de-duplication. New `Refresh()` method. | Stage 4: customizable Vargostrap RPC. |
| `Froststrap/Integrations/PlayerDiscordRichPresence.cs` | One line: `VargoRpc.ApplyInGameLogo(ref smallImage, ref smallImageText)` after the default small image is chosen | Stage 4: optional Vargostrap logo in the in-game presence. The account avatar still wins. |
| `Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs` (Stage 4) | `SaveSettings()` also saves `VargoApp.Settings` | Vargostrap settings follow the window's Save button. |
| `Froststrap/UI/Elements/Settings/Pages/IntegrationsPage.axaml` | `vargoui` xmlns, plus one `<vargoui:VargoRpcSection />` under the Vargostrap RPC toggle | Stage 4. |
| `Froststrap/UI/Elements/Settings/Pages/BehaviourPage.axaml` | `vargoui` xmlns, plus one `<vargoui:AhkScriptsSection />` at the end of the page | Stage 4. |
| `Froststrap/Watcher.cs` (Stage 5) | The session hook now also passes `ActivityWatcher`: `VargoSession.Begin(_watcherData, ActivityWatcher)` | The network monitor needs game join/leave events and the server address. |
| `Froststrap/Bootstrapper/Bootstrapper.cs` (Stage 5) | After `GetClosestRegionsForAutoModeAsync`: `topRegions = Vargostrap.Network.RegionRanking.Rank(topRegions);` | Better Matchmaking tries your preferred regions first. |
| `Froststrap/Integrations/RobloxServerFetcher.cs` (Stage 5) | Same one-line ranking hook in `JoinBestServerAsync` | Same, for the tray menu's "join best server". |
| `Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs` (Stage 5) | `NavigateToPingPerformanceCommand` (declaration and init), `pingperformance` icon (`Activity`), last-page case | New Ping & Performance page. |
| `Froststrap/UI/Elements/Settings/MainWindow.axaml(.cs)` (Stage 5) | New nav item (above Region Selector), navigation action, `_pageInfo` entry, view mapping | |
| `Froststrap/UI/Utility/SearchIndexBuilder.cs` (Stage 5) | Search entry for `pingperformance` | |
| `Froststrap/Program.cs` (Stage 6) | At the top of `Main`: if the first argument is `--vargostrap-elevated`, run `ElevatedHelper.Run(args)` and exit | The UAC-elevated helper for admin-only changes. No UI, exits when done. |
| `Froststrap/Program.cs` (Stage 7) | Second line at the top of `Main`: if the first argument is `--vargostrap-reset`, run `SafeReset.RunFromCommandLine()` and exit | Safe-mode reset that works even if the UI won't start. |
| `Froststrap/App.axaml.cs` (Stage 6) | After `Updater.RunMigrations()`: `SafeReset.RecoverAtStartupAsync` in the background | Undoes system changes left over from a session that crashed. |
| `Froststrap/UI/Elements/About/Pages/AboutPage.axaml` | Title "Vargostrap". Repo and issue buttons → `Brand`. New **Credits** section linking Froststrap, Fishstrap and Bloxstrap, with a license note. | Visible upstream credit, as requested. |

## New files

| File | Purpose |
|---|---|
| `Froststrap/Vargostrap/Brand.cs` | Single source of truth for the fork's name, repo, update feed and ids |
| `Froststrap/Vargostrap/VargoApp.cs` | Vargostrap singletons (`VargostrapSettings.json` manager, allowlist service) |
| `Froststrap/Vargostrap/VargoStrings.cs` | English UI text that upstream files reference (nav titles) |
| `Froststrap/Vargostrap/Models/VargostrapSettings.cs` | Vargostrap-only settings |
| `Froststrap/Vargostrap/FastFlags/AllowlistService.cs` | Allowlist fetch (signed default or custom URL), disk cache, built-in snapshot, violation check |
| `Froststrap/Vargostrap/FastFlags/FpsPresets.cs` | Balanced / Max FPS / Low-end PC / Roblox defaults presets |
| `Froststrap/Vargostrap/UI/FpsOptimizationPage.axaml(.cs)`, `FpsOptimizationViewModel.cs` | FPS Optimization page (subclasses upstream `FastFlagsViewModel`) |
| `Froststrap/Vargostrap/VargoTheme.cs` | Default theme (Custom) and brown gradient |
| `Froststrap/Vargostrap/VargoSession.cs` | Per-Roblox-session hook called from `Watcher` |
| `Froststrap/Vargostrap/RobloxSettings/SettingsBackup.cs` | One-time backup and restore of `GlobalBasicSettings_13.xml` |
| `Froststrap/Vargostrap/RobloxSettings/SettingsLock.cs` | Opt-in read-only lock and per-session problem detection |
| `Froststrap/Vargostrap/UI/RobloxSettingsPage.axaml(.cs)`, `RobloxSettingsViewModel.cs` | Roblox Settings page (subclasses upstream `GlobalSettingsViewModel`) |
| `Froststrap/Vargostrap/Discord/VargoRpc.cs` | Builds the customized app presence and the in-game logo; holds the Discord application ID |
| `Froststrap/Vargostrap/UI/VargoRpcSection.axaml(.cs)`, `VargoRpcSectionViewModel.cs` | "Customize Vargostrap RPC" section with live preview |
| `Froststrap/Vargostrap/Native/Win32.cs` | LibraryImport bindings: job objects, suspended `CreateProcessW`, `AssocQueryStringW` |
| `Froststrap/Vargostrap/Ahk/AhkInstallation.cs` | Finds AutoHotkey via the `.ahk` association or `HKLM\SOFTWARE\AutoHotkey` |
| `Froststrap/Vargostrap/Ahk/AhkRunner.cs` | Starts the chosen scripts in a kill-on-close job and ends them with the session |
| `Froststrap/Vargostrap/UI/AhkScriptsSection.axaml(.cs)`, `AhkScriptsSectionViewModel.cs` | "AutoHotkey scripts" section with list, one-time warning and missing-install banner |
| `Froststrap/Vargostrap/Network/LatencyProbe.cs` | ICMP probe that falls back to timing a TCP handshake with `www.roblox.com:443` when the target ignores ping |
| `Froststrap/Vargostrap/Network/NetworkMonitor.cs` | 1 s sampling, jitter (RFC 3550), rolling loss, spike detection, throttled traceroute, live CSV |
| `Froststrap/Vargostrap/Network/Traceroute.cs` | ICMP TTL traceroute |
| `Froststrap/Vargostrap/Network/NetworkSessionLog.cs` | CSV session logs (`%LocalAppData%\Vargostrap\NetworkLogs`, newest 50 kept), summaries and live tail |
| `Froststrap/Vargostrap/Network/SessionNetworkWatcher.cs` | One monitor per game joined during a Roblox session |
| `Froststrap/Vargostrap/Network/DatacenterDirectory.cs` | RoValra datacenter/region list, cached on disk for 24 h |
| `Froststrap/Vargostrap/Network/RegionRanking.cs` | Preferred regions first, then upstream's nearest-first order |
| `Froststrap/Vargostrap/UI/PingPerformancePage.axaml(.cs)`, `PingPerformanceViewModel.cs`, `LatencyChart.cs` | Ping & Performance page and the self-drawn live chart |
| `Froststrap/Vargostrap/SystemChanges/ChangeJournal.cs` | Write-ahead journal of system changes (`ChangeJournal.json` for normal-user changes, `ChangeJournal.admin.json` for the helper's) |
| `Froststrap/Vargostrap/SystemChanges/SystemActions.cs` | Reversible changes (priority, suspend, registry, service stop, QoS policy), protected-process list, PowerShell runner |
| `Froststrap/Vargostrap/SystemChanges/ElevatedHelper.cs` | UAC-elevated helper: applies the admin plan, waits for Roblox to exit, undoes it; also a revert mode |
| `Froststrap/Vargostrap/SystemChanges/RestorePoint.cs` | System Restore point before the first admin change of the day |
| `Froststrap/Vargostrap/SystemChanges/SessionSystemChanges.cs` | Per-session orchestration of priority and traffic changes |
| `Froststrap/Vargostrap/SystemChanges/SafeReset.cs` | Startup crash recovery and the safe-mode reset |
| `Froststrap/Vargostrap/Performance/PriorityManager.cs` | Roblox priority class, EcoQoS opt-out, memory priority, GPU preference, DSCP plan |
| `Froststrap/Vargostrap/Performance/BackgroundAppCatalog.cs` | Known bandwidth-heavy apps and services with default actions; per-app remembered choices |
| `Froststrap/Vargostrap/Performance/BackgroundScanner.cs` | Live detection: process I/O rate plus TCP/UDP connection counts (no admin) |
| `Froststrap/Vargostrap/Performance/TrafficController.cs` | Per-session pause/lower/throttle with a 30 s rescan |
| `Froststrap/Vargostrap/UI/SystemTuningSection.axaml(.cs)`, `SystemTuningViewModel.cs` | Priority, background traffic and safety sections of Ping & Performance |
| `Froststrap/Vargostrap/Performance/WindowsTweaks.cs` | Power plans (powercfg), Game Mode and Game Bar/DVR tweaks, primary-adapter detection, adapter power-saving tweak |
| `Froststrap/Vargostrap/Performance/RobloxCleanup.cs` | Preview-then-delete cleanup of Roblox logs and cache |
| `Froststrap/Vargostrap/Performance/Benchmark.cs` | Benchmarks (PresentMon frames plus process CPU/RAM), results store, comparison, CSV export |
| `Froststrap/Vargostrap/Network/NetworkTools.cs` | Bufferbloat test (Cloudflare speed endpoints) and DNS benchmark (raw UDP queries) |
| `Froststrap/Vargostrap/UI/WindowsToolsSection.axaml(.cs)`, `WindowsToolsViewModel.cs` | Power/Windows settings, adapter panel, cleanup |
| `Froststrap/Vargostrap/UI/DiagnosticsSection.axaml(.cs)`, `DiagnosticsViewModel.cs` | Benchmarking and network tools |
| `vargostrap/samples/ahk-test.ahk` | Harmless tooltip-only script for testing the AHK feature |
| `vargostrap/rebrand.ps1` | Idempotent string rebrand (see above) |
| `vargostrap/build-dev.ps1` | Windows debug build without Visual Studio (nightly Rust GNU target + WinLibs MinGW) |
| `vargostrap/CHANGES.md` | This file |
| `vargostrap/INTEGRATION_PLAN.md` | Architecture notes and the staged feature plan |

## Intentionally left pointing at upstream

- `App.ProjectRemoteDataLink` (Froststrap's `config/Data.json`). It supplies the FastFlag allowlist, mappings and community data. Stage 2 makes the allowlist source configurable.
- Mod generator, community mods and the Studio RPC plugin download from Froststrap's repos. These are upstream features that Vargostrap reuses.
- The About page Discord button still goes to Froststrap's server. Change it when Vargostrap has its own.
- **Logo (replaced in Stage 3):** the file **names** stay the same so no code changes are needed, but their **contents** are now the Vargostrap logo (`C:\Users\bubba\Downloads\vargostrappng.ico`): `Froststrap/Froststrap.ico` (exe, installer, tray), `Froststrap/Resources/IconFroststrap.ico` (default bootstrapper/Roblox icon), `Froststrap/Froststrap.png` (About page, launch menu, shortcuts), and `build/icon512.png` / `build/macicon512.png` (Linux/macOS packages, upscaled from 256 px). On an upstream merge that touches these binaries, keep ours. `IconFroststrapClassic.ico` is the **Bloxstrap** logo (its label says "Bloxstrap"), so it stays. README images in `.resources/` are upstream's.
- `ProjectHelpLink` goes to the Bloxstrap wiki, as it does upstream.
- `LICENSE`, `LICENSES/`, `REUSE.toml` and SPDX headers are unchanged. New files use `SPDX-License-Identifier: MPL-2.0`.
