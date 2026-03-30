# Improvement Plan

Roughly ordered from highest-value / lowest-effort to more ambitious.

---

## Phase 1 — Portability & configuration (low effort, high impact)

### 1.1 External config file (`config.json` or `config.psd1`)

Right now everything is hardcoded in the script. A `config.psd1` (PowerShell data file) would let users tune the tool without touching the script, and would survive script updates without merge conflicts.

Suggested keys:
```powershell
@{
    AdapterName         = "Ethernet 2"
    GameAffinityMask    = 0x0FFF
    FirefoxAffinityMask = 0x3000
    BgAffinityMask      = 0xC000
    AlertGpuTempC       = 80
    AlertVramPct        = 90
    AlertGpuUtilPct     = 95
    AlertCpuZonePct     = 90
    AlertSustainedTicks = 4
    GamePaths           = @(
        "C:\Program Files (x86)\Steam\steamapps\common"
    )
    ExtraThrottledProcs = @()
}
```

The script loads defaults then merges an optional `config.psd1` from `$PSScriptRoot`.

### 1.2 CPU topology auto-detection

Currently the affinity masks are hardcoded for 16 threads (8c/16t). Add a startup check that reads `[System.Environment]::ProcessorCount`, warns if it doesn't match the masks, and optionally auto-generates sensible defaults (e.g., bottom 75% of cores = game zone, top two cores = browser + background).

### 1.3 Network adapter auto-detection fallback

If `$ADAPTER_NAME` is not found by `Get-NetAdapterStatistics`, fall back to the adapter with the highest current traffic rather than silently showing zeros.

### 1.4 Fix unit test exit code propagation in `full_test_pass.ps1`

Currently the runner exits with only the integration test's exit code. It should exit non-zero if *either* suite fails:

```powershell
pwsh -ExecutionPolicy Bypass -File "$root\gaming-optimizer.tests.ps1"
$unitExit = $LASTEXITCODE
pwsh -ExecutionPolicy Bypass -File "$root\gaming-optimizer-tests.ps1"
exit ([Math]::Max($unitExit, $LASTEXITCODE))
```

---

## Phase 2 — Test infrastructure

### 2.1 Migrate to Pester 5

The current hand-rolled test framework works but is non-standard. [Pester 5](https://pester.dev/) is the de-facto PowerShell testing framework; migrating would give:
- `Describe`/`It`/`BeforeEach`/`AfterEach` structure
- Built-in `Should` assertions with better error messages
- `Mock` for cmdlets and functions
- NUnit/JUnit XML output for GitHub Actions test summary annotations
- `Invoke-Pester -CodeCoverage` for coverage reports

Migration is mostly mechanical: each `Test-Case` block becomes an `It` block, `Assert-Equal $a $b` becomes `$a | Should -Be $b`.

### 2.2 GitHub Actions test summary

Once on Pester, add the NUnit XML output step to the workflow:

```yaml
- name: Run unit tests
  shell: pwsh
  run: |
    $result = Invoke-Pester -PassThru -OutputFormat NUnitXml -OutputFile TestResults.xml
    exit $result.FailedCount

- name: Publish test results
  uses: dorny/test-reporter@v1
  if: always()
  with:
    name: Unit Tests
    path: TestResults.xml
    reporter: java-junit
```

This renders a per-test pass/fail table directly in the GitHub Actions UI.

### 2.3 Add integration test matrix for CI-safe sections

Some integration test sections (parse/structure, affinity constants, report hygiene) don't need admin or GPU. Extract those into a separate `gaming-optimizer-ci-tests.ps1` that can run in GitHub Actions. Keep the full integration suite for local runs only.

---

## Phase 3 — AMD GPU support

`nvidia-smi` is NVIDIA-only. To support AMD:

1. Add a startup probe: try `nvidia-smi`, fall back to `rocm-smi` (AMD ROCm) or WMI `Win32_VideoController`.
2. Abstract GPU queries behind a `Get-GpuMetrics` function that returns a consistent hashtable regardless of vendor.
3. Note: WMI `Win32_VideoController` gives utilization and temperature on most AMD cards without extra tools, but not power draw or clock speeds.

---

## Phase 4 — UX improvements

### 4.1 Interactive config editor

Add a `--configure` mode that walks through each setting interactively and writes `config.psd1`:

```
> pwsh -File gaming-optimizer.ps1 -Mode configure
Network adapter [Ethernet 2]: _
Game paths (comma-separated): _
```

### 4.2 Per-game affinity profiles

Different games have different thread patterns. Allow `config.psd1` to override affinity per game executable:

```powershell
GameProfiles = @{
    "Cyberpunk2077.exe" = @{ AffinityMask = 0x0FFF; Priority = "High" }
    "hearthstone.exe"   = @{ AffinityMask = 0x00FF; Priority = "AboveNormal" }
}
```

### 4.3 Tray icon / minimal mode

A `--tray` mode that runs in the background with no console window, surfacing only alerts as Windows toast notifications via `BurntToast` or the built-in `Windows.UI.Notifications` WinRT API.

---

## Phase 5 — Distribution

### 5.1 Code signing

Sign `gaming-optimizer.ps1` with a self-signed certificate (or a free Certum / Sectigo Open Source cert) so it can be run with `AllSigned` policy without `-ExecutionPolicy Bypass`.

Instructions to add to README:
```powershell
# Create and trust a self-signed cert (once, as admin)
$cert = New-SelfSignedCertificate -Subject "CN=GameOptimizer" -CertStoreLocation Cert:\LocalMachine\My -Type CodeSigning
Set-AuthenticodeSignature -FilePath gaming-optimizer.ps1 -Certificate $cert
```

### 5.2 Installer / setup script

An `Install.ps1` that:
- Copies script to a stable location
- Creates a signed `.lnk` shortcut on the desktop (Run as Administrator)
- Registers a scheduled task to run `-Mode cleanup` on user logon (catches crash recovery)
- Optionally installs as a Windows startup task

### 5.3 PowerShell Gallery publish

Wrap as a module (`GamingOptimizer.psm1`) with exported commands:
- `Start-GamingOptimizer`
- `Stop-GamingOptimizer`
- `Invoke-GamingOptimizerCleanup`

Publish to PSGallery so users can `Install-Module GamingOptimizer`.

---

## Backlog / low priority

| Item | Notes |
|---|---|
| Per-session graph export | Write a PNG sparkline using System.Drawing or a simple SVG |
| Localization | Console output uses Unicode box-drawing; verify it renders on non-UTF-8 terminals |
| `--dry-run` flag | Show what affinity/priority changes *would* be applied without doing them |
| Linux / WSL2 stub | Out of scope but several functions (bottleneck detection, session tracking) are pure logic with no OS dependency |
| Rate-limiting WMI queries | `Win32_PerfFormattedData_PerfOS_Processor` is expensive; consider caching with a 1-second TTL instead of querying every loop |
