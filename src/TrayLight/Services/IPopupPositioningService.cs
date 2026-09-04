using System.Windows;

namespace TrayLight.Services;

public interface IPopupPositioningService
{
    void PositionAboveTray(Window window);

    /// <summary>Work area (screen minus taskbar) of the monitor under the cursor.</summary>
    Rect GetWorkArea(Window window);
}
