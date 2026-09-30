# Idle Visualizer + Spotify Overlay (macOS)

A menu-bar app that opens a fullscreen music visualizer with a Spotify now-playing overlay when the Mac goes idle (or on command), and closes on any input. Not a real screensaver, just a fullscreen window on top of everything.

## Behavior summary

- **Opens** after N idle minutes, or from a global hotkey / terminal command, but only if Spotify is running and has a current track (playing or paused). Otherwise it doesn't open, and a manual trigger flashes the menu-bar icon.
- **Closes** on any input (mouse/trackpad movement, click, scroll, key, gesture), as sensitive as a macOS screensaver. It also fades out if Spotify quits or the track disappears.
- **Overlay** is Spotify-only and looks like the Spotify TV app's now-playing screen, with the visualizer as the background. Font: Figtree.
- **Visualizer** reacts to Spotify's audio and runs at 70% brightness (adjustable). It is built in: Butterchurn (WebGL Milkdrop) with a bundled preset library plus your own presets imported from a folder.
- **Main display only** for now.

| Spotify state                 | Opens? | Overlay                                   |
|-------------------------------|--------|-------------------------------------------|
| Not running / no track        | No     | n/a                                       |
| Song, playing                 | Yes    | Full TV-style layout                      |
| Song, paused                  | Yes    | Progress bar only, frozen in place        |
| Podcast (playing or paused)   | Yes    | None                                      |
| Ad between songs              | Yes    | Minimal: "Advertisement" label + progress bar |
| Ad in or between podcasts     | Yes    | None                                      |

**Non-goals:** other players (Apple Music, browsers), Spotify's Web API (no login/OAuth/Premium dependency), a `.saver` bundle, third-party visualizer apps (Synesthesia etc.), multi-display (see "Later").

---

## Architecture

One window, one web page. The page holds the Butterchurn visualizer canvas and the overlay.

```
Menu-bar helper (Swift)
├─ Triggers: IdleWatcher, Hotkey, URL scheme ──► OpenRules ──► WindowController
├─ DismissWatcher ──────────────────────────────────────────► WindowController (close)
├─ SpotifyInfo (notifications + AppleScript) ──► page: window.nowPlaying(json)
├─ SpotifyAudioTap (Core Audio → FFT/PCM) ─────► page: window.audioFrame(data)
└─ PresetLibrary (scan/watch custom folder) ───► page: window.setCustomPresets(json)

Page layers (bottom → top): Butterchurn canvas → dim layer → overlay
```

### Menu-bar helper
- `LSUIElement = YES`, launch at login (`SMAppService.mainApp.register()`).
- The menu-bar menu is deliberately tiny, with two rows:
  1. **Settings…** (the only clickable item) opens the separate settings window.
  2. Below it, a disabled, informational row showing the current open hotkey (e.g. "Open visualizer: ⌃⌥V"), read from `KeyboardShortcuts` so it updates if the shortcut changes.
