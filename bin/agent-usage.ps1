<#
.SYNOPSIS
    Write Claude Code's usage record for the bar's agents indicator.

.DESCRIPTION
    A port of Omarchy's omarchy-agent-usage-claude collector. It writes one display-ready JSON
    record to %LOCALAPPDATA%\omawin\agents\usage\claude.json, with the same field names Omarchy's
    agents panel reads, so the bar only ever reads that file and never touches Claude's files or
    Anthropic's endpoint itself.

    - Limits come from Anthropic's OAuth usage endpoint, asked with the token Claude Code saved
      when it signed in. The token goes nowhere but that request's Authorization header, and it is
      never refreshed here: only Claude Code mints a new one, so a lapsed token reads
      "Limits paused" until Claude Code next runs.
    - Local stats come from the transcripts under ~/.claude/projects. Only the last 7 days count
      (Omarchy counts all time), so the totals, the model breakdown and the active days all
      describe this week. Transcripts are append-only, so an index remembers what each file
      held and where it was read up to; a later run reads only what was added since.

        agent-usage.ps1              # refresh the record
        agent-usage.ps1 -Print       # and print a short summary
        agent-usage.ps1 -Force       # re-read every transcript and re-ask Anthropic now
        agent-usage.ps1 -LimitsOnly  # re-ask for limits, reuse local stats up to 15 minutes old
        agent-usage.ps1 -Json        # and print the record (the bar reads it: widgets/agents.js)
#>
#Requires -Version 7.5
[CmdletBinding()]
param([switch]$Print, [switch]$Force, [switch]$LimitsOnly, [switch]$Json)
$ErrorActionPreference = 'Stop'

$UsageEndpoint = 'https://api.anthropic.com/api/oauth/usage'
$AuthHelp = 'Run `claude auth login` to restore authoritative usage.'
# Anthropic rate-limits its usage endpoint readily, so a result this recent is reused rather
# than asked again, and one this recent still describes the account when a re-check is refused.
$ProbeMinIntervalSeconds = 15
$CurrentEnoughSeconds = 900
# A transcript untouched for longer than this can't hold anything from the last 7 days.
$TranscriptMaxAgeDays = 8

$stateRoot = Join-Path $env:LOCALAPPDATA 'omawin\agents'
$usageDir = Join-Path $stateRoot 'usage'
$cacheDir = Join-Path $stateRoot 'cache'
New-Item -ItemType Directory -Force $usageDir, $cacheDir | Out-Null

$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($env:CLAUDE_CONFIG_DIR)) }
             else { Join-Path $HOME '.claude' }
$utf8 = [Text.UTF8Encoding]::new($false)

function Write-Atomic([string]$Path, [string]$Text) {
    # A temp name unique to this run: the bar and a -Print run can overlap, and a shared temp
    # path would let one move the other's file away.
    $tmp = '{0}.{1}.{2}.tmp' -f $Path, $PID, [guid]::NewGuid().ToString('N').Substring(0, 8)
    [IO.File]::WriteAllText($tmp, $Text, $utf8)
    # The bar may be reading the old file at this very moment; give it a moment to let go.
    for ($i = 0; ; $i++) {
        try { [IO.File]::Move($tmp, $Path, $true); return }
        catch { if ($i -ge 10) { Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue; throw } ; [Threading.Thread]::Sleep(50) }
    }
}

function Read-JsonFile([string]$Path) {
    # -DateKind String keeps timestamps exactly as written instead of turning them into dates.
    try { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -DateKind String } catch { $null }
}

function Get-Num($Value) {
    try { $n = [double]$Value; if ([double]::IsNaN($n)) { 0 } else { [long][math]::Round($n) } } catch { 0 }
}

$nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$today = [DateTime]::Now.Date
$todayStr = $today.ToString('yyyy-MM-dd')
$recentDates = 6..0 | ForEach-Object { $today.AddDays(-$_).ToString('yyyy-MM-dd') }

# ---------------------------------------------------------------- local stats

