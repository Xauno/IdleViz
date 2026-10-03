using System.Buffers.Binary;

namespace IdleViz.Core.Tests;

public class AudioAnalyzerTests
{
    private const double Rate = 48_000;
    private const int Count = AudioFrame.SampleCount;

    [Fact]
    public void SilenceStaysSilent()
    {
        var analyzer = new AudioAnalyzer();
        var frame = Settle(analyzer, Zeros(), Zeros());
        Assert.All(frame.Bands, band => Assert.Equal(0, band));
        Assert.Equal([0f, 0f, 0f, 0f], [frame.Bass, frame.Mid, frame.Treble, frame.Rms]);
        Assert.All(frame.MonoBytes, b => Assert.Equal(128, b));
        Assert.Equal(frame.MonoBytes, frame.LeftBytes);
        Assert.Equal(1, analyzer.Gain);
    }

    [Fact]
    public void BandsFallBackToExactZeroAfterSound()
    {
        var analyzer = new AudioAnalyzer();
        var tone = Sine(1000, 0.3f);
        Settle(analyzer, tone, tone);
        var frame = Settle(analyzer, Zeros(), Zeros(), frames: 240);
        Assert.Equal(0, frame.Bands.Max());
    }

    [Fact]
    public void AToneLightsUpItsBand()
    {
        var analyzer = new AudioAnalyzer();
        var tone = Sine(1000, 0.2f);
        var frame = Settle(analyzer, tone, tone);
        var loudest = Array.IndexOf(frame.Bands, frame.Bands.Max());
        Assert.Equal(Band(1000), loudest);
        Assert.True(frame.Bands[Band(1000)] > 0.8);
        Assert.True(frame.Bands[Band(100)] < 0.1);
        Assert.True(frame.Bands[Band(10_000)] < 0.1);
        // Bands 30–63 start at about 660 Hz, so 1 kHz counts as treble.
        Assert.True(frame.Treble > frame.Bass);
    }

    [Fact]
    public void BassToneReadsAsBass()
    {
        var analyzer = new AudioAnalyzer();
        var tone = Sine(60, 0.2f);
        var frame = Settle(analyzer, tone, tone);
        Assert.True(frame.Bass > 0.3);
        Assert.True(frame.Bass > frame.Mid * 2);
        Assert.True(frame.Treble < 0.05);
    }

    [Fact]
    public void AFullScaleSineOnABinReadsOne()
    {
        // Bin 64 of 512 is 3 kHz at 48 kHz, so the sine fits the window exactly.
        var tone = Enumerable.Range(0, Count).Select(i => (float)Math.Sin(2 * Math.PI * 64 * i / Count)).ToArray();
        var spectrum = new AudioAnalyzer().Spectrum(tone);
        Assert.Equal(1, AudioAnalyzer.Magnitude(spectrum, 63.5, 64.5), 0.01);
    }

    [Fact]
    public void TheTransformMatchesADirectOne()
    {
        // A mix of tones off the bins, checked against the plain sum the FFT stands for.
        var samples = Enumerable.Range(0, Count).Select(i => (float)((0.3 * Math.Sin(i * 0.37)) + (0.1 * Math.Cos(i * 1.9)))).ToArray();
        var spectrum = new AudioAnalyzer().Spectrum(samples);
        foreach (var k in new[] { 3, 60, 300, 511 })
        {
            double re = 0, im = 0;
            for (var n = 0; n < Count; n++)
            {
                var windowed = samples[n] * 0.5 * (1 - Math.Cos(2 * Math.PI * n / Count));
                re += windowed * Math.Cos(2 * Math.PI * k * n / Count);
                im -= windowed * Math.Sin(2 * Math.PI * k * n / Count);
            }

            Assert.Equal(Math.Sqrt((re * re) + (im * im)) * 4 / Count, spectrum[k], 0.0001);
        }
    }

    [Fact]
    public void BandValuesStayInRange()
    {
        var analyzer = new AudioAnalyzer();
        var random = new Random(7);
        for (var i = 0; i < 60; i++)
        {
            var left = Enumerable.Range(0, Count).Select(_ => (random.NextSingle() * 2) - 1).ToArray();
            var right = Enumerable.Range(0, Count).Select(_ => (random.NextSingle() * 2) - 1).ToArray();
            var frame = analyzer.Analyze(left, right, Rate);
            Assert.All(frame.Bands, band => Assert.InRange(band, 0, 1));
            Assert.All(frame.Waveform, sample => Assert.InRange(sample, -1, 1));
            Assert.InRange(frame.Rms, 0, 1);
        }
    }

