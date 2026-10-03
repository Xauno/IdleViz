using System.Globalization;

namespace IdleViz.Core;

/// <summary>
/// The "Brightness" setting: how much of the visualizer shows through the black dim layer.
/// Ported from <c>DisplaySettings.swift</c>.
/// </summary>
public static class BrightnessSetting
{
    public const string Key = "visualizerBrightness";
    public const double DefaultValue = 0.7;
    public const double Minimum = 0.5;
    public const double Maximum = 1;

    /// <summary>A brightness inside the slider's range, in whole percent.</summary>
    public static double Normalized(double value)
    {
        if (!double.IsFinite(value))
        {
            return DefaultValue;
        }

        var clamped = Math.Min(Math.Max(value, Minimum), Maximum);
        return Math.Round(clamped * 100, MidpointRounding.AwayFromZero) / 100;
    }

    public static string Label(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(Normalized(value) * 100, MidpointRounding.AwayFromZero)}%");

    /// <summary>Reads the stored value. A value that was never set, or isn't a number, is the default.</summary>
    public static double Value(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Normalized(settings.GetDouble(Key) ?? DefaultValue);
    }

    public static string Script(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"window.setBrightness?.({Normalized(value):0.0#})");
}

/// <summary>The "Show Spotify overlay" switch. Off means the page shows only the visualizer and the dim layer.</summary>
public static class OverlaySetting
{
    public const string Key = "showOverlay";

    /// <summary>On unless it was switched off.</summary>
    public static bool Value(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.GetBool(Key) ?? true;
    }

    public static string Script(bool enabled) => $"window.setOverlayEnabled?.({(enabled ? "true" : "false")})";
}
