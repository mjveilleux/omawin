// Music, podcasts and radio in the bar: an island on the right with one button, a record that spins
// while anything plays; the panel it opens (the record, controls, then Music, Podcasts or Radio:
// their playlists or shows, their tracks, and a box to paste a link; Music first); and the flowing waves in the middle
// of the bar while anything plays. The player is omawin-music (music/ in the repo):
// `omawin-music watch` says what it's doing, one line at a time (S state, P collections,
// L level), and every button runs `omawin-music <command>`.

const icon = {
  play: '\u{F040A}',
  pause: '\u{F03E4}',
  next: '\u{F04AD}',
  prev: '\u{F04AE}',
  shuffle: '\u{F049D}',
  link: '\u{F0337}',
  back: '\u{F0141}',
  podcast: '\u{F0994}',
  music: '\u{F0387}',
  radio: '\u{F0439}',
  suno: '\u{F0674}',
  star: '\u{F04CE}',
  starOff: '\u{F04D2}',
  station: '\u{F0F3A}',
  create: '\u{F0415}',
};
// Your stars for what's playing (music/Ratings.cs); a skip is not a rating.
const STARS = ['', 'never again', 'not for me', 'fine', 'like it', 'love it'];
const KINDS = {
  music: { label: 'Music', paste: 'Paste a playlist or video link, Enter', empty: 'Paste a YouTube playlist, a mix, or one long video below.' },
  podcasts: { label: 'Podcasts', paste: 'Paste an RSS or Apple Podcasts link, Enter', empty: "Paste a podcast's RSS feed, its Apple Podcasts link, or a YouTube playlist below." },
  radio: { label: 'Radio', paste: "Paste a station's stream link, Enter", empty: "Paste a station's stream link (an .mp3, .aac or .ogg address) below." },
  // Suno plays in Suno's own player (music/suno): its window opens beside this panel on this tab.
  suno: { label: 'Suno', paste: 'Paste a Suno playlist or song link, Enter', empty: "Paste a Suno playlist or song link below. It plays in Suno's own player, in the window beside this panel (sign in there for your private ones)." },
};
const TAB_ICON = { music: icon.music, podcasts: icon.podcast, radio: icon.radio, suno: icon.suno };

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const time = s => {
  s = Math.max(0, Math.floor(s || 0));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${sec}` : `${m}:${sec}`;
};
const day = d => (d ? new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) : '');
const hex = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));

export function startMusic({ zebar, fitWindow, onChange }) {
  const $ = id => document.getElementById(id);
  let st = { running: false }; // the player's state, as it last said
  let stAt = performance.now(); // when it said it (the progress bar moves on in between)
  let collections = [];
  const tracks = {}; // slug -> { count, updated, list }, only while the panel is open
  let tab = 'music';
  const selected = { music: null, podcasts: null, radio: null };
  let open = false;
  let sunoCreate = false; // the Suno window is big, on Suno's create page (Make a song)
  let aurora = [];

  const run = (...args) => zebar.shellExec('omawin-music', args).catch(err => console.error('omawin-music', err));
  const playing = () => st.running && st.state === 'playing';
  const active = () => st.running && st.state !== 'stopped' && !!(st.title || st.state === 'loading');
  const ofKind = kind => collections.filter(c => c.kind === kind);

  // --- the island: the record ----------------------------------------------------------------
  const pill = $('music-pill');
  pill.innerHTML = `<button class="item record-btn" data-open="music" title="Music and podcasts"><span class="mini-disc"></span></button>`;
  const miniDisc = pill.querySelector('.mini-disc');

  const waves = $('waves');
  const wctx = waves.getContext('2d');

  // --- the panel -----------------------------------------------------------------------------
  const panel = document.createElement('div');
  panel.className = 'music-panel drop hidden';
  panel.id = 'music-panel';
  panel.tabIndex = -1; // takes the keyboard when Win+M opens it
  panel.innerHTML = `
    <div class="music-hero">
      <div class="music-backdrop"></div>
      <div class="music-top">
        <div class="disc-wrap"><div class="disc-ring"></div><div class="disc"><div class="art"></div></div></div>
        <div class="music-info">
          <div class="music-kicker"></div>
          <div class="music-title"></div>
          <div class="music-artist"></div>
          <div class="music-stars"></div>
        </div>
      </div>
      <div class="skyline"></div>
      <div class="music-progress"><div class="bar-bg"></div><div class="bar-fill"></div></div>
      <div class="music-times"><span class="pos">0:00</span><span class="dur">0:00</span></div>
      <div class="music-controls">
        <button class="small shuffle" data-music="shuffle" title="Shuffle">${icon.shuffle}</button>
        <button data-music="prev" title="Back">${icon.prev}</button>
        <button class="play" data-music="toggle" title="Play / pause (Space)">${icon.play}</button>
        <button data-music="next" title="Skip">${icon.next}</button>
        <span style="width:34px"></span>
      </div>
    </div>
    <div class="music-library">
      <div class="music-tabs">${Object.entries(KINDS)
        .map(([k, v]) => `<button data-tab="${k}">${TAB_ICON[k]} ${v.label}</button>`)
        .join('')}</div>
      <div class="music-list-head"></div>
      <div class="music-songs"></div>
      <label class="music-add"><span class="link">${icon.link}</span><input spellcheck="false" /></label>
      <div class="music-status"></div>
    </div>`;
  document.body.appendChild(panel);
  const q = sel => panel.querySelector(sel);
  // The bar's window grows with the panel, whatever changed its size (a list arriving, a status).
  new ResizeObserver(() => {
    if (!open) return;
    fitWindow();
    placeSuno(); // the Suno window follows the panel's size
  }).observe(panel);

  const SKY = 44;
  const sky = [];
  for (let i = 0; i < SKY; i++) sky.push(q('.skyline').appendChild(document.createElement('span')));
  const skyHistory = new Array(SKY).fill(0);

  // The theme's aurora (theme.css --aurora-1..5), again after a theme switch. No palette (yet):
  // nothing is painted, rather than colors of a theme that isn't the user's.
  function paint() {
    const css = getComputedStyle(document.documentElement);
    const colors = [1, 2, 3, 4, 5].map(i => css.getPropertyValue(`--aurora-${i}`).trim()).filter(c => /^#[0-9a-f]{6}$/i.test(c));
    if (colors.length < 2) return;
    aurora = colors;
    sky.forEach((el, i) => (el.style.background = gradientAt(i / (SKY - 1))));
  }
  function gradientAt(t) {
    const k = Math.min(Math.floor(t * (aurora.length - 1)), aurora.length - 2);
    const f = t * (aurora.length - 1) - k;
    const [a, b] = [hex(aurora[k]), hex(aurora[k + 1])];
    return `rgb(${a.map((v, i) => Math.round(v + (b[i] - v) * f)).join(',')})`;
  }
  paint();
  document.addEventListener('omawin-theme', paint);

  // --- showing the state ---------------------------------------------------------------------
  let lastArt = null;
  function show() {
    const on = active();
    pill.classList.toggle('active', on);
    pill.classList.toggle('playing', playing());
    pill.querySelector('.record-btn').title = on ? `${st.title ?? ''}${st.artist ? ' — ' + st.artist : ''}` : 'Music and podcasts';
    // The record wears the cover of what's playing, else of the newest playlist.
    const cover = (on && st.thumb) || ofKind('music')[0]?.thumb || '';
    const art = cover ? `url("${cover}")` : '';
    if (art !== lastArt) {
      lastArt = art;
      miniDisc.style.backgroundImage = art;
    }
    const big = on && st.thumb ? `url("${st.thumb}")` : '';
    if (q('.disc .art').style.backgroundImage !== big) {
      q('.disc .art').style.backgroundImage = big;
      q('.music-backdrop').style.backgroundImage = big;
    }
    waves.classList.toggle('hidden', !playing());

    panel.classList.toggle('playing', playing());
    panel.classList.toggle('loading', st.state === 'loading');
    const where = st.state === 'loading' && st.index < 0 ? 'loading' : st.live ? 'live' : st.kind === 'podcasts' ? day(st.published) : st.station ? '' : `${st.index + 1} / ${st.count}`;
    q('.music-kicker').textContent = on ? [st.playlistTitle, where].filter(Boolean).join(' · ') : 'omawin music';
    q('.music-title').textContent = on ? st.title || 'Finding the stream…' : collections.length ? 'Pick something to play' : 'Nothing here yet';
    q('.music-artist').textContent = on ? (st.artist ?? '') + (st.ducked ? ' · quiet while you dictate' : '') : '';
    // Stars: what you gave what's playing (none on a live station). Click one, or press 1-5.
    const rateable = on && !!st.title && !st.live;
    const starsKey = rateable ? String(st.stars || 0) : '';
    const stars = q('.music-stars');
    if (stars.dataset.key !== starsKey) {
      stars.dataset.key = starsKey;
      stars.innerHTML = rateable
        ? [1, 2, 3, 4, 5].map(n => `<button data-star="${n}" class="${n <= (st.stars || 0) ? 'on' : ''}" title="${n}: ${STARS[n]} (key ${n})">${n <= (st.stars || 0) ? icon.star : icon.starOff}</button>`).join('') +
          `<span class="said">${st.stars ? STARS[st.stars] : ''}</span>`
        : '';
    }
    q('.music-controls .play').textContent = playing() || st.state === 'loading' ? icon.pause : icon.play;
    q('.shuffle').classList.toggle('on', !!st.shuffle);
    q('.dur').textContent = st.live ? 'live' : time(st.duration);
    if (st.error) status(st.error, 'error');
    renderLibrary();
    loop();
    onChange?.();
  }

  // Both sections work the same way: first the list of playlists (music: newest added first) or
  // shows (podcasts: newest episode first), then, once you pick one, its tracks under a back
  // button. The playing one is marked. Only drawn while the panel is open.
  let libraryKey = '';
  function renderLibrary() {
    if (!open || renaming) return;
    const mine = ofKind(tab);
    if (!mine.some(c => c.slug === selected[tab])) selected[tab] = null; // gone: back to the list
    const slug = selected[tab];
    const list = tracks[slug]?.list;
    const key = JSON.stringify([tab, mine.map(c => c.slug + c.count + c.updated + c.title), slug, !!list, st.playlist, st.index, st.state, st.station, confirmSlug]);
    if (key === libraryKey) return;
    libraryKey = key;
    panel.querySelectorAll('[data-tab]').forEach(b => b.classList.toggle('selected', b.dataset.tab === tab));
    q('.music-add input').placeholder = KINDS[tab].paste;
    const c = mine.find(x => x.slug === slug);
    q('.music-list-head').innerHTML = c
      ? `<button class="back" data-music="back" title="All ${KINDS[tab].label.toLowerCase()}">${icon.back}</button><span class="name" title="Double-click to rename">${esc(c.title)}</span><button data-music="play-all">${icon.play} Play</button>${
          tab === 'music' ? `<button data-music="shuffle-all">${icon.shuffle} Shuffle</button>` : ''
        }`
      : '';
    const songs = q('.music-songs');
    // Music opens on your station first: songs or jams, endless, from your stars (music/Station.cs).
    const stationRow = tab === 'music' && !c
      ? `<div class="station-row">${['songs', 'jams']
          .map(f => `<button class="show station ${st.station === f && active() ? 'current' : ''}" data-station="${f}" title="Endless ${f}, picked from what you rate 4 and 5 stars">
              <span class="img">${icon.station}</span><span class="t">Your station<small>${f}</small></span></button>`)
          .join('')}</div>`
      : '';
    // Suno opens on Make a song: Suno's create page, in its window made big (again: back to small).
    const createRow = tab === 'suno' && !c
      ? `<div class="station-row"><button class="show station ${sunoCreate ? 'current' : ''}" data-suno-create title="${sunoCreate ? 'Back to the small player' : "Suno's create page, in the window beside this panel (sign in there)"}">
          <span class="img">${sunoCreate ? icon.back : icon.create}</span><span class="t">${sunoCreate ? 'Done making' : 'Make a song'}<small>${sunoCreate ? 'back to the player' : 'with your Suno credits'}</small></span></button></div>`
      : '';
    if (!c && mine.length) {
      const sorted = tab === 'podcasts' ? [...mine].sort((a, b) => new Date(b.latest ?? 0) - new Date(a.latest ?? 0)) : mine;
      songs.innerHTML = stationRow + createRow + sorted
        .map(s => {
          const sub = tab === 'radio' ? 'live' : tab === 'podcasts' ? `${s.latest ? 'latest ' + day(s.latest) + ' · ' : ''}${s.count} episodes` : `${s.count} track${s.count === 1 ? '' : 's'}`;
          return `<button class="show ${s.slug === confirmSlug ? 'confirm' : ''} ${s.slug === st.playlist && active() ? 'current' : ''}" data-slug="${esc(s.slug)}" title="${esc(s.title)}\nRight-click to remove">
            <span class="img">${s.thumb ? `<img src="${esc(s.thumb)}" />` : ''}</span>
            <span class="t">${s.slug === confirmSlug ? 'remove? click' : esc(s.title)}<small>${sub}</small></span></button>`;
        })
        .join('');
    } else if (!c) songs.innerHTML = createRow + `<div class="music-empty">${KINDS[tab].empty} It's saved in Music\\omawin and streamed when you play it.</div>`;
    else if (!list) {
      songs.innerHTML = `<div class="music-empty">…</div>`;
      loadTracks(c);
    } else {
      songs.innerHTML = list
        .map((t, i) => {
          const cur = st.playlist === c.slug && st.index === i;
          // Music: its number and artist. Podcasts: newest first, with the day it came out.
          const n = cur ? (playing() ? icon.play : icon.pause) : tab === 'podcasts' ? '' : i + 1;
          const sub = tab === 'podcasts' ? day(t.published) : t.artist;
          return `<button class="song ${cur ? 'current' : ''}" data-index="${i}" title="${esc(t.title)}">
            <span class="n">${n}</span><span class="t">${esc(t.title)}<small>${esc(sub)}</small></span>
            <span class="d">${t.duration ? time(t.duration) : ''}</span></button>`;
        })
        .join('');
      q('.music-songs .current')?.scrollIntoView({ block: 'nearest' });
    }
    fitWindow();
  }

  async function loadTracks(c) {
    // A podcast more than half an hour old is read again first, for new episodes.
    if (c.kind === 'podcasts' && Date.now() - new Date(c.updated) > 30 * 60 * 1000 && !refreshing.has(c.slug)) {
      refreshing.add(c.slug);
      run('refresh', c.slug).finally(() => refreshing.delete(c.slug));
    }
    const res = await run('tracks', c.slug);
    try {
      const list = JSON.parse(res?.stdout ?? '');
      if (Array.isArray(list) && open) {
        tracks[c.slug] = { count: list.length, updated: c.updated, list };
        libraryKey = '';
        renderLibrary();
      }
    } catch {}
  }
  const refreshing = new Set();

  function status(text, kind = '') {
    const el = q('.music-status');
    el.textContent = text;
    el.className = `music-status ${kind}`;
    if (open) fitWindow();
  }

  // --- animation: only while something plays or the panel is open --------------------------------
  // The bar draws in software (Zebar runs without a GPU), so every frame costs CPU: the waves
  // alone get 12 frames a second (they move slowly), the open panel 20.
  let target = 0; // the speakers' latest level
  let level = 0; // smoothed
  let calm = 0; // smoothed much more, for the waves
  let peak = 0.1; // auto-gain: the loudest lately, decaying
  let running = false;
  let lastShift = 0;
  function loop() {
    if (running || !(playing() || open)) return;
    running = true;
    requestAnimationFrame(frame);
  }
  function frame(now) {
    if (!(playing() || open)) {
      running = false;
      level = 0;
      return;
    }
    // The next frame at this rate (a timer, not every display frame: nothing wakes in between).
    setTimeout(() => requestAnimationFrame(frame), open ? 50 : 83);
    const raw = playing() ? target : 0;
    peak = Math.max(raw, peak * 0.995, 0.05);
    const norm = Math.pow(Math.min(1, raw / peak), 0.9);
    level += (norm - level) * (norm > level ? 0.55 : 0.12);
    calm += (level - calm) * 0.06;
    if (playing()) drawWaves(now);
    if (open) {
      panel.style.setProperty('--level', level.toFixed(3));
      // The skyline scrolls right to left: each bar is a moment, newest on the right.
      if (now - lastShift > 60) {
        skyHistory.push(level);
        skyHistory.shift();
        lastShift = now;
      }
      sky.forEach((el, i) => (el.style.transform = `scaleY(${Math.max(0.04, skyHistory[i]).toFixed(3)})`));
      const pos = Math.min(st.duration || Infinity, (st.position || 0) + (playing() ? (now - stAt) / 1000 : 0));
      // A station has no position worth showing: the bar says LIVE instead of a progress bar.
      q('.bar-fill').style.transform = `scaleX(${!st.live && st.duration ? Math.min(1, pos / st.duration).toFixed(4) : 0})`;
      q('.pos').textContent = st.live ? 'LIVE' : time(pos);
    }
  }

  // Silk ribbons: a few soft sine strands in the aurora's colors, tapered to nothing at both ends,
  // drifting slowly sideways, swelling and settling with the sound (calm on purpose: it's always
  // in view). The glow is a wide faint stroke under each one; a canvas shadow blur cost several
  // times more without a GPU.
  const STRANDS = [
    { f: 1.2, s: 0.45, p: 0, a: 1, wd: 2 },
    { f: 1.7, s: -0.6, p: 1.7, a: 0.8, wd: 1.5 },
    { f: 2.2, s: 0.8, p: 3.1, a: 0.6, wd: 1.2 },
    { f: 0.9, s: -0.3, p: 4.4, a: 0.9, wd: 1 },
  ];
  let stroke = null, strokeKey = '';
  function drawWaves(now) {
    const dpr = window.devicePixelRatio || 1;
    const w = waves.clientWidth, h = waves.clientHeight;
    if (!w || aurora.length < 2) return;
    if (waves.width !== Math.round(w * dpr)) {
      waves.width = Math.round(w * dpr);
      waves.height = Math.round(h * dpr);
      strokeKey = '';
    }
    if (strokeKey !== aurora.join()) {
      strokeKey = aurora.join();
      stroke = wctx.createLinearGradient(0, 0, w, 0);
      aurora.forEach((c, i) => stroke.addColorStop(i / (aurora.length - 1), c));
    }
    const t = now / 1000;
    wctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    wctx.clearRect(0, 0, w, h);
    wctx.globalCompositeOperation = 'lighter';
    wctx.lineCap = 'round';
    wctx.strokeStyle = stroke;
    STRANDS.forEach((k, n) => {
      const amp = (h / 2 - 2) * (0.15 + 0.75 * calm) * k.a;
      const path = new Path2D();
      for (let x = 0; x <= w; x += 4) {
        const u = x / w;
        const env = Math.pow(Math.sin(Math.PI * u), 1.6);
        const y = h / 2 + amp * env * (0.8 * Math.sin(u * k.f * Math.PI * 2 + t * k.s + k.p) + 0.2 * Math.sin(u * 5 * Math.PI - t * 0.9 + n));
        x ? path.lineTo(x, y) : path.moveTo(x, y);
      }
      wctx.lineWidth = k.wd + 4;
      wctx.globalAlpha = 0.08 + 0.1 * calm;
      wctx.stroke(path);
      wctx.lineWidth = k.wd;
      wctx.globalAlpha = 0.35 + 0.5 * k.a;
      wctx.stroke(path);
    });
  }

  // --- opening and closing -------------------------------------------------------------------
  let anchor = null;
  function setOpen(o, kind) {
    open = o;
    if (kind) tab = kind;
    pill.querySelector('.record-btn').classList.toggle('open', o);
    panel.classList.toggle('hidden', !o);
    if (o) {
      libraryKey = '';
      renderLibrary();
      const a = (anchor ?? pill).getBoundingClientRect();
      const left = a.left + a.width / 2 - panel.offsetWidth / 2;
      panel.style.left = `${Math.max(10, Math.min(window.innerWidth - panel.offsetWidth - 10, left))}px`;
      loop();
    } else {
      confirmSlug = null;
      sunoCreate = false; // it reopens small, as the player
      for (const k in tracks) delete tracks[k]; // read again next time; nothing kept while closed
      q('.music-songs').innerHTML = '';
    }
    fitWindow();
    tell();
    placeSuno();
  }

  // --- input ---------------------------------------------------------------------------------
  let confirmSlug = null; // a playlist or show right-clicked once: a click now removes it
  document.addEventListener('click', e => {
    const opener = e.target.closest('#music-pill [data-open]');
    if (opener) {
      anchor = opener;
      return setOpen(!open, 'music');
    }
    if (!open) return;
    if (!e.target.closest('#music-panel')) return setOpen(false);
    const tabBtn = e.target.closest('[data-tab]');
    const btn = e.target.closest('[data-music]');
    const entry = e.target.closest('.show');
    const row = e.target.closest('.song');
    const star = e.target.closest('[data-star]');
    const station = e.target.closest('[data-station]');
    if (star) rate(Number(star.dataset.star));
    else if (e.target.closest('[data-suno-create]')) {
      sunoCreate = !sunoCreate;
      if (sunoCreate) run('suno-create');
      libraryKey = '';
      renderLibrary();
      placeSuno();
    } else if (station) {
      status(`Finding ${station.dataset.station} for your station…`);
      run('station', station.dataset.station).then(() => status(''));
    } else if (tabBtn) {
      tab = tabBtn.dataset.tab;
      sunoCreate = false;
      selected[tab] = null; // a section opens on its list
      confirmSlug = null;
      status('');
      renderLibrary();
      placeSuno();
    } else if (btn) {
      const what = btn.dataset.music;
      if (what === 'back') {
        selected[tab] = null;
        renderLibrary();
      } else if (what === 'play-all') run('play', selected[tab], '0');
      else if (what === 'shuffle-all') (st.shuffle ? Promise.resolve() : run('shuffle')).then(() => run('play', selected[tab]));
      else if (what === 'toggle' && !active()) {
        if (selected[tab]) run('play', selected[tab], '0');
      } else {
        if (what === 'toggle') {
          st.state = playing() ? 'paused' : 'playing'; // show it now; the player confirms
          stAt = performance.now();
          show();
        }
        run(what);
      }
    } else if (entry) {
      if (entry.dataset.slug === confirmSlug) {
        run('remove', confirmSlug);
        confirmSlug = null;
      } else if (tab === 'radio') {
        // A station is one stream: clicking it tunes in, rather than opening a list of one.
        run('play', entry.dataset.slug, '0');
      } else {
        confirmSlug = null;
        selected[tab] = entry.dataset.slug;
      }
      renderLibrary();
    } else if (row) {
      run('play', selected[tab], row.dataset.index);
    } else if (e.target.closest('.music-progress') && st.duration && !st.live) {
      const r = q('.music-progress').getBoundingClientRect();
      st.position = ((e.clientX - r.left) / r.width) * st.duration;
      stAt = performance.now();
      run('seek', st.position.toFixed(1));
    }
  });
  // Double-click the name over the list: name it yourself (Enter keeps it, Esc doesn't).
  let renaming = null;
  panel.addEventListener('dblclick', e => {
    if (!e.target.closest('.music-list-head .name') || !selected[tab]) return;
    const c = collections.find(x => x.slug === selected[tab]);
    const name = q('.music-list-head .name');
    if (!c || !name) return;
    renaming = c.slug;
    const field = document.createElement('input');
    field.className = 'rename-field';
    field.value = c.title;
    field.spellcheck = false;
    name.replaceWith(field);
    field.focus();
    field.select();
    const finish = save => {
      if (renaming !== c.slug) return;
      renaming = null;
      const title = field.value.trim();
      if (save && title && title !== c.title) {
        c.title = title; // shown now; the file follows
        run('rename', c.slug, title);
      }
      libraryKey = '';
      renderLibrary();
    };
    field.addEventListener('keydown', k => {
      k.stopPropagation(); // Esc cancels the rename, not the panel
      if (k.key === 'Enter') finish(true);
      if (k.key === 'Escape') finish(false);
    });
    field.addEventListener('blur', () => finish(true));
  });

  panel.addEventListener('contextmenu', e => {
    const entry = e.target.closest('.show');
    if (!entry) return;
    confirmSlug = entry.dataset.slug;
    renderLibrary();
  });
  document.addEventListener('keydown', e => {
    if (!open) return;
    if (e.key === 'Escape') return setOpen(false);
    if (e.target.tagName === 'INPUT') return;
    if (e.key === ' ') { e.preventDefault(); run('toggle'); }
    if (e.key === 'ArrowRight') run('seek', String(Math.round((st.position || 0) + 10)));
    if (e.key === 'ArrowLeft') run('seek', String(Math.max(0, Math.round((st.position || 0) - 10))));
    if (/^[1-5]$/.test(e.key)) rate(Number(e.key));
  });
  // Stars for what's playing; the same stars again take them away. Shown at once, the player
  // confirms (one star also skips it, for good).
  function rate(n) {
    if (!active() || st.live) return;
    n = n === st.stars ? 0 : n;
    st.stars = n;
    show();
    run('rate', String(n));
  }
  // Losing the keyboard closes the panel, except on the Suno tab: clicking into the Suno window
  // beside it takes the keyboard there. Esc, the record or Win+M close it then.
  window.addEventListener('blur', () => open && !adding && tab !== 'suno' && setOpen(false));
  // Another drop-down opening (the agents' panel) closes this one: one open at a time (bar.html).
  document.addEventListener('drop-open', () => open && setOpen(false));

  // The Suno tab: Suno's own player (music/suno) in its window, beside this panel while the tab is
  // open, out of sight otherwise (it plays on). In screen pixels: this window's place plus the
  // panel's, on the panel's left (its right when there's no room). Nothing starts the Suno window
  // until the tab is first opened.
  let sunoAt = '';
  async function placeSuno() {
    let want = '';
    if (open && tab === 'suno') {
      try {
        const pos = await zebar.currentWidget().tauriWindow.outerPosition();
        const dpr = window.devicePixelRatio || 1;
        const r = panel.getBoundingClientRect();
        // Making a song wants room: Suno's create page, as wide and tall as fits.
        const top = r.top, fit = window.screen.availHeight - top - 20;
        const w = Math.round((sunoCreate ? Math.max(460, Math.min(1000, r.left - 30)) : 460) * dpr);
        const h = Math.round((sunoCreate ? Math.max(fit, 600) : Math.max(r.height, 600)) * dpr);
        const gap = Math.round(10 * dpr);
        let x = Math.round(pos.x + r.left * dpr) - w - gap;
        if (x < pos.x) x = Math.round(pos.x + r.right * dpr) + gap;
        want = `${x} ${Math.round(pos.y + r.top * dpr)} ${w} ${h}`;
      } catch (err) {
        console.error('suno: placing its window', err);
      }
    }
    if (want === sunoAt) return;
    sunoAt = want;
    if (want) run('suno-show', ...want.split(' '));
    else run('suno-hide');
  }

  // Paste a link, Enter: the player reads it (a few seconds for a long playlist), checks it's
  // what this tab holds, and saves it.
  let adding = false;
  const input = q('.music-add input');
  input.addEventListener('keydown', async e => {
    if (e.key !== 'Enter') return;
    const url = input.value.trim();
    if (!/^https?:\/\/\S+$/.test(url)) return status("That doesn't look like a link", 'error');
    adding = true;
    status('Reading the link…', 'busy');
    const res = await run(...(tab === 'podcasts' ? ['add', 'podcasts', url] : tab === 'radio' ? ['add', 'radio', url] : tab === 'suno' ? ['add', 'suno', url] : ['add', url]));
    adding = false;
    let out = null;
    try { out = JSON.parse(res?.stdout?.trim().split('\n').pop() ?? ''); } catch {}
    if (out?.ok) {
      input.value = '';
      // A station stays on the list (there is nothing inside it to open); a playlist opens.
      if (tab !== 'radio') selected[tab] = out.slug;
      status(`Added ${out.title}${tab === 'radio' ? '' : ` · ${out.count} ${tab === 'podcasts' ? 'episode' : 'track'}${out.count === 1 ? '' : 's'}`}`, 'ok');
    } else status(out?.error || "Couldn't read that link", 'error');
  });

  // Win+M (omawin music open): the panel opens on Music with the keyboard in it, so Space plays
  // or pauses straight away; again closes it. With several monitors, each bar has its own feed
  // and tells it whether its panel is open and whether it's on GlazeWM's focused monitor;
  // `omawin-music open` reads that and says "O close" to every bar with a panel open, or else
  // "O open" to the focused monitor's bar only (music/Watch.cs).
  let feed = null, focused = false, told = '';
  function tell() {
    const says = `panel ${open ? 1 : 0}\nfocus ${focused ? 1 : 0}\n`;
    if (!feed || says === told) return;
    told = says;
    // Zebar 3.3's own write() sends `processId` where its backend wants `pid` (and always
    // fails), so the backend command is called directly while that's so.
    const to = feed;
    const tauri = window.__TAURI_INTERNALS__;
    Promise.resolve()
      .then(() => (tauri ? tauri.invoke('shell_write', { pid: to.processId, buffer: says }) : to.write(says)))
      .catch(err => console.error('omawin-music watch: write', err));
  }
  async function fromKey(what) {
    if (what === 'close') return void (open && setOpen(false));
    if (open) return;
    try { await zebar.currentWidget().tauriWindow.setFocus(); } catch {}
    anchor = pill.querySelector('.record-btn');
    setOpen(true, 'music');
    panel.focus();
  }

  // --- the feed ------------------------------------------------------------------------------
  async function watch() {
    try {
      const proc = await zebar.shellSpawn('omawin-music', 'watch');
      feed = proc;
      told = '';
      tell();
      proc.onStdout(line => {
        line = String(line).trim();
        const kind = line[0], body = line.slice(2);
        if (kind === 'L') return void (target = parseFloat(body) || 0);
        if (kind === 'O') return void fromKey(body);
        try {
          if (kind === 'S') {
            st = JSON.parse(body);
            stAt = performance.now();
          } else if (kind === 'P') {
            collections = JSON.parse(body);
            // Changed ones (a refresh, a new add) are read again.
            for (const c of collections) if (tracks[c.slug] && (tracks[c.slug].count !== c.count || tracks[c.slug].updated !== c.updated)) delete tracks[c.slug];
          }
          show();
        } catch {}
      });
      proc.onExit(() => {
        feed = null;
        st = { running: false };
        show();
        setTimeout(watch, 3000); // again if it stops (a rebuild stops it)
      });
    } catch (err) {
      console.error('omawin-music unavailable', err);
      setTimeout(watch, 30000);
    }
  }
  watch();
  show();

  return {
    // The bar's middle shows the waves instead of the title of what this player plays.
    isOurs: session => st.running && !!st.title && session?.title === st.title,
    // Whether this bar sits on GlazeWM's focused monitor (bar.html, from its glazewm provider).
    setFocused: on => {
      focused = !!on;
      tell();
    },
  };
}
