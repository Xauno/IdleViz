namespace IdleViz.Core.Tests;

public class MicrophonesTests
{
    private static readonly Microphone s_builtIn = new("built-in", "Microphone Array (Realtek Audio)", AudioTransport.BuiltIn, IsDefault: false);
    private static readonly Microphone s_usb = new("usb", "Microphone (HD Pro Webcam C920)", AudioTransport.Usb, IsDefault: false);
    private static readonly Microphone s_headset = new("headset", "Headset (WH-1000XM5)", AudioTransport.Bluetooth, IsDefault: false);

    [Fact]
    public void ReadsTheConnectionFromTheDriverFamily()
    {
        Assert.Equal(AudioTransport.Usb, Microphones.Transport("USB"));
        Assert.Equal(AudioTransport.Bluetooth, Microphones.Transport("BTHENUM"));
        Assert.Equal(AudioTransport.Bluetooth, Microphones.Transport("BTHHFENUM"));
        Assert.Equal(AudioTransport.Bluetooth, Microphones.Transport("bthenum"));
        Assert.Equal(AudioTransport.BuiltIn, Microphones.Transport("HDAUDIO"));
        Assert.Equal(AudioTransport.BuiltIn, Microphones.Transport("INTELAUDIO"));
        Assert.Equal(AudioTransport.Virtual, Microphones.Transport("ROOT"));
        Assert.Equal(AudioTransport.Virtual, Microphones.Transport("SWD"));
        Assert.Equal(AudioTransport.Virtual, Microphones.Transport(null));
    }

    [Fact]
    public void TheBuiltInMicrophoneWinsOverTheDefault()
    {
        var choice = Microphones.Choose([s_usb with { IsDefault = true }, s_builtIn], pickedId: null);
        Assert.Equal(s_builtIn, choice.Microphone);
        Assert.Null(choice.Problem);

        // Even over a Bluetooth headset that is the default input.
        Assert.Equal(s_builtIn, Microphones.Choose([s_headset with { IsDefault = true }, s_builtIn], null).Microphone);
    }

    [Fact]
    public void OfTwoBuiltInMicrophonesTheDefaultIsUsed()
    {
        var second = s_builtIn with { Id = "second", IsDefault = true };
        Assert.Equal(second, Microphones.Choose([s_builtIn, second], null).Microphone);
    }

    [Fact]
    public void WithoutABuiltInMicrophoneTheDefaultInputIsUsed()
    {
        var webcam = s_usb with { IsDefault = true };
        Assert.Equal(webcam, Microphones.Choose([s_usb with { Id = "other" }, webcam], null).Microphone);
    }

    [Fact]
    public void ABluetoothDefaultIsRefusedWithTheReason()
    {
        var choice = Microphones.Choose([s_usb, s_headset with { IsDefault = true }], null);
        Assert.Null(choice.Microphone);
        Assert.Equal("Headset (WH-1000XM5) is a Bluetooth microphone. Using it would change the delay. Pick another microphone.", choice.Problem);
    }

    [Fact]
    public void ThePickedMicrophoneIsUsed()
    {
        var choice = Microphones.Choose([s_builtIn, s_usb, s_headset with { IsDefault = true }], "usb");
        Assert.Equal(s_usb, choice.Microphone);
    }

    [Fact]
    public void APickedBluetoothMicrophoneIsRefusedToo()
    {
        var choice = Microphones.Choose([s_builtIn, s_headset], "headset");
        Assert.Null(choice.Microphone);
        Assert.Contains("Bluetooth", choice.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void APickedMicrophoneThatIsGoneCountsAsNotPicked()
    {
        Assert.Equal(s_builtIn, Microphones.Choose([s_builtIn, s_usb], "unplugged").Microphone);
    }

    [Fact]
    public void NoMicrophoneAtAll()
    {
        var choice = Microphones.Choose([], null);
        Assert.Null(choice.Microphone);
        Assert.Equal(Microphones.NoneFound, choice.Problem);

        // Microphones exist, but none is the default and none is built in: nothing says which to use.
        Assert.Equal(Microphones.NoneFound, Microphones.Choose([s_usb], null).Problem);
    }
}
