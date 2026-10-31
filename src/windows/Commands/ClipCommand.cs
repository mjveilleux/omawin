namespace Omawin.Windows;

// omawin clip <transform> (8.14): the clipboard's text converted in place, ready to paste: JSON
// pretty, Base64, URL encoding, a timestamp as a date, a path for WSL… `omawin clip` lists them.
static class ClipCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("omawin clip <name> changes the clipboard's text:");
            foreach (var t in Transforms.All) Console.WriteLine($"  {t.Name,-10} {t.Says}");
            return 0;
        }
        var transform = Transforms.Find(args[0]);
        if (transform == null) return Program.Fail($"no transform \"{args[0]}\" (omawin clip lists them)");
        var text = Clipboard.GetText();
        if (text == null) return Program.Fail("there's no text on the clipboard");
        var result = transform.Apply(text);
        if (result == null) return Program.Fail($"the clipboard's text isn't {transform.Says.Split(',')[0]}");
        Clipboard.SetText(result);
        var preview = result.Replace("\r", "").Split('\n')[0];
        return Program.Say($"Copied: {(preview.Length > 70 ? preview[..70] + "…" : preview)}{(result.Contains('\n') ? " …" : "")}");
    }
}
