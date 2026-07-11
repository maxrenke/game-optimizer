using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace GameOptimizer.Services;

public class SystemService
{
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    private const string PrioKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string PrioValue = "Win32PrioritySeparation";
    private const int GamingPrio = 26;

    // Since Windows 10 2004, timeBeginPeriod only affects the calling process.
    // This key restores legacy global behavior so a 1ms timer also reaches games
    // that do not request it themselves. Read at boot - a reboot is required.
    private const string GlobalTimerKey   = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string GlobalTimerValue = "GlobalTimerResolutionRequests";

    // Game DVR / background capture - HKCU values that gate Xbox capture
    private const string DvrKey      = @"System\GameConfigStore";
    private const string DvrValue    = "GameDVR_Enabled";
    private const string CaptureKey  = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string CaptureVal  = "AppCaptureEnabled";

    private int? _originalPrio;
    private bool _sysMainStopped;
    private readonly List<string> _stoppedServices = new();

    private int? _origGlobalTimer;
    private bool _globalTimerSet;

    private int? _origDvr;
    private int? _origCapture;
    private bool _gameDvrDisabled;

    public event Action<string>? LogEntry;

    public void EnableGamingOptimizations(IEnumerable<string>? stopServices = null)
    {
        // Win32PrioritySeparation -> short fixed quanta, no foreground boost
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PrioKey, writable: true);
            if (key is not null)
            {
                _originalPrio = (int?)key.GetValue(PrioValue);
                key.SetValue(PrioValue, GamingPrio, RegistryValueKind.DWord);
                LogEntry?.Invoke($"[SYS] PrioritySeparation: {_originalPrio} -> 26 (fixed quanta)");
            }
        }
        catch { LogEntry?.Invoke("[SYS] PrioritySeparation change failed (needs admin)"); }

        timeBeginPeriod(1);
        LogEntry?.Invoke("[SYS] Timer resolution set to 1ms");
        EnsureGlobalTimerResolution();

        // Suspend SysMain (Superfetch) - useless on NVMe
        try
        {
            using var svc = new ServiceController("SysMain");
            if (svc.StartType != ServiceStartMode.Disabled && svc.Status == ServiceControllerStatus.Running)
            {
                _sysMainStopped = true;
                Task.Run(() => { try { svc.Stop(); } catch { } });
                LogEntry?.Invoke($"[SYS] SysMain suspended (was {svc.StartType})");
            }
            else if (svc.StartType == ServiceStartMode.Disabled)
            {
                LogEntry?.Invoke("[SYS] SysMain already disabled");
            }
        }
        catch { }

        // Stop the configured background services (StartType left untouched; they
        // are restarted on teardown). Same stop-only, fully reversible model as
        // SysMain above - never disabled.
        if (stopServices is not null)
        {
            foreach (var name in stopServices)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                try
                {
                    using var svc = new ServiceController(name.Trim());
                    if (svc.StartType != ServiceStartMode.Disabled &&
                        svc.Status == ServiceControllerStatus.Running)
                    {
                        _stoppedServices.Add(svc.ServiceName);
                        Task.Run(() => { try { svc.Stop(); } catch { } });
                        LogEntry?.Invoke($"[SYS] {svc.ServiceName} stopped (was {svc.StartType})");
                    }
                }
                catch { } // unknown/inaccessible service - skip
            }
        }
    }

    public void DisableGamingOptimizations()
    {
        if (_originalPrio.HasValue)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PrioKey, writable: true);
                key?.SetValue(PrioValue, _originalPrio.Value, RegistryValueKind.DWord);
                LogEntry?.Invoke($"[SYS] PrioritySeparation restored to {_originalPrio}");
            }
            catch { }
        }

        timeEndPeriod(1);

        if (_sysMainStopped)
        {
            try
            {
                // We only stopped the service; its StartType was never changed,
                // so restarting it is all that's needed to undo our change.
                using var svc = new ServiceController("SysMain");
                svc.Start();
                LogEntry?.Invoke("[SYS] SysMain restarted");
            }
            catch { }
        }

        // Restart every service we stopped this session (idempotent if already running)
        foreach (var name in _stoppedServices)
        {
            try
            {
                using var svc = new ServiceController(name);
                if (svc.Status != ServiceControllerStatus.Running) svc.Start();
                LogEntry?.Invoke($"[SYS] {name} restarted");
            }
            catch { }
        }
        _stoppedServices.Clear();

        // Always undo a Game DVR change on teardown/reset (no-op if not set)
        RestoreGameDvr();
    }

    /// <summary>
    /// Sets <c>GlobalTimerResolutionRequests=1</c> so a 1ms timer reaches games
    /// that do not call <c>timeBeginPeriod</c> themselves. The key is read at
    /// boot, so this is a persistent tweak that takes effect on the next reboot
    /// and is deliberately NOT reverted on normal teardown (reverting same-boot
    /// would cancel the benefit before it ever applies). Use
    /// <see cref="RestoreGlobalTimer"/> for an explicit reset. Needs admin.
    /// </summary>
    private void EnsureGlobalTimerResolution()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GlobalTimerKey, writable: true);
            if (key is null) return;
            var cur = (int?)key.GetValue(GlobalTimerValue);
            if (cur == 1)
            {
                LogEntry?.Invoke("[SYS] Global timer resolution already enabled");
                return;
            }
            _origGlobalTimer = cur;
            key.SetValue(GlobalTimerValue, 1, RegistryValueKind.DWord);
            _globalTimerSet = true;
            LogEntry?.Invoke("[SYS] Global timer resolution enabled (reboot required to take effect)");
        }
        catch { LogEntry?.Invoke("[SYS] Global timer resolution change failed (needs admin)"); }
    }

    /// <summary>
    /// Restores <c>GlobalTimerResolutionRequests</c> to the value saved by
    /// <see cref="EnsureGlobalTimerResolution"/>. Only meaningful within the
    /// session that set it; the standalone Reset-Optimizer.ps1 covers the
    /// cross-session case. A reboot is required for the change to take effect.
    /// </summary>
    public void RestoreGlobalTimer()
    {
        if (!_globalTimerSet) return;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GlobalTimerKey, writable: true);
            if (key is null) return;
            if (_origGlobalTimer.HasValue) key.SetValue(GlobalTimerValue, _origGlobalTimer.Value, RegistryValueKind.DWord);
            else key.DeleteValue(GlobalTimerValue, throwOnMissingValue: false);
            _globalTimerSet = false;
            LogEntry?.Invoke("[SYS] Global timer resolution restored (reboot required)");
        }
        catch { }
    }

    /// <summary>
    /// Disables Windows Game DVR and Xbox background capture - their
    /// always-on recording costs CPU/GPU during gameplay. Originals are saved
    /// for <see cref="RestoreGameDvr"/>. Idempotent.
    /// </summary>
    public void DisableGameDvr()
    {
        if (_gameDvrDisabled) return;
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(DvrKey))
            {
                _origDvr = (int?)k?.GetValue(DvrValue);
                k?.SetValue(DvrValue, 0, RegistryValueKind.DWord);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(CaptureKey))
            {
                _origCapture = (int?)k?.GetValue(CaptureVal);
                k?.SetValue(CaptureVal, 0, RegistryValueKind.DWord);
            }
            _gameDvrDisabled = true;
            LogEntry?.Invoke("[SYS] Game DVR / background capture disabled");
        }
        catch { LogEntry?.Invoke("[SYS] Game DVR disable failed"); }
    }

    /// <summary>
    /// Restores Game DVR / capture to the values saved by
    /// <see cref="DisableGameDvr"/>. Idempotent - a no-op if never disabled.
    /// </summary>
    public void RestoreGameDvr()
    {
        if (!_gameDvrDisabled) return;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(DvrKey, writable: true))
            {
                if (k is not null)
                {
                    if (_origDvr.HasValue) k.SetValue(DvrValue, _origDvr.Value, RegistryValueKind.DWord);
                    else k.DeleteValue(DvrValue, throwOnMissingValue: false);
                }
            }
            using (var k = Registry.CurrentUser.OpenSubKey(CaptureKey, writable: true))
            {
                if (k is not null)
                {
                    if (_origCapture.HasValue) k.SetValue(CaptureVal, _origCapture.Value, RegistryValueKind.DWord);
                    else k.DeleteValue(CaptureVal, throwOnMissingValue: false);
                }
            }
            _gameDvrDisabled = false;
            LogEntry?.Invoke("[SYS] Game DVR / background capture restored");
        }
        catch { }
    }

    // Check if PrioritySep is stuck at 26 from a crash and reset it
    public static void CheckStalePriority(Action<string> log)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PrioKey, writable: true);
            if (key?.GetValue(PrioValue) is int cur && cur == GamingPrio)
            {
                key.SetValue(PrioValue, 2, RegistryValueKind.DWord);
                log("[INIT] Stale PrioritySeparation=26 detected (prev crash?) - reset to 2");
            }
        }
        catch { }
    }
}
