namespace IdleViz.Core;

/// <summary>A few seconds of audio from one source, for Detect delay.</summary>
/// <param name="Samples">Mono samples.</param>
/// <param name="SampleRate">Samples per second.</param>
/// <param name="Start">The time of the first sample, in seconds on a clock both recordings share.</param>
public sealed record DelayRecording(float[] Samples, double SampleRate, double Start = 0);

/// <summary>The best lag found, and how clearly it stands out.</summary>
/// <param name="Delay">Seconds the heard recording lags behind the sent one.</param>
/// <param name="ZScore">How far the peak stands above the other lags, in standard deviations.</param>
/// <param name="Ratio">The peak over the best lag elsewhere.</param>
public readonly record struct DelayPeak(double Delay, double ZScore, double Ratio)
{
    /// <summary>Whether the peak is clear enough to trust.</summary>
    public bool IsClear => ZScore >= DelayDetector.MinimumZScore && Ratio >= DelayDetector.MinimumPeakRatio;
}

/// <summary>
/// Measures how far the sound from the speakers lags behind the audio Spotify sent, from two
/// recordings of the same few seconds: the capture's signal and the microphone's.
///
/// The two waveforms are cross-correlated with a softened phase transform (GCC-PHAT): every
/// frequency counts nearly the same however loud it is, so the tone of the speakers and the room
/// matters little and the right lag shows up as a sharp spike with its echoes just behind it.
/// Ported from <c>DelayDetector</c> in <c>AudioDelay.swift</c>, with a plain radix-2 FFT in place
/// of Apple's vDSP.
/// </summary>
public static class DelayDetector
{
    /// <summary>The peak has to stand this many standard deviations above the other lags…</summary>
    public const double MinimumZScore = 10;

    /// <summary>…and this far above the best lag elsewhere, or a steady beat could match one beat late.</summary>
    public const double MinimumPeakRatio = 1.5;

    // Only these frequencies are compared: small speakers play little below, and above the
    // microphone hears mostly noise.
    private const double LowestFrequency = 300;
    private const double HighestFrequency = 6000;

    // How much of each frequency's loudness is divided away. 1 is the pure phase transform;
    // a little less keeps frequencies the microphone barely hears from adding only noise.
    private const double Whitening = 0.8;

    // The correlation is averaged over this many seconds, which adds a spike's closest echoes to it.
    private const double EchoWindow = 0.005;

    // Lags this close to the peak, in seconds, belong to it: echoes off the desk and the walls.
    private const double PeakWidth = 0.1;

    // The search starts a little below zero: the speakers can sound slightly before the capture
    // hands the audio over. Such a result is saved as no delay.
    private const double EarliestLag = -0.1;

    // The sines and cosines of the last transform size, kept because every measurement uses the same size.
    private static (float[] Cosines, float[] Sines)? s_twiddles;

