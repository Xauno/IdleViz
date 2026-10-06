namespace IdleViz.Core;

/// <summary>What a key can do while the visualizer is on screen, instead of closing it.</summary>
public enum VisualizerAction
{
    /// <summary>Adds the preset on screen to the favorites, or takes it off again.</summary>
    Like,

    /// <summary>Moves on to the next preset. Only shuffle mode has one.</summary>
    Skip,

    /// <summary>Adds the preset on screen to the blocklist, and in shuffle mode moves on to the next one.</summary>
    Block,
}

/// <summary>
/// The "Like key", "Skip key" and "Block key" settings. Each is stored as a virtual-key code, or
/// <see cref="Off"/>. The key names are the Mac's, the codes are not: the two systems number their
/// keys differently. Ported from <c>VisualizerKeys.swift</c>; the block key is Windows only so far.
/// </summary>
public sealed record VisualizerKeys
{
    public const string LikeKey = "likeKey";
    public const string SkipKey = "skipKey";
    public const string BlockKey = "blockKey";

    /// <summary>The stored value for "Off".</summary>
    public const int Off = -1;

    /// <summary>L.</summary>
    public const int DefaultLike = 0x4C;

    /// <summary>N.</summary>
    public const int DefaultSkip = 0x4E;

    /// <summary>B.</summary>
    public const int DefaultBlock = 0x42;

    private const ushort Escape = 0x1B;

    public VisualizerKeys(ushort? like = DefaultLike, ushort? skip = DefaultSkip, ushort? block = DefaultBlock)
    {
        Like = like;
        // One key can't do two things. Settings never records that, so this only happens with hand-edited
        // values, or when an older choice for like or skip is the key a newer action defaults to.
        Skip = skip == like ? null : skip;
        Block = block == Like || block == Skip ? null : block;
    }

    public ushort? Like { get; }

    public ushort? Skip { get; }

    public ushort? Block { get; }

    /// <summary>The key codes that do something other than close the visualizer.</summary>
    public IReadOnlySet<ushort> Codes => new[] { Like, Skip, Block }.OfType<ushort>().ToHashSet();

    /// <summary>
    /// Whether a key can be one of these keys: any keyboard key but Esc, which cancels the recorder,
    /// the modifiers, which are keys of their own to the visualizer, and the media keys, which keep
    /// their own job.
    /// </summary>
    public static bool CanBe(int code) =>
        KeyboardInput.IsKeyboardKey(code) && code != Escape && !Hotkey.IsModifierKey((ushort)code) && !MediaKeys.Contains((ushort)code);

    /// <summary>"L", "Space", "F5", or "Not set" for no key.</summary>
    public static string Label(ushort? code) => code is { } key ? Hotkey.KeyName(key) : "Not set";

    /// <summary>Reads the stored keys. A value that was never set, or can't be one of these keys, is the default.</summary>
    public static VisualizerKeys Read(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new VisualizerKeys(
            Code(settings.GetInt(LikeKey), DefaultLike),
            Code(settings.GetInt(SkipKey), DefaultSkip),
            Code(settings.GetInt(BlockKey), DefaultBlock));
    }

    public VisualizerAction? Action(ushort code)
    {
        if (code == Like)
        {
            return VisualizerAction.Like;
        }

        if (code == Skip)
        {
            return VisualizerAction.Skip;
        }

        return code == Block ? VisualizerAction.Block : null;
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

        return CanBe(value) ? (ushort)value : (ushort)fallback;
    }
}
