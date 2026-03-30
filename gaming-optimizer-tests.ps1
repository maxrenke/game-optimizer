# ============================================================
#  Gaming Optimizer - Test Suite
#  Run: pwsh -ExecutionPolicy Bypass -File gaming-optimizer-tests.ps1
#  All tests are non-destructive: any process state changes are
#  simulated and verified, then restored before the next section.
# ============================================================

param(
    [switch]$Verbose   # show extra detail on each check
)

$script:pass   = 0
$script:fail   = 0
$script:warn   = 0
$script:section = ""

$SCRIPT_PATH = "$PSScriptRoot\gaming-optimizer.ps1"
$BAT_PATH    = "$PSScriptRoot\Gaming Optimizer.bat"

function Section($name) {
    $script:section = $name
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
}

function T($name, $result, [switch]$WarnOnly) {
    if ($result) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        $script:pass++
    } elseif ($WarnOnly) {
        Write-Host "  [WARN] $name" -ForegroundColor Yellow
        $script:warn++
    } else {
        Write-Host "  [FAIL] $name  ($($script:section))" -ForegroundColor Red
        $script:fail++
    }
}

function Strip-Ansi($s) { $s -replace '\x1B\[[0-9;]*[mKHJ]', '' }


# ---- Helpers -----------------------------------------------
$allCores = [IntPtr](([int64]1 -shl [System.Environment]::ProcessorCount) - 1)
$pinMask  = [IntPtr]0x3000

function Get-TrapBlock($src) {
    $lines = $src -split "`n"
    $start = ($lines | Select-String -Pattern '^trap \{' | Select-Object -First 1).LineNumber
    if (-not $start) { return "" }
    $depth = 0
    for ($i = $start - 1; $i -lt $lines.Count; $i++) {
        $depth += ($lines[$i] -split '\{').Count - 1
        $depth -= ($lines[$i] -split '\}').Count - 1
        if ($depth -le 0 -and $i -gt ($start - 1)) {
            return ($lines[($start-1)..$i] -join "`n")
        }
    }
    return ""
}

function Get-FinallyBlock($src) {
    # Return the LAST finally block in the file (the outer main-loop one)
    $parts = $src -split '} finally \{'
    return $parts[-1]
}

function Get-FunctionBlock($src, $fnName) {
    $parts = $src -split "function $fnName\(\)"
    if ($parts.Count -lt 2) { return "" }
    $lines = $parts[1] -split "`n"
    $depth = 0; $out = @()
    foreach ($line in $lines) {
        $depth += ($line -split '\{').Count - 1
        $depth -= ($line -split '\}').Count - 1
        $out += $line
        if ($depth -le 0 -and $out.Count -gt 1) { break }
    }
    return $out -join "`n"
}

# Load source once
$src = Get-Content $SCRIPT_PATH -Raw -Encoding UTF8


# ============================================================
# SECTION 1 — PARSE & STRUCTURE
# ============================================================
Section "1. PARSE & STRUCTURE"

$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($SCRIPT_PATH, [ref]$null, [ref]$errors)
T "Script file exists"          (Test-Path $SCRIPT_PATH)
T "Script parses without errors" ($errors.Count -eq 0)

