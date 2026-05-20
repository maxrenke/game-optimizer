#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Full-screen TUI that monitors CPU affinity and priority changes live.

.DESCRIPTION
    Run alongside Gaming Optimizer to verify exactly which processes it
    modifies and how. Redraws a dashboard every interval with:

      SUMMARY            - running totals: pinned / restored / priority events
      CORE USAGE         - how many modified processes are assigned to each core
      CURRENTLY MODIFIED - every process whose affinity or priority differs
                           from when the watcher started, with its zone
      RECENT EVENTS      - the latest changes as they happen

    Affinity changes are annotated with the matching Gaming Optimizer zone
    (GAME / MEDIA / BG) read from config.json. Every event is also written to
    a timestamped log file; a summary is logged when you stop with Ctrl+C.

.PARAMETER Name
    Optional process-name filter (substring, case-insensitive). Omit for all.

.PARAMETER IntervalSeconds
    Redraw interval in seconds. Default 1.

.PARAMETER LogFile
    Log file path. Default: Documents\GamingOptimizer\affinity-watch_<timestamp>.log

.PARAMETER NoLog
    Disable file logging.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1
#>
param(
    [string]$Name,
    [double]$IntervalSeconds = 1,
    [string]$LogFile,
    [switch]$NoLog
)

$ErrorActionPreference = 'Stop'
$coreCount = [Environment]::ProcessorCount
$allMask   = ([long]1 -shl $coreCount) - 1
$selfPid   = $PID
$startTime = Get-Date

# ── Log setup ───────────────────────────────────────────────────────────────
$logPath = $null
if (-not $NoLog) {
    if (-not $LogFile) {
        $dir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GamingOptimizer'
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $LogFile = Join-Path $dir ("affinity-watch_{0}.log" -f (Get-Date -Format 'yyyy-MM-dd_HHmm'))
    }
    $logPath = $LogFile
}
function Write-Log([string]$t) { if ($logPath) { Add-Content -Path $logPath -Value $t } }

# ── Affinity zones from the app's config ────────────────────────────────────
$zones = @{}
$cfgPath = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GamingOptimizer\config.json'
if (Test-Path $cfgPath) {
    try {
        $c = Get-Content $cfgPath -Raw | ConvertFrom-Json
        $zones[[long]$c.GameAffinityMask]    = 'GAME'
        $zones[[long]$c.FirefoxAffinityMask] = 'MEDIA'
        $zones[[long]$c.BgAffinityMask]      = 'BG'
    }
    catch { }
}
function Get-Zone([long]$m) {
    if ($m -eq $allMask) { return 'all' }
    if ($zones.ContainsKey($m)) { return $zones[$m] }
    return 'custom'
}

function Format-Mask([long]$mask) {
    if ($mask -eq 0) { return '0x0 [none]' }
    if ($mask -eq $allMask) { return ('0x{0:X} [ALL]' -f $mask) }
    $list = for ($i = 0; $i -lt 64; $i++) { if ($mask -band ([long]1 -shl $i)) { $i } }
    $ranges = @(); $start = $list[0]; $prev = $list[0]
    foreach ($x in ($list | Select-Object -Skip 1)) {
        if ($x -eq $prev + 1) { $prev = $x }
        else { $ranges += $(if ($start -eq $prev) { "$start" } else { "$start-$prev" }); $start = $x; $prev = $x }
    }
    $ranges += $(if ($start -eq $prev) { "$start" } else { "$start-$prev" })
    return ('0x{0:X} [{1}]' -f $mask, ($ranges -join ','))
}

function Get-Snapshot {
    $snap = @{}
    foreach ($p in Get-Process) {
        if ($p.Id -eq $selfPid) { continue }
        if ($Name -and $p.ProcessName -notlike "*$Name*") { continue }
        try {
            $snap[$p.Id] = [pscustomobject]@{
                Name = $p.ProcessName; Affinity = $p.ProcessorAffinity.ToInt64()
                Priority = $p.PriorityClass.ToString()
            }
        }
        catch { }
    }
    return $snap
}

function Show-Name([string]$n) {
    if ($n.Length -gt 22) { return $n.Substring(0, 22) }
    return $n
}

