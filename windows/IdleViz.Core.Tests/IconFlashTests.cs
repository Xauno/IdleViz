namespace IdleViz.Core.Tests;

public class IconFlashTests
{
    [Fact]
    public void ShowsTheSlashedGlyphThreeTimes()
    {
        var steps = Enumerable.Range(0, IconFlash.Steps).Select(IconFlash.IsSlashed);
        Assert.Equal([true, false, true, false, true, false], steps);
    }

    [Fact]
    public void LastsAboutASecond()
    {
        Assert.InRange(IconFlash.Steps * IconFlash.StepMilliseconds, 900, 1100);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    public void IsNormalOutsideTheFlash(int step)
    {
        Assert.False(IconFlash.IsSlashed(step));
    }
}
