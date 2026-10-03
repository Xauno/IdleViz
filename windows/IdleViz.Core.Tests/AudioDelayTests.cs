namespace IdleViz.Core.Tests;

public class AudioDelaySettingTests
{
    [Fact]
    public void NormalizesToTheSlider()
    {
        Assert.Equal(0.18, AudioDelaySetting.Normalized(0.184), 9);
        Assert.Equal(0.19, AudioDelaySetting.Normalized(0.186), 9);
        Assert.Equal(0, AudioDelaySetting.Normalized(-1));
        Assert.Equal(2.5, AudioDelaySetting.Normalized(9));
        Assert.Equal(0, AudioDelaySetting.Normalized(double.NaN));
    }

    [Fact]
    public void ANewDeviceStartsAtItsReportedLatency()
    {
        var saved = new Dictionary<string, double> { ["headphones"] = 0.21 };
        Assert.Equal(0.21, AudioDelaySetting.DelayFor("headphones", saved, 0.15), 9);
        Assert.Equal(0.15, AudioDelaySetting.DelayFor("speaker", saved, 0.1532), 9);
        Assert.Equal(2.5, AudioDelaySetting.DelayFor("network", saved, 4));
        Assert.Equal(0, AudioDelaySetting.DelayFor("wired", saved, 0.004));
    }

    [Fact]
    public void ReadsSavedDelays()
    {
        var store = new SettingsStore();
        Assert.Empty(store.GetDoubleMap(AudioDelaySetting.Key));
        store.SetDoubleMap(AudioDelaySetting.Key, new Dictionary<string, double> { ["a"] = 0.2, ["c"] = 1 });
        Assert.Equal(new Dictionary<string, double> { ["a"] = 0.2, ["c"] = 1 }, store.GetDoubleMap(AudioDelaySetting.Key));
    }

    [Fact]
    public void LabelAndScript()
    {
        Assert.Equal("180 ms", AudioDelaySetting.Label(0.18));
        Assert.Equal("0 ms", AudioDelaySetting.Label(0));
        Assert.Equal("2500 ms", AudioDelaySetting.Label(2.5));
        Assert.Equal("window.setAudioDelay?.(0.25)", AudioDelaySetting.Script(0.25));
        Assert.Equal("window.setAudioDelay?.(2.5)", AudioDelaySetting.Script(99));
        Assert.Equal("window.setAudioDelay?.(0.07)", AudioDelaySetting.Script(0.07));
        Assert.Equal("window.setAudioDelay?.(0)", AudioDelaySetting.Script(0));
    }

    [Fact]
    public void EveryStepIsWrittenWithTwoDecimalsAtMost()
    {
        for (var step = 0; step <= 250; step++)
        {
            var script = AudioDelaySetting.Script(step * AudioDelaySetting.Step);
            Assert.True(script.Length <= "window.setAudioDelay?.(0.00)".Length, script);
        }
    }
}