# The index: per transcript, the usage records already parsed out of it and the byte offset they
# end at, plus a hash of what was read so a file rewritten in place isn't mistaken for an append.
# A record is key, model, day, input, output, cache read, cache write, session. Records hold
# local days, so an index written in another time zone, or for another Claude config folder
# (CLAUDE_CONFIG_DIR), is thrown away.
$IndexVersion = "omawin-claude-index 1 $([TimeZoneInfo]::Local.Id) $claudeDir"
$indexFile = Join-Path $cacheDir 'claude-index.tsv'
$scanFile = Join-Path $cacheDir 'claude-scan.json'
$HeadBytes = 4096

function Get-ReadDigest([string]$Path, [long]$Length) {
    $fs = [IO.FileStream]::new($Path, 'Open', 'Read', [IO.FileShare]'ReadWrite, Delete')
    try {
        $sha = [Security.Cryptography.SHA1]::Create()
        $n = [int][math]::Min($Length, $HeadBytes)
        $buf = [byte[]]::new($n)
        $null = $fs.Read($buf, 0, $n)
        $null = $sha.TransformBlock($buf, 0, $n, $null, 0)
        $null = $fs.Seek([math]::Max(0, $Length - $HeadBytes), 'Begin')
        $null = $fs.Read($buf, 0, $n)
        $null = $sha.TransformFinalBlock($buf, 0, $n)
        [Convert]::ToHexString($sha.Hash)
    } finally { $fs.Dispose() }
}

function Read-Index {
    $files = @{}
    if ($Force -or -not (Test-Path -LiteralPath $indexFile)) { return $files }
    foreach ($e in [OmawinClaudeTranscript]::ReadIndex($indexFile, $IndexVersion)) {
        $files[$e[0]] = @{ size = $e[1]; mtime = $e[2]; offset = $e[3]; head = $e[4]; records = $e[5] }
    }
    $files
}

function Write-Index($Files) {
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($path in $Files.Keys) {
        $e = $Files[$path]
        $entries.Add([object[]]@($path, [long]$e.size, [long]$e.mtime, [long]$e.offset, $e.head, $e.records))
    }
    Write-Atomic $indexFile ([OmawinClaudeTranscript]::FormatIndex($IndexVersion, $entries))
}

# The line-by-line work is in C#: a week of transcripts can be close to a gigabyte, and parsing
# its usage lines in PowerShell took most of a minute where this takes a few seconds. It is
# compiled once per version of this source and of PowerShell, and the DLL kept in the cache.
$TranscriptReaderSource = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

public static class OmawinClaudeTranscript
{
    static readonly byte[] Needle = Encoding.ASCII.GetBytes("\"usage\":");

