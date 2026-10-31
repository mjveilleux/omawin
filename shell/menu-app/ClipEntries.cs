// The clipboard history's files (roadmap 8.14), decided without Win32 so it can be tested: texts
// and pictures, newest first, pinned ones on top and never trimmed, and the few byte formats a
// picture on the clipboard comes in. Background.cs's ClipboardHistory does the reading and writing.

using System.Buffers.Binary;

namespace SystemMenu;

public static class ClipEntries
{
    public const string Stamp = "yyyyMMdd-HHmmss-fff";
    public const string PinnedFolder = "pinned";
    // A picture bigger than this isn't kept (a screenshot of a 4K screen is ~10 MB as PNG).
    public const int MaxImageBytes = 32 * 1024 * 1024;

    public static bool IsImage(string file) => file.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    public static bool IsEntry(string file) =>
        (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || IsImage(file)) && !file.EndsWith(".thumb.png", StringComparison.OrdinalIgnoreCase);

    // Pinned first, then the rest; each newest first (the names are their times).
    public static List<string> Order(IEnumerable<string> pinned, IEnumerable<string> rest) =>
        [.. pinned.Where(IsEntry).OrderByDescending(Name, StringComparer.Ordinal), .. rest.Where(IsEntry).OrderByDescending(Name, StringComparer.Ordinal)];

    // The unpinned entries past the newest `keep`, to delete.
    public static IEnumerable<string> Trim(IEnumerable<string> unpinned, int keep) =>
        unpinned.Where(IsEntry).OrderByDescending(Name, StringComparer.Ordinal).Skip(keep);

    static string Name(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    public static bool IsPinned(string file) =>
        Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals(PinnedFolder, StringComparison.OrdinalIgnoreCase);

    // A PNG's width and height from its header (the IHDR chunk always comes first), or null.
    public static (int Width, int Height)? PngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        return ((int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]));
    }

    public static string Describe((int Width, int Height)? size) =>
        size is var (w, h) ? $"Picture, {w} × {h}" : "Picture";

    // CF_DIB / CF_DIBV5 (a bitmap's header, maybe its colour masks and palette, then the pixels)
    // as a .bmp file, which any decoder reads: the 14-byte file header in front, pointing at where
    // the pixels start. Null when it isn't a bitmap header this knows.
    public static byte[]? DibToBmp(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40) return null;
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        if (headerSize is not (40 or 52 or 56 or 108 or 124) || headerSize > dib.Length) return null;
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        uint used = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        // BI_BITFIELDS with the plain 40-byte header: three masks follow it (the bigger headers hold them).
        uint masks = headerSize == 40 && compression == 3 ? 12u : headerSize == 40 && compression == 6 ? 16u : 0u;
        uint palette = bits <= 8 ? (used != 0 ? used : 1u << bits) * 4 : used * 4;
        long offset = 14 + headerSize + masks + palette;
        if (offset - 14 > dib.Length) return null;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)offset);
        dib.CopyTo(bmp.AsSpan(14));
        return bmp;
    }
}
