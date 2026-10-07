using IdleViz.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// The settings window: small, portrait, one page that scrolls, with the rows that hang off another
/// row folded into it. The width is fixed and the height can be dragged. Closing it only closes the
/// window; the app keeps running in the tray.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double WidthInPixels = 440;
    private const double HeightInPixels = 680;
    private const double MinHeightInPixels = 360;

    // Segoe Fluent Icons: Heart and HeartFill.
    private const int HeartGlyph = 0xE006;
    private const int FilledHeartGlyph = 0xE00B;

    private readonly App _app;

    // Set while the rows are being filled in from the settings, so the pickers' own events aren't taken as changes.
    // It starts set: a slider reports a change as soon as its range is, while the window is still being built.
    private bool _showing = true;

    // What the Failed to load list shows, so it is only rebuilt when that changes.
    private IReadOnlyList<PresetFailure> _shownFailures = [];

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        _showing = false;

        Title = "IdleViz Settings";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "IdleViz.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        var scale = PInvoke.GetDpiForWindow(Handle) / 96.0;
        var width = (int)Math.Round(WidthInPixels * scale);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Only the height can be dragged: the rows are laid out for this width.
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = presenter.PreferredMaximumWidth = width;
            presenter.PreferredMinimumHeight = (int)Math.Round(MinHeightInPixels * scale);
        }

        AppWindow.Resize(new SizeInt32(width, (int)Math.Round(HeightInPixels * scale)));

        ShowTimes();
        Recorder.Attach(_app);
        Recorder.HotkeyChanged += ShowHotkeyState;
        ShowHotkeyState();

        LikeKeyRecorder.KeyChanged += key => ChangeKey(VisualizerKeys.LikeKey, key);
        SkipKeyRecorder.KeyChanged += key => ChangeKey(VisualizerKeys.SkipKey, key);
        BlockKeyRecorder.KeyChanged += key => ChangeKey(VisualizerKeys.BlockKey, key);
        ShowKeys();
        ShowDisplayOptions();
        MultiDisplayWarning.Message = MultiDisplaySettings.GpuWarning;
        ShowDisplays();
        // Read again whenever the window comes forward: the entry can be switched off in Windows too,
        // and a display may have been plugged in.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                ShowStartup();
                ShowDisplays();
            }
        };
        ShowStartup();
        _app.Updates.Changed += ShowUpdates;
        ShowUpdates();

        FillPresetChoices();
        Presets.Changed += ShowPresets;
        ShowPresets();

        ShowMicrophones();
        AudioDelay.Changed += ShowDelay;
        ShowDelay();
        Closed += (_, _) =>
        {
            Presets.Changed -= ShowPresets;
            AudioDelay.Changed -= ShowDelay;
            _app.Updates.Changed -= ShowUpdates;
            // The window can be closed with the test's dialog still open.
            AudioDelay.StopTest();
        };
    }

    private PresetController Presets => _app.Presets;

    private AudioDelayController AudioDelay => _app.AudioDelay;

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>Shows the window, or brings it forward if it is already open.</summary>
    /// <summary>Shows a warning's details: what it means, the error text, and a way to the log.</summary>
    public async void ShowWarning(Warning warning)
    {
        // A window that was only just made has nothing to hang a dialog on until its content is loaded.
        if (Content is FrameworkElement { IsLoaded: false } content)
        {
            content.Loaded += (_, _) => ShowWarning(warning);
            return;
        }

        var text = new StackPanel { Spacing = 12 };
        text.Children.Add(new TextBlock { Text = warning.Explanation, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = warning.Details,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = warning.Title,
            Content = text,
            PrimaryButtonText = "Open log folder",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Log.OpenFolder();
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Another dialog is already open on this window.
            Log.Info("settings", $"Couldn't show the warning's details: {error.Message}");
        }
    }

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        PInvoke.SetForegroundWindow(Handle);
    }

    private void ShowHotkeyState()
    {
        if (_app.HotkeyRegistered)
        {
            HotkeyCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
        }
        else
        {
            HotkeyCard.Description = "Another app is already using this shortcut. Pick a different one.";
        }
    }

    /// <summary>Fills the idle, keep-awake and battery rows in from the settings.</summary>
    private void ShowTimes()
    {
        var times = TimingSettings.Read(_app.Settings);
        _showing = true;
        try
        {
            ShowMinutes(IdlePicker, IdleChoices, times.IdleMinutes, IdleLabel);
            ShowMinutes(KeepAwakePicker, KeepAwakeSetting.Choices, times.KeepAwakeMinutes, KeepAwakeSetting.Label);
            ShowMinutes(BatteryIdlePicker, IdleChoices, times.BatteryIdleMinutes, IdleLabel);
            ShowMinutes(BatteryKeepAwakePicker, KeepAwakeSetting.Choices, times.BatteryKeepAwakeMinutes, KeepAwakeSetting.Label);
            BatterySwitch.IsOn = times.UseBatteryTimes;
        }
        finally
        {
            _showing = false;
        }

        BatteryExpander.Visibility = _app.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        BatteryIdleCard.IsEnabled = BatteryKeepAwakeCard.IsEnabled = times.UseBatteryTimes;
    }

    // 5, 10, 15 and 30 min, then Off, as on the Mac.
    private static IReadOnlyList<int> IdleChoices { get; } = [.. IdleTimeoutSetting.Choices, 0];

    private static string IdleLabel(int minutes) => minutes > 0 ? $"{minutes} min" : "Off";

    // A stored value that isn't a choice gets its own entry.
    private static void ShowMinutes(ComboBox picker, IReadOnlyList<int> choices, int current, Func<int, string> label)
    {
        var minutes = choices.ToList();
        if (!minutes.Contains(current))
        {
            minutes.Insert(0, current);
        }

        if (!minutes.SequenceEqual(picker.Items.OfType<ComboBoxItem>().Select(item => (int)item.Tag)))
        {
            picker.Items.Clear();
            foreach (var value in minutes)
            {
                picker.Items.Add(new ComboBoxItem { Content = label(value), Tag = value });
            }
        }

        Select(picker, current);
    }

    private void ChangeMinutes(ComboBox picker, string key)
    {
        if (!_showing && picker.SelectedItem is ComboBoxItem { Tag: int minutes })
        {
            _app.Settings.SetInt(key, minutes);
            ShowTimes();
        }
    }

    private void OnIdleChanged(object sender, SelectionChangedEventArgs e) => ChangeMinutes(IdlePicker, IdleTimeoutSetting.Key);

    private void OnKeepAwakeChanged(object sender, SelectionChangedEventArgs e) => ChangeMinutes(KeepAwakePicker, KeepAwakeSetting.Key);

    private void OnBatteryIdleChanged(object sender, SelectionChangedEventArgs e) => ChangeMinutes(BatteryIdlePicker, BatteryTimesSetting.IdleTimeoutKey);

    private void OnBatteryKeepAwakeChanged(object sender, SelectionChangedEventArgs e) =>
        ChangeMinutes(BatteryKeepAwakePicker, BatteryTimesSetting.KeepAwakeKey);

    private void OnBatteryTimesToggled(object sender, RoutedEventArgs e)
    {
        if (_showing)
        {
            return;
        }

        var settings = _app.Settings;
        if (BatterySwitch.IsOn)
        {
            // The first time it is turned on, the battery rows start as copies of the rows above.
            var times = TimingSettings.Read(settings);
            if (settings.GetInt(BatteryTimesSetting.IdleTimeoutKey) is null)
            {
                settings.SetInt(BatteryTimesSetting.IdleTimeoutKey, times.IdleMinutes);
            }

            if (settings.GetInt(BatteryTimesSetting.KeepAwakeKey) is null)
            {
                settings.SetInt(BatteryTimesSetting.KeepAwakeKey, times.KeepAwakeMinutes);
            }
        }

        settings.SetBool(BatteryTimesSetting.EnabledKey, BatterySwitch.IsOn);
        ShowTimes();
        Unfold(BatteryExpander, BatterySwitch.IsOn);
    }

    // Each recorder refuses the keys the other two use. With Close on input off no key is watched
    // while the visualizer is open, so the rows are greyed out and say why.
    private void ShowKeys()
    {
        var keys = VisualizerKeys.Read(_app.Settings);
        var watched = MultiDisplaySettings.Read(_app.Settings).CloseOnInput;
        ShowKey(LikeKeyCard, LikeKeyRecorder, keys.Like, keys, watched, "Favorites what's on screen");
        ShowKey(SkipKeyCard, SkipKeyRecorder, keys.Skip, keys, watched, "Next visualizer, in Shuffle");
        ShowKey(BlockKeyCard, BlockKeyRecorder, keys.Block, keys, watched, "Blocks what's on screen and skips to the next");
    }

    private static void ShowKey(
        CommunityToolkit.WinUI.Controls.SettingsCard card, KeyRecorder recorder, ushort? key, VisualizerKeys keys, bool watched, string description)
    {
        recorder.Show(key, keys.Codes.Where(code => code != key));
        card.IsEnabled = watched;
        card.Description = watched ? description : "Needs Close on input";
    }

    private void ChangeKey(string setting, ushort? key)
    {
        _app.Settings.SetInt(setting, key ?? VisualizerKeys.Off);
        ShowKeys();
    }

    private void ShowStartup()
    {
        var state = RunAtStartup.State;
        _showing = true;
        try
        {
            StartupSwitch.IsOn = state == StartupState.On;
        }
        finally
        {
            _showing = false;
        }

        if (state == StartupState.DisabledInWindows)
        {
            var link = new HyperlinkButton { Content = "Turned off in Windows. Open Startup apps\u2026", Padding = new Thickness(0) };
            link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(StartupEntry.WindowsSettings);
            StartupCard.Description = link;
        }
        else
        {
            StartupCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
        }
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            RunAtStartup.Set(StartupSwitch.IsOn);
            ShowStartup();
        }
    }

    // The switch, what the last check found, and a button that checks at once.
    private void ShowUpdates()
    {
        var updates = _app.Updates;
        var installed = updates.Installed is { } version ? UpdateCheck.Label(version) : "unknown";
        _showing = true;
        try
        {
            UpdatesSwitch.IsOn = updates.Enabled;
        }
        finally
        {
            _showing = false;
        }

        CheckNowButton.IsEnabled = updates.Enabled && updates.Result != UpdateResult.Checking;
        UpdatesCard.Description = !updates.Enabled
            ? $"This is version {installed}"
            : updates.Result switch
            {
                UpdateResult.Checking => "Checking\u2026",
                UpdateResult.Failed => "Couldn't reach GitHub",
                UpdateResult.UpToDate => $"Version {installed} is the latest",
                _ => updates.Available is { } available
                    ? $"Version {UpdateCheck.Label(available)} is available"
                    : $"Once a day. This is version {installed}",
            };
    }

    private void OnUpdatesToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            // The checker follows the setting and reports back, which shows the row again.
            _app.Settings.SetBool(UpdateCheck.EnabledKey, UpdatesSwitch.IsOn);
        }
    }

    private void OnCheckNowClick(object sender, RoutedEventArgs e) => _app.Updates.CheckNow();

    private void ShowDisplayOptions()
    {
        var brightness = BrightnessSetting.Value(_app.Settings);
        _showing = true;
        try
        {
            OverlaySwitch.IsOn = OverlaySetting.Value(_app.Settings);
            PresetTitleSwitch.IsOn = PresetTitleSetting.Value(_app.Settings);
            BrightnessSlider.Value = brightness;
        }
        finally
        {
            _showing = false;
        }

        BrightnessLabel.Text = BrightnessSetting.Label(brightness);
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(OverlaySetting.Key, OverlaySwitch.IsOn);
            ShowDisplays();
        }
    }

    private void OnPresetTitleToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(PresetTitleSetting.Key, PresetTitleSwitch.IsOn);
        }
    }

    private void OnBrightnessChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_showing)
        {
            return;
        }

        var brightness = BrightnessSetting.Normalized(e.NewValue);
        _app.Settings.SetDouble(BrightnessSetting.Key, brightness);
        BrightnessLabel.Text = BrightnessSetting.Label(brightness);
    }

    private void OnOpenNowClick(object sender, RoutedEventArgs e) => _app.OpenVisualizer(TriggerSource.Settings);

    /// <summary>Fills the Displays rows in from the settings and the displays connected right now.</summary>
    private void ShowDisplays()
    {
        var settings = MultiDisplaySettings.Read(_app.Settings);
        var displays = Displays.Current();
        var connected = displays.Select(display => (display.Name, DisplayLabel.For(display))).ToList();

        // A stored display that is unplugged right now stays in the list, so the choice isn't lost.
        IEnumerable<(string, string)> Missing(string? name) =>
            name is null || displays.Any(display => display.Name == name) ? [] : [(name, $"{DisplayLabel.Brief(name)} (not connected)")];

        var overlayIsDisplay = settings.OverlayDisplay is not (MultiDisplaySettings.OverlayOnMain or MultiDisplaySettings.OverlayOnAll);
        _showing = true;
        try
        {
            Fill(MainDisplayPicker, [(string.Empty, "Windows primary"), .. connected, .. Missing(settings.MainDisplay)], settings.MainDisplay ?? string.Empty);
            MultiDisplaySwitch.IsOn = settings.Enabled;
            Fill(
                PlacementPicker,
                [
                    (MultiDisplaySettings.PlacementName(DisplayPlacement.Mirror), "Same on each display"),
                    (MultiDisplaySettings.PlacementName(DisplayPlacement.Extend), "Extend across displays"),
                ],
                MultiDisplaySettings.PlacementName(settings.Placement));
            CloseOnInputSwitch.IsOn = settings.CloseOnInput;
            Fill(
                OverlayDisplayPicker,
                [
                    (MultiDisplaySettings.OverlayOnMain, "Main display"),
                    .. connected,
                    .. Missing(overlayIsDisplay ? settings.OverlayDisplay : null),
                    (MultiDisplaySettings.OverlayOnAll, "All displays"),
                ],
                settings.OverlayDisplay);
        }
        finally
        {
            _showing = false;
        }

        // Every display but the main one, each with a tick.
        var others = displays.Count == 0 ? [] : displays.Where(display => display != settings.Main(displays)).ToList();
        OtherDisplaysMenu.Items.Clear();
        foreach (var display in others)
        {
            var item = new ToggleMenuFlyoutItem { Text = DisplayLabel.For(display), Tag = display.Name, IsChecked = settings.Covers(display) };
            item.Click += OnOtherDisplayClick;
            OtherDisplaysMenu.Items.Add(item);
        }

        var chosen = others.Count(settings.Covers);
        OtherDisplaysButton.Content = others.Count == 0 ? "None connected" : chosen == others.Count ? "All" : chosen == 0 ? "None" : $"{chosen} of {others.Count} displays";

        MultiDisplayWarning.IsOpen = settings.Enabled;
        PlacementCard.IsEnabled = settings.Enabled;
        OtherDisplaysCard.IsEnabled = settings.Enabled && others.Count > 0;

        // With one display covered the overlay has only one place to be.
        OverlayDisplayCard.IsEnabled = settings.Enabled && OverlaySwitch.IsOn;
        if (settings.Enabled)
        {
            OverlayDisplayCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
        }
        else
        {
            OverlayDisplayCard.Description = "Multi-display only";
        }
    }

    // A picker keeps its items unless they changed: emptying one from inside its own selection event takes the app down.
    private static void Fill(ComboBox picker, IReadOnlyList<(string Tag, string Label)> choices, string selected)
    {
        var shown = picker.Items.OfType<ComboBoxItem>().Select(item => ((string)item.Tag, (string)item.Content));
        if (!choices.SequenceEqual(shown))
        {
            picker.Items.Clear();
            foreach (var (tag, label) in choices)
            {
                picker.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
            }
        }

        Select(picker, selected);
    }

    private void OnMainDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || MainDisplayPicker.SelectedItem is not ComboBoxItem { Tag: string name })
        {
            return;
        }

        if (name.Length == 0)
        {
            _app.Settings.Remove(MultiDisplaySettings.MainDisplayKey);
        }
        else
        {
            _app.Settings.SetString(MultiDisplaySettings.MainDisplayKey, name);
        }

        ShowDisplays();
    }

    private void OnMultiDisplayToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(MultiDisplaySettings.EnabledKey, MultiDisplaySwitch.IsOn);
            ShowDisplays();
            Unfold(MultiDisplayExpander, MultiDisplaySwitch.IsOn);
        }
    }

    private void OnOtherDisplayClick(object sender, RoutedEventArgs e)
    {
        var items = OtherDisplaysMenu.Items.OfType<ToggleMenuFlyoutItem>().ToList();
        if (items.All(item => item.IsChecked))
        {
            // All of them, including a display that is plugged in later.
            _app.Settings.Remove(MultiDisplaySettings.OtherDisplaysKey);
        }
        else
        {
            _app.Settings.SetStringList(MultiDisplaySettings.OtherDisplaysKey, items.Where(item => item.IsChecked).Select(item => (string)item.Tag));
        }

        // Not from inside the menu's own click: the menu is rebuilt.
        DispatcherQueue.TryEnqueue(ShowDisplays);
    }

    private void OnPlacementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && PlacementPicker.SelectedItem is ComboBoxItem { Tag: string placement })
        {
            _app.Settings.SetString(MultiDisplaySettings.PlacementKey, placement);
        }
    }

    private void OnCloseOnInputToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(MultiDisplaySettings.CloseOnInputKey, CloseOnInputSwitch.IsOn);
            ShowKeys();
        }
    }

    private void OnOverlayDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && OverlayDisplayPicker.SelectedItem is ComboBoxItem { Tag: string display })
        {
            _app.Settings.SetString(MultiDisplaySettings.OverlayDisplayKey, display);
        }
    }

    /// <summary>Fills the audio delay rows in. Runs again on every change: the slider, Detect delay, the test, another device.</summary>
    private void ShowDelay()
    {
        var delay = AudioDelay;
        _showing = true;
        try
        {
            DelaySlider.Value = delay.Delay;
        }
        finally
        {
            _showing = false;
        }

        DelayLabel.Text = AudioDelaySetting.Label(delay.Delay);
        DelayExpander.Description = new TextBlock { Text = $"For {delay.DeviceName}", MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };

        DetectButton.Content = delay.Detecting ? "Listeningâ€¦" : "Detect";
        DetectButton.IsEnabled = !delay.Detecting && !delay.Testing;
        TestButton.IsEnabled = !delay.Detecting && !delay.Testing;
        if (delay.MicrophoneDenied)
        {
            var link = new HyperlinkButton { Content = "Microphone access is off. Open Settingsâ€¦", Padding = new Thickness(0) };
            link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(AudioDelayController.MicrophoneSettings);
            DetectCard.Description = link;
        }
        else
        {
            DetectCard.Description = new TextBlock { Text = delay.Hint ?? "Listens for a few seconds", TextWrapping = TextWrapping.Wrap };
        }
    }

    private void OnDelayChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_showing)
        {
            AudioDelay.Delay = e.NewValue;
        }
    }

    private void OnDetectClick(object sender, RoutedEventArgs e) => AudioDelay.Detect();

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualDelayDialog(AudioDelay) { XamlRoot = Content.XamlRoot };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Only one dialog can be open in a window at a time.
            Log.Info("settings", $"Can't show the manual delay test: {error.Message}");
        }
    }

    // "Automatic" (built-in, else the default input), then every microphone Windows lists right now.
    private void ShowMicrophones()
    {
        var picked = AudioDelay.PickedMicrophone;
        _showing = true;
        try
        {
            MicrophonePicker.Items.Clear();
            MicrophonePicker.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = string.Empty });
            foreach (var microphone in AudioDevices.Microphones())
            {
                MicrophonePicker.Items.Add(new ComboBoxItem { Content = microphone.Name, Tag = microphone.Id });
            }

            // A picked microphone that is unplugged right now shows as Automatic, which is what Detect delay then does.
            Select(MicrophonePicker, picked ?? string.Empty);
            if (MicrophonePicker.SelectedIndex < 0)
            {
                MicrophonePicker.SelectedIndex = 0;
            }
        }
        finally
        {
            _showing = false;
        }
    }

    // Microphones come and go, so the list is read again each time it is opened.
    private void OnMicrophonesOpened(object? sender, object e) => ShowMicrophones();

    private void OnMicrophoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && MicrophonePicker.SelectedItem is ComboBoxItem { Tag: string id })
        {
            AudioDelay.PickedMicrophone = id.Length > 0 ? id : null;
        }
    }

    private void FillPresetChoices()
    {
        ModePicker.Items.Add(new ComboBoxItem { Content = "Shuffle", Tag = PresetMode.Shuffle });
        ModePicker.Items.Add(new ComboBoxItem { Content = "Single", Tag = PresetMode.Single });
        foreach (var source in Enum.GetValues<ShuffleSource>())
        {
            ShuffleFromPicker.Items.Add(new ComboBoxItem { Content = source.ToString(), Tag = source });
        }

        foreach (var seconds in PresetSettings.SecondsChoices)
        {
            SecondsPicker.Items.Add(new ComboBoxItem { Content = PresetSettings.SecondsLabel(seconds), Tag = seconds });
        }

        foreach (var blend in PresetSettings.BlendChoices)
        {
            BlendPicker.Items.Add(new ComboBoxItem { Content = PresetSettings.BlendLabel(blend), Tag = blend });
        }
    }

    /// <summary>Fills the preset rows in from the settings. Runs again on every change, from here or elsewhere.</summary>
    private void ShowPresets()
    {
        var settings = Presets.Settings;
        _showing = true;
        try
        {
            Select(ModePicker, settings.Mode);
            var shuffle = settings.Mode == PresetMode.Shuffle;
            ShuffleFromCard.Visibility = SecondsCard.Visibility = BlendCard.Visibility = shuffle ? Visibility.Visible : Visibility.Collapsed;
            SingleCard.Visibility = shuffle ? Visibility.Collapsed : Visibility.Visible;

            Select(ShuffleFromPicker, settings.ShuffleFrom);
            if (settings.EmptySourceHint(Presets.HasCustomPresets) is { } hint)
            {
                ShuffleFromCard.Description = hint;
            }
            else
            {
                ShuffleFromCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
            }

            Select(SecondsPicker, settings.SecondsPerPreset);
            Select(BlendPicker, settings.BlendSeconds);
            SingleLabel.Text = Presets.Name(settings.SinglePreset);
            ToolTipService.SetToolTip(SingleButton, SingleLabel.Text);

            if (Presets.LastShown is { } id)
            {
                LastShownCard.Visibility = Visibility.Visible;
                LastShownCard.Description = Presets.Name(id);
                var favorite = settings.IsFavorite(id);
                FavoriteButton.IsChecked = favorite;
                FavoriteIcon.Glyph = char.ConvertFromUtf32(favorite ? FilledHeartGlyph : HeartGlyph);
                Label(FavoriteButton, favorite ? "Remove from favorites" : "Add to favorites");
                var blocked = settings.IsBlocked(id);
                BlockButton.IsChecked = blocked;
                Label(BlockButton, blocked ? "Remove from the blocklist" : "Add to the blocklist");
            }
            else
            {
                // Hidden until a preset has been on screen.
                LastShownCard.Visibility = Visibility.Collapsed;
            }

            FavoritesButton.Content = $"Manage ({settings.Favorites.Count})";
            BlocklistButton.Content = $"Manage ({settings.Blocked.Count})";

            var failed = Presets.Failures.Count;
            LibraryExpander.Description = $"{Presets.BundledCount} bundled, {Presets.Library.CustomCount} custom" + (failed > 0 ? $", {failed} failed to load" : string.Empty);
            ShowFailures(Presets.Failures);
        }
        finally
        {
            _showing = false;
        }
    }

    private void ShowFailures(IReadOnlyList<PresetFailure> failures)
    {
        if (failures.SequenceEqual(_shownFailures))
        {
            return;
        }

        _shownFailures = failures;
        FailedList.Children.Clear();
        if (failures.Count == 0)
        {
            return;
        }

        // The rows sit under the Library rows, so they are drawn like them: a line above, the same insets.
        var line = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        FailedList.Children.Add(new Border
        {
            BorderBrush = line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(32, 12, 44, 4),
            Child = new TextBlock
            {
                Text = $"Failed to load ({failures.Count})",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            },
        });
        foreach (var failure in failures)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = Presets.Name(failure.Id), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = failure.Error,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = secondary,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(32, 8, 44, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);
            if (Presets.Library.FileFor(failure.Id) is not null)
            {
                var id = failure.Id;
                var reveal = new Button { Content = "Reveal", VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(reveal, $"Reveal {Presets.Name(id)}");
                reveal.Click += (_, _) => Presets.Library.Reveal(id);
                Grid.SetColumn(reveal, 1);
                row.Children.Add(reveal);
            }

            FailedList.Children.Add(row);
        }
    }

    private async void OnImportFilesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
            foreach (var extension in PresetImport.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                Presets.Library.Import(files.Select(file => file.Path));
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info("settings", $"The file picker failed: {error.Message}");
        }
    }

    private async void OnImportFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
            if (await picker.PickSingleFolderAsync() is { } folder)
            {
                Presets.Library.Import([folder.Path]);
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info("settings", $"The folder picker failed: {error.Message}");
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => Presets.Library.OpenFolder();

    private void OnReloadClick(object sender, RoutedEventArgs e) => Presets.Library.Reload();

    private static void Select<T>(ComboBox picker, T value)
    {
        var item = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, value));
        if (item is not null && !ReferenceEquals(picker.SelectedItem, item))
        {
            picker.SelectedItem = item;
        }
    }

    // Opens a folded row when its switch is turned on, so the rows it has just brought to life are seen.
    // Turning it off leaves the row as it is.
    private static void Unfold(CommunityToolkit.WinUI.Controls.SettingsExpander expander, bool on)
    {
        if (on)
        {
            expander.IsExpanded = true;
        }
    }

    private static void Label(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }

    private void Change<T>(ComboBox picker, Action<PresetSettings, T> apply)
    {
        if (!_showing && picker.SelectedItem is ComboBoxItem { Tag: T value })
        {
            Presets.Update(settings => apply(settings, value));
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e) =>
        Change<PresetMode>(ModePicker, (settings, mode) => settings.Mode = mode);

    private void OnShuffleFromChanged(object sender, SelectionChangedEventArgs e) =>
        Change<ShuffleSource>(ShuffleFromPicker, (settings, source) => settings.ShuffleFrom = source);

    private void OnSecondsChanged(object sender, SelectionChangedEventArgs e) =>
        Change<int>(SecondsPicker, (settings, seconds) => settings.SecondsPerPreset = seconds);

    private void OnBlendChanged(object sender, SelectionChangedEventArgs e) =>
        Change<double>(BlendPicker, (settings, blend) => settings.BlendSeconds = blend);

    private async void OnSingleClick(object sender, RoutedEventArgs e) => await ShowDialog(new PresetPickerDialog(Presets), "Visualizer");

    // The toggle buttons flip themselves on click; ShowPresets then sets them from the settings.
    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (Presets.LastShown is { } id)
        {
            Presets.Update(settings => settings.SetFavorite(id, !settings.IsFavorite(id)));
        }
    }

    private void OnBlockClick(object sender, RoutedEventArgs e)
    {
        if (Presets.LastShown is { } id)
        {
            Presets.Update(settings => settings.SetBlocked(id, !settings.IsBlocked(id)));
        }
    }

    private async void OnFavoritesClick(object sender, RoutedEventArgs e) => await ShowList(PresetList.Favorites);

    private async void OnBlocklistClick(object sender, RoutedEventArgs e) => await ShowList(PresetList.Blocklist);

    private Task ShowList(PresetList list) => ShowDialog(new PresetListDialog(list, Presets, _app.PreviewPreset), list.ToString());

    private async Task ShowDialog(ContentDialog dialog, string name)
    {
        dialog.XamlRoot = Content.XamlRoot;
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Only one dialog can be open in a window at a time.
            Log.Info("settings", $"Can't show the {name} dialog: {error.Message}");
        }
    }
}
