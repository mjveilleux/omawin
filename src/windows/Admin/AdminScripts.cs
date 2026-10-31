using System.Text;
using System.Text.RegularExpressions;

namespace Omawin.Windows;

// The few changes here that need admin (the hosts file, a startup entry or service for every
// account), as the Windows PowerShell 5.1 script one prompt runs (AGENTS.md: opt-in, one prompt that
// first says what changes; the elevated side is Windows' own powershell.exe, never a per-user exe
// that something could swap). Pure: the scripts are text, built only from values checked here, and
// test/windows.Tests reads them. Elevated.cs runs one.
public static partial class AdminScripts
{
    // A PowerShell single-quoted string: nothing inside is expanded; a quote is doubled. Curly
    // quotes count as quotes to PowerShell, so they're doubled too.
    public static string Quote(string s)
    {
        var b = new StringBuilder("'");
        foreach (var c in s)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛') b.Append(c);
            b.Append(c);
        }
        return b.Append('\'').ToString();
    }

    // Every script reports to a file the account that asked can read: "ok", or "fail: <why>". The
    // elevated process may be another account (a standard account's prompt takes an admin's password).
    static string Wrap(string status, string body) =>
        "$ErrorActionPreference = 'Stop'\n" +
        $"$status = {Quote(status)}\n" +
        "try {\n" + body + "\nSet-Content -LiteralPath $status -Value 'ok' -Encoding UTF8\n}\n" +
        "catch { Set-Content -LiteralPath $status -Value ('fail: ' + $_.Exception.Message) -Encoding UTF8; exit 1 }\n";

    [GeneratedRegex(@"^[A-Za-z0-9_.$ ()+\-]{1,256}$")] private static partial Regex ServiceName();

    // A startup entry for every account on or off: the StartupApproved value Task Manager writes.
    public static string StartupMachine(string approvedSub, string name, bool on, string status)
    {
        if (approvedSub is not ("Run" or "Run32" or "StartupFolder")) throw new ArgumentException("not a StartupApproved key", nameof(approvedSub));
        if (name.Length is 0 or > 260 || name.Any(char.IsControl)) throw new ArgumentException("not a startup entry's name", nameof(name));
        var key = Quote(@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + approvedSub);
        var body = new StringBuilder()
            .Append($"$k = {key}\n")
            .Append("if (-not (Test-Path -LiteralPath $k)) { New-Item -Path $k -Force | Out-Null }\n")
            .Append("$b = New-Object byte[] 12\n")
            .Append(on ? "$b[0] = 2\n" : "$b[0] = 3\n[BitConverter]::GetBytes([DateTime]::UtcNow.ToFileTimeUtc()).CopyTo($b, 4)\n")
            .Append($"New-ItemProperty -LiteralPath $k -Name {Quote(name)} -Value $b -PropertyType Binary -Force | Out-Null");
        return Wrap(status, body.ToString());
    }

    // A program's service: off = started by hand (Manual), on = automatic again (delayed if it was).
    // Through sc.exe, so the service manager itself records the change.
    public static string Service(string name, bool on, string status)
    {
        if (!ServiceName().IsMatch(name)) throw new ArgumentException("not a service name", nameof(name));
        var n = Quote(name);
        var body = on
            ? $"$d = (Get-ItemProperty -LiteralPath ('HKLM:\\SYSTEM\\CurrentControlSet\\Services\\' + {n}) -Name DelayedAutostart -ErrorAction SilentlyContinue).DelayedAutostart\n" +
              $"$mode = if ($d -eq 1) {{ 'delayed-auto' }} else {{ 'auto' }}\n" +
              $"& sc.exe config {n} start= $mode | Out-Null\n"
            : $"& sc.exe config {n} start= demand | Out-Null\n";
        body += "if ($LASTEXITCODE -ne 0) { throw ('sc.exe config failed with ' + $LASTEXITCODE) }";
        return Wrap(status, body);
    }

    // The hosts file replaced by the one you approved: read once, its SHA-256 checked against what
    // you saw (so a file changed after the prompt is refused), every line checked again, the old file
    // kept beside it, then DNS's cache cleared so the change shows at once.
    public static string HostsWrite(string pendingFile, string sha256, string stamp, string status)
    {
        if (!Regex.IsMatch(sha256, "^[0-9A-F]{64}$")) throw new ArgumentException("not a SHA-256", nameof(sha256));
        if (!Regex.IsMatch(stamp, "^[0-9-]{1,20}$")) throw new ArgumentException("not a time stamp", nameof(stamp));
        var body =
            $"$bytes = [IO.File]::ReadAllBytes({Quote(pendingFile)})\n" +
            "$sha = New-Object Security.Cryptography.SHA256Managed\n" +
            "$hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))) -replace '-', ''\n" +
            $"if ($hash -ne '{sha256}') {{ throw 'the new hosts file changed after you approved it' }}\n" +
            "$text = (New-Object Text.UTF8Encoding $false).GetString($bytes)\n" +
            "$lines = $text -split \"`r?`n\"\n" +
            $"foreach ($l in $lines) {{ if ($l -notmatch {Quote(HostsFile.LinePattern)}) {{ throw ('not a hosts line: ' + $l) }} }}\n" +
            "$h = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'\n" +
            $"if (Test-Path -LiteralPath $h) {{ Copy-Item -LiteralPath $h -Destination ($h + '.backup-{stamp}') -Force }}\n" +
            "[IO.File]::WriteAllBytes($h, $bytes)\n" +
            "& ipconfig.exe /flushdns | Out-Null";
        return Wrap(status, body);
    }

    // DNS servers for these adapters (by interface index), or back to the network's own (DHCP) when
    // none are given; then DNS's cache cleared, so the next lookup asks the new servers. DnsClient's
    // cmdlets are in Windows PowerShell 5.1 since Windows 8.
    public static string Dns(IReadOnlyList<int> interfaces, IReadOnlyList<string> servers, string status)
    {
        if (interfaces.Count == 0 || interfaces.Any(i => i is <= 0 or > 1_000_000)) throw new ArgumentException("not interface indexes", nameof(interfaces));
        foreach (var s in servers)
            if (!System.Net.IPAddress.TryParse(s, out _) || s.Any(c => !(char.IsAsciiHexDigit(c) || c is '.' or ':')))
                throw new ArgumentException($"not an IP address: {s}", nameof(servers));
        var body = new StringBuilder();
        foreach (var i in interfaces)
            body.Append(servers.Count == 0
                ? $"Set-DnsClientServerAddress -InterfaceIndex {i} -ResetServerAddresses\n"
                : $"Set-DnsClientServerAddress -InterfaceIndex {i} -ServerAddresses @({string.Join(", ", servers.Select(Quote))})\n");
        body.Append("Clear-DnsClientCache");
        return Wrap(status, body.ToString());
    }

    // powershell.exe -EncodedCommand takes the script as UTF-16LE Base64: no quoting survives the
    // command line to go wrong.
    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
