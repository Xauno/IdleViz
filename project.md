# Idle Visualizer + Spotify Overlay (macOS)

A menu-bar app that opens a fullscreen music visualizer with a Spotify now-playing overlay when the Mac goes idle (or on command), and closes on any input. Not a real screensaver, just a fullscreen window on top of everything.

## Behavior summary

- **Opens** after N idle minutes, or from a global hotkey / terminal command, but only if Spotify is running and has a current track (playing or paused). Otherwise it doesn't open, and a manual trigger flashes the menu-bar icon.
- **Doesn't open on idle** while the screen is locked, or while another app is keeping the display awake (a video, a call, a presentation). Manual triggers skip that second check, since you're clearly there.
- **Closes** on any input (mouse/trackpad movement, click, scroll, key, gesture), as sensitive as a macOS screensaver. It also fades out if Spotify quits or the track disappears.
- **Stays awake, up to a limit.** While it's showing, the display doesn't sleep. After the keep-awake limit (default 1 hour, adjustable in settings) it fades out and the Mac goes back to its normal sleep, screensaver and lock schedule.
- **Overlay** is Spotify-only and looks like the Spotify TV app's now-playing screen, with the visualizer as the background. It can be turned off in settings, leaving only the visualizer. Font: Figtree.
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
| Playing on another device (Spotify Connect) | Yes | Same as the rows above. The visuals get silence and drift slowly. |

**Non-goals:** other players (Apple Music, browsers), Spotify's Web API (no login/OAuth/Premium dependency), a `.saver` bundle, third-party visualizer apps (Synesthesia etc.), multi-display (see "Later"), and distribution to other people. The app is for personal use: no notarization, Developer ID or auto-updates. If that ever changes, the app name must not contain "Spotify" (Spotify's brand rules).

---

## Build and signing