    // Reads the complete lines from offset on, adding or replacing usage records, and returns
    // the offset just past the last complete line. A line without its newline is still being
    // written, so it is left for the next run, whole.
    public static long Read(string path, long offset, List<object> records)
    {
        var positions = new Dictionary<string, int>();
        for (int i = 0; i < records.Count; i++) positions[(string)((object[])records[i])[0]] = i;

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
        {
            long end = fs.Length;
            long readTo = offset;
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] buf = new byte[1 << 22];
            int start = 0, len = 0;
            while (true)
            {
                int nl = new ReadOnlySpan<byte>(buf, start, len - start).IndexOf((byte)'\n');
                if (nl < 0)
                {
                    if (start > 0) { Buffer.BlockCopy(buf, start, buf, 0, len - start); len -= start; start = 0; }
                    if (len == buf.Length) Array.Resize(ref buf, buf.Length * 2);
                    int want = (int)Math.Min(buf.Length - len, end - readTo);
                    if (want <= 0) break;
                    int got = fs.Read(buf, len, want);
                    if (got <= 0) break;
                    len += got; readTo += got;
                    continue;
                }
                int lineStart = start, lineLength = nl;
                start += nl + 1;
                offset += nl + 1;
                // Most bytes are tool output; only lines that carry usage are worth parsing.
                if (new ReadOnlySpan<byte>(buf, lineStart, lineLength).IndexOf(Needle) < 0) continue;
                try { ParseLine(new ReadOnlyMemory<byte>(buf, lineStart, lineLength), path, offset, records, positions); }
                catch (JsonException) { }
            }
        }
        return offset;
    }

    // The index is text: a version line, then per transcript an F line (path, size, mtime,
    // offset, digest) followed by an R line per record. Entries come back as
    // { path, size, mtime, offset, digest, records }.
    public static List<object> ReadIndex(string path, string version)
    {
        var entries = new List<object>();
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != version) return entries;
        List<object> records = null;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] p = lines[i].Split('\t');
            if (p[0] == "F" && p.Length == 6)
            {
                records = new List<object>();
                entries.Add(new object[] { p[1], long.Parse(p[2]), long.Parse(p[3]), long.Parse(p[4]), p[5], records });
            }
            else if (p[0] == "R" && p.Length == 9 && records != null)
            {
                records.Add(new object[] { p[1], p[2], p[3], long.Parse(p[4]), long.Parse(p[5]), long.Parse(p[6]), long.Parse(p[7]), p[8] });
            }
        }
        return entries;
    }

    public static string FormatIndex(string version, List<object> entries)
    {
        var sb = new StringBuilder(version).Append('\n');
        foreach (object[] e in entries)
        {
            sb.Append('F');
            for (int i = 0; i < 5; i++) sb.Append('\t').Append(e[i]);
            sb.Append('\n');
            foreach (object[] r in (List<object>)e[5])
            {
                sb.Append('R');
                foreach (object v in r) sb.Append('\t').Append(v);
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    static string Str(JsonElement e, string name)
    {
        JsonElement v;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    static long Tokens(JsonElement usage, string snake, string camel)
    {
        JsonElement v;
        if ((usage.TryGetProperty(snake, out v) || usage.TryGetProperty(camel, out v)) && v.ValueKind == JsonValueKind.Number)
            return (long)Math.Round(v.GetDouble());
        return 0;
    }

    static void ParseLine(ReadOnlyMemory<byte> line, string path, long offset, List<object> records, Dictionary<string, int> positions)
    {
        using (var doc = JsonDocument.Parse(line))
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            JsonElement msg, usage;
            if (!root.TryGetProperty("message", out msg) || msg.ValueKind != JsonValueKind.Object) msg = default(JsonElement);
            if (Str(root, "type") != "assistant" && Str(msg, "role") != "assistant") return;
            if (!((msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("usage", out usage)) || root.TryGetProperty("usage", out usage))
                || usage.ValueKind != JsonValueKind.Object) return;

            long input = Tokens(usage, "input_tokens", "inputTokens");
            long output = Tokens(usage, "output_tokens", "outputTokens");
            long cacheRead = Tokens(usage, "cache_read_input_tokens", "cacheReadInputTokens");
            long cacheWrite = Tokens(usage, "cache_creation_input_tokens", "cacheCreationInputTokens");
            if (input + output + cacheRead + cacheWrite <= 0) return;

            string key = Str(msg, "id") ?? Str(root, "messageId");
            if (string.IsNullOrEmpty(key)) key = path + ":" + (Str(root, "uuid") ?? Str(root, "requestId") ?? offset.ToString(CultureInfo.InvariantCulture));

            // A streamed response is several lines sharing one message id, and only the last
            // carries the real output count: keep the line with the highest one (the later on a
            // tie), whole, so fields from different snapshots never mix.
            int held;
            bool known = positions.TryGetValue(key, out held);
            if (known && output < (long)((object[])records[held])[4]) return;

            string stamp = Str(root, "timestamp") ?? Str(msg, "timestamp");
            string day = "";
            DateTimeOffset parsed;
            if (stamp != null && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
                day = parsed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var record = new object[] { key, Str(msg, "model") ?? Str(root, "model") ?? "claude", day,
                                        input, output, cacheRead, cacheWrite, Str(root, "sessionId") ?? path };
            if (known) records[held] = record;
            else { positions[key] = records.Count; records.Add(record); }
        }
    }
}
'@

if (-not ('OmawinClaudeTranscript' -as [type])) {
    $sha = [Security.Cryptography.SHA1]::Create()
    $hash = [Convert]::ToHexString($sha.ComputeHash($utf8.GetBytes($TranscriptReaderSource + $PSVersionTable.PSVersion))).Substring(0, 12)
    $dll = Join-Path $cacheDir "claude-transcript-$hash.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        Get-ChildItem -LiteralPath $cacheDir -Filter 'claude-transcript-*.dll' | Remove-Item -ErrorAction SilentlyContinue
        # Built under a name of its own, so two first runs at once never load a half-written DLL.
        $tmp = "$dll.$PID.tmp.dll"
        Add-Type -TypeDefinition $TranscriptReaderSource -OutputAssembly $tmp -ReferencedAssemblies System.Text.Json, System.Memory, System.Runtime, System.Collections
        try { [IO.File]::Move($tmp, $dll, $true) } catch { $dll = $tmp }
    }
    Add-Type -LiteralPath $dll
}

function Read-Transcript([IO.FileInfo]$File, $Entry) {
    $size = $File.Length
    $mtime = $File.LastWriteTimeUtc.Ticks
    if ($Entry -and $Entry.size -eq $size -and $Entry.mtime -eq $mtime) { return $Entry }

    # Only the same file grown past what was read, with what was read still in place, is an
    # append; anything else is read from the start.
    if ($Entry -and $size -gt $Entry.size -and $Entry.head -eq (Get-ReadDigest $File.FullName $Entry.offset)) {
        $records = $Entry.records; $offset = $Entry.offset
    } else {
        $records = [Collections.Generic.List[object]]::new(); $offset = 0L
    }
    $offset = [OmawinClaudeTranscript]::Read($File.FullName, $offset, $records)
    @{ size = $size; mtime = $mtime; offset = $offset; head = (Get-ReadDigest $File.FullName $offset); records = $records }
}

function Get-LocalStats {
    $previous = Read-Index
    $current = [ordered]@{}
    $projects = Join-Path $claudeDir 'projects'
    $cutoff = [DateTime]::UtcNow.AddDays(-$TranscriptMaxAgeDays)
    $files = if (Test-Path -LiteralPath $projects) {
        [IO.Directory]::EnumerateFiles($projects, '*.jsonl', [IO.SearchOption]::AllDirectories) |
            ForEach-Object { [IO.FileInfo]::new($_) } | Where-Object { $_.LastWriteTimeUtc -gt $cutoff } |
            Sort-Object FullName
    }
    $changed = $false
    foreach ($file in $files) {
        $old = $previous[$file.FullName]
        try { $entry = Read-Transcript $file $old }
        catch { Write-Warning "Skipping unreadable transcript $($file.Name): $($_.Exception.Message)"; $entry = $old }
        if ($entry) { $current[$file.FullName] = $entry; if (-not [object]::ReferenceEquals($entry, $old)) { $changed = $true } }
    }
    if ($changed -or $current.Count -ne $previous.Count) { Write-Index $current }

    # One API message can show up in several transcripts (a resumed session carries the earlier
    # ones). As within a file, the record with the highest output count wins, the later file a tie.
    $seen = @{}
    foreach ($e in $current.Values) {
        foreach ($r in $e.records) {
            $held = $seen[$r[0]]
            if ($null -ne $held -and $r[4] -lt $held[4]) { continue }
            $seen[$r[0]] = $r
        }
    }

    $recent = [ordered]@{}; foreach ($d in $recentDates) { $recent[$d] = 0L }
    $sessions = @{}; $todaySessions = @{}; $activeDays = @{}
    $todayByModel = [ordered]@{}; $byModel = [ordered]@{}
    $prompts = 0; $todayPrompts = 0; $todayTokens = 0L
    foreach ($r in $seen.Values) {
        $day = if ($r[2]) { $r[2] } else { $todayStr }
        if (-not $recent.Contains($day)) { continue }
        $total = $r[3] + $r[4] + $r[5] + $r[6]
        $recent[$day] += $total
        $sessions[$r[7]] = 1; $activeDays[$day] = 1; $prompts++
        $b = $byModel[$r[1]]
        if (-not $b) { $b = $byModel[$r[1]] = [ordered]@{ inputTokens = 0L; outputTokens = 0L; cacheReadInputTokens = 0L; cacheCreationInputTokens = 0L } }
        $b.inputTokens += $r[3]; $b.outputTokens += $r[4]; $b.cacheReadInputTokens += $r[5]; $b.cacheCreationInputTokens += $r[6]
        if ($day -eq $todayStr) {
            $todayPrompts++; $todaySessions[$r[7]] = 1; $todayTokens += $total
            $todayByModel[$r[1]] = [long]$todayByModel[$r[1]] + $total
        }
    }

    [ordered]@{
        todayPrompts       = $todayPrompts
        todaySessions      = $todaySessions.Count
        todayTotalTokens   = $todayTokens
        todayTokensByModel = $todayByModel
        # messageCount is a token total, despite the name Omarchy's records share with Claude's
        # own stats cache.
        recentDays         = @($recentDates | ForEach-Object { [ordered]@{ date = $_; messageCount = $recent[$_] } })
        modelUsage         = $byModel
        totalPrompts       = $prompts
        totalSessions      = $sessions.Count
        activeDays         = $activeDays.Count
        activeDates        = @($activeDays.Keys | Sort-Object)
        statsWindowDays    = 7
    }
}

# --------------------------------------------------------------------- limits

# The profile Claude Code keeps for a config home: a legacy .config.json inside it, else
# ~/.claude.json for the default home and <home>/.claude.json for one CLAUDE_CONFIG_DIR names.
function Get-ProfilePath {
    $legacy = Join-Path $claudeDir '.config.json'
    if (Test-Path -LiteralPath $legacy) { return $legacy }
    if (-not $env:CLAUDE_CONFIG_DIR) { return Join-Path $HOME '.claude.json' }
    Join-Path $claudeDir '.claude.json'
}

# The credentials carry the tier their token was minted with, and an upgrade doesn't restamp
# it; the profile is refreshed against the account, so a tier there that names a multiplier wins.
# The subscription still leads the label, so a Team seat on a Max ceiling reads "Team 5x".
function Get-Login {
    $creds = Read-JsonFile (Join-Path $claudeDir '.credentials.json')
    $login = if ($creds) { $creds['claudeAiOauth'] }
    if ($login -isnot [hashtable]) { return @{ token = ''; expiresAt = 0; refreshExpiresAt = 0; plan = '' } }
    $tier = ''
    $cliProfile = Read-JsonFile (Get-ProfilePath)
    $account = if ($cliProfile) { $cliProfile['oauthAccount'] }
    if ($account -is [hashtable]) {
        foreach ($k in 'userRateLimitTier', 'organizationRateLimitTier') { if ($account[$k]) { $tier = [string]$account[$k]; break } }
    }
    if ($tier -notmatch '(?i)max_(\d+x)') { $tier = [string]$login['rateLimitTier'] }
    $sub = [string]$login['subscriptionType']
    $plan = if ($sub) { $sub.Substring(0, 1).ToUpper() + $sub.Substring(1) } else { '' }
    if ($tier -match '(?i)max_(\d+x)') { $plan = $(if ($plan) { $plan } else { 'Max' }) + ' ' + $Matches[1] }
    @{ token = [string]$login['accessToken']; expiresAt = Get-Num $login['expiresAt']
       refreshExpiresAt = Get-Num $login['refreshTokenExpiresAt']; plan = $plan }
}

function ConvertTo-Utilization($Value) {
    if ($null -eq $Value) { return [double]::NaN }
    $n = 0.0
    if ([double]::TryParse(([string]$Value).Trim().Replace('%', ''), [Globalization.NumberStyles]::Float,
                           [Globalization.CultureInfo]::InvariantCulture, [ref]$n)) { $n } else { [double]::NaN }
}

# The endpoint reports percentages (37.0, 1.0); older payloads used fractions (0.37). A payload
# with any value of 1 or more is percent-scaled, so 1.0 reads as 1%, not 100%.
function ConvertTo-Fraction($Value, [bool]$PercentScale) {
    $n = ConvertTo-Utilization $Value
    if (-not ($n -ge 0)) { return -1.0 }
    if ($PercentScale -or $n -gt 1) { [math]::Min(1.0, $n / 100.0) } else { [math]::Min(1.0, $n) }
}

function ConvertTo-ResetAt($Value) {
    $raw = ([string]$Value).Trim()
    if (-not $raw) { return '' }
    try {
        if ($raw -match '^\d+$') {
            $ts = [long]$raw; if ($ts -lt 1e12) { $ts *= 1000 }
            return [DateTimeOffset]::FromUnixTimeMilliseconds($ts).ToString('o')
        }
        [DateTimeOffset]::Parse($raw, [Globalization.CultureInfo]::InvariantCulture).ToString('o')
    } catch { $raw }
}

# An entry's kind names its window the way the flat buckets' keys do ("weekly_scoped"). The
# window is settled here and travels as an explicit title, since a model name like
# "Opus 5 (1M context)" can't be read for one.
function Get-ScopedWindow([string]$Kind) {
    $k = $Kind.ToLower()
    if ($k.Contains('month')) { 'Monthly' }
    elseif ($k.Contains('week') -or $k.Contains('day')) { 'Weekly' }
    elseif ($k.Contains('hour') -or $k.Contains('session')) { 'Session' }
    else { '' }
}

# A model-scoped allowance (a weekly window only one model draws from) shows up only in the
# payload's limits array; the matching flat keys stay null.
function Get-ScopedLimits($Payload, [bool]$PercentScale) {
    $seen = @{}
    foreach ($entry in @($Payload['limits'])) {
        if ($entry -isnot [hashtable]) { continue }
        $scope = $entry['scope']; $model = if ($scope -is [hashtable]) { $scope['model'] }
        if ($model -isnot [hashtable]) { continue }
        $name = ([string]$(if ($model['display_name']) { $model['display_name'] } else { $model['id'] })).Trim()
        $kind = ([string]$entry['kind']).Trim()
        if (-not $name -or $seen.ContainsKey("$name`t$kind")) { continue }
        $percent = ConvertTo-Fraction $entry['percent'] $PercentScale
        if ($percent -lt 0) { continue }
        $seen["$name`t$kind"] = 1
        $window = Get-ScopedWindow $kind
        $title = if ($window) { "$name $window" } else { $name }
        [ordered]@{ label = $title; title = $title; percent = $percent; resetsAt = ConvertTo-ResetAt $entry['resets_at'] }
    }
}

function Invoke-LimitsProbe([string]$Token) {
    try {
        $resp = Invoke-WebRequest -Uri $UsageEndpoint -TimeoutSec 10 -SkipHttpErrorCheck -Verbose:$false -Headers @{
            Authorization    = "Bearer $Token"
            'anthropic-beta' = 'oauth-2025-04-20'
            Accept           = 'application/json'
        }
    } catch {
        # Reached no server at all (no route, no DNS); worth trying again soon.
        return @{ ok = $false; transport = $true
                  helpText = "Couldn't reach Anthropic's usage endpoint. Retrying shortly. Local Claude Code stats are still shown." }
    }
    if ($resp.StatusCode -ne 200) {
        $retry = "$($resp.Headers['retry-after'])"
        $help = if ($resp.StatusCode -eq 429) {
            "Anthropic's usage endpoint is rate limiting checks right now" + $(if ($retry) { " (retry after ${retry}s)" }) + '. Local Claude Code stats are still shown.'
        } else { "Anthropic's usage endpoint returned status $($resp.StatusCode). Local Claude Code stats are still shown." }
        return @{ ok = $false; helpText = $help }
    }
    $payload = try { $resp.Content | ConvertFrom-Json -AsHashtable -DateKind String } catch { $null }
    if ($payload -isnot [hashtable]) { return @{ ok = $false; helpText = "Anthropic's usage endpoint returned no limits. Local Claude Code stats are still shown." } }

    $weekly = if ($payload['seven_day_oauth_apps'] -is [hashtable]) { $payload['seven_day_oauth_apps'] } elseif ($payload['seven_day'] -is [hashtable]) { $payload['seven_day'] }
    $session = if ($payload['five_hour'] -is [hashtable]) { $payload['five_hour'] }
    # One payload speaks one convention, so every value settles the scale together.
    $raw = @($(if ($session) { $session['utilization'] }), $(if ($weekly) { $weekly['utilization'] }))
    $raw += @($payload['limits'] | Where-Object { $_ -is [hashtable] } | ForEach-Object { $_['percent'] })
    $percentScale = @($raw | Where-Object { (ConvertTo-Utilization $_) -ge 1 }).Count -gt 0

    $limits = [Collections.Generic.List[object]]::new()
    if ($session) {
        $p = ConvertTo-Fraction $session['utilization'] $percentScale
        if ($p -ge 0) { $limits.Add([ordered]@{ label = 'Session (5-hour)'; percent = $p; resetsAt = ConvertTo-ResetAt $session['resets_at'] }) }
    }
    if ($weekly) {
        $p = ConvertTo-Fraction $weekly['utilization'] $percentScale
        if ($p -ge 0) { $limits.Add([ordered]@{ label = 'Weekly (7-day)'; percent = $p; resetsAt = ConvertTo-ResetAt $weekly['resets_at'] }) }
    }
    foreach ($s in @(Get-ScopedLimits $payload $percentScale)) { $limits.Add($s) }
    if ($limits.Count -eq 0) { return @{ ok = $false; helpText = "Anthropic's usage endpoint returned no limits. Local Claude Code stats are still shown." } }
    @{ ok = $true; limits = $limits.ToArray() }
}

# Cached windows whose period has ended read 0%, with no reset time until a fresh probe.
function Get-CachedLimits($Cached) {
    $now = [DateTimeOffset]::UtcNow
    foreach ($e in @($Cached['limits'])) {
        if ($e -isnot [hashtable]) { continue }
        $at = [DateTimeOffset]::MinValue
        if ($e['resetsAt'] -and [DateTimeOffset]::TryParse([string]$e['resetsAt'], [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$at) -and $at -le $now) {
            $e = $e.Clone(); $e['percent'] = 0.0; $e['resetsAt'] = ''
        }
        $e
    }
}

function Get-Limits($Login) {
    $result = @{ limits = @(); usageStatusText = ''; authHelpText = $AuthHelp; live = $false; fetchedAtMs = 0L }
    $cacheFile = Join-Path $cacheDir 'claude-limits.json'
    $cached = Read-JsonFile $cacheFile
    if ($cached -isnot [hashtable]) { $cached = @{} }
    $fallback = @(Get-CachedLimits $cached)
    $result.fetchedAtMs = Get-Num $cached['fetchedAtMs']

    # Only Claude Code can mint a new token (it refreshes its credentials when it runs), so a
    # lapsed one is reported, never renewed here, and the last numbers stay up meanwhile.
    if (-not $Login.token) { $result.limits = $fallback; $result.usageStatusText = 'Waiting for auth'; return $result }
    if ($Login.expiresAt -gt 0 -and $Login.expiresAt -le $nowMs) {
        $result.limits = $fallback
        $kept = if ($fallback.Count) { ' — showing the last known limits.' } else { '.' }
        if ($Login.refreshExpiresAt -gt $nowMs) {
            # A lapsed access token is routine (it lives about eight hours), not a sign-out.
            $result.usageStatusText = 'Limits paused'
            $result.authHelpText = "Claude Code's access token has lapsed$kept Start Claude Code to refresh it; signing in again is not needed."
        } else {
            $result.usageStatusText = 'Sign-in expired'
            $result.authHelpText = "Claude Code's saved sign-in expired$kept Start Claude Code, or run ``claude auth login``, to refresh it."
        }
        return $result
    }

    # Repeated runs reuse a result this recent instead of asking again; -Force asks anyway.
    $age = ($nowMs - $result.fetchedAtMs) / 1000
    if ($fallback.Count -and -not $Force -and $age -ge 0 -and $age -lt $ProbeMinIntervalSeconds) {
        $result.limits = $fallback; $result.live = $true; $result.authHelpText = ''; return $result
    }

    $probe = Invoke-LimitsProbe $Login.token
    if ($probe.ok) {
        $result.limits = $probe.limits; $result.live = $true; $result.authHelpText = ''
        $result.fetchedAtMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        Write-Atomic $cacheFile ([ordered]@{ fetchedAtMs = $result.fetchedAtMs; limits = $probe.limits } | ConvertTo-Json -Depth 6 -Compress)
        return $result
    }
    if ($probe.transport) { $result.retryAdvised = $true }
    if ($fallback.Count) {
        # A refused re-check of numbers this recent still describes the account.
        $result.limits = $fallback
        $age = ($nowMs - $result.fetchedAtMs) / 1000
        $result.live = $age -ge 0 -and $age -lt $CurrentEnoughSeconds
    } else {
        $result.usageStatusText = 'Claude limits unavailable'
        $result.authHelpText = $probe.helpText
    }
    $result
}

# --------------------------------------------------------------------- record

$sw = [Diagnostics.Stopwatch]::StartNew()

# Local stats are reused for a short while (15 minutes with -LimitsOnly), so a bar that asks
# often doesn't re-walk the transcripts each time.
$scanMaxAge = if ($Force) { 0 } elseif ($LimitsOnly) { 900 } else { 20 }
$stats = $null
if ($scanMaxAge -gt 0 -and (Test-Path -LiteralPath $scanFile)) {
    $age = ([DateTime]::UtcNow - (Get-Item -LiteralPath $scanFile).LastWriteTimeUtc).TotalSeconds
    if ($age -ge 0 -and $age -le $scanMaxAge) {
        $stats = Get-Content -LiteralPath $scanFile -Raw | ConvertFrom-Json -AsHashtable -DateKind String
        # A cached scan from yesterday would put yesterday's numbers on today.
        if ($stats -and ($stats['scannedOn'] -ne $todayStr -or $stats['scannedFrom'] -ne $claudeDir)) { $stats = $null }
    }
}
if (-not $stats) {
    $stats = Get-LocalStats
    $stats['scannedOn'] = $todayStr
    $stats['scannedFrom'] = $claudeDir
    Write-Atomic $scanFile ($stats | ConvertTo-Json -Depth 6 -Compress)
}
$scanMs = $sw.ElapsedMilliseconds

$login = Get-Login
$limits = Get-Limits $login
$login.token = $null

$record = [ordered]@{
    schemaVersion   = 1
    id              = 'claude'
    name            = 'Claude Code'
    updatedAt       = [DateTimeOffset]::UtcNow.ToString('o')
    ready           = (Get-Num $stats['totalPrompts']) -gt 0 -or @($limits.limits).Count -gt 0
    hasLocalStats   = $true
    tierLabel       = $login.plan
    usageStatusText = $limits.usageStatusText
    authHelpText    = $limits.authHelpText
    limits          = @($limits.limits)
    # Numbers kept past a failed probe, and when they were measured, so the bar can tell them
    # apart from ones checked just now.
    limitsStale     = -not $limits.live
    limitsFetchedAt = $limits.fetchedAtMs
}
if ($limits.retryAdvised) { $record.retryAdvised = $true }
foreach ($k in $stats.Keys) { if ($k -notin 'scannedOn', 'scannedFrom') { $record[$k] = $stats[$k] } }

$recordText = $record | ConvertTo-Json -Depth 6 -Compress
Write-Atomic (Join-Path $usageDir 'claude.json') $recordText
if ($Json) { $recordText }

if ($Print) {
    function Format-Tokens([double]$n) {
        if ($n -ge 1e9) { '{0:0.#}B' -f ($n / 1e9) } elseif ($n -ge 1e6) { '{0:0.#}M' -f ($n / 1e6) }
        elseif ($n -ge 1e3) { '{0:0.#}K' -f ($n / 1e3) } else { "$n" }
    }
    "Claude Code  $($record.tierLabel)" + $(if ($record.usageStatusText) { "  [$($record.usageStatusText)]" })
    if ($record.authHelpText) { "  $($record.authHelpText)" }
    foreach ($l in $record.limits) {
        $reset = if ($l.resetsAt) { [DateTimeOffset]::Parse($l.resetsAt).LocalDateTime.ToString('ddd HH:mm') } else { '-' }
        '  {0,-22} {1,4:0}%  resets {2}' -f $l.label, ($l.percent * 100), $reset
    }
    if ($record.limitsStale) { '  (limits are from an earlier check)' }
    $week = ($record.recentDays | ForEach-Object { $_.messageCount } | Measure-Object -Sum).Sum
    $top = $record.modelUsage.GetEnumerator() | Sort-Object { $_.Value.inputTokens + $_.Value.outputTokens + $_.Value.cacheReadInputTokens + $_.Value.cacheCreationInputTokens } -Descending | Select-Object -First 1
    $busiest = $record.recentDays | Sort-Object { $_.messageCount } -Descending | Select-Object -First 1
    "  Tokens this week $(Format-Tokens $week), today $(Format-Tokens $record.todayTotalTokens)"
    if ($top) { "  Mostly $($top.Key); busiest day $([DateTime]::Parse($busiest.date).DayOfWeek)" }
    "  Today: $($record.todayPrompts) responses in $($record.todaySessions) sessions; week: $($record.totalPrompts) in $($record.totalSessions)"
    "  ({0} ms: local stats {1} ms)" -f $sw.ElapsedMilliseconds, $scanMs
}
