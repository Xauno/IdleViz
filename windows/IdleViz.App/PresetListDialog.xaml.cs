using System.Collections.ObjectModel;
using System.ComponentModel;
using IdleViz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdleViz.App;

/// <summary>
/// The Favorites or Blocklist dialog: one tab lists the presets on the list, where they can be
/// removed, the other every preset, where they can be added. Both have the search box, and every
/// row a button that opens the visualizer on that preset. Ported from <c>PresetListSheet</c> in
/// <c>PresetControls.swift</c>.
/// </summary>
public sealed partial class PresetListDialog : ContentDialog
{
    private readonly PresetList _list;
    private readonly PresetController _presets;
    private readonly Action<string> _preview;
    private readonly ObservableCollection<PresetRow> _rows = [];

    /// <param name="list">Favorites or Blocklist.</param>
    /// <param name="presets">The preset controls.</param>
    /// <param name="preview">Opens the visualizer on a preset.</param>
    internal PresetListDialog(PresetList list, PresetController presets, Action<string> preview)
    {
        _list = list;
        _presets = presets;
        _preview = preview;
        InitializeComponent();
        Title = list.Title();
        Rows.ItemsSource = _rows;
        ShowRows();
    }

    private bool ShowingAll => Tabs.SelectedItem == AllTab;

    private void ShowRows()
    {
        var ids = _list.Ids(_presets.Settings);
        ListedTab.Text = $"On the list ({ids.Count})";
        // A preset on the list may be gone from the library, so those rows are made from the ids.
        var source = ShowingAll
            ? _presets.Presets
            : [.. ids.Select(id => new PresetInfo(id, _presets.Name(id), id.StartsWith("custom:", StringComparison.Ordinal) ? "custom" : "bundled"))];
        _rows.Clear();
        foreach (var preset in PresetLists.Search(source, SearchBox.Text))
        {
            _rows.Add(new PresetRow(preset.Id, preset.Name, preset.IsCustom, ids.Contains(preset.Id), _list));
        }

        EmptyText.Text = !ShowingAll && ids.Count == 0 ? _list.EmptyText() : "No presets match.";
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowRows();

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ShowRows();

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetRow row })
        {
            _preview(row.Id);
        }
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetRow row })
        {
            return;
        }

        var listed = !row.Listed;
        _presets.Update(settings => _list.Set(settings, row.Id, listed));
        if (ShowingAll)
        {
            // Changed in place, so the list keeps its scroll position.
            row.Listed = listed;
            ListedTab.Text = $"On the list ({_list.Ids(_presets.Settings).Count})";
        }
        else
        {
            ShowRows();
        }
    }
}

/// <summary>One row of the Favorites or Blocklist dialog.</summary>
public sealed partial class PresetRow : INotifyPropertyChanged
{
    private readonly PresetList _list;
    private bool _listed;

    internal PresetRow(string id, string name, bool custom, bool listed, PresetList list)
    {
        Id = id;
        Name = name;
        CustomVisibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _listed = listed;
        _list = list;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Name { get; }

    public Visibility CustomVisibility { get; }

    public bool Listed
    {
        get => _listed;
        set
        {
            if (_listed == value)
            {
                return;
            }

            _listed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonLabel)));
        }
    }

    public string ButtonText => Listed ? "Remove" : "Add";

    public string PreviewLabel => $"Preview {Name}";

    /// <summary>What a screen reader says: "Remove Geiss - Swirlie 5 from favorites".</summary>
    public string ButtonLabel => Listed
        ? $"Remove {Name} from {_list.Title().ToLowerInvariant()}"
        : $"Add {Name} to {_list.Title().ToLowerInvariant()}";
}
