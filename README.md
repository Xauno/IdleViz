# Spotify Music Visualizer

[![CI](https://github.com/Xauno/Spotify-Music-Visualizer/actions/workflows/ci.yml/badge.svg)](https://github.com/Xauno/Spotify-Music-Visualizer/actions/workflows/ci.yml)
![Platform: macOS 14.2+](https://img.shields.io/badge/platform-macOS%2014.2%2B-lightgrey)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Status: planning](https://img.shields.io/badge/status-planning-orange)

A macOS menu-bar app that turns your Mac into a music display. When the Mac goes idle, or when you press a hotkey, it opens a fullscreen [Butterchurn](https://github.com/jberg/butterchurn) visualizer that reacts to Spotify's audio, with a now-playing overlay styled after the Spotify TV app. Any mouse, key or trackpad input closes it.

It is not a real screensaver, just a fullscreen window on top of everything.

> **Status: planning.** The design is written and the repo tooling is in place, but none of the app is built yet. See [Roadmap](#roadmap) for progress.

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

Planned (nothing below is implemented yet):

- Opens after a set idle time, from a global hotkey, or with `open idleviz://open`, and only when Spotify is running with a track loaded.
- Closes on any input, as sensitive as a macOS screensaver.
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
| 1    | Open/close shell: Xcode project, menu-bar app, hotkey, fullscreen window, dismiss | Planned |
| 2    | Audio spike: prove Spotify audio can drive Butterchurn (findings only)            | Planned |
| 3    | Spotify now-playing: launch/quit tracking, track info, artwork, content type, ads | Planned |
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

- macOS 14.2 or later (needed for the Core Audio process tap)
- The Spotify desktop app

## Installation

There is nothing to install yet. Once step 1 lands, this section will explain how to build and run the app from source.

## Usage

Planned behavior:

- **Menu bar:** a small menu with a **Settings...** button and the current hotkey shown below it.
- **Open now:** press the hotkey, or run `open idleviz://open` in Terminal.
- **Idle:** set the idle timeout in Settings (5, 10, 15 or 30 minutes, or off).
- **Close:** move the mouse, click, scroll or press any key.

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

Swift code, once it exists, is built and tested with `swift build` and `swift test`, and linted with SwiftLint.

CI runs all of this on every pull request.

## Project layout

```
.
├─ project.md              Design and build order
├─ LICENSE                 MIT license
├─ AGENTS.md               Instructions for AI coding agents (CLAUDE.md points to it)
├─ aurora.js               Example visualizer plugin
├─ aurora-demo.html        Test page that feeds a plugin audio from a file or microphone
├─ docs/                   Guides, including the custom visualizer guide
├─ tests/                  Vitest suite and fake WebGL helpers
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
