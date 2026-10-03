using System.Collections.ObjectModel;
using System.ComponentModel;
using IdleViz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdleViz.App;

/// <summary>
/// The Favorites or Blocklist dialog: one tab lists the presets on the list, where they can be
/// removed, the other every preset with a search box, where they can be added. Ported from
/// <c>PresetListSheet</c> in <c>PresetControls.swift</c>.
/// </summary>
public sealed partial class PresetListDialog : ContentDialog
{
    private readonly PresetList _list;
    private readonly PresetController _presets;
    private readonly ObservableCollection<PresetRow> _rows = [];

    internal PresetListDialog(PresetList list, PresetController presets)
    {
        _list = list;
        _presets = presets;
        InitializeComponent();
        Title = list.Title();
        EmptyText.Text = list.EmptyText();
        Rows.ItemsSource = _rows;
        ShowRows();
    }

    private bool ShowingAll => Tabs.SelectedItem == AllTab;

    private void ShowRows()
    {
        var ids = _list.Ids(_presets.Settings);
        ListedTab.Text = $"On the list ({ids.Count})";
        SearchBox.Visibility = ShowingAll ? Visibility.Visible : Visibility.Collapsed;
        _rows.Clear();
        if (ShowingAll)
        {
            foreach (var preset in PresetLists.Search(_presets.Presets, SearchBox.Text))
            {
                _rows.Add(new PresetRow(preset.Id, preset.Name, preset.IsCustom, ids.Contains(preset.Id), _list));
            }
        }
        else
        {
            foreach (var id in ids)
            {
                _rows.Add(new PresetRow(id, _presets.Name(id), id.StartsWith("custom:", StringComparison.Ordinal), listed: true, _list));
            }
        }

        EmptyText.Visibility = !ShowingAll && ids.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowRows();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ShowRows();

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

    /// <summary>What a screen reader says: "Remove Geiss - Swirlie 5 from favorites".</summary>
    public string ButtonLabel => Listed
        ? $"Remove {Name} from {_list.Title().ToLowerInvariant()}"
        : $"Add {Name} to {_list.Title().ToLowerInvariant()}";
}
