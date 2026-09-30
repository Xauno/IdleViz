import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createAudio, createFakeCanvas } from "./helpers/fake-gl.js";

// Every visualizer plugin must satisfy the contract in docs/custom-visualizer.md.
const root = resolve(import.meta.dirname, "..");
const visualsDir = join(root, "IdleViz", "web", "visuals");
const pluginFiles = readdirSync(visualsDir)
  .filter((f) => f.endsWith(".js"))
  .map((f) => join(visualsDir, f));

// Things plugins must not use (see "What plugins can't do" in the guide).
const FORBIDDEN = [
  /\bfetch\s*\(/,
  /\bXMLHttpRequest\b/,
  /\bWebSocket\b/,
  /\bgetUserMedia\b/,
  /\bAudioContext\b/,
  /\bMediaStream\b/,
  /\baddEventListener\b/,
  /\bonkeydown\b|\bonmousemove\b|\bonclick\b/,
  /\brequestAnimationFrame\b/,
  /\bsetInterval\b|\bsetTimeout\b/,
  /\blocalStorage\b|\bsessionStorage\b|\bdocument\.cookie\b/,
  /\bdocument\./,
  /\bimport\s*\(/,
  /^\s*import\s.+from\s/m,
];

beforeEach(() => {
  vi.stubGlobal("window", { devicePixelRatio: 2 });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe.each(pluginFiles.map((file) => [file.replace(`${root}/`, ""), file]))("%s", (_name, file) => {
  const load = () => import(pathToFileURL(file).href + `?t=${Math.random()}`);

  it("exports init, frame and dispose", async () => {
    const plugin = await load();
    expect(typeof plugin.init).toBe("function");
    expect(typeof plugin.frame).toBe("function");
    expect(typeof plugin.dispose).toBe("function");
  });

  it("has a string meta.name when meta is exported", async () => {
    const { meta } = await load();
    if (meta !== undefined) expect(typeof meta.name).toBe("string");
  });

  it("renders silence and loud audio without throwing", async () => {
    const plugin = await load();
    const { canvas } = createFakeCanvas();
    plugin.init(canvas);
    for (let i = 0; i < 10; i++) plugin.frame(createAudio({ level: 0 }), i / 60);
    for (let i = 0; i < 10; i++) plugin.frame(createAudio({ level: 1 }), 1 + i / 60);
    plugin.dispose();
  });

  it("never passes NaN or Infinity to the GPU", async () => {
    const plugin = await load();
    const { canvas, calls } = createFakeCanvas();
    plugin.init(canvas);
    plugin.frame(createAudio({ level: 0.5 }), 0);
    plugin.frame(createAudio({ level: 0.5 }), 12.5);
    plugin.dispose();
    const bad = calls.filter((c) => c.args.some((a) => typeof a === "number" && !Number.isFinite(a)));
    expect(bad).toEqual([]);
  });

  it("sizes the canvas to its CSS size at device pixel ratio, capped at 2", async () => {
    const plugin = await load();
    const { canvas } = createFakeCanvas({ clientWidth: 1000, clientHeight: 500 });
    vi.stubGlobal("window", { devicePixelRatio: 3 });
    plugin.init(canvas);
    plugin.frame(createAudio(), 0);
    expect(canvas.width).toBe(2000);
    expect(canvas.height).toBe(1000);
    plugin.dispose();
  });

  it("follows a resize between frames", async () => {
    const plugin = await load();
    const { canvas } = createFakeCanvas({ clientWidth: 800, clientHeight: 600 });
    plugin.init(canvas);
    plugin.frame(createAudio(), 0);
    canvas.clientWidth = 1200;
    canvas.clientHeight = 700;
    plugin.frame(createAudio(), 1);
    expect([canvas.width, canvas.height]).toEqual([2400, 1400]);
    plugin.dispose();
  });

  it("frees every GPU resource it created on dispose", async () => {
    const plugin = await load();
    const { canvas, liveResources } = createFakeCanvas();
    plugin.init(canvas);
    plugin.frame(createAudio({ level: 0.3 }), 0);
    plugin.dispose();
    expect(liveResources()).toEqual([]);
  });

  it("can be initialised again after dispose without leaking", async () => {
    const plugin = await load();
    for (let round = 0; round < 3; round++) {
      const { canvas, liveResources } = createFakeCanvas();
      plugin.init(canvas);
      plugin.frame(createAudio({ level: 0.3 }), round);
      plugin.dispose();
      expect(liveResources()).toEqual([]);
    }
  });

  it("uses none of the forbidden APIs", () => {
    const source = readFileSync(file, "utf8");
    // Ignore comments so the docs-style header doesn't trip the scan.
    const code = source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/^\s*\/\/.*$/gm, "");
    const hits = FORBIDDEN.filter((re) => re.test(code)).map(String);
    expect(hits).toEqual([]);
  });
});
