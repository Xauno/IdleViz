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

Still open, to be answered by the W2 spike: whether Spotify reports position and duration to the Windows media controls (R1), how to tell a podcast or an ad (R2), fading a window that holds WebView2 (R4), the custom scheme, sandboxed frame and CSP in WebView2 (R5), and the frame cap on displays faster than 120 Hz (R6).

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
- The cursor is hidden over the window by answering `WM_SETCURSOR`. A pointer on another display stays visible.
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