public class DelayLineTests
{
    private static string Frame(int index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ReleasesEachFrameAfterTheDelay()
    {
        var line = new DelayLine<string>();
        for (var frame = 0; frame < 30; frame++)
        {
            line.Push(Frame(frame), frame / 60.0);
        }

        // At t = 0.5 s with a 0.25 s delay, the frame captured at 0.25 s (frame 15) is due.
        Assert.Equal("15", line.Pop(0.5, 0.25));
        // Nothing new is due at the same moment.
        Assert.Null(line.Pop(0.5, 0.25));
        Assert.Equal("16", line.Pop(0.5 + (1.0 / 60), 0.25));
    }

    [Fact]
    public void NothingIsDueBeforeTheDelayHasPassed()
    {
        var line = new DelayLine<string>();
        line.Push("1", 10);
        Assert.Null(line.Pop(10.1, 0.2));
        Assert.Equal("1", line.Pop(10.2, 0.2));
    }

    [Fact]
    public void ZeroDelayPassesStraightThrough()
    {
        var line = new DelayLine<string>();
        line.Push("7", 3);
        Assert.Equal("7", line.Pop(3, 0));
        Assert.Equal(0, line.Count);
    }

    [Fact]
    public void SkipsFramesWhenSeveralAreDue()
    {
        var line = new DelayLine<string>();
        for (var frame = 0; frame < 10; frame++)
        {
            line.Push(Frame(frame), frame);
        }

        Assert.Equal("5", line.Pop(6, 1));
        Assert.Equal(4, line.Count);
    }

    [Fact]
    public void AShorterDelayCatchesUp()
    {
        var line = new DelayLine<string>();
        for (var frame = 0; frame < 120; frame++)
        {
            line.Push(Frame(frame), frame / 60.0);
        }

        Assert.Equal("30", line.Pop(2, 1.5));
        Assert.Equal("90", line.Pop(2, 0.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.07)]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(2.5)]
    public void AnUnevenTimerDoesNotDropFrames(double delay)
    {
        // 60 calls a second, each up to 2 ms early or late. A delay of a whole number of frames puts
        // every frame right on the edge of being due.
        var line = new DelayLine<string>();
        var released = new List<int>();
        var jitter = new[] { 0.0, 0.0015, -0.002, 0.0007, -0.0012, 0.002, -0.0004 };
        for (var tick = 0; tick < 600; tick++)
        {
            var time = (tick / 60.0) + jitter[tick % jitter.Length];
            line.Push(Frame(tick), time);
            if (line.Pop(time, delay) is { } frame)
            {
                released.Add(int.Parse(frame, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // In order, with none left out.
        Assert.Equal(Enumerable.Range(0, released.Count), released);
        var expected = 600 - (int)Math.Round(delay * 60);
        Assert.InRange(released.Count, expected - 2, expected);
    }

    [Fact]
    public void TwoDueFramesComeOutOnePerCall()
    {
        var line = new DelayLine<string>();
        line.Push("1", 1);
        line.Push("2", 2);
        Assert.Equal("1", line.Pop(3, 0.5));
        Assert.Equal("2", line.Pop(3, 0.5));
        Assert.Null(line.Pop(3, 0.5));
    }

    [Fact]
    public void StaysBounded()
    {
        var line = new DelayLine<string>();
        for (var frame = 0; frame < 1000; frame++)
        {
            line.Push(Frame(frame), frame);
        }

        Assert.Equal(DelayLine<string>.Capacity, line.Count);
        line.Clear();
        Assert.Equal(0, line.Count);
    }
}

public class DelayDetectorTests
{
    private const double Rate = 48_000;

    [Fact]
    public void FindsBluetoothAndNetworkSizedDelays()
    {
        foreach (var delay in new[] { 0.0, 0.03, 0.18, 0.29, 1.0, 2.0, 2.4 })
        {
            var found = Measure(delay);
            Assert.True(found is not null, $"no result for {delay} s");
            Assert.Equal(delay, found.Value, 0.011);
        }
    }

    [Fact]
    public void WorksThroughRoomNoise()
    {
        // Noise at a quarter of the music's level at the microphone.
        Assert.Equal(0.22, Assert.NotNull(Measure(0.22, noise: 0.05f)), 0.011);
    }

    [Fact]
    public void AllowsForRecordingsThatStartAtDifferentTimes()
    {
        Assert.Equal(0.2, Assert.NotNull(Measure(0.2, offset: 0.137)), 0.011);
        Assert.Equal(0.2, Assert.NotNull(Measure(0.2, offset: -0.05)), 0.011);
    }

    [Fact]
    public void SubtractsTheMicrophonesOwnLatency()
    {
        Assert.Equal(0.21, Assert.NotNull(Measure(0.25, inputLatency: 0.04)), 0.011);
    }

    [Fact]
    public void SoundThatComesBeforeTheCaptureHandsItOverCountsAsNoDelay()
    {
        // The microphone hears the music 40 ms before the capture delivers it.
        var sent = Music(42);
        var mic = Heard(sent, 0.06);
        Assert.Equal(0, Assert.NotNull(DelayDetector.Delay(Recording(sent, 100.1), Recording(mic, 100))));
        var peak = Assert.NotNull(DelayDetector.FindPeak(Recording(sent, 100.1), Recording(mic, 100)));
        Assert.Equal(-0.04, peak.Delay, 0.002);
    }

    [Fact]
    public void EchoesDoNotHideThePeak()
    {
        // A speaker across the room: the direct sound, then reflections 35 and 60 ms behind it.
        var sent = Music(42);
        var direct = Heard(sent, 1.95, gain: 0.05f, noise: 0);
        var first = Heard(sent, 1.985, gain: 0.04f, noise: 0);
        var second = Heard(sent, 2.01, gain: 0.03f, noise: 0.01f);
        var mic = direct.Select((sample, index) => sample + first[index] + second[index]).ToArray();
        Assert.Equal(1.95, Assert.NotNull(DelayDetector.Delay(Recording(sent), Recording(mic))), 0.011);
    }

    [Fact]
    public void AMicrophoneAtAnotherSampleRate()
    {
        var sent = Music(7);
        var delayed = Heard(sent, 0.3);
        // Resample 48 kHz to 44.1 kHz by picking the nearest sample; rough, as a real one is never exact.
        const double Ratio = 48_000.0 / 44_100.0;
        var resampled = Enumerable.Range(0, (int)(delayed.Length / Ratio)).Select(index => delayed[(int)(index * Ratio)]).ToArray();
        var found = DelayDetector.Delay(Recording(sent), new DelayRecording(resampled, 44_100));
        Assert.Equal(0.3, Assert.NotNull(found), 0.011);
    }

    [Fact]
    public void GivesUpWhenTheMicrophoneHearsOnlyNoise()
    {
        // Headphones: the music never reaches the microphone.
        var random = new Random(3);
        var sent = Music(42);
        var noise = sent.Select(_ => 0.01f * random.Between(-1, 1)).ToArray();
        Assert.Null(DelayDetector.Delay(Recording(sent), Recording(noise)));
    }

    [Fact]
    public void GivesUpWhenTheMicrophoneHearsDifferentMusic()
    {
        var other = Heard(Music(1234), 0.2);
        Assert.Null(DelayDetector.Delay(Recording(Music(42)), Recording(other)));
    }

    [Fact]
    public void GivesUpOnSilence()
    {
        var zeros = new float[(int)(6 * Rate)];
        Assert.Null(DelayDetector.Delay(Recording(zeros), Recording(zeros)));
        Assert.Null(DelayDetector.Delay(Recording(Music(42)), Recording(zeros)));
    }

    [Fact]
    public void ALoopStillGivesTheRightLag()
    {
        // Half a second of sound repeated exactly lines up nearly as well one repeat later. Both
        // recordings are cut from the middle of a longer stretch, as a real measurement is.
        var random = new Random(5);
        var loop = Enumerable.Range(0, (int)(0.5 * Rate)).Select(_ => random.Between(-0.5f, 0.5f)).ToArray();
        var whole = Enumerable.Range(0, (int)(8 * Rate)).Select(index => loop[index % loop.Length]).ToArray();
        var sent = whole[(int)(1 * Rate)..(int)(7 * Rate)];
        foreach (var (delay, micStart) in new[] { (0.2, 1.0), (0.2, 1.15), (0.31, 0.9), (0.04, 1.3) })
        {
            var mic = Heard(whole, delay)[(int)(micStart * Rate)..(int)(7 * Rate)];
            var found = DelayDetector.Delay(Recording(sent, 1), Recording(mic, micStart));
            Assert.True(found is not null, $"no result for {delay} s, microphone from {micStart} s");
            Assert.Equal(delay, found.Value, 0.011);
        }
    }

    [Fact]
    public void RecordingsThatAreTooShortGiveNothing()
    {
        var brief = Music(42, seconds: 2);
        Assert.Null(DelayDetector.Delay(Recording(brief), Recording(brief)));
        Assert.Null(DelayDetector.FindPeak(Recording([]), Recording([])));
    }

    [Fact]
    public void ResamplingKeepsTheLength()
    {
        var samples = Enumerable.Range(0, 441).Select(index => (float)index).ToArray();
        Assert.Same(samples, DelayDetector.Resample(samples, 48_000, 48_000));
        var up = DelayDetector.Resample(samples, 44_100, 48_000);
        Assert.Equal(479, up.Length);
        Assert.Equal(0, up[0]);
        // A straight line stays a straight line.
        Assert.Equal(100 * 44_100.0 / 48_000, up[100], 3);
    }

    private static double? Measure(double delay, double offset = 0, double inputLatency = 0, float noise = 0.01f)
    {
        var sent = Music(42);
        // A microphone recording that starts `offset` seconds later holds each sound that much earlier.
        var mic = Heard(sent, delay - offset, noise: noise);
        return DelayDetector.Delay(Recording(sent, 100), Recording(mic, 100 + offset), inputLatency);
    }

    private static DelayRecording Recording(float[] samples, double start = 0) => new(samples, Rate, start);

    /// <summary>Six seconds of "music": drum hits at uneven times over a quiet tone.</summary>
    private static float[] Music(ulong seed, double seconds = 6)
    {
        var random = new Random(seed);
        var count = (int)(seconds * Rate);
        var samples = new float[count];
        for (var index = 0; index < count; index++)
        {
            samples[index] = 0.02f * (float)Math.Sin(2 * Math.PI * 220 * index / Rate);
        }

        var time = 0.1;
        while (time < seconds - 0.2)
        {
            var start = (int)(time * Rate);
            var level = random.Between(0.3f, 0.9f);
            for (var offset = 0; offset < (int)(0.08 * Rate) && start + offset < count; offset++)
            {
                var decay = (float)Math.Exp(-offset / (0.02 * Rate));
                samples[start + offset] += level * decay * random.Between(-1, 1);
            }

            time += random.Between(0.12f, 0.55f);
        }

        return samples;
    }

    /// <summary>What a microphone would pick up: later, quieter, a little muffled, with room noise.</summary>
    private static float[] Heard(float[] sent, double delay, float gain = 0.2f, float noise = 0.01f, ulong seed = 9)
    {
        var random = new Random(seed);
        var shift = (int)(delay * Rate);
        var heard = new float[sent.Length];
        var previous = 0f;
        for (var index = shift; index < sent.Length; index++)
        {
            previous = (previous * 0.5f) + (sent[index - shift] * 0.5f);
            heard[index] = previous * gain;
        }

        for (var index = 0; index < heard.Length; index++)
        {
            heard[index] += noise * random.Between(-1, 1);
        }

        return heard;
    }

    /// <summary>A repeatable random source, so the tests don't flake.</summary>
    private sealed class Random(ulong seed)
    {
        private ulong _state = seed;

        public float Between(float low, float high)
        {
            _state = unchecked((_state * 6_364_136_223_846_793_005) + 1_442_695_040_888_963_407);
            // The top 24 bits; the low bits of this kind of generator repeat quickly.
            return low + ((high - low) * ((_state >> 40) / (float)(1 << 24)));
        }
    }
}
