namespace IdleViz.Core.Tests;

public class CloseReasonTests
{
    [Fact]
    public void InputClosesFasterThanTheLimit()
    {
        Assert.True(CloseReason.Input.FadeSeconds() <= 0.3);
        Assert.True(CloseReason.Input.FadeSeconds() > 0);
        Assert.True(CloseReason.KeepAwakeLimit.FadeSeconds() > CloseReason.Input.FadeSeconds());
    }

    [Fact]
    public void ADisplayChangeClosesAtOnce()
    {
        Assert.Equal(0, CloseReason.DisplayChanged.FadeSeconds());
    }

    [Fact]
    public void OpeningFadesIn()
    {
        Assert.Equal(0.6, VisualizerFade.OpenSeconds);
    }
}
