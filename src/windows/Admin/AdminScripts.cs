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

    // DNS over HTTPS on or off for these adapters' servers (8.19): each one's DohFlags key, made or
    // removed; only servers Windows knows an encrypted address for. Then the cache is cleared.
    public static string DnsEncryption(IReadOnlyList<(Guid Adapter, string Server)> servers, bool on, string status)
    {
        if (servers.Count == 0) throw new ArgumentException("no servers", nameof(servers));
        var body = new StringBuilder();
        foreach (var (adapter, server) in servers)
        {
            if (!System.Net.IPAddress.TryParse(server, out var ip) || DnsPresets.Encryptable(ip) == null)
                throw new ArgumentException($"not a server with an encrypted address: {server}", nameof(servers));
            // .NET's registry calls, not the provider's paths: an IPv6 key name has colons in it.
            var key = Quote(DnsPresets.DohKey(adapter, ip));
            body.Append(on
                ? $"$k = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey({key}); $k.SetValue('DohFlags', [long]{DnsPresets.DohAutomatic}, [Microsoft.Win32.RegistryValueKind]::QWord); $k.Close()\n"
                : $"[Microsoft.Win32.Registry]::LocalMachine.DeleteSubKey({key}, $false)\n");
        }
        body.Append("Clear-DnsClientCache");
        return Wrap(status, body.ToString());
    }

    // Windows' own key remap (8.12), read at the next start. A map that was there before omawin's
    // first is kept beside it ("Scancode Map (before omawin)"), and off puts that one back.
    public const string ScancodeBackup = "Scancode Map (before omawin)";

    public static string ScancodeMapWrite(byte[]? map, string status)
    {
        if (map != null && ScancodeMap.Read(map) == null) throw new ArgumentException("not a Scancode Map", nameof(map));
        var body = new StringBuilder($"$k = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey({Quote(ScancodeMap.Key)})\ntry {{\n")
            .Append($"$old = $k.GetValue({Quote(ScancodeMap.Value)})\n")
            .Append($"$kept = $k.GetValue({Quote(ScancodeBackup)})\n");
        if (map != null)
            body.Append($"if ($old -ne $null -and $kept -eq $null) {{ $k.SetValue({Quote(ScancodeBackup)}, [byte[]]$old, [Microsoft.Win32.RegistryValueKind]::Binary) }}\n")
                .Append($"$k.SetValue({Quote(ScancodeMap.Value)}, [byte[]]@({string.Join(",", map.Select(x => $"0x{x:X2}"))}), [Microsoft.Win32.RegistryValueKind]::Binary)\n");
        else
            body.Append($"if ($kept -ne $null) {{ $k.SetValue({Quote(ScancodeMap.Value)}, [byte[]]$kept, [Microsoft.Win32.RegistryValueKind]::Binary); $k.DeleteValue({Quote(ScancodeBackup)}, $false) }}\n")
                .Append($"else {{ $k.DeleteValue({Quote(ScancodeMap.Value)}, $false) }}\n");
        body.Append("} finally { $k.Close() }");
        return Wrap(status, body.ToString());
    }

    // Windows Update's pause (8.18): the values Settings writes, each a UTC time checked here; or,
    // with none given, the pause removed (resume). The service reads them at its next check, and
    // Settings shows "Resume updates" as for its own pause.
    public static string UpdatePause(IReadOnlyList<(string Name, string Value)> values, string status) =>
        Wrap(status, PauseBody(values));

    static string PauseBody(IReadOnlyList<(string Name, string Value)> values)
    {
        var key = Quote(@"HKLM:\" + UpdatePlan.UxKey);
        var body = new StringBuilder($"$k = {key}\nif (-not (Test-Path -LiteralPath $k)) {{ New-Item -Path $k -Force | Out-Null }}\n");
        if (values.Count == 0)
            foreach (var name in UpdatePlan.PauseValueNames)
                body.Append($"Remove-ItemProperty -LiteralPath $k -Name {Quote(name)} -ErrorAction SilentlyContinue\n");
        foreach (var (name, value) in values)
        {
            if (!UpdatePlan.PauseValueNames.Contains(name)) throw new ArgumentException($"not a pause value: {name}", nameof(values));
            if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")) throw new ArgumentException($"not a UTC time: {value}", nameof(values));
            body.Append($"New-ItemProperty -LiteralPath $k -Name {Quote(name)} -Value {Quote(value)} -PropertyType String -Force | Out-Null\n");
        }
        return body.ToString().TrimEnd();
    }

    // An update taken off (8.44), in the order that keeps a way back: a restore point first (8.42,
    // skipped when System Protection is off, never turned on), then Windows' own uninstaller for it
    // (wusa, which shows its own window and asks), and when wusa can't (a cumulative update shipped
    // with its servicing stack), DISM on the package whose details name that KB, never a guess; then
    // updates paused so it isn't put straight back. A KB nothing on the PC names fails, with where to
    // look instead. The restart that finishes it is left to the caller (asked).
    public static string UpdateUninstall(string kb, IReadOnlyList<(string Name, string Value)> pause, string status)
    {
        if (UpdateSuspects.ParseKb(kb) is not { } n || n != kb) throw new ArgumentException("not a KB number", nameof(kb));
        var body =
            RestorePointBody($"omawin: before uninstalling KB{n}") + "\n" +
            "$wusa = Join-Path $env:SystemRoot 'System32\\wusa.exe'\n" +
            $"$p = Start-Process -FilePath $wusa -ArgumentList '/uninstall', '/kb:{n}', '/norestart' -Wait -PassThru\n" +
            // 0 done, 3010 done once restarted; 1223 or 1602: you said no in wusa's own window.
            "if ($p.ExitCode -in 1223, 1602) { throw 'you said no in the uninstaller; nothing changed' }\n" +
            "if ($p.ExitCode -notin 0, 3010) {\n" +
            "  $found = $null\n" +
            "  foreach ($q in @(Get-WindowsPackage -Online | Where-Object { $_.PackageState -eq 'Installed' -and $_.PackageName -match 'RollupFix|Package_for_KB|Package_for_DotNetRollup' })) {\n" +
            $"    if ($q.PackageName -match 'KB{n}(\\D|$)') {{ $found = $q.PackageName; break }}\n" +
            "    $d = Get-WindowsPackage -Online -PackageName $q.PackageName\n" +
            $"    if (\"$($d.Description) $($d.SupportInformation) $($d.DisplayName)\" -match 'KB{n}(\\D|$)|kbid={n}(\\D|$)') {{ $found = $q.PackageName; break }}\n" +
            "  }\n" +
            $"  if (-not $found) {{ throw ('Windows has no removable update KB{n} (wusa said ' + $p.ExitCode + '); Settings › Windows Update › Update history › Uninstall updates lists what can be removed') }}\n" +
            "  Remove-WindowsPackage -Online -PackageName $found -NoRestart | Out-Null\n" +
            "}\n" +
            PauseBody(pause);
        return Wrap(status, body);
    }

    // A restore point (8.42) inside a run you approved; never fatal, never turns System Protection
    // on. Windows makes at most one a day unless SystemRestorePointCreationFrequency is 0: set for the
    // call and put back as it was.
    static string RestorePointBody(string description) =>
        "$restoreKey = 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore'\n" +
        "$oldFrequency = (Get-ItemProperty $restoreKey -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency\n" +
        "try {\n" +
        "  if (-not (Test-Path $restoreKey)) { New-Item $restoreKey -Force | Out-Null }\n" +
        "  Set-ItemProperty $restoreKey -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord\n" +
        $"  Checkpoint-Computer -Description {Quote(description)} -RestorePointType MODIFY_SETTINGS -ErrorAction Stop\n" +
        "} catch { }\n" +
        "finally {\n" +
        "  if ($null -eq $oldFrequency) { Remove-ItemProperty $restoreKey -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }\n" +
        "  else { Set-ItemProperty $restoreKey -Name SystemRestorePointCreationFrequency -Value $oldFrequency -Type DWord }\n" +
        "}";

    // Active hours (8.18): set by hand from start to end (Windows' automatic choice off), or, with
    // null, Windows choosing them again from when the PC is used.
    public static string UpdateActiveHours((int Start, int End)? hours, string status)
    {
        var body = new StringBuilder($"$k = {Quote(@"HKLM:\" + UpdatePlan.UxKey)}\nif (-not (Test-Path -LiteralPath $k)) {{ New-Item -Path $k -Force | Out-Null }}\n");
        if (hours is { } h)
        {
            if (h.Start is < 0 or > 23 || h.End is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hours), "hours are 0 to 23");
            body.Append($"New-ItemProperty -LiteralPath $k -Name 'ActiveHoursStart' -Value {h.Start} -PropertyType DWord -Force | Out-Null\n")
                .Append($"New-ItemProperty -LiteralPath $k -Name 'ActiveHoursEnd' -Value {h.End} -PropertyType DWord -Force | Out-Null\n")
                .Append("New-ItemProperty -LiteralPath $k -Name 'SmartActiveHoursState' -Value 0 -PropertyType DWord -Force | Out-Null");
        }
        else body.Append("New-ItemProperty -LiteralPath $k -Name 'SmartActiveHoursState' -Value 1 -PropertyType DWord -Force | Out-Null");
        return Wrap(status, body.ToString());
    }

    // powershell.exe -EncodedCommand takes the script as UTF-16LE Base64: no quoting survives the
    // command line to go wrong.
    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