- There is no Quit item in the menu. Quit lives in the settings window (and ⌘Q while it's focused).
- **Settings window** (separate native window, a normal `NSWindow` with SwiftUI content; the app switches to `.regular` activation policy while it's open so it can take focus, then back to accessory on close). It holds everything that used to be in the menu:
  - **General:** idle timeout (5/10/15/30 min, Off), open hotkey recorder (`KeyboardShortcuts.Recorder`), launch at login, an "Open now" button, Quit.
  - **Visualizer:** brightness slider (50–100%), shuffle on/off, seconds per preset, blend time, source filter (all/bundled/custom), favorites and blocklist management.
  - **Presets:** Import Presets…, Open Presets Folder, Reload Presets, and the list of presets that failed to load.
- Settings in `UserDefaults`. The window writes them and the helper applies changes live.

### Triggers
- **Idle:** `CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)` (any input). Instead of polling on a fixed interval, schedule the next check for `timeout − idleTime` (the earliest it could fire). Optional: skip if a fullscreen video is frontmost.
- **Hotkey:** `KeyboardShortcuts` Swift package.
- **Terminal:** URL scheme, so `open idleviz://open` works.
- **Open rules check:** Spotify is running (see below) and AppleScript returns a current track. On failure from a manual trigger, flash the menu-bar icon (alt icon 2–3 times over ~1 s).

### Dismiss
- Make the window key (subclass `NSWindow`, override `canBecomeKey` → `true`) so a **local** `NSEvent` monitor receives keys without needing Accessibility/Input Monitoring permission. Add a global monitor for mouse events as a backstop.
- Events: `.mouseMoved`, `.leftMouseDown`, `.rightMouseDown`, `.otherMouseDown`, `.scrollWheel`, `.keyDown`, `.flagsChanged`, `.gesture`, `.magnify`, `.swipe`.
- Threshold ≈ 0: close on the first `.mouseMoved` with any nonzero delta (≥ 1 px if the trackpad sends phantom zero-delta events).
- Ignore input for the first ~300–500 ms after opening so the trigger itself doesn't close it.
- Backup: while open, check `secondsSinceLastEventType` every ~100 ms; if it's less than the time since opening (minus the grace period), close.
- Hide the cursor while open, restore on close.

### Window
- One borderless `NSWindow` on `NSScreen.screens.first` (the menu-bar display). Create it via a function that takes an `NSScreen`, so multi-display is easy later.
- `level = .screenSaver`, `collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]`.
- Content: one `WKWebView` loading `web/index.html`. To load custom preset files from disk, serve them through a `WKURLSchemeHandler` (e.g. `idleviz-presets://`) or pass their JSON contents via `evaluateJavaScript`, since `file://` pages can't freely read other folders.
- Keep the window and web view alive between opens (hide instead of destroying it) so opening is instant.

---

## Spotify now-playing

**Never launch Spotify.** Sending an Apple Event to a closed app launches it, so only talk to Spotify when it's running:
- Track running state with `NSWorkspace` `didLaunchApplicationNotification` / `didTerminateApplicationNotification` (bundle ID `com.spotify.client`), seeded once from `runningApplications`. No polling.

**Getting track info without constant polling:**
- Spotify posts a distributed notification, `com.spotify.client.PlaybackStateChanged`, on play/pause/track change. Observe it with `DistributedNotificationCenter` and use it as the trigger for updates. (Verify which fields it includes; it has historically carried name, artist, album, duration, position, player state and track ID, but not artwork.)
- On each notification (and once when the window opens), run one AppleScript query for the full state, including artwork URL:

```applescript
tell application "Spotify"
    set s to player state as string
    if s is "stopped" then return "stopped"
    set t to current track
    return s & "||" & (name of t) & "||" & (artist of t) & "||" & (album of t) & "||" & (artwork url of t) & "||" & (duration of t) & "||" & (player position) & "||" & (spotify url of t)
end tell
```

- The page advances the progress bar locally from position + timestamp; no per-second polling. While the window is open, do a light re-sync every ~5 s to correct drift and catch seeks.
- Run via `NSAppleScript`, compiled once. Needs `NSAppleEventsUsageDescription`.
- Units: `duration` in **milliseconds**, `player position` in **seconds**.
- "No current track" = state `stopped`, an error, or an empty name/URL.

**Content type** from the `spotify url` prefix: `track:` / `local:` → song, `episode:` → podcast, `ad:` → ad.
- Ad context: remember the type of the last non-ad item. After a podcast episode, an ad is a podcast ad (no overlay); otherwise it's a music ad (minimal overlay).
- Test how ads report in practice. Ads baked into podcast audio never show up as a separate item and simply count as the podcast.

---

## Overlay design (Spotify TV app look)

Match the Spotify TV app's now-playing screen exactly; the only change is the visualizer background.

- Save reference screenshots (1920×1080 and 4K) in `design/reference/`. During development, lay a screenshot over the page at 50% opacity to match positions and sizes.
- Match album art (size, radius, shadow), title, artist, progress bar (height, played/unplayed colors, knob), time labels, the playback control row if present (static, reflecting play/pause state; not clickable), and any logo or "Playing on" line.
- Build on a fixed 1920×1080 stage scaled with `transform: scale()` so proportions match the TV on any display.
- Paused: fade out everything except the progress bar (and its time labels), which stays exactly where it is.
- Track change: crossfade art and text.
- **Font:** Figtree (SIL Open Font License), bundled locally in `web/fonts/` with its license file; don't load it from a CDN. Use the variable font. Starting weights: title 700–800, artist 500, times 400; titles slightly tight (`letter-spacing: -0.01em` to `-0.02em`). Tune against the screenshots. Don't copy Spotify's font files from the Spotify app.
- **Brightness:** a black dim layer between the visualizer and the overlay. This is cheaper than a CSS `filter` on a WebGL canvas.

```css
@font-face {
  font-family: "Figtree";
  src: url("fonts/Figtree[wght].ttf") format("truetype");
  font-weight: 300 900;
}
:root {
  --overlay-font: "Figtree", -apple-system, sans-serif;
  --viz-brightness: 0.7;            /* setting: visualizerBrightness */
}
#dim { background: #000; opacity: calc(1 - var(--viz-brightness)); }
```

Swift updates brightness live (from the settings window slider): `webView.evaluateJavaScript("document.documentElement.style.setProperty('--viz-brightness', '\(value)')")`.

---

## Visualizer (Butterchurn)

The only visualizer is **Butterchurn** (WebGL port of Milkdrop), chosen because the goal is a big, browsable library rather than hand-written scenes. There is no external-app mode.

### Audio
- Core Audio process tap (macOS 14.2+) on Spotify only.
  - Find Spotify's audio process objects via `kAudioHardwarePropertyProcessObjectList` (match `com.spotify.client`; tap all matching processes).
  - `CATapDescription(stereoMixdownOfProcesses:)` with `muteBehavior = .unmuted` → `AudioHardwareCreateProcessTap` → private aggregate device → `AudioDeviceCreateIOProcIDWithBlock`.
  - Needs `NSAudioCaptureUsageDescription`; macOS prompts once. Unsigned builds may get all-zero buffers until permission is granted to the right binary.
  - Recreate the tap when Spotify relaunches. Run it only while the window is open.
- Swift sends PCM (or a 1024-sample waveform plus FFT bands) to the page ~60×/s via `evaluateJavaScript`, base64-encoded to keep it cheap.
- **Spotify-only audio guarantee** (applies to Butterchurn and to every custom plugin):
  - The only audio source is the Spotify process tap above. Never use a global/system tap, an input device, or the microphone. Always build the tap with `CATapDescription(stereoMixdownOfProcesses:)` containing Spotify's process objects only. If none are found, send silence.
  - Audio reaches JS only through `window.audioFrame(data)`, then a single host wrapper hands each visual its frame as an argument. A plugin never gets an `AudioContext`, `MediaStream` or node it could wire to something else.
  - The web view denies all media capture: implement `WKUIDelegate`'s `requestMediaCapturePermissionFor` to always return `.deny`, and add no microphone/camera entitlements or usage strings. This stops a plugin from calling `getUserMedia` to get mic or other audio.
  - If Spotify quits, or the track is an ad or podcast with no audio source, the frame is silence (zeros), not a fallback to anything else.
  - Test: play other audio (a YouTube tab, system sounds) while Spotify is paused and confirm the visualizer stays flat.
- Butterchurn normally takes a Web Audio node. Feed it from Swift's tap by pushing PCM into an `AudioBuffer`/`AudioWorklet` source in the page. Verify this against the version you use.

### Presets
- **Bundled:** `butterchurn-presets` (hundreds), shipped in the app.
- **Custom (folder import):** any preset you add yourself, loaded alongside the bundled ones.
  - Folder: `~/Library/Application Support/IdleViz/Presets/` (created on first launch; subfolders allowed, so a downloaded pack can be dropped in as-is).
  - Accepted files:
    - `.json`: Butterchurn-format presets, loaded directly.
    - `.js`: custom visual plugins (see "Custom JS plugins" below).
    - `.milk`: original Milkdrop presets, converted with `butterchurn-preset-converter`. Converted results are cached (e.g. `Presets/.cache/<hash>.json`) so conversion runs once per file. Verify the converter runs fully in-page/offline in the version you use; if it needs a server or build step, convert in a Node script instead.
  - Settings window controls (Presets tab):
    - **Import Presets…** opens an `NSOpenPanel` (files or folders) and copies the picks into the presets folder.
    - **Open Presets Folder** reveals it in Finder.
    - **Reload Presets** rescans it.
  - `PresetLibrary.swift` scans the folder and watches it for changes (`DispatchSource` file-system events or FSEvents), so dropping files in updates the library without a restart. It sends the list to the page with `window.setCustomPresets(json)`.
  - Each preset is validated (parse, compile its shaders) in a `try/catch` when first loaded. Bad ones are skipped, added to the blocklist, and reported in the settings window ("3 presets failed to load").
  - Presets are tagged by source (bundled / custom) so the settings can shuffle all, bundled only, or custom only.
  - Check each pack's license before sharing the app. User-imported presets stay on the user's machine and are never bundled.
- **Preset control** (in the settings window, since any input closes the visualizer): shuffle on/off, seconds per preset, blend time, source filter (all/bundled/custom), favorites, and a blocklist for presets you dislike.
- **Custom JS plugins:** users can drop `.js` files into the presets folder (subfolders allowed) and they appear in the preset list next to Butterchurn presets, tagged "custom".
  - Each file is an ES module with named exports `init(canvas)`, `frame(audio, time)` and `dispose()`, plus an optional `meta = { name }`. The same interface is used for plugins bundled in `web/visuals/*.js` (see `aurora.js` for an example). The full author guide is in `docs/custom-visualizer.md`.
  - `audio` is a plain object: `{ bands: Float32Array(64), bass, mid, treble, waveform: Float32Array(1024), rms }`, built from the Spotify-only frame (see the audio guarantee above). Bands are 0..1 and log-spaced (about 40 Hz to 16 kHz), already noise-floored and smoothed (fast attack, slow release); `bass`/`mid`/`treble` average bands 0-7, 8-29 and 30-63. The arrays are reused every frame. It is the only audio data a plugin ever sees.
  - Plugins draw only into the canvas they are given. Each `init` gets a fresh canvas, so a plugin picks its own context type (WebGL2 or 2D).
  - Loading: Swift serves the file via the presets URL scheme. The page imports it with `import()` inside `try/catch`, calls `init`, and wraps `frame` so that a throw, or a frame that takes over ~50 ms for several seconds in a row, disables that plugin and moves on to the next preset.
  - Isolation: plugins run in the same web view, so use a `Content-Security-Policy` of `connect-src 'none'` (no `fetch`/XHR/WebSocket), `img-src`/`media-src` limited to `data:`/`blob:` and the presets scheme, and no `getUserMedia` (see the audio guarantee). This isn't a true sandbox, so the settings window shows a one-time warning that custom plugins are code and should only come from sources the user trusts.
  - Same license caveat as presets: imported plugins stay on the user's machine and are never bundled.
  - Settings window shows plugin load failures alongside preset failures, and includes plugins in favorites and the blocklist.
- Alternative considered: native **libprojectM** (open-source Milkdrop engine, C++/OpenGL). Larger ecosystem, but OpenGL is deprecated on macOS and it needs a native rendering layer under the window, so Butterchurn is the simpler start.

---

## File layout

```
IdleViz/
├─ App/
│  ├─ AppDelegate.swift          # menu bar (Settings… + hotkey row), icon flash
│  ├─ SettingsWindow.swift       # separate settings UI (general, visualizer, presets)
│  ├─ Triggers.swift             # idle, hotkey, URL scheme, open rules
│  ├─ DismissWatcher.swift
│  ├─ WindowController.swift     # key-capable borderless window + WKWebView
│  ├─ SpotifyInfo.swift          # launch/quit tracking, notifications, AppleScript, content type
│  ├─ SpotifyAudioTap.swift      # process tap + FFT
│  ├─ PresetLibrary.swift        # scan/watch custom preset folder, import, send list to page
│  └─ Info.plist                 # LSUIElement, NSAppleEventsUsageDescription, NSAudioCaptureUsageDescription
├─ web/
│  ├─ index.html
│  ├─ overlay.css / overlay.js   # nowPlaying(), progress interpolation, states
│  ├─ fonts/                     # Figtree + OFL license
│  └─ visuals/                   # Butterchurn wrapper, preset manager, plugin modules
└─ design/reference/             # Spotify TV app screenshots
```

## Build order

Each step is one pull request. At the end of each step, update the README (Roadmap table, Features, Installation, Usage) and add tests for the step, as described in `CONTRIBUTING.md`.

1. **Open/close shell:** menu-bar app (Settings… + hotkey row, settings window stubbed), hotkey, fullscreen black window on the main display, dismiss on any input. Compare the feel side by side with a real macOS screensaver.
2. **SpotifyInfo:** launch/quit tracking, notification + AppleScript, content type and ad context, printed to the console. Confirm it never launches Spotify. Test songs, paused, podcasts, music ads, podcast ads.
3. **Open rules + icon flash** wired to the hotkey.
4. **Overlay page** matched to the reference screenshots, over a placeholder animated gradient (no audio needed yet), covering every state in the table.
5. **Idle trigger.**
6. **Visualizer:**
   - Butterchurn in the page with bundled presets and fake audio.
   - Process tap → FFT/PCM → real audio into Butterchurn.
   - Preset controls (shuffle, timing, blend, favorites, blocklist).
   - Custom preset folder: `.json` loading, folder watching, `.js` plugin loading (with the audio and CSP restrictions), Import/Open/Reload controls in settings, then `.milk` conversion with caching and failure handling.
7. **Polish:** fades, launch at login, brightness slider in settings.

## Later

- Multi-display: one window per `NSScreen`, visualizer on each (or mirrored), overlay on main only or all, and handle display changes while open (`NSApplication.didChangeScreenParametersNotification`).

## Requirements

- macOS 14.2+ (process tap)
- Spotify desktop app