    [Fact]
    public void QuietAudioIsBroughtUp()
    {
        // A minute, since the gain turns up slowly.
        var loudFrame = Settle(new AudioAnalyzer(), Sine(440, 0.3f), Sine(440, 0.3f), frames: 3600);
        var quietFrame = Settle(new AudioAnalyzer(), Sine(440, 0.02f), Sine(440, 0.02f), frames: 3600);
        Assert.Equal(AutoGain.Target, loudFrame.Rms, 0.02);
        Assert.Equal(AutoGain.Target, quietFrame.Rms, 0.02);
        Assert.Equal(loudFrame.Bands[Band(440)], quietFrame.Bands[Band(440)], 0.05);
    }

    [Fact]
    public void ChannelsKeepTheirOwnSamples()
    {
        var frame = new AudioAnalyzer().Analyze(Sine(440, 0.28f), Zeros(), Rate);
        Assert.All(frame.RightBytes, b => Assert.Equal(128, b));
        Assert.NotEqual(frame.LeftBytes, frame.RightBytes);
        Assert.True(frame.LeftBytes.Max() > 150);
    }

    [Fact]
    public void BytesClampAtFullScale()
    {
        Assert.Equal([128, 255, 0, 192, 255, 0], AudioAnalyzer.Bytes([0, 1, -1, 0.5f, 3, -3], 1));
        Assert.Equal([192], AudioAnalyzer.Bytes([0.25f], 2));
    }

    [Fact]
    public void SequenceCountsUp()
    {
        var analyzer = new AudioAnalyzer();
        var sequences = Enumerable.Range(0, 3).Select(_ => analyzer.Analyze(Zeros(), Zeros(), Rate).Sequence).ToArray();
        Assert.Equal([0u, 1u, 2u], sequences);
    }

    private static float[] Zeros() => new float[Count];

    private static float[] Sine(double hertz, float amplitude) =>
        Enumerable.Range(0, Count).Select(i => amplitude * (float)Math.Sin(2 * Math.PI * hertz * i / Rate)).ToArray();

    private static int Band(double hertz) => (int)(AudioFrame.BandCount * Math.Log(hertz / 40) / Math.Log(400));

    // Runs enough frames for the band smoothing and the gain to settle.
    private static AudioFrame Settle(AudioAnalyzer analyzer, float[] left, float[] right, int frames = 120)
    {
        var frame = analyzer.Analyze(left, right, Rate);
        for (var i = 1; i < frames; i++)
        {
            frame = analyzer.Analyze(left, right, Rate);
        }

        return frame;
    }
}

public class AudioFrameTests
{
    [Fact]
    public void PackedLayout()
    {
        var frame = AudioFrame.Silence(sequence: 7, sampleRate: 44_100);
        frame.Bass = 0.25f;
        frame.Rms = 0.5f;
        frame.Bands[63] = 1;
        frame.Waveform[0] = -1;
        frame.RightBytes[1023] = 200;
        var data = frame.Packed();
        Assert.Equal(AudioFrame.ByteLength, data.Length);
        Assert.Equal(7448, data.Length);

        float At(int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(data));
        Assert.Equal(44_100, At(4));
        Assert.Equal(0.25f, At(8));
        Assert.Equal(0.5f, At(20));
        Assert.Equal(1, At(24 + (63 * 4)));
        Assert.Equal(-1, At(280));
        Assert.Equal(128, data[4376]);
        Assert.Equal(200, data[7447]);
    }

    [Fact]
    public void ScriptIsASafeCall() =>
        Assert.Equal("window.audioFrame?.('//4nXA==')", AudioFrame.Script([0xFF, 0xFE, 0x27, 0x5C]));
}

public class AutoGainTests
{
    private const float Tick = 1f / 60;

    [Fact]
    public void HoldsTheGainThroughSilence()
    {
        var gain = Run(new AutoGain(), 0.05f, 3600);
        var settled = gain.Gain;
        Assert.Equal(4, settled, 0.1);
        for (var i = 0; i < 600; i++)
        {
            Assert.Equal(settled, gain.Update(0, Tick));
        }

        Assert.Equal(settled, gain.Update(0.0005f, Tick));
    }

    [Fact]
    public void StaysWithinLimits()
    {
        Assert.Equal(AutoGain.MaxGain, Run(new AutoGain(), 0.002f, 6000).Gain);
        Assert.Equal(AutoGain.MinGain, Run(new AutoGain(), 0.9f, 600).Gain);
    }

    [Fact]
    public void StartsAtUnityAndDoesNotJumpOnAFadeIn()
    {
        var gain = new AutoGain();
        Assert.Equal(1, gain.Gain);
        // The first frames after pressing play are a quiet fade-in.
        Assert.True(Run(gain, 0.01f, 6).Gain < 1.05);
    }

    [Fact]
    public void TurnsDownQuicklyAndUpSlowly()
    {
        var gain = Run(new AutoGain(), 0.05f, 3600);
        // One second of loud audio is enough to come most of the way down.
        Assert.True(Run(gain, 0.4f, 60).Gain < 0.7);
        // One second of quiet audio barely raises it again.
        Assert.True(Run(gain, 0.05f, 60).Gain < 0.8);
    }

