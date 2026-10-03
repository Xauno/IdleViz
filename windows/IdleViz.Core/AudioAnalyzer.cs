namespace IdleViz.Core;

/// <summary>
/// Turns the newest 1024 stereo samples into an <see cref="AudioFrame"/>: automatic gain, a 64-band
/// spectrum with a noise floor and smoothing, and Butterchurn's byte arrays. Ported from
/// <c>AudioAnalyzer.swift</c>, with a plain radix-2 FFT in place of Apple's vDSP.
/// </summary>
public sealed class AudioAnalyzer
{
    // Band levels map this range of decibels to 0 to 1. A full-scale sine is 0 dB.
    private const float FloorDecibels = -65;
    private const float RangeDecibels = 55;

    // How far a band moves towards a higher and a lower value each frame: fast attack, slow release.
    private const float Attack = 0.7f;
    private const float Release = 0.12f;
    private const double LowestHz = 40;
    private const double HighestHz = 16_000;
    private const int Count = AudioFrame.SampleCount;

    private static readonly float[] s_window = HannWindow();
    private static readonly int[] s_bitReversed = BitReversal();
    private static readonly double[] s_cosines = Twiddles(Math.Cos);
    private static readonly double[] s_sines = Twiddles(Math.Sin);

    private readonly AutoGain _gainControl = new();
    private readonly float[] _bands = new float[AudioFrame.BandCount];
    private readonly double[] _real = new double[Count];
    private readonly double[] _imaginary = new double[Count];
    private uint _sequence;

    /// <summary>The gain currently applied, for logging.</summary>
    public float Gain => _gainControl.Gain;

    /// <param name="left">The newest 1024 samples of the left channel, oldest first.</param>
    /// <param name="right">The same for the right channel.</param>
    /// <param name="sampleRate">The capture's sample rate.</param>
    /// <param name="seconds">Time since the last frame, for the automatic gain.</param>
    public AudioFrame Analyze(float[] left, float[] right, double sampleRate, float seconds = 1f / 60)
    {
        if (left.Length != Count || right.Length != Count)
        {
            throw new ArgumentException($"Analyze needs {Count} samples per channel");
        }

        var frame = new AudioFrame { Sequence = _sequence++, SampleRate = (float)sampleRate };
        var mono = frame.Waveform;
        for (var i = 0; i < Count; i++)
        {
            mono[i] = (left[i] + right[i]) * 0.5f;
        }

        var gain = _gainControl.Update(RootMeanSquare(mono), seconds);
        for (var i = 0; i < Count; i++)
        {
            mono[i] = Math.Clamp(mono[i] * gain, -1, 1);
        }

        UpdateBands(mono, sampleRate);
        _bands.CopyTo(frame.Bands, 0);
        frame.Bass = Mean(_bands, 0, 8);
        frame.Mid = Mean(_bands, 8, 30);
        frame.Treble = Mean(_bands, 30, 64);
        frame.Rms = Math.Min(RootMeanSquare(mono), 1);
        WriteBytes(mono, 1, frame.MonoBytes);
        WriteBytes(left, gain, frame.LeftBytes);
        WriteBytes(right, gain, frame.RightBytes);
        return frame;
    }

    /// <summary>Unsigned 8-bit samples around 128, as Web Audio's <c>getByteTimeDomainData</c> would give Butterchurn.</summary>
    public static byte[] Bytes(float[] samples, float gain)
    {
        var bytes = new byte[samples.Length];
        WriteBytes(samples, gain, bytes);
        return bytes;
    }

    /// <summary>Magnitude of each frequency bin, scaled so a full-scale sine reads 1.</summary>
    public float[] Spectrum(float[] mono)
    {
        for (var i = 0; i < Count; i++)
        {
            var j = s_bitReversed[i];
            _real[j] = mono[i] * s_window[i];
            _imaginary[j] = 0;
        }

        Transform(_real, _imaginary);
        // The Hann window halves a sine's peak, so a sine of amplitude A reads A × N / 4.
        // Bin 0 also holds the highest bin, as vDSP's packed real transform does.
        var half = Count / 2;
        var magnitudes = new float[half];
        magnitudes[0] = (float)(Math.Sqrt((_real[0] * _real[0]) + (_real[half] * _real[half])) * 4 / Count);
        for (var k = 1; k < half; k++)
        {
            magnitudes[k] = (float)(Math.Sqrt((_real[k] * _real[k]) + (_imaginary[k] * _imaginary[k])) * 4 / Count);
        }

        return magnitudes;
    }

