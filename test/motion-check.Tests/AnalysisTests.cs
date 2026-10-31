using System.Buffers.Binary;
using System.IO.Compression;
using Omawin.MotionCheck;
using SystemMenu;

namespace Omawin.MotionCheck.Tests;

// The motion check's judgement, on frames made here: a backdrop, a "menu" over it, and fades
// between them timed like a 60 Hz recording.
public class AnalysisTests
{
    const int W = 40, H = 30;
    static readonly Frame Backdrop = Frame.Solid(0, W, H, 40, 150, 90);
    static readonly Frame Menu = Pattern();

    // Something menu-like: dark panel, lighter rows, a bright accent stripe.
    static Frame Pattern()
    {
        var f = Frame.Solid(0, W, H, 30, 30, 36);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int at = (y * W + x) * 4;
                if (y % 6 == 2) { f.Bgra[at] = 200; f.Bgra[at + 1] = 200; f.Bgra[at + 2] = 210; }
                if (x < 3) { f.Bgra[at] = 60; f.Bgra[at + 1] = 120; f.Bgra[at + 2] = 240; }
            }
        return f;
    }

    // ease-out-cubic from 0 to 1 over `ms`, starting `delay` after the request, sampled every 1000/60 ms.
    static List<Frame> Fade(double ms, double delay, double total, bool inward = true)
    {
        var frames = new List<Frame>();
        for (double t = -50; t <= total; t += 1000.0 / 60)
        {
            double p = t < delay ? 0 : ms <= 0 ? 1 : Math.Min(1, (t - delay) / ms);
            double a = 1 - Math.Pow(1 - p, 3);
            if (!inward) a = 1 - a;
            frames.Add(FrameMath.Blend(Menu, Backdrop, a, t));
        }
        return frames;
    }

    static List<Sample> Levels(List<Frame> frames, Frame zero, Frame one) =>
        frames.Where(f => f.Ms >= 0).Select(f => new Sample(f.Ms, FrameMath.Level(f, zero, one))).ToList();

    static Transition Opening(List<Frame> frames) => Analysis.Find(Levels(frames, Backdrop, Menu), Way.In);
    static Transition Closing(List<Frame> frames) => Analysis.Find(Levels(frames, Backdrop, Menu), Way.Out);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void A_fade_s_level_is_its_opacity(double alpha)
    {
        var f = FrameMath.Blend(Menu, Backdrop, alpha, 0);
        Assert.InRange(FrameMath.Level(f, Backdrop, Menu, step: 1), alpha - 0.01, alpha + 0.01);
    }

    [Fact]
    public void Alike_frames_can_t_be_measured()
    {
        var almost = FrameMath.Blend(Menu, Backdrop, 0.001, 0);
        Assert.True(double.IsNaN(FrameMath.Level(Backdrop, Backdrop, almost)));
        Assert.Throws<ArgumentException>(() => FrameMath.Difference(Backdrop, Frame.Solid(0, W + 1, H, 0, 0, 0)));
    }

    [Fact]
    public void The_allowance_is_motion_s_own_numbers()
    {
        var on = Expectation.For(animated: true, budgetMs: 100);
        double cascade = (Motion.Lead + Motion.Stagger * Math.Max(Motion.CascadeRows, Motion.CascadeTiles)).TotalMilliseconds;
        Assert.Equal(Motion.Enter.TotalMilliseconds + cascade + 100, on.InWithinMs);
        Assert.Equal(Motion.Exit.TotalMilliseconds + 100, on.OutWithinMs);
    }

    [Fact]
    public void A_fade_within_motion_s_time_passes()
    {
        var expect = Expectation.For(true, 100);
        // The request takes a moment to reach the menu; then Enter, then the last tile's cascade.
        var open = Opening(Fade(Motion.Enter.TotalMilliseconds + 350, delay: 40, total: 1600));
        var close = Closing(Fade(Motion.Exit.TotalMilliseconds, delay: 10, total: 900, inward: false));
        Assert.NotNull(open.StartMs);
        Assert.InRange(open.StartMs!.Value, 40, 60);
        Assert.True(open.Between > 5);
        Assert.True(close.Between > 2);
        Assert.Empty(Analysis.Problems(open, close, expect));
    }

    [Fact]
    public void A_jump_with_animations_on_didn_t_animate()
    {
        var open = Opening(Fade(0, delay: 30, total: 800));
        var close = Closing(Fade(0, delay: 30, total: 800, inward: false));
        Assert.Equal(0, open.Between);
        var problems = Analysis.Problems(open, close, Expectation.For(true, 100));
        Assert.Contains(problems, p => p.StartsWith("opening") && p.Contains("didn't animate"));
        Assert.Contains(problems, p => p.StartsWith("closing") && p.Contains("didn't animate"));
        // And with them off, that's what's asked.
        Assert.Empty(Analysis.Problems(open, close, Expectation.For(false, 100)));
    }

    [Fact]
    public void A_fade_with_animations_off_fails()
    {
        var open = Opening(Fade(200, delay: 30, total: 900));
        var close = Closing(Fade(0, delay: 30, total: 900, inward: false));
        var problems = Analysis.Problems(open, close, Expectation.For(false, 100));
        Assert.Single(problems);
        Assert.StartsWith("opening:", problems[0]);
    }

    [Fact]
    public void An_exit_slower_than_motion_s_fails()
    {
        var open = Opening(Fade(200, delay: 30, total: 1200));
        var close = Closing(Fade(600, delay: 10, total: 1200, inward: false));
        var problems = Analysis.Problems(open, close, Expectation.For(true, 100));
        Assert.Single(problems);
        Assert.Contains("to leave, allowed 230 ms", problems[0]);
    }

    [Fact]
    public void A_menu_stuck_half_way_never_arrives()
    {
        var frames = Fade(200, delay: 30, total: 900).Select(f => FrameMath.Blend(f, Backdrop, 0.6, f.Ms)).ToList();
        // Levels against the real menu: it stops at 0.6.
        var open = Opening(frames);
        Assert.Null(open.EndMs);
        Assert.InRange(open.Furthest, 0.55, 0.65);
        Assert.Contains(Analysis.Problems(open, Closing(Fade(130, 10, 600, inward: false)), Expectation.For(true, 100)),
            p => p.Contains("never reached full opacity"));
    }

    [Fact]
    public void Nothing_on_screen_is_said_so()
    {
        var still = Enumerable.Range(0, 40).Select(i => Backdrop.At(i * 16.7)).ToList();
        var problems = Analysis.Problems(Opening(still), Closing(Fade(130, 10, 600, inward: false)), Expectation.For(true, 100));
        Assert.Contains("opening: nothing changed on screen", problems);
    }

    [Fact]
    public void A_flash_back_after_closing_fails()
    {
        var frames = Fade(130, delay: 10, total: 700, inward: false);
        frames.Add(Menu.At(720)); // shown again for a frame
        frames.Add(Backdrop.At(737));
        var close = Closing(frames);
        Assert.False(close.Stayed);
        Assert.Contains(Analysis.Problems(Opening(Fade(200, 30, 900)), close, Expectation.For(true, 100)), p => p.Contains("flashed back"));
    }

    [Fact]
    public void Frames_a_second_ignore_one_slow_frame()
    {
        var times = Enumerable.Range(0, 30).Select(i => i * 1000.0 / 60).ToList();
        times.Add(times[^1] + 200);
        Assert.InRange(Analysis.Fps(times), 59, 61);
        Assert.Equal(0, Analysis.Fps([5]));
    }

    [Fact]
    public void Png_is_well_formed_and_holds_the_pixels()
    {
        var png = Png.Encode(Menu);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int at = 8;
        var types = new List<string>();
        byte[]? idat = null;
        while (at < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(crc, Png.Crc32(0xFFFFFFFF, png.AsSpan(at + 4, 4 + length)) ^ 0xFFFFFFFF);
            if (type == "IHDR")
            {
                Assert.Equal(W, BinaryPrimitives.ReadInt32BigEndian(data));
                Assert.Equal(H, BinaryPrimitives.ReadInt32BigEndian(data[4..]));
            }
            if (type == "IDAT") idat = data.ToArray();
            types.Add(type);
            at += 12 + length;
        }
        Assert.Equal(["IHDR", "IDAT", "IEND"], types);
        using var z = new ZLibStream(new MemoryStream(idat!), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();
        Assert.Equal(H * (1 + W * 3), bytes.Length);
        // Row 2, column 0: the accent stripe, as RGB.
        int pixel = 2 * (1 + W * 3) + 1;
        Assert.Equal(0, bytes[2 * (1 + W * 3)]); // no filter
        Assert.Equal(new byte[] { 240, 120, 60 }, bytes[pixel..(pixel + 3)]);
    }

    [Fact]
    public void Png_s_crc_is_the_standard_one()
    {
        // CRC-32 of "123456789" is CBF43926.
        Assert.Equal(0xCBF43926u, Png.Crc32(0xFFFFFFFF, "123456789"u8) ^ 0xFFFFFFFF);
    }

    [Fact]
    public void A_contact_sheet_lays_frames_out_with_their_levels()
    {
        var frames = Enumerable.Range(0, 10).Select(i => (Frame: Menu.At(i), Level: i / 9.0)).ToList();
        var sheet = ContactSheet.Make(frames, tileWidth: 40);
        int cell = 30 + ContactSheet.BarHeight + ContactSheet.Gap;
        Assert.Equal(ContactSheet.Columns * (40 + ContactSheet.Gap) + ContactSheet.Gap, sheet.Width);
        Assert.Equal(2 * cell + ContactSheet.Gap, sheet.Height);
        // The last frame (level 1) has a green bar its whole width; the first (0) a red sliver.
        int barY = ContactSheet.Gap + cell + 30 + 2;
        int left = ContactSheet.Gap + 1 * (40 + ContactSheet.Gap);
        int at = (barY * sheet.Width + left + 39) * 4;
        Assert.Equal(new byte[] { 100, 200, 80 }, sheet.Bgra[at..(at + 3)]); // BGR of (80, 200, 100)
        int first = ((ContactSheet.Gap + 30 + 2) * sheet.Width + ContactSheet.Gap) * 4;
        Assert.Equal(new byte[] { 70, 70, 220 }, sheet.Bgra[first..(first + 3)]);
    }
}
