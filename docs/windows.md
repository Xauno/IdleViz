# IdleViz for Windows

The Windows version of IdleViz: the same app as on the Mac, behaving the same way, looking like it belongs on Windows 11. This file records how it is built and what was decided. [project.md](../project.md) is the full Mac design that it follows. The step-by-step build notes and the findings of the first spike were removed once the app was finished; they are in the git history of this file.

The page in `web/` is shared by both apps and is not forked. The native helper around it is rewritten in C#.

## Layout

```
windows/
├─ IdleViz.sln
├─ global.json               .NET SDK version, and the test runner dotnet test uses
├─ Directory.Build.props     Version, and the lint settings every project gets
├─ Directory.Packages.props  NuGet package versions, in one place
├─ .editorconfig             C# style rules
├─ IdleViz.Core/             Logic that needs no screen, ported from mac/Sources/IdleVizCore
├─ IdleViz.Core.Tests/       xUnit tests, ported from mac/Tests/IdleVizCoreTests
├─ IdleViz.App/              The WinUI 3 app: tray icon, windows, system calls
├─ installer/IdleViz.iss     Inno Setup script
├─ installer/test-installer.ps1  Installs, updates and uninstalls, and checks the result (CI)
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
- **Installer.** Per-user, so no admin prompt: files go to `%LOCALAPPDATA%\Programs\IdleViz` or a folder the user picks, with an uninstaller under Installed apps and the `idleviz://` protocol registered for the user. Installing over a running copy stops it first. Unsigned, so a downloaded setup file gets a SmartScreen warning; one built on the same PC doesn't.
- **Wizard.** Welcome, the MIT license, the folder, the options, then it installs; there is no separate "ready" page. The options are **Start menu entry** (ticked), **Desktop shortcut** (not ticked) and **Run at startup** (ticked). A silent install (`-Install`, or `/VERYSILENT`) takes the same defaults.
  - **Updates.** The folder page is left out and the update goes into the installed copy's folder, so there is never a second copy. The shortcut options start as they were at the last install, and unticking one removes its shortcut.
  - **Run at startup** writes the value the app's own switch writes (`StartupEntry.Command`). On an update the box starts as that value is now, not as it was ticked last time, because the switch in settings may have changed it since; this also holds for a silent update. An entry that starts another copy, such as a Debug build, counts as off and is left alone. `/TASKS=` or `/MERGETASKS=` on the command line wins over this.
  - **The old files are cleared** before the new ones are copied, but only in a folder that already holds `IdleViz.exe`, since the user may pick a folder with other things in it. That also clears setup's record of which folders it made, so the uninstaller is told to remove the install folder itself if it ends up empty; before this, uninstalling after an update left an empty folder.
- **CI.** The `windows` job on `windows-latest` runs the format check, build, tests and the installer build, then `installer/test-installer.ps1`: a first install into a folder of its own, updates with other options, and the uninstaller, checking the files, shortcuts and registry after each. The script refuses to run on a PC that has IdleViz installed, because it would replace and remove that copy.
- **Releases.** Pushing a tag like `v0.1.0` runs `.github/workflows/release.yml`: it builds and tests the setup file, builds and tests the Mac disk image in a second job, and a third job publishes a GitHub release with `IdleViz-Setup.exe`, `IdleViz.dmg` and generated notes. The tag has to match `Version` in `Directory.Build.props` and the Mac app's `MARKETING_VERSION`, or the workflow fails before building.
- **Line endings.** `.gitattributes` makes every checkout LF. With CRLF, Prettier fails and the vendored libraries no longer match their checksums.

## Third-party packages

| Package                                          | Used for                                            |
| ------------------------------------------------ | --------------------------------------------------- |
| H.NotifyIcon.WinUI                               | The tray icon and its clicks                        |
| CommunityToolkit.WinUI.Controls.SettingsControls | The settings cards in the settings window           |
| Microsoft.Windows.CsWin32                        | Generates the Win32 calls (build-time only)         |
| xunit.v3                                         | Tests                                               |

## Decisions

What the owner decided before and during the build, starting on 2 October 2026.

| Point | Decision |
| ----- | -------- |
| Repository | Code lives in `windows/` in this repository and loads `web/` as it is. xUnit for tests. Lint is `dotnet format` plus the built-in analyzers, warnings as errors. |
| Packages | A few well-known third-party packages are fine (H.NotifyIcon.WinUI, NAudio, CsWin32). Each new one is named in its pull request and in the table above. |
| Settings window | About 440 × 680, title "IdleViz Settings", "PC" for "Mac", "Run at startup", the Mac's sheets as `ContentDialog`s, the trust warning as an `InfoBar`. The layout was later regrouped into the same six sections as on the Mac. |
| Input | Low-level keyboard and mouse hooks while the visualizer is open. Like and skip work without focus. The key or click that closes the visualizer is swallowed, so it never reaches the app behind. |
| Warnings | Two warnings: "Spotify audio can't be captured" and "Can't read what Spotify is playing". Clicking one opens a small dialog with the error text and a button that opens the log folder. |
| Installer | Inno Setup, per-user install in `%LOCALAPPDATA%\Programs\IdleViz`, built by `windows/build-installer.ps1`. Presets folder `%APPDATA%\IdleViz\Presets\`. A taken import name is numbered as in File Explorer: "Tunnel (2).milk". |
| Hotkey | Default open hotkey Ctrl + Alt + V. |
| Idle skip rules | The idle trigger is skipped while the session is locked, an app is fullscreen or presenting, or any app other than Spotify is producing sound. A silent video in a normal window is still missed. |
| Microphone | Detect delay uses the built-in microphone if there is one, otherwise the default input, unless that is Bluetooth: then it doesn't run and the hint says why. Settings also gets a microphone picker, a row the Mac app doesn't have. |
| Battery | Battery times are built and unit-tested. The development PC has no battery, so they have not been run on one. |
| Ads | Not recognised on Windows. Windows doesn't say what an item is and no ad could be observed (Premium), so nothing unverified is built: no artist means podcast, anything else is a song. To be revisited if someone with a free account reports what an ad looks like. |
| Track gaps | A track seen in the last 1.5 s still counts as a track while Spotify is running, so an open during the brief "no track" between two items isn't refused. |
| First reading | A trigger that comes before Windows has said anything about Spotify (the app has just started) waits up to 2 s for the first reading, then decides. |
| App icon | The tray's five-bar waveform, white on a dark rounded square. |
| Docs | The README has a Windows section with its own roadmap. This file holds how the app is built and what was decided. |
| Updates | The app tells the user about a newer release in the flyout and installs nothing; see "Update notice" in [project.md](../project.md). The request is made with `HttpClient` and names the app as `IdleViz`, since GitHub refuses requests without a user agent. |
| Shared page | One change to the shared page was approved: it also accepts `https://app.idleviz.invalid/visuals/…` and `https://presets.idleviz.invalid/…` for plugin and preset URLs, because WebView2 refuses every request from the sandboxed plugin frame to a custom scheme. The Windows app answers those two addresses itself; `.invalid` never resolves. |
