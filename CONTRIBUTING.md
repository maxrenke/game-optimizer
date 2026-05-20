# Contributing to Gaming Optimizer

Thanks for your interest. This project welcomes bug reports, feature requests,
and pull requests.

## Quick start

### Requirements

- Windows 10 1809+ or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview channel)
- Windows App SDK workload: `winget install Microsoft.WindowsAppRuntimeInstaller`
- Developer Mode enabled: `Settings > System > For Developers > Developer Mode`
- Visual Studio 2022 17.12+ with the **Windows application development** workload,
  or VS Code with the C# Dev Kit extension

### Clone and run

```powershell
git clone https://github.com/maxrenke/game-optimizer
cd game-optimizer
dotnet run
```

The app self-elevates via UAC on launch. Run from an elevated terminal to skip
the UAC prompt during development.

### Run the tests

The test project targets plain `net10.0-windows` (no WinUI, no WinAppSDK) so
it runs without elevated privileges or a display:

```powershell
dotnet test Tests/GameOptimizer.Tests.csproj -c Release
```

## How to contribute

### Bug reports

Use the [Bug Report](.github/ISSUE_TEMPLATE/bug_report.yml) issue template.
Include your Windows version, CPU model, and steps to reproduce. Attach the
event log from the app's footer if relevant.

### Feature requests

Use the [Feature Request](.github/ISSUE_TEMPLATE/feature_request.yml) template.
Explain the problem you want solved, not just the solution you have in mind.

### Pull requests

1. Fork the repo and create a branch: `git checkout -b feature/my-change`
2. Make your changes
3. Run `dotnet test Tests/GameOptimizer.Tests.csproj` - all tests must pass
4. Update `CHANGELOG.md` under `[Unreleased]`
5. Open a PR against `main`

## Code conventions

- **C# 13 / .NET 10** - use modern language features where they add clarity
- **No public API changes without tests** - especially for `ProcessManager` and
  `OptimizerService`; the pinning-safety invariant (`PinningEnabled = false`
  must never modify any process) is load-bearing and must stay green
- **No telemetry, analytics, or outbound connections** beyond the configurable
  latency ping host
- **Admin-aware** - document when a code path requires elevation; prefer silent
  degraded behavior over crashes when running without admin

## Architecture notes

See the Architecture section in [README.md](README.md) for a file-by-file map.
The key safety invariant: every code path in `ProcessManager` that modifies a
process (`ProcessorAffinity`, `PriorityClass`, I/O priority) is wrapped in
`if (PinningEnabled)`. Do not bypass this.

## License

By submitting a pull request you agree that your contribution will be licensed
under the [MIT License](LICENSE).
