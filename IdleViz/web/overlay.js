import { NO_TRACK_GRACE_MS, formatTime, fraction, layoutFor, parseNowPlaying, positionAt } from "./overlay-state.js";

const STAGE_WIDTH = 1920;
const NOTE_PATH = "M9 18V5.5l12-2.5v12.5a3 3 0 1 1-2-2.83V7.4l-8 1.66v9.44a3 3 0 1 1-2-2.83z";

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

function fitStage() {
  stage.style.transform = `scale(${window.innerWidth / STAGE_WIDTH})`;
}

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
  text.append(title, artist);

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
  const position = positionAt(current, receivedAt, performance.now());
  played.style.transform = `scaleX(${fraction(position, current.durationMs)})`;
  elapsedLabel.textContent = formatTime(position);
  durationLabel.textContent = formatTime(current.durationMs / 1000);
}

function render() {
  const layout = layoutFor(current);
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

Object.assign(window, { nowPlaying });
