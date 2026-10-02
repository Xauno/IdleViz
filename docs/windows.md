# IdleViz for Windows

The Windows version of IdleViz: the same app as on the Mac, behaving the same way, looking like it belongs on Windows 11. This file records how it is built and what was decided. [windows-port.html](../windows-port.html) is the original brief (open it in a browser), and [project.md](../project.md) is the full Mac design that the brief refers to.

The page in `IdleViz/web/` is shared by both apps and is not forked. The native helper around it is rewritten in C#.

## Layout

```
windows/
├─ IdleViz.sln
├─ global.json               .NET SDK version, and the test runner dotnet test uses
├─ Directory.Build.props     Version, and the lint settings every project gets
├─ Directory.Packages.props  NuGet package versions, in one place
├─ .editorconfig             C# style rules
├─ IdleViz.Core/             Logic that needs no screen, ported from Sources/IdleVizCore
├─ IdleViz.Core.Tests/       xUnit tests, ported from tests/IdleVizCoreTests
├─ IdleViz.App/              The WinUI 3 app: tray icon, windows, system calls
├─ installer/IdleViz.iss     Inno Setup script
├─ tools/make-icon.ps1       Draws IdleViz.App/Assets/IdleViz.ico
└─ build-installer.ps1       Builds the app and the setup file
```

## Build and test

