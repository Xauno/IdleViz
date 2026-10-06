# To do on the Mac

The Windows app has two things the Mac app doesn't have yet, and one the Mac has but that was never run on a real second display. All were built and checked on Windows only. This file is the brief for the session on a Mac that brings them over. Delete each part once its PR is merged, and the file when it is empty.

Before any of them: [project.md](../project.md) is the Mac plan, [mockups.html](../mockups.html) the Mac UI reference, and [AGENTS.md](../AGENTS.md) the rules (ask before any judgement call; one PR per step, with tests; update the README).

Where the Windows version made a choice "without asking the owner", it is listed in [windows.md](windows.md) under that heading. Don't carry those over silently: ask the owner whether the Mac should do the same.

## 1. More than one display: test it with a second display

Built and merged, but only ever run with one display connected, using the Debug switch `-IdleVizSplitDisplay YES` (the two halves of the main display stand in for two displays). What it is and how it differs from Windows is in [project.md](../project.md) under "Brought over from the Windows app". Delete this part once these have been seen on a real second display:

- **Same on each display:** both displays show the visualizer on the same preset, each with its own picture. They open, fade and close together.
- **The names** in **Main display**, **Other displays** and **Show it on** are the ones macOS shows, and a choice survives unplugging the display and plugging it back in, also into another port.
- **Main display** set to the second display: the visualizer opens there, and the overlay follows it.
- **Other displays** with the second display unticked: only the main one is covered.
- **Show it on** set to the main display, the other one, and **All displays**. With the like key, the heart shows on every display that has the overlay.
- **Extend across displays** with "Displays have separate Spaces" on (the default): the warning shows in settings and each display runs its own visualizer.
- **Extend across displays** with it off (System Settings → Desktop & Dock, then log out and in): one picture across both displays, the overlay at each display's own bottom edge, and the main display's part as sharp as when it is covered alone. This is the case most likely to need a fix: nothing has shown yet that one borderless window really covers two displays.
- **Displays at different scales** (the built-in Retina display with a 1× monitor), mirrored and extended.
- **Over a fullscreen app** on the second display: the visualizer covers it. Then switch a display on in settings and try again without restarting the app.
- **Close on input off** with the visualizer on one display: keep typing in an app on the other display, then click on the visualizer. The hotkey closes it.
- **Unplug the second display** while the visualizer is open, and put the Mac to sleep: every window closes.
- **Frame rate** with two displays covered, in `log stream --level debug --predicate 'subsystem == "com.xauno.IdleViz" AND category == "page"'`.

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
| **Close on input** under Opening | The Mac has the switch, under **General** until the window is regrouped. The like and skip rows don't grey out yet while it is off. |
