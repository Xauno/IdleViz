# Writing a custom visualizer

A custom visualizer is a single `.js` file. Drop it in the presets folder (`~/Library/Application Support/IdleViz/Presets/`, subfolders allowed) and it shows up in the preset list, tagged "custom". `aurora.js` in the repo is a complete working example, and `aurora-demo.html` is a test page you can load it into.

## The contract

A plugin is an ES module with three named exports:

```js
export function init(canvas) {}          // required: set up once
export function frame(audio, time) {}    // required: draw one frame
export function dispose() {}             // required: release everything

export const meta = { name: "My Visual" };   // optional
```

- **`init(canvas)`** is called once when the preset is selected. The app gives you a fresh `<canvas>` that nothing has drawn on, so you can call `canvas.getContext("webgl2")` or `canvas.getContext("2d")` yourself. Throw an `Error` if you can't get what you need (for example no WebGL2). The app skips the plugin and moves to the next preset.
- **`frame(audio, time)`** is called once per display refresh (about 60 times a second). `time` is seconds since `init`. Draw the whole frame every call.
- **`dispose()`** is called when the preset is switched away or the window closes. Free every GPU resource you created (textures, buffers, programs, framebuffers) and drop references. Dispose may run many times over a long session, so leaks add up.
- **`meta.name`** is the display name. If it's missing, the file name is used.

Don't run your own `requestAnimationFrame` loop or timers that outlive `dispose()`. The app owns the loop.

## The `audio` object

```js
audio = {
  bands:    Float32Array(64),    // spectrum, each value 0..1
  bass:     number,              // 0..1
  mid:      number,              // 0..1
  treble:   number,              // 0..1
  waveform: Float32Array(1024),  // raw samples, -1..1
  rms:      number,              // overall loudness, 0..1
}
```

- `bands` are 64 log-spaced bands from about 40 Hz to 16 kHz, low to high. The app has already applied a noise floor and a fast-attack, slow-release smoothing, so you can use the values directly. Don't smooth them again unless you want extra lag.
- `bass`, `mid` and `treble` are averages of the bands: 0-7, 8-29 and 30-63. They move more gently than individual bands, so they work well for things like scale, hue shifts and camera motion.
- `waveform` is for oscilloscope-style visuals.
- The arrays are **reused every frame**. Read them during `frame`. If you need history, copy the values out. Never store the array itself.
- The app sends silence (all zeros) when Spotify is paused, closed or playing something with no audio. The visual should still look good, and ideally keep moving slowly, at zero.
- This is the only audio data you get. There is no `AudioContext`, microphone or system audio. Don't try to create one.

## Canvas and rendering

- **Size the canvas yourself.** The canvas fills the screen, but its pixel buffer starts at the default size. On each frame, compare `canvas.clientWidth * dpr` with `canvas.width` and resize if they differ (see `aurora.js`). Cap `dpr` at 2, because a 4K display at full device pixels is four times the work for little visible gain.
- **Use resolution-independent coordinates** (for example normalize by the canvas height) so the visual looks the same on any display.
- **Prefer WebGL2** and draw with one fullscreen triangle plus a fragment shader. The GPU handles it better than a pile of 2D canvas calls. Create the context with `{ antialias: false, alpha: false }` unless you really need otherwise.
- **Opaque output.** The canvas sits under the dimming layer and the Spotify overlay. Fill every pixel.
- **Keep it fairly dark and low in contrast at the edges.** The Spotify overlay (title, artist, progress bar) is drawn on top, and its text needs to stay readable. Avoid large pure-white areas behind the bottom third of the screen.
- **Don't draw text or UI.** The overlay owns that.

## Performance

