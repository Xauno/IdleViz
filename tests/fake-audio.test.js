import { describe, expect, it } from "vitest";
import { SAMPLE_COUNT, fillFakeAudio, sampleAt, toByte } from "../IdleViz/web/fake-audio.js";

const makeLevels = () => ({
  timeByteArray: new Uint8Array(SAMPLE_COUNT),
  timeByteArrayL: new Uint8Array(SAMPLE_COUNT),
  timeByteArrayR: new Uint8Array(SAMPLE_COUNT),
});

const loudness = (bytes) => Math.sqrt(bytes.reduce((sum, b) => sum + (b - 128) ** 2, 0) / bytes.length);

describe("fake audio", () => {
  it("maps samples to bytes around 128", () => {
    expect([toByte(0), toByte(1), toByte(-1), toByte(5), toByte(-5)]).toEqual([128, 255, 1, 255, 0]);
  });

  it("stays within -1..1", () => {
    for (let t = 0; t < 8; t += 0.0137) {
      const [left, right] = sampleAt(t);
      expect(Math.abs(left)).toBeLessThanOrEqual(1);
      expect(Math.abs(right)).toBeLessThanOrEqual(1);
    }
  });

  it("gives the same frame for the same time", () => {
    const a = makeLevels();
    const b = makeLevels();
    fillFakeAudio(a, 3.21);
    fillFakeAudio(b, 3.21);
    expect(a).toEqual(b);
  });

  it("is louder on the beat than just before it", () => {
    const onBeat = makeLevels();
    const beforeBeat = makeLevels();
    fillFakeAudio(onBeat, 2.0 + 0.03);
    fillFakeAudio(beforeBeat, 2.0 - 0.01);
    expect(loudness(onBeat.timeByteArray)).toBeGreaterThan(loudness(beforeBeat.timeByteArray) * 1.5);
  });

  it("differs between left and right", () => {
    const levels = makeLevels();
    fillFakeAudio(levels, 1.3);
    expect(levels.timeByteArrayL).not.toEqual(levels.timeByteArrayR);
  });
});
