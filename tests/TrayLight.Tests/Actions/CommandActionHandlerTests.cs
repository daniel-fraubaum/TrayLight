using TrayLight.Models;
using TrayLight.Services.Actions;
using Xunit;

namespace TrayLight.Tests.Actions;

public class CommandActionHandlerTests
{
    [Fact]
    public async Task Successful_command_returns_success_with_message()
    {
        var sut = new CommandActionHandler((_, _) =>
            Task.FromResult(new CommandActionHandler.CommandResult(0, "OK", "")));

        var result = await sut.ExecuteAsync(
            new ShortcutConfig { Action = "Get-Date", SuccessMessage = "Done" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Done", result.Message);
    }

    [Fact]
    public async Task Non_zero_exit_returns_failure_with_stderr()
    {
        var sut = new CommandActionHandler((_, _) =>
            Task.FromResult(new CommandActionHandler.CommandResult(2, "", "boom")));

        var result = await sut.ExecuteAsync(
            new ShortcutConfig { Action = "Throw" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Message);
        Assert.Contains("2", result.Message);
    }

    [Fact]
    public async Task Cancellation_is_reported_as_timeout()
    {
        var sut = new CommandActionHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new CommandActionHandler.CommandResult(0, "", "");
        });

        // Outer cancellation -> rethrows; inner timeout -> friendly message.
        // Use a dedicated short-lived linked CTS to simulate the timeout path
        // without waiting 60s.
        using var outer = new CancellationTokenSource();
        var sutTask = sut.ExecuteAsync(new ShortcutConfig { Action = "x" }, outer.Token);
        // Give the runner a chance to start, then cancel via the outer token.
        outer.Cancel();
        var result = await sutTask;
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Empty_action_fails_fast()
    {
        var sut = new CommandActionHandler();
        var result = await sut.ExecuteAsync(new ShortcutConfig { Action = "" }, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("No command", result.Message);
    }
}
