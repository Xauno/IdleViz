// Stand-in audio until the Spotify tap arrives (step 7b): a synthetic loop with a kick,
// a bass note and a hi-hat, so presets have a beat to react to. Free of the DOM.

export const SAMPLE_COUNT = 1024;
export const SAMPLE_RATE = 48_000;
const BEAT_SECONDS = 0.5; // 120 BPM
const BASS_NOTES_HZ = [55, 55, 65.4, 49];
const pair = [0, 0];

/** Deterministic noise in -1..1 for a sample index, so the same time always gives the same frame. */
function noise(n) {
  const x = Math.sin(n * 12.9898) * 43758.5453;
  return (x - Math.floor(x)) * 2 - 1;
}

/**
 * One sample of the loop.
 * @param {number} t  Seconds.
 * @param {number[]} [out]  Receives left and right, each within -1..1. Pass one to avoid allocating.
 */
export function sampleAt(t, out = [0, 0]) {
  const beat = t / BEAT_SECONDS;
  const inBeat = (beat - Math.floor(beat)) * BEAT_SECONDS;
  // Kick: a sine that drops in pitch and dies away within the beat.
  const kick = Math.sin(2 * Math.PI * (50 * inBeat + 20 * (1 - Math.exp(-inBeat * 30)))) * Math.exp(-inBeat * 9);
  // Bass: one note per bar of four beats.
  const note = BASS_NOTES_HZ[Math.floor(beat / 4) % BASS_NOTES_HZ.length];
  const bass = Math.sin(2 * Math.PI * note * t) * 0.25;
  // Hi-hat on the off-beat.
  const off = inBeat - BEAT_SECONDS / 2;
  const hat = off >= 0 ? noise(Math.round(t * SAMPLE_RATE)) * Math.exp(-off * 60) * 0.3 : 0;
  // A slow melody line, panned by a slow sweep so left and right differ.
  const lead = Math.sin(2 * Math.PI * 440 * t) * 0.12 * (0.5 + 0.5 * Math.sin(2 * Math.PI * 0.25 * t));
  const pan = 0.5 + 0.4 * Math.sin(2 * Math.PI * 0.1 * t);
  const centre = kick * 0.6 + bass;
  const left = centre + (hat + lead) * (1 - pan);
  const right = centre + (hat + lead) * pan;
  out[0] = Math.max(-1, Math.min(1, left));
  out[1] = Math.max(-1, Math.min(1, right));
  return out;
}

/** A float sample as the unsigned byte Butterchurn expects (128 is silence). */
export function toByte(sample) {
  return Math.max(0, Math.min(255, Math.round(sample * 127) + 128));
}

/**
 * Fills Butterchurn's three time-domain arrays with the samples that end at time `t`.
 * @param {{ timeByteArray: Uint8Array, timeByteArrayL: Uint8Array, timeByteArrayR: Uint8Array }} levels
 * @param {number} t  Seconds.
 */
export function fillFakeAudio(levels, t) {
  const start = t - SAMPLE_COUNT / SAMPLE_RATE;
  for (let i = 0; i < SAMPLE_COUNT; i++) {
    sampleAt(start + i / SAMPLE_RATE, pair);
    levels.timeByteArrayL[i] = toByte(pair[0]);
    levels.timeByteArrayR[i] = toByte(pair[1]);
    levels.timeByteArray[i] = toByte((pair[0] + pair[1]) / 2);
  }
}
