namespace IdleViz.Core;

/// <summary>What a key can do while the visualizer is on screen, instead of closing it.</summary>
public enum VisualizerAction
{
    /// <summary>Adds the preset on screen to the favorites, or takes it off again.</summary>
    Like,

    /// <summary>Moves on to the next preset. Only shuffle mode has one.</summary>
    Skip,
}

/// <summary>
/// One key the "Like key" and "Skip key" pickers offer, by its Windows virtual-key code. For these
/// keys the code names the letter or digit the key types, whatever the keyboard layout.
/// </summary>
public readonly record struct VisualizerKey(ushort Code, string Label)
{
    private const ushort Left = 0x25;
    private const ushort Up = 0x26;
    private const ushort Right = 0x27;
    private const ushort Down = 0x28;
    private const ushort Space = 0x20;

    /// <summary>A to Z, 0 to 9, the arrow keys and Space, in the order the pickers list them.</summary>
    public static IReadOnlyList<VisualizerKey> Choices { get; } =
    [
        .. Enumerable.Range('A', 26).Select(code => new VisualizerKey((ushort)code, ((char)code).ToString())),
        .. Enumerable.Range('0', 10).Select(code => new VisualizerKey((ushort)code, ((char)code).ToString())),
        new(Left, "←"),
        new(Right, "→"),
        new(Up, "↑"),
        new(Down, "↓"),
        new(Space, "Space"),
    ];
}

/// <summary>
/// The "Like key" and "Skip key" settings. Each is stored as a virtual-key code, or <see cref="Off"/>.
/// The key names are the Mac's, the codes are not: the two systems number their keys differently.
/// Ported from <c>VisualizerKeys.swift</c>.
/// </summary>
public sealed record VisualizerKeys
{
    public const string LikeKey = "likeKey";
    public const string SkipKey = "skipKey";

    /// <summary>The stored value for "Off".</summary>
    public const int Off = -1;

    /// <summary>L.</summary>
    public const int DefaultLike = 0x4C;

    /// <summary>N.</summary>
    public const int DefaultSkip = 0x4E;

    public VisualizerKeys(ushort? like = DefaultLike, ushort? skip = DefaultSkip)
    {
        Like = like;
        // One key can't do both. Settings never offers that, so this only happens with hand-edited values.
        Skip = skip == like ? null : skip;
    }

    public ushort? Like { get; }

    public ushort? Skip { get; }

    /// <summary>The key codes that do something other than close the visualizer.</summary>
    public IReadOnlySet<ushort> Codes => new[] { Like, Skip }.OfType<ushort>().ToHashSet();

    /// <summary>Reads the stored keys. A value that was never set, or isn't one of the choices, is the default.</summary>
    public static VisualizerKeys Read(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new VisualizerKeys(Code(settings.GetInt(LikeKey), DefaultLike), Code(settings.GetInt(SkipKey), DefaultSkip));
    }

    public VisualizerAction? Action(ushort code)
    {
        if (code == Like)
        {
            return VisualizerAction.Like;
        }

        return code == Skip ? VisualizerAction.Skip : null;
    }

    /// <summary>The call that asks the page for the next preset.</summary>
    public const string SkipScript = "window.skipPreset?.()";

    /// <summary>The call that shows the heart: filled for a preset that was just liked, an outline for one that was unliked.</summary>
    public static string LikeScript(bool liked) => $"window.showLike?.({(liked ? "true" : "false")})";

    private static ushort? Code(int? stored, int fallback)
    {
        var value = stored ?? fallback;
        if (value == Off)
        {
            return null;
        }

        return VisualizerKey.Choices.Any(key => key.Code == value) ? (ushort)value : (ushort)fallback;
    }
}
