# Changelog

All notable changes to Gaming Optimizer are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- Configurable media zone: `MediaProcs` replaces the hardcoded firefox/vlc pair,
  so mpv, Chrome, Edge, or any second-monitor player gets the media zone at
  Normal priority instead of falling through to the game zone
- "Reserve a full physical core for media" option, for watching video on a second
  monitor while gaming: sizes the media and background zones at a whole physical
  core (an SMT sibling pair) each rather than one starved hyperthread
- "Close Apps" button on the dashboard: terminates the apps listed in
  `CloseToFreeRam` (default: both OneDrive processes) and logs the RAM reclaimed.
  Suspending frees CPU and disk I/O but no memory - `NtSuspendProcess` stops
  threads while the working set stays mapped, so only a process exit returns its
  pages. Confirmation dialog first; killed PIDs are dropped from the suspend
  journal so recovery never chases them. Styled as a destructive action, and
  its hover lists exactly what would be killed - each app, its instance count,
  its working set, and the total reclaimable RAM, read live at hover time
- "Recalculate zones for this CPU" action in Settings - zone detection previously
  only ever ran when the config file was first created
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
- Hardware monitors (HWiNFO, CapFrameX, RTSS) are no longer throttled to the
  background zone - pinning a sampler to Idle priority on one core made it miss
  polls and corrupt the frametime data used to judge whether tuning helped
- Storefront and companion UIs (steamwebhelper, Playnite, WowUp, U.GG, Everything,
  Riot Client) throttle to the background zone while a game runs
- Cloud-sync defaults for pause-during-gameplay extended to Syncthing, MEGA,
  pCloud, Nextcloud, and the OneDrive ListSync service
- Phone Link (`phoneexperiencehost`) moved from throttled to paused-during-
  gameplay - it idles around 700 MB and freezing it only defers phone
  notifications until the game closes. `crossdeviceservice` stays throttled:
  it is a separate package backing Nearby Share and camera streaming
- A process listed in both the media and throttle lists now stays in the media
  zone instead of oscillating between Normal and Idle on alternating scans
- Hybrid CPU zone split reserves one SMT pair per 16 threads instead of 8 -
  games keep more physical cores on mainstream 8C/16T parts
- Settings save no longer discards all changes when one affinity hex is
  invalid - each mask parses independently with a fallback and a warning
- Config is validated and sanitized on every save
- Reset-Optimizer.ps1 also clears the global timer key, restarts WSearch/
  DiagTrack, and resets GPU clocks

### Fixed
- Taskbar, Alt-Tab and Explorer showed the generic shell icon: the project had
  no `ApplicationIcon`, so the exe carried no Win32 icon resource. For an
  unpackaged app the shell reads that resource, not `AppWindow.SetIcon`, which
  only dresses the window itself
- `AppWindow.SetIcon` used a path relative to the working directory rather than
  the exe folder, so the window icon silently failed whenever the app was
  launched from anywhere else
- Suspended apps are no longer stranded frozen by a crash. Every cleanup path
  (`ReleasePinning`, `ResumeAllSuspended`, `Dispose`) needed managed code to run
  at shutdown, and a WinUI stowed exception (`0xC000027B`) terminates the process
  without running any of it - `NtSuspendProcess` is not tied to the caller's
  lifetime, so Windows never unfroze them. Suspensions are now journalled to
  `suspended.json` beside the config and recovered on next launch. Entries store
  the process creation time as well as the PID, so a recycled PID is skipped
  rather than resuming an unrelated process
- A suspend is skipped entirely when the process start time cannot be read,
  since such an entry could not be verified during recovery
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
