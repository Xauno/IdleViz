using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>Opens and closes the visualizer window: fades, focus, the cursor and the dismiss watcher.</summary>
internal sealed class VisualizerController : IDisposable
{
    private enum State
    {
        Closed,
        Open,
        Closing,
    }

    private readonly VisualizerWindow _window = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _fadeTimer;
    private readonly DispatcherQueueTimer _cursorTimer;
    private readonly Stopwatch _fadeClock = new();
    private readonly bool _dismissEnabled;
    private State _state = State.Closed;
    private DismissWatcher? _dismissWatcher;
    private HWND _previousWindow;
    private double _fadeFrom;
    private double _fadeTo;
    private double _fadeSeconds;
    private double _opacity;

    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="dismissEnabled">False keeps the window open on input, for inspecting it (Debug builds).</param>
    public VisualizerController(DispatcherQueue dispatcher, bool dismissEnabled)
    {
        _dispatcher = dispatcher;
        _dismissEnabled = dismissEnabled;
        _fadeTimer = dispatcher.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(8);
        _fadeTimer.Tick += (_, _) => OnFadeTick();
        _cursorTimer = dispatcher.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(50);
        _cursorTimer.Tick += (_, _) => _window.HideRestingCursor();
    }

    /// <summary>False again as soon as it starts to fade out.</summary>
    public bool IsOpen => _state == State.Open;

    public void Open(TriggerSource source)
    {
        if (_state == State.Open)
        {
            // Nothing else can close it in the no-dismiss mode, so a second trigger does.
            if (!_dismissEnabled)
            {
                Close(CloseReason.Input);
            }

            return;
        }

        // Triggered again while it fades out: finish that close first, so everything starts clean.
        if (_state == State.Closing)
        {
            FinishClosing();
        }

        _previousWindow = PInvoke.GetForegroundWindow();
        _window.SetClickThrough(false);
        _window.HidesCursor = _dismissEnabled;
        SetOpacity(0);
        _window.Show();
        var focused = _window.TakeFocus();
        _state = State.Open;
        _cursorTimer.Start();
        Fade(to: 1, seconds: VisualizerFade.OpenSeconds);
        Log.Info("window", $"Opened via {source}: {(focused ? "has focus" : "focus refused")}");

        if (_dismissEnabled)
        {
            _dismissWatcher = new DismissWatcher(_dispatcher, () => Close(CloseReason.Input));
            _dismissWatcher.Start();
        }
    }

    /// <summary>Hands the PC back at once (cursor, focus, clicks) and fades the window out on top of it.</summary>
    public void Close(CloseReason reason)
    {
        // A close that can't wait cuts a fade-out short.
        if (_state == State.Closing && reason.FadeSeconds() == 0)
        {
            FinishClosing();
        }

        if (_state != State.Open)
        {
            return;
        }

        _state = State.Closing;
        Log.Info("window", $"Closing: {reason}");
        _dismissWatcher?.Dispose();
        _dismissWatcher = null;
        _window.SetClickThrough(true);
        _window.HidesCursor = false;
        _cursorTimer.Stop();

        // Only if focus is still here: someone may have switched apps while it was open (no-dismiss mode).
        if (PInvoke.GetForegroundWindow() == _window.Handle && PInvoke.IsWindow(_previousWindow))
        {
            PInvoke.SetForegroundWindow(_previousWindow);
        }

        _previousWindow = HWND.Null;

        if (reason.FadeSeconds() > 0)
        {
            Fade(to: 0, seconds: reason.FadeSeconds());
        }
        else
        {
            FinishClosing();
        }
    }

    public void Dispose()
    {
        _fadeTimer.Stop();
        _cursorTimer.Stop();
        _dismissWatcher?.Dispose();
        _window.Dispose();
    }

    private void FinishClosing()
    {
        if (_state != State.Closing)
        {
            return;
        }

        _fadeTimer.Stop();
        _window.Hide();
        _state = State.Closed;
    }

    private void Fade(double to, double seconds)
    {
        _fadeFrom = _opacity;
        _fadeTo = to;
        _fadeSeconds = seconds;
        _fadeClock.Restart();
        _fadeTimer.Start();
    }

    private void OnFadeTick()
    {
        var progress = Math.Min(1, _fadeClock.Elapsed.TotalSeconds / _fadeSeconds);
        // Ease in and out, like the Mac's window fade.
        var eased = progress < 0.5 ? 2 * progress * progress : 1 - (Math.Pow((-2 * progress) + 2, 2) / 2);
        SetOpacity(_fadeFrom + ((_fadeTo - _fadeFrom) * eased));
        if (progress < 1)
        {
            return;
        }

        _fadeTimer.Stop();
        FinishClosing();
    }

    private void SetOpacity(double opacity)
    {
        _opacity = opacity;
        _window.SetOpacity(opacity);
    }
}
