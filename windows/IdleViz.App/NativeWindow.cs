using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// A plain Win32 window. The visualizer window and the hidden window that receives the hotkey are
/// both one of these: WinUI windows can't be made see-through for the fades, and can't sit hidden
/// without a taskbar button as simply.
/// </summary>
internal abstract unsafe class NativeWindow : IDisposable
{
    private const string ClassName = "IdleVizWindow";

    private static readonly Dictionary<nint, NativeWindow> s_windows = [];

    // Kept in a field: Windows holds only a raw pointer to it, which the garbage collector can't see.
    private static readonly WNDPROC s_windowProc = WindowProc;
    private static bool s_classRegistered;

    protected NativeWindow(string title, WINDOW_EX_STYLE extendedStyle, WINDOW_STYLE style)
    {
        RegisterClass();
        Handle = PInvoke.CreateWindowEx(
            extendedStyle, ClassName, title, style, 0, 0, 0, 0, HWND.Null, null, PInvoke.GetModuleHandle((string?)null), null);
        if (Handle.IsNull)
        {
            throw new InvalidOperationException($"Could not create the window \"{title}\".");
        }

        s_windows[Handle] = this;
    }

    public HWND Handle { get; private set; }

    /// <summary>Handles a message, or returns null to let Windows do its default.</summary>
    protected virtual LRESULT? OnMessage(uint message, WPARAM wParam, LPARAM lParam) => null;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Handle.IsNull)
        {
            return;
        }

        s_windows.Remove(Handle);
        PInvoke.DestroyWindow(Handle);
        Handle = HWND.Null;
    }

    private static void RegisterClass()
    {
        if (s_classRegistered)
        {
            return;
        }

        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = s_windowProc,
                hInstance = PInvoke.GetModuleHandle((PCWSTR)null),
                lpszClassName = className,
                hbrBackground = new HBRUSH((nint)PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH).Value),
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("Could not register the window class.");
            }
        }

        s_classRegistered = true;
    }

    private static LRESULT WindowProc(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (s_windows.TryGetValue(window, out var instance) && instance.OnMessage(message, wParam, lParam) is { } result)
        {
            return result;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
