# The agent on Claude Code: `claude -p` in this repo (so it reads AGENTS.md), agent/prompt.md
# appended to its system prompt, and only what config.json allows (dontAsk: the rest is refused,
# never asked). Live (Ctrl+Win+H's herdr session) it's Claude Code's own interface, which asks
# before anything outside the allow list.

function Get-ClaudeAllowed($Allow, [bool]$edit) {
    $allowed = [Collections.Generic.List[string]]::new()
    # Bash and PowerShell both: Claude Code may have either shell tool (CLAUDE_CODE_USE_POWERSHELL_TOOL).
    foreach ($c in $Allow.commands) {
        foreach ($t in 'Bash', 'PowerShell') { $allowed.Add("$t($c)"); $allowed.Add("$t($c *)") }
    }
    if ($Allow.read) { $allowed.AddRange([string[]]@('Read', 'Glob', 'Grep')) }
    if ($Allow.web) { $allowed.AddRange([string[]]@('WebSearch', 'WebFetch')) }
    if ($edit) { $allowed.AddRange([string[]]@('Edit', 'Write')) }
    , $allowed
}

function Get-Claude {
    $claude = (Get-Command claude -ErrorAction SilentlyContinue).Source
    if ($claude) { $claude } else { Join-Path $HOME '.local\bin\claude.exe' }
}

function Invoke-AgentBackend {
    param($Settings, [string]$Request, [string]$System, $Allow, $Live, [string]$Repo, [string]$Log, [scriptblock]$OnStep)

    $session = [guid]::NewGuid().ToString()
    $promptFile = Join-Path ([IO.Path]::GetTempPath()) "omawin-agent-$session.md"
    [IO.File]::WriteAllText($promptFile, $System)
    $claudeArgs = @('-p', '--output-format', 'stream-json', '--verbose', '--session-id', $session,
        '--append-system-prompt-file', $promptFile, '--permission-mode', 'dontAsk')
    if ($Settings.model) { $claudeArgs += @('--model', $Settings.model) }
    $claudeArgs += @('--allowedTools') + (Get-ClaudeAllowed $Allow $false)

    $result = @{ ok = $false; answer = ''; session = $session; cost = 0.0; resume = "claude --resume $session" }
    try {
        # The request goes in on stdin, so no quoting can break it.
        $Request | & (Get-Claude) @claudeArgs 2>&1 | ForEach-Object {
            $line = "$_"
            Add-Content -LiteralPath $Log -Value $line -Encoding utf8
            try { $msg = $line | ConvertFrom-Json } catch { return }
            if ($msg.type -eq 'assistant') {
                foreach ($part in $msg.message.content) { if ($part.type -eq 'tool_use') { & $OnStep $part.name $part.input } }
            } elseif ($msg.type -eq 'result') {
                $result.ok = -not $msg.is_error
                $result.answer = "$($msg.result)".Trim()
                $result.cost = $msg.total_cost_usd
            }
        }
    } finally { Remove-Item $promptFile -ErrorAction SilentlyContinue }
    $result
}

# Live, in this terminal (herdr's pane): Claude Code's own interface, the request already sent.
function Invoke-AgentLive {
    param($Settings, [string]$Request, [string]$System, $Allow, $Live, [string]$Repo)
    $promptFile = Join-Path ([IO.Path]::GetTempPath()) "omawin-agent-live-$PID.md"
    [IO.File]::WriteAllText($promptFile, $System)
    $claudeArgs = @('--append-system-prompt-file', $promptFile)
    if ($Settings.model) { $claudeArgs += @('--model', $Settings.model) }
    $claudeArgs += @('--allowedTools') + (Get-ClaudeAllowed $Allow ([bool]$Live.edit))
    try { & (Get-Claude) @claudeArgs -- $Request }
    finally { Remove-Item $promptFile -ErrorAction SilentlyContinue }
}
