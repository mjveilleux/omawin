using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmawinMusic;

// The player's side of the Suno window (omawin-music --suno, SunoHost.cs): started the first time
// a Suno playlist plays, driven over \\.\pipe\omawin-suno, its reports (what's playing) passed up
// as events. The Suno window stays running once started (signing in, the page, all kept warm)
// until the player quits.
sealed class SunoEngine
{
    readonly object gate = new();
    NamedPipeClientStream? pipe;
    StreamWriter? writer;

    public event Action<JsonObject>? Event;

    public bool Running { get { lock (gate) return pipe is { IsConnected: true }; } }

    void Ensure()
    {
        lock (gate)
        {
            if (pipe is { IsConnected: true }) return;
            if (!File.Exists(@"\\.\pipe\" + SunoHost.PipeName))
            {
                var exe = Environment.ProcessPath!;
                Process.Start(new ProcessStartInfo(exe, "--suno") { UseShellExecute = false, CreateNoWindow = true });
            }
            var client = new NamedPipeClientStream(".", SunoHost.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            var until = DateTime.Now.AddSeconds(15); // WebView2's first start takes a few seconds
            while (true)
            {
                try { client.Connect(250); break; }
                catch (TimeoutException)
                {
                    if (DateTime.Now > until) { client.Dispose(); throw new Exception("the Suno window didn't start"); }
                }
            }
            pipe = client;
            writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var reader = new StreamReader(client, Encoding.UTF8);
            new Thread(() => Read(reader)) { IsBackground = true, Name = "suno events" }.Start();
        }
    }

    void Read(StreamReader reader)
    {
        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
                if (JsonNode.Parse(line) is JsonObject msg)
                    try { Event?.Invoke(msg); } catch (Exception ex) { Program.Log(ex); }
        }
        catch { }
    }

    public void Send(object command)
    {
        Ensure();
        lock (gate)
        {
            try { writer!.WriteLine(JsonSerializer.Serialize(command)); }
            catch (Exception ex) { pipe?.Dispose(); pipe = null; throw new Exception("the Suno window went away: " + ex.Message); }
        }
    }

    // Only if it's running: nothing starts the Suno window but playing Suno or showing it.
    public void SendIfRunning(object command) { if (Running) Send(command); }

    public void Stop()
    {
        lock (gate)
        {
            try { writer?.WriteLine("""{"cmd":"quit"}"""); } catch { }
            pipe?.Dispose();
            pipe = null;
            writer = null;
        }
    }
}
