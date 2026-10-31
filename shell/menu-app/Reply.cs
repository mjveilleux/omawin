namespace SystemMenu;

// The menu app's half of omawin's `ask` action (bin/omawin.cs): a request whose answer is printed
// (`omawin setting get explorer.hidden`). The request comes the usual way, with --reply <pid> in
// front; the answer goes to %LOCALAPPDATA%\launcher\replies\<pid>.txt (first line the exit code,
// then the text), and the named event Local\omawin-reply-<pid> wakes the waiting omawin.exe.
// Done off the UI thread: settings can block (radios, WMI), and the window stays responsive.
static class Reply
{
    public static void Answer(string pid, string[] request)
    {
        if (pid.Length == 0 || pid.Length > 10 || !pid.All(char.IsAsciiDigit)) return; // it names a file and an event
        Task.Run(async () =>
        {
            // A long answer (removing apps) says how it's going: a line at a time beside the
            // reply, which omawin.exe prints as it waits, and which keeps it waiting.
            var progressFile = Path.Combine(MenuModel.StateDir, "replies", pid + ".progress");
            void Progress(string line)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(progressFile)!);
                    File.AppendAllText(progressFile, line.Replace('\n', ' ') + "\n");
                }
                catch (IOException ex) { App.Log(ex); }
            }
            SettingCommands.Answer answer;
            try { answer = await WinSettings.Answer(request, Progress); }
            catch (Exception ex) { App.Log(ex); answer = new(1, ex.Message); }
            try
            {
                var dir = Path.Combine(MenuModel.StateDir, "replies");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, pid + ".txt"), answer.Code + "\n" + answer.Text);
                if (EventWaitHandle.TryOpenExisting(@"Local\omawin-reply-" + pid, out var done)) using (done) done.Set();
            }
            catch (Exception ex) { App.Log(ex); }
        });
    }
}
