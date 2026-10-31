namespace SystemMenu.Tests;

public class ReservedPortsTests
{
    // As netsh prints it (CRLF, a header in the PC's language, * for an administered range).
    const string Netsh = "\r\nProtocol tcp Port Exclusion Ranges\r\n\r\nStart Port    End Port\r\n----------    --------\r\n      2980        3079\r\n      5357        5357\r\n      8080        8080     *\r\n     50000       50059     *\r\n\r\n* - Administered port exclusions.\r\n\r\n";

    [Fact]
    public void ParsesNetshInAnyLanguage()
    {
        var ranges = ReservedPorts.Parse(Netsh);
        Assert.Equal([new PortRange(2980, 3079, false), new PortRange(5357, 5357, false), new PortRange(8080, 8080, true), new PortRange(50000, 50059, true)], ranges);
    }

    // 3000 and 3001 sit in Hyper-V's range; 8080 is set aside by an administrator on purpose
    // (a server may still bind it), so it isn't reported.
    [Fact]
    public void NamesOnlyTheDevPortsWindowsTook()
    {
        var blocked = ReservedPorts.Blocked(ReservedPorts.Parse(Netsh), ReservedPorts.DevPorts);
        Assert.Equal([3000, 3001], blocked.Select(b => b.Port));
        Assert.Equal("Ports 3000, 3001 reserved by Windows (2980-3079, Hyper-V / WSL): no server can start there until a restart",
            ReservedPorts.Describe(blocked));
        Assert.Null(ReservedPorts.Describe([]));
    }
}
