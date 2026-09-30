// Pure visualizer logic, kept free of the DOM and WebGL so it runs under Vitest.

/** Butterchurn renders at most this wide; the GPU scales it up on larger displays. */
export const MAX_RENDER_WIDTH = 2560;
/** Frames closer together than this are skipped, which halves 120 Hz to 60 fps. A strict
 *  16.7 ms would also drop about a third of the frames on a 60 Hz display (timestamp jitter). */
export const MIN_FRAME_GAP_MS = 12;
export const SECONDS_PER_PRESET = 30;
export const BLEND_SECONDS = 2.7;

/**
 * @typedef {object} PresetEntry
 * @property {string} id       Stable key: the source, a colon, then the name.
 * @property {string} name
 * @property {"bundled" | "custom"} source
 * @property {object} preset   The Butterchurn preset itself.
 */

/**
 * One sorted list from several preset packs. A name that appears in more than one pack
 * is kept once, from the first pack that has it.
 * @param {Array<Record<string, object> | null | undefined>} packs
 * @param {"bundled" | "custom"} [source]
 * @returns {PresetEntry[]}
 */
export function collectPresets(packs, source = "bundled") {
  /** @type {Map<string, PresetEntry>} */
  const byName = new Map();
  for (const pack of packs) {
    if (!pack || typeof pack !== "object") continue;
    for (const [name, preset] of Object.entries(pack)) {
      if (!preset || typeof preset !== "object" || byName.has(name)) continue;
      byName.set(name, { id: `${source}:${name}`, name, source, preset });
    }
  }
  return [...byName.values()].sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));
}

/**
 * Hands out ids in random order and goes through all of them before any repeats.
 */
export class ShuffleBag {
  /**
   * @param {string[]} ids
   * @param {() => number} [random]  Returns a number in [0, 1), like Math.random.
   */
  constructor(ids, random = Math.random) {
    this.ids = [...ids];
    this.random = random;
    /** @type {string[]} */
    this.bag = [];
    /** @type {string | null} */
    this.last = null;
  }

  /** The next id, or null when there are none. */
  next() {
    if (this.ids.length === 0) return null;
    if (this.bag.length === 0) this.refill();
    this.last = /** @type {string} */ (this.bag.pop());
    return this.last;
  }

  refill() {
    const bag = [...this.ids];
    for (let i = bag.length - 1; i > 0; i--) {
      const j = Math.floor(this.random() * (i + 1));
      [bag[i], bag[j]] = [bag[j], bag[i]];
    }
    // The end of the array is handed out first. Don't start a round with the preset just shown.
    const end = bag.length - 1;
    if (end > 0 && bag[end] === this.last) [bag[0], bag[end]] = [bag[end], bag[0]];
    this.bag = bag;
  }
}

/**
 * Counts time on screen and says when the next preset is due. It is fed the time between
 * rendered frames, so time while the window is closed doesn't count.
 */
export class Rotation {
  /** @param {number} secondsPerPreset */
  constructor(secondsPerPreset) {
    this.secondsPerPreset = secondsPerPreset;
    this.elapsed = 0;
  }

  /**
   * @param {number} seconds  Time since the last frame.
   * @returns {boolean} True once per period, when the preset should change.
   */
  tick(seconds) {
    this.elapsed += seconds;
    if (this.elapsed < this.secondsPerPreset) return false;
    this.elapsed = 0;
    return true;
  }

  reset() {
    this.elapsed = 0;
  }
}

/**
 * The canvas size in pixels: the CSS size at the device pixel ratio, scaled down to `maxWidth`.
 * @param {number} cssWidth
 * @param {number} cssHeight
 * @param {number} devicePixelRatio
 * @param {number} [maxWidth]
 */
export function renderSize(cssWidth, cssHeight, devicePixelRatio, maxWidth = MAX_RENDER_WIDTH) {
  const width = Math.max(cssWidth, 1);
  const height = Math.max(cssHeight, 1);
  const scale = Math.min(devicePixelRatio > 0 ? devicePixelRatio : 1, maxWidth / width);
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

/**
 * Whether to render on this animation frame.
 * @param {number} now   `requestAnimationFrame` timestamp, ms.
 * @param {number} last  Timestamp of the last rendered frame, ms.
 */
export function shouldRender(now, last) {
  return now - last >= MIN_FRAME_GAP_MS;
}
