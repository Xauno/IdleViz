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

    public PresetController(PageView page, SettingsStore store, PresetLibrary library)
    {
        _page = page;
        _store = store;
        Library = library;
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
        page.FailuresChanged += failures =>
        {
            PageFailures = failures;
            Changed?.Invoke();
        };
        page.Hung += library.MarkHung;
        library.PayloadChanged += page.SendCustomPresets;
        library.Changed += () => Changed?.Invoke();
        page.SendPresetSettings(Settings);
        library.Start();
    }

    /// <summary>The custom presets folder.</summary>
    public PresetLibrary Library { get; }

    /// <summary>Presets and plugins the page couldn't load.</summary>
    public IReadOnlyList<PresetFailure> PageFailures { get; private set; } = [];

    /// <summary>Everything that failed to load: files that didn't convert, then what the page reported.</summary>
    public IReadOnlyList<PresetFailure> Failures
    {
        get
        {
            var converted = Library.ConversionFailures;
            var known = converted.Select(failure => failure.Id).ToHashSet(StringComparer.Ordinal);
            return [.. converted, .. PageFailures.Where(failure => !known.Contains(failure.Id))];
        }
    }

    public int BundledCount => Presets.Count(preset => !preset.IsCustom);

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

    /// <summary>Runs the like or skip key.</summary>
    public async void Perform(VisualizerAction action)
    {
        if (action == VisualizerAction.Skip)
        {
            _page.SkipPreset();
            return;
        }

        // LastShown can be a second behind, so ask the page what is on screen right now.
        if (await _page.CurrentPreset() is not { } id)
        {
            return;
        }

        var liked = !Settings.IsFavorite(id);
        Update(settings => settings.SetFavorite(id, liked));
        _page.ShowLike(liked);
        Log.Info("presets", $"{(liked ? "Liked" : "Unliked")} {id}");
    }

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
