namespace SystemMenu;

// What a file's preview shows, shared by the finder's preview (MainWindow.xaml.cs) and Quick Look
// in Explorer (QuickLook.cs, roadmap 8.4): which files are pictures, how text is cut to fit, and
// for Quick Look which way a file is drawn and where its window goes. Pure, tested in
// menu-app.Tests.
public enum PreviewKind { Folder, Picture, Text, Handler, Icon }

public static class FilePreview
{
    // Drawn as pictures (Windows' own decoders: WebP and HEIC where Windows has them); the
    // finder's Images chip finds these.
    public static readonly string[] PictureExtensions = ["png", "jpg", "jpeg", "gif", "webp", "bmp", "ico", "tif", "tiff", "heic", "avif", "svg"];

    public static bool IsPicture(string name) => PictureExtensions.Contains(Extension(name));

    // Text omawin draws itself even when a program has registered a preview handler for it
    // (an editor's handler for code): ours is instant and in the theme.
    static readonly HashSet<string> PlainText =
    [
        "txt", "md", "markdown", "log", "csv", "tsv", "json", "jsonc", "yaml", "yml", "toml", "xml", "ini", "cfg", "conf", "env",
        "cs", "csx", "fs", "vb", "ps1", "psm1", "psd1", "sh", "bash", "zsh", "fish", "bat", "cmd", "py", "rb", "js", "mjs", "cjs",
        "ts", "tsx", "jsx", "go", "rs", "c", "h", "cpp", "hpp", "cc", "java", "kt", "swift", "lua", "r", "sql", "html", "htm",
        "css", "scss", "vue", "svelte", "gitignore", "gitattributes", "editorconfig", "dockerfile", "makefile", "lock", "nix",
    ];

    // Larger text is shown from its start, never read whole.
    public const long TextLimit = 4 << 20;

    // A file's preview: a folder's contents; a picture; text it can read; Windows' preview handler
    // for the rest that has one (Office, PDF, fonts, …); else its icon or thumbnail. `head` is the
    // file's first bytes (up to 8000), to tell text from binary.
    public static PreviewKind Kind(string name, bool isDir, long size, bool hasHandler, ReadOnlySpan<byte> head)
    {
        if (isDir) return PreviewKind.Folder;
        if (IsPicture(name)) return PreviewKind.Picture;
        var ext = Extension(name);
        bool plain = PlainText.Contains(ext) || PlainText.Contains(Path.GetFileName(name).ToLowerInvariant());
        if (hasHandler && !plain) return PreviewKind.Handler;
        if (size <= TextLimit && !LooksBinary(head)) return PreviewKind.Text;
        return hasHandler ? PreviewKind.Handler : PreviewKind.Icon;
    }

    // Git's rule: a NUL in the first 8000 bytes is binary; a UTF-16 file (its byte-order mark)
    // is full of NULs and is still text. An empty file is text.
    public static bool LooksBinary(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 2 && (head[0] == 0xFF && head[1] == 0xFE || head[0] == 0xFE && head[1] == 0xFF)) return false;
        return head[..Math.Min(head.Length, 8000)].IndexOf((byte)0) >= 0;
    }

    // The first `count` lines, tabs as four spaces, each cut at `width` characters with an ellipsis.
    public static IEnumerable<string> Lines(IEnumerable<string> lines, int count, int width) =>
        lines.Take(count).Select(l => l.Replace("\t", "    ") is var s && s.Length > width ? s[..width] + "…" : s);

    // ← and → through several selected files, round from the last to the first.
    public static int Step(int index, int count, int by) => count <= 0 ? 0 : ((index + by) % count + count) % count;

    // Quick Look's window: centred on the work area (the monitor less the bar) Explorer is on,
    // 60% of its width and 75% of its height, at most 1100 × 820 DIPs (scaled), at least 480 × 360.
    public static PixelBox Place(PixelBox workArea, double scale)
    {
        if (scale <= 0 || double.IsNaN(scale)) scale = 1;
        int width = (int)Math.Clamp(workArea.Width * 0.6, Math.Min(480 * scale, workArea.Width), Math.Max(1100 * scale, 480 * scale));
        int height = (int)Math.Clamp(workArea.Height * 0.75, Math.Min(360 * scale, workArea.Height), Math.Max(820 * scale, 360 * scale));
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        int left = workArea.Left + (workArea.Width - width) / 2, top = workArea.Top + (workArea.Height - height) / 2;
        return new PixelBox(left, top, left + width, top + height);
    }

    static string Extension(string name) => Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
}
