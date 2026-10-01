// Pure overlay logic, kept free of the DOM so it runs under Vitest.

/** How long "no track" has to last before the overlay hides. Spotify briefly reports
 *  no track while switching to a new album or playlist. */
export const NO_TRACK_GRACE_MS = 1500;
/** Matches `--viz-brightness` in overlay.css. */
export const DEFAULT_BRIGHTNESS = 0.7;

/**
 * @typedef {object} NowPlaying
 * @property {string} id          Spotify URL, used to spot track changes.
 * @property {"playing" | "paused"} state
 * @property {"song" | "podcast" | "musicAd" | "podcastAd"} content
 * @property {string} title
 * @property {string} artist
 * @property {string | null} artwork   `data:` URL, or null.
 * @property {boolean} artworkPending  True while Swift is still downloading it.
 * @property {number} durationMs
 * @property {number} position         Seconds, when Swift read it.
 */

/**
 * Which layout to show.
 * - "full": art, title, artist and progress (song, playing)
 * - "progress": progress row only (song, paused)
 * - "ad": "Advertisement" label and progress (ad between songs)
 * - "none": nothing (podcasts, podcast ads, no track)
 * @param {NowPlaying | null} item
 * @param {boolean} [overlayEnabled]  The "Show Spotify overlay" setting. Off means nothing in any state.
 * @returns {"full" | "progress" | "ad" | "none"}
 */
export function layoutFor(item, overlayEnabled = true) {
  if (!item || !overlayEnabled) return "none";
  switch (item.content) {
    case "song":
      return item.state === "paused" ? "progress" : "full";
    case "musicAd":
      return "ad";
    default:
      return "none";
  }
}

/**
 * Current position in seconds, advanced locally from the last reading while playing.
 * @param {NowPlaying} item
 * @param {number} receivedAt  `performance.now()` when the reading arrived.
 * @param {number} now         `performance.now()`.
 * @param {number} [audioDelay]  Seconds the speakers lag behind Spotify (Bluetooth, AirPlay). While
 *   playing, what you hear is that far behind the position Spotify reports.
 */
export function positionAt(item, receivedAt, now, audioDelay = 0) {
  const playing = item.state === "playing";
  const elapsed = playing ? Math.max(0, now - receivedAt) / 1000 : 0;
  const lag = playing && Number.isFinite(audioDelay) ? Math.max(0, audioDelay) : 0;
  const position = Math.max(0, item.position + elapsed - lag);
  const duration = item.durationMs / 1000;
  return duration > 0 ? Math.min(position, duration) : position;
}

/** Played fraction, 0..1. A zero duration (podcasts right after they start) reads as 0. */
export function fraction(position, durationMs) {
  if (!(durationMs > 0)) return 0;
  return Math.min(1, Math.max(0, (position * 1000) / durationMs));
}

/** "00:17", "03:48", or "1:02:03" past an hour, as on the TV. */
export function formatTime(seconds) {
  const total = Math.max(0, Math.floor(Number.isFinite(seconds) ? seconds : 0));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

/**
 * Checks the shape of a `nowPlaying` payload from Swift. Returns null for anything else.
 * @param {unknown} value
 * @returns {NowPlaying | null}
 */
export function parseNowPlaying(value) {
  if (!value || typeof value !== "object") return null;
  const item = /** @type {Record<string, unknown>} */ (value);
  const text = (v) => (typeof v === "string" ? v : "");
  const number = (v) => (typeof v === "number" && Number.isFinite(v) ? v : 0);
  const state = item.state === "paused" ? "paused" : "playing";
  const contents = ["song", "podcast", "musicAd", "podcastAd"];
  if (!contents.includes(/** @type {string} */ (item.content))) return null;
  const artwork = typeof item.artwork === "string" && item.artwork.startsWith("data:image/") ? item.artwork : null;
  return {
    id: text(item.id),
    state,
    content: /** @type {NowPlaying["content"]} */ (item.content),
    title: text(item.title),
    artist: text(item.artist),
    artwork,
    artworkPending: item.artworkPending === true && !artwork,
    durationMs: number(item.durationMs),
    position: number(item.position),
  };
}

/**
 * The brightness setting as a number the dim layer can use: 0.5 to 1, or the default for anything else.
 * @param {unknown} value
 */
export function clampBrightness(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return DEFAULT_BRIGHTNESS;
  return Math.min(Math.max(value, 0.5), 1);
}
