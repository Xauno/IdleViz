// Audio spike: Swift sends one packed frame ~60×/s through window.audioFrame(base64).
// Layout (little-endian), see SpotifyAudioTap.swift:
//   u32 sequence, f32 sampleRate, f32 bass, f32 mid, f32 treble, f32 rms,
//   f32[64] bands, f32[1024] waveform, u8[1024] mono, u8[1024] left, u8[1024] right
const N = 1024;
const HEADER = 24;
const BANDS_OFF = HEADER;
const WAVE_OFF = BANDS_OFF + 64 * 4;
const BYTES_OFF = WAVE_OFF + N * 4;
const FRAME_LEN = BYTES_OFF + 3 * N;

const canvas = /** @type {HTMLCanvasElement} */ (document.getElementById("c"));
const hud = /** @type {HTMLElement} */ (document.getElementById("hud"));
const meter = /** @type {HTMLElement} */ (document.getElementById("meter"));

const levels = {
  timeByteArray: new Uint8Array(N).fill(128),
  timeByteArrayL: new Uint8Array(N).fill(128),
  timeByteArrayR: new Uint8Array(N).fill(128),
};
const bands = new Float32Array(64);
const waveform = new Float32Array(N);
let raw = new Uint8Array(FRAME_LEN);
let rms = 0;
let lastSeq = -1;
let dropped = 0;
let framesIn = 0;
let decodeMs = 0;
let lastHud = {};

// Butterchurn only needs an AudioContext for its analysers, which we bypass with audioLevels.
const bc = /** @type {any} */ (window).butterchurn;
const butterchurn = bc.default ?? bc;
const audioContext = new AudioContext();
const MAX_WIDTH = /** @type {any} */ (window).SPIKE_MAX_WIDTH || 2560;
let visualizer = null;

function size() {
  const scale = Math.min(window.devicePixelRatio, MAX_WIDTH / window.innerWidth);
  const w = Math.round(window.innerWidth * scale);
  const h = Math.round(window.innerHeight * scale);
  canvas.width = w;
  canvas.height = h;
  visualizer?.setRendererSize(w, h);
  return { w, h };
}

// Cycles through a few beat-driven presets to judge how well the visuals follow the music.
const presets = Object.entries(/** @type {any} */ (window).SPIKE_PRESETS);
let presetIndex = -1;
let presetName = "";
function loadNext() {
  presetIndex = (presetIndex + 1) % presets.length;
  const [name, preset] = presets[presetIndex];
  presetName = name;
  visualizer.loadPreset(preset, 2);
}

try {
  const { w, h } = size();
  visualizer = butterchurn.createVisualizer(audioContext, canvas, { width: w, height: h, pixelRatio: 1 });
  loadNext();
  setInterval(loadNext, 15000);
} catch (err) {
  hud.textContent = `butterchurn failed: ${err}`;
}
window.addEventListener("resize", size);

/** @param {string} b64 */
function audioFrame(b64) {
  const t0 = performance.now();
  const bin = atob(b64);
  if (bin.length !== FRAME_LEN) return;
  if (raw.length !== FRAME_LEN) raw = new Uint8Array(FRAME_LEN);
  for (let i = 0; i < FRAME_LEN; i++) raw[i] = bin.charCodeAt(i);
  const view = new DataView(raw.buffer);
  const seq = view.getUint32(0, true);
  if (lastSeq >= 0 && seq > lastSeq + 1) dropped += seq - lastSeq - 1;
  lastSeq = seq;
  rms = view.getFloat32(20, true);
  bands.set(new Float32Array(raw.buffer, BANDS_OFF, 64));
  waveform.set(new Float32Array(raw.buffer, WAVE_OFF, N));
  levels.timeByteArray.set(raw.subarray(BYTES_OFF, BYTES_OFF + N));
  levels.timeByteArrayL.set(raw.subarray(BYTES_OFF + N, BYTES_OFF + 2 * N));
  levels.timeByteArrayR.set(raw.subarray(BYTES_OFF + 2 * N, BYTES_OFF + 3 * N));
  framesIn++;
  decodeMs += performance.now() - t0;
}
/** @type {any} */ (window).audioFrame = audioFrame;

// Render at most ~60 fps, also on 120 Hz displays.
let lastRender = 0;
let rendered = 0;
let hudAt = performance.now();
let rafs = 0;
let renderMs = 0;
let bcMin = Infinity;
let bcMax = -Infinity;
let lastRaf = 0;
let gaps = [0, 0, 0, 0];
const NO_CAP = Boolean(/** @type {any} */ (window).SPIKE_NO_CAP);
function tick(now) {
  requestAnimationFrame(tick);
  rafs++;
  const gap = now - lastRaf;
  lastRaf = now;
  gaps[gap < 12 ? 0 : gap < 20 ? 1 : gap < 40 ? 2 : 3]++;
  // Skip only frames that come much sooner than 60 Hz (every other one at 120 Hz).
  // A strict 16.4 ms threshold dropped ~1 in 3 frames on a 60 Hz display because of timestamp jitter.
  if (!NO_CAP && now - lastRender < 12) return;
  lastRender = now;
  if (visualizer) {
    const r0 = performance.now();
    visualizer.render({ audioLevels: levels });
    renderMs += performance.now() - r0;
    const b = visualizer.renderer.audioLevels.bass;
    bcMin = Math.min(bcMin, b);
    bcMax = Math.max(bcMax, b);
    meter.style.width = `${Math.round(bands[2] * 300)}px`;
    rendered++;
  }
  if (now - hudAt >= 1000) {
    const secs = (now - hudAt) / 1000;
    hud.textContent =
      `${presetName}\n` +
      `render ${(rendered / secs).toFixed(0)} fps · frames in ${(framesIn / secs).toFixed(0)}/s · dropped ${dropped}\n` +
      `decode ${(decodeMs / Math.max(framesIn, 1)).toFixed(3)} ms · rms ${rms.toFixed(3)} · bass ${bands[2].toFixed(2)}\n` +
      `canvas ${canvas.width}×${canvas.height} · ctx ${audioContext.sampleRate} Hz`;
    lastHud = {
      fps: +(rendered / secs).toFixed(1),
      framesIn: +(framesIn / secs).toFixed(1),
      decodeMs: +(decodeMs / Math.max(framesIn, 1)).toFixed(3),
      canvas: `${canvas.width}x${canvas.height}`,
      ctxRate: audioContext.sampleRate,
      bass: +bands[2].toFixed(2),
      raf: +(rafs / secs).toFixed(1),
      bcBass: `${bcMin.toFixed(2)}..${bcMax.toFixed(2)}`,
      gaps: `<12:${gaps[0]} 12-20:${gaps[1]} 20-40:${gaps[2]} >40:${gaps[3]}`,
      renderMs: +(renderMs / Math.max(rendered, 1)).toFixed(2),
    };
    rafs = 0;
    bcMin = Infinity;
    bcMax = -Infinity;
    gaps = [0, 0, 0, 0];
    renderMs = 0;
    rendered = 0;
    framesIn = 0;
    decodeMs = 0;
    hudAt = now;
  }
}
requestAnimationFrame(tick);

// Swift polls this to log page-side numbers next to its own.
/** @type {any} */ (window).spikeStats = () => ({ dropped, lastSeq, rms: +rms.toFixed(4), ...lastHud });
