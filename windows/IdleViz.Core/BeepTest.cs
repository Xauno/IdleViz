namespace IdleViz.Core;

/// <summary>A lit panel in the manual delay test.</summary>
/// <param name="Accent">True for the marked beep's flash.</param>
public readonly record struct BeepFlash(bool Accent);

/// <summary>
/// The manual delay test: a beep every second, and a panel that lights up one audio delay after
/// each beep was sent to the speakers. When the light and the sound land together, the delay is right.
///
/// Every fourth beep is higher and its flash has another colour, so a flash can be told apart
/// from the one a beep earlier or later even when the delay is longer than the gap between beeps.
/// Ported from <c>BeepTest.swift</c>.
/// </summary>
public static class BeepTest
{
    /// <summary>Seconds from one beep to the next.</summary>
    public const double Period = 1;

    /// <summary>Every this many beeps, one is marked.</summary>
    public const int AccentEvery = 4;

    public const double BeepSeconds = 0.06;

    /// <summary>The panel stays lit a little longer than the beep sounds, so it's easy to see.</summary>
    public const double FlashSeconds = 0.12;

    public const double Frequency = 880;
    public const double AccentFrequency = 1760;

    private const float Volume = 0.4f;

    // The beep fades in and out over this long, so it doesn't click.
    private const double FadeSeconds = 0.005;

    /// <summary>Whether the beep with this index, counted from 0, is a marked one.</summary>
    public static bool IsAccent(int index) => index % AccentEvery == 0;

    /// <summary>When the beep with this index is sent to the speakers.</summary>
    public static double BeepTime(int index, double start) => start + (index * Period);

    /// <summary>The flash showing at <paramref name="time"/>, or null while the panel is dark.</summary>
    /// <param name="time">Now.</param>
    /// <param name="start">When the first beep was sent to the speakers, on the same clock as <paramref name="time"/>.</param>
    /// <param name="delay">The audio delay being tried.</param>
    public static BeepFlash? Flash(double time, double start, double delay)
    {
        var sinceFirst = time - start - delay;
        if (!(sinceFirst >= 0))
        {
            return null;
        }

        var index = (int)(sinceFirst / Period);
        return sinceFirst - (index * Period) < FlashSeconds ? new BeepFlash(IsAccent(index)) : null;
    }

    /// <summary>One beep as mono samples: a sine tone with a short fade at both ends.</summary>
    public static float[] Samples(bool accent, double sampleRate)
    {
        if (!(sampleRate > 0))
        {
            return [];
        }

        var count = (int)(BeepSeconds * sampleRate);
        var fade = Math.Max(1, (int)(FadeSeconds * sampleRate));
        var step = 2 * Math.PI * (accent ? AccentFrequency : Frequency) / sampleRate;
        var samples = new float[count];
        for (var index = 0; index < count; index++)
        {
            var envelope = Math.Min(1, (float)Math.Min(index, count - 1 - index) / fade);
            samples[index] = (float)Math.Sin(index * step) * envelope * Volume;
        }

        return samples;
    }
}
