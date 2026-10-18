// Workspaces, after Omarchy's omarchy.workspaces: 1 to 5 always, and any others in use, by
// number (10 shows as 0). The one on screen shows its layout instead of its number (a square:
// tiling; a triangle: its focused window floats); empty ones are dimmed. Click one to go there;
// click the one on screen to turn its tiling direction (Omarchy cycles its layout); the wheel
// moves along the row and stops at the ends.
const TILING = '\u{F14FB}'; // 󱓻
const FLOATING = '\u{F0536}'; // 󰔶

export function create(ctx) {
  const el = document.createElement('div');
  el.className = 'workspaces';
  let ids = [];
  let shown = null;
  let lastKey = '';

  function update(o) {
    const wm = o.glazewm;
    if (!wm) return;
    // This monitor's workspaces; numbers only (named ones keep their number as their name).
    const mine = (wm.currentWorkspaces ?? []).filter(w => /^\d+$/.test(w.name));
    const byName = new Map(mine.map(w => [Number(w.name), w]));
    const set = new Set([1, 2, 3, 4, 5, ...byName.keys()]);
    ids = [...set].sort((a, b) => a - b);
    shown = mine.find(w => w.isDisplayed);
    const floating = wm.focusedContainer?.type === 'window' && wm.focusedContainer?.state?.type === 'floating';
    const html = ids
      .map(id => {
        const w = byName.get(id);
        const isShown = shown && Number(shown.name) === id;
        const occupied = w && w.children?.length > 0;
        const label = isShown ? `<span class="glyph">${floating ? FLOATING : TILING}</span>` : id === 10 ? '0' : String(id);
        return `<button class="ws ${occupied || isShown ? '' : 'empty'}" data-id="${id}" title="Workspace ${id}">${label}</button>`;
      })
      .join('');
    if (html !== lastKey) { lastKey = html; el.innerHTML = html; }
  }

  el.addEventListener('click', e => {
    const b = e.target.closest('[data-id]');
    if (!b) return;
    const id = Number(b.dataset.id);
    if (shown && Number(shown.name) === id) ctx.glazewm('toggle-tiling-direction');
    else ctx.glazewm(`focus --workspace ${id}`);
  });
  // A wheel notch moves one along the row, holding at either end rather than wrapping.
  let acc = 0;
  el.addEventListener('wheel', e => {
    acc += e.deltaY;
    if (Math.abs(acc) < 100) return;
    const step = acc > 0 ? 1 : -1;
    acc = 0;
    const i = ids.indexOf(Number(shown?.name));
    if (i < 0) return;
    const next = ids[Math.max(0, Math.min(ids.length - 1, i + step))];
    if (next !== ids[i]) ctx.glazewm(`focus --workspace ${next}`);
  }, { passive: true });
  return { el, update };
}
