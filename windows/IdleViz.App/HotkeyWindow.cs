using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// A window that is never shown. Windows delivers the global hotkey to it, the change of the
/// taskbar's light or dark colour, the session being locked or unlocked, and waking from sleep.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow
{
    private const int HotkeyId = 1;
    private const uint PowerResumeAutomatic = 0x12; // PBT_APMRESUMEAUTOMATIC
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
                return null;
            case PInvoke.WM_WTSSESSION_CHANGE when wParam.Value is PInvoke.WTS_SESSION_LOCK or PInvoke.WTS_SESSION_UNLOCK:
                SessionLocked = wParam.Value == PInvoke.WTS_SESSION_LOCK;
                LockChanged?.Invoke(SessionLocked);
                return (LRESULT)0;
            case PInvoke.WM_POWERBROADCAST when wParam.Value == PowerResumeAutomatic:
                Resumed?.Invoke();
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
