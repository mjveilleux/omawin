namespace SystemMenu.Tests.Look;

// The tray's icons from the app's own file (shell/menu-app/Bar/IconGroup.cs, roadmap 1.8 step 5).
public class IconGroupTests
{
    // A GRPICONDIR as a program's file holds it.
    static byte[] Group(params (int W, int Bits, ushort Id)[] entries)
    {
        var d = new List<byte> { 0, 0, 1, 0, (byte)entries.Length, 0 };
        foreach (var (w, bits, id) in entries)
            d.AddRange([(byte)(w == 256 ? 0 : w), (byte)(w == 256 ? 0 : w), 0, 0, 1, 0, (byte)bits, 0, 0x10, 0, 0, 0, (byte)id, (byte)(id >> 8)]);
        return [.. d];
    }

    [Fact]
    public void ReadsEverySizeAnd256AsZero()
    {
        var list = IconGroup.Parse(Group((16, 32, 1), (256, 32, 300)));
        Assert.Equal([new IconGroup.Entry(16, 16, 32, 1), new IconGroup.Entry(256, 256, 32, 300)], list);
        Assert.Empty(IconGroup.Parse([0, 0, 2, 0, 1, 0])); // a cursor group, not icons
        Assert.Single(IconGroup.Parse(Group((16, 32, 1), (32, 32, 2)).AsSpan(0, 6 + 14 + 5))); // cut short: what's whole
    }

    [Fact]
    public void TheSmallestAtOrAboveElseTheLargestWithAlphaFirst()
    {
        var group = IconGroup.Parse(Group((16, 32, 1), (24, 32, 2), (48, 32, 3), (256, 32, 4), (32, 8, 5)));
        Assert.Equal(3, IconGroup.Choose(group, 32)!.Value.Id);   // 48 (the 32 is 8-bit, no alpha)
        Assert.Equal(2, IconGroup.Choose(group, 20)!.Value.Id);
        Assert.Equal(4, IconGroup.Choose(group, 300)!.Value.Id);  // nothing that big: the largest
        Assert.Null(IconGroup.Choose([], 32));
    }
}
