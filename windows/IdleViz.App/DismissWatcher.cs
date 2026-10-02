using System.Diagnostics;
using System.Runtime.InteropServices;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// Closes the visualizer on any input. Low-level keyboard and mouse hooks, installed only while it is
/// open, see every key, click, scroll and mouse move whether or not the window has focus. A check of
/// the system's last-input time every 100 ms covers input the hooks miss (Windows drops a hook that
/// answers too slowly). The rules themselves, including keys still held after the grace period, live
/// in <see cref="DismissTracker"/>.
///
/// The input that closes the visualizer is swallowed, so a key never types into the app behind and a
/// click never presses something nobody could see. The media keys go through and don't close it.
/// </summary>
internal sealed class DismissWatcher : IDisposable
{
    /// <summary>How long the backup check waits for the hooks to explain an input before closing on it.</summary>
    private static readonly TimeSpan s_settleTime = TimeSpan.FromMilliseconds(30);

    private readonly DismissTracker _tracker = new();
    private readonly KeyboardInput _keyboard = new();
    private readonly Stopwatch _sinceOpened = Stopwatch.StartNew();
    private readonly Action _onDismiss;
    private readonly DispatcherQueueTimer _timer;

    // Kept in fields: Windows holds only raw pointers to them.
    private readonly HOOKPROC _keyboardProc;
    private readonly HOOKPROC _mouseProc;
    private UnhookWindowsHookExSafeHandle? _keyboardHook;
    private UnhookWindowsHookExSafeHandle? _mouseHook;
    private System.Drawing.Point _lastMouse;
    private bool _armed;
    private bool _settling;

    public DismissWatcher(DispatcherQueue dispatcher, Action onDismiss)
    {
        _onDismiss = onDismiss;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        _timer = dispatcher.CreateTimer();
        _timer.Tick += (_, _) => OnTimer();
    }

    private double Elapsed => _sinceOpened.Elapsed.TotalSeconds;

    public void Start()
    {
        PInvoke.GetCursorPos(out _lastMouse);
        _keyboard.Reset(HeldKeys());
        var module = PInvoke.GetModuleHandle((string?)null);
        _keyboardHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook.IsInvalid || _mouseHook.IsInvalid)
        {
            Log.Info("dismiss", $"A hook could not be installed (error {Marshal.GetLastWin32Error()}); the backup check still closes.");
        }

        // First tick: the grace period is over. After that: the backup check.
        _timer.Interval = TimeSpan.FromSeconds(_tracker.GracePeriod);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _keyboardHook?.Dispose();
        _mouseHook?.Dispose();
        _keyboardHook = null;
        _mouseHook = null;
    }

    public void Dispose() => Stop();

    private void OnTimer()
    {
        if (!_armed)
        {
            // Keys still down now are the trigger's; their release is ignored.
            _armed = true;
            _tracker.Arm(HeldKeys(), Elapsed);
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            return;
        }

        if (!BackupSaysDismiss())
        {
            _settling = false;
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            return;
        }

        if (!_settling)
        {
            // The last-input time moves the moment a key does, before the hook has run. If that was a
            // media key, the hook explains it in a moment, so look again shortly before closing.
            _settling = true;
            _timer.Interval = s_settleTime;
            return;
        }

        Fire("input the hooks did not report (backup check)");
    }

    private bool BackupSaysDismiss()
    {
        var lastInput = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref lastInput))
        {
            return false;
        }

        // Both are milliseconds since Windows started, and the subtraction is right across the 49-day wrap.
        var idleSeconds = unchecked((uint)Environment.TickCount - lastInput.dwTime) / 1000.0;
        return _tracker.ShouldDismiss(idleSeconds, HeldKeys(), Elapsed);
    }

    private void Fire(string cause)
    {
        Log.Info("dismiss", $"Closed by {cause} after {Elapsed:F2} s");
        Stop();
        _onDismiss();
    }

    private LRESULT KeyboardProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isDown = (uint)wParam.Value is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
            var input = _keyboard.Translate((ushort)key.vkCode, isDown);
            if (_tracker.ShouldDismiss(input, Elapsed))
            {
                Fire($"key 0x{key.vkCode:X2}");
                return (LRESULT)1;
            }
        }

        return PInvoke.CallNextHookEx(null, code, wParam, lParam);
    }

    private LRESULT MouseProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && MouseInput((uint)wParam.Value, lParam) is { } input && _tracker.ShouldDismiss(input, Elapsed))
        {
            Fire(input.Kind == InputKind.MouseMoved ? $"mouse move ({input.DeltaX}, {input.DeltaY})" : input.Kind.ToString());
            // A move is let through, or the pointer would stick for one step. Clicks and scrolls are swallowed.
            if (input.Kind != InputKind.MouseMoved)
            {
                return (LRESULT)1;
            }
        }

        return PInvoke.CallNextHookEx(null, code, wParam, lParam);
    }

    private InputEvent? MouseInput(uint message, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE:
                var position = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
                var moved = InputEvent.MouseMoved(position.X - _lastMouse.X, position.Y - _lastMouse.Y);
                _lastMouse = position;
                return moved;
            case PInvoke.WM_LBUTTONDOWN:
            case PInvoke.WM_RBUTTONDOWN:
            case PInvoke.WM_MBUTTONDOWN:
            case PInvoke.WM_XBUTTONDOWN:
                return InputEvent.MouseDown;
            case PInvoke.WM_MOUSEWHEEL:
            case PInvoke.WM_MOUSEHWHEEL:
                return InputEvent.Scroll;
            default:
                // Button releases: the press already counted.
                return null;
        }
    }

    /// <summary>Virtual-key codes that are down right now.</summary>
    private static HashSet<ushort> HeldKeys()
    {
        var held = new HashSet<ushort>();
        for (var key = 0x08; key <= 0xFE; key++)
        {
            if (KeyboardInput.IsKeyboardKey(key) && PInvoke.GetAsyncKeyState(key) < 0)
            {
                held.Add((ushort)key);
            }
        }

        return held;
    }
}