    /// <summary>
    /// Cross-correlates the two recordings over lags of just below 0 to 2.5 s and returns the best one, clear or not.
    /// </summary>
    /// <param name="sent">What the capture delivered.</param>
    /// <param name="heard">What the microphone recorded.</param>
    /// <param name="inputLatency">The microphone's own latency in seconds, which is not part of the speakers' delay.</param>
    /// <returns>Null if the recordings are too short or one of them is silent.</returns>
    public static DelayPeak? FindPeak(DelayRecording sent, DelayRecording heard, double inputLatency = 0)
    {
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentNullException.ThrowIfNull(heard);
        var rate = sent.SampleRate;
        const double MaxLag = AudioDelaySetting.Maximum;
        if (!(rate > 0) || !(heard.SampleRate > 0))
        {
            return null;
        }

        var heardSamples = Resample(heard.Samples, heard.SampleRate, rate);
        if (!(sent.Samples.Length > MaxLag * rate) || !(heardSamples.Length > MaxLag * rate))
        {
            return null;
        }

        // Long enough that the correlation doesn't wrap around.
        var log2n = (int)Math.Ceiling(Math.Log2(sent.Samples.Length + heardSamples.Length));
        var count = 1 << log2n;
        if (PhaseCorrelation(sent.Samples, heardSamples, count, rate) is not { } correlation)
        {
            return null;
        }

        // correlation[shift] compares sent[t] with heard[t + shift]; a negative shift wraps to the end.
        // The microphone recording started `offset` seconds after the capture's, so a lag is `shift / rate + offset`.
        // The sound reached the microphone `inputLatency` before it reached the recording.
        var offset = heard.Start - inputLatency - sent.Start;
        var firstShift = (int)Math.Round((EarliestLag - offset) * rate, MidpointRounding.AwayFromZero);
        var shifts = (int)((MaxLag - EarliestLag) * rate) + 1;
        var values = new float[shifts];
        for (var index = 0; index < shifts; index++)
        {
            var shift = firstShift + index;
            if (Math.Abs((long)shift) >= count / 2)
            {
                continue;
            }

            var value = correlation[(shift + count) % count];
            values[index] = value * value;
        }

        values = SmoothedRoot(values, Math.Max(1, (int)(EchoWindow * rate)));

        var peak = 0;
        for (var index = 1; index < values.Length; index++)
        {
            if (values[index] > values[peak])
            {
                peak = index;
            }
        }

        if (!(values[peak] > 0))
        {
            return null;
        }

        // Everything further than the peak's width from it.
        var width = (int)(PeakWidth * rate);
        var othersCount = 0;
        var sum = 0.0;
        var runnerUp = 0f;
        for (var index = 0; index < values.Length; index++)
        {
            if (index >= peak - width && index <= peak + width)
            {
                continue;
            }

            othersCount++;
            sum += values[index];
            runnerUp = Math.Max(runnerUp, values[index]);
        }

        if (othersCount <= 2)
        {
            return null;
        }

        var mean = sum / othersCount;
        var squares = 0.0;
        for (var index = 0; index < values.Length; index++)
        {
            if (index < peak - width || index > peak + width)
            {
                squares += (values[index] - mean) * (values[index] - mean);
            }
        }

        var deviation = Math.Sqrt(squares / othersCount);
        if (!(deviation > 0) || !(runnerUp > 0))
        {
            return null;
        }

        return new DelayPeak(
            ((firstShift + peak) / rate) + offset,
            (values[peak] - mean) / deviation,
            values[peak] / runnerUp);
    }

    /// <summary>The whole measurement: both recordings in, the delay to save out.</summary>
    /// <returns>Null if no lag stands out clearly: too quiet, a noisy room, or headphones.</returns>
    public static double? Delay(DelayRecording sent, DelayRecording heard, double inputLatency = 0) =>
        Delay(FindPeak(sent, heard, inputLatency));

    /// <summary>The delay to save for a peak, or null if there is none or it isn't clear enough.</summary>
    public static double? Delay(DelayPeak? peak) =>
        peak is { IsClear: true } clear ? AudioDelaySetting.Normalized(clear.Delay) : null;

    /// <summary>Straight-line resampling. Good enough here: the comparison stops at 6 kHz.</summary>
    public static float[] Resample(float[] samples, double rate, double target)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (rate == target || samples.Length <= 1)
        {
            return samples;
        }

        var step = rate / target;
        var count = (int)((samples.Length - 1) / step) + 1;
        var result = new float[count];
        for (var index = 0; index < count; index++)
        {
            var position = index * step;
            var lower = (int)position;
            var fraction = (float)(position - lower);
            var upper = Math.Min(lower + 1, samples.Length - 1);
            result[index] = samples[lower] + ((samples[upper] - samples[lower]) * fraction);
        }

