using System.Buffers.Binary;
using System.Net;

namespace Omawin.Windows;

// Which app is talking to the network (roadmap 8.20) and what listens where (Localhost, 8.17). The
// tables GetExtendedTcpTable / GetExtendedUdpTable fill (iphlpapi.h's MIB_*_OWNER_PID layouts), taken
// apart from their bytes. Pure: Sockets.cs fetches the bytes; test/windows.Tests builds tables by hand.
public sealed record Socket(string Protocol, IPAddress Local, int LocalPort, IPAddress? Remote, int RemotePort, string State, uint Pid)
{
    public bool Listening => State == "LISTEN" || Protocol == "UDP";
    public bool Established => State == "ESTABLISHED";
    // Talking to another machine: not to this PC itself, not to nowhere.
    public bool External => Established && Remote != null && !IPAddress.IsLoopback(Remote) && !Remote.Equals(IPAddress.Any) && !Remote.Equals(IPAddress.IPv6Any);
}

public static class SocketTable
{
    // MIB_TCP_STATE, 1-12.
    static readonly string[] States = ["", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"];

    static string State(uint s) => s < States.Length ? States[s] : "";

    // A port sits in the low 16 bits of a DWORD, in network byte order: its first two bytes in memory.
    static int Port(ReadOnlySpan<byte> at) => BinaryPrimitives.ReadUInt16BigEndian(at);

    static uint U32(ReadOnlySpan<byte> at) => BinaryPrimitives.ReadUInt32LittleEndian(at);

    static IEnumerable<(int Offset, int Index)> Rows(ReadOnlySpan<byte> table, int rowSize, int count)
    {
        // dwNumEntries, then the rows. A count larger than the bytes allow is cut to what's there.
        int fits = Math.Max(0, (table.Length - 4) / rowSize);
        var rows = new List<(int, int)>();
        for (int i = 0; i < Math.Min(count, fits); i++) rows.Add((4 + i * rowSize, i));
        return rows;
    }

    // MIB_TCPTABLE_OWNER_PID: rows of 24 bytes: state, local address, local port, remote address, remote port, pid.
    public static List<Socket> Tcp4(byte[] table)
    {
        var list = new List<Socket>();
        if (table.Length < 4) return list;
        foreach (var (o, _) in Rows(table, 24, (int)U32(table)))
        {
            var r = table.AsSpan(o, 24);
            list.Add(new Socket("TCP", new IPAddress(r.Slice(4, 4)), Port(r[8..]), new IPAddress(r.Slice(12, 4)), Port(r[16..]), State(U32(r)), U32(r[20..])));
        }
        return list;
    }

    // MIB_TCP6TABLE_OWNER_PID: rows of 56: local address[16], scope 16, port 20, remote address[16] 24,
    // scope 40, port 44, state 48, pid 52.
    public static List<Socket> Tcp6(byte[] table)
    {
        var list = new List<Socket>();
        if (table.Length < 4) return list;
        foreach (var (o, _) in Rows(table, 56, (int)U32(table)))
        {
            var r = table.AsSpan(o, 56);
            list.Add(new Socket("TCP", new IPAddress(r[..16], U32(r[16..])), Port(r[20..]), new IPAddress(r.Slice(24, 16), U32(r[40..])), Port(r[44..]), State(U32(r[48..])), U32(r[52..])));
        }
        return list;
    }

    // MIB_UDPTABLE_OWNER_PID: rows of 12: local address, local port, pid.
    public static List<Socket> Udp4(byte[] table)
    {
        var list = new List<Socket>();
        if (table.Length < 4) return list;
        foreach (var (o, _) in Rows(table, 12, (int)U32(table)))
        {
            var r = table.AsSpan(o, 12);
            list.Add(new Socket("UDP", new IPAddress(r[..4]), Port(r[4..]), null, 0, "", U32(r[8..])));
        }
        return list;
    }

    // MIB_UDP6TABLE_OWNER_PID: rows of 28: local address[16], scope 16, port 20, pid 24.
    public static List<Socket> Udp6(byte[] table)
    {
        var list = new List<Socket>();
        if (table.Length < 4) return list;
        foreach (var (o, _) in Rows(table, 28, (int)U32(table)))
        {
            var r = table.AsSpan(o, 28);
            list.Add(new Socket("UDP", new IPAddress(r[..16], U32(r[16..])), Port(r[20..]), null, 0, "", U32(r[24..])));
        }
        return list;
    }

    // "142.250.74.78:443", "[2606:4700::1]:443".
    public static string Endpoint(IPAddress? address, int port)
    {
        if (address == null) return "";
        var a = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{a}]:{port}" : $"{a}:{port}";
    }

    // The ports most connections go to, by name, so a row reads "github.com https".
    public static string Service(int port) => port switch
    {
        22 => "ssh", 53 => "dns", 80 => "http", 443 => "https", 993 => "imaps", 587 => "smtp", 3389 => "remote desktop",
        5228 => "Google push", 5223 => "Apple push", 1433 => "SQL Server", 5432 => "PostgreSQL", 3306 => "MySQL", 6379 => "Redis",
        _ => port.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
