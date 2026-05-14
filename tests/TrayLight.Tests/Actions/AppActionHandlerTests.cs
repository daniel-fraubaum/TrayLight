using System.IO;
using TrayLight.Models;
using TrayLight.Services.Actions;
using Xunit;

namespace TrayLight.Tests.Actions;

public class AppActionHandlerTests
{
    [Fact]
    public void IsAvailable_true_for_existing_executable()
    {
        var sut = new AppActionHandler();
        // notepad ships with every Windows install used by CI.
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.True(sut.IsAvailable(new ShortcutConfig { Action = notepad }));
    }

    [Fact]
    public void IsAvailable_false_for_missing_executable()
    {
        var sut = new AppActionHandler();
        Assert.False(sut.IsAvailable(new ShortcutConfig
        {
            Action = @"C:\does\not\exist\nope.exe"
        }));
    }

    [Fact]
    public void IsAvailable_true_for_shell_AppsFolder_uri()
    {
        var sut = new AppActionHandler();
        Assert.True(sut.IsAvailable(new ShortcutConfig
        {
            Action = "shell:AppsFolder\\Microsoft.CompanyPortal_8wekyb3d8bbwe!App"
        }));
    }

    [Fact]
    public async Task ExecuteAsync_returns_failure_for_empty_action()
    {
        var sut = new AppActionHandler();
        var result = await sut.ExecuteAsync(new ShortcutConfig { Action = "" }, CancellationToken.None);
        Assert.False(result.Success);
    }
}
