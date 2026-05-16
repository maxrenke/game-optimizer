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

    private int? _originalPrio;
    private ServiceStartMode? _sysMainOriginalStart;
    private bool _sysMainStopped;

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
        catch { LogEntry?.Invoke("[SYS] PrioritySep change failed (needs admin)"); }

        timeBeginPeriod(1);
        LogEntry?.Invoke("[SYS] Timer resolution set to 1ms");

        // Suspend SysMain (Superfetch) - useless on NVMe
        try
        {
            using var svc = new ServiceController("SysMain");
            if (svc.StartType != ServiceStartMode.Disabled && svc.Status == ServiceControllerStatus.Running)
            {
                _sysMainOriginalStart = svc.StartType;
                _sysMainStopped = true;
                Task.Run(() => { try { svc.Stop(); } catch { } });
                LogEntry?.Invoke($"[SYS] SysMain suspended (was {_sysMainOriginalStart}, NVMe - no benefit)");
            }
            else if (svc.StartType == ServiceStartMode.Disabled)
            {
                LogEntry?.Invoke("[SYS] SysMain already disabled - skipping");
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
                var startType = _sysMainOriginalStart ?? ServiceStartMode.Automatic;
                using var svc = new ServiceController("SysMain");
                ServiceHelper.ChangeStartMode(svc, startType);
                svc.Start();
                LogEntry?.Invoke($"[SYS] SysMain restarted (StartType restored to {startType})");
            }
            catch { }
        }
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
                log("[INIT] Stale PrioritySep=26 detected (prev crash?) - reset to 2");
            }
        }
        catch { }
    }
}

// Helper to change service start mode (ServiceController doesn't expose this directly)
internal static class ServiceHelper
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig(IntPtr hService, uint nServiceType, uint nStartType,
        uint nErrorControl, string? lpBinaryPathName, string? lpLoadOrderGroup, IntPtr lpdwTagId,
        string? lpDependencies, string? lpServiceStartName, string? lpPassword, string? lpDisplayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    public static void ChangeStartMode(ServiceController svc, ServiceStartMode mode)
    {
        try
        {
            // Use ServiceController's handle via reflection (simplest portable approach)
            var field = typeof(ServiceController).GetField("serviceHandle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var handle = (SafeHandle?)field?.GetValue(svc);
            if (handle is not null)
            {
                ChangeServiceConfig(handle.DangerousGetHandle(),
                    0xFFFFFFFF, (uint)mode, 0xFFFFFFFF,
                    null, null, IntPtr.Zero, null, null, null, null);
            }
        }
        catch
        {
            // Fall back: use sc.exe
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config SysMain start= {mode switch
                {
                    ServiceStartMode.Automatic => "auto",
                    ServiceStartMode.Manual => "demand",
                    ServiceStartMode.Disabled => "disabled",
                    _ => "auto"
                }}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }
}
