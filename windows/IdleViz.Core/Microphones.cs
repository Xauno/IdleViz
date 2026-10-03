namespace IdleViz.Core;

/// <summary>How a sound device is connected to the PC.</summary>
public enum AudioTransport
{
    /// <summary>The PC's own sound hardware.</summary>
    BuiltIn,
    Usb,
    Bluetooth,

    /// <summary>A device that exists only in software, such as a streaming or VR driver's.</summary>
    Virtual,
}

/// <summary>A microphone Windows lists as usable.</summary>
/// <param name="Id">The endpoint ID Windows gives it.</param>
/// <param name="Name">Its name as Windows shows it, for example "Microphone (HD Pro Webcam C920)".</param>
/// <param name="Transport">How it is connected.</param>
/// <param name="IsDefault">Whether it is the default input device.</param>
public sealed record Microphone(string Id, string Name, AudioTransport Transport, bool IsDefault);

/// <summary>The microphone Detect delay should use, or why it can't run.</summary>
public readonly record struct MicrophoneChoice(Microphone? Microphone, string? Problem);

/// <summary>
/// Which microphone Detect delay records with. On its own it takes the PC's built-in microphone,
/// else the default input, but never a Bluetooth one: recording from a Bluetooth headset's own
/// microphone switches it to call mode and changes the delay being measured. The microphone row
/// in settings can name one instead.
/// </summary>
public static class Microphones
{
    /// <summary>The ID of the microphone picked in settings. Not set means the app chooses.</summary>
    public const string Key = "detectDelayMicrophone";

    public const string NoneFound = "No microphone found";

    /// <summary>The connection, from the name of the Windows driver family the device belongs to ("USB", "BTHENUM", "HDAUDIO").</summary>
    public static AudioTransport Transport(string? enumerator)
    {
        var name = (enumerator ?? string.Empty).Trim().ToUpperInvariant();
        if (name.StartsWith("BTH", StringComparison.Ordinal))
        {
            return AudioTransport.Bluetooth;
        }

        return name switch
        {
            "USB" => AudioTransport.Usb,
            "ROOT" or "SWD" or "" => AudioTransport.Virtual,
            _ => AudioTransport.BuiltIn,
        };
    }

    /// <param name="all">Every active microphone.</param>
    /// <param name="pickedId">The one picked in settings, or null. A picked one that is gone counts as not picked.</param>
    public static MicrophoneChoice Choose(IReadOnlyList<Microphone> all, string? pickedId)
    {
        ArgumentNullException.ThrowIfNull(all);
        var chosen = all.FirstOrDefault(microphone => microphone.Id == pickedId)
            ?? all.FirstOrDefault(microphone => microphone.Transport == AudioTransport.BuiltIn && microphone.IsDefault)
            ?? all.FirstOrDefault(microphone => microphone.Transport == AudioTransport.BuiltIn)
            ?? all.FirstOrDefault(microphone => microphone.IsDefault);
        if (chosen is null)
        {
            return new MicrophoneChoice(null, NoneFound);
        }

        return chosen.Transport == AudioTransport.Bluetooth
            ? new MicrophoneChoice(null, BluetoothProblem(chosen.Name))
            : new MicrophoneChoice(chosen, null);
    }

    public static string BluetoothProblem(string name) =>
        $"{name} is a Bluetooth microphone. Using it would change the delay. Pick another microphone.";
}
