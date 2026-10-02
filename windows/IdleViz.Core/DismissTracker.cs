namespace IdleViz.Core;

public enum InputKind
{
    MouseMoved,
    MouseDown,
    Scroll,
    Gesture,
    Key,
    MediaKey,
}

/// <summary>One user input, reduced to what the dismiss rules care about.</summary>
public readonly record struct InputEvent
{
    private InputEvent(InputKind kind, double deltaX = 0, double deltaY = 0, ushort code = 0, bool isDown = false, bool isRepeat = false)
    {
        Kind = kind;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Code = code;
        IsDown = isDown;
        IsRepeat = isRepeat;
    }

    public InputKind Kind { get; }

    public double DeltaX { get; }

    public double DeltaY { get; }

    /// <summary>The virtual-key code of a <see cref="InputKind.Key"/> event.</summary>
    public ushort Code { get; }

    public bool IsDown { get; }

    public bool IsRepeat { get; }

    public static InputEvent MouseDown { get; } = new(InputKind.MouseDown);

    public static InputEvent Scroll { get; } = new(InputKind.Scroll);

    public static InputEvent Gesture { get; } = new(InputKind.Gesture);

    /// <summary>One of the keys that leave you watching: playback or volume.</summary>
    public static InputEvent MediaKey { get; } = new(InputKind.MediaKey);

    public static InputEvent MouseMoved(double deltaX, double deltaY) => new(InputKind.MouseMoved, deltaX, deltaY);

    /// <summary>A key or modifier going down or up. <paramref name="isRepeat"/> marks key-repeat presses.</summary>
    public static InputEvent Key(ushort code, bool isDown, bool isRepeat) =>
        new(InputKind.Key, code: code, isDown: isDown, isRepeat: isRepeat);
}

/// <summary>
/// The keys that don't close the visualizer and still do their job. On Windows they are ordinary
/// virtual keys. Brightness and keyboard-backlight keys are handled by the PC's firmware and never
/// show up as keys at all, so they need no entry here.
/// </summary>
public static class MediaKeys
{
    /// <summary>
    /// Mute, volume down and up (0xAD to 0xAF), next and previous track (0xB0, 0xB1) and play/pause (0xB3).
    /// Stop (0xB2), and the keys that open an app (mail, browser, calculator), still close it.
    /// </summary>
    private static readonly HashSet<ushort> s_virtualKeys = [0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB3];

    public static bool Contains(ushort virtualKey) => s_virtualKeys.Contains(virtualKey);
}

/// <summary>
/// Decides when the visualizer closes. Pure logic, so the app's input hooks
/// and the ~100 ms backup check can both be tested without a screen.
///
/// Input during the grace period after opening is ignored, so the trigger itself
/// (the hotkey, the click on "Open now") doesn't close the window again. Keys still
/// held when the grace period ends are "stuck": letting go of them, or their key
/// repeat, is ignored too. Pressing one again closes the window.
///
/// The like and skip keys (<c>passKeys</c>) and the media keys never close the window when the
/// hooks see them.
/// </summary>
public sealed class DismissTracker
{
    /// <summary>
    /// The backup check reads the time of the last input from Windows' tick count, which moves in
    /// steps of about 16 ms, while the hooks are timed with a stopwatch. Input this soon after the
    /// last one the hooks explained is the same input, not a new one.
    /// </summary>
    public const double ClockSlackSeconds = 0.05;

    private readonly HashSet<ushort> _stuckKeys = [];
    private bool _armed;

    /// <summary>Elapsed time up to which the backup check treats input as explained by stuck or pass keys.</summary>
    private double _ignoreInputUntil;

    public DismissTracker(double gracePeriod = 0.4, IEnumerable<ushort>? passKeys = null)
    {
        GracePeriod = gracePeriod;
        PassKeys = new HashSet<ushort>(passKeys ?? []);
    }

    public double GracePeriod { get; }

    /// <summary>Key codes that do something else than close the window.</summary>
    public IReadOnlySet<ushort> PassKeys { get; }

    public IReadOnlySet<ushort> StuckKeys => _stuckKeys;

    /// <summary>
    /// Call once when the grace period ends, with the key codes held at that moment.
    /// Until then, nothing closes the window.
    /// </summary>
    public void Arm(IEnumerable<ushort> heldKeys, double elapsed)
    {
        _armed = true;
        _stuckKeys.Clear();
        _stuckKeys.UnionWith(heldKeys);
        _ignoreInputUntil = elapsed;
    }

    /// <summary><paramref name="elapsed"/> is the time since the window opened.</summary>
    public bool ShouldDismiss(InputEvent input, double elapsed)
    {
        if (!_armed)
        {
            return false;
        }

        switch (input.Kind)
        {
            case InputKind.MouseMoved:
                // Touchpads can send zero-delta moves while nobody touches them.
                return input.DeltaX != 0 || input.DeltaY != 0;
            case InputKind.MouseDown:
            case InputKind.Scroll:
            case InputKind.Gesture:
                return true;
            case InputKind.MediaKey:
                // Down, repeating or up: input the backup check shouldn't close on either.
                _ignoreInputUntil = Math.Max(_ignoreInputUntil, elapsed);
                return false;
            case InputKind.Key:
                return ShouldDismissOnKey(input, elapsed);
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    private bool ShouldDismissOnKey(InputEvent input, double elapsed)
    {
        if (PassKeys.Contains(input.Code))
        {
            // Pressed, repeating or let go, it's input the backup check shouldn't close on either.
            _stuckKeys.Remove(input.Code);
            _ignoreInputUntil = Math.Max(_ignoreInputUntil, elapsed);
            return false;
        }

        if (!_stuckKeys.Contains(input.Code))
        {
            return input.IsDown;
        }

        if (input.IsDown && !input.IsRepeat)
        {
            // A fresh press means the release was missed; it counts as new input.
            _stuckKeys.Remove(input.Code);
            return true;
        }

        if (!input.IsDown)
        {
            _stuckKeys.Remove(input.Code);
        }

        _ignoreInputUntil = Math.Max(_ignoreInputUntil, elapsed);
        return false;
    }

    /// <summary>
    /// Backup for input the hooks didn't see (Windows drops a hook that answers too slowly).
    /// <paramref name="heldKeys"/> are the key codes down right now. A pass key or media key the hooks
    /// didn't see did nothing here, so it closes the window like any other key.
    /// </summary>
    public bool ShouldDismiss(double secondsSinceLastInput, IEnumerable<ushort> heldKeys, double elapsed)
    {
        if (!_armed)
        {
            return false;
        }

        var held = heldKeys as IReadOnlySet<ushort> ?? new HashSet<ushort>(heldKeys);
        var released = _stuckKeys.RemoveWhere(key => !held.Contains(key));

        // A stuck key let go since the last check, or one still repeating, explains any input so far.
        if (released > 0 || _stuckKeys.Count > 0)
        {
            _ignoreInputUntil = Math.Max(_ignoreInputUntil, elapsed);
        }

        var inputAt = elapsed - secondsSinceLastInput;
        return inputAt > Math.Max(GracePeriod, _ignoreInputUntil + ClockSlackSeconds);
    }
}
