#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Standalone "Reset All" - undoes every change Gaming Optimizer makes.

.DESCRIPTION
    A test/recovery helper that resets process and system state to Windows
    defaults without needing the app (use it if the app crashed or you want
    a clean slate between test runs). It performs the same restore the app's
    in-app "Reset All Process State" button does:

      1. Win32PrioritySeparation registry value -> 2 (Windows default)
      2. SysMain (SuperFetch) service -> Automatic, started
      3. Every process -> all-cores CPU affinity
      4. Every process at BelowNormal/Idle priority -> Normal
      5. Known background/cloud-sync apps -> resumed (if frozen) and
         restored to Normal disk I/O priority

    High priority is intentionally NOT reset: many system processes
    (dwm, csrss, audiodg) legitimately run High, and a game left at High
    after a crash is harmless and clears when the game exits.

.NOTES
    If Gaming Optimizer is running with CPU pinning ON it will re-apply its
    changes within a second. Turn pinning off (or close the app) first.
#>

$ErrorActionPreference = 'Continue'

Add-Type -Namespace GO -Name Nt -MemberDefinition @'
[DllImport("ntdll.dll")]  public static extern int NtResumeProcess(IntPtr h);
[DllImport("ntdll.dll")]  public static extern int NtSetInformationProcess(IntPtr h, int cls, ref int info, int len);
[DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
'@

function Resume-Process([int]$id) {
    $h = [GO.Nt]::OpenProcess(0x0800, $false, $id)   # PROCESS_SUSPEND_RESUME
    if ($h -eq [IntPtr]::Zero) { return $false }
    try { return [GO.Nt]::NtResumeProcess($h) -eq 0 }
    finally { [void][GO.Nt]::CloseHandle($h) }
}

function Reset-IoPriority([int]$id) {
    $h = [GO.Nt]::OpenProcess(0x0200, $false, $id)   # PROCESS_SET_INFORMATION
    if ($h -eq [IntPtr]::Zero) { return }
    $normal = 2                                      # IoPriorityNormal
    try { [void][GO.Nt]::NtSetInformationProcess($h, 33, [ref]$normal, 4) }  # ProcessIoPriority
    finally { [void][GO.Nt]::CloseHandle($h) }
}

$coreCount = [Environment]::ProcessorCount
$allMask   = ([long]1 -shl $coreCount) - 1

Write-Host ''
Write-Host '  Gaming Optimizer - Reset All' -ForegroundColor White
Write-Host '  Restoring process and system state to Windows defaults' -ForegroundColor DarkGray
Write-Host ''

# ── 1. Win32PrioritySeparation ──────────────────────────────────────────────
try {
    Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' `
        -Name 'Win32PrioritySeparation' -Value 2 -Type DWord
    Write-Host '  [OK]   Win32PrioritySeparation reset to 2' -ForegroundColor Green
}
catch { Write-Host "  [FAIL] Win32PrioritySeparation: $($_.Exception.Message)" -ForegroundColor Red }

# ── 2. SysMain service ──────────────────────────────────────────────────────
try {
    Set-Service -Name 'SysMain' -StartupType Automatic
    Start-Service -Name 'SysMain' -ErrorAction SilentlyContinue
    Write-Host '  [OK]   SysMain service set to Automatic and started' -ForegroundColor Green
}
catch { Write-Host "  [FAIL] SysMain: $($_.Exception.Message)" -ForegroundColor Red }

# ── 3 & 4. Affinity + priority across all processes ─────────────────────────
$affReset = 0; $prioReset = 0
foreach ($p in Get-Process) {
    try {
        if ($p.ProcessorAffinity.ToInt64() -ne $allMask) {
            $p.ProcessorAffinity = [IntPtr]$allMask
            $affReset++
        }
    }
    catch { }
    try {
        $pc = $p.PriorityClass
        if ($pc -eq [System.Diagnostics.ProcessPriorityClass]::BelowNormal -or
            $pc -eq [System.Diagnostics.ProcessPriorityClass]::Idle) {
            $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal
            $prioReset++
        }
    }
    catch { }
}
Write-Host "  [OK]   Affinity reset to all $coreCount cores on $affReset process(es)" -ForegroundColor Green
Write-Host "  [OK]   Priority reset to Normal on $prioReset process(es)" -ForegroundColor Green

# ── 5. Resume + I/O priority for known background / cloud-sync apps ─────────
$bgNames = @(
    'onedrive', 'icloudckks', 'iclouddrive', 'icloudservices', 'icloudhome',
    'phoneexperiencehost', 'crossdeviceservice', 'malwarebytes', 'mbamservice',
    'hearthstonedecktracker', 'backgroundtaskhost', 'windowspackagemanagerserver',
    'hwinfo64', 'nahimicsvc32', 'nahimicsvc64', 'unigetui', 'appcontrol',
    'dropbox', 'googledrivefs'
)
$cfgPath = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GamingOptimizer\config.json'
if (Test-Path $cfgPath) {
    try {
        $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
        if ($cfg.ExtraThrottledProcs) { $bgNames += $cfg.ExtraThrottledProcs }
        if ($cfg.SuspendDuringGame)   { $bgNames += $cfg.SuspendDuringGame.ProcessName }
    }
    catch { }
}
$bgNames = $bgNames | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique

$resumed = 0; $ioReset = 0
foreach ($p in Get-Process) {
    if ($bgNames -notcontains $p.ProcessName.ToLowerInvariant()) { continue }
    if (Resume-Process $p.Id) { $resumed++ }
    Reset-IoPriority $p.Id
    $ioReset++
}
Write-Host "  [OK]   Resumed $resumed suspended app(s); reset I/O priority on $ioReset" -ForegroundColor Green

Write-Host ''
Write-Host '  Reset complete.' -ForegroundColor White
Write-Host '  If Gaming Optimizer is running with CPU pinning ON, turn it off' -ForegroundColor DarkGray
Write-Host '  or close the app - otherwise it will re-apply its changes.' -ForegroundColor DarkGray
Write-Host ''
