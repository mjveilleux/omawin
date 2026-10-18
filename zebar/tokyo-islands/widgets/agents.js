// Agents, after Omarchy 4's omarchy.agents: a robot that turns the active color when a limit is
// nearly reached (90% of a window). Click: the panel, a line per limit window with its meter and
// when it resets, and a line that rotates through the week's numbers; right click: Claude Code in
// a terminal; middle click: check now.
//
// The numbers come from bin/agent-usage.ps1 (Omarchy's omarchy-agent-usage-claude): your Claude
// Code sign-in on this PC asks Anthropic for its limits, and its transcripts give the token
// counts. Every 15 minutes, as Omarchy does; 30 seconds after a failed check.
const ROBOT = '\u{F16A3}'; // 󱚣
const EVERY = 15 * 60 * 1000;

const tokens = n => (n >= 1e9 ? `${(n / 1e9).toFixed(1)}B` : n >= 1e6 ? `${(n / 1e6).toFixed(1)}M` : n >= 1e3 ? `${(n / 1e3).toFixed(1)}K` : String(n ?? 0));
function until(iso) {
  const ms = new Date(iso) - Date.now();
  if (!(ms > 0)) return 'now';
  const m = Math.round(ms / 60000), h = Math.floor(m / 60), d = Math.floor(h / 24);
  return d >= 1 ? `${d}d ${h % 24}h` : h >= 1 ? `${h}h ${m % 60}m` : `${m}m`;
}

export function create(ctx) {
  const el = ctx.button('agents hidden', `<span class="icon">${ROBOT}</span>`, 'Agents');
  el.dataset.drop = 'agents';
  const panel = ctx.drop('agents-panel');
  let rec = null;
  let heroIndex = 0;
  let timer = null;

  // The rotating line: the week, today, the model, the busiest day, prompts and sessions.
  function heroLines(r) {
    const week = (r.recentDays ?? []).reduce((a, d) => a + (d.messageCount ?? 0), 0);
    const busiest = [...(r.recentDays ?? [])].sort((a, b) => b.messageCount - a.messageCount)[0];
    const top = Object.entries(r.modelUsage ?? {}).sort((a, b) => sum(b[1]) - sum(a[1]))[0];
    const lines = [];
    if (week) lines.push(`<b>${tokens(week)}</b> tokens this week`);
    if (r.todayTotalTokens) lines.push(`<b>${tokens(r.todayTotalTokens)}</b> today`);
    if (top) lines.push(`mostly <b>${ctx.esc(top[0].replace(/^claude-/, ''))}</b>`);
    if (busiest?.messageCount) lines.push(`busiest: <b>${new Date(busiest.date + 'T12:00').toLocaleDateString(undefined, { weekday: 'long' })}</b>`);
    if (r.totalPrompts) lines.push(`<b>${r.totalPrompts.toLocaleString()}</b> prompts in <b>${r.totalSessions}</b> sessions`);
    return lines;
  }
  const sum = u => (u.inputTokens ?? 0) + (u.outputTokens ?? 0) + (u.cacheReadInputTokens ?? 0) + (u.cacheCreationInputTokens ?? 0);

  function draw() {
    const r = rec;
    if (!r) return;
    const limits = r.limits ?? [];
    const alarming = limits.some(l => l.percent >= 0.9);
    el.classList.toggle('hidden', !r.ready && !limits.length);
    el.classList.toggle('active', alarming);
    const worst = limits.reduce((a, l) => (l.percent > (a?.percent ?? -1) ? l : a), null);
    el.title = worst ? `${r.name}: ${Math.round(worst.percent * 100)}% of ${worst.label}` : r.name;
    if (!panel.open) return;
    const rows = limits
      .map(l => {
        const p = Math.max(0, Math.min(1, l.percent ?? 0));
        return `<div class="limit"><div class="limit-head"><span>${ctx.esc(l.label)}</span><span title="${(p * 100).toFixed(1)}%">${Math.round(p * 100)}% · resets in ${until(l.resetsAt)}</span></div>
          <div class="meter ${p >= 0.9 ? 'alarm' : ''}"><span style="transform:scaleX(${p.toFixed(3)})"></span></div></div>`;
      })
      .join('');
    const hero = heroLines(r);
    const status = [r.usageStatusText, r.authHelpText].filter(Boolean).map(ctx.esc).join(' ');
    const ago = r.limitsFetchedAt ? Math.round((Date.now() - r.limitsFetchedAt) / 60000) : null;
    panel.el.innerHTML = `
      <div class="agent-head"><span class="agent-name">${ctx.esc(r.name)}</span><span class="plan">${ctx.esc(r.tierLabel ?? '')}</span></div>
      ${rows || '<div class="note">No limits yet.</div>'}
      ${status ? `<div class="note">${status}</div>` : ''}
      <div class="hero">${hero.length ? hero[heroIndex % hero.length] : ''}</div>
      <div class="foot">${ago !== null ? `checked ${ago < 1 ? 'just now' : ago + ' min ago'}${r.limitsStale ? ' (kept from an earlier check)' : ''} · ` : ''}right click: Claude · middle click: check now</div>`;
  }

  async function refresh(force) {
    clearTimeout(timer);
    try {
      const args = ['-NoProfile', '-NonInteractive', '-Command', `& "$HOME\\omawin\\bin\\agent-usage.ps1" -Json${force ? ' -Force' : ''}`];
      const out = await ctx.zebar.shellExec('pwsh', args);
      const text = String(out.stdout).trim().split('\n').pop();
      rec = JSON.parse(text);
      draw();
    } catch (err) {
      console.error('agent usage', err);
    }
    timer = setTimeout(refresh, rec?.retryAdvised ? 30000 : EVERY);
  }

  el.addEventListener('click', () => {
    if (panel.open) return panel.hide();
    // Filled first: show() centers it by its width, and an empty panel would land off-center.
    draw();
    panel.show(el);
    ctx.fitWindow();
  });
  el.addEventListener('contextmenu', () => ctx.omawin('launch terminal -- claude'));
  el.addEventListener('auxclick', e => { if (e.button === 1) refresh(true); });
  setInterval(() => { heroIndex++; if (panel.open) draw(); }, 4000);
  setTimeout(refresh, 5000); // after the rest of the bar is up
  return { el };
}
