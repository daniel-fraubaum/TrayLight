namespace TrayLight.Services;

public interface IAutoStartService
{
    bool IsEnabled();
    void Enable();
    void Disable();
}