    [Fact]
    public void IgnoresBadInput()
    {
        var gain = new AutoGain();
        Assert.Equal(1, gain.Update(float.NaN, Tick));
        Assert.Equal(1, gain.Update(float.PositiveInfinity, Tick));
    }

    private static AutoGain Run(AutoGain gain, float rms, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            gain.Update(rms, Tick);
        }

        return gain;
    }
}

public class SampleRingTests
{
    [Fact]
    public void ReturnsTheNewestSamplesOldestFirst()
    {
        var ring = new SampleRing();
        ring.Append([1, -1, 2, -2, 3, -3], channels: 2, frames: 3);
        var (left, right) = Latest(ring, 2);
        Assert.Equal([2f, 3f], left);
        Assert.Equal([-2f, -3f], right);
        Assert.Equal([0f, 1f, 2f, 3f], Latest(ring, 4).Left);
    }

    [Fact]
    public void WrapsAround()
    {
        var ring = new SampleRing();
        var samples = Enumerable.Range(0, SampleRing.Capacity + 10).SelectMany(i => new[] { (float)i, (float)i }).ToArray();
        ring.Append(samples, channels: 2, frames: SampleRing.Capacity + 10);
        Assert.Equal([4103f, 4104f, 4105f], Latest(ring, 3).Left);
    }

    [Fact]
    public void MonoGoesToBothSides()
    {
        var ring = new SampleRing();
        ring.Append([0.5f, 0.25f], channels: 1, frames: 2);
        var (left, right) = Latest(ring, 2);
        Assert.Equal([0.5f, 0.25f], left);
        Assert.Equal([0.5f, 0.25f], right);
    }

    [Fact]
    public void CountsBuffersAndZeroBuffers()
    {
        var ring = new SampleRing();
        ring.Append([0, 0, 0, 0], channels: 2, frames: 2);
        ring.Append([0, 0.1f], channels: 2, frames: 1);
        ring.AppendSilence(2);
        Assert.Equal(3, ring.TakeCounts().Buffers);
        ring.AppendSilence(1);
        Assert.Equal((1, 1), ring.TakeCounts());
    }

    [Fact]
    public void SilentPacketsWriteZeros()
    {
        var ring = new SampleRing();
        ring.Append([1, 1, 1, 1], channels: 2, frames: 2);
        ring.AppendSilence(1);
        Assert.Equal([1f, 0f], Latest(ring, 2).Left);
    }

    [Fact]
    public void ClearLeavesSilence()
    {
        var ring = new SampleRing();
        ring.Append([1, 1, 1, 1], channels: 2, frames: 2);
        ring.Clear();
        Assert.Equal([0f, 0f, 0f, 0f], Latest(ring, 4).Left);
    }

    private static (float[] Left, float[] Right) Latest(SampleRing ring, int count)
    {
        var left = Enumerable.Repeat(-9f, count).ToArray();
        var right = Enumerable.Repeat(-9f, count).ToArray();
        ring.Latest(left, right);
        return (left, right);
    }
}

public class TapHealthTests
{
    [Fact]
    public void SuspectsAfterFiveSilentSecondsWhilePlaying()
    {
        var health = new TapHealth();
        for (var i = 0; i < 4; i++)
        {
            Assert.False(health.Record(90, 90, spotifyPlaying: true));
        }

        Assert.True(health.Record(90, 90, spotifyPlaying: true));
        Assert.True(health.Suspected);
        // Reported once, not every second.
        Assert.False(health.Record(90, 90, spotifyPlaying: true));
    }

    [Fact]
    public void NoBuffersAtAllCountsToo()
    {
        var health = new TapHealth();
        var results = Enumerable.Range(0, 5).Select(_ => health.Record(0, 0, spotifyPlaying: true)).ToArray();
        Assert.Equal([false, false, false, false, true], results);
    }

    [Fact]
    public void PausedTimeDoesNotCount()
    {
        var health = new TapHealth();
        for (var i = 0; i < 20; i++)
        {
            Assert.False(health.Record(90, 90, spotifyPlaying: false));
        }

        for (var i = 0; i < 4; i++)
        {
            health.Record(90, 90, spotifyPlaying: true);
        }

        Assert.False(health.Record(90, 90, spotifyPlaying: false));
        Assert.False(health.Record(90, 90, spotifyPlaying: true));
    }

    [Fact]
    public void RealAudioClearsTheSuspicion()
    {
        var health = new TapHealth();
        for (var i = 0; i < 5; i++)
        {
            health.Record(90, 90, spotifyPlaying: true);
        }

        Assert.False(health.Record(90, 12, spotifyPlaying: true));
        Assert.False(health.Suspected);
    }
}
