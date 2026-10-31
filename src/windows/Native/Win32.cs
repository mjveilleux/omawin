using System.Runtime.InteropServices;

namespace Omawin.Windows;

// The few kernel32 calls several fixes share (plans/07-fixing-windows.md §0.2): a process's exe and
// its start time. A pid alone is reused once its process ends; a pid with its start time names one
// process for good, so nothing here ever acts on a process that took over an old pid.
public static partial class Win32
{
    public const int ERROR_ACCESS_DENIED = 5, ERROR_INSUFFICIENT_BUFFER = 122, ERROR_MORE_DATA = 234, ERROR_NO_MORE_ITEMS = 259;
    public const uint PROCESS_TERMINATE = 0x0001, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(IntPtr process, uint exitCode);

    // The exe a process runs, or null when it can't be opened (protected processes, some services).
    public static unsafe string? ExePath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(h, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally { CloseHandle(h); }
    }

    // When the process started (a FILETIME), 0 when it can't be opened or is gone.
    public static long StartTime(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try { return GetProcessTimes(h, out var created, out _, out _, out _) ? created : 0; }
        finally { CloseHandle(h); }
    }
}
