using System.Runtime.InteropServices;
using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace IdleViz.App;

/// <summary>
/// Records one microphone for Detect delay. The audio never leaves this object except as the
/// recording handed to the delay math, and nothing is written to disk. Runs on its own thread.
/// Plays the part of <c>MicrophoneRecorder</c> in <c>AudioDelay.swift</c>.
/// </summary>
internal sealed class MicrophoneRecorder : IDisposable
{
    private readonly string _deviceId;
    private readonly float[] _samples;
    private readonly ManualResetEvent _stop = new(false);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private int _recorded;
    private double _start;

    /// <param name="deviceId">The microphone's endpoint ID.</param>
    /// <param name="maxSeconds">Recording stops being kept after this long.</param>
    public MicrophoneRecorder(string deviceId, double maxSeconds)
    {
        _deviceId = deviceId;
        _samples = new float[(int)(maxSeconds * AudioDevices.SampleRate)];
    }

    /// <summary>The microphone's own latency in seconds: sound reaches the recording this much late.</summary>
    public double InputLatency { get; private set; }

    /// <summary>
    /// Starts recording. Fails with <see cref="UnauthorizedAccessException"/> if Windows doesn't let
    /// desktop apps use the microphone, and with a <see cref="COMException"/> if it can't be opened.
    /// </summary>
    public Task Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Microphone", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        return _started.Task;
    }

    /// <summary>Stops the microphone and returns what it heard.</summary>
    public DelayRecording Stop()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        return new DelayRecording(_samples.AsSpan(0, _recorded).ToArray(), AudioDevices.SampleRate, _start);
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    private void Run()
    {
        IAudioClient? client = null;
        try
        {
            AudioDevices.Enumerator().GetDevice(_deviceId, out var device);
            client = AudioDevices.Client(device);
            var format = AudioDevices.FloatFormat(channels: 1);
            unsafe
            {
                client.Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AudioDevices.ConvertFlags, 200_000, 0, &format, null);
            }

            client.GetStreamLatency(out var latency);
            InputLatency = latency / 10_000_000.0;
            using var ready = new AutoResetEvent(false);
            client.SetEventHandle(new HANDLE(ready.SafeWaitHandle.DangerousGetHandle()));
            object service;
            unsafe
            {
                var iid = typeof(IAudioCaptureClient).GUID;
                client.GetService(&iid, out service);
            }

            var capture = (IAudioCaptureClient)service;
            client.Start();
            _started.TrySetResult();
            WaitHandle[] handles = [_stop, ready];
            while (WaitHandle.WaitAny(handles, 200) != 0)
            {
                Drain(capture);
            }

            // The microphone stops right here.
            client.Stop();
            Marshal.ReleaseComObject(capture);
        }
        catch (Exception error)
        {
            // Before the start this is the answer to Start; after it, the recording just ends early.
            if (!_started.TrySetException(error))
            {
                Log.Info("delay", $"The microphone stopped: {error.Message}");
            }
        }
        finally
        {
            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    private unsafe void Drain(IAudioCaptureClient capture)
    {
        capture.GetNextPacketSize(out var packet);
        while (packet > 0)
        {
            capture.GetBuffer(out var data, out var frames, out var flags, out _, out var recordedAt);
            var count = (int)Math.Min(frames, _samples.Length - _recorded);
            if (_recorded == 0 && count > 0)
            {
                // When the first sample was recorded. Without a time from Windows, the packet has just ended.
                _start = recordedAt != 0 ? AudioClock.FromPacketTime(recordedAt) : AudioClock.Now - ((double)frames / AudioDevices.SampleRate);
            }

            if ((flags & (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
            {
                Array.Clear(_samples, _recorded, count);
            }
            else
            {
                new ReadOnlySpan<float>(data, count).CopyTo(_samples.AsSpan(_recorded));
            }

            _recorded += count;
            capture.ReleaseBuffer(frames);
            capture.GetNextPacketSize(out packet);
        }
    }
}
