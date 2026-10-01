// Unpacks the audio frame Swift sends about 60 times a second. Free of the DOM, so it runs under Vitest.
// Layout (little-endian), packed by AudioFrame.swift:
//   u32 sequence, f32 sampleRate, f32 bass, f32 mid, f32 treble, f32 rms,
//   f32[64] bands, f32[1024] waveform, u8[1024] mono, u8[1024] left, u8[1024] right

export const SAMPLE_COUNT = 1024;
export const BAND_COUNT = 64;
export const DEFAULT_SAMPLE_RATE = 48_000;
const HEADER_LENGTH = 24;
const BANDS_OFFSET = HEADER_LENGTH;
const WAVEFORM_OFFSET = BANDS_OFFSET + BAND_COUNT * 4;
const BYTES_OFFSET = WAVEFORM_OFFSET + SAMPLE_COUNT * 4;
export const FRAME_LENGTH = BYTES_OFFSET + 3 * SAMPLE_COUNT;

/**
 * @typedef {object} AudioState
 * @property {number} sequence     Frame counter from Swift, or -1 before the first frame.
 * @property {number} sampleRate
 * @property {{ timeByteArray: Uint8Array, timeByteArrayL: Uint8Array, timeByteArrayR: Uint8Array }} levels
 *   Butterchurn's input: time-domain samples as bytes, 128 being silence.
 * @property {{ bands: Float32Array, waveform: Float32Array, bass: number, mid: number, treble: number, rms: number }} audio
 *   What plugins get (see docs/custom-visualizer.md). The arrays are reused every frame.
 * @property {Uint8Array} raw      Scratch space for decoding.
 */

/**
 * Silence, with every array allocated once.
 * @returns {AudioState}
 */
export function createAudioState() {
  return {
    sequence: -1,
    sampleRate: DEFAULT_SAMPLE_RATE,
    levels: {
      timeByteArray: new Uint8Array(SAMPLE_COUNT).fill(128),
      timeByteArrayL: new Uint8Array(SAMPLE_COUNT).fill(128),
      timeByteArrayR: new Uint8Array(SAMPLE_COUNT).fill(128),
    },
    audio: {
      bands: new Float32Array(BAND_COUNT),
      waveform: new Float32Array(SAMPLE_COUNT),
      bass: 0,
      mid: 0,
      treble: 0,
      rms: 0,
    },
    raw: new Uint8Array(FRAME_LENGTH),
  };
}

const finite = (value) => (Number.isFinite(value) ? value : 0);
const unit = (value) => Math.min(1, Math.max(0, finite(value)));

/**
 * Decodes one base64 frame into `state`, reusing its arrays.
 * @param {unknown} base64
 * @param {AudioState} state
 * @returns {boolean} False, leaving `state` as it was, if the frame isn't the expected size.
 */
export function decodeAudioFrame(base64, state) {
  if (typeof base64 !== "string") return false;
  let binary;
  try {
    binary = atob(base64);
  } catch {
    return false;
  }
  if (binary.length !== FRAME_LENGTH) return false;
  const raw = state.raw;
  for (let i = 0; i < FRAME_LENGTH; i++) raw[i] = binary.charCodeAt(i);

  const view = new DataView(raw.buffer, raw.byteOffset, FRAME_LENGTH);
  state.sequence = view.getUint32(0, true);
  const sampleRate = view.getFloat32(4, true);
  if (sampleRate >= 8000 && sampleRate <= 192_000) state.sampleRate = sampleRate;
  const audio = state.audio;
  audio.bass = unit(view.getFloat32(8, true));
  audio.mid = unit(view.getFloat32(12, true));
  audio.treble = unit(view.getFloat32(16, true));
  audio.rms = unit(view.getFloat32(20, true));
  for (let i = 0; i < BAND_COUNT; i++) audio.bands[i] = unit(view.getFloat32(BANDS_OFFSET + i * 4, true));
  for (let i = 0; i < SAMPLE_COUNT; i++) {
    audio.waveform[i] = Math.min(1, Math.max(-1, finite(view.getFloat32(WAVEFORM_OFFSET + i * 4, true))));
  }
  state.levels.timeByteArray.set(raw.subarray(BYTES_OFFSET, BYTES_OFFSET + SAMPLE_COUNT));
  state.levels.timeByteArrayL.set(raw.subarray(BYTES_OFFSET + SAMPLE_COUNT, BYTES_OFFSET + 2 * SAMPLE_COUNT));
  state.levels.timeByteArrayR.set(raw.subarray(BYTES_OFFSET + 2 * SAMPLE_COUNT, FRAME_LENGTH));
  return true;
}
