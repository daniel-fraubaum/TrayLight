using System.Globalization;
using TrayLight.Models;
using TrayLight.Services;
using TrayLight.Services.Actions;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Actions;

public class ShortcutPlaceholderResolverTests
{
    private sealed class FakeProvider : IInfoItemProvider
    {
        private readonly InfoItemData _data;
        public FakeProvider(string type, string value, string detail = "")
        {
            Type = type;
            _data = new InfoItemData(type, value, detail, false, string.Empty, string.Empty);
        }

        public string Type { get; }
        public Task<InfoItemData> GetDataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_data);
        public event EventHandler<InfoItemData>? DataChanged { add { } remove { } }
        public void Configure(InfoItemConfig config) { }
        public void Start(TimeSpan refreshInterval) { }
        public void Stop() { }
        public Task ExecuteClickAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ShortcutPlaceholderResolver BuildResolver() => new(
        new IInfoItemProvider[]
        {
            new FakeProvider(ComputerNameProvider.TypeKey, "DESK-01"),
            new FakeProvider(OsVersionProvider.TypeKey,    "Win 11 Ent 25H2"),
            new FakeProvider(LastRebootProvider.TypeKey,   "4h 11m ago"),
            new FakeProvider(StorageUsedProvider.TypeKey,  "66% used"),
            new FakeProvider(NetworkInfoProvider.TypeKey,  "Ethernet", "192.168.199.52"),
        },
        userName:     () => "jdoe",
        domainName:   () => "CONTOSO",
        serialNumber: () => "SN-12345",
        intuneSync:   () => "13m ago");

    [Fact]
    public async Task ExpandAsync_resolves_all_supported_placeholders()
    {
        var resolver = BuildResolver();
        const string input =
            "app:{{ComputerName}}|{{OsVersion}}|{{LastReboot}}|{{Storage}}|{{Network}}|" +
            "{{UserName}}|{{DomainName}}|{{SerialNumber}}|{{IntuneSync}}";

        var result = await resolver.ExpandAsync(input);

        Assert.Equal(
            "app:DESK-01|Win 11 Ent 25H2|4h 11m ago|66% used|Ethernet - 192.168.199.52|" +
            "jdoe|CONTOSO|SN-12345|13m ago",
            result);
    }

    [Fact]
    public async Task Network_placeholder_includes_wifi_ssid()
    {
        // The Network provider already resolves the SSID into its Value on WiFi;
        // the placeholder joins it with the IP as "WiFi {SSID} - {IP}".
        var resolver = new ShortcutPlaceholderResolver(new IInfoItemProvider[]
        {
            new FakeProvider(NetworkInfoProvider.TypeKey, "WiFi CorpNet", "10.0.0.4"),
        });

        Assert.Equal("WiFi CorpNet - 10.0.0.4",
            await resolver.ExpandAsync("{{Network}}"));
    }

    [Fact]
    public async Task ExpandAsync_url_encodes_for_mailto()
    {
        var resolver = BuildResolver();
        const string input =
            "mailto:it@example.com?subject=Support%20-%20{{ComputerName}}&body=OS:%20{{OsVersion}}";

        var result = await resolver.ExpandAsync(input);

        Assert.Contains("subject=Support%20-%20DESK-01", result);
        Assert.Contains("OS:%20Win%2011%20Ent%2025H2", result);
    }

    [Fact]
    public async Task ExpandAsync_returns_input_unchanged_when_no_tokens()
    {
        var resolver = BuildResolver();
        const string input = "https://example.com/static";

        Assert.Equal(input, await resolver.ExpandAsync(input));
    }

    [Fact]
    public async Task ExpandAsync_unresolved_token_becomes_NA()
    {
        // Resolver with no providers and disabled tiles -> provider tokens are N/A.
        var resolver = new ShortcutPlaceholderResolver(Array.Empty<IInfoItemProvider>());

        var result = await resolver.ExpandAsync("app:{{ComputerName}}");

        Assert.Equal("app:N/A", result);
    }

