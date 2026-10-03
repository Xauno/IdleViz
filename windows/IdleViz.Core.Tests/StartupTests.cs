namespace IdleViz.Core.Tests;

public class StartupTests
{
    private const string Path = @"C:\Users\me\AppData\Local\Programs\IdleViz\IdleViz.exe";

    [Fact]
    public void TheCommandIsThePathInQuotes() =>
        Assert.Equal("\"C:\\Users\\me\\AppData\\Local\\Programs\\IdleViz\\IdleViz.exe\"", StartupEntry.Command(Path));

    [Fact]
    public void NoEntryMeansOff() =>
        Assert.Equal(StartupState.Off, StartupEntry.State(null, null, Path));

    [Fact]
    public void AnEntryForThisCopyMeansOn()
    {
        Assert.Equal(StartupState.On, StartupEntry.State(StartupEntry.Command(Path), null, Path));
        Assert.Equal(StartupState.On, StartupEntry.State(StartupEntry.Command(Path).ToUpperInvariant(), null, Path));
        Assert.Equal(StartupState.On, StartupEntry.State(StartupEntry.Command(Path), [], Path));
    }

    [Fact]
    public void AnEntryForAnotherCopyIsNotThisCopys() =>
        Assert.Equal(StartupState.Off, StartupEntry.State("\"C:\\dev\\IdleViz\\bin\\Debug\\IdleViz.exe\"", null, Path));

    [Theory]
    [InlineData(2, StartupState.On)]
    [InlineData(6, StartupState.On)]
    [InlineData(3, StartupState.DisabledInWindows)]
    [InlineData(7, StartupState.DisabledInWindows)]
    public void WindowsOwnSwitchIsRead(byte first, StartupState expected)
    {
        byte[] approved = [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(expected, StartupEntry.State(StartupEntry.Command(Path), approved, Path));
    }

    [Fact]
    public void WindowsSwitchOnlyMattersWithAnEntry() =>
        Assert.Equal(StartupState.Off, StartupEntry.State(null, [3, 0, 0, 0], Path));
}
