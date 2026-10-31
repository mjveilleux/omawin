using System.Buffers.Binary;
using System.Net;

namespace Omawin.Windows.Tests;

// The socket tables, built byte for byte as iphlpapi lays them out.
public class NetTests
{
    // A port as Windows stores it: network order in the low 16 bits of a DWORD.
    static void Port(Span<byte> at, int port) { at.Clear(); BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)port); }

    static byte[] Tcp4(params (byte[] Local, int LocalPort, byte[] Remote, int RemotePort, uint State, uint Pid)[] rows)
    {
        var t = new byte[4 + rows.Length * 24];
        BinaryPrimitives.WriteUInt32LittleEndian(t, (uint)rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            var r = t.AsSpan(4 + i * 24, 24);
            BinaryPrimitives.WriteUInt32LittleEndian(r, rows[i].State);
            rows[i].Local.CopyTo(r[4..]);
            Port(r.Slice(8, 4), rows[i].LocalPort);
            rows[i].Remote.CopyTo(r[12..]);
            Port(r.Slice(16, 4), rows[i].RemotePort);
            BinaryPrimitives.WriteUInt32LittleEndian(r[20..], rows[i].Pid);
        }
        return t;
    }

    [Fact]
    public void Tcp4RowsReadAddressesPortsStateAndPid()
    {
        var t = Tcp4(([127, 0, 0, 1], 3000, [0, 0, 0, 0], 0, 2, 4120), ([192, 168, 1, 20], 51234, [140, 82, 112, 3], 443, 5, 9000));
        var s = SocketTable.Tcp4(t);
        Assert.Equal(2, s.Count);
        Assert.Equal(("127.0.0.1", 3000, "LISTEN", 4120u), (s[0].Local.ToString(), s[0].LocalPort, s[0].State, s[0].Pid));
        Assert.True(s[0].Listening);
        Assert.False(s[0].External);
        Assert.Equal(("140.82.112.3", 443, "ESTABLISHED"), (s[1].Remote!.ToString(), s[1].RemotePort, s[1].State));
        Assert.True(s[1].External);
    }

    [Fact]
    public void ATableShorterThanItsCountIsCut()
    {
        var t = Tcp4(([127, 0, 0, 1], 1, [0, 0, 0, 0], 0, 2, 1));
        BinaryPrimitives.WriteUInt32LittleEndian(t, 50);
        Assert.Single(SocketTable.Tcp4(t));
        Assert.Empty(SocketTable.Tcp4([1, 0]));
    }

    [Fact]
    public void Tcp6AndUdpRowsRead()
    {
        var t6 = new byte[4 + 56];
        BinaryPrimitives.WriteUInt32LittleEndian(t6, 1);
        var r = t6.AsSpan(4);
        IPAddress.IPv6Loopback.GetAddressBytes().CopyTo(r);
        Port(r.Slice(20, 4), 5173);
        IPAddress.Parse("2606:4700::6810:84e5").GetAddressBytes().CopyTo(r[24..]);
        Port(r.Slice(44, 4), 443);
        BinaryPrimitives.WriteUInt32LittleEndian(r[48..], 5);
        BinaryPrimitives.WriteUInt32LittleEndian(r[52..], 77);
        var s6 = Assert.Single(SocketTable.Tcp6(t6));
        Assert.Equal((5173, "2606:4700::6810:84e5", 443, "ESTABLISHED", 77u), (s6.LocalPort, s6.Remote!.ToString(), s6.RemotePort, s6.State, s6.Pid));

        var u4 = new byte[4 + 12];
        BinaryPrimitives.WriteUInt32LittleEndian(u4, 1);
        Port(u4.AsSpan(8, 4), 5353);
        BinaryPrimitives.WriteUInt32LittleEndian(u4.AsSpan(12), 1234);
        var u = Assert.Single(SocketTable.Udp4(u4));
        Assert.Equal(("UDP", 5353, 1234u, true), (u.Protocol, u.LocalPort, u.Pid, u.Listening));
    }

    [Fact]
    public void EndpointsAndServicesRead()
    {
        Assert.Equal("1.2.3.4:443", SocketTable.Endpoint(IPAddress.Parse("::ffff:1.2.3.4"), 443));
        Assert.Equal("[2606:4700::1]:443", SocketTable.Endpoint(IPAddress.Parse("2606:4700::1"), 443));
        Assert.Equal("https", SocketTable.Service(443));
        Assert.Equal("8443", SocketTable.Service(8443));
    }

    [Fact]
    public void ConnectionsGroupByAppAndRemote()
    {
        var gh = IPAddress.Parse("140.82.112.3");
        var sockets = new[]
        {
            new Socket("TCP", IPAddress.Parse("10.0.0.2"), 50001, gh, 443, "ESTABLISHED", 10),
            new Socket("TCP", IPAddress.Parse("10.0.0.2"), 50002, gh, 443, "ESTABLISHED", 11),
            new Socket("TCP", IPAddress.Parse("::ffff:10.0.0.2"), 50003, IPAddress.Parse("::ffff:140.82.112.3"), 443, "ESTABLISHED", 11),
            new Socket("TCP", IPAddress.Parse("10.0.0.2"), 50004, IPAddress.Parse("1.1.1.1"), 853, "ESTABLISHED", 20),
            new Socket("TCP", IPAddress.Loopback, 50005, IPAddress.Loopback, 3000, "ESTABLISHED", 20),   // to this PC: not shown
            new Socket("TCP", IPAddress.Parse("10.0.0.2"), 50006, gh, 443, "TIME_WAIT", 10),              // closing: not shown
        };
        var apps = Connections.ByApp(sockets, pid => pid < 20 ? "chrome" : "Code");
        Assert.Equal(["chrome", "Code"], apps.Select(a => a.App));
        var chrome = apps[0];
        Assert.Equal(3, chrome.Total);
        Assert.Equal((gh, 443, 3), Assert.Single(chrome.Remotes));
        Assert.Equal(1, apps[1].Total);
    }
}
