# Vargostrap: architecture notes and integration plan

## 0. Correction to the brief: Froststrap is no longer WPF

Upstream Froststrap has moved to **Avalonia 12** (cross-platform XAML: `.axaml` files) on **.NET 10**, published with NativeAOT. It has a small **Rust backend** (`backend/`: virtual display, notifications, logger) and a Rust build tool (`build/`) that runs Velopack to produce the installer. It runs on Windows, Linux (through Sober/Wine) and macOS.

Consequences:
- New pages are `.axaml` + view model (CommunityToolkit.Mvvm), not WPF.
- Code must stay **trim/AOT-safe**. That means no reflection-heavy libraries and no `dynamic`, and JSON goes through the source-generated `JsonContext.cs`.
- Most Ping & Performance features are Windows-only. They go behind `OperatingSystem.IsWindows()`, and their nav items hide on other platforms, the same way the existing Linux page hides on Windows.
- Building needs the .NET 10 SDK **and** the Rust toolchain (`cargo`), because `backend.csproj` runs `cargo build`.

## 1. How the codebase works

### Settings
- `JsonManager<T>` (`Froststrap/JsonManager.cs`) loads and saves one JSON file under `Paths.Base` (`%LocalAppData%\Vargostrap`). It tracks a saved hash for "unsaved changes".
- Singletons on `App` (`App.axaml.cs`): `App.Settings` (`Models/Persistable/Settings.cs`: every user option, grouped by page), `App.State` (installed mods, window state), `App.PlayerState`/`StudioState`, `App.FastFlags`, `App.GlobalSettings` (Roblox's own settings file) and `App.RemoteData` (remote config, including the FastFlag allowlist).
- Settings pages bind to view-model properties that read and write `App.Settings.Prop.*` directly. The Save button in the settings window persists them all.
- **Vargostrap plan:** Vargostrap options go in a separate `JsonManager<VargostrapSettings>` (`VargostrapSettings.json`), not in upstream's `Settings` class, so upstream changes to `Settings.cs` never conflict.

### Pages and navigation
- `UI/Elements/Settings/MainWindow.axaml` holds a FluentAvalonia `FANavigationView`. Each item has a string `Tag` (`"integrations"`, `"fastflags"`, `"globalsettings"`, …).
- `MainWindow.axaml.cs` maps tag → navigate command → view model → page (`IntegrationsViewModel => new IntegrationsPage()`), plus a tag → (title, icon) table for the header and search.
- `MainWindowViewModel.Navigate(tag, title, description, viewModel)` swaps the content. Building blocks are `controls:OptionControl`, `CardExpander` and `Card`.
- Page names in the brief map to upstream as follows: **FastFlags** = "FastFlag Settings" (`FastFlagsPage` + `FastFlags/FastFlagEditor`), **Roblox Settings** = "Global Settings" (`GlobalSettingsPage`, which edits `GlobalBasicSettings_13.xml`), **Integrations** = `IntegrationsPage`, **Bootstrapper** = `BehaviourPage` (its title string is "Bootstrapper").

### Launch flow
1. `Program.cs` parses the CLI (DotMake) into `App.LaunchSettings`. A `roblox-player:` URI, the menu, or a shortcut leads to `LaunchHandler.ProcessLaunchArgs()`.
2. `LaunchHandler.LaunchRoblox(mode)` creates a `Bootstrapper`, shows the chosen dialog style and awaits `Bootstrapper.Run()`.
3. `Bootstrapper.Run()`: check connectivity → wait for other instances (InterProcessLock) → get the latest version for the channel → download or upgrade packages if needed → `ApplyModifications()` (mods, fonts, cursors, writes `ClientSettings/ClientAppSettings.json` from `App.FastFlags`) → `StartRoblox()`. Pre-launch custom integrations run before Roblox starts.
4. On success it fills `PendingWatcherData` (process id, log file, launch mode), and `LaunchHandler.LaunchWatcher()` starts a `Watcher`.
5. `Watcher` (`Watcher.cs`) owns the lifetime integrations. `ActivityWatcher` tails the Roblox log file to detect game join and leave, server IP, place and job id. It feeds `PlayerDiscordRichPresence`, `IntegrationWatcher` (custom integrations launched on game join and closed on leave or exit), `WindowManipulation` and `Softkey`. The watcher ends when Roblox exits.

### FastFlag system
- `FastFlagManager` (a `JsonManager<Dictionary<string, object>>`) writes `…\Modifications\ClientSettings\ClientAppSettings.json`. Roblox reads this file at startup. This is the **supported** mechanism, limited by Roblox's allowlist.
- `PresetFlags` maps friendly preset keys to real flag names (gray sky, pause voxelizer, texture quality, MSAA, low-poly meshes, rendering API, grass). Enum dictionaries back the dropdowns. It also handles profiles, undo/redo and per-place profiles.
- **Allowlist (already partly there):** `RemoteData.Prop.AllowedFastFlags` is a base64 JSON `{ "Allowed": [...] }` that Froststrap publishes in its `config/Data.json`. `FastFlagEditor` decodes it to warn on non-allowlisted flags, and "Clean List" removes them. There is no on-disk cache with a timestamp, the source isn't configurable, and **presets aren't checked**.

### Discord RPC
- Three `DiscordRpcClient`s, each with its own Discord application id:
  - `FroststrapRichPresence` ("Playing Froststrap" while the app is open; shows the current page). App id `1399535282713399418`, image key `froststrap`.
  - `PlayerDiscordRichPresence` (in-game: place name and thumbnail as image **URLs**). Uses Roblox's app id `363445589247131668`, so Discord shows "Playing Roblox".
  - `StudioDiscordRichPresence` (Studio, with its own Froststrap Studio app id).
- The **displayed app name comes from the Discord application**, not from code. For Discord to say "Vargostrap" you need your own app in the Discord Developer Portal (free), and its id goes in `Brand`. Images can be HTTPS URLs, so the logo can be a hosted PNG rather than an uploaded Discord asset.

### Bootstrapper and integrations
- `Bootstrapper/` holds deployment, package download and verification, mods, and the launch itself.
- `Models/CustomIntegration.cs` plus `IntegrationWatcher` and `Bootstrapper.LaunchIntegrationAsync` already launch arbitrary programs (pre-launch, on game join, or for a specific game) and auto-close them. The AHK feature builds on this pattern.
- `Integrations/Cleaner.cs` already deletes Roblox and app logs/caches on a schedule.
- `Integrations/RobloxServerFetcher.cs` plus `RegionSelectorPage` already implement region-based server selection. They rely on the third-party `apis.rovalra.com` and on your Roblox cookie (opt-in) to resolve server regions.

## 2. Design decisions

1. **Keep the `Froststrap` namespace and folder.** Only the assembly, exe, data folder, installer and UI text are renamed. This keeps upstream merges small. It is invisible to users.
2. **Separate settings file** (`VargostrapSettings.json`) and **separate folder** (`Froststrap/Vargostrap/`) for all new features.
3. **Self-update is disabled** until you publish your own release feed. Otherwise Velopack would install Froststrap over Vargostrap.
4. **One "change journal" for every system change** (`Vargostrap/System/ChangeJournal`). Before any Windows change (power plan, priority, QoS policy, GPU preference, service pause, file read-only), the original value is written to `ChangeJournal.json`, and the entry is removed only after the change is reverted. Startup, Roblox exit, app exit and the **Safe-mode reset** (`Vargostrap.exe --vargostrap-reset`, plus a button) all replay the journal. This is how "restore everything, even after a crash" works.
5. **Preview before apply.** Every system-change feature goes through one confirmation dialog that lists the exact changes from the journal entries it is about to write.
6. **Admin only where needed.** The app keeps running unelevated. Admin-only actions (restore point, QoS policies, stopping services) run in a short-lived elevated helper (`Vargostrap.exe --vargostrap-elevated <action-file>`), using UAC `runas`, one batch at a time.
7. **Restore point.** `Checkpoint-Computer` before the first batch of system changes in a session. Windows allows **one restore point per 24 h** by default and needs System Protection enabled. If it can't create one, the dialog says so and the journal-based undo is still in place.
8. **New UI strings start in English only** and are hardcoded in Vargostrap's own XAML. They can move to a `VargostrapStrings.resx` later without touching upstream's resx.

## 3. Staged plan

Each stage ends with a build, a manual test checklist, and your go-ahead.

### Stage 2: FPS Optimization page (replaces FastFlag Settings). **Done (2026-10-29)**

Results:
- The live allowlist has **18 flags**. Roblox's official DevForum post and Froststrap's signed `Data.json` match exactly.
- **No presets were removed.** Every upstream preset (`FastFlagManager.PresetFlags`) and every bundled profile (`Resources/PresetFlags/*`) uses only allowlisted flags.
- **No FPS-unlock flag is allowlisted** (`DFIntTaskSchedulerTargetFps` is not on the list). The frame-rate control writes Roblox's own `FramerateCap` setting instead.
- **"Unlimited" frame rate** (added during Stage 3, on both pages) writes `FramerateCap = 9999`. Stored values of 1000 or more show as Unlimited. Roblox officially supports up to 240 ([DevForum](https://devforum.roblox.com/t/roblox-caps-fps-at-240-regardless-of-globalbasicsettings13xml-framerate-cap/4451048)) and may clamp to 240 regardless, so the UI says so. Memory-patching unlockers are deliberately not used. **Flag for Roblox updates:** if Roblox enforces 240, Unlimited behaves like 240.
- The rendering API offers D3D11 (default), Vulkan and OpenGL. All three `FFlagDebugGraphicsPrefer*` flags are allowlisted.

Original plan:
- New `FpsOptimizationPage` + view model in `Vargostrap/UI/`. The `"fastflags"` nav tag points to it (a one-line edit each in `MainWindowViewModel` and `MainWindow.axaml.cs`, plus the nav label). The existing **FastFlag Editor** stays as the "Custom flags" sub-page.
- `AllowlistService`: the source URL list lives in `VargostrapSettings` (default: Froststrap's `Data.json`, with an optional raw-JSON URL). It caches to `Cache\fflag-allowlist.json` with a fetched-at time and has a **Refresh** button. It checks presets and custom flags and shows a banner naming each non-compliant flag ("Roblox ignores this flag").
- Every preset flag is checked against the live list during this stage. Non-allowlisted presets are removed or disabled, and I'll report which.
- Balanced / Max FPS / Low-end PC presets, each built only from allowlisted flags, with plain-language descriptions.
- FPS cap/unlock (preferring Roblox's own `FramerateCap` setting where it covers the need), D3D11/Vulkan selection, and quality level, each only if allowlisted.

### Stage 3: Roblox Settings page. **Done (2026-10-29)**

Results:
- The page replaces Global Settings (same `globalsettings` tag) and shows only settings in Roblox's in-game menu: max frame rate, graphics quality (0 = automatic), fullscreen, volume, camera sensitivity, invert camera, shift lock switch, menu background transparency, text size, reduced motion, performance stats and chat translation.
- **Left off the main page on purpose**, because they aren't in Roblox's menu or I couldn't confirm the range: Max Quality, Vignette, Studio volume, party/voice-chat volume, first/third-person sensitivity vectors, haptics, player-list layout, the chat-translation tutorial flag and VR. All of them are still in the "Advanced settings editor" (upstream's raw editor).
- **Backup:** taken once, right before Vargostrap first writes the file (hooked into `GBSEditor.Save`/`ImportSettings`), and never overwritten. Restore puts it back and turns the lock off.
- **Lock:** opt-in, with a confirmation that explains the trade-off. Every Roblox session while it's on is checked for a quick exit (<20 s), settings-save errors in the Roblox log, or the read-only attribute disappearing. Any finding is shown in a dialog that offers to unlock, and on the page until dismissed. If the lock was removed outside Vargostrap, the page shows a "Lock again" banner.
- Also in this stage: the Vargostrap logo replaced the Froststrap logo, and the default theme is now a brown gradient (see CHANGES.md).

Original plan:
- Reuses `GBSEditor` (already reads and writes `GlobalBasicSettings_13.xml`, already has `SetReadOnly`). Adds a curated page of player-exposed settings only: framerate cap, master volume, mouse/camera sensitivity, graphics quality, fullscreen, and others from Roblox's in-game menu.
- Backup of the original file before the first change (`Backups\GlobalBasicSettings_13.original.xml`), with a Restore button.
- "Lock settings (read-only)", opt-in with an explanation. Each launch checks that Roblox started and wrote its log normally. If Roblox fails to save settings or launch while locked, the app reports it and offers to unlock.

### Stage 4: Integrations logo and AHK scripts. **Done (2026-10-29)**

Results:
- **Customize Vargostrap RPC** (Integrations page, under the Vargostrap RPC toggle):
  - Top and second line with `{page}`/`{version}` placeholders.
  - Timer, version and logo toggles; logo as an https link or a Discord art-asset name.
  - Optional logo on the in-game presence; two custom buttons.
  - Your own Discord application ID, with Apply (reconnects) and a link to the Developer Portal.
  - Live preview and "Reset to defaults".
  - Edits apply to Discord immediately and are saved with the Save button.
- **Logo in Discord:** Discord can't show local files. It needs either an https image link or an art asset uploaded to your own Discord application. Until one of those is set, the logo toggle has nothing to show and Discord shows the application icon. The UI explains both options.
- **AutoHotkey scripts** (end of the Bootstrapper page):
  - Toggle, add/remove scripts (`.ahk`/`.ah2`), and enable or disable each one.
  - One-time warning about macros and bans, which must be accepted before scripts can be added or the feature turned on.
  - "AutoHotkey isn't installed" banner with a download button.
  - Scripts start when Roblox (Player) starts through Vargostrap and are ended when it closes. They run in a job object with kill-on-close, so they also end if Vargostrap crashes.
  - Scripts are started the way double-clicking does (the `.ahk` association), so AutoHotkey v2's launcher still picks v1 or v2 per script.
- **Known limitation:** scripts are ended immediately (job termination), not asked to exit. A script that holds a key down at that moment could leave it logically pressed until you press it again. This is rare with remap scripts.

Original plan:

**Added on request: customization for the Vargostrap Discord RPC.** This is the "Playing Vargostrap" presence shown while the app is open (`FroststrapRichPresence`). Today it shows a fixed line ("Customize Roblox to your liking!"), the current page or dialog, an elapsed timer, the logo with the version on hover, and fixed GitHub and Discord buttons. Planned options, saved in `VargostrapSettings` and shown in a "Vargostrap RPC" expander on the Integrations page:
- Custom top line (details) and custom status line. Either one can still show the current page/dialog through a `{page}` placeholder.
- Toggles: show current page, show elapsed time, show version on hover, show the Vargostrap logo (the logo toggle from the original brief).
- Up to two custom buttons (label + https URL), or none. The defaults point at the Vargostrap repo.
- Live preview card on the page, plus a "Reset to defaults" button.
- Limits: Discord caps text at 128 characters and buttons at 32-character labels, and only https URLs. Inputs are validated against these.
- Caveat: the **app name** Discord shows ("Playing …") comes from the Discord application ID, not from code. Until you create a Vargostrap app in the Discord Developer Portal, it says "Froststrap". There will be a field for your own application ID.
- Integrations: "Show Vargostrap logo in Discord presence" toggle. Placeholder `Froststrap/Vargostrap/Assets/vargostrap-logo.png` for in-app use, plus `Brand.DiscordLogoUrl` (a hosted PNG URL) and `Brand.DiscordApplicationId`, with setup steps documented.
- Bootstrapper: AutoHotkey scripts list (add/remove, enable per script). Scripts are launched as `AutoHotkey64.exe "<script>"` when Roblox starts. They are assigned to a **Job Object** with kill-on-close so they always exit when Roblox closes, even if Vargostrap crashes. The app detects AutoHotkey (registry `HKLM\SOFTWARE\AutoHotkey`, then `PATH`) and links to autohotkey.com if it is missing. A one-time warning about macros and bans is shown. Nothing is generated, injected or read from Roblox.

### Stage 5: Ping & Performance shell, network monitor, server picker. **Done (2026-10-29)**

Tested on this PC: Roblox game servers (e.g. Dallas `128.116.14.33`) **don't answer ping**, and a traceroute gets no replies past the ISP edge (hop 5). That changed two parts of the design:

- **Network monitor:**
  - Each game joined is monitored once a second. It tries ping to the game server first and, after 5 silent seconds, falls back to timing a TCP handshake with `www.roblox.com:443`. That measures your connection to Roblox's network, so it is best for problems on your side (Wi-Fi, a busy line, the ISP), not the game server's own latency. The UI says this plainly.
  - Jitter, loss, spikes and a traceroute on spikes (at most once a minute).
  - Live chart, a "Run test" button, per-game CSV history with Export, and a traceroute viewer.
  - The page follows a session's live CSV, so it also works when Roblox was launched by a different Vargostrap process.
- **Server picker:**
  - Builds on upstream's Better Matchmaking (auto-join on launch, Skip button, manual region override), so there's no duplicate launch logic.
  - Adds a preferred-regions list (tried first, in order) and shows the region ranking.
  - "Find the best server now" takes a place ID, limited to one search per 10 s, with Join via the public `roblox://experiences/start` deep link.
  - The region list is cached for 24 h.
- **Dropped:** "learned per-region latency" (it was briefly built). It can't collect data because ICMP is ignored, and probing servers with Roblox's own UDP protocol would not be a public API. Dropping it also removed the ipinfo.io lookups it needed.
- **Third-party dependency (review):** server-to-region data comes from **RoValra** (`apis.rovalra.com`), as in upstream. Roblox's public server list (`games.roblox.com/.../servers/Public`) has no region or IP information. If RoValra changes or goes away, the picker falls back to upstream behaviour or finds nothing. Note: RoValra's `limit` parameter only accepts 10, 50 or 100.

Original plan:
- New nav item and page with sections. Windows-only parts hide elsewhere.
- Network monitor: the current game server IP comes from the log (`ActivityWatcher` already parses it). ICMP every 1 s, falling back to timed TCP connects to Roblox HTTPS endpoints when ICMP is blocked. It computes latency, jitter (RFC 3550 style) and loss over a rolling window, with a live chart, session history and CSV export. It runs a traceroute (ICMP with increasing TTL) when latency jumps past a threshold.
- Server picker: see the **review list** below for what public endpoints can and cannot tell us. It joins via the public `roblox://experiences/start?placeId=…&gameInstanceId=…` deep link. Results are cached, requests are rate-limited with backoff, and there is a manual override and a preferred-regions list.

### Stage 6: Background traffic control and priority management. **Done (2026-10-29)**

**Safety layer (shared with Stage 7):**
- Every system change is journaled before it's made and undone when Roblox closes.
- Leftovers from a crash are undone on the next Vargostrap start. Admin leftovers need a yes and one UAC prompt.
- The safe-mode reset button undoes everything, unlocks the Roblox settings file, and turns the features off.
- Admin-only changes run in a separate UAC-elevated helper (`Vargostrap.exe --vargostrap-elevated session …`). It applies the plan, waits for Roblox to exit, and undoes the changes itself, so it still cleans up if the main app crashes. That means one UAC prompt per launch, and only when an admin item is enabled.
- QoS policies go into Windows' non-persistent active store, so a reboot clears them whatever happens.
- A System Restore point is requested before the first admin change of the day. Windows may refuse it if System Protection is off or one was made in the last 24 h.
- **Tested here:**
  - A simulated crashed session (a paused, lowered-priority process plus an HKCU value) was fully undone at startup, and the process kept running normally.
  - The **admin helper** in session mode, with a stand-in process and a UAC approval: it created the restore point, set an HKLM value and created a DSCP QoS policy (for a dummy exe), waited for the stand-in to exit, then undid both and left the admin journal empty.
  - That test found that undo removed the value but left a registry **key** Vargostrap had created. Fixed: the journal now records "key created" before anything is touched, undo deletes the key if it's still empty, and the fix was verified with the helper's revert mode.
  - **Not tested:** a real Roblox session.

**Priority management** (opt-in, preview before enabling):
- Roblox gets Above normal or High CPU priority (never Realtime), an EcoQoS exemption and normal memory priority (Windows' maximum).
- High-performance GPU entry in `HKCU\…\DirectX\UserGpuPreferences`, per exe, re-applied each launch and removed on exit.
- Optional DSCP 46 QoS policy, plus `HKLM\…\Tcpip\QoS "Do not use NLA"=1` so Windows applies DSCP on non-domain PCs. Admin, with a note that it only helps if the router honours DSCP.
- No affinity, core parking or I/O priority changes. I/O priority has no documented API.
- The Roblox exe path is read with `QueryFullProcessImageName`, **not** `Process.MainModule`, which would read Roblox's memory.

**Background traffic control** (opt-in, preview before enabling):
- Live list of running catalogue apps (Windows Update, Delivery Optimization, BITS, OneDrive, Google Drive, Dropbox, iCloud, Steam, Epic, Battle.net, Xbox, EA, Ubisoft, browsers, torrent clients, Discord, Zoom, Teams, Spotify, OBS, updaters, Backblaze), plus up to 8 other busy internet-connected apps you can add.
- Actions per app: Ignore, Lower priority, Pause (suspend the process, or stop the service with admin) and Throttle uploads (QoS, admin). Choices are remembered per app.
- Never kills anything. Session-0 and system processes, Roblox and Vargostrap are hard-protected.
- **Limits (for your review):**
  - Activity is total process I/O (network and disk) plus connection count, because exact per-app network bytes need admin kernel tracing.
  - Windows QoS throttling only limits uploads, so Pause is the effective option for downloads.
  - Suspending an app can make it show "Not responding" until Roblox closes. That's expected, and it resumes.

Original plan:
- Live per-process network use from Windows ETW kernel network events, which needs admin (unelevated fallback: TCP connection stats, which miss UDP). A built-in catalogue covers Windows Update, Delivery Optimization, OneDrive, Google Drive, Dropbox, iCloud, Steam, Epic, Battle.net, Xbox, browsers, torrent clients, streaming and video-call apps, and backup and updater services.
- Actions per app: **throttle** (Windows QoS policy `New-NetQosPolicy -ThrottleRateActionBitsPerSecond`, admin), **pause** (stop a service and restore its prior state, or use the app's own pause mechanism where one exists; processes are never killed), or **ignore**. Allow/deny choices are remembered per app. A hard-coded protected list (System, csrss, lsass, svchost core groups, Defender, drivers, audio) can never be acted on. Everything is journaled and restored on Roblox exit, app exit, or the next start after a crash.
- Priority: Roblox to High or Above Normal (never Realtime), opt out of EcoQoS (`SetProcessInformation` power throttling), memory priority (documented `ProcessMemoryPriority`), and I/O priority (see review list). Background apps are lowered and restored afterwards. GPU preference goes in `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, set per exe path and **reapplied each launch** because the Roblox path changes with every update. There is an optional DSCP 46 QoS policy, with a note that it only helps if the router honours it. No affinity or core-parking changes.

### Stage 7: Windows settings, cleanup, benchmarking, network tools. **In progress (paused 2026-10-29)**

Done (compiles, not wired to UI yet, not tested):
- Persistent-tweak journals (`PersistentChanges.json` and `.admin.json`), never undone automatically. Selective revert by group; registry changes take an id prefix.
- `PowerPlans` (powercfg; Ultimate, else High performance), with a `PowerPlan` change kind and `PowerPlanDuringPlay` hooked into the session (journaled, undone on exit).
- `WindowsTweaks`: Game Mode and Game Bar/DVR capture as HKCU tweaks, plus primary-adapter detection (skips virtual adapters; flags Wi-Fi) and a PnPCapabilities lookup.
- Elevated helper `persist` / `unpersist <group>` modes and a `RegistryHklmDword` action (for adapter power saving).
- `BackgroundAction.CloseOnLaunch`: graceful close at launch only, never a default, can't be undone.
- `RobloxCleanup`: preview, then delete exactly the previewed files. Old logs (N days) and `%TEMP%\Roblox`.

Left to do:
- Benchmarking: PresentMon path chosen by the user, launched elevated; parse `MsBetweenPresents`/`FrameTime` and `MsGPUBusy`/`GPUBusy`; Roblox CPU% from process times; RAM from working set; before/after compare; CSV export.
- Network tools: bufferbloat test (Cloudflare speed endpoints, with a data-use warning and an SQM grade), DNS benchmark (raw UDP queries for Roblox domains; no automatic DNS change), adapter panel (Wi-Fi warning; the power-saving toggle through helper `persist`).
- Safe reset: also revert both persistent journals, plus a `--vargostrap-reset` CLI hook in Program.cs (`Paths.Initialize()` is safe to call there).
- UI sections on Ping & Performance (Windows settings, Cleanup, Benchmarking, Network tools), then build, test, and the CHANGES.md entries for Stage 7. Fix CA1861 in `WindowsTweaks.IsVirtual`.

Original plan:
- High Performance (or Ultimate) power plan during play via `powercfg`, then restore the previous plan. Toggles for Game Mode (`HKCU\Software\Microsoft\GameBar`) and Game Bar / Game DVR capture (`HKCU\System\GameConfigStore`, `…\GameDVR`).
- Cleanup: preview list with sizes, then delete old Roblox logs, http cache and temp. Builds on `Cleaner`. Optionally closes chosen non-essential apps on launch (graceful close, never force-kill by default).
- Benchmark: frame time and FPS from **PresentMon** (Intel's open-source ETW tool, run as a separate process, an optional download the user approves), CPU, GPU and RAM from Windows performance counters. Sessions are saved with tags such as "before" and "after", with a comparison view and CSV export.
- Network tools: bufferbloat test (idle ping vs ping while saturating download/upload, with SQM/Smart Queue advice), NIC tuning (disable "allow the computer to turn off this device" power saving, plus a Wi-Fi vs Ethernet warning), and an optional DNS benchmark. **No TCP/Nagle registry tweaks.**

## 4. Things likely to break after a Roblox update

- **FastFlag allowlist** changes without notice. That is why it is cached and refreshable and every flag is checked. Presets can silently stop working.
- **`GlobalBasicSettings_13.xml`**: the version suffix (`_13`) and setting names have changed before. A version bump would make the Roblox Settings page see "no file".
- **Log-line formats** that `ActivityWatcher` parses (game join, server IP). The network monitor and RPC depend on them.
- **Roblox exe path** changes every version (`Versions\version-<hash>\`), which is why GPU preference and QoS policies are reapplied per launch.
- **Server list API** fields and rate limits (`games.roblox.com/v1/games/{id}/servers/Public`).
- **Deep-link format** for joining a specific server.
- Hyperion (anti-cheat) changes could start objecting to external handles that change process priority (see review list).

## 5. Features to review: unsure whether Roblox allows them

1. **Changing Roblox's process priority, EcoQoS and memory/I/O priority from outside.** *(Stage 6 outcome: implemented with priority class, EcoQoS and memory priority only. Handles use PROCESS_SET_INFORMATION and PROCESS_QUERY_LIMITED_INFORMATION and never read memory. I/O priority was left out.)* This only opens a handle with `PROCESS_SET_INFORMATION` (what Task Manager does) and never touches memory. I/O priority has **no documented API**: it needs `NtSetInformationProcess`, which is undocumented. I plan to leave I/O priority out unless you want it.
2. **Read-only lock on `GlobalBasicSettings`.** Long used by Bloxstrap-family apps and not against the ToS that I know of, but it does stop Roblox from saving settings you change in-game.
3. **Auto-joining a specific server.** *(Stage 5 outcome: implemented on top of upstream's Better Matchmaking. Per-region latency turned out to be impossible to measure from outside Roblox, so regions are ranked by preference, then distance.)* Uses only the public deep link that roblox.com itself uses. Per-region latency **cannot** be measured from public, unauthenticated endpoints, because the server list has no IPs or regions. The options are: (a) rank by the server-reported `ping` field (that is the players' ping, not yours), (b) reuse Froststrap's existing opt-in cookie-based region lookup, which calls the game-join API with your session and depends on the third-party `apis.rovalra.com`, or (c) both. Hammering the server list can get rate-limited, which is why requests are capped.
4. **AutoHotkey launching.** Allowed as a launcher feature. The scripts themselves may break game rules, so the user carries that responsibility and the app warns about it.
5. **FPS unlock flag.** Settled in Stage 2: it is **not** allowlisted, so it isn't used. The FPS cap goes through Roblox's own `FramerateCap` setting (the in-game "Maximum Frame Rate"). The rendering-API flags are allowlisted.
6. **Pausing other apps' services and throttling their traffic** affects other software, not Roblox. It is safe for Roblox but needs admin and could annoy users if a restore fails. The journal plus safe-mode reset covers this.

## 6. Open items for you

- Your GitHub owner/repo name → `Brand.Owner` (the About and issue links point at a placeholder `Vargostrap/Vargostrap` until then).
- The Vargostrap logo (PNG, ideally 512×512), plus a hosted URL if you want it shown in Discord.
- A Discord application id if you want Discord to show "Playing Vargostrap" instead of "Froststrap".
- Whether to install the build tools here (.NET 10 SDK and Rust).
- If both Froststrap and Vargostrap are installed, **both register as the `roblox-player:` handler**. Whichever ran last opens Roblox from the website.