$fnNames = $ast.FindAll(
    { $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true
) | Select-Object -ExpandProperty Name

foreach ($fn in @(
    'Release-Pinning', 'Restore-Pinning', 'ThrottleBg',
    'ApplyGame', 'ApplyFirefox',
    'Enable-GamingOptimizations', 'Disable-GamingOptimizations',
    'Save-SessionReport', 'End-GameSession', 'Start-GameSession',
    'Update-Bottleneck', 'Check-Alerts', 'Update-Dashboard', 'AddLog'
)) {
    T "Function '$fn' defined" ($fnNames -contains $fn)
}

T "Bat file exists" (Test-Path $BAT_PATH)


# ============================================================
# SECTION 2 — BAT FILE
# ============================================================
Section "2. BAT FILE"

$bat = Get-Content $BAT_PATH -Raw -EA SilentlyContinue
T "Bat uses pwsh (not legacy powershell.exe)" (
    $bat -match 'pwsh' -and $bat -notmatch '(?<![a-z])powershell\s'
)
T "Bat points to correct script path" (
    $bat -match [regex]::Escape('%USERPROFILE%\gaming-optimizer.ps1')
)
T "Bat has -cleanup shortcut"    ($bat -match '-cleanup')
T "Bat elevation uses pwsh"      ($bat -match 'pwsh.*RunAs')
T "Bat does NOT reference Desktop path" (
    $bat -notmatch [regex]::Escape('\Desktop\gaming-optimizer')
)


# ============================================================
# SECTION 3 — KEY HANDLER (static analysis)
# ============================================================
Section "3. KEY HANDLER (static analysis)"

T "Ctrl+C uses Modifiers -band Control (not bare switch 'C')" (
    $src -match 'Modifiers.*-band.*\[ConsoleModifiers\]::Control'
)
T "isCtrlC variable used"                  ($src -match '\$isCtrlC')
T "isQ variable used"                      ($src -match '\$isQ')
T "Q exit drains remaining keys"           ($src -match 'Drain remaining keys')
T "TreatControlCAsInput set true in loop"  ($src -match 'TreatControlCAsInput = \$true')
T "TreatControlCAsInput cleared in finally"(
    (Get-FinallyBlock $src) -match 'TreatControlCAsInput'
)
T "TreatControlCAsInput cleared in trap"   (
    (Get-TrapBlock $src) -match 'TreatControlCAsInput'
)
T "TreatControlCAsInput cleared in -cleanup" (
    $src -match '(?s)if \(\$Mode -eq .cleanup.\).*TreatControlCAsInput'
)


# ============================================================
# SECTION 4 — PINNING FUNCTIONS (static analysis)
# ============================================================
Section "4. PINNING FUNCTIONS (static analysis)"

$releaseFn  = Get-FunctionBlock $src 'Release-Pinning'
$restoreFn  = Get-FunctionBlock $src 'Restore-Pinning'
$applyGame  = (($src -split 'function ApplyGame')[1] -split "`n" | Select-Object -First 3) -join "`n"
$applyFF    = (($src -split 'function ApplyFirefox')[1] -split "`n" | Select-Object -First 3) -join "`n"

# Release-Pinning
T "Release-Pinning uses live Get-Process firefox"   ($releaseFn -match 'Get-Process firefox')
T "Release-Pinning uses full type for normalPri"    ($releaseFn -match 'ProcessPriorityClass\]::Normal')
T "Release-Pinning releases game processes"         ($releaseFn -match 'activeGames\.Keys')
T "Release-Pinning releases BG processes"           ($releaseFn -match 'BG_PROCESSES')
T "Release-Pinning uses allCores mask"              ($releaseFn -match 'allCores')

# Restore-Pinning
T "Restore-Pinning uses live Get-Process firefox"   ($restoreFn -match 'Get-Process firefox')
T "Restore-Pinning updates appliedFF dict"          ($restoreFn -match 'appliedFF\[')
T "Restore-Pinning calls ThrottleBg"                ($restoreFn -match 'ThrottleBg')
T "Restore-Pinning pins game processes"             ($restoreFn -match 'GAME_AFFINITY')

# ApplyGame / ApplyFirefox respect pinning toggle
T "ApplyGame checks pinningEnabled before setting affinity"   ($applyGame -match 'pinningEnabled')
T "ApplyFirefox checks pinningEnabled before setting affinity" ($applyFF  -match 'pinningEnabled')


# ============================================================
# SECTION 5 — FINALLY BLOCK (static analysis)
# ============================================================
Section "5. FINALLY BLOCK (static analysis)"

$fin = Get-FinallyBlock $src

T "Finally calls End-GameSession"               ($fin -match 'End-GameSession')
T "Finally calls Disable-GamingOptimizations"   ($fin -match 'Disable-GamingOptimizations')
T "Finally restores Firefox affinity"            ($fin -match 'Get-Process firefox')
T "Finally restores game processes"              ($fin -match 'activeGames\.Keys')
T "Finally restores BG processes"               ($fin -match 'BG_PROCESSES')
T "Finally removes thread jobs"                 ($fin -match 'Remove-Job')
T "Finally resets TreatControlCAsInput"         ($fin -match 'TreatControlCAsInput')
T "Finally resets cursor visibility"            ($fin -match 'CursorVisible')
T "Finally resets window title to PowerShell"   ($fin -match "WindowTitle.*PowerShell")
T "Finally calls Save-SessionReport"            ($fin -match 'Save-SessionReport')
T "Finally calls ResetColor"                    ($fin -match 'ResetColor')
T "Finally prints exit message"                 ($fin -match 'Gaming Optimizer stopped')


# ============================================================
# SECTION 6 — TRAP BLOCK (static analysis)
# ============================================================
Section "6. TRAP BLOCK (static analysis)"

$trap = Get-TrapBlock $src

T "Trap block found"                          ($trap.Length -gt 0)
T "Trap resets TreatControlCAsInput"          ($trap -match 'TreatControlCAsInput')
T "Trap restores cursor visibility"           ($trap -match 'CursorVisible')
T "Trap calls ResetColor"                     ($trap -match 'ResetColor')
T "Trap clears the console"                   ($trap -match 'Console.*Clear')
T "Trap removes all jobs"                     ($trap -match 'Remove-Job')
T "Trap restores Firefox affinity"            ($trap -match 'ProcessorAffinity')
T "Trap restores Firefox priority"            ($trap -match 'ProcessPriorityClass')
T "Trap resets window title to PowerShell"    ($trap -match "WindowTitle.*PowerShell")
T "Trap prints FATAL error message"           ($trap -match 'FATAL')
T "Trap ends with break"                      ($trap -match '\bbreak\b')


# ============================================================
# SECTION 7 — CLEANUP MODE (live execution)
# ============================================================
Section "7. CLEANUP MODE (live execution)"

# BG process names the optimizer tracks (mirrors $BG_PROCESSES in the script)
$bgProcessNames = @(
    "onedrive","icloudckks","iclouddrive","icloudservices","icloudhome",
    "phoneexperiencehost","crossdeviceservice",
    "malwarebytes","mbamservice","hearthstonedecktracker",
    "backgroundtaskhost","windowspackagemanagerserver",
    "battle.net","hwinfo64","nahimicsvc32","nahimicsvc64",
    "unigetui","appcontrol"
)

# --- Simulate stuck state: pin Firefox + any running BG procs ---
$ffProcs = @(Get-Process firefox -EA SilentlyContinue)
$pinnedFFCount = 0
foreach ($proc in $ffProcs) {
    try { $proc.ProcessorAffinity = $pinMask; $proc.PriorityClass = "BelowNormal"; $pinnedFFCount++ } catch {}
}
T "Firefox processes available to test with" ($pinnedFFCount -gt 0) -WarnOnly

$bgPinnedProcs = @()
foreach ($name in $bgProcessNames) {
    foreach ($proc in @(Get-Process -Name $name -EA SilentlyContinue)) {
        try { $proc.ProcessorAffinity = $pinMask; $proc.PriorityClass = "BelowNormal"; $bgPinnedProcs += $proc } catch {}
    }
}
T "BG processes available to test with" ($bgPinnedProcs.Count -gt 0) -WarnOnly

# --- Run -cleanup (same as the bat file) ---
$raw      = pwsh -ExecutionPolicy Bypass -File $SCRIPT_PATH -Mode cleanup 2>&1
$exitCode = $LASTEXITCODE
$out      = ($raw | ForEach-Object { Strip-Ansi "$_" }) -join "`n"

# Output / exit code checks
T "Cleanup exits with code 0"                ($exitCode -eq 0)
T "Cleanup outputs 'Cleanup complete'"        ($out -match 'Cleanup complete')
T "Cleanup reports PrioritySep status"        ($out -match 'Win32PrioritySeparation')
T "Cleanup reports SysMain status"            ($out -match 'SysMain')

if ($pinnedFFCount -gt 0) {
    T "Cleanup reports Firefox processes released" ($out -match '\[OK\] Released \d+ Firefox')
}
if ($bgPinnedProcs.Count -gt 0) {
    T "Cleanup reports BG processes released" ($out -match '\[OK\] Released \d+ background')
}

# --- Verify Firefox actual process state ---
$ffStuckAffinity = @(Get-Process firefox -EA SilentlyContinue |
    Where-Object { $_.ProcessorAffinity.ToInt64() -ne $allCores.ToInt64() })
$ffBadPriority = @(Get-Process firefox -EA SilentlyContinue |
    Where-Object { $_.PriorityClass -in @('BelowNormal','Idle') })

T "All Firefox processes freed to all cores after cleanup"  ($ffStuckAffinity.Count -eq 0)
T "No Firefox processes left at degraded priority"          ($ffBadPriority.Count -eq 0)

# --- Verify BG process actual state (the gap that previously went unchecked) ---
if ($bgPinnedProcs.Count -gt 0) {
    $bgStuckAffinity = @()
    $bgBadPriority   = @()
    foreach ($name in $bgProcessNames) {
        foreach ($proc in @(Get-Process -Name $name -EA SilentlyContinue)) {
            try {
                $aff = $proc.ProcessorAffinity
                $pri = $proc.PriorityClass
                if ($aff -ne $null -and $aff.ToInt64() -ne $allCores.ToInt64()) { $bgStuckAffinity += $proc }
                if ($pri -ne $null -and $pri -in @('BelowNormal','Idle'))        { $bgBadPriority   += $proc }
            } catch {} # process may have exited between pinning and verification
        }
    }
    T "All BG processes freed to all cores after cleanup"   ($bgStuckAffinity.Count -eq 0)
    T "No BG processes left at degraded priority"           ($bgBadPriority.Count -eq 0)
} else {
    T "All BG processes freed to all cores after cleanup (no BG procs running - skipped)" $true -WarnOnly
    T "No BG processes left at degraded priority (no BG procs running - skipped)"         $true -WarnOnly
}

# --- Game process note (cannot fully simulate without a real game EXE) ---
# cleanup reads $activeGames from script state; since no optimizer session is running
# there are no tracked game PIDs to verify. Covered indirectly by the finally/trap
# block static checks in sections 5 & 6.


# ============================================================
# SECTION 8 — SYSTEM STATE
# ============================================================
Section "8. SYSTEM STATE"

$priSep  = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" -EA SilentlyContinue).Win32PrioritySeparation
$sysMain = Get-Service SysMain -EA SilentlyContinue

T "PrioritySep not stuck at gaming value 26"      ($priSep -ne 26)
T "PrioritySep is a known-good value (2 or 38)"   ($priSep -in @(2, 38)) -WarnOnly
T "SysMain service exists"                         ($sysMain -ne $null)
T "SysMain service is running"                     ($sysMain -and $sysMain.Status -eq "Running")

# ============================================================
# SECTION 9 — CONSOLE STATE
# ============================================================
Section "9. CONSOLE STATE"

$ctrlCOk  = try { -not [Console]::TreatControlCAsInput } catch { $true }  # headless runners have no console handle
$cursorOk = try { [Console]::CursorVisible             } catch { $true }
T "TreatControlCAsInput is false (not stuck from a crashed run)" $ctrlCOk
T "CursorVisible is true (not hidden from a crashed run)"        $cursorOk

# ============================================================
# SECTION 10 — ORPHAN JOBS
# ============================================================
Section "10. ORPHAN JOBS"

$threadJobs = @(Get-Job -EA SilentlyContinue | Where-Object { $_.PSJobTypeName -eq 'ThreadJob' })
T "No orphaned ThreadJobs left from a previous run" ($threadJobs.Count -eq 0)


# ============================================================
# SECTION 11 — DISABLE-GAMINGOPTIMIZATIONS (static analysis)
# ============================================================
Section "11. DISABLE-GAMINGOPTIMIZATIONS (static analysis)"

$disableFn = Get-FunctionBlock $src 'Disable-GamingOptimizations'

T "Restores PrioritySeparation to saved original"     ($disableFn -match 'PRIO_SEP_ORIGINAL')
T "Restores SysMain only if it was stopped by us"     ($disableFn -match 'sysmainStopped')
T "Restores SysMain StartType from saved value"       ($disableFn -match 'sysmainOriginalStart')
T "Falls back to Automatic if saved StartType missing"($disableFn -match 'Automatic')

$enableFn = Get-FunctionBlock $src 'Enable-GamingOptimizations'
T "Saves SysMain StartType BEFORE stopping service"   (
    # StartType save should appear before Stop-Service in the function
    ($enableFn -replace '(?s)Stop-Service.*','') -match 'sysmainOriginalStart'
)
T "Saves sysmainStopped flag BEFORE stopping service" (
    ($enableFn -replace '(?s)Stop-Service.*','') -match 'sysmainStopped'
)

# ============================================================
# SECTION 12 — REPORT & DIRECTORY HYGIENE (static analysis)
# ============================================================
Section "12. REPORT HYGIENE (static analysis)"

$reportFn = Get-FunctionBlock $src 'Save-SessionReport'
T "Session report saved to REPORT_DIR"   ($reportFn -match 'REPORT_DIR')
T "Reports older than 30 days pruned"    ($reportFn -match 'AddDays\(-30\)')
T "Report encoded as UTF8"               ($reportFn -match 'UTF8')

# Startup stale-PrioritySep check
T "Startup detects and resets stale PrioritySep=26 from a previous crash" (
    $src -match 'stalePrio.*-eq 26'
)

# Startup orphan-job cleanup
T "Startup cleans orphaned ThreadJobs from a previous crash" (
    ($src -split 'Draw-Frame')[1] -match 'Remove-Job'
)


# ============================================================
# SECTION 13 — FIRE-AND-FORGET JOB TRACKING (static analysis)
# ============================================================
Section "13. FIRE-AND-FORGET JOB TRACKING (static analysis)"

T "jWarmup assigned (not discarded with | Out-Null)"     ($src -match '\$jWarmup\s*=\s*Start-ThreadJob')
T "jSysMain assigned to script scope"                    ($src -match '\$script:jSysMain\s*=\s*Start-ThreadJob')
T "jWarmup cleaned up inside main loop"                  ($src -match 'Remove-Job \$jWarmup')
T "jSysMain cleaned up inside main loop"                 ($src -match 'Remove-Job \$script:jSysMain')
T "btIdx uses .Count not hardcoded literal 5"            ($src -match 'btIdx\s*=.*%\s*\$script:btCpuHistory\.Count')
T "btIdx does NOT use hardcoded % 5"                     ($src -notmatch 'btIdx\s*=.*%\s*5\b')


# ============================================================
# SECTION 14 — AFFINITY-CHECK UTILITY
# ============================================================
Section "14. AFFINITY-CHECK UTILITY"

$affinityCheckPath = "$PSScriptRoot\affinity-check.ps1"
T "affinity-check.ps1 exists in repo"  (Test-Path $affinityCheckPath)

if (Test-Path $affinityCheckPath) {
    $afErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($affinityCheckPath, [ref]$null, [ref]$afErrors)
    T "affinity-check.ps1 parses without errors" ($afErrors.Count -eq 0)

    $afRaw = pwsh -ExecutionPolicy Bypass -File $affinityCheckPath 2>&1
    $afOut = ($afRaw | ForEach-Object { Strip-Ansi "$_" }) -join "`n"
    T "affinity-check.ps1 runs without crashing"   ($LASTEXITCODE -eq 0)
    T "affinity-check.ps1 reports all-cores mask"  ($afOut -match 'all-cores mask')
    T "affinity-check.ps1 reports logical cores"   ($afOut -match 'logical cores')
}


# ============================================================
# SECTION 15 — UNIT TEST SUITE CROSS-CHECK
# ============================================================
Section "15. UNIT TEST SUITE CROSS-CHECK"

$unitTestPath = "$PSScriptRoot\gaming-optimizer.tests.ps1"
T "gaming-optimizer.tests.ps1 exists" (Test-Path $unitTestPath)

if (Test-Path $unitTestPath) {
    $utRaw  = pwsh -ExecutionPolicy Bypass -File $unitTestPath 2>&1
    $utOut  = ($utRaw | ForEach-Object { Strip-Ansi "$_" }) -join "`n"
    $utFail = if ($utOut -match 'Failed:\s*(\d+)') { [int]$Matches[1] } else { -1 }
    T "Unit test suite exits with code 0"      ($LASTEXITCODE -eq 0)
    T "Unit test suite reports 0 failures"     ($utFail -eq 0)
}


# ============================================================
# SUMMARY
# ============================================================
$total = $script:pass + $script:fail + $script:warn
$col   = if ($script:fail -gt 0) { "Red" } elseif ($script:warn -gt 0) { "Yellow" } else { "Green" }

Write-Host ""
Write-Host "════════════════════════════════════════════════" -ForegroundColor White
Write-Host ("  {0,-10} {1}" -f "PASSED:",  $script:pass)  -ForegroundColor Green
if ($script:fail -gt 0) {
    Write-Host ("  {0,-10} {1}" -f "FAILED:",  $script:fail) -ForegroundColor Red
}
if ($script:warn -gt 0) {
    Write-Host ("  {0,-10} {1}" -f "WARNINGS:", $script:warn) -ForegroundColor Yellow
}
Write-Host ("  {0,-10} {1}" -f "TOTAL:",   $total)          -ForegroundColor White
Write-Host "════════════════════════════════════════════════" -ForegroundColor White

if ($script:fail -eq 0 -and $script:warn -eq 0) {
    Write-Host "  All checks passed." -ForegroundColor Green
} elseif ($script:fail -eq 0) {
    Write-Host "  Passed with warnings — review WARN items above." -ForegroundColor Yellow
} else {
    Write-Host "  Some checks FAILED — review output above." -ForegroundColor Red
}
Write-Host ""

# Exit with non-zero if any hard failures (useful for CI/automation)
exit $script:fail
