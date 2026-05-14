using TrayLight.Models;
using TrayLight.Services.Actions;
using Xunit;

namespace TrayLight.Tests.Actions;

public class ActionExecutorTests
{
    private sealed class FakeHandler : IShortcutActionHandler
    {
        public ShortcutActionType ActionType { get; }
        public bool Available { get; set; } = true;
        public Func<ShortcutConfig, Task<ActionResult>> OnExecute { get; set; } =
            _ => Task.FromResult(ActionResult.Ok());
        public int Calls;

        public FakeHandler(ShortcutActionType type) { ActionType = type; }
        public bool IsAvailable(ShortcutConfig config) => Available;
        public Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return OnExecute(config);
        }
    }

    private sealed class FakeNotifier : INotificationService
    {
        public List<(string Title, string Message, NotificationSeverity Severity)> Messages { get; } = new();
        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
            => Messages.Add((title, message, severity));
    }

    private sealed class FakeConfirm : IConfirmationService
    {
        public bool Result { get; set; } = true;
        public int Calls;
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task Unknown_action_type_returns_failure_and_notifies()
    {
        var notifier = new FakeNotifier();
        var sut = new ActionExecutor(
            handlers: Array.Empty<IShortcutActionHandler>(),
            confirmation: new FakeConfirm(),
            notifications: notifier);

        var result = await sut.ExecuteAsync(new ShortcutConfig
        {
            Title = "X",
            ActionType = ShortcutActionType.Url,
            Action = "https://example.com"
        });

        Assert.False(result.Success);
        Assert.Single(notifier.Messages);
        Assert.Equal(NotificationSeverity.Error, notifier.Messages[0].Severity);
    }

    [Fact]
    public void IsVisible_reflects_handler_IsAvailable()
    {
        var handler = new FakeHandler(ShortcutActionType.App) { Available = false };
        var sut = new ActionExecutor(
            new[] { handler },
            new FakeConfirm(),
            new FakeNotifier());

        Assert.False(sut.IsVisible(new ShortcutConfig
        {
            ActionType = ShortcutActionType.App,
            Action = "x"
        }));

        handler.Available = true;
        Assert.True(sut.IsVisible(new ShortcutConfig
        {
            ActionType = ShortcutActionType.App,
            Action = "x"
        }));
    }

    [Fact]
    public async Task RequiresConfirmation_aborts_when_user_declines()
    {
        var handler = new FakeHandler(ShortcutActionType.Command);
        var confirm = new FakeConfirm { Result = false };
        var sut = new ActionExecutor(new[] { handler }, confirm, new FakeNotifier());

        var result = await sut.ExecuteAsync(new ShortcutConfig
        {
            Title = "Reset",
            ActionType = ShortcutActionType.Command,
            Action = "x",
            RequiresConfirmation = true,
            ConfirmationMessage = "Sure?"
        });

        Assert.False(result.Success);
        Assert.Equal(1, confirm.Calls);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Success_with_message_emits_success_notification()
    {
        var handler = new FakeHandler(ShortcutActionType.Command)
        {
            OnExecute = _ => Task.FromResult(ActionResult.Ok("All clean"))
        };
        var notifier = new FakeNotifier();
        var sut = new ActionExecutor(new[] { handler }, new FakeConfirm(), notifier);

        await sut.ExecuteAsync(new ShortcutConfig
        {
            Title = "Cleanup",
            ActionType = ShortcutActionType.Command,
            Action = "x"
        });

        Assert.Single(notifier.Messages);
        Assert.Equal(NotificationSeverity.Success, notifier.Messages[0].Severity);
        Assert.Equal("All clean", notifier.Messages[0].Message);
    }

    [Fact]
    public async Task Success_without_message_does_not_notify()
    {
        var handler = new FakeHandler(ShortcutActionType.Url);
        var notifier = new FakeNotifier();
        var sut = new ActionExecutor(new[] { handler }, new FakeConfirm(), notifier);

        await sut.ExecuteAsync(new ShortcutConfig
        {
            Title = "Open",
            ActionType = ShortcutActionType.Url,
            Action = "https://x"
        });

        Assert.Empty(notifier.Messages);
    }

    [Fact]
    public async Task Handler_exception_is_translated_to_error_notification()
    {
        var handler = new FakeHandler(ShortcutActionType.Command)
        {
            OnExecute = _ => throw new InvalidOperationException("boom")
        };
        var notifier = new FakeNotifier();
        var sut = new ActionExecutor(new[] { handler }, new FakeConfirm(), notifier);

        var result = await sut.ExecuteAsync(new ShortcutConfig
        {
            Title = "Bad",
            ActionType = ShortcutActionType.Command,
            Action = "x"
        });

        Assert.False(result.Success);
        Assert.Single(notifier.Messages);
        Assert.Equal(NotificationSeverity.Error, notifier.Messages[0].Severity);
    }
}
