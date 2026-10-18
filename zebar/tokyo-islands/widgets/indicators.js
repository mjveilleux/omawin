// Indicators, after Omarchy's omarchy.indicators: small icons for states worth knowing, shown
// while they're on (hidden otherwise; the togglable ones show faintly while the mouse is on the
// bar, so they can be turned on there). Here:
//   dictation   the mic, red and pulsing, while voice typing (Win+H) or omawin's agent
//               (Ctrl+Win+H) listens (voice-meter, ~/.local/bin; Omarchy's voxtype indicator)
//   stay awake  a mug while the PC is kept awake (omawin toggle stay-awake); click to stop
// Omarchy's do-not-disturb and night light have no Windows API to read, so they aren't here.
const MIC = '\u{F036C}'; // 󰍬
const MUG = '\u{EF59}';

export function create(ctx) {
  const el = document.createElement('div');
  el.className = 'indicators';
  el.innerHTML = `
    <button class="indicator off active" data-ind="dictation" title="Listening"><span class="icon">${MIC}</span></button>
    <button class="indicator off reveal" data-ind="awake" title="Stay awake"><span class="icon">${MUG}</span></button>`;
  const dictation = el.querySelector('[data-ind="dictation"]');
  const awake = el.querySelector('[data-ind="awake"]');

  awake.addEventListener('click', () => {
    ctx.omawin('toggle stay-awake');
    setTimeout(checkAwake, 800);
  });

  // Stay awake: the menu app keeps a file while it's on.
  async function checkAwake() {
    try {
      const out = await ctx.zebar.shellExec('pwsh', ['-NoProfile', '-NonInteractive', '-Command', 'Test-Path "$env:LOCALAPPDATA\\launcher\\stay-awake"']);
      awake.classList.toggle('off', String(out.stdout).trim() !== 'True');
    } catch (err) { console.error('stay-awake', err); }
  }
  checkAwake();
  setInterval(checkAwake, 30000);

  // Dictation: voice-meter prints "1 <level>" while voice typing listens, "2 ..." for the agent.
  async function startVoiceMeter() {
    try {
      const proc = await ctx.zebar.shellSpawn('voice-meter');
      proc.onStdout(line => {
        const on = String(line).trim().split(' ')[0];
        dictation.classList.toggle('off', on !== '1' && on !== '2');
        dictation.classList.toggle('listening', on === '1' || on === '2');
        dictation.title = on === '2' ? 'omawin is listening' : 'Listening';
      });
      proc.onExit(() => {
        dictation.classList.add('off');
        setTimeout(startVoiceMeter, 3000);
      });
    } catch (err) { console.error('voice-meter unavailable', err); }
  }
  startVoiceMeter();
  return { el };
}
