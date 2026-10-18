// Media: omawin's music player (music.js), Omarchy's optional media widget. Its record shows
// while it plays; click it for the player (Win+M too).
import { startMusic } from '../music.js';

export function create(ctx) {
  const el = document.createElement('div');
  el.className = 'media';
  el.innerHTML = `<div id="music-pill"></div><canvas id="waves" class="hidden"></canvas>`;
  let music = null;
  // music.js finds its elements by id: they're in the page once this is placed, so start after.
  queueMicrotask(() => {
    music = startMusic({ zebar: ctx.zebar, fitWindow: ctx.fitWindow, onChange: () => {} });
  });
  function update(o) {
    const g = o.glazewm;
    if (music && g?.currentMonitor && g?.focusedMonitor) music.setFocused(g.currentMonitor.id === g.focusedMonitor.id);
  }
  return { el, update };
}
