using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class InfoItemProviderBaseTests
{
    private sealed class FakeProvider : InfoItemProviderBase
    {
        public Func<InfoItemData> Collector { get; set; } = () =>
            new InfoItemData("T", "v", "d", false, string.Empty, "i");
        public int Calls;

        public override string Type => "fake";
        protected override string DefaultTitle => "T";
        protected override string DefaultIcon => "i";

        protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Collector());
        }
    }

    [Fact]
    public async Task Exception_in_collect_yields_unavailable()
    {
        var sut = new FakeProvider { Collector = () => throw new InvalidOperationException("nope") };

        var data = await sut.GetDataAsync();

        Assert.Equal("Not available", data.Value);
        Assert.Equal("nope", data.DetailText);
    }

    [Fact]
    public async Task DataChanged_fires_only_when_value_differs()
    {
        var sut = new FakeProvider();
        var fires = 0;
        sut.DataChanged += (_, _) => Interlocked.Increment(ref fires);

        await sut.RefreshAsync();
        await sut.RefreshAsync();
        await sut.RefreshAsync();

        Assert.Equal(1, fires);
    }

    [Fact]
    public async Task DataChanged_fires_again_when_value_changes()
    {
        var value = "a";
        var sut = new FakeProvider
        {
            Collector = () => new InfoItemData("T", value, string.Empty, false, string.Empty, "i")
        };
        var fires = 0;
        sut.DataChanged += (_, _) => Interlocked.Increment(ref fires);

        await sut.RefreshAsync();
        value = "b";
        await sut.RefreshAsync();

        Assert.Equal(2, fires);
    }

    [Fact]
    public async Task GetDataAsync_calls_collect_only_once_for_cached_values()
    {
        var sut = new FakeProvider();
        await sut.GetDataAsync();
        await sut.GetDataAsync();
        await sut.GetDataAsync();
        Assert.Equal(1, sut.Calls);
    }

    [Fact]
    public async Task Configure_then_Effective_uses_overrides()
    {
        var sut = new FakeProvider();
        sut.Configure(new InfoItemConfig { Title = "Override", Icon = "X" });

        // Effective values are exposed through the failure path: when the
        // collector throws, the base class falls back to EffectiveTitle/Icon.
        sut.Collector = () => throw new Exception("x");
        var data = await sut.RefreshAsync();

        Assert.Equal("Override", data.Title);
        Assert.Equal("X", data.Icon);
    }
}
