# game-optimizer

A real-time TUI (Terminal User Interface) for gaming performance optimization on Windows. Pins game processes to dedicated CPU cores, throttles background tasks, monitors GPU/CPU/network metrics, detects bottlenecks, and saves per-session stats — all from a single terminal window.

![CI](https://github.com/maxrenke/game-optimizer/actions/workflows/test.yml/badge.svg)

---

## What it does

| Feature | Detail |
|---|---|
| **CPU affinity zoning** | Cores 0–5 → games (High priority), Core 6 → Firefox (Normal), Core 7 → background (Below Normal) |
| **Background throttling** | Automatically restricts OneDrive, iCloud, antivirus scans, etc. while a game is running |
| **Real-time dashboard** | CPU/GPU utilization, VRAM, temperature, power draw, clock speeds, network RX/TX sparkline |
| **Bottleneck detection** | Classifies each tick as GPU-bound, CPU-bound, Balanced, or Headroom using a 5-sample rolling window |
| **Alerts** | GPU temp ≥ 80 °C, VRAM ≥ 90 %, sustained GPU util ≥ 95 % for 12 s, sustained CPU zone ≥ 90 % |
| **Session reports** | Per-game stats (peak CPU/GPU, VRAM, duration, bottleneck distribution) saved to `~/Documents/GamingOptimizer/` |
| **Crash recovery** | Trap + finally blocks and a `-Mode cleanup` flag restore all process state even after a crash |

---

## Requirements

| Requirement | Notes |
|---|---|
| **Windows 10/11** | Required for WMI/CIM, Process affinity APIs |
| **PowerShell 7+ (pwsh)** | `winget install Microsoft.PowerShell` |
| **Administrator** | Needed for affinity/priority changes and registry edits |
| **NVIDIA GPU + nvidia-smi** | In PATH; installed with the NVIDIA driver |
| **ThreadJob module** | Ships with PowerShell 7 (`Install-Module ThreadJob` for PS 5) |

The script was written for a **Ryzen 7 5800X3D / 8 c / 16 t** system with an **RTX 4070 SUPER**. See [Configuration](#configuration) if your hardware differs.

---

## Quick start

```powershell
# 1. Clone
git clone https://github.com/maxrenke/game-optimizer.git
cd game-optimizer

# 2. (Optional) run the setup wizard to generate config.psd1
pwsh -ExecutionPolicy Bypass -File gaming-optimizer.ps1 -Mode configure

# 3. Launch as admin (right-click Terminal → "Run as Administrator")
pwsh -ExecutionPolicy Bypass -File gaming-optimizer.ps1
```

The dashboard refreshes every ~3 seconds. Press **Q** to exit cleanly.

### Install (recommended for regular use)

```powershell
# As Administrator — copies to AppData, creates desktop shortcuts,
# registers a cleanup scheduled task for crash recovery
pwsh -ExecutionPolicy Bypass -File Install.ps1

# To uninstall
pwsh -ExecutionPolicy Bypass -File Install.ps1 -Uninstall
```

### Modes

| Mode | Command | What it does |
|---|---|---|
| **TUI** (default) | `gaming-optimizer.ps1` | Full real-time dashboard |
| **Tray** | `-Mode tray` | Hides window, runs in background, Windows toast alerts only |
| **Configure** | `-Mode configure` | Interactive wizard that writes `config.psd1` |
| **Cleanup** | `-Mode cleanup` | Restores all system state (run after a crash) |

### Tray mode

Runs silently in the background — no TUI — and fires a Windows toast notification whenever an alert triggers (GPU overheating, VRAM full, etc.). Good for gaming sessions where you don't want a terminal on screen.

```powershell
# Start in background (window hides itself automatically)
pwsh -ExecutionPolicy Bypass -File gaming-optimizer.ps1 -Mode tray

# Stop cleanly: drop a STOP file next to the script
New-Item "$env:LOCALAPPDATA\GamingOptimizer\STOP" -Force
# Or: kill the pwsh process via Task Manager
```

The `Install.ps1` creates a **Gaming Optimizer (Tray)** desktop shortcut that does this in one click.

### Emergency cleanup

If the script crashes without restoring system state:

```powershell
pwsh -ExecutionPolicy Bypass -File gaming-optimizer.ps1 -Mode cleanup
```

This resets Win32PrioritySeparation, re-enables SysMain, and releases all process affinity/priority overrides.

---

## Controls

| Key | Action |
|---|---|
| **Q** | Graceful exit — restores all process state |
| **Ctrl+C** | Toggle CPU pinning on / off |

---

## Configuration

The easiest way to configure is the interactive wizard:

```powershell
pwsh -ExecutionPolicy Bypass -File gaming-optimizer.ps1 -Mode configure
```

This walks you through every setting and writes `config.psd1` next to the script. All keys are optional — omit any to keep the default.

Alternatively, copy `config.psd1.example` to `config.psd1` and edit it manually.

### Network adapter name

```powershell
# In config.psd1:
NicName = "Ethernet 2"   # Get-NetAdapter | Select-Object Name, Status
```

Run `Get-NetAdapter | Select-Object Name` to find yours. If the name isn't found at startup the script auto-selects the fastest UP adapter and logs what it picked.

### CPU affinity masks

```powershell
# ~line 70 — bitmasks for a 16-thread Ryzen 5800XT
$GAME_MASK    = [IntPtr]0x0FFF   # threads 0-11  (6 physical cores × 2 SMT)
$FIREFOX_MASK = [IntPtr]0x3000   # threads 12-13 (core 6)
$BG_MASK      = [IntPtr]0xC000   # threads 14-15 (core 7)
```

For a different CPU, calculate masks as bitmasks over your logical processor count. All three must be disjoint and together cover all threads.

### Game detection paths

```powershell
# ~line 90 — add or remove paths for your game library
$GAME_PATHS = @(
    "C:\Program Files (x86)\Steam\steamapps\common",
    "C:\Program Files\Epic Games",
    # ...
)
```

Any process whose executable path starts with one of these strings is treated as a game.

### Alert thresholds

```powershell
$ALERT_GPU_TEMP      = 80    # °C
$ALERT_VRAM_PCT      = 90    # %
$ALERT_GPU_UTIL_TICK = 95    # % — must hold for $ALERT_SUSTAINED_TICKS
$ALERT_CPU_ZONE_TICK = 90    # %
$ALERT_SUSTAINED_TICKS = 4   # × 3 s ≈ 12 s
```

---

## Session reports

Reports are written to `~/Documents/GamingOptimizer/` in plain text, named `<GameName>_<timestamp>.txt`. Files older than 30 days are pruned automatically at the start of each session.

---

## Test suite

### Unit tests (CI-safe, no admin required)

Tests all pure logic: game detection, affinity mask math, bottleneck classification, alert conditions, session tracking, report generation.

```powershell
pwsh -ExecutionPolicy Bypass -File gaming-optimizer.tests.ps1
```

73 test cases. Exits non-zero on any failure.

### Integration tests (requires local Windows + admin)

Validates live system behavior: process state changes, registry edits, service management, console state, crash recovery paths, report hygiene.

```powershell
pwsh -ExecutionPolicy Bypass -File gaming-optimizer-tests.ps1
```

> **Note:** Several sections (BAT file, system state, GPU) will warn/skip if the corresponding resources aren't present. This is expected in non-gaming environments.

### Run everything

```powershell
pwsh -ExecutionPolicy Bypass -File full_test_pass.ps1
```

---

## CI

GitHub Actions runs the **unit tests** on every push and pull request using a `windows-latest` runner. Integration tests are not run in CI because they require admin privileges, a real GPU, and live system services.

See [`.github/workflows/test.yml`](.github/workflows/test.yml).

Repo: https://github.com/maxrenke/game-optimizer

---

## Repository layout

```
game-optimizer/
├── gaming-optimizer.ps1           Main TUI application (~1000 lines)
├── gaming-optimizer.tests.ps1     Unit test suite (73 tests)
├── gaming-optimizer-tests.ps1     Integration test suite (15 sections)
├── full_test_pass.ps1             Runs both suites sequentially
└── .github/
    └── workflows/
        └── test.yml               GitHub Actions CI (unit tests)
```

---

## How it works

1. **Startup**: Sets `Win32PrioritySeparation = 26` (short fixed quanta, no foreground boost), stops SysMain service (redundant on NVMe).
2. **Main loop** (every ~3 s):
   - Enumerates all processes; classifies each as game, Firefox, or background by path.
   - Applies affinity masks and priority classes.
   - Queries nvidia-smi for GPU metrics.
   - Queries WMI `Win32_PerfFormattedData_PerfOS_Processor` for per-core CPU util.
   - Redraws the 118-char-wide TUI dashboard.
3. **Exit**: `finally` block restores all affinity/priority overrides, re-enables SysMain, resets registry key, cleans up ThreadJobs.

The affinity is re-applied every loop to counter tools like Process Lasso that may reset it.

---

## Known limitations

- NVIDIA GPU only (nvidia-smi). AMD GPU support is not implemented.
- CPU affinity masks are sized for 16 logical processors. Running on a CPU with a different thread count requires manual mask recalculation.
- The network sparkline is hardcoded to one adapter name. Multi-NIC setups show only that adapter.
- No config file — all tuning requires editing the script directly. This is tracked as a planned improvement.

---

## License

MIT