function Get-ZoneColor([string]$z) {
    switch ($z) {
        'GAME'  { [ConsoleColor]::Green }
        'MEDIA' { [ConsoleColor]::Cyan }
        'BG'    { [ConsoleColor]::DarkYellow }
        'all'   { [ConsoleColor]::DarkGray }
        default { [ConsoleColor]::Yellow }
    }
}

# ── Render a frame (array of {Text,Color}) without flicker ──────────────────
function Render($lines) {
    $w = [Console]::WindowWidth
    $h = [Console]::WindowHeight
    try { [Console]::SetCursorPosition(0, 0) } catch { return }
    $shown = [Math]::Min($lines.Count, $h - 1)
    for ($i = 0; $i -lt $shown; $i++) {
        $t = [string]$lines[$i].Text
        if ($t.Length -gt $w) { $t = $t.Substring(0, $w) }
        [Console]::ForegroundColor = $lines[$i].Color
        [Console]::Write($t.PadRight($w))
    }
    [Console]::ResetColor()
    for ($i = $shown; $i -lt $h - 1; $i++) { [Console]::Write((' ' * $w)) }
}

function L($text, $color) { [pscustomobject]@{ Text = "  $text"; Color = $color } }

$gray  = [ConsoleColor]::Gray
$dim   = [ConsoleColor]::DarkGray
$white = [ConsoleColor]::White

# ── State ───────────────────────────────────────────────────────────────────
$baseline = Get-Snapshot
$last     = $baseline
$stats    = @{ Pinned = 0; Restored = 0; Priority = 0 }
$events   = [System.Collections.Generic.List[object]]::new()

