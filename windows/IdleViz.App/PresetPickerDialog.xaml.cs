using IdleViz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdleViz.App;

/// <summary>
/// The dialog behind the "Visualizer" row of Single mode: every preset with a search box. Picking
/// one applies at once, so it can be watched while the visualizer is open; Done closes the dialog.
/// </summary>
public sealed partial class PresetPickerDialog : ContentDialog
{
    private readonly PresetController _presets;

    // The ids behind the list's names, in the same order.
    private List<string> _ids = [];

    // Set while the list is being filled in, so its own selection event isn't taken as a pick.
    private bool _showing;

    internal PresetPickerDialog(PresetController presets)
    {
        _presets = presets;
        InitializeComponent();
        ShowRows();
        Opened += (_, _) =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            // Once the list has been laid out: before that it has nowhere to scroll to.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollToSelected);
        };
    }

    private void ShowRows()
    {
        var selected = _presets.Settings.SinglePreset;
        var presets = _presets.Presets;
        // Keeps the stored choice in the list while the page is still loading or the preset is gone.
        IReadOnlyList<PresetInfo> choices = presets.Any(preset => preset.Id == selected)
            ? presets
            : [new PresetInfo(selected, _presets.Name(selected), "bundled"), .. presets];
        var shown = PresetLists.Search(choices, SearchBox.Text);
        _ids = [.. shown.Select(preset => preset.Id)];
        _showing = true;
        try
        {
            Rows.ItemsSource = shown.Select(preset => preset.Name).ToList();
            Rows.SelectedIndex = _ids.IndexOf(selected);
        }
        finally
        {
            _showing = false;
        }

        ScrollToSelected();
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollToSelected()
    {
        if (Rows.SelectedItem is { } item)
        {
            Rows.ScrollIntoView(item);
        }
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ShowRows();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = Rows.SelectedIndex;
        if (!_showing && index >= 0 && index < _ids.Count)
        {
            var id = _ids[index];
            _presets.Update(settings => settings.SinglePreset = id);
        }
    }
}
