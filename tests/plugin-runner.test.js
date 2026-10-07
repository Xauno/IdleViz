import { describe, expect, it, vi } from "vitest";
import { SLOW_FRAME_MS, SLOW_SECONDS, checkPlugin, createRunner, describeError } from "../web/plugin-runner-core.js";

/** A clock the test moves by hand. */
function makeClock() {
  let time = 1000;
  return { now: () => time, advance: (ms) => (time += ms) };
}

const message = (overrides = {}) => ({
  bands: new Float32Array(64).fill(0.5),
  waveform: new Float32Array(1024).fill(-0.25),
  bass: 0.1,
  mid: 0.2,
  treble: 0.3,
  rms: 0.4,
  ...overrides,
});

describe("checkPlugin", () => {
  it("accepts a module with init, frame and dispose", () => {
    const module = { init() {}, frame() {}, dispose() {} };
    expect(checkPlugin(module)).toBe(module);
  });

  it("names the missing export", () => {
    expect(() => checkPlugin({ init() {}, frame() {} })).toThrow("The plugin doesn't export dispose()");
    expect(() => checkPlugin(null)).toThrow("init()");
    expect(() => checkPlugin({ init: 1, frame() {}, dispose() {} })).toThrow("init()");
  });
});

describe("describeError", () => {
  it("gives one short line", () => {
    expect(describeError(new Error("Shader compile failed"))).toBe("Shader compile failed");
    expect(describeError("plain text")).toBe("plain text");
    expect(describeError(new Error("x".repeat(1000)))).toHaveLength(300);
    expect(describeError(new TypeError(""))).toBe("TypeError");
    expect(describeError("")).toBe("Unknown error");
  });
});

describe("createRunner", () => {
  it("hands the plugin the audio and the time since it started", () => {
    const clock = makeClock();
    const plugin = { frame: vi.fn(), dispose: vi.fn() };
    const runner = createRunner({ plugin, now: clock.now, report: vi.fn() });
    clock.advance(2500);
    runner.frame(message());
    const [audio, time] = plugin.frame.mock.calls[0];
    expect(time).toBe(2.5);
    expect(audio.bands).toHaveLength(64);
    expect(audio.bands[10]).toBe(0.5);
    expect(audio.waveform[1023]).toBe(-0.25);
    expect(audio).toMatchObject({ bass: 0.1, mid: 0.2, treble: 0.3, rms: 0.4 });
  });

  it("copies each message into the same arrays", () => {
    const seen = [];
    const plugin = { frame: (audio) => seen.push([audio.bands, audio.waveform, audio.bands[0]]), dispose() {} };
    const runner = createRunner({ plugin, now: makeClock().now, report: vi.fn() });
    const first = message();
    runner.frame(first);
    runner.frame(message({ bands: new Float32Array(64).fill(0.75) }));
    expect(seen[0][0]).toBe(seen[1][0]);
    expect(seen[0][1]).toBe(seen[1][1]);
    expect(seen[0][0]).not.toBe(first.bands);
    expect([seen[0][2], seen[1][2]]).toEqual([0.5, 0.75]);
  });

  it("gives silence for a message that isn't an audio object", () => {
    const plugin = { frame: vi.fn(), dispose() {} };
    const runner = createRunner({ plugin, now: makeClock().now, report: vi.fn() });
    runner.frame({ bands: [1, 2, 3], waveform: "loud", bass: "x", rms: NaN });
    runner.frame(null);
    const [audio] = plugin.frame.mock.calls[1];
    expect(Math.max(...audio.bands, ...audio.waveform)).toBe(0);
    expect(audio).toMatchObject({ bass: 0, mid: 0, treble: 0, rms: 0 });
  });

  it("reports a throw once and stops calling the plugin", () => {
    const report = vi.fn();
    const plugin = {
      frame: vi.fn(() => {
        throw new Error("lost context");
      }),
      dispose() {},
    };
    const runner = createRunner({ plugin, now: makeClock().now, report });
    runner.frame(message());
    runner.frame(message());
    expect(report).toHaveBeenCalledExactlyOnceWith("lost context");
    expect(plugin.frame).toHaveBeenCalledTimes(1);
    expect(runner.stopped).toBe(true);
  });

  it("disables a plugin whose frames stay slow for several seconds", () => {
    const clock = makeClock();
    const report = vi.fn();
    const plugin = { frame: () => clock.advance(SLOW_FRAME_MS + 10), dispose() {} };
    const runner = createRunner({ plugin, now: clock.now, report });
    const framesNeeded = Math.ceil((SLOW_SECONDS * 1000) / (SLOW_FRAME_MS + 10));
    for (let i = 0; i < framesNeeded - 1; i++) runner.frame(message());
    expect(report).not.toHaveBeenCalled();
    runner.frame(message());
    runner.frame(message());
    expect(report).toHaveBeenCalledTimes(1);
    expect(report.mock.calls[0][0]).toMatch(/^Too slow/);
  });

  it("forgives slow frames when a fast one comes in between", () => {
    const clock = makeClock();
    const report = vi.fn();
    let slow = true;
    const plugin = { frame: () => clock.advance(slow ? 200 : 2), dispose() {} };
    const runner = createRunner({ plugin, now: clock.now, report });
    for (let i = 0; i < 100; i++) {
      slow = i % 10 !== 9;
      runner.frame(message());
    }
    expect(report).not.toHaveBeenCalled();
  });

  it("disposes the plugin and ignores frames afterwards", () => {
    const plugin = { frame: vi.fn(), dispose: vi.fn() };
    const report = vi.fn();
    const runner = createRunner({ plugin, now: makeClock().now, report });
    runner.dispose();
    runner.frame(message());
    expect(plugin.dispose).toHaveBeenCalledTimes(1);
    expect(plugin.frame).not.toHaveBeenCalled();
    expect(report).not.toHaveBeenCalled();
  });

  it("reports a dispose that throws", () => {
    const report = vi.fn();
    const plugin = {
      frame() {},
      dispose() {
        throw new Error("already freed");
      },
    };
    createRunner({ plugin, now: makeClock().now, report }).dispose();
    expect(report).toHaveBeenCalledExactlyOnceWith("already freed");
  });

  it("runs the bundled Aurora plugin the way the frame does", async () => {
    const { createFakeCanvas } = await import("./helpers/fake-gl.js");
    vi.stubGlobal("window", { devicePixelRatio: 2 });
    const plugin = checkPlugin(await import("../web/visuals/aurora.js"));
    const { canvas, liveResources } = createFakeCanvas();
    plugin.init(canvas);
    const report = vi.fn();
    const runner = createRunner({ plugin, now: makeClock().now, report });
    for (let i = 0; i < 5; i++) runner.frame(message());
    runner.dispose();
    expect(report).not.toHaveBeenCalled();
    expect(liveResources()).toEqual([]);
    vi.unstubAllGlobals();
  });
});