Write-Log ('=== Affinity Watcher started {0} ===' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Log ("Baseline: {0} processes, {1} cores, ALL = {2}" -f $baseline.Count, $coreCount, (Format-Mask $allMask))

[Console]::CursorVisible = $false
try { Clear-Host } catch { }

try {
    while ($true) {
        $now = Get-Snapshot

        # detect events versus the previous tick
        foreach ($id in $now.Keys) {
            $cur = $now[$id]
            if (-not $last.ContainsKey($id)) { $baseline[$id] = $cur; continue }  # new process
            $old = $last[$id]
            $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

            if ($cur.Affinity -ne $old.Affinity) {
                $restored = $cur.Affinity -eq $allMask
                if ($restored) { $stats.Restored++ } else { $stats.Pinned++ }
                $verb = if ($restored) { 'RESTORED' } else { 'PINNED  ' }
                $txt  = '{0} {1,-22} PID {2,-6} {3} -> {4}  ({5})' -f `
                    $verb, (Show-Name $cur.Name), $id, (Format-Mask $old.Affinity), `
                    (Format-Mask $cur.Affinity), (Get-Zone $cur.Affinity)
                $events.Add([pscustomobject]@{
                    Time = (Get-Date -Format 'HH:mm:ss'); Text = $txt
                    Color = $(if ($restored) { [ConsoleColor]::Green } else { [ConsoleColor]::Yellow })
                })
                Write-Log "$stamp  $txt"
            }
            if ($cur.Priority -ne $old.Priority) {
                $stats.Priority++
                $txt = 'PRIORITY {0,-22} PID {1,-6} {2} -> {3}' -f `
                    (Show-Name $cur.Name), $id, $old.Priority, $cur.Priority
                $events.Add([pscustomobject]@{
                    Time = (Get-Date -Format 'HH:mm:ss'); Text = $txt; Color = [ConsoleColor]::Cyan
                })
                Write-Log "$stamp  $txt"
            }
        }
        $last = $now

        # processes whose state differs from the baseline = currently modified
        $modified = foreach ($id in $now.Keys) {
            if (-not $baseline.ContainsKey($id)) { continue }
            $b = $baseline[$id]; $cur = $now[$id]
            if ($cur.Affinity -ne $b.Affinity -or $cur.Priority -ne $b.Priority) {
                [pscustomobject]@{ Id = $id; Name = $cur.Name
                    Affinity = $cur.Affinity; Priority = $cur.Priority }
            }
        }
        $modified = @($modified | Sort-Object Name)

        # per-core occupancy by the modified processes
        $coreUse = New-Object int[] $coreCount
        foreach ($m in $modified) {
            for ($i = 0; $i -lt $coreCount; $i++) {
                if ($m.Affinity -band ([long]1 -shl $i)) { $coreUse[$i]++ }
            }
        }
        $pinnedNow = @($modified | Where-Object { $_.Affinity -ne $allMask }).Count

        # ── build the frame ────────────────────────────────────────────────
        $elapsed = '{0:hh\:mm\:ss}' -f ((Get-Date) - $startTime)
        $f = [System.Collections.Generic.List[object]]::new()
        $f.Add((L ('CPU AFFINITY MONITOR'.PadRight(48) + "elapsed $elapsed") $white))
        $f.Add((L ("{0} cores   ALL = {1}   {2}" -f $coreCount, (Format-Mask $allMask),
            $(if ($Name) { "filter: $Name" } else { '' })) $dim))
        $f.Add((L ('-' * 74) $dim))

        $f.Add((L 'SUMMARY' $white))
        $f.Add((L ("  pinned events {0,-6} restored events {1,-6} priority changes {2}" -f `
            $stats.Pinned, $stats.Restored, $stats.Priority) $gray))
        $f.Add((L ("  currently modified {0,-4} of which pinned {1}" -f `
            $modified.Count, $pinnedNow) $gray))

        $hdr = '  CORE  ' + (0..($coreCount - 1) | ForEach-Object { '{0,3}' -f $_ }) -join ''
        $usg = '  PROC  ' + (0..($coreCount - 1) | ForEach-Object { '{0,3}' -f $coreUse[$_] }) -join ''
        $f.Add((L $hdr $dim))
        $f.Add((L $usg $gray))
        $f.Add((L ('-' * 74) $dim))

        $f.Add((L ('CURRENTLY MODIFIED   ({0})' -f $modified.Count) $white))
        $f.Add((L ('{0,-22} {1,-7} {2,-22} {3,-13} {4}' -f `
            'NAME', 'PID', 'AFFINITY', 'PRIORITY', 'ZONE') $dim))
        $rowCap = 12
        if ($modified.Count -eq 0) {
            $f.Add((L '(nothing modified yet)' $dim))
        }
        else {
            foreach ($m in ($modified | Select-Object -First $rowCap)) {
                $z = Get-Zone $m.Affinity
                $f.Add((L ('{0,-22} {1,-7} {2,-22} {3,-13} {4}' -f `
                    (Show-Name $m.Name), $m.Id, (Format-Mask $m.Affinity), $m.Priority, $z) `
                    (Get-ZoneColor $z)))
            }
            if ($modified.Count -gt $rowCap) {
                $f.Add((L ('... and {0} more' -f ($modified.Count - $rowCap)) $dim))
            }
        }
        $f.Add((L ('-' * 74) $dim))

        $f.Add((L 'RECENT EVENTS' $white))
        $evCap = 8
        if ($events.Count -eq 0) {
            $f.Add((L '(waiting for changes...)' $dim))
        }
        else {
            $recent = $events | Select-Object -Last $evCap
            foreach ($e in $recent) { $f.Add((L ("[{0}] {1}" -f $e.Time, $e.Text) $e.Color)) }
        }
        $f.Add((L '' $dim))
        $f.Add((L ('Ctrl+C to stop' + $(if ($logPath) { '   |   logging to ' + (Split-Path $logPath -Leaf) } else { '' })) $dim))

        Render $f
        Start-Sleep -Seconds $IntervalSeconds
    }
}
finally {
    [Console]::ResetColor()
    [Console]::CursorVisible = $true
    try { [Console]::SetCursorPosition(0, [Math]::Max(0, [Console]::WindowHeight - 3)) } catch { }
    $summary = 'Stopped. {0} pinned, {1} restored, {2} priority changes.' -f `
        $stats.Pinned, $stats.Restored, $stats.Priority
    Write-Host ''
    Write-Host "  $summary" -ForegroundColor White
    if ($logPath) { Write-Host "  Log saved: $logPath" -ForegroundColor DarkGray }
    Write-Log ('=== {0}  {1} ===' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $summary)
}
