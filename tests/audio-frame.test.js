import { describe, expect, it } from "vitest";
import {
  BAND_COUNT,
  DEFAULT_SAMPLE_RATE,
  FRAME_LENGTH,
  SAMPLE_COUNT,
  createAudioState,
  decodeAudioFrame,
} from "../IdleViz/web/audio-frame.js";

/** Packs a frame the way AudioFrame.swift does. */
function pack({ sequence = 1, sampleRate = 48000, bass = 0, mid = 0, treble = 0, rms = 0, fill } = {}) {
  const bytes = new Uint8Array(FRAME_LENGTH);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, sequence, true);
  [sampleRate, bass, mid, treble, rms].forEach((value, i) => view.setFloat32(4 + i * 4, value, true));
  bytes.fill(128, 24 + BAND_COUNT * 4 + SAMPLE_COUNT * 4);
  fill?.(view, bytes);
  return Buffer.from(bytes).toString("base64");
}

describe("decodeAudioFrame", () => {
  it("has the size Swift packs", () => {
    expect(FRAME_LENGTH).toBe(7448);
  });

  it("starts as silence", () => {
    const state = createAudioState();
    expect(state.sampleRate).toBe(DEFAULT_SAMPLE_RATE);
    expect([...state.levels.timeByteArray].every((b) => b === 128)).toBe(true);
    expect(state.audio.bands).toHaveLength(64);
    expect(state.audio.waveform).toHaveLength(1024);
  });

  it("unpacks every field", () => {
    const state = createAudioState();
    const frame = pack({
      sequence: 42,
      sampleRate: 44100,
      bass: 0.5,
      mid: 0.25,
      treble: 0.125,
      rms: 0.75,
      fill(view, bytes) {
        view.setFloat32(24, 0.5, true); // band 0
        view.setFloat32(24 + 63 * 4, 1, true); // band 63
        view.setFloat32(280, -0.5, true); // waveform 0
        view.setFloat32(280 + 1023 * 4, 0.25, true); // waveform 1023
        bytes[4376] = 1; // mono 0
        bytes[4376 + 1024] = 2; // left 0
        bytes[4376 + 2048] = 3; // right 0
        bytes[7447] = 250; // right 1023
      },
    });
    expect(decodeAudioFrame(frame, state)).toBe(true);
    expect(state.sequence).toBe(42);
    expect(state.sampleRate).toBe(44100);
    expect(state.audio).toMatchObject({ bass: 0.5, mid: 0.25, treble: 0.125, rms: 0.75 });
    expect([state.audio.bands[0], state.audio.bands[1], state.audio.bands[63]]).toEqual([0.5, 0, 1]);
    expect([state.audio.waveform[0], state.audio.waveform[1023]]).toEqual([-0.5, 0.25]);
    expect(state.levels.timeByteArray[0]).toBe(1);
    expect(state.levels.timeByteArrayL[0]).toBe(2);
    expect(state.levels.timeByteArrayR[0]).toBe(3);
    expect(state.levels.timeByteArrayR[1023]).toBe(250);
    expect(state.levels.timeByteArray[1]).toBe(128);
  });

  it("reuses the same arrays every frame", () => {
    const state = createAudioState();
    const { bands, waveform } = state.audio;
    const { timeByteArray } = state.levels;
    decodeAudioFrame(pack(), state);
    expect(state.audio.bands).toBe(bands);
    expect(state.audio.waveform).toBe(waveform);
    expect(state.levels.timeByteArray).toBe(timeByteArray);
  });

  it("rejects anything that isn't a whole frame and keeps the last one", () => {
    const state = createAudioState();
    decodeAudioFrame(pack({ sequence: 5, bass: 0.5 }), state);
    expect(decodeAudioFrame("", state)).toBe(false);
    expect(decodeAudioFrame("not base64 !!", state)).toBe(false);
    expect(decodeAudioFrame(Buffer.alloc(100).toString("base64"), state)).toBe(false);
    expect(decodeAudioFrame(Buffer.alloc(FRAME_LENGTH + 1).toString("base64"), state)).toBe(false);
    expect(decodeAudioFrame(null, state)).toBe(false);
    expect(decodeAudioFrame(12345, state)).toBe(false);
    expect(state.sequence).toBe(5);
    expect(state.audio.bass).toBe(0.5);
  });

  it("clamps values and ignores NaN", () => {
    const state = createAudioState();
    const frame = pack({
      bass: 7,
      mid: -1,
      treble: NaN,
      rms: Infinity,
      fill(view) {
        view.setFloat32(24, 3, true);
        view.setFloat32(28, NaN, true);
        view.setFloat32(280, -9, true);
        view.setFloat32(284, NaN, true);
      },
    });
    decodeAudioFrame(frame, state);
    expect(state.audio).toMatchObject({ bass: 1, mid: 0, treble: 0, rms: 0 });
    expect([state.audio.bands[0], state.audio.bands[1]]).toEqual([1, 0]);
    expect([state.audio.waveform[0], state.audio.waveform[1]]).toEqual([-1, 0]);
  });

  it("keeps the last sample rate when the frame's is implausible", () => {
    const state = createAudioState();
    decodeAudioFrame(pack({ sampleRate: 44100 }), state);
    decodeAudioFrame(pack({ sampleRate: 0 }), state);
    expect(state.sampleRate).toBe(44100);
    decodeAudioFrame(pack({ sampleRate: 1e9 }), state);
    expect(state.sampleRate).toBe(44100);
  });
});
