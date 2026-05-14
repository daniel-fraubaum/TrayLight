using System.Windows;
using System.Windows.Media;

namespace TrayLight.Helpers;

public static class DpiHelper
{
    public static double GetDpiScale(Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget is null)
        {
            return 1.0;
        }

        Matrix m = source.CompositionTarget.TransformToDevice;
        return m.M11; // Horizontal scale; Windows uses uniform scaling for tray placement.
    }
}
