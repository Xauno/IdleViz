using System.Diagnostics;
using System.Runtime.InteropServices;
using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using Windows.Win32.System.Variant;

namespace IdleViz.App;

/// <summary>
/// The clock the audio delay is measured on: seconds on the performance counter, which is also
/// the clock Windows stamps captured audio with.
/// </summary>
internal static class AudioClock
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>A time Windows reports with a packet, which is in units of 100 ns.</summary>
    public static double FromPacketTime(ulong time) => time / 10_000_000.0;
}

/// <summary>The speakers or headphones Windows plays through by default.</summary>
/// <param name="Id">The endpoint ID, which stays the same across restarts.</param>
/// <param name="Name">The name as Windows shows it.</param>
/// <param name="ReportedLatency">The latency Windows reports for it, in seconds.</param>
internal sealed record OutputDevice(string Id, string Name, double ReportedLatency);

/// <summary>Windows sound-device lookups for the audio delay: devices, their names and their latency.</summary>
internal static class AudioDevices
{
    /// <summary>32-bit float at 48 kHz. Windows converts to and from whatever the device uses.</summary>
    public const int SampleRate = 48_000;

    public const uint ConvertFlags = PInvoke.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | PInvoke.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;

    public static WAVEFORMATEX FloatFormat(int channels) => new()
    {
        wFormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
        nChannels = (ushort)channels,
        nSamplesPerSec = SampleRate,
        wBitsPerSample = 32,
        nBlockAlign = (ushort)(channels * 4),
        nAvgBytesPerSec = (uint)(SampleRate * channels * 4),
    };

    public static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumerator();

    /// <summary>The default output device, or null if there is none (or Windows can't say).</summary>
    public static OutputDevice? DefaultOutput()
    {
        try
        {
            Enumerator().GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            var id = Id(device);
            return new OutputDevice(id, Name(device) ?? "this device", ReportedLatency(device));
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            // "Element not found" when nothing is plugged in.
            return null;
        }
    }

    /// <summary>Every microphone that is plugged in and switched on.</summary>
    public static IReadOnlyList<Microphone> Microphones()
    {
        var list = new List<Microphone>();
        try
        {
            var enumerator = Enumerator();
            string? defaultId = null;
            try
            {
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eMultimedia, out var defaultDevice);
                defaultId = Id(defaultDevice);
            }
            catch (COMException)
            {
                // No default input.
            }

            enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var devices);
            devices.GetCount(out var count);
            for (uint index = 0; index < count; index++)
            {
                devices.Item(index, out var device);
                var id = Id(device);
                var transport = Core.Microphones.Transport(Property(device, PInvoke.PKEY_Device_EnumeratorName));
                list.Add(new Microphone(id, Name(device) ?? "Microphone", transport, id == defaultId));
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            Log.Info("delay", $"Listing the microphones failed: {error.Message}");
        }

        return list;
    }

    /// <summary>Opens a device for playing or recording. Call on the thread that will use it.</summary>
    public static IAudioClient Client(IMMDevice device)
    {
        device.Activate(typeof(IAudioClient).GUID, CLSCTX.CLSCTX_ALL, null, out var activated);
        return (IAudioClient)activated;
    }

    public static string Id(IMMDevice device)
    {
        device.GetId(out var id);
        try
        {
            return id.ToString();
        }
        finally
        {
            unsafe
            {
                PInvoke.CoTaskMemFree(id.Value);
            }
        }
    }

    private static string? Name(IMMDevice device) => Property(device, PInvoke.PKEY_Device_FriendlyName);

    private static string? Property(IMMDevice device, PROPERTYKEY key)
    {
        device.OpenPropertyStore(STGM.STGM_READ, out var store);
        store.GetValue(key, out var value);
        try
        {
            unsafe
            {
                return value.vt == VARENUM.VT_LPWSTR && value.Anonymous.Anonymous.Anonymous.pwszVal.Value is not null
                    ? value.Anonymous.Anonymous.Anonymous.pwszVal.ToString()
                    : null;
            }
        }
        finally
        {
            PInvoke.PropVariantClear(ref value);
        }
    }

    // What Windows says the device adds on top of its own mixing: the device's buffer and whatever
    // the driver reports. Often too low for Bluetooth, so it's a starting point, not a measurement.
    private static double ReportedLatency(IMMDevice device)
    {
        try
        {
            var client = Client(device);
            try
            {
                var format = FloatFormat(2);
                unsafe
                {
                    client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, ConvertFlags, 0, 0, &format, null);
                }

                client.GetStreamLatency(out var latency);
                return latency / 10_000_000.0;
            }
            finally
            {
                Marshal.ReleaseComObject(client);
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

/// <summary>Tells the UI thread when Windows switches to other speakers or headphones.</summary>
internal sealed class DefaultOutputWatcher : IMMNotificationClient, IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator = AudioDevices.Enumerator();
    private readonly Action _changed;
    private bool _registered;

    /// <param name="changed">Called on one of Windows' own threads.</param>
    public DefaultOutputWatcher(Action changed)
    {
        _changed = changed;
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
            _registered = true;
        }
        catch (COMException error)
        {
            Log.Info("delay", $"Can't follow the default output device: {error.Message}");
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            _registered = false;
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
    }

    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR pwstrDefaultDeviceId)
    {
        if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
        {
            _changed();
        }
    }

    public void OnDeviceStateChanged(PCWSTR pwstrDeviceId, DEVICE_STATE dwNewState)
    {
    }

    public void OnDeviceAdded(PCWSTR pwstrDeviceId)
    {
    }

    public void OnDeviceRemoved(PCWSTR pwstrDeviceId)
    {
    }

    public void OnPropertyValueChanged(PCWSTR pwstrDeviceId, PROPERTYKEY key)
    {
    }
}
