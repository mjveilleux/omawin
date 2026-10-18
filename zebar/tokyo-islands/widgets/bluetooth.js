// Bluetooth, after Omarchy's omarchy.bluetooth. Zebar can't read the radio, so this shows the
// Bluetooth glyph and opens Quick settings (where the radio and paired devices are: Win+A).
const BT = '\u{F00AF}'; // 󰂯

export function create(ctx) {
  const el = ctx.button('bluetooth', `<span class="icon">${BT}</span>`, 'Bluetooth');
  el.addEventListener('click', () => ctx.omawin('launcher quick'));
  return { el };
}
