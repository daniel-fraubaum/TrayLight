using TrayLight.Models;
using TrayLight.Services.Badges;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Badges;

public class NotificationBadgeServiceTests
{
    /// <summary>Test fake exposing only the bits the badge service consumes.</summary>
    private sealed class FakeProvider : IInfoItemProvider
    {
        public string Type { get; }
        public FakeProvider(string type) { Type = type; }

        public event EventHandler<InfoItemData>? DataChanged;

        public void Emit(InfoItemData data) => DataChanged?.Invoke(this, data);

        // Unused parts of the contract.
        public Task<InfoItemData> GetDataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InfoItemData.Unavailable("?", "?"));
        public void Configure(InfoItemConfig config) { }
        public void Start(TimeSpan refreshInterval) { }
        public void Stop() { }
        public Task ExecuteClickAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static InfoItemData Warning(string title, string msg) =>
        new(title, "v", "d", true, msg, "i");
    private static InfoItemData Ok(string title) =>
        new(title, "v", "d", false, string.Empty, "i");

    [Fact]
    public void Warning_event_adds_entry_and_raises_BadgeChanged_and_WarningRaised()
    {
        var provider = new FakeProvider("storageUsed");
        using var sut = new NotificationBadgeService(new[] { provider });
        sut.Start();

        BadgeState? lastBadge = null;
        WarningEntry? raised = null;
        sut.BadgeChanged   += (_, s) => lastBadge = s;
        sut.WarningRaised  += (_, e) => raised = e;

        provider.Emit(Warning("Storage", "Disk 95% full"));

        Assert.NotNull(raised);
        Assert.Equal("storageUsed", raised!.TypeKey);
        Assert.Equal("Disk 95% full", raised.Message);
        Assert.Equal(1, lastBadge!.Count);
        Assert.True(lastBadge.HasWarnings);
        Assert.Equal(1, sut.Current.Count);
    }

    [Fact]
    public void Clearing_warning_removes_entry_and_fires_BadgeChanged_only()
    {
        var provider = new FakeProvider("storageUsed");
        using var sut = new NotificationBadgeService(new[] { provider });
        sut.Start();

        provider.Emit(Warning("Storage", "msg"));
        var raisedCount = 0;
        var changedCount = 0;
        sut.WarningRaised += (_, _) => Interlocked.Increment(ref raisedCount);
        sut.BadgeChanged += (_, _) => Interlocked.Increment(ref changedCount);

        provider.Emit(Ok("Storage"));

        Assert.Equal(0, raisedCount);
        Assert.Equal(1, changedCount);
        Assert.Equal(0, sut.Current.Count);
    }

    [Fact]
    public void Repeat_warning_with_same_message_is_idempotent()
    {
        var provider = new FakeProvider("osVersion");
        using var sut = new NotificationBadgeService(new[] { provider });
        sut.Start();

        provider.Emit(Warning("OS", "updates"));

        var raised = 0;
        var changed = 0;
        sut.WarningRaised += (_, _) => Interlocked.Increment(ref raised);
        sut.BadgeChanged += (_, _) => Interlocked.Increment(ref changed);

        provider.Emit(Warning("OS", "updates"));
        provider.Emit(Warning("OS", "updates"));

        Assert.Equal(0, raised);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Updating_message_fires_BadgeChanged_but_not_WarningRaised()
    {
        var provider = new FakeProvider("osVersion");
        using var sut = new NotificationBadgeService(new[] { provider });
        sut.Start();
        provider.Emit(Warning("OS", "old message"));

        var raised = 0;
        var changed = 0;
        sut.WarningRaised += (_, _) => Interlocked.Increment(ref raised);
        sut.BadgeChanged += (_, _) => Interlocked.Increment(ref changed);

        provider.Emit(Warning("OS", "new message"));

        Assert.Equal(0, raised);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Multiple_providers_aggregate_into_a_single_count()
    {
        var a = new FakeProvider("a");
        var b = new FakeProvider("b");
        var c = new FakeProvider("c");
        using var sut = new NotificationBadgeService(new IInfoItemProvider[] { a, b, c });
        sut.Start();

        a.Emit(Warning("A", "x"));
        b.Emit(Warning("B", "y"));
        c.Emit(Ok("C"));

        Assert.Equal(2, sut.Current.Count);
    }

    [Fact]
    public void Stop_unsubscribes_from_providers()
    {
        var provider = new FakeProvider("x");
        var sut = new NotificationBadgeService(new[] { provider });
        sut.Start();
        sut.Stop();

        var raised = 0;
        sut.WarningRaised += (_, _) => Interlocked.Increment(ref raised);
        provider.Emit(Warning("X", "m"));

        Assert.Equal(0, raised);
    }
}
