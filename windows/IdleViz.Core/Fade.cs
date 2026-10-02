namespace IdleViz.Core;

/// <summary>Why the visualizer closes. Each reason fades out at its own speed.</summary>
public enum CloseReason
{
    /// <summary>Any mouse, key or touch input.</summary>
    Input,

    /// <summary>The keep-awake limit was reached.</summary>
    KeepAwakeLimit,

    /// <summary>The PC is going to sleep or the displays changed.</summary>
    DisplayChanged,
}

public static class CloseReasonExtensions
{
    public static double FadeSeconds(this CloseReason reason) => reason switch
    {
        // Someone is at the PC and wants it back, so this is as quick as a screensaver.
        CloseReason.Input => 0.25,
        // Nobody is at the PC, so it leaves gently.
        CloseReason.KeepAwakeLimit => 1.5,
        // The screen it's on may be gone before a fade could finish.
        CloseReason.DisplayChanged => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}

public static class VisualizerFade
{
    public const double OpenSeconds = 0.6;
}
