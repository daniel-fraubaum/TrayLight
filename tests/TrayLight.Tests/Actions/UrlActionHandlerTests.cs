using TrayLight.Models;
using TrayLight.Services.Actions;
using Xunit;

namespace TrayLight.Tests.Actions;

public class UrlActionHandlerTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://intranet")]
    [InlineData("mailto:helpdesk@contoso.com")]
    [InlineData("tel:+15551234567")]
    [InlineData("ms-settings:network")]
    [InlineData("microsoft-edge:https://example.com")]
    public void Allowed_schemes_are_available(string url)
    {
        var sut = new UrlActionHandler();
        Assert.True(sut.IsAvailable(new ShortcutConfig { Action = url }));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData("not a url")]
    public void Disallowed_or_invalid_schemes_are_unavailable(string url)
    {
        var sut = new UrlActionHandler();
        Assert.False(sut.IsAvailable(new ShortcutConfig { Action = url }));
    }

    [Fact]
    public async Task Disallowed_scheme_execute_fails()
    {
        var sut = new UrlActionHandler();
        var result = await sut.ExecuteAsync(
            new ShortcutConfig { Action = "javascript:alert(1)" }, CancellationToken.None);
        Assert.False(result.Success);
    }
}
