using System.Text;

namespace SystemMenu.Tests.Search;

// What a file's preview shows (FilePreview.cs): the finder's and Quick Look's (roadmap 8.4).
public class FilePreviewTests
{
    static readonly byte[] Text = Encoding.UTF8.GetBytes("hello\nworld\n");
    static readonly byte[] Binary = [0x4D, 0x5A, 0x90, 0x00, 0x03];

    [Fact]
    public void FoldersAndPicturesFirst()
    {
        Assert.Equal(PreviewKind.Folder, FilePreview.Kind(@"C:\src", true, 0, false, []));
        Assert.Equal(PreviewKind.Picture, FilePreview.Kind("cat.HEIC", false, 10, true, Binary));
    }

    [Fact]
    public void DocumentsGoToWindowsPreviewHandler()
    {
        // A PDF's first bytes can look like text; its handler draws it.
        Assert.Equal(PreviewKind.Handler, FilePreview.Kind("report.pdf", false, 1000, true, Encoding.ASCII.GetBytes("%PDF-1.7\n")));
        Assert.Equal(PreviewKind.Handler, FilePreview.Kind("plan.docx", false, 1000, true, Binary));
    }

    [Fact]
    public void TextIsDrawnHereEvenWhenAnEditorRegisteredAHandler()
    {
        Assert.Equal(PreviewKind.Text, FilePreview.Kind("Program.cs", false, 100, true, Text));
        Assert.Equal(PreviewKind.Text, FilePreview.Kind("Dockerfile", false, 100, true, Text));
        Assert.Equal(PreviewKind.Text, FilePreview.Kind("notes.unknown", false, 100, false, Text));
    }

    [Fact]
    public void BinaryOrHugeWithNoHandlerIsAnIcon()
    {
        Assert.Equal(PreviewKind.Icon, FilePreview.Kind("tool.exe", false, 100, false, Binary));
        Assert.Equal(PreviewKind.Icon, FilePreview.Kind("dump.log", false, FilePreview.TextLimit + 1, false, Text));
        Assert.Equal(PreviewKind.Handler, FilePreview.Kind("dump.txt", false, FilePreview.TextLimit + 1, true, Text));
    }

    [Fact]
    public void ANulMeansBinaryButUtf16IsText()
    {
        Assert.True(FilePreview.LooksBinary(Binary));
        Assert.False(FilePreview.LooksBinary(Text));
        Assert.False(FilePreview.LooksBinary([]));
        Assert.False(FilePreview.LooksBinary([0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0]));
    }

    [Fact]
    public void LinesAreCutAndTabsWidened()
    {
        var lines = FilePreview.Lines(["\tindented", "abcdefghij", "third"], 2, 6).ToList();
        Assert.Equal(["    in…", "abcdef…"], lines);
    }

    [Theory]
    [InlineData(0, 3, 1, 1)]
    [InlineData(2, 3, 1, 0)]
    [InlineData(0, 3, -1, 2)]
    [InlineData(0, 0, 1, 0)]
    public void StepsGoRound(int index, int count, int by, int expected) => Assert.Equal(expected, FilePreview.Step(index, count, by));

    [Fact]
    public void TheWindowIsCentredAndSizedToTheMonitor()
    {
        // 1920 × 1040 work area under a 40 px bar, at 100%: 75% of the height, the width capped at 1100.
        Assert.Equal(new PixelBox(410, 170, 1510, 950), FilePreview.Place(new PixelBox(0, 40, 1920, 1080), 1));
        // 4K at 150%: capped at 1100 × 820 DIPs.
        Assert.Equal(new PixelBox(1095, 465, 2745, 1695), FilePreview.Place(new PixelBox(0, 0, 3840, 2160), 1.5));
        // A tiny screen: the minimum, never more than the screen.
        Assert.Equal(new PixelBox(0, 0, 400, 300), FilePreview.Place(new PixelBox(0, 0, 400, 300), 1));
    }
}
