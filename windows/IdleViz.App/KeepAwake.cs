using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace IdleViz.App;

/// <summary>
/// Keeps the display awake while the visualizer is open, up to a time limit counted from when it
/// opened. After the limit the PC goes back to its normal sleep, screen saver and lock. Ported
/// from <c>KeepAwake.swift</c>.
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private readonly Func<double> _limit;
    private readonly Action _onLimit;
    private readonly DispatcherQueueTimer _timer;
    private SafeFileHandle? _request;
    private double? _openedAt;
    private double _scheduledLimit;

    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="limit">The limit that applies right now, in seconds. It depends on the settings and the power source.</param>
    /// <param name="onLimit">Called once when the limit is reached.</param>
    public KeepAwake(DispatcherQueue dispatcher, Func<double> limit, Action onLimit)
    {
        _limit = limit;
        _onLimit = onLimit;
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Check();
    }

    /// <summary>True while the display is being kept awake.</summary>
    public bool IsActive => _openedAt is not null;

    public void Start()
    {
        if (_openedAt is not null)
        {
            return;
        }

        _openedAt = Now;
        _request = CreateRequest();
        // A display request also stops the screen saver and the lock that follows it.
        if (_request is null || !PInvoke.PowerSetRequest(_request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired))
        {
            Log.Info("keepawake", $"Couldn't keep the display awake (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
        }

        Schedule();
    }

    public void Stop()
    {
        if (_openedAt is null)
        {
            return;
        }

        _openedAt = null;
        _timer.Stop();
        if (_request is not null)
        {
            PInvoke.PowerClearRequest(_request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);
            _request.Dispose();
            _request = null;
        }
    }

    /// <summary>
    /// Call when the setting or the power source changed. The new limit still counts from when the
    /// window opened, so a shorter one may already be over.
    /// </summary>
    public void LimitMayHaveChanged()
    {
        if (_openedAt is not null && _limit() != _scheduledLimit)
        {
            Schedule();
        }
    }

    public void Dispose() => Stop();

    // Counts time asleep too, which doesn't matter: going to sleep closes the visualizer.
    private static double Now => Environment.TickCount64 / 1000.0;

    private void Schedule()
    {
        _scheduledLimit = _limit();
        Check();
    }

    private void Check()
    {
        _timer.Stop();
        if (_openedAt is not { } openedAt)
        {
            return;
        }

        var remaining = TimingSettings.Remaining(_scheduledLimit, openedAt, Now);
        if (remaining <= 0)
        {
            Log.Info("keepawake", $"Keep-awake limit of {_scheduledLimit / 60:0.#} min reached");
            _onLimit();
            return;
        }

        _timer.Interval = TimeSpan.FromSeconds(remaining);
        _timer.Start();
    }

    // The reason shows in "powercfg /requests".
    private static unsafe SafeFileHandle? CreateRequest()
    {
        fixed (char* reason = "IdleViz visualizer")
        {
            var context = new REASON_CONTEXT
            {
                Version = 0, // POWER_REQUEST_CONTEXT_VERSION
                Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            };
            context.Reason.SimpleReasonString = reason;
            var request = PInvoke.PowerCreateRequest(context);
            return request.IsInvalid ? null : request;
        }
    }
}
