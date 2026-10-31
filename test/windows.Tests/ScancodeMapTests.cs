namespace Omawin.Windows.Tests;

// Windows' own key remap (8.12): the bytes Microsoft's scan-code mapper reads.
public class ScancodeMapTests
{
    [Fact]
    public void CapsAsEscapeIsTheDocumentedBytes() =>
        Assert.Equal("0000000000000000020000000100" + "3A0000000000", Convert.ToHexString(ScancodeMap.Build([(ScancodeMap.Caps, ScancodeMap.Esc)])));

    [Fact]
    public void ExtendedKeysKeepTheirE0()
    {
        var b = ScancodeMap.Build([(ScancodeMap.RAlt, ScancodeMap.RCtrl)]);
        Assert.Equal("1DE038E0", Convert.ToHexString(b.AsSpan(12, 4)));
    }

    [Fact]
    public void ReadsBackWhatItBuilds()
    {
        foreach (var (_, _, maps) in ScancodeMap.Presets)
            Assert.Equal(maps, ScancodeMap.Read(ScancodeMap.Build(maps)));
        Assert.Null(ScancodeMap.Read([1, 2, 3]));
        Assert.Null(ScancodeMap.Read(new byte[16])); // a count of 0
        var bad = ScancodeMap.Build([(ScancodeMap.Caps, ScancodeMap.Esc)]);
        bad[^1] = 1;
        Assert.Null(ScancodeMap.Read(bad));
    }

    [Fact]
    public void DescribesPresetsAndOthers()
    {
        Assert.Equal("Caps Lock and left Control swapped", ScancodeMap.Describe([(ScancodeMap.LCtrl, ScancodeMap.Caps), (ScancodeMap.Caps, ScancodeMap.LCtrl)]));
        Assert.Equal("right Alt is Menu", ScancodeMap.Describe([(ScancodeMap.RAlt, ScancodeMap.Apps)]));
    }

    [Fact]
    public void ScriptKeepsTheMapThatWasThere()
    {
        var on = AdminScripts.ScancodeMapWrite(ScancodeMap.Build([(ScancodeMap.Caps, ScancodeMap.Esc)]), "s");
        Assert.Contains(@"CreateSubKey('SYSTEM\CurrentControlSet\Control\Keyboard Layout')", on);
        Assert.Contains("if ($old -ne $null -and $kept -eq $null) { $k.SetValue('Scancode Map (before omawin)'", on);
        Assert.Contains("[byte[]]@(0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x02,0x00,0x00,0x00,0x01,0x00,0x3A,0x00,0x00,0x00,0x00,0x00)", on);
        var off = AdminScripts.ScancodeMapWrite(null, "s");
        Assert.Contains("if ($kept -ne $null)", off);
        Assert.Contains("DeleteValue('Scancode Map', $false)", off);
        Assert.Throws<ArgumentException>(() => AdminScripts.ScancodeMapWrite([1, 2, 3], "s"));
    }
}
