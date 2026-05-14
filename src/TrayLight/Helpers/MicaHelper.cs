using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TrayLight.Helpers;

/// <summary>
/// Win11 desktop window manager helpers: enables Mica backdrop, immersive
/// dark mode, and rounded corners on a top-level WPF window. All operations
/// fail silently on older Windows builds where the attributes are unsupported.
/// </summary>
public static class MicaHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win10 2004+)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    // DWMWA_WINDOW_CORNER_PREFERENCE = 33 (Win11+)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    // DWMWA_SYSTEMBACKDROP_TYPE = 38 (Win11 22H2+)
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;          // rounded corners
    private const int DWMSBT_MAINWINDOW = 2;     // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    public static void Apply(Window window, bool useDarkMode, bool acrylic = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        // Mica/Acrylic requires the WPF window background to be transparent
        // so the system backdrop shows through. Done here so callers don't have
        // to remember.
        window.Background = Brushes.Transparent;

        TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, useDarkMode ? 1 : 0);
        TrySet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        TrySet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW);
    }

    private static void TrySet(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            int v = value;
            DwmSetWindowAttribute(hwnd, attribute, ref v, sizeof(int));
        }
        catch
        {
            // Older OS without this attribute -- ignore.
        }
    }
}
