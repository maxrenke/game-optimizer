# Changelog

All notable changes to Gaming Optimizer are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- GPU clock lock (NVIDIA): pins graphics clocks to max via `nvidia-smi` while a
  game runs, resets on exit; configurable in Settings
- Stop configurable services (default `WSearch`, `DiagTrack`) for the session,
  restarted on teardown - same reversible model as the existing SysMain handling
- Global timer resolution registry key so the 1ms timer reaches games that
  don't request it themselves (post-Windows 10 2004 behavior change)
- Auto-pin mode: CPU pinning turns on automatically when a game is detected
  and off when it exits; manual toggles always override the automation
- "Detect Libraries" button in Settings re-runs the Steam/Epic/GOG/Ubisoft/EA
  scan (previously only ran on first launch)
- NIC and ping-host changes apply live, no service restart required
- Dashboard and Settings visual refresh: shared card/typography styles

### Changed
- Hybrid CPU zone split reserves one SMT pair per 16 threads instead of 8 -
  games keep more physical cores on mainstream 8C/16T parts
- Settings save no longer discards all changes when one affinity hex is
  invalid - each mask parses independently with a fallback and a warning
- Config is validated and sanitized on every save
- Reset-Optimizer.ps1 also clears the global timer key, restarts WSearch/
  DiagTrack, and resets GPU clocks

### Fixed
- Default window height increased so the event log isn't cut off
- Running without admin now logs a visible warning instead of failing silently

### Performance
- `ProcessManager.Scan` takes a single process snapshot per tick instead of
  several `GetProcessesByName` sweeps and per-PID re-opens
- Non-game processes are classified once and cached, instead of re-resolving
  their executable path every second
- UI: sparklines redraw only the canvas whose data changed; brushes are
  shared instances instead of being reallocated every tick

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
