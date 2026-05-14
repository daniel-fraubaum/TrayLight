using Microsoft.Extensions.Logging;
using TrayLight.Services.Logging;
using Xunit;

namespace TrayLight.Tests.Logging;

public class LogEventsTests
{
    [Fact]
    public void All_event_ids_match_the_documented_contract()
    {
        // Pinning the numeric ids prevents accidental renumbering — these
        // values are baked into customer monitoring dashboards.
        Assert.Equal(1000, LogEvents.AppStarted.Id);
        Assert.Equal(1001, LogEvents.AppStopped.Id);
        Assert.Equal(1002, LogEvents.ConfigLoaded.Id);
        Assert.Equal(1003, LogEvents.ConfigError.Id);
        Assert.Equal(2000, LogEvents.InfoItemUpdated.Id);
        Assert.Equal(2001, LogEvents.InfoItemWarning.Id);
        Assert.Equal(3000, LogEvents.ActionExecuted.Id);
        Assert.Equal(3001, LogEvents.ActionFailed.Id);
        Assert.Equal(9000, LogEvents.UnhandledException.Id);
    }

    [Fact]
    public void Event_names_match_property_names()
    {
        Assert.Equal("AppStarted",         LogEvents.AppStarted.Name);
        Assert.Equal("UnhandledException", LogEvents.UnhandledException.Name);
    }
}
