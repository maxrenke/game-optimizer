# Gaming Optimizer

[![CI](https://github.com/maxrenke/game-optimizer/actions/workflows/ci.yml/badge.svg)](https://github.com/maxrenke/game-optimizer/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Windows 11](https://img.shields.io/badge/Windows-11-0078D4)](https://www.microsoft.com/windows)

A native WinUI 3 desktop app that squeezes every frame out of your PC by managing CPU affinity, process priorities, and system timers in real time - automatically, the moment a game launches.

---

## What it does

When a game starts, Gaming Optimizer instantly:

- **Pins the game** to your fastest CPU cores (P-cores on Intel hybrid, all cores on AMD) at High priority
- **Isolates Firefox and VLC** to dedicated cores so they never steal game CPU time
- **Demotes background processes** (OneDrive, iCloud, MalwareBytes, etc.) to BelowNormal priority on the remaining cores
- **Sets timer resolution to 1ms** via `timeBeginPeriod` so OS scheduling jitter drops from ~15ms to ~1ms
- **Tweaks Win32PrioritySeparation** to short fixed quanta, eliminating the foreground boost penalty
- **Suspends SysMain (Superfetch)** - pointless on NVMe, actively harmful under memory pressure

When the game closes, everything is restored to its original state.

---

## Features

### Real-time Dashboard
- Per-zone CPU utilization (Game zone / Firefox+VLC zone / Background zone)
- GPU metrics: utilization, VRAM, temperature, power draw, core/mem clocks
- 30-second network sparkline (RX/TX KB/s with peak tracking)
- Bottleneck detector: CPU-bound / GPU-bound / Balanced / Headroom
- Active zone process list - see exactly which processes are in each affinity zone
- Event log with color-coded entries

### Intelligent Detection
- **Instant process detection** via WMI `Win32_ProcessStartTrace` / `Win32_ProcessStopTrace` - no polling delay
- **Game library auto-scan** on first launch: Steam, Epic Games, GOG, Ubisoft Connect, EA App
- **Intel hybrid CPU support**: auto-detects P-cores vs E-cores via registry MHz sampling and assigns games to P-cores only
- **Fallback polling** every 1s catches anything WMI misses

### System Tray
- Real-time tooltip: `Gaming: Elden Ring | PIN ON`
- Balloon notifications for thermal/utilization alerts
- Toggle pinning, show status, or exit without opening the window

### Session Reports
- Per-game session tracking: avg/peak CPU%, avg/peak GPU%, peak VRAM%, peak temp
- Bottleneck breakdown: % of session spent CPU-bound / GPU-bound / balanced
- Auto-saved to `Documents\GamingOptimizer\` as plain text; pruned after 30 days

### Settings
- Configurable affinity masks (hex), alert thresholds, game paths, extra throttled processes
- Start minimized / Start with Windows (creates a scheduled task at HIGHEST privilege)
- Auto-detects NIC if the configured one is missing

---

## Architecture

```
GameOptimizer/
├── Services/
│   ├── OptimizerService.cs      # Orchestrator: 1s scan tick + 3s heavy tick
│   ├── ProcessManager.cs        # Affinity/priority + WMI event watchers
│   ├── CpuMonitor.cs            # PDH API per-core sampling (~5ms, persistent query)
│   ├── GpuMonitor.cs            # NVML / NvAPI GPU metrics
│   ├── NetworkMonitor.cs        # NIC byte counters -> KB/s ring buffer
│   ├── SystemService.cs         # Win32PrioritySeparation, SysMain, timeBeginPeriod
│   ├── SessionTracker.cs        # Per-game session stats + report generation
│   ├── BottleneckDetector.cs    # CPU vs GPU saturation heuristic
│   ├── AlertMonitor.cs          # Threshold-based thermal/utilization alerts
│   ├── AffinityCalculator.cs    # P/E-core detection, zone mask generation
│   ├── GameLibraryScanner.cs    # Steam/Epic/GOG/Ubisoft/EA path discovery
│   └── OptimizerConfig.cs       # JSON config with auto-detect on first run
├── ViewModels/
│   ├── MainPageViewModel.cs     # CommunityToolkit.Mvvm, ApplySnapshot
│   └── SettingsViewModel.cs     # Config editing + schtasks wiring
├── Tray/
│   └── TrayService.cs           # H.NotifyIcon.WinUI, Win32 HICON via LoadImage
├── Converters/
│   └── Converters.cs            # IValueConverter implementations
├── Tests/
│   ├── AffinityCalculatorTests.cs
│   ├── CpuMonitorTests.cs
│   ├── BottleneckDetectorTests.cs
│   ├── AlertMonitorTests.cs
│   ├── NetworkMonitorTests.cs
│   └── SessionTrackerTests.cs
└── .github/workflows/ci.yml     # xUnit on windows-latest, .NET 10 preview
```

**Loop design:** `OptimizerService` runs two interleaved tasks. The fast path (every 1s) does process scanning, network sampling, bottleneck update, and snapshot emission. The slow path (every 3s) calls the PDH CPU query and GPU monitor - both are I/O-bound and would stall the fast path if run every second.

**Thread safety:** All cross-thread state uses `ConcurrentDictionary`. WMI event callbacks fire on a WMI thread pool; the main scan loop runs on a `Task.Run` thread. No locks needed in the hot path.

**PDH vs WMI:** WMI `Win32_PerfFormattedData_PerfOS_Processor` takes 300-500ms per query (COM overhead + kernel round-trip). The PDH API with a persistent open query takes ~5ms - a 60-100x improvement that unlocks true 1s cadence for everything.

---

## Tech Stack

| Layer | Technology |
|---|---|
| UI framework | WinUI 3 / Windows App SDK 2.0 |
| Language | C# 13 / .NET 10 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| System tray | H.NotifyIcon.WinUI 2.4 |
| CPU sampling | PDH API (P/Invoke) |
| Process events | WMI Win32_ProcessStartTrace |
| Timer resolution | winmm.dll timeBeginPeriod |
| Affinity | kernel32.dll SetProcessAffinityMask |
| Tray icon | user32.dll LoadImage (real Win32 HICON) |
| Tests | xUnit 2.9 / 49 tests |
| CI | GitHub Actions (windows-latest) |

---

## Building

Requires:
- Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) workload
- Developer Mode enabled (`Settings > System > For Developers`)

```powershell
git clone https://github.com/maxrenke/game-optimizer
cd game-optimizer
dotnet run
```

Run tests:
```powershell
dotnet test Tests/GameOptimizer.Tests.csproj -c Release
```

---

## How affinity zones work

On a 16-core system the default layout looks like this:

```
Cores 0-11   [Game zone]      0x0FFF  - your game gets the most cores at High priority
Cores 12-13  [Media zone]     0x3000  - Firefox + VLC, Normal priority, isolated from game
Cores 14-15  [Background]     0xC000  - everything else at BelowNormal priority
```

On Intel hybrid CPUs (e.g. i9-13900K with P-cores + E-cores), `AffinityCalculator` reads the `~MHz` value per logical processor from `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor` to separate P-cores from E-cores, then assigns the game zone exclusively to P-cores.

All masks are configurable in Settings as hex values.

---

## Alerts

Configurable thresholds trigger tray balloon notifications:

| Metric | Default threshold |
|---|---|
| GPU temperature | 80°C |
| VRAM usage | 90% |
| GPU utilization | 95% |
| CPU zone utilization | 90% |
| Sustained ticks before alert | 4 (4 seconds) |

---

## Requires admin

CPU affinity changes and service management require administrator rights. The app self-elevates via UAC on launch. If elevation is declined it runs in degraded mode (monitoring only, no affinity/priority changes).

For Start with Windows, a scheduled task is created with `/rl HIGHEST` so UAC is bypassed automatically at login.

---

## License

MIT
