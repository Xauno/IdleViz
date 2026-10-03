namespace IdleViz.Core;

/// <summary>Which list the Favorites or Blocklist dialog manages. Ported from <c>PresetControls.swift</c>.</summary>
public enum PresetList
{
    Favorites,
    Blocklist,
}

public static class PresetLists
{
    public static string Title(this PresetList list) => list == PresetList.Favorites ? "Favorites" : "Blocklist";

    public static string EmptyText(this PresetList list) => list == PresetList.Favorites
        ? "No favorites yet. Add some from All presets, or with the heart next to Last shown."
        : "Nothing blocked. Blocked presets are left out of shuffle.";

    public static IReadOnlyList<string> Ids(this PresetList list, PresetSettings settings) =>
        list == PresetList.Favorites ? settings.Favorites : settings.Blocked;

    /// <summary>Adds or removes a preset. A preset is never on both lists: adding it to one takes it off the other.</summary>
    public static void Set(this PresetList list, PresetSettings settings, string id, bool listed)
    {
        if (list == PresetList.Favorites)
        {
            settings.SetFavorite(id, listed);
        }
        else
        {
            settings.SetBlocked(id, listed);
        }
    }

    /// <summary>The presets whose name contains the search text, ignoring case. Empty text matches all.</summary>
    public static IReadOnlyList<PresetInfo> Search(IReadOnlyList<PresetInfo> presets, string text)
    {
        var query = text.Trim();
        return query.Length == 0
            ? presets
            : [.. presets.Where(preset => preset.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))];
    }
}
