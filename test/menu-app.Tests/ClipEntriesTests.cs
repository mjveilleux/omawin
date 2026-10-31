using System.Buffers.Binary;

namespace SystemMenu.Tests;

// The clipboard history's files (8.14): pins, trimming, and pictures' bytes.
public class ClipEntriesTests
{
    [Fact]
    public void PinnedFirstThenNewest()
    {
        var order = ClipEntries.Order(
            [@"C:\c\pinned\20261001-100000-000.txt"],
            [@"C:\c\20261030-100000-000.txt", @"C:\c\20261031-090000-000.png", @"C:\c\20261031-090000-000.thumb.png", @"C:\c\notes.md"]);
        Assert.Equal([@"C:\c\pinned\20261001-100000-000.txt", @"C:\c\20261031-090000-000.png", @"C:\c\20261030-100000-000.txt"], order);
    }

    [Fact]
    public void TrimKeepsTheNewestAndNeverSeesPins()
    {
        var files = Enumerable.Range(0, 5).Select(i => $@"C:\c\2026103{i}-000000-000.txt").ToList();
        Assert.Equal([@"C:\c\20261031-000000-000.txt", @"C:\c\20261030-000000-000.txt"], ClipEntries.Trim(files, 3));
    }

    [Fact]
    public void PinnedIsTheFolder()
    {
        Assert.True(ClipEntries.IsPinned(Path.Combine("x", "clipboard", "pinned", "a.txt")));
        Assert.False(ClipEntries.IsPinned(Path.Combine("x", "clipboard", "a.txt")));
    }

    static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), (uint)h);
        return b;
    }

    [Fact]
    public void PngSizeFromTheHeader()
    {
        Assert.Equal((1920, 1080), ClipEntries.PngSize(Png(1920, 1080)));
        Assert.Equal("Picture, 1920 × 1080", ClipEntries.Describe(ClipEntries.PngSize(Png(1920, 1080))));
        Assert.Null(ClipEntries.PngSize(new byte[33]));
        Assert.Equal("Picture", ClipEntries.Describe(null));
    }

    static byte[] Dib(uint header, ushort bits, uint compression, uint used, int pixels)
    {
        var b = new byte[header + pixels + 2048];
        BinaryPrimitives.WriteUInt32LittleEndian(b, header);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(14), bits);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), used);
        return b;
    }

    [Theory]
    [InlineData(40u, (ushort)32, 0u, 0u, 54u)]             // BI_RGB: pixels straight after
    [InlineData(40u, (ushort)32, 3u, 0u, 66u)]             // BI_BITFIELDS: three masks first
    [InlineData(40u, (ushort)8, 0u, 0u, 54u + 1024u)]      // a full 256-colour palette
    [InlineData(40u, (ushort)8, 0u, 16u, 54u + 64u)]       // a 16-entry one
    [InlineData(124u, (ushort)32, 3u, 0u, 138u)]           // BITMAPV5HEADER holds its masks
    public void DibBecomesABmpPointingAtItsPixels(uint header, ushort bits, uint compression, uint used, uint offset)
    {
        var dib = Dib(header, bits, compression, used, 64);
        var bmp = ClipEntries.DibToBmp(dib)!;
        Assert.Equal("BM"u8.ToArray(), bmp[..2]);
        Assert.Equal((uint)bmp.Length, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(2)));
        Assert.Equal(offset, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)));
        Assert.Equal(dib, bmp[14..]);
    }

    [Fact]
    public void NotABitmap()
    {
        Assert.Null(ClipEntries.DibToBmp(new byte[10]));
        Assert.Null(ClipEntries.DibToBmp(Dib(41, 32, 0, 0, 0)));
    }
}
