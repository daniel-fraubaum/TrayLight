using System.ComponentModel;
using TrayLight.Models;
using TrayLight.Services;
using TrayLight.Services.Actions;
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

    private sealed class StubConfig : IConfigurationService
    {
        public AppConfiguration Current { get; private set; }
        public string ConfigPath => string.Empty;
        public DateTime? LastLoadedUtc => DateTime.UtcNow;
        public event PropertyChangedEventHandler? PropertyChanged;

        public StubConfig(bool notifyOnUpdates, bool? lastRebootBadge = null) =>
            Current = Build(notifyOnUpdates, lastRebootBadge);

        public void Push(bool notifyOnUpdates, bool? lastRebootBadge = null)
        {
            Current = Build(notifyOnUpdates, lastRebootBadge);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }

        private static AppConfiguration Build(bool notifyOnUpdates, bool? lastRebootBadge) => new()
        {
            Behavior = new BehaviorConfig { NotifyOnUpdates = notifyOnUpdates },
            InfoItems =
            {
                new InfoItemConfig { Type = InfoItemType.LastReboot,  ShowNotificationBadge = lastRebootBadge },
                new InfoItemConfig { Type = InfoItemType.StorageUsed, ShowNotificationBadge = true },
            },
        };

        public AppConfiguration Load() => Current;
        public void StartWatching() { }
        public void Dispose() { }
    }

    private sealed class FakeNotifier : INotificationService
    {
        public int Count { get; private set; }
        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
            => Count++;
    }

    [Fact]
    public void Master_switch_disabled_suppresses_warning_icon_and_toast_despite_tile_badge_on()
    {
        var provider = new FakeProvider("lastReboot");
        var config = new StubConfig(notifyOnUpdates: false, lastRebootBadge: true);
        using var sut = new NotificationBadgeService(new[] { provider }, config);
        var notifier = new FakeNotifier();
        using var toasts = new ToastWarningNotifier(sut, notifier);
        sut.Start();

        provider.Emit(Warning("Last reboot", "Reboot pending"));

        Assert.False(sut.Current.HasWarnings);
        Assert.Equal(0, notifier.Count);
    }

    [Fact]
    public void Per_tile_badge_disabled_only_suppresses_that_tile()
    {
        var reboot = new FakeProvider("lastReboot");
        var storage = new FakeProvider("storageUsed");
        var config = new StubConfig(notifyOnUpdates: true, lastRebootBadge: false);
        using var sut = new NotificationBadgeService(new IInfoItemProvider[] { reboot, storage }, config);
        sut.Start();

        reboot.Emit(Warning("Last reboot", "Reboot pending"));
        storage.Emit(Warning("Storage", "Disk full"));

        var entry = Assert.Single(sut.Current.Warnings);
        Assert.Equal("storageUsed", entry.TypeKey);
    }

    [Fact]
    public void Disabling_master_switch_at_runtime_clears_active_warnings()
    {
        var provider = new FakeProvider("lastReboot");
        var config = new StubConfig(notifyOnUpdates: true);
        using var sut = new NotificationBadgeService(new[] { provider }, config);
        sut.Start();
        provider.Emit(Warning("Last reboot", "Reboot pending"));
        Assert.True(sut.Current.HasWarnings);

        BadgeState? last = null;
        sut.BadgeChanged += (_, s) => last = s;
        config.Push(notifyOnUpdates: false, lastRebootBadge: true);

        Assert.NotNull(last);
        Assert.False(last!.HasWarnings);
        Assert.False(sut.Current.HasWarnings);
    }

    [Fact]
    public void Enabling_master_switch_at_runtime_raises_pending_warning()
    {
        var provider = new FakeProvider("lastReboot");
        var config = new StubConfig(notifyOnUpdates: false);
        using var sut = new NotificationBadgeService(new[] { provider }, config);
        sut.Start();
        provider.Emit(Warning("Last reboot", "Reboot pending"));

        WarningEntry? raised = null;
        sut.WarningRaised += (_, e) => raised = e;
        config.Push(notifyOnUpdates: true);

        Assert.NotNull(raised);
        Assert.True(sut.Current.HasWarnings);
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
