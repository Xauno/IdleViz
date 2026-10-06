# To do on the Mac

The Windows app has one thing the Mac app doesn't have yet (part 3), and one the Mac has but that was never run on a real second display (part 1). All were built and checked on Windows only. This file is the brief for the session on a Mac that brings them over. Delete each part once its PR is merged, and the file when it is empty.

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
| **Close on input** under Opening | The Mac has the switch, under **Opening**. The like and skip rows don't grey out yet while it is off. |
