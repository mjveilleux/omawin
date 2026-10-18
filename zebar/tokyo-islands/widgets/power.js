// Power, after Omarchy's omarchy.power: the battery by charge (or a plug while charging; a power
// symbol on a PC without one). Right click: its percentage beside it, or not; click: the
// system menu's power options.
const BATTERY = ['\u{F007A}', '\u{F007B}', '\u{F007C}', '\u{F007D}', '\u{F007E}', '\u{F007F}', '\u{F0080}', '\u{F0081}', '\u{F0082}', '\u{F0079}'];
const CHARGING = '\u{F0084}';
const POWER = '\u{F0425}'; // 󰐥

export function create(ctx) {
  const el = ctx.button('power', `<span class="icon">${POWER}</span><span class="pct hidden"></span>`, 'Power');
  const icon = el.querySelector('.icon');
  const label = el.querySelector('.pct');
  let showPct = false;
  function update(o) {
    const b = o.battery;
    if (!b || b.state === 'unknown') { icon.textContent = POWER; label.classList.add('hidden'); return; }
    const p = Math.round(b.chargePercent);
    const glyph = b.isCharging ? CHARGING : BATTERY[Math.min(9, Math.floor(p / 10))];
    if (icon.textContent !== glyph) icon.textContent = glyph;
    el.classList.toggle('active', p < 15 && !b.isCharging);
    label.textContent = `${p}%`;
    label.classList.toggle('hidden', !showPct);
    el.title = `${p}%${b.isCharging ? ', charging' : ''}`;
  }
  el.addEventListener('click', () => ctx.omawin('menu system'));
  el.addEventListener('contextmenu', () => { showPct = !showPct; update(ctx.output()); });
  return { el, update };
}