Needs the .NET 10 SDK, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the setup file. Run these in `windows/`, in PowerShell (not WSL, which can't build or start a WinUI app):

```powershell
dotnet format --verify-no-changes   # style check
dotnet build                        # the analyzers run here, and any warning fails the build
dotnet test
.\build-installer.ps1               # artifacts\IdleViz-Setup.exe
.\build-installer.ps1 -Install      # the same, then installs it and starts the app
```

- **App.** Unpackaged and self-contained, x64 only: the installed folder needs nothing else on the PC. Windows 11 only (minimum build 22000), with no fallbacks for Windows 10.
- **Publishing needs `EnableMsixTooling`**, although the app isn't an MSIX package. Without it `dotnet publish` leaves out the compiled XAML (`IdleViz.pri`), and the installed app starts but crashes as soon as it opens a window, while the Debug build works. `build-installer.ps1` checks the file is there.
- **Tests.** xUnit v3 on Microsoft.Testing.Platform, which is what `dotnet test` needs on the .NET 10 SDK.
- **Lint.** `dotnet format` checks the `.editorconfig` style. The built-in .NET analyzers (`latest-recommended`) and the code-style rules run in every build with warnings as errors.
- **Installer.** Per-user, so no admin prompt: files go to `%LOCALAPPDATA%\Programs\IdleViz`, with a Start menu entry, an uninstaller under Installed apps, and the `idleviz://` protocol registered for the user. Installing over a running copy stops it first. Unsigned, so a downloaded setup file would get a SmartScreen warning; one built on the same PC doesn't.
- **CI.** The `windows` job on `windows-latest` runs the format check, build, tests and the installer build.
- **Line endings.** `.gitattributes` makes every checkout LF. With CRLF, Prettier fails and the vendored libraries no longer match their checksums.

## Third-party packages

| Package                                          | Used for                                            |
| ------------------------------------------------ | --------------------------------------------------- |
| H.NotifyIcon.WinUI                               | The tray icon and its clicks                        |
| CommunityToolkit.WinUI.Controls.SettingsControls | The settings cards in the settings window           |
| Microsoft.Windows.CsWin32                        | Generates the Win32 calls (build-time only)         |
| xunit.v3                                         | Tests                                               |

## Decisions

Answers the owner gave to the open points in section 9 of the brief, on 2 October 2026.

| Point | Decision |
| ----- | -------- |
| J1 | Code lives in `windows/` in this repository and loads `IdleViz/web/` as it is. xUnit for tests. Lint is `dotnet format` plus the built-in analyzers, warnings as errors. |
| Packages | A few well-known third-party packages are fine (H.NotifyIcon.WinUI, NAudio, CsWin32). Each new one is named in its pull request and in the table above. |
| J2 | Settings window as suggested: about 440 × 680, title "IdleViz Settings", "PC" for "Mac", "Run at startup", the Mac's sheets as `ContentDialog`s, the trust warning as an `InfoBar`. |
| J3 | Low-level keyboard and mouse hooks while the visualizer is open. Like and skip work without focus. The key or click that closes the visualizer is swallowed, so it never reaches the app behind. |
| J4 | Two warnings: "Spotify audio can't be captured" and "Can't read what Spotify is playing". Clicking one opens a small dialog with the error text and a button that opens the log folder. |
| J5 | Inno Setup, per-user install in `%LOCALAPPDATA%\Programs\IdleViz`, built by `windows/build-installer.ps1`. Presets folder `%APPDATA%\IdleViz\Presets\`. A taken import name is numbered as in File Explorer: "Tunnel (2).milk". |
| J6 | Default open hotkey Ctrl + Alt + V. |
| R3 | The idle trigger is skipped while the session is locked, an app is fullscreen or presenting, or any app other than Spotify is producing sound. A silent video in a normal window is still missed. |
| Microphone | Detect delay uses the built-in microphone if there is one, otherwise the default input, unless that is Bluetooth: then it doesn't run and the hint says why. Settings also gets a microphone picker, a row the Mac app doesn't have. |
| Battery | Battery times are built and unit-tested. The development PC has no battery, so they have not been run on one. |
| App icon | The tray's five-bar waveform, white on a dark rounded square. |
| Docs | The README has a Windows section with its own roadmap. This file holds the as-built design and the spike findings. The brief stays as it was written. |

The W2 spike answered R1, R4 and R5, and gave the facts for R2. R6 could not be measured. See the next section.

After the spike the owner approved one change to the shared page: it also accepts `https://app.idleviz.invalid/visuals/…` and `https://presets.idleviz.invalid/…` for plugin and preset URLs (R5 below).

## Spike findings (W2)

A throwaway build on the branch `spike/windows-w2`, which is not merged. It added a `--spike` mode to the app (`windows/IdleViz.App/Spike/`) and a small side test (`windows/Spike.Scheme/`). Measured on 2 October 2026 on one PC: Windows 11 build 26200, Spotify 1.301 from the Microsoft Store (Premium), WebView2 runtime 124.0.2478.51, an RTX 3080, primary display 3440 × 1440 at 120 Hz.

### Answers

| Point | Answer |
| ----- | ------ |
| R1: position and duration | **Yes.** Spotify reports both, and raises a timeline change about every 4.5 s while playing, and on every seek, pause and resume. |
| R2: podcast or ad | **A podcast has an empty artist**, as on the Mac. Its album is the show's name and the track number is 0. Windows calls it "Music" like a song. **Ads were not seen** (Premium), so nothing is known about them. The rule is for W3 to settle with the owner. |
| R4: fading with WebView2 | **Window opacity works.** With the page running in WebView2 inside the layered window, setting the window to half opacity showed the desktop through the page. Checked on a screenshot at one fixed opacity; nobody has watched a moving fade. |
| R5: scheme, frame, CSP | **The host page works from `idleviz-app://` unchanged; the sandboxed plugin frame does not.** Details below. Fixed by serving the page from an `https:` address the app answers itself, plus the one approved page change. |
| R6: frame cap above 120 Hz | **Not measured**: there is no display faster than 120 Hz here. On 120 Hz the page renders 60 fps. By the page's rule (skip a frame closer than 12 ms to the last) a 144 Hz display would give 72 fps, 165 Hz 82.5 and 240 Hz 80. |

### Spotify-only audio

- **Process loopback works on the Store version of Spotify.** `ActivateAudioInterfaceAsync` on `VAD\Process_Loopback`, in "include the process tree" mode, aimed at the Spotify process that owns the main window. Spotify runs as seven processes; the one with a window is their parent.
- It needs no package. The interop is about 100 lines: the activation parameters go in as a `VT_BLOB` `PROPVARIANT`, and the completion handler is an ordinary C# class.
- The capture format has to be given: `GetMixFormat` and `GetStreamLatency` return "not implemented". 32-bit float, 48 kHz, stereo was accepted, with the flags for loopback, event callback and automatic conversion.
- Packets are 480 frames (10 ms), 100 a second. The first arrived 15 to 30 ms after the start. No permission prompt and no recording indicator appeared.
- **It hears only Spotify.** With Spotify paused, a 440 Hz tone played by another program read as exact zero.
- **While Spotify is paused, packets keep coming, filled with zeros.** So "no packets" is not the sign of a pause; the level is.
- **Volume.** The capture is taken before the system volume (5 % and 52 % read the same) but after Spotify's volume in the Windows mixer (10 % read ten times lower) and after Spotify's own volume slider (raising it doubled the level). At the owner's usual slider position the level was about −42 dBFS, so the automatic gain has work to do.
- **Spotify Connect:** with playback moved to another device the capture is silent, as expected, while the media controls keep reporting (below).

### The page in WebView2

- WebView2 can be created straight on the visualizer's plain window handle (`CoreWebView2ControllerWindowReference.CreateFromWindowHandle`), with no WinUI window. The environment took about 20 ms, the controller about 200 ms, and the page was loaded 700 to 800 ms after the start.
- **It does not take focus**: the app in front stayed in front. The pointer was hidden over it, both at rest and after a move.
- **Frames.** `ExecuteScriptAsync("window.audioFrame?.(\"…\")")` 60 times a second: all 60 arrived, none were dropped, and a call took 0.6 to 0.9 ms from send to completion. Building a frame (with a rough analysis) took 0.25 ms.
- **Butterchurn held 59 to 60 fps** at 3440 × 1440 over 30 seconds.
- **Cost:** the app used about 6 % of one core, the WebView2 processes together about 25 % of one core (renderer about 17 %, GPU process about 8 %).
- From `idleviz-app://app/index.html` the unchanged page was a secure context with origin `idleviz-app://app`, loaded its ES modules, and `new Function` worked under the CSP sent as a response header.
- The page logs one warning, "The AudioContext was not allowed to start" (`visualizer.js`, line 48). Butterchurn renders all the same.

### R5 in detail: the plugin frame

The plugin frame is sandboxed without `allow-same-origin`, so its origin is opaque. WebView2 refuses every request from an opaque origin to a custom scheme: the frame's `plugin-runner.js` failed with a CORS error and the request never reached the app. This held with the scheme's allowed origins empty, `*`, `null` and `idleviz-app://*`. Microsoft's documentation says the same.

Served from `https://app.idleviz.invalid/` instead, the frame loaded its runner and the bundled Aurora plugin, with the sandbox attribute unchanged. The app answers these addresses in `WebResourceRequested` before anything goes to the network, and `.invalid` is a reserved name that never resolves. This needs no custom scheme at all.

So the Windows app will serve:

| Mac | Windows |
| --- | ------- |
| `idleviz-app://app/…` | `https://app.idleviz.invalid/…` |
| `idleviz-app://presets/…` | `https://presets.idleviz.invalid/…` |

The CSP is sent by each app, so Windows sends its own with these hosts in place of `idleviz-app:`. The only place where the page itself names the scheme is the list of URL prefixes it accepts for plugins and presets, and that list now holds both forms (the approved page change). The spike showed this working with the plain .NET WebView2 API; the WinUI flavour used by the app has not served an `https:` address yet.

### What Spotify reports

Through `GlobalSystemMediaTransportControlsSessionManager`. The Store version's session is `SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`. The version from spotify.com identifies itself differently and was not tested.

| Case | What was reported |
| ---- | ----------------- |
| Song, playing | Title, artist, album, album artist, track number. Playback type "Music". Status Playing. Position, and the duration as the timeline's end. Shuffle and repeat state. |
| Artwork | A PNG thumbnail, 50 to 120 KB. |
| Song, paused | Status Paused. The position stops. Timeline changes keep arriving with the same position. |
| Seek | A timeline change at once with the new position. |
| Track change | For about half a second: an empty title, no thumbnail and a duration of 0. Then the new track. This is the "no track for a moment" the page's 1.5 s wait is for. Each change arrives two or three times. |
| Podcast | Title of the episode, **artist empty**, album is the show's name, track number 0, playback type still "Music", a thumbnail, position and duration. |
| Spotify Connect (playing on another device) | The same as a song playing here: status Playing, position moving, seeks and pauses reported. Nothing says the sound is elsewhere. |
| Local file | Not tested. |
| Ad | Not seen (Premium). |

There is no track ID or Spotify URL, so a track change has to be recognised from title, artist and album.

### Things the spike ran into

- **The WinUI flavour of WebView2 ignores `Add` on its lists.** `options.CustomSchemeRegistrations.Add(…)` and `scheme.AllowedOrigins.Add(…)` do nothing, with no error: the getters hand out copies. Assigning a whole list to `CustomSchemeRegistrations` works. `AllowedOrigins` cannot be set at all.
- **A `DispatcherQueueTimer` kept only in a local variable is collected and stops** after a few seconds. Timers must live in fields. The `--open-at-launch` debug switch has this fault.
- **The WebView2 runtime on this PC is old** (124, from April 2024) although Windows 11 is current. The app must not assume a recent one.
- **A request handler that throws** makes the navigation fail with "connection aborted" and no other sign.

## As built

### W0: scaffold

- `IdleViz.Core` starts with `Fade.cs` (`CloseReason`, the fade times) and `Trigger.cs` (`TriggerSource`, the `idleviz://` commands), ported from the Swift files of the same names with their tests. The URL parser takes text, not a URL object, because Windows hands the protocol URL over as a command-line argument that may be anything.
- The app is a tray icon and nothing else: tooltip "IdleViz", and a right-click menu with **Exit**. It has no window, flyout, hotkey or visualizer yet.
- The right-click menu is the native Windows popup menu. The library's XAML menu needs a window to live in, and the app has none.
- The setup file is about 62 MB and installs about 234 MB, because the .NET runtime and the Windows App SDK are inside it.

### W1: open/close shell

The app opens a black fullscreen window from the hotkey, the URL, the tray menu or **Open now**, and closes it on any input. There are no open rules yet (it opens whether or not Spotify has a track), no page in the window and no idle trigger.

**Files**

| File | Job |
| ---- | --- |
| `Program.cs` | Entry point. One running copy: a second launch hands its command line to the first and exits. |
| `App.xaml.cs` | Wires everything together at launch. |
| `NativeWindow.cs` | A plain Win32 window, the base of the next two. |
| `HotkeyWindow.cs` | A window that is never shown. Receives the global hotkey and the taskbar's light/dark change. |
| `VisualizerWindow.cs`, `VisualizerController.cs` | The fullscreen window, and its open/close state, fades, focus and cursor. |
| `DismissWatcher.cs` | Keyboard and mouse hooks while open, and the backup check. |
| `TrayIcon.cs`, `TrayGlyph.cs`, `TrayMenu.cs`, `FlyoutWindow.xaml` | The tray icon, its drawing, the right-click menu and the left-click flyout. |
| `SettingsWindow.xaml`, `HotkeyRecorder.xaml` | The settings window and the hotkey recorder. |
| `Log.cs` | The log file. |

In `IdleViz.Core`: `DismissTracker.cs` (ported with its tests), `KeyboardInput.cs`, `Hotkey.cs`, `SettingsStore.cs` and `LaunchOptions.cs`.

**The window**

- A plain Win32 window, not a WinUI one: borderless, topmost, covering the primary display's full bounds, taskbar included. It is created at launch and hidden between opens.
- It is a layered window, so the fade is the window's own opacity and the desktop shows through, as on the Mac. In over 0.6 s, out over 0.25 s, eased. Whether this still works once the window holds WebView2 is the R4 question for the W2 spike.
- When a fade-out starts the PC is handed back at once: the hooks are removed, the cursor returns, clicks pass through the window, and focus goes back to the window that had it. The window is hidden when the fade ends. A trigger during a fade-out finishes the close and opens again.
- On open it asks for keyboard focus and logs whether Windows gave it. Nothing depends on it, since the hooks see input either way.
- The cursor is hidden over the window by answering `WM_SETCURSOR`. Windows only sends that when the mouse moves over the window, and a move closes the visualizer, so a pointer at rest kept the arrow in about one open in four. While open, the window therefore checks every 50 ms whether a cursor is showing over it and, if so, sets the pointer to where it already is, which makes Windows send the message without moving anything. A pointer on another display stays visible.
- A manual trigger while it is open does nothing.

**Dismiss**

- `WH_KEYBOARD_LL` and `WH_MOUSE_LL` hooks, installed when the window opens and removed when it starts to close. They need no permission.
- The input that closes the visualizer is swallowed: the key press, click or scroll never reaches the app behind. A mouse move is let through, so the pointer doesn't stick. The release of a swallowed key or button does reach the app behind, on its own, which does nothing.
- Grace period 0.4 s. Keys still held when it ends are stuck, as on the Mac: their release and repeat are ignored, and a fresh press closes.
- The hook doesn't mark key repeats, so `KeyboardInput` remembers which keys are down.
- Key codes are Windows virtual-key codes. The hooks report the sided modifiers (left Ctrl is `0xA2`), so the key-state scan skips the unsided ones (`0x10` to `0x12`), which would otherwise look like extra held keys.
- Mute, volume down and up, next, previous and play/pause don't close it and still do their job. Media stop and every other key close it. Brightness and keyboard-backlight keys never arrive as keys on Windows, so there is nothing to handle. Like and skip come in W8c.
- Backup check every 100 ms against `GetLastInputInfo`, with the 30 ms second look, for input the hooks missed: Windows silently drops a hook that answers too slowly.

**Tray**

- The glyph is drawn at run time, white on a dark taskbar and black on a light one (`SystemUsesLightTheme`), and redrawn when that changes.
- The flyout is a small WinUI window with the acrylic backdrop, placed in the corner of the primary display's work area. It closes on Esc or when it loses focus.
- The right-click menu is a WinUI `MenuFlyout`, so it looks like a Windows 11 menu. It needs a window to belong to, so `TrayMenu` makes an invisible one at the pointer for as long as the menu is open. H.NotifyIcon has the same idea built in (`SecondWindow`), but it creates that window at launch, and it took keyboard focus from the app in use every time IdleViz started. Its native mode (`PopupMenu`) doesn't, but draws an old-style menu.
- The app sets `DispatcherShutdownMode` to explicit: by default a WinUI app ends when its last window closes, which here would be the flyout.

**Settings window**

- 440 × 680, fixed, Mica, Maximize disabled, with **Open hotkey** and **Open now** so far.
- The recorder lets go of the current hotkey while it listens, and registers it again on Esc or when it loses focus.
- A combination another app has registered can't be recorded at all: Windows gives the keys to that app and the recorder never sees them. The "Another app is already using this shortcut" hint under the row therefore shows when the stored hotkey is refused, which is at launch.
- The settings cards stack their control under the label below a width that is wider than this window, so the window lowers that width.

**Settings file:** `%LOCALAPPDATA%\IdleViz\settings.json`. The hotkey is two numbers, `openHotkeyKey` (a virtual-key code, 0 for cleared) and `openHotkeyModifiers` (Alt 1, Ctrl 2, Shift 4, Win 8). Nothing stored means Ctrl + Alt + V.

**Log:** `%LOCALAPPDATA%\IdleViz\logs\idleviz.log`, with every start, open (trigger, and whether focus was given), close reason, and what closed it ("key 0x41", "mouse move (4, 0)"). At 1 MB it is renamed to `idleviz.old.log`.

**Debug switches** (Debug builds only; they can also be passed to a second launch, which hands them to the running copy):

| Switch | Effect |
| ------ | ------ |
| `--no-dismiss` | Input doesn't close the visualizer. Trigger it again to close. |
| `--open-at-launch` | Opens the visualizer three seconds after launch. |
| `--show-settings` | Opens the settings window. |
| `--show-flyout` | Opens the tray flyout. |
| `--show-menu` | Opens the tray menu at the pointer. |

**A second launch** with no URL (clicking the Start menu entry while it runs) opens the settings window, since there is no other window to bring forward. This was not asked; it is the usual behaviour of a Windows tray app.

**The Debug copy and the installed copy are separate apps to Windows**: each keeps its own single running copy, and `idleviz://` always starts the installed one. With both running, a URL open goes to the installed copy and the second copy to start can't register the hotkey. Stop the installed copy before testing a Debug build, and test URL opens on an installed build.
