using IdleViz.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace IdleViz.App;

/// <summary>
/// The manual delay test: beeps play through the speakers, the panel lights up one audio delay
/// after each, and the slider is dragged until the two land together. Done saves the delay for
/// the device. Ported from <c>ManualDelaySheet</c> in <c>PresetControls.swift</c>.
/// </summary>
public sealed partial class ManualDelayDialog : ContentDialog
{
    private readonly AudioDelayController _delay;
    private readonly SolidColorBrush _dark = new(Colors.Black);
    private readonly SolidColorBrush _lit = new(Colors.White);
    private readonly SolidColorBrush _accent = new(Colors.Orange);

    // Set while the slider is being moved to the stored value, so that isn't taken as a drag.
    private bool _showing;

    internal ManualDelayDialog(AudioDelayController delay)
    {
        _delay = delay;
        InitializeComponent();
        Opened += (_, _) =>
        {
            _delay.Changed += Show;
            // Redrawn every frame; the flash is worked out from the clock the beeps are placed on.
            CompositionTarget.Rendering += OnRendering;
            _delay.StartTest();
            Show();
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _delay.Changed -= Show;
            _delay.StopTest();
        };
    }

    private void Show()
    {
        _showing = true;
        try
        {
            DelaySlider.Value = _delay.Delay;
        }
        finally
        {
            _showing = false;
        }

        DelayText.Text = AudioDelaySetting.Label(_delay.Delay);
        DeviceText.Text = $"For {_delay.DeviceName}";
        ProblemText.Text = _delay.TestProblem ?? string.Empty;
        ProblemText.Visibility = _delay.TestProblem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRendering(object? sender, object e)
    {
        var brush = _delay.TestFlash switch
        {
            null => _dark,
            { Accent: true } => _accent,
            _ => _lit,
        };
        if (!ReferenceEquals(FlashPanel.Background, brush))
        {
            FlashPanel.Background = brush;
        }
    }

    private void OnDelayChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_showing)
        {
            _delay.Delay = e.NewValue;
        }
    }

    private void OnEarlierClick(object sender, RoutedEventArgs e) => _delay.Delay -= AudioDelaySetting.Step;

    private void OnLaterClick(object sender, RoutedEventArgs e) => _delay.Delay += AudioDelaySetting.Step;
}
