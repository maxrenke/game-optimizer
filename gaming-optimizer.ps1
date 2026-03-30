# ============================================================
#  Universal Gaming Optimizer  -  TUI Edition v4
#  Ryzen 7 5800XT  |  8c / 16t  |  RTX 4070 SUPER
#  Run: powershell -ExecutionPolicy Bypass -File gaming-optimizer.ps1
# ============================================================

# ---- COMMAND LINE OPTIONS -----------------------------------
param(
    [string]$Mode = ""
)

if ($Mode -eq "cleanup") {
    Write-Host "Running cleanup mode — restoring system defaults..." -ForegroundColor Yellow
    $allCores  = [IntPtr](([int64]1 -shl [System.Environment]::ProcessorCount) - 1)
    $normalPri = [System.Diagnostics.ProcessPriorityClass]::Normal

    # 1. Restore Win32PrioritySeparation if stuck at gaming value 26
    try {
        $keyPath = "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl"
        $cur = (Get-ItemProperty -Path $keyPath).Win32PrioritySeparation
        if ($cur -eq 26) {
            Set-ItemProperty -Path $keyPath -Name "Win32PrioritySeparation" -Value 2 -Type DWord -Force
            Write-Host "  [OK] Win32PrioritySeparation: 26 -> 2" -ForegroundColor Green
        } else {
            Write-Host "  [--] Win32PrioritySeparation already $cur, skipping" -ForegroundColor DarkGray
        }
    } catch {
        Write-Host "  [!!] Failed to restore Win32PrioritySeparation (needs admin)" -ForegroundColor Red
    }

    # 2. Restore SysMain
    try {
        $sysMain = Get-Service SysMain -EA SilentlyContinue
        if ($sysMain -and $sysMain.Status -ne "Running") {
            Set-Service SysMain -StartupType Automatic -EA SilentlyContinue
            Start-Service SysMain -EA SilentlyContinue
            Write-Host "  [OK] SysMain restored and restarted" -ForegroundColor Green
        } else {
            Write-Host "  [--] SysMain already running, skipping" -ForegroundColor DarkGray
        }
    } catch {
        Write-Host "  [!!] Failed to restore SysMain" -ForegroundColor Red
    }

    # 3. Release all Firefox processes to all cores + Normal priority
    $ffFixed = 0
    foreach ($proc in (Get-Process firefox -EA SilentlyContinue)) {
        try { $proc.ProcessorAffinity = $allCores; $proc.PriorityClass = $normalPri; $ffFixed++ } catch {}
    }
    if ($ffFixed -gt 0) {
        Write-Host "  [OK] Released $ffFixed Firefox process(es) to all cores" -ForegroundColor Green
    } else {
        Write-Host "  [--] No Firefox processes found" -ForegroundColor DarkGray
    }

    # 4. Release any background processes that may still be throttled
    $bgProcs = @(
        "onedrive","icloudckks","iclouddrive","icloudservices","icloudhome",
        "phoneexperiencehost","crossdeviceservice",
        "malwarebytes","mbamservice","hearthstonedecktracker",
        "backgroundtaskhost","windowspackagemanagerserver",
        "battle.net","hwinfo64","nahimicsvc32","nahimicsvc64",
        "unigetui","appcontrol"
    )
    $bgFixed = 0
    foreach ($name in $bgProcs) {
        foreach ($proc in (Get-Process -Name $name -EA SilentlyContinue)) {
            try { $proc.ProcessorAffinity = $allCores; $proc.PriorityClass = $normalPri; $bgFixed++ } catch {}
        }
    }
    if ($bgFixed -gt 0) {
        Write-Host "  [OK] Released $bgFixed background process(es)" -ForegroundColor Green
    }

    # 5. Kill any orphaned thread jobs from a crashed session
    $jobs = Get-Job -EA SilentlyContinue
    if ($jobs) {
        $jobs | Remove-Job -Force -EA SilentlyContinue
        Write-Host "  [OK] Removed $($jobs.Count) orphaned job(s)" -ForegroundColor Green
    }

    # 6. Reset console state in case script crashed mid-TUI
    try { [Console]::TreatControlCAsInput = $false } catch {}
    try { [Console]::CursorVisible = $true } catch {}
    try { [Console]::ResetColor() } catch {}

    Write-Host ""
    Write-Host "Cleanup complete." -ForegroundColor Green
    exit
}

# ---- AFFINITY MASKS ----------------------------------------
# Cores 0-5  (threads  0-11) -> Games       0xFFF  = 4095
# Core  6    (threads 12-13) -> Firefox     0x3000 = 12288
# Core  7    (threads 14-15) -> Background  0xC000 = 49152
$GAME_AFFINITY    = [IntPtr]0xFFF
$FIREFOX_AFFINITY = [IntPtr]0x3000
$BG_AFFINITY      = [IntPtr]0xC000

$HIGH      = [System.Diagnostics.ProcessPriorityClass]::High
$NORMAL    = [System.Diagnostics.ProcessPriorityClass]::Normal
$BELOWNORM = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
$ALL_CORES = [IntPtr](([int64]1 -shl [System.Environment]::ProcessorCount) - 1)

# ---- SYSTEM OPTIMIZATIONS ----------------------------------
$NIC_NAME          = "Ethernet 2"          # used for network monitoring only
$REPORT_DIR        = "$env:USERPROFILE\Documents\GamingOptimizer"
$PRIO_SEP_KEY      = "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl"
$PRIO_SEP_GAMING   = 26     # short fixed quanta, no foreground boost
$PRIO_SEP_ORIGINAL = $null  # saved on startup

# ---- ALERT THRESHOLDS --------------------------------------
$ALERT_GPU_TEMP   = 80
$ALERT_VRAM_PCT   = 90
$ALERT_GPU_UTIL   = 95
$ALERT_CPU_GAME   = 90
$SUSTAINED_TICKS  = 4   # x3s = 12s sustained

# ---- GAME INSTALL PATHS ------------------------------------
$GAME_PATHS = @(
    "C:\Program Files (x86)\Steam\steamapps\common"
    "C:\Program Files\Steam\steamapps\common"
    "C:\Program Files (x86)\GOG Galaxy\Games"
    "C:\Program Files\GOG Galaxy\Games"
    "C:\Program Files (x86)\Hearthstone"
    "C:\Program Files (x86)\Overwatch"
    "C:\Program Files (x86)\Overwatch\_retail_"
    "C:\Program Files (x86)\World of Warcraft"
    "C:\Program Files (x86)\Diablo IV"
    "C:\Program Files\Epic Games"
)

# ---- NOT A GAME --------------------------------------------
$NOT_A_GAME = @(
    "steam","steamwebhelper","steamservice","gameoverlayui",
    "gogalaxy","gogalaxy-notifications","gogcomm","gogservices",
    "battle.net","epicgameslauncher","epicwebhelper",
    "unitycrashandler64","unitycrashandler32","crashreportclient",
    "unrealcefsubprocess","easyanticheat","easyanticheat_setup",
    "bsoverlay","nvcapcli","nvidiaoverlaycontainer",
    "gamebarftstserver","gamebarpresencewriter",
    "stardocklauncher","unins000"
)

# ---- BACKGROUND PROCESSES TO THROTTLE ----------------------
$BG_PROCESSES = @(
    "onedrive","icloudckks","iclouddrive","icloudservices","icloudhome",
    "phoneexperiencehost","crossdeviceservice",
    "malwarebytes","mbamservice","hearthstonedecktracker",
    "backgroundtaskhost","windowspackagemanagerserver",
    "hwinfo64","nahimicsvc32","nahimicsvc64",
    "unigetui","appcontrol"
    # NOTE: battle.net intentionally excluded — it is a game launcher;
    # throttling it causes child game processes to inherit BG_AFFINITY on spawn
)

