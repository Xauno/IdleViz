using IdleViz.Core;

namespace IdleViz.App;

/// <summary>
/// The preset controls behind the settings window: keeps them in the settings file, sends each
/// change to the page, and knows the page's preset list and which preset was last on screen.
/// Ported from <c>PresetController.swift</c>.
/// </summary>
internal sealed class PresetController
{
    private readonly PageView _page;
    private readonly SettingsStore _store;
    private Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public PresetController(PageView page, SettingsStore store)
    {
        _page = page;
        _store = store;
        Settings = PresetSettings.Read(store);
        LastShown = store.GetString(PresetSettings.LastShownKey);
        page.PresetsLoaded += presets =>
        {
            Presets = presets;
            _names = presets.GroupBy(preset => preset.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
            Changed?.Invoke();
        };
        page.PresetShown += id =>
        {
            if (id == LastShown)
            {
                return;
            }

            LastShown = id;
            _store.SetString(PresetSettings.LastShownKey, id);
            Changed?.Invoke();
        };
        page.SendPresetSettings(Settings);
    }

    /// <summary>Raised on the UI thread after the settings, the preset list or the last-shown preset changed.</summary>
    public event Action? Changed;

    /// <summary>The current controls. Change them through <see cref="Update"/>.</summary>
    public PresetSettings Settings { get; }

    /// <summary>Every preset the page can show, in the page's order. Empty until the page has loaded.</summary>
    public IReadOnlyList<PresetInfo> Presets { get; private set; } = [];

    /// <summary>The preset that was last on screen, so it can be liked or blocked after closing the visualizer.</summary>
    public string? LastShown { get; private set; }

    public bool HasCustomPresets => Presets.Any(preset => preset.IsCustom);

    public string Name(string id) => _names.TryGetValue(id, out var name) ? name : PresetInfo.FallbackName(id);

    /// <summary>Applies a change, stores it and hands it to the page, which applies it at once.</summary>
    public void Update(Action<PresetSettings> change)
    {
        var before = Settings.Script;
        change(Settings);
        if (Settings.Script == before)
        {
            return;
        }

        Settings.Save(_store);
        _page.SendPresetSettings(Settings);
        Changed?.Invoke();
    }
}
