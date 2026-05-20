#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates all Gaming Optimizer desktop shortcuts - reproducible setup.

.DESCRIPTION
    Creates / refreshes three desktop shortcuts:

      GameOptimizer     - launches the app elevated with no UAC prompt, via a
                          RunLevel-Highest scheduled task it also registers.
      Affinity Watcher  - runs Tools\Watch-Affinity.ps1 (elevated).
      Reset Optimizer   - runs Tools\Reset-Optimizer.ps1 (elevated, stays open).

    The two tool shortcuts launch powershell.exe, so they open in whatever
    you have configured as the default terminal (the "Default terminal
    application" setting). Set that to Windows Terminal / Windows Terminal
    Preview if you want them to open there.

    Run once from an elevated PowerShell. Re-run after moving the repo or
    switching build configuration - it auto-detects the newest build.

.PARAMETER ExePath
    Explicit path to GameOptimizer.exe. When omitted, the newest build under
    <repo>\bin is used.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Install-Shortcuts.ps1
#>
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$taskName = 'GameOptimizerElevated'
$desktop  = [Environment]::GetFolderPath('Desktop')
$icon     = Join-Path $repoRoot 'Assets\AppIcon.ico'
$psExe    = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$ws       = New-Object -ComObject WScript.Shell

# Marks a .lnk to always run as administrator (extra-data byte 0x15, bit 0x20).
function Set-RunAsAdmin([string]$lnkPath) {
    $bytes = [System.IO.File]::ReadAllBytes($lnkPath)
    $bytes[0x15] = $bytes[0x15] -bor 0x20
    [System.IO.File]::WriteAllBytes($lnkPath, $bytes)
}

# Desktop shortcut that runs a PowerShell script elevated, in the default
# terminal. -NoExit (KeepOpen) leaves the window up after the script ends.
function New-ScriptShortcut([string]$name, [string]$script, [string]$desc, [switch]$KeepOpen) {
    $lnk = Join-Path $desktop "$name.lnk"
    $noexit = if ($KeepOpen) { '-NoExit ' } else { '' }
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath       = $psExe
    $s.Arguments        = "-NoProfile -ExecutionPolicy Bypass ${noexit}-File `"$script`""
    $s.WorkingDirectory = Split-Path $script
    $s.Description      = $desc
    if (Test-Path $icon) { $s.IconLocation = $icon }
    $s.Save()
    Set-RunAsAdmin $lnk
    Write-Host "Shortcut   : $lnk"
}

# ── 1. GameOptimizer app - elevated via scheduled task, no UAC prompt ───────
if (-not $ExePath) {
    $candidate = Get-ChildItem -Path (Join-Path $repoRoot 'bin') -Recurse `
            -Filter 'GameOptimizer.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\AppX\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $candidate) {
        throw "GameOptimizer.exe not found under $repoRoot\bin - build the project first, or pass -ExePath."
    }
    $ExePath = $candidate.FullName
}
if (-not (Test-Path $ExePath)) { throw "Executable not found: $ExePath" }
Write-Host "Executable : $ExePath"

$action    = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory (Split-Path $ExePath)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
                                        -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
                                          -DontStopIfGoingOnBatteries `
                                          -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $taskName -Action $action `
                       -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Task       : $taskName (registered, RunLevel Highest)"

$lnk = Join-Path $desktop 'GameOptimizer.lnk'
$s = $ws.CreateShortcut($lnk)
$s.TargetPath       = "$env:SystemRoot\System32\schtasks.exe"
$s.Arguments        = "/run /tn `"$taskName`""
$s.WorkingDirectory = Split-Path $ExePath
$s.Description      = 'Gaming Optimizer (launches elevated, no UAC prompt)'
if (Test-Path $icon) { $s.IconLocation = $icon } else { $s.IconLocation = "$ExePath,0" }
$s.WindowStyle      = 7   # minimized - hides the brief schtasks console flash
$s.Save()
Write-Host "Shortcut   : $lnk"

# ── 2 & 3. Diagnostic tool shortcuts ────────────────────────────────────────
New-ScriptShortcut 'Affinity Watcher' (Join-Path $repoRoot 'Tools\Watch-Affinity.ps1') `
    'Live CPU affinity / priority watcher (Gaming Optimizer debug tool)'
New-ScriptShortcut 'Reset Optimizer' (Join-Path $repoRoot 'Tools\Reset-Optimizer.ps1') `
    'Reset all Gaming Optimizer changes to Windows defaults' -KeepOpen

Write-Host ''
Write-Host 'Done. Three desktop shortcuts created/updated.'
Write-Host 'The tool shortcuts open in your default terminal application.'