# ---- STATE --------------------------------------------------
$script:activeGames          = @{}
$script:appliedFF            = @{}
$script:log                  = [System.Collections.Generic.List[string]]::new()
$script:alerts               = [System.Collections.Generic.List[string]]::new()
$script:startTime            = Get-Date
$script:gpuHighTicks         = 0
$script:cpuHighTicks         = 0
$script:plLoggedThisSession  = $false
$script:pinningEnabled       = $true   # Ctrl+C toggles CPU pinning on/off; Q exits
$script:exitRequested        = $false  # Q key requests clean exit

# Bottleneck detection - rolling 5-sample window (~15s) for stable reading
$script:btCpuHistory         = [int[]](0..4 | ForEach-Object { 0 })
$script:btGpuHistory         = [int[]](0..4 | ForEach-Object { 0 })
$script:btIdx                = 0
$script:bottleneck           = "none"   # "cpu" | "gpu" | "balanced" | "headroom" | "none"
$script:pathFailCache        = [System.Collections.Generic.HashSet[int]]::new()
$script:sysmainStopped       = $false
$script:sysmainOriginalStart = $null   # saved StartType so we restore it exactly

# Network graph: circular buffer of 30 samples (RX, TX KB/s)
$NET_GRAPH_W             = 30   # chars wide (30 samples = 90s of history)
$script:netRxHistory     = [int[]](0..$($NET_GRAPH_W-1) | ForEach-Object { 0 })
$script:netTxHistory     = [int[]](0..$($NET_GRAPH_W-1) | ForEach-Object { 0 })
$script:netHistIdx       = 0
$script:netPrevRx        = 0
$script:netPrevTx        = 0
$script:netPrevTime      = [DateTime]::UtcNow

# Session stats (per-game, persisted to report on exit)
$script:sessionGames     = [System.Collections.Generic.List[hashtable]]::new()
$script:currentGame      = $null

# ---- TUI LAYOUT CONSTANTS ----------------------------------
$W   = 118   # total box width
$IN  = $W - 4

$DIVX    = 56    # vertical divider x between CPU and GPU panels
$GPU_X   = $DIVX + 2

$ROW_TOP      = 0
$ROW_TITLE    = 1
$ROW_DIV1     = 2
$ROW_STATUS   = 3
$ROW_DIV2     = 4
$ROW_ZONE_HDR = 5
$ROW_BAR_G    = 6
$ROW_BAR_Y    = 7
$ROW_BAR_B    = 8
$ROW_GPU_ROW1 = 6   # GPU util
$ROW_GPU_ROW2 = 7   # VRAM
$ROW_GPU_ROW3 = 8   # temp/pwr
$ROW_GPU_ROW4 = 9   # clocks
$ROW_DIV3     = 10
$ROW_NET_HDR  = 11
$ROW_NET_RX   = 12
$ROW_NET_TX   = 13
$ROW_DIV4     = 14
$ROW_ALT_HDR  = 15
$ROW_ALT1     = 16
$ROW_DIV5     = 17
$ROW_LOG_HDR  = 18
$ROW_LOG_0    = 19   # 10 log lines: 19-28
$ROW_DIV6     = 29
$ROW_FOOTER   = 30
$TOTAL_ROWS   = 31

# ---- CONSOLE HELPERS ----------------------------------------
function Write-At($x, $y, $text, $fg = $null, $bg = $null) {
    [Console]::SetCursorPosition($x, $y)
    $oFg = [Console]::ForegroundColor; $oBg = [Console]::BackgroundColor
    if ($fg -ne $null) { [Console]::ForegroundColor = $fg }
    if ($bg -ne $null) { [Console]::BackgroundColor = $bg }
    [Console]::Write($text)
    [Console]::ForegroundColor = $oFg; [Console]::BackgroundColor = $oBg
}

function Draw-HDiv($y, $lm = "╠", $rm = "╣") {
    Write-At 0 $y $lm Cyan; Write-At 1 $y ("─" * ($W-2)) Cyan; Write-At ($W-1) $y $rm Cyan
}

function Draw-VDiv($x, $y1, $y2) {
    for ($r = $y1; $r -le $y2; $r++) { Write-At $x $r "│" Cyan }
}

function Draw-Bar($x, $y, $pct, $width) {
    $fill  = [math]::Min($width, [math]::Round($pct / 100 * $width))
    $empty = $width - $fill
    $col   = if ($pct -gt 80) { [ConsoleColor]::Red } elseif ($pct -gt 50) { [ConsoleColor]::Yellow } else { [ConsoleColor]::Green }
    [Console]::SetCursorPosition($x, $y)
    $oFg = [Console]::ForegroundColor; $oBg = [Console]::BackgroundColor
    [Console]::ForegroundColor = $col;  [Console]::BackgroundColor = $col;  [Console]::Write(" " * $fill)
    [Console]::ForegroundColor = [ConsoleColor]::DarkGray; [Console]::BackgroundColor = [ConsoleColor]::DarkGray; [Console]::Write(" " * $empty)
    [Console]::ForegroundColor = $oFg; [Console]::BackgroundColor = $oBg
}

function Draw-TempColor($temp) {
    if ($temp -ge $ALERT_GPU_TEMP) { return [ConsoleColor]::Red }
    elseif ($temp -ge 70)          { return [ConsoleColor]::Yellow }
    else                           { return [ConsoleColor]::Green }
}

# Pct -> ConsoleColor helper used for numeric readouts
function Pct-Color($pct) {
    if ($pct -gt 80) { return [ConsoleColor]::Red }
    elseif ($pct -gt 50) { return [ConsoleColor]::Yellow }
    else { return [ConsoleColor]::White }
}

# Sparkline-style network graph using block characters
function Draw-NetGraph($x, $y, [int[]]$history, $idx, $width, $maxVal, $color) {
    # Blocks: ▁▂▃▄▅▆▇█ (1/8 to 8/8 height)
    $blocks = [char[]]@(0x2581,0x2582,0x2583,0x2584,0x2585,0x2586,0x2587,0x2588)
    [Console]::SetCursorPosition($x, $y)
    $oFg = [Console]::ForegroundColor
    [Console]::ForegroundColor = $color
    $cap = if ($maxVal -lt 1) { 1 } else { $maxVal }
    for ($i = 0; $i -lt $width; $i++) {
        $sampleIdx = ($idx - $width + $i + $history.Count) % $history.Count
        $val = $history[$sampleIdx]
        $blockIdx = [math]::Min(7, [math]::Floor($val / $cap * 8))
        if ($val -le 0) { [Console]::Write(" ") }
        else            { [Console]::Write($blocks[$blockIdx]) }
    }
    [Console]::ForegroundColor = $oFg
}

# ---- STATIC FRAME (drawn once) ----------------------------
function Draw-Frame() {
    [Console]::Clear()
    Write-At 0 $ROW_TOP "╔$("═" * ($W-2))╗" Cyan
    Write-At 0 $ROW_TITLE "║" Cyan; Write-At ($W-1) $ROW_TITLE "║" Cyan
    Draw-HDiv $ROW_DIV1
    Draw-HDiv $ROW_DIV2
    Draw-HDiv $ROW_DIV3
    Draw-HDiv $ROW_DIV4
    Draw-HDiv $ROW_DIV5
    Draw-HDiv $ROW_DIV6
    Draw-HDiv $ROW_FOOTER "╚" "╝"

    foreach ($r in @($ROW_STATUS,$ROW_ZONE_HDR,$ROW_BAR_G,$ROW_BAR_Y,$ROW_BAR_B,
                     $ROW_GPU_ROW4,$ROW_NET_HDR,$ROW_NET_RX,$ROW_NET_TX,
                     $ROW_ALT_HDR,$ROW_ALT1,$ROW_LOG_HDR)) {
        Write-At 0 $r "║" Cyan; Write-At ($W-1) $r "║" Cyan
    }
    for ($r = $ROW_LOG_0; $r -lt $ROW_DIV6; $r++) {
        Write-At 0 $r "║" Cyan; Write-At ($W-1) $r "║" Cyan
    }

    # Vertical divider CPU | GPU (rows 5-9)
    Draw-VDiv $DIVX $ROW_ZONE_HDR $ROW_GPU_ROW4
    Write-At $DIVX $ROW_DIV2 "╦" Cyan
    Write-At $DIVX $ROW_DIV3 "╩" Cyan

    # Static section labels
    Write-At 2        $ROW_ZONE_HDR "  CPU ZONES" DarkCyan
    Write-At $GPU_X   $ROW_ZONE_HDR "GPU  ·  RTX 4070 SUPER" DarkCyan
    Write-At 2        $ROW_NET_HDR  "  NETWORK  ·  Intel I225-V  (30s history)" DarkCyan
    Write-At 2        $ROW_ALT_HDR  "  BOTTLENECK  ·  ALERTS" DarkCyan
    Write-At 2        $ROW_LOG_HDR  "  EVENT LOG" DarkCyan
}

