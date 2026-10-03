using System.Globalization;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace IdleViz.App;

/// <summary>
/// The audio delay for the current output device: loads and saves it per device, follows the
/// default output device, and runs Detect delay and the manual delay test. Everything here runs
/// on the UI thread. Ported from <c>AudioDelayController</c> in <c>AudioDelay.swift</c>.
/// </summary>
internal sealed class AudioDelayController : IDisposable
{
    public const double ListeningSeconds = 5;

    /// <summary>The Windows settings page where desktop apps are given the microphone.</summary>
    public static readonly Uri MicrophoneSettings = new("ms-settings:privacy-microphone");

    private readonly DispatcherQueue _dispatcher;
    private readonly SettingsStore _store;
    private readonly AudioPump _pump;
    private readonly Func<bool> _spotifyIsPlaying;
    private readonly Action _pauseSpotify;
    private readonly Action _resumeSpotify;
    private readonly BeepTestPlayer _beeps = new();
    private readonly DefaultOutputWatcher _watcher;
    private string _deviceId = string.Empty;
    private double _delay;
    private bool _pausedSpotify;

    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="store">The settings file.</param>
    /// <param name="pump">Records Spotify's side for Detect delay.</param>
    /// <param name="spotifyIsPlaying">Whether Spotify says it's playing; Detect delay needs music.</param>
    /// <param name="pauseSpotify">Pauses Spotify, for the manual delay test.</param>
    /// <param name="resumeSpotify">Starts it again afterwards.</param>
    public AudioDelayController(
        DispatcherQueue dispatcher, SettingsStore store, AudioPump pump, Func<bool> spotifyIsPlaying, Action pauseSpotify, Action resumeSpotify)
    {
        _dispatcher = dispatcher;
        _store = store;
        _pump = pump;
        _spotifyIsPlaying = spotifyIsPlaying;
        _pauseSpotify = pauseSpotify;
        _resumeSpotify = resumeSpotify;
        LoadDevice();
        _watcher = new DefaultOutputWatcher(() => _dispatcher.TryEnqueue(LoadDevice));
    }

    /// <summary>Raised after anything the settings rows show has changed.</summary>
    public event Action? Changed;

    /// <summary>Raised with the new delay whenever it changes, by hand, by Detect delay or with the device.</summary>
    public event Action<double>? DelayChanged;

    /// <summary>Seconds, on the slider's 10 ms steps. Setting it saves it for the current device.</summary>
    public double Delay
    {
        get => _delay;
        set
        {
            var normalized = AudioDelaySetting.Normalized(value);
            if (normalized == _delay)
            {
                return;
            }

            _delay = normalized;
            Save();
            DelayChanged?.Invoke(_delay);
            Changed?.Invoke();
        }
    }

    /// <summary>The speakers or headphones the delay belongs to.</summary>
    public string DeviceName { get; private set; } = "this device";

    public bool Detecting { get; private set; }

    /// <summary>What the last Detect delay came to, or why it didn't run.</summary>
    public string? Hint { get; private set; }

    /// <summary>Windows doesn't let desktop apps use the microphone; the row links to the settings page.</summary>
    public bool MicrophoneDenied { get; private set; }

    /// <summary>True while the manual delay test is beeping.</summary>
    public bool Testing { get; private set; }

    /// <summary>Why the manual delay test couldn't start, for its dialog.</summary>
    public string? TestProblem { get; private set; }

    /// <summary>The flash the test's panel shows right now, or null while it's dark.</summary>
    public BeepFlash? TestFlash => _beeps.FirstBeep is { } firstBeep ? BeepTest.Flash(AudioClock.Now, firstBeep, _delay) : null;

