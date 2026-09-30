import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { runInNewContext } from "node:vm";
import { describe, expect, it } from "vitest";
import {
  MAX_RENDER_WIDTH,
  Rotation,
  ShuffleBag,
  collectPresets,
  renderSize,
  shouldRender,
} from "../IdleViz/web/visualizer-state.js";

/** A repeatable stand-in for Math.random. */
function seeded(seed) {
  let state = seed;
  return () => {
    state = (state * 1664525 + 1013904223) % 4294967296;
    return state / 4294967296;
  };
}

describe("collectPresets", () => {
  it("merges packs, sorted by name without regard to case", () => {
    const list = collectPresets([{ beta: { b: 1 }, Alpha: { a: 1 } }, { gamma: { c: 1 } }]);
    expect(list.map((p) => p.name)).toEqual(["Alpha", "beta", "gamma"]);
    expect(list[0]).toEqual({ id: "bundled:Alpha", name: "Alpha", source: "bundled", preset: { a: 1 } });
  });

  it("keeps a repeated name once, from the first pack", () => {
    const list = collectPresets([{ same: { from: 1 } }, { same: { from: 2 } }]);
    expect(list).toHaveLength(1);
    expect(list[0].preset).toEqual({ from: 1 });
  });

  it("skips missing packs and entries that aren't objects", () => {
    expect(collectPresets([undefined, null, { ok: {}, bad: "text", none: null }]).map((p) => p.name)).toEqual(["ok"]);
  });

  it("tags the source", () => {
    expect(collectPresets([{ mine: {} }], "custom")[0]).toMatchObject({ id: "custom:mine", source: "custom" });
  });
});

describe("the bundled packs", () => {
  const vendor = resolve(import.meta.dirname, "..", "IdleViz", "web", "vendor");
  const load = (file, name) => {
    const sandbox = { self: {} };
    runInNewContext(readFileSync(resolve(vendor, file), "utf8"), sandbox);
    return sandbox.self[name].getPresets();
  };

  it("give 395 presets, each with the parts Butterchurn needs", () => {
    const list = collectPresets([
      load("butterchurnPresets.min.js", "butterchurnPresets"),
      load("butterchurnPresetsExtra.min.js", "butterchurnPresetsExtra"),
      load("butterchurnPresetsExtra2.min.js", "butterchurnPresetsExtra2"),
      load("butterchurnPresetsMD1.min.js", "butterchurnPresetsMD1"),
    ]);
    expect(list).toHaveLength(395);
    expect(new Set(list.map((p) => p.id)).size).toBe(395);
    // The packs run in their own VM context, so check shapes rather than `instanceof`.
    const incomplete = list.filter(
      ({ preset }) =>
        typeof preset.baseVals !== "object" || !Array.isArray(preset.shapes) || !Array.isArray(preset.waves),
    );
    expect(incomplete.map((p) => p.name)).toEqual([]);
  });
});

describe("ShuffleBag", () => {
  const ids = ["a", "b", "c", "d", "e"];

  it("goes through every id before repeating one", () => {
    const bag = new ShuffleBag(ids, seeded(1));
    for (let round = 0; round < 4; round++) {
      const seen = ids.map(() => bag.next());
      expect([...seen].sort()).toEqual(ids);
    }
  });

  it("never shows the same id twice in a row across rounds", () => {
    for (let seed = 1; seed <= 50; seed++) {
      const bag = new ShuffleBag(ids, seeded(seed));
      let last = null;
      for (let i = 0; i < 40; i++) {
        const id = bag.next();
        expect(id).not.toBe(last);
        last = id;
      }
    }
  });

  it("mixes the order", () => {
    const bag = new ShuffleBag(ids, seeded(7));
    const first = ids.map(() => bag.next());
    const second = ids.map(() => bag.next());
    expect(first).not.toEqual(second);
  });

  it("repeats a single id and returns null when empty", () => {
    const one = new ShuffleBag(["only"]);
    expect([one.next(), one.next()]).toEqual(["only", "only"]);
    expect(new ShuffleBag([]).next()).toBeNull();
  });
});

describe("Rotation", () => {
  it("is due once per period of rendered time", () => {
    const rotation = new Rotation(30);
    let due = 0;
    for (let frame = 0; frame < 60 * 95; frame++) if (rotation.tick(1 / 60)) due++;
    expect(due).toBe(3);
  });

  it("starts over on reset", () => {
    const rotation = new Rotation(10);
    expect(rotation.tick(9)).toBe(false);
    rotation.reset();
    expect(rotation.tick(9)).toBe(false);
    expect(rotation.tick(1)).toBe(true);
  });
});

describe("renderSize", () => {
  it("uses the device pixel ratio below the cap", () => {
    expect(renderSize(1024, 640, 2)).toEqual({ width: 2048, height: 1280 });
  });

  it("scales down to the cap and keeps the aspect ratio", () => {
    // A 1512 × 982 pt MacBook Pro display at 2× would be 3024 px wide.
    expect(renderSize(1512, 982, 2)).toEqual({ width: MAX_RENDER_WIDTH, height: 1663 });
    expect(renderSize(3840, 2160, 1)).toEqual({ width: 2560, height: 1440 });
  });

  it("never returns an empty size", () => {
    expect(renderSize(0, 0, 2)).toEqual({ width: 2, height: 2 });
    expect(renderSize(100, 50, 0)).toEqual({ width: 100, height: 50 });
  });
});

describe("shouldRender", () => {
  it("renders every frame at 60 Hz, jitter included", () => {
    expect(shouldRender(16.7, 0)).toBe(true);
    expect(shouldRender(15.1, 0)).toBe(true);
  });

  it("skips every other frame at 120 Hz", () => {
    let last = 0;
    let rendered = 0;
    for (let frame = 1; frame <= 120; frame++) {
      const now = frame * (1000 / 120);
      if (shouldRender(now, last)) {
        rendered++;
        last = now;
      }
    }
    expect(rendered).toBe(60);
  });
});
