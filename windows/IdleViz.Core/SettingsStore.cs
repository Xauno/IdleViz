using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdleViz.Core;

/// <summary>
/// The app's settings: one JSON file, with the same key names the Mac app uses in UserDefaults.
/// A value of the wrong type reads as "never set", so each setting falls back to its default.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly JsonObject _values = [];

    /// <summary>A store kept in memory only, for tests.</summary>
    public SettingsStore()
    {
    }

    /// <summary>Reads the file if it exists. A missing or unreadable file starts empty.</summary>
    public SettingsStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject values)
            {
                _values = values;
            }
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unusable settings fall back to the defaults. The next change writes a good file.
            _values = [];
        }
    }

    /// <summary>Raised after a value changed, with its key.</summary>
    public event EventHandler<SettingChangedEventArgs>? Changed;

    public int? GetInt(string key) =>
        Value(key) is { } value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number)
            ? number
            : null;

    public bool? GetBool(string key) =>
        Value(key)?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    public string? GetString(string key) =>
        Value(key) is { } value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>A number. A whole number set with <see cref="SetInt"/> reads too.</summary>
    public double? GetDouble(string key)
    {
        if (Value(key) is not { } value || value.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        // A value read from the file converts to anything; one set in this run only to its own type.
        if (value.TryGetValue<double>(out var number))
        {
            return double.IsFinite(number) ? number : null;
        }

        return value.TryGetValue<int>(out var whole) ? whole : null;
    }

    /// <summary>A list of strings. Entries that aren't strings are skipped; anything but a list reads as not set.</summary>
    public IReadOnlyList<string>? GetStringList(string key) =>
        _values[key] is JsonArray list
            ? [.. list.OfType<JsonValue>().Where(item => item.GetValueKind() == JsonValueKind.String).Select(item => item.GetValue<string>())]
            : null;

    /// <summary>A map from names to numbers. Entries that aren't finite numbers are skipped; anything but a map reads as empty.</summary>
    public IReadOnlyDictionary<string, double> GetDoubleMap(string key)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        if (_values[key] is not JsonObject values)
        {
            return map;
        }

        foreach (var (name, node) in values)
        {
            if (node is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
            {
                var number = value.TryGetValue<double>(out var real) ? real : value.TryGetValue<int>(out var whole) ? whole : double.NaN;
                if (double.IsFinite(number))
                {
                    map[name] = number;
                }
            }
        }

        return map;
    }

    public void SetDoubleMap(string key, IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Set(key, new JsonObject(values.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)JsonValue.Create(pair.Value)))));
    }

    public void SetInt(string key, int value) => Set(key, JsonValue.Create(value));

    public void SetBool(string key, bool value) => Set(key, JsonValue.Create(value));

    public void SetString(string key, string value) => Set(key, JsonValue.Create(value));

    public void SetDouble(string key, double value) => Set(key, JsonValue.Create(value));

    public void SetStringList(string key, IEnumerable<string> values) => Set(key, new JsonArray([.. values.Select(value => (JsonNode)JsonValue.Create(value))]));

    public void Remove(string key)
    {
        if (_values.Remove(key))
        {
            Save();
            Changed?.Invoke(this, new SettingChangedEventArgs(key));
        }
    }

    private JsonValue? Value(string key) => _values[key] as JsonValue;

    private void Set(string key, JsonNode value)
    {
        if (_values[key] is { } current && JsonNode.DeepEquals(current, value))
        {
            return;
        }

        _values[key] = value;
        Save();
        Changed?.Invoke(this, new SettingChangedEventArgs(key));
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Written next to the file and moved into place, so a crash can't leave half a file behind.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, _values.ToJsonString(s_writeOptions));
        File.Move(temporary, _path, overwrite: true);
    }
}

public sealed class SettingChangedEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}
