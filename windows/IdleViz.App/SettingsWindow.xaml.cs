using System.Globalization;
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
/// The settings window: small, portrait, fixed size, one page that scrolls. Closing it only closes
/// the window; the app keeps running in the tray.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double WidthInPixels = 440;
    private const double HeightInPixels = 680;

    // Segoe Fluent Icons: Heart and HeartFill.
    private const int HeartGlyph = 0xE006;
    private const int FilledHeartGlyph = 0xE00B;

    private readonly App _app;

    // Set while the rows are being filled in from the settings, so the pickers' own events aren't taken as changes.
    private bool _showing;

    // What the Visualizer picker was last filled with: the page's list and a stored preset missing from it.
    private IReadOnlyList<PresetInfo>? _singleChoices;
    private string? _singleExtra;

    // The ids behind the Visualizer picker's names, in the same order.
    private List<string> _singleIds = [];

    // What the Failed to load list shows, so it is only rebuilt when that changes.
    private IReadOnlyList<PresetFailure> _shownFailures = [];

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();

        Title = "IdleViz Settings";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "IdleViz.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        var scale = PInvoke.GetDpiForWindow(Handle) / 96.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(WidthInPixels * scale), (int)Math.Round(HeightInPixels * scale)));

        ShowIdleChoices();
        Recorder.Attach(_app);
        Recorder.HotkeyChanged += ShowHotkeyState;
        ShowHotkeyState();

        FillPresetChoices();
        Presets.Changed += ShowPresets;
        Closed += (_, _) => Presets.Changed -= ShowPresets;
        ShowPresets();
    }

    private PresetController Presets => _app.Presets;

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>Shows the window, or brings it forward if it is already open.</summary>
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

    // 5, 10, 15 and 30 min, then Off, as on the Mac. A stored value that isn't a choice gets its own entry.
    private void ShowIdleChoices()
    {
        var current = _app.IdleMinutes;
        var minutes = IdleTimeoutSetting.Choices.Append(0).ToList();
        if (!minutes.Contains(current))
        {
            minutes.Insert(0, current);
        }

        foreach (var value in minutes)
        {
            IdlePicker.Items.Add(new ComboBoxItem { Content = value > 0 ? $"{value} min" : "Off", Tag = value });
        }

        IdlePicker.SelectedIndex = minutes.IndexOf(current);
    }

    private void OnIdleChanged(object sender, SelectionChangedEventArgs e)
    {
        // Showing the stored value selects it too; that is not a change.
        if (IdlePicker.SelectedItem is ComboBoxItem { Tag: int minutes } && minutes != _app.IdleMinutes)
        {
            _app.IdleMinutes = minutes;
        }
    }

    private void OnOpenNowClick(object sender, RoutedEventArgs e) => _app.OpenVisualizer(TriggerSource.Settings);

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
            SecondsPicker.Items.Add(new ComboBoxItem { Content = seconds.ToString(CultureInfo.InvariantCulture), Tag = seconds });
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
            ShowSingleChoices(settings.SinglePreset);

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

            FolderCard.Description = $"{Presets.BundledCount} bundled, {Presets.Library.CustomCount} custom";
            ShowFailures(Presets.Failures);
        }
        finally
        {
            _showing = false;
        }
    }

    // The page's list has a few hundred entries, so the picker is only refilled when it changed, and gets
    // them as a plain list of names, which it can virtualize.
    private void ShowSingleChoices(string selected)
    {
        var presets = Presets.Presets;
        // Keeps the stored choice selectable while the list is still loading or the preset is gone.
        var extra = presets.Any(preset => preset.Id == selected) ? null : selected;
        if (!ReferenceEquals(presets, _singleChoices) || extra != _singleExtra)
        {
            _singleChoices = presets;
            _singleExtra = extra;
            var choices = (extra is null ? [] : new[] { new PresetInfo(extra, Presets.Name(extra), "bundled") }).Concat(presets).ToList();
            _singleIds = [.. choices.Select(preset => preset.Id)];
            SinglePicker.ItemsSource = choices.Select(preset => preset.Name).ToList();
        }

        var index = _singleIds.IndexOf(selected);
        if (SinglePicker.SelectedIndex != index)
        {
            SinglePicker.SelectedIndex = index;
        }
    }

    private void ShowFailures(IReadOnlyList<PresetFailure> failures)
    {
        if (failures.SequenceEqual(_shownFailures))
        {
            return;
        }

        _shownFailures = failures;
        FailedHeader.Text = $"Failed to load ({failures.Count})";
        FailedHeader.Visibility = failures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FailedList.Children.Clear();
        foreach (var failure in failures)
        {
            var card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = new TextBlock { Text = Presets.Name(failure.Id), TextTrimming = TextTrimming.CharacterEllipsis },
                Description = new TextBlock { Text = failure.Error, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            if (Presets.Library.FileFor(failure.Id) is not null)
            {
                var id = failure.Id;
                var reveal = new Button { Content = "Reveal" };
                AutomationProperties.SetName(reveal, $"Reveal {Presets.Name(id)}");
                reveal.Click += (_, _) => Presets.Library.Reveal(id);
                card.Content = reveal;
            }

            FailedList.Children.Add(card);
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

    private void OnSingleChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = SinglePicker.SelectedIndex;
        if (!_showing && index >= 0 && index < _singleIds.Count)
        {
            var id = _singleIds[index];
            Presets.Update(settings => settings.SinglePreset = id);
        }
    }

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

    private async Task ShowList(PresetList list)
    {
        var dialog = new PresetListDialog(list, Presets) { XamlRoot = Content.XamlRoot };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Only one dialog can be open in a window at a time.
            Log.Info("settings", $"Can't show the {list} dialog: {error.Message}");
        }
    }
}
