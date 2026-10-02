using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Win32;
using Windows.Win32;

namespace IdleViz.App;

/// <summary>Draws the tray icon: a five-bar waveform in one colour, white or black to suit the taskbar.</summary>
internal static class TrayGlyph
{
    /// <summary>The waveform on a 16 × 16 grid: x, top and bottom of each bar. tools/make-icon.ps1 draws the same shape.</summary>
    private static readonly (float X, float Top, float Bottom)[] s_bars =
    [
        (2, 7, 9), (5, 4, 12), (8, 2, 14), (11, 5, 11), (14, 7, 9),
    ];

    /// <summary>The colour for the current taskbar: black on a light one, white on a dark one.</summary>
    public static Color TaskbarColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var lightTaskbar = key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        return lightTaskbar ? Color.Black : Color.White;
    }

    /// <summary>The icon at the size the tray uses on this PC's main display.</summary>
    public static Icon Create(Color color)
    {
        var size = (int)Math.Round(16 * PInvoke.GetDpiForSystem() / 96.0);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            var scale = size / 16f;
            using var pen = new Pen(color, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (var (x, top, bottom) in s_bars)
            {
                graphics.DrawLine(pen, x * scale, top * scale, x * scale, bottom * scale);
            }
        }

        // FromHandle doesn't own the handle, so the icon is copied and the handle released here.
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            PInvoke.DestroyIcon(new Windows.Win32.UI.WindowsAndMessaging.HICON(handle));
        }
    }
}
