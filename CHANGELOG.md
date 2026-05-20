# Changelog

All notable changes to Gaming Optimizer are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0] - 2026-05-20

### Added
- Real-time dashboard: per-zone CPU %, GPU metrics (util/VRAM/temp/power/clocks),
  network sparkline (RX/TX auto-scaling KB/s - MB/s), latency/jitter sparkline,
  1-minute CPU and GPU history sparklines
- Bottleneck detector: CPU-bound / GPU-bound / Balanced / Headroom classification
- Active zone process list showing which processes are in each affinity zone
- WMI `Win32_ProcessStartTrace` / `Win32_ProcessStopTrace` instant detection -
  no polling delay on game launch
- Game library auto-scan on first launch (Steam, Epic Games, GOG, Ubisoft Connect,
  EA App)
- Intel hybrid CPU support: P-core vs E-core detection via registry MHz sampling
- Per-game affinity/priority profile overrides
- Background app suspension during gameplay (OneDrive, Dropbox, Google Drive by
  default) with per-entry enable checkbox
- Configurable CPU affinity masks (hex), alert thresholds, game paths, extra
  throttled processes
- System tray with real-time tooltip, balloon notifications, and context menu
- Per-game session reports: avg/peak CPU%, GPU%, VRAM%, temp; bottleneck breakdown;
  cumulative `sessions.csv`
- Standby RAM flush on demand and on game launch
- Disable Game DVR / Xbox background capture while pinning is active
- Start minimized / Start with Windows via scheduled task at HIGHEST privilege
- Reset All Process State: emergency recovery restoring every affinity/priority to
  Windows defaults without stopping the service
- 106 xUnit tests covering pinning-safety invariants, ClearState, ResetAll,
  affinity zone math, bottleneck detection, session tracking
- GitHub Actions CI on `windows-latest` (.NET 10)

[Unreleased]: https://github.com/maxrenke/game-optimizer/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/maxrenke/game-optimizer/releases/tag/v1.0.0
