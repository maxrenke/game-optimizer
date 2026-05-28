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

    // Game DVR / background capture - HKCU values that gate Xbox capture
    private const string DvrKey      = @"System\GameConfigStore";
    private const string DvrValue    = "GameDVR_Enabled";
    private const string CaptureKey  = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string CaptureVal  = "AppCaptureEnabled";

    private int? _originalPrio;
    private bool _sysMainStopped;

    private int? _origDvr;
    private int? _origCapture;
    private bool _gameDvrDisabled;

    public event Action<string>? LogEntry;

    public void EnableGamingOptimizations()
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

        // Always undo a Game DVR change on teardown/reset (no-op if not set)
        RestoreGameDvr();
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