# ---- CPU SAMPLING ------------------------------------------
# Uses cached WMI data passed from thread job.
# FIX: loop up to ProcessorCount instead of hardcoded 16 for portability.
function Calc-ZonePct($cpuRows, $mask) {
    # Iterate over actual rows, not ProcessorCount — WMI always returns one row
    # per logical processor in production, and tests supply their own row count.
    $vals = for ($i = 0; $i -lt $cpuRows.Count; $i++) {
        if (([int64]$mask -band ([int64]1 -shl $i)) -ne 0) {
            [int]$cpuRows[$i].PercentProcessorTime
        }
    }
    if ($vals.Count) { return [math]::Round(($vals | Measure-Object -Average).Average) }
    return 0
}

# ---- PROCESS HELPERS ----------------------------------------
function Get-ProcPath($proc) {
    # Skip PIDs we already know return nothing (avoids re-querying WMI on same PID every loop)
    if ($script:pathFailCache.Contains($proc.Id)) { return $null }
    $p = $null
    try { $p = $proc.MainModule.FileName } catch {}
    # WMI fallback — handles 32-bit processes queried from a 64-bit shell (e.g. Hearthstone)
    # where MainModule throws a partial-read Win32Exception
    if (-not $p) {
        try { $p = (Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)" -EA Stop).ExecutablePath } catch {}
    }
    if (-not $p) {
        # Only permanently blacklist the PID once the process has been running >5s.
        # Brand-new processes may not have a fully-loaded module list yet; retrying
        # them on the next tick is cheap and prevents missing short-lived game launches.
        $ageMs = ((Get-Date) - $proc.StartTime).TotalMilliseconds
        if ($ageMs -gt 5000) { $script:pathFailCache.Add($proc.Id) | Out-Null }
    }
    return $p
}
function IsGamePath($path) {
    if (-not $path) { return $false }
    foreach ($root in $GAME_PATHS) { if ($path.ToLower().StartsWith($root.ToLower())) { return $true } }
    return $false
}
function IsExcluded($name) { return $NOT_A_GAME -contains ($name.ToLower() -replace '\.exe$','') }
function ApplyGame($proc)    { try { $proc.PriorityClass = $HIGH;   if ($script:pinningEnabled) { $proc.ProcessorAffinity = $GAME_AFFINITY }    return $true } catch { return $false } }
function ApplyFirefox($proc) { try { $proc.PriorityClass = $NORMAL; if ($script:pinningEnabled) { $proc.ProcessorAffinity = $FIREFOX_AFFINITY } return $true } catch { return $false } }

function Release-Pinning() {
    # Remove affinity masks from all tracked processes (let OS schedule freely).
    # FIX: also restore game processes to Normal priority — previously only affinity was released.
    $allCores = $ALL_CORES
    $normalPri = [System.Diagnostics.ProcessPriorityClass]::Normal
    foreach ($gid in $script:activeGames.Keys) {
        $p = Get-Process -Id $gid -EA SilentlyContinue
        if ($p) { try { $p.ProcessorAffinity = $allCores; $p.PriorityClass = $normalPri } catch {} }
    }
    # Use live enumeration (not just tracked dict) so any Firefox spawned after last scan is caught
    foreach ($proc in (Get-Process firefox -EA SilentlyContinue)) {
        try { $proc.ProcessorAffinity = $allCores; $proc.PriorityClass = $normalPri } catch {}
    }
    foreach ($name in $BG_PROCESSES) {
        foreach ($p in (Get-Process -Name $name -EA SilentlyContinue)) {
            try { $p.ProcessorAffinity = $allCores; $p.PriorityClass = $normalPri } catch {}
        }
    }
    AddLog "[PIN] CPU pinning DISABLED — all processes running on all cores"
}

function Restore-Pinning() {
    # Re-apply affinity masks to all tracked processes
    foreach ($gid in $script:activeGames.Keys) {
        $p = Get-Process -Id $gid -EA SilentlyContinue
        if ($p) { try { $p.ProcessorAffinity = $GAME_AFFINITY; $p.PriorityClass = $HIGH } catch {} }
    }
    # Use live Firefox enumeration — not just the tracked dict — so processes
    # that spawned while pinning was off also get pinned, and update the dict.
    foreach ($proc in (Get-Process firefox -EA SilentlyContinue)) {
        try {
            $proc.ProcessorAffinity = $FIREFOX_AFFINITY
            $proc.PriorityClass = $NORMAL
            $script:appliedFF[$proc.Id] = $true
        } catch {}
    }
    ThrottleBg
    AddLog "[PIN] CPU pinning ENABLED — affinities restored"
}

function ThrottleBg() {
    $count = 0
    foreach ($name in $BG_PROCESSES) {
        foreach ($proc in (Get-Process -Name $name -EA SilentlyContinue)) {
            try { $proc.PriorityClass = $BELOWNORM; $proc.ProcessorAffinity = $BG_AFFINITY; $count++ } catch {}
        }
    }
    return $count
}

function AddLog($msg) {
    $ts = Get-Date -Format "HH:mm:ss"
    $script:log.Add("[$ts] $msg")
    if ($script:log.Count -gt 10) { $script:log.RemoveAt(0) }
}

# ---- SYSTEM OPTIMIZATION ON/OFF ----------------------------
function Enable-GamingOptimizations() {
    # 1. Win32PrioritySeparation -> short fixed quanta, no foreground boost
    try {
        $cur = (Get-ItemProperty $PRIO_SEP_KEY).Win32PrioritySeparation
        $script:PRIO_SEP_ORIGINAL = $cur
        Set-ItemProperty $PRIO_SEP_KEY -Name "Win32PrioritySeparation" -Value 26 -Type DWord -EA Stop
        AddLog "[SYS] PrioritySeparation: $cur -> 26 (fixed quanta)"
    } catch { AddLog "[SYS] PrioritySep change failed (needs admin)" }

    # 2. Suspend SysMain (Superfetch) - useless on NVMe, causes stalls
    $sysMain = Get-Service SysMain -EA SilentlyContinue
    if ($sysMain -and $sysMain.StartType -ne "Disabled" -and $sysMain.Status -eq "Running") {
        # Save StartType FIRST so a crash between save and stop still restores correctly
        $script:sysmainOriginalStart = $sysMain.StartType
        $script:sysmainStopped = $true
        # Fire stop in background — service stop can block for 2-4s; no need to wait
        $script:jSysMain = Start-ThreadJob { Stop-Service SysMain -Force -EA SilentlyContinue }
        AddLog "[SYS] SysMain suspended (was $($sysMain.StartType), NVMe - no benefit)"
    } elseif ($sysMain -and $sysMain.StartType -eq "Disabled") {
        AddLog "[SYS] SysMain already disabled - skipping"
    }
}

function Disable-GamingOptimizations() {
    # Restore Win32PrioritySeparation
    if ($script:PRIO_SEP_ORIGINAL -ne $null) {
        try {
            Set-ItemProperty $PRIO_SEP_KEY -Name "Win32PrioritySeparation" -Value $script:PRIO_SEP_ORIGINAL -Type DWord -EA Stop
            AddLog "[SYS] PrioritySeparation restored to $($script:PRIO_SEP_ORIGINAL)"
        } catch {}
    }

    # Restart SysMain and restore its original StartType
    if ($script:sysmainStopped) {
        $startType = if ($script:sysmainOriginalStart) { $script:sysmainOriginalStart } else { "Automatic" }
        try { Set-Service SysMain -StartupType $startType -EA Stop } catch {}
        Start-Service SysMain -EA SilentlyContinue
        AddLog "[SYS] SysMain restarted (StartType restored to $startType)"
    }
}

# ---- SESSION TRACKING --------------------------------------
function Start-GameSession($name) {
    $script:plLoggedThisSession = $false
    $script:currentGame = @{
        Name        = $name
        StartTime   = Get-Date
        EndTime     = $null
        PeakCpu     = 0
        PeakGpu     = 0
        PeakTemp    = 0
        PeakVramPct = 0
        Samples     = 0
        SumCpu      = 0
        SumGpu      = 0
        BtCpuTicks  = 0
        BtGpuTicks  = 0
        BtBalTicks  = 0
        BtHrTicks   = 0
    }
}

function Update-GameSession($cpuPct, $gpu) {
    if (-not $script:currentGame) { return }
    $g = $script:currentGame
    $g.Samples++
    $g.SumCpu += $cpuPct
    if ($cpuPct -gt $g.PeakCpu) { $g.PeakCpu = $cpuPct }
    if ($gpu) {
        $g.SumGpu += $gpu.GpuUtil
        if ($gpu.GpuUtil -gt $g.PeakGpu)  { $g.PeakGpu  = $gpu.GpuUtil }
        if ($gpu.Temp    -gt $g.PeakTemp) { $g.PeakTemp = $gpu.Temp }
        $vp = [math]::Round($gpu.MemUsedMB / $gpu.MemTotMB * 100)
        if ($vp -gt $g.PeakVramPct) { $g.PeakVramPct = $vp }
    }
    switch ($script:bottleneck) {
        "cpu"      { $g.BtCpuTicks++ }
        "gpu"      { $g.BtGpuTicks++ }
        "balanced" { $g.BtBalTicks++ }
        "headroom" { $g.BtHrTicks++  }
    }
}

function End-GameSession() {
    if (-not $script:currentGame) { return }
    $script:currentGame.EndTime = Get-Date
    $script:sessionGames.Add($script:currentGame)
    $script:currentGame = $null
}

# ---- SESSION REPORT ----------------------------------------
function Save-SessionReport() {
    if (-not (Test-Path $REPORT_DIR)) { New-Item -ItemType Directory -Path $REPORT_DIR -Force | Out-Null }
    # Prune reports older than 30 days
    Get-ChildItem $REPORT_DIR -Filter "*.txt" -EA SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
        Remove-Item -Force -EA SilentlyContinue

    $date      = Get-Date -Format "yyyy-MM-dd"
    $dateTime  = Get-Date -Format "yyyy-MM-dd HH:mm"
    $file      = "$REPORT_DIR\session_$($date)_$(Get-Date -Format 'HHmm').txt"
    $uptime    = (Get-Date) - $script:startTime
    $uptimeStr = "{0}h {1}m" -f [int]$uptime.TotalHours, $uptime.Minutes

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("╔══════════════════════════════════════════════════════╗")
    $lines.Add("║  Gaming Optimizer  -  Session Report                 ║")
    $lines.Add("║  $dateTime    uptime $uptimeStr".PadRight(54) + "║")
    if ($script:sessionGames.Count -eq 0) {
        $lines.Add("╠══════════════════════════════════════════════════════╣")
        $lines.Add("║  No games detected this session.                     ║")
    } else {
        foreach ($g in $script:sessionGames) {
            $dur    = if ($g.EndTime) { $g.EndTime - $g.StartTime } else { (Get-Date) - $g.StartTime }
            $durStr = "{0}h {1:D2}m {2:D2}s" -f [int]$dur.TotalHours, $dur.Minutes, $dur.Seconds
            $avgCpu = if ($g.Samples -gt 0) { [math]::Round($g.SumCpu / $g.Samples) } else { 0 }
            $avgGpu = if ($g.Samples -gt 0) { [math]::Round($g.SumGpu / $g.Samples) } else { 0 }
            $lines.Add("╠══════════════════════════════════════════════════════╣")
            $lines.Add("║  Game    : $($g.Name)".PadRight(54) + "║")
            $lines.Add("║  Started : $(($g.StartTime).ToString('HH:mm:ss'))    Duration: $durStr".PadRight(54) + "║")
            $lines.Add("║  CPU zone  avg $("$avgCpu%".PadLeft(4))   peak $("$($g.PeakCpu)%".PadLeft(4))".PadRight(54) + "║")
            $lines.Add("║  GPU util  avg $("$avgGpu%".PadLeft(4))   peak $("$($g.PeakGpu)%".PadLeft(4))   temp peak $($g.PeakTemp)°C".PadRight(54) + "║")
            $lines.Add("║  VRAM peak $("$($g.PeakVramPct)%".PadLeft(4))".PadRight(54) + "║")
            $btTotal = $g.BtCpuTicks + $g.BtGpuTicks + $g.BtBalTicks + $g.BtHrTicks
            if ($btTotal -gt 0) {
                $pCpu = [math]::Round($g.BtCpuTicks / $btTotal * 100)
                $pGpu = [math]::Round($g.BtGpuTicks / $btTotal * 100)
                $pBal = [math]::Round($g.BtBalTicks / $btTotal * 100)
                $pHr  = [math]::Round($g.BtHrTicks  / $btTotal * 100)
                $dominant = if ($pGpu -ge $pCpu -and $pGpu -ge $pBal -and $pGpu -ge $pHr) { "GPU-bound" }
                            elseif ($pCpu -ge $pBal -and $pCpu -ge $pHr) { "CPU-bound" }
                            elseif ($pBal -ge $pHr) { "Balanced" } else { "Headroom" }
                $lines.Add("║  Bottleneck: $dominant most of session".PadRight(54) + "║")
                $lines.Add("║    GPU $("$pGpu%".PadLeft(4))  CPU $("$pCpu%".PadLeft(4))  Balanced $("$pBal%".PadLeft(4))  Headroom $("$pHr%".PadLeft(4))".PadRight(54) + "║")
            }
        }
    }
    $lines.Add("╚══════════════════════════════════════════════════════╝")

    $lines | Set-Content -Path $file -Encoding UTF8
    return $file
}

# ---- BOTTLENECK DETECTION ----------------------------------
function Update-Bottleneck($gameCpu, $gpu) {
    $script:btCpuHistory[$script:btIdx] = $gameCpu
    $script:btGpuHistory[$script:btIdx] = if ($gpu) { $gpu.GpuUtil } else { 0 }
    $script:btIdx = ($script:btIdx + 1) % $script:btCpuHistory.Count

    if ($script:activeGames.Count -eq 0) { $script:bottleneck = "none"; return }

    $avgCpu = [math]::Round(($script:btCpuHistory | Measure-Object -Average).Average)
    $avgGpu = [math]::Round(($script:btGpuHistory | Measure-Object -Average).Average)

    # Classification thresholds (tuned for RTX 4070 Super + Ryzen 7 5800XT)
    $script:bottleneck = if     ($avgGpu -ge 90)                         { "gpu" }
                         elseif ($avgCpu -ge 75 -and $avgGpu -lt 75)     { "cpu" }
                         elseif ($avgCpu -ge 55 -or  $avgGpu -ge 55)     { "balanced" }
                         else                                             { "headroom" }
}

# ---- ALERT CHECK -------------------------------------------
function Check-Alerts($gameCpu, $gpu) {
    $script:alerts.Clear()
    if ($gpu) {
        if ($gpu.Temp -ge $ALERT_GPU_TEMP) { $script:alerts.Add("[!] GPU temp $($gpu.Temp)°C — exceeds ${ALERT_GPU_TEMP}°C") }
        $vp = [math]::Round($gpu.MemUsedMB / $gpu.MemTotMB * 100)
        if ($vp -ge $ALERT_VRAM_PCT) { $script:alerts.Add("[!] VRAM ${vp}% ($([math]::Round($gpu.MemUsedMB/1024,1))/$([math]::Round($gpu.MemTotMB/1024,0))GB)") }
        if ($gpu.GpuUtil -ge $ALERT_GPU_UTIL) { $script:gpuHighTicks++ } else { $script:gpuHighTicks = 0 }
        if ($script:gpuHighTicks -ge $SUSTAINED_TICKS) { $script:alerts.Add("[!] GPU util $($gpu.GpuUtil)% sustained — possible bottleneck") }
    }
    if ($gameCpu -ge $ALERT_CPU_GAME) { $script:cpuHighTicks++ } else { $script:cpuHighTicks = 0 }
    if ($script:cpuHighTicks -ge $SUSTAINED_TICKS) { $script:alerts.Add("[!] Game CPU zone ${gameCpu}% — cores 0-5 saturated") }
}

# ---- DASHBOARD UPDATE ---------------------------------------
function Update-Dashboard($gamePct, $ffPct, $bgPct, $gpu) {
    $gaming    = $script:activeGames.Count -gt 0
    $gameNames = if ($gaming) { ($script:activeGames.Values | Select-Object -Unique) -join ", " } else { "—" }
    $ffCount   = (Get-Process firefox -EA SilentlyContinue | Measure-Object).Count
    $uptime    = (Get-Date) - $script:startTime
    $uptimeStr = "{0:D2}h {1:D2}m {2:D2}s" -f [int]$uptime.TotalHours, $uptime.Minutes, $uptime.Seconds

    # ── Title ─────────────────────────────────────────────
    Write-At 2       $ROW_TITLE "  Universal Gaming Optimizer  v4" White
    Write-At ($W-22) $ROW_TITLE "uptime $uptimeStr" DarkGray

    # ── Status ────────────────────────────────────────────
    Write-At 0 $ROW_STATUS "║" Cyan; Write-At ($W-1) $ROW_STATUS "║" Cyan
    if ($gaming) {
        $badge   = "  [*] GAMING MODE ACTIVE"
        $gameStr = "  Game: $($gameNames.PadRight(22))"
        $ffStr   = "Firefox: $ffCount PIDs  "
        $gap     = $IN - $badge.Length - $gameStr.Length - $ffStr.Length
        Write-At 2 $ROW_STATUS ($badge + $gameStr + (" " * [math]::Max(0,$gap)) + $ffStr).PadRight($IN).Substring(0,$IN) Magenta
    } else {
        $idle  = "  [ ] Idle — waiting for game launch"
        $ffStr = "Firefox: $ffCount PIDs  "
        $gap   = $IN - $idle.Length - $ffStr.Length
        Write-At 2 $ROW_STATUS ($idle + (" " * [math]::Max(0,$gap)) + $ffStr).PadRight($IN).Substring(0,$IN) DarkGray
    }

    # ── CPU bars (left panel) ─────────────────────────────
    # FIX: label was "YouTube c6" — corrected to "Firefox c6"
    $cpuLX = 2; $bx = 17; $CPU_BAR_W = 24
    Write-At $cpuLX $ROW_BAR_G "   Game   0-5  " $(if ($gaming) { [ConsoleColor]::Magenta } else { [ConsoleColor]::DarkGray })
    Write-At $cpuLX $ROW_BAR_Y "   Firefox c6  " DarkYellow
    Write-At $cpuLX $ROW_BAR_B "   Bg/OS   c7  " DarkGray
    Draw-Bar $bx $ROW_BAR_G $gamePct $CPU_BAR_W
    Draw-Bar $bx $ROW_BAR_Y $ffPct   $CPU_BAR_W
    Draw-Bar $bx $ROW_BAR_B $bgPct   $CPU_BAR_W
    $px = $bx + $CPU_BAR_W + 1
    # FIX: use Pct-Color helper (ConsoleColor enum) instead of bare string literals
    Write-At $px $ROW_BAR_G "$("$gamePct%".PadLeft(4)) " (Pct-Color $gamePct)
    Write-At $px $ROW_BAR_Y "$("$ffPct%".PadLeft(4)) "  (Pct-Color $ffPct)
    Write-At $px $ROW_BAR_B "$("$bgPct%".PadLeft(4)) "  (Pct-Color $bgPct)

    # ── GPU panel (right) ─────────────────────────────────
    if ($gpu) {
        $vramPct = [math]::Round($gpu.MemUsedMB / $gpu.MemTotMB * 100)
        $vramGB  = "$([math]::Round($gpu.MemUsedMB/1024,1))/$([math]::Round($gpu.MemTotMB/1024,0))GB"
        $GPU_BAR_W = 24; $gbx = $GPU_X + 6
        Write-At $GPU_X $ROW_GPU_ROW1 " Util " DarkGray
        Draw-Bar $gbx $ROW_GPU_ROW1 $gpu.GpuUtil $GPU_BAR_W
        $gpx = $gbx + $GPU_BAR_W + 1
        Write-At $gpx $ROW_GPU_ROW1 "$("$($gpu.GpuUtil)%".PadLeft(4))  " (Pct-Color $gpu.GpuUtil)
        Write-At $GPU_X $ROW_GPU_ROW2 " VRAM " DarkGray
        Draw-Bar $gbx $ROW_GPU_ROW2 $vramPct $GPU_BAR_W
        Write-At $gpx $ROW_GPU_ROW2 " $($vramGB.PadRight(10))" $(if($vramPct-gt80){[ConsoleColor]::Red}elseif($vramPct-gt50){[ConsoleColor]::Yellow}else{[ConsoleColor]::Cyan})
        $tempCol = Draw-TempColor $gpu.Temp
        Write-At $GPU_X      $ROW_GPU_ROW3 " Temp " DarkGray
        Write-At ($GPU_X+6)  $ROW_GPU_ROW3 "$($gpu.Temp)°C   " $tempCol
        Write-At ($GPU_X+16) $ROW_GPU_ROW3 " Pwr " DarkGray
        Write-At ($GPU_X+21) $ROW_GPU_ROW3 "$($gpu.PowerW)W/$($gpu.PowerLimW)W    " White
        Write-At $GPU_X      $ROW_GPU_ROW4 " Core " DarkGray
        Write-At ($GPU_X+6)  $ROW_GPU_ROW4 "$($gpu.ClkMhz) MHz  " White
        Write-At ($GPU_X+20) $ROW_GPU_ROW4 " Mem " DarkGray
        Write-At ($GPU_X+25) $ROW_GPU_ROW4 "$($gpu.MemClkMhz) MHz  " White
    } else {
        Write-At $GPU_X $ROW_GPU_ROW1 " nvidia-smi unavailable" DarkGray
    }

    # ── Network graph ─────────────────────────────────────
    Write-At 0 $ROW_NET_RX "║" Cyan; Write-At ($W-1) $ROW_NET_RX "║" Cyan
    Write-At 0 $ROW_NET_TX "║" Cyan; Write-At ($W-1) $ROW_NET_TX "║" Cyan

    $rxNow  = $script:netRxHistory[($script:netHistIdx - 1 + $NET_GRAPH_W) % $NET_GRAPH_W]
    $txNow  = $script:netTxHistory[($script:netHistIdx - 1 + $NET_GRAPH_W) % $NET_GRAPH_W]
    $rxPeak = ($script:netRxHistory | Measure-Object -Maximum).Maximum
    $txPeak = ($script:netTxHistory | Measure-Object -Maximum).Maximum

    $rxLabel = "  RX  $("$rxNow KB/s".PadLeft(12))  peak $("$rxPeak KB/s".PadLeft(10))  "
    $txLabel = "  TX  $("$txNow KB/s".PadLeft(12))  peak $("$txPeak KB/s".PadLeft(10))  "
    Write-At 2 $ROW_NET_RX $rxLabel $(if($rxNow-gt500){[ConsoleColor]::Yellow}else{[ConsoleColor]::DarkCyan})
    Write-At 2 $ROW_NET_TX $txLabel $(if($txNow-gt500){[ConsoleColor]::Yellow}else{[ConsoleColor]::DarkGray})

    $graphX = $rxLabel.Length + 2
    $graphW = [math]::Min($NET_GRAPH_W, $IN - $graphX - 2)
    $rxCap  = [math]::Max(100, $rxPeak)
    $txCap  = [math]::Max(100, $txPeak)
    Draw-NetGraph $graphX $ROW_NET_RX $script:netRxHistory $script:netHistIdx $graphW $rxCap ([ConsoleColor]::Cyan)
    Draw-NetGraph $graphX $ROW_NET_TX $script:netTxHistory $script:netHistIdx $graphW $txCap ([ConsoleColor]::DarkGray)
    $clearX = $graphX + $graphW; $clearW = $IN - $clearX - 2
    if ($clearW -gt 0) {
        Write-At $clearX $ROW_NET_RX (" " * $clearW)
        Write-At $clearX $ROW_NET_TX (" " * $clearW)
    }

    # ── Bottleneck + Alerts ──────────────────────────────
    Write-At 0 $ROW_ALT1 "║" Cyan; Write-At ($W-1) $ROW_ALT1 "║" Cyan

    $btLabel = switch ($script:bottleneck) {
        "gpu"      { @{ text = "  [GPU-BOUND]  Lower resolution/quality — GPU is the limit   "; fg = [ConsoleColor]::Red } }
        "cpu"      { @{ text = "  [CPU-BOUND]  Lower draw dist/entities — CPU starving GPU    "; fg = [ConsoleColor]::Yellow } }
        "balanced" { @{ text = "  [BALANCED ]  Healthy load — good settings balance           "; fg = [ConsoleColor]::Green } }
        "headroom" { @{ text = "  [HEADROOM ]  Both low — you can raise quality settings      "; fg = [ConsoleColor]::Cyan } }
        default    { @{ text = "  [--------]  No game running                                 "; fg = [ConsoleColor]::DarkGray } }
    }
    Write-At 2 $ROW_ALT1 $btLabel.text.PadRight(56).Substring(0,56) $btLabel.fg

    $alertX = 58; $alertW = $IN - $alertX
    if ($script:alerts.Count -gt 0) {
        Write-At $alertX $ROW_ALT1 ("⚠ " + $script:alerts[0]).PadRight($alertW).Substring(0,$alertW) Red
    } else {
        Write-At $alertX $ROW_ALT1 "✓ All zones healthy".PadRight($alertW).Substring(0,$alertW) DarkGray
    }

    # ── Event log ────────────────────────────────────────
    $logLines = 10
    $padded = [System.Collections.Generic.List[string]]::new()
    foreach ($e in $script:log) { $padded.Add($e) }
    while ($padded.Count -lt $logLines) { $padded.Add("") }
    for ($i = 0; $i -lt $logLines; $i++) {
        $line = $padded[$i]; $row = $ROW_LOG_0 + $i
        $fg = [ConsoleColor]::DarkGray
        if    ($line -match '\[GAME\]')  { $fg = [ConsoleColor]::Magenta }
        elseif($line -match '\[ENDED\]') { $fg = [ConsoleColor]::Yellow  }
        elseif($line -match '\[FF\]')    { $fg = [ConsoleColor]::Cyan    }
        elseif($line -match '\[SYS\]')   { $fg = [ConsoleColor]::Yellow  }
        elseif($line -match '\[PIN\]')   { $fg = [ConsoleColor]::Green   }   # FIX: added [PIN] coloring
        elseif($line -match '\[BG\]')    { $fg = [ConsoleColor]::DarkGray}
        elseif($line -match '\[!\]')     { $fg = [ConsoleColor]::Red     }
        elseif($line -match '\[INIT\]')  { $fg = [ConsoleColor]::Green   }
        Write-At 2 $row ("  " + $line).PadRight($IN) $fg
    }

    # ── Footer ───────────────────────────────────────────
    $reportCount = if (Test-Path $REPORT_DIR) { (Get-ChildItem $REPORT_DIR -Filter "*.txt" -EA SilentlyContinue).Count } else { 0 }
    $pinLabel    = if ($script:pinningEnabled) { "[PIN ON ]" } else { "[PIN OFF]" }
    $pinColor    = if ($script:pinningEnabled) { [ConsoleColor]::Green } else { [ConsoleColor]::Yellow }
    Write-At 2         $ROW_FOOTER "  Ctrl+C: toggle CPU pinning   Q: exit   Reports: $REPORT_DIR  ($reportCount sessions)".PadRight($IN) DarkGray
    Write-At ($W - 14) $ROW_FOOTER $pinLabel $pinColor
}

# ---- CONFIG FILE (optional overrides) ----------------------
# Copy config.psd1.example to config.psd1 and edit to customize.
# All keys are optional; omitted keys keep their defaults above.
$_cfgPath = if ($PSScriptRoot) { "$PSScriptRoot\config.psd1" } else { $null }
if ($_cfgPath -and (Test-Path $_cfgPath)) {
    try {
        $cfg = Import-PowerShellDataFile $_cfgPath -EA Stop
        if ($cfg.ContainsKey('NicName'))             { $NIC_NAME         = $cfg.NicName }
        if ($cfg.ContainsKey('GameAffinityMask'))     { $GAME_AFFINITY    = [IntPtr]([int64]$cfg.GameAffinityMask) }
        if ($cfg.ContainsKey('FirefoxAffinityMask'))  { $FIREFOX_AFFINITY = [IntPtr]([int64]$cfg.FirefoxAffinityMask) }
        if ($cfg.ContainsKey('BgAffinityMask'))       { $BG_AFFINITY      = [IntPtr]([int64]$cfg.BgAffinityMask) }
        if ($cfg.ContainsKey('AlertGpuTempC'))        { $ALERT_GPU_TEMP   = [int]$cfg.AlertGpuTempC }
        if ($cfg.ContainsKey('AlertVramPct'))          { $ALERT_VRAM_PCT  = [int]$cfg.AlertVramPct }
        if ($cfg.ContainsKey('AlertGpuUtilPct'))       { $ALERT_GPU_UTIL  = [int]$cfg.AlertGpuUtilPct }
        if ($cfg.ContainsKey('AlertCpuZonePct'))       { $ALERT_CPU_GAME  = [int]$cfg.AlertCpuZonePct }
        if ($cfg.ContainsKey('AlertSustainedTicks'))   { $SUSTAINED_TICKS = [int]$cfg.AlertSustainedTicks }
        if ($cfg.ContainsKey('GamePaths'))             { $GAME_PATHS      = $cfg.GamePaths }
        if ($cfg.ContainsKey('ExtraThrottledProcs'))   { $BG_PROCESSES    = $BG_PROCESSES + $cfg.ExtraThrottledProcs }
    } catch {
        Write-Warning "config.psd1 failed to load: $_"
    }
}
Remove-Variable _cfgPath -EA SilentlyContinue

# ---- STARTUP -----------------------------------------------
$Host.UI.RawUI.WindowTitle = "Gaming Optimizer v4"
[Console]::CursorVisible  = $false
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Outer guard: ensures cursor + title + all jobs are always restored even on startup crash
trap {
    try { [Console]::TreatControlCAsInput = $false } catch {}
    [Console]::CursorVisible = $true
    [Console]::ResetColor()
    [Console]::Clear()
    $Host.UI.RawUI.WindowTitle = "PowerShell"
    Get-Job | Remove-Job -Force -EA SilentlyContinue
    # FIX: restore all three process groups on crash, not just Firefox
    try {
        $allCores = $ALL_CORES
        foreach ($gid in $script:activeGames.Keys) {
            $p = Get-Process -Id $gid -EA SilentlyContinue
            if ($p) { try { $p.ProcessorAffinity = $allCores; $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal } catch {} }
        }
        Get-Process firefox -EA SilentlyContinue | ForEach-Object {
            try { $_.ProcessorAffinity = $allCores; $_.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal } catch {}
        }
        foreach ($name in $BG_PROCESSES) {
            Get-Process -Name $name -EA SilentlyContinue | ForEach-Object {
                try { $_.ProcessorAffinity = $allCores; $_.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal } catch {}
            }
        }
    } catch {}
    Write-Host "`n[FATAL] Unhandled error: $_" -ForegroundColor Red
    break
}

try {
    $ui = $Host.UI.RawUI
    $b  = $ui.BufferSize
    if ($b.Width  -lt $W+2)            { $b.Width  = $W+2 }
    if ($b.Height -lt $TOTAL_ROWS + 4) { $b.Height = $TOTAL_ROWS + 4 }
    $ui.BufferSize = $b
    $wn = $ui.WindowSize
    if ($wn.Width  -lt $W+2)            { $wn.Width  = $W+2 }
    if ($wn.Height -lt $TOTAL_ROWS + 2) { $wn.Height = $TOTAL_ROWS + 2 }
    $ui.WindowSize = $wn
} catch {}

if (-not (Test-Path $REPORT_DIR)) { New-Item -ItemType Directory -Path $REPORT_DIR -Force | Out-Null }

Draw-Frame

# Clean up any orphaned jobs from a previous crashed session
Get-Job | Where-Object { $_.Name -like 'Job*' -or $_.PSJobTypeName -eq 'ThreadJob' } | Remove-Job -Force -EA SilentlyContinue

# Pre-warm WMI perf counters — first call takes 3-5s cold; fire it now so it
# runs in parallel with the remaining init steps instead of blocking the first loop tick
$jWarmup = Start-ThreadJob { Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -EA SilentlyContinue | Out-Null }

# Safety check: if a previous session crashed, PrioritySep might still be 26
try {
    $stalePrio = (Get-ItemProperty $PRIO_SEP_KEY -EA Stop).Win32PrioritySeparation
    if ($stalePrio -eq 26) {
        Set-ItemProperty $PRIO_SEP_KEY -Name "Win32PrioritySeparation" -Value 2 -Type DWord -EA Stop
        AddLog "[INIT] Stale PrioritySep=26 detected (prev crash?) — reset to 2"
    }
} catch {}

Enable-GamingOptimizations

AddLog "[INIT] v4 started — optimizations applied"

# Validate affinity masks against actual thread count
$_threads = [System.Environment]::ProcessorCount
$_maxMaskBit = [math]::Max([math]::Max([int64]$GAME_AFFINITY, [int64]$FIREFOX_AFFINITY), [int64]$BG_AFFINITY)
$_bitsNeeded = [math]::Ceiling([math]::Log($_maxMaskBit + 1, 2))
if ($_bitsNeeded -gt $_threads) {
    AddLog "[INIT] WARNING: affinity masks need $_bitsNeeded threads, CPU has $_threads — update config.psd1"
} elseif ($_threads -ne 16) {
    AddLog "[INIT] NOTE: running on $_threads-thread CPU (masks tuned for 16t) — verify zones in config.psd1"
}
Remove-Variable _threads, _maxMaskBit, _bitsNeeded -EA SilentlyContinue

foreach ($p in $GAME_PATHS) {
    if (Test-Path $p) { AddLog "[INIT] OK: $(Split-Path $p -Leaf)" }
}
if (Get-Command nvidia-smi -EA SilentlyContinue) {
    AddLog "[INIT] nvidia-smi found — GPU monitoring active"
} else {
    AddLog "[INIT] WARNING: nvidia-smi not in PATH — GPU panel will be empty"
}
if (-not (Get-NetAdapter -Name $NIC_NAME -EA SilentlyContinue)) {
    # Try to fall back to the first physical UP adapter, sorted by link speed
    $_fallback = Get-NetAdapter -EA SilentlyContinue |
        Where-Object { $_.Status -eq "Up" -and $_.InterfaceDescription -notmatch "Virtual|Loopback|Bluetooth|Wi-Fi Direct|TAP|VPN" } |
        Sort-Object LinkSpeed -Descending |
        Select-Object -First 1
    if ($_fallback) {
        $_prevName = $NIC_NAME
        $NIC_NAME  = $_fallback.Name
        AddLog "[INIT] NIC '$_prevName' not found — auto-selected '$NIC_NAME'"
        Remove-Variable _fallback, _prevName -EA SilentlyContinue
    } else {
        AddLog "[INIT] WARNING: NIC '$NIC_NAME' not found and no fallback — network graph disabled"
    }
}
# Init network baseline (done here so NIC auto-detection above can update $NIC_NAME first)
try {
    $s0 = Get-NetAdapterStatistics -Name $NIC_NAME -EA Stop
    $script:netPrevRx   = $s0.ReceivedBytes
    $script:netPrevTx   = $s0.SentBytes
    $script:netPrevTime = [DateTime]::UtcNow
} catch {}

# ---- PRE-THROTTLE GAME SCAN ---------------------------------
# Detect any games already running BEFORE ThrottleBg fires so they are
# in $activeGames and get GAME_AFFINITY rather than sitting unprotected.
foreach ($proc in (Get-Process | Where-Object { -not $_.HasExited })) {
    if (IsExcluded $proc.Name) { continue }
    $exePath = Get-ProcPath $proc
    if (IsGamePath $exePath) {
        if (ApplyGame $proc) {
            $script:activeGames[$proc.Id] = $proc.Name
            AddLog "[INIT] Pre-existing game detected: $($proc.Name) (PID $($proc.Id)) — affinity set before throttle"
            if (-not $script:currentGame) { Start-GameSession $proc.Name }
        }
    }
}

ThrottleBg | Out-Null
AddLog "[BG] Background processes throttled"

$loopCount = 0

# ---- MAIN LOOP ---------------------------------------------
try {
    while (-not $script:exitRequested) {
        $loopCount++

        [Console]::TreatControlCAsInput = $true

        while ([Console]::KeyAvailable) {
            $key     = [Console]::ReadKey($true)
            $isCtrlC = ($key.Key -eq [ConsoleKey]::C) -and ($key.Modifiers -band [ConsoleModifiers]::Control)
            $isQ     = ($key.Key -eq [ConsoleKey]::Q)

            if ($isCtrlC) {
                $script:pinningEnabled = -not $script:pinningEnabled
                if ($script:pinningEnabled) { Restore-Pinning } else { Release-Pinning }
            } elseif ($isQ) {
                $script:exitRequested = $true
                # Drain remaining keys so no stale input reaches the terminal after exit
                while ([Console]::KeyAvailable) { [Console]::ReadKey($true) | Out-Null }
                break
            }
        }
        if ($script:exitRequested) { break }

        # Detect new games
        foreach ($proc in (Get-Process | Where-Object { -not $_.HasExited })) {
            if ($script:activeGames.ContainsKey($proc.Id)) { continue }
            if (IsExcluded $proc.Name) { continue }
            $exePath = Get-ProcPath $proc
            if (IsGamePath $exePath) {
                if (ApplyGame $proc) {
                    $script:activeGames[$proc.Id] = $proc.Name
                    AddLog "[GAME] DETECTED: $($proc.Name) (PID $($proc.Id))"
                    AddLog "[GAME] High priority + affinity 0xFFF (cores 0-5)"
                    if (-not $script:currentGame) { Start-GameSession $proc.Name }
                }
            }
        }

        # Re-apply affinity to active games every loop (counters Process Lasso overrides)
        if ($script:pinningEnabled) {
            $gKeys = @($script:activeGames.Keys)
            foreach ($gid in $gKeys) {
                $gProc = Get-Process -Id $gid -EA SilentlyContinue
                if ($gProc -and $gProc.ProcessorAffinity.ToInt64() -ne $GAME_AFFINITY.ToInt64()) {
                    try {
                        $gProc.ProcessorAffinity = $GAME_AFFINITY
                        if (-not $script:plLoggedThisSession) {
                            AddLog "[GAME] Affinity restored (Process Lasso override detected)"
                            $script:plLoggedThisSession = $true
                        }
                    } catch {}
                }
            }
        }

        # Detect exited games
        $gKeys = @($script:activeGames.Keys)
        foreach ($gid in $gKeys) {
            if (-not (Get-Process -Id $gid -EA SilentlyContinue)) {
                AddLog "[ENDED] $($script:activeGames[$gid]) closed (PID $gid)"
                $script:activeGames.Remove($gid)
                if ($script:activeGames.Count -eq 0) { End-GameSession }
            }
        }

        # Pin new Firefox instances
        foreach ($proc in (Get-Process firefox -EA SilentlyContinue)) {
            if (-not $script:appliedFF.ContainsKey($proc.Id)) {
                ApplyFirefox $proc | Out-Null
                $script:appliedFF[$proc.Id] = $true
                AddLog "[FF] Pinned firefox PID $($proc.Id) to core 6"
            }
        }
        $fKeys = @($script:appliedFF.Keys)
        foreach ($fid in $fKeys) {
            if (-not (Get-Process -Id $fid -EA SilentlyContinue)) { $script:appliedFF.Remove($fid) }
        }

        # Re-throttle background every ~60s
        # FIX: only log when processes were actually found, to avoid noisy log spam
        if ($loopCount % 20 -eq 0) {
            $bgCount = ThrottleBg
            if ($bgCount -gt 0) { AddLog "[BG] Re-throttled $bgCount background process(es)" }
            $deadPids = @($script:pathFailCache | Where-Object { -not (Get-Process -Id $_ -EA SilentlyContinue) })
            foreach ($dp in $deadPids) { $script:pathFailCache.Remove($dp) | Out-Null }
        }

        # Clean up fire-and-forget startup jobs once they finish (no result needed)
        if ($jWarmup -and $jWarmup.State -in @('Completed','Failed','Stopped')) {
            Remove-Job $jWarmup -Force -EA SilentlyContinue; $jWarmup = $null
        }
        if ($script:jSysMain -and $script:jSysMain.State -in @('Completed','Failed','Stopped')) {
            Remove-Job $script:jSysMain -Force -EA SilentlyContinue; $script:jSysMain = $null
        }

        # Sample CPU / GPU / NIC in parallel thread jobs
        # FIX: NIC thread job now uses $using:NIC_NAME instead of hardcoded "Ethernet 2"
        $jCpu = $null; $jGpu = $null; $jNet = $null
        $cpuData = $null; $gpuResult = $null; $netResult = $null
        try {
            $jCpu = Start-ThreadJob { Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -EA SilentlyContinue | Where-Object Name -ne "_Total" | Sort-Object { [int]$_.Name } }
            $jGpu = Start-ThreadJob { try { $r = & nvidia-smi --query-gpu=temperature.gpu,utilization.gpu,utilization.memory,memory.used,memory.total,power.draw,power.limit,clocks.current.graphics,clocks.current.memory --format=csv,noheader,nounits 2>&1; $p = ($r -split ",") | ForEach-Object { $_.Trim() }; if ($p.Count -ge 9) { @{ Temp=[int]$p[0]; GpuUtil=[int]$p[1]; MemUtil=[int]$p[2]; MemUsedMB=[int]$p[3]; MemTotMB=[int]$p[4]; PowerW=[math]::Round([double]$p[5],1); PowerLimW=[math]::Round([double]$p[6],0); ClkMhz=[int]$p[7]; MemClkMhz=[int]$p[8] } } } catch { $null } }
            $jNet = Start-ThreadJob { param($nic) try { Get-NetAdapterStatistics -Name $nic -EA Stop } catch { $null } } -ArgumentList $NIC_NAME
            $jobs = @($jCpu, $jGpu, $jNet) | Where-Object { $_ -ne $null }
            $null = Wait-Job $jobs -Timeout 8
            if ($jCpu) { $cpuData   = Receive-Job $jCpu }
            if ($jGpu) { $gpuResult = Receive-Job $jGpu }
            if ($jNet) { $netResult = Receive-Job $jNet }
        } finally {
            if ($jCpu) { Remove-Job $jCpu -Force -EA SilentlyContinue }
            if ($jGpu) { Remove-Job $jGpu -Force -EA SilentlyContinue }
            if ($jNet) { Remove-Job $jNet -Force -EA SilentlyContinue }
        }

        $gamePct = Calc-ZonePct $cpuData 0xFFF
        $ffPct   = Calc-ZonePct $cpuData 0x3000
        $bgPct   = Calc-ZonePct $cpuData 0xC000
        $gpu     = $gpuResult

        if ($netResult -and $script:netPrevRx -gt 0) {
            $dt = ([DateTime]::UtcNow - $script:netPrevTime).TotalSeconds
            if ($dt -gt 0) {
                $rxKbps = [math]::Max(0, [math]::Round(($netResult.ReceivedBytes - $script:netPrevRx) / $dt / 1KB))
                $txKbps = [math]::Max(0, [math]::Round(($netResult.SentBytes    - $script:netPrevTx) / $dt / 1KB))
                $script:netRxHistory[$script:netHistIdx] = $rxKbps
                $script:netTxHistory[$script:netHistIdx] = $txKbps
                $script:netHistIdx = ($script:netHistIdx + 1) % $NET_GRAPH_W
            }
        }
        if ($netResult) {
            $script:netPrevRx   = $netResult.ReceivedBytes
            $script:netPrevTx   = $netResult.SentBytes
            $script:netPrevTime = [DateTime]::UtcNow
        }

        if ($script:currentGame) { Update-GameSession $gamePct $gpu }
        Update-Bottleneck $gamePct $gpu

        $prevCount = $script:alerts.Count
        Check-Alerts $gamePct $gpu
        if ($script:alerts.Count -gt 0 -and $script:alerts.Count -ne $prevCount) {
            foreach ($a in $script:alerts) { AddLog $a }
        }

        Update-Dashboard $gamePct $ffPct $bgPct $gpu
        Start-Sleep -Seconds 3
    }
} finally {
    End-GameSession
    Disable-GamingOptimizations

    # Restore all pinned/throttled processes
    foreach ($gid in $script:activeGames.Keys) {
        $p = Get-Process -Id $gid -EA SilentlyContinue
        if ($p) { try { $p.ProcessorAffinity = $ALL_CORES; $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal } catch {} }
    }
    foreach ($proc in (Get-Process firefox -EA SilentlyContinue)) {
        try { $proc.ProcessorAffinity = $ALL_CORES; $proc.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal } catch {}
    }
    foreach ($name in $BG_PROCESSES) {
        foreach ($p in (Get-Process -Name $name -EA SilentlyContinue)) {
            try { $p.ProcessorAffinity = $ALL_CORES; $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal } catch {}
        }
    }

    Get-Job | Where-Object { $_.Name -like 'Job*' -or $_.PSJobTypeName -eq 'ThreadJob' } | Remove-Job -Force -EA SilentlyContinue

    $reportFile = Save-SessionReport

    try { [Console]::TreatControlCAsInput = $false } catch {}
    [Console]::CursorVisible = $true
    [Console]::ResetColor()
    [Console]::Clear()
    $Host.UI.RawUI.WindowTitle = "PowerShell"
    Write-Host "Gaming Optimizer stopped."
    Write-Host "Session report saved to: $reportFile"
    Start-Sleep -Seconds 2
}
