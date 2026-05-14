using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class EntraIdStatusProviderTests
{
    [Fact]
    public void Parses_pure_AzureAd_join()
    {
        var output = """
            +----------------------------------------------------------------------+
            | Device State                                                         |
            +----------------------------------------------------------------------+

                         AzureAdJoined : YES
                          DomainJoined : NO
                       WorkplaceJoined : NO
            """;

        var parsed = EntraIdStatusProvider.Parse(output);

        Assert.Equal(EntraIdStatusProvider.JoinState.EntraJoined, parsed.State);
        Assert.Equal("Entra ID Joined", parsed.StateDisplay);
    }

    [Fact]
    public void Parses_hybrid_join()
    {
        var output = "AzureAdJoined : YES\nDomainJoined : YES\nWorkplaceJoined : NO";

        var parsed = EntraIdStatusProvider.Parse(output);

        Assert.Equal(EntraIdStatusProvider.JoinState.HybridJoined, parsed.State);
    }

    [Fact]
    public void Parses_workplace_registered()
    {
        var output = "AzureAdJoined : NO\nDomainJoined : NO\nWorkplaceJoined : YES";

        var parsed = EntraIdStatusProvider.Parse(output);

        Assert.Equal(EntraIdStatusProvider.JoinState.Registered, parsed.State);
    }

    [Fact]
    public void Parses_not_joined_and_returns_no_tenant()
    {
        var parsed = EntraIdStatusProvider.Parse("AzureAdJoined : NO\nDomainJoined : NO\nWorkplaceJoined : NO");

        Assert.Equal(EntraIdStatusProvider.JoinState.NotJoined, parsed.State);
        Assert.Null(parsed.TenantName);
    }

    [Fact]
    public void Extracts_tenant_name()
    {
        var output = "AzureAdJoined : YES\nDomainJoined : NO\nWorkplaceJoined : NO\nTenantName : contoso.onmicrosoft.com";

        var parsed = EntraIdStatusProvider.Parse(output);

        Assert.Equal("contoso.onmicrosoft.com", parsed.TenantName);
    }

    [Fact]
    public async Task Provider_does_not_warn_when_not_joined()
    {
        var sut = new EntraIdStatusProvider(_ =>
            Task.FromResult("AzureAdJoined : NO\nDomainJoined : NO\nWorkplaceJoined : NO"));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Not Joined", data.Value);
        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Provider_swallows_runner_exception_and_returns_unavailable()
    {
        var sut = new EntraIdStatusProvider(_ => throw new InvalidOperationException("dsregcmd missing"));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Not available", data.Value);
    }
}
