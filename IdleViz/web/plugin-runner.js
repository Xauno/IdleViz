// Inside the sandboxed frame: loads one plugin and runs it on the frames the host page posts.
// The frame has an opaque origin and a Content-Security-Policy of its own, so the plugin can't
// reach the network, storage, the overlay or the app. Its only input is the audio in each message.
import { checkPlugin, createRunner, describeError } from "./plugin-runner-core.js";

const canvas = /** @type {HTMLCanvasElement} */ (document.getElementById("canvas"));
// The frame's policy allows no stylesheets, so the layout is set from script.
for (const element of [document.documentElement, document.body]) {
  Object.assign(element.style, { margin: "0", width: "100%", height: "100%", overflow: "hidden", background: "#000" });
}
Object.assign(canvas.style, { display: "block", width: "100%", height: "100%" });

/** @type {ReturnType<typeof createRunner> | null} */
let runner = null;
let loading = false;

const tell = (message) => window.parent.postMessage(message, "*");

async function load(url) {
  if (loading || runner) return;
  loading = true;
  try {
    const plugin = checkPlugin(await import(url));
    plugin.init(canvas);
    runner = createRunner({
      plugin,
      now: () => performance.now(),
      report: (error) => tell({ type: "failed", error }),
    });
    tell({ type: "ready" });
  } catch (error) {
    tell({ type: "failed", error: describeError(error) });
  }
}

window.addEventListener("message", (event) => {
  // Only the host page talks to this frame.
  if (event.source !== window.parent) return;
  const message = event.data;
  switch (message?.type) {
    case "load":
      if (typeof message.url === "string") load(message.url);
      break;
    case "frame":
      runner?.frame(message.audio);
      break;
    case "dispose":
      runner?.dispose();
      break;
  }
});

tell({ type: "loaded" });
