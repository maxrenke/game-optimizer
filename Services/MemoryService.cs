using System.Runtime.InteropServices;

namespace GameOptimizer.Services;

/// <summary>
/// Flushes the Windows standby memory list - pages that are cached but not
/// actively used. Freeing them before a game launch gives the game more
/// readily-available physical memory (the same thing ISLC does).
/// </summary>
public static class MemoryService
{
    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? host, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref TokenPrivileges newState, int bufferLength, IntPtr prevState, IntPtr returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    // Matches the native TOKEN_PRIVILEGES with a single LUID_AND_ATTRIBUTES.
    // Pack=4 prevents the long from forcing 8-byte alignment.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    private const int SystemMemoryListInformation = 0x50;
    private const int MemoryPurgeStandbyList = 4;
    private const int SE_PRIVILEGE_ENABLED = 0x2;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;

    /// <summary>
    /// Purges the standby memory list. Returns the approximate number of MB
    /// freed (measured via available physical memory before/after), or -1 if
    /// the operation failed - typically because the process is not elevated.
    /// </summary>
    public static long FlushStandbyList()
    {
        try
        {
            if (!EnableProfilePrivilege()) return -1;

            var before = AvailablePhysicalBytes();
            int command = MemoryPurgeStandbyList;
            int status = NtSetSystemInformation(
                SystemMemoryListInformation, ref command, sizeof(int));
            if (status != 0) return -1;

            var after = AvailablePhysicalBytes();
            long freed = (long)after - (long)before;
            return freed > 0 ? freed / (1024 * 1024) : 0;
        }
        catch { return -1; }
    }

    // Purging the standby list requires SeProfileSingleProcessPrivilege,
    // which an elevated token holds but does not enable by default.
    private static bool EnableProfilePrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(),
                TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
                return false;
            var tp = new TokenPrivileges
            {
                Count = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED,
            };
            return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    private static ulong AvailablePhysicalBytes()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.ullAvailPhys : 0;
    }
}
