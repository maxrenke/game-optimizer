# =============================================================
#  gaming-optimizer.tests.ps1
#  Test framework for gaming-optimizer.ps1
#  Run: powershell -ExecutionPolicy Bypass -File gaming-optimizer.tests.ps1
# =============================================================
$SCRIPT_PATH = "$PSScriptRoot\gaming-optimizer.ps1"
# ── Test runner ───────────────────────────────────────────────
$script:passed  = 0
$script:failed  = 0
$script:results = [System.Collections.Generic.List[hashtable]]::new()
function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:passed++
        $script:results.Add(@{ Name=$Name; Status="PASS"; Error=$null })
    } catch {
        $script:failed++
        $script:results.Add(@{ Name=$Name; Status="FAIL"; Error=$_.Exception.Message })
    }
}
function Assert-Equal     { param($A,$E,[string]$M="") ; if ($A -ne $E)  { throw "Expected '$E' got '$A'$(if($M){" | $M"})" } }
function Assert-True      { param($C,[string]$M="Assert-True failed")    ; if (-not $C) { throw $M } }
function Assert-False     { param($C,[string]$M="Assert-False failed")   ; if ($C)      { throw $M } }
function Assert-Null      { param($V,[string]$M="Expected null")         ; if ($V -ne $null) { throw "$M (got '$V')" } }
function Assert-NotNull   { param($V,[string]$M="Expected non-null")     ; if ($V -eq $null) { throw $M } }
function Assert-Match     { param($S,$P,[string]$M="")                   ; if ($S -notmatch $P) { throw "'$S' !~ '$P'$(if($M){" | $M"})" } }
# ── Load script via AST (UTF-8 safe, no main-loop side effects) ──
Write-Host "Loading script via AST (UTF-8)..." -ForegroundColor Cyan
$rawUtf8 = [System.IO.File]::ReadAllText($SCRIPT_PATH, [System.Text.Encoding]::UTF8)
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($rawUtf8, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) {
    Write-Host "FATAL: Script has $($errors.Count) parse errors:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
# Invoke all top-level constant/variable assignments (stop before startup code)
$startupLine = 1
$_lines = $rawUtf8 -split "`r?`n"
for ($i = 0; $i -lt $_lines.Count; $i++) {
    if ($_lines[$i] -like '*# ---- STARTUP*') { $startupLine = $i + 1; break }
}
$topStmts = $ast.EndBlock.Statements | Where-Object {
    $_.Extent.StartLineNumber -lt $startupLine -and
    $_ -is [System.Management.Automation.Language.AssignmentStatementAst]
}
foreach ($stmt in $topStmts) {
    try { Invoke-Expression $stmt.Extent.Text } catch {}
}
# Invoke all function definitions
$fns = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)
foreach ($fn in $fns) {
    Invoke-Expression $fn.Extent.Text
}
Write-Host "Loaded $($fns.Count) functions, $($topStmts.Count) constants." -ForegroundColor Cyan
# Re-init mutable script-scope state (functions reset these per test)
function Reset-State {
    $script:log          = [System.Collections.Generic.List[string]]::new()
    $script:alerts       = [System.Collections.Generic.List[string]]::new()
    $script:activeGames  = @{}
    $script:appliedFF    = @{}
    $script:startTime    = Get-Date
    $script:gpuHighTicks = 0
    $script:cpuHighTicks = 0
    $script:plLoggedThisSession = $false
    $script:pinningEnabled      = $true
    $script:exitRequested       = $false
    $script:btCpuHistory = [int[]](0..4 | ForEach-Object { 0 })
    $script:btGpuHistory = [int[]](0..4 | ForEach-Object { 0 })
    $script:btIdx        = 0
    $script:bottleneck   = "none"
    $script:pathFailCache      = [System.Collections.Generic.HashSet[int]]::new()
    $script:sysmainStopped     = $false
    $script:sysmainOriginalStart = $null
    $script:netRxHistory = [int[]](0..29 | ForEach-Object { 0 })
    $script:netTxHistory = [int[]](0..29 | ForEach-Object { 0 })
    $script:netHistIdx   = 0
    $script:netPrevRx    = 0; $script:netPrevTx = 0
    $script:netPrevTime  = [DateTime]::UtcNow
    $script:sessionGames = [System.Collections.Generic.List[hashtable]]::new()
    $script:currentGame  = $null
    $script:PRIO_SEP_ORIGINAL = $null
}
Reset-State
Write-Host "Running tests...`n" -ForegroundColor Cyan
# =============================================================
#  1. IsExcluded
# =============================================================
Write-Host "── IsExcluded ──────────────────────────────────────────" -ForegroundColor DarkCyan
Test-Case "IsExcluded: 'steam' -> true"          { Assert-True  (IsExcluded "steam") }
Test-Case "IsExcluded: 'steamwebhelper' -> true" { Assert-True  (IsExcluded "steamwebhelper") }
Test-Case "IsExcluded: 'battle.net' -> true"     { Assert-True  (IsExcluded "battle.net") }
Test-Case "IsExcluded: 'easyanticheat' -> true"  { Assert-True  (IsExcluded "easyanticheat") }
Test-Case "IsExcluded: 'gogalaxy' -> true"       { Assert-True  (IsExcluded "gogalaxy") }
Test-Case "IsExcluded: 'mygame' -> false"        { Assert-False (IsExcluded "mygame") }
Test-Case "IsExcluded: case-insensitive 'Steam'" { Assert-True  (IsExcluded "Steam") }
Test-Case "IsExcluded: '.exe' suffix stripped"   { Assert-True  (IsExcluded "steam.exe") }
Test-Case "IsExcluded: 'nvidiashareoverlay' -> false (not in list)" { Assert-False (IsExcluded "nvidiashareoverlay") }
# =============================================================
#  2. IsGamePath
# =============================================================
Write-Host "`n── IsGamePath ──────────────────────────────────────────" -ForegroundColor DarkCyan
Test-Case "IsGamePath: Steam common path -> true" {
    Assert-True (IsGamePath "C:\Program Files (x86)\Steam\steamapps\common\SomeGame\game.exe")
}
Test-Case "IsGamePath: Epic Games path -> true" {
    Assert-True (IsGamePath "C:\Program Files\Epic Games\Fortnite\FortniteGame.exe")
}
Test-Case "IsGamePath: Hearthstone path -> true" {
    Assert-True (IsGamePath "C:\Program Files (x86)\Hearthstone\Hearthstone.exe")
}
Test-Case "IsGamePath: Diablo IV path -> true" {
    Assert-True (IsGamePath "C:\Program Files (x86)\Diablo IV\Diablo IV.exe")
}
Test-Case "IsGamePath: system path -> false" {
    Assert-False (IsGamePath "C:\Windows\System32\notepad.exe")
}
Test-Case "IsGamePath: null -> false"            { Assert-False (IsGamePath $null) }
Test-Case "IsGamePath: empty string -> false"    { Assert-False (IsGamePath "") }
Test-Case "IsGamePath: case-insensitive match"   {
    Assert-True (IsGamePath "c:\program files (x86)\steam\steamapps\common\game.exe")
}
Test-Case "IsGamePath: partial path not a StartsWith match -> false" {
    Assert-False (IsGamePath "D:\Backups\Steam_Backup\steamapps\common\game.exe")
}
# =============================================================
#  3. AddLog
# =============================================================
Write-Host "`n── AddLog ──────────────────────────────────────────────" -ForegroundColor DarkCyan
Test-Case "AddLog: entry added"              { Reset-State; AddLog "test"; Assert-Equal $script:log.Count 1 }
Test-Case "AddLog: timestamp prefix HH:mm:ss" {
    Reset-State; AddLog "hello"
    Assert-Match $script:log[0] '^\[\d{2}:\d{2}:\d{2}\]'
}
Test-Case "AddLog: message content preserved" {
    Reset-State; AddLog "my unique msg"
    Assert-True ($script:log[0] -like "*my unique msg*")
}
Test-Case "AddLog: capped at 10 entries" {
    Reset-State
    1..15 | ForEach-Object { AddLog "msg $_" }
    Assert-Equal $script:log.Count 10
}
Test-Case "AddLog: oldest entry removed (FIFO)" {
    Reset-State
    1..12 | ForEach-Object { AddLog "msg $_" }
    Assert-True ($script:log[0] -like "*msg 3*") "First entry should be msg 3"
}
# =============================================================
#  4. Calc-ZonePct
# =============================================================
Write-Host "`n── Calc-ZonePct ────────────────────────────────────────" -ForegroundColor DarkCyan
function New-CpuRows([int[]]$pcts) {
    $r = @(); for ($i=0;$i -lt $pcts.Count;$i++) {
        $r += [PSCustomObject]@{ Name="$i"; PercentProcessorTime=$pcts[$i] }
    }; return $r
}
Test-Case "Calc-ZonePct: game zone all zeros -> 0" {
    Assert-Equal (Calc-ZonePct (New-CpuRows (@(0)*16)) 0xFFF) 0
}
Test-Case "Calc-ZonePct: game zone all 100% -> 100" {
    Assert-Equal (Calc-ZonePct (New-CpuRows (@(100)*16)) 0xFFF) 100
}
Test-Case "Calc-ZonePct: game zone (0xFFF) avg of threads 0-11" {
    $pcts = (@(60)*12) + (@(0)*4)
    Assert-Equal (Calc-ZonePct (New-CpuRows $pcts) 0xFFF) 60
}
Test-Case "Calc-ZonePct: Firefox zone (0x3000) = threads 12-13 only" {
    $pcts = (@(0)*12) + @(80,80,0,0)
    Assert-Equal (Calc-ZonePct (New-CpuRows $pcts) 0x3000) 80
}
Test-Case "Calc-ZonePct: BG zone (0xC000) = threads 14-15 only" {
    $pcts = (@(0)*14) + @(40,40)
    Assert-Equal (Calc-ZonePct (New-CpuRows $pcts) 0xC000) 40
}
Test-Case "Calc-ZonePct: empty rows -> 0" {
    Assert-Equal (Calc-ZonePct @() 0xFFF) 0
}
Test-Case "Calc-ZonePct: zones independent — BG load doesn't bleed into game zone" {
    $pcts = (@(50)*12) + (@(100)*4)
    Assert-Equal (Calc-ZonePct (New-CpuRows $pcts) 0xFFF) 50
}
Test-Case "Calc-ZonePct: mixed values in game zone average correctly" {
    # threads 0-11: 0,10,20,30,40,50,60,70,80,90,100,0 => avg = 550/12 = 45.83 -> 46
    $pcts = @(0,10,20,30,40,50,60,70,80,90,100,0,0,0,0,0)
    $result = Calc-ZonePct (New-CpuRows $pcts) 0xFFF
    Assert-True ($result -ge 45 -and $result -le 47) "Expected ~46, got $result"
}
# =============================================================
#  5. Update-Bottleneck
# =============================================================
Write-Host "`n── Update-Bottleneck ───────────────────────────────────" -ForegroundColor DarkCyan
function Fill-Bottleneck([int]$cpu,[hashtable]$gpu,[int]$n=5) {
    1..$n | ForEach-Object { Update-Bottleneck $cpu $gpu }
}
Test-Case "Bottleneck: no active games -> 'none'" {
    Reset-State
    Fill-Bottleneck 90 @{ GpuUtil=95 }
    Assert-Equal $script:bottleneck "none"
}
Test-Case "Bottleneck: GPU avg >= 90% -> 'gpu'" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 50 @{ GpuUtil=92 }
    Assert-Equal $script:bottleneck "gpu"
}
Test-Case "Bottleneck: CPU >= 75% and GPU < 75% -> 'cpu'" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 80 @{ GpuUtil=60 }
    Assert-Equal $script:bottleneck "cpu"
}
Test-Case "Bottleneck: CPU ~70% and GPU ~70% -> 'balanced'" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 70 @{ GpuUtil=70 }
    Assert-Equal $script:bottleneck "balanced"
}
Test-Case "Bottleneck: both < 55% -> 'headroom'" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 30 @{ GpuUtil=30 }
    Assert-Equal $script:bottleneck "headroom"
}
Test-Case "Bottleneck: null GPU doesn't throw" {
    Reset-State; $script:activeGames[1]="Game"
    $threw=$false; try { Fill-Bottleneck 80 $null } catch { $threw=$true }
    Assert-False $threw "Update-Bottleneck threw with null GPU"
}
Test-Case "Bottleneck: rolling window smooths single spike (4 low + 1 high)" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 30 @{ GpuUtil=30 } 4
    Update-Bottleneck 95 @{ GpuUtil=95 }
    # avg GPU = (30*4+95)/5 = 43 -> headroom, not gpu
    Assert-Equal $script:bottleneck "headroom"
}
Test-Case "Bottleneck: CPU 75% exact boundary -> 'cpu' (not balanced)" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 75 @{ GpuUtil=50 }
    Assert-Equal $script:bottleneck "cpu"
}
Test-Case "Bottleneck: GPU 90% exact boundary -> 'gpu'" {
    Reset-State; $script:activeGames[1]="Game"
    Fill-Bottleneck 50 @{ GpuUtil=90 }
    Assert-Equal $script:bottleneck "gpu"
}
Test-Case "Bottleneck: btIdx wraps correctly after exactly history.Count fills" {
    # Validates the % $btCpuHistory.Count fix (was hardcoded % 5)
    Reset-State
    $histLen = $script:btCpuHistory.Count
    Fill-Bottleneck 50 @{ GpuUtil=50 } $histLen
    Assert-Equal $script:btIdx 0 "btIdx should wrap back to 0 after $histLen fills"
}
Test-Case "Bottleneck: btIdx stays in bounds over 3x history size" {
    Reset-State
    $histLen = $script:btCpuHistory.Count
    Fill-Bottleneck 50 @{ GpuUtil=50 } ($histLen * 3 + 2)
    Assert-True ($script:btIdx -ge 0 -and $script:btIdx -lt $histLen) "btIdx out of bounds: $($script:btIdx)"
}
Test-Case "Bottleneck: ThrottleBg returns an integer" {
    # ThrottleBg counts affected processes; result must always be a non-negative integer
    Reset-State
    $r = ThrottleBg
    Assert-True ($r -is [int]) "ThrottleBg should return [int], got $($r.GetType().Name)"
    Assert-True ($r -ge 0) "ThrottleBg should return >= 0, got $r"
}
# =============================================================
#  6. Check-Alerts
# =============================================================
Write-Host "`n── Check-Alerts ────────────────────────────────────────" -ForegroundColor DarkCyan
function Healthy-GPU { return @{ Temp=65; GpuUtil=70; MemUsedMB=6000; MemTotMB=12288 } }
Test-Case "Alerts: healthy values -> no alerts" {
    Reset-State; Check-Alerts 50 (Healthy-GPU)
    Assert-Equal $script:alerts.Count 0
}
Test-Case "Alerts: GPU temp >= 80 -> alert" {
    Reset-State
    Check-Alerts 50 @{ Temp=82; GpuUtil=60; MemUsedMB=6000; MemTotMB=12288 }
    Assert-True ($script:alerts | Where-Object { $_ -like "*GPU temp*" })
}
Test-Case "Alerts: GPU temp 79 (below threshold) -> no temp alert" {
    Reset-State
    Check-Alerts 50 @{ Temp=79; GpuUtil=60; MemUsedMB=6000; MemTotMB=12288 }
    Assert-False ($script:alerts | Where-Object { $_ -like "*GPU temp*" })
}
Test-Case "Alerts: VRAM >= 90% -> alert" {
    Reset-State
    Check-Alerts 50 @{ Temp=65; GpuUtil=70; MemUsedMB=11060; MemTotMB=12288 }
    Assert-True ($script:alerts | Where-Object { $_ -like "*VRAM*" })
}
Test-Case "Alerts: VRAM 89% -> no VRAM alert" {
    Reset-State
    Check-Alerts 50 @{ Temp=65; GpuUtil=70; MemUsedMB=10935; MemTotMB=12288 }
    Assert-False ($script:alerts | Where-Object { $_ -like "*VRAM*" })
}
Test-Case "Alerts: sustained GPU util >= 95% for 4 ticks -> alert" {
    Reset-State
    1..4 | ForEach-Object { Check-Alerts 50 @{ Temp=65; GpuUtil=96; MemUsedMB=6000; MemTotMB=12288 } }
    Assert-True ($script:alerts | Where-Object { $_ -like "*GPU util*sustained*" })
}
Test-Case "Alerts: GPU util resets when it drops below 95%" {
    Reset-State
    1..3 | ForEach-Object { Check-Alerts 50 @{ Temp=65; GpuUtil=96; MemUsedMB=6000; MemTotMB=12288 } }
    Check-Alerts 50 @{ Temp=65; GpuUtil=80; MemUsedMB=6000; MemTotMB=12288 }
    Assert-False ($script:alerts | Where-Object { $_ -like "*GPU util*sustained*" })
}
Test-Case "Alerts: sustained CPU >= 90% for 4 ticks -> alert" {
    Reset-State
    1..4 | ForEach-Object { Check-Alerts 92 (Healthy-GPU) }
    Assert-True ($script:alerts | Where-Object { $_ -like "*CPU*" })
}
Test-Case "Alerts: CPU resets when it drops below 90%" {
    Reset-State
    1..3 | ForEach-Object { Check-Alerts 92 (Healthy-GPU) }
    Check-Alerts 50 (Healthy-GPU)
    Assert-False ($script:alerts | Where-Object { $_ -like "*CPU*" })
}
Test-Case "Alerts: null GPU -> no throw" {
    Reset-State; $threw=$false
    try { Check-Alerts 50 $null } catch { $threw=$true }
    Assert-False $threw "Check-Alerts threw with null GPU"
}
# =============================================================
#  7. Session Tracking
# =============================================================
Write-Host "`n── Session Tracking ────────────────────────────────────" -ForegroundColor DarkCyan
function Mock-GPU { return @{ GpuUtil=70; Temp=65; MemUsedMB=6000; MemTotMB=12288 } }
Test-Case "Session: Start-GameSession initializes currentGame" {
    Reset-State; Start-GameSession "TestGame"
    Assert-NotNull $script:currentGame
    Assert-Equal $script:currentGame.Name "TestGame"
    Assert-Equal $script:currentGame.Samples 0
    Assert-Equal $script:currentGame.PeakCpu 0
}
Test-Case "Session: Update-GameSession increments sample count" {
    Reset-State; Start-GameSession "G"
    Update-GameSession 50 (Mock-GPU)
    Assert-Equal $script:currentGame.Samples 1
}
Test-Case "Session: tracks peak CPU" {
    Reset-State; Start-GameSession "G"
    Update-GameSession 40 (Mock-GPU); Update-GameSession 80 (Mock-GPU); Update-GameSession 60 (Mock-GPU)
    Assert-Equal $script:currentGame.PeakCpu 80
}
Test-Case "Session: tracks peak GPU util" {
    Reset-State; Start-GameSession "G"
    Update-GameSession 50 @{ GpuUtil=70; Temp=65; MemUsedMB=4000; MemTotMB=12288 }
    Update-GameSession 50 @{ GpuUtil=95; Temp=65; MemUsedMB=4000; MemTotMB=12288 }
    Update-GameSession 50 @{ GpuUtil=80; Temp=65; MemUsedMB=4000; MemTotMB=12288 }
    Assert-Equal $script:currentGame.PeakGpu 95
}
Test-Case "Session: tracks peak GPU temp" {
    Reset-State; Start-GameSession "G"
    Update-GameSession 50 @{ GpuUtil=70; Temp=72; MemUsedMB=4000; MemTotMB=12288 }
    Update-GameSession 50 @{ GpuUtil=70; Temp=81; MemUsedMB=4000; MemTotMB=12288 }
    Assert-Equal $script:currentGame.PeakTemp 81
}
Test-Case "Session: accumulates SumCpu correctly" {
    Reset-State; Start-GameSession "G"
    Update-GameSession 40 (Mock-GPU); Update-GameSession 60 (Mock-GPU)
    Assert-Equal $script:currentGame.SumCpu 100
}
Test-Case "Session: Update-GameSession no-op when currentGame is null" {
    Reset-State; $threw=$false
    try { Update-GameSession 50 (Mock-GPU) } catch { $threw=$true }
    Assert-False $threw
}
Test-Case "Session: End-GameSession moves game to sessionGames" {
    Reset-State; Start-GameSession "G"; End-GameSession
    Assert-Equal $script:sessionGames.Count 1
    Assert-Null $script:currentGame
}
Test-Case "Session: End-GameSession sets EndTime" {
    Reset-State; Start-GameSession "G"; End-GameSession
    Assert-NotNull $script:sessionGames[0].EndTime
}
Test-Case "Session: End-GameSession no-op when currentGame is null" {
    Reset-State; $threw=$false
    try { End-GameSession } catch { $threw=$true }
    Assert-False $threw
}
# =============================================================
#  8. Save-SessionReport
# =============================================================
Write-Host "`n── Save-SessionReport ──────────────────────────────────" -ForegroundColor DarkCyan
function With-TempReport([scriptblock]$Body) {
    $td = "$env:TEMP\go_tests_$(Get-Random)"
    Set-Variable -Name REPORT_DIR -Value $td -Scope Script
    try { & $Body $td }
    finally {
        Remove-Item $td -Recurse -Force -EA SilentlyContinue
        Set-Variable -Name REPORT_DIR -Value "$env:USERPROFILE\Documents\GamingOptimizer" -Scope Script
    }
}
function New-MockGame([string]$name="TestGame") {
    return @{
        Name=$name; StartTime=(Get-Date).AddMinutes(-30); EndTime=(Get-Date)
        PeakCpu=75; PeakGpu=88; PeakTemp=72; PeakVramPct=65
        Samples=100; SumCpu=6000; SumGpu=7500
        BtCpuTicks=10; BtGpuTicks=60; BtBalTicks=20; BtHrTicks=10
    }
}
Test-Case "Report: creates file in REPORT_DIR" {
    With-TempReport {
        Reset-State
        $f = Save-SessionReport
        Assert-True (Test-Path $f) "Report file not created"
    }
}
Test-Case "Report: no-game session says 'No games detected'" {
    With-TempReport {
        Reset-State
        $f = Save-SessionReport; $c = Get-Content $f -Raw
        Assert-True ($c -like "*No games detected*")
    }
}
Test-Case "Report: game name appears in report" {
    With-TempReport {
        Reset-State; $script:sessionGames.Add((New-MockGame "MyAwesomeGame"))
        $script:startTime = (Get-Date).AddMinutes(-35)
        $f = Save-SessionReport; $c = Get-Content $f -Raw
        Assert-True ($c -like "*MyAwesomeGame*")
    }
}
Test-Case "Report: peak CPU/GPU stats appear" {
    With-TempReport {
        Reset-State; $script:sessionGames.Add((New-MockGame "G"))
        $script:startTime = (Get-Date).AddMinutes(-35)
        $f = Save-SessionReport; $c = Get-Content $f -Raw
        Assert-True ($c -like "*75%*") "Peak CPU 75% not found in report"
    }
}
Test-Case "Report: bottleneck summary appears when ticks > 0" {
    With-TempReport {
        Reset-State; $script:sessionGames.Add((New-MockGame "G"))
        $script:startTime = (Get-Date).AddMinutes(-35)
        $f = Save-SessionReport; $c = Get-Content $f -Raw
        Assert-True ($c -like "*Bottleneck*")
    }
}
Test-Case "Report: prunes files older than 30 days" {
    With-TempReport { param($td)
        New-Item -ItemType Directory $td -Force | Out-Null
        $oldFile = "$td\session_old.txt"
        Set-Content $oldFile "old"; (Get-Item $oldFile).LastWriteTime = (Get-Date).AddDays(-31)
        Reset-State; Save-SessionReport | Out-Null
        Assert-False (Test-Path $oldFile) "Old report was not pruned"
    }
}
# =============================================================
#  9. Affinity Constants & Masks
# =============================================================
Write-Host "`n── Affinity Constants ──────────────────────────────────" -ForegroundColor DarkCyan
Test-Case "GAME_AFFINITY = 0xFFF (threads 0-11)"         { Assert-Equal ([int64]$GAME_AFFINITY)    0xFFF   }
Test-Case "FIREFOX_AFFINITY = 0x3000 (threads 12-13)"    { Assert-Equal ([int64]$FIREFOX_AFFINITY) 0x3000  }
Test-Case "BG_AFFINITY = 0xC000 (threads 14-15)"         { Assert-Equal ([int64]$BG_AFFINITY)      0xC000  }
Test-Case "GAME and FIREFOX masks don't overlap"          { Assert-Equal (([int64]$GAME_AFFINITY)    -band ([int64]$FIREFOX_AFFINITY)) 0 }
Test-Case "GAME and BG masks don't overlap"               { Assert-Equal (([int64]$GAME_AFFINITY)    -band ([int64]$BG_AFFINITY))      0 }
Test-Case "FIREFOX and BG masks don't overlap"            { Assert-Equal (([int64]$FIREFOX_AFFINITY) -band ([int64]$BG_AFFINITY))      0 }
Test-Case "All three zones cover exactly all 16 threads"  {
    $all = ([int64]$GAME_AFFINITY) -bor ([int64]$FIREFOX_AFFINITY) -bor ([int64]$BG_AFFINITY)
    Assert-Equal $all 0xFFFF
}
Test-Case "SUSTAINED_TICKS = 4" { Assert-Equal $SUSTAINED_TICKS 4 }
Test-Case "ALERT_GPU_TEMP = 80" { Assert-Equal $ALERT_GPU_TEMP 80 }
Test-Case "ALERT_VRAM_PCT = 90" { Assert-Equal $ALERT_VRAM_PCT 90 }
# =============================================================
#  10. Draw-TempColor
# =============================================================
Write-Host "`n── Draw-TempColor ──────────────────────────────────────" -ForegroundColor DarkCyan
Test-Case "TempColor: 65 -> Green"               { Assert-Equal (Draw-TempColor 65) ([ConsoleColor]::Green)  }
Test-Case "TempColor: 74 -> Yellow"              { Assert-Equal (Draw-TempColor 74) ([ConsoleColor]::Yellow) }
Test-Case "TempColor: 80 (= alert) -> Red"       { Assert-Equal (Draw-TempColor 80) ([ConsoleColor]::Red)    }
Test-Case "TempColor: 70 (boundary) -> Yellow"   { Assert-Equal (Draw-TempColor 70) ([ConsoleColor]::Yellow) }
Test-Case "TempColor: 69 (just below 70) -> Green" { Assert-Equal (Draw-TempColor 69) ([ConsoleColor]::Green) }
Test-Case "TempColor: 100 (extreme) -> Red"      { Assert-Equal (Draw-TempColor 100) ([ConsoleColor]::Red)   }
# =============================================================
#  RESULTS
# =============================================================
$total = $script:passed + $script:failed
Write-Host ""
Write-Host ("=" * 62) -ForegroundColor Cyan
Write-Host "  RESULTS" -ForegroundColor White
Write-Host ("=" * 62) -ForegroundColor Cyan
foreach ($r in $script:results) {
    if ($r.Status -eq "PASS") {
        Write-Host "  [PASS] $($r.Name)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $($r.Name)" -ForegroundColor Red
        Write-Host "         $($r.Error)" -ForegroundColor DarkRed
    }
}
Write-Host ("=" * 62) -ForegroundColor Cyan
$color = if ($script:failed -gt 0) { "Yellow" } else { "Green" }
Write-Host "  Total: $total  |  Passed: $script:passed  |  Failed: $script:failed" -ForegroundColor $color
Write-Host ("=" * 62) -ForegroundColor Cyan
exit $script:failed