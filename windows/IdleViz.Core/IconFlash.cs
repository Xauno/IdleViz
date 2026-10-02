namespace IdleViz.Core;

/// <summary>
/// The tray icon's signal for a refused manual open: the slashed waveform and the normal one in
/// turn, 3 times over about 1 s. The same timing as the Mac's menu-bar icon.
/// </summary>
public static class IconFlash
{
    /// <summary>Slashed, normal, slashed, normal, slashed, normal.</summary>
    public const int Steps = 6;

    public const int StepMilliseconds = 170;

    /// <summary>Whether the slashed glyph shows at a step. Before the first step and after the last, it doesn't.</summary>
    public static bool IsSlashed(int step) => step is >= 0 and < Steps && step % 2 == 0;
}
