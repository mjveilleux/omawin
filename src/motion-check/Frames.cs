using System.Buffers.Binary;
using System.IO.Compression;

namespace Omawin.MotionCheck;

// One picture of the menu's place on screen: 32-bit BGRA (what GDI hands back), top row first,
// and when it was taken, in milliseconds from the request (negative: before it). No Windows
// calls here, so the tests build these by hand on Linux.
public sealed class Frame
{
    public double Ms { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public Frame(double ms, int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "a frame needs pixels");
        if (bgra.Length != width * height * 4) throw new ArgumentException($"{width}x{height} needs {width * height * 4} bytes, not {bgra.Length}", nameof(bgra));
        Ms = ms;
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    // The same pixels, timed from another moment.
    public Frame At(double ms) => new(ms, Width, Height, Bgra);

    // One colour all over (the tests' backgrounds; the contact sheet's canvas).
    public static Frame Solid(double ms, int width, int height, byte r, byte g, byte b)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = b; bytes[i + 1] = g; bytes[i + 2] = r; bytes[i + 3] = 255; }
        return new Frame(ms, width, height, bytes);
    }
}

public static class FrameMath
{
    // Below this mean difference (0-255 a channel) the menu and what's behind it are too alike to
    // tell a fade from noise.
    public const double MinSpan = 2.0;

    // Mean absolute difference per colour channel (0-255) of two frames of one size, over every
    // `step`th pixel each way (every pixel is more than the answer needs).
    public static double Difference(Frame a, Frame b, int step = 2)
    {
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException($"frames of different sizes: {a.Width}x{a.Height} and {b.Width}x{b.Height}");
        if (step < 1) step = 1;
        long sum = 0, count = 0;
        var x = a.Bgra;
        var y = b.Bgra;
        for (int row = 0; row < a.Height; row += step)
        {
            int at = row * a.Width * 4;
            for (int col = 0; col < a.Width; col += step, at += 4 * step)
            {
                sum += Math.Abs(x[at] - y[at]) + Math.Abs(x[at + 1] - y[at + 1]) + Math.Abs(x[at + 2] - y[at + 2]);
                count += 3;
            }
        }
        return count == 0 ? 0 : (double)sum / count;
    }

    // How far `f` has gone from `zero` toward `one`: 0 at zero, 1 at one. For a fade (each pixel a
    // blend of the two) this is exactly the opacity: |a*one + (1-a)*zero - zero| = a*|one - zero|.
    // NaN when the two are too alike to measure.
    public static double Level(Frame f, Frame zero, Frame one, int step = 2)
    {
        double span = Difference(zero, one, step);
        return span < MinSpan ? double.NaN : Difference(f, zero, step) / span;
    }

    // `a` over `b` at opacity `alpha` (the tests' fades).
    public static Frame Blend(Frame a, Frame b, double alpha, double ms)
    {
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException("frames of different sizes");
        var bytes = new byte[a.Bgra.Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Math.Round(a.Bgra[i] * alpha + b.Bgra[i] * (1 - alpha));
        return new Frame(ms, a.Width, a.Height, bytes);
    }
}

// PNG, written by hand (zlib from the BCL): no image library, so it builds and is tested anywhere.
public static class Png
{
    static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // 8-bit RGB, no alpha (the screen has none worth keeping).
    public static byte[] Encode(Frame f)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var line = new byte[1 + f.Width * 3]; // filter byte 0 (none), then the row
            for (int row = 0; row < f.Height; row++)
            {
                int at = row * f.Width * 4;
                for (int col = 0, o = 1; col < f.Width; col++, at += 4, o += 3)
                {
                    line[o] = f.Bgra[at + 2];
                    line[o + 1] = f.Bgra[at + 1];
                    line[o + 2] = f.Bgra[at];
                }
                z.Write(line);
            }
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, f.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), f.Height);
        header[8] = 8;  // bits a channel
        header[9] = 2;  // RGB
        using var png = new MemoryStream();
        png.Write(Signature);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        s.Write(four);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(four, crc);
        s.Write(four);
    }

    static readonly uint[] Table = MakeTable();
    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    // PNG's CRC-32 running over `data` from `crc` (start at 0xFFFFFFFF, finish with ^ 0xFFFFFFFF).
    public static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

// Every frame of a phase, small, in time order, left to right and down, each with a bar under it
// as long as its level (green arrived, red gone, amber on the way): a fade reads at a glance, and
// a frame stuck half way stands out. No text: the summary lists the times.
public static class ContactSheet
{
    public const int Columns = 8, Gap = 4, BarHeight = 6;

    public static Frame Make(IReadOnlyList<(Frame Frame, double Level)> frames, int tileWidth = 192)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames", nameof(frames));
        var first = frames[0].Frame;
        int tileHeight = Math.Max(1, (int)Math.Round((double)first.Height * tileWidth / first.Width));
        int columns = Math.Min(Columns, frames.Count);
        int rows = (frames.Count + Columns - 1) / Columns;
        int cellHeight = tileHeight + BarHeight + Gap;
        int width = columns * (tileWidth + Gap) + Gap;
        int height = rows * cellHeight + Gap;
        var sheet = Frame.Solid(0, width, height, 24, 24, 24);
        for (int i = 0; i < frames.Count; i++)
        {
            int left = Gap + i % Columns * (tileWidth + Gap);
            int top = Gap + i / Columns * cellHeight;
            Draw(sheet, frames[i].Frame, left, top, tileWidth, tileHeight);
            var level = frames[i].Level;
            var (r, g, b) = double.IsNaN(level) ? ((byte)128, (byte)128, (byte)128)
                : level >= Analysis.High ? ((byte)80, (byte)200, (byte)100)
                : level <= Analysis.Low ? ((byte)220, (byte)70, (byte)70)
                : ((byte)230, (byte)170, (byte)40);
            int length = double.IsNaN(level) ? tileWidth : (int)Math.Round(Math.Clamp(level, 0, 1) * tileWidth);
            Fill(sheet, left, top + tileHeight + 1, Math.Max(1, length), BarHeight - 1, r, g, b);
        }
        return sheet;
    }

    // `f` scaled into the box (nearest pixel: these are previews).
    static void Draw(Frame sheet, Frame f, int left, int top, int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(f.Height - 1, y * f.Height / h);
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(f.Width - 1, x * f.Width / w);
                int from = (sy * f.Width + sx) * 4;
                int to = ((top + y) * sheet.Width + left + x) * 4;
                sheet.Bgra[to] = f.Bgra[from];
                sheet.Bgra[to + 1] = f.Bgra[from + 1];
                sheet.Bgra[to + 2] = f.Bgra[from + 2];
                sheet.Bgra[to + 3] = 255;
            }
        }
    }

    static void Fill(Frame sheet, int left, int top, int w, int h, byte r, byte g, byte b)
    {
        for (int y = top; y < Math.Min(sheet.Height, top + h); y++)
            for (int x = left; x < Math.Min(sheet.Width, left + w); x++)
            {
                int at = (y * sheet.Width + x) * 4;
                sheet.Bgra[at] = b;
                sheet.Bgra[at + 1] = g;
                sheet.Bgra[at + 2] = r;
                sheet.Bgra[at + 3] = 255;
            }
    }
}
