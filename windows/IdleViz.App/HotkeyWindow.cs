using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// A window that is never shown. Windows delivers the global hotkey to it, the change of the
/// taskbar's light or dark colour, the session being locked or unlocked, going to sleep and waking,
/// the power source changing, and the displays changing.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow
{
    private const int HotkeyId = 1;
    private bool _registered;
    private readonly bool _sessionNotifications;

    public HotkeyWindow()
        : base("IdleViz", 0, WINDOW_STYLE.WS_OVERLAPPED)
    {
        _sessionNotifications = PInvoke.WTSRegisterSessionNotification(Handle, PInvoke.NOTIFY_FOR_THIS_SESSION);
    }

    public event Action? HotkeyPressed;

    public event Action? ThemeChanged;

    /// <summary>Raised with true when this session is locked, and false when it is unlocked.</summary>
    public event Action<bool>? LockChanged;

    /// <summary>Raised when the PC woke from sleep.</summary>
    public event Action? Resumed;

    /// <summary>Raised when the PC is about to sleep.</summary>
    public event Action? Suspending;

    /// <summary>Raised when the PC was plugged in or unplugged, or the charge level changed.</summary>
    public event Action? PowerStatusChanged;

    /// <summary>Raised when a display may have been added, removed or changed. Windows also sends this when nothing did.</summary>
    public event Action? DisplaysMayHaveChanged;

    /// <summary>Whether this session is locked, as far as the lock and unlock notices tell.</summary>
    public bool SessionLocked { get; private set; }

    /// <summary>
    /// Makes this the open hotkey, replacing the last one. Null registers none.
    /// Returns false if Windows refuses the combination, which means another app has it.
    /// </summary>
    public bool Register(Hotkey? hotkey)
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        if (hotkey is not { IsValid: true } combination)
        {
            return true;
        }

        // No repeat: holding the keys down must not fire again and again.
        var modifiers = (HOT_KEY_MODIFIERS)(uint)combination.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        _registered = PInvoke.RegisterHotKey(Handle, HotkeyId, modifiers, combination.Key);
        return _registered;
    }

    protected override LRESULT? OnMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_HOTKEY when (int)wParam.Value == HotkeyId:
                HotkeyPressed?.Invoke();
                return (LRESULT)0;
            case PInvoke.WM_SETTINGCHANGE:
                ThemeChanged?.Invoke();
                // A change of scale arrives as a settings change on some PCs, not as a display change.
                DisplaysMayHaveChanged?.Invoke();
                return null;
            case PInvoke.WM_DISPLAYCHANGE:
                DisplaysMayHaveChanged?.Invoke();
                return null;
            case PInvoke.WM_WTSSESSION_CHANGE when wParam.Value is PInvoke.WTS_SESSION_LOCK or PInvoke.WTS_SESSION_UNLOCK:
                SessionLocked = wParam.Value == PInvoke.WTS_SESSION_LOCK;
                LockChanged?.Invoke(SessionLocked);
                return (LRESULT)0;
            case PInvoke.WM_POWERBROADCAST when wParam.Value == PInvoke.PBT_APMRESUMEAUTOMATIC:
                Resumed?.Invoke();
                return (LRESULT)1;
            case PInvoke.WM_POWERBROADCAST when wParam.Value == PInvoke.PBT_APMSUSPEND:
                Suspending?.Invoke();
                return (LRESULT)1;
            case PInvoke.WM_POWERBROADCAST when wParam.Value == PInvoke.PBT_APMPOWERSTATUSCHANGE:
                PowerStatusChanged?.Invoke();
                return (LRESULT)1;
            default:
                return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_sessionNotifications && !Handle.IsNull)
        {
            PInvoke.WTSUnRegisterSessionNotification(Handle);
        }

        if (_registered)
        {
            PInvoke.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        base.Dispose(disposing);
    }
}
