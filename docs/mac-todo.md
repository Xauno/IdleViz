# To do on the Mac

Everything the Windows app has is in the Mac app now. One thing is left: more than one display was built with only one display connected, so it has never run on a real second one. Delete this file once the checks below are done.

Also never tried by hand, since the checks used no keys or pointer: recording a key under **Keys** (Esc, the ✕ button, a key another row uses), the block key in the open visualizer, the **Preview** buttons and the search boxes in the preset sheets, and the folded rows of the settings window (that a switch in a row's label toggles without folding the row, and that turning it on opens the row).

## More than one display: test it with a second display

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
