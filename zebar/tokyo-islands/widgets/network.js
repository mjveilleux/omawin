// Network, after Omarchy's omarchy.network: wifi by signal strength, ethernet, or disconnected
// (Omarchy's glyphs). Click: Quick settings (networks, Bluetooth, the sound: Win+A).
const WIFI = ['\u{F092F}', '\u{F091F}', '\u{F0922}', '\u{F0925}', '\u{F0928}']; // 󰤯 󰤟 󰤢 󰤥 󰤨
const ETHERNET = '\u{F0200}'; // 󰈀
const OFF = '\u{F092E}'; // 󰤮

export function create(ctx) {
  const el = ctx.button('network', `<span class="icon"></span>`, '');
  const icon = el.querySelector('.icon');
  el.addEventListener('click', () => ctx.omawin('launcher quick'));
  function update(o) {
    const n = o.network;
    if (!n) return;
    // Past a VPN or a virtual adapter to the real link.
    const up = i => i?.ipv4Addresses?.length && !/virtual|hyper-v|vethernet/i.test(`${i.description} ${i.friendlyName}`);
    const ifaces = n.interfaces ?? [];
    const link = [n.defaultInterface, ...ifaces.filter(i => i.type === 'wifi'), ...ifaces].find(i => (i?.type === 'wifi' || i?.type === 'ethernet') && up(i));
    let glyph = OFF, title = 'Disconnected';
    if (link?.type === 'ethernet') { glyph = ETHERNET; title = 'Ethernet'; }
    else if (link?.type === 'wifi') {
      const s = n.defaultGateway?.signalStrength ?? 100;
      glyph = WIFI[s >= 80 ? 4 : s >= 60 ? 3 : s >= 40 ? 2 : s >= 20 ? 1 : 0];
      title = `${n.defaultGateway?.ssid ?? 'Wi-Fi'} · ${s}%`;
    }
    if (icon.textContent !== glyph) icon.textContent = glyph;
    el.title = title;
  }
  return { el, update };
}
