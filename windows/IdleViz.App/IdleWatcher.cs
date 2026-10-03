using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// Fires the idle trigger. Each check is scheduled for the earliest moment the timeout could be
/// reached (see <see cref="IdleScheduler"/>), so there is no fixed polling interval. Ported from
/// <c>IdleWatcher.swift</c>.
/// </summary>
internal sealed class IdleWatcher : IDisposable
{
    // Peak meters show the loudest sample of the last few milliseconds, so they are read a few
    // times: a gap between two words of a call shouldn't count as silence.
    private const int SoundSamples = 6;
    private const int SoundSampleMilliseconds = 50;

    private readonly IdleScheduler _scheduler;
    private readonly Func<double?> _timeout;
    private readonly Action _onIdle;
    private readonly HotkeyWindow _session;
    private readonly DispatcherQueueTimer _timer;
    private bool _checkingSkips;

    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="timeout">The timeout that applies right now, in seconds, or null for off.</param>
    /// <param name="session">Tells when the session is locked or unlocked and when the PC wakes.</param>
    /// <param name="onIdle">Called when the idle trigger fires and no skip rule blocks it.</param>
    public IdleWatcher(DispatcherQueue dispatcher, Func<double?> timeout, HotkeyWindow session, Action onIdle)
    {
        _timeout = timeout;
        _onIdle = onIdle;
        _session = session;
        _scheduler = new IdleScheduler(timeout());
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Check();
        // The timer doesn't count time asleep or locked the way idle time does, so check again then.
        session.Resumed += Reschedule;
        session.LockChanged += _ => Reschedule();
    }

    public void Start()
    {
        Log.Info("idle", Describe(_scheduler.TimeoutSeconds));
        Reschedule();
    }

    /// <summary>Call when the setting changed.</summary>
    public void TimeoutMayHaveChanged()
    {
        var timeout = _timeout();
        if (timeout == _scheduler.TimeoutSeconds)
        {
            return;
        }

        _scheduler.TimeoutSeconds = timeout;
        Log.Info("idle", Describe(timeout));
        Reschedule();
    }

    /// <summary>After the keep-awake limit closed the visualizer the PC is still idle. Don't open again until there is input.</summary>
    public void WaitForInput()
    {
        _scheduler.WaitForInput(Now, SecondsSinceLastInput());
        Reschedule();
    }

    public void Dispose()
    {
        _timer.Stop();
        _session.Resumed -= Reschedule;
    }

    private static double Now => Environment.TickCount64 / 1000.0;

    private static string Describe(double? timeout) =>
        timeout is { } seconds ? $"Opens after {seconds / 60:0.#} min without input" : "The idle trigger is off";

    private void Reschedule()
    {
        _timer.Stop();
        Check();
    }

    private void Check()
    {
        var step = _scheduler.Check(Now, SecondsSinceLastInput());
        if (step.Fire)
        {
            Fire();
            step = _scheduler.Check(Now, SecondsSinceLastInput());
        }

        if (step.WaitSeconds is { } wait)
        {
            // A little past the deadline, so the next check doesn't land just short of it.
            _timer.Interval = TimeSpan.FromSeconds(wait + 0.1);
            _timer.Start();
        }
    }

    // Manual triggers mean someone is at the PC, so only the idle trigger checks the skip rules.
    private async void Fire()
    {
        if (_checkingSkips)
        {
            return;
        }

        _checkingSkips = true;
        try
        {
            var conditions = new IdleConditions(_session.SessionLocked, OnConsole(), ShellNow(), []);
            var skip = IdleSkipRules.Skip(conditions, new HashSet<int>());
            if (skip is null)
            {
                // Only worth listening for when nothing else blocks.
                conditions = conditions with { Sounds = await ListenToApps() };
                skip = IdleSkipRules.Skip(conditions, IgnoredProcessIds());
            }

            if (skip is { } reason)
            {
                Log.Info("idle", $"Idle, but not opening: {IdleSkipRules.Describe(reason)}. Waiting for input.");
                return;
            }

            Log.Info("idle", "Idle: opening");
            _onIdle();
        }
        catch (Exception error)
        {
            Log.Info("idle", $"Checking the skip rules failed; not opening. {error.Message}");
        }
        finally
        {
            _checkingSkips = false;
        }
    }

    private static double SecondsSinceLastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
        {
            return 0;
        }

        // Both are the 32-bit tick count; the unsigned difference is right across its wrap after 49.7 days.
        return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
    }

    // Remote desktop, or another user switched in with this session in the background.
    private static bool OnConsole()
    {
        using var process = Process.GetCurrentProcess();
        return PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) == 0
            && PInvoke.WTSGetActiveConsoleSessionId() == (uint)process.SessionId;
    }

    private static ShellState ShellNow()
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Failed)
        {
            return ShellState.Unknown;
        }

        var value = (int)state;
        return Enum.IsDefined(typeof(ShellState), value) ? (ShellState)value : ShellState.Unknown;
    }

    private static HashSet<int> IgnoredProcessIds()
    {
        var ids = new HashSet<int> { Environment.ProcessId };
        foreach (var process in Process.GetProcessesByName("Spotify"))
        {
            ids.Add(process.Id);
            process.Dispose();
        }

        return ids;
    }

    /// <summary>Every app with an active sound session on any output, with the loudest peak read over about 300 ms.</summary>
    private static async Task<IReadOnlyList<AppSound>> ListenToApps()
    {
        var meters = OpenMeters();
        var peaks = new float[meters.Count];
        for (var sample = 0; sample < SoundSamples; sample++)
        {
            if (sample > 0)
            {
                await Task.Delay(SoundSampleMilliseconds);
            }

            for (var i = 0; i < meters.Count; i++)
            {
                try
                {
                    meters[i].Meter.GetPeakValue(out var peak);
                    peaks[i] = Math.Max(peaks[i], peak);
                }
                catch (Exception)
                {
                    // The session ended while it was being read; it is silent now.
                }
            }
        }

        return [.. meters.Select((meter, i) => new AppSound(meter.ProcessId, ProcessName(meter.ProcessId), peaks[i]))];
    }

    private static List<(int ProcessId, IAudioMeterInformation Meter)> OpenMeters()
    {
        var meters = new List<(int, IAudioMeterInformation)>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var devices);
        devices.GetCount(out var deviceCount);
        for (uint d = 0; d < deviceCount; d++)
        {
            devices.Item(d, out var device);
            device.Activate(typeof(IAudioSessionManager2).GUID, CLSCTX.CLSCTX_ALL, null, out var activated);
            var manager = (IAudioSessionManager2)activated;
            var sessions = manager.GetSessionEnumerator();
            sessions.GetCount(out var sessionCount);
            for (var s = 0; s < sessionCount; s++)
            {
                sessions.GetSession(s, out var control);
                if (control is not IAudioSessionControl2 session)
                {
                    continue;
                }

                session.GetState(out var state);
                // Windows' own sounds (a notification ding) aren't someone watching something.
                if (state != AudioSessionState.AudioSessionStateActive || session.IsSystemSoundsSession() == 0)
                {
                    continue;
                }

                session.GetProcessId(out var processId);
                meters.Add(((int)processId, (IAudioMeterInformation)session));
            }
        }

        return meters;
    }

    private static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return $"process {processId}";
        }
    }
}
