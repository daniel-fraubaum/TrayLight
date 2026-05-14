namespace TrayLight.Services;

public class SystemInfoService : ISystemInfoService
{
    public string MachineName => Environment.MachineName;
    public string UserName => Environment.UserName;
    public string OsVersion => Environment.OSVersion.VersionString;
}
