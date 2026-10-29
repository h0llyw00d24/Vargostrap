<h1 align="center">Vargostrap</h1>

<p align="center">
    <img src="./.resources/vargostrap.png" height="200" alt="Vargostrap logo"/>
</p>

<p align="center">
    A Mike Vargo-inspired Roblox bootstrapper focused on FPS, ping and a smoother game, built on
    <a href="https://github.com/Froststrap/Froststrap">Froststrap</a>.
</p>

<p align="center">
    <a href="https://github.com/h0llyw00d24/Vargostrap/releases/latest">Download</a> ·
    <a href="https://github.com/h0llyw00d24/Vargostrap/issues">Report a problem</a>
</p>

---

## What it does

Vargostrap launches Roblox for you (from the website's Play button, like Roblox's own launcher) and adds tools around it. It never modifies the Roblox client, reads its memory or touches its anti-cheat: it only uses Roblox's allowed FastFlags and settings, public web APIs, and standard Windows features.

### FPS Optimization
- One-click **Balanced**, **Max FPS** and **Low-end PC** presets, plus individual toggles (gray sky, remove grass, pause voxelizer, low-detail meshes, texture quality, anti-aliasing, rendering API)
- **Allowlist checker**: warns about any FastFlag Roblox ignores, using a cached, refreshable copy of Roblox's official allowlist
- Frame-rate limit through Roblox's own setting, including an Unlimited option
- Full custom FastFlag editor

### Roblox Settings
- Edit Roblox's in-game settings (frame rate, graphics, volume, camera sensitivity and more) from outside the game
- Automatic backup of your original settings with one-click restore
- Optional read-only lock, with checks that warn you if it causes problems

### Ping & Performance
- **Network monitor**: live latency, jitter and packet loss chart, spike detection with automatic traceroute, per-game history with CSV export
- **Server picker** with preferred regions, built on automatic best-server matchmaking
- **Priority management**: CPU priority, EcoQoS exemption, high-performance GPU, optional DSCP tagging
- **Background traffic control**: pause, slow down or close bandwidth-heavy apps (cloud sync, launchers, torrents, updaters) while you play, and put them back afterwards
- **Power and Windows**: high-performance power plan while playing, Game Mode, Game Bar capture, network adapter power saving
- **Benchmarks** (FPS and frame times via Intel PresentMon, CPU, GPU, RAM) with before/after comparison
- **Bufferbloat test**, **DNS benchmark** and **cleanup** of Roblox logs and cache

### Integrations
- Customizable Discord Rich Presence for Vargostrap: text, logo, buttons, timer, with live preview
- Run your own AutoHotkey scripts alongside Roblox; they close when Roblox closes

### Safe by design
- Every system change is recorded before it's made and undone when Roblox closes, after a crash, or with one **Undo all system changes** button
- Admin rights are requested only for the few changes that need them, and a System Restore point is created first
- If the app won't start: `Vargostrap.exe --vargostrap-reset`

## A note on game rules

Remapping keys with AutoHotkey is generally fine, but macros that play for you (auto-clicking, auto-farming) can break Roblox's or a game's rules and get your account banned. You're responsible for the scripts you run.

## Building from source

See [MAINTAINING.md](MAINTAINING.md).

## Credits

Vargostrap is a fork of **[Froststrap](https://github.com/Froststrap/Froststrap)**, which is derived from **[Fishstrap](https://github.com/fishstrap/fishstrap)** and **[Bloxstrap](https://github.com/bloxstraplabs/bloxstrap)**. All credit for the original work goes to their authors and contributors, who are also listed in the app's About page.

Licensed under the [Mozilla Public License 2.0](LICENSE), like Froststrap. Third-party license texts are in [LICENSES](LICENSES).

Vargostrap is not affiliated with Roblox Corporation.
