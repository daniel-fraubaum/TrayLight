using System.Windows;

namespace TrayLight.Services;

public interface IPopupPositioningService
{
    void PositionAboveTray(Window window);
}
