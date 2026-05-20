<div align="center">

# Gaming Optimizer

**Automatic CPU affinity management for Windows gamers**

Auto-pins your game to your fastest cores the moment it launches.
Demotes background processes. Restores everything on exit.

[![CI](https://github.com/maxrenke/game-optimizer/actions/workflows/ci.yml/badge.svg)](https://github.com/maxrenke/game-optimizer/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/maxrenke/game-optimizer?label=download)](https://github.com/maxrenke/game-optimizer/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Windows 10+](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com)

**[⬇ Download](https://github.com/maxrenke/game-optimizer/releases/latest)** · **[Changelog](CHANGELOG.md)** · **[Contributing](CONTRIBUTING.md)**

<!-- Add dashboard GIF here: docs/screenshots/dashboard.gif -->

</div>

---

> [!NOTE]
> **CPU pinning is opt-in and off by default.** The app monitors and reports with zero process modifications until you explicitly enable pinning via the toggle in the dashboard or system tray. When pinning is off, no process affinity, priority, or system setting is touched - guaranteed by 106 tests.

---

## What it does

When a game is detected, Gaming Optimizer instantly:

| | |
|---|---|
| 📌 **Pins the game** | to your fastest CPU cores (P-cores on Intel hybrid, all cores on AMD) at High priority |
| 🎯 **Isolates media** | Firefox and VLC get dedicated cores so they never steal game CPU time |
| 🔇 **Demotes background** | OneDrive, iCloud, antivirus, etc. moved to BelowNormal priority and lowest disk I/O class |
| ❄️ **Suspends cloud sync** | OneDrive, Dropbox, Google Drive fully suspended for the session - no mid-game disk stutter |
| ⏱️ **Tightens timer resolution** | `timeBeginPeriod(1)` drops OS scheduling jitter from ~15ms to ~1ms |
| ⚙️ **Fixes scheduler quanta** | `Win32PrioritySeparation = 26` removes the foreground boost penalty |
| 🗑️ **Stops SysMain** | Superfetch is pointless on NVMe and harmful under memory pressure |

When the game closes, every change is reverted - processes restored, system settings back to Windows defaults.

---

## Features

### Real-time Dashboard

- **Per-zone CPU %** - Game zone / Firefox+VLC zone / Background zone tracked separately
- **GPU metrics** - utilization, VRAM %, temperature, power draw, core and memory clocks
- **Network sparkline** - RX/TX auto-scaling KB/s to MB/s, configurable 30s / 2m / 5m window
- **Latency and jitter** - pings a configurable host (default `1.1.1.1`) every 2 seconds; rendered as a colour-coded sparkline. Jitter is the metric that actually predicts online-game smoothness, not raw ping.
- **Bottleneck detector** - CPU-bound / GPU-bound / Balanced / Headroom, updated every second
- **Zone process list** - see exactly which processes are in each affinity zone right now
- **1-minute history sparklines** - CPU game-zone and GPU utilization over the last 60 seconds

### Automatic Detection

- **Instant** via WMI `Win32_ProcessStartTrace` - no polling delay when a game starts
- **Game library auto-scan** on first launch across Steam, Epic Games, GOG, Ubisoft Connect, EA App
- **Intel hybrid CPU** - auto-detects P-cores vs E-cores via registry MHz sampling; games go to P-cores only
- **1s fallback poll** catches anything WMI misses

### System Tray

- Live tooltip: `Gaming: Elden Ring | CPU 72% | GPU 94% 68°C | PIN ON`
- Double-click to show/hide; right-click for quick access to pinning toggle and exit
- Balloon notifications when thermal or utilization alerts fire

### Session Reports

- Avg/peak CPU%, GPU%, VRAM%, temperature per game session
- Bottleneck breakdown: % of session CPU-bound vs GPU-bound vs balanced
- Plain-text reports in `Documents\GamingOptimizer\`, pruned after 30 days
- Cumulative `sessions.csv` for spreadsheet trend tracking - never pruned

### Settings

- Affinity masks, alert thresholds, game library paths - all configurable
- **Per-game profiles** - different affinity and priority per game by process name
- **Suspend Apps** - editable list of apps to freeze during gameplay, with per-entry toggle
- **Disable Game DVR** - kills Xbox background recording while pinning is active
- **Start with Windows** - registers a `RunLevel=Highest` scheduled task to bypass UAC at login
- **Reset All** (Danger Zone) - restores every affinity and priority to Windows defaults instantly, without stopping the service

---

## Download

> [!WARNING]
> **Windows SmartScreen will warn "unrecognized app"** on first launch. Click **More info → Run anyway**. This is expected for open-source apps without a paid code-signing certificate. The `.zip` is built directly from this source via [GitHub Actions](.github/workflows/release.yml) - you can verify the build chain or build from source.

**[Download the latest release (.zip) →](https://github.com/maxrenke/game-optimizer/releases/latest)**

Extract anywhere and run `GameOptimizer.exe`. No installer.

**Requirements**
- Windows 10 1809 (build 17763) or Windows 11
- x64 processor
- Administrator rights - prompted once on launch via UAC

---

## Privacy

> [!IMPORTANT]
> **No telemetry. No analytics. No update checks. No phoning home.**

The only outbound connection Gaming Optimizer makes is the latency ping to your configured host (default: `1.1.1.1`). Change it in Settings or point it at your game server's IP. Everything else is local. The full source is here.

---

## How affinity zones work

On a 16-core system, the default layout looks like this:

```
Cores  0–11  [Game zone]    mask 0x0FFF  ←  your game, High priority
Cores 12–13  [Media zone]   mask 0x3000  ←  Firefox, VLC, Normal priority
Cores 14–15  [Background]   mask 0xC000  ←  everything else, BelowNormal priority
```

On **Intel hybrid CPUs** (P-cores + E-cores), `AffinityCalculator` reads the `~MHz` registry value per logical processor to separate them by frequency - no CPUID or native code needed. Games go exclusively to P-cores.

On **AMD CPUs**, the game zone gets all cores by default. 3D V-Cache core detection is planned.

All masks are configurable in Settings as hex values and validated on load so a typo can't accidentally pin a game to zero cores.

---

## Alerts

Sustained threshold breaches trigger tray balloon notifications:

| Metric | Default | Range |
|---|---|---|
| GPU temperature | 80 °C | 50–110 °C |
| VRAM usage | 90% | 50–100% |
| GPU utilization | 95% | 50–100% |
| CPU game-zone load | 90% | 50–100% |
| Sustained ticks before firing | 4 (~4 seconds) | 1–20 |

All thresholds are configurable in Settings.

---

## Tools

Three PowerShell scripts in `Tools/` for setup and debugging. All require elevation.

### `Install-Shortcuts.ps1`

Run once after cloning. Creates desktop shortcuts for the app, Affinity Watcher, and Reset Optimizer, and registers a `RunLevel=Highest` scheduled task so the app launches elevated without a UAC prompt.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Install-Shortcuts.ps1
```

Re-run after moving the repo or switching build configurations - it finds the newest build under `bin\` automatically.

---

### `Watch-Affinity.ps1`

A full-screen TUI that shows every CPU affinity and priority change on the system in real time, annotated with the Gaming Optimizer zone (GAME / MEDIA / BG) that owns each core range. Reads zone boundaries from `config.json` so the labels always match your configuration.

```
  CPU AFFINITY MONITOR                            elapsed 00:02:14
  16 cores   ALL = 0xFFFF [ALL]
  ──────────────────────────────────────────────────────────────────────────
  SUMMARY
    pinned events 12     restored events 8      priority changes 5
    currently modified 4   of which pinned 4

  CORE    0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
  PROC    1  1  1  1  1  1  1  1  1  1  1  1  0  0  0  0

  CURRENTLY MODIFIED   (4)
  NAME                   PID     AFFINITY               PRIORITY      ZONE
  eldenring              18432   0x0FFF [0-11]           High          GAME
  firefox                9104    0x3000 [12-13]          Normal        MEDIA
  onedrive               4280    0xC000 [14-15]          BelowNormal   BG
  googledrivefs          7392    0xC000 [14-15]          BelowNormal   BG

  RECENT EVENTS
  [14:32:01] PINNED   eldenring   PID 18432  0xFFFF [ALL] -> 0x0FFF [0-11]  (GAME)
  [14:32:01] PINNED   firefox     PID 9104   0xFFFF [ALL] -> 0x3000 [12-13] (MEDIA)
```

Every event is logged to `Documents\GamingOptimizer\`. Ctrl+C prints a session summary.

```powershell
# Watch everything
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1

# Filter to one process
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1 -Name firefox

# Faster refresh, no log
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1 -IntervalSeconds 0.5 -NoLog
```

---

### `Reset-Optimizer.ps1`

Standalone emergency reset - restores process and system state to Windows defaults without needing the app to be running. Useful after a crash or between test runs.

Resets in order:
1. `Win32PrioritySeparation` → `2` (Windows default)
2. SysMain → Automatic, started
3. All non-default CPU affinities → all cores
4. All BelowNormal / Idle priorities → Normal
5. Suspended cloud-sync apps (and anything in your `config.json`) → resumed, I/O priority restored

> [!TIP]
> If Gaming Optimizer is running with pinning **on** when you run this, it will re-apply its changes within a second. Turn pinning off or close the app first.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Reset-Optimizer.ps1
```

---

## Building from source

**Requirements**
- Windows 10 1809+ or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview channel)
- Windows App SDK: `winget install Microsoft.WindowsAppRuntimeInstaller`
- Developer Mode: Settings → System → For Developers → Developer Mode

```powershell
git clone https://github.com/maxrenke/game-optimizer
cd game-optimizer
dotnet run
```

The app self-elevates via UAC. Run from an elevated terminal during development to skip the prompt.

**Run the tests** (no elevation or display required):

```powershell
dotnet test Tests/GameOptimizer.Tests.csproj -c Release
```

106 tests across 13 files, targeting plain `net10.0-windows` without WinUI - CI runs them on `windows-latest` without a display.

---

## Common questions

**Does it actually help FPS?**
Depends on the game and system. The biggest wins are on Intel hybrid CPUs where games accidentally land on E-cores, and on systems where cloud sync or antivirus causes 1% low spikes. The latency and jitter improvements from timer resolution are consistent and measurable. FPS counter metrics are the wrong place to look - watch 1% lows and frame time variance instead.

**Why not just use Process Lasso or Razer Cortex?**
Process Lasso is excellent and does more than this app. Use it if you want a mature, full-featured product. This app is open source, has no background service that runs at all times, makes no network requests except a configurable ping, and focuses specifically on the subset of optimizations that are measurable rather than a kitchen-sink feature list.

**Windows only?**
Yes, by design. The app uses PDH, WMI, Win32 process APIs, `ntdll` for I/O priority and process suspension, the Windows registry for CPU topology, and WinUI 3 for the UI. There is no meaningful cross-platform equivalent for most of this.

**Does it need admin rights permanently?**
The app requests elevation once at launch. For Start with Windows, it creates a scheduled task with `RunLevel=Highest` so subsequent launches are silent. You can also use `Install-Shortcuts.ps1` to set this up with a desktop shortcut that skips the UAC prompt entirely.

---

<details>
<summary><strong>Architecture</strong></summary>

```
GameOptimizer/
├── Services/
│   ├── OptimizerService.cs      # Orchestrator: 1s scan tick + 3s heavy tick
│   ├── ProcessManager.cs        # Affinity/priority + WMI watchers; all ops gated on PinningEnabled
│   ├── ProcessControl.cs        # Suspend/resume + I/O priority via ntdll P/Invoke
│   ├── CpuMonitor.cs            # PDH API per-core sampling (~5ms with persistent query)
│   ├── GpuMonitor.cs            # nvidia-smi → rocm-smi → WDDM WMI fallback chain
│   ├── NetworkMonitor.cs        # NIC byte counters → KB/s ring buffer (300 samples / 5 min)
│   ├── LatencyMonitor.cs        # Ping-based RTT + RFC 3550 jitter estimation
│   ├── SystemService.cs         # Win32PrioritySeparation, SysMain, timeBeginPeriod, GameDVR
│   ├── MemoryService.cs         # Standby memory list purge via NtSetSystemInformation
│   ├── SessionTracker.cs        # Per-game session stats and report generation
│   ├── BottleneckDetector.cs    # 5-sample rolling CPU vs GPU saturation heuristic
│   ├── AlertMonitor.cs          # Threshold-based thermal/utilization alerts
│   ├── AffinityCalculator.cs    # P/E-core detection via registry MHz, zone mask generation
│   ├── GameLibraryScanner.cs    # Steam/Epic/GOG/Ubisoft/EA install path discovery
│   └── OptimizerConfig.cs       # JSON config with auto-detect defaults and Validate() on load
├── ViewModels/
│   ├── MainPageViewModel.cs     # CommunityToolkit.Mvvm, snapshot → observable properties
│   └── SettingsViewModel.cs     # Config editing, schtasks wiring for Start with Windows
├── Tray/
│   └── TrayService.cs           # H.NotifyIcon.WinUI, Win32 HICON loaded via LoadImage
├── Tests/                       # 106 xUnit tests across 13 files
└── Tools/                       # PowerShell setup and diagnostic scripts
```

**Pinning safety invariant**
`PinningEnabled = false` (the default) is a hard guarantee of zero process modifications. Every call site in `ProcessManager` that writes to `ProcessorAffinity` or `PriorityClass` is wrapped in `if (PinningEnabled)`. `ThrottleBg()` returns immediately at the top when pinning is off. WMI event callbacks for process start/stop do nothing when pinning is off. This invariant is verified by tests that subscribe to the `LogEntry` event and assert nothing is logged - since every actual modification produces a log entry.

**Loop design**
Two interleaved cadences. Fast path (1s): process scan, network sample, bottleneck update, snapshot emit. An initial snapshot fires before the first heavy sample so the UI renders immediately with live data. Slow path (3s): PDH CPU query + GPU monitor. PDH with a persistent open query costs ~5ms vs ~400ms for WMI `Win32_PerfFormattedData_PerfOS_Processor` - a 60-100x improvement that makes the 1s cadence viable.

**Thread safety**
Cross-thread process state uses `ConcurrentDictionary`. The event log uses `ConcurrentQueue` - WMI callbacks enqueue from WMI thread pool threads concurrently with the main loop draining them. No locks in the hot path.

**Clean exit**
Stop cancels the loop token, waits up to 10s, then walks every modified PID to restore affinity and priority, calls `timeEndPeriod(1)`, restores `Win32PrioritySeparation`, restarts SysMain if it was stopped, and disposes PDH and WMI handles. An `Interlocked` flag prevents double-cleanup if `Stop()` and `Dispose()` race.

</details>

<details>
<summary><strong>Tech Stack</strong></summary>

| Layer | Technology |
|---|---|
| UI framework | WinUI 3 / Windows App SDK 2.0 |
| Language | C# 13 / .NET 10 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| System tray | H.NotifyIcon.WinUI 2.4 |
| CPU sampling | PDH API (P/Invoke) |
| GPU sampling | nvidia-smi CLI / rocm-smi CLI / WDDM WMI fallback |
| Process events | WMI `Win32_ProcessStartTrace` / `Win32_ProcessStopTrace` |
| Timer resolution | `winmm.dll timeBeginPeriod` |
| Affinity | `System.Diagnostics.Process.ProcessorAffinity` |
| Suspend / I/O priority | `ntdll.dll NtSuspendProcess`, `NtSetInformationProcess` |
| Tray icon | `user32.dll LoadImage` (synchronous Win32 HICON) |
| Tests | xUnit 2.9 / 106 tests |
| CI | GitHub Actions (`windows-latest`) |

</details>

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and code conventions.

- **Bugs and feature requests** → [Issues](https://github.com/maxrenke/game-optimizer/issues)
- **Questions and discussion** → [Discussions](https://github.com/maxrenke/game-optimizer/discussions)
- **Security vulnerabilities** → [Security Advisories](https://github.com/maxrenke/game-optimizer/security/advisories/new) (private)

---

<div align="center">

[MIT License](LICENSE) · Copyright © 2026 Max Renke

</div>
