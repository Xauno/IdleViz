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
- **Tests.** xUnit v3 on Microsoft.Testing.Platform, which is what `dotnet test` needs on the .NET 10 SDK.
- **Lint.** `dotnet format` checks the `.editorconfig` style. The built-in .NET analyzers (`latest-recommended`) and the code-style rules run in every build with warnings as errors.
- **Installer.** Per-user, so no admin prompt: files go to `%LOCALAPPDATA%\Programs\IdleViz`, with a Start menu entry, an uninstaller under Installed apps, and the `idleviz://` protocol registered for the user. Installing over a running copy stops it first. Unsigned, so a downloaded setup file would get a SmartScreen warning; one built on the same PC doesn't.
- **CI.** The `windows` job on `windows-latest` runs the format check, build, tests and the installer build.
- **Line endings.** `.gitattributes` makes every checkout LF. With CRLF, Prettier fails and the vendored libraries no longer match their checksums.

## Third-party packages

| Package            | Used for                              |
| ------------------ | ------------------------------------- |
| H.NotifyIcon.WinUI | The tray icon and its right-click menu |
| xunit.v3           | Tests                                 |

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
