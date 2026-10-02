using IdleViz.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace IdleViz.App;

/// <summary>
/// The "Open hotkey" control: click it, press a key together with at least one modifier, and that
/// becomes the hotkey. Esc or clicking elsewhere cancels. The ✕ button clears it.
/// </summary>
public sealed partial class HotkeyRecorder : UserControl
{
    private App? _app;
    private bool _recording;

    public HotkeyRecorder()
    {
        InitializeComponent();
    }

    /// <summary>Raised after the hotkey was set or cleared, whether or not Windows accepted it.</summary>
    public event Action? HotkeyChanged;

    public void Attach(App app)
    {
        _app = app;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        var hotkey = _app?.OpenHotkey;
        RecordButton.Content = hotkey?.Label ?? "Not set";
        ClearButton.IsEnabled = hotkey is not null;
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recording || _app is null)
        {
            return;
        }

        _recording = true;
        // Let the current hotkey go, or pressing it here would open the visualizer instead.
        _app.SuspendHotkey();
        RecordButton.Content = "Press a shortcut";
    }

    private void OnRecordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording || _app is null)
        {
            return;
        }

        // Nothing typed here should click the button or move focus.
        e.Handled = true;
        var key = (ushort)e.Key;
        var modifiers = HeldModifiers();

        if (Hotkey.IsModifierKey(key))
        {
            ShowHeld(modifiers | Hotkey.ModifierFor(key));
            return;
        }

        if (e.Key == VirtualKey.Escape && modifiers == HotkeyModifiers.None)
        {
            StopRecording();
            return;
        }

        var hotkey = new Hotkey(modifiers, key);
        if (!hotkey.IsValid)
        {
            // A key with no modifier: keep listening.
            return;
        }

        _recording = false;
        _app.SetOpenHotkey(hotkey);
        ShowCurrent();
        HotkeyChanged?.Invoke();
    }

    private void OnRecordKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_recording)
        {
            e.Handled = true;
            ShowHeld(HeldModifiers() & ~Hotkey.ModifierFor((ushort)e.Key));
        }
    }

    private void OnRecordLostFocus(object sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            StopRecording();
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_app is null)
        {
            return;
        }

        _recording = false;
        _app.SetOpenHotkey(null);
        ShowCurrent();
        HotkeyChanged?.Invoke();
    }

    /// <summary>Ends a recording that set nothing: the stored hotkey is registered again.</summary>
    private void StopRecording()
    {
        _recording = false;
        _app?.ApplyHotkey();
        ShowCurrent();
        HotkeyChanged?.Invoke();
    }

    /// <summary>Shows the modifiers held so far, as feedback while recording.</summary>
    private void ShowHeld(HotkeyModifiers modifiers)
    {
        if (modifiers == HotkeyModifiers.None)
        {
            RecordButton.Content = "Press a shortcut";
            return;
        }

        // The label of a hotkey with these modifiers and no key yet, with the key left open.
        var label = new Hotkey(modifiers, 0).Label;
        RecordButton.Content = label[..label.LastIndexOf('+')] + "+ …";
    }

    private static HotkeyModifiers HeldModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
