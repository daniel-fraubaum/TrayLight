using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TrayLight.Services;

/// <summary>
/// Places a window in the bottom-right corner of the work area on the monitor
/// that hosts the cursor (i.e. the one the user just clicked the tray icon
/// on). Falls back to the primary monitor's <c>SystemParameters.WorkArea</c>.
/// </summary>
public class PopupPositioningService : IPopupPositioningService
{
    private const double Margin = 12;

    public void PositionAboveTray(Window window)
    {
        var workArea = GetCursorMonitorWorkArea(window) ?? SystemParameters.WorkArea;

        window.Left = workArea.Right  - window.Width  - Margin;
        window.Top  = workArea.Bottom - window.Height - Margin;
    }

    private static Rect? GetCursorMonitorWorkArea(Window window)
    {
        try
        {
            if (!GetCursorPos(out var pt)) return null;

            const uint MONITOR_DEFAULTTONEAREST = 2;
            var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMon == IntPtr.Zero) return null;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMon, ref info)) return null;

            // Convert from physical pixels to DIPs using the window's DPI.
            var source = PresentationSource.FromVisual(window) ??
                         (window.IsLoaded ? null : EnsureSource(window));
            var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

            var work = info.rcWork;
            return new Rect(
                work.left   / scale,
                work.top    / scale,
                (work.right  - work.left) / scale,
                (work.bottom - work.top)  / scale);
        }
        catch
        {
            return null;
        }
    }

    private static PresentationSource? EnsureSource(Window window)
    {
        // Force handle creation so PresentationSource is non-null before Show().
        new WindowInteropHelper(window).EnsureHandle();
        return PresentationSource.FromVisual(window);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
