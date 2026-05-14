namespace TrayLight.Services;

public interface ISystemInfoService
{
    string MachineName { get; }
    string UserName { get; }
    string OsVersion { get; }
}
