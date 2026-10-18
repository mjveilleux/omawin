// The menu button, after Omarchy's omarchy.menu: its logo (the omarchy icon font). Click: the
// system menu; right click: a terminal.
export function create(ctx) {
  const el = ctx.button('menu', '<span class="logo"></span>', 'Menu (right click: a terminal)');
  el.addEventListener('click', () => ctx.omawin('menu'));
  el.addEventListener('contextmenu', () => ctx.omawin('launch terminal'));
  return { el };
}
