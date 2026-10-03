namespace IdleViz.Core;

/// <summary>
/// Slow automatic gain. The capture hears Spotify after its own volume slider and its volume in
/// the Windows mixer, so a low Spotify volume would mean weak visuals. This follows the loudness
/// and scales it to a steady level. Ported from <c>AutoGain.swift</c>.
/// </summary>
public sealed class AutoGain
{
    /// <summary>The loudness (RMS) the loud parts of a song are brought to.</summary>
    public const float Target = 0.2f;

    /// <summary>
    /// Below this RMS (about −60 dBFS) the input counts as silence: the gain is held, not raised,
    /// so pauses, fades and gaps between tracks stay quiet.
    /// </summary>
    public const float Gate = 0.001f;

    public const float MinGain = 0.5f;
    public const float MaxGain = 32;

    // Seconds for the followed loudness to rise to a louder passage, and to fall to a quieter one.
    // Falling is slow on purpose: a quiet bridge should look quiet, not get turned up.
    private const float RiseSeconds = 0.5f;
    private const float FallSeconds = 5;

    // Starts at the target (a gain of 1), so the fade-in after a pause isn't mistaken for a quiet song.
    private float _level = Target;

    public float Gain { get; private set; } = 1;

    /// <summary>Feeds the loudness of the newest samples and returns the gain to apply to them.</summary>
    /// <param name="rms">RMS of the samples, before gain.</param>
    /// <param name="seconds">Time since the last call.</param>
    public float Update(float rms, float seconds)
    {
        if (!float.IsFinite(rms) || rms < Gate)
        {
            return Gain;
        }

        var time = rms > _level ? RiseSeconds : FallSeconds;
        _level += (rms - _level) * (1 - MathF.Exp(-Math.Max(seconds, 0) / time));
        Gain = Math.Clamp(Target / _level, MinGain, MaxGain);
        return Gain;
    }
}
