import {
  NO_TRACK_GRACE_MS,
  clampBrightness,
  formatTime,
  fraction,
  layoutFor,
  likePlacement,
  parseNowPlaying,
  positionAt,
} from "./overlay-state.js";

const STAGE_WIDTH = 1920;
const NOTE_PATH = "M9 18V5.5l12-2.5v12.5a3 3 0 1 1-2-2.83V7.4l-8 1.66v9.44a3 3 0 1 1-2-2.83z";
const HEART_PATH = "M12 21C7 17 3 13 3 8.5a4.5 4.5 0 0 1 9 0 4.5 4.5 0 0 1 9 0C21 13 17 17 12 21z";

const stage = /** @type {HTMLElement} */ (document.getElementById("stage"));
const tracks = /** @type {HTMLElement} */ (document.getElementById("tracks"));
const played = /** @type {HTMLElement} */ (document.getElementById("played"));
const elapsedLabel = /** @type {HTMLElement} */ (document.getElementById("elapsed"));
const durationLabel = /** @type {HTMLElement} */ (document.getElementById("duration"));

/** @type {import("./overlay-state.js").NowPlaying | null} */
let current = null;
let receivedAt = 0;
/** @type {HTMLElement | null} */
let currentTrack = null;
/** @type {ReturnType<typeof setTimeout> | undefined} */
let hideTimer;
/** Seconds the speakers lag behind Spotify, from Swift. */
let audioDelay = 0;
/** The "Show Spotify overlay" setting, from Swift. */
let overlayEnabled = true;

function fitStage() {
  stage.style.transform = `scale(${window.innerWidth / STAGE_WIDTH})`;
}

/** The heart that confirms the like key. It stays hidden until `showLike` starts its animation. */
function makeHeart() {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.classList.add("like");
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", HEART_PATH);
  svg.append(path);
  svg.addEventListener("animationend", () => svg.classList.remove("liked", "unliked"));
  return svg;
}

/** For when the track block isn't showing. It sits outside the stage, which may be hidden. */
const cornerHeart = makeHeart();
cornerHeart.id = "like-corner";
document.body.append(cornerHeart);

function makeTrack(item) {
  const track = document.createElement("div");
  track.className = "track entering";
  track.dataset.id = item.id;

  const art = document.createElement("div");
  art.className = "art";
  const img = document.createElement("img");
  img.alt = "";
  img.addEventListener("load", () => img.classList.add("loaded"));
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.classList.add("note");
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", NOTE_PATH);
  svg.append(path);
  art.append(img, svg);

  const text = document.createElement("div");
  text.className = "text";
  const title = document.createElement("div");
  title.className = "title";
  const artist = document.createElement("div");
  artist.className = "artist";
  const titleRow = document.createElement("div");
  titleRow.className = "title-row";
  titleRow.append(title, makeHeart());
  text.append(titleRow, artist);

  track.append(art, text);
  return track;
}

/** Fills a track block. Text uses textContent, so titles can't inject markup. */
function fillTrack(track, item) {
  /** @type {HTMLElement} */ (track.querySelector(".title")).textContent = item.title;
  /** @type {HTMLElement} */ (track.querySelector(".artist")).textContent = item.artist;
  const art = /** @type {HTMLElement} */ (track.querySelector(".art"));
  const img = /** @type {HTMLImageElement} */ (art.querySelector("img"));
  art.classList.toggle("missing", !item.artwork && !item.artworkPending);
  if (item.artwork && img.getAttribute("src") !== item.artwork) {
    img.classList.remove("loaded");
    img.src = item.artwork;
  } else if (!item.artwork) {
    img.removeAttribute("src");
    img.classList.remove("loaded");
  }
}

/** Crossfades to a new track block when the item changes; updates in place otherwise. */
function showTrack(item) {
  if (currentTrack && currentTrack.dataset.id === item.id) {
    fillTrack(currentTrack, item);
    return;
  }
  const old = currentTrack;
  const next = makeTrack(item);
  fillTrack(next, item);
  tracks.append(next);
  currentTrack = next;
  // Start the fade-in on the next frame so the transition runs from the "entering" state.
  requestAnimationFrame(() => requestAnimationFrame(() => next.classList.remove("entering")));
  if (old) {
    old.classList.add("leaving");
    old.addEventListener("transitionend", () => old.remove(), { once: true });
  }
}

function renderProgress() {
  if (!current) return;
  const position = positionAt(current, receivedAt, performance.now(), audioDelay);
  played.style.transform = `scaleX(${fraction(position, current.durationMs)})`;
  elapsedLabel.textContent = formatTime(position);
  durationLabel.textContent = formatTime(current.durationMs / 1000);
}

function render() {
  const layout = layoutFor(current, overlayEnabled);
  if (layout === "none") {
    // Hide the overlay but keep the last content in place while it fades.
    stage.classList.add("hidden");
    return;
  }
  stage.classList.remove("hidden", "progress-only", "ad");
  if (layout === "progress") stage.classList.add("progress-only");
  if (layout === "ad") stage.classList.add("ad");
  if (layout !== "ad") showTrack(current);
  renderProgress();
}

/**
 * Called by Swift with the current Spotify item, or null when there's no track.
 * @param {unknown} payload
 */
function nowPlaying(payload) {
  const item = parseNowPlaying(typeof payload === "string" ? safeParse(payload) : payload);
  clearTimeout(hideTimer);
  if (!item) {
    // Spotify reports "no track" for a moment when switching context, so wait before hiding.
    hideTimer = setTimeout(() => {
      current = null;
      render();
    }, NO_TRACK_GRACE_MS);
    return;
  }
  current = item;
  receivedAt = performance.now();
  render();
}

function safeParse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

window.addEventListener("resize", fitStage);
fitStage();
// Only the time labels and bar move between readings; a few updates a second is plenty.
setInterval(renderProgress, 250);

/**
 * Called by Swift with the audio delay of the current speakers or headphones.
 * @param {unknown} seconds
 */
function setAudioDelay(seconds) {
  audioDelay = typeof seconds === "number" && Number.isFinite(seconds) ? Math.min(Math.max(seconds, 0), 2.5) : 0;
  renderProgress();
}

/**
 * Called by Swift with the "Show Spotify overlay" setting.
 * @param {unknown} enabled
 */
function setOverlayEnabled(enabled) {
  overlayEnabled = enabled !== false;
  render();
}

/**
 * Called by Swift with the brightness setting. The dim layer's opacity follows the variable.
 * @param {unknown} value
 */
function setBrightness(value) {
  document.documentElement.style.setProperty("--viz-brightness", String(clampBrightness(value)));
}

/**
 * Called by Swift when the like key added the preset on screen to the favorites (a filled heart)
 * or took it off again (an outline).
 * @param {unknown} liked
 */
function showLike(liked) {
  const beside =
    likePlacement(layoutFor(current, overlayEnabled)) === "title" ? currentTrack?.querySelector(".like") : null;
  const heart = beside ?? cornerHeart;
  for (const other of document.querySelectorAll(".like")) other.classList.remove("liked", "unliked");
  // Reading the layout lets the animation start over when the key is pressed again mid-fade.
  heart.getBoundingClientRect();
  heart.classList.add(liked === false ? "unliked" : "liked");
}

Object.assign(window, { nowPlaying, setAudioDelay, setOverlayEnabled, setBrightness, showLike });
