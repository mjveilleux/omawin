// Audio, after Omarchy's omarchy.audio: the speaker by volume (headphones when the device is
// headphones). Wheel: volume; right click: mute; click: the sound page (speakers, what plays,
// microphones).
const MUTED = '\u{F075F}'; // 󰝟
const LEVELS = ['\u{F057F}', '\u{F0580}', '\u{F057E}']; // 󰕿 󰖀 󰕾
const HEADPHONES = '\u{F02CB}'; // 󰋋

export function create(ctx) {
  const el = ctx.button('audio', `<span class="icon"></span>`, '');
  const icon = el.querySelector('.icon');
  let device = null;
  function update(o) {
    device = o.audio?.defaultPlaybackDevice ?? null;
    if (!device) return;
    const v = Math.round(device.volume ?? 0);
    const muted = device.isMuted || v === 0;
    const glyph = muted ? MUTED : /headphone|headset|earbud|airpods|buds/i.test(device.name ?? '') ? HEADPHONES : LEVELS[v < 34 ? 0 : v < 67 ? 1 : 2];
    if (icon.textContent !== glyph) icon.textContent = glyph;
    el.title = `${device.name ?? 'Sound'} · ${muted ? 'muted' : v + '%'}`;
  }
  el.addEventListener('click', () => ctx.omawin('launcher audio'));
  el.addEventListener('contextmenu', () => {
    const a = ctx.output().audio;
    if (a?.setMute && device) a.setMute(!device.isMuted, { deviceId: device.deviceId });
  });
  el.addEventListener('wheel', e => {
    const a = ctx.output().audio;
    if (!a?.setVolume || !device) return;
    const v = Math.max(0, Math.min(100, Math.round((device.volume ?? 0) + (e.deltaY < 0 ? 5 : -5))));
    a.setVolume(v, { deviceId: device.deviceId });
  }, { passive: true });
  return { el, update };
}
