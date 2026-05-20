#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Live watcher for CPU affinity and priority changes across all processes.

.DESCRIPTION
    Run this alongside Gaming Optimizer to verify exactly which processes it
    modifies and how. It baselines every process, then prints a colored line
    whenever a process's CPU affinity mask or priority class changes:

      YELLOW  - affinity narrowed to a subset of cores (pinned)
      GREEN   - affinity widened back to all cores (restored)
      CYAN    - priority class changed

    Each affinity change is annotated with the matching Gaming Optimizer zone
    (GAME / MEDIA / BG) read from the app's config.json, so it is obvious what
    the app did. Every change is also written to a timestamped log file, and a
    summary is printed when you stop the watcher with Ctrl+C.

.PARAMETER Name
    Optional process-name filter (substring, case-insensitive). Omit for all.

.PARAMETER IntervalSeconds
    Poll interval in seconds. Default 1.

.PARAMETER LogFile
    Path to the log file. Default: Documents\GamingOptimizer\affinity-watch_<timestamp>.log

.PARAMETER NoLog
    Disable file logging (console only).

.PARAMETER DumpBaseline
    Also write the full initial affinity/priority of every process to the log.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1 -Name firefox -DumpBaseline
#>
param(
    [string]$Name,
    [int]$IntervalSeconds = 1,
    [string]$LogFile,
    [switch]$NoLog,
    [switch]$DumpBaseline
)

$ErrorActionPreference = 'Stop'
$coreCount = [Environment]::ProcessorCount
$allMask   = ([long]1 -shl $coreCount) - 1
$selfPid   = $PID

# ── Log file setup ──────────────────────────────────────────────────────────
$logPath = $null
if (-not $NoLog) {
    if (-not $LogFile) {
        $dir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GamingOptimizer'
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $LogFile = Join-Path $dir ("affinity-watch_{0}.log" -f (Get-Date -Format 'yyyy-MM-dd_HHmm'))
    }
    $logPath = $LogFile
}

function Write-Log([string]$text) {
    if ($logPath) { Add-Content -Path $logPath -Value $text }
}

# ── Affinity zones from the app's config (for annotation) ───────────────────
$zones = @{}
$cfgPath = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GamingOptimizer\config.json'
if (Test-Path $cfgPath) {
    try {
        $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
        $zones[[long]$cfg.GameAffinityMask]    = 'GAME zone'
        $zones[[long]$cfg.FirefoxAffinityMask] = 'MEDIA zone'
        $zones[[long]$cfg.BgAffinityMask]      = 'BG zone'
    }
    catch { }
}

function Get-Zone([long]$mask) {
    if ($mask -eq $allMask)      { return 'all cores' }
    if ($zones.ContainsKey($mask)) { return $zones[$mask] }
    return 'custom'
}

# Render an affinity mask as hex plus a compressed core list, e.g. "0xFFF [0-11]".
function Format-Mask([long]$mask) {
    if ($mask -eq 0) { return '0x0 [none]' }
    if ($mask -eq $allMask) { return ('0x{0:X} [ALL]' -f $mask) }
    $list = for ($i = 0; $i -lt 64; $i++) { if ($mask -band ([long]1 -shl $i)) { $i } }
    $ranges = @(); $start = $list[0]; $prev = $list[0]
    foreach ($c in ($list | Select-Object -Skip 1)) {
        if ($c -eq $prev + 1) { $prev = $c }
        else {
            $ranges += $(if ($start -eq $prev) { "$start" } else { "$start-$prev" })
            $start = $c; $prev = $c
        }
    }
    $ranges += $(if ($start -eq $prev) { "$start" } else { "$start-$prev" })
    return ('0x{0:X} [{1}]' -f $mask, ($ranges -join ','))
}

# Snapshot every readable process's affinity + priority, keyed by PID.
function Get-Snapshot {
    $snap = @{}
    foreach ($p in Get-Process) {
        if ($p.Id -eq $selfPid) { continue }
        if ($Name -and $p.ProcessName -notlike "*$Name*") { continue }
        try {
            $snap[$p.Id] = [pscustomobject]@{
                Name     = $p.ProcessName
                Affinity = $p.ProcessorAffinity.ToInt64()
                Priority = $p.PriorityClass.ToString()
            }
        }
        catch { }   # protected process - cannot be read even when elevated
    }
    return $snap
}

