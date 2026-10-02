namespace IdleViz.Core;

/// <summary>
/// Turns what a low-level keyboard hook reports (a virtual key going down or up) into input events.
/// The hook doesn't say whether a press is a key repeat, so this remembers which keys are down.
/// </summary>
public sealed class KeyboardInput
{
    private readonly HashSet<ushort> _down = [];

    /// <summary>Call when the hooks start, with the keys already held, so their repeats count as repeats.</summary>
    public void Reset(IEnumerable<ushort> heldKeys)
    {
        _down.Clear();
        _down.UnionWith(heldKeys);
    }

    public InputEvent Translate(ushort virtualKey, bool isDown)
    {
        var isRepeat = false;
        if (isDown)
        {
            isRepeat = !_down.Add(virtualKey);
        }
        else
        {
            _down.Remove(virtualKey);
        }

        return MediaKeys.Contains(virtualKey) ? InputEvent.MediaKey : InputEvent.Key(virtualKey, isDown, isRepeat);
    }

    /// <summary>
    /// Whether a virtual-key code is a keyboard key worth asking the key state of. Codes 1 to 6 are mouse
    /// buttons. 0x10 to 0x12 are Shift, Ctrl and Alt without a side: they are down whenever the left or
    /// right one is, and the hook only ever reports the sided codes (0xA0 to 0xA5).
    /// </summary>
    public static bool IsKeyboardKey(int virtualKey) =>
        virtualKey is >= 0x08 and <= 0xFE and not (0x10 or 0x11 or 0x12);
}
