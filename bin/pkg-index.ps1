<#
.SYNOPSIS
    Every package winget knows, as a list: for omawin pkg install / remove (bin/pkg-install.ps1).

.DESCRIPTION
    winget keeps its whole catalog on the PC, a small SQLite database in its source package
    (Microsoft.Winget.Source, Public\index.db, refreshed by winget about daily). This reads it
    with Windows' own SQLite (winsqlite3.dll): ~16,000 packages in a few milliseconds, no network,
    no winget call. The list is kept in %LOCALAPPDATA%\launcher\winget-index.tsv, one package a
    line: id, name, short name (moniker), latest version, tags; read again only when winget's
    catalog has changed.

        .\pkg-index.ps1           the path of the list, made or refreshed first
        .\pkg-index.ps1 -Force    read the catalog again whatever

    Runs under Windows PowerShell 5.1 too.
#>
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$dir = Join-Path $env:LOCALAPPDATA 'launcher'
$list = Join-Path $dir 'winget-index.tsv'
$stamp = Join-Path $dir 'winget-index.version'

$source = Get-AppxPackage -Name 'Microsoft.Winget.Source' -ErrorAction SilentlyContinue | Sort-Object Version | Select-Object -Last 1
if (-not $source) { throw "winget's catalog isn't on this PC yet: run winget once (winget search x), then try again" }
$db = Join-Path $source.InstallLocation 'Public\index.db'
$version = "$($source.Version)"
if (-not $Force -and (Test-Path $list) -and (Test-Path $stamp) -and (Get-Content $stamp -Raw).Trim() -eq $version) { return $list }

if (-not ('Omawin.WingetIndex' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace Omawin {
public static class WingetIndex {
    [DllImport("winsqlite3.dll")] static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll")] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr st, IntPtr tail);
    [DllImport("winsqlite3.dll")] static extern int sqlite3_step(IntPtr st);
    [DllImport("winsqlite3.dll")] static extern IntPtr sqlite3_column_text(IntPtr st, int i);
    [DllImport("winsqlite3.dll")] static extern int sqlite3_column_bytes(IntPtr st, int i);
    [DllImport("winsqlite3.dll")] static extern int sqlite3_finalize(IntPtr st);
    [DllImport("winsqlite3.dll")] static extern int sqlite3_close(IntPtr db);
    static byte[] Z(string s) { var b = Encoding.UTF8.GetBytes(s); var z = new byte[b.Length + 1]; Array.Copy(b, z, b.Length); return z; }
    static string Text(IntPtr st, int i) {
        IntPtr p = sqlite3_column_text(st, i);
        if (p == IntPtr.Zero) return "";
        var buf = new byte[sqlite3_column_bytes(st, i)];
        Marshal.Copy(p, buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
    // id, name, moniker, latest version, tags (comma-separated): one tab-separated line each.
    public static List<string> Read(string file) {
        IntPtr db;
        // Read only, and immutable: the file sits in WindowsApps, where nothing may write.
        if (sqlite3_open_v2(Z("file:///" + file.Replace('\\', '/') + "?immutable=1"), out db, 0x01 | 0x40, IntPtr.Zero) != 0) throw new Exception("couldn't open " + file);
        var tags = new Dictionary<string, List<string>>();
        var lines = new List<string>();
        try {
            IntPtr st;
            if (sqlite3_prepare_v2(db, Z("select m.package, t.tag from tags2_map m join tags2 t on t.rowid = m.tag"), -1, out st, IntPtr.Zero) == 0) {
                while (sqlite3_step(st) == 100) {
                    string p = Text(st, 0); List<string> l;
                    if (!tags.TryGetValue(p, out l)) { l = new List<string>(); tags[p] = l; }
                    l.Add(Text(st, 1));
                }
                sqlite3_finalize(st);
            }
            if (sqlite3_prepare_v2(db, Z("select rowid, id, name, moniker, latest_version from packages order by name collate nocase"), -1, out st, IntPtr.Zero) != 0) throw new Exception("the catalog has no packages table (a newer winget?)");
            while (sqlite3_step(st) == 100) {
                List<string> t; tags.TryGetValue(Text(st, 0), out t);
                lines.Add(Text(st, 1) + "\t" + Text(st, 2) + "\t" + Text(st, 3) + "\t" + Text(st, 4) + "\t" + (t == null ? "" : string.Join(", ", t.ToArray())));
            }
            sqlite3_finalize(st);
        } finally { sqlite3_close(db); }
        return lines;
    }
}
}
'@
}
$lines = [Omawin.WingetIndex]::Read($db)
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllLines($list, $lines, (New-Object Text.UTF8Encoding $false))
Set-Content $stamp $version -NoNewline
$list
