using System.Runtime.InteropServices;

namespace GameOptimizer.Services;

/// <summary>
/// Low-level control of other processes: suspend/resume and disk I/O priority.
/// Used to keep background apps (cloud-sync clients, etc.) from competing with
/// a running game for the CPU and the disk queue.
/// </summary>
public static class ProcessControl
{
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(
        IntPtr processHandle, int infoClass, ref int info, int infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint PROCESS_SUSPEND_RESUME = 0x0800;
    private const uint PROCESS_SET_INFORMATION = 0x0200;

    private const int ProcessIoPriority = 33;   // PROCESSINFOCLASS.ProcessIoPriority
    private const int IoPriorityVeryLow = 0;    // IO_PRIORITY_HINT
    private const int IoPriorityNormal = 2;

    /// <summary>Freezes every thread of the process. Reverse with <see cref="Resume"/>.</summary>
    public static bool Suspend(int pid)
    {
        var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return NtSuspendProcess(h) == 0; }
        catch { return false; }
        finally { CloseHandle(h); }
    }

    /// <summary>Resumes a process previously frozen by <see cref="Suspend"/>.</summary>
    public static bool Resume(int pid)
    {
        var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return NtResumeProcess(h) == 0; }
        catch { return false; }
        finally { CloseHandle(h); }
    }

    /// <summary>Drops the process to the lowest disk I/O priority.</summary>
    public static bool SetIoPriorityVeryLow(int pid) => SetIoPriority(pid, IoPriorityVeryLow);

    /// <summary>Restores the process to normal disk I/O priority.</summary>
    public static bool SetIoPriorityNormal(int pid) => SetIoPriority(pid, IoPriorityNormal);

    private static bool SetIoPriority(int pid, int hint)
    {
        var h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return NtSetInformationProcess(h, ProcessIoPriority, ref hint, sizeof(int)) == 0; }
        catch { return false; }
        finally { CloseHandle(h); }
    }
}