function Show-Name([string]$n) {
    if ($n.Length -gt 24) { return $n.Substring(0, 24) }
    return $n
}

# Print to console (colored) and append to the log (plain, full timestamp).
function Emit([string]$text, [string]$color) {
    $ts = Get-Date -Format 'HH:mm:ss'
    Write-Host ("  [{0}] {1}" -f $ts, $text) -ForegroundColor $color
    Write-Log ("{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $text)
}

# ── Startup banner ──────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  CPU Affinity / Priority Watcher' -ForegroundColor White
Write-Host ("  {0} logical processors    ALL = {1}" -f $coreCount, (Format-Mask $allMask)) -ForegroundColor DarkGray
Write-Host ("  Watching {0}.  Press Ctrl+C to stop." -f `
    $(if ($Name) { "processes matching '$Name'" } else { 'all processes' })) -ForegroundColor DarkGray
if ($logPath) { Write-Host "  Logging to: $logPath" -ForegroundColor DarkGray }
if ($zones.Count) { Write-Host '  Zone labels loaded from config.json' -ForegroundColor DarkGray }
Write-Host ''

Write-Log ('=== Affinity Watcher started {0} ===' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Log ("{0} logical processors, ALL = {1}" -f $coreCount, (Format-Mask $allMask))

$state = Get-Snapshot
Write-Host ("  Baseline: {0} processes captured at {1}" -f $state.Count, (Get-Date -Format HH:mm:ss)) -ForegroundColor DarkGray
Write-Host '  ...waiting for changes...' -ForegroundColor DarkGray
Write-Host ''
Write-Log ("Baseline: {0} processes" -f $state.Count)

if ($DumpBaseline) {
    Write-Log '--- baseline snapshot ---'
    foreach ($id in ($state.Keys | Sort-Object { $state[$_].Name })) {
        $e = $state[$id]
        Write-Log ("  {0,-24} PID {1,-6}  {2,-18}  {3}" -f `
            (Show-Name $e.Name), $id, (Format-Mask $e.Affinity), $e.Priority)
    }
    Write-Log '--- end baseline ---'
}

$stats = @{ Pinned = 0; Restored = 0; Priority = 0 }

try {
    while ($true) {
        Start-Sleep -Seconds $IntervalSeconds
        $now = Get-Snapshot

        foreach ($id in $now.Keys) {
            $cur = $now[$id]
            if (-not $state.ContainsKey($id)) { $state[$id] = $cur; continue }  # new process
            $old = $state[$id]

            if ($cur.Affinity -ne $old.Affinity) {
                $restored = $cur.Affinity -eq $allMask
                if ($restored) { $color = 'Green'; $verb = 'RESTORED'; $stats.Restored++ }
                else           { $color = 'Yellow'; $verb = 'PINNED  '; $stats.Pinned++ }
                Emit ("{0,-24} PID {1,-6}  AFFINITY {2}  {3} -> {4}  ({5})" -f `
                    (Show-Name $cur.Name), $id, $verb, `
                    (Format-Mask $old.Affinity), (Format-Mask $cur.Affinity), `
                    (Get-Zone $cur.Affinity)) $color
            }
            if ($cur.Priority -ne $old.Priority) {
                $stats.Priority++
                Emit ("{0,-24} PID {1,-6}  PRIORITY          {2} -> {3}" -f `
                    (Show-Name $cur.Name), $id, $old.Priority, $cur.Priority) 'Cyan'
            }
            $state[$id] = $cur
        }

        # drop processes that have exited so the table does not grow unbounded
        foreach ($id in @($state.Keys)) {
            if (-not $now.ContainsKey($id)) { $state.Remove($id) | Out-Null }
        }
    }
}
finally {
    $summary = ("Stopped. {0} pinned, {1} restored, {2} priority changes." -f `
        $stats.Pinned, $stats.Restored, $stats.Priority)
    Write-Host ''
    Write-Host "  $summary" -ForegroundColor White
    if ($logPath) { Write-Host "  Log saved: $logPath" -ForegroundColor DarkGray }
    Write-Host ''
    Write-Log ('=== {0}  {1} ===' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $summary)
}
