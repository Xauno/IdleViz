namespace IdleViz.Core.Tests;

public class BeepTestTests
{
    [Fact]
    public void ThePanelLightsOneDelayAfterEachBeep()
    {
        // Beeps at 10, 11, 12… with a delay of 0.25 s: lit from 10.25 for 0.12 s.
        Assert.Null(BeepTest.Flash(10.2, 10, 0.25));
        Assert.NotNull(BeepTest.Flash(10.25, 10, 0.25));
        Assert.NotNull(BeepTest.Flash(10.36, 10, 0.25));
        Assert.Null(BeepTest.Flash(10.38, 10, 0.25));
        Assert.NotNull(BeepTest.Flash(11.3, 10, 0.25));
        Assert.Null(BeepTest.Flash(11.9, 10, 0.25));
    }

    [Fact]
    public void WithNoDelayThePanelLightsWithTheBeep()
    {
        Assert.NotNull(BeepTest.Flash(5, 5, 0));
        Assert.Null(BeepTest.Flash(4.99, 5, 0));
    }

    [Fact]
    public void NothingLightsBeforeTheFirstBeepIsDue()
    {
        Assert.Null(BeepTest.Flash(9, 10, 0));
        // The first beep's flash is still 2 s away, though later beeps have sounded.
        Assert.Null(BeepTest.Flash(11.05, 10, 2));
    }

    [Fact]
    public void EveryFourthFlashIsMarked()
    {
        var accents = Enumerable.Range(0, 9).Select(index => BeepTest.Flash(10.05 + index, 10, 0)?.Accent).ToArray();
        Assert.Equal([true, false, false, false, true, false, false, false, true], accents);
    }

    [Fact]
    public void AFlashKeepsItsBeepsMarkWithALongDelay()
    {
        // With a 2.3 s delay the flash at 12.35 belongs to beep 0, the marked one, not to beep 2.
        Assert.Equal(new BeepFlash(true), BeepTest.Flash(12.35, 10, 2.3));
        Assert.Equal(new BeepFlash(false), BeepTest.Flash(13.35, 10, 2.3));
        Assert.Equal(new BeepFlash(true), BeepTest.Flash(16.35, 10, 2.3));
    }

    [Fact]
    public void BeepTimes()
    {
        Assert.Equal(10, BeepTest.BeepTime(0, 10));
        Assert.Equal(17, BeepTest.BeepTime(7, 10));
        Assert.True(BeepTest.IsAccent(0));
        Assert.False(BeepTest.IsAccent(3));
        Assert.True(BeepTest.IsAccent(8));
    }

    [Fact]
    public void ABeepIsAShortToneThatStartsAndEndsSilent()
    {
        var samples = BeepTest.Samples(accent: false, sampleRate: 48_000);
        Assert.Equal(2880, samples.Length);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1], 0.001f);
        Assert.Equal(0.4f, samples.Max(Math.Abs), 0.01f);
        Assert.Empty(BeepTest.Samples(accent: true, sampleRate: 0));
    }

    [Fact]
    public void TheMarkedBeepIsAnOctaveHigher()
    {
        static int Crossings(float[] samples) => samples.Zip(samples.Skip(1)).Count(pair => pair.First < 0 && pair.Second >= 0);

        // 880 Hz and 1760 Hz for 60 ms.
        Assert.Equal(52.8, Crossings(BeepTest.Samples(accent: false, sampleRate: 48_000)), 1.5);
        Assert.Equal(105.6, Crossings(BeepTest.Samples(accent: true, sampleRate: 48_000)), 1.5);
    }
}
