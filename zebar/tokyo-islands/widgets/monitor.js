// Monitor, after Omarchy's omarchy.monitor: a screen (two with more than one connected). Wheel:
// brightness (omawin brightness, every monitor that reports one); click: Quick settings.
const ONE = '\u{F0379}'; // 󰍹
const MANY = '\u{F037A}'; // 󰍺

export function create(ctx) {
  const el = ctx.button('monitor', `<span class="icon">${ONE}</span>`, 'Wheel: brightness');
  const icon = el.querySelector('.icon');
  function update(o) {
    const n = o.glazewm?.allMonitors?.length ?? 1;
    const glyph = n > 1 ? MANY : ONE;
    if (icon.textContent !== glyph) icon.textContent = glyph;
  }
  el.addEventListener('click', () => ctx.omawin('launcher quick'));
  let busy = false;
  el.addEventListener('wheel', e => {
    if (busy) return;
    busy = true;
    setTimeout(() => (busy = false), 150);
    ctx.omawin(`brightness ${e.deltaY < 0 ? 'up' : 'down'}`);
  }, { passive: true });
  return { el, update };
}