    /// <summary>
    /// The average magnitude between two fractional bin positions. Low bands are narrower than
    /// one bin, so those read a value interpolated between the two nearest bins.
    /// </summary>
    public static float Magnitude(float[] magnitudes, double low, double high)
    {
        var first = (int)Math.Ceiling(low);
        var last = (int)Math.Floor(high);
        if (last > first)
        {
            return Mean(magnitudes, first, last + 1);
        }

        var centre = (low + high) / 2;
        var below = (int)Math.Floor(centre);
        var above = Math.Min(below + 1, magnitudes.Length - 1);
        var fraction = (float)(centre - below);
        return (magnitudes[below] * (1 - fraction)) + (magnitudes[above] * fraction);
    }

    private void UpdateBands(float[] mono, double sampleRate)
    {
        var magnitudes = Spectrum(mono);
        var binHz = sampleRate / Count;
        var lastBin = (double)((Count / 2) - 1);
        var ratio = HighestHz / LowestHz;
        for (var band = 0; band < AudioFrame.BandCount; band++)
        {
            var low = LowestHz * Math.Pow(ratio, (double)band / AudioFrame.BandCount) / binHz;
            var high = LowestHz * Math.Pow(ratio, (double)(band + 1) / AudioFrame.BandCount) / binHz;
            var magnitude = Magnitude(magnitudes, Math.Min(low, lastBin), Math.Min(high, lastBin));
            var decibels = 20 * MathF.Log10(Math.Max(magnitude, 1e-9f));
            var level = Math.Clamp((decibels - FloorDecibels) / RangeDecibels, 0, 1);
            var previous = _bands[band];
            var next = previous + ((level - previous) * (level > previous ? Attack : Release));
            // Snap to zero so silence becomes exact silence instead of fading forever.
            _bands[band] = next < 0.0005f ? 0 : next;
        }
    }

    private static void WriteBytes(float[] samples, float gain, byte[] bytes)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var scaled = MathF.Round(samples[i] * gain * 128, MidpointRounding.AwayFromZero);
            bytes[i] = (byte)Math.Clamp(scaled + 128, 0, 255);
        }
    }

    private static float RootMeanSquare(float[] samples)
    {
        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static float Mean(float[] values, int start, int end)
    {
        double sum = 0;
        for (var i = start; i < end; i++)
        {
            sum += values[i];
        }

        return (float)(sum / (end - start));
    }

    // An in-place iterative radix-2 FFT on input already in bit-reversed order.
    private static void Transform(double[] real, double[] imaginary)
    {
        for (var size = 2; size <= Count; size *= 2)
        {
            var halfSize = size / 2;
            var step = Count / size;
            for (var start = 0; start < Count; start += size)
            {
                for (var k = 0; k < halfSize; k++)
                {
                    var cos = s_cosines[k * step];
                    var sin = s_sines[k * step];
                    var a = start + k;
                    var b = a + halfSize;
                    var re = (real[b] * cos) + (imaginary[b] * sin);
                    var im = (imaginary[b] * cos) - (real[b] * sin);
                    real[b] = real[a] - re;
                    imaginary[b] = imaginary[a] - im;
                    real[a] += re;
                    imaginary[a] += im;
                }
            }
        }
    }

    // The same window as vDSP's denormalized Hann window: 0.5 × (1 − cos(2πn / N)).
    private static float[] HannWindow()
    {
        var window = new float[Count];
        for (var n = 0; n < Count; n++)
        {
            window[n] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * n / Count)));
        }

        return window;
    }

    private static int[] BitReversal()
    {
        var bits = (int)Math.Log2(Count);
        var table = new int[Count];
        for (var i = 0; i < Count; i++)
        {
            var reversed = 0;
            for (var bit = 0; bit < bits; bit++)
            {
                reversed |= ((i >> bit) & 1) << (bits - 1 - bit);
            }

            table[i] = reversed;
        }

        return table;
    }

    private static double[] Twiddles(Func<double, double> function)
    {
        var table = new double[Count / 2];
        for (var k = 0; k < table.Length; k++)
        {
            table[k] = function(2 * Math.PI * k / Count);
        }

        return table;
    }
}