    private sealed class CapturingHandler : IShortcutActionHandler
    {
        public ShortcutActionType ActionType => ShortcutActionType.App;
        public string? SeenAction { get; private set; }
        public bool IsAvailable(ShortcutConfig config) => true;
        public Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken ct)
        {
            SeenAction = config.Action;
            return Task.FromResult(ActionResult.Ok());
        }
    }

    private sealed class NullNotifier : INotificationService
    {
        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info) { }
    }

    private sealed class AutoConfirm : IConfirmationService
    {
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task ActionExecutor_expands_action_at_click_time()
    {
        var handler = new CapturingHandler();
        var resolver = BuildResolver();
        var sut = new ActionExecutor(
            new IShortcutActionHandler[] { handler },
            new AutoConfirm(),
            new NullNotifier(),
            logger: null,
            placeholders: resolver);

        var result = await sut.ExecuteAsync(new ShortcutConfig
        {
            Title = "Open",
            ActionType = ShortcutActionType.App,
            Action = "tool.exe --host {{ComputerName}}"
        });

        Assert.True(result.Success);
        Assert.Equal("tool.exe --host DESK-01", handler.SeenAction);
    }

    // ---- Placeholder / tile parity (issue: {{LastReboot}} == tile) --------

    private static void WithCulture(string culture, Action body)
    {
        var origUi = CultureInfo.CurrentUICulture;
        var orig = CultureInfo.CurrentCulture;
        try
        {
            var ci = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = ci;
            CultureInfo.CurrentCulture = ci;
            body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = origUi;
            CultureInfo.CurrentCulture = orig;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("nl-NL")]
    public void LastReboot_placeholder_equals_the_tile_display(string culture)
    {
        WithCulture(culture, () =>
        {
            var uptime = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(9);
            var provider = new LastRebootProvider(() => uptime, (_, _) => { }, () => 0);
            provider.Configure(new InfoItemConfig());

            // The tile and the provider both format through RelativeTimeFormatter,
            // so the provider value must equal the tile's display string.
            var tileValue = RelativeTimeFormatter.FormatUptime(uptime);
            var providerValue = provider.GetDataAsync().GetAwaiter().GetResult().Value;
            Assert.Equal(tileValue, providerValue);

            var resolver = new ShortcutPlaceholderResolver(new IInfoItemProvider[] { provider });
            var expanded = resolver.ExpandAsync("body:{{LastReboot}}").GetAwaiter().GetResult();

            Assert.Equal($"body:{tileValue}", expanded);
            Assert.DoesNotContain("Today", expanded, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LastReboot_placeholder_is_localized_to_dutch()
    {
        WithCulture("nl-NL", () =>
        {
            var uptime = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(9);
            var provider = new LastRebootProvider(() => uptime, (_, _) => { }, () => 0);
            provider.Configure(new InfoItemConfig());
            var resolver = new ShortcutPlaceholderResolver(new IInfoItemProvider[] { provider });

            Assert.Equal("body:2u 9m geleden",
                resolver.ExpandAsync("body:{{LastReboot}}").GetAwaiter().GetResult());
        });
    }

    [Fact]
    public async Task Provider_backed_placeholders_return_the_provider_value_verbatim()
    {
        // Proves the resolver reads shared provider data and never re-formats it
        // through a separate function.
        var resolver = new ShortcutPlaceholderResolver(new IInfoItemProvider[]
        {
            new FakeProvider(ComputerNameProvider.TypeKey, "SENTINEL-PC"),
            new FakeProvider(OsVersionProvider.TypeKey,    "SENTINEL-OS"),
            new FakeProvider(LastRebootProvider.TypeKey,   "SENTINEL-REBOOT"),
            new FakeProvider(StorageUsedProvider.TypeKey,  "SENTINEL-STORAGE"),
        });

        var result = await resolver.ExpandAsync(
            "{{ComputerName}}|{{OsVersion}}|{{LastReboot}}|{{Storage}}");

        Assert.Equal("SENTINEL-PC|SENTINEL-OS|SENTINEL-REBOOT|SENTINEL-STORAGE", result);
    }
}
