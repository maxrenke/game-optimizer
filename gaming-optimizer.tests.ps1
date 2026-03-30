# =============================================================
#  gaming-optimizer.tests.ps1
#  Pester 5 test suite for gaming-optimizer.ps1
#  Run: pwsh -ExecutionPolicy Bypass -File gaming-optimizer.tests.ps1
#       Invoke-Pester gaming-optimizer.tests.ps1 -PassThru
# =============================================================

# Allow running directly as a script (not just via Invoke-Pester)
if (-not (Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -ge '5.0' })) {
    Install-Module Pester -Force -Scope CurrentUser -MinimumVersion 5.0
}
Import-Module Pester -MinimumVersion 5.0 -Force

$config = New-PesterConfiguration
$config.Run.Path = $PSCommandPath
$config.Run.Exit = $true

# Only bootstrap when run as a script, not when already inside Pester
if ($MyInvocation.InvocationName -ne '.' -and $PSCmdlet -eq $null -and $null -eq (Get-Variable -Name 'Pester' -Scope Global -ErrorAction SilentlyContinue)) {
    $result = Invoke-Pester -Configuration $config -PassThru
    exit $result.FailedCount
}

# =============================================================
#  Test suite
# =============================================================

Describe "gaming-optimizer.ps1" {

    BeforeAll {
        # ── Load script via AST (UTF-8 safe, no main-loop side effects) ──
        $SCRIPT_PATH = "$PSScriptRoot\gaming-optimizer.ps1"
        Write-Host "Loading script via AST (UTF-8)..." -ForegroundColor Cyan
        $rawUtf8 = [System.IO.File]::ReadAllText($SCRIPT_PATH, [System.Text.Encoding]::UTF8)
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($rawUtf8, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) {
            Write-Host "FATAL: Script has $($errors.Count) parse errors:" -ForegroundColor Red
            $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            throw "Script parse errors — cannot continue"
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

        # ── Re-init mutable script-scope state ──────────────────────────
        function script:Reset-State {
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

        # ── Helper functions ─────────────────────────────────────────────
        function script:New-CpuRows([int[]]$pcts) {
            $r = @(); for ($i = 0; $i -lt $pcts.Count; $i++) {
                $r += [PSCustomObject]@{ Name = "$i"; PercentProcessorTime = $pcts[$i] }
            }; return $r
        }

        function script:Fill-Bottleneck([int]$cpu, [hashtable]$gpu, [int]$n = 5) {
            1..$n | ForEach-Object { Update-Bottleneck $cpu $gpu }
        }

        function script:Mock-GPU {
            return @{ GpuUtil = 70; Temp = 65; MemUsedMB = 6000; MemTotMB = 12288 }
        }

        function script:Healthy-GPU {
            return @{ Temp = 65; GpuUtil = 70; MemUsedMB = 6000; MemTotMB = 12288 }
        }

        function script:With-TempReport([scriptblock]$Body) {
            $td = "$env:TEMP\go_tests_$(Get-Random)"
            Set-Variable -Name REPORT_DIR -Value $td -Scope Script
            try { & $Body $td }
            finally {
                Remove-Item $td -Recurse -Force -EA SilentlyContinue
                Set-Variable -Name REPORT_DIR -Value "$env:USERPROFILE\Documents\GamingOptimizer" -Scope Script
            }
        }

        function script:New-MockGame([string]$name = "TestGame") {
            return @{
                Name = $name; StartTime = (Get-Date).AddMinutes(-30); EndTime = (Get-Date)
                PeakCpu = 75; PeakGpu = 88; PeakTemp = 72; PeakVramPct = 65
                Samples = 100; SumCpu = 6000; SumGpu = 7500
                BtCpuTicks = 10; BtGpuTicks = 60; BtBalTicks = 20; BtHrTicks = 10
            }
        }

        Reset-State
    }

    BeforeEach {
        Reset-State
    }

    # =============================================================
    #  1. IsExcluded
    # =============================================================
    Context "IsExcluded" {

        It "steam -> true" {
            (IsExcluded "steam") | Should -BeTrue
        }

        It "steamwebhelper -> true" {
            (IsExcluded "steamwebhelper") | Should -BeTrue
        }

        It "battle.net -> true" {
            (IsExcluded "battle.net") | Should -BeTrue
        }

        It "easyanticheat -> true" {
            (IsExcluded "easyanticheat") | Should -BeTrue
        }

        It "gogalaxy -> true" {
            (IsExcluded "gogalaxy") | Should -BeTrue
        }

        It "mygame -> false" {
            (IsExcluded "mygame") | Should -BeFalse
        }

        It "case-insensitive 'Steam'" {
            (IsExcluded "Steam") | Should -BeTrue
        }

        It ".exe suffix stripped" {
            (IsExcluded "steam.exe") | Should -BeTrue
        }

        It "nvidiashareoverlay -> false (not in list)" {
            (IsExcluded "nvidiashareoverlay") | Should -BeFalse
        }
    }

    # =============================================================
    #  2. IsGamePath
    # =============================================================
    Context "IsGamePath" {

        It "Steam common path -> true" {
            (IsGamePath "C:\Program Files (x86)\Steam\steamapps\common\SomeGame\game.exe") | Should -BeTrue
        }

        It "Epic Games path -> true" {
            (IsGamePath "C:\Program Files\Epic Games\Fortnite\FortniteGame.exe") | Should -BeTrue
        }

        It "Hearthstone path -> true" {
            (IsGamePath "C:\Program Files (x86)\Hearthstone\Hearthstone.exe") | Should -BeTrue
        }

        It "Diablo IV path -> true" {
            (IsGamePath "C:\Program Files (x86)\Diablo IV\Diablo IV.exe") | Should -BeTrue
        }

        It "system path -> false" {
            (IsGamePath "C:\Windows\System32\notepad.exe") | Should -BeFalse
        }

        It "null -> false" {
            (IsGamePath $null) | Should -BeFalse
        }

        It "empty string -> false" {
            (IsGamePath "") | Should -BeFalse
        }

        It "case-insensitive match" {
            (IsGamePath "c:\program files (x86)\steam\steamapps\common\game.exe") | Should -BeTrue
        }

        It "partial path not a StartsWith match -> false" {
            (IsGamePath "D:\Backups\Steam_Backup\steamapps\common\game.exe") | Should -BeFalse
        }
    }

    # =============================================================
    #  3. AddLog
    # =============================================================
    Context "AddLog" {

        It "entry added" {
            Reset-State; AddLog "test"
            $script:log.Count | Should -Be 1
        }

        It "timestamp prefix HH:mm:ss" {
            Reset-State; AddLog "hello"
            $script:log[0] | Should -Match '^\[\d{2}:\d{2}:\d{2}\]'
        }

        It "message content preserved" {
            Reset-State; AddLog "my unique msg"
            ($script:log[0] -like "*my unique msg*") | Should -BeTrue
        }

        It "capped at 10 entries" {
            Reset-State
            1..15 | ForEach-Object { AddLog "msg $_" }
            $script:log.Count | Should -Be 10
        }

        It "oldest entry removed (FIFO)" {
            Reset-State
            1..12 | ForEach-Object { AddLog "msg $_" }
            ($script:log[0] -like "*msg 3*") | Should -BeTrue
        }
    }

    # =============================================================
    #  4. Calc-ZonePct
    # =============================================================
    Context "Calc-ZonePct" {

        It "game zone all zeros -> 0" {
            (Calc-ZonePct (New-CpuRows (@(0) * 16)) 0xFFF) | Should -Be 0
        }

        It "game zone all 100% -> 100" {
            (Calc-ZonePct (New-CpuRows (@(100) * 16)) 0xFFF) | Should -Be 100
        }

        It "game zone (0xFFF) avg of threads 0-11" {
            $pcts = (@(60) * 12) + (@(0) * 4)
            (Calc-ZonePct (New-CpuRows $pcts) 0xFFF) | Should -Be 60
        }

        It "Firefox zone (0x3000) = threads 12-13 only" {
            $pcts = (@(0) * 12) + @(80, 80, 0, 0)
            (Calc-ZonePct (New-CpuRows $pcts) 0x3000) | Should -Be 80
        }

        It "BG zone (0xC000) = threads 14-15 only" {
            $pcts = (@(0) * 14) + @(40, 40)
            (Calc-ZonePct (New-CpuRows $pcts) 0xC000) | Should -Be 40
        }

        It "empty rows -> 0" {
            (Calc-ZonePct @() 0xFFF) | Should -Be 0
        }

        It "zones independent — BG load doesn't bleed into game zone" {
            $pcts = (@(50) * 12) + (@(100) * 4)
            (Calc-ZonePct (New-CpuRows $pcts) 0xFFF) | Should -Be 50
        }

        It "mixed values in game zone average correctly" {
            # threads 0-11: 0,10,20,30,40,50,60,70,80,90,100,0 => avg = 550/12 = 45.83 -> 46
            $pcts = @(0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 0, 0, 0, 0, 0)
            $result = Calc-ZonePct (New-CpuRows $pcts) 0xFFF
            $result | Should -BeGreaterOrEqual 45
            $result | Should -BeLessOrEqual 47
        }
    }

    # =============================================================
    #  5. Update-Bottleneck / ThrottleBg
    # =============================================================
    Context "Update-Bottleneck / ThrottleBg" {

        It "no active games -> 'none'" {
            Reset-State
            Fill-Bottleneck 90 @{ GpuUtil = 95 }
            $script:bottleneck | Should -Be "none"
        }

        It "GPU avg >= 90% -> 'gpu'" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 50 @{ GpuUtil = 92 }
            $script:bottleneck | Should -Be "gpu"
        }

        It "CPU >= 75% and GPU < 75% -> 'cpu'" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 80 @{ GpuUtil = 60 }
            $script:bottleneck | Should -Be "cpu"
        }

        It "CPU ~70% and GPU ~70% -> 'balanced'" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 70 @{ GpuUtil = 70 }
            $script:bottleneck | Should -Be "balanced"
        }

        It "both < 55% -> 'headroom'" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 30 @{ GpuUtil = 30 }
            $script:bottleneck | Should -Be "headroom"
        }

        It "null GPU doesn't throw" {
            Reset-State; $script:activeGames[1] = "Game"
            { Fill-Bottleneck 80 $null } | Should -Not -Throw
        }

        It "rolling window smooths single spike (4 low + 1 high)" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 30 @{ GpuUtil = 30 } 4
            Update-Bottleneck 95 @{ GpuUtil = 95 }
            # avg GPU = (30*4+95)/5 = 43 -> headroom, not gpu
            $script:bottleneck | Should -Be "headroom"
        }

        It "CPU 75% exact boundary -> 'cpu' (not balanced)" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 75 @{ GpuUtil = 50 }
            $script:bottleneck | Should -Be "cpu"
        }

        It "GPU 90% exact boundary -> 'gpu'" {
            Reset-State; $script:activeGames[1] = "Game"
            Fill-Bottleneck 50 @{ GpuUtil = 90 }
            $script:bottleneck | Should -Be "gpu"
        }

        It "btIdx wraps correctly after exactly history.Count fills" {
            Reset-State
            $histLen = $script:btCpuHistory.Count
            Fill-Bottleneck 50 @{ GpuUtil = 50 } $histLen
            $script:btIdx | Should -Be 0
        }

        It "btIdx stays in bounds over 3x history size" {
            Reset-State
            $histLen = $script:btCpuHistory.Count
            Fill-Bottleneck 50 @{ GpuUtil = 50 } ($histLen * 3 + 2)
            $script:btIdx | Should -BeGreaterOrEqual 0
            $script:btIdx | Should -BeLessOrEqual ($histLen - 1)
        }

        It "ThrottleBg returns an integer" {
            Reset-State
            $r = ThrottleBg
            ($r -is [int]) | Should -BeTrue
            $r | Should -BeGreaterOrEqual 0
        }
    }

    # =============================================================
    #  6. Check-Alerts
    # =============================================================
    Context "Check-Alerts" {

        It "healthy values -> no alerts" {
            Reset-State; Check-Alerts 50 (Healthy-GPU)
            $script:alerts.Count | Should -Be 0
        }

        It "GPU temp >= 80 -> alert" {
            Reset-State
            Check-Alerts 50 @{ Temp = 82; GpuUtil = 60; MemUsedMB = 6000; MemTotMB = 12288 }
            ($script:alerts | Where-Object { $_ -like "*GPU temp*" }) | Should -Not -BeNullOrEmpty
        }

        It "GPU temp 79 (below threshold) -> no temp alert" {
            Reset-State
            Check-Alerts 50 @{ Temp = 79; GpuUtil = 60; MemUsedMB = 6000; MemTotMB = 12288 }
            ($script:alerts | Where-Object { $_ -like "*GPU temp*" }) | Should -BeNullOrEmpty
        }

        It "VRAM >= 90% -> alert" {
            Reset-State
            Check-Alerts 50 @{ Temp = 65; GpuUtil = 70; MemUsedMB = 11060; MemTotMB = 12288 }
            ($script:alerts | Where-Object { $_ -like "*VRAM*" }) | Should -Not -BeNullOrEmpty
        }

        It "VRAM 89% -> no VRAM alert" {
            Reset-State
            Check-Alerts 50 @{ Temp = 65; GpuUtil = 70; MemUsedMB = 10935; MemTotMB = 12288 }
            ($script:alerts | Where-Object { $_ -like "*VRAM*" }) | Should -BeNullOrEmpty
        }

        It "sustained GPU util >= 95% for 4 ticks -> alert" {
            Reset-State
            1..4 | ForEach-Object { Check-Alerts 50 @{ Temp = 65; GpuUtil = 96; MemUsedMB = 6000; MemTotMB = 12288 } }
            ($script:alerts | Where-Object { $_ -like "*GPU util*sustained*" }) | Should -Not -BeNullOrEmpty
        }

        It "GPU util resets when it drops below 95%" {
            Reset-State
            1..3 | ForEach-Object { Check-Alerts 50 @{ Temp = 65; GpuUtil = 96; MemUsedMB = 6000; MemTotMB = 12288 } }
            Check-Alerts 50 @{ Temp = 65; GpuUtil = 80; MemUsedMB = 6000; MemTotMB = 12288 }
            ($script:alerts | Where-Object { $_ -like "*GPU util*sustained*" }) | Should -BeNullOrEmpty
        }

        It "sustained CPU >= 90% for 4 ticks -> alert" {
            Reset-State
            1..4 | ForEach-Object { Check-Alerts 92 (Healthy-GPU) }
            ($script:alerts | Where-Object { $_ -like "*CPU*" }) | Should -Not -BeNullOrEmpty
        }

        It "CPU resets when it drops below 90%" {
            Reset-State
            1..3 | ForEach-Object { Check-Alerts 92 (Healthy-GPU) }
            Check-Alerts 50 (Healthy-GPU)
            ($script:alerts | Where-Object { $_ -like "*CPU*" }) | Should -BeNullOrEmpty
        }

        It "null GPU -> no throw" {
            Reset-State
            { Check-Alerts 50 $null } | Should -Not -Throw
        }
    }

    # =============================================================
    #  7. Session Tracking
    # =============================================================
    Context "Session Tracking" {

        It "Start-GameSession initializes currentGame" {
            Reset-State; Start-GameSession "TestGame"
            $script:currentGame | Should -Not -BeNullOrEmpty
            $script:currentGame.Name | Should -Be "TestGame"
            $script:currentGame.Samples | Should -Be 0
            $script:currentGame.PeakCpu | Should -Be 0
        }

        It "Update-GameSession increments sample count" {
            Reset-State; Start-GameSession "G"
            Update-GameSession 50 (Mock-GPU)
            $script:currentGame.Samples | Should -Be 1
        }

        It "tracks peak CPU" {
            Reset-State; Start-GameSession "G"
            Update-GameSession 40 (Mock-GPU); Update-GameSession 80 (Mock-GPU); Update-GameSession 60 (Mock-GPU)
            $script:currentGame.PeakCpu | Should -Be 80
        }

        It "tracks peak GPU util" {
            Reset-State; Start-GameSession "G"
            Update-GameSession 50 @{ GpuUtil = 70; Temp = 65; MemUsedMB = 4000; MemTotMB = 12288 }
            Update-GameSession 50 @{ GpuUtil = 95; Temp = 65; MemUsedMB = 4000; MemTotMB = 12288 }
            Update-GameSession 50 @{ GpuUtil = 80; Temp = 65; MemUsedMB = 4000; MemTotMB = 12288 }
            $script:currentGame.PeakGpu | Should -Be 95
        }

        It "tracks peak GPU temp" {
            Reset-State; Start-GameSession "G"
            Update-GameSession 50 @{ GpuUtil = 70; Temp = 72; MemUsedMB = 4000; MemTotMB = 12288 }
            Update-GameSession 50 @{ GpuUtil = 70; Temp = 81; MemUsedMB = 4000; MemTotMB = 12288 }
            $script:currentGame.PeakTemp | Should -Be 81
        }

        It "accumulates SumCpu correctly" {
            Reset-State; Start-GameSession "G"
            Update-GameSession 40 (Mock-GPU); Update-GameSession 60 (Mock-GPU)
            $script:currentGame.SumCpu | Should -Be 100
        }

        It "Update-GameSession no-op when currentGame is null" {
            Reset-State
            { Update-GameSession 50 (Mock-GPU) } | Should -Not -Throw
        }

        It "End-GameSession moves game to sessionGames" {
            Reset-State; Start-GameSession "G"; End-GameSession
            $script:sessionGames.Count | Should -Be 1
            $script:currentGame | Should -BeNullOrEmpty
        }

        It "End-GameSession sets EndTime" {
            Reset-State; Start-GameSession "G"; End-GameSession
            $script:sessionGames[0].EndTime | Should -Not -BeNullOrEmpty
        }

        It "End-GameSession no-op when currentGame is null" {
            Reset-State
            { End-GameSession } | Should -Not -Throw
        }
    }

    # =============================================================
    #  8. Save-SessionReport
    # =============================================================
    Context "Save-SessionReport" {

        It "creates file in REPORT_DIR" {
            With-TempReport {
                Reset-State
                $f = Save-SessionReport
                (Test-Path $f) | Should -BeTrue
            }
        }

        It "no-game session says 'No games detected'" {
            With-TempReport {
                Reset-State
                $f = Save-SessionReport; $c = Get-Content $f -Raw
                ($c -like "*No games detected*") | Should -BeTrue
            }
        }

        It "game name appears in report" {
            With-TempReport {
                Reset-State; $script:sessionGames.Add((New-MockGame "MyAwesomeGame"))
                $script:startTime = (Get-Date).AddMinutes(-35)
                $f = Save-SessionReport; $c = Get-Content $f -Raw
                ($c -like "*MyAwesomeGame*") | Should -BeTrue
            }
        }

        It "peak CPU/GPU stats appear" {
            With-TempReport {
                Reset-State; $script:sessionGames.Add((New-MockGame "G"))
                $script:startTime = (Get-Date).AddMinutes(-35)
                $f = Save-SessionReport; $c = Get-Content $f -Raw
                ($c -like "*75%*") | Should -BeTrue
            }
        }

        It "bottleneck summary appears when ticks > 0" {
            With-TempReport {
                Reset-State; $script:sessionGames.Add((New-MockGame "G"))
                $script:startTime = (Get-Date).AddMinutes(-35)
                $f = Save-SessionReport; $c = Get-Content $f -Raw
                ($c -like "*Bottleneck*") | Should -BeTrue
            }
        }

        It "prunes files older than 30 days" {
            With-TempReport { param($td)
                New-Item -ItemType Directory $td -Force | Out-Null
                $oldFile = "$td\session_old.txt"
                Set-Content $oldFile "old"; (Get-Item $oldFile).LastWriteTime = (Get-Date).AddDays(-31)
                Reset-State; Save-SessionReport | Out-Null
                (Test-Path $oldFile) | Should -BeFalse
            }
        }
    }

    # =============================================================
    #  9. Affinity Constants & Masks
    # =============================================================
    Context "Affinity Constants" {

        It "GAME_AFFINITY = 0xFFF (threads 0-11)" {
            ([int64]$GAME_AFFINITY) | Should -Be 0xFFF
        }

        It "FIREFOX_AFFINITY = 0x3000 (threads 12-13)" {
            ([int64]$FIREFOX_AFFINITY) | Should -Be 0x3000
        }

        It "BG_AFFINITY = 0xC000 (threads 14-15)" {
            ([int64]$BG_AFFINITY) | Should -Be 0xC000
        }

        It "GAME and FIREFOX masks don't overlap" {
            (([int64]$GAME_AFFINITY) -band ([int64]$FIREFOX_AFFINITY)) | Should -Be 0
        }

        It "GAME and BG masks don't overlap" {
            (([int64]$GAME_AFFINITY) -band ([int64]$BG_AFFINITY)) | Should -Be 0
        }

        It "FIREFOX and BG masks don't overlap" {
            (([int64]$FIREFOX_AFFINITY) -band ([int64]$BG_AFFINITY)) | Should -Be 0
        }

        It "All three zones cover exactly all 16 threads" {
            $all = ([int64]$GAME_AFFINITY) -bor ([int64]$FIREFOX_AFFINITY) -bor ([int64]$BG_AFFINITY)
            $all | Should -Be 0xFFFF
        }

        It "SUSTAINED_TICKS = 4" {
            $SUSTAINED_TICKS | Should -Be 4
        }

        It "ALERT_GPU_TEMP = 80" {
            $ALERT_GPU_TEMP | Should -Be 80
        }

        It "ALERT_VRAM_PCT = 90" {
            $ALERT_VRAM_PCT | Should -Be 90
        }
    }

    # =============================================================
    #  10. Draw-TempColor
    # =============================================================
    Context "Draw-TempColor" {

        It "65 -> Green" {
            (Draw-TempColor 65) | Should -Be ([ConsoleColor]::Green)
        }

        It "74 -> Yellow" {
            (Draw-TempColor 74) | Should -Be ([ConsoleColor]::Yellow)
        }

        It "80 (= alert) -> Red" {
            (Draw-TempColor 80) | Should -Be ([ConsoleColor]::Red)
        }

        It "70 (boundary) -> Yellow" {
            (Draw-TempColor 70) | Should -Be ([ConsoleColor]::Yellow)
        }

        It "69 (just below 70) -> Green" {
            (Draw-TempColor 69) | Should -Be ([ConsoleColor]::Green)
        }

        It "100 (extreme) -> Red" {
            (Draw-TempColor 100) | Should -Be ([ConsoleColor]::Red)
        }
    }
}
