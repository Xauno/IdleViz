using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace IdleViz.App;

/// <summary>
/// While the window is open: captures Spotify, analyses the newest samples 60 times a second and
/// hands each frame to the page. The analysis runs on its own thread, paced by a high-resolution
/// timer (the ordinary timers tick every 15.6 ms, too coarse for 60 frames); only the call into
/// the page goes through the UI thread. Ported from <c>AudioPump.swift</c>.
/// </summary>
internal sealed class AudioPump : IDisposable
{
    public const double FramesPerSecond = 60;

    // Frames posted to the UI thread that haven't run yet. More means it's busy, so newer frames are dropped.
    private const int MaxQueued = 2;

    private readonly SpotifyCapture _capture = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _spotifyIsPlaying;
    private readonly Action<string> _send;
    private readonly TapHealth _health = new();
    private Thread? _thread;
    private ManualResetEvent? _stop;
    private int _queued;
    private Totals _totals;
    private double _delay;

    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="spotifyIsPlaying">Whether Spotify says it's playing. Read on the UI thread.</param>
    /// <param name="send">Receives each frame's script, on the UI thread.</param>
    public AudioPump(DispatcherQueue dispatcher, Func<bool> spotifyIsPlaying, Action<string> send)
    {
        _dispatcher = dispatcher;
        _spotifyIsPlaying = spotifyIsPlaying;
        _send = send;
    }

    /// <summary>How long each frame waits before it goes to the page, so the visuals match what the speakers play.</summary>
    public double Delay
    {
        get => Volatile.Read(ref _delay);
        set => Volatile.Write(ref _delay, value);
    }

    /// <summary>Starts keeping the capture's signal for Detect delay. The capture runs for this even while the window is closed.</summary>
    public void StartRecording(double seconds)
    {
        _capture.Retain();
        _capture.Ring.StartRecording((int)(seconds * SpotifyCapture.SampleRate));
    }

    /// <summary>Stops and returns what the capture delivered, with the time it handed over the first sample on <see cref="AudioClock"/>.</summary>
    public DelayRecording StopRecording()
    {
        var (samples, start) = _capture.Ring.StopRecording();
        _capture.Release();
        return new DelayRecording(samples, SpotifyCapture.SampleRate, start);
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _capture.Retain();
        // Frames from the last time the window was open are stale; start from silence.
        _send(AudioFrame.Script(AudioFrame.Silence(0, SpotifyCapture.SampleRate).Packed()));
        _capture.Ring.TakeCounts();
        _totals = default;
        var stop = new ManualResetEvent(false);
        _stop = stop;
        _thread = new Thread(() => Run(stop)) { IsBackground = true, Name = "Audio frames", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread is null || _stop is null)
        {
            return;
        }

        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
        _thread = null;
        _stop = null;
        _capture.Release();
        var t = _totals;
        Log.Info("audio", $"While open: {t.Frames} frames, {t.Dropped} dropped; {t.Buffers} packets captured, {t.SilentBuffers} silent; loudest {t.LoudestRms:0.000} at gain {t.Gain:0.0}");
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
    }

    private void Run(ManualResetEvent stop)
    {
        var analyzer = new AudioAnalyzer();
        var left = new float[AudioFrame.SampleCount];
        var right = new float[AudioFrame.SampleCount];
        // Starts empty with every open, so no frame from the last one is shown.
        var delayLine = new DelayLine<byte[]>();
        using var timer = HighResolutionTimer();
        WaitHandle[] handles = timer is null ? [stop] : [stop, timer];
        var clock = Stopwatch.StartNew();
        var interval = 1 / FramesPerSecond;
        var frame = 0L;
        var lastFrame = 0.0;
        var lastHealthCheck = 0.0;
        while (true)
        {
            // Paced from the start, not from the last frame, so the rate doesn't drift.
            var due = ++frame * interval;
            var wait = due - clock.Elapsed.TotalSeconds;
            if (wait < -interval)
            {
                // Fell behind (the PC was busy): skip ahead rather than send a burst.
                frame = (long)(clock.Elapsed.TotalSeconds / interval);
                wait = 0;
            }

            if (timer is not null)
            {
                Arm(timer, wait);
            }

            if (WaitHandle.WaitAny(handles, timer is null ? (int)Math.Ceiling(Math.Max(wait, 0) * 1000) : Timeout.Infinite) == 0)
            {
                return;
            }

            var now = clock.Elapsed.TotalSeconds;
            var seconds = lastFrame == 0 ? (float)interval : (float)Math.Min(now - lastFrame, 0.25);
            lastFrame = now;
            _capture.Ring.Latest(left, right);
            var analysed = analyzer.Analyze(left, right, SpotifyCapture.SampleRate, seconds);
            _totals.LoudestRms = Math.Max(_totals.LoudestRms, analysed.Rms);
            _totals.Gain = analyzer.Gain;
            var time = AudioClock.Now;
            delayLine.Push(analysed.Packed(), time);
            // Until a frame is old enough, the page keeps showing the last one it got.
            if (delayLine.Pop(time, Delay) is { } packed)
            {
                Post(AudioFrame.Script(packed));
            }

            if (now - lastHealthCheck >= 1)
            {
                lastHealthCheck = now;
                var counts = _capture.Ring.TakeCounts();
                _dispatcher.TryEnqueue(() => CheckHealth(counts.Buffers, counts.Zero));
            }
        }
    }

    private void Post(string script)
    {
        if (Interlocked.Increment(ref _queued) > MaxQueued)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _totals.Dropped);
            return;
        }

        var posted = _dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            Interlocked.Decrement(ref _queued);
            // A frame queued just before the window closed isn't sent after it.
            if (_thread is not null)
            {
                _send(script);
                _totals.Frames++;
            }
        });
        if (!posted)
        {
            Interlocked.Decrement(ref _queued);
        }
    }

    // On the UI thread, where Spotify's state lives.
    private void CheckHealth(int buffers, int zeroBuffers)
    {
        if (_thread is null)
        {
            return;
        }

        _totals.Buffers += buffers;
        _totals.SilentBuffers += zeroBuffers;
        if (!_capture.IsCapturing)
        {
            return;
        }

        if (_health.Record(buffers, zeroBuffers, _spotifyIsPlaying()))
        {
            Log.Info("audio", $"Spotify says it's playing, but its audio has been silent for {TapHealth.SuspectAfterSeconds} s: it is playing on another device, or is muted in the Windows mixer");
        }
    }

    // A waitable timer that keeps to the millisecond without raising the whole system's timer rate.
    private static TimerHandle? HighResolutionTimer()
    {
        var handle = PInvoke.CreateWaitableTimerEx(
            null, null, PInvoke.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, (uint)SYNCHRONIZATION_ACCESS_RIGHTS.TIMER_ALL_ACCESS);
        if (handle.IsInvalid)
        {
            Log.Info("audio", "No high-resolution timer; frames may come unevenly");
            return null;
        }

        var timer = new TimerHandle(new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: true));
        handle.SetHandleAsInvalid();
        return timer;
    }

    private static void Arm(TimerHandle timer, double seconds)
    {
        // Negative means relative, in units of 100 ns.
        var due = -(long)(Math.Max(seconds, 0) * 10_000_000);
        unsafe
        {
            PInvoke.SetWaitableTimer(new Windows.Win32.Foundation.HANDLE(timer.SafeWaitHandle.DangerousGetHandle()), &due, 0, null, null, false);
        }
    }

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    private struct Totals
    {
        public int Frames;
        public int Dropped;
        public int Buffers;
        public int SilentBuffers;
        public float LoudestRms;
        public float Gain;
    }
}
