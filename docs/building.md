# Building and developing

How to build either app from source, run the checks, and cut a release. To install a released build instead, see the [README](../README.md#install).

## What you need

- **The page:** Node 22 or later.
- **macOS:** macOS 26 or later, full Xcode 26 or later (not just the Command Line Tools) and a free Apple Development certificate (Xcode's personal team).
- **Windows:** Windows 11, the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Build the Mac app

Building it yourself signs the app with your own certificate, so the permissions stay across updates.

1. Clone the repo and create your local signing config:

   ```bash
   cp mac/Config/Local.example.xcconfig mac/Config/Local.xcconfig
   ```

   Set `DEVELOPMENT_TEAM` in `mac/Config/Local.xcconfig` to your team ID. It's the `OU=` value printed by:

   ```bash
   security find-certificate -c "Apple Development" -p | openssl x509 -noout -subject
   ```

   Always sign with the same certificate. macOS ties the app's permissions to its signature, so a changing signature makes permission prompts come back.

2. Double-click `IdleViz.command` in the `mac` folder in Finder (or run `mac/IdleViz.command`). It builds the Release app, replaces `/Applications/IdleViz.app` with it, quitting a running copy first, and opens it. Run it again after pulling changes to update.

   To do it by hand instead, open `mac/IdleViz.xcodeproj` in Xcode and run the **IdleViz** scheme, or build from Terminal in `mac/`:

   ```bash
   xcodebuild build -project IdleViz.xcodeproj -scheme IdleViz -configuration Release -derivedDataPath .build/xcode
   ```

3. Copy `mac/.build/xcode/Build/Products/Release/IdleViz.app` to `/Applications` and open it. The app has no Dock icon; it lives in the menu bar.

On first launch a welcome window asks for the permissions; see [First run on the Mac](usage.md#first-run-on-the-mac).

## Build the Windows app

In PowerShell, from the repo:

```powershell
cd windows
.\build-installer.ps1 -Install
```

This builds the app and `windows\artifacts\IdleViz-Setup.exe`, runs it without questions (so with the wizard's default options, listed in [usage.md](usage.md#windows), or as they were at the last install), and starts IdleViz. Run it again after pulling changes to update. Without `-Install` it only builds the setup file, which you can run yourself to get the wizard.

## Development

### The page

You need Node 22 or later. The JavaScript side (plugins, overlay page), which both apps share, is checked with:

```bash
npm install
npm run check
```

`npm run check` runs ESLint, Prettier, a TypeScript typecheck over the page's JavaScript, and the Vitest suite. The test suite loads every visualizer plugin against a fake WebGL2 context and checks it against the plugin guide: required exports, canvas sizing, no NaN values sent to the GPU, everything freed on dispose, and no forbidden APIs.

### macOS

The Swift side is split in two. `IdleVizCore` (`Package.swift`, `Sources/`, `Tests/`) holds logic that runs without a screen, such as the dismiss rules and parsing Spotify's replies. The app target in `IdleViz.xcodeproj` (`IdleViz/`) is a thin AppKit and SwiftUI layer on top. In `mac/`:

```bash
swift build && swift test
swiftlint lint --strict
```

In Debug builds, the launch argument `-IdleVizNoDismiss YES` keeps the window open so it can be inspected; trigger it again to close. `-IdleVizShowSettings YES` opens the settings window at launch, `-IdleVizShowWelcome YES` the welcome window, `-IdleVizFakePermissions automation=notAsked,audio=denied` shows the yellow icon, error rows and welcome window in that state without touching the real permissions, `-IdleVizFakeUpdate 9.9.9` offers that version in the popup and in settings as if it were the latest release, `-IdleVizDetectDelay YES` runs Detect delay four seconds after launch, and `-IdleVizOpenAtLaunch YES` opens the visualizer three seconds after launch (unlike `open idleviz://open`, that can't end up in another copy of the app). The page is inspectable in Debug builds: with the window open, attach Safari's Web Inspector from **Develop → [your Mac] → IdleViz**. The scheme has this argument ready to tick. Each open logs whether macOS let the app activate, and each preset change is logged too:

```bash
log stream --predicate 'subsystem == "com.xauno.IdleViz"'
```

Add `--level debug` to also see, once a second, what the tap delivered (buffers, gain, levels) and how many frames the page rendered and received.

### Windows

The C# side is split the same way: `IdleViz.Core` holds the logic and its tests, and `IdleViz.App` is the WinUI 3 layer on top. In `windows/`:

```powershell
dotnet format --verify-no-changes
dotnet build
dotnet test
```

Debug builds take switches on the command line. `--pretend-update` offers version 99.0.0 without waiting for a real release, so the row in the flyout and the text under **Check for updates** can be tried; add `--show-flyout` or `--show-settings` to open them at launch:

```powershell
dotnet run --project IdleViz.App -- --pretend-update --show-flyout
```

In any build, **Check now** under **Check for updates** in settings asks GitHub at once and says what it found, and the answer is written to the log.

The build treats warnings as errors. Details are in [docs/windows.md](windows.md).

### CI and releases

CI runs all of this on every pull request, and on a `macos-26` runner builds the Mac app unsigned with `xcodebuild` and then the disk image, which it mounts and checks. Pushing a version tag such as `v0.1.2` builds and tests the Windows setup file and the Mac disk image and publishes a GitHub release with both; nothing is published unless both pass. The tag has to match the version in `windows/Directory.Build.props` and `MARKETING_VERSION` in the Xcode project.

To build the disk image yourself, run `mac/installer/build-dmg.sh` (the result is `mac/artifacts/IdleViz.dmg`) and check it with `mac/installer/test-dmg.sh`. The app icon and the image's background are drawn by `swift installer/make-assets.swift`, run in `mac/`, and committed.

## Project layout

```
.
├─ web/                    The page both apps show: visualizer and overlay HTML, CSS and JS, the plugin frame and its runner, the converter page, Figtree font
│  ├─ vendor/              Butterchurn, its preset packs and the Milkdrop converter, copied unchanged from npm
│  └─ visuals/             Bundled visualizer plugins (aurora.js, the example plugin)
├─ tests/                  Vitest suite for the page, with fake WebGL helpers
├─ mac/                    The Mac app
│  ├─ IdleViz.xcodeproj    App target: bundle, signing
│  ├─ IdleViz/             The app: menu-bar app, settings window, welcome window and permission checks, triggers, fullscreen window, dismiss, keep awake, power source, Spotify info, Spotify audio tap, web view, presets folder and Milkdrop converter, Info.plist, entitlements
│  ├─ Package.swift        IdleVizCore Swift package (testable logic)
│  ├─ Sources/IdleVizCore/ The update check, dismiss rules, the like and skip keys, open rules, idle timing and skip rules, keep-awake and battery times, fade times, permission states, brightness and overlay settings, page scheme and CSP, overlay payload, URL commands, activation stats, Spotify query parsing and tracking, audio analysis (bands, automatic gain, frame packing), page status checks, preset settings, the custom presets folder (scanning, import names, Milkdrop conversion checks), the audio delay (per-device setting, delay line, delay detection, the manual delay test's timing and beeps)
│  ├─ Tests/               The Swift tests (IdleVizCoreTests)
│  ├─ Config/              Build settings; your signing team goes in Local.xcconfig
│  ├─ installer/           Builds and checks the disk image, draws the app icon and the image's background
│  └─ IdleViz.command      Builds the app, installs it in /Applications and opens it
├─ windows/                The Windows app: IdleViz.Core (logic) and its tests, IdleViz.App (WinUI 3), the installer script
├─ docs/                   The usage guide, this file, the roadmap, the custom visualizer guide and its test page (aurora-demo.html), the Windows notes, what the Mac app still needs, the README's images
├─ project.md              Design of the Mac app and the decisions behind it
├─ AGENTS.md               Instructions for AI coding agents (CLAUDE.md points to it)
├─ LICENSE                 MIT license
└─ .github/                CI and release workflows, PR template, Dependabot
```
