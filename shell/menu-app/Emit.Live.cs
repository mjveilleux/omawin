namespace SystemMenu;

// The half of Emit.cs that reads bin/commands.txt and acts: the request goes to HandleRequest on
// the UI thread, exactly as the request file's do; a command that isn't the menu app's own runs
// through omawin.exe.
public static partial class Emit
{
    static Dictionary<string, string> rows = [];
    static DateTime stamp;
    static readonly object reading = new();
    static string CommandsFile => Path.Combine(MenuModel.Repo, "bin", "commands.txt");

    // A custom event from GlazeWM, on its socket's thread. Only `omawin` is ours.
    public static void Handle(string name, string[] words, Microsoft.UI.Dispatching.DispatcherQueue ui, Action<string[]> request)
    {
        if (name != "omawin") return;
        Program.Trace("emit omawin " + string.Join(' ', words));
        var resolved = Resolve(Load(), words);
        switch (resolved.Kind)
        {
            case Kind.Request: ui.TryEnqueue(() => request(resolved.Words)); break;
            case Kind.Omawin: MenuModel.Omawin(resolved.Words); break;
            default: App.Log(new Exception("emit omawin: no command \"" + string.Join(' ', words) + "\" in bin/commands.txt")); break;
        }
    }

    // Read again only when the file changed: it's live, edited in the repo.
    static Dictionary<string, string> Load()
    {
        lock (reading)
        {
            try
            {
                var now = File.GetLastWriteTimeUtc(CommandsFile);
                if (now != stamp)
                {
                    rows = Table(File.ReadAllLines(CommandsFile));
                    stamp = now;
                }
            }
            catch (IOException) { } // being written: the old table, read again next time
            catch (UnauthorizedAccessException) { }
            return rows;
        }
    }
}
