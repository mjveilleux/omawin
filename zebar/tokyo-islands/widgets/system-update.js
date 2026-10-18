// System update, after Omarchy's omarchy.system-update: an arrow in the active color, only while
// an omawin update is waiting (bin/update-available.ps1, every 6 hours, as Omarchy checks).
// Click: omawin update, in a floating window.
const SYNC = '\u{F021}';
const SIX_HOURS = 6 * 60 * 60 * 1000;

export function create(ctx) {
  const el = ctx.button('system-update active hidden', `<span class="icon">${SYNC}</span>`, 'An omawin update is waiting');
  el.addEventListener('click', () => {
    el.classList.add('hidden');
    ctx.omawin('launch terminal --title menu-task -- pwsh -NoLogo -Command omawin update');
  });
  async function check() {
    try {
      const out = await ctx.zebar.shellExec('pwsh', ['-NoProfile', '-NonInteractive', '-Command', '& "$HOME\\omawin\\bin\\update-available.ps1"']);
      const n = parseInt(String(out.stdout).trim(), 10) || 0;
      el.classList.toggle('hidden', n === 0);
      el.title = `${n} omawin update${n === 1 ? '' : 's'} waiting: click to update`;
    } catch (err) { console.error('update check', err); }
  }
  setTimeout(check, 20000); // not in the rush of signing in
  setInterval(check, SIX_HOURS);
  return { el };
}
