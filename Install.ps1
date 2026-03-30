# Install.ps1 — Gaming Optimizer installer
# Run once as Administrator from the repo directory:
#   pwsh -ExecutionPolicy Bypass -File Install.ps1
#
# What it does:
#   1. Copies the script to a stable location in AppData
#   2. Creates a desktop shortcut that launches as Administrator
#   3. Registers a scheduled task to run -Mode cleanup on logon
#      (recovers system state if the optimizer ever crashes)
#   4. Optionally creates a shortcut for tray mode
#
# To uninstall:  pwsh -ExecutionPolicy Bypass -File Install.ps1 -Uninstall

param([switch]$Uninstall)

$ErrorActionPreference = "Stop"

$INSTALL_DIR  = "$env:LOCALAPPDATA\GamingOptimizer"
$SCRIPT_NAME  = "gaming-optimizer.ps1"
$SHORTCUT_TUI = "$env:USERPROFILE\Desktop\Gaming Optimizer.lnk"
$SHORTCUT_TRAY= "$env:USERPROFILE\Desktop\Gaming Optimizer (Tray).lnk"
$TASK_NAME    = "GamingOptimizerCleanup"

function Write-Step([string]$msg) { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Err([string]$msg)  { Write-Host "  [!!] $msg" -ForegroundColor Red }

# ── Uninstall ────────────────────────────────────────────────────────────────
if ($Uninstall) {
    Write-Host "`nUninstalling Gaming Optimizer..." -ForegroundColor Yellow
    try { Unregister-ScheduledTask -TaskName $TASK_NAME -Confirm:$false -EA SilentlyContinue; Write-OK "Scheduled task removed" } catch {}
    foreach ($lnk in @($SHORTCUT_TUI, $SHORTCUT_TRAY)) {
        if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-OK "Removed shortcut: $(Split-Path $lnk -Leaf)" }
    }
    if (Test-Path $INSTALL_DIR) { Remove-Item $INSTALL_DIR -Recurse -Force; Write-OK "Removed $INSTALL_DIR" }
    Write-Host "`nUninstall complete." -ForegroundColor Green
    exit
}

# ── Require admin ────────────────────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Err "This installer requires Administrator. Right-click the terminal and choose 'Run as Administrator'."
    exit 1
}

Write-Host "`n  Gaming Optimizer — Installer" -ForegroundColor Cyan
Write-Host "  Install directory: $INSTALL_DIR`n" -ForegroundColor DarkGray

$srcDir = $PSScriptRoot

# ── 1. Copy files ────────────────────────────────────────────────────────────
Write-Step "Copying files to $INSTALL_DIR..."
$null = New-Item -ItemType Directory -Path $INSTALL_DIR -Force
$filesToCopy = @($SCRIPT_NAME, "config.psd1.example")
# If the user has a config.psd1, copy that too (but don't overwrite an existing one)
if (Test-Path "$srcDir\config.psd1") { $filesToCopy += "config.psd1" }
foreach ($f in $filesToCopy) {
    $src = "$srcDir\$f"; $dst = "$INSTALL_DIR\$f"
    if ($f -eq "config.psd1" -and (Test-Path $dst)) {
        Write-OK "config.psd1 already present — skipping (keeping existing)"
    } else {
        Copy-Item $src $dst -Force -EA SilentlyContinue
        Write-OK "Copied $f"
    }
}

# ── 2. Desktop shortcut — TUI mode ───────────────────────────────────────────
Write-Step "Creating TUI desktop shortcut..."
try {
    $wsh  = New-Object -ComObject WScript.Shell
    $link = $wsh.CreateShortcut($SHORTCUT_TUI)
    $link.TargetPath       = "pwsh.exe"
    $link.Arguments        = "-ExecutionPolicy Bypass -File `"$INSTALL_DIR\$SCRIPT_NAME`""
    $link.WorkingDirectory = $INSTALL_DIR
    $link.Description      = "Gaming Optimizer — TUI dashboard"
    $link.IconLocation     = "shell32.dll,15"   # game controller icon
    $link.Save()
    # Embed runas verb via COM shortcut extended data (forces Run as Administrator)
    $bytes = [System.IO.File]::ReadAllBytes($SHORTCUT_TUI)
    $bytes[0x15] = $bytes[0x15] -bor 0x20   # set bit 5 of flags = RunAsAdministrator
    [System.IO.File]::WriteAllBytes($SHORTCUT_TUI, $bytes)
    Write-OK "Shortcut: $(Split-Path $SHORTCUT_TUI -Leaf)"
} catch { Write-Err "Shortcut failed: $_" }

# ── 3. Desktop shortcut — Tray mode ─────────────────────────────────────────
Write-Step "Creating tray mode desktop shortcut..."
try {
    $wsh  = New-Object -ComObject WScript.Shell
    $link = $wsh.CreateShortcut($SHORTCUT_TRAY)
    $link.TargetPath       = "pwsh.exe"
    $link.Arguments        = "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$INSTALL_DIR\$SCRIPT_NAME`" -Mode tray"
    $link.WorkingDirectory = $INSTALL_DIR
    $link.Description      = "Gaming Optimizer — background tray mode (toast alerts only)"
    $link.IconLocation     = "shell32.dll,15"
    $link.Save()
    $bytes = [System.IO.File]::ReadAllBytes($SHORTCUT_TRAY)
    $bytes[0x15] = $bytes[0x15] -bor 0x20
    [System.IO.File]::WriteAllBytes($SHORTCUT_TRAY, $bytes)
    Write-OK "Shortcut: $(Split-Path $SHORTCUT_TRAY -Leaf)"
} catch { Write-Err "Shortcut failed: $_" }

# ── 4. Scheduled task — cleanup on logon ────────────────────────────────────
Write-Step "Registering cleanup scheduled task ($TASK_NAME)..."
try {
    $action  = New-ScheduledTaskAction -Execute "pwsh.exe" `
        -Argument "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$INSTALL_DIR\$SCRIPT_NAME`" -Mode cleanup"
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings= New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest
    Register-ScheduledTask -TaskName $TASK_NAME -Action $action -Trigger $trigger `
        -Settings $settings -Principal $principal -Force | Out-Null
    Write-OK "Scheduled task registered (runs -Mode cleanup at each logon)"
} catch { Write-Err "Scheduled task failed: $_" }

# ── Done ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  Installation complete." -ForegroundColor Green
Write-Host ""
Write-Host "  TUI mode:   double-click 'Gaming Optimizer' on the desktop" -ForegroundColor White
Write-Host "  Tray mode:  double-click 'Gaming Optimizer (Tray)' — runs hidden, alerts via toast" -ForegroundColor White
Write-Host "  Configure:  pwsh -File `"$INSTALL_DIR\$SCRIPT_NAME`" -Mode configure" -ForegroundColor White
Write-Host "  Cleanup:    pwsh -File `"$INSTALL_DIR\$SCRIPT_NAME`" -Mode cleanup" -ForegroundColor White
Write-Host "  Uninstall:  pwsh -File `"$srcDir\Install.ps1`" -Uninstall" -ForegroundColor White
Write-Host ""