- Aim for **under 8 ms per frame** on an M1-class Mac. The window runs for hours, and often while the machine is otherwise idle, so heavy visuals waste power and heat.
- If `frame` runs longer than about 50 ms for several seconds in a row, the app disables the plugin and moves on.
- Don't allocate in `frame`. Create arrays, textures and uniform locations in `init` and reuse them. Garbage collection pauses show up as visible stutter.
- Limit loops in shaders (fbm octaves, ray steps). A heavy fragment shader at 4K is the most common way a visual ends up too slow.

## What plugins can't do

The app runs plugins in the same web view as everything else but enforces these limits:

| Not allowed | Why |
|---|---|
| Network (`fetch`, XHR, WebSocket, external scripts or images) | A Content-Security-Policy blocks it. Everything must be self-contained. |
| `getUserMedia` or any audio or video capture | Denied. Plugins only see Spotify's audio, via `audio`. |
| Keyboard, mouse or touch listeners | Any input closes the visualizer. There's nothing to interact with. |
| Touching the DOM outside your canvas | The page belongs to the overlay. |
| `import` of other files or URLs | Keep it to one file. Inline shaders as template strings. |
| Persistent storage (`localStorage`, cookies) | Not supported. |

Images or data you need can be embedded in the file as `data:` URIs, or generated in code.

Plugins are code, not just data. Only install ones from sources you trust.

## Errors

- A throw in `init` or `frame` disables the plugin for this session. It appears in **Settings > Presets** with the error message.
- Compile WebGL shaders in `init` and check `COMPILE_STATUS` and `LINK_STATUS`. Throw with `gl.getShaderInfoLog(...)` so the error shows up in settings (`aurora.js` does this).
- Handle a lost WebGL context by throwing from `frame`. The app moves to the next preset.

## Minimal template

```js
// Pulse: a dot whose size follows the bass, with a bar for each band.
export const meta = { name: "Pulse" };

let ctx, canvasRef;

export function init(canvas) {
  canvasRef = canvas;
  ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("2D canvas not available");
}

export function frame(audio, time) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const w = canvasRef.clientWidth * dpr;
  const h = canvasRef.clientHeight * dpr;
  if (canvasRef.width !== w || canvasRef.height !== h) {
    canvasRef.width = w;
    canvasRef.height = h;
  }

  ctx.fillStyle = "#05060a";
  ctx.fillRect(0, 0, w, h);

  const bw = w / audio.bands.length;
  for (let i = 0; i < audio.bands.length; i++) {
    const bh = audio.bands[i] * h * 0.4;
    ctx.fillStyle = `hsl(${200 + i * 2 + time * 10}, 70%, 55%)`;
    ctx.fillRect(i * bw, h - bh, bw - 2, bh);
  }

  ctx.fillStyle = "#fff";
  ctx.beginPath();
  ctx.arc(w / 2, h / 2, h * (0.05 + audio.bass * 0.12), 0, Math.PI * 2);
  ctx.fill();
}

export function dispose() {
  ctx = canvasRef = null;
}
```

## Testing

1. Open `aurora-demo.html` in a browser. It is a test harness that can feed your plugin audio from a file or the microphone (the real app never uses the microphone) and shows the same `audio` object the app provides. Swap in your file to try it.
2. Test with **silence** (all zeros), a quiet track and a loud one.
3. Test at both a small window and a large one (resize the window, and try a 4K display if you have one).
4. Switch away and back, or reload the page, several times and watch the memory and GPU usage to make sure `dispose` really frees things.
5. Put your file in the presets folder and check that it appears in the list and shows no error in **Settings > Presets**.

## Checklist

- [ ] `init`, `frame` and `dispose` are exported, and `dispose` frees everything from `init`.
- [ ] No allocation, `requestAnimationFrame`, timers, event listeners or network calls.
- [ ] Canvas resized on the fly, `dpr` capped at 2.
- [ ] Every pixel is drawn, the result is fairly dark, and the bottom of the screen isn't bright white.
- [ ] Looks fine with silent audio.
- [ ] Runs under about 8 ms per frame.
- [ ] Self-contained in one file.
