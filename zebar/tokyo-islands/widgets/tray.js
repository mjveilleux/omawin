// The tray, after Omarchy's omarchy.tray: a drawer that opens while the mouse is on its arrow,
// the apps' icons in a row. Click an icon: the app's own click; right click: its menu.
const CHEVRON = '\u{F0141}'; // 󰅁 points left: the drawer opens that way

export function create(ctx) {
  const el = document.createElement('div');
  el.className = 'tray';
  el.innerHTML = `<div class="drawer"></div><button class="chev-btn" title="Tray"><span class="chev">${CHEVRON}</span></button>`;
  const drawer = el.querySelector('.drawer');
  let key = '';
  let closeTimer = null;
  el.addEventListener('mouseenter', () => { clearTimeout(closeTimer); el.classList.add('open'); });
  el.addEventListener('mouseleave', () => { closeTimer = setTimeout(() => el.classList.remove('open'), 600); });

  function update(o) {
    const icons = o.systray?.icons ?? [];
    const k = icons.map(i => i.id + ':' + i.iconHash).join(',');
    if (k === key) return;
    key = k;
    drawer.innerHTML = icons
      .map(i => `<button data-tray="${ctx.esc(i.id)}" title="${ctx.esc(String(i.tooltip ?? '').trim())}"><img src="${ctx.esc(i.iconUrl)}" /></button>`)
      .join('');
  }
  drawer.addEventListener('click', e => {
    const b = e.target.closest('[data-tray]');
    if (b) ctx.output().systray?.onLeftClick(b.dataset.tray);
  });
  drawer.addEventListener('contextmenu', e => {
    const b = e.target.closest('[data-tray]');
    if (b) ctx.output().systray?.onRightClick(b.dataset.tray);
  });
  return { el, update };
}
