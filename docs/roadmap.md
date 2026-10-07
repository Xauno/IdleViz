# Roadmap

Each step was one pull request. Features added to both apps after these steps, such as the update notice, are not in the tables.

## macOS

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

## Windows

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

## Planned

Nothing new is planned. What is left is to check more than one display on the Mac with a real second display; the list is in [docs/mac-todo.md](mac-todo.md).
