# Gaming Optimizer

[![CI](https://github.com/maxrenke/game-optimizer/actions/workflows/ci.yml/badge.svg)](https://github.com/maxrenke/game-optimizer/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/maxrenke/game-optimizer)](https://github.com/maxrenke/game-optimizer/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Windows 10+](https://img.shields.io/badge/Windows-10%2B-0078D4)](https://www.microsoft.com/windows)

A native WinUI 3 desktop app that automatically pins your game to your fastest CPU cores, isolates Firefox/media players to separate cores, and demotes background processes the instant a game launches - then restores everything when the game closes.

> **CPU pinning is opt-in.** By default the app monitors and reports with zero process modifications. Enable pinning via the toggle in the dashboard or tray. When pinning is off, no process affinity, priority, or system setting is touched.

<!-- SCREENSHOT: Add a screenshot or GIF of the main dashboard here -->
<!-- Suggested: a 1000x700 screenshot showing the dashboard while a game is running -->

---

## Download

**[Download the latest release (.zip)](https://github.com/maxrenke/game-optimizer/releases/latest)**

Extract anywhere and run `GameOptimizer.exe`.

> **Windows SmartScreen may warn "unrecognized app"** - click "More info" then "Run anyway". This is expected for open-source apps without a paid code-signing certificate. The binary is built from this source via GitHub Actions; you can verify the workflow in [`.github/workflows/release.yml`](.github/workflows/release.yml) or build from source yourself.

**Requirements:**
- Windows 10 1809 (build 17763) or Windows 11
- x64 CPU
- Administrator rights (prompted on first launch via UAC)

---

## What it does

When you start a game, Gaming Optimizer instantly:

- **Pins the game** to your fastest CPU cores (P-cores on Intel hybrid, all cores on AMD) at High priority
- **Isolates Firefox and VLC** to dedicated cores so they never steal game CPU time
- **Demotes background processes** (OneDrive, iCloud, Malwarebytes, etc.) to BelowNormal CPU priority and lowest disk I/O class
- **Suspends cloud-sync apps** (OneDrive, Dropbox, Google Drive by default) for the duration of the session - no mid-game disk stutter from background syncs
- **Sets timer resolution to 1ms** via `timeBeginPeriod` so OS scheduling jitter drops from ~15ms to ~1ms
- **Tweaks Win32PrioritySeparation** to short fixed quanta, removing the foreground boost penalty
- **Stops SysMain (Superfetch)** - pointless on NVMe, actively harmful under memory pressure

When the game closes, all processes are restored to their original affinities and priorities, and all system settings are reverted.

---

## Features

### Real-time Dashboard
- Per-zone CPU utilization (Game zone / Firefox+VLC zone / Background zone)
- GPU metrics: utilization, VRAM %, temperature, power draw, core/mem clocks
- Network sparkline (RX/TX, auto-scaling KB/s to MB/s), configurable 30s / 2m / 5m window
- Latency and jitter: pings a configurable host (default `1.1.1.1`) - the metrics that actually predict online-game smoothness - with a colour-coded sparkline
- 1-minute CPU game-zone sparkline and GPU utilization sparkline
- Bottleneck detector: CPU-bound / GPU-bound / Balanced / Headroom
- Active zone process list - see exactly which processes are in each affinity zone
- Event log with color-coded entries

### Intelligent Detection
- **Instant process detection** via WMI `Win32_ProcessStartTrace` / `Win32_ProcessStopTrace` - no polling delay
- **Game library auto-scan** on first launch: Steam, Epic Games, GOG, Ubisoft Connect, EA App
- **Intel hybrid CPU support**: auto-detects P-cores vs E-cores via registry MHz sampling and assigns games to P-cores only
- **Fallback polling** every 1s catches anything WMI misses

### System Tray
- Real-time tooltip: `Gaming: Elden Ring | CPU 72% | GPU 94% 68C | PIN ON`
- Double-click to show/hide; right-click for context menu
- Balloon notifications for thermal/utilization alerts
- Toggle pinning or exit without opening the main window

### Session Reports
- Per-game session tracking: avg/peak CPU%, avg/peak GPU%, peak VRAM%, peak temp
- Bottleneck breakdown: % of session spent CPU-bound / GPU-bound / balanced
- Auto-saved to `Documents\GamingOptimizer\` as plain text; pruned after 30 days
- Cumulative `sessions.csv` (one row per session, never pruned) for spreadsheet trend tracking

### Settings
- Configurable affinity masks (hex), alert thresholds, game library paths, extra throttled processes
- **Per-game profiles** - override CPU affinity and priority per game by process name
- **Suspend Apps During Gameplay** - editable list with per-entry enable checkbox
- **Disable Game DVR** - disables Xbox background recording while pinning is active
- Start minimized / Start with Windows (creates a scheduled task at HIGHEST privilege to bypass UAC at login)
- **Reset All Process State** (Danger Zone) - immediately restores every affinity/priority to Windows defaults and clears all pinning state; useful after a crash

---

## Privacy

**No telemetry. No analytics. No update checks.**

The app makes exactly one type of outbound network connection: the configurable latency ping (default: `1.1.1.1`). Everything else is local. The source is here; you can verify this yourself.

---

## How affinity zones work

On a 16-core system the default layout:

```
Cores 0-11   [Game zone]      0x0FFF  - game gets the most cores at High priority
Cores 12-13  [Media zone]     0x3000  - Firefox + VLC, Normal priority
Cores 14-15  [Background]     0xC000  - everything else at BelowNormal priority
```

On Intel hybrid CPUs (e.g. i9-13900K with P-cores + E-cores), `AffinityCalculator` reads the `~MHz` value per logical processor from `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor` to separate P-cores from E-cores, then assigns the game zone exclusively to P-cores.

All masks are configurable in Settings as hex values. `AffinityCalculator.Calculate()` auto-computes sensible defaults for your specific CPU topology on first launch.

---

## Alerts

Configurable thresholds trigger tray balloon notifications:

| Metric | Default threshold |
|---|---|
| GPU temperature | 80 C |
| VRAM usage | 90% |
| GPU utilization | 95% |
| CPU zone utilization | 90% |
| Sustained ticks before alert | 4 (approx. 4 seconds) |

---

## Building from source

**Requirements:**
- Windows 10 1809+ or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview channel)
- Windows App SDK: `winget install Microsoft.WindowsAppRuntimeInstaller`
- Developer Mode: `Settings > System > For Developers > Developer Mode`

```powershell
git clone https://github.com/maxrenke/game-optimizer
cd game-optimizer
dotnet run
```

The app self-elevates via UAC. Run from an elevated terminal during development to skip the prompt.

**Run tests** (no elevation required):

```powershell
dotnet test Tests/GameOptimizer.Tests.csproj -c Release
```

106 tests across 13 test files. Runs on plain `net10.0-windows` without WinUI so CI works on `windows-latest` without a display.

---

## Architecture

```
GameOptimizer/
- Services/
  - OptimizerService.cs      # Orchestrator: 1s scan tick + 3s heavy tick + ResetAll
  - ProcessManager.cs        # Affinity/priority + WMI event watchers; all ops gated on PinningEnabled
  - ProcessControl.cs        # Suspend/resume + I/O priority P/Invoke (ntdll)
  - CpuMonitor.cs            # PDH API per-core sampling (~5ms, persistent query)
  - GpuMonitor.cs            # nvidia-smi -> rocm-smi -> WDDM WMI fallback
  - NetworkMonitor.cs        # NIC byte counters -> KB/s ring buffer (300 samples / 5 min)
  - LatencyMonitor.cs        # Ping-based RTT + RFC 3550 jitter
  - SystemService.cs         # Win32PrioritySeparation, SysMain, timeBeginPeriod, GameDVR
  - MemoryService.cs         # Standby memory list purge (NtSetSystemInformation)
  - SessionTracker.cs        # Per-game session stats + report generation
  - BottleneckDetector.cs    # 5-sample rolling CPU vs GPU saturation heuristic
  - AlertMonitor.cs          # Threshold-based thermal/utilization alerts
  - AffinityCalculator.cs    # P/E-core detection, zone mask generation
  - GameLibraryScanner.cs    # Steam/Epic/GOG/Ubisoft/EA path discovery
  - OptimizerConfig.cs       # JSON config with auto-detect + Validate on load
  - GameProfile.cs           # Per-game affinity/priority override model
  - SuspendApp.cs            # Config entry for an app to freeze during gameplay
- ViewModels/
  - MainPageViewModel.cs     # CommunityToolkit.Mvvm, ApplySnapshot
  - SettingsViewModel.cs     # Config editing + schtasks wiring
- Tray/
  - TrayService.cs           # H.NotifyIcon.WinUI, Win32 HICON via LoadImage
- Tests/                     # 106 xUnit tests across 13 files
- Tools/
  - Install-Shortcuts.ps1    # Creates desktop shortcuts + scheduled task (run once, elevated)
  - Watch-Affinity.ps1       # Live CPU affinity/priority watcher (TUI debug tool)
  - Reset-Optimizer.ps1      # Emergency reset script independent of the app
```

**Pinning safety invariant:** `PinningEnabled = false` (the default) guarantees zero process modifications. Every write to `ProcessorAffinity` or `PriorityClass` in `ProcessManager` is wrapped in `if (PinningEnabled)`. `ThrottleBg()` short-circuits at the top. WMI callbacks do nothing when pinning is off. This is verified by tests that subscribe to `LogEntry` and assert no log entries are emitted (since every actual modification produces a log entry).

**Loop design:** The fast path (every 1s) does process scanning, network sampling, bottleneck update, and snapshot emission. An initial snapshot is emitted before the first heavy sample so the UI renders immediately with live data. The slow path (every 3s) runs the PDH CPU query and GPU monitor - both are I/O-bound. PDH with a persistent open query takes ~5ms vs ~400ms for WMI `Win32_PerfFormattedData_PerfOS_Processor` - a 60-100x improvement.

**Thread safety:** All cross-thread process state uses `ConcurrentDictionary`. The event log uses `ConcurrentQueue` - WMI event callbacks enqueue log entries from WMI thread pool threads concurrently with the main loop reading them.

**Clean exit:** On stop, the service cancels the loop token, waits up to 10s, then restores all process affinities/priorities, calls `timeEndPeriod(1)`, restores Win32PrioritySeparation, restarts SysMain, and closes PDH/WMI handles. An `Interlocked` flag prevents double-cleanup.

---

## Tools

Three PowerShell scripts live in `Tools/` for setup and debugging. All require an elevated terminal.

### `Install-Shortcuts.ps1` - Desktop shortcut setup

Run once after cloning. Creates three desktop shortcuts:

- **GameOptimizer** - launches the app elevated with no UAC prompt, via a `RunLevel=Highest` scheduled task it registers
- **Affinity Watcher** - opens `Watch-Affinity.ps1` in your default terminal
- **Reset Optimizer** - opens `Reset-Optimizer.ps1` and keeps the window open

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Install-Shortcuts.ps1
```

Re-run after moving the repo or switching build configurations - it auto-detects the newest build under `bin\`.

---

### `Watch-Affinity.ps1` - Live CPU affinity/priority watcher

A full-screen TUI that shows every affinity and priority change on the system in real time, annotated with the Gaming Optimizer zone that owns each core range. Useful for verifying exactly what the app is doing (or not doing) to your processes.

```
  CPU AFFINITY MONITOR                            elapsed 00:02:14
  16 cores   ALL = 0xFFFF [ALL]
  --------------------------------------------------------------------------
  SUMMARY
    pinned events 12     restored events 8      priority changes 5
    currently modified 4   of which pinned 4

  CORE    0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
  PROC    1  1  1  1  1  1  1  1  1  1  1  1  0  0  0  0

  CURRENTLY MODIFIED   (4)
  NAME                   PID     AFFINITY                PRIORITY      ZONE
  eldenring              18432   0x0FFF [0-11]            High          GAME
  firefox                9104    0x3000 [12-13]           Normal        MEDIA
  onedrive               4280    0xC000 [14-15]           BelowNormal   BG
  googledrivefs          7392    0xC000 [14-15]           BelowNormal   BG

  RECENT EVENTS
  [14:32:01] PINNED   eldenring              PID 18432  0xFFFF -> 0x0FFF [0-11]  (GAME)
  [14:32:01] PINNED   firefox                PID 9104   0xFFFF -> 0x3000 [12-13] (MEDIA)
```

Reads affinity zone boundaries from `config.json` so the GAME / MEDIA / BG labels match your actual configuration. Every event is written to a timestamped log in `Documents\GamingOptimizer\`. Stop with Ctrl+C for a session summary.

```powershell
# Watch everything
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1

# Filter to a specific process name
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1 -Name firefox

# Faster refresh, no log file
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1 -IntervalSeconds 0.5 -NoLog
```

---

### `Reset-Optimizer.ps1` - Emergency standalone reset

Restores all process and system state to Windows defaults without requiring the app to be running. Use this if the app crashed and left processes pinned, or between test runs when you want a guaranteed clean slate.

What it resets:

1. `Win32PrioritySeparation` registry value back to `2` (Windows default)
2. SysMain service back to Automatic and started
3. Every process with a non-default affinity mask back to all cores
4. Every process at BelowNormal or Idle priority back to Normal
5. Known background and cloud-sync apps (OneDrive, Dropbox, Google Drive, and anything in your `config.json`) resumed from suspension and restored to normal disk I/O priority

Note: high-priority processes are intentionally not touched - many system processes (dwm, csrss, audiodg) legitimately run at High, and a game left at High after a crash is harmless and clears when the game exits.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Reset-Optimizer.ps1
```

> If Gaming Optimizer is running with CPU pinning **on** when you run this, it will re-apply its changes within a second. Turn pinning off (or close the app) first.

---

## Tech Stack

| Layer | Technology |
|---|---|
| UI framework | WinUI 3 / Windows App SDK 2.0 |
| Language | C# 13 / .NET 10 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| System tray | H.NotifyIcon.WinUI 2.4 |
| CPU sampling | PDH API (P/Invoke) |
| GPU sampling | nvidia-smi CLI / rocm-smi CLI / WDDM WMI fallback |
| Process events | WMI Win32_ProcessStartTrace / Win32_ProcessStopTrace |
| Timer resolution | winmm.dll timeBeginPeriod |
| Affinity | System.Diagnostics.Process.ProcessorAffinity |
| Tray icon | user32.dll LoadImage (synchronous Win32 HICON) |
| Tests | xUnit 2.9 / 106 tests |
| CI | GitHub Actions (windows-latest) |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and feature requests go in
[Issues](https://github.com/maxrenke/game-optimizer/issues). Questions and
discussion go in [Discussions](https://github.com/maxrenke/game-optimizer/discussions).

---

## Requires admin

CPU affinity changes and service management require administrator rights. The app
self-elevates via UAC on launch. If elevation is declined it runs in degraded
mode (monitoring only, no affinity or priority changes).

For Start with Windows, a scheduled task is created with `/rl HIGHEST` so UAC
is bypassed automatically at login.

---

## License

[MIT](LICENSE) - Copyright (c) 2026 Max Renke
