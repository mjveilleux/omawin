namespace Omawin.Windows.Tests;

// The scripts the one admin prompt runs: built only from checked values, nothing a name could break out of.
public class AdminScriptsTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("curly\u2019s", "'curly\u2019\u2019s'")]
    [InlineData("$(evil) `n", "'$(evil) `n'")]
    public void QuoteKeepsTextLiteral(string text, string quoted) => Assert.Equal(quoted, AdminScripts.Quote(text));

    [Fact]
    public void StartupMachineWritesTheApprovedValue()
    {
        var s = AdminScripts.StartupMachine("Run32", "Vendor's Tray", on: false, status: @"C:\Users\u\AppData\Local\launcher\r.txt");
        Assert.Contains(@"'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32'", s);
        Assert.Contains("-Name 'Vendor''s Tray'", s);
        Assert.Contains("$b[0] = 3", s);
        Assert.Contains("Set-Content -LiteralPath $status -Value 'ok'", s);
        Assert.Contains("$b[0] = 2", AdminScripts.StartupMachine("Run", "x", on: true, status: "s"));
    }

    [Theory]
    [InlineData("Services")]
    [InlineData("Run\\..\\..")]
    public void StartupMachineRefusesOtherKeys(string sub) =>
        Assert.Throws<ArgumentException>(() => AdminScripts.StartupMachine(sub, "x", true, "s"));

    [Fact]
    public void ServiceGoesThroughScAndKeepsDelayedStart()
    {
        var off = AdminScripts.Service("VendorUpdater", on: false, status: "s");
        Assert.Contains("& sc.exe config 'VendorUpdater' start= demand", off);
        var on = AdminScripts.Service("VendorUpdater", on: true, status: "s");
        Assert.Contains("delayed-auto", on);
        Assert.Throws<ArgumentException>(() => AdminScripts.Service("x'; Remove-Item C:\\", true, "s"));
    }

    [Fact]
    public void HostsWriteChecksTheHashAndEveryLine()
    {
        var hash = new string('A', 64);
        var s = AdminScripts.HostsWrite(@"C:\Users\u\AppData\Local\launcher\pending.hosts", hash, "20261031-090000", "s");
        Assert.Contains($"if ($hash -ne '{hash}')", s);
        Assert.Contains(AdminScripts.Quote(HostsFile.LinePattern), s);
        Assert.Contains("($h + '.backup-20261031-090000')", s);
        Assert.Contains("ipconfig.exe /flushdns", s);
        Assert.Throws<ArgumentException>(() => AdminScripts.HostsWrite("f", "not a hash", "1", "s"));
        Assert.Throws<ArgumentException>(() => AdminScripts.HostsWrite("f", hash, "1; evil", "s"));
    }

    [Fact]
    public void EncodeIsUtf16Base64()
    {
        Assert.Equal("dwBoAG8AYQBtAGkA", AdminScripts.Encode("whoami"));
    }
}
