# IdleViz

[![CI](https://github.com/Xauno/IdleViz/actions/workflows/ci.yml/badge.svg)](https://github.com/Xauno/IdleViz/actions/workflows/ci.yml)
![Platform: macOS 26+ and Windows 11](https://img.shields.io/badge/platform-macOS%2026%2B%20%7C%20Windows%2011-lightgrey)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Status: in development](https://img.shields.io/badge/status-in%20development-orange)

IdleViz turns a Mac or a Windows PC into a music display. When the computer goes idle, or when you press a hotkey, it opens a fullscreen [Butterchurn](https://github.com/jberg/butterchurn) visualizer that reacts to Spotify's audio, with a now-playing overlay styled after the Spotify TV app. Any mouse or key input closes it.

There are two apps, one for macOS and one for Windows 11. Both show the same page, so the visualizer, the presets, the plugins and the overlay are the same on each. Around the page, each has its own native helper: a menu-bar app in Swift on the Mac, and a tray app in C# and WinUI 3 on Windows.

It is not a real screensaver, just a fullscreen window on top of everything.

> **Status: in development.** Every step in the [Roadmap](#roadmap) is built for both apps. The Windows app has a setup file on the [Releases page](https://github.com/Xauno/IdleViz/releases); the Mac app is built from source. The Mac app runs on the main display only, while the Windows app can cover several. The Windows app has only been used on one PC, a desktop with two displays and no battery, so its battery times are untested on real hardware.

## Contents

- [Features](#features)
- [macOS and Windows compared](#macos-and-windows-compared)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Custom visualizers](#custom-visualizers)
- [How it works](#how-it-works)
- [Roadmap](#roadmap)
- [Development](#development)
- [Project layout](#project-layout)
- [Contributing](#contributing)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## Features

Both apps do all of this. Where they differ is in [macOS and Windows compared](#macos-and-windows-compared).

**Opening and closing**

- Lives in the menu bar (Mac) or the tray (Windows), with a small popup: **Settings** and the current open hotkey.
- Opens a fullscreen window from a global hotkey (⌃⌥V on the Mac, Ctrl + Alt + V on Windows), from the URL `idleviz://open`, or from **Open now** in settings.
- Only opens while Spotify is running and has a track loaded, playing or paused. Otherwise the icon flashes and nothing opens.
- Opens by itself after 5 idle minutes (or 10, 15, 30, or never, set in settings). It doesn't open on idle while the screen is locked or while something else is clearly in use, such as a video or a call. After a blocked attempt it waits until you've used the computer again.
- Closes on any input (mouse movement, click, scroll, key), then returns focus to the app you were using. The pointer is hidden while it's open.
- The playback and volume keys don't close it, so you can turn the music up or pause it while it's showing.
- Two more keys don't close it: **L** likes the visualizer on screen (adds it to your favorites, or takes it off again) and shows a heart, and **N** skips to the next one. Both keys can be changed or turned off in settings.
- Fades in when it opens, and fades out quickly on input.
- Keeps the screen awake while it's showing, up to a limit you set (1 hour by default; 30 minutes, 2 hours or 4 hours also available). At the limit it fades out slowly, and the computer sleeps and locks as usual. It doesn't open again until you've used the computer.
- Optional separate idle and keep-awake times for when the computer runs on its battery. They apply the moment you unplug or plug in, even while the visualizer is open. Computers without a battery don't show the option.
- Closes when the computer goes to sleep or the displays change (one is plugged in or removed, or its resolution changes).
- Can start when you log in.

**Now playing**

- Overlay styled after the Spotify TV app: album art, title, artist, progress bar and times, in the Figtree font. When paused, only the progress bar stays, and podcasts show no overlay. If Spotify quits or the track goes away, the overlay fades out and the window stays open.
- Never launches Spotify and never uses Spotify's web API or a login. It reads what's playing from the system, and only while Spotify is running.
- A switch hides the overlay and shows only the visualizer.

**Visualizer**

- [Butterchurn](https://github.com/jberg/butterchurn) (WebGL Milkdrop) with 395 bundled presets. By default it shows a random preset every 30 seconds with a 2.7-second blend, and goes through all of them before repeating one.
- Reacts to Spotify's audio only, captured while the window is open. The visuals never hear the microphone or other apps. A slow automatic gain keeps the visuals lively when Spotify's volume is low, and silence stays silent: paused, quit, or playing on another device, the visuals just drift.
- Preset controls in settings: shuffle or one fixed preset, which presets to shuffle from, time per preset, blend time, favorites, and a blocklist for presets you don't like. Changes apply at once, even while the visualizer is open.
- Adjustable brightness (50 to 100%, 70% by default).
- If the page ever stops responding, the app replaces it within a few seconds.

**Your own presets and plugins**

- Drop Butterchurn `.json` or original Milkdrop `.milk` files, or custom `.js` visual plugins, into the presets folder, or use **Import…** in settings. Subfolders work, so a downloaded pack can go in as it is. The folder is watched, so new files show up without a restart. `.milk` files are converted once and cached.
- Custom plugins run in a sandboxed frame with no network, no storage and no access to the rest of the app. A plugin that throws, runs too slowly or hangs is dropped and listed under **Failed to load** in settings, next to presets that couldn't be read or converted.

**Audio sync**

- Audio delay for Bluetooth and network speakers, which play the sound later than the computer sends it: the visuals and the progress bar wait to match. Set it with a slider (0 to 2.5 s), or press **Detect** and the app measures it with the microphone for about 5 seconds. It is saved for each speaker or pair of headphones, and a device used for the first time starts at the delay the system reports for it.
- A manual delay test for when the microphone can't help, such as with headphones: the app beeps once a second and lights up a panel, and you drag the delay until the light and the beep land together. Spotify is paused while it runs.

## macOS and Windows compared

|                        | macOS                                                                                             | Windows                                                                                                    |
| ---------------------- | ------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| Getting it             | Built from source with Xcode                                                                      | `IdleViz-Setup.exe` from the [Releases page](https://github.com/Xauno/IdleViz/releases), or built yourself |
| Where it lives         | Menu bar                                                                                          | Tray (notification area)                                                                                   |
| Displays               | The main display only                                                                             | One or several: the same visualizer on each, or one picture extended across them                           |
| Permissions            | A welcome window asks for Spotify control and Spotify audio; the microphone is asked at Detect    | No prompts; the microphone follows the Windows privacy setting                                             |
| When something's wrong | Yellow icon while a permission is missing                                                         | Yellow icon while Spotify's sound or track can't be read                                                   |
| Now playing comes from | Spotify itself                                                                                    | The Windows media controls                                                                                 |
| Ads                    | Shown with an "Advertisement" label and the progress bar                                          | Not told apart from songs                                                                                  |
| Idle open is skipped   | While the screen is locked or another app keeps the display awake                                 | While locked, over Remote Desktop, with something fullscreen, or while another app is making sound         |
| Like and skip keys     | Work while the visualizer has keyboard focus; picked from a list                                  | Work even if another window has the keyboard; almost any key, recorded by pressing it; plus a block key    |
| Keys that leave it open | Brightness, keyboard backlight, playback and volume                                              | Playback and volume                                                                                        |
| Settings window        | **General** and **Visualizer**                                                                    | Six sections: **Opening**, **Look**, **Presets**, **Keys**, **Audio sync**, **Displays**                   |
| Presets folder         | `~/Library/Application Support/IdleViz/Presets/`                                                  | `%APPDATA%\IdleViz\Presets\`                                                                               |
| Log                    | `log stream`, subsystem `com.xauno.IdleViz`                                                       | `%LOCALAPPDATA%\IdleViz\logs\idleviz.log`                                                                  |
| Quit                   | ⌘Q while settings is focused                                                                      | **Exit** in the tray icon's right-click menu                                                               |

The things the Windows app has that the Mac app doesn't yet (more than one display, the regrouped settings window, and the newer settings: recorded keys, the block key, preset search and preview, **Close on input** and the visualizer title) are listed under [Planned](#planned).

## Requirements

Both need the Spotify desktop app.

- **macOS:** macOS 26 or later. To build: full Xcode 26 or later (not just the Command Line Tools) and a free Apple Development certificate (Xcode's personal team).
- **Windows:** Windows 11. To build: the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Installation

### macOS

The Mac app is built from source for personal use. There is no download.

1. Clone the repo and create your local signing config:

   ```bash
   cp Config/Local.example.xcconfig Config/Local.xcconfig
   ```

   Set `DEVELOPMENT_TEAM` in `Config/Local.xcconfig` to your team ID. It's the `OU=` value printed by:

   ```bash
   security find-certificate -c "Apple Development" -p | openssl x509 -noout -subject
   ```

   Always sign with the same certificate. macOS ties the app's permissions to its signature, so a changing signature makes permission prompts come back.

2. Double-click `IdleViz.command` in Finder (or run `./IdleViz.command`). It builds the Release app, replaces `/Applications/IdleViz.app` with it, quitting a running copy first, and opens it. Run it again after pulling changes to update. Then skip to step 4.

   To do it by hand instead, open `IdleViz.xcodeproj` in Xcode and run the **IdleViz** scheme, or build from Terminal:

   ```bash
   xcodebuild build -project IdleViz.xcodeproj -scheme IdleViz -configuration Release -derivedDataPath .build/xcode
   ```

3. Copy `.build/xcode/Build/Products/Release/IdleViz.app` to `/Applications` and open it. The app has no Dock icon; it lives in the menu bar.

4. A welcome window opens. With Spotify running, click **Continue**. If Spotify isn't running, open it and the window carries on by itself. macOS then asks two things in turn:
   - Whether IdleViz may control Spotify. Click **OK**; the app uses this only to read what's playing.
   - Whether IdleViz may record system audio. Click **Allow**; the app listens to Spotify's audio only, and only while the visualizer is open.

5. If you said no to either, the menu-bar icon turns yellow and its popup shows what's missing. macOS only asks once, so turn it on in System Settings → Privacy & Security: under **Automation** for Spotify control, or under **Screen & System Audio Recording** → **System Audio Recording Only** for the audio. Without Spotify control the visualizer doesn't open. Without the audio it opens but doesn't react to the music.

6. The first time you press **Detect** next to **Detect delay** in settings, macOS asks for the microphone. This one is optional. The app listens only while it says **Listening…**, about 5 seconds, and keeps nothing.

### Windows

Download `IdleViz-Setup.exe` from the [Releases page](https://github.com/Xauno/IdleViz/releases) and run it. Each version tag gets a release with the setup file. The setup file isn't signed, so Windows SmartScreen warns about a downloaded one: choose **More info**, then **Run anyway**.

The wizard has a welcome page, the license, the install folder and three options: **Start menu entry** (ticked), **Desktop shortcut** (not ticked) and **Run at startup** (ticked). It installs for your user only, in `%LOCALAPPDATA%\Programs\IdleViz` unless you pick another folder, so there is no admin prompt. Running a newer setup file updates the installed copy in its folder, without asking for the folder again, and **Run at startup** then starts as the switch in IdleViz's settings last left it. Uninstall from **Settings → Apps → Installed apps**.

To build it yourself, in PowerShell, from the repo:

```powershell
cd windows
.\build-installer.ps1 -Install
```

This builds the app and `windows\artifacts\IdleViz-Setup.exe`, runs it without questions (so with the options as ticked above, or as they were at the last install), and starts IdleViz. Run it again after pulling changes to update. Without `-Install` it only builds the setup file, which you can run yourself to get the wizard.

## Usage

### On both

- **Open:** press the hotkey (⌃⌥V on the Mac, Ctrl + Alt + V on Windows), open the URL `idleviz://open`, or click **Open now** in settings. Spotify has to be running with a track loaded, playing or paused. If it isn't, the icon flashes a few times and nothing opens.
- **Close:** move the mouse, click, scroll or press any key other than the like and skip keys and the media keys below. The window fades out quickly and focus returns to where you were. Input in the first 0.4 s after opening is ignored, so the hotkey itself doesn't close it. Keys you're still holding after that are ignored until you let go; pressing one again closes it.
- **Media keys:** play/pause, next, previous, mute and volume do their usual job and leave the visualizer open.
- **Like and skip:** while the visualizer is open, **L** adds the preset on screen to your favorites, or takes it off again, and a heart confirms it. **N** blends to the next preset in about half a second; it does nothing in **Single** mode. In settings, **Like key** and **Skip key** let you pick another key (A–Z, 0–9, the arrow keys or Space) or **Off**. With a modifier key held, they close the visualizer like any other key.
- **Idle:** after the **Start after idle** time with no input (5 minutes by default; 10, 15 or 30 minutes, or off), it opens by itself, with the same Spotify check. A refused or skipped idle open isn't tried again until you've used the computer.
- **Keep awake:** while the visualizer is showing, the display doesn't sleep and the computer doesn't lock by itself. After the **Keep screen awake** time (1 hour by default; 30 minutes, 2 hours or 4 hours also available), counted from when it opened, it fades out slowly and the system's own sleep and lock settings take over. It then stays closed until you use the computer again.
- **On battery:** on a computer with a battery, turn on **Different times on battery** in settings to get a second **Start after idle** and **Keep screen awake** time that apply while it's unplugged.
- **Brightness and overlay:** **Brightness** dims the visuals behind the overlay (50 to 100%), and **Show Spotify overlay** turns the now-playing layout off, leaving only the visualizer. Both apply at once, even while the visualizer is open.
- **Visualizer:** it moves to whatever Spotify is playing on this computer, and a new preset blends in every 30 seconds. With Spotify paused, or playing on another device, the visuals get silence and drift slowly.
- **Presets:** in settings, **Mode** is **Shuffle** or **Single**. Shuffle has **Shuffle from** (all, bundled, custom or favorites), the time per preset and **Blend time**. Single has one **Visualizer** picker, and that preset stays on screen. Besides the like key, presets can be rated after the visualizer has closed: **Last shown** names the preset that was just on screen, with a button to add it to your favorites and one to leave it out of shuffle. **Favorites** and **Blocklist** open a list where you can remove entries or search all presets to add more.
- **Your own presets:** in settings, **Import…** copies files or whole folders into the presets folder (`.json`, `.milk`, `.js`), a button next to it opens the folder, and **Reload** reads it again, though it also notices changes by itself. Custom presets appear in the **Visualizer** picker and the favorites and blocklist lists, and **Shuffle from** can be set to **Custom**. Anything that couldn't be loaded is listed under **Failed to load** with the reason and a **Reveal** button; fix or replace the file and it is tried again. Custom plugins and presets are code, so only import ones you trust.
- **Audio delay:** with Bluetooth or network speakers the picture runs ahead of the sound. In settings, drag **Audio delay** until they match, or play something out loud and press **Detect**. The app listens only while the button says **Listening…**, about 5 seconds, and keeps nothing. The row names the speakers the value is for, and it changes by itself when you switch devices. Detect can't work with headphones, since the microphone can't hear them; it then says so and keeps the old value.
- **Manual delay test:** **Start** next to **Manual delay test** pauses Spotify if it was playing. A beep plays every second through the current speakers or headphones, and the panel lights up one audio delay after each beep is sent. Drag the slider, or use the **−10 ms** and **+10 ms** buttons, until the panel lights up exactly when you hear the beep. Every fourth beep is higher and its flash is orange, so with a long delay you can still tell which flash belongs to which beep. **Done** saves the delay for that device and starts Spotify again. It needs no microphone.

### On the Mac

- **Menu bar:** click the waveform icon for a small popup with **Settings…** (⌘,) and the current open hotkey. A yellow icon means a permission is missing: the popup then has a row for each, and clicking one opens the welcome window (if macOS hasn't asked yet) or the System Settings page where it's switched on.
- **Open:** the URL is opened with `open idleviz://open` in Terminal. The visualizer covers the main display; other displays keep showing the desktop.
- **Close:** a trackpad gesture closes it too. Besides the media keys, the brightness and keyboard backlight keys leave it open. The other top-row keys (Mission Control, Spotlight, Dictation, Focus) still close it, and so do F1–F12 pressed as standard function keys and the fn key itself.
- **Like and skip:** the keys are matched by their position on a US keyboard, and they and the media keys only work while the visualizer has keyboard focus (if macOS refused to activate the app, they close it like any other key). After **L**, a filled heart appears next to the track title for a moment; pressing it again takes the preset off, and the heart is then an outline. With the overlay off, paused or hidden, the heart shows in the top-right corner instead.
- **Idle:** it doesn't open while the screen is locked or another app keeps the display awake (a video, a call, a presentation).
- **Settings:** two sections, **General** and **Visualizer**, with the preset controls under **Presets**. Close the window with its red button; ⌘Q quits the app while settings is focused.
- **Launch at login:** the switch in settings adds IdleViz to your login items. It works best from the copy in `/Applications`. macOS may ask you to allow it under System Settings → General → Login Items.
- **Now playing:** the overlay updates whenever Spotify's track or state changes, and re-syncs the progress bar every 5 s while the window is open. Music ads show an "Advertisement" label with the progress bar. The app also logs each change:

  ```bash
  log stream --predicate 'subsystem == "com.xauno.IdleViz" AND category == "spotify"'
  ```

- **Your own presets:** the presets folder is `~/Library/Application Support/IdleViz/Presets/`. **Open** shows it in Finder.
- **Rendering:** 60 fps, at most 2560 pixels wide, and only while the window is open.

### On Windows

- **Tray icon:** IdleViz appears in the notification area, possibly behind the **^** overflow arrow. Left-click it for a small flyout with **Settings** and the current open hotkey. Right-click it for a menu with **Settings**, **Open visualizer** and **Exit**. Exit is the only way to quit; closing the settings window leaves the app running.
- **Warnings:** if IdleViz can't capture Spotify's sound or can't read what Spotify is playing, the tray icon turns yellow and the flyout gets a row for the problem. Click the row for the error text and a button that opens the log folder. The check runs again whenever you open the flyout, when Spotify starts and when the visualizer opens, and the warning goes away by itself once it passes. Spotify not running, and silence because Spotify plays on another device or is muted, are not warnings.
- **Open:** the URL is opened with `start idleviz://open` in a terminal, and **Open visualizer** in the right-click menu opens it too. A podcast counts as a loaded track. When an open is refused, the tray icon flashes to a slashed waveform 3 times. The visualizer fades in over the whole main display, taskbar included.
- **Close:** the key or click that closes it is not passed on to the window you return to. It also closes when a display changes its scale.
- **Like, skip and block:** all three work even if another window has the keyboard, and the key is not passed on to that window. Holding a key down doesn't repeat it. The modifiers that make them close the visualizer are Ctrl, Alt, Shift and the Windows key. **B** is the **Block key**: it puts the preset on screen on the blocklist (and takes it off your favorites), and in **Shuffle** the visualizer skips to the next preset at once. In **Single** mode the preset stays, as the blocklist only counts in Shuffle.
- **Changing the keys:** under **Keys** in settings, click **Like key**, **Skip key** or **Block key**, then press the key you want. Almost any key works: letters, digits, F-keys, punctuation, the numpad, arrows, Space, Enter. Esc keeps the key it had, the **✕** button turns the key off, and a key one of the other two already uses is refused. Ctrl, Alt, Shift, the Windows key and the playback and volume keys can't be used.
- **Close on input:** under **Opening** in settings, and it works with one display too. Turned off, the visualizer stays until you press the hotkey again, choose **Open visualizer** again or the keep-awake limit is reached; the pointer stays visible, and the like, skip and block keys are off (their rows are greyed out and say so).
- **Visualizer title:** **Show visualizer title** under **Look** shows the name of the preset on screen in small type in the top left corner, on every display the visualizer covers. It is off unless you turn it on.
- **Finding a preset:** the **Visualizer** row of **Single** mode opens a list of every preset with a search box; picking one applies at once. The **Favorites** and **Blocklist** dialogs have the search box on both tabs, and every row has a **Preview** button (▷) that opens the visualizer on that preset. Close the preview like any open visualizer; your **Mode** and other preset settings are untouched. Like every open, a preview needs a Spotify track.
- **Idle:** it doesn't open while the PC is locked, used over Remote Desktop, showing something fullscreen or in presentation mode, or while any app other than Spotify is making sound (a video or a call in a normal window, say).
- **On battery:** a desktop PC on a UPS counts as having no battery. This part has only been checked with a pretend battery, as the test PC has no real one.
- **Displays:** in settings under **Displays**. **Main display** is the display the visualizer opens on: the one Windows calls primary, or one you pick. Turn on **Use more than one display** to cover others too; the rows for it are folded into that row, so click its arrow to see them. **Other displays** ticks which ones (all of them unless you change it). **Placement** is **Same on each display**, where every display runs its own copy of the visualizer with the same preset and the same sound, or **Extend across displays**, where one picture runs across all of them. Extended, the picture covers the rectangle that encloses the displays, so with displays of different sizes the parts no display covers aren't seen. **Show it on**, folded into **Show Spotify overlay** under **Look**, puts the track, cover and progress bar on the main display, on one display you pick, or on all of them. These three rows are greyed out while **Use more than one display** is off. To keep working on another display while it runs, turn off **Close on input** under **Opening**. Covering more than one display takes more GPU power and the visualizer may run less smoothly; the row shows a warning while the switch is on. A change made while the visualizer is open applies the next time it opens.
- **Settings:** one page in six sections: **Opening**, **Look**, **Presets**, **Keys**, **Audio sync** and **Displays**, with **Open now** above them. Rows that depend on another row are folded into it; click the arrow at its right to open it. The window is 440 wide and can be dragged taller. Click **Open hotkey**, then press a key together with Ctrl, Alt, Shift or the Windows key to change it. Esc cancels, and the **✕** button clears it. If another app already has the stored combination, the row says so.
- **Run at startup:** turn it on in settings and IdleViz starts in the tray when you sign in. If you switch IdleViz off in Windows' own list (Settings → Apps → Startup), the row shows Off with a link to that list; turn it back on there.
- **Presets:** the rows for **Mode** are folded into it, and the time per preset is **Time per preset**. If the set chosen in **Shuffle from** is empty, the row says all presets are used instead. **Last shown** has **Favorite** and **Block** buttons, and a preset is never on both lists.
- **Your own presets:** the presets folder is `%APPDATA%\IdleViz\Presets\`. **Library** under **Presets** shows how many presets there are and holds **Import…** (a name that's taken gets a number, as File Explorer does; nothing is replaced), **Open folder**, which shows the folder in File Explorer, and **Reload**. Anything that couldn't be loaded is counted on the **Library** row and listed inside it under **Failed to load**.
- **Stuck page:** if a preset or plugin freezes the page, the page is replaced within about 4 s and that preset is left out until IdleViz restarts, or, for your own files, until the file changes.
- **Audio delay:** under **Audio sync**, open **Audio delay** for the slider and **Detect**. **Microphone** chooses what Detect listens with: **Automatic** takes the PC's own microphone, else the default input. A Bluetooth microphone is refused, because using it would change the delay. If Windows doesn't let desktop apps use the microphone, the row links to the Windows setting.
- **Overlay:** what Spotify is playing is read from the Windows media controls. When Spotify stops having a track, the overlay goes 1.5 s later. Ads aren't told apart from songs on Windows; none has been seen yet, as the test PC has Spotify Premium. Silence also follows from Spotify being muted in the Windows volume mixer.
- **Log:** `%LOCALAPPDATA%\IdleViz\logs\idleviz.log` records each start, open and close, what closed it, why an open was refused, every change in what Spotify is playing, whether the page loaded, which preset is showing, and, at each close, how many frames the page drew and how much Spotify audio it got.

## Custom visualizers

You can add your own visuals without touching either app. The easiest way is a single `.js` file that draws into a canvas and receives Spotify's audio levels every frame. [`aurora.js`](IdleViz/web/visuals/aurora.js) is a complete working example, and the full guide is [docs/custom-visualizer.md](docs/custom-visualizer.md).

## How it works

One borderless window holds one web page. The page stacks the Butterchurn canvas, a dim layer and the Spotify-style overlay, and it is the same page in both apps: in a WKWebView on the Mac and in WebView2 on Windows. A native helper around it watches for idle time and hotkeys, reads now-playing info, captures Spotify's audio and sends the analysis to the page about 60 times a second. The helper is Swift on the Mac and C# with WinUI 3 on Windows.

The full design, including the decisions behind it, is in [project.md](project.md). The brief for the Windows version is [windows-port.html](windows-port.html), and what was built and decided there is in [docs/windows.md](docs/windows.md).

## Roadmap

Each step becomes one pull request, and these tables are updated as it merges.

### macOS

The steps are from [project.md](project.md).

| Step | What                                                                              | Status |
| ---- | --------------------------------------------------------------------------------- | ------ |
| 0    | Repo tooling: CI, tests, PR workflow, plugin guide, Aurora example plugin         | Done   |
| 1    | Open/close shell: Xcode project, menu-bar app, hotkey, fullscreen window, dismiss | Done   |
| 2    | Audio spike: prove Spotify audio can drive Butterchurn (findings only)            | Done   |
| 3    | Spotify now-playing: launch/quit tracking, track info, artwork, content type, ads | Done   |
| 4    | Open rules and menu-bar icon flash                                                | Done   |
| 5    | Overlay page matched to the Spotify TV app, all states                            | Done   |
| 6    | Idle trigger, with skip rules (locked screen, video or call playing)              | Done   |
| 7a   | Visualizer: Butterchurn with bundled presets                                      | Done   |
| 7b   | Visualizer: real Spotify audio through the process tap                            | Done   |
| 7c   | Visualizer: preset controls                                                       | Done   |
| 7d   | Visualizer: custom preset folder and sandboxed plugins                            | Done   |
| 7e   | Visualizer: audio delay, with a microphone-based detector                         | Done   |
| 8a   | Polish: keep-awake limit, battery times, fades                                    | Done   |
| 8b   | Polish: permission setup (welcome window, yellow icon, error rows)                | Done   |
| 8c   | Polish: brightness, overlay switch, launch at login                               | Done   |

### Windows

The steps are from [windows-port.html](windows-port.html) and [docs/windows.md](docs/windows.md).

| Step | What                                                                                                    | Status |
| ---- | ------------------------------------------------------------------------------------------------------- | ------ |
| W0   | Scaffold: solution, `IdleViz.Core` with its first tests, CI job, installer script, empty tray app       | Done   |
| W1   | Open/close shell: tray flyout and menu, hotkey, URL, black fullscreen window, dismiss                   | Done   |
| W2   | Spike: Spotify-only audio capture, the page in WebView2, what the media controls report (findings only) | Done   |
| W3   | Spotify now-playing from the Windows media controls, content type, artwork                              | Done   |
| W4   | Open rules and tray icon flash                                                                          | Done   |
| W5   | The page in the window: overlay, CSP, all overlay states                                                | Done   |
| W6   | Idle trigger, with skip rules                                                                           | Done   |
| W7a  | Visualizer: real Spotify audio, and recovery from a stuck page                                          | Done   |
| W7b  | Visualizer: preset controls                                                                             | Done   |
| W7c  | Visualizer: custom preset folder, sandboxed plugins, `.milk` conversion                                 | Done   |
| W7d  | Visualizer: audio delay, with Detect and the manual test                                                | Done   |
| W8a  | Polish: keep-awake limit, battery times, fades                                                          | Done   |
| W8b  | Polish: warnings (yellow icon, flyout rows)                                                             | Done   |
| W8c  | Polish: brightness, overlay switch, run at startup, like, skip and media keys                           | Done   |
| W9   | More than one display: main display, mirror or extend, overlay display, close on input                  | Done   |

### Planned

These are in the Windows app and still to come on the Mac. The brief is [docs/mac-todo.md](docs/mac-todo.md).

- **More than one display on the Mac.** The Windows app has it (step W9): a main display, further displays that either each run the same visualizer or share one extended picture, a choice of display for the now-playing overlay, and a switch for whether input closes it. The Mac app still uses the main display only. No step in `project.md` covers the Mac side yet; it's listed there under "Later".
- **The regrouped settings window on the Mac.** The Windows settings window has six sections with the rows that depend on another row folded into it. The Mac window still has its two sections.
- **The newer settings on the Mac.** Keys that are recorded by pressing them instead of picked from a list, a block key, search in every preset list, a preview from the Favorites and Blocklist lists, **Close on input** and **Show visualizer title**. The title is drawn by the shared page, so the Mac only needs the switch.

## Development

### The page

You need Node 22 or later. The JavaScript side (plugins, overlay page), which both apps share, is checked with:

```bash
npm install
npm run check
```

`npm run check` runs ESLint, Prettier, a TypeScript typecheck over the page's JavaScript, and the Vitest suite. The test suite loads every visualizer plugin against a fake WebGL2 context and checks it against the plugin guide: required exports, canvas sizing, no NaN values sent to the GPU, everything freed on dispose, and no forbidden APIs.

### macOS

The Swift side is split in two. `IdleVizCore` (`Package.swift`, `Sources/`, `tests/IdleVizCoreTests/`) holds logic that runs without a screen, such as the dismiss rules and parsing Spotify's replies. The app target in `IdleViz.xcodeproj` (`IdleViz/App/`) is a thin AppKit and SwiftUI layer on top.

```bash
swift build && swift test
swiftlint lint --strict
```

In Debug builds, the launch argument `-IdleVizNoDismiss YES` keeps the window open so it can be inspected; trigger it again to close. `-IdleVizShowSettings YES` opens the settings window at launch, `-IdleVizShowWelcome YES` the welcome window, `-IdleVizFakePermissions automation=notAsked,audio=denied` shows the yellow icon, error rows and welcome window in that state without touching the real permissions, `-IdleVizDetectDelay YES` runs Detect delay four seconds after launch, and `-IdleVizOpenAtLaunch YES` opens the visualizer three seconds after launch (unlike `open idleviz://open`, that can't end up in another copy of the app). The page is inspectable in Debug builds: with the window open, attach Safari's Web Inspector from **Develop → [your Mac] → IdleViz**. The scheme has this argument ready to tick. Each open logs whether macOS let the app activate, and each preset change is logged too:

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

The build treats warnings as errors. Details are in [docs/windows.md](docs/windows.md).

### CI and releases

CI runs all of this on every pull request, and builds the Mac app unsigned with `xcodebuild` on a `macos-26` runner. Pushing a version tag such as `v0.1.0` builds and tests the Windows setup file and publishes a GitHub release with it; the tag has to match the version in `windows/Directory.Build.props`.

## Project layout

```
.
├─ project.md              Design and build order
├─ LICENSE                 MIT license
├─ AGENTS.md               Instructions for AI coding agents (CLAUDE.md points to it)
├─ IdleViz/web/            The page both apps show: visualizer and overlay HTML, CSS and JS, the plugin frame and its runner, the converter page, Figtree font
├─ IdleViz/web/vendor/     Butterchurn, its preset packs and the Milkdrop converter, copied unchanged from npm
├─ IdleViz/web/visuals/    Bundled visualizer plugins (aurora.js, the example plugin)
├─ IdleViz.command         Builds the Mac app, installs it in /Applications and opens it
├─ Package.swift           IdleVizCore Swift package (testable logic)
├─ Sources/IdleVizCore/    Dismiss rules, the like and skip keys, open rules, idle timing and skip rules, keep-awake and battery times, fade times, permission states, brightness and overlay settings, page scheme and CSP, overlay payload, URL commands, activation stats, Spotify query parsing and tracking, audio analysis (bands, automatic gain, frame packing), page status checks, preset settings, the custom presets folder (scanning, import names, Milkdrop conversion checks), the audio delay (per-device setting, delay line, delay detection, the manual delay test's timing and beeps)
├─ IdleViz.xcodeproj       Mac app target: bundle, Info.plist, entitlements, signing
├─ IdleViz/App/            The Mac app: menu-bar app, settings window, welcome window and permission checks, triggers, fullscreen window, dismiss, keep awake, power source, Spotify info, Spotify audio tap, web view, presets folder and Milkdrop converter
├─ Config/                 Mac build settings; your signing team goes in Local.xcconfig
├─ windows/                The Windows app: IdleViz.Core (logic) and its tests, IdleViz.App (WinUI 3), the installer script
├─ aurora-demo.html        Test page that feeds a plugin audio from a file or microphone
├─ mockups.html            UI mockups for the Mac menu-bar popup and settings window
├─ windows-port.html       Brief for the Windows version: what to reuse, the Windows UI, every decision so far
├─ docs/                   Guides: the custom visualizer guide, the Windows notes, what the Mac app still needs
├─ tests/                  Vitest suite, fake WebGL helpers, and the Swift tests (IdleVizCoreTests)
└─ .github/                CI and release workflows, PR template, Dependabot
```

## Contributing

`main` is protected. All changes go through a pull request with passing CI. See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow.

## License

[MIT](LICENSE). Butterchurn, the bundled preset packs and the Milkdrop converter are MIT licensed too; their license files are in [IdleViz/web/vendor/](IdleViz/web/vendor/). Milkdrop presets and plugins that you import yourself keep their own licenses.

## Acknowledgments

- [Butterchurn](https://github.com/jberg/butterchurn), [butterchurn-presets](https://github.com/jberg/butterchurn-presets), [milkdrop-preset-converter](https://github.com/jberg/milkdrop-preset-converter) and [Milkdrop](https://www.geisswerks.com/milkdrop/), the visualizer engine and the presets it plays
- [KeyboardShortcuts](https://github.com/sindresorhus/KeyboardShortcuts) for the global hotkey on the Mac
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) for the tray icon, and the [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows) for the settings cards, on Windows
- [Figtree](https://github.com/erikdkennedy/figtree) for the overlay font
