// The clock, after Omarchy's omarchy.clock: "Saturday 22:58" (bar.json's clock.format). Right
// click: the other format, the date and week ("17 October W42 2026"); click: Windows' calendar
// (the notification center, where it lives on Windows 11).
export function create(ctx) {
  const el = ctx.button('clock', '', 'Click: calendar · right click: the date');
  let alt = false;
  let last = '';
  function update(o) {
    if (!o.date) return;
    const [main, other] = o.date.formatted.split('|');
    const text = alt ? other : main;
    if (text !== last) { last = text; el.textContent = text; }
  }
  el.addEventListener('contextmenu', () => { alt = !alt; update(ctx.output()); });
  el.addEventListener('click', () => ctx.glazewm('shell-exec explorer.exe ms-actioncenter:'));
  return { el, update };
}
