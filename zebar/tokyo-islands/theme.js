// Follows a theme switch without restarting Zebar. themes/render.ps1 rewrites theme.css (the
// picked theme's colors); theme-watch (~/.local/bin, built from theme-watch.cs) says when, and
// this reads it again and swaps the new colors in: they show ~0.1 s after the file is written.
// A restart of the bar and dock took ~3 s, with both gone meanwhile.
// Pages that compute something from the colors listen for the 'omawin-theme' event.
// Zebar's client from disk, as bar.html loads it (esm.sh only where it was never built).
const zebar = await import('./lib/zebar.js').catch(() => import('https://esm.sh/zebar@3.3'));

const live = document.createElement('style');
document.head.appendChild(live); // after theme.css's <link>, so it wins
let last = null;

async function reload() {
  try {
    const response = await fetch('./theme.css', { cache: 'no-store' });
    if (!response.ok) return;
    const css = await response.text();
    if (css === last) return;
    last = css;
    live.textContent = css;
    document.dispatchEvent(new Event('omawin-theme'));
  } catch {
    // The file mid-swap: the next change, or the check below, picks it up.
  }
}

async function watch() {
  try {
    const proc = await zebar.shellSpawn('theme-watch');
    proc.onStdout(() => reload());
    // Restarted if it ever stops; a read meanwhile catches a switch it missed.
    proc.onExit(() => setTimeout(() => { reload(); watch(); }, 3000));
  } catch (err) {
    // No theme-watch (not built yet): look every 2 s instead.
    console.error('theme-watch unavailable', err);
    setInterval(reload, 2000);
  }
}

// The first read is applied too: Zebar's cache can hand the <link> an older copy.
reload();
watch();
