// "Port already in use" that isn't (roadmap 8.34): Hyper-V and WSL reserve ranges of ports at
// boot (WinNAT, from Windows' dynamic port range), and a dev server on 3000 or 8080 then fails to
// start with "access denied" or "address in use" while nothing is listening there. The Localhost
// page names the common dev ports caught in such a range, so the failure has a reason.
// Parsing is pure (menu-app.Tests); Read runs netsh, which needs no admin to show the ranges.
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SystemMenu;

public sealed record PortRange(int Start, int End, bool Administered)
{
    public bool Contains(int port) => port >= Start && port <= End;
}

public static partial class ReservedPorts
{
    // The ports dev servers start on by default: Next / Express / Rails, Angular, Astro, Flask,
    // Vite, Django and friends, the 8000s.
    public static readonly int[] DevPorts = [3000, 3001, 4200, 4321, 5000, 5173, 5432, 6379, 8000, 8080, 8081, 8443, 8888, 9000];

    // `netsh int ipv4 show excludedportrange protocol=tcp`: a header, then "start end" per range,
    // with a * after the ones an administrator added (store=persistent). Numbers only, so any
    // language's output reads the same.
    [GeneratedRegex(@"^\s*(\d{1,5})\s+(\d{1,5})\s*(\*)?\s*$")] private static partial Regex Row();

    public static List<PortRange> Parse(string netshOutput)
    {
        var ranges = new List<PortRange>();
        foreach (var line in netshOutput.Split('\n'))
            if (Row().Match(line.TrimEnd('\r')) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var a) && int.TryParse(m.Groups[2].Value, out var b) && a <= b)
                ranges.Add(new PortRange(a, b, m.Groups[3].Success));
        return ranges;
    }

    // The dev ports Windows itself took (not ones an administrator set aside on purpose: those
    // a server may still bind), each with the range that holds it.
    public static List<(int Port, PortRange Range)> Blocked(IEnumerable<PortRange> ranges, IEnumerable<int> ports) =>
        ports.SelectMany(p => ranges.Where(r => !r.Administered && r.Contains(p)).Take(1).Select(r => (p, r))).ToList();

    // One line for the page: which ports, in which ranges.
    public static string? Describe(List<(int Port, PortRange Range)> blocked)
    {
        if (blocked.Count == 0) return null;
        var ports = string.Join(", ", blocked.Select(b => b.Port));
        var ranges = string.Join(", ", blocked.Select(b => $"{b.Range.Start}-{b.Range.End}").Distinct());
        return $"{(blocked.Count == 1 ? "Port" : "Ports")} {ports} reserved by Windows ({ranges}, Hyper-V / WSL): no server can start there until a restart";
    }

    // The ranges on this PC; none when netsh can't be run.
    public static List<PortRange> Read()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", "int ipv4 show excludedportrange protocol=tcp")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return Parse(text);
        }
        catch (Exception) { return []; }
    }
}
