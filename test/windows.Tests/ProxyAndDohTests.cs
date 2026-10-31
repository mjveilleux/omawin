using System.Net;

namespace Omawin.Windows.Tests;

// 8.19's rest: the system proxy's values, and DNS over HTTPS per adapter.
public class ProxyAndDohTests
{
    [Theory]
    [InlineData("proxy.example.com:8080", "proxy.example.com:8080")]
    [InlineData("http://proxy.example.com:3128/", "proxy.example.com:3128")]
    [InlineData("10.0.0.1:80", "10.0.0.1:80")]
    [InlineData("[::1]:8888", "[::1]:8888")]
    [InlineData("http=a.example:80; https=b.example:443", "http=a.example:80;https=b.example:443")]
    public void ServersAreStoredAsWinInetTakesThem(string typed, string stored) =>
        Assert.Equal(stored, ProxySettings.NormalizeServer(typed, out _));

    [Theory]
    [InlineData("proxy.example.com")]
    [InlineData("proxy.example.com:0")]
    [InlineData("proxy.example.com:70000")]
    [InlineData("a b:80")]
    [InlineData("a.example:80;b.example:81")] // several without schemes
    [InlineData("")]
    public void BadServersAreRefusedWithWhy(string typed)
    {
        Assert.Null(ProxySettings.NormalizeServer(typed, out var why));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void BypassAlwaysKeepsTheLocalNetwork()
    {
        Assert.Equal("<local>", ProxySettings.Bypass([]));
        Assert.Equal("*.corp.example;localhost;<local>", ProxySettings.Bypass(["*.corp.example,localhost", "<local>", "LOCALHOST"]));
    }

    [Fact]
    public void DescribeSaysWhatIsKept()
    {
        Assert.Equal(["Proxy: none"], ProxySettings.Describe(new ProxyState(false, null, null, null, false)));
        Assert.Contains("Proxy: off (p:8080 is kept for omawin proxy on)", ProxySettings.Describe(new ProxyState(false, "p:8080", null, null, false)));
        var on = ProxySettings.Describe(new ProxyState(true, "p:8080", "localhost;<local>", "http://wpad/x.pac", true)).ToList();
        Assert.Equal(["Setup script: http://wpad/x.pac", "Detect settings automatically: on (WPAD)", "Proxy: on, p:8080", "  not for: localhost, <local>"], on);
        Assert.True(ProxySettings.IsPacUrl("https://example.com/proxy.pac"));
        Assert.False(ProxySettings.IsPacUrl("javascript:alert(1)"));
    }

    static readonly Guid Wifi = Guid.Parse("4F3A7C1E-1234-4ABC-9DEF-0123456789AB");

    [Fact]
    public void DohKeysAreSettingsOwn()
    {
        Assert.Equal(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{4f3a7c1e-1234-4abc-9def-0123456789ab}\DohInterfaceSettings\Doh\1.1.1.1",
            DnsPresets.DohKey(Wifi, IPAddress.Parse("1.1.1.1")));
        Assert.EndsWith(@"\Doh6\2606:4700:4700::1111", DnsPresets.DohKey(Wifi, IPAddress.Parse("2606:4700:4700::1111")));
    }

    [Fact]
    public void EncryptionIsDescribedPerServer()
    {
        IPAddress[] cf = [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1")];
        Assert.Contains("encrypted (DNS over HTTPS)", DnsPresets.DescribeEncryption(cf, _ => true));
        Assert.Contains("1 of 2", DnsPresets.DescribeEncryption(cf, s => s.Equals(cf[0])));
        Assert.Contains("encrypt turns it on", DnsPresets.DescribeEncryption(cf, _ => false));
        Assert.Contains("no encrypted address", DnsPresets.DescribeEncryption([IPAddress.Parse("192.168.1.1")], _ => false));
    }

    [Fact]
    public void EncryptionScriptOnlyForKnownServers()
    {
        var on = AdminScripts.DnsEncryption([(Wifi, "1.1.1.1"), (Wifi, "2606:4700:4700::1111")], on: true, "s");
        Assert.Contains(@"CreateSubKey('SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{4f3a7c1e-1234-4abc-9def-0123456789ab}\DohInterfaceSettings\Doh\1.1.1.1')", on);
        Assert.Contains("SetValue('DohFlags', [long]1, [Microsoft.Win32.RegistryValueKind]::QWord)", on);
        Assert.Contains(@"\Doh6\2606:4700:4700::1111'", on);
        Assert.Contains("Clear-DnsClientCache", on);
        Assert.Contains("DeleteSubKey(", AdminScripts.DnsEncryption([(Wifi, "9.9.9.9")], on: false, "s"));
        Assert.Throws<ArgumentException>(() => AdminScripts.DnsEncryption([(Wifi, "192.168.1.1")], true, "s"));
        Assert.Throws<ArgumentException>(() => AdminScripts.DnsEncryption([(Wifi, "1.1.1.1'; x")], true, "s"));
    }
}
