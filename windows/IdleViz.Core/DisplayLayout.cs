namespace IdleViz.Core;

/// <summary>One display, reduced to what decides whether the visualizer still fits it.</summary>
/// <param name="Name">The name Windows gives it, for example <c>\\.\DISPLAY1</c>.</param>
/// <param name="Left">The left edge of the whole display in screen coordinates, taskbar included.</param>
/// <param name="Top">Its top edge.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Dpi">Its scale, as dots per inch: 96 is 100 %.</param>
public sealed record Display(string Name, int Left, int Top, int Width, int Height, int Dpi);

/// <summary>
/// Decides whether a display-change notice closes the visualizer. Windows sends such notices for
/// more than the displays themselves, so the full layout is compared with the last one seen and a
/// notice where nothing differs is ignored. Ported from <c>DisplayLayout.swift</c>.
/// </summary>
/// <param name="layout">The connected displays, the primary one first.</param>
public sealed class DisplayTracker(IReadOnlyList<Display> layout)
{
    public IReadOnlyList<Display> Layout { get; private set; } = layout;

    /// <summary>Call on each notice with the layout as it is now.</summary>
    public bool ShouldClose(IReadOnlyList<Display> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.SequenceEqual(Layout))
        {
            return false;
        }

        Layout = current;
        return true;
    }
}
