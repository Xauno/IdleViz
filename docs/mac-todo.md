# To do on the Mac

The Windows app has three things the Mac app doesn't have yet. All were built and checked on Windows only. This file is the brief for the session on a Mac that brings them over. Delete each part once its PR is merged, and the file when it is empty.

Before any of them: [project.md](../project.md) is the Mac plan, [mockups.html](../mockups.html) the Mac UI reference, and [AGENTS.md](../AGENTS.md) the rules (ask before any judgement call; one PR per step, with tests; update the README). The delay line frame drop is fixed in code (PR 47, merged without a run on a Mac); the owner wants it tested on the Mac before anything else.

Where the Windows version made a choice "without asking the owner", it is listed in [windows.md](windows.md) under that heading. Don't carry those over silently: ask the owner whether the Mac should do the same.

## 1. More than one display

**What it is:** [windows.md, "W9: more than one display"](windows.md#w9-more-than-one-display). Read the whole section; it has the settings, the two placements, the overlay layout, what **Close on input** off means, and what the owner chose.

**Already shared, nothing to port:** the page. `IdleViz/web/` is used by both apps, so the overlay regions (`setOverlayRegions` in `overlay.js`, `overlay-state.js`, `overlay.css`, `index.html`) and the render width cap (`setRenderWidthCap` in `visualizer.js`, `visualizer-state.js`) are there. The Mac app never sends a layout today, so the page shows one region: the whole window.

**To port, C# to Swift:**

| Windows | What the Mac needs |
| ------- | ------------------ |
| `windows/IdleViz.Core/MultiDisplay.cs` and `MultiDisplayTests.cs` | The same in `Sources/IdleVizCore/` with its tests: `MultiDisplaySettings` (six stored values, same keys and defaults), `DisplayPlan.For(settings, displays)`, `DisplayLabel`. Displays are stored by the name Windows gives them; the Mac needs its own stable name for a display (ask the owner which: the display's UUID is the usual one). |
| `windows/IdleViz.Core/PresetSettings.cs`, `FollowScript` | The same in `PresetSettings.swift`: the preset controls held on one preset, for a page that follows the main page. |
| `windows/IdleViz.Core/DisplayLayout.cs` | The Mac equivalent from `NSScreen` frames. Mind that AppKit's y axis points up and the page wants rectangles from the top left, in device pixels. |
| `windows/IdleViz.App/VisualizerController.cs` | One window and page per mirrored display, made ahead and kept hidden, shown, faded and hidden together. |
| `windows/IdleViz.App/PageView.cs` (`SyncMirrors`, `SendLayout`) | The main page forwards what it is sent to its mirrors, asks for its preset every 250 ms and passes a change on, and sends each window its layout. |
| `windows/IdleViz.App/VisualizerWindow.cs`, `Show(PlannedWindow)` | A window on any display or across several, not always the main one. |
| `windows/IdleViz.App/Displays.cs` | The connected displays and their names for settings. |
| The Displays rows in `SettingsWindow.xaml` | The same rows in `IdleViz/App/SettingsWindow.swift`, laid out as in part 2. |

**Things that will differ on the Mac, so ask or check:**

- **Extend across displays.** On Windows one window covers the rectangle that encloses the displays. On a Mac, a window only spans displays when "Displays have separate Spaces" is off in System Settings; with it on (the default) a window shows on one display only. Find out what works and ask the owner how to handle the default case before building it.
- **Close on input off.** On Windows no hooks are installed, the window doesn't take focus and the pointer stays visible. Work out the Mac equivalent with the existing dismiss code (`DismissTracker`, the event monitors) and the activation rules.
- **The stuck page.** On Windows, replacing a stuck page ends every renderer, so the mirrors are remade too. WKWebView has a process per web view, so this may not apply; check before porting it.
- **Keep awake, sleep and display changes** already close the visualizer; check they close every window.

The README says the Mac app uses the main display only in its status note, in the "macOS and Windows compared" table and under "On the Mac" in Usage, and lists it under Planned. Change all four when this is done.

## 2. The settings window, regrouped

**What it is:** [windows.md, "Settings window regrouped"](windows.md#settings-window-regrouped). The Windows window went from four headings (General, Visualizer, Displays, Presets) and one flat list to six sections, with the rows that depend on another row folded into it. The owner asked for the Mac window to be regrouped the same way.

The Mac window (`IdleViz/App/SettingsWindow.swift`, `PresetControls.swift`, and the audio delay, launch at login and permissions rows around them) still has the old layout: **General** and **Visualizer**, with a Presets part.

**The layout to match** (folded rows in brackets):

| Section | Rows |
| ------- | ---- |
| (above the sections) | Open the visualizer, with the **Open now** button |
| Opening | Start after idle, Keep screen awake, Different times on battery (Start after idle, Keep screen awake), Open hotkey, Launch at login |
| Look | Brightness, Show Spotify overlay (Show it on) |
| Presets | Mode (Shuffle from, Time per preset and Blend time, or Visualizer), Last shown, Favorites, Blocklist, Library (the trust warning, Import presets, Presets folder, Reload presets, Failed to load) |
| Keys | Like key, Skip key |
| Audio sync | Audio delay (Set by hand, Detect with the microphone, Microphone if the Mac has that row, Manual delay test) |
| Displays | Main display, Use more than one display (the GPU warning, Other displays, Placement, Close on input) |

**Rules the owner agreed to:**

- One page, portrait, the same width as now; the height can be dragged.
- A row that depends on a switch is greyed out while the switch is off, not hidden. Turning the switch on opens its row. The battery row is still left out on a Mac with no battery, and **Mode** still swaps its rows between Shuffle and Single.
- **Open now** is not a setting, so it sits above the sections.
- Labels say what the row does and values carry their unit: "Time per preset" with "30 s" and "2 min" (`PresetSettings.SecondsLabel` on Windows; port it with its test), buttons that say **Favorite** and **Block**, "Presets folder" with **Open folder**, "Reload presets" with **Reload**, "Import presets" with **Import…**. The full list of renames is in the windows.md section.
- The audio delay's value is on the Audio delay row, and the slider inside runs the row's width.
- The failed count is on the Library row.

**For the Mac to decide with the owner:**

- What a folded row is in SwiftUI. `DisclosureGroup` inside the grouped `Form` is the obvious one; a switch or picker in its label has to stay clickable without folding the row.
- Mac wording where it already differs: "Launch at login", "Mac" for "PC", "Show in Finder" for the folder.
- Rows only the Mac has (the permission rows, anything else in the window today): which section each goes in.
- `mockups.html` shows the old layout. Ask whether to update it.
- Do part 1 first or leave the Displays section out until it exists.

Update the Mac parts of the README (Features, Usage) to the new section and row names when this is done.

## 3. Recorded keys, the block key, preset search and preview, the visualizer title

**What it is:** [windows.md, "Keys, preset search and preview, close on input, visualizer title"](windows.md#keys-preset-search-and-preview-close-on-input-visualizer-title). The owner asked for these on Windows; ask which of them the Mac should get before building any.

| Windows | What the Mac needs |
| ------- | ------------------ |
| `KeyRecorder.xaml`, `VisualizerKeys.CanBe` and `Label` | **Like key** and **Skip key** recorded by pressing a key instead of picked from a list: Esc keeps the old key, a button turns the key off, a key another row uses is refused. The Mac matches keys by position (key codes), so decide with the owner which keys can be recorded and how they are named. |
| `VisualizerAction.Block`, `blockKey`, `PresetController.Perform` | A **Block key**, default B: blocks the preset on screen, and in Shuffle skips to the next one first, so it leaves as fast as with the skip key. No page change is needed. |
| `PresetPickerDialog.xaml` | The **Visualizer** picker of Single mode as a list with a search box. |
| `PresetListDialog.xaml` | The search box on both tabs of the Favorites and Blocklist sheets, and a **Preview** button on every row. |
| `PresetSettings.PreviewScript`, `PresetController.Preview` and `EndPreview`, `App.PreviewPreset` | A preview opens the visualizer under the usual open rules, with the page held on one preset (Single mode, no blend) while the stored controls stay as they are; they are sent again when the visualizer starts to close or the open is refused. |
| `PresetTitleSetting`, `PageView.SendPresetTitleEnabled` | The **Show visualizer title** switch (key `showPresetTitle`, off by default). The page does the rest and is already shared: `setPresetTitleEnabled` in `overlay.js`, `idlevizPresetTitle` in `visualizer.js`, `.preset-title` in `overlay.css`. |
| **Close on input** under Opening | The Mac has no such switch at all yet; it comes with part 1's "Close on input off" question. On Windows it no longer depends on **Use more than one display**. |
