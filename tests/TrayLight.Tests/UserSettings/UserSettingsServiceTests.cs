using System.IO;
using TrayLight.Services.UserSettings;
using Xunit;

namespace TrayLight.Tests.UserSettings;

public class UserSettingsServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public UserSettingsServiceTests()
    {
        _dir  = Path.Combine(Path.GetTempPath(), "TrayLight.Tests." + Guid.NewGuid().ToString("N"));
        _file = Path.Combine(_dir, "user-settings.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_missing()
    {
        var sut = new UserSettingsService(_file);
        var s = sut.Load();
        Assert.False(s.WelcomeShown);
        Assert.Equal(1, s.SchemaVersion);
    }

    [Fact]
    public void Save_persists_and_reload_returns_same_values()
    {
        var sut = new UserSettingsService(_file);
        sut.Save(new TrayLight.Services.UserSettings.UserSettings { WelcomeShown = true });

        var sut2 = new UserSettingsService(_file);
        Assert.True(sut2.Load().WelcomeShown);
    }

    [Fact]
    public void Save_creates_target_directory()
    {
        Assert.False(Directory.Exists(_dir));
        var sut = new UserSettingsService(_file);
        sut.Save(new TrayLight.Services.UserSettings.UserSettings { WelcomeShown = true });
        Assert.True(File.Exists(_file));
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_json()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file, "{ this is not json");
        var sut = new UserSettingsService(_file);
        Assert.False(sut.Load().WelcomeShown);
    }

    [Fact]
    public void Current_lazy_loads_on_first_access()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file, "{\"welcomeShown\":true}");
        var sut = new UserSettingsService(_file);
        Assert.True(sut.Current.WelcomeShown);
    }
}
