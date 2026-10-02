using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// A window that is never shown. Windows delivers the global hotkey to it, and the change of
/// the taskbar's light or dark colour.
/// </summary>
internal sealed class HotkeyWindow() : NativeWindow("IdleViz", 0, WINDOW_STYLE.WS_OVERLAPPED)
{
    private const int HotkeyId = 1;
    private bool _registered;

    public event Action? HotkeyPressed;

    public event Action? ThemeChanged;

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
            default:
                return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        base.Dispose(disposing);
    }
}
