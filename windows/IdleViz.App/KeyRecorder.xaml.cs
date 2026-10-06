using IdleViz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace IdleViz.App;

/// <summary>
/// The control of the "Like key", "Skip key" and "Block key" rows: click it, press a key, and that
/// becomes the key. Esc or clicking elsewhere keeps the key it had. The ✕ button turns the key off.
/// </summary>
public sealed partial class KeyRecorder : UserControl
{
    private const string Prompt = "Press a key";

    private ushort? _key;
    private HashSet<ushort> _taken = [];
    private bool _recording;

    public KeyRecorder()
    {
        InitializeComponent();
        ShowCurrent();
    }

    /// <summary>Raised with the new key, or null when the key was turned off.</summary>
    public event Action<ushort?>? KeyChanged;

    /// <summary>What a screen reader calls the row: "Like key".</summary>
    public string ActionName
    {
        get => AutomationProperties.GetName(RecordButton);
        set
        {
            AutomationProperties.SetName(RecordButton, value);
            AutomationProperties.SetName(ClearButton, $"Turn off: {value}");
        }
    }

    /// <summary>Shows the stored key. The keys the other rows use can't be recorded here.</summary>
    public void Show(ushort? key, IEnumerable<ushort> taken)
    {
        _key = key;
        _taken = taken.ToHashSet();
        if (!_recording)
        {
            ShowCurrent();
        }
    }

    private void ShowCurrent()
    {
        RecordButton.Content = VisualizerKeys.Label(_key);
        ClearButton.IsEnabled = _key is not null;
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        _recording = true;
        RecordButton.Content = Prompt;
    }

    private void OnRecordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        // Nothing typed here should click the button or move focus.
        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            StopRecording();
            return;
        }

        // A repeat is the key that clicked the button, still held. Modifiers and media keys can't be the key.
        var code = (int)e.Key;
        if (e.KeyStatus.WasKeyDown || !VisualizerKeys.CanBe(code))
        {
            return;
        }

        if (_taken.Contains((ushort)code))
        {
            RecordButton.Content = "In use, press another";
            return;
        }

        _recording = false;
        _key = (ushort)code;
        ShowCurrent();
        KeyChanged?.Invoke(_key);
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
        _recording = false;
        _key = null;
        ShowCurrent();
        KeyChanged?.Invoke(null);
    }

    /// <summary>Ends a recording that set nothing: the key stays what it was.</summary>
    private void StopRecording()
    {
        _recording = false;
        ShowCurrent();
    }
}
