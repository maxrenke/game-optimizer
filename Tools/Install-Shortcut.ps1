#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Registers an elevated scheduled task for Gaming Optimizer and creates a
    desktop shortcut that launches it WITHOUT a UAC prompt.

.DESCRIPTION
    Triggering an already-registered elevated scheduled task does not raise a
    UAC prompt, so the desktop shortcut runs `schtasks /run` against a task
    registered at RunLevel Highest. This skips the prompt for this one app
    only - UAC stays fully enabled system-wide.

    Run once from an elevated PowerShell. Re-run after switching build
    configurations or moving the repo: it auto-detects the newest
    GameOptimizer.exe under bin\.

.PARAMETER ExePath
    Explicit path to GameOptimizer.exe. When omitted, the newest build under
    <repo>\bin is used.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Install-Shortcut.ps1
#>
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$taskName = 'GameOptimizerElevated'

# Locate the executable: explicit -ExePath, else newest build under bin\
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

# Register (or update) the elevated, on-demand scheduled task
$action    = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory (Split-Path $ExePath)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
                                        -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
                                          -DontStopIfGoingOnBatteries `
                                          -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $taskName -Action $action `
                       -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Task       : $taskName (registered, RunLevel Highest)"

# Create / update the desktop shortcut that triggers the task
$icon = Join-Path $repoRoot 'Assets\AppIcon.ico'
$lnk  = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GameOptimizer.lnk'
$ws = New-Object -ComObject WScript.Shell
$s  = $ws.CreateShortcut($lnk)
$s.TargetPath       = "$env:SystemRoot\System32\schtasks.exe"
$s.Arguments        = "/run /tn `"$taskName`""
$s.WorkingDirectory = Split-Path $ExePath
$s.Description      = 'Gaming Optimizer (launches elevated, no UAC prompt)'
if (Test-Path $icon) { $s.IconLocation = $icon } else { $s.IconLocation = "$ExePath,0" }
$s.WindowStyle      = 7   # minimized - hides the brief schtasks console flash
$s.Save()
Write-Host "Shortcut   : $lnk"
Write-Host ""
Write-Host "Done. The desktop shortcut now launches Gaming Optimizer elevated with no UAC prompt."
