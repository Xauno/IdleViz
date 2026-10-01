import { createAudioState, decodeAudioFrame } from "./audio-frame.js";
import {
  BLEND_SECONDS,
  Rotation,
  SECONDS_PER_PRESET,
  ShuffleBag,
  collectPresets,
  renderSize,
  shouldRender,
} from "./visualizer-state.js";

const PACKS = ["butterchurnPresets", "butterchurnPresetsExtra", "butterchurnPresetsExtra2", "butterchurnPresetsMD1"];
/** After a failed build (no WebGL right after wake, for example), wait this long before trying again. */
const REBUILD_WAIT_MS = 1000;
/** The longest step a single frame may take, so reopening the window doesn't jump time forward. */
const MAX_FRAME_SECONDS = 0.1;

const host = /** @type {HTMLElement} */ (document.getElementById("viz"));
const globals = /** @type {any} */ (window);
const butterchurn = globals.butterchurn?.default ?? globals.butterchurn;

const presets = collectPresets(PACKS.map((pack) => globals[pack]?.getPresets?.()));
const byId = new Map(presets.map((entry) => [entry.id, entry]));
const shuffle = new ShuffleBag(presets.map((entry) => entry.id));
const rotation = new Rotation(SECONDS_PER_PRESET);
/** Presets that threw while loading; skipped for the rest of the session. */
const failed = new Set();

// The only audio the visuals ever see: frames from Swift's tap on Spotify, silence until one arrives.
const audioState = createAudioState();
// Butterchurn only reads the sample rate from this context, to place its bass, mid and treble
// ranges. It stays suspended and unconnected: the audio arrives as plain arrays through
// render({ audioLevels }).
let audioContext = new AudioContext({ sampleRate: audioState.sampleRate });

/** @type {HTMLCanvasElement | null} */
let canvas = null;
/** @type {any} */
let visualizer = null;
/** @type {import("./visualizer-state.js").PresetEntry | null} */
let current = null;
let lastRender = 0;
let nextBuildAt = 0;
let frames = 0;
let audioFrames = 0;

function targetSize() {
  // The web view has no size until the window first opens; build for a common one meanwhile.
  return renderSize(window.innerWidth || 1920, window.innerHeight || 1080, window.devicePixelRatio);
}

/** Creates a fresh canvas and Butterchurn instance, and shows the current preset again. */
function build() {
  canvas?.remove();
  visualizer = null;
  canvas = document.createElement("canvas");
  canvas.addEventListener("webglcontextlost", onContextLost);
  host.append(canvas);
  const { width, height } = targetSize();
  canvas.width = width;
  canvas.height = height;
  visualizer = butterchurn.createVisualizer(audioContext, canvas, { width, height, pixelRatio: 1 });
  if (!show(current, 0)) showNext(0);
}

/** WebGL contexts can be lost after sleep and wake. The old canvas is gone for good, so start over. */
function onContextLost(event) {
  event.preventDefault();
  visualizer = null;
  nextBuildAt = 0;
}

function resize() {
  if (!visualizer || !canvas) return;
  const { width, height } = targetSize();
  if (canvas.width === width && canvas.height === height) return;
  canvas.width = width;
  canvas.height = height;
  visualizer.setRendererSize(width, height);
}

/**
 * @param {import("./visualizer-state.js").PresetEntry | null} entry
 * @param {number} blendSeconds
 * @returns {boolean} False if there was nothing to show or the preset failed to load.
 */
function show(entry, blendSeconds) {
  if (!entry || !visualizer || failed.has(entry.id)) return false;
  try {
    visualizer.loadPreset(entry.preset, blendSeconds);
  } catch (error) {
    failed.add(entry.id);
    console.warn(`Preset failed to load: ${entry.name}`, error);
    return false;
  }
  current = entry;
  rotation.reset();
  return true;
}

function showNext(blendSeconds) {
  // Bounded, so a library where every preset fails can't loop forever.
  for (let tries = 0; tries < presets.length; tries++) {
    const id = shuffle.next();
    if (id !== null && show(byId.get(id) ?? null, blendSeconds)) return;
  }
}

function tick(now) {
  requestAnimationFrame(tick);
  if (!shouldRender(now, lastRender)) return;
  const seconds = lastRender > 0 ? Math.min((now - lastRender) / 1000, MAX_FRAME_SECONDS) : 1 / 60;
  lastRender = now;

  if (!visualizer) {
    if (now < nextBuildAt) return;
    nextBuildAt = now + REBUILD_WAIT_MS;
    try {
      build();
    } catch (error) {
      console.warn("Butterchurn failed to start", error);
      visualizer = null;
      return;
    }
  }

  if (rotation.tick(seconds)) showNext(BLEND_SECONDS);
  visualizer.render({ audioLevels: audioState.levels, elapsedTime: seconds });
  frames++;
}

/**
 * Called by Swift about 60 times a second with one packed, base64-encoded frame.
 * @param {unknown} base64
 */
function audioFrame(base64) {
  if (!decodeAudioFrame(base64, audioState)) return;
  audioFrames++;
  if (audioState.sampleRate !== audioContext.sampleRate) {
    // A different output device can change the tap's rate. Butterchurn reads it once, so start over.
    audioContext.close();
    audioContext = new AudioContext({ sampleRate: audioState.sampleRate });
    visualizer = null;
    nextBuildAt = 0;
  }
}

/** Swift asks the page how it's doing with this; the page has no way to call Swift. */
function idlevizStatus() {
  return { preset: current?.id ?? null, frames, audioFrames, presets: presets.length, failed: [...failed] };
}
Object.assign(window, { audioFrame, idlevizStatus });

if (butterchurn && presets.length > 0) {
  try {
    // Built at load so the shaders are compiled before the window first opens.
    build();
  } catch (error) {
    console.warn("Butterchurn failed to start", error);
    visualizer = null;
  }
  window.addEventListener("resize", resize);
  requestAnimationFrame(tick);
} else {
  console.warn("Butterchurn or its presets are missing; the visualizer stays black");
}