        return result;
    }

    // The cross-correlation of the two signals with every frequency in the band weighted equally.
    // Null if there is nothing to compare, for example silence.
    private static float[]? PhaseCorrelation(float[] sent, float[] heard, int count, double sampleRate)
    {
        var half = count / 2;
        var sentReal = Padded(sent, count);
        var sentImaginary = new float[count];
        var heardReal = Padded(heard, count);
        var heardImaginary = new float[count];
        Transform(sentReal, sentImaginary);
        Transform(heardReal, heardImaginary);

        // heard × conj(sent), scaled to nearly length one inside the band and zero outside it.
        // The upper half of the spectrum mirrors the lower, which makes the result real.
        var binWidth = sampleRate / count;
        var lowest = Math.Max(1, (int)Math.Ceiling(LowestFrequency / binWidth));
        var highest = Math.Min(half - 1, (int)(HighestFrequency / binWidth));
        var found = false;
        var real = new float[count];
        var imaginary = new float[count];
        for (var bin = lowest; bin <= highest; bin++)
        {
            var re = ((double)heardReal[bin] * sentReal[bin]) + ((double)heardImaginary[bin] * sentImaginary[bin]);
            var im = ((double)heardImaginary[bin] * sentReal[bin]) - ((double)heardReal[bin] * sentImaginary[bin]);
            var magnitude = Math.Sqrt((re * re) + (im * im));
            if (!(magnitude > 0))
            {
                continue;
            }

            found = true;
            var weight = 1 / Math.Pow(magnitude, Whitening);
            real[bin] = (float)(re * weight);
            imaginary[bin] = (float)(im * weight);
            real[count - bin] = real[bin];
            imaginary[count - bin] = -imaginary[bin];
        }

        if (!found)
        {
            return null;
        }

        // The inverse transform is the forward one with the imaginary parts negated. Its scale doesn't
        // matter: only the shape of the result is used.
        for (var bin = 0; bin < count; bin++)
        {
            imaginary[bin] = -imaginary[bin];
        }

        Transform(real, imaginary);
        return real;
    }

    private static float[] Padded(float[] samples, int count)
    {
        var padded = new float[count];
        Array.Copy(samples, padded, Math.Min(samples.Length, count));
        return padded;
    }

    // The square root of the average of `squares` over `window` values centred on each one.
    private static float[] SmoothedRoot(float[] squares, int window)
    {
        var sums = new double[squares.Length + 1];
        for (var index = 0; index < squares.Length; index++)
        {
            sums[index + 1] = sums[index] + squares[index];
        }

        var result = new float[squares.Length];
        for (var index = 0; index < squares.Length; index++)
        {
            var first = Math.Max(0, index - (window / 2));
            var last = Math.Min(squares.Length, first + window);
            result[index] = (float)Math.Sqrt((sums[last] - sums[first]) / (last - first));
        }

        return result;
    }

    // An in-place iterative radix-2 FFT. The length must be a power of two.
    private static void Transform(float[] real, float[] imaginary)
    {
        var count = real.Length;
        for (int index = 1, reversed = 0; index < count; index++)
        {
            var bit = count >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
            {
                reversed ^= bit;
            }

            reversed ^= bit;
            if (index < reversed)
            {
                (real[index], real[reversed]) = (real[reversed], real[index]);
                (imaginary[index], imaginary[reversed]) = (imaginary[reversed], imaginary[index]);
            }
        }

        var (cosines, sines) = Twiddles(count);
        for (var size = 2; size <= count; size <<= 1)
        {
            var halfSize = size / 2;
            var stride = count / size;
            for (var start = 0; start < count; start += size)
            {
                for (var k = 0; k < halfSize; k++)
                {
                    var cosine = cosines[k * stride];
                    var sine = sines[k * stride];
                    var upper = start + k + halfSize;
                    var lower = start + k;
                    var re = (real[upper] * cosine) + (imaginary[upper] * sine);
                    var im = (imaginary[upper] * cosine) - (real[upper] * sine);
                    real[upper] = real[lower] - re;
                    imaginary[upper] = imaginary[lower] - im;
                    real[lower] += re;
                    imaginary[lower] += im;
                }
            }
        }
    }

    private static (float[] Cosines, float[] Sines) Twiddles(int count)
    {
        if (s_twiddles is { } known && known.Cosines.Length == count / 2)
        {
            return known;
        }

        var cosines = new float[count / 2];
        var sines = new float[count / 2];
        for (var k = 0; k < cosines.Length; k++)
        {
            var angle = 2 * Math.PI * k / count;
            cosines[k] = (float)Math.Cos(angle);
            sines[k] = (float)Math.Sin(angle);
        }

        s_twiddles = (cosines, sines);
        return (cosines, sines);
    }
}
