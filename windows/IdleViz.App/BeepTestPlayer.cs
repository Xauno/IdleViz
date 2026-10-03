using System.Runtime.InteropServices;
using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace IdleViz.App;

/// <summary>
/// Plays the manual delay test's beeps on the default output device as one unbroken stream, so
/// each beep sits at an exact sample, and says when the first one was handed to Windows on
/// <see cref="AudioClock"/>. The dialog lights its panel one audio delay later. Runs on its own
/// thread. Plays the part of <c>BeepTestPlayer.swift</c>.
/// </summary>
internal sealed class BeepTestPlayer
{
    private const int Channels = 2;

    // The first beep comes this long after the stream starts.
    private const double LeadSeconds = 0.5;

    private readonly Lock _lock = new();
    private Thread? _thread;
    private ManualResetEvent? _stop;

    // When the first beep is mixed into the device's sound, or NaN while stopped or starting.
    private double _firstBeep = double.NaN;

    /// <summary>When the first beep is sent to the speakers, on <see cref="AudioClock"/>. Null while stopped.</summary>
    public double? FirstBeep
    {
        get
        {
            var time = Volatile.Read(ref _firstBeep);
            return double.IsNaN(time) ? null : time;
        }
    }

    /// <summary>Starts the beeps on the default output device. Fails with a <see cref="COMException"/> if there is none.</summary>
    public Task Start()
    {
        Stop();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            var stop = new ManualResetEvent(false);
            _stop = stop;
            _thread = new Thread(() => Run(stop, started)) { IsBackground = true, Name = "Test beeps", Priority = ThreadPriority.AboveNormal };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        return started.Task;
    }

    public void Stop()
    {
        Thread? thread;
        ManualResetEvent? stop;
        lock (_lock)
        {
            thread = _thread;
            stop = _stop;
            _thread = null;
            _stop = null;
        }

        if (thread is null || stop is null)
        {
            return;
        }

        stop.Set();
        thread.Join(TimeSpan.FromSeconds(2));
        stop.Dispose();
        Volatile.Write(ref _firstBeep, double.NaN);
    }

    private void Run(ManualResetEvent stop, TaskCompletionSource started)
    {
        IAudioClient? client = null;
        try
        {
            AudioDevices.Enumerator().GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            client = AudioDevices.Client(device);
            var format = AudioDevices.FloatFormat(Channels);
            unsafe
            {
                // A 30 ms buffer, kept topped up, so little sits between a sample being written and being mixed.
                client.Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AudioDevices.ConvertFlags, 300_000, 0, &format, null);
            }

            client.GetBufferSize(out var bufferFrames);
            using var ready = new AutoResetEvent(false);
            client.SetEventHandle(new HANDLE(ready.SafeWaitHandle.DangerousGetHandle()));
            object service;
            unsafe
            {
                var iid = typeof(IAudioRenderClient).GUID;
                client.GetService(&iid, out service);
            }

            var render = (IAudioRenderClient)service;
            var beep = BeepTest.Samples(accent: false, AudioDevices.SampleRate);
            var accentBeep = BeepTest.Samples(accent: true, AudioDevices.SampleRate);
            long written = 0;
            // Silence first, so the stream is running before anything has to be on time.
            written += Write(client, render, bufferFrames, written, beep, accentBeep);
            client.Start();
            started.TrySetResult();
            WaitHandle[] handles = [stop, ready];
            while (WaitHandle.WaitAny(handles, 200) != 0)
            {
                written += Write(client, render, bufferFrames, written, beep, accentBeep);
            }

            client.Stop();
            Marshal.ReleaseComObject(render);
        }
        catch (Exception error)
        {
            if (!started.TrySetException(error))
            {
                // Usually the device went away. The controller starts over on the new default device.
                Log.Info("delay", $"The test beeps stopped: {error.Message}");
            }
        }
        finally
        {
            Volatile.Write(ref _firstBeep, double.NaN);
            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    // Fills whatever room the device's buffer has with the next stretch of the stream.
    private unsafe int Write(IAudioClient client, IAudioRenderClient render, uint bufferFrames, long written, float[] beep, float[] accentBeep)
    {
        client.GetCurrentPadding(out var padding);
        var frames = (int)(bufferFrames - padding);
        if (frames <= 0)
        {
            return 0;
        }

        if (written > 0 && double.IsNaN(Volatile.Read(ref _firstBeep)))
        {
            // The stream is running: the frame written next is mixed once the frames still waiting have been,
            // which places the stream's first frame, and with it every beep, on the clock.
            var streamStart = AudioClock.Now + ((double)padding / AudioDevices.SampleRate) - ((double)written / AudioDevices.SampleRate);
            Volatile.Write(ref _firstBeep, streamStart + LeadSeconds);
        }

        render.GetBuffer((uint)frames, out var data);
        var samples = new Span<float>(data, frames * Channels);
        const int Lead = (int)(LeadSeconds * AudioDevices.SampleRate);
        const int Period = (int)(BeepTest.Period * AudioDevices.SampleRate);
        for (var frame = 0; frame < frames; frame++)
        {
            var sinceLead = written + frame - Lead;
            var value = 0f;
            if (sinceLead >= 0)
            {
                var offset = (int)(sinceLead % Period);
                if (offset < beep.Length)
                {
                    value = (BeepTest.IsAccent((int)(sinceLead / Period)) ? accentBeep : beep)[offset];
                }
            }

            samples[frame * Channels] = value;
            samples[(frame * Channels) + 1] = value;
        }

        render.ReleaseBuffer((uint)frames, 0);
        return frames;
    }
}
