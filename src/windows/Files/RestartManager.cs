using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Which processes hold a file (roadmap 8.1), from Windows' Restart Manager (restartmanager.h,
// rstrtmgr.dll): documented, no admin, and it reports processes of every session (names for other
// accounts' processes; ending those needs admin). It sees open handles and loaded modules, not a
// process's current folder (Holders.NoneFound says so).
public static unsafe partial class RestartManager
{
    const int CCH_RM_SESSION_KEY = 32, CCH_RM_MAX_APP_NAME = 255, CCH_RM_MAX_SVC_NAME = 63;

    // FILETIME as two uints keeps the struct 4-aligned, as the header lays it out: 12 bytes.
    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS { public uint dwProcessId; public uint StartLow, StartHigh; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO // 668 bytes
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char strAppName[CCH_RM_MAX_APP_NAME + 1];
        public fixed char strServiceShortName[CCH_RM_MAX_SVC_NAME + 1];
        public int ApplicationType;   // RM_APP_TYPE
        public uint AppStatus;
        public uint TSSessionId;
        public int bRestartable;      // BOOL
    }

    [LibraryImport("rstrtmgr.dll")] private static partial int RmStartSession(out uint session, uint flags, char* sessionKey);
    [LibraryImport("rstrtmgr.dll")] private static partial int RmEndSession(uint session);
    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmRegisterResources(uint session, uint nFiles, IntPtr* files, uint nApplications, RM_UNIQUE_PROCESS* applications, uint nServices, IntPtr* services);
    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmGetList(uint session, out uint needed, ref uint count, RM_PROCESS_INFO* info, out uint rebootReasons);

    // Every process holding any of these files, or a file under these folders (the first maxFiles:
    // a folder of a million files would take minutes, and the holder is almost always among the first).
    public static List<Holder> Who(IEnumerable<string> paths, int maxFiles = 5000)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
                }).Take(maxFiles - files.Count));
            else files.Add(p);
            if (files.Count >= maxFiles) break;
        }
        if (files.Count == 0) return [];

        char* key = stackalloc char[CCH_RM_SESSION_KEY + 1];
        int err = RmStartSession(out uint session, 0, key);
        if (err != 0) throw new Win32Exception(err);
        var strings = new IntPtr[files.Count];
        try
        {
            for (int i = 0; i < files.Count; i++) strings[i] = Marshal.StringToHGlobalUni(files[i]);
            // In chunks of 500: one call with thousands of names works, but chunks keep each short.
            for (int at = 0; at < strings.Length; at += 500)
                fixed (IntPtr* p = &strings[at])
                {
                    err = RmRegisterResources(session, (uint)Math.Min(500, strings.Length - at), p, 0, null, 0, null);
                    if (err != 0) throw new Win32Exception(err);
                }

            uint count = 0, needed;
            var info = Array.Empty<RM_PROCESS_INFO>();
            while (true)
            {
                fixed (RM_PROCESS_INFO* pi = info)
                    err = RmGetList(session, out needed, ref count, pi, out _);
                if (err == 0) break;
                if (err != Win32.ERROR_MORE_DATA) throw new Win32Exception(err);
                info = new RM_PROCESS_INFO[needed + 4]; // more may open between the two calls
                count = (uint)info.Length;
            }

            var result = new List<Holder>((int)count);
            for (int i = 0; i < count; i++)
            {
                ref var r = ref info[i];
                fixed (char* name = r.strAppName)
                fixed (char* svc = r.strServiceShortName)
                {
                    long started = ((long)r.Process.StartHigh << 32) | r.Process.StartLow;
                    var service = new string(svc);
                    var kind = Enum.IsDefined((HolderKind)r.ApplicationType) ? (HolderKind)r.ApplicationType : HolderKind.Unknown;
                    result.Add(new Holder(r.Process.dwProcessId, started, new string(name),
                        service.Length > 0 ? service : null, kind, Win32.ExePath(r.Process.dwProcessId)));
                }
            }
            return Holders.Merge(result);
        }
        finally
        {
            foreach (var s in strings) if (s != IntPtr.Zero) Marshal.FreeHGlobal(s);
            RmEndSession(session);
        }
    }
}
