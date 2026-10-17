// Injected into every Suno page the Suno window (SunoHost.cs) opens, before Suno's own scripts.
// It reports what's playing and works Suno's own player, so omawin's controls and ducking reach
// it. Suno's audio is decrypted by its own code and plays through an ordinary <audio> element.
//   reports: the page's media session (title, artist, cover) and that element (position, length,
//            paused), on the element's own events: a hidden page's timers are slowed down, its
//            media events aren't
//   works:   the page's own media-key handlers (play, pause, next, previous, seek), caught as the
//            page sets them; the element's volume, scaled by omawin's gain (your volume, lowered
//            while you dictate) without touching the volume Suno itself sets
(() => {
  if (window.__omawin) return;
  const o = (window.__omawin = { handlers: {}, media: new Set(), gain: 1 });
  const post = m => window.chrome?.webview?.postMessage(m);

  const ms = navigator.mediaSession;
  if (ms?.setActionHandler) {
    const set = ms.setActionHandler.bind(ms);
    ms.setActionHandler = (action, fn) => {
      if (fn) o.handlers[action] = fn;
      else delete o.handlers[action];
      return set(action, fn);
    };
  }

  // Volume: Suno's own setting times omawin's gain.
  const vol = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'volume');
  const clamp = v => Math.max(0, Math.min(1, v));
  const apply = el => vol.set.call(el, clamp((el.__want ?? 1) * o.gain));
  Object.defineProperty(HTMLMediaElement.prototype, 'volume', {
    configurable: true,
    get() { return this.__want ?? vol.get.call(this); },
    set(v) { this.__want = v; vol.set.call(this, clamp(v * o.gain)); },
  });

  const watch = el => {
    if (o.media.has(el)) return;
    o.media.add(el);
    apply(el);
    for (const ev of ['play', 'pause', 'ended', 'loadedmetadata', 'durationchange', 'emptied']) el.addEventListener(ev, () => report());
    el.addEventListener('timeupdate', () => report(true));
  };
  // Why something doesn't play (the browser refusing, the audio failing) goes to omawin's log.
  const play = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function () {
    watch(this);
    const p = play.apply(this, arguments);
    p?.catch?.(err => post({ ev: 'log', text: `play refused (${this.currentSrc ? 'song' : 'no source'}): ${err?.name}: ${err?.message}` }));
    return p;
  };
  document.addEventListener('error', e => {
    if (e.target instanceof HTMLMediaElement) post({ ev: 'log', text: `audio failed: code ${e.target.error?.code} ${e.target.error?.message || ''}` });
  }, true);

  // The element that matters: the one playing, else the last one that has a song in it.
  const current = () => {
    const els = [...o.media].filter(e => e.duration && isFinite(e.duration) && !/sil-\d+\.mp3/.test(e.currentSrc || ''));
    return els.find(e => !e.paused) || els[els.length - 1];
  };

  let last = 0;
  function report(throttled) {
    const now = Date.now();
    if (throttled && now - last < 900) return;
    last = now;
    const e = current();
    const md = ms?.metadata;
    const art = md?.artwork?.length ? md.artwork[md.artwork.length - 1].src : '';
    post({
      ev: 'state',
      title: md?.title || '',
      artist: md?.artist || '',
      art,
      // The song's id, from its cover's address (image_<id>.jpeg): what omawin rates it by.
      id: (art.match(/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/) || [])[1] || '',
      playing: !!e && !e.paused,
      position: e?.currentTime || 0,
      duration: e?.duration || 0,
      page: location.href,
    });
  }

  o.cmd = (what, arg) => {
    const h = o.handlers;
    const e = current();
    switch (what) {
      case 'play': h.play ? h.play({ action: 'play' }) : e?.play(); break;
      case 'pause': h.pause ? h.pause({ action: 'pause' }) : e?.pause(); break;
      case 'toggle': o.cmd(e && !e.paused ? 'pause' : 'play'); return;
      case 'next': h.nexttrack?.({ action: 'nexttrack' }); break;
      case 'prev': h.previoustrack?.({ action: 'previoustrack' }); break;
      case 'seek': h.seekto ? h.seekto({ action: 'seekto', seekTime: arg }) : e && (e.currentTime = arg); break;
      case 'gain': o.gain = clamp(arg); o.media.forEach(apply); break;
    }
    setTimeout(() => report(), 300);
  };

  // The page's own Play button for what it shows (a playlist or a song): what opening one does.
  o.playPage = () => {
    const buttons = [...document.querySelectorAll('button')];
    const label = b => b.getAttribute('aria-label') || '';
    const b = buttons.find(b => /^play (playlist|song)$/i.test(label(b))) || buttons.find(b => /^play\b/i.test(label(b)) && !/playbar/i.test(label(b)));
    if (!b) return false;
    b.click();
    return true;
  };

  // A heartbeat too (slowed while hidden; the audio's own events carry on).
  setInterval(() => report(), 5000);
})();
