using TrayLight.Services.Actions;
using TrayLight.Services.Badges;
using Xunit;

namespace TrayLight.Tests.Badges;

public class ToastWarningNotifierTests
{
    private sealed class FakeBadges : INotificationBadgeService
    {
        public BadgeState Current { get; } = BadgeState.Empty;
        public event EventHandler<BadgeState>? BadgeChanged;
        public event EventHandler<WarningEntry>? WarningRaised;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }

        public void Raise(WarningEntry e) => WarningRaised?.Invoke(this, e);

        // Suppresses CS0067 — events are part of the test seam.
        public void NudgeBadge(BadgeState s) => BadgeChanged?.Invoke(this, s);
    }

    private sealed class FakeNotifier : INotificationService
    {
        public List<(string Title, string Message, NotificationSeverity Severity)> Messages { get; } = new();
        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
            => Messages.Add((title, message, severity));
    }

    [Fact]
    public void First_warning_for_a_type_is_forwarded()
    {
        var badges = new FakeBadges();
        var notifier = new FakeNotifier();
        var clock = DateTime.UtcNow;
        using var sut = new ToastWarningNotifier(badges, notifier, () => clock);

        badges.Raise(new WarningEntry("storageUsed", "Storage", "Disk full"));

        Assert.Single(notifier.Messages);
        Assert.Equal(NotificationSeverity.Warning, notifier.Messages[0].Severity);
        Assert.Equal("Disk full", notifier.Messages[0].Message);
    }

    [Fact]
    public void Repeat_within_throttle_window_is_suppressed()
    {
        var badges = new FakeBadges();
        var notifier = new FakeNotifier();
        var clock = DateTime.UtcNow;
        using var sut = new ToastWarningNotifier(badges, notifier, () => clock);

        badges.Raise(new WarningEntry("storageUsed", "Storage", "1"));
        clock = clock.AddMinutes(30);
        badges.Raise(new WarningEntry("storageUsed", "Storage", "2"));

        Assert.Single(notifier.Messages);
    }

    [Fact]
    public void Repeat_after_throttle_window_is_delivered()
    {
        var badges = new FakeBadges();
        var notifier = new FakeNotifier();
        var clock = DateTime.UtcNow;
        using var sut = new ToastWarningNotifier(badges, notifier, () => clock);

        badges.Raise(new WarningEntry("storageUsed", "Storage", "1"));
        clock = clock.Add(ToastWarningNotifier.ThrottlePerType + TimeSpan.FromMinutes(1));
        badges.Raise(new WarningEntry("storageUsed", "Storage", "2"));

        Assert.Equal(2, notifier.Messages.Count);
    }

    [Fact]
    public void Different_types_are_throttled_independently()
    {
        var badges = new FakeBadges();
        var notifier = new FakeNotifier();
        var clock = DateTime.UtcNow;
        using var sut = new ToastWarningNotifier(badges, notifier, () => clock);

        badges.Raise(new WarningEntry("storageUsed", "Storage", "1"));
        badges.Raise(new WarningEntry("osVersion",   "OS",      "2"));

        Assert.Equal(2, notifier.Messages.Count);
    }

    [Fact]
    public void Empty_message_falls_back_to_generic_text()
    {
        var badges = new FakeBadges();
        var notifier = new FakeNotifier();
        using var sut = new ToastWarningNotifier(badges, notifier, () => DateTime.UtcNow);

        badges.Raise(new WarningEntry("intuneCompliance", "Intune", ""));

        Assert.Single(notifier.Messages);
        Assert.Contains("attention", notifier.Messages[0].Message);
    }

    [Fact]
    public void Dispose_unsubscribes()
    {
        var badges = new FakeBadges();
        var notifier = new FakeNotifier();
        var sut = new ToastWarningNotifier(badges, notifier, () => DateTime.UtcNow);
        sut.Dispose();

        badges.Raise(new WarningEntry("x", "X", "m"));

        Assert.Empty(notifier.Messages);
    }
}
