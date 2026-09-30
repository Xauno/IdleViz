# Spotify Music Visualizer

[![CI](https://github.com/Xauno/Spotify-Music-Visualizer/actions/workflows/ci.yml/badge.svg)](https://github.com/Xauno/Spotify-Music-Visualizer/actions/workflows/ci.yml)
![Platform: macOS 26+](https://img.shields.io/badge/platform-macOS%2026%2B-lightgrey)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Status: in development](https://img.shields.io/badge/status-in%20development-orange)

A macOS menu-bar app that turns your Mac into a music display. When the Mac goes idle, or when you press a hotkey, it opens a fullscreen [Butterchurn](https://github.com/jberg/butterchurn) visualizer that reacts to Spotify's audio, with a now-playing overlay styled after the Spotify TV app. Any mouse, key or trackpad input closes it.

It is not a real screensaver, just a fullscreen window on top of everything.

> **Status: in development.** The menu-bar app opens a fullscreen black window from a hotkey or a URL and closes it on any input. It reads what Spotify is playing and writes it to the system log, but doesn't show it yet. There is no visualizer or overlay yet. See [Roadmap](#roadmap) for progress.

## Contents

- [Features](#features)
- [Roadmap](#roadmap)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Custom visualizers](#custom-visualizers)
- [How it works](#how-it-works)
- [Development](#development)
- [Project layout](#project-layout)
- [Contributing](#contributing)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## Features

Working now:

- Menu-bar app with a small glass popup: **Settings…** and the current open hotkey.
- Opens a fullscreen black window on the main display from a global hotkey (⌃⌥V by default), with `open idleviz://open`, or from **Open now** in settings.
- Closes on any input (mouse movement, click, scroll, key, modifier key, trackpad gesture), then returns focus to the app you were using. The cursor is hidden while it's open.
- Settings window with a hotkey recorder and an **Open now** button.
- Reads what Spotify is playing (title, artist, album, playing or paused, position, length) and whether it's a song, a podcast or an ad, and downloads the album art. For now this only goes to the system log. It never launches Spotify: it only asks while Spotify is running, and updates when Spotify says something changed rather than polling.

Planned:

- Opens after a set idle time too, and only when Spotify is running with a track loaded.
- Shows the now-playing info on screen (it's only logged for now).
- Reacts to Spotify's audio only, through a Core Audio process tap. The visuals never hear the microphone or other system audio.
- Butterchurn (WebGL Milkdrop) visualizer with hundreds of bundled presets, shuffle, blend time and a blocklist.
- Bring your own presets: drop Butterchurn `.json` or Milkdrop `.milk` files, or custom `.js` visual plugins, into a folder.
- Now-playing overlay with album art, title, artist and progress bar. Songs, paused songs and ads each get their own layout. Podcasts show no overlay.
- Keeps the screen awake while it's showing, up to a limit you set (1 hour by default). After that the Mac sleeps and locks as usual.
- Optional separate idle and keep-awake times for when the Mac is on battery.
- Audio delay for Bluetooth and AirPlay speakers, set by hand or measured with a quick microphone check, saved per device.
- The menu-bar icon turns yellow when a permission is missing, and the popup says what to fix.
- Adjustable visualizer brightness, launch at login, and a small settings window.
- Never launches Spotify and never uses Spotify's web API or a login.

## Roadmap

Each step from [project.md](project.md) becomes one pull request, and this table is updated as it merges.

| Step | What                                                                              | Status  |
| ---- | --------------------------------------------------------------------------------- | ------- |
| 0    | Repo tooling: CI, tests, PR workflow, plugin guide, Aurora example plugin         | Done    |
| 1    | Open/close shell: Xcode project, menu-bar app, hotkey, fullscreen window, dismiss | Done    |
| 2    | Audio spike: prove Spotify audio can drive Butterchurn (findings only)            | Done    |
| 3    | Spotify now-playing: launch/quit tracking, track info, artwork, content type, ads | Done    |
| 4    | Open rules and menu-bar icon flash                                                | Planned |
| 5    | Overlay page matched to the Spotify TV app, all states                            | Planned |
| 6    | Idle trigger, with skip rules (locked screen, video or call playing)              | Planned |
| 7a   | Visualizer: Butterchurn with bundled presets                                      | Planned |
| 7b   | Visualizer: real Spotify audio through the process tap                            | Planned |
| 7c   | Visualizer: preset controls                                                       | Planned |
| 7d   | Visualizer: custom preset folder and sandboxed plugins                            | Planned |
| 7e   | Visualizer: audio delay, with a microphone-based detector                         | Planned |
| 8    | Polish: fades, launch at login, brightness, keep-awake, battery, permission setup  | Planned |

## Requirements

- macOS 26 or later
- The Spotify desktop app
- To build: full Xcode 26 or later (not just the Command Line Tools) and a free Apple Development certificate (Xcode's personal team)

## Installation

The app is built from source for personal use. There is no download.

1. Clone the repo and create your local signing config:

   ```bash
   cp Config/Local.example.xcconfig Config/Local.xcconfig
   ```

   Set `DEVELOPMENT_TEAM` in `Config/Local.xcconfig` to your team ID. It's the `OU=` value printed by:

   ```bash
   security find-certificate -c "Apple Development" -p | openssl x509 -noout -subject
   ```

   Always sign with the same certificate. macOS ties the app's permissions to its signature, so a changing signature makes permission prompts come back.

2. Open `IdleViz.xcodeproj` in Xcode and run the **IdleViz** scheme, or build from Terminal:

   ```bash
   xcodebuild build -project IdleViz.xcodeproj -scheme IdleViz -configuration Release -derivedDataPath .build/xcode
   ```

3. Copy `.build/xcode/Build/Products/Release/IdleViz.app` to `/Applications` and open it. The app has no Dock icon; it lives in the menu bar.

4. With Spotify running, macOS asks whether IdleViz may control Spotify. Click **OK**; the app uses this only to read what's playing. If you clicked **Don't Allow**, turn it on in System Settings → Privacy & Security → Automation.

## Usage

- **Menu bar:** click the waveform icon for a small popup with **Settings…** (⌘,) and the current open hotkey.
- **Open:** press the hotkey (⌃⌥V by default), run `open idleviz://open` in Terminal, or click **Open now** in settings.
- **Close:** move the mouse, click, scroll, press any key or use a trackpad gesture. Input in the first 0.4 s after opening is ignored, so the hotkey itself doesn't close it. Keys you're still holding after that are ignored until you let go; pressing one again closes it.
- **Settings:** change the hotkey. Close the window with its red button; ⌘Q quits the app while settings is focused.

- **Now playing:** the app logs what Spotify is playing whenever it changes, and every 5 s while the window is open. Watch it with:

  ```bash
  log stream --predicate 'subsystem == "com.xauno.IdleViz" AND category == "spotify"'
  ```

Idle opening, the Spotify check before opening, the visualizer and the overlay are not built yet.

## Custom visualizers

You can add your own visuals without touching the app. The easiest way is a single `.js` file that draws into a canvas and receives Spotify's audio levels every frame. [`aurora.js`](aurora.js) is a complete working example, and the full guide is [docs/custom-visualizer.md](docs/custom-visualizer.md).

## How it works

One borderless window holds one web page. The page stacks the Butterchurn canvas, a dim layer and the Spotify-style overlay. A Swift helper watches for idle time and hotkeys, reads now-playing info from Spotify, and sends audio analysis to the page about 60 times a second. The full design, including the decisions behind it, is in [project.md](project.md).

## Development

You need Node 22 or later. The JavaScript side (plugins, overlay page) is checked with:

```bash
npm install
npm run check
```

`npm run check` runs ESLint, Prettier, a TypeScript typecheck over the plugin files, and the Vitest suite. The test suite loads every visualizer plugin against a fake WebGL2 context and checks it against the plugin guide: required exports, canvas sizing, no NaN values sent to the GPU, everything freed on dispose, and no forbidden APIs.

The Swift side is split in two. `IdleVizCore` (`Package.swift`, `Sources/`, `tests/IdleVizCoreTests/`) holds logic that runs without a screen, such as the dismiss rules and parsing Spotify's replies. The app target in `IdleViz.xcodeproj` (`IdleViz/App/`) is a thin AppKit and SwiftUI layer on top.

```bash
swift build && swift test
swiftlint lint --strict
```

In Debug builds, the launch argument `-IdleVizNoDismiss YES` keeps the window open so it can be inspected; trigger it again to close. The scheme has this argument ready to tick. Each open logs whether macOS let the app activate:

```bash
log stream --predicate 'subsystem == "com.xauno.IdleViz"'
```

CI runs all of this on every pull request, and builds the app unsigned with `xcodebuild` on a `macos-26` runner.

## Project layout

```
.
├─ project.md              Design and build order
├─ LICENSE                 MIT license
├─ AGENTS.md               Instructions for AI coding agents (CLAUDE.md points to it)
├─ Package.swift           IdleVizCore Swift package (testable logic)
├─ Sources/IdleVizCore/    Dismiss rules, URL commands, activation stats, Spotify query parsing and tracking
├─ IdleViz.xcodeproj       App target: bundle, Info.plist, entitlements, signing
├─ IdleViz/App/            Menu-bar app, settings window, triggers, fullscreen window, dismiss, Spotify info
├─ Config/                 Build settings; your signing team goes in Local.xcconfig
├─ aurora.js               Example visualizer plugin
├─ aurora-demo.html        Test page that feeds a plugin audio from a file or microphone
├─ docs/                   Guides, including the custom visualizer guide
├─ tests/                  Vitest suite, fake WebGL helpers, and the Swift tests (IdleVizCoreTests)
└─ .github/                CI workflow, PR template, Dependabot
```

## Contributing

`main` is protected. All changes go through a pull request with passing CI. See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow.

## License

[MIT](LICENSE). Milkdrop presets and plugins that you import yourself keep their own licenses.

## Acknowledgments

- [Butterchurn](https://github.com/jberg/butterchurn) and [Milkdrop](https://www.geisswerks.com/milkdrop/), the visualizer engine and the presets it plays
- [KeyboardShortcuts](https://github.com/sindresorhus/KeyboardShortcuts) for the global hotkey
- [Figtree](https://github.com/erikdkennedy/figtree) for the overlay font
