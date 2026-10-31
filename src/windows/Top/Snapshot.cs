using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Every process at one moment (8.17). One NtQuerySystemInformation(SystemProcessInformation) call
// gives them all with their private working set (Task Manager's Memory column) and CPU times, no
// handle per process, protected ones included. Its layout is only partly documented (winternl.h);
// the offsets below are the 64-bit ones every process tool uses. Anything unexpected (a 32-bit
// process, a call that fails) falls back to .NET's documented Process list, which reports commit
// instead of the private working set.
public static unsafe partial class Snapshot
{
    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returned);

    const int SystemProcessInformation = 5;
    const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    public static List<Proc> Take()
    {
        if (IntPtr.Size == 8)
            try { return Native(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException) { }
        return Managed();
    }

    // SYSTEM_PROCESS_INFORMATION (64-bit): NextEntryOffset 0, WorkingSetPrivateSize 8, CreateTime 32,
    // UserTime 40, KernelTime 48, ImageName (UNICODE_STRING: Length 56, Buffer 64), UniqueProcessId 80,
    // InheritedFromUniqueProcessId 88, SessionId 100.
    static List<Proc> Native()
    {
        int size = 1 << 20;
        for (int tries = 0; tries < 8; tries++)
        {
            var buffer = new byte[size];
            fixed (byte* b = buffer)
            {
                int status = NtQuerySystemInformation(SystemProcessInformation, b, size, out int needed);
                if (status == STATUS_INFO_LENGTH_MISMATCH) { size = Math.Max(needed, size) + (1 << 16); continue; }
                if (status != 0) throw new System.ComponentModel.Win32Exception(status);
                var list = new List<Proc>(512);
                byte* end = b + size;
                for (byte* p = b; p + 104 <= end;)
                {
                    ushort nameBytes = *(ushort*)(p + 56);
                    char* name = *(char**)(p + 64);
                    list.Add(new Proc(
                        (uint)*(nint*)(p + 80), (uint)*(nint*)(p + 88),
                        name == null ? (*(nint*)(p + 80) == 0 ? "System Idle" : "System") : new string(name, 0, nameBytes / 2),
                        *(long*)(p + 32), *(long*)(p + 40) + *(long*)(p + 48), *(long*)(p + 8), *(uint*)(p + 100)));
                    uint next = *(uint*)p;
                    if (next == 0) break;
                    p += next;
                }
                return list;
            }
        }
        throw new System.ComponentModel.Win32Exception(STATUS_INFO_LENGTH_MISMATCH);
    }

    // The documented route: slower (a handle per process) and blind to protected processes' CPU.
    static List<Proc> Managed()
    {
        var list = new List<Proc>();
        foreach (var p in Process.GetProcesses())
            using (p)
            {
                long created = 0, cpu = 0;
                try { created = p.StartTime.ToFileTimeUtc(); cpu = p.TotalProcessorTime.Ticks; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                long memory = 0;
                try { memory = p.PrivateMemorySize64; } catch (InvalidOperationException) { }
                list.Add(new Proc((uint)p.Id, 0, p.ProcessName + (p.Id > 4 ? ".exe" : ""), created, cpu, memory, (uint)p.SessionId));
            }
        return list;
    }
}
