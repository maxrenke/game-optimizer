#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Live watcher for CPU affinity and priority changes across all processes.

.DESCRIPTION
    Run this alongside Gaming Optimizer to verify exactly which processes it
    modifies and how. It baselines every process, then prints a colored line
    whenever a process's CPU affinity mask or priority class changes.

    Use it to confirm behavior: start the watcher, then in the app toggle CPU
    pinning, launch a game, or close the app - and watch what actually gets
    changed and restored.

      YELLOW  - affinity narrowed to a subset of cores (pinned)
      GREEN   - affinity widened back to all cores (restored)
      CYAN    - priority class changed

.PARAMETER Name
    Optional process-name filter (substring, case-insensitive). Omit for all.

.PARAMETER IntervalSeconds
    Poll interval in seconds. Default 1.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Watch-Affinity.ps1 -Name firefox
#>
param(
    [string]$Name,
    [int]$IntervalSeconds = 1
)

$ErrorActionPreference = 'Stop'
$coreCount = [Environment]::ProcessorCount
$allMask   = ([long]1 -shl $coreCount) - 1
$selfPid   = $PID

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

Write-Host ''
Write-Host '  CPU Affinity / Priority Watcher' -ForegroundColor White
Write-Host ("  {0} logical processors    ALL = {1}" -f $coreCount, (Format-Mask $allMask)) -ForegroundColor DarkGray
Write-Host ("  Watching {0}.  Press Ctrl+C to stop." -f `
    $(if ($Name) { "processes matching '$Name'" } else { 'all processes' })) -ForegroundColor DarkGray
Write-Host ''

$state = Get-Snapshot
Write-Host ("  Baseline: {0} processes captured at {1}" -f $state.Count, (Get-Date -Format HH:mm:ss)) -ForegroundColor DarkGray
Write-Host '  ...waiting for changes...' -ForegroundColor DarkGray
Write-Host ''

while ($true) {
    Start-Sleep -Seconds $IntervalSeconds
    $now = Get-Snapshot
    $ts  = Get-Date -Format HH:mm:ss

    foreach ($id in $now.Keys) {
        $cur = $now[$id]
        if (-not $state.ContainsKey($id)) { $state[$id] = $cur; continue }  # new process
        $old = $state[$id]

        if ($cur.Affinity -ne $old.Affinity) {
            $restored = $cur.Affinity -eq $allMask
            $color = if ($restored) { 'Green' } else { 'Yellow' }
            $verb  = if ($restored) { 'RESTORED' } else { 'PINNED  ' }
            Write-Host ("  [{0}] {1,-24} PID {2,-6}  AFFINITY {3}  {4} -> {5}" -f `
                $ts, (Show-Name $cur.Name), $id, $verb, `
                (Format-Mask $old.Affinity), (Format-Mask $cur.Affinity)) -ForegroundColor $color
        }
        if ($cur.Priority -ne $old.Priority) {
            Write-Host ("  [{0}] {1,-24} PID {2,-6}  PRIORITY          {3} -> {4}" -f `
                $ts, (Show-Name $cur.Name), $id, $old.Priority, $cur.Priority) -ForegroundColor Cyan
        }
        $state[$id] = $cur
    }

    # drop processes that have exited so the table does not grow unbounded
    foreach ($id in @($state.Keys)) {
        if (-not $now.ContainsKey($id)) { $state.Remove($id) | Out-Null }
    }
}
