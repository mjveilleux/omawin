using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Every TCP connection and UDP socket on the PC with the process that owns it (iphlpapi.h,
// documented, no admin). SocketTable takes the bytes apart.
public static unsafe partial class Sockets
{
    const int AF_INET = 2, AF_INET6 = 23;
    const int TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1;

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(void* table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sorted, int af, int tableClass, uint reserved);
    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedUdpTable(void* table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sorted, int af, int tableClass, uint reserved);

    // The table's bytes. The first call says how big it is; connections open between the two calls,
    // so it's asked again until it fits.
    static byte[] Table(bool tcp, int af)
    {
        int size = 0;
        for (int tries = 0; tries < 10; tries++)
        {
            var buffer = new byte[Math.Max(size, 4)];
            uint r;
            fixed (byte* b = buffer)
            {
                int asked = buffer.Length;
                r = tcp ? GetExtendedTcpTable(b, ref asked, false, af, TCP_TABLE_OWNER_PID_ALL, 0)
                        : GetExtendedUdpTable(b, ref asked, false, af, UDP_TABLE_OWNER_PID, 0);
                size = asked + 1024;
            }
            if (r == 0) return buffer;
            if (r != Win32.ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception((int)r);
        }
        throw new Win32Exception(Win32.ERROR_INSUFFICIENT_BUFFER);
    }

    public static List<Socket> All()
    {
        var list = new List<Socket>();
        list.AddRange(SocketTable.Tcp4(Table(true, AF_INET)));
        list.AddRange(SocketTable.Tcp6(Table(true, AF_INET6)));
        list.AddRange(SocketTable.Udp4(Table(false, AF_INET)));
        list.AddRange(SocketTable.Udp6(Table(false, AF_INET6)));
        return list;
    }
}
