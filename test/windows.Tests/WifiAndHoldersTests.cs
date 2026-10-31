namespace Omawin.Windows.Tests;

public class WifiTests
{
    // A profile as WlanGetProfile returns it with the plain-text key flag.
    static string Profile(string ssid, string auth, string encryption, string? key, bool isProtected = false, bool hidden = false) => $"""
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>{ssid}</name>
          <SSIDConfig><SSID><hex>00</hex><name>{ssid}</name></SSID>{(hidden ? "<nonBroadcast>true</nonBroadcast>" : "")}</SSIDConfig>
          <connectionType>ESS</connectionType><connectionMode>auto</connectionMode>
          <MSM><security>
            <authEncryption><authentication>{auth}</authentication><encryption>{encryption}</encryption><useOneX>false</useOneX></authEncryption>
            {(key == null ? "" : $"<sharedKey><keyType>passPhrase</keyType><protected>{(isProtected ? "true" : "false")}</protected><keyMaterial>{key}</keyMaterial></sharedKey>")}
          </security></MSM>
        </WLANProfile>
        """;

    [Fact]
    public void PersonalNetworkGivesItsKeyAndAWpaCode()
    {
        var n = WifiShare.FromProfile(Profile("Home; 5G", "WPA2PSK", "AES", @"pa:ss\word"))!;
        Assert.Equal(("Home; 5G", @"pa:ss\word", false), (n.Ssid, n.Key, n.KeyProtected));
        Assert.False(WifiShare.IsEnterprise(n));
        Assert.Equal(@"WIFI:T:WPA;S:Home\; 5G;P:pa\:ss\\word;;", WifiShare.QrText(n));
    }

    [Fact]
    public void ProtectedKeyIsntShared()
    {
        var n = WifiShare.FromProfile(Profile("Cafe", "WPA3SAE", "AES", "01000000D08C9DDF0115", isProtected: true))!;
        Assert.Null(n.Key);
        Assert.True(n.KeyProtected);
    }

    [Fact]
    public void OpenWepHiddenAndEnterprise()
    {
        var open = WifiShare.FromProfile(Profile("Airport", "open", "none", null))!;
        Assert.Equal("WIFI:T:nopass;S:Airport;;", WifiShare.QrText(open));
        var wep = WifiShare.FromProfile(Profile("Old", "open", "WEP", "abcde", hidden: true))!;
        Assert.Equal("WIFI:T:WEP;S:Old;P:abcde;H:true;;", WifiShare.QrText(wep));
        Assert.True(WifiShare.IsEnterprise(WifiShare.FromProfile(Profile("Corp", "WPA2", "AES", null))!));
        Assert.Null(WifiShare.FromProfile("<not xml"));
    }

    [Theory]
    [InlineData(@"WIFI:T:WPA;S:Home\; 5G;P:pa\:ss\\word;;")]
    [InlineData("WIFI:T:WPA;S:Café de la Gare;P:crème brûlée;;")]
    public void TheCodeReadsBackAsItsText(string text)
    {
        var dark = WifiShare.Modules(text);
        int w = dark.GetLength(0), h = dark.GetLength(1);
        Assert.True(dark[2, 2]); // the finder pattern's corner, inside the two-module quiet zone
        Assert.False(dark[0, 0]);
        // Rendered 4 pixels a module, read with the decoder phones' apps share.
        const int scale = 4;
        var pixels = new byte[w * scale * h * scale];
        for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                pixels[y * w * scale + x] = dark[x / scale, y / scale] ? (byte)0 : (byte)255;
        var source = new ZXing.RGBLuminanceSource(pixels, w * scale, h * scale, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
        var result = new ZXing.QrCode.QRCodeReader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(source)),
            new Dictionary<ZXing.DecodeHintType, object> { [ZXing.DecodeHintType.CHARACTER_SET] = "UTF-8" });
        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void DrawPutsTwoRowsInEachLine()
    {
        var dark = new bool[3, 3];
        dark[0, 0] = true; dark[0, 1] = true;  // column 0: both halves of the first line
        dark[1, 0] = true;                     // column 1: the top half
        dark[2, 1] = true;                     // column 2: the bottom half
        dark[0, 2] = true;                     // third row, alone on the second line
        var lines = WifiShare.Draw(dark);
        Assert.Equal(2, lines.Count);
        Assert.Contains("█▀▄", lines[0]);
        Assert.Contains("▀  ", lines[1]);
        Assert.All(lines, l => Assert.EndsWith("\u001b[0m", l));
    }
}

public class HoldersTests
{
    [Fact]
    public void ServicesInOneProcessAreOneRow()
    {
        var rows = new[]
        {
            new Holder(900, 5, "Host Process", "Dnscache", HolderKind.Service, @"C:\Windows\System32\svchost.exe"),
            new Holder(900, 5, "Host Process", "CryptSvc", HolderKind.Service, @"C:\Windows\System32\svchost.exe"),
            new Holder(901, 6, "Code", null, HolderKind.MainWindow, @"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe"),
        };
        var merged = Holders.Merge(rows);
        Assert.Equal(2, merged.Count);
        Assert.Equal("Dnscache, CryptSvc", merged[0].Service);
    }

    [Fact]
    public void DescribeAndWhatCanBeDone()
    {
        var code = new Holder(4120, 1, "Visual Studio Code", null, HolderKind.MainWindow, @"C:\Apps\Code.exe");
        Assert.Equal(@"Visual Studio Code (pid 4120, a window)  C:\Apps\Code.exe", Holders.Describe(code));
        Assert.True(Holders.CanClose(code));
        var explorer = code with { Name = "Windows Explorer", Kind = HolderKind.Explorer };
        Assert.False(Holders.CanClose(explorer)); // its windows include the desktop: closing that asks to shut down
        var csrss = new Holder(600, 1, "", null, HolderKind.Critical, @"C:\Windows\System32\csrss.exe");
        Assert.Equal(@"csrss.exe (pid 600, part of Windows, can't be ended)  C:\Windows\System32\csrss.exe", Holders.Describe(csrss));
        Assert.False(Holders.CanEnd(csrss));
    }

    [Theory]
    [InlineData("\"C:\\Users\\me\\a b.txt\"", @"C:\Users\me\a b.txt")]
    [InlineData("  D:/x/y \r\nsecond line", "D:/x/y")]
    [InlineData(@"\\server\share\f.docx", @"\\server\share\f.docx")]
    [InlineData("just some words", null)]
    [InlineData("C:\\a?b", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PathFromClipboardText(string? text, string? path) => Assert.Equal(path, Holders.PathFromText(text));

    [Fact]
    public void NoneFoundMentionsAFoldersCurrentDirectory()
    {
        Assert.Contains("current folder", Holders.NoneFound(folder: true));
        Assert.Equal("Nothing holds it open now.", Holders.NoneFound(folder: false));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\csrss.exe", "csrss.exe", "csrss")]
    [InlineData("noext", "noext", "noext")]
    [InlineData(@"C:\a.b\.hidden", ".hidden", ".hidden")]
    public void WindowsPathsSplitOnAnyOs(string path, string name, string bare)
    {
        Assert.Equal(name, WindowsPath.FileName(path));
        Assert.Equal(bare, WindowsPath.FileNameWithoutExtension(path));
    }
}
