// Runs one visual plugin (see docs/custom-visualizer.md). Free of the DOM, so it runs under Vitest.

/** A frame slower than this counts as slow. */
export const SLOW_FRAME_MS = 50;
/** This long with nothing but slow frames disables the plugin. */
export const SLOW_SECONDS = 3;
const BAND_COUNT = 64;
const SAMPLE_COUNT = 1024;

/**
 * Checks that a loaded module is a plugin.
 * @param {any} module
 * @returns {{ init: Function, frame: Function, dispose: Function, meta?: { name?: unknown } }}
 */
export function checkPlugin(module) {
  for (const name of ["init", "frame", "dispose"]) {
    if (typeof module?.[name] !== "function") throw new Error(`The plugin doesn't export ${name}()`);
  }
  return module;
}

/** An error as one short line of text. */
export function describeError(error) {
  const text = error instanceof Error ? error.message || error.name : String(error);
  return text.slice(0, 300) || "Unknown error";
}

const number = (value) => (typeof value === "number" && Number.isFinite(value) ? value : 0);

/**
 * Wraps a plugin whose `init` has already run.
 * @param {object} options
 * @param {{ frame: Function, dispose: Function }} options.plugin
 * @param {() => number} options.now            Milliseconds, like performance.now.
 * @param {(error: string) => void} options.report  Called once, when the plugin has to be disabled.
 */
export function createRunner({ plugin, now, report }) {
  // The plugin gets these same arrays every frame; each message is copied into them.
  const audio = {
    bands: new Float32Array(BAND_COUNT),
    waveform: new Float32Array(SAMPLE_COUNT),
    bass: 0,
    mid: 0,
    treble: 0,
    rms: 0,
  };
  const started = now();
  let stopped = false;
  /** When the current unbroken run of slow frames began, or null. */
  let slowSince = null;

  function stop(error) {
    if (stopped) return;
    stopped = true;
    report(error);
  }

  return {
    /** @param {any} message  The audio object posted by the host page. */
    frame(message) {
      if (stopped) return;
      if (message?.bands?.length === BAND_COUNT) audio.bands.set(message.bands);
      if (message?.waveform?.length === SAMPLE_COUNT) audio.waveform.set(message.waveform);
      audio.bass = number(message?.bass);
      audio.mid = number(message?.mid);
      audio.treble = number(message?.treble);
      audio.rms = number(message?.rms);

      const before = now();
      try {
        plugin.frame(audio, (before - started) / 1000);
      } catch (error) {
        stop(describeError(error));
        return;
      }
      const after = now();
      if (after - before <= SLOW_FRAME_MS) {
        slowSince = null;
      } else if (slowSince === null) {
        slowSince = before;
      } else if (after - slowSince >= SLOW_SECONDS * 1000) {
        stop(`Too slow: every frame took over ${SLOW_FRAME_MS} ms for ${SLOW_SECONDS} s`);
      }
    },

    /** Lets the plugin free what it created. Safe to call more than once. */
    dispose() {
      const wasStopped = stopped;
      stopped = true;
      try {
        plugin.dispose();
      } catch (error) {
        if (!wasStopped) report(describeError(error));
      }
    },

    get stopped() {
      return stopped;
    },
  };
}