    /// <summary>The microphone picked in settings, or null when the app chooses.</summary>
    public string? PickedMicrophone
    {
        get => _store.GetString(Microphones.Key);
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _store.Remove(Microphones.Key);
            }
            else
            {
                _store.SetString(Microphones.Key, value);
            }
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        StopTest();
    }

    // Manual delay test

    /// <summary>
    /// Starts the beeps. The dialog lights its panel <see cref="Delay"/> after each one, and the delay
    /// is right when the two land together. Music would drown the beeps out, so Spotify is paused.
    /// </summary>
    public async void StartTest()
    {
        if (Testing || Detecting)
        {
            return;
        }

        TestProblem = null;
        Testing = true;
        Changed?.Invoke();
        if (!await StartBeeps())
        {
            return;
        }

        if (_spotifyIsPlaying())
        {
            _pausedSpotify = true;
            _pauseSpotify();
        }

        Log.Info("delay", $"Manual delay test started for {DeviceName}");
    }

    public void StopTest()
    {
        if (!Testing)
        {
            return;
        }

        _beeps.Stop();
        Testing = false;
        // Save it even when it wasn't moved, so this device counts as measured.
        Save();
        if (_pausedSpotify)
        {
            _resumeSpotify();
        }

        _pausedSpotify = false;
        Log.Info("delay", $"Manual delay test ended at {AudioDelaySetting.Label(_delay)}");
        Changed?.Invoke();
    }

    // Detect delay

    /// <summary>
    /// Listens with a microphone for a few seconds while Spotify plays, and sets the delay from how
    /// far the sound lags behind the audio Spotify sent.
    /// </summary>
    public async void Detect()
    {
        if (Detecting || Testing)
        {
            return;
        }

        MicrophoneDenied = false;
        if (!_spotifyIsPlaying())
        {
            ShowHint("Play something in Spotify first, out loud");
            return;
        }

        var choice = Microphones.Choose(AudioDevices.Microphones(), PickedMicrophone);
        if (choice.Microphone is not { } microphone)
        {
            Log.Info("delay", $"Not detecting: {choice.Problem}");
            ShowHint(choice.Problem);
            return;
        }

        Detecting = true;
        ShowHint(null);
        try
        {
            await Measure(microphone);
        }
        catch (Exception error)
        {
            Log.Info("delay", $"Detect delay failed: {error.Message}");
            Hint = "Couldn't use the microphone";
        }
        finally
        {
            Detecting = false;
            Changed?.Invoke();
        }
    }

    private async Task Measure(Microphone microphone)
    {
        if (!MicrophoneAllowed())
        {
            Log.Info("delay", "Not detecting: Windows doesn't let desktop apps use the microphone");
            MicrophoneDenied = true;
            return;
        }

        using var recorder = new MicrophoneRecorder(microphone.Id, ListeningSeconds + 1);
        _pump.StartRecording(ListeningSeconds + 1);
        try
        {
            await recorder.Start();
        }
        catch (Exception error)
        {
            _pump.StopRecording();
            Log.Info("delay", $"Couldn't start {microphone.Name}: {error.Message}");
            if (error is UnauthorizedAccessException)
            {
                MicrophoneDenied = true;
            }
            else
            {
                Hint = "Couldn't use the microphone";
            }

            return;
        }

        Log.Info("delay", $"Listening with {microphone.Name}");
        await Task.Delay(TimeSpan.FromSeconds(ListeningSeconds));
        // The microphone stops right here. Its audio is only held in memory for the math below.
        var heard = recorder.Stop();
        var sent = _pump.StopRecording();
        var inputLatency = recorder.InputLatency;
        var peak = await Task.Run(() => DelayDetector.FindPeak(sent, heard, inputLatency));
        LogDiagnostics(sent, heard, inputLatency, peak);

        if (DelayDetector.Delay(peak) is { } result)
        {
            Log.Info("delay", $"Detected a delay of {AudioDelaySetting.Label(result)} for {DeviceName}");
            var unchanged = result == _delay;
            Delay = result;
            // Save it even when it matches the reported latency, so this device counts as measured.
            if (unchanged)
            {
                Save();
            }

            Hint = $"Measured {AudioDelaySetting.Label(result)}";
        }
        else if (heard.Samples.All(sample => sample == 0))
        {
            // A switched-off or software-only microphone (a streaming or VR driver's) delivers exact silence.
            Log.Info("delay", $"Couldn't detect the delay: {microphone.Name} delivered only silence");
            Hint = $"{microphone.Name} heard nothing at all. Pick another microphone. Kept {AudioDelaySetting.Label(_delay)}.";
        }
        else
        {
            Log.Info("delay", "Couldn't detect the delay: no lag stands out clearly");
            Hint = $"Couldn't hear the music clearly (too quiet, or headphones). Kept {AudioDelaySetting.Label(_delay)}.";
        }
    }

    // Windows has one switch for all desktop apps, and no prompt.
    private static bool MicrophoneAllowed()
    {
        try
        {
            var status = AppCapability.Create("microphone").CheckAccess();
            return status is not (AppCapabilityAccessStatus.DeniedByUser or AppCapabilityAccessStatus.DeniedBySystem);
        }
        catch (Exception error)
        {
            // Can't be asked: try the microphone, which says so itself if it's blocked.
            Log.Info("delay", $"Can't read the microphone privacy setting: {error.Message}");
            return true;
        }
    }

    // What the two recordings looked like and the best lag between them, whether or not it was clear enough to use.
    private static void LogDiagnostics(DelayRecording sent, DelayRecording heard, double inputLatency, DelayPeak? peak)
    {
        static double Level(float[] samples) =>
            samples.Length == 0 ? double.NegativeInfinity : 10 * Math.Log10((samples.Sum(sample => (double)sample * sample) / samples.Length) + 1e-12);

        var now = AudioClock.Now;
        Log.Info("delay", string.Create(
            CultureInfo.InvariantCulture,
            $"Spotify: {sent.Samples.Length} samples, {Level(sent.Samples):0.0} dB, started {now - sent.Start:0.000} s ago. Microphone: {heard.Samples.Length} samples, {Level(heard.Samples):0.0} dB, started {now - heard.Start:0.000} s ago, latency {inputLatency:0.000} s. Peak: lag {peak?.Delay ?? -1:0.000} s, z-score {peak?.ZScore ?? 0:0.0}, ratio {peak?.Ratio ?? 0:0.00}"));
    }

    // Switches to the default output device's delay: its saved one, or what Windows reports for it.
    private void LoadDevice()
    {
        if (AudioDevices.DefaultOutput() is not { } device)
        {
            return;
        }

        var saved = _store.GetDoubleMap(AudioDelaySetting.Key);
        var sameDevice = device.Id == _deviceId;
        _deviceId = device.Id;
        DeviceName = device.Name;
        if (!sameDevice)
        {
            Hint = null;
            MicrophoneDenied = false;
        }

        _delay = AudioDelaySetting.DelayFor(device.Id, saved, device.ReportedLatency);
        var source = saved.ContainsKey(device.Id) ? "saved" : "reported by Windows";
        Log.Info("delay", $"Output device: {DeviceName}, delay {AudioDelaySetting.Label(_delay)} ({source})");
        // Tell the listeners even if the number is the same as the last device's.
        DelayChanged?.Invoke(_delay);
        Changed?.Invoke();
        if (Testing && !sameDevice)
        {
            // The beeps were playing on the old device. Start over on the new one.
            _ = StartBeeps();
        }
    }

    private async Task<bool> StartBeeps()
    {
        try
        {
            await _beeps.Start();
        }
        catch (Exception error)
        {
            Log.Info("delay", $"Couldn't play the test beeps: {error.Message}");
            _beeps.Stop();
            TestProblem = "Couldn't play the test beeps";
            Changed?.Invoke();
            return false;
        }

        if (!Testing)
        {
            // Stopped while the beeps were starting.
            _beeps.Stop();
            return false;
        }

        return true;
    }

    private void Save()
    {
        if (_deviceId.Length == 0)
        {
            return;
        }

        var saved = new Dictionary<string, double>(_store.GetDoubleMap(AudioDelaySetting.Key), StringComparer.Ordinal) { [_deviceId] = _delay };
        _store.SetDoubleMap(AudioDelaySetting.Key, saved);
    }

    private void ShowHint(string? hint)
    {
        Hint = hint;
        Changed?.Invoke();
    }
}
