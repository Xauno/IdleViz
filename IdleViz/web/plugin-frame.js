// The host page's side of a plugin: one sandboxed frame per plugin, fed audio with postMessage.

/** A plugin that hasn't reported in by then is given up on. */
const START_TIMEOUT_MS = 5000;

export class PluginFrame {
  /**
   * @param {HTMLElement} host   Where the frame goes.
   * @param {string} url         The plugin module.
   * @param {(error: string) => void} onFailed  Called at most once, if the plugin fails at any point.
   */
  constructor(host, url, onFailed) {
    this.url = url;
    this.onFailed = onFailed;
    this.started = false;
    this.ended = false;
    this.iframe = document.createElement("iframe");
    // allow-scripts without allow-same-origin: the frame gets an opaque origin.
    this.iframe.setAttribute("sandbox", "allow-scripts");
    this.iframe.src = "plugin-host.html";
    /** Resolves true when the plugin's init has run, false if it failed first. */
    this.ready = new Promise((resolve) => {
      this.resolveReady = resolve;
    });
    this.onMessage = (event) => this.receive(event);
    window.addEventListener("message", this.onMessage);
    this.timeout = setTimeout(() => this.fail("The plugin didn't start"), START_TIMEOUT_MS);
    host.append(this.iframe);
  }

  receive(event) {
    if (this.ended || event.source !== this.iframe.contentWindow) return;
    const message = event.data;
    switch (message?.type) {
      case "loaded":
        this.send({ type: "load", url: this.url });
        break;
      case "ready":
        clearTimeout(this.timeout);
        this.started = true;
        this.resolveReady(true);
        break;
      case "failed":
        this.fail(typeof message.error === "string" ? message.error.slice(0, 300) : "The plugin failed");
        break;
    }
  }

  send(message) {
    this.iframe.contentWindow?.postMessage(message, "*");
  }

  /** @param {object} audio  The plugin audio object; it is copied on the way over. */
  post(audio) {
    if (this.started && !this.ended) this.send({ type: "frame", audio });
  }

  /** Fades the frame in or out. */
  fade(shown, seconds) {
    this.iframe.style.transitionDuration = `${seconds}s`;
    // Read a layout value so the starting opacity is committed before it changes.
    void this.iframe.offsetWidth;
    this.iframe.classList.toggle("shown", shown);
  }

  fail(error) {
    if (this.ended) return;
    clearTimeout(this.timeout);
    this.resolveReady(false);
    this.remove();
    this.onFailed(error);
  }

  /** Lets the plugin clean up, then takes the frame away. */
  remove() {
    if (this.ended) return;
    this.ended = true;
    clearTimeout(this.timeout);
    this.resolveReady(false);
    window.removeEventListener("message", this.onMessage);
    this.send({ type: "dispose" });
    const iframe = this.iframe;
    // Give the dispose message a moment to arrive before the frame goes.
    setTimeout(() => iframe.remove(), 100);
  }
}