- **Xcode project + Swift package.** `IdleViz.xcodeproj` holds the app target: the `.app` bundle, `Info.plist`, entitlements, the `idleviz://` URL scheme and signing. All logic that can be tested without a screen lives in a local Swift package, `IdleVizCore` (`Package.swift` at the repo root), which the app target depends on. The app target stays a thin AppKit/SwiftUI layer. Building needs full Xcode, not just the Command Line Tools.
- **Testability.** Put anything that touches the system behind a small protocol (AppleScript runner, idle-time source, clock, power assertions, Spotify running state) so `IdleVizCore` logic runs against fakes in `swift test`. CI can't test permissions, Spotify or audio, so those are checked by hand and written up in each PR's Verification section.
- **CI.** `swift test` for the package, `xcodebuild build` for the app with `CODE_SIGNING_ALLOWED=NO`, and SwiftLint.
- **Signing.** Sign every local build with the same free Apple Development certificate (Xcode's personal team). macOS ties the Automation and System Audio Recording permissions to the signature, so ad-hoc or changing signatures make the prompts come back or silently return all-zero audio.
- **Hardened Runtime** on, with the `com.apple.security.automation.apple-events` entitlement.
- **Not sandboxed.** The sandbox would need a temporary-exception entitlement for Apple Events to Spotify and would move the presets folder into a container.
- **Install** by copying the built app to `/Applications`. Launch at login (`SMAppService`) works best from there.

---

## Permissions and first launch

The app needs two permissions: **Automation** (to ask Spotify what's playing) and **System Audio Recording** (the process tap).

- On first launch, a small welcome window explains both and triggers the prompts on purpose: one AppleScript query (if Spotify is running) and a short tap. That way a prompt never pops up during an idle open while nobody is at the Mac.
- Check Automation without prompting with `AEDeterminePermissionToAutomateTarget(..., askUserIfNeeded: false)`.
- There's no public API to check the audio permission. Treat several seconds of exact-zero buffers while Spotify reports "playing" as "probably denied" and log it.
- No microphone or camera permission, ever (see the audio guarantee).

---

## Architecture

One window, one web page. The page holds the Butterchurn visualizer canvas, a sandboxed frame for custom JS plugins, and the overlay.

```
Menu-bar helper (Swift)
├─ Triggers: IdleWatcher, Hotkey, URL scheme ──► OpenRules ──► WindowController
├─ DismissWatcher ──────────────────────────────────────────► WindowController (close)
├─ KeepAwake (display-sleep assertion + time limit) ───────► WindowController (close at limit)
├─ SpotifyInfo (notifications + AppleScript + artwork) ──► page: window.nowPlaying(json)
├─ SpotifyAudioTap (Core Audio → FFT in Swift) ──► page: window.audioFrame(data)
└─ PresetLibrary (scan/watch custom folder) ───► page: window.setCustomPresets(json)

Page layers (bottom → top): Butterchurn canvas or plugin frame → dim layer → overlay
```

### Menu-bar helper
**UI reference:** build the menu-bar popup and the settings window to match [mockups.html](mockups.html) (layout, grouping, row order, sizes, and build notes). Open it in a browser.

- `LSUIElement = YES`, launch at login (`SMAppService.mainApp.register()`).
- The menu-bar popup is a small glass-style popover (SwiftUI `MenuBarExtra` with `.window` style, or `NSPopover`), not a plain `NSMenu`. The background is real Liquid Glass (`glassEffect`) on macOS 26+ and a system blur material on older versions, in one small view chosen with `#available(macOS 26, *)`; the minimum stays 14.2. It is deliberately tiny, with two rows:
  1. **Settings…** (the only clickable item) opens the separate settings window.
  2. Below it, a disabled, informational row showing the current open hotkey (e.g. "Open visualizer: ⌃⌥V"), read from `KeyboardShortcuts` so it updates if the shortcut changes.
- There is no Quit item in the menu or in settings. The red close button closes the settings window, and ⌘Q (while the window is focused) quits the app.
- **Settings window** (separate native window, a normal `NSWindow` with SwiftUI content that follows the macOS light/dark appearance automatically, with no setting; the app switches to `.regular` activation policy while it's open so it can take focus, then back to accessory on close). It is small, portrait and fixed-size (about 340 × 560 pt): no `.resizable` in the style mask, `collectionBehavior = [.fullScreenNone]`, zoom button disabled. It is one scrolling page (no sidebar or tabs) with three sections, in this order:
  - **General:** idle timeout (5/10/15/30 min, Off), keep screen awake for (30 min, 1 hour, 2 hours, 4 hours; default 1 hour), open hotkey recorder (`KeyboardShortcuts.Recorder`), an "Open now" button, launch at login.
  - **Visualizer:** Show Spotify overlay toggle, brightness slider (50–100%), mode (Single or Shuffle). Single: a picker for the one visualizer to show. Shuffle: shuffle-from filter (all/bundled/custom/favorites), seconds per preset, blend time. Then favorites and blocklist management.
  - **Presets:** plugin trust warning, Import Presets…, Open Presets Folder, Reload Presets, and the list of presets that failed to load.
- Settings in `UserDefaults`. The window writes them and the helper applies changes live.

### Triggers
- **Idle:** `CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)` (any input). Instead of polling on a fixed interval, schedule the next check for `timeout − idleTime` (the earliest it could fire).
- **Idle skip rules** (idle trigger only):
  - The screen is locked (`CGSessionCopyCurrentDictionary`, `CGSSessionScreenIsLocked`), or the session isn't on the console (fast user switching).
  - Another process, not Spotify, holds a `PreventUserIdleDisplaySleep` / `NoDisplaySleepAssertion` power assertion (`IOPMCopyAssertionsByProcess`). This covers fullscreen video, calls and presentations, whether or not they're frontmost.
  - After the keep-awake limit closed the visualizer, don't reopen until there has been new input. Otherwise it would reopen at once, since the Mac is still idle.
- **Hotkey:** `KeyboardShortcuts` Swift package.
- **Terminal:** URL scheme, so `open idleviz://open` works.
- **Open rules check:** Spotify is running (see below) and AppleScript returns a current track. On failure from a manual trigger, flash the menu-bar icon (alt icon 2–3 times over ~1 s).

### Dismiss
- Make the window key (subclass `NSWindow`, override `canBecomeKey` → `true`) so a **local** `NSEvent` monitor receives keys without needing Accessibility/Input Monitoring permission. Add a global monitor for mouse events as a backstop.
- **Activation may be refused.** On macOS 14+ activation is cooperative, so `NSApp.activate()` can be denied for an idle open (another app is active and there was no user action). Then the window isn't key and the local monitor gets no keys. The backup below still closes the window, but the key that woke it goes to the app behind (for example, a letter typed into a document). Check in step 1 how often this happens.
- Remember the frontmost app when opening and reactivate it on close, so focus returns where it was.
- Events: `.mouseMoved`, `.leftMouseDown`, `.rightMouseDown`, `.otherMouseDown`, `.scrollWheel`, `.keyDown`, `.flagsChanged`, `.gesture`, `.magnify`, `.swipe`.
- Threshold ≈ 0: close on the first `.mouseMoved` with any nonzero delta (≥ 1 px if the trackpad sends phantom zero-delta events).
- Ignore input for the first ~300–500 ms after opening so the trigger itself doesn't close it.
- Backup: while open, check `secondsSinceLastEventType` every ~100 ms; if it's less than the time since opening (minus the grace period), close. This needs no permissions and also catches keys when the window isn't key.
- Hide the cursor while open, restore on close.
- **Debug switch:** in Debug builds, the launch argument `-IdleVizNoDismiss YES` turns dismiss off, so the page can be inspected while it's open.

### Keep awake
- While the window is open, hold a `kIOPMAssertionTypePreventUserIdleDisplaySleep` assertion (`IOPMAssertionCreateWithName`, named "IdleViz visualizer"). Release it on close.
- The limit (setting `keepAwakeLimit`, default 60 min) counts from when the window opened. When it's reached, fade out, close and release the assertion, so the normal display sleep, screensaver and lock take over.
- Security tradeoff, accepted: during the limit the Mac doesn't lock on its own.

### Window
- One borderless `NSWindow` on `NSScreen.screens.first` (the menu-bar display). Create it via a function that takes an `NSScreen`, so multi-display is easy later.
- `level = .screenSaver`, `collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]`.
- Content: one `WKWebView`. Serve everything from a custom scheme with a `WKURLSchemeHandler`, not `file://`:
  - `idleviz-app://app/…` serves the bundled `web/` folder.
  - `idleviz-app://presets/…` serves the custom presets folder, with `Access-Control-Allow-Origin: *` so the sandboxed plugin frame (an opaque origin) can import from it.
  - This gives one origin, working ES modules, and a place to send the Content-Security-Policy as a response header. `idleviz://` stays the external "open" URL scheme and is never loaded in the page.
- Keep the window and web view alive between opens (hide instead of destroying it) so opening is instant.
- **Recovery:**
  - Reload the page and re-send state on `webViewWebContentProcessDidTerminate`.
  - Rebuild Butterchurn on `webglcontextlost`, which can happen after sleep and wake.
  - Close the visualizer on `NSWorkspace.willSleepNotification` and `NSApplication.didChangeScreenParametersNotification`, since the main display may have changed.
- Set `isInspectable = true` in Debug builds so Safari's Web Inspector can attach.
- Deny all media capture (see the audio guarantee) and set `mediaTypesRequiringUserActionForPlayback = []` in case Web Audio is used.

---

## Spotify now-playing

**Never launch Spotify.** Sending an Apple Event to a closed app launches it, so only talk to Spotify when it's running:
- Track running state with `NSWorkspace` `didLaunchApplicationNotification` / `didTerminateApplicationNotification` (bundle ID `com.spotify.client`), seeded once from `runningApplications`. No polling.

**Getting track info without constant polling:**
- Spotify posts a distributed notification, `com.spotify.client.PlaybackStateChanged`, on play/pause/track change. Observe it with `DistributedNotificationCenter` and use it as the trigger for updates. (Verify which fields it includes; it has historically carried name, artist, album, duration, position, player state and track ID, but not artwork.)
- On each notification (and once when the window opens), run one AppleScript query for the full state, including artwork URL:

```applescript
tell application "Spotify"
    with timeout of 2 seconds
        set s to player state as string
        if s is "stopped" then return "stopped"
        set t to current track
        return s & "||" & (name of t) & "||" & (artist of t) & "||" & (album of t) & "||" & (artwork url of t) & "||" & (duration of t) & "||" & (player position) & "||" & (spotify url of t)
    end timeout
end tell
```

- The page advances the progress bar locally from position + timestamp; no per-second polling. While the window is open, do a light re-sync every ~5 s to correct drift and catch seeks.
- Run via `NSAppleScript`, compiled once, on one dedicated background thread (`NSAppleScript` isn't thread-safe, and a hung Spotify must never block the main thread). Needs `NSAppleEventsUsageDescription`.
- Units: `duration` in **milliseconds**, `player position` in **seconds**.
- "No current track" = state `stopped`, an error, or an empty name/URL.
- **Artwork:** Swift downloads the artwork URL with `URLSession`, keeps a few recent images in memory, and passes the image to the page as a `data:` URL inside the `nowPlaying` JSON. The page itself never uses the network. Local files (`local:`) have no artwork, so the overlay shows a placeholder, designed against the reference screenshots.
- **Spotify Connect:** when Spotify plays on another device, AppleScript still reports the track, so the window opens as usual. The tap finds no local audio, so the visuals get silence. No special detection needed.

**Content type** from the `spotify url` prefix: `track:` / `local:` → song, `episode:` → podcast, `ad:` → ad.
- Ad context: remember the type of the last non-ad item. After a podcast episode, an ad is a podcast ad (no overlay); otherwise it's a music ad (minimal overlay).
- Test how ads report in practice. Ads baked into podcast audio never show up as a separate item and simply count as the podcast.

---

## Overlay design (Spotify TV app look)

Match the Spotify TV app's now-playing screen exactly; the only change is the visualizer background.

- Save reference screenshots (1920×1080 and 4K) in `design/reference/`. The folder is in `.gitignore`: the repo is public and the screenshots are Spotify's copyrighted images, so they stay local. During development, lay a screenshot over the page at 50% opacity to match positions and sizes.
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

### Page security (CSP)
Sent as a response header by the scheme handler for the host page:

```
default-src 'none';
script-src 'self' 'unsafe-eval';
style-src 'self';
font-src 'self';
img-src 'self' data: blob:;
connect-src 'self' idleviz-app://presets;
frame-src 'self';
```

- `'unsafe-eval'` is needed because Butterchurn compiles preset equations with `new Function`. Verify this against the version you use.
- That means custom `.json` and `.milk` presets are code too: their equations run in the host page. The plugin trust warning in settings covers them as well.

---

## Visualizer (Butterchurn)

The only visualizer is **Butterchurn** (WebGL port of Milkdrop), chosen because the goal is a big, browsable library rather than hand-written scenes. There is no external-app mode.

### Audio
- Core Audio process tap (macOS 14.2+) on Spotify only.
  - Find Spotify's audio process objects via `kAudioHardwarePropertyProcessObjectList`. Match every process whose bundle ID starts with `com.spotify.client`, which includes its helper processes, and tap them all.
  - A process only appears in that list once it starts playing audio. Listen for changes to `kAudioHardwarePropertyProcessObjectList` and rebuild the tap when Spotify's processes come or go, not just when Spotify relaunches.
  - `CATapDescription(stereoMixdownOfProcesses:)` with `muteBehavior = .unmuted` → `AudioHardwareCreateProcessTap` → private aggregate device → `AudioDeviceCreateIOProcIDWithBlock`.
  - Needs `NSAudioCaptureUsageDescription`; macOS prompts once (see "Permissions and first launch"). Builds with a changing signature may get all-zero buffers until permission is granted to the right binary.
  - Run it only while the window is open.
- **Analysis happens in Swift** (vDSP). Each frame, Swift computes everything the page needs, so JS only unpacks numbers:
  - the plugin audio object: 64 bands (noise-floored, smoothed), `bass`/`mid`/`treble`, `rms`, and a 1024-sample waveform;
  - Butterchurn's input: 1024-sample 8-bit time-domain data, mono plus left and right.
  - Swift packs this into one binary frame and sends it ~60×/s with `evaluateJavaScript`, base64-encoded.
- **Automatic gain.** The tap captures after Spotify's own volume slider (but before the system volume), so a low Spotify volume would mean weak visuals. Normalize with a slow automatic gain on the RMS level, with a gate so real silence stays silent.
- **Feeding Butterchurn.** Prefer passing the levels straight to `visualizer.render({ audioLevels: { timeByteArray, timeByteArrayL, timeByteArrayR } })`, which skips Web Audio entirely. Verify that the version you use supports this. Fallback: an `AudioWorklet` source feeding Butterchurn's analyser, connected through a zero-gain node to the destination, since WebKit may not process nodes that aren't connected to the output.
- **Spotify-only audio guarantee** (applies to Butterchurn and to every custom plugin):
  - The only audio source is the Spotify process tap above. Never use a global/system tap, an input device, or the microphone. Always build the tap with `CATapDescription(stereoMixdownOfProcesses:)` containing Spotify's process objects only. If none are found, send silence.
  - Audio reaches JS only through `window.audioFrame(data)`, then a single host wrapper hands each visual its frame. A plugin never gets an `AudioContext`, `MediaStream` or node it could wire to something else.
  - The web view denies all media capture: implement `WKUIDelegate`'s `requestMediaCapturePermissionFor` to always return `.deny`, and add no microphone/camera entitlements or usage strings. This stops a plugin from calling `getUserMedia` to get mic or other audio.
  - If Spotify quits, plays on another device, or the track is an ad or podcast with no audio source, the frame is silence (zeros), not a fallback to anything else.
  - Test: play other audio (a YouTube tab, system sounds) while Spotify is paused and confirm the visualizer stays flat.

### Performance
- Cap rendering at 60 fps, including on 120 Hz ProMotion displays (skip every other `requestAnimationFrame`).
- Render Butterchurn below full device resolution on large displays (for example at most ~2560 px wide) and let the GPU scale it up. Tune by eye and by power use.

### Presets
- **Bundled:** `butterchurn-presets` (hundreds), shipped in the app.
- **Custom (folder import):** any preset you add yourself, loaded alongside the bundled ones.
  - Folder: `~/Library/Application Support/IdleViz/Presets/` (created on first launch; subfolders allowed, so a downloaded pack can be dropped in as-is).
  - Accepted files:
    - `.json`: Butterchurn-format presets, loaded directly.
    - `.js`: custom visual plugins (see "Custom JS plugins" below).
    - `.milk`: original Milkdrop presets, converted with `butterchurn-preset-converter`. Converted results are cached (e.g. `Presets/.cache/<hash>.json`) so conversion runs once per file. Verify the converter runs fully in-page/offline in the version you use; if it needs a server or build step, convert in a Node script instead.
  - Settings window controls (Presets section):
    - **Import Presets…** opens an `NSOpenPanel` (files or folders) and copies the picks into the presets folder.
    - **Open Presets Folder** reveals it in Finder.
    - **Reload Presets** rescans it.
  - `PresetLibrary.swift` scans the folder and watches it for changes (`DispatchSource` file-system events or FSEvents), so dropping files in updates the library without a restart. It sends the list to the page with `window.setCustomPresets(json)`.
  - Each preset is validated (parse, compile its shaders) in a `try/catch` when first loaded. Bad ones are skipped, added to the blocklist, and reported in the settings window ("3 presets failed to load").
  - Presets are tagged by source (bundled / custom) so the settings can shuffle all, bundled only, or custom only.
  - Check each pack's license before sharing the app. User-imported presets stay on the user's machine and are never bundled.
- **Preset control** (in the settings window, since any input closes the visualizer): mode (single visualizer or shuffle), seconds per preset, blend time, shuffle-from filter (all/bundled/custom/favorites), favorites, and a blocklist for presets you dislike.
- **Custom JS plugins:** users can drop `.js` files into the presets folder (subfolders allowed) and they appear in the preset list next to Butterchurn presets, tagged "custom".
  - Each file is an ES module with named exports `init(canvas)`, `frame(audio, time)` and `dispose()`, plus an optional `meta = { name }`. The same interface is used for plugins bundled in `web/visuals/*.js` (see `aurora.js` for an example; it moves to `web/visuals/` in step 7a). The full author guide is in `docs/custom-visualizer.md`.
  - `audio` is a plain object: `{ bands: Float32Array(64), bass, mid, treble, waveform: Float32Array(1024), rms }`, built from the Spotify-only frame (see the audio guarantee above). Bands are 0..1 and log-spaced (about 40 Hz to 16 kHz), already noise-floored and smoothed (fast attack, slow release); `bass`/`mid`/`treble` average bands 0-7, 8-29 and 30-63. The arrays are reused every frame. It is the only audio data a plugin ever sees.
  - Plugins draw only into the canvas they are given. Each `init` gets a fresh canvas, so a plugin picks its own context type (WebGL2 or 2D).
  - **Isolation: each plugin runs in its own sandboxed frame.**
    - The host page creates `<iframe sandbox="allow-scripts">` (no `allow-same-origin`, so it gets an opaque origin) loading `idleviz-app://app/plugin-host.html`, which holds the plugin's canvas.
    - The frame's own CSP: `default-src 'none'; script-src idleviz-app:; img-src data: blob:`. No network, no storage.
    - Each frame, the host posts the audio object to the frame with `postMessage`. A small runner inside the frame copies it into reused arrays and calls the plugin's `frame(audio, time)`.
    - The frame can't reach the overlay DOM, the host's JS, or Swift.
  - **The Swift bridge stays out of reach.** Register the `WKScriptMessageHandler`s in a named `WKContentWorld`, with the host bridge script running in that world, so neither page code nor plugin code can call into Swift. Validate every message anyway.
  - **Loading and failure:**
    - The runner imports the file with `import()` inside `try/catch` and calls `init`.
    - It wraps `frame` so that a throw, or a frame that takes over ~50 ms for several seconds in a row, reports a failure to the host. The host then removes the frame and moves on to the next preset.
    - WebKit may run the frame on the same thread as the host, so a plugin stuck in an endless loop freezes the whole page. The page tells Swift which plugin is starting and sends a heartbeat; if the heartbeat stops for ~3 s, Swift reloads the web view and blocklists that plugin.
  - The regex check in `tests/plugin-contract.test.js` is a lint for plugins in the repo, not a security boundary. The sandboxed frame and CSP are what enforce the limits.
  - The settings window still shows a one-time warning that custom plugins (and custom presets, see "Page security") are code and should only come from sources the user trusts.
  - Same license caveat as presets: imported plugins stay on the user's machine and are never bundled.
  - Settings window shows plugin load failures alongside preset failures, and includes plugins in favorites and the blocklist.
- Alternative considered: native **libprojectM** (open-source Milkdrop engine, C++/OpenGL). Larger ecosystem, but OpenGL is deprecated on macOS and it needs a native rendering layer under the window, so Butterchurn is the simpler start.

---

## File layout

```
IdleViz.xcodeproj                # app target: bundle, Info.plist, entitlements, URL scheme, signing
Package.swift                    # IdleVizCore package (testable logic)
Sources/IdleVizCore/             # open rules, idle/skip rules, keep-awake timing, Spotify parsing, content type, FFT/bands
Tests/IdleVizCoreTests/          # XCTest, runs in CI with swift test
IdleViz/
├─ App/
│  ├─ AppDelegate.swift          # menu bar (Settings… + hotkey row), icon flash
│  ├─ SettingsWindow.swift       # separate settings UI (one scrolling page, per mockups.html)
│  ├─ WelcomeWindow.swift        # first launch: explains and triggers the two permission prompts
│  ├─ Triggers.swift             # idle, hotkey, URL scheme, open rules
│  ├─ DismissWatcher.swift
│  ├─ KeepAwake.swift            # display-sleep assertion + time limit
│  ├─ WindowController.swift     # key-capable borderless window + WKWebView + scheme handler
│  ├─ SpotifyInfo.swift          # launch/quit tracking, notifications, AppleScript, artwork
│  ├─ SpotifyAudioTap.swift      # process tap → IdleVizCore analysis
│  ├─ PresetLibrary.swift        # scan/watch custom preset folder, import, send list to page
│  ├─ Info.plist                 # LSUIElement, NSAppleEventsUsageDescription, NSAudioCaptureUsageDescription
│  └─ IdleViz.entitlements       # hardened runtime + apple-events, no sandbox
├─ web/
│  ├─ index.html
│  ├─ plugin-host.html           # sandboxed frame that runs one custom JS plugin
│  ├─ overlay.css / overlay.js   # nowPlaying(), progress interpolation, states
│  ├─ fonts/                     # Figtree + OFL license
│  └─ visuals/                   # Butterchurn wrapper, preset manager, bundled plugin modules (aurora.js)
└─ design/reference/             # Spotify TV app screenshots (gitignored, local only)
```

## Build order

Each step is one pull request. At the end of each step, update the README (Roadmap table, Features, Installation, Usage) and add tests for the step, as described in `CONTRIBUTING.md`.

1. **Open/close shell:** Xcode project + `IdleVizCore` package, signing, CI building the app with `xcodebuild`. Menu-bar app (Settings… + hotkey row, settings window stubbed), hotkey, fullscreen black window on the main display, dismiss on any input, focus returned to the previous app, the debug no-dismiss switch. Compare the feel side by side with a real macOS screensaver, and note how often activation is refused.
2. **Audio spike (throwaway):** prove the riskiest part before building on it. A process tap on Spotify gets real audio, Swift turns it into levels, and they reach the page ~60×/s and drive Butterchurn with one preset. Check `render({ audioLevels })`, the permission prompt, and CPU/GPU use. The spike code stays on a branch and isn't merged; this step's PR only writes the findings (what worked, what had to change) into `project.md`.
3. **SpotifyInfo:** launch/quit tracking, notification + AppleScript (on its own thread, with a timeout), artwork download, content type and ad context, printed to the console. Confirm it never launches Spotify. Test songs, paused, podcasts, music ads, podcast ads, local files and Spotify Connect.
4. **Open rules + icon flash** wired to the hotkey.
5. **Overlay page** matched to the reference screenshots, served from `idleviz-app://` with the CSP, over a placeholder animated gradient (no audio needed yet), covering every state in the table plus the missing-artwork placeholder.
6. **Idle trigger**, with the skip rules (locked screen, another app keeping the display awake, wait for input after the keep-awake limit).
7. **Visualizer**, split into four PRs:
   - **7a.** Butterchurn in the page with bundled presets and fake audio. Move `aurora.js` to `web/visuals/`.
   - **7b.** Real audio: process tap (with process-list changes), analysis in Swift, automatic gain, silence rules, web view recovery.
   - **7c.** Preset controls (mode, shuffle, timing, blend, favorites, blocklist).
   - **7d.** Custom preset folder: `.json` loading, folder watching, `.js` plugins in sandboxed frames (with the audio, CSP and heartbeat rules), Import/Open/Reload controls in settings, then `.milk` conversion with caching and failure handling.
8. **Polish:** fades, launch at login, brightness slider in settings, keep awake with its time limit setting, first-launch welcome window for permissions.

## Open questions

- **Battery:** should the idle trigger open on battery power or in Low Power Mode?
- **Audio delay:** with Bluetooth or AirPlay speakers, the visuals run ahead of the sound. Add a delay setting?
- **Missing permissions:** after first launch, where should the app show that a permission is missing (for example a warning row in settings)?

## Later

- Multi-display: one window per `NSScreen`, visualizer on each (or mirrored), overlay on main only or all, and handle display changes while open (`NSApplication.didChangeScreenParametersNotification`).

## Requirements

- macOS 14.2+ (process tap)
- Spotify desktop app
- To build: full Xcode (not just the Command Line Tools) and a free Apple Development certificate (Xcode's personal team)
